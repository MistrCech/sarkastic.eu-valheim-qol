# Changelog

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
