# Rust client

The game's client, "Do Damage" to players: a rewrite of the old AIR client in Rust that runs on
Windows, Linux, macOS and in the browser. It's what players use; the AIR client is deprecated.
It uses the same art and the same server protocol as the AIR client; browsers (and desktop
clients built for `https://dodmg.com/api`) reach the servers through the WebSocket bridge in
`gateway`. Read [../AGENTS.md](../AGENTS.md) first. [PARITY.md](PARITY.md) lists what the AIR client did and
what's still to port.

## Build and run

```
python tools\gen_rs_manifest.py              # after the AIR client's asset lists change
cd client-rs; cargo run                      # desktop, development layout
python tools\build_client_rs.py              # build\client-rs\native: exe + assets\
python tools\build_client_rs.py --web        # build\client-rs\web: index.html + wasm + assets/
python tools\build_client_rs.py --account https://dodmg.com/api --package --publish   # a release for players (main checkout only)
```

- **Releasing to players** (ask the owner first): raise the version and name the changelog section
  (below) on the release branch, and squash-merge its pull request into `main` (../AGENTS.md, Git,
  "Release branches"). Then, in the **main checkout**, which holds the signing key, run
  `python deploy\deploy.py`: when `client-rs/` or `client/src/` changed since the newest release in
  `dist\client`, it runs the line above first (with `https://<site>/api` from the VPS config), and
  stops if the version wasn't raised or the checkout isn't `main`. That writes
  `dist\Do-Damage-windows.zip` (the website's download) and a signed release in `dist\client`, and
  the deploy puts both on the VPS, where clients find it at `<account>/client/`. Publishing by hand
  needs `--account` (the script refuses without it): a release built without it has
  `http://127.0.0.1:8080` built in, and players who restart after updating to it are stranded, as
  2.1.1 and the first 2.2.0 were. Desktop clients switch at their next portal, or straight
  away on the menu, the character list or an empty sign-in screen; nobody is disconnected. Browser players get
  the new build when they next load the page. Check `https://dodmg.com/api/client/windows/latest.json`
  afterwards.

- **Versions and updates:** the version is `[workspace.package] version` in `Cargo.toml`, or
  `[workspace.metadata] release` there for a four-part one between Cargo's (1.1.3.1; the build
  script bakes it in as `BSK_VERSION`, and refuses one that doesn't extend the version); every
  version needs a section in `CHANGELOG.md` (a test checks this one's). Changes collect under
  `## Unreleased` there (see ../AGENTS.md); on a release, raise the version and rename that
  section to it, as `--publish` refuses while it's left. `--package` and
  `--publish` builds have the update key built in (`build/client-update.key`, made by
  `bsk-publish` on first use); they check `<account>/client/` (`update.rs`), download changed
  files in the background, and hand over to the new version at the next portal (`--resume`, with
  the login, the portal's key, the window and the chat on a pipe). Other builds never update.
  `bsk-update` holds the release format, `publish` the tool that writes it. To try it, publish to
  the test stack: `--account http://127.0.0.1:8082 --publish --publish-to build/iso1/client`.

- **Web:** needs `rustup target add wasm32-unknown-unknown` and `wasm-bindgen-cli` at the
  version in `Cargo.lock` (the script says which). The build writes a `.gz` beside each
  compressible file, which the gateway sends to browsers (`precompressed_gzip`), and fills in the
  page's loading bar totals; the gateway marks `?v=` files cacheable for good and the page
  `no-cache`. The game reports each asset to the page (`window.bskAssetLoaded`) and says when
  it can draw (`window.bskReady`). Serve it with the gateway (below);
  `.claude/launch.json` has `client-rs-web`, a gateway on port 8095 for isolated stack slot 1.
  The `?v=` is the version and commit, so a rebuild before committing is cached under the old
  one: refetch both files with `fetch(url, {cache: "reload"})` and reload the page.
- **Keys in the browser:** winit cancels every key press on the canvas; `web.rs` hands a few
  back to the browser (the clipboard keys, so the page's paste, copy and cut events come, and
  reload, tabs, zoom, the developer tools). Clipboard text goes through those events
  (`textedit.rs` `clipboard`). To test them, dispatch `KeyboardEvent`s (with `code`, Control
  pressed first) and `ClipboardEvent`s on the canvas: the built-in browser's own key presses
  leave `code` empty for some keys, and its pane draws about two frames a second in the
  background, so the sign-in screens, which take keys at the next frame, need a second between
  steps. Its pane can't go fullscreen at all.
- **Window title** is "Do Damage", the game's name for players, never "betterSkillys" (that's
  the AIR client's). Settings and the saved login stay in `betterSkillys-rs` folders, so the
  rename didn't lose anyone's.

## Gateway (browser play)

Browsers can't open TCP sockets, so `bsk-gateway` serves the web build and bridges to the servers
on one port: `/api/<path>` goes to the account server, `/ws/<host>:<port>` is a WebSocket carrying
the world server's TCP stream. Only `--allow`ed world servers are reachable
(`--allow <public>:2051=127.0.0.1:2051` takes the first and connects to the second, as on the VPS).
`--world-state <dir>` (the world proxy's state folder, `build/live`) connects a bridge meant for the
proxy's port straight to the world server slot the player's Hello belongs to, routed as
`tools/world_proxy.py` routes (`gateway/src/world.rs`). It binds 127.0.0.1
unless `--bind` says otherwise; sharing it with players means `--bind 0.0.0.0:<port>` and opening
that port, which is the owner's call.

On the VPS it runs in slots (`bsk-gateway@a`/`b`/`c`) so deploys kick nobody: `/healthz` answers
`ok <bridges>`, and SIGUSR1 makes it hand over (503 there, keeps its bridges, exits when they've
closed or after `--drain-limit` seconds). See SETUP-LOCAL.md, "Deploys that kick nobody".

```
cargo run -p bsk-gateway -- --web build/client-rs/web --account http://127.0.0.1:8080 --allow 127.0.0.1:2051
```

## Testing against the servers

Only ever against an isolated stack, never the live servers. If another session holds the default
stack (ports 6380/8081/2052), start your own slot; in a worktree, copy the data from the main
checkout:

```
python tools\testclient\iso_stack.py --slot 1 start --data-from <main checkout>\source\Redis\data
cargo run -p bsk-bot -- --account http://127.0.0.1:8082 --credentials <main>\test-account.local.txt --seconds 60
cargo run -p bsk-bot -- --account http://127.0.0.1:8095/api --gateway ws://127.0.0.1:8095/ws --credentials ...
python tools\testclient\iso_stack.py --slot 1 stop
```

The bot logs in, joins the nexus with the first living character and answers the server as the
AIR client does (`bsk-session`), following Reconnects (world changes, rolling deploys) through a
new connection; it exits non-zero if it gets disconnected. The second form goes
through the gateway, as the browser does. With no living character it makes a Wizard.
`--say "/godlands;;/god"` sends lines (split by `;;`) 3 s apart after each join, and
`--wander <tiles>` walks it round a circle that size, so several bots keep a realm busy for load
tests (accounts from the account server's `/account/register` on the isolated stack). Every 10 s it
logs the ping the server measured and the gaps between NewTicks as they arrived (100 ms when
nothing holds them up).
`--fight` plays as a player does (`bot/src/fight.rs`, the client's `bsk-world` logic): it circles
the nearest enemy at its weapon's reach, shoots it at the weapon's rate of fire, uses its ability
when it has the mana, keeps off ground that hurts, and sends the hits as the client does.
`--class <name or 0x type>` is the class of a new character, and `--view <tiles>` the sight it asks
for (a large window asks for about 20). It reads the game's XML from `assets/` beside it (only the
manifest's ground, object, particle and model files) or the repository's. God mode is lost with
each world change, so `--say` it after joining: `"/gear;;/arena;;/god"` (one bot `/openarena` first).
`--enter "Wine Cellar Portal;;Oryx's Sanctuary Portal"` takes the first portal it sees with each
object id, once each, so with `/tq` and `/smite` in `--say` it walks down the Oryx chain.

The client itself can be scripted for tests (desktop only):

| Flag | What |
|---|---|
| `--credentials <file>` | Sign in with the file's `email:` and `password:` and play the first character (a new Wizard if there is none) |
| `--stay` | With `--credentials`: stop at the character list instead |
| `--no-update` | Don't look for or install updates (published builds) |
| `--bench` | No vsync or frame cap for this run (the settings are left alone), and every 2 s the frame rate, CPU time by section and draw calls in the log. Measure a release build, after the first few seconds |
| `--shot <png> --shot-after <seconds>` | Save one frame, then quit (logs the position and what it sent) |
| `--walk-to x,y[;x,y...]` | Walk to world positions in turn (in the first world joined, or the one `--say` moves to) |
| `--hold <key>:<ms>` | Hold keys one after another: `w a s d q e r o z 0 1`-`8 space hp mp`, `f` fires, `i` turns autofire on or off (it fires at the mouse while other keys are held), `-` waits |
| `--aim <degrees>` / `--aim auto` | Where `f` fires on the screen, or at the nearest enemy. `auto` is staff's `/autoaim`: the client sends it first and aims only once the server agrees, so the account must be an admin on the test stack (`rank` 6 in its Redis) |
| `--say <text>` | Send a chat line or command once in the game; several lines split by `;;` go out in turn, each once another world is joined or 3 s after the last (`"/dungeon Oryx's Sanctuary;;/god"`) |
| `--npc <class>` | Open that NPC's window once in the game (`GuildBoard`, `PotionStorage`...), wherever you stand; `Vault` opens the vault window once in the vault (with `--say "/vault"`) |
| `--quests <tab>` | Open the quest log at a tab once its data has come: `daily`, `weekly`, `monthly`, `login`, `achievements` (with `/questadmin` in `--say` to set up a state) |
| `--effect <type>` | Show that `ShowEffect` type on you every half second (0: an area attack beside you), to check particles |
| `--type <text>` | Type into the first NPC prompt that opens (then Enter), onto the guild board being edited, or into the Divine Forge's or the vault window's search (with `--npc Forge` or `--npc Vault`) |
| `--size WxH` | Open the window at this size (physical pixels), to check the layout |
| `--mouse x,y` | Put the mouse there (window pixels) and ignore the real one and its buttons: tooltips, aiming |
| `--mouse-script "down@ms:x,y;move@ms:x,y;up@ms:x,y;right@ms:x,y"` | Mouse steps after joining: left-button drags and clicks on the HUD, and right-clicks |

- **Paths and slashes:** from Git Bash, run it with `MSYS_NO_PATHCONV=1` and give
  `--credentials` a Windows path (`C:/Users/...`); a `/c/Users/...` path isn't found and the client
  stays on the sign-in screen.
- **Your login stays yours:** sign-ins from `--credentials` are never saved ("Remember me" is
  skipped for them). But a run without `--credentials` signs in with the login saved on this PC,
  which is the owner's own: its sign-in screen shows their email. Don't share those shots.
- **In the background:** a run with `--shot`, `--hold` or `--mouse-script` opens its window
  without taking focus, so the keyboard stays with the window in use; `--mouse` also ignores
  clicks on it.
- **Chat in the log:** with `RUST_LOG=info,betterskillys=debug` every chat line is logged
  (`chat <name>: <text>`), for reading results such as the vault Training Dummy's.
- **No server needed** for the screens outside the game: `--account http://127.0.0.1:1` with
  `--shot` shows the menu and sign-in screens over the sky, at any `--size`.

For example, the Arena (its monsters come 60 s after you enter; its portal is in the Nexus's south
yard): `--walk-to 45.5,68.5 --hold 0:200 --hold -:6000 --hold f:100000 --aim auto --shot a.png --shot-after 95`.

## Layout

| Part | What |
|---|---|
| `assets/manifest.json` | Generated by `tools/gen_rs_manifest.py`; paths relative to `client/src` |
| `crates/assets` | Manifest types; loads files from disk, or over HTTP in a browser |
| `crates/render` | wgpu renderer: instanced quads from shelf-packed atlas pages, one draw per page run |
| `crates/proto` | All 88 messages and framing, no I/O; golden byte tests from the server's C# layouts |
| `crates/session` | Connection state machine: Hello, Load/Create, the acks and one Move per NewTick |
| `crates/net` | Account HTTP + XML, Hello's RSA, the game socket (TCP, or WebSocket in a browser) |
| `crates/data` | Ground and object XML: textures, projectiles, flags; 3D models |
| `crates/world` | Map, objects, walking (`Player` port), projectiles and hits, the seeded `Random` |
| `app` | The winit app (`betterskillys` binary, also the wasm module); `sprites.rs` looks up tiles |
| `bot` | Headless client for protocol tests |
| `gateway` | Web server and WebSocket bridge for browser play |

- **Assets:** in development the files are read straight from `client/src`. Packaged builds have
  `assets/manifest.json` and the files under `assets/`, beside the exe or the page.
- **Atlas:** pages are 4096², or the GPU's limit (WebGL2 can be 2048). Sheets bigger than a page
  are cut into pieces on tile boundaries (`Sheet::rect`).
- **Colours:** the surface is non-sRGB and the atlas premultiplied, so blending matches Flash.
- **Signing in:** the client opens on its main menu (`Screen::Home`: Play, Sign in, Register,
  Options, Changelog, Quit), checking a remembered login behind it; then the sign-in screen,
  register, character select, new character, delete. The account server is `--account <url>`, else `BSK_ACCOUNT` from the
  environment or the build, else `http://127.0.0.1:8080`. An address ending in `/api` is a
  gateway (`https://<site>/api` through Caddy): the game then goes over its WebSocket bridge
  (`wss://<site>/ws`), encrypted, instead of TCP to the world server. `BSK_CA=<pem>` trusts a test
  CA as well (`tools/testclient/tls_front.py`; see SETUP-LOCAL.md, "Encrypted connections"). "Remember me" keeps the login in
  `%APPDATA%\betterSkillys-rs\login.txt` (`~/.config/...` on Linux and macOS, local storage in a
  browser), as the AIR client keeps it in its shared object.
- **Look:** the website's (`website/`). `theme.rs` holds the shared style (its palette: dark
  violet glass panels, the ember accent; shadows, gradients, buttons, fields, slots edged by item
  tier); the HUD and the screens draw with it, so use `color::` names rather than new hex
  values for interface colours. `sky.rs` draws the screens' night sky (a port of the site's
  hero) and the game's name in a 5×7 pixel font.
- **Controls:** WASD move, Q/E turn, Z resets the view, mouse fires (I toggles autofire), Space
  uses the ability at the mouse, 1-8 equip or use an inventory slot, F/V drink a stacked potion,
  0 enters a portal (or opens the NPC beside you), R goes to the Nexus, Enter or / chats, Tab
  whispers (again to cycle who to), G and P guild and party chat, Page Up/Down the chat history,
  - and = zoom the minimap, O opens the options, J the quest log, K the vault window (in a vault) (all of these can be rebound there), Escape opens the menu (character select,
  log out). Items drag between slots, onto the
  world to drop; double-click equips, uses or takes from a bag; shift-click uses; Ctrl+click moves an
  item to the other page (inventory, backpack); Ctrl+right-click breaks it down.
- **Layout:** `hud::Layout` works everything out from the window size each frame: the world
  fills the window and the HUD is one bar down the right (200 HUD units wide, 780 fitted to the
  height, at most 30% of the width). Its top: the minimap square in the corner, the bars, and
  the character card (portrait, name, then the Stats or Records tab), which takes the space
  down to the bar's foot and spreads its page to fit. Its foot, from the bottom: the interact area (a bag's loot,
  portals, shops, invitations, the players nearby), the inventory, the inventory/backpack tabs
  and the potions, the equipment, the Nexus button. Gold, fame and the frame rate sit beside
  it; the boss (or the boss history, behind its menu button) at the top left. The pieces
  (`group`, `hud_box`, `grid_slot`, `burger_button`, `status_bar`, `draw_bars`, `draw_card`, `draw_stats`,
  `draw_records`) take a rectangle, so they can go anywhere. Chat wraps to the space left of
  the bar. F11 or Alt+Enter toggles borderless fullscreen, as does Options, Graphics
  (desktop); it's kept in the settings (`fullscreen=1`) and the next run opens that way.
- **Swap-out row:** held back for a later release. The server already keeps slots 20-23 for it
  (one for each equipment slot, taking the same items, sent as stats 144-147; `hud::SWAP_FIRST`,
  `stat::SWAP_0`, the server's `InventoryConstants`); the client doesn't show them yet.
- **Fame records:** the client asks for `FameStats` (93/94): the character's `FameStats` (an
  id and a count each), every few seconds while they change, for the card's Records tab.
- **Quests:** the client asks for `QuestsData` (103/104) in every world, keeps it current from
  `QuestProgress` (108) and pops up `QuestNotice`s (107); claims are `QuestClaim` (105), answered by
  `QuestResult` (106). The quest log (`quests.rs`) and the HUD's Quests button and its badge read
  it. The Tinkerer (`QuestMaster`) and the Chronicler (`Chronicler`) open it at their pages.
- **Vault window** (`vault.rs`): in a vault (`vault::is_vault`), K or the HUD's chest button beside
  Quests opens it; it asks for `VaultContents` (114/115) on opening and after each move: every
  vault chest's items, and in our own vault each chest's object id and position. Items move with
  `InvSwap`s to those ids (drags between chests and the HUD's inventory slots, which the window
  takes while open; a click sends one across). With no answer in 5 s it stops asking for that game
  (an older server).
- **Boss damage:** the client asks for `QuestDamageFull` (91/92) as well as `QuestDamage`
  (89/90): everyone who hit the quest with their class, its maximum health, and a last message
  marked `dead` when it dies. The client keeps the last 3 bosses it hit
  (`Game::boss_history`), listed at the top left while no boss shows there.
- **Async loading:** everything that touches files or the GPU at startup is `async`, because the
  browser can't block. On desktop it runs under `pollster::block_on`.

## Plan

0. Scaffolding: manifest, renderer, window on desktop and web. **Done.**
1. Protocol crate (88 messages, golden tests), headless bot on the isolated stack, WebSocket
   bridge for the browser. **Done.**
2. World: tiles, objects, walls, 3D objects, shadows, camera; walk around the Nexus. **Done**
   (connected and cave walls, composite and edge tiles, object animations, AltTexture), except
   particles.
3. Combat: projectiles (exact port of the paths and `Random`), hits, conditions. **Done**
   (shooting, enemy and player hits, ground damage, damage numbers); particles still to do.
4. In-game UI: HUD, minimap, inventory and bags, tooltips, chat and speech balloons, the Escape
   menu. **Done**, except trading, options and player menus.
5. Front-end screens: sign in, register, character select and delete, new character, servers.
   **Done.**
6. Party, trading, guild and the NPC windows (`npc.rs`): **Done.** Market, emotes, sound to do.
7. Packaging: `build_client_rs.py --account <url> --package` (the address built in, release
   builds without a console window on Windows, the website's crown as the window and taskbar icon,
   `tools/draw_client_icon.py`). **Done.**
8. Updates: versions, the changelog window, signed releases on the account server, downloads in
   the background and the switch at the next portal. **Done** (desktop; the browser loads the
   newest build when the page loads).

Not planned: the map editor, the tutorial, Kabam's payment, offer and news screens.
