using System;
using System.Collections.Generic;
using System.Linq;

namespace SarkasticQoL.Features
{
	/*
		A chest named after what is inside (!label on): "Coal 156", "Wood 240, Stone 120 +2", shown
		wherever the game shows the chest's name -- the text when a player looks at it and the title
		of the opened chest -- and translated by each client, since the names are the game's own
		name keys ($item_coal). Done with the per-object field override Container.m_name, which the
		game reads when it creates the object, so a chest whose contents changed is created afresh
		under a new id: never while somebody has it open, and at most every RefreshSeconds, so it
		does not blink at every item taken out. An empty chest, or !label off, goes back to its own
		name. Nothing is placed in the world; a vanilla client cannot rename a chest, so nothing of
		the players' is overwritten.
	*/
	internal class Labels : IFeature
	{
		public static readonly int LabelKey = "SarkasticQoL.Label".GetStableHashCode();
		private static readonly int NameKey = Fields.Key("Container", "m_name");
		private static readonly int RenamedAtKey = "SarkasticQoL.LabelAt".GetStableHashCode();
		// 0.2.0 put a sign in front of the chest instead; those are taken away.
		private static readonly KeyValuePair<int, int> SignOf = ZDO.GetHashZDOID("SarkasticQoL.LabelSign");
		private static readonly KeyValuePair<int, int> ChestOf = ZDO.GetHashZDOID("SarkasticQoL.LabelChest");

		// Chest -> the data revision last looked at, so the items are only read when something changed.
		private readonly Dictionary<ZDOID, uint> seen = new Dictionary<ZDOID, uint>();

		public World.Kind Kinds => World.Kind.Container | World.Kind.Sign;

		public void Start() { }
		public void Stop() { seen.Clear(); }

		public static bool Wanted(ZDO chest)
		{
			return chest.GetBool(LabelKey, QoLPlugin.Settings.LabelsDefault.Value);
		}

		// The name the chest carries now, or null for its own.
		public static string Current(ZDO chest)
		{
			string name = chest.GetString(NameKey, "");
			return name.Length > 0 ? name : null;
		}

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			if (!QoLPlugin.Settings.LabelsEnabled.Value)
			{
				return;
			}
			if ((kind & World.Kind.Sign) != 0)
			{
				if (!zdo.GetZDOID(ChestOf).IsNone())
				{
					QoLPlugin.Log.LogInfo($"Label sign at {zdo.GetPosition():F0} from 0.2.0 removed");
					Fields.Destroy(zdo);
				}
				return;
			}
			if (zdo.GetLong(ZDOVars.s_creator) == 0L || (kind & World.Kind.Piece) == 0)
			{
				return;
			}
			ZDOID signId = zdo.GetZDOID(SignOf);
			if (!signId.IsNone())
			{
				ZDO sign = ZDOMan.instance.GetZDO(signId);
				if (sign != null)
				{
					Fields.Destroy(sign);
				}
				zdo.Set(SignOf, ZDOID.None);
			}
			string current = Current(zdo);
			if (!Wanted(zdo))
			{
				if (current != null && zdo.GetInt(ZDOVars.s_inUse) == 0)
				{
					Rename(zdo, null);
				}
				return;
			}
			if (zdo.GetInt(ZDOVars.s_inUse) != 0 || (seen.TryGetValue(zdo.m_uid, out uint revision) && revision == zdo.DataRevision))
			{
				return;
			}
			seen[zdo.m_uid] = zdo.DataRevision;
			string text = Describe(zdo);
			if (text == current)
			{
				return;
			}
			double now = ZNet.instance.GetTimeSeconds();
			if (now - zdo.GetFloat(RenamedAtKey, -1e9f) < QoLPlugin.Settings.LabelsRefreshSeconds.Value)
			{
				seen.Remove(zdo.m_uid); // look again next time
				return;
			}
			Rename(zdo, text);
		}

		// Sets the name (null: the chest's own) and has every client create the chest afresh so it shows.
		private static void Rename(ZDO chest, string text)
		{
			if (text == null)
			{
				chest.RemoveString(NameKey);
			}
			else
			{
				Fields.Set(chest, "Container", "m_name", text);
			}
			chest.Set(RenamedAtKey, (float)ZNet.instance.GetTimeSeconds());
			QoLPlugin.Log.LogInfo($"{World.PrefabName(chest)} at {chest.GetPosition():F0}: named '{text ?? "(its own name)"}'");
			if (Fields.IsLoaded(chest))
			{
				Fields.Recreate(chest);
			}
		}

		// "$item_wood 240, $item_stone 120, $item_copperore 30 +4"; null when empty.
		private static string Describe(ZDO chest)
		{
			byte[] data = chest.GetByteArray(ZDOVars.s_items);
			if (data == null)
			{
				return null;
			}
			Inventory inventory = new Inventory("label", null, 64, 64);
			inventory.Load(new ZPackage(data));
			var counts = new Dictionary<string, int>();
			foreach (ItemDrop.ItemData item in inventory.GetAllItems())
			{
				string name = item.m_shared.m_name;
				if (string.IsNullOrEmpty(name))
				{
					name = item.m_dropPrefab ? World.PrettyName(item.m_dropPrefab.name) : "?";
				}
				counts[name] = (counts.TryGetValue(name, out int n) ? n : 0) + item.m_stack;
			}
			if (counts.Count == 0)
			{
				return null;
			}
			int max = Math.Max(1, QoLPlugin.Settings.LabelsMaxItems.Value);
			string text = string.Join(", ", counts.OrderByDescending(p => p.Value).Take(max).Select(p => $"{p.Key} {p.Value}"));
			return counts.Count > max ? $"{text} +{counts.Count - max}" : text;
		}
	}
}
