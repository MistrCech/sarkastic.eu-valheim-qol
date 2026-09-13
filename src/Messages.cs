using SarkasticQoL.Features;
using UnityEngine;

namespace SarkasticQoL
{
	/*
		Messages to vanilla clients through the game's own routed RPCs: ShowMessage (top left or
		centre of the screen, handled by MessageHud), RPC_DamageText (floating text in the world)
		and ChatMessage (a line in the chat, shown only when the sender is in the client's player
		list -- see ServerPresence).
	*/
	internal static class Messages
	{
		public static void ToPeer(ZNetPeer peer, MessageHud.MessageType type, string text)
		{
			if (peer != null)
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)type, text);
			}
		}

		public static void ToAll(MessageHud.MessageType type, string text)
		{
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)type, text);
		}

		// Floating text at a world position, as damage numbers are shown; one RPC per player in range.
		public static void InWorld(ZNetPeer peer, Vector3 position, string text)
		{
			ZPackage pkg = new ZPackage();
			pkg.Write((int)DamageText.TextType.Normal);
			pkg.Write(position);
			pkg.Write(text);
			pkg.Write(false);
			ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "RPC_DamageText", pkg);
		}

		/*
			A line in the player's chat from the server's own entry in the player list. The
			position is put far below the player so the client draws no speech bubble for it (it
			only does within Minimap.m_nomapPingDistance). The client strips < and > from chat.
		*/
		public static void Chat(ZNetPeer peer, string text)
		{
			if (peer == null)
			{
				return;
			}
			if (!ServerPresence.Enabled)
			{
				ToPeer(peer, MessageHud.MessageType.TopLeft, text);
				return;
			}
			UserInfo sender = new UserInfo { Name = ServerPresence.Name, UserId = ServerPresence.Id };
			Vector3 at = World.Position(peer) + Vector3.down * 5000f;
			ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ChatMessage", at, (int)Talker.Type.Normal, sender, text);
		}

		// The reply to a command: in the chat when the server is in the player list, else at the top left.
		public static void Reply(ZNetPeer peer, string text)
		{
			if (QoLPlugin.Settings.ChatReplyInChat.Value)
			{
				Chat(peer, text);
			}
			else
			{
				ToPeer(peer, MessageHud.MessageType.TopLeft, text);
			}
		}
	}
}
