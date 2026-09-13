using System;
using System.Collections.Generic;
using HarmonyLib;
using SarkasticQoL.Features;

namespace SarkasticQoL
{
	/*
		Commands from players, three ways in:

		1. Chat addressed to the server. A client sends every chat message once per entry of the
		   player list, addressed to that entry's peer id, and to nobody else. With the server
		   listed as a player (ServerPresence) one copy is addressed to the server and handled in
		   HandleRoutedRPC -- alone or not. The copies for the other players pass through RouteRPC,
		   where a command is swallowed so nobody else sees it.

		2. Chat without the server in the list (ServerPresence off): only the copies for other
		   players exist, so a command works while somebody else is online; the first copy is
		   handled in RouteRPC and the rest swallowed.

		3. The game console: a vanilla client forwards a few of the game's own console commands
		   to the server when it cannot run them itself (ZNet.RemoteCommand -> RPC_RemoteCommand),
		   where the game only lets admins run them. `sleep` is one of them and is taken over
		   here for everyone as the sleep vote: /sleep always reaches the server.

		Replies go to the chat (as a line from the server's entry) or, without it, to the top
		left of the screen.
	*/
	internal static class Commands
	{
		private const float Reach = 5f;
		private static readonly int SayHash = "Say".GetStableHashCode();
		private static readonly int ChatMessageHash = "ChatMessage".GetStableHashCode();
		private static long s_lastSender;
		private static string s_lastText;
		private static DateTime s_lastAt;

		// Chat text out of a "Say" (Talker: normal, whisper) or "ChatMessage" (shout, ping) routed RPC.
		private static bool TryReadChat(ZRoutedRpc.RoutedRPCData data, out string text, out bool ping)
		{
			text = null;
			ping = false;
			ZPackage pkg = data.m_parameters;
			try
			{
				pkg.SetPos(0);
				int type;
				if (data.m_methodHash == SayHash)
				{
					type = pkg.ReadInt();
				}
				else
				{
					pkg.ReadVector3();
					type = pkg.ReadInt();
				}
				new UserInfo().Deserialize(ref pkg);
				text = pkg.ReadString();
				ping = type == (int)Talker.Type.Ping;
				return true;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				pkg.SetPos(0);
			}
		}

		private static bool IsCommand(string text, out string command)
		{
			command = null;
			string prefix = QoLPlugin.Settings.ChatPrefix.Value;
			if (prefix.Length == 0 || text == null || !text.StartsWith(prefix, StringComparison.Ordinal))
			{
				return false;
			}
			command = text.Substring(prefix.Length);
			return true;
		}

		// One message arrives in several copies; only the first is acted on.
		private static bool Seen(ZNetPeer peer, string text)
		{
			DateTime now = DateTime.UtcNow;
			if (peer.m_uid == s_lastSender && text == s_lastText && (now - s_lastAt).TotalSeconds < 2)
			{
				return true;
			}
			s_lastSender = peer.m_uid;
			s_lastText = text;
			s_lastAt = now;
			return false;
		}

		private static bool Ready()
		{
			return QoLPlugin.WorldReady() && QoLPlugin.Settings.Enabled.Value;
		}

		// The copy addressed to the server.
		[HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
		internal static class HandlePatch
		{
			static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData data)
			{
				if (!__instance.m_server || (data.m_methodHash != SayHash && data.m_methodHash != ChatMessageHash) || !Ready())
				{
					return true;
				}
				if (data.m_senderPeerID == __instance.m_id || !TryReadChat(data, out string text, out bool ping) || ping)
				{
					return true;
				}
				ZNetPeer peer = ZNet.instance.GetPeer(data.m_senderPeerID);
				if (peer == null)
				{
					return true;
				}
				if (!IsCommand(text, out string command))
				{
					if (QoLPlugin.Settings.ChatLog.Value)
					{
						QoLPlugin.Log.LogInfo($"Chat: {peer.m_playerName}: {text}");
					}
					return false;
				}
				if (!Seen(peer, text))
				{
					Reply(peer, Run(peer, command));
				}
				return false;
			}
		}

		// The copies for the other players: a command is not passed on.
		[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
		internal static class RoutePatch
		{
			static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData rpcData)
			{
				if (!__instance.m_server || (rpcData.m_methodHash != SayHash && rpcData.m_methodHash != ChatMessageHash) || !Ready())
				{
					return true;
				}
				if (rpcData.m_senderPeerID == __instance.m_id || !TryReadChat(rpcData, out string text, out bool ping) || ping || !IsCommand(text, out string command))
				{
					return true;
				}
				ZNetPeer peer = ZNet.instance.GetPeer(rpcData.m_senderPeerID);
				if (peer != null && !Seen(peer, text))
				{
					Reply(peer, Run(peer, command));
				}
				return false;
			}
		}

		[HarmonyPatch(typeof(ZNet), "RPC_RemoteCommand")]
		internal static class RemoteCommandPatch
		{
			static bool Prefix(ZNet __instance, ZRpc rpc, string command)
			{
				if (!Ready())
				{
					return true;
				}
				string text = (command ?? "").Trim();
				if (text.Split(' ')[0].ToLowerInvariant() != "sleep")
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
					Reply(peer, reply);
					__instance.RemotePrint(rpc, reply);
				}
				return false;
			}
		}

		private static void Reply(ZNetPeer peer, string reply)
		{
			if (reply != null)
			{
				Messages.Reply(peer, reply);
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
				case "feed":
					return PieceToggle(peer, World.Kind.Smelter | World.Kind.ShieldGenerator, "station", Feeding.FeedKey, words, 1,
						z => Feeding.Wanted(z, World.KindOf(z)), "feeds itself from containers nearby");
				case "fire":
					return words.Length >= 2 && words[1] == "feed"
						? PieceToggle(peer, World.Kind.Fireplace, "fire", Feeding.FeedKey, words, 2, z => Feeding.Wanted(z, World.Kind.Fireplace), "feeds itself from containers nearby")
						: "Usage: fire feed on|off (the fire next to you)";
				case "label":
					return Label(peer, words);
				case "clock":
					return PieceToggle(peer, World.Kind.Sign, "sign", Clocks.ClockKey, words, 1, z => z.GetBool(Clocks.ClockKey), "shows the day and time");
				case "sort":
					return PieceToggle(peer, World.Kind.Container, "chest", Sorting.SortKey, words, 1, Sorting.Wanted, "keeps itself sorted");
				default:
					return $"Unknown command {p}{words[0]}. {p}help lists them";
			}
		}

		private static string Help(string p)
		{
			string help = $"{p}sleep (vote to skip the night) | next to a piece: {p}ballista players|tames on|off, {p}door auto on|off, {p}feed on|off, {p}fire feed on|off, {p}label on|off, {p}sort on|off, {p}clock on|off | {p}tame on|off";
			return ServerPresence.Enabled ? help : help + $" -- {p}commands reach the server only while another player is online";
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

		// on|off for the nearest piece of a kind, stored in the piece; `index` is where on|off sits in the words.
		private static string PieceToggle(ZNetPeer peer, World.Kind kind, string what, int key, string[] words, int index, Func<ZDO, bool> current, string does)
		{
			ZDO piece = World.Nearest(peer, kind, Reach);
			if (piece == null)
			{
				return $"No {what} within {Reach:0} m of you";
			}
			bool? on = words.Length > index ? OnOff(words[index]) : null;
			if (on == null)
			{
				return $"This {what} {does}: {(current(piece) ? "on" : "off")}. Change with {string.Join(" ", words, 0, index)} on|off";
			}
			piece.Set(key, on.Value ? 1 : 0);
			return $"This {what} {does}: {(on.Value ? "on" : "off")}";
		}

		private static string Label(ZNetPeer peer, string[] words)
		{
			ZDO chest = World.Nearest(peer, World.Kind.Container, Reach);
			if (chest == null)
			{
				return $"No chest within {Reach:0} m of you";
			}
			bool? on = words.Length >= 2 ? OnOff(words[1]) : null;
			if (on == null)
			{
				return $"This chest has a label sign: {(Labels.Wanted(chest) ? "on" : "off")}. Change with label on|off";
			}
			chest.Set(Labels.LabelKey, on.Value);
			if (!on.Value)
			{
				Labels.Remove(chest, Labels.SignFor(chest));
			}
			return on.Value ? "This chest gets a label sign in a moment" : "Label sign removed";
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
