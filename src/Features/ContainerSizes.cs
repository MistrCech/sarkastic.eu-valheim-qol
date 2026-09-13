using System.Collections.Generic;
using UnityEngine;

namespace SarkasticQoL.Features
{
	/*
		Inventory size of player-built containers from [Containers] in the config: Container.m_width
		and m_height as per-object field overrides, then the container is created afresh so that
		every client opens it with the new grid. Never while a player has it open, and never
		shrunk below what its items occupy. The rest of the container's data (its items, name,
		creator) carries over; a container has no links by id that could be lost.
	*/
	internal class ContainerSizes : IFeature
	{
		public World.Kind Kinds => World.Kind.Container;

		public void Start() { }
		public void Stop() { }

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			Settings s = QoLPlugin.Settings;
			if (!s.ContainersEnabled.Value || zdo.GetLong(ZDOVars.s_creator) == 0L || (kind & World.Kind.Piece) == 0)
			{
				return;
			}
			if (!s.ContainerSizes.TryGetValue(zdo.GetPrefab(), out var entry) || !Settings.ParseSize(entry.Value, out int width, out int height))
			{
				return;
			}
			GameObject prefabObject = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
			Container prefab = prefabObject ? prefabObject.GetComponentInChildren<Container>() : null;
			if (!prefab || zdo.GetInt(ZDOVars.s_inUse) != 0)
			{
				return;
			}
			int nowWidth = Fields.GetInt(zdo, "Container", "m_width", prefab.m_width);
			int nowHeight = Fields.GetInt(zdo, "Container", "m_height", prefab.m_height);
			if (nowWidth == width && nowHeight == height)
			{
				return;
			}
			if (!ItemsFit(zdo, width, height))
			{
				return;
			}
			Fields.Set(zdo, "Container", "m_width", width);
			Fields.Set(zdo, "Container", "m_height", height);
			Fields.Recreate(zdo);
			QoLPlugin.Log.LogInfo($"{World.PieceName(zdo)} at {zdo.GetPosition():F0}: {nowWidth}x{nowHeight} -> {width}x{height}");
		}

		// True when every stored item lies inside the new grid.
		private static bool ItemsFit(ZDO zdo, int width, int height)
		{
			byte[] data = zdo.GetByteArray(ZDOVars.s_items);
			if (data == null)
			{
				return true;
			}
			Inventory inventory = new Inventory("check", null, 64, 64);
			inventory.Load(new ZPackage(data));
			foreach (ItemDrop.ItemData item in inventory.GetAllItems())
			{
				if (item.m_gridPos.x >= width || item.m_gridPos.y >= height)
				{
					return false;
				}
			}
			return true;
		}
	}
}
