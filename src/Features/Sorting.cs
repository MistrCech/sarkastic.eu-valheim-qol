using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SarkasticQoL.Features
{
	/*
		A chest that keeps itself tidy (!sort on): whenever its data has changed and nobody has
		it open, stacks of the same item and quality are merged, items are ordered by name and
		quality, and laid out from the top left. The chest's own layout is what a client shows,
		so it changes the moment the chest is opened again.
	*/
	internal class Sorting : IFeature
	{
		public static readonly int SortKey = "SarkasticQoL.Sort".GetStableHashCode();

		private readonly Dictionary<ZDOID, uint> sorted = new Dictionary<ZDOID, uint>();

		public World.Kind Kinds => World.Kind.Container;

		public void Start() { }
		public void Stop() { sorted.Clear(); }

		public static bool Wanted(ZDO chest)
		{
			return chest.GetBool(SortKey, QoLPlugin.Settings.SortDefault.Value);
		}

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			if (!QoLPlugin.Settings.SortEnabled.Value || zdo.GetLong(ZDOVars.s_creator) == 0L || (kind & World.Kind.Piece) == 0 || !Wanted(zdo))
			{
				return;
			}
			if (zdo.GetInt(ZDOVars.s_inUse) != 0 || (sorted.TryGetValue(zdo.m_uid, out uint revision) && revision == zdo.DataRevision))
			{
				return;
			}
			byte[] data = zdo.GetByteArray(ZDOVars.s_items);
			if (data == null)
			{
				sorted[zdo.m_uid] = zdo.DataRevision;
				return;
			}
			GameObject prefabObject = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
			Container container = prefabObject ? prefabObject.GetComponentInChildren<Container>() : null;
			if (!container)
			{
				return;
			}
			int width = Fields.GetInt(zdo, "Container", "m_width", container.m_width);
			int height = Fields.GetInt(zdo, "Container", "m_height", container.m_height);
			Inventory inventory = new Inventory("sort", null, 64, 64);
			inventory.Load(new ZPackage(data));
			List<ItemDrop.ItemData> items = inventory.GetAllItems().ToList();
			// Merge stacks of the same kind.
			List<ItemDrop.ItemData> merged = new List<ItemDrop.ItemData>();
			foreach (ItemDrop.ItemData item in items.OrderBy(i => i.m_shared.m_name).ThenBy(i => i.m_quality).ThenByDescending(i => i.m_stack))
			{
				ItemDrop.ItemData into = merged.LastOrDefault();
				if (into != null && SameKind(into, item) && into.m_stack < into.m_shared.m_maxStackSize)
				{
					int moved = Mathf.Min(into.m_shared.m_maxStackSize - into.m_stack, item.m_stack);
					into.m_stack += moved;
					item.m_stack -= moved;
					if (item.m_stack <= 0)
					{
						continue;
					}
				}
				merged.Add(item);
			}
			bool changed = merged.Count != items.Count;
			for (int i = 0; i < merged.Count; i++)
			{
				Vector2i at = new Vector2i(i % width, i / width);
				if (at.y >= height)
				{
					// Does not fit the grid (it was bigger before): leave the chest as it is.
					return;
				}
				if (merged[i].m_gridPos != at)
				{
					merged[i].m_gridPos = at;
					changed = true;
				}
			}
			if (changed)
			{
				Inventory result = new Inventory("sort", null, 64, 64);
				foreach (ItemDrop.ItemData item in merged)
				{
					result.m_inventory.Add(item);
				}
				ZPackage pkg = new ZPackage();
				result.Save(pkg);
				zdo.Set(ZDOVars.s_items, pkg.GetArray());
				QoLPlugin.Log.LogInfo($"{World.PrefabName(zdo)} at {zdo.GetPosition():F0}: sorted, {items.Count} -> {merged.Count} stacks");
			}
			sorted[zdo.m_uid] = zdo.DataRevision;
		}

		private static bool SameKind(ItemDrop.ItemData a, ItemDrop.ItemData b)
		{
			return a.m_shared.m_name == b.m_shared.m_name && a.m_quality == b.m_quality && a.m_variant == b.m_variant
				&& a.m_shared.m_maxStackSize > 1 && a.m_customData.Count == 0 && b.m_customData.Count == 0;
		}
	}
}
