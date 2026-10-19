# Running betterSkillys on this PC

The live game, Do Damage, runs on a Linux VPS at dodmg.com ("Running on a VPS" below). This part is for a copy on a Windows PC, which runs alongside Doomed Realms (which uses ports 8888, 2050 and 843).

| Part | Port | Started by |
|---|---|---|
| Redis (database, `source/Redis/redis.local.conf`, data in `source/Redis/data/`) | 6379, this PC only | `Run Server.bat` |
| Account server (`source/App`) | 8080 | `Run Server.bat` |
| World proxy (`tools/world_proxy.py`) | 2051 | `Run Server.bat` |
| World server (`source/WorldServer`, settings in `wServer.json`), run from `build/live/a` or `b` | 12051 / 22051, this PC only | `Run Server.bat`, `tools/rolling_restart.py` |

## Play

1. `Run Server.bat`. It rebuilds the servers (needs the .NET 8 SDK) unless one is already running, then opens Redis, Account Server, World Proxy and World Server windows. Run again while they're up, it updates the world server without kicking anyone (see below); the account server keeps running its old build. Close those windows to stop the servers (or `python tools\rolling_restart.py stop` for the proxy and world servers).
2. `Play Client.bat`. This runs the client with the AIR runtime from `tools/airsdk`.

## Client

The client is an Adobe AIR app built from `client/src` with the AIR SDK 32 compiler. Unzip `AIRSDK_Compiler.zip` from airdownload.adobe.com into `tools/airsdk/`.

```
python tools\build_client.py                            # client\bin\client.swf for this PC
python tools\build_client.py 188.141.12.3 --package     # dist\betterSkillys-Client.zip for other players
```

For testing changes without touching the live servers or your own client, `tools/testclient/` has isolated test servers (ports 6380, 8081, 2052) and a separate test client; see `client/AGENTS.md`.

The account server address is compiled into `LocalhostSetup.as`, so the script copies the sources to `build/src` and sets it there. The package is a self-contained Windows app (AIR captive runtime) signed with a self-signed certificate in `build/`.

The Rust rewrite of the client (`client-rs/`, see its AGENTS.md) does everything the AIR client does and also runs in a browser. Its package is built the same way:

```
python tools\build_client_rs.py --account http://188.141.12.3:8080 --package   # dist\Do-Damage-windows.zip
python tools\build_client_rs.py --account http://188.141.12.3:8080 --publish   # dist\client: players' clients update to it
```

Players only need the zip once. Its client keeps itself up to date from `dist\client`, which the account server serves at `/client/`: it looks every five minutes, downloads what changed in the background, and switches to the new version at the player's next portal (or straight away on the character list). `Run Server.bat` publishes on every start; to publish without restarting anything, run the `--publish` line above.

- **Versions:** the version is in `client-rs/Cargo.toml` (`[workspace.package]`). Every change goes under `## Unreleased` at the top of `client-rs/CHANGELOG.md` as it lands, in plain words for players. For each release, raise the version and rename that section to it: publishing refuses a version with no section or with an `Unreleased` one left, and the game's Changelog window shows the file.
- **Signing:** releases are signed with `build/client-update.key`, made on the first publish. Keep it (and back it up): clients only take releases signed with the key they were built with, so a new key means sending everyone a new zip.
- **Rolling back:** `cargo run -p bsk-publish -- rollback --to dist/client --version <x.y.z>` (in `client-rs`) makes an earlier published version the latest again; clients switch to it like any update.

## Updating the world server without kicking anyone

The world proxy owns port 2051 and forwards each player to one of two world servers on this PC,
slot a (port 12051) or slot b (22051), each run from its own build in `build/live/<slot>`.
To put a new world server build live, run `Run Server.bat` again: with the servers running it builds the
current source and switches to it (`rolling_restart.py update`), publishing the client first. By hand:

```
python tools\rolling_restart.py update          # start them if they aren't running, else deploy (what Run Server.bat runs)
python tools\rolling_restart.py deploy          # build source/WorldServer into the free slot and switch to it
python tools\rolling_restart.py status          # which slot is active, and how many players the old one still has
```

- **What players see:** nothing at first. The new server takes the Nexus and every new login at
  once; players in the old server's Nexus move there straight away (a short loading screen).
  Everyone else keeps their world, dungeon runs included, gets a chat message that an update is
  waiting, and switches at their next trip to the Nexus or a realm. After 60 minutes
  (`--minutes`) whoever is left is moved to the new Nexus, with warnings 5 and 1 minutes before.
  The old server then stops by itself.
- **The 2-hourly restart** (`restartTime` in `wServer.json`) works the same way: the server asks
  the proxy, which runs `rolling_restart.py deploy --reuse` (the same build, copied to the other
  slot). Nobody is kicked any more. A request that comes while another deploy runs waits for it
  (`proxy.log` says so); one from a server that's already being replaced is dropped; a server
  that wasn't replaced asks again every 30 minutes; a scheduled restart still running after 2
  hours is stopped.
- **A world server that doesn't come up** (not listening within 10 minutes, or the tool stopped
  while it waits) is stopped again: it has no players yet. It's in `state.json` as `starting`
  from the moment it starts, so if the tool itself dies (killed, out of memory), the next deploy
  finds it and stops it rather than failing on "port ... is already taken". The proxy sends
  nothing to a starting server. (Until 2026-10-17 a failed start left the new server running
  untracked on its slot.)
- **A second deploy while the old server is still handing over** waits for it to empty;
  `deploy --now` moves its players to the new Nexus first.
- **Only the world server is deployed this way.** The account server can be restarted without
  kicking anyone in game. A message change still needs the client and server shipped together.
- **Logs:** `build/live/<slot>/logs/wServer/`, and the proxy's in `build/live/proxy.log`.
- **Switching over the first time:** the world server started by the old `Run Server.bat` holds
  port 2051 itself. Stop it (this kicks everyone, so ask first), then run `Run Server.bat` again
  (or `python tools\rolling_restart.py up`). Restart the account server too, so it lists the two
  world servers of a hand-over as one.
- **Try it on a test stack:** `python tools\testclient\iso_stack.py --slot N start --rolling`,
  then `python tools\rolling_restart.py --iso N deploy`.

## Letting other people play

- **World server address:** `source/WorldServer/wServer.json` sets `serverInfo.address` to the public IP (188.141.12.3). The account server sends 127.0.0.1 instead to clients on this PC.
- **Firewall:** right-click `Allow Remote Players.bat` and choose **Run as administrator**, once. It opens TCP 8080 and 2051.
- **Router:** forward TCP 8080 and 2051 to this PC.
- **If the public IP changes:** update `wServer.json` and rebuild the package.
- **Play on this PC with `Play Client.bat`**, not the friends' package. The package connects to the public IP, so all of your game traffic goes out through the router and back in (NAT loopback), which loads the router and makes the game lag for players connecting from outside. The account server now sends this PC's LAN address to clients that arrive that way, but the local client avoids the router altogether.

## Running on a VPS

The same servers run on a Linux VPS (Ubuntu 24.04, x86_64; 4 GB of RAM is enough, 8 GB is
comfortable), so this PC doesn't have to stay on. Everything is built on this PC by
`deploy/deploy.py` and copied over ssh; the VPS only runs it. Players open `https://<site>`, the
website, and play from there in the browser (`https://<site>/play/`), or download the desktop
client. A desktop client built with `--account https://<site>/api` goes the browser's way too:
sign-in over https and the game over wss, through Caddy and the gateway, so passwords and game
traffic are encrypted (see "Encrypted connections" below). Older desktop clients use
`http://<public_address>:8080` and TCP 2051, unencrypted; those stay open for them.

| Service (systemd) | What | Port |
|---|---|---|
| `bsk-redis` | Ubuntu's Redis 7, `deploy/remote/redis.conf`, data in `/opt/bsk/redis/data` | 6379, VPS only |
| `bsk-account` | Account server, `/opt/bsk/app` | 8080 |
| `bsk-world` | World proxy and world servers (`rolling_restart.py serve`), `/opt/bsk/build/live` | 2051 (slots 12051 / 22051, VPS only) |
| `bsk-gateway@a`, `@b`, `@c` | Browser client and its WebSocket bridge, `/opt/bsk/gateway/<slot>`: one slot takes new players, another may be handing over (see "Deploys that kick nobody") | 8095, 8096, 8097, VPS only |
| `caddy` | https for the site (its own Let's Encrypt certificate): the website from `/opt/bsk/site`, and `/play/`, `/api/` and `/ws/` to the gateway slot whose `/healthz` says ok. Caddy 2.7 or later, from Caddy's own apt repository | 80, 443 |

**The website** (`website/`, built by `python tools\build_site.py` into `build\site`):
- **What it shows:** the home page with the server's live player count (from `/api/app/serverList`),
  the downloads, the controls and the two newest versions from `client-rs/CHANGELOG.md`, plus a
  full changelog page. The game's name on it is `site_name` in the VPS config.
- **Wiki** (`/wiki/`, `tools/build_wiki.py`, run by `build_site.py`): guides for players in
  `website/wiki/*.md`, with sections generated from the game's data by `tools/wiki_data.py` (the
  dungeon list from `Dungeons.xml`, the portals, keys and behaviours' drops; the classes and what
  `/gear` gives each; the player commands; the Oryx chain and incantations; Bazaar prices; pets;
  realm events) and numbers from the code (`{{v:...}}`: account limits, `/gear` limits, the
  Support Bot's timers). Rebuild the site after a content change and the wiki follows. A new
  player command needs its words in `COMMAND_HELP` (the build names any that lack them); a boss a
  script spawns, which its dungeon's map doesn't place, goes in `BOSS_OVERRIDE`.
- **Screenshots:** the gallery's pictures are in `website/screenshots/` (`<name>.webp` at 1600x900,
  `<name>-small.webp` at 640x360). Take new ones with the Rust client's `--shot` on an isolated
  stack, with `--credentials` and a test character renamed there, so no real login or player shows.
- **Deploying:** `deploy.py` builds it into the bundle on every deploy.
- **Downloads:** the site offers the zips in `dist\` (`Do-Damage-<os>.zip`). Build them for
  the VPS first: `python tools\build_client_rs.py --account https://<site>/api --package`.
- **Trying it on this PC:** `.claude/launch.json` has `site`, which serves `build\site` on port
  8097. The server status reads "unavailable" there, as nothing answers `/api`.

**First time:**

1. A VPS with Ubuntu 24.04, and a domain (or subdomain) whose A record points at its IP. Open TCP
   22, 80, 443, 8080 and 2051 if the provider has a firewall. Set up ssh with a key, so
   `ssh ubuntu@<vps>` works without a password; that user needs `sudo` without one (OVH's does).
2. On this PC, the build tools: the .NET 8 SDK, Rust with `wasm32-unknown-unknown` and
   `wasm-bindgen-cli` (as for the web client), and WSL with Ubuntu 24.04 and Rust inside it, which
   builds the Linux gateway (`curl https://sh.rustup.rs -sSf | sh` and `sudo apt install
   build-essential` in WSL).
3. Copy `deploy/vps.example.json` to `deploy/vps.local.json` (not tracked) and fill in `ssh`,
   `site` (the domain) and `public_address` (the same domain, or the IP).
4. Move the data. Stop the servers on this PC (that kicks everyone: tell them first), so nothing
   changes after the copy, then:

   ```
   python deploy\deploy.py --import-data source\Redis\data
   ```

   It builds everything, uploads it, installs Redis, the .NET runtime, Caddy and the services,
   copies `dump.rdb` and `appendonly.aof` in, and starts it all. Redis 7 reads the Redis 3.2
   files as they are: on its first start it moves `appendonly.aof` into `appendonlydir/` as the
   base of its new AOF (tried with a copy of the live data: all 139 keys loaded). It refuses to overwrite data already on the VPS; `--force-data`
   replaces it (the old folder is kept as `data.before-import.<time>`).
5. Point players at the VPS: publish the desktop client for the new address
   (`python tools\build_client_rs.py --account https://<site>/api --publish`, then run
   `deploy\deploy.py` again so the VPS serves it at `/client/`) and send the `--package` zip to
   anyone who needs it. Clients built for the old address keep updating from this PC until it
   stops, so publish the new-address build from this PC once as well.

**The live game moved here on 2026-10-16:** `dodmg.com` (Contabo, 169.58.244.146, ssh as root
with `~/.ssh/dodmg_ed25519`; the config is `deploy/vps.local.json`, not tracked). The servers on
this PC are stopped and `Run Server.bat` asks before starting them. Desktop clients from before
the move still look for updates at this PC's address: `python tools\update_relay.py` serves them
the releases (and nothing else) on port 8080, so they switch to `dodmg.com` by themselves. Leave
it running for a while; it doesn't survive a restart of this PC.

**Encrypted connections (desktop client):**
- **How:** a desktop client whose account address ends in `/api` treats it as a site's gateway:
  account requests go to `https://<site>/api/...` and the game to `wss://<site>/ws/<host>:<port>`,
  the WebSocket bridge the browser client uses (`client-rs/crates/net`: `tls.rs`, `socket.rs`).
  Caddy holds the certificate; nothing new listens on the VPS. Any other address (`http://...:8080`)
  is the account server itself, with the game over plain TCP, as before.
- **Switching players over:** publish with `--account https://<site>/api` (step 5 above) and run
  `deploy\deploy.py`. Older clients still update from `http://<public_address>:8080/client/`
  (releases are signed, so that's safe over http), and the new version has the https address
  built in, so they switch by themselves. Once nobody plays an old version, ports 8080 and 2051
  can be closed in the VPS firewall; until then they carry old clients' passwords in the clear.
- **Players' addresses:** through the gateway, the world servers would see every player as
  127.0.0.1, so IP bans and mutes would hit everyone at once. With `"forward_address": true` in
  the deploy config, the gateway sends the player's address (from Caddy's `X-Forwarded-For`) as a
  PROXY_INFO message, which `world_proxy.py` passes on in place of its own. Turn it on only after
  `bsk-world` has restarted with the world proxy from 2026-10-16 (that restart kicks everyone, so
  pick a quiet moment and tell players): the old proxy, still running until then, would route
  such connections to the active world server whatever their Hello asks for. Until it's on,
  players on https show as 127.0.0.1 in game, as browser players already do.
- **Trying it on this PC:** start a test stack, a gateway in front of it and
  `tools/testclient/tls_front.py` (a stand-in for Caddy with a test certificate), then point the
  client at it with `BSK_CA` naming the test CA (only for that run):

  ```
  python tools\testclient\iso_stack.py --slot 3 start --rolling --data-from <main>\source\Redis\data
  client-rs\target\debug\bsk-gateway --web build\iso3\web --account http://127.0.0.1:8084 --allow 127.0.0.1:2055 --bind 127.0.0.1:8198 --forward-address
  python tools\testclient\tls_front.py --listen 8443 --to 8198 --dir build\iso3\tls --forwarded-for 203.0.113.7
  set BSK_CA=build\iso3\tls\ca.pem
  client-rs\target\debug\betterskillys --account https://127.0.0.1:8443/api --credentials test-account.local.txt
  ```

  Without `BSK_CA` the client refuses the test certificate ("UnknownIssuer"), as it should.
- **The RSA key** that encrypts the password in the Hello (`source/WorldServer/networking/RSA.cs`)
  is the well-known upstream pair, so it hides nothing from anyone who looks it up. Over wss it
  doesn't matter (TLS covers the Hello); it was left as it is so old clients keep working.

**Updating:** `python deploy\deploy.py`. It replaces only what changed, and nobody is disconnected
(see below). `--skip-build` uploads the last build again, `--no-releases` leaves `dist\client`
out, and `--status` shows the services, the world server slots and the gateway slots (each with
its open connections).

**Deploys that kick nobody** (`deploy/remote/install.sh`):
- **World server:** a rolling deploy, as on this PC: players switch to the new one at their next
  trip to the Nexus or a realm, an hour at most.
- **Gateway** (which nearly every player goes through: the browser, and desktop clients on
  https): a new build or a new web client starts in a free slot (`bsk-gateway@a`/`b`/`c`, ports
  8095-8097, each with its own copy in `/opt/bsk/gateway/<slot>`), and once it answers, the old
  slot gets SIGUSR1 and hands over. Caddy lists all three slots and checks each one's `/healthz`
  every second (`lb_policy first`, retrying another slot while none answers), so new connections
  go to the new slot without a reload. The old slot keeps its players' connections until they
  close: every world change opens a new one, which goes to the new slot. It exits by itself once
  it has none, or after 3 hours (`--drain-limit`), closing what's left. If both other slots are
  still handing over, the one that started first is stopped. `/opt/bsk/gateway/active` names the
  slot taking players; only it is enabled at boot. `curl 127.0.0.1:8095/healthz` (8096, 8097)
  says `ok <connections>` or `draining <connections>`.
- **Account server:** it restarts when its files or settings changed. Nobody in game notices;
  sign-ins and account requests pause for the few seconds it takes to start, and the gateway holds
  https ones (up to 20 s) until it answers. Older clients on port 8080 get an error meanwhile.
- **Caddy** reloads only when the Caddyfile changed, and keeps open WebSockets through it
  (`stream_close_delay 3h`). Ubuntu's Caddy 2.6.2 can't (it closes every WebSocket on a reload),
  so `install.sh` replaces it with Caddy's own package (dl.cloudsmith.io/public/caddy/stable),
  once: that restarts Caddy, which disconnects https players that one time.
- **Units and settings:** a changed `bsk-gateway@` unit or `bsk.env` means a new gateway slot; a
  changed `bsk-account` unit restarts the account server; a changed `bsk-world` or `bsk-redis`
  unit waits for their next restart, which is never done by a deploy (restarting `bsk-world`
  kicks everyone).
- **Package upgrades** (unattended-upgrades) never restart the game's services or Caddy:
  `install.sh` tells needrestart so in `/etc/needrestart/conf.d/bsk.conf`. They pick up new
  libraries at their next deploy or restart.
- **The first deploy with this layout** (from the single `bsk-gateway` service of before
  2026-10-17) disconnects https players once: Caddy is replaced and its new config loaded, and the
  old gateway can't hand over. Pick a quiet moment for it. Deploys after it kick nobody.
- **If a connection drops anyway,** the Rust client (from the release after 1.2.3) takes the player back to the
  Nexus by itself, trying for about two minutes; the bot follows Reconnects too.

**On the VPS:**

```
sudo systemctl status bsk-world                       # also bsk-redis, bsk-account, 'bsk-gateway@*', caddy
sudo journalctl -u bsk-world -f                       # the proxy and the world servers' console
sudo -u bsk python3 /opt/bsk/tools/rolling_restart.py status
sudo -u bsk python3 /opt/bsk/tools/rolling_restart.py request deploy --reuse   # a rolling restart
sudo systemctl restart bsk-world                      # kicks everyone (they're saved first)
redis-cli -p 6379 hget names MEAPY                    # Redis, as on this PC
```

- **Logs:** `/opt/bsk/build/live/<slot>/logs/wServer/`, `/opt/bsk/build/live/proxy.log` and
  `deploy.log`, `/opt/bsk/app/logs/`, and the journal.
- **Stopping:** `systemctl stop bsk-world` sends the world servers SIGTERM; they save everyone and
  stop, as Ctrl+C does on Windows (`SignalListenerLinux`).
- **Deploys inside the service:** `bsk-world` runs `rolling_restart.py serve`, which starts a
  world server and then becomes the world proxy, so the world servers belong to the service. A
  deploy starts its new world server through the proxy (`rolling_restart.py request update`,
  `deploy.request` in `/opt/bsk/build/live`); the 2-hourly restart already went that way.
- **Ports and address** come from `/opt/bsk/bsk.env`, which `deploy.py` writes from the config.
  `rolling_restart.py` takes them from `BSK_WORLD_PORT`, `BSK_REDIS_PORT` and
  `BSK_PUBLIC_ADDRESS`. Changing the Redis port needs `systemctl restart bsk-redis` by hand.
- **The gateway** takes `--allow <public>:2051=127.0.0.1:2051`: the browser asks for the address
  in the server list and the gateway connects to the world proxy on the VPS itself.
- **Backups:** copy `/opt/bsk/redis/data` (and keep `build/client-update.key` on this PC).
- **Memory:** a world server uses about 800 MB, two run side by side during a hand-over, and the
  account server about 250 MB. `install.sh` adds a 2 GB swap file when the VPS has no swap.
- **Trying it on this PC:** `deploy/deploy.py` can install into a WSL distro instead of a VPS
  (`"target": "wsl"`). The test used Ubuntu-24.04 with ports kept clear of the live servers:
  `{"target": "wsl", "wsl": "Ubuntu-24.04", "site": "http://localhost:8444", "public_address":
  "127.0.0.1", "ports": {"redis": 6399, "account": 8099, "world": 2099, "gateway": 8191},
  "forward_address": true}` in `deploy/wsl-test.local.json`, then
  `python deploy\deploy.py --config deploy\wsl-test.local.json`. Keep `wsl -d Ubuntu-24.04 -- sleep
  14400` running meanwhile, or the distro shuts down. To check that deploys kick nobody, play the
  bot through it (`bsk-bot --account http://localhost:8444/api --gateway ws://localhost:8444/ws
  --credentials <main>\test-account.local.txt --seconds 600`) while deploying changed builds
  (2026-10-17: a Caddyfile change, a gateway, an account server and a world server in turn; the
  bot stayed in, and its connection after the world deploy went to the new gateway).

## Admin

Rank is the `rank` field of the account in Redis:

| Rank | Role |
|---|---|
| 0 | Regular |
| 1 | Donator |
| 2 | Supporter |
| 3 | Sponsor |
| 4 | Sandbox |
| 5 | Moderator |
| 6 | Admin |

```
source\Redis\redis-cli.exe hget names MEAPY      # account id for a name (upper case)
source\Redis\redis-cli.exe hset account.2 rank 6
```

The new rank applies from the player's next login. The commands are in `source/WorldServer/core/commands/`.

Full admin powers come from a separate `admin` flag. It passes every command check, and lets the player fire items from any inventory slot (for the admin class weapons) and visit other players' vaults. It is stored as a single byte:

```
echo HSET account.2 admin "\x01" | source\Redis\redis-cli.exe
```

## Code history

The code is [Meapy/Do-Damage](https://github.com/Meapy/Do-Damage), a standalone repository. It began as a fork of HaloCelsius/betterSkillys (itself from runes02/betterSkillys and Slendergo's TK-Reborn), merged with [Aurusenth/betterSkillys](https://github.com/Aurusenth/betterSkillys), which adds item tiers and glow, an admin class, the Admin Arena and Shaitan dungeons, Battle for the Nexus, a new nexus and vault, and more. In `PlayerShootHandler.cs`, the merge keeps HaloCelsius's rate-of-fire and projectile-count checks for players who aren't admins.

On 2026-10-18 the history was condensed from 1,416 commits to one per milestone, each holding the exact files of the original commit that ended it:

- upstream: TK-Reborn's months in 2022, its 2023 cleanup, betterSkillys in 2024, and HaloCelsius's changes to March 2025, with the people who worked on each as co-authors;
- this setup and the Aurusenth merge, the playable dungeons, the Nexus Bazaar;
- then one commit per Rust client release, from 1.1.0 on.

Each commit's message lists the original changes and says which range of the full history it replaces. The full history is the local branch `full-history` (not pushed) and the old fork, Meapy/betterSkillys.

The remotes `upstream` (HaloCelsius) and `aurusenth` share no history with `main` any more: bring in a later change of theirs by hand (`git cherry-pick` or a copy), not with `git merge`.

## Changes from upstream

- **Trading with the backpack** (`Player.Trade.cs`, `ChangeTradeHandler`, `AcceptTradeMessageHandler`; Rust client `hud.rs`): `TradeStart` lists 20 slots for a player with a backpack (12 without, `Player.TradeSlots`), and a trade puts what you get into your backpack too once the inventory is full. Offers from the client are cut to those slots, with equipment, empty slots and soulbound items left out (`Player.CleanTradeOffer`), in `ChangeTrade` and now `AcceptTrade` too (it took the client's offer as it came). Also fixed: `AcceptTrade` read the second offer with the first one's length, a missing trade target threw, and the partner's item data went through a transaction on the wrong inventory (the player's), so traded items' data could be lost or overwrite the player's; an item given away now takes its data with it. The Rust client shows the backpack as two more rows; older Rust clients show the first 12 and still trade. Deploy the world server and the client together.

- **Boss damage history** (`QuestDamageFullSubscribeHandler`, `QuestDamageFull`, `Player.SendQuestDamage`): a client that sends `QUEST_DAMAGE_FULL_SUBSCRIBE` (91) gets `QUEST_DAMAGE_FULL` (92) instead of `QUEST_DAMAGE`: everyone who hit its quest (name, class, damage; up to 255), the quest's maximum health, its own damage, and one more message with `Dead` set when the quest dies. The Rust client shows the top five under the boss and keeps the last 3 bosses it hit. Older clients still get `QUEST_DAMAGE` (the top three); a server without 91 ignores it.
- **The HUD's bar, and slots for a swap-out row** (`client-rs/app/src/hud.rs`; `InvSwapHandler`, `InventoryItems`, `EntityUtils.ForceUpdate`, `Player.ExportSelf`): the Rust client's HUD is one bar down the right of a full-window world (minimap, bars, character card; Nexus button, equipment, tabs and potions, inventory, interact area), with the boss and its damage at the top left. The server gets ready for a swap-out row the client will show in a later release: character slots 20-23, which were already saved with the character (28 slots) but unused, take their equipment slot's type (`Player.SlotTypes`), are sent to their owner as stats 144-147 (`SwapSlot0`-`3`), and `InvSwap` accepts them for players (other containers stay below 20). Before, slots 20-27 were tied to stats 75-82 (`HasBackpack`, `Skin`...), so changing one would have overwritten those; the free-slot searches (loot, `/gear`, crafting, `/ci`) also ran to slot 27 with a backpack and now stop at 19. Also fixed: a forced update of a backpack slot (a refused swap) sent the wrong stat (`BackPack0 + slot` instead of `+ slot - 12`). Potions come back out of their stacks: an `InvSwap` from slot 254 or 255 onto an empty inventory or backpack slot pops one.
- **Fame records in the game** (`FameStatsSubscribeHandler`, `FameStatsUpdate`, `Player.SendFameStats`): a client that sends `FAME_STATS_SUBSCRIBE` (93) gets `FAME_STATS` (94), the character's `FameStats` as id and count pairs, every 25 ticks while they change. The Rust client shows them in the character card's Records tab.
- **Realms for small groups** (`RealmManager`, `RealmWorld`, `World`, `CollisionMap`): a realm used to close after 25 event kills, one event at a time, each anywhere in the realm at full XML health (plus the `ScaleHP2` 20 to 35% per extra player, uncapped), so a few players couldn't close one. Now it scales with the most players the realm had at once in the last 5 minutes (`RecentPlayers`): events to close `clamp(players + 5, 8, 25)`, events at once 1, 2 from 6 players, 3 from 12. Event bosses (`ScaledEventMonsters`) get their health from the realm instead of `ScaleHP2` (`Enemy.RealmScaled`): 60% of it for 1 or 2 players seen within 30 tiles (each account once), 20% more per extra one, at most 300%; it only goes up, and a boss below its XML health gives that share of the experience (`Enemy.XpScale`). Loot is unchanged per kill. Table: 1 player 8 events, 1 at once, 60%; 3: 8, 1, 80%; 5: 10, 1, 120%; 10: 15, 2, 220%; 20+: 25, 3, 300% (before: 25 events, 1 at once, and 100%, 140%, 180%, 280%, 480%). The next event comes 15 s after one is spawned or defeated (`EventGapMs`), 25 to 80 tiles from a random player in the godlands-to-midlands band. An event is running while one of its parts lives (`EventParts`), so a boss that leaves no longer stalls the realm, and one nobody has hit for 10 minutes lets another come (at most 4 alive). The Killer Bee Nest counts once, when its third guardian bee dies (its bees used to count three times, through `ProcEvent`). The Shatters' Defense System event needs 5 recent players; the one every realm has as a set piece doesn't hold up events. The Castle is built on a pool thread (`CreateNewWorldAsync`). Admins: `/realmstatus`. Tested on an isolated stack with admin bots and `/killall`: 1 and 3 players closed a realm at 8 events and were all sent to Oryx's Castle.
- **Realm monsters wake by sight** (`World.WakesBySight`, `CollisionMap.GetChunksAround`): realms tick monsters within 2 chunks (32 to 47 tiles) of each player, or 3 for a sight radius over 20, instead of 3 chunks round every player, pet and decoy. With bots walking the godlands: 5 players before, 693 to 1,236 monsters awake, 1.5 to 2.6 ms per tick on monsters, 2.3 to 3.3 ms of work; 6 players after, 406 to 626 awake, 0.85 to 1.2 ms, 1.7 to 2.1 ms (about 2.3 times less per player). Tick stats show "enemies awake", and a player tick over 200 ms is logged as "Slow player tick". Other worlds keep the old rule. One 11 s "Slow tick" in the players phase was seen once before the change and not again; the new log names the player if it comes back.
- **Puppet Master's Theatre chest** (`EmbeddedData_PuppetMasterCXML.xml` and the client's `PuppetMaster.xml`): the Puppet Loot Chest (and the unused Puppet Treasure Chest) went from 30,000 HP and 35 defense to 15,000 and 15. It had RotMG's own chest values, but this server's Puppet Master has 30,000 HP (RotMG's had 70,000 to 150,000), so the chest was as tough as the boss and better armoured. Defense 35 also took 60% off each hit of the level-20 weapons the dungeon is meant for (a T10 staff at 55 attack: 56 to 21). Timed on a test stack with a level 20 Wizard, Staff of Astral Knowledge, 55 attack and dexterity, alone: 23.8 s of fire before, 9.3 s after, near the Manor of the Immortals' Coffin (RotMG's own 20,000 HP and 25 defense, about 16 s). Its `ScaleHP2(20)` still adds 20% per extra player, and its loot is unchanged. The other dungeon chests were left alone: the end-game balloons and chests (Lair of Draconis, Ivory Wyvern, Shatters, Ice Cave, Ice Tomb, Parasite Chambers) take a maxed T12 character about 18 to 26 s alone and Shaitan's about 35 s, all RotMG's values. The server decides the damage, so a world server deploy is enough; the client's copy only makes its own damage numbers match.
- **Recent Deaths** (account server, world server, Rust client): `Database.Death` pushes each death onto the Redis list `deaths:recent` (JSON: account and character ids, account name, class, skin, dyes, level, fame, killer, Unix time, world name), trimmed to the last 30 (`DbRecentDeaths`). `/fame/recent` (form field `count`, default 20) lists them newest first, with `Ago` in seconds so clients need no clock. The Legends screen's fourth tab shows them; a row opens that death's `/char/fame` summary. Admins' deaths are listed too (they're left out of the legends). `/fame/list` is unchanged.
- **Every class open from the start:** `Database.CreateCharacter` no longer checks the classes' `<UnlockLevel>` (a level in another class), and `/char/list` reports every class `unrestricted`; admin-only classes stay admin-only. The Rust client doesn't lock classes on its new-character screen any more. Skins still need their class level.

- **Castle nexus and vault** (`tools/build_castle_maps.py`): the nexus (`Exalt/Nexus Castle.jm`, 97 by 92 tiles, was `Nexus New Auru.jm`, 377 by 392) is a castle courtyard in the client's and website's look: curtain walls and towers with lit windows and a dark moat, the keep's great hall with the 21 realm portal tiles, the spawn with the healing fountains, two market wards with every shop's old stall count, and a south yard with the arena portals (`DoomedWorlds.NexusPortals` moved with them), the Wine Cellar and the memorial. The four shared nexus chests stay on their old tiles, which are their Redis keys (`DbNexusChest`). The vault (`Exalt/Vault Castle.jm`, was `Vault New Auru.jm`) is a cellar of Oryx's Sanctuary high walls with the same 60 vault chest and 26 gift chest tiles. Both are generated: change the script and rerun it. The old maps are still in the repository.
  - **Event maps:** the script also writes a decorated copy of each map per event in its `EVENTS` (now `Halloween`: jack-o'-lanterns, green candles, fallen leaves, dead and autumn trees, a cauldron), in decoration slots the layouts mark, so nothing players use moves. `serverSettings.mapEvent` in `wServer.json` (empty by default) picks them: a world whose map is `<map>.jm` loads `<map> <mapEvent>.jm` where one exists (`World.LoadMapFromData`). A world server reads it when it starts, so a rolling deploy switches it without kicking anyone.
- **Assassin poison** (world server): `PoisonEnemy` in `Player.UseItem.cs` ignores defense (it took defense off the total), spreads `TotalDamage` over `DurationMS` from the elapsed tick time with one hit a second (it hit every 1.2 s for a per-second amount, so it ran 20% long), and stops when the enemy dies or leaves the world (a dead enemy's timer used to fire every tick forever). `Enemy.Damage` kills at 0 health: it caps damage at the enemy's health, so the old `Health < 0` check let poison and other ability damage leave monsters alive on 0.
- **Nexus vault chests keep their items:** the shared Vault Chests the nexus map places (four of them) were plain containers, emptied by every world server restart. `NexusWorld.LinkVaultChests` links each to a Redis record (`DbNexusChest`) when the nexus starts: the hash `nexusChests`, fields `items.<x>_<y>` and `datas.<x>_<y>` for the chest's tile. Every change to a chest is saved at once, as vault chests are. Moving a chest in the map starts it empty (its old items stay under the old tile's fields).
- **Servers screen (Rust client 1.1.3)**: the character list has a Servers button again (the "Server:" pill under the characters opens it too). It lists the world servers from `/char/list` with Normal/Crowded/Full and the player count, as the AIR client's `ServersScreen` does; a click picks the server and returns to the characters, and the choice is kept (`preferredServer`). The AIR client's screen (title screen, Servers) is unchanged. Client only.
- `client/src/WebMain.as`: on a first run the map scale setting (`mscale`) is undefined until the player enters the game. The first window resize divided by it and asked for a 0-pixel-wide Stage3D back buffer (Error #3780); it now defaults to 1.
- `source/App`: `char/list` and `app/serverList` send 127.0.0.1 as the world server address to clients on this PC, and this PC's LAN address to clients on the local network or reaching us through our own public IP.
- **Network performance** (world server):
  - Each world thread queues the messages it sends during one pass of its loop and sends them together at the end (`NetworkSendHandler.BeginBatch`/`FlushBatch`). A tick's messages leave in a few TCP segments per player instead of one segment per message.
  - World threads sleep until a player's message arrives or the next tick is due, instead of polling and keeping a CPU core busy for every world with players in it. `Program.cs` asks Windows for 1 ms timer precision so ticks stay 100 ms apart.
  - Fixed byte counting when a send needs more than two chunks, which could drop data and corrupt the stream. A message that doesn't fit in the send buffer now goes first in the next send, so messages stay in order.
  - Quick Edit is turned off in the World Server console window. Clicking in it no longer pauses the server.
  - Per-hit debug prints are gone from the console.
  - Ticks slower than 200 ms are logged as "Slow tick" in `logs/wServer`, slow passes over players' messages as "Slow messages", and every 30 s each world with players logs "Tick stats" (work, gaps between ticks, time per part, garbage collections).
  - New dungeons, vaults and `/dungeon` worlds are built on a pool thread (`WorldManager.CreateNewWorldAsync`). Built inline, a dungeon froze the realm it was opened from, and every dungeon of that realm, for up to a second (measured: a 1070 ms gap with four players while one opened six dungeons; afterwards no slow passes). Players who use a portal while its dungeon is being built go in with the first.
  - Players in a dungeon or vault (a branch world, run on its realm's or the Nexus's thread) now wake that thread when they send something; their moves and shots used to wait for the next tick, up to 100 ms.
  - The world server uses the server garbage collector with DATAS (`WorldServer.csproj`): collections run on every core and are rare. With the default collector a full collection of the ~1 GB heap stopped every world for up to 1.8 s; measured afterwards, at most 137 ms. It uses about 300 MB more memory; remove the three GC lines from the project to go back.
  - The WorldServer and Shared projects are compiled with optimisation, in the Debug build that `Run Server.bat` uses too.
- `wServer.json`: port 2051 instead of 2050, and the public address.
- **Client rendering**, ported from the Doomed Realms client's optimisations and adapted to the GPU (Stage3D) renderer. In the nexus this took the test client from 110 to 191 fps.
  - **Ground chunks** (`Map`, `Square`): static ground tiles are copied into one 128x128 image per 16x16 tiles, and each visible chunk is one textured quad (one GPU draw call) instead of up to 256 tiles. Animated tiles and blended edges are still drawn on their own, on top; their plain texture also goes into the chunk so coasts have no dark seam. A chunk is rebuilt (as a new image, since the GPU caches textures by image) when a tile in it or next to it changes, at most 2 builds per frame, and freed after 10 s unseen (`TextureFactory.disposeTexture`). Clicks and aiming find the tile by inverting the ground transform, and objects drawn flat on the ground get their tile's corners from it.
  - **Tile culling** (`Map.draw`): of the 15-tile circle around the player, only tiles whose centre is on screen (with 2 tiles of margin, 4 below for tall sprites) are drawn.
  - **Batching** (`Renderer`): every plain quad of a frame goes into one vertex buffer, uploaded once, and each run of quads with the same image (or the same atlas page) is one draw call; GPU state that is already bound isn't set again. Shadows are one draw call. This is the GPU counterpart of Doomed Realms' particle batching.
  - **Texture atlas** (`TextureAtlas`): images up to 128 px that stay in use (sprites, wall faces, tile edges) are packed into up to six 1024x1024 textures, so runs of different sprites share a draw call. Tiles drawn on their own draw all base faces first and then all edge overlays, to keep the same images together.
  - **Fullscreen** fills the whole screen: the client works out how far its view reaches and asks the server for that sight radius (`VIEW_RADIUS` message, 15 to 40 tiles; `Player.SightRadius`). Players with a normal window keep 15 and its cost. At 4K the whole view draws at about 78 fps (with ~340 draw calls instead of ~1060).
  - **Projection cache** (`Camera`, `Face3D`): while the camera only moves (no rotation or zoom), wall and tile faces reuse their projected shape and texture matrix, shifted by the camera translation. Tiles also no longer rebuild their texture matrix every frame when their offset hasn't changed (`SquareFace`).
  - **Projectiles** check the distance to each target before anything else.
  - **Flashing objects:** the renderer's atlas check only peeks at an image's one-frame tint (`GraphicsFillExtra.peekColorTransform`); it used to use it up, so objects stopped flashing.
  - **Errors:** `GameSprite` keeps its frame time up to date even when a frame throws, so the player can't run away at the speed of the time since the error.
  - **No garbage per frame:** the game used to create about 4,000 objects every frame at 4K, now about 40. `TextureRedrawer` finds cached images without building string keys, the boss damage text is only set when it changes (it was set and measured once per object in the world, every frame), and `GraphicsFillExtra` no longer makes a new ColorTransform per lookup. A normal-sized window went from 139 to 201 fps.
  - **Performance Stats** (Options) also shows the map draw time per frame (and how much of it is the GPU), the time in tiles, objects and the HUD, the draw calls by kind, and `frame = game + flash`: the game's own time per frame against the AIR runtime's. The runtime needs about 4 ms per frame at 4K to composite the screen, whatever the game does, so about 240 fps is the ceiling there.
  - Not ported, because betterSkillys already has them or they only mattered in Ruffle/Flash: the frame-rate option and render-quality toggle (both exist), projectile hit lists (exist), the fullscreen zoom (betterSkillys has map scale), and the Ruffle fixes (outline glow, chat text, 32-bit float drift).
- **Build warnings** (all projects build with none):
  - `Enemy.OnDeath` was never raised, so `OnParentDeathTransition` (used by Lost Halls monsters) never fired. It is raised on death now, and the transition keeps its flag per monster instead of on the shared behaviour definition.
  - BouncyCastle 1.8.9 (known vulnerabilities) is replaced by BouncyCastle.Cryptography 2.4.0; login password decryption (`RSA.Decrypt`) was checked against .NET's own RSA.
  - Obsolete crypto calls in `Shared/Utils.cs` use their replacements, an unused `APIItem` class with a placeholder secret in `MarketBuyHandler` is gone, and the rest were unused code, duplicate usings and Json.NET-filled fields.
  - `/sell` had a dead check (`item.Tier == null`, but untiered items have tier -1); it was removed and the behaviour is unchanged: untiered weapons, armour, rings and abilities still can't be sold.
- **Server name** (`WorldServer/wServer.json`): "Europe" in the server list, was "Localhost".
- **Spawn tiles in setpieces** (`Wmap.ProjectOntoWorld`): a setpiece's Spawn-region tiles were added to the world's spawn points, and the realm's `Shtrs Defense System.jm` (the Avatar's arena) has four, so some players entered the realm in it. Setpieces no longer add spawn points (and a repeated region tile no longer throws).
- **Boss health** (`ScaleHP2`): its default `scaleStart` was 0, so even a lone player added its percentage (15-40%) to every boss using it; scaling now starts with the second player. Dr Terrible 55,000/25 def to 25,000/20, Horrific Creation 60,000/30 to 35,000/25 (Mad Lab), Ghost of Skuld 130,000 to 50,000 (Haunted Cemetery). The other bosses were checked against their dungeon's tier and left as they are.
- **Pet owner stat** (`Pet.ExportStats`, `StatDataType.PetOwner` = 143): a pet sends its owner's object id, so the Rust client can fade or hide other players' pets and their effects. It is an int stat, so older clients read and ignore it.
- **Blobombs** (`BehaviorDb.Avatar.cs`): the Avatar of the Forgotten King's "Blobs, attack!" phase only taunted; it now orders the 8 `shtrs blobomb maker` objects around its arena to spawn Blobombs, as the Shatters' first boss does.
- **Buying a character slot** (`App/Controllers/AccountController.cs`): the handler awaited a command queued on a Redis transaction before running the transaction, so it hung forever holding the account lock. Being `async void`, it had already answered with an empty success, so the client showed the slot while the server never added it, and logging in failed with "Account in use" until the 45 s lock expired. It now runs the transaction, releases the lock and answers with the real result (success, insufficient funds or account in use).
- **Moves onto no-walk squares** (`MoveHandler.cs`, `Player.RejectMove`): the client only stops a player entering a no-walk square, so it can end up standing on one (it happened on a Sprite World "Space" square). The server disconnected for "NoClipping"; with that disconnect commented out it still dropped the move without counting its tick, so the next move disconnected with "TickIds don't match". It now keeps the server position, counts the move, and sends the player back with a Goto, at most once a second.
- **Taunts from game objects:** `Taunt` assumed its host was an enemy and threw for game objects (spawners, controllers), so their lines were never said. It now works for any host.
- **Beer God:** the realm event in the high forest had object data but no behaviour, so it stood still. It now wanders until a player comes near, then follows and throws beer barrels (Drunk) and punches, with a ring of shockwaves every few seconds, and at 30% health "Last call" does all three faster. It drops tiered loot, the Mighty Stein and the Mini Beer God pet stone (`BehaviorDb.Highland.cs`).
- **Monsters without HP:** objects with no `MaxHitPoints` (anchors, spawners, traps, controllers) started at 0 HP and `Enemy.Tick` killed them on their first tick, before their behaviour ran. 139 behaviours were affected, among them the Tomb's boss anchor (which dropped the exit portal at once), the Pentaract, turrets and most dungeon controllers. Only enemies with HP now die at 0 HP; the others end through their behaviour.
- **Missing projectiles:** 358 `Shoot` behaviours named a projectile index that their monster's XML doesn't define, so `Shoot` logged "Doesnt have projectile" and the attack never fired. Each now uses the projectile it most plausibly meant: the Forgotten King's tentacle spirals 4 → 0 (White Bullet, otherwise only in its death burst), the rage rings of Oryx 2 and Oryx Deux 7/8 → 13/14 (the Quiet and Weak spinners), Cyclops God's blade attack 4 → 10 (Blade), the NM Blue Dragon God 4 → 6 (the Red Spinning Fireball it never used), Stheno's spiral 2 → 1 (the blinding bullet), the Red Guardian Bee 4 → 3 (its Ball; the other guardian bees' 4 is a second Ball), Archdemon Malphas and Malphas Deux in their small phase 1 → 0 (fast small bullet), the Royal Cnidarian's rings 0 → 2 (Bubble Wave), F.E.R.A.L.'s spray 0 → 2 (Psychic Attack), and the Wishing Troll, Epic Larva and small Cnidarians to the only projectile each has. `python tools/check_shoot_projectiles.py` lists any such call; it attributes each `Shoot` to the `.Init` it is in, or to every `.Init` that calls its helper method.
- **Nexus Bazaar** (`Exalt/Nexus New Auru.jm`, built by `tools/build_nexus_bazaar.py`): the big sand island south of the town, with the class memorial, couldn't be reached, because the town's dock ended at Dark Water Flowing. A bridge now continues the dock to the island, a road with lanterns leads to the memorial plaza, and nine market pavilions stand around it, with palm trees and beach umbrellas on the open sand. Only stores 5 and 6 (pet generators, large cloths) had stalls on this map, and the other shop lists were empty. Every bazaar shop sells for fame (players have no way to earn gold); prices match the Doomed Realms `/shop` where both sell an item (`MerchantLists.cs`):
  - Dungeon Keys (Store 11): the regular key of each of the 43 dungeons that has a world, one stall each, 10 to 500 fame. Elder and Doomed Keys (Store 12): 20 keys and incantations. Enchanted Keys (Store 4): 18 keys that also turn into a Mystery ST Crate, at twice the key plus 250. Guild, test and reusable keys aren't sold.
  - Stat Potions (Store 1): the eight stat potions, 150 fame (Life and Mana 300). Consumables (Store 2): health and magic potions, tinctures, effusions and drake eggs. Specials (Store 3): XP Boosters, the Loot Drop Potion, Backpack, Vault Chest and Char Slot Unlockers, mystery dyes. Small cloths, clothing dyes and accessory dyes (Stores 7 to 9) rotate through their stalls as the town's large cloths do.
  - Merchant fixes: a stall whose shop had every item out on other stalls was dropped for good (`NexusWorld.SendOutMerchant`); it now waits and tries again. A shop item missing from the game data is removed at start-up instead of becoming a merchant with an invalid item.
- **Nexus fountains:** the four fountains in the nexus's water basins were the frozen winter kind, which give off nothing. They are now the White Fountain, with its spray, and standing within 3.5 tiles of one gives the Healing condition (`NexusWorld.HealByFountains`, renewed every half second). The map change and the healing take effect when the world server restarts.
- **Client updates** (account server): `/client/` serves the published Rust client (`serverSettings.clientFolder` in `server.json`, else the nearest `dist/client` above the server) for players' clients to update from. The release lists are never cached; the files, named by their content's hash, are cached for good. It's there once the account server restarts with this, and `dist/client` exists when it starts (`Run Server.bat` publishes before starting the servers).
- **Quest damage dealers** (`QUEST_DAMAGE_SUBSCRIBE` 89, `QUEST_DAMAGE` 90): a client that asks gets its quest's top three damage dealers and its own damage, as the server counts them (`DamageCounter`), whenever they change (`Player.SendQuestDamage`, checked twice a second). The Rust client shows them under the boss bar. The AIR client never asks, so nothing changes for it; a server without this ignores the request.
- **Boosters:** the XP Boosters and the Loot Drop Potion mark their effect with `<XpBoost/>` or `<LDBoosted/>` and a `<Timer>`, which nothing read, so using one did nothing. `Player.Activate` now starts the boost from them (`<Timer>-1</Timer>`: until level 20). Using an XP Booster while one runs gives it back instead of eating it, and starting one says so. `/boosts` lists an active XP Booster (it only counted ones with a positive time, so one lasting until level 20 never showed) and the time left. Loot tier boosts (`<LTBoosted/>`, the Loot Tier Potion and half of the Lucky Clover) still aren't implemented, so the Loot Tier Potion isn't sold.
- **Dungeons** (world server):
  - Map files are looked up without regard to case (`XmlData.WorldDataCache`). Cave of a Thousand Treasures and the Avatar, Skull Shrine and Grand Sphinx setpieces didn't load before.
  - At start-up the server logs worlds whose map is missing and portal destinations with no world (`XmlData.ValidateWorlds`).
  - New world entry for Wine Cellar (map and Oryx 2 already existed). The Ivory Wyvern world is now `The Ivory Wyvern`, the name its portal uses.
  - Maps that placed the nonexistent "Portal to Nexus" (Ocean Trench, Oryx's Chamber, guild halls) use "Nexus Portal", which `UsePortalHandler` treats as an exit. A portal without a destination shows an error instead of throwing.
  - Loot entries that named missing items were fixed (display names replaced by ids, such as `PriestST1` for Book of Geb) or removed (items not in these XMLs).
  - **Endgame:** when a realm closes, its players are sent to a new Oryx's Castle (`RealmManager.SendPlayersToCastle`) instead of being teleported inside the realm. In the castle, killing both Stone Guardians opens the wall to Janus the Doorwarden (`md dwGenerator`); Janus (new fight) drops the Oryx's Chamber portal, Oryx 1 the Wine Cellar portal.
  - **The Oryx chain** runs from a realm's close to the end of Doomed Realms' content. Janus drops the open Chamber portal; each Oryx after that drops a locked portal to the next stage, which takes an incantation to open (below), and a Realm Portal: Oryx's Castle (Stone Guardians, then Janus) → Oryx's Chamber (Oryx 1) → Wine Cellar (Oryx 2) → Oryx's Sanctuary (Oryx 3, new) → Hash Cellar (Oryx 4) → Doomed Cellar (Oryx 5). Each Oryx's death is announced server-wide.
    - **Difficulty curve:** `BossStats` (`logic/behaviors/BossStats.cs`) sets each boss's health, defense and shot damage from its behaviour, so the stages sit on one curve without changing the client's XML. Shot damage is the XML damage times a scale (`Enemy.DamageScale`, applied in `Shoot`). Health rises by a share of the base for each player after the first. It also clears the random rare/epic/legendary roll quest monsters get (up to triple health), which would break the curve.

      | Stage | Boss | Health (solo) | +per extra player | Defense | Typical shot |
      |---|---|---|---|---|---|
      | 1 | Stone Guardians (each) | 35,000 | 25% | 30 | 85-110 |
      | 2 | Janus the Doorwarden | 70,000 | 25% | 25 | 120 |
      | 3 | Oryx 1 | 100,000 | 30% | 35 | 90-210 |
      | 4 | Oryx 2 | 130,000 | 30% | 40 | 135-240 |
      | 5 | Oryx 3 | 180,000 | 30% | 45 | 170-245, Exalted 195-350 |
      | 6 | Oryx 4 | 240,000 | 30% | 50 | 140-280, rare 410-500 |
      | 7 | Oryx 5 | 320,000 | 35% | 55 | 270-350 |

      **Loot** rises the same way. `python tools/oryx_loot_report.py` works out what one player can expect per kill from the tables, as `Loot.Handle` rolls them: each entry is rolled at its chance times 1 + the player's share of the damage + the loot boosts, so a solo player rolls at double the listed chance and each of three equal players at about 1.6 times (with the 25% server-wide event). For a player in a group of three:

      | Stage | Stat potions | Life / Mana | Potion of Oryx | Gear (tier) | White bag |
      |---|---|---|---|---|---|
      | 1 Stone Guardians (both) | 1.4 | - | - | 0.5 (T10-12) | 0.5% (Ancient Stone Sword) |
      | 2 Janus | 1.9 | - | - | 0.5 (T10-12) | - |
      | 3 Oryx 1 | 2.2 | 0.1 / - | - | 0.6 (T10-12) | - |
      | 4 Oryx 2 | 2.4 | 1 / 0.5 | - | 0.7 (T11-13) | 0.9% (Seal of Blasphemous Prayer) |
      | 5 Oryx 3 | 2.6 | 1 / 1 | 6% | 0.6 (T12-14) | 1.6% (Oryx's Escutcheon) |
      | 6 Oryx 4 | 2.9 | 1.1 / 1 | 13% | 0.6 (T13-15) | 7% (the Oryx set) |
      | 7 Oryx 5 | 3.8 | 1.2 / 1.2 | 24% | 0.6 (T14-15) | 18% (Oryx set, Doom Poison, Sword of Dark Enchantment, Crown) |

      Gear stays at about one piece every two kills while its tier rises. A Potion of Oryx counts as all eight potions (`/forge` makes one from them), so it stays rare. Each piece of the Oryx set (Ring of Oryx, Oryx Hide Armor, Oryx Hide Robe, Oryx Scale Armor, Oryx Tooth Sword, Doom Dirk, Flying Doom Daggers, Tome of Noble Assault) is 0.6% from Oryx 4 and 1.4% from Oryx 5 before the multiplier. Oryx 4 and 5 also drop the Oryx forge materials. The first tables were far richer: with the weekday boost bug below, Oryx 5 gave each of three players a Potion of Oryx every kill and an Oryx set piece or other special item about half the time.
    - **White bags for the Oryx set:** Doomed Realms numbered its loot bags differently, so the Oryx set dropped in brown and blue bags without an announcement. `tools/port_doomed.py` (`BAG_TYPES`) now gives the set, Doom Poison and Sword of Dark Enchantment the white bag (6) and Potion of Oryx and Potion of Power the special-consumable bag (7). Only those lines of the generated XML were updated: a full regeneration also rewrites unrelated parts (Native White Demon moves from ElderObjects.xml to DoomedRealms.xml, Elder Abyss.jm changes), so the committed generated files have drifted from the generator.
    - **Weekend loot boost:** `Loot.Handle` added `wkndBoost` (+30%) to every roll every day, while `/boosts` and the login message only claim it on weekends. It now applies only when `World.isWeekend`. This lowers drop rates everywhere on weekdays, back to what players are told.
    - **Castle:** the Stone Guardians' ring attacks never fired: `RingAttack` only shoots when its radius is 0, and theirs was 20. They now use `Shoot` rings. (Other monsters' `RingAttack`s with a radius are left as they are.)
    - **Oryx 1** cycles minions, an invulnerable dance with ring elements and a spiral, his artifacts, his gaze and a second dance; his second dance used to loop forever with him invulnerable three quarters of the time. Below 20% he grows and chases.
    - **Oryx 2** gains a call for guards at 55% (henchmen and vintners under silencing and weakening spinners) and a faster second phase.
    - **Oryx's Sanctuary** (`tools/build_oryx_sanctuary.py`, `BehaviorDb.OryxSanctuary.cs`): a new map in the Exalt sanctuary art both clients already have: a vestibule, a gallery of nobles, the Court (three officers and three clergy; the Deacon heals the Cleric and Cardinal) and a gate that opens when the court is dead (`O3 Gate Deleter 1`), then the round Sanctum. Oryx 3 cycles missiles, a charge that leaves his wheels behind, a telegraphed stunning ring ("KNEEL!"; stand more than eight tiles away), bombs that burst into spikes and spinners. At half health his three Protectors must die before he can be hurt again; after that he uses the stronger Exalted attacks, and below 20% he hunts players down. His Patricians and Oryx Portals aren't used: their shots put players in stasis, and a player in stasis still takes full damage here.
    - **Chicken Chamber** (`tools/build_chicken_chamber.py`, `BehaviorDb.ChickenChamber.cs`): RotMG's April Fools Oryx's Chamber, whose objects (Giant Oryx Chicken, Oryx Chicken Minion, Egg; still the test ids `TestChicken 2`, `TestChicken Small`, `Test Egg` in `OryxChickenChamber.xml`), portal and key were in the prod XML with no world or behaviours. It's a side door beside Oryx 1: a vestibule, a coop and a henhouse of chickens and eggs (an egg cracks once a player is near and hatches a chicken after 7.5 s unless it's broken), then a round chamber in Oryx's blue tiles where the Giant Oryx Chicken roosts. It wakes and grows when a player comes within 11 tiles; pecks with pink spreads, confusing star rings and tossed eggs; at 66% molts (invulnerable feathers, blasters and a clutch of four eggs); then spins rainbow spirals; at 33% grows again and chases players with its chicks. It drops the locked Wine Cellar portal (the Wine Cellar Incantation's `onlyInArea` already named the Chicken Chamber) and a Realm Portal, potions and T10-12 gear like Oryx 1, and the Chicken Leg of Doom (5%), Anatis Staff (5%), Apocalypse Feather (1%) and Oryx's Sanctuary Incantation (4%). The Oryx Chicken Chamber Key comes from Janus (3%) and the Nexus Bazaar (400 fame); its description no longer says it's a test item (prod XML, server and client copies). Tested on the isolated stack with the Rust client: the portal, the coop, every phase (pushed through with `/smite`), the death announcement, both portals and the loot bags.
    - **Oryx 4** (Hash Cellar) and **Oryx 5** (Doomed Cellar) are hand-written (`BehaviorDb.OryxDoomed.cs`) and replace the generated Doomed Realms versions; that Oryx 4 stayed in its first ring phase for good. Oryx 4 is Oryx 1's moveset made harder, with Knights of Doom, six orbiting White Moons and a telegraphed weakening ring. Oryx 5 is invulnerable until the twenty Trees of Doom around him are cut down, with the castle's Stone Guardians beside him (stronger here); he then cycles fire, a blade charge, star spirals and summons of Knights and Trees of Doom (each living tree heals him), turns faster with Fire Boomerangs below 60% and makes a last stand below 20%.
    - Also given behaviours: Guardian Elements 3 and 4, Outer Guardian Elements, White Moons, Knights and Trees of Doom, the Hash Cellar's Jungle Fires (a mild fireball at anyone standing right beside one; they line the path) and the Doomed Cellar's Oryx Guardian Swords.
    - **Portals:** each Oryx drops the locked portal (10 minutes) two tiles to one side and a Realm Portal (2 minutes instead of the default 30 seconds) two tiles to the other, so neither hides under the loot bags.
    - **Incantations:** the locked portal is the `Locked Wine Cellar Portal` object, named by the server for where it leads ("Locked: Oryx's Sanctuary"; `DropPortalOnDeath` `name`, which the Rust client shows). Entering it says which incantation it needs (`Portal.LockedHint`, `UsePortalHandler`). Using that incantation within 25 tiles of it, in that Oryx's dungeon, turns it into the open portal for 5 minutes and tells the dungeon who opened it. This is the XML `ChangeObject` activate effect (`targetId`, `newId`, `onlyInArea`, `customString`), which was never implemented before (`Player.AEChangeObject`); an incantation used in the wrong place, or with no locked portal near, says why and isn't spent. Refused item uses (that, too little mana, a consumable while trading) re-send the slot (`ForceUpdate`), because the client empties it as it asks, and put a popped health or magic potion back on its stack.

      | Oryx | Locked portal to | Incantation | Where to get it |
      |---|---|---|---|
      | 1 | Wine Cellar | Wine Cellar Incantation | Janus (4%), the Nexus Bazaar (125 fame), and as before the Tomb, Ocean Trench, Snake Pit, Candyland and Ivory Wyvern bosses |
      | 2 | Oryx's Sanctuary | Oryx's Sanctuary Incantation (`Oryx Sanctuary Key`) | Oryx 1 (4%), the Nexus Bazaar (300 fame) |
      | 3 | Hash Cellar | Hash Cellar Incantation | Oryx 2 (4%), the Nexus Bazaar (500 fame) |
      | 4 | Doomed Cellar | Doomed Cellar Incantation | Oryx 3 (4%), the Nexus Bazaar (1000 fame) |

      The Oryx Sanctuary Key (a test item that opened the Sanctuary anywhere) became the Sanctuary's incantation in the prod XML (server and client copies). The Hash and Doomed Cellar Incantations opened their dungeon anywhere, like keys; `tools/port_doomed.py` (`INCANTATIONS`) makes them unlock the locked portal instead (only those items' lines of the generated XML were updated). The Wine Cellar Key no longer opens the Wine Cellar from anywhere: it works like a Wine Cellar Incantation (prod XML, server and client copies), and the Bazaar sells the incantation in its place. The Bazaar's Oryx Chamber Key still opens the Chamber directly.
    - **Quest arrow:** monsters with `<Oryx/>` now always win the quest choice (`Player.FindQuest`): the Doomed Cellar's Skull Shrine, an Encounter, outscored Oryx 5, and Oryx 3's XML level is 1.
    - Oryx 3's Protectors spawn on him and fly out to their orbits; tossed ones could fail to land (a toss needs a free target square) and leave the phase empty.
    - **Tested** on the isolated stack with the Rust client: realm close to the Castle, every stage's fight and phases (pushed through with `/smite`), each death's announcement and portals, and walking through Oryx 2's portal into the Sanctuary. Not yet played through by a group without admin help, so the numbers above are a first pass.
  - **Portal sources:** Ghost Ship drops Davy Jones' Locker, the Killer Bee Nest The Hive, Oryx 2 Lost Halls (10%), Lord of the Lost Lands Ice Tomb (25%), Hermit God Cnidarian Reef (25%), the Statues altar Magic Woods (25%). Rock Dragon drops Lair of Shaitan, because Lair of Draconis has no map or world yet.
  - **New boss fights** for bosses that stood still: Mixcoatl and its Boss Totems (Forbidden Jungle), Belladonna, the Masked Party God (Beachzone, no attacks), the Abyss Idols, and Horrific Creation in Mad Lab, whose room opens when Dr Terrible dies (`Mad Lab Open Wall` removes the Blue Torch Walls). Horrific Creation drops the Horrific Sorcerer set (`SorcererST0`-`3`).
  - **Ancient Ruins** (`BehaviorDb.AncientRuins.cs`): every monster now has a behaviour. Cacti are ringed by invisible Cactus Burst traps, Monoliths summon Terracotta Paladins and Knights, and the Sandstone Titan (`Ruins Boss Golem`) fights in three phases: invulnerable while its three Ancient Spirits live, and it wakes the Sand Bombs in its chamber. It drops the golem Trickster set (`TricksterST0`-`3`), Sandstone Seal and the exit portal; the Realm Portal that the map placed beside it was removed. The Grand Sphinx has a 25% chance to drop the Ancient Ruins portal.
  - **Cursed Library** (`BehaviorDb.CursedLibrary.cs`): behaviours for every placed monster; the Eyes of the Edified heal the monsters around them. Avalon the Archivist fights in three phases, wakes the Book Bombs around her desk and tosses candles that spin slowing cyclones. She drops Mark of the Archivist, Scholar's Seal and the exit portal. The Skull Shrine has a 25% chance to drop the Cursed Library portal. Optional event: walking up to one of the four Corruption Triggers summons the Corruption Phantom when none is loose (each trigger fires once); it calls its four wisps at half health and drops Skull of Corrupted Souls.
  - **Fungal Cavern** (`BehaviorDb.FungalCavern.cs`): behaviours for every placed monster; the Mushroom Alchemists heal nearby mushrooms and the loose Red Worm segments act as turrets. The map has no Crystal Worm Mother (she belongs to the Crystal Cavern, which has no map here), so the Mushroom Berserker is the final fight: it drops the Mushroom Tribes set (Tome, Sporous Spray Spell, Fungal Breastplate, Cave Dweller Trap) and the exit portal. The Pentaract has a 25% chance to drop the Fungal Cavern portal.
  - **Parasite Chambers** (`BehaviorDb.ParasiteChambers.cs`): behaviours for every placed monster and hazard (Pulsating Obstacles, Pterasite nests); the rooms are walled off by destructible pD Blockades. The colonies and the boss have defense in the thousands, so they apply ArmorBroken to themselves when they're meant to take damage: Swarm Colonies always, the Nightmare Colony (`pD Boss`) in 8-second exposed windows between 12-second shielded ones, and for good below 30% HP. Its support under the wall fires while it fights. The Infested Chest in the treasure room can be broken once its Blood Slurps are dead. The boss drops the parasite set (`pD Tome`, `pD Scepter`, `pD Spell`, `pD Armor`) and the exit portal. The Cube God has a 25% chance to drop the Parasite Chambers portal.
  - **Secluded Thicket** (`BehaviorDb.SecludedThicket.cs`): the map had no spawn point and no boss, so it now has a Spawn region at the south end and Tezcacoatl the Great Basilisk in the lightning arena to the north. The two Adult Basilisks guard the south clearing; once they die, the braziers open the El Dorado temple's brick doorways (paths through the forest also lead around it). Tezcacoatl fights in three phases, summons basilisks, and drops Tezcacoatl's Tail, Mask of Lightning, Lightning in a Bottle, Staff of the Crystal Serpent, Cracked Crystal Skull and the exit portal. Mixcoatl (Forbidden Jungle) now drops an exit portal and has a 50% chance to drop the Secluded Thicket portal.
  - **Idle monsters given behaviours:** Oryx's Castle's Pets of Oryx (chase and bite) and Living Floors (silence and weaken players who stand near), Wine Cellar's Purple Goo puddles, the second Wizard Puppet in the Puppet Master's Theatre, the Shatters' "S" Glassier Archmages (same as the regular ones) and the Deadwater Docks Macaw.
  - **Tomb of the Ancients** (`BehaviorDb.Tomb.cs`): players start beside the dormant Bes, Nut and Geb statues; five sarcophagi around the tomb are guarded by Beam Priests and Priestesses. Each sarcophagus opens once its own guards (within 15 tiles) are dead, and the statues wake once all five are destroyed. Before, a sarcophagus waited for every priest in the map, the statues woke as soon as the sarcophagi opened (without destroying them), the priests had nothing to orbit, the boss anchor died at once (dropping the exit portal at the start), and the artifacts orbited the wrong bosses. Each boss now summons its own three artifacts (Bes the Pyramid, Nut the Sphinx, Geb the Nile ones), which die with it. The boss fights themselves are the existing port.
  - **Ice Cave / Inner Sanctum** (`BehaviorDb.IceCave.cs`): the Inner Sanctum portal never appeared, because the Ice Cave's `ic boss spawner live` is a game object and can't die (its `Suicide` threw and `TransformOnDeath` never ran); it now spawns the portal itself once every Big Yeti and Snow Bat Mama is dead, then removes itself. Big Yetis throw their boulders (they only followed before). Esben checked for his crosses and shielded kings in the same tick he ordered them, before they existed, so he skipped those phases; he now waits 2 s for them.
  - **Exit portals:** these final bosses had no way back except the nexus and now drop a Realm Portal: Thessal (Ocean Trench), Daichi (Mountain Temple), Belladonna, the Masked Party God, Ghost of Skuld (Haunted Cemetery's last stage), the Ice Tomb chest, the Royal Cnidarian, Gulpord (Toxic Sewers), the Head of Shaitan and Oryx 2 (Wine Cellar).
  - **Daichi the Fallen** (Mountain Temple) stayed invincible forever below 40% HP: his "elements" and "increaseSize" states had no way out. He now grows, puts the Powers to sleep and fights again.
  - Ice Tomb's chest spawner runs on the map's Ice Tomb Boss Anchor. The Magic Woods fountain gate waits for the Fountain Spirit (it waited for a nonexistent "Grim Reaper" and opened at once).
- **Admins handle items like everyone else:** admins can trade with players, drop items into public loot bags, and move items into and out of bags they don't own. Before, trades between an admin and a non-admin were refused or cancelled, everything an admin dropped went into a private soulbound bag, and admins couldn't touch other bags. Soulbound items still follow the usual rules.
- **Skin selection showed every skin twice:** the client parses the class list twice at start-up (the regular classes, then the Doomed Realms ones), and the skin parser, hooked to the same signal, added the whole skin list each time. A class's skin list now ignores a skin it already has (`CharacterSkins.addSkin`). Client only.
- **Admin-only classes:** a class with `<AdminOnly/>` in its XML (Vaelith, "For Realm Creators") is only for admin accounts. For everyone else the account server lists it as unavailable, so the class screen hides it, and the world server refuses to create or load a character of that class ("Class locked"). Before this, any player could pick Vaelith, which can hold any item in any slot.
- **Potion storage:** it can be upgraded from its window: each upgrade costs 500 fame and holds 50 more of every potion (account field `potionStorageLevel`, 50 + 50 per level; `UseStorageHandler` action 4). `/account/grabPotions` sends the capacity after the eight counts, which older clients ignore. Its window opens only once at a time (the button or interact key closes it again), its X has a solid click area, and it closes cleanly; before, every press opened another window on top, so the X seemed not to close it. Potions stored above the limit (an account can hold more than it, from before the limit applied) show as a red "+N" beside the full bar instead of being hidden; they can still be used or withdrawn, but no more of that potion can be deposited until it is back under the limit.
- **Tomb quests:** the closed sarcophagi are quests now, so the quest arrow leads to the nearest one; before, it had nothing to point at until a sarcophagus opened.
- **Shadows on the GPU renderer:** every shadow was drawn as a hard-edged black oval with an uneven alpha (0.01 to 0.3 across its corners), ignoring its colour and gradient. They are now drawn like the software renderer draws them: the shadow's colour, half opaque in the middle and fading evenly to nothing at the edge (`Renderer.as`, `Graphic3D.as`). Coloured shadows, such as candle and brazier glows, now show their colour. Client only.
- **Tomb Defender loot:** a misplaced bracket put its whole loot table (Potions of Life, Tomb key, Ring of the Pyramid, Tome of Holy Protection) inside its winter-only drop, so outside winter it dropped nothing.
- **Random dungeon layouts** (`Shared/terrain/DungeonGenerator.cs`): a world with `<RandomLayout>` in `Dungeons.xml` gets a new layout for every instance, built from the rooms of its own map. The map is cut into rooms (wide areas, with their monsters, objects and walls) and corridors (passages up to `passage` tiles wide); the rooms are joined again in a random tree with straight corridors `corridor` tiles wide. The spawn room is the root and each room holding a `<Goal>` object ends a branch, about 60% of the other rooms lie on the way to the goals and the rest become side rooms. Each instance picks a size: small 30% of the time (40% of the other rooms, a random pick), medium 60% (65%) and large 10% (all of them, the old size); the `small`, `medium` and `large` attributes change those shares, and small and medium layouts keep the corridor gaps to 6 tiles at most. The spawn and goal rooms are always used, and the world server logs the size it picked ("Random layout for ..."). Each room is used at most once; monsters standing in the old corridors move into a room next to them, while corridor decoration is left out. If no layout fits in 40 tries, or anything fails, the fixed map is used. Background walled in on every side (the hollow middle of a block of wall) is filled with wall, so it doesn't show as a black hole.
- **Boss portals in dungeons** (`World.BossDied`, `DropPortalOnDeath`, `DoomedRuntime.DeathPortal`): portals a monster drops in a dungeon (the Realm Portal, Elder dungeon portals) no longer close; only those dropped in a realm keep their timeout, so realms don't fill up with portals. And every dungeon has a way back: when a quest monster dies in a dungeon (not arenas or Zombies), two seconds later, if no quest monster is left and no Realm Portal is open, one opens where it died (logged as "... left no realm portal"). This covers bosses with no realm portal of their own (Septavius, Stheno, Dreadstump, Thessal and others) and bosses spawned by a controller, which `DropPortalOnDeath` skips.
  - Enabled for the Tomb of the Ancients (the boss room is the hub and each of the five sarcophagus rooms ends a branch), Pirate Cave, Spider Den, Snake Pit, Forest Maze, Undead Lair, Abyss of Demons, Toxic Sewers, Woodland Labyrinth, Ocean Trench and Ancient Ruins.
  - Left fixed: Mountain Temple (Daichi moves to fixed map positions), Parasite Chambers (its blockades seal the rooms from the corridors), Cave of a Thousand Treasures (the boulder corridor would be lost), and the Mad Lab, Crawling Depths and Davy Jones' Locker (their bosses sit behind doors and walls their scripts open). Open maps (Sprite World, the Hive, the Ice Cave) don't split into rooms, and the multi-stage dungeons (Oryx's Castle, Lost Halls, the Shatters and so on) depend on their layouts.
- **Rolling world server updates:** the world proxy (`tools/world_proxy.py`) owns port 2051 and forwards each connection by the game id in its Hello: world ids from 1000000 belong to world server slot a, from 2000000 to slot b (`serverSettings.worldIdBase`), anything else goes to the active one. `tools/rolling_restart.py deploy` starts a new world server beside the old one, which then hands its players over at their next Nexus or realm trip (`GameServer.DrainTick`; `Client.Reconnect` sends them to the Nexus with no key, so the proxy picks the new server); see "Updating the world server without kicking anyone" above. The proxy passes on each player's address (message 250, `PROXY_INFO`), and a world server behind it listens on 127.0.0.1 only (`serverInfo.listenPort`). A login whose account another world server still holds asks it to sign the account out (Redis channel `Kick`) and waits up to 5 seconds before "Account in Use". The account server lists world servers that share an address and port as one. A new world server is in `state.json` (`starting`) before the tool waits for it, and is stopped again if it doesn't come up, so a failed start never leaves an untracked server on a slot; scheduled restart requests are retried (the server asks every 30 minutes) and logged rather than dropped silently. The tool watches a world server it has just started through its own process handle (`Popen.poll`): on Linux, `/proc/<pid>/cmdline` can read empty while the new process is still turning into dotnet, and on 2026-10-18 that made a deploy give up on a server that was starting fine and leave it running outside the state.
- **Encrypted connections** (Rust desktop client, gateway, world proxy, world server): an account address ending in `/api` makes the desktop client sign in over https and play over wss through the site's Caddy and gateway, as the browser client does (see "Encrypted connections" under "Running on a VPS"). `BSK_CA=<pem>` adds a test CA. The gateway's `--forward-address` sends the player's address as a PROXY_INFO before the game bytes; `world_proxy.py` takes one from this machine in place of its own and drops one from anyone else, and the world server takes a PROXY_INFO only as a connection's first message, so a player can't choose the address IP bans see. The gateway also no longer passes on an `X-Forwarded-For` a player sent it directly.
- **Deploys that kick nobody** (gateway, deploy, Rust client, bot): the gateway runs in three systemd slots behind Caddy's health checks and hands over on SIGUSR1 (`/healthz` turns 503, it keeps its open bridges and exits when they've closed, 3 hours at most); it also holds account requests for up to 20 s while the account server restarts. `install.sh` starts a new gateway slot instead of restarting the gateway, restarts the account server only for its own changes, replaces Ubuntu's Caddy 2.6.2 (which closes every WebSocket on a reload) with Caddy's own 2.7+ package and sets `stream_close_delay`, and keeps needrestart off the game's services (see "Deploys that kick nobody" under "Running on a VPS"). The Rust client goes back to the Nexus by itself when its connection drops in the game without the server refusing it (a Failure), retrying "Account in Use" for about two minutes; `bsk-bot` follows Reconnects instead of exiting.
- **Sign-in checks** (account server): `/char/list` no longer makes a throwaway guest list (account 0) for an email it doesn't know; it answers "No account uses that email", and a wrong password "Wrong password". Registering and changing a password need at least 6 characters.
- **Guest accounts** (account server, Rust client): `/account/guest` makes a real account with a made-up login (`guest-<32 hex>` and a random password, `guest` flag set) and a unique name ("Guest" and four random letters, so the world server lets it in and finds it by name). At most 10 an hour per address and 300 an hour in all; behind the gateway or Caddy the address comes from `X-Forwarded-For`, which only a request from this machine may set. The client keeps the login on the device (`guest.txt` in `betterSkillys-rs`, or local storage), apart from "Remember me", and Play on the main menu uses it. Guests play like anyone else, except that Nexus merchants don't sell to them (an upstream rule). `/account/register` with the guest's `guid` and `password` turns the guest's account into a normal one (`RenameUUID`, `RenameIGN`), keeping its characters, vault and fame. A guest whose device forgets the login loses that account; unused guest accounts stay in Redis.
- **Password storage and guessing limit** (`Shared/database/Database.cs`): passwords are stored as salted PBKDF2-SHA256, `pbkdf2$<iterations>$<salt>$<hash>` in the `logins` hash (100,000 iterations, about 15 ms a check; the servers check the password on every request). Upstream stored an unsalted MD5 of the password and of its first half, which a leaked `logins` hash gives away at once. Old hashes still work, and an account's hash is rewritten in the new form the first time it signs in. After 10 wrong passwords for one account within 15 minutes (counted in Redis key `loginFails.<EMAIL>`, by the account and world servers together), sign-in is refused for the rest of the 15 minutes, "Too many wrong passwords"; a right password clears the count. Delete the key to unblock an account early.
- **Enchanted Keys, mystery boxes and items that don't work yet** (`Player.MysteryBoxes.cs`): the `Exchange` effect now gives its item (an Enchanted Key's Mystery ST Crate, the Tinkerer's token stacks' rewards) into the first free inventory slot, or the gift chest when the inventory is full. Before, it only said "Exchange is not yet implemented", so an Enchanted Key opened its dungeon but never gave the crate it was priced for. `MysteryDyes` gives one item from a pool: the Mystery ST Crate by weight out of 1000 (each of the 12 ST pieces it lists 15, Char Slot and Vault Chest Unlockers 30, Ambrosia 20, ST Shards x5/x3/x2/x1 50/80/110/140, Superburger 60, Golden Lucky Clover 40, Lucky Clover 100, Loot Drop and Loot Tier Potions 80; its "Golden Clover" is the Golden Lucky Clover), the Mystery ST Chest any ST set piece (an id ending in `ST<digit>`), and the two shop Mystery Dyes any clothing or accessory dye. An item with an effect the server can't carry out (`UnlockPetSkin`, `CreatePet`, `TeleportToObject`, `MysteryPortal`, `KillRealmHeroes`, an `Exchange` to an unknown item, or `MysteryDyes` on any other box, such as the Mystery ST Skin or the goodie bags) is refused before it's spent, "This item doesn't work yet."; before, it disappeared for nothing. The Rust client's tooltip names the item an `Exchange` gives.
- **Monsters left alive that shots couldn't hit** (Rust client, world server): the client decides what its shots hit, and when it reckons a shot kills a monster it marks it dead until the server removes it: its shots pass through and its own shots are dropped (`GameObject.dead_`, as in the AIR client). When the server didn't kill it, it stayed that way for good: alive, unhittable by shots and silent, until an ability the server resolves (a Necromancer skull, poison) finished it. The client now takes a kill the server hasn't confirmed back after 1 s (`UNCONFIRMED_KILL_MS` in `client-rs/crates/world/src/combat.rs`). The server's `Player.EnemyHit` also kills at 0 health, as `Enemy.Damage` does; it killed only below 0, while the client counts 0 as a kill, so a shot leaving a monster on exactly 0 always caused it. Other ways the two disagree remain and now only cost a second: the monster turning invulnerable or healing before the shot arrives, and damage rolls that drift apart once the server refuses one shot (rate of fire, shot count or bullet id in `PlayerShootHandler`) the client already rolled for. Measured on a test stack: 3 of 8 Pirates left on 0 health before, none after; with drifted rolls, 2 of 8 mispredicted kills were shot again and killed a second later.
- **Damage rolls kept in step** (world server, Rust client): the client and the world server roll each shot's and each ground hit's damage from one shared random sequence (`Random` in `client-rs/crates/world/src/random.rs`, `ClientRandom` as `Client.Random` on the server, seeded on MapInfo), and the client rolls before the server decides. `PlayerShootHandler` returned before `PlayerShoot` rolled when it refused a shot (rate of fire, shot count, bullet-id desync for non-admins), so after one refusal every later damage number and kill prediction was off for the rest of the connection. A refused shot now uses up its roll (`Player.SkipShotRoll`), and so does a refused ground hit (`ForceGroundHit` rolls before its Paused/Invincible check); the server's own ground-damage timer (`GroundEffect`, before the client's first GroundDamage) no longer draws from the shared sequence. The commonest refusal came from abilities: ability shots shared the weapon's rate-of-fire clock and per-time shot count, so using one while firing refused the ability's shot or the weapon's next volley. The ability slot now has its own clock, checked against the item's `Cooldown` as the client waits it out, and its own count (`Player.Shoot.cs`). The Rust client also applies the attack multiplier to ability shots, as `PlayerShoot` does (the AIR client leaves it out), so their numbers match. Measured on a test stack (Archer, Double Bow and Magic Quiver, firing with Space every 2 s, non-admin): before, 1 ability shot refused for count and 18 weapon shots for rate, and 416 of 433 accepted shots then rolled differently on the two sides; after, nothing refused and 452 of 452 matched, and with every 10th shot refused on purpose 407 of 407 still matched. Each side helps alone: an old client keeps working with the new server, only its ability numbers stay low.
- **Nexus Support Bot** (`worlds/impl/NexusGuide.cs`): a Support Bot (Doomed Realms' pet sprite, its pet behaviour cleared, invincible) stands beside the spawn at (46.5, 46.5), left of the Armoire. It greets each player who comes within 5 tiles (/gear, /realm and /godlands; again after 5 minutes) and says the next of ten new-player tips every 45 s to players within 15 tiles. Every 10 minutes, if every gift of the last wave is dead, it puts out 100 XP Gifts (0x0940, what an admin's `/spawn 100 xp gift` makes: 600 HP, no attacks, the usual kill experience), one to a tile on the open plaza tiles 3.5 to 10 tiles from the spawn's middle. Between tips (every other line) it says how many gifts are left and when the next check is (`GiftNews`). Every 10 minutes too, 5 minutes after the gift checks, it opens a portal to a random dungeon at (48.5, 49.5), replacing the last one, and closes it once its dungeon is cleared: a quest monster died there (`World.QuestKills`, counted in `BossDied`) and none has been left for 15 s, or the dungeon closed with none left (`CheckCleared`; it logs "Nexus: opened a ... portal" and "... was cleared; its portal closed"). Players inside stay either way: a dungeon lives while it has players, whatever happens to its portal (`DungeonPortals`: the regular dungeons realm monsters drop and a few like them, Pirate Cave to Mountain Temple; a portal whose dungeon has no world is skipped). One tip gives the GitHub link (github.com/Meapy/Do-Damage). The first wave comes when the Nexus opens. World server only: both objects are in the client's XML already.
- **/godlands from anywhere:** outside a realm it sends the player to the busiest realm that isn't full and, once they're in, to a random free Godlands tile (`GodlandsCommand.PendingArrivals`, checked in `RealmWorld.UpdateLogic`, good for a minute). Before, it only worked inside a realm.
- **Arena defeats** (`Player.Death`, `World.DefeatToNexus`): in a world with `<DefeatToNexus/>` in its XML (or `DefeatToNexus = true` in its class), a lethal hit (shot, contact, ground, `/kill`) sends the player to the Nexus at full HP and MP with a chat line ("You were defeated in the arena..."), the way the test world and resurrecting items do: the character, its gear and fame stay, and there's no grave, death record, Legends or Recent Deaths entry, fame bonus or announcement. It comes before a Resurrect item, which isn't used up. On for the betterSkillys arena (`/arena`, `Worlds.xml`), the Admin Arena (`Dungeons.xml`, an empty room for admin-run fights) and Doomed Realms' Battle Arena and Free Battle Arena (`DoomedBattleArena`, whose world XML is generated). Leaving this way is the same as leaving the arena any other way: the waves go on for the others, the wave reached still goes on `/leaderboard` once everyone has left, and the arena resets when it empties. The flag belongs to the world, so a portal opened inside an arena leads to an ordinary world. Not on for Battle for the Nexus (a boss dungeon with loot) or the Zombies minigames.

## Content ported from Doomed Realms

- **Commands:** `source/WorldServer/core/commands/doomed/DoomedRealmsCommands.cs` has 29 commands:
  - Everyone: `/swho`, `/getquest`, `/rules`, `/stats`, `/nothing`.
  - Supporter: `/d`, `/say`, `/stars`.
  - Admin: `/level`, `/summon`, `/god`, `/ci`, `/fame`, `/gold`, `/hp`, `/mp`, `/att`, `/def`, `/spd`, `/dex`, `/vit`, `/wis`, `/addeff`, `/remeff`, `/osay`, `/kill`, `/rank`, `/ip`.
- **Items and pets:** `python tools\port_doomed.py --dr "..\Doomed Realms\Doomed Realms"` regenerates them:
  - About 390 items and dyes, 122 pets and their projectiles, written to `custom/DoomedRealms.xml` for both the server and the client.
  - Type codes are 0xD000–0xDFFF. `tools/doomed/types.json` keeps them stable, because characters store items by type.
  - Sprites use copies of the Doomed Realms sprite sheets, registered with a `dr` prefix (`client/src/kabam/rotmg/assets/doomed`).
  - Remote textures are saved in `tools/doomed/remote`.
  - Pets whose id is already used in betterSkillys get a " (Doomed)" suffix and keep their display name.
  - Pet behaviour is in `BehaviorDb.DoomedPets.cs`: PetFollow, plus PetHeal and PetStrike (melee and thrown attacks) with the Doomed Realms amounts and cooldowns. Pets that fired projectiles use PetShoot: the same projectiles, counts and spreads, sent as the owner's server shots (`ServerPlayerShoot`), so the owner's client reports the hits and the damage is the owner's. `ServerPlayerShoot` has a trailing projectile-type byte for this (older clients ignore it and draw projectile 0).
  - Boss pets: a pet with a betterSkillys boss's sprite (a `<Quest/>` monster with projectiles, or one in `EXTRA_BOSSES`) gets that boss's projectiles (without their condition effects) and a PetShoot for each distinct Shoot in the boss's behaviour (up to 4, at most one volley per 300 ms). `port_doomed_pets.find_bosses` matches them; the script prints "<pet> shoots like <boss>".
  - The Rust client draws pets' shots (a shot whose container is a Character, not a weapon) smaller and see-through, other players' pets' fainter than your own, and their hit sparks as faint: Options, Visuals, "Pets' shots" Normal / Subtle (default: 60% size, 55% or 35% opaque) / Hidden (`options::PetShots`). Only the look changes: hits, damage and the server are as before.
  - The port script writes each pet's damage per second on one enemy and its healing onto the generators' `PermaPet` activates (`petDps`, `petHp`/`petHpMs`, `petMp`/`petMpMs`); the Rust client's tooltip shows them.
  - Doomed Realms items are kept out of the random tier drops.
- **Chat:** public chat no longer needs 2 stars (`PlayerTextHandler.cs`).
- **Classes:** 13 Doomed Realms classes, generated by the same tool:
  - Mechanic, Ascendant, Summoner, Druid, Sharklord, Blademaster, King, Palemaster, Father, Adept and Monk.
  - Pirate and Snowman too. Their ids are "Pirate (Doomed)" and "Snowman (Doomed)" because betterSkillys uses those names for monsters, but they show as Pirate and Snowman.
  - Their abilities are in `Player.UseItem.Doomed.cs`: PearlAbility, BuildTower, Summon, Halo, SamuraiAbility, TomeDamage, PartyAOE, MultiDecoy, MonsterToss and MiniPot. The Halo entity is in `Halo.cs`.
  - Turrets, summons and pearl substitutes are betterSkillys allies, with behaviours generated into `BehaviorDb.DoomedAllies.cs`.
  - Slot types 26 and 28 became 126 and 128, because betterSkillys uses them for pet eggs and lutes. The client names the Doomed Realms slot types in `ItemConstants.as`, which is also what lets their items be equipped.
  - Where Doomed Realms gave a class a starting weapon or ability that doesn't exist or doesn't fit its slot, the class starts with the lowest-tier item of that slot.
  - The class screens also read `DoomedRealms.xml`, the class grid adapts to the number of classes, and class names use DisplayId.
- **Dungeons:** 31 Doomed Realms dungeons plus its shop, generated by the same tool:
  - 13 Elder dungeons: Pirate Cave, Draconis, Abyss, Snake Pit, Spider Den, Sprite World, Tomb and Undead Lair, plus Manor, Ocean Trench, Jungle, Beach Zone and Davy Jones' Locker.
  - Doomed Realms made keys, portals and monsters for those last five but never their maps. Every Elder dungeon but Draconis (which has no betterSkillys map) is now built from betterSkillys' own map of the regular dungeon instead of Doomed Realms' (see the Elder bullets below); Elder Draconis keeps Doomed Realms' map.
  - Doomed Cellar, Hash Cellar, Doom Crystal, Forgotten Island, Zombies, Zombies Minigame, Sheep Herding, Forest Sanctuary, Christmas Oryx's Chamber, Party Cellar, Kitchen, Secret, Battle Arena, Free Battle Arena, Underground Mine, The Gauntlet, Turkey Hunting Grounds and Admin Room.
  - `port_doomed_worlds.py` converts the `.wmap` maps to `.jm` in `source/Shared/resources/worlds/Doomed/` and writes `xml/custom/DoomedRealmsWorlds.xml`.
  - Their portals point at those worlds and their keys create the portals (CreatePortal). Monsters' death portals work as in Doomed Realms.
  - Admins can open any dungeon with `/dungeon <name>`; with no name it lists the Doomed Realms ones. Several of them (Battle Arena, Kitchen, Mine, Gauntlet, ...) have no other entrance.
  - Fixed from Doomed Realms: the Elder Beach Key opened the Elder Jungle.
  - **Elder stats:** Doomed Realms balanced its Elder monsters for its own much stronger characters (10 to 700 times the regular monster's health, 10 or more times its damage, defense in the thousands). The port gives every Elder monster twice its regular version's health, its defense + 10 and 1.3 times its projectile damage instead, and twice the experience (`<XpBonus>2</XpBonus>`, which also doubles the per-kill experience cap, so level 20 characters get it too; fame follows experience). An Elder monster is also a quest where its regular version is (`rebalance_elders` in `port_doomed.py`).
  - **Elder dungeons from betterSkillys maps:** `tools/make_elder.py` (run by `port_doomed.py` first) lists the Elder dungeons built from betterSkillys' own map of the regular dungeon (`DUNGEONS`). For each it generates darker "Elder <id>" copies of every ground and wall the regular map uses, with "Lit 1" to "Lit 6" versions of each walkable ground, onto the client's `ElderAssets8x8.png`, an "Elder <id>" copy, with the Elder stats, of every monster the regular map or behaviours use that Doomed Realms gave no Elder version, and an "Elder <id>" copy of every other object the map places that the behaviours define (spawners such as the Snake Grate, so the Elder version spawns Elder monsters). `port_doomed.py` always ports the Doomed Realms objects these regular maps place (`make_elder.regular_map_objects`, e.g. Abyss of Demons' Native White Demon), since `make_elder.py` copies monsters from the last port's output: before, a regenerate alternated between two states. A monster newly placed on such a map takes two regenerates to get its Elder copy. Its outputs are `xml/custom/ElderGrounds.xml` and `ElderObjects.xml` (and client copies); type codes stay stable in `tools/elder/types.json`. `port_doomed_worlds.py` then builds each map from the regular one with everything swapped for its Elder version, braziers and their light, and a random layout where the dungeon lists goals.
  - **Elder Tomb of the Ancients:** rebuilt from betterSkillys' own Tomb: the same rooms, laid out anew for every visit (`<RandomLayout>`), with every monster swapped for its Elder version, the tiles for dark stone and the walls for aged gold Elder versions (`Elder Sandstone Tile` and co., `Elder Tomb Wall`; generated with their sprites by `tools/make_elder.py`, see below). Elder Braziers stand by the rooms' walls, corners first, about 9 tiles apart. The client has no lighting, so their light is baked into the map: the floor a brazier can see within 10 tiles becomes a lighter, warmer "Lit 1" to "Lit 6" version of its ground, brighter closer to it (`LIGHT_REACH` in `port_doomed_worlds.py`). It stops at walls and turns with the camera. The braziers themselves have no glow, which would show behind walls. Its monsters use the regular Tomb's behaviours (`BehaviorDb.Tomb.cs` builds both) with a quarter shorter cooldowns, and the Elder bosses drop the regular loot plus their Doomed Realms Elder drops. The `Elder Tomb Boss Anchor` drops the exit portal once the Elder bosses are dead; the regular Tomb's anchor also drops the Elder Tomb portal half the time. Before, the Elder Tomb's map was a rough path, its bosses had 1,000,000 HP, 5,000 defense and ten times the damage, and the regular anchor it used dropped the exit portal two seconds in. `port_doomed_worlds.py` builds it.
  - **The other Elder dungeons** (Pirate Cave, Spider Den, Snake Pit, Undead Lair, Abyss, Ocean Trench, Beach Zone, Jungle, Manor, Sprite World and Davy Jones' Locker) are built the same way from the regular betterSkillys map: darker tiles, braziers and their light, every monster swapped for its Elder version, and a new layout every visit where the regular dungeon has one. Each dungeon's behaviour file builds both versions (`PirateCaveBehaviours(db, elder)` and so on, with the helpers in `BehaviorDb.Elder.cs`): the Elder version uses the regular behaviours with a quarter shorter cooldowns and spawns Elder minions, and every Elder monster drops its regular loot plus its Doomed Realms Elder drops. The regular boss drops its Elder dungeon's portal half the time. Before, these used Doomed Realms' maps and behaviours, written for its far stronger characters, and many Elder monsters dropped nothing. Walls a behaviour opens by name (Davy's doors) keep their regular look so their scripts still find them.
  - The Undead Lair map's invisible Doomed Realms "Septavius Summoner" dropped a second Realm Portal (an Elder Abyss portal in the Elder Lair) when Septavius died; it now removes itself, since Septavius drops the portals.
- **Minigames:** `source/WorldServer/core/worlds/impl/doomed/DoomedWorlds.cs` runs the worlds that had code of their own; `WorldManager.CreateNewWorld` asks it first.
  - **Battle Arena / Free Battle Arena:** their portals stand in the nexus, under the Guild Hall portal (paid) and the Vault portal (free). Everyone who enters joins the same game until its countdown ends; after that, or once it closes, the portal opens a new one. Each game starts with a 1-minute countdown, then waves of gods and a boss that gets harder every 5 waves. The paid arena gives fame every wave. When everyone has left, the wave reached goes on `/leaderboard`, stored in Redis as the sorted set `doomed.arenaLeaderboard`.
  - **Zombies Minigame:** defend the tower; each zombie that reaches it damages it. When it falls, everyone gets the fame pot.
  - **Sheep Herding:** 2 minutes to herd sheep into the pen for fame; a black sheep empties the pot.
  - **Zombies:** endless waves over the whole map. Doomed Realms aimed most of its zombies outside this map and never spawned Crawlers; here every zombie lands on open ground, Crawlers included, with a Zombie of Draconis every fifth wave (its unused "special zombie").
  - Monsters report to their world through the WorldEvent behaviour. Destructible walls don't count as monsters.
- **Monsters:** `source/WorldServer/logic/doomed/DoomedRuntime.cs` rebuilds the Doomed Realms behaviour engine on betterSkillys:
  - It covers its composites (If, Cooldown, QueuedBehavior, keys, states), attacks, movement, spawning, events (death, hit, chat) and loot.
  - `port_doomed_monsters.py` copies the 270 monster definitions (191 ported monsters, 79 betterSkillys ones) from Doomed Realms into `DoomedMonsters.cs` nearly verbatim, with object types looked up by id.
  - A behaviour written by hand for a monster replaces the generated one: generated monsters register through `InitDoomed`, which steps aside for an `Init` of the same monster (the Elder Tomb's monsters use this).
  - **betterSkillys monsters without a behaviour** take the Doomed Realms one of the same name: `port_doomed.py` lists every enemy no hand-written `.Init` covers (`port_doomed_monsters.handwritten_behaviours`) that Doomed Realms gives a behaviour, and generates it like a ported monster. That gave 79 monsters their behaviour, which upstream never wrote: the classic realm heroes (Red Demon, Phoenix Lord and its egg and reborn form, Lich, Ent Ancient, Ghost King, Oasis Giant, Deathmage, Turkey God) and their minions (Imps, Demons, Birdmen, ghosts, skeletons, mummies, golems and their satellites, swarms, Jungle Men, ...), the Abyss lava bomb, Bonegrind the Butcher's kitchen, Evil Chickens and the tutorial guns. Before, they stood still and never shot. The engine gained what they use: AngleMove, ChasingGroup, Escaping, Retracting, StrictCirclingGroup, HealGroup, TutorialTower, GolemSatelliteSpawn/GolemSatellites (which look for the "Golems" group: Doomed Realms looked for "Golem", so its satellites always left after 30 s), MonsterSetPiece (the lava bomb's TempLava patterns), ChefTaunt ("Other" conditions, now checked every tick) and IncLoot. Elder monsters `make_elder.py` made with no behaviour of their own share their regular version's (Elder Ice Sprite, Elder Magic Sprite, Elder Native White Demon).
  - **Their loot:** Doomed Realms gave many of these no loot or only low-tier items. `logic/doomed/DoomedExtraLoot.cs` (written by hand) adds betterSkillys drops, which `DoomedSetup.Drops` puts beside the generated ones: the heroes drop tiered gear like the Actual Lich and Ent Ancient, stepping up with their level (Deathmage T5-6, Oasis Giant and Ghost King T6-7, Phoenix Reborn and Red Demon T7-8, with rings, abilities, a stat potion and potions), and the minions a low-tier item now and then and potions. The Phoenix Lord and its egg drop nothing: the Phoenix Reborn they turn into does.
  - Elder monsters Doomed Realms gave no behaviour use their regular version's behaviour, which spawns the Elder versions of its minions (e.g. Elder Lord Ruthven). Where only betterSkillys has a behaviour for the regular version, the Elder one shares it (`DoomedAliases`, applied in `BehaviorDb.Initialize`).
  - Loot becomes betterSkillys drops with the same chances; soulbound bags need the same damage, as a share of the monster's HP.
  - Objects whose remote sprite no longer exists borrow the sprite of a betterSkillys object with the same name (e.g. Oryx the Mad God 5), or of `SPRITE_DONORS` in `port_doomed.py` (the Elder Beach portal uses the Beachzone portal's).
- **Economy:** `port_doomed_economy.py` generates `core/commands/doomed/DoomedEconomyData.cs` from Doomed Realms' forge recipes, sell prices and shop lists; the commands are in `DoomedEconomyCommands.cs`.
  - `/forge <slot> <slot> ...` (inventory slots 1-8, backpack 9-16), then `/yes` or `/no`. 256 recipes. Wearing a Forge Amulet halves the price and unlocks the 3 advanced recipes, and the amulet breaks when used. `/forgelist` shows what you can make, `/forgelist <item>` searches, `/forgelist all` lists everything.
  - `/sellfame <slot> ...` sells items for fame (Doomed Realms' `/sell`; betterSkillys' `/sell` is its market).
  - `/shop <name>` opens a shop whose merchants sell one list: abilities, weapons, armor, rings, consumables, dyes, keys and pets for fame, and Doomed Realms' donor shops for gold. With no name it lists the shops. Doomed Realms' default shop sold five starter weapons; here each of its store lists is a shop. Its pet generators had no price; they cost 1250 fame like betterSkillys' own.
  - `/arenas` lists running arenas, `/leaderboard` the best arena waves, `/gleaderboard` the guilds with the most fame.
  - `/afk` and `/pause` toggle Paused when no monster is near (not in arenas).
  - Everyone: `/gear` equips a mid tier item in each of your class's slots, about half way up the regular tiers (T7 weapon, T8 armor, T4 ability and ring; never above T8). Doomed Realms gave it to supporters; here it's starter gear for all, so it's limited: once per character (Redis set `doomed.gearClaimed`, `<account id>.<character id>`), in the Nexus, below level 20, and it only upgrades (untiered items and items as good or better stay), moving the items it replaces to the backpack, which needs room for them. It also puts an XP Booster and a Loot Drop Potion (both soulbound) into the backpack, counted in that room; a character whose gear is already better still gets them. Admin: `/set` (the same, with none of those limits), `/giveeff <player> <effect>`, `/message <title> <text>`, `/killallx <name>` (kills with experience), `/grave [name] [0-8]`, `/dragons` (Admin Room), `/openarena [free]` (a Battle Arena portal for 30 seconds; betterSkillys' `/arena` is its own arena).
  - Fixed from Doomed Realms: a recipe accepted the same item twice for two different ingredients, and the forge used to take the items even if they changed between `/forge` and `/yes`.
- **Not ported:** Doomed Realms commands betterSkillys already has (`/who`, `/tell`, `/visit`, `/ban`, `/rename`, `/vault`, guild commands, ...), its multiple backpacks (`/b`, `/bp`), and its group/solo joining, which it had switched off.
- Remote sprites that are a strip of seven frames but are only used as an item (the Elder Draconis Key and three others) showed the whole strip; they now show the first frame (`port_doomed.py`, `DoomedAssets.as`). Client only.

### betterSkillys fixes made for the port

- `World.RemoveFromWorld` now removes every Enemy from the enemy list, as `AddToWorld` adds them. Before, monsters without `<Enemy/>` (like the herding sheep) stayed in it after leaving the world.
- `NexusMerchant` returns to any `IMerchantWorld` (the nexus or the Doomed shop) instead of assuming the nexus.
- Earlier dungeon ports put map regions after Store_9 one slot off (Doomed Realms has 9 store regions, not 10); the maps that use them (Elder Undead Lair, Party Cellar, Sheep Herding, both Zombies maps) are regenerated.
