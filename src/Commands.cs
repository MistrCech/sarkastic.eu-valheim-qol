using System;
using System.Collections.Generic;
using HarmonyLib;
using SarkasticQoL.Features;

namespace SarkasticQoL
{
	/*
		Chat commands from players. A message a player types into the chat reaches the server as
		the routed RPC "Say" on the player's own character (Talker.Say), which the server hands to
		every other player. One that starts with the prefix is handled here instead and not passed
		on, so only the sender sees it (in their own chat, as the client shows it at once). The
		reply is a message at the top left of their screen.
	*/
	[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
	internal static class Commands
	{
		private const float Reach = 5f;
		private static readonly int SayHash = "Say".GetStableHashCode();

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
			string reply;
			try
			{
				reply = Handle(peer, text.Substring(prefix.Length).Trim());
			}
			catch (Exception e)
			{
				QoLPlugin.Log.LogWarning($"Chat command '{text}' from {peer.m_playerName} failed: {e}");
				reply = "That did not work, sorry (the server log has why)";
			}
			if (reply != null)
			{
				Messages.ToPeer(peer, MessageHud.MessageType.TopLeft, reply);
			}
			return false;
		}

		public static string Handle(ZNetPeer peer, string command)
		{
			string[] words = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string p = QoLPlugin.Settings.ChatPrefix.Value;
			if (words.Length == 0)
			{
				return Help(p);
			}
			QoLPlugin.Log.LogInfo($"Chat: {peer.m_playerName}: {p}{command}");
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
					return Sleep();
				default:
					return $"Unknown command {p}{words[0]}. {p}help lists them";
			}
		}

		private static string Help(string p)
		{
			return $"{p}ballista players|tames on|off (the one next to you) | {p}door auto on|off | {p}tame on|off | {p}sleep";
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

		private static string Sleep()
		{
			Settings s = QoLPlugin.Settings;
			if (!s.SleepEnabled.Value)
			{
				return "The night is skipped when everyone is in bed";
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
			int needed = Math.Max(s.SleepMinInBed.Value, (total * s.SleepRequiredPercent.Value + 99) / 100);
			return $"{inBed} of {total} in bed; {needed} needed to skip the night ({s.SleepRequiredPercent.Value}%)";
		}
	}
}
