using System;
using System.Collections.Generic;
using UnityEngine;

namespace SarkasticQoL.Features
{
	/*
		Floating text above a creature being tamed (tameness, hungry or not), an egg hatching and
		a young animal growing up, for the players near it who have not switched it off (!tame off).
		Shown again every ProgressStepPercent. Read from the objects' data: s_tameTimeLeft (taming
		time left, default the full taming time), s_tameLastFeeding, s_growStart (egg),
		s_spawnTime (young) against the prefab's m_tamingTime / m_fedDuration / m_growTime.
	*/
	internal class TameProgress : IFeature
	{
		private readonly Dictionary<ZDOID, int> shown = new Dictionary<ZDOID, int>();

		public World.Kind Kinds => World.Kind.Tameable | World.Kind.Egg | World.Kind.Growup;

		public void Start() { }
		public void Stop() { shown.Clear(); }

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			Settings s = QoLPlugin.Settings;
			if (!s.TamesProgress.Value)
			{
				return;
			}
			string text = Describe(zdo, kind, out int percent);
			if (text == null)
			{
				shown.Remove(zdo.m_uid);
				return;
			}
			int step = Mathf.Max(1, s.TamesProgressStepPercent.Value);
			int bucket = percent / step;
			if (shown.TryGetValue(zdo.m_uid, out int last) && last == bucket)
			{
				return;
			}
			shown[zdo.m_uid] = bucket;
			Vector3 at = zdo.GetPosition() + Vector3.up * 1.5f;
			float rangeSq = s.TamesProgressRange.Value * s.TamesProgressRange.Value;
			foreach (ZNetPeer peer in peers)
			{
				if ((World.Position(peer) - at).sqrMagnitude <= rangeSq && World.Players.Wants(peer, "tame"))
				{
					Messages.InWorld(peer, at, text);
				}
			}
		}

		private static string Describe(ZDO zdo, World.Kind kind, out int percent)
		{
			percent = 0;
			if ((kind & World.Kind.Egg) != 0)
			{
				EggGrow egg = Fields.PrefabComponent<EggGrow>(zdo.GetPrefab());
				float start = zdo.GetFloat(ZDOVars.s_growStart);
				if (!egg || start <= 0f || egg.m_growTime <= 0f)
				{
					return null;
				}
				percent = Percent((ZNet.instance.GetTimeSeconds() - start) / egg.m_growTime);
				return $"Hatching {percent}%";
			}
			if ((kind & World.Kind.Growup) != 0)
			{
				Growup growup = Fields.PrefabComponent<Growup>(zdo.GetPrefab());
				long spawned = zdo.GetLong(ZDOVars.s_spawnTime, 0L);
				if (!growup || spawned == 0L || growup.m_growTime <= 0f)
				{
					return null;
				}
				double elapsed = (ZNet.instance.GetTime() - new DateTime(spawned)).TotalSeconds;
				percent = Percent(elapsed / growup.m_growTime);
				return $"Growing {percent}%";
			}
			if ((kind & World.Kind.Tameable) != 0)
			{
				Tameable tameable = Fields.PrefabComponent<Tameable>(zdo.GetPrefab());
				if (!tameable || zdo.GetBool(ZDOVars.s_tamed) || tameable.m_tamingTime <= 0f)
				{
					return null;
				}
				float left = zdo.GetFloat(ZDOVars.s_tameTimeLeft, tameable.m_tamingTime);
				if (left >= tameable.m_tamingTime)
				{
					return null;
				}
				percent = Percent(1.0 - left / tameable.m_tamingTime);
				long fed = zdo.GetLong(ZDOVars.s_tameLastFeeding, 0L);
				bool hungry = fed == 0L || (ZNet.instance.GetTime() - new DateTime(fed)).TotalSeconds > tameable.m_fedDuration;
				return hungry ? $"Tameness {percent}%, hungry" : $"Tameness {percent}%";
			}
			return null;
		}

		private static int Percent(double fraction)
		{
			return Mathf.Clamp((int)Math.Floor(fraction * 100.0), 0, 100);
		}
	}
}
