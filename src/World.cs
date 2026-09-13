using System;
using System.Collections.Generic;
using SarkasticQoL.Features;
using UnityEngine;

namespace SarkasticQoL
{
	/*
		The scan: every ScanSeconds, for one player per frame, the objects in the zones around
		them (3x3, the zones a client has loaded) are handed to the features that care about
		their kind. A few thousand objects at most per player; the features do nothing unless
		something needs changing. Also finds pieces near a player for the chat commands.
	*/
	internal static class World
	{
		internal static readonly PlayerState Players = new PlayerState();
		internal static readonly List<IFeature> Features = new List<IFeature>();

		private static readonly List<ZDO> s_near = new List<ZDO>();
		// Objects replaced during the current scan (created afresh under a new id): the old ones are still in the list.
		private static readonly HashSet<ZDOID> s_retired = new HashSet<ZDOID>();

		public static void Retire(ZDO zdo)
		{
			s_retired.Add(zdo.m_uid);
		}

		public static bool IsRetired(ZDO zdo)
		{
			return s_retired.Contains(zdo.m_uid);
		}

		// The objects around the player being scanned, for features that look at neighbours (containers near a smelter).
		public static IReadOnlyList<ZDO> Near => s_near;
		private static readonly SimulationDistance s_zonesAround = new SimulationDistance(1, 0, classic: true);
		private static readonly Dictionary<int, Kind> s_kinds = new Dictionary<int, Kind>();
		private static float s_timer;
		private static int s_next;
		private static readonly List<ZNetPeer> s_peers = new List<ZNetPeer>();

		[Flags]
		public enum Kind
		{
			None = 0,
			Door = 1,
			Turret = 2,
			Container = 4,
			Tameable = 8,
			Egg = 16,
			Growup = 32,
			Fireplace = 64,
			Smelter = 128,
			Sign = 256,
			Piece = 512,
			ShieldGenerator = 1024,
			Location = 2048,
			Portal = 4096,
			MapTable = 8192,
			PinObject = 16384, // a prefab named in the pins' lists (berries, ore deposits)
		}

		// Adds a kind to a prefab's, for the objects a feature picks by name (the pins' lists) rather than by component.
		public static void Flag(int prefabHash, Kind kind)
		{
			s_kinds[prefabHash] = (s_kinds.TryGetValue(prefabHash, out Kind current) ? current : Kind.None) | kind;
		}

		public static void Start()
		{
			Players.Load();
			QoLPlugin.Settings.BindContainerSizes();
			s_kinds.Clear();
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				if (!prefab)
				{
					continue;
				}
				Kind kind = Kind.None;
				if (prefab.GetComponent<Door>()) kind |= Kind.Door;
				if (prefab.GetComponent<Turret>()) kind |= Kind.Turret;
				if (prefab.GetComponent<Container>()) kind |= Kind.Container;
				if (prefab.GetComponent<Tameable>()) kind |= Kind.Tameable;
				if (prefab.GetComponent<EggGrow>()) kind |= Kind.Egg;
				if (prefab.GetComponent<Growup>()) kind |= Kind.Growup;
				if (prefab.GetComponent<Fireplace>()) kind |= Kind.Fireplace;
				if (prefab.GetComponent<Smelter>()) kind |= Kind.Smelter;
				if (prefab.GetComponent<Sign>()) kind |= Kind.Sign;
				if (prefab.GetComponent<Piece>()) kind |= Kind.Piece;
				if (prefab.GetComponent<ShieldGenerator>()) kind |= Kind.ShieldGenerator;
				if (prefab.GetComponent<LocationProxy>()) kind |= Kind.Location;
				if (prefab.GetComponent<TeleportWorld>()) kind |= Kind.Portal;
				if (prefab.GetComponent<MapTable>()) kind |= Kind.MapTable;
				if (kind != Kind.None)
				{
					s_kinds[prefab.name.GetStableHashCode()] = kind;
				}
			}
			Features.Clear();
			Features.Add(new AutoDoors());
			Features.Add(new Ballistas());
			Features.Add(new TameProgress());
			Features.Add(new ContainerSizes());
			Features.Add(new PrefabFields());
			Features.Add(new Feeding());
			Features.Add(new Labels());
			Features.Add(new Clocks());
			Features.Add(new Sorting());
			Features.Add(new Pins());
			foreach (IFeature feature in Features)
			{
				feature.Start();
			}
			SleepVote.Reset();
			Motd.Reset();
			TameDeaths.Load();
			s_timer = 0f;
		}

		public static void Stop()
		{
			foreach (IFeature feature in Features)
			{
				feature.Stop();
			}
			Features.Clear();
		}

		public static Kind KindOf(ZDO zdo)
		{
			return s_kinds.TryGetValue(zdo.GetPrefab(), out Kind kind) ? kind : Kind.None;
		}

		public static void Tick(float dt)
		{
			Motd.Tick();
			try
			{
				Pins.Tick(dt);
			}
			catch (Exception e)
			{
				QoLPlugin.Log.LogWarning($"Pins failed: {e}");
			}
			s_timer -= dt;
			if (s_timer > 0f)
			{
				return;
			}
			// One player per frame; a full round takes as many frames as there are players.
			if (s_next == 0)
			{
				s_peers.Clear();
				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					if (peer.IsReady())
					{
						s_peers.Add(peer);
					}
				}
			}
			if (s_next < s_peers.Count)
			{
				try
				{
					Scan(s_peers[s_next]);
				}
				catch (Exception e)
				{
					QoLPlugin.Log.LogWarning($"Scan around {s_peers[s_next].m_playerName} failed: {e}");
				}
				s_next++;
			}
			if (s_next >= s_peers.Count)
			{
				s_next = 0;
				s_timer = Mathf.Max(0.5f, QoLPlugin.Settings.ScanSeconds.Value);
			}
		}

		private static void Scan(ZNetPeer peer)
		{
			Vector3 at = Position(peer);
			s_near.Clear();
			s_retired.Clear();
			ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), s_zonesAround, s_near);
			foreach (IFeature feature in Features)
			{
				(feature as IScanAware)?.BeginScan(peer);
			}
			foreach (ZDO zdo in s_near)
			{
				Kind kind = KindOf(zdo);
				if (kind == Kind.None || s_retired.Contains(zdo.m_uid))
				{
					continue;
				}
				foreach (IFeature feature in Features)
				{
					// A feature may have replaced the object (created it afresh under a new id) a moment ago.
					if ((feature.Kinds & kind) != 0 && !s_retired.Contains(zdo.m_uid))
					{
						feature.Visit(zdo, kind, s_peers);
					}
				}
			}
			foreach (IFeature feature in Features)
			{
				(feature as IScanAware)?.EndScan(peer);
			}
		}

		public static Vector3 Position(ZNetPeer peer)
		{
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			return character != null ? character.GetPosition() : peer.GetRefPos();
		}

		public static bool AnyPlayerWithin(List<ZNetPeer> peers, Vector3 position, float distance)
		{
			float sq = distance * distance;
			foreach (ZNetPeer peer in peers)
			{
				if ((Position(peer) - position).sqrMagnitude <= sq)
				{
					return true;
				}
			}
			return false;
		}

		// The nearest object of that kind within the distance of a player, for the chat commands.
		public static ZDO Nearest(ZNetPeer peer, Kind kind, float distance)
		{
			Vector3 at = Position(peer);
			List<ZDO> list = new List<ZDO>();
			ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), s_zonesAround, list);
			ZDO best = null;
			float bestSq = distance * distance;
			foreach (ZDO zdo in list)
			{
				if ((KindOf(zdo) & kind) == 0)
				{
					continue;
				}
				float sq = (zdo.GetPosition() - at).sqrMagnitude;
				if (sq < bestSq)
				{
					bestSq = sq;
					best = zdo;
				}
			}
			return best;
		}

		public static string PrefabName(ZDO zdo)
		{
			GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
			return prefab ? prefab.name : zdo.GetPrefab().ToString();
		}

		// For the log: the prefab name (the piece's display name is a localisation key, and the server has no translations).
		public static string PieceName(ZDO zdo)
		{
			return PrefabName(zdo);
		}

		// "CopperOre" -> "Copper ore", "piece_chest_wood" -> "chest wood": readable without the game's translations.
		public static string PrettyName(string prefab)
		{
			if (string.IsNullOrEmpty(prefab))
			{
				return "";
			}
			string name = prefab.StartsWith("piece_") ? prefab.Substring(6) : prefab;
			name = name.Replace('_', ' ');
			System.Text.StringBuilder text = new System.Text.StringBuilder(name.Length + 4);
			for (int i = 0; i < name.Length; i++)
			{
				char c = name[i];
				if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) && name[i - 1] != ' ')
				{
					text.Append(' ');
					text.Append(char.ToLowerInvariant(c));
				}
				else
				{
					text.Append(c);
				}
			}
			return text.ToString();
		}
	}

	internal interface IFeature
	{
		World.Kind Kinds { get; }
		void Start();
		void Stop();
		void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers);
	}

	// A feature that looks at the objects around a player as a whole (clusters of bushes), not one by one.
	internal interface IScanAware
	{
		void BeginScan(ZNetPeer peer);
		void EndScan(ZNetPeer peer);
	}
}
