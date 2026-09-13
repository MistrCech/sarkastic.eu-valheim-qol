using System;
using System.Reflection;
using HarmonyLib;
using Splatform;

namespace SarkasticQoL.Features
{
	/*
		The server as a player in the player list. A Valheim client sends its chat to every entry
		of the player list it got from the server (one copy each, addressed to the entry's peer
		id) and to nobody else, so with the server itself listed -- under a name of our choosing,
		with the server's own peer id as its "character" -- every chat message also reaches the
		server, alone or not. And a chat line the server sends is shown by the client only if the
		sender's platform id is in that list, so this is also what lets replies appear in the chat
		under that name. (Valheim 1.0.12's RelationsManager.CheckPermissionAsync grants everything,
		so no Steam check is involved.) The entry has no public position, so no map pin; the
		client's player list shows it as one more name.
	*/
	[HarmonyPatch(typeof(ZNet), "SendPlayerList")]
	internal static class ServerPresence
	{
		private static string s_steamId;

		public static bool Enabled => QoLPlugin.Settings.ChatServerPresence.Value;
		public static string Name => QoLPlugin.Settings.ChatServerName.Value;

		// The "character" id of the entry: its user part is the server's peer id, so the client addresses chat to the server.
		public static ZDOID CharacterId => new ZDOID(ZNet.GetUID(), 1u);

		// The server's own Steam game server id if Steamworks can tell it, else 1; either way a Steam-shaped id no player has.
		public static PlatformUserID Id => new PlatformUserID(ZNet.instance.m_steamPlatform, SteamId());

		private static string SteamId()
		{
			if (s_steamId == null)
			{
				s_steamId = "1";
				try
				{
					Type server = Type.GetType("Steamworks.SteamGameServer, com.rlabrecque.steamworks.net");
					object id = server?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
					object raw = id?.GetType().GetField("m_SteamID")?.GetValue(id);
					if (raw is ulong value && value != 0)
					{
						s_steamId = value.ToString();
					}
				}
				catch (Exception)
				{
					// Not on Steam, or the API changed: the fallback id does the job.
				}
			}
			return s_steamId;
		}

		static bool Prefix(ZNet __instance)
		{
			if (!Enabled || !__instance.IsServer())
			{
				return true;
			}
			__instance.UpdatePlayerList();
			if (__instance.m_peers.Count <= 0)
			{
				return false;
			}
			// Same layout as ZNet.WritePlayerInfo, with one more entry.
			ZPackage pkg = new ZPackage();
			pkg.Write(__instance.m_players.Count + 1);
			foreach (ZNet.PlayerInfo player in __instance.m_players)
			{
				Write(pkg, player);
			}
			Write(pkg, new ZNet.PlayerInfo
			{
				m_name = Name,
				m_characterID = CharacterId,
				m_userInfo = new ZNet.CrossNetworkUserInfo { m_id = Id, m_displayName = Name, m_serverAssignedDisplayName = Name, m_playfabId = "" },
				m_publicPosition = false,
			});
			foreach (ZNetPeer peer in __instance.m_peers)
			{
				if (peer.IsReady())
				{
					peer.m_rpc.Invoke("PlayerList", pkg);
				}
			}
			return false;
		}

		private static void Write(ZPackage pkg, ZNet.PlayerInfo player)
		{
			pkg.Write(player.m_name);
			pkg.Write(player.m_characterID);
			pkg.Write(player.m_userInfo.m_id.ToString());
			pkg.Write(player.m_userInfo.m_displayName ?? "");
			pkg.Write(player.m_userInfo.m_serverAssignedDisplayName ?? "");
			pkg.Write(player.m_userInfo.m_playfabId ?? "");
			pkg.Write(player.m_publicPosition);
			if (player.m_publicPosition)
			{
				pkg.Write(player.m_position);
			}
		}
	}
}
