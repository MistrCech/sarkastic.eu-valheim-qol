using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SarkasticQoL.Features
{
	/*
		The game skips the night when every character is in bed (Game.EverybodyIsTryingToSleep,
		checked every 2 s on the server in the afternoon and at night). Here a share is enough,
		and a player who is not in bed can vote with /sleep instead. Once the share is reached
		everyone is told and, after WarnSeconds, the night is skipped: the game then fades every
		player to black for about 12 s, keeps them safe from damage and wakes them Rested where
		they stood. Whenever the count changes, everyone is told where the vote stands.
	*/
	[HarmonyPatch(typeof(Game), "EverybodyIsTryingToSleep")]
	internal static class SleepVote
	{
		private static int s_lastCount = -1;
		private static DateTime s_skipAt = DateTime.MaxValue;
		private static DateTime s_lastAsked = DateTime.MinValue;
		// Players (peer ids) who said /sleep without being in bed; cleared when the night is skipped.
		private static readonly HashSet<long> s_votes = new HashSet<long>();

		public static void Reset()
		{
			s_lastCount = -1;
			s_skipAt = DateTime.MaxValue;
		}

		static bool Prefix(ref bool __result)
		{
			Settings s = QoLPlugin.Settings;
			if (!s.SleepEnabled.Value || !QoLPlugin.WorldReady())
			{
				return true;
			}
			Count(out int total, out int sleeping);
			__result = Decide(total, sleeping, s);
			return false;
		}

		// Everyone with a character; in bed, or voted while awake.
		public static void Count(out int total, out int sleeping)
		{
			total = 0;
			sleeping = 0;
			HashSet<long> awakeVoters = new HashSet<long>(s_votes);
			foreach (ZDO character in ZNet.instance.GetAllCharacterZDOS())
			{
				total++;
				if (character.GetBool(ZDOVars.s_inBed))
				{
					sleeping++;
					awakeVoters.Remove(character.GetOwner());
				}
			}
			foreach (long uid in awakeVoters)
			{
				if (ZNet.instance.GetPeer(uid) != null)
				{
					sleeping++;
				}
				else
				{
					s_votes.Remove(uid);
				}
			}
		}

		public static int Needed(int total, Settings s)
		{
			return Math.Max(s.SleepMinInBed.Value, (total * s.SleepRequiredPercent.Value + 99) / 100);
		}

		// /sleep: on = vote, off = withdraw, null = toggle. Returns what to tell the player.
		public static string Vote(ZNetPeer peer, bool? on)
		{
			Settings s = QoLPlugin.Settings;
			if (!s.SleepEnabled.Value)
			{
				return "The night is skipped when everyone is in bed";
			}
			bool now = on ?? !s_votes.Contains(peer.m_uid);
			if (now)
			{
				s_votes.Add(peer.m_uid);
			}
			else
			{
				s_votes.Remove(peer.m_uid);
			}
			Count(out int total, out int sleeping);
			string state = now ? "You vote to skip the night" : "Vote withdrawn";
			return $"{state}: {sleeping} of {total} want to sleep, {Needed(total, s)} needed. The night can only be skipped in the afternoon or at night";
		}

		public static string Status()
		{
			Settings s = QoLPlugin.Settings;
			if (!s.SleepEnabled.Value)
			{
				return "The night is skipped when everyone is in bed";
			}
			Count(out int total, out int sleeping);
			return $"{sleeping} of {total} want to sleep; {Needed(total, s)} needed to skip the night ({s.SleepRequiredPercent.Value}%). Vote with sleep on|off";
		}

		private static bool Decide(int total, int sleeping, Settings s)
		{
			DateTime now = DateTime.UtcNow;
			// The game only asks in the afternoon and at night, every 2 s; a vote left over from the
			// last evening (no more asked, so never called off) must not skip the next one unwarned.
			if ((now - s_lastAsked).TotalSeconds > 30)
			{
				Reset();
			}
			s_lastAsked = now;
			if (total == 0 || sleeping == 0)
			{
				Reset();
				return false;
			}
			bool enough = sleeping == total || sleeping >= Needed(total, s);
			if (!enough)
			{
				if (s_skipAt != DateTime.MaxValue)
				{
					s_skipAt = DateTime.MaxValue;
					Messages.ToAll(MessageHud.MessageType.Center, $"Sleep called off: {sleeping} of {total} want to sleep");
					s_lastCount = sleeping;
				}
				else if (sleeping != s_lastCount && s.SleepShowProgress.Value)
				{
					s_lastCount = sleeping;
					Messages.ToAll(MessageHud.MessageType.Center, $"{sleeping} of {total} want to sleep, {Needed(total, s)} needed to skip the night");
				}
				return false;
			}
			if (s_skipAt == DateTime.MaxValue)
			{
				int warn = s.SleepWarnSeconds.Value;
				s_skipAt = now.AddSeconds(warn);
				s_lastCount = sleeping;
				if (warn > 0)
				{
					Messages.ToAll(MessageHud.MessageType.Center, $"{sleeping} of {total} want to sleep: the night is skipped in {warn} s");
					return false;
				}
			}
			if (now < s_skipAt)
			{
				return false;
			}
			QoLPlugin.Log.LogInfo($"Sleep: {sleeping} of {total} want to sleep, skipping the night");
			Reset();
			s_votes.Clear();
			return true;
		}
	}
}
