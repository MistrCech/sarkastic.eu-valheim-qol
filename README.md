# Sarkastic.eu QoL

A server-side [BepInEx](https://github.com/BepInEx/BepInEx) plugin for Valheim 1.0 dedicated servers:
small conveniences for the players, all switchable, none of them needing anything on the client.

- **Sleep by vote:** the night is skipped once half the players (configurable) are in bed, with a
  warning a few seconds before, and a note to everyone whenever the count changes.
- **Doors close by themselves** once nobody is near (player-built, unlocked doors only).
- **Ballistas** stop shooting at players and tamed creatures (each ballista can be set on its own).
- **Taming, hatching and growing progress** floats above the creature or egg.
- **Container sizes** per kind of container, from the config, changeable while the server runs.
- **Field overrides** for any player-built piece from a text file: the range of a crafting
  station, a fireplace that needs no fuel, whatever the game keeps in a public field.
- **Message of the day** after logging in.

Players need nothing: vanilla clients work. The plugin depends on BepInEx (with its Harmony) and
nothing else.

## How players use it

Players type commands into the normal chat. The reply appears at the top left of their screen.

| Command | |
|---|---|
| `/sleep` | Vote to skip the night (again: withdraw; `/sleep off`, `/sleep ?`). Works whenever you are online, alone or not. |
| `!help` | Lists the commands. |
| `!ballista players on\|off`, `!ballista tames on\|off` | The ballista next to you (within 5 m). Without on/off: shows its setting. |
| `!door auto on\|off` | The door next to you. |
| `!tame on\|off` | Whether you see taming, hatching and growing progress. |

Why two forms: a Valheim client sends chat only to the *other* players (one copy each, for the
server to pass on) and never to the server itself, so a `!` message reaches the server only while
another player is online, and is then handled and not passed on. The game's own `sleep` console
command, on the other hand, is one of the few a client always forwards to the server (where the
game would only let admins run it), so `/sleep` works alone too; typed in the chat it goes the
same way.

A choice for one piece is kept in that piece; a choice for yourself is kept on the server under
your platform id.

## How the admin uses it

On the server's own console -- with [Dedicated Simulation](https://github.com/cechacek/valheim-serverside)
that is the panel's console, e.g. AMP -- the command `qol`:

| Command | |
|---|---|
| `qol status` | What is on. |
| `qol set <Section.Key> <value>` | Change a setting now, e.g. `qol set Sleep.RequiredPercent 60`, `qol set Containers.piece_chest_wood 6x2`, `qol set Motd.Text Welcome!`. Saved to the config file. |
| `qol reload` | Re-read the config file and the field override file after editing them. |
| `qol containers` | The container sizes with the game's defaults. |

Without Dedicated Simulation the game's console exists on a dedicated server but nothing feeds
it; edit the config file and restart instead.

## Configuration

`BepInEx/config/sarkasticeu.qol.cfg`; everything is read live.

| Setting | Default | |
|---|---|---|
| `[General] ChatPrefix` | `!` | What a chat message starts with to be a command. |
| `[General] ScanSeconds` | 2 | How often the objects around each player are looked at. |
| `[Motd] Text`, `DelaySeconds` | empty, 6 | Shown in the middle of the screen after the player's character appears; `\|` breaks a line. |
| `[Sleep] RequiredPercent`, `MinInBed`, `WarnSeconds`, `ShowProgress` | 50, 1, 10, true | The share of online players that must be in bed, the least number, the warning before the skip, and whether everyone is told when the count changes. |
| `[Doors] CloseAfterSeconds`, `PlayerDistance` | 3, 4 | A door closes this long after the last player left this distance. |
| `[Ballistas] TargetPlayers`, `TargetTames` | false, false | Defaults for ballistas nobody set with `!ballista`. |
| `[Tames] Progress`, `ProgressStepPercent`, `ProgressRange` | true, 5, 30 | Progress text, how often it repeats, who sees it. |
| `[Containers] <prefab>` | the game's size | One entry per buildable container appears once the world is loaded, `WIDTHxHEIGHT`, at most 8 wide. A container is only shrunk when its items fit. |
| `[Prefabs] File` | `sarkasticeu.qol.prefabs.txt` | One override per line: `<prefab> <Component>.<field> <value>`, e.g. `piece_workbench CraftingStation.m_rangeBuild 20`. |

Every feature has its own `Enabled`.

## How it works

Everything goes through what a vanilla client already understands:

- **The objects' data.** A door's open/closed state, a ballista's per-piece choice, a container's
  items: the server changes the world object's data and every client shows the result.
- **Per-object field overrides.** The game lets a world object override the public fields of its
  own components (`ZNetView.LoadFields`: `HasFields`, `HasFields<Component>`,
  `<Component>.<field>`), which every client applies when it creates the object. Container sizes
  and the field override file use this; an object that is already loaded is created afresh under a
  new id so the change shows at once.
- **The game's own messages.** `ShowMessage` for the top-left and centre texts, `RPC_DamageText`
  for text floating in the world.
- **Four patched methods:** the sleep vote (`Game.EverybodyIsTryingToSleep`), chat commands
  caught before they are passed on (`ZRoutedRpc.RouteRPC`), `/sleep` taken over from the game's
  admin-only console command (`ZNet.RPC_RemoteCommand`), and a player's character appearing
  (`ZNet.RPC_CharacterID`).

Every `ScanSeconds` the objects in the zones around one player per frame are handed to the
features; a few thousand at most, and nothing is changed unless something differs.

## Installation

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2350 or newer on the dedicated server.
2. Copy `SarkasticEU_QoL.dll` from the latest release into `BepInEx/plugins/`.
3. Restart the server. `BepInEx/LogOutput.log` shows `Sarkastic.eu QoL running`.

Made for the Sarkastic.eu server together with
[Dedicated Simulation](https://github.com/cechacek/valheim-serverside) (the server simulates the
world around players, admin console) and
[Resource Regrowth](https://github.com/cechacek/valheim-resource-regrowth) (one-time world content
comes back). Each works on its own.

## Building

`dotnet build src/SarkasticEU_QoL.csproj -c Release` with `VALHEIM_DEDI_INSTALL` pointing at a
dedicated server install that has BepInEx. The game assemblies are publicized at build time.
