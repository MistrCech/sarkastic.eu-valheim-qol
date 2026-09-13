using System;
using HarmonyLib;

namespace SarkasticQoL.Features
{
	/*
		The game skips the night when every character is in bed (Game.EverybodyIsTryingToSleep,
		checked every 2 s on the server). Here a share is enough. Once it is reached everyone is
		told and, after WarnSeconds, the night is skipped: the game then fades every player to
		black for about 12 s, keeps them safe from damage and wakes them Rested where they stood.
		Whenever the number in bed changes, everyone is told where the vote stands.
	*/
	[HarmonyPatch(typeof(Game), "EverybodyIsTryingToSleep")]
	internal static class SleepVote
	{
		private static int s_lastInBed = -1;
		private static DateTime s_skipAt = DateTime.MaxValue;
		private static DateTime s_lastAsked = DateTime.MinValue;

		public static void Reset()
		{
			s_lastInBed = -1;
			s_skipAt = DateTime.MaxValue;
		}

		static bool Prefix(ref bool __result)
		{
			Settings s = QoLPlugin.Settings;
			if (!s.SleepEnabled.Value || !QoLPlugin.WorldReady())
			{
				return true;
			}
			int total = 0, inBed = 0;
			foreach (ZDO character in ZNet.instance.GetAllCharacterZDOS())
			{
				total++;
				if (character.GetBool(ZDOVars.s_inBed))
				{
					inBed++;
				}
			}
			__result = Decide(total, inBed, s);
			return false;
		}

		private static bool Decide(int total, int inBed, Settings s)
		{
			DateTime now = DateTime.UtcNow;
			// The game only asks in the afternoon and at night, every 2 s; a vote left over from the
			// last evening (no more asked, so never called off) must not skip the next one unwarned.
			if ((now - s_lastAsked).TotalSeconds > 30)
			{
				Reset();
			}
			s_lastAsked = now;
			if (total == 0 || inBed == 0)
			{
				Reset();
				return false;
			}
			bool enough = inBed == total || (inBed >= s.SleepMinInBed.Value && inBed * 100 >= total * s.SleepRequiredPercent.Value);
			if (!enough)
			{
				if (s_skipAt != DateTime.MaxValue)
				{
					s_skipAt = DateTime.MaxValue;
					Messages.ToAll(MessageHud.MessageType.Center, $"Sleep called off: {inBed} of {total} in bed");
					s_lastInBed = inBed;
				}
				else if (inBed != s_lastInBed && s.SleepShowProgress.Value)
				{
					s_lastInBed = inBed;
					int needed = Math.Max(s.SleepMinInBed.Value, (total * s.SleepRequiredPercent.Value + 99) / 100);
					Messages.ToAll(MessageHud.MessageType.Center, $"{inBed} of {total} in bed, {needed} needed to skip the night");
				}
				return false;
			}
			if (s_skipAt == DateTime.MaxValue)
			{
				int warn = s.SleepWarnSeconds.Value;
				s_skipAt = now.AddSeconds(warn);
				s_lastInBed = inBed;
				if (warn > 0)
				{
					Messages.ToAll(MessageHud.MessageType.Center, $"{inBed} of {total} in bed: the night is skipped in {warn} s");
					return false;
				}
			}
			if (now < s_skipAt)
			{
				return false;
			}
			QoLPlugin.Log.LogInfo($"Sleep: {inBed} of {total} in bed, skipping the night");
			Reset();
			return true;
		}
	}
}
