# Rust client

The game's client, "Do Damage" to players: a Rust rewrite of the AIR client for Windows, Linux,
macOS and the browser, with the same art and server protocol. Browsers (and desktop clients built
for `https://dodmg.com/api`) reach the servers through the WebSocket bridge in `gateway`. Read
[../AGENTS.md](../AGENTS.md) first; [PARITY.md](PARITY.md) lists what the AIR client did and
what's still to port.

## Build and run

```
python tools\gen_rs_manifest.py              # after the asset lists change
cd client-rs; cargo run                      # desktop, development layout
cargo test -p bsk-client                     # includes the translation checks
python tools\build_client_rs.py              # build\client-rs\native: exe + assets\
python tools\build_client_rs.py --web        # build\client-rs\web: index.html + wasm + assets/
```

- **Releasing to players** (ask the owner first; ../AGENTS.md, Git, "Releasing"): on the release
  branch raise the version and name the changelog section, then squash-merge its pull request. The
  merge deploys it: GitHub's `client` job runs `deploy.py --publish-only`, which, when
  `client-rs/` or `client/src/` changed since the server's newest release, builds with
  `--account https://<site>/api --package --publish` and stops the deploy if the version wasn't
  raised. It writes `dist\Do-Damage-windows.zip` (the website's download) and the release's files
  (only the blobs the server lacks, `bsk-publish --known`), and the `deploy` job puts them on the
  VPS at `<account>/client/`. `python deploy\deploy.py` from the main checkout is the fallback.
  Never publish without `--account` (the script refuses): a build without it has
  `http://127.0.0.1:8080` built in and strands players (2.1.1, the first 2.2.0). Desktop clients
  switch at their next portal or on the menu; browsers at the next page load. Check
  `https://dodmg.com/api/client/windows/latest.json` afterwards.
- **Versions:** `[workspace.package] version` in `Cargo.toml`, or `[workspace.metadata] release`
  for a four-part one (1.1.3.1, baked in as `BSK_VERSION`; it must extend the version). Every
  version needs a `CHANGELOG.md` section (a test checks), and `--publish` refuses while
  `## Unreleased` is left.
- **Updates:** `--package` and `--publish` builds carry the update key (`build/client-update.key`);
  they check `<account>/client/` (`update.rs`), download changed files in the background and hand
  over at the next portal (`--resume`, passing the login, the portal's key, the window and the
  chat on a pipe). Other builds never update. `bsk-update` is the release format, `publish` the
  tool. To try it: `--account http://127.0.0.1:8082 --publish --publish-to build/iso1/client`.
- **Web:** needs `rustup target add wasm32-unknown-unknown` and `wasm-bindgen-cli` at the
  `Cargo.lock` version (the script says). Each compressible file gets a `.gz` and `.br` beside it,
  which the gateway serves. Every file is fetched with a content hash as `?v=` (the page names the
  module's, wasm's and manifest's; the manifest's `versions` each asset's), so updates download
  only what changed; the gateway caches `?v=` files for good and the page `no-cache`. The game
  reports assets (`window.bskAssetLoaded`) and readiness (`window.bskReady`) to the page.
  `.claude/launch.json` has `client-rs-web`, a gateway on 8095 for isolated stack slot 1.
- **Keys in the browser:** winit cancels key presses on the canvas; `web.rs` hands back the
  clipboard keys, reload, tabs, zoom and the developer tools, and clipboard text comes through the
  page's events (`textedit.rs` `clipboard`). To test, dispatch `KeyboardEvent`s (with `code`,
  Control first) and `ClipboardEvent`s on the canvas; the built-in browser pane leaves `code`
  empty for some keys, draws about two frames a second in the background (wait a second between
  steps on the sign-in screens) and can't go fullscreen.
- **Window title** is "Do Damage", never "betterSkillys" (the AIR client's). Settings and the
  saved login stay in `betterSkillys-rs` folders.

## Languages

English is written in the code (`app/src/i18n.rs`); German, Spanish, French, Italian, Dutch,
Polish, Brazilian Portuguese, Russian, Turkish and Ukrainian are tables in `app/lang/<code>.json`,
built in. The player picks one in Options, Language (`language=<code>`), else the system's is
used. For text players see:

- Wrap it where it's drawn: `tr("Play")`; `tf!("Level {level}", level)` for values (named, so a
  language can reorder them); `tn!(count, "{n} day|{n} days")` for counts (Russian, Ukrainian and
  Polish give three forms, Turkish may give one); `mark("Move up")` in a `const` table with `tr`
  where it's drawn. Never build a sentence from pieces: give each whole line its own key. Put the
  literal right inside the call (`if a { tr("Yes") } else { tr("No") }`), where the test finds it.
- Add the English line as the key in **every** table, translated as a native player would write
  it: the game's own words, the language's quotes and spacing (French « … » with a no-break space
  before `!?:;`, German „…“), short enough for its button. Keep `{name}`s, `/commands`, key names
  and numbers. `cargo test -p bsk-client i18n` fails on a missing or unused line, a differing
  `{name}`, the wrong number of forms, or a character Myriad Pro lacks (no CJK, Arabic or
  Vietnamese in bold).
- German and Russian run a third longer: `Ui::label_fit` and `button` shrink a line to fit; check
  new windows with `--lang de` and `--lang ru` in a `--shot`.
- Not translated: what the server sends (chat, item and monster names, NPC speech, quest titles,
  board names), player names and the logs. A server name the client shows as its own label is
  looked up in a `server::` list (as `community::SERVER_NAMES`), else shown as sent.

## Gateway (browser play)

`bsk-gateway` serves the web build and bridges to the servers on one port: `/api/<path>` to the
account server, `/ws/<host>:<port>` a WebSocket carrying the world server's TCP stream, only to
`--allow`ed servers (`--allow <public>:2051=127.0.0.1:2051` maps one to the other, as on the VPS).
`--world-state <dir>` (`build/live`) routes each bridge to the world server slot its Hello belongs
to, as `tools/world_proxy.py` does (`gateway/src/world.rs`). It binds 127.0.0.1 unless `--bind`
says otherwise; opening it to players is the owner's call. On the VPS it runs in slots
(`bsk-gateway@a`/`b`/`c`): `/healthz` answers `ok <bridges>`, and SIGUSR1 makes it hand over (503
there, exits once its bridges close or after `--drain-limit` seconds).

```
cargo run -p bsk-gateway -- --web build/client-rs/web --account http://127.0.0.1:8080 --allow 127.0.0.1:2051
```

## Testing against the servers

Only ever against an isolated stack. If another session holds the default one (6380/8081/2052),
start your own slot; in a worktree, copy the data from the main checkout:

```
python tools\testclient\iso_stack.py --slot 1 start --data-from <main checkout>\source\Redis\data
cargo run -p bsk-bot -- --account http://127.0.0.1:8082 --credentials <main>\test-account.local.txt --seconds 60
cargo run -p bsk-bot -- --account http://127.0.0.1:8095/api --gateway ws://127.0.0.1:8095/ws --credentials ...
python tools\testclient\iso_stack.py --slot 1 stop
```

**The bot** (`bsk-bot`) logs in, joins the Nexus with the first living character (or makes a
Wizard) and follows Reconnects through new connections; it exits non-zero if disconnected. The
second form goes through the gateway, as a browser does. Every 10 s it logs the server's ping and
the gaps between NewTicks. Its flags:

- `--say "/godlands;;/god"`: lines (split by `;;`) 3 s apart after each join. God mode is lost
  with each world change, so `--say` it after joining (`"/gear;;/arena;;/god"`).
- `--wander <tiles>`: walk a circle, for load tests (accounts from `/account/register`).
- `--fight`: play like a player (`bot/src/fight.rs`): circle the nearest enemy at weapon reach,
  shoot at the weapon's rate, use the ability, avoid hurting ground. `--class <name or 0x type>`
  for a new character, `--view <tiles>` for its sight. It reads the game's XML from `assets/`
  beside it, or the repository's.
- `--enter "Wine Cellar Portal;;Oryx's Sanctuary Portal"`: take the first portal with each id,
  once each (with `/tq` and `/smite` it walks the Oryx chain).
- `--arena-vote "5;0"`: vote at each Arena break (1-5 a combine pick, 0 Next) and log `ArenaVotes`.
- `--visit-answer yes|no|wait`: answer `/visit` requests, logging each.
- `--ping "1;3"`: ping those kinds (0 look, 1 danger, 2 go, 3 loot, 4 help) and log every
  `PingShow`; `--ping ""` only listens.
- `--shots`: log every enemy volley seen (monster, projectile id, count, spread, angle, damage):
  `/spawn` a monster in a `/vault` to check its behaviour.

**The client** can be scripted too (desktop only):

| Flag | What |
|---|---|
| `--credentials <file>` | Sign in with the file's `email:` and `password:` and play the first character (a new Wizard if none) |
| `--stay` | With `--credentials`: stop at the character list |
| `--screen <name>` | With `--credentials --stay`: show `home`, `account`, `discord`, `discord-code`, `sprites`, `sprites-mine`, `sprites-review` (needs an admin, rank 6 in the stack's Redis), `sprites-item:<item id>`, `class:0x<type>` (a class's page) or `replay:<account>:<char>` (that death's summary, then its replay) |
| `--replay <file>` | Play a saved death replay (`.bskreplay`) over the first screen, signed in or not |
| `--no-update` | Don't look for or install updates |
| `--bench` | No vsync or frame cap, and every 2 s the frame rate, CPU time by section and draw calls in the log (measure a release build) |
| `--shot <png> --shot-after <seconds>` | Save one frame, then quit |
| `--walk-to x,y[;x,y...]` | Walk to world positions in turn |
| `--hold <key>:<ms>` | Hold keys in turn: `w a s d q e r o z 0 1`-`8 space hp mp`, `f` fires, `i` toggles autofire, `-` waits |
| `--aim <degrees>` / `--aim auto` | Where `f` fires, or at the nearest enemy (`/autoaim`: needs an admin on the test stack) |
| `--say <text>` | Chat lines or commands split by `;;`, each once another world is joined or 3 s after the last |
| `--npc <class>` | Open that NPC's window once in the game (`GuildBoard`, `PotionStorage`, `Vault` in a vault, `ScryingGlass`...) |
| `--chat <channel>[:text]` | Open the chat box on `all`, `local`, `guild` or `party`, with the text typed in |
| `--quests <tab>` | Open the quest log at `daily`, `weekly`, `monthly`, `login`, `achievements` or `customize` |
| `--pass <tab>` | Open the Realm Pass at `rewards`, `earn`, `looks` or `pets` |
| `--options <tab>` | Open Options at `controls`, `gameplay`, `graphics`, `visuals`, `crosshair`, `sound` or `language` |
| `--effect <type>` | Show that `ShowEffect` type on you every half second (101 and up: the Necromantic, Ascendant and Father looks, `relic::look`, 133 to 138 the ward's; 140 to 148: the halos' forcefields, tier 0 to 8; 2xx: look xx, for the looks from 39 on; 243 plays the Vial of Cascading Dusk's whole cascade) |
| `--type <text>` | Type into the first NPC prompt, the guild board, the forge's or vault's search, the Marketplace's price or the Scrying Glass's chat |
| `--lang <code>` | Use that language for this run |
| `--size WxH` | Open the window at this size (physical pixels) |
| `--mouse x,y` | Put the mouse there and ignore the real one: tooltips, aiming |
| `--mouse-script "down@ms:x,y;move@ms:x,y;up@ms:x,y;right@ms:x,y"` | Mouse steps after joining (also `middle`, `mdown`, `mup` for pings), timed from the first world joined; positions in window pixels, read off a `--shot` first |

- From Git Bash use `MSYS_NO_PATHCONV=1` and a Windows path for `--credentials`
  (`C:/Users/...`), or it stays on the sign-in screen. `--shot` too: a `/c/Users/...` path saves
  nothing.
- `iso_stack.py start` makes Tester (account 1) an admin, so `/dungeon`, `/tq`, `/empower` work;
  with no account 1 in the data (a fresh stack) it says so. Start the client only once the world
  log (`build/iso<N>/ws/ws.out`) has `RealmManager.Restore` or `opened again`: before that the
  account server lists no world servers and the client stops at the server list.
- `--credentials` plays the first living character in the list (the lowest id), and no flag picks
  another. To play a different class on an isolated copy, take the others out of the account's
  living set and put them back afterwards. The members are 4-byte little-endian ids, so pipe the
  bytes in: `printf '\x01\x00\x00\x00' | redis-cli -p <port> -x srem alive.1` (`sadd` to put it
  back). A quoted `"\x01..."` argument or a line on stdin doesn't match and removes nothing.
- Reading a binary hash field (a character's `items`) with `redis-cli --raw` on Windows turns
  each newline byte (0x0A) into CR LF, so a script that edits and writes it back shifts every slot after
  it (2026-11-26). Write the whole value fresh from a file (`redis-cli -x hset char.1.2 items <
  items.bin`) and check it with `--no-raw`. The old Redis takes one field per `hset`.
- To see a class's skins as the game draws them, `--credentials ... --stay --screen
  class:0x<type>` shows its page with every skin and price; in the Nexus the Lil' Oryx pet can
  stand right on the player and hide them.
- `--size` is in physical pixels: on a scaled display `--size 1280x720` gave 1024x576 shots, and
  `--mouse` positions are in those. Read them off a `--shot` first. An ability aimed at the HUD
  bar or off the floor (a Mechanic's blueprint at the sky) is refused without a word.
- `--hold space:<ms>` didn't use a Mechanic's blueprint in the vault (2026-11-24): four presses, MP
  still full, cause not found. Check MP in the shot before trusting a scripted ability, and test an
  ability's logic with a server unit test rather than spending runs on it.
- The window's size isn't the same from run to run: without `--size` it was 1280x800, and with
  `--size 1280x720` one run came out 2560x1369 (2026-11-25). Take a `--shot` first and read
  `--mouse` positions off that run's size, or the aim lands off the floor.
- `--hold space:<ms>` sent the ability in 3 of 7 runs with the same flags (2026-11-25, a Druid in
  a vault): the client's log line `sent: {...}` names `UseItem` when it went. Check it before
  reading anything into a shot; one run that sends it is enough.
- On the copied data (2026-11-26) `--credentials` played Tester's character 2 (the Mechanic), not
  character 1, though both are in `alive.1`: before changing a character for a test, check which
  one the first run plays (the class in its shot's character card), or change both.
- `--effect 270` repeats the Maw every half second, so a shot shows two or three on top of each
  other: judge how heavy an effect looks from a real cast (`--mouse` on the target, `--hold
  space:150`), not from `--effect`.
- The world log doesn't always say `RealmManager.Restore` or `opened again` (2026-11-27, a fresh
  copy of the data): wait instead for port 2052 (plus the slot) to listen and a
  `NexusGuide.OpenDungeon` line; the client signed in fine then.
- Teleporting onto ground no one can walk on (a chasm's `Tomb Abyss`) is refused by the client
  without a word: the server said "Teleported" and the shot showed the player where they were
  (2026-11-27). `/tq <id>` now lands beside such an object. A random-layout dungeon's rooms differ
  each run, so reach one with `/tq <an object in it>` (`Lake Heart`, `Chasm Heart`, a keeper)
  rather than coordinates, and expect "There is no ..." when that run didn't pick the room.
- `iso_stack.py start --data-from` copies the data again: changes made to the test copy's
  characters (a class, items, `alive.1`) are lost and must be made again after each start.
- To wear a skin on the test copy, setting the character's `skin` isn't enough: the server puts it
  back to 0 at sign-in unless the account owns it. Add it to the account's `skins` first, 2-byte
  little-endian types: `printf '\x14\xe2' | redis-cli -p <port> -x hset account.1 skins` (0xE214),
  then `hset char.1.<id> skin 57876`. A pet out stands in front of you in the Nexus.
- To try a class on the test copy (2026-11-26): `hset char.1.<id> charType <type in decimal>`,
  then give it that class's gear in Redis: `/gear` gives nothing to a level 20 character ("starter
  gear for characters below level 20"), and the old class's weapon and ability are dropped from
  their slots. `items` is 28 little-endian ushorts (0xFFFF empty), written from Python:
  `python -c "import sys,struct; sys.stdout.buffer.write(b''.join(struct.pack('<H', i & 0xffff) for i in [0x5810, 0x581b] + [-1] * 26))" | redis-cli -p <port> -x hset char.1.<id> items`.
- To see your own shots (2026-11-26): `--hold f:<ms>` never fired (no `PlayerShoot` in the `sent:`
  lines; F also drinks a potion); `--hold i:100` (autofire) with `--mouse` does. The Nexus doesn't
  let you shoot at all. The `--hold` steps start when the first world is joined, after
  `--shot-after`'s clock, so a long `--hold -:<ms>` first can leave the shot taken before the
  autofire: keep the wait short (3000). On the test stack your Lil' Oryx fights the vault's dummy:
  its hits count in `EnemyHit` and its shots (grey blades) fly from where you stand, so neither
  shows that you fired.
- `/autoaim` toggles: with `--aim auto` already turning it on, saying it too turns it off. A
  monster `/spawn`ed in a vault stays there across runs, on the spot you arrive at, and hides you
  in every shot after: spawn it a step away or test in the Nexus.
- `--effect` took only values up to 255 until 2026-11-26 (it was a `u8`), so `--effect 256` and up
  (the looks from 56 on: turrets, caches, pearls, the Monk's Flow) did nothing, without a word.
- `--credentials` logins are never saved; a run without it signs in with the owner's saved login
  (their email on screen): don't share those shots.
- Runs with `--shot`, `--hold` or `--mouse-script` open without taking focus, on the display
  Visual Studio Code is on (`--display-of <text>` picks another, Windows only).
- `RUST_LOG=info,betterskillys=debug` logs every chat line (`chat <name>: <text>`).
- A bare `cargo run` in `client-rs` stops with "could not determine which binary to run"
  (2026-11-27): run `cargo run -p bsk-client -- <flags>`. To aim at the vault's Training Dummy
  with a sword, `--say "/vault" --mouse 320,530 --hold -:3000 --hold i:100` at `--size 1280x720`
  (read the dummy's place off a first `--shot`); `/position` says where you stand.
- No server is needed for the menu and sign-in screens: `--account http://127.0.0.1:1 --shot`.
- Death replays need no server either: `--account http://127.0.0.1:1 --replay <file>`. How a
  saved one's hits line up with their shots (`app/src/replay/sync.rs`) is printed, hit by hit, by
  `BSK_REPLAY=<file> cargo test -p bsk-client lines_up_a_saved_replay -- --ignored --nocapture`:
  quicker than catching a hit in a `--shot`.
- Screenshots for the website (2026-11-25): `--npc GuildBoard` on the test account shows "Not in
  guild" and an empty board, `--say "/crucible"` stays in the Nexus (the test account hasn't
  reached Arena round 50), and `--npc ChampionsBoard` shows real players' names from the copied
  data, so leave that one out or rename them in the stack's Redis first. A `--size 1600x900` shot
  came out 1280x720 on a 125% display.

Example, the Arena (portal in the shrine's floor north-west of the spawn; monsters come 60 s in):
`--walk-to "62.5,56;62.5,40;52,40;36,40;34.5,31.5" --hold 0:200 --hold -:6000 --hold f:100000 --aim auto --shot a.png --shot-after 95`.

## Layout

| Part | What |
|---|---|
| `assets/manifest.json` | Generated by `tools/gen_rs_manifest.py`; paths relative to `client/src` |
| `crates/assets` | Manifest types; loads from disk, or over HTTP in a browser |
| `crates/render` | wgpu renderer: instanced quads from shelf-packed atlas pages |
| `crates/proto` | All messages and framing, no I/O; golden byte tests from the server's C# layouts |
| `crates/session` | Connection state machine: Hello, Load/Create, acks, one Move per NewTick |
| `crates/net` | Account HTTP + XML, Hello's RSA, the game socket (TCP, or WebSocket in a browser) |
| `crates/data` | Ground and object XML, 3D models |
| `crates/world` | Map, objects, walking, projectiles and hits, the seeded `Random` |
| `app` | The winit app (`betterskillys` binary, also the wasm module) |
| `bot` | Headless client for tests |
| `gateway` | Web server and WebSocket bridge |

- **Assets:** development reads `client/src` directly; packaged builds carry
  `assets/manifest.json` and `assets/`. Atlas pages are 4096² or the GPU's limit (WebGL2 may be
  2048); bigger sheets are cut on tile boundaries (`Sheet::rect`). The surface is non-sRGB and the
  atlas premultiplied, so blending matches Flash.
- **Signing in:** the client opens on its main menu (`Screen::Home`). The account server is
  `--account <url>`, else `BSK_ACCOUNT`, else `http://127.0.0.1:8080`; an address ending in `/api`
  is a gateway, and the game then goes over `wss://<site>/ws`. `BSK_CA=<pem>` trusts a test CA
  (SETUP-LOCAL.md, "Encrypted connections"). "Remember me" keeps the login in
  `%APPDATA%\betterSkillys-rs\login.txt` (`~/.config/...`, or local storage in a browser).
- **Look:** the website's. `theme.rs` holds the shared style: use its `color::` names, not new hex
  values. `sky.rs` draws the screens' night sky and the game's name.
- **Controls:** WASD move, Q/E turn, Z resets the view, mouse fires (I toggles autofire), Space
  uses the ability, 1-8 use an inventory slot, F/V drink a stacked potion, 0 enters a portal or
  opens the NPC beside you, middle mouse pings (held: the ping wheel, `pings.rs`), R goes to the
  Nexus, Enter or / chats, Tab whispers, G and P guild and party chat, O options, J the quest log,
  K the vault window, - and = zoom the minimap, Escape the menu; all rebindable in Options.
  Double-click equips or takes from a bag, shift-click uses, Ctrl+click moves to the other page,
  Ctrl+right-click breaks an item down. F11 or Alt+Enter toggles fullscreen (kept as
  `fullscreen=1`).
- **HUD:** `hud::Layout` works everything out from the window size each frame: the world fills the
  window and the HUD is one bar down the right (minimap, bars, character card at the top; interact
  area, inventory, potions, equipment and the Nexus button at the foot). The pieces (`group`,
  `hud_box`, `grid_slot`, `status_bar`, `draw_card`...) take a rectangle, so they can go anywhere.
- **Swap-out row:** held back. The server keeps slots 20-23 for it (stats 144-147,
  `hud::SWAP_FIRST`, `InventoryConstants`); the client doesn't show them yet.
- **Messages the client asks for:** `FameStats` (93/94) for the Records tab; `QuestsData`
  (103/104) with `QuestProgress` (108), `QuestNotice` (107) and `QuestClaim`/`QuestResult`
  (105/106) for the quest log (`quests.rs`); `VaultContents` (114/115) for the vault window
  (`vault.rs`, which stops asking after 5 s without an answer); `QuestDamageFull` (91/92) beside
  `QuestDamage` (89/90) for boss damage (`Game::boss_history`, the last 3 bosses);
  `GuideSubscribe` (176) with `DungeonGuide` (177) for the Gilded pass's trail to a dungeon's boss
  (`guide.rs`; Options, Gameplay turns it off), and `CommunityBoost` (175) to rally a goal
  (`community.rs`).
- **Async loading:** everything touching files or the GPU at startup is `async` (the browser can't
  block); desktop runs it under `pollster::block_on`.

Not planned: the map editor, the tutorial, Kabam's payment, offer and news screens.
