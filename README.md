# Do Damage

A Realm of the Mad God private server (C#, .NET 8) with a client written in Rust that runs on the
desktop and in the browser, for a small group of players. Play at [dodmg.com](https://dodmg.com).
The code keeps its old name, betterSkillys; the original Adobe AIR client (ActionScript 3) is
still here but no longer used.

This repository stands on its own, with its history condensed to one commit per milestone
([SETUP-LOCAL.md](SETUP-LOCAL.md), "Code history"). It started from [runes02/betterSkillys](https://github.com/runes02/betterSkillys) (a better
NR-CORE/Skilly's 7.0, built on TK-Reborn, with the XMLs of RotMG 1.0.4.0). On top of that it has:

- [Aurusenth/betterSkillys](https://github.com/Aurusenth/betterSkillys) merged in: item tiers and
  glow, an admin class, the Admin Arena and Shaitan dungeons, Battle for the Nexus, a new nexus
  and vault, and more.
- The content of Doomed Realms, another private server: its items, pets, classes, dungeons,
  minigames, commands and economy.
- Many dungeons made playable, random dungeon layouts, rebuilt Elder dungeons and a Nexus Bazaar.
- Its own endgame: Realm 2, the Oryx chain up to Oryx 5, the Lost Halls with the Cultist Hideout
  and the Void, the Divine Forge and Workbench with imprints, quests and achievements, and the
  Arena.
- Server networking and AIR client rendering made much faster.
- The Rust client.

[SETUP-LOCAL.md](SETUP-LOCAL.md) lists every change in detail.

## Play and run

The live game runs on a Linux VPS at dodmg.com: Caddy serves the website and browser play over
https, a gateway carries the game over WebSocket, and Redis, the account server and the world
servers run as systemd services.

| Part | Port |
|---|---|
| Caddy (website, `/play/`, `/api`, `/ws`) | 80, 443 |
| Gateway slots | 8095-8097, VPS only |
| Account server (`source/App`) | 8080 |
| World proxy and world servers (`source/WorldServer`) | 2051 |
| Redis | 6379, VPS only |
| Discord bot (release posts, server status, slash commands) | none |

```
python deploy\deploy.py                                                          # deploy; kicks nobody
python tools\build_client_rs.py --account https://dodmg.com/api --publish        # update players' clients
python tools\build_client_rs.py --account https://dodmg.com/api --package        # dist\Do-Damage-windows.zip
```

[SETUP-LOCAL.md](SETUP-LOCAL.md) covers the VPS ("Running on a VPS"), deploys that kick nobody, and
running a copy on a Windows PC (`Run Server.bat`) or an isolated test stack
(`tools\testclient\iso_stack.py`).

## Developing locally

`Dev Test.bat` (Windows) runs everything on your own PC, away from the live game: test servers
on their own ports and the Rust client signed in to them, or the website with its wiki.
Double-click it for a menu of the four, or give one from a terminal:

```
"Dev Test.bat" all       # build and start the test servers, then build and open the client
"Dev Test.bat" server    # rebuild and restart the servers only (sign in again in your client)
"Dev Test.bat" client    # build and open the client only
"Dev Test.bat" site      # build the website and wiki and open http://127.0.0.1:8000/wiki/
```

- **Servers:** an isolated stack (`tools\testclient\iso_stack.py`, slot 1): Redis on 6381, the
  account server on 8082 and the world server on 2053, built from your checkout into
  `build\iso1`. The first start copies `source\Redis\data` if you have it; on a fresh clone the
  stack starts empty, so register an account in the client. Its data is kept between runs.
- **Client:** `cargo run` in `client-rs`, pointed at the test account server
  (`--account http://127.0.0.1:8082`). Close it before restarting the servers.
- **Website and wiki:** `tools\build_site.py` builds `website\` and the wiki (generated from the
  game's XML, behaviours and maps by `tools\build_wiki.py`) into `build\site`, which is then
  served on port 8000. Run it again after a change to see it; Ctrl+C stops the server.

It needs the [prerequisites](#prerequisites) below (not the AIR SDK). Admin commands on your
test account: set `admin` on its `account.<id>` in the stack's Redis
(`source\Redis\redis-cli.exe -p 6381`), as in [source/AGENTS.md](source/AGENTS.md).

## The Rust client

`client-rs/` is a complete rewrite of the AIR client. It uses the same art and the same server
protocol, so it works with these servers unchanged. It does everything the AIR client does
([client-rs/PARITY.md](client-rs/PARITY.md) lists each feature) and adds:

- **Any window size:** the HUD lays itself out, with crisp text, a zoomable minimap with
  player menus, and nearby players' health, magic and gear on hover.
- **Browser play:** the web build runs through `bsk-gateway`, which serves the page and bridges
  the browser to the servers.
- **Speed:** over 1,000 frames a second at 2560×1440 (wgpu: DirectX 12, Vulkan, Metal, WebGPU or
  WebGL2).
- **Condition effects as the AIR client shows them:** icons and rising names over players and
  monsters, blurred, dark or grey views while drunk, blind or paused, and so on.
- **Options:**
  - key bindings;
  - world zoom (90% to 130%) and camera modes;
  - frame rate (vsync, unlocked or capped);
  - particles, shadows, health bars, sound and music.

  They are kept across versions in `%APPDATA%\betterSkillys-rs\settings.txt`, and the login in
  `login.txt` beside it.
- **Updates itself:** players get the zip once; after that, each version you publish downloads
  in the background and takes over at their next portal. A Changelog window (and
  `client-rs/CHANGELOG.md`) says what changed in each version.
- **The rest of the game:** the marketplace, trading, guilds, potion storage, the quest log,
  the vault window, the Divine Workbench, NPC windows, emotes, the graveyard, borderless
  fullscreen, and test flags for scripted runs.

```
cd client-rs; cargo run                                  # desktop, for development
python tools\build_client_rs.py                          # build\client-rs\native
python tools\build_client_rs.py --web                    # build\client-rs\web, served by bsk-gateway
```

See [client-rs/AGENTS.md](client-rs/AGENTS.md) for the gateway, testing and layout.

## Repository

| Path | What |
|---|---|
| `source/` | The servers: `App` (account server), `WorldServer`, `Shared` (game data, XMLs, maps in `resources/worlds`), `Redis` |
| `client/` | The AIR client (`src/`, ActionScript 3) |
| `client-rs/` | The Rust client: `app` (the game), `crates/` (renderer, protocol, session, data, world, net, assets), `gateway`, `bot` |
| `tools/` | Build scripts, the Doomed Realms porting tools, the Elder dungeon generator, the Nexus Bazaar builder, and `testclient/` (isolated test servers and a test client) |

The working notes for each part are [AGENTS.md](AGENTS.md), [source/AGENTS.md](source/AGENTS.md),
[client/AGENTS.md](client/AGENTS.md) and [client-rs/AGENTS.md](client-rs/AGENTS.md).

## Prerequisites

- .NET 8 SDK (the servers)
- AIR SDK 32, unzipped into `tools/airsdk/` (the AIR client)
- Rust stable, with the `wasm32-unknown-unknown` target and `wasm-bindgen-cli` for the browser
  build (the Rust client)
- Python 3 (the scripts in `tools/`)
- Redis is included in `source/Redis`.

## What changed here

The highlights; [SETUP-LOCAL.md](SETUP-LOCAL.md) has the rest.

### Gameplay and content

- **Realm 2:** a second, larger realm always open from the Nexus, with its own lands, redrawn
  monsters and events, and waystones to travel between cleared lands.
- **The Oryx chain:** from a realm's close through Oryx's Castle, Chamber, Wine Cellar and
  Sanctuary to Oryx 4 in Oryx's Inferno and Oryx 5, the Ascended God, in Oryx's Elysium.
- **The Lost Halls, the Cultist Hideout and the Void:** the halls' marble rooms laid out anew every
  run up to the Marble Colossus; Malus and his cultists in their hideout; and the Void Entity,
  reached with Malus' Vial of Pure Darkness.
- **The Divine Forge and Workbench:** forging, shards, selling and imprints for gear, in one window
  (`/forge` from anywhere).
- **Quests and achievements:** daily, weekly and monthly quests, a daily login reward, and 22
  achievements with tiers, points and chat colours.
- **The Arena:** waves with group votes to start or skip ahead, shared chests, and half fame and
  loot; defeats send you to the Nexus.
- **Legends:** the top characters of the week, month and all time glow red and get a small loot
  boost.
- **Doomed Realms port:**
  - 13 classes and about 390 items, dyes and 122 pets;
  - 31 dungeons and its minigames (the Arena, Zombies, Sheep Herding);
  - 29 commands, the forge, fame selling and the shops;
  - its monster behaviour engine.
- **Elder dungeons:** rebuilt from this server's own maps, with darker tiles, braziers whose light
  is baked into the floor, Elder versions of every monster and a fresh layout every visit.
  Elder monsters are rebalanced to suit these characters.
- **Random dungeon layouts:** the Tomb, Pirate Cave, Spider Den, Snake Pit, Forest Maze, Undead
  Lair, Abyss, Toxic Sewers, Woodland Labyrinth, Ocean Trench and Ancient Ruins rearrange their
  rooms for every visit.
- **Playable dungeons:**
  - Ancient Ruins, Cursed Library (with the Corruption Phantom event), Fungal Cavern, Parasite
    Chambers, Secluded Thicket and the Tomb of the Ancients;
  - the Ice Cave and Inner Sanctum, and the Oryx's Castle endgame chain;
  - new fights for bosses that stood still (Mixcoatl, Belladonna, the Abyss Idols, Horrific
    Creation and others);
  - exit portals for final bosses, and portal sources for dungeons nothing led to.
- **Nexus:**
  - the Nexus Bazaar, a market island selling keys, potions, consumables, dyes and cloths for
    fame;
  - fountains that spray and heal;
  - the Arena's portal.
- **Fixes:**
  - 358 monster attacks that named missing projectiles;
  - objects without HP dying on their first tick;
  - buying character slots;
  - XP and loot boosters doing nothing;
  - the skin list showing every skin twice;
  - admin-only classes, admins trading and sharing bags;
  - potion storage upgrades.

### Performance

- **World server:**
  - a tick's messages leave together;
  - world threads sleep instead of spinning;
  - a stream corruption bug in large sends is fixed.
- **AIR client:**
  - rendering ported from the Doomed Realms client: ground chunks, tile culling, batching and a
    texture atlas;
  - fullscreen sees the whole screen;
  - almost no garbage per frame.

  In the nexus it went from 110 to 191 fps, and to about 78 fps at 4K with the whole view in
  sight.
- **Rust client:** about 1,100 fps at 2560×1440.

## Content status

What's finished and what's still to do, upstream's work and this repository's together.

### Done

- [x] From upstream: TK-Reborn cleaned up, XMLs up to date with RotMG 1.0.4.0, the damage
  counter, Potion Storage, the Marketplace, seasonal drop tables, animated emotes, Samurai and
  Bard.
- [x] The Rust client, on the desktop and in the browser, in place of the AIR client (and with
  it the AIR client's memory leak).
- [x] The live game at dodmg.com, with the website and its wiki; updates and deploys kick nobody.
- [x] Random dungeon layouts, each run small, medium or large; rebuilt Elder dungeons.
- [x] The Nexus rebuilt as a castle courtyard, with the Nexus Bazaar.
- [x] About 80 monsters that stood still now fight (Red Demon, Phoenix Lord, Lich, Ent Ancient,
  Ghost King, Oasis Giant and more), with loot to match; the Beer God can be seen.
- [x] Boss pets fire their boss's shots.
- [x] Arena defeats send you to the Nexus instead of killing you.
- [x] Realm 2, always open from the Nexus.
- [x] Quests, achievements and a daily login reward.
- [x] The Divine Forge and Workbench, with imprints.
- [x] A Discord server and bot: release posts, server status and slash commands.

### Dungeons and bosses

✮ finished but rough.

- [x] Garnet/Jade Statues, Killer Bee Nest, Mountain Temple, Toxic Sewers, Hive, Magic Woods,
  Ice Tomb, Tomb of the Ancients.
- [x] Ancient Ruins, Cursed Library, Fungal Cavern, Parasite Chambers, Secluded Thicket: each has
  behaviours for every monster, a boss with loot and an exit portal, and a portal source.
- [x] Abyss Idols, Horrific Creation, Mixcoatl, Belladonna: all have fights now.
- [x] The Oryx chain: Oryx's Castle, Oryx's Chamber, Wine Cellar, Oryx's Sanctuary, Oryx's Inferno
  and Oryx's Elysium, opened with incantations; and the Chicken Chamber.
- [x] Lair of Draconis and its Elder version ✮.
- [x] Manor of the Immortals and its Elder version: a whole house laid out anew each visit, and
  Lord Ruthven's full fight.
- [x] Lair of Shaitan, with a new arena, and the Avatar of the Forgotten King.
- [x] Ivory Wyvern ✮, Ice Cave / Inner Sanctum ✮.
- [x] Battle for the Nexus ✮: four bosses with loot; its key is sold in the Nexus Bazaar.
- [x] Lost Halls ✮: marble rooms laid out anew every run, each with its own monsters, up to the
  Marble Colossus (the Lost Sentry in Realm 2 and, now and then, Oryx 2 drop the portal).
- [x] Cultist Hideout ✮: Malus' followers and demons, then Malus, his four cultists, Molek and
  Balaam; a portal in the Lost Halls or a Cult Key opens it.
- [x] The Void ✮: the Void Entity, opened with Malus' Vial of Pure Darkness where the Marble
  Colossus fell.

### Still to do

- [ ] Every equipment item from RotMG 1.0.4.0 working: some items still can't be used.
- [ ] Court of Oryx: a map, but no world or monsters.
- [ ] The Nest: no map or world yet.

## Credits

- **Runes:** made betterSkillys.
- **Slendergo:** created TK-Reborn, which betterSkillys is based on, and helped update the XMLs to
  RotMG 1.0.4.0, among other things.
- **Zolmex:** animated emotes, wis-mod, and many GitHub repositories that went into betterSkillys.
- **Aurusenth:** item tiers and glow, the admin class, the Admin Arena and Shaitan dungeons,
  Battle for the Nexus, the new nexus and vault.
- **Doomed Realms:** the items, pets, classes, dungeons, minigames, commands and client rendering
  optimisations ported here.
- **MIKEEEEEEEEEE:** maps for Mountain Temple, Toxic Sewers, The Nest and Hive.
- **Evil:** behaviours for the Garnet/Jade statues, and pointers to other sources of behaviours.
- **MoonMan:** behaviours for Magic Woods and other entities.
- **animus:** the proxy tool used to rip maps from RotMG Exalt.
- **Boftheb:** helped rip the Fungal Cavern, Secluded Thicket, Lost Halls, Cultist Hideout and
  other maps.
- **Gummy:** the Lair of Draconis map.
- **nekoT:** betterAssets in the client.
- **Orb, GlassBBQ:** credited on TK-Reborn.
- **rusjaell, Aaron, TKROfficial, kevin, Comfrog, Dante Tyler:** work on TK-Reborn and
  betterSkillys; the condensed history names them as co-authors of each milestone.
