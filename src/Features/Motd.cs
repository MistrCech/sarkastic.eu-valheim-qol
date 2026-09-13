using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SarkasticQoL.Features
{
	/*
		A message of the day in the middle of the screen a few seconds after a player's character
		has appeared (the client tells the server its character id: ZNet.RPC_CharacterID). Sent
		later rather than at once so the client has the world around it and shows it.
	*/
	[HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
	internal static class Motd
	{
		private static readonly List<KeyValuePair<long, DateTime>> s_due = new List<KeyValuePair<long, DateTime>>();
		private static readonly HashSet<long> s_told = new HashSet<long>();

		public static void Reset()
		{
			s_due.Clear();
			s_told.Clear();
		}

		static void Postfix(ZNet __instance, ZRpc rpc, ZDOID characterID)
		{
			if (!__instance.IsServer() || characterID.IsNone())
			{
				return;
			}
			ZNetPeer peer = __instance.GetPeer(rpc);
			if (peer != null && s_told.Add(peer.m_uid))
			{
				s_due.Add(new KeyValuePair<long, DateTime>(peer.m_uid, DateTime.UtcNow.AddSeconds(QoLPlugin.Settings.MotdDelaySeconds.Value)));
			}
		}

		public static void Tick()
		{
			if (s_due.Count == 0)
			{
				return;
			}
			DateTime now = DateTime.UtcNow;
			for (int i = s_due.Count - 1; i >= 0; i--)
			{
				if (s_due[i].Value > now)
				{
					continue;
				}
				ZNetPeer peer = ZNet.instance.GetPeer(s_due[i].Key);
				s_due.RemoveAt(i);
				string text = QoLPlugin.Settings.MotdText.Value.Trim();
				if (peer != null && text.Length > 0)
				{
					Messages.ToPeer(peer, MessageHud.MessageType.Center, text.Replace("|", "\n"));
					QoLPlugin.Log.LogInfo($"Motd shown to {peer.m_playerName}");
				}
			}
			// A player who left may come back and should be told again.
			s_told.RemoveWhere(uid => ZNet.instance.GetPeer(uid) == null);
		}
	}
}
