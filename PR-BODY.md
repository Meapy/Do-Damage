Release 2.5.10: taking a portal near the Scrying Glass no longer disconnects players (code 1005), and the glass follows players smoothly (deploys the client and the world server; nobody is disconnected)

## For players

- **Nexus:** the Scrying Glass follows the player it shows smoothly, in the glass and in its big view: the picture used to jump from place to place while they moved, now it glides along with them.

- **Fixes:** taking a portal from beside the Scrying Glass (a dungeon, the vault) no longer disconnects you (the browser's "code 1005"), and neither does a chat line or market search sent while changing worlds.

## Details

### The Scrying Glass follows smoothly
- **Why it jumped:** a player's own tick leaves them out when only their position changed (their client moves them itself), and the glass's feed passed that tick on as it was. So the glass only learned where the watched player was when one of their stats changed, and its camera, centred on them, jumped there.
- **Server:** `SpectatorHub.Tick` puts the watched player's place in every tick it sends watchers (with no stats when none changed), so they glide like any other player.
- **Client:** `glass.rs` eases its camera toward the watched player (about 110 ms to close two thirds of the gap) and cuts straight there after a jump of 4 tiles or more (a teleport, a new floor), so turns and stops don't jerk the picture.
- **Deploys:** the world server (the fix itself) and the client (the eased camera); either alone helps, nobody is disconnected, nothing in Redis changes.

### Changing worlds no longer disconnects players
- **Why:** near the Scrying Glass the client watches it; taking a portal made it say it stopped watching (`Spectate` 0), and that went out on the new connection right after the Hello, before the server had the player in. `NetworkHandler` cut off any message but Hello/Load/Create before then ("Invalid State"), so the player was dropped and the gateway closed their WebSocket without a code (1005). The live log on 2026-11-14/15 had 49 such drops: 34 `SPECTATE`, 12 `PLAYERTEXT`, 3 `MARKET_BROWSE`.
- **Server:** `NetworkHandler` drops such a message instead of the player (only more than 32 on one connection still disconnect it), and now resets its receive buffer after it (the old path skipped that).
- **Client:** `glass.rs` forgets its watching while there's no player in the world and asks again once there is; `Game::glass` sends nothing between worlds.
- **Deploys:** the world server (fixes it for every client, 2.5.9 included) and the client; nobody is disconnected, nothing in Redis changes.

## Checks

- **The Scrying Glass follows smoothly:** world server builds, `cargo test -p bsk-client` passes (100). On an isolated stack (slot 300), the client watched a `bsk-bot` fighting in a Snake Pit through the big view for 25 s with the watched player's and the camera's positions logged every frame (3,698 frames, a temporary log since removed): both moved steadily at about 5.3 tiles/s, never more than 0.09 tiles in a frame, with no stop-and-go frames. A `--shot` shows the big view following the bot.

- **Changing worlds no longer disconnects players:** world server builds; `cargo test --workspace` passes. Found from the live world server's log (`SPECTATE Received with null player` then `Invalid State` on each portal). Not tried on an isolated stack, to get the fix out quickly.
