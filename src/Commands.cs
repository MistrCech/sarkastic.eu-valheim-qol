using System;
using System.Collections.Generic;
using HarmonyLib;
using SarkasticQoL.Features;

namespace SarkasticQoL
{
	/*
		Commands from players, two ways in:

		1. Chat: a message a player types reaches the server as the routed RPC "Say" on the
		   player's own character (Talker.Say) -- but only as one copy per *other* player, sent
		   for the server to pass on (Chat.CheckPermissionsAndSendChatMessageRPCsAsync; the
		   server itself is never a recipient). A copy that starts with the prefix is handled here
		   and not passed on, so no other player sees it. With nobody else online nothing is sent
		   at all, so chat commands need another player online.

		2. The game console: a vanilla client forwards a few of the game's own console commands
		   to the server when it cannot run them itself (ZNet.RemoteCommand -> RPC_RemoteCommand),
		   where the game only lets admins run them. `sleep` is one of them and is taken over
		   here for everyone as the sleep vote: /sleep in the chat or the console always reaches
		   the server, alone or not.

		Replies go to the top left of the sender's screen (and, for the console way, to their
		console as well).
	*/
	[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
	internal static class Commands
	{
		private const float Reach = 5f;
		private static readonly int SayHash = "Say".GetStableHashCode();
		private static long s_lastSender;
		private static string s_lastText;
		private static DateTime s_lastAt;

		static bool Prefix(ZRoutedRpc.RoutedRPCData rpcData)
		{
			if (rpcData.m_methodHash != SayHash || !QoLPlugin.WorldReady() || !QoLPlugin.Settings.Enabled.Value)
			{
				return true;
			}
			string prefix = QoLPlugin.Settings.ChatPrefix.Value;
			if (prefix.Length == 0)
			{
				return true;
			}
			string text;
			ZPackage pkg = rpcData.m_parameters;
			try
			{
				pkg.SetPos(0);
				pkg.ReadInt();
				new UserInfo().Deserialize(ref pkg);
				text = pkg.ReadString();
			}
			catch (Exception)
			{
				return true;
			}
			finally
			{
				pkg.SetPos(0);
			}
			if (!text.StartsWith(prefix, StringComparison.Ordinal))
			{
				return true;
			}
			ZNetPeer peer = ZNet.instance.GetPeer(rpcData.m_senderPeerID);
			if (peer == null)
			{
				return true;
			}
			// One copy arrives per other player; handle the first, swallow the rest.
			DateTime now = DateTime.UtcNow;
			if (peer.m_uid == s_lastSender && text == s_lastText && (now - s_lastAt).TotalSeconds < 2)
			{
				return false;
			}
			s_lastSender = peer.m_uid;
			s_lastText = text;
			s_lastAt = now;
			string reply = Run(peer, text.Substring(prefix.Length));
			if (reply != null)
			{
				Messages.ToPeer(peer, MessageHud.MessageType.TopLeft, reply);
			}
			return false;
		}

		[HarmonyPatch(typeof(ZNet), "RPC_RemoteCommand")]
		internal static class RemoteCommandPatch
		{
			static bool Prefix(ZNet __instance, ZRpc rpc, string command)
			{
				if (!QoLPlugin.WorldReady() || !QoLPlugin.Settings.Enabled.Value)
				{
					return true;
				}
				string text = (command ?? "").Trim();
				string first = text.Split(' ')[0].ToLowerInvariant();
				if (first != "sleep")
				{
					return true;
				}
				ZNetPeer peer = __instance.GetPeer(rpc);
				if (peer == null)
				{
					return true;
				}
				string reply = Run(peer, text);
				if (reply != null)
				{
					Messages.ToPeer(peer, MessageHud.MessageType.TopLeft, reply);
					__instance.RemotePrint(rpc, reply);
				}
				return false;
			}
		}

		private static string Run(ZNetPeer peer, string command)
		{
			try
			{
				return Handle(peer, command.Trim());
			}
			catch (Exception e)
			{
				QoLPlugin.Log.LogWarning($"Command '{command}' from {peer.m_playerName} failed: {e}");
				return "That did not work, sorry (the server log has why)";
			}
		}

		public static string Handle(ZNetPeer peer, string command)
		{
			string[] words = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string p = QoLPlugin.Settings.ChatPrefix.Value;
			if (words.Length == 0)
			{
				return Help(p);
			}
			QoLPlugin.Log.LogInfo($"Command: {peer.m_playerName}: {command}");
			switch (words[0].ToLowerInvariant())
			{
				case "help":
					return Help(p);
				case "ballista":
					return Ballista(peer, words);
				case "door":
					return Door(peer, words);
				case "tame":
					return Toggle(peer, "tame", words, "Taming and hatching progress");
				case "sleep":
					return Sleep(peer, words);
				default:
					return $"Unknown command {p}{words[0]}. {p}help lists them";
			}
		}

		private static string Help(string p)
		{
			return $"/sleep (vote to skip the night; on|off) | {p}ballista players|tames on|off (the one next to you) | {p}door auto on|off | {p}tame on|off"
				+ $" -- {p}commands reach the server only while another player is online";
		}

		private static bool? OnOff(string word)
		{
			switch (word.ToLowerInvariant())
			{
				case "on": case "1": case "yes": return true;
				case "off": case "0": case "no": return false;
				default: return null;
			}
		}

		private static string Sleep(ZNetPeer peer, string[] words)
		{
			if (words.Length >= 2)
			{
				switch (words[1].ToLowerInvariant())
				{
					case "status": case "?": case "info":
						return SleepVote.Status();
					case "help":
						return "sleep: vote to skip the night | sleep off: withdraw | sleep ?: how the vote stands";
				}
				bool? on = OnOff(words[1]);
				if (on != null)
				{
					return SleepVote.Vote(peer, on);
				}
			}
			return SleepVote.Vote(peer, null);
		}

		private static string Ballista(ZNetPeer peer, string[] words)
		{
			ZDO turret = World.Nearest(peer, World.Kind.Turret, Reach);
			if (turret == null)
			{
				return $"No ballista within {Reach:0} m of you";
			}
			Settings s = QoLPlugin.Settings;
			if (words.Length < 3 || OnOff(words[2]) == null || (words[1] != "players" && words[1] != "tames"))
			{
				bool players = Ballistas.Wanted(turret, Ballistas.PlayersKey, s.BallistasTargetPlayers.Value);
				bool tames = Ballistas.Wanted(turret, Ballistas.TamesKey, s.BallistasTargetTames.Value);
				return $"This ballista shoots at players: {(players ? "on" : "off")}, tames: {(tames ? "on" : "off")}. Change with ballista players|tames on|off";
			}
			bool on = OnOff(words[2]).Value;
			turret.Set(words[1] == "players" ? Ballistas.PlayersKey : Ballistas.TamesKey, on ? 1 : 0);
			Ballistas.Apply(turret, Ballistas.Wanted(turret, Ballistas.PlayersKey, s.BallistasTargetPlayers.Value),
				Ballistas.Wanted(turret, Ballistas.TamesKey, s.BallistasTargetTames.Value));
			return $"This ballista now shoots at {words[1]}: {(on ? "on" : "off")}";
		}

		private static string Door(ZNetPeer peer, string[] words)
		{
			ZDO door = World.Nearest(peer, World.Kind.Door, Reach);
			if (door == null)
			{
				return $"No door within {Reach:0} m of you";
			}
			bool? on = words.Length >= 3 && words[1] == "auto" ? OnOff(words[2]) : null;
			if (on == null)
			{
				return $"This door closes by itself: {(AutoDoors.Wanted(door) ? "on" : "off")}. Change with door auto on|off";
			}
			door.Set(AutoDoors.AutoKey, on.Value);
			return $"This door closes by itself: {(on.Value ? "on" : "off")}";
		}

		private static string Toggle(ZNetPeer peer, string feature, string[] words, string what)
		{
			bool? on = words.Length >= 2 ? OnOff(words[1]) : null;
			if (on == null)
			{
				return $"{what} for you: {(World.Players.Wants(peer, feature) ? "on" : "off")}. Change with {feature} on|off";
			}
			World.Players.Set(peer, feature, on.Value);
			return $"{what} for you: {(on.Value ? "on" : "off")}";
		}
	}
}
