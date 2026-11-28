# Rust client

The game's client, "Do Damage" to players: a Rust rewrite of the AIR client for Windows, Linux,
macOS and the browser, with the same art and server protocol. Browsers (and desktop clients built
for `https://dodmg.com/api`) reach the servers through the WebSocket bridge in `gateway`. Read
[../AGENTS.md](../AGENTS.md) first; [PARITY.md](PARITY.md) lists what the AIR client did and
what's still to port.

## Build and run

```
python tools\gen_rs_manifest.py              # after the asset lists change
cd client-rs; cargo run -p bsk-client        # desktop, development layout (a bare `cargo run` can't pick a binary)
cargo test -p bsk-client                     # includes the translation checks
python tools\build_client_rs.py              # build\client-rs\native: exe + assets\
python tools\build_client_rs.py --web        # build\client-rs\web: index.html + wasm + assets/
```

The package is `bsk-client`, its binary `betterskillys` (`target/<profile>/betterskillys.exe`). A
fresh worktree's first release build takes about 5 minutes.

- **Releasing to players** (ask the owner first; ../AGENTS.md, Git, "Releasing"): on the release
  branch raise the version and name the changelog section, then squash-merge its pull request. The
  merge deploys it: GitHub's `client` job runs `deploy.py --publish-only`, which, when
  `client-rs/` or `client/src/` changed since the server's newest release, builds with
  `--account https://<site>/api --package --publish` and stops the deploy if the version wasn't
  raised. It writes `dist\Do-Damage-windows.zip` (the website's download) and the release's files
  (only the blobs the server lacks, `bsk-publish --known`), and the `deploy` job puts them on the
  VPS at `<account>/client/`. Never publish without `--account` (the script refuses): a build
  without it has `http://127.0.0.1:8080` built in and strands players. Desktop clients switch at
  their next portal or on the menu; browsers at the next page load.
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

- `iso_stack.py start` makes Tester (account 1) an admin, so `/dungeon`, `/tq`, `/empower` work;
  with no account 1 in the data (a fresh stack) it says so. Start a client only once the world
  server takes players: port 2052 (plus the slot) listens and its log (`build/iso<N>/ws/ws.out`)
  has `NexusGuide.OpenDungeon` (`RealmManager.Restore` or `opened again` don't always show).
  Before that the account server lists no world servers and the client stops at the server list.
- Each `start --data-from` copies the data again: changes made to the test copy are lost.
- No server is needed for the menu and sign-in screens (`--account http://127.0.0.1:1 --shot ...`)
  or a death replay (`--account http://127.0.0.1:1 --replay <file>`).

### The bot

`bsk-bot` logs in, joins the Nexus with the first living character (`--char <id>` picks one, or it
makes a Wizard) and follows Reconnects through new connections; it exits non-zero if
disconnected. The second form above goes through the gateway, as a browser does. Every 10 s it
logs the server's ping and the gaps between NewTicks. Its flags:

- `--say "/godlands;;/god"`: lines (split by `;;`) 3 s apart after each join. God mode is lost
  with each world change, so `--say` it after joining (`"/gear;;/arena;;/god"`).
- `--wander <tiles>`: walk a circle, for load tests (accounts from `/account/register`).
- `--fight`: play like a player (`bot/src/fight.rs`): circle the nearest enemy at weapon reach,
  shoot at the weapon's rate, use the ability, avoid hurting ground. `--class <name or 0x type>`
  for a new character, `--view <tiles>` for its sight. It reads the game's XML from `assets/`
  beside it, or the repository's.
- `--enter "Wine Cellar Portal;;Oryx's Sanctuary Portal"`: take the first portal with each id,
  once each (with `/tq` and `/smite` it walks the Oryx chain).
- `--vault "put <inv> <chest>;take <chest> <inv>;upgrade;buychest [gold];goto x y;wait <s>"`: vault
  moves in turn.
- `--arena-vote "5;0"`: vote at each Arena break (1-5 a combine pick, 0 Next) and log `ArenaVotes`.
- `--visit-answer yes|no|wait`: answer `/visit` requests, logging each.
- `--ping "1;3"`: ping those kinds (0 look, 1 danger, 2 go, 3 loot, 4 help) and log every
  `PingShow`; `--ping ""` only listens.
- `--shots`: log every enemy volley seen (monster, projectile id, count, spread, angle, damage):
  `/spawn` a monster in a `/vault` to check its behaviour.

### The client's scripted flags (desktop only)

| Flag | What |
|---|---|
| `--credentials <file>` | Sign in with the file's `email:` and `password:` and play the first character in the list (a new Wizard if none); never saved |
| `--stay` | With `--credentials`: stop at the character list |
| `--screen <name>` | With `--credentials --stay`: show `home`, `account`, `discord`, `discord-code`, `sprites`, `sprites-mine`, `sprites-review` (needs an admin), `sprites-item:<item id>`, `class:0x<type>` (a class's page, every skin and price), `replay:<account>:<char>` (that death's summary, then its replay), `replay:s:<n>` (death saved number n's replay) or `legends:<tab>` (`week`, `month`, `all`, `recent`, `saved`) |
| `--replay <file>` | Play a saved death replay (`.bskreplay`) over the first screen, signed in or not |
| `--no-update` | Don't look for or install updates |
| `--bench` | No vsync or frame cap, and every 2 s the frame rate, CPU time by section and draw calls in the log (measure a release build) |
| `--shot <png> --shot-after <seconds>` | Save one frame, then quit |
| `--size WxH` | Open the window at this size (physical pixels) |
| `--look off\|glow\|vivid\|all` | The glow and colours for this run, whatever the options say |
| `--lang <code>` | Use that language for this run |
| `--walk-to x,y[;x,y...]` | Walk to world positions in turn |
| `--hold <key>:<ms>` | Hold keys in turn, from the first world joined: `w a s d q e r o z 0 1`-`8 space hp mp`, `f` fires toward `--aim`, `i` toggles autofire, `-` waits |
| `--aim <degrees>` / `--aim auto` | Where `f` fires, or at the nearest enemy (turns on `/autoaim`, which needs an admin) |
| `--mouse x,y` | Put the mouse there and ignore the real one: tooltips, aiming |
| `--mouse-script "down@ms:x,y;move@ms:x,y;up@ms:x,y;right@ms:x,y"` | Mouse steps after joining (also `middle`, `mdown`, `mup` for pings), timed from the first world joined |
| `--say <text>` | Chat lines or commands split by `;;`, each once another world is joined or 3 s after the last |
| `--npc <class>` | Open that NPC's window once in the game (`GuildBoard`, `PotionStorage`, `Vault` in a vault, `ScryingGlass`, `CommunityGoals`...) |
| `--champions <board>` | Open the Champions board at that board |
| `--chat <channel>[:text]` | Open the chat box on `all`, `local`, `guild` or `party`, with the text typed in |
| `--quests <tab>` | Open the quest log at `daily`, `weekly`, `monthly`, `login`, `achievements` or `customize` |
| `--pass <tab>` | Open the Realm Pass at `rewards`, `earn`, `looks` or `pets` |
| `--options <tab>` | Open Options at `controls`, `gameplay`, `graphics`, `visuals`, `crosshair`, `sound` or `language` |
| `--effect <type>` | Show that `ShowEffect` type on you every half second (40: a boss's arrival mark, every 1.5 s; 101 and up: the Necromantic, Ascendant and Father looks, `relic::look`, 133 to 138 the ward's; 140 to 148: the halos' forcefields, tier 0 to 8; 2xx: look xx, for the looks from 39 on; 243 plays the Vial of Cascading Dusk's whole cascade) |
| `--type <text>` | Type into the first NPC prompt, the guild board, the forge's or vault's search, the Marketplace's price or the Scrying Glass's chat |
| `--display-of <text>` | Open on the display that window is on (Windows only; by default Visual Studio Code's) |

Runs with `--shot`, `--hold` or `--mouse-script` open without taking focus. A run without
`--credentials` signs in with the owner's saved login (their email on screen): don't share those
shots. `RUST_LOG=info,betterskillys=debug` logs every chat line (`chat <name>: <text>`), and the
log's `sent: {...}` lines show each message sent.

Example, the Arena (portal in the shrine's floor north-west of the spawn; monsters come 60 s in):
`--walk-to "62.5,56;62.5,40;52,40;36,40;34.5,31.5" --hold 0:200 --hold -:6000 --hold f:100000 --aim auto --shot a.png --shot-after 95`.

### Pitfalls

- **Git Bash:** use `MSYS_NO_PATHCONV=1` and `C:/Users/...` paths for `--credentials` and
  `--shot`: a `/c/Users/...` path stays on the sign-in screen or saves nothing.
- **Window size changes from run to run** (1280x800 without `--size`; `--size 1280x720` gave
  2560x1369 once and 1024x576 on a scaled display): take a `--shot` first and read `--mouse` and
  `--mouse-script` positions off that run's size, or the aim lands off the floor. An ability aimed
  at the HUD bar or off the floor is refused without a word.
- **Which character plays:** check the class in the first shot's character card before changing
  one for a test (on the copied data `--credentials` played Tester's character 2, not 1); or
  change both, or take the others out of `alive.1` (The test copy's data, below).
- **Abilities:** `--hold space:<ms>` doesn't always send it (3 of 7 runs with the same flags; never
  for a Mechanic's blueprint in the vault): check the log for `sent:` naming `UseItem`, and MP in
  the shot, before reading anything into it; one run that sends it is enough. Test an ability's
  logic with a server unit test rather than spending runs on it.
- **Looking at monsters:** Tester's character on the copied data is a Mechanic, and its turrets
  and pet kill what you `/spawn` in a vault within seconds (a Court boss inside 20 s), and the
  monsters crowd onto you. For a sprite's look, render the sheet with an outline instead (as
  `tools/ice_tomb/pixels.py`'s were checked); for a shot in game, take it a few seconds after the
  last `/spawn`.
- **Your own shots:** the Nexus lets nobody shoot. Without `--aim`, `--hold f` sent no
  `PlayerShoot`; `--hold i:100` (autofire) with `--mouse` does. The `--hold` steps start at the
  first world joined, after `--shot-after`'s clock has started, so keep a leading `--hold -:<ms>`
  short (3000). Your Lil' Oryx pet fights the vault's dummy too (its grey blades fly from where
  you stand, its hits count in `EnemyHit`), so neither shows that you fired. To hit the Training
  Dummy with a sword: `--size 1280x720 --say "/vault" --mouse 320,530 --hold -:3000 --hold i:100`
  (read its place off a first `--shot`; `/position` says where you stand).
- **`/autoaim` toggles:** with `--aim auto` already on, saying it turns it off.
- **Teleports:** `/tq <id>` lands beside the object; teleporting onto ground no one can walk on is
  refused by the client without a word. A random-layout dungeon's rooms differ each run, so reach
  one with `/tq <an object in it>` (`Lake Heart`, `Chasm Heart`, a keeper) and expect "There is
  no ..." when that run didn't pick the room.
- **What hides the player:** a monster `/spawn`ed in a vault stays across runs on the spot you
  arrive at (spawn it a step away, or test in the Nexus), and in the Nexus a pet can stand on you.
- **`--effect` repeats** every half second, so two or three overlap in a shot: judge how heavy an
  effect looks from a real cast (`--mouse` on the target, `--hold space:150`).
- **Website screenshots:** on the test account `--npc GuildBoard` shows "Not in guild" and an
  empty board, `/crucible` stays in the Nexus (Arena round 50 not reached), and
  `--npc ChampionsBoard` shows real players' names from the copied data: leave it out or rename
  them in the stack's Redis first.
- **Replay sync:** how a saved replay's hits line up with their shots (`app/src/replay/sync.rs`) is
  printed hit by hit by
  `BSK_REPLAY=<file> cargo test -p bsk-client lines_up_a_saved_replay -- --ignored --nocapture`,
  quicker than catching a hit in a `--shot`.
- **One client an account:** two clients signed in to the same account at once drop each other
  ("The connection dropped: you're back in the Nexus"), resetting the glass, open windows and
  `--mouse-script` steps under them. Run scripted clients one after another, never side by side.
- **A death saved:** put an amulet that resurrects in the ring slot (slot 3 of `items`, e.g. the
  Amulet of Mother Nature, 0xD0ED, below), then
  `--say "/vault;;/position;;/position;;/position;;/kill Tester Oryx"`. `/kill <name> <killer>`
  calls the death directly (no hit, so the save shows no blow); the `/position` lines keep you
  in the vault about 9 s first, as a `/kill` straight after `/vault` runs the moment the vault
  is joined and leaves an empty replay. The amulet is used up: put it back before each run.
  `/fame/saved` and `/fame/feed` show the save at once.

### The test copy's data

Binary values go in through `redis-cli -x` (the last argument from stdin), written whole from
Python or `printf`: a quoted `"\x01..."` argument or a typed line doesn't match. Never read one
with `--raw` and write it back: on Windows that turns each 0x0A byte into CR LF and shifts every
slot after it (check with `--no-raw`). The old Redis takes one field per `hset`.

- **Play another character:** take the others out of the living set and put them back after:
  `printf '\x01\x00\x00\x00' | redis-cli -p <port> -x srem alive.1` (`sadd` to put it back).
- **Change class:** `hset char.1.<id> charType <type in decimal>`, then give it that class's gear
  (`/gear` gives nothing at level 20, and the old weapon and ability are dropped). `items` is 28
  little-endian ushorts (0xFFFF empty):
  `python -c "import sys,struct; sys.stdout.buffer.write(b''.join(struct.pack('<H', i & 0xffff) for i in [0x5810, 0x581b] + [-1] * 26))" | redis-cli -p <port> -x hset char.1.<id> items`.
- **Wear a skin:** the server resets `skin` to 0 at sign-in unless the account owns it, so add it
  to the account's `skins` first (2-byte little-endian types):
  `printf '\x14\xe2' | redis-cli -p <port> -x hset account.1 skins` (0xE214), then
  `hset char.1.<id> skin 57876`.

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
| `publish` | `bsk-publish`, the release tool |

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
