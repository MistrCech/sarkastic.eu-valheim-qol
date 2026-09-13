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
			foreach (IFeature feature in Features)
			{
				feature.Start();
			}
			SleepVote.Reset();
			Motd.Reset();
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
			ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), s_zonesAround, s_near);
			foreach (ZDO zdo in s_near)
			{
				Kind kind = KindOf(zdo);
				if (kind == Kind.None)
				{
					continue;
				}
				foreach (IFeature feature in Features)
				{
					if ((feature.Kinds & kind) != 0)
					{
						feature.Visit(zdo, kind, s_peers);
					}
				}
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
	}

	internal interface IFeature
	{
		World.Kind Kinds { get; }
		void Start();
		void Stop();
		void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers);
	}
}
