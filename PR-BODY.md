Release 2.4.4. Deploys the account server and Caddy's config (the deploy rewrites the Caddyfile and reloads Caddy). Nobody is disconnected.

## For players

- **Game:** dying plays out in the world before the death screen: the world freezes and greys, the character falls in a burst of red with its death cry and a jolt of the camera, the screen darkens round where it fell and "You died" comes up with what killed it, then it fades (4 seconds; a click or a key skips it after the first second). The fame summary is fetched meanwhile, so it shows at once (no "Counting your fame…"); its panel fits what it shows, its bonuses come in one by one and the total counts up (Skip, then Continue).
- **Website:** `https://dodmg.com/realmdex/stats` gives the number of players online as plain text, for RealmDex's server list.
- **Servers:** a second server, Europe 2, on its own machine (91.90.80.8), in the server list beside Europe: the same accounts, characters, vaults, guilds and market, with its own realms.

## Details

### RealmDex stats endpoint

- `source/App/Controllers/RealmdexController.cs`: `GET`/`POST realmdex/stats` answers the players online as plain text (`text/plain`, e.g. `12`), the same count as `app/status` (`StatusHistory.Current()`: the public world servers, admin-only ones left out). `Cache-Control: no-store` and `Access-Control-Allow-Origin: *`, so a page on another site can read it too.
- `deploy/deploy.py`: the Caddyfile rewrites `/realmdex/stats` to `/api/realmdex/stats`, which the gateway forwards to the account server. It also answers on the account server's own port (`http://dodmg.com:8080/realmdex/stats`).
- Redeploy: the account server (a restart only pauses sign-ins) and Caddy (reloads only because the Caddyfile changed, keeping WebSockets). Nothing in Redis changes. Nothing for the owner to do, beyond giving RealmDex the URL.

### Death sequence and death screen

- `client-rs/app/src/game.rs`: the `Death` message no longer ends the game at once. It starts `Game::dying` (`Dying`: when, who killed, skipped), which ends the game after `DYING_MS` (4.2 s). While dying, nothing more is read from the socket (the server closes it after a death, and reading that close is what used to end the game), walking and turning stop, and the death adds a kill burst of the character's colours, a red ring (`particles.damaged`, `aoe`) and the class's `DeathSound`.
- `client-rs/app/src/death.rs` (new): the overlay drawn over everything while dying: a red flash, a dark red vignette closing on the character, "YOU DIED" (the existing "You died" line, upper-cased) settling in with a line drawn under it, "Killed by {by}", a "Click to skip" hint after 1.4 s, and a fade into the screens' background colour; plus the camera's shake.
- `client-rs/app/src/main.rs`: while dying, the world is drawn through the grey filter, our own sprite fades out (`Scene::me_alpha`, `view.rs`), the crosshair, status line, quest toasts and community banners are hidden, firing, abilities, pings, the windows and the Escape menu are off, and clicks and keys only skip (after `DYING_SKIP_MS`). The fame request goes out as soon as the death starts; an answer that comes while dying is kept (`death_fame`) and shown when the game is left (`show_fame`), else the old "Counting your fame…" screen waits for it.
- `client-rs/app/src/screens.rs`: the fame summary is as tall as its content (window height at most) and centred, rises in, shows the base fame and bonuses one row at a time (a tick each), counts the total up over a second (a chime when it lands, a fading glow), and its button reads Skip until then (Enter or a click jumps to the end). Same for summaries opened from the graveyard and Recent Deaths.
- Two new lines translated into all ten tables: "Killed by {by}", "Click to skip".
- Wiki: `website/wiki/progression.md`, "Dying", says what players see.
- Redeploy: the client only (no message changes; older clients keep the old behaviour). Nobody is disconnected; nothing in Redis changes.

### Europe 2 (a second world server)

- New VPS 91.90.80.8 (Contabo, KVM, 6 cores, 12 GB): only `bsk-world` (world proxy and world servers), from a build of this branch. Redis, the account server, gateway, Caddy, the bot and the website stay on dodmg.com; Europe 2 reaches Redis through `bsk-redis-tunnel` (SSH, forwarding to dodmg.com's `127.0.0.1:6379` only, as the no-shell `bsk-tunnel` user there). SETUP-LOCAL.md, "Running on a VPS", "Europe 2".
- `source/Shared/ServerSettings.cs`, `RealmSnapshots.cs`, `WorldManager.cs`: `serverSettings.realmsKey` (default `realms`, as before) names the Redis hash of the server's realm snapshots; Europe 2 uses `realms:Europe 2`, so neither server opens the other's realms. Europe's config is unchanged.
- `tools/world_proxy.py`: `BSK_TRUSTED_GATEWAYS` (IPs) lets another machine's gateway give its players' addresses (PROXY_INFO), as a local one does; Europe 2 trusts dodmg.com's, so IP bans work there.
- `source/App/CoreService.cs`: the server list gave `127.0.0.1` (or the LAN address) for every world server to a client on this machine, which is everyone through the gateway, so Europe 2's entry led to Europe. On a VPS (`BSK_PUBLIC_ADDRESS` set, from `bsk.env`) only the world servers with that address are rewritten; others keep theirs. Unset (this PC) as before.
- `deploy/deploy.py`: `"other_worlds"` in the VPS config adds `--allow <host:port>` to the gateway (`BSK_GATEWAY_FLAGS`), so browser and wss players can reach Europe 2. **Owner:** add `"other_worlds": ["91.90.80.8:2051"]` to `deploy/vps.local.json` and the `VPS_CONFIG` secret before merging; the changed `bsk.env` means a new gateway slot and an account server restart (sign-ins pause a few seconds), nobody disconnected.
- Redeploy: account server (the server list), world server (the new setting, default unchanged), the gateway's flags. Europe 2 itself isn't deployed by `deploy.py`: update it by hand on the same version (SETUP-LOCAL.md). Nothing in Redis migrates; Europe 2 adds the hash `realms:Europe 2`.
- **Europe 2 is installed but stopped** (`bsk-world` disabled there) until this release is live: before it, its list entry led to Europe and the gateway refused it. After the deploy: `systemctl enable --now bsk-world` on 91.90.80.8, and check `https://dodmg.com/api/app/serverList` (POST) gives Europe 2 the address 91.90.80.8. If it isn't started by the release, hold back its changelog and For players lines.

## Checks

- Death sequence: `cargo test -p bsk-client` (79 passed, languages included), `cargo clippy` (nothing new), `cargo check --target wasm32-unknown-unknown`. On an isolated stack (slot 3, the test account made admin there) the client died to `/kill Tester Lord of the Lost Lands` with `--shot`s: the frame 1 s in (grey world, flash, title rising), and with the timer stretched to 20 s for the test only, the full overlay in English and `--lang de`; after it the summary came straight up with no wait. Not checked: the count-up mid-way and the spin screen's path (the test characters had no fame), and sound.
- RealmDex stats endpoint: account server builds (Release, no warnings); on an isolated stack (slot 7) `GET /realmdex/stats` answered `200`, `text/plain; charset=utf-8`, body `0`, matching `app/status`'s `players`. The Caddy rewrite isn't tried locally (no Caddy here): check `https://dodmg.com/realmdex/stats` after the deploy.
- Europe 2: world server builds (Debug, linux-x64); `py_compile` of `world_proxy.py` and `deploy.py`. On 91.90.80.8: a 1 ms sleep overshoots by 0.19 ms at worst (dodmg.com: up to 297 ms) and a fixed CPU loop takes a steady 124 ms (dodmg.com: 438-1541 ms); 0.5 ms to dodmg.com. The tunnel: `redis-cli ping` through it answers, and the tunnel key gets no shell on dodmg.com. Europe 2 started once against the live data (proxy on 2051, slot a): it joined the network, opened its 2 realms under `realms:Europe 2` while Europe's 4 stayed in `realms`, and showed in the server list, with the wrong address (`127.0.0.1`), which the account server fix is for; then stopped. Account server builds. Not tested: the fixed list (needs the deploy), and a player on Europe 2 (no test client against live servers).
