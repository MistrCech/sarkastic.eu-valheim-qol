using UnityEngine;

namespace SarkasticQoL
{
	/*
		Messages to vanilla clients through the game's own routed RPCs: ShowMessage (top left or
		centre of the screen, handled by MessageHud) and RPC_DamageText (floating text in the world).
		Chat is no use: a 1.0 client only shows chat that comes from a real player.
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
	}
}
