using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace SarkasticQoL
{
	/*
		Everything is read live: a change made with `qol set` or `qol reload` applies at the next
		scan, no restart. Per-piece choices players make with chat commands are stored in the piece
		itself and win over the defaults here.
	*/
	internal class Settings
	{
		public readonly ConfigFile File;

		public readonly ConfigEntry<bool> Enabled;
		public readonly ConfigEntry<string> ChatPrefix;
		public readonly ConfigEntry<float> ScanSeconds;

		public readonly ConfigEntry<bool> ChatServerPresence;
		public readonly ConfigEntry<string> ChatServerName;
		public readonly ConfigEntry<bool> ChatReplyInChat;
		public readonly ConfigEntry<bool> ChatLog;

		public readonly ConfigEntry<string> MotdText;
		public readonly ConfigEntry<float> MotdDelaySeconds;

		public readonly ConfigEntry<bool> SleepEnabled;
		public readonly ConfigEntry<int> SleepRequiredPercent;
		public readonly ConfigEntry<int> SleepMinInBed;
		public readonly ConfigEntry<int> SleepWarnSeconds;
		public readonly ConfigEntry<bool> SleepShowProgress;

		public readonly ConfigEntry<bool> DoorsEnabled;
		public readonly ConfigEntry<float> DoorsCloseAfterSeconds;
		public readonly ConfigEntry<float> DoorsPlayerDistance;

		public readonly ConfigEntry<bool> BallistasEnabled;
		public readonly ConfigEntry<bool> BallistasTargetPlayers;
		public readonly ConfigEntry<bool> BallistasTargetTames;

		public readonly ConfigEntry<bool> TamesProgress;
		public readonly ConfigEntry<int> TamesProgressStepPercent;
		public readonly ConfigEntry<float> TamesProgressRange;

		public readonly ConfigEntry<bool> ContainersEnabled;
		public readonly Dictionary<int, ConfigEntry<string>> ContainerSizes = new Dictionary<int, ConfigEntry<string>>();

		public readonly ConfigEntry<bool> PrefabsEnabled;
		public readonly ConfigEntry<string> PrefabsFile;

		public Settings(ConfigFile config)
		{
			File = config;
			Enabled = config.Bind("General", "Enabled", true, "Enable or disable the plugin.");
			ChatPrefix = config.Bind("General", "ChatPrefix", "!",
				"What a chat message must start with to be a command for this plugin. Such messages are not shown to other players. (A leading / never leaves the client.)");
			ScanSeconds = config.Bind("General", "ScanSeconds", 2f,
				"How often (seconds) the objects around each player are looked at: doors, ballistas, tames, containers.");

			ChatServerPresence = config.Bind("Chat", "ServerPresence", true,
				"List the server as a player, under the name below. A Valheim client sends its chat only to the players in that list, so this is what makes chat commands reach the server when a player is alone, and what lets replies appear as chat lines. The name shows in the players list; there is no map pin.");
			ChatServerName = config.Bind("Chat", "ServerName", "Server",
				"The server's name in the player list and in front of its chat lines.");
			ChatReplyInChat = config.Bind("Chat", "ReplyInChat", true,
				"Answer commands in the chat (needs ServerPresence). Off: at the top left of the screen, where it fades after a few seconds.");
			ChatLog = config.Bind("Chat", "Log", true,
				"Write the players' chat to the server log (needs ServerPresence to see it at all).");

			MotdText = config.Bind("Motd", "Text", "",
				"Shown in the middle of a player's screen after they log in. Empty = nothing. Use | for a line break.");
			MotdDelaySeconds = config.Bind("Motd", "DelaySeconds", 6f,
				"Seconds after the player's character appears before the message is shown, so the world has loaded around them.");

			SleepEnabled = config.Bind("Sleep", "Enabled", true,
				"Skip the night once enough players are in bed instead of all of them.");
			SleepRequiredPercent = config.Bind("Sleep", "RequiredPercent", 50,
				new ConfigDescription("Share of online players that must be in bed. 100 = everyone, as in the game.", new AcceptableValueRange<int>(1, 100)));
			SleepMinInBed = config.Bind("Sleep", "MinInBed", 1,
				new ConfigDescription("At least this many players must be in bed whatever the share.", new AcceptableValueRange<int>(1, 64)));
			SleepWarnSeconds = config.Bind("Sleep", "WarnSeconds", 10,
				new ConfigDescription("Once enough are in bed, tell everyone and wait this long before the night is skipped, so someone sailing or fighting can stop. 0 = at once.", new AcceptableValueRange<int>(0, 120)));
			SleepShowProgress = config.Bind("Sleep", "ShowProgress", true,
				"Tell everyone how many are in bed whenever that number changes.");

			DoorsEnabled = config.Bind("Doors", "Enabled", true,
				"Close player-built doors (not locked ones) once nobody is near. A player can turn it off for one door with !door auto off.");
			DoorsCloseAfterSeconds = config.Bind("Doors", "CloseAfterSeconds", 3f,
				"Seconds a door stays open after the last player left its surroundings.");
			DoorsPlayerDistance = config.Bind("Doors", "PlayerDistance", 4f,
				"A door with a player within this many metres stays open.");

			BallistasEnabled = config.Bind("Ballistas", "Enabled", true,
				"Set what player-built ballistas shoot at. A player can change one ballista with !ballista.");
			BallistasTargetPlayers = config.Bind("Ballistas", "TargetPlayers", false,
				"Default for new ballistas: shoot at players.");
			BallistasTargetTames = config.Bind("Ballistas", "TargetTames", false,
				"Default for new ballistas: shoot at tamed creatures.");

			TamesProgress = config.Bind("Tames", "Progress", true,
				"Show taming, hatching and growing progress as text above the creature or egg to players nearby. A player can turn it off with !tame off.");
			TamesProgressStepPercent = config.Bind("Tames", "ProgressStepPercent", 5,
				new ConfigDescription("Show the text again every this many percent.", new AcceptableValueRange<int>(1, 50)));
			TamesProgressRange = config.Bind("Tames", "ProgressRange", 30f,
				"Players within this many metres see the text.");

			ContainersEnabled = config.Bind("Containers", "Enabled", true,
				"Apply the sizes below to player-built containers. One entry per container piece appears here once the world is loaded, as WIDTHxHEIGHT (game default). A container is only shrunk when its items fit.");

			PrefabsEnabled = config.Bind("Prefabs", "Enabled", true,
				"Apply the per-piece field overrides from the file below.");
			PrefabsFile = config.Bind("Prefabs", "File", "sarkasticeu.qol.prefabs.txt",
				"In BepInEx/config. One override per line: <prefab> <Component>.<field> <value>, e.g. piece_workbench CraftingStation.m_rangeBuild 20. Lines starting with # are comments. Applied to objects the game loads; `qol reload` re-reads it.");
		}

		/*
			Called once the game's prefabs exist: one entry per container a player can build (a
			piece in some tool's piece table: chests, ships, carts), defaulting to its own size.
			Loot chests in ruins are pieces too but in no table, and stay as they are.
		*/
		public void BindContainerSizes()
		{
			foreach (GameObject item in ObjectDB.instance.m_items)
			{
				PieceTable table = item ? item.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces : null;
				if (!table)
				{
					continue;
				}
				foreach (GameObject prefab in table.m_pieces)
				{
					// Ships and carts keep their container on a child object; the override reaches it all the same.
					Container container = prefab ? prefab.GetComponentInChildren<Container>() : null;
					if (!container || ContainerSizes.ContainsKey(prefab.name.GetStableHashCode()))
					{
						continue;
					}
					string name = prefab.GetComponent<Piece>() ? prefab.GetComponent<Piece>().m_name : prefab.name;
					ContainerSizes[prefab.name.GetStableHashCode()] = File.Bind("Containers", prefab.name, $"{container.m_width}x{container.m_height}",
						$"Inventory size of {name} ({prefab.name}), WIDTHxHEIGHT. Game: {container.m_width}x{container.m_height}, at most 8 wide.");
				}
			}
		}

		public static bool ParseSize(string text, out int width, out int height)
		{
			width = height = 0;
			string[] parts = (text ?? "").Trim().ToLowerInvariant().Split('x');
			return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
				&& width >= 1 && width <= 8 && height >= 1 && height <= 12;
		}

		public void Reload()
		{
			try
			{
				File.Reload();
			}
			catch (Exception e)
			{
				QoLPlugin.Log.LogWarning($"Could not re-read the config, keeping the current settings: {e.Message}");
			}
		}
	}
}
