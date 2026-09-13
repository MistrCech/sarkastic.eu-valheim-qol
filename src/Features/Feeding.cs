using System.Collections.Generic;
using UnityEngine;

namespace SarkasticQoL.Features
{
	/*
		Smelters, kilns, windmills, spinning wheels, blast furnaces and shield generators feed
		themselves from player-built containers within Range, and so do fireplaces if switched
		on: ore goes into the queue the way Smelter.QueueOre does (item<N> = prefab name,
		s_queued), fuel into s_fuel. Only when the station is below half, never while a player
		is within PlayerDistance (they may be using it), never from a container someone has open,
		and LeaveAtLeast of each item stays in the container. A player's choice for one station
		(!feed, !fire feed) is stored in the station and wins over the config defaults.
	*/
	internal class Feeding : IFeature
	{
		public static readonly int FeedKey = "SarkasticQoL.Feed".GetStableHashCode();
		private static readonly int QueuedKey = ZDOVars.s_queued;

		public World.Kind Kinds => World.Kind.Smelter | World.Kind.ShieldGenerator | World.Kind.Fireplace;

		public void Start() { }
		public void Stop() { }

		// -1 = no choice made, 0 = off, 1 = on
		public static bool Wanted(ZDO zdo, World.Kind kind)
		{
			Settings s = QoLPlugin.Settings;
			bool fallback = (kind & World.Kind.Fireplace) != 0 ? s.FeedFireplaces.Value : s.FeedSmelters.Value;
			int choice = zdo.GetInt(FeedKey, -1);
			return choice < 0 ? fallback : choice == 1;
		}

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			Settings s = QoLPlugin.Settings;
			if (!s.FeedEnabled.Value || zdo.GetLong(ZDOVars.s_creator) == 0L || !Wanted(zdo, kind))
			{
				return;
			}
			if (World.AnyPlayerWithin(peers, zdo.GetPosition(), s.FeedPlayerDistance.Value))
			{
				return;
			}
			GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
			if (!prefab)
			{
				return;
			}
			if ((kind & World.Kind.Fireplace) != 0)
			{
				Fireplace fire = prefab.GetComponent<Fireplace>();
				if (fire && fire.m_fuelItem && fire.m_canRefill && zdo.GetInt(ZDOVars.s_state, 1) != 0)
				{
					float fuel = zdo.GetFloat(ZDOVars.s_fuel, -1f);
					if (fuel >= 0f && fuel < fire.m_maxFuel / 2f)
					{
						int taken = Take(zdo, peers, fire.m_fuelItem, Mathf.FloorToInt(fire.m_maxFuel - fuel));
						if (taken > 0)
						{
							zdo.Set(ZDOVars.s_fuel, Mathf.Clamp(fuel + taken, 0f, fire.m_maxFuel));
						}
					}
				}
				return;
			}
			if ((kind & World.Kind.ShieldGenerator) != 0)
			{
				ShieldGenerator shield = prefab.GetComponent<ShieldGenerator>();
				if (shield && shield.m_fuelItems.Count > 0)
				{
					float fuel = zdo.GetFloat(ZDOVars.s_fuel, shield.m_defaultFuel);
					if (fuel < shield.m_maxFuel / 2f)
					{
						int taken = Take(zdo, peers, shield.m_fuelItems[0], Mathf.FloorToInt(shield.m_maxFuel - fuel));
						if (taken > 0)
						{
							zdo.Set(ZDOVars.s_fuel, Mathf.Clamp(fuel + taken, 0f, shield.m_maxFuel));
						}
					}
				}
				return;
			}
			Smelter smelter = prefab.GetComponent<Smelter>();
			if (!smelter)
			{
				return;
			}
			if (smelter.m_fuelItem)
			{
				float fuel = zdo.GetFloat(ZDOVars.s_fuel);
				if (fuel < smelter.m_maxFuel / 2f)
				{
					int taken = Take(zdo, peers, smelter.m_fuelItem, Mathf.FloorToInt(smelter.m_maxFuel - fuel));
					if (taken > 0)
					{
						zdo.Set(ZDOVars.s_fuel, Mathf.Clamp(fuel + taken, 0f, smelter.m_maxFuel));
					}
				}
			}
			int queued = zdo.GetInt(QueuedKey);
			if (queued < smelter.m_maxOre / 2 && smelter.m_conversion.Count > 0)
			{
				// Keep to the kind of ore already queued, so a furnace does not mix copper into a run of iron.
				string current = queued > 0 ? zdo.GetString("item" + (queued - 1)) : null;
				foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
				{
					if (!conversion.m_from || (current != null && conversion.m_from.gameObject.name != current))
					{
						continue;
					}
					int taken = Take(zdo, peers, conversion.m_from, smelter.m_maxOre - queued);
					for (int i = 0; i < taken; i++)
					{
						zdo.Set("item" + (queued + i), conversion.m_from.gameObject.name);
					}
					if (taken > 0)
					{
						queued += taken;
						zdo.Set(QueuedKey, queued);
						if (queued >= smelter.m_maxOre)
						{
							break;
						}
					}
				}
			}
		}

		// Takes up to `wanted` of the item from player-built containers within range; returns how many.
		private static int Take(ZDO station, List<ZNetPeer> peers, ItemDrop item, int wanted)
		{
			Settings s = QoLPlugin.Settings;
			string sharedName = item.m_itemData.m_shared.m_name;
			float rangeSq = s.FeedRange.Value * s.FeedRange.Value;
			int taken = 0;
			foreach (ZDO container in World.Near)
			{
				if (wanted <= 0)
				{
					break;
				}
				if ((World.KindOf(container) & World.Kind.Container) == 0 || World.IsRetired(container) || container.GetLong(ZDOVars.s_creator) == 0L
					|| container.GetInt(ZDOVars.s_inUse) != 0 || (container.GetPosition() - station.GetPosition()).sqrMagnitude > rangeSq)
				{
					continue;
				}
				byte[] data = container.GetByteArray(ZDOVars.s_items);
				if (data == null)
				{
					continue;
				}
				Inventory inventory = new Inventory("feed", null, 64, 64);
				inventory.Load(new ZPackage(data));
				int have = inventory.CountItems(sharedName) - s.FeedLeaveAtLeast.Value;
				if (have <= 0)
				{
					continue;
				}
				int take = Mathf.Min(have, wanted);
				inventory.RemoveItem(sharedName, take);
				ZPackage pkg = new ZPackage();
				inventory.Save(pkg);
				container.Set(ZDOVars.s_items, pkg.GetArray());
				taken += take;
				wanted -= take;
			}
			if (taken > 0)
			{
				QoLPlugin.Log.LogInfo($"{World.PrefabName(station)} at {station.GetPosition():F0}: +{taken} {item.gameObject.name} from containers");
				if (s.FeedShowText.Value)
				{
					Vector3 at = station.GetPosition() + Vector3.up * 1.5f;
					foreach (ZNetPeer peer in peers)
					{
						if ((World.Position(peer) - at).sqrMagnitude <= 30f * 30f)
						{
							Messages.InWorld(peer, at, $"+{taken} {World.PrettyName(item.gameObject.name)}");
						}
					}
				}
			}
			return taken;
		}
	}
}
