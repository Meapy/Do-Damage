# Server development

C# on .NET 8: `betterSkillys.sln` has the account server (`App`), the world server
(`WorldServer`), the shared library (`Shared`) and Redis. It's based on TK-Reborn. Read
[../AGENTS.md](../AGENTS.md) first for the shared rules, above all: **ask before restarting the
live servers**, because players get disconnected.

## Projects

| Project | What it is | Config |
|---|---|---|
| `App` | Account server (ASP.NET): login, character list, server list, web endpoints in `Controllers/` | `App/server.json` (Redis, port 8080) |
| `WorldServer` | The game: worlds, players, monsters, commands, networking | `WorldServer/wServer.json` (Redis, port 2051, public address, realm settings) |
| `Shared` | Game data (`resources/`), the Redis database layer (`database/`), shared types | |
| `Redis` | Bundled Redis 3.2 for Windows, `redis.local.conf` | |

## Build and check

```
dotnet build source\betterSkillys.sln -c Debug          # what Run Server.bat does
dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\wscheck   # while the live server runs
```

- **The live servers are on the VPS** (dodmg.com), installed under `/opt/bsk` by
  `deploy/deploy.py` (SETUP-LOCAL.md, "Running on a VPS"): the account server in `/opt/bsk/app`,
  the world servers in `/opt/bsk/build/live/<slot>`. The copies on this PC (`App/bin/Debug/net8.0`,
  `build/live/a` and `b`) are the old live servers, stopped since the move. To check that
  something compiles, build into another folder with `-o`, as above.
- **`Optimize` is on for `WorldServer` and `Shared` in Debug too**, because Debug is what
  `Run Server.bat` builds and runs.
- **To try a change in game,** use the isolated stack, `tools/testclient/iso_stack.py start`. It
  builds App and WorldServer from the current source into `build/iso`, on Redis 6380 (a copy of the
  live data), account 8081 and world 2052; `--slot N` adds N to each, and in a worktree pass
  `--data-from <main checkout>\source\Redis\data` or it starts empty. Play on it with the Rust
  client's scripted flags or the bot (client-rs/AGENTS.md, "Testing against the servers"), and
  stop the stack with `iso_stack.py stop` when done.
- **Logs** are in `build/live/<slot>/logs/wServer/<date>/info.txt` (and `error.txt`), and
  `App/bin/Debug/net8.0/logs/app/`. Ticks slower than 200 ms are logged as "Slow tick", and passes
  over players' messages slower than 100 ms as "Slow messages". Every 30 s each world with players
  logs "Tick stats" (`core/worlds/TickProfile.cs`): its ticks' work, the gaps between them (what
  players feel), the time per part of the tick, and the garbage collections; dungeons and vaults log
  their own. A garbage collection that stops every world for over 50 ms is logged as "GC pause"
  (`core/GcPauseLog.cs`). Measure with those.
- **Deploying a server change:** it goes out with the next release: merging the release's pull
  request into `main` deploys it (../AGENTS.md, Git, "Releasing"; ask the owner first: it changes
  what players run). Nothing the deploy does kicks anyone: a new world server goes in
  with a rolling deploy (see "Rolling deploys" below); a changed account server restarts, which
  pauses sign-ins for a few seconds (in-game players are on the world servers, and the gateway
  holds https account requests until it's back); a new gateway starts in a free slot while the old
  one hands over (SETUP-LOCAL.md, "Deploys that kick nobody"). A message change ships with the
  client, as before.
- **On the VPS:** logs are in `/opt/bsk/build/live/<slot>/logs/wServer/`,
  `/opt/bsk/app/logs/` and the journal (`journalctl -u bsk-world`, `-u 'bsk-gateway@*'`); Redis is
  `redis-cli -p 6379` there. `python deploy\deploy.py --status` shows the services, the world
  server slots and the gateway slots.
- **Stopping a world server** saves everyone on Ctrl+C (Windows) or SIGTERM (Linux): `GameServer.Stop`
  ends the loop and `ConnectionListener.Shutdown` disconnects each player, which saves them.
  A forced kill (`taskkill /F`, which `rolling_restart.py stop` uses on Windows) skips that.

## Rolling deploys (world proxy)

- **Layout:** `tools/world_proxy.py` owns the public port (2051) and forwards each connection to
  world server slot a (12051) or b (22051), on 127.0.0.1 (`serverInfo.listenPort`).
  `tools/rolling_restart.py` builds the slots into `build/live/<slot>` and keeps
  `build/live/state.json`, which the proxy rereads when it changes.
- **Routing:** by the game id in the Hello. Each slot has its own world id range
  (`serverSettings.worldIdBase`: 1000000 for a, 2000000 for b), so a player moving between
  worlds of the old server stays on it. The Nexus and other negative ids, new logins and unknown
  ids go to the active slot. Don't hand out fixed positive world ids: the proxy can't place them.
- **Hand-over (drain):** the tool writes `drain.request` (minutes allowed) into the old slot's
  folder. `GameServer.DrainTick` then moves its Nexus players at once, and `Client.Reconnect`
  turns any trip to the Nexus or a realm into a keyless Reconnect to the Nexus (`HandsOver`), which
  the proxy sends to the new server. At the deadline everyone left is moved; empty, it stops. It
  writes `drain.status` (players left) meanwhile, and stops the market sweeper.
- **Account locks:** the old server saves the character and releases the lock when the client
  leaves it, which can be just after the new server sees the Hello. `ConnectionManager` asks
  other world servers to sign the account out (Redis channel `Kick`) and waits up to 5 s.
- **Scheduled restart:** behind the proxy, `restartTime` writes `restart.request` instead of
  kicking; the proxy runs `rolling_restart.py deploy --reuse`. The server asks again every 30
  minutes until it's draining, so a failed restart is retried.
  Without the proxy (an isolated stack, `start.bat`) it kicks everyone 5 minutes after warning
  them; on Windows it then starts itself again by full path, the way it was started
  (`dotnet WorldServer.dll` on an isolated stack, which `iso_stack.py status` and `stop` find).
  On Linux, systemd starts it again.
- **Realms carry over:** each realm the Nexus opens keeps a snapshot in the Redis hash `realms`
  (`core/worlds/RealmSnapshots.cs`, `RealmManager.Snapshot.cs`), and a new server opens the same
  realms from it; `/uptime` reads `worlds:upSince`, which a hand-over keeps (`core/GameUptime.cs`).
  SETUP-LOCAL.md, "Realm state across restarts". New realm state worth keeping goes in the
  snapshot, and anything it can't redo leaves the realm as it was rather than failing.
- **Failed starts:** `start_backend` records a new server as `starting` in the state before it
  waits, and stops it (SIGTERM, or Ctrl+Break on Windows) if it doesn't listen in time or the
  tool is interrupted; a deploy stops a `starting` server a dead tool left. Never leave a world
  server running that the state doesn't list.
- **Client addresses:** the proxy sends message 250 (`PROXY_INFO`) with the player's address
  before the Hello. It's only taken by a server behind the proxy, before login.
- **Testing:** `iso_stack.py --slot N start --rolling`, then `rolling_restart.py --iso N deploy`
  (front port 2052+N, slots on 12052+N and 22052+N). The world servers of a test stack run as
  `dotnet WorldServer.dll`, never as `WorldServer.exe`.

## How the world server works

- **World threads:** each world runs on a world thread (`core/worlds/RootWorldThread.cs`) at 100 ms
  ticks. The thread sleeps until a player's message arrives or the next tick is due; it doesn't
  spin. `Program.cs` asks Windows for 1 ms timer precision.
- **Branches:** a world made from inside another (a dungeon from a realm, a vault from the Nexus)
  has no thread of its own: it's a branch, updated and fed its players' messages on its root's
  thread (`WorldBranch`), and it wakes that thread when its players send something
  (`World.SignalIO`). `World.Post` runs code on a world's thread.
- **New worlds:** build them with `WorldManager.CreateNewWorldAsync` from a world thread (portals,
  `/vault`, `/dungeon`): the map, random layout and entities are built on a pool thread, then the
  world is attached and the callback runs on the caller's world thread. Building one inline froze
  the realm and every dungeon under it for hundreds of ms.
- **Sending:** messages a thread sends during one pass of its loop are queued and sent together at
  the end (`BeginBatch`/`FlushBatch` in `core/connection/NetworkHandler.cs`). A message that
  doesn't fit in the send buffer goes first in the next send, so the order is kept.
- **Incoming messages:** ids are the `MessageId` enum in `networking/packets/MessageIds.cs`.
  Handlers are classes deriving from `IMessageHandler` in `core/net/handlers/`, found by
  reflection, so a new handler needs no registration (see `ViewRadiusHandler.cs`). An unknown id
  is logged and counted as packet spam, and more than 32 disconnect the player. The client's ids
  are in its `GameServerConnection.as`, so keep the two in step and deploy them together.
- **Players:** `Player` is split into partial classes in `core/objects/player/`
  (`Player.Update.cs`, `Player.UseItem.cs`, and so on).
- **What a player sees:** `Player.SightRadius`, 15 tiles by default and up to 40 when the client
  asks (message 88, `SetSightRadius`). Sight shapes are cached per radius, so a bigger view costs
  only the players who use it. `Player.VISIBILITY_RADIUS` is 15 and `MAX_VISIBILITY_RADIUS` 40.
  Enemies only tick in active chunks near players.
- **Worlds:**
  - `core/worlds/WorldManager.cs` creates them. Its default case asks
    `impl/doomed/DoomedWorlds.Create` first, for the Doomed Realms minigames.
  - World definitions are in `Shared/resources/xml` (`Worlds.xml`, `Dungeons.xml`,
    `custom/DoomedRealmsWorlds.xml`). Maps are `.jm` files in `Shared/resources/worlds/`.
- **Removing entities:** `World.RemoveFromWorld` removes any `entity is Enemy` from the enemy
  list, as `AddToWorld` adds them. Don't go back to checking `ObjectDesc.Enemy`: monsters without
  `<Enemy/>` (like the herding sheep) would stay in the list.
- **Merchants:** they return to any `IMerchantWorld` (the nexus or the Doomed shop).
- **Shooting:** in `PlayerShootHandler.cs`, the rate-of-fire and projectile-count checks apply
  only to players who aren't admins; the ability slot is timed by its `Cooldown`, apart from the
  weapon. The client rolls each shot's and ground hit's damage from the sequence it shares with
  `Client.Random` before the server decides, so anything that refuses one must still use up its
  roll (`Player.SkipShotRoll`), and the server's own rolls must not come from `Client.Random`.

## Behaviours (monsters)

- **betterSkillys behaviours:** `logic/db/BehaviorDb.<Area>.cs`, partial classes of `BehaviorDb`.
  The building blocks are in `logic/behaviors/` and `logic/transitions/`.
- **Doomed Realms monsters** run on a rebuilt copy of the Doomed Realms behaviour engine,
  `logic/doomed/DoomedRuntime.cs`:
  - It covers its composites (If, Cooldown, QueuedBehavior, states), attacks, movement, spawning,
    events (death, hit, chat, `WorldEvent`, `OnHit` from `Enemy.Damage`) and loot.
  - The monster definitions in `logic/doomed/DoomedMonsters.cs` are generated.
  - Elder monsters without a behaviour share their regular version's (`DoomedAliases`, applied in
    `BehaviorDb.Initialize`).
  - A hand-written behaviour replaces a generated one: `BehaviorDb.DoomedMonsters.cs` registers with
    `InitDoomed`, which steps aside for an `Init` of the same monster. `BehaviorDb.Tomb.cs` builds
    both the Tomb and the Elder Tomb this way.
- **Never spawn hostile monsters in the nexus**: they kill low-level characters there.
- **The start-up log lists "Failed to add behavior ... Xml data not found"** for a few upstream
  behaviours with no object data. That's long-standing upstream noise, not a new error.

## Quests and achievements

- **Rules** are in `Shared/quests/` (no game code, so `dotnet test source\Tests` covers them), the
  numbers in `Shared/resources/data/quests.json`. `WorldServer/core/quests/QuestTracker.cs` is the
  game side: one per player, fed by hooks (kills, dungeon clears, realm closes, Arena waves,
  potions, fame, levels, forging, white bags, trades) through `Player.Quests.Event`.
- **State:** the `progress` field of `account.<id>` (JSON). `Client.Save` stores it on leaving;
  claims save before paying, so a claim is never paid twice.
- **Testing:** `/questadmin time <hours>` moves the quests' clock for the whole server (resets,
  daily rewards), `add <event> [n] [flags]` counts an event on you, `done` finishes your quests and
  `reset` starts you afresh. Admins only; use it on a test stack, never the live servers.

## Commands

- **Where:** `core/commands/`, `admin/`, `player/`, `supporter/` and `doomed/`. Each command is a
  nested class of the partial `Command` class; `CommandManager` finds them by reflection.
- **Permissions:** each command overrides `RankRequirement` (`Command.cs`) with the rank it needs. The account's `admin`
  flag passes every check.
- **Name clashes:** Doomed Realms commands that clash with betterSkillys ones were renamed
  (`/sellfame` for its `/sell`, `/openarena` for its `/arena`).

## Redis data

- **Accounts:** `account.<id>` hashes (fields such as `name`, `rank`, `admin`). The `names` hash
  maps upper-case names to ids, and characters are `char.<accountId>.<charId>`.
- **Field formats:** numbers are stored as strings, item lists as little-endian ushort arrays, and
  the `admin` flag as a single byte 0x01.
- **Ranks:** 0 regular to 6 admin. A rank change applies from the player's next login.
- **Arena leaderboard:** the sorted set `doomed.arenaLeaderboard`.

```
source\Redis\redis-cli.exe hget names MEAPY
source\Redis\redis-cli.exe -p 6380 hgetall account.1     # isolated test copy
```

- **Accounts in use:**
  - Meapy (account 2) is the owner's account. Don't change it or its characters without asking.
  - Tester (account 1) is the throwaway test account: change its characters freely, preferably on
    the isolated copy (port 6380).

## Doomed Realms port (generated files)

`python tools\port_doomed.py --dr "<Doomed Realms repo>"` regenerates the Doomed Realms content
and calls the other `tools/port_doomed_*.py` scripts. Its outputs are:
- **XML:** `Shared/resources/xml/custom/DoomedRealms.xml` and `DoomedRealmsGrounds.xml`, plus the
  client's copies.
- **Worlds:** `Shared/resources/xml/custom/DoomedRealmsWorlds.xml` and `Shared/resources/worlds/Doomed/*.jm`
  (`port_doomed_worlds.py`).
- **Monsters:** `WorldServer/logic/doomed/DoomedMonsters.cs` and
  `logic/db/BehaviorDb.DoomedMonsters.cs` (`port_doomed_monsters.py`).
- **Allies and pets:** `logic/db/BehaviorDb.DoomedAllies.cs` and `BehaviorDb.DoomedPets.cs`.
- **Economy:** `core/commands/doomed/DoomedEconomyData.cs` (`port_doomed_economy.py`).

Fix problems in the scripts and regenerate; don't edit these files by hand. Type codes 0xD000 to
0xDFFF are kept stable in `tools/doomed/types.json`, because characters store items by type code:
never renumber them.

Behaviour that isn't generated: `DoomedRuntime.cs`, `DoomedExtraLoot.cs` (loot added to monsters whose Doomed Realms loot is missing or thin), `core/worlds/impl/doomed/DoomedWorlds.cs` (arenas, zombies,
herding, shop), `DoomedRealmsCommands.cs`, `DoomedEconomyCommands.cs`, `Player.UseItem.Doomed.cs`
(class abilities) and `Halo.cs`. Doomed Realms' slot types 26 and 28 are 126 and 128 here.
