# Changelog

## [0.3.1] - 2026-09-13

### Changed

- Chest labels no longer place a sign; the chest itself is named after its contents ("Coal 156",
  "Wood 240, Stone 120 +2"), shown when a player looks at it and as the title of the opened chest,
  in each player's language. The name is a per-object field override (`Container.m_name`), which
  the game reads when it creates the chest, so a chest whose contents changed is created afresh
  (a blink): never while it is open, at most every `[Labels] RefreshSeconds` (10). An empty chest
  has its own name. The 0.2.0 label signs still in the world are removed. Settings moved from
  `[Signs] Labels*` to `[Labels]`; `LabelsEmptyText` is gone.

## [0.3.0] - 2026-09-13

### Added

- Map pins on the cartography tables for what players have found within 30 m: clusters of
  berries, mushrooms, thistle, dandelions, tin, obsidian, tar and the like (three or more of a kind
  within 24 m, "Raspberries x7" at the centre; nothing within 20 m of a player-built piece), copper,
  silver and flametal deposits, burial chambers, crypts, troll and frost caves, infested mines,
  Hildir's places and Haldor, and player-built portals by their tag. Every table in the world gets
  them; a client reads them off a table as usual. A pin a player deletes and writes back to a
  table is taken off every table and not made again; a deposit that is mined out or a portal that
  is gone loses its pin. `!pins on|off` per player (whether what they find is put on the tables),
  `qol pins` for the admin (`list`, `forget`, `clear`), `[Pins]` in the config with the name lists,
  icons and distances.

## [0.2.0] - 2026-09-13

### Added

- Feeding: smelters, kilns, windmills, spinning wheels, blast furnaces and shield generators take
  ore and fuel from player-built chests within 4 m once below half (`!feed on|off` per station,
  on by default); fireplaces too when a player switches one on (`!fire feed on`). Never while a
  player is within 4 m of the station or has the chest open; one of each item stays behind; "+N
  item" floats above the station.
- Chest labels (`!label on|off`): a sign in front of the chest lists what is inside, kept up to
  date with the chest, following it if it settles, removed with the chest.
- Clocks (`!clock on|off`): a sign shows the in-game day and time in ten-minute steps.
- Tidy chests (`!sort on|off`): stacks merged, items ordered by name and quality, laid out from
  the top left, whenever the chest changed and nobody has it open.
- The scan skips an object a feature has just created afresh under a new id, so nothing acts on
  the old copy in the same pass.

Verified on a local 1.0.12 server with a spawned scene: a smelter took 19 coal and 9 copper ore
and left one of each, a fire took 10 wood, a chest with four stacks became two, a label read
"Wood 40 | Stone 12 | Copper ore 3 | +1" and followed the chest, a clock read "Day 44 - 19:10",
`!label off` removed the sign.

## [0.1.3] - 2026-09-13

### Added

- `[Guards] PersistentEventsAdminOnly` (on): only admins and the game itself may start or stop
  persistent world events. In Valheim 1.0.12 the client console command `pevents start|stop
  <name>` has no cheat or admin flag, so any player can type it into the chat, and the server's
  handlers check nobody. Others are told "Only admins can start or stop world events" and logged.

## [0.1.2] - 2026-09-13

### Added

- The server appears in the player list under `[Chat] ServerName` (`Server`). A Valheim client
  sends its chat only to the listed players, so this makes every chat message reach the server --
  a player alone can use `!` commands -- and lets the server answer as a chat line under that name
  (`[Chat] ReplyInChat`), instead of a top-left message that fades. Ordinary chat is written to the
  server log (`[Chat] Log`). Verified on a local 1.0.12 server with fake peers: the list carries the
  entry with the server's peer id as its character, a chat message addressed to the server is
  handled, the reply arrives as a `ChatMessage` from that entry.

## [0.1.1] - 2026-09-13

### Fixed

- Chat commands never reached the server from a player who was alone: a Valheim client sends chat
  only to the other players, never to the server. `!` commands now work while another player is
  online (the first copy is handled, the rest swallowed), and the sleep vote moved to `/sleep`,
  which rides on the game's own `sleep` console command that a client always forwards to the
  server; taken over here for every player, not just admins. `/sleep` votes, `/sleep off`
  withdraws, `/sleep ?` shows the state; a vote counts like being in bed.

## [0.1.0] - 2026-09-13

First release. Chat commands (`!help`, `!ballista`, `!door`, `!tame`, `!sleep`), the `qol`
console command (`status`, `set`, `reload`, `containers`), and the features: sleep by vote with
a warning before the skip, doors that close by themselves, ballistas that leave players and tames
alone, taming/hatching/growing progress text, container sizes, field overrides from a text file,
message of the day.

Verified on a local Valheim 1.0.12 server with fake peers: commands and replies, the vote logic,
the message of the day, chests resized (grown, and shrunk only when their items fit) and created
afresh while loaded, a field override applied to workbenches, the console commands through
Dedicated Simulation's console.
