Release 2.4.4. Deploys the account server and Caddy's config (the deploy rewrites the Caddyfile and reloads Caddy). Nobody is disconnected.

## For players

- **Game:** dying plays out in the world before the death screen: the world freezes and greys, the character falls in a burst of red with its death cry and a jolt of the camera, the screen darkens round where it fell and "You died" comes up with what killed it, then it fades (4 seconds; a click or a key skips it after the first second). The fame summary is fetched meanwhile, so it shows at once (no "Counting your fame…"); its panel fits what it shows, its bonuses come in one by one and the total counts up (Skip, then Continue).
- **Website:** `https://dodmg.com/realmdex/stats` gives the number of players online as plain text, for RealmDex's server list.

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

## Checks

- Death sequence: `cargo test -p bsk-client` (79 passed, languages included), `cargo clippy` (nothing new), `cargo check --target wasm32-unknown-unknown`. On an isolated stack (slot 3, the test account made admin there) the client died to `/kill Tester Lord of the Lost Lands` with `--shot`s: the frame 1 s in (grey world, flash, title rising), and with the timer stretched to 20 s for the test only, the full overlay in English and `--lang de`; after it the summary came straight up with no wait. Not checked: the count-up mid-way and the spin screen's path (the test characters had no fame), and sound.
- RealmDex stats endpoint: account server builds (Release, no warnings); on an isolated stack (slot 7) `GET /realmdex/stats` answered `200`, `text/plain; charset=utf-8`, body `0`, matching `app/status`'s `players`. The Caddy rewrite isn't tried locally (no Caddy here): check `https://dodmg.com/realmdex/stats` after the deploy.
