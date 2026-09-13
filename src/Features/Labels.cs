using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SarkasticQoL.Features
{
	/*
		A sign in front of a chest that lists what is inside (!label on), kept up to date whenever
		the chest's data changes. The sign is an ordinary sign piece placed by the server: no
		creator (so it counts as nothing player-built), not removable with the hammer, no wear,
		indestructible. Chest and sign point at each other by id; a sign whose chest is gone is
		removed, and !label off removes it too. Item names are the prefab names split into words
		(the server has no translations for the game's own name keys).
	*/
	internal class Labels : IFeature
	{
		public static readonly int LabelKey = "SarkasticQoL.Label".GetStableHashCode();
		private static readonly KeyValuePair<int, int> SignOf = ZDO.GetHashZDOID("SarkasticQoL.LabelSign");
		private static readonly KeyValuePair<int, int> ChestOf = ZDO.GetHashZDOID("SarkasticQoL.LabelChest");
		private static readonly int SignPrefab = "sign".GetStableHashCode();

		// Where the sign goes: up (half the chest's height) and forward (in front of the lid), by chest prefab.
		private static readonly Dictionary<string, Vector2> Offsets = new Dictionary<string, Vector2>
		{
			{ "piece_chest_wood", new Vector2(0.4f, 0.45f) },
			{ "piece_chest", new Vector2(0.55f, 0.55f) },
			{ "piece_chest_private", new Vector2(0.4f, 0.45f) },
			{ "piece_chest_blackmetal", new Vector2(0.5f, 0.75f) },
			{ "piece_chest_barrel", new Vector2(0.45f, 0.45f) },
			{ "piece_chest_grausten", new Vector2(0.5f, 0.75f) },
		};

		private readonly Dictionary<ZDOID, uint> written = new Dictionary<ZDOID, uint>();
		// A sign whose chest could not be found, and in how many scans in a row: a chest that was just
		// created afresh (new id, e.g. resized) points its sign back at itself on its next visit.
		private readonly Dictionary<ZDOID, int> orphaned = new Dictionary<ZDOID, int>();

		public World.Kind Kinds => World.Kind.Container | World.Kind.Sign;

		public void Start() { }
		public void Stop() { written.Clear(); orphaned.Clear(); }

		public static bool Wanted(ZDO chest)
		{
			return chest.GetBool(LabelKey, QoLPlugin.Settings.LabelsDefault.Value);
		}

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			if (!QoLPlugin.Settings.LabelsEnabled.Value)
			{
				return;
			}
			if ((kind & World.Kind.Sign) != 0)
			{
				// One of ours whose chest is gone, seen so twice in a row.
				ZDOID chestId = zdo.GetZDOID(ChestOf);
				if (!chestId.IsNone() && ZDOMan.instance.GetZDO(chestId) == null)
				{
					int seen = orphaned.TryGetValue(zdo.m_uid, out int n) ? n + 1 : 1;
					if (seen >= 2)
					{
						orphaned.Remove(zdo.m_uid);
						Fields.Destroy(zdo);
					}
					else
					{
						orphaned[zdo.m_uid] = seen;
					}
				}
				else
				{
					orphaned.Remove(zdo.m_uid);
				}
				return;
			}
			if (zdo.GetLong(ZDOVars.s_creator) == 0L || (kind & World.Kind.Piece) == 0)
			{
				return;
			}
			ZDOID signId = zdo.GetZDOID(SignOf);
			ZDO sign = signId.IsNone() ? null : ZDOMan.instance.GetZDO(signId);
			if (!Wanted(zdo))
			{
				if (sign != null)
				{
					Remove(zdo, sign);
				}
				return;
			}
			if (sign == null)
			{
				sign = Place(zdo);
				if (sign == null)
				{
					return;
				}
				written.Remove(zdo.m_uid);
			}
			else if (sign.GetZDOID(ChestOf) != zdo.m_uid)
			{
				// The chest was created afresh under a new id (resized, say): point the sign back at it.
				sign.Set(ChestOf, zdo.m_uid);
				orphaned.Remove(sign.m_uid);
			}
			if (!written.TryGetValue(zdo.m_uid, out uint revision) || revision != zdo.DataRevision)
			{
				written[zdo.m_uid] = zdo.DataRevision;
				string text = Describe(zdo);
				if (sign.GetString(ZDOVars.s_text) != text)
				{
					sign.Set(ZDOVars.s_text, text);
				}
				// The chest may have settled since (a freshly built piece drops onto the ground): follow it.
				Placement(zdo, out Vector3 position, out Quaternion facing);
				if ((sign.GetPosition() - position).sqrMagnitude > 0.01f)
				{
					sign.SetPosition(position);
					sign.SetRotation(facing);
					if (Fields.IsLoaded(sign))
					{
						World.Retire(sign);
						ZDO moved = Fields.Recreate(sign);
						zdo.Set(SignOf, moved.m_uid);
					}
				}
			}
		}

		private static void Placement(ZDO chest, out Vector3 position, out Quaternion facing)
		{
			string prefab = World.PrefabName(chest);
			Vector2 offset = Offsets.TryGetValue(prefab, out Vector2 known) ? known : new Vector2(0.5f, 0.5f);
			Quaternion rotation = chest.GetRotation();
			position = chest.GetPosition() + rotation * new Vector3(0f, offset.x, offset.y);
			facing = Quaternion.Euler(0f, rotation.eulerAngles.y + 270f, 0f);
		}

		private static ZDO Place(ZDO chest)
		{
			string prefab = World.PrefabName(chest);
			Placement(chest, out Vector3 position, out Quaternion facing);
			ZDO sign = Fields.Place(SignPrefab, position, facing);
			if (sign == null)
			{
				QoLPlugin.Log.LogWarning("No 'sign' prefab; labels are off");
				return null;
			}
			Fields.Set(sign, "Piece", "m_canBeRemoved", false);
			Fields.Set(sign, "WearNTear", "m_noRoofWear", false);
			Fields.Set(sign, "WearNTear", "m_noSupportWear", false);
			Fields.Set(sign, "WearNTear", "m_health", -1f);
			// A piece settles onto the ground and gets pushed out of terrain by itself; this one must stay where it is put.
			Fields.Set(sign, "StaticPhysics", "m_fall", false);
			Fields.Set(sign, "StaticPhysics", "m_pushUp", false);
			sign.Set(ChestOf, chest.m_uid);
			chest.Set(SignOf, sign.m_uid);
			QoLPlugin.Log.LogInfo($"Label sign placed at {prefab} {chest.GetPosition():F0}");
			return sign;
		}

		public static void Remove(ZDO chest, ZDO sign)
		{
			chest.Set(SignOf, ZDOID.None);
			if (sign != null)
			{
				Fields.Destroy(sign);
			}
		}

		public static ZDO SignFor(ZDO chest)
		{
			ZDOID id = chest.GetZDOID(SignOf);
			return id.IsNone() ? null : ZDOMan.instance.GetZDO(id);
		}

		// "Wood 240 | Stone 120 | Copper ore 30 | +4"
		private static string Describe(ZDO chest)
		{
			byte[] data = chest.GetByteArray(ZDOVars.s_items);
			if (data == null)
			{
				return QoLPlugin.Settings.LabelsEmptyText.Value;
			}
			Inventory inventory = new Inventory("label", null, 64, 64);
			inventory.Load(new ZPackage(data));
			var counts = new Dictionary<string, int>();
			foreach (ItemDrop.ItemData item in inventory.GetAllItems())
			{
				string name = item.m_dropPrefab ? item.m_dropPrefab.name : item.m_shared.m_name;
				counts[name] = (counts.TryGetValue(name, out int n) ? n : 0) + item.m_stack;
			}
			if (counts.Count == 0)
			{
				return QoLPlugin.Settings.LabelsEmptyText.Value;
			}
			int max = Math.Max(1, QoLPlugin.Settings.LabelsMaxItems.Value);
			List<string> parts = counts.OrderByDescending(p => p.Value).Take(max).Select(p => $"{World.PrettyName(p.Key)} {p.Value}").ToList();
			if (counts.Count > max)
			{
				parts.Add($"+{counts.Count - max}");
			}
			return string.Join(" | ", parts);
		}
	}
}
