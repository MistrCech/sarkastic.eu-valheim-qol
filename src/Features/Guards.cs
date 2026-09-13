using HarmonyLib;

namespace SarkasticQoL.Features
{
	/*
		Holes in what the game lets any player do to a dedicated server.

		Persistent events (the long world events of 1.0): the client console command `pevents
		start|stop <name>` carries no cheat or admin flag, so any player can type it into the chat,
		and the server's RPC_RequestStartEvent / RPC_RequestStopEvent check nothing about the
		sender. Here they are allowed only from the server itself (the game's own triggers) and
		from admins in adminlist.txt; anyone else is told so and logged.
	*/
	internal static class Guards
	{
		private static bool AllowedEventRequest(long sender, string what)
		{
			if (!QoLPlugin.Settings.GuardPersistentEvents.Value || !ZNet.instance || !ZNet.instance.IsServer() || sender == ZNet.GetUID())
			{
				return true;
			}
			ZNetPeer peer = ZNet.instance.GetPeer(sender);
			if (peer == null)
			{
				return true;
			}
			if (ZNet.instance.IsAdmin(peer.m_socket.GetHostName()))
			{
				QoLPlugin.Log.LogInfo($"Persistent event {what} by admin {peer.m_playerName}");
				return true;
			}
			QoLPlugin.Log.LogWarning($"Blocked: {peer.m_playerName} ({peer.m_socket.GetHostName()}) tried to {what} a persistent event (/pevents)");
			Messages.Reply(peer, "Only admins can start or stop world events");
			return false;
		}

		[HarmonyPatch(typeof(PersistentEventSystem), "RPC_RequestStartEvent")]
		internal static class StartPatch
		{
			static bool Prefix(long sender)
			{
				return AllowedEventRequest(sender, "start");
			}
		}

		[HarmonyPatch(typeof(PersistentEventSystem), "RPC_RequestStopEvent")]
		internal static class StopPatch
		{
			static bool Prefix(long sender)
			{
				return AllowedEventRequest(sender, "stop");
			}
		}
	}
}
