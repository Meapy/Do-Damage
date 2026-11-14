Release 2.5.10: the Scrying Glass follows players smoothly (deploys the client and the world server; nobody is disconnected)

## For players

- **Nexus:** the Scrying Glass follows the player it shows smoothly, in the glass and in its big view: the picture used to jump from place to place while they moved, now it glides along with them.

## Details

### The Scrying Glass follows smoothly
- **Why it jumped:** a player's own tick leaves them out when only their position changed (their client moves them itself), and the glass's feed passed that tick on as it was. So the glass only learned where the watched player was when one of their stats changed, and its camera, centred on them, jumped there.
- **Server:** `SpectatorHub.Tick` puts the watched player's place in every tick it sends watchers (with no stats when none changed), so they glide like any other player.
- **Client:** `glass.rs` eases its camera toward the watched player (about 110 ms to close two thirds of the gap) and cuts straight there after a jump of 4 tiles or more (a teleport, a new floor), so turns and stops don't jerk the picture.
- **Deploys:** the world server (the fix itself) and the client (the eased camera); either alone helps, nobody is disconnected, nothing in Redis changes.

## Checks

- **The Scrying Glass follows smoothly:** world server builds, `cargo test -p bsk-client` passes (100). On an isolated stack (slot 300), the client watched a `bsk-bot` fighting in a Snake Pit through the big view for 25 s with the watched player's and the camera's positions logged every frame (3,698 frames, a temporary log since removed): both moved steadily at about 5.3 tiles/s, never more than 0.09 tiles in a frame, with no stop-and-go frames. A `--shot` shows the big view following the bot.
