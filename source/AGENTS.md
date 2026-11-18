# Server development

C# on .NET 8, based on TK-Reborn: `betterSkillys.sln` holds the account server (`App`), the world
server (`WorldServer`), the shared library (`Shared`) and Redis. Read [../AGENTS.md](../AGENTS.md)
first, above all: **ask before restarting the live servers**.

| Project | What it is | Config |
|---|---|---|
| `App` | Account server (ASP.NET): login, characters, server list, web endpoints in `Controllers/` | `App/server.json` (Redis, port 8080) |
| `WorldServer` | The game: worlds, players, monsters, commands, networking | `WorldServer/wServer.json` (Redis, port 2051, public address, realm settings) |
| `Shared` | Game data (`resources/`), the Redis layer (`database/`), shared types | |
| `Redis` | Bundled Redis 3.2 for Windows, `redis.local.conf` | |

## Build, test, deploy

```
dotnet build source\betterSkillys.sln -c Debug
dotnet build source\WorldServer\WorldServer.csproj -c Debug -o build\wscheck   # just to check it compiles
dotnet test source\Tests
```

- `Optimize` is on for `WorldServer` and `Shared` in Debug too (Debug is what `Run Server.bat`
  runs).
- **Live servers** are on the VPS under `/opt/bsk` (`app`, `build/live/<slot>`); this PC's copies
  (`App/bin/Debug/net8.0`, `build/live/a` and `b`) are stopped since the move.
- **Trying a change in game:** `tools/testclient/iso_stack.py start` builds App and WorldServer
  into `build/iso`, on Redis 6380 (a copy of the live data), account 8081 and world 2052;
  `--slot N` adds N, and in a worktree pass `--data-from <main checkout>\source\Redis\data` or it
  starts empty. Play with the Rust client's scripted flags or the bot (client-rs/AGENTS.md,
  "Testing against the servers"); `iso_stack.py stop` when done.
- **Logs:** `build/live/<slot>/logs/wServer/<date>/info.txt` and `error.txt`,
  `App/bin/Debug/net8.0/logs/app/`; on the VPS `/opt/bsk/build/live/<slot>/logs/wServer/`,
  `/opt/bsk/app/logs/` and `journalctl -u bsk-world` / `-u 'bsk-gateway@*'`. Slow ticks (>200 ms)
  and message passes (>100 ms) are logged; every 30 s each busy world logs "Tick stats"
  (`core/worlds/TickProfile.cs`), and GC pauses over 50 ms log "GC pause" (`core/GcPauseLog.cs`).
  Measure with those. `python deploy\deploy.py --status` shows the services and slots; Redis on
  the VPS is `redis-cli -p 6379`.
- **Deploying** happens with the next release (merging its pull request into `main`; ask the
  owner first) and kicks nobody: world servers roll (below), the account server restart pauses
  sign-ins for seconds, a new gateway takes a free slot. A message change ships with the client.
- **Stopping cleanly:** Ctrl+C or SIGTERM: `GameServer.Stop` ends the loop and
  `ConnectionListener.Shutdown` disconnects (and so saves) each player. A forced kill skips that.

## Rolling deploys (world proxy)

- `tools/world_proxy.py` owns port 2051 and forwards each connection to slot a (12051) or b
  (22051) on 127.0.0.1; `tools/rolling_restart.py` builds the slots into `build/live/<slot>` and
  keeps `build/live/state.json`, which the proxy rereads.
- **Routing** is by the game id in the Hello: each slot has its own world id range
  (`serverSettings.worldIdBase`, 1000000 for a, 2000000 for b); the Nexus, negative and unknown ids
  and new logins go to the active slot. Don't hand out fixed positive world ids.
- **Hand-over:** the tool writes `drain.request` (minutes allowed) into the old slot's folder;
  `GameServer.DrainTick` moves its Nexus players at once, and `Client.Reconnect` turns trips to the
  Nexus or a realm into a keyless Reconnect the proxy sends to the new server (`HandsOver`). At the
  deadline the rest move, and empty, it stops. It writes `drain.status` and stops the market
  sweeper meanwhile.
- **Account locks:** `ConnectionManager` asks other world servers to sign the account out (Redis
  channel `Kick`) and waits up to 5 s for the old server to save and release it.
- **Scheduled restarts:** behind the proxy, `restartTime` writes `restart.request` and the proxy
  runs `rolling_restart.py deploy --reuse` (asked again every 30 minutes until draining). Without
  the proxy it kicks everyone 5 minutes after a warning and starts itself again (on Linux, systemd
  does).
- **Realms carry over:** each Nexus realm keeps a snapshot in the Redis hash `realms`
  (`core/worlds/RealmSnapshots.cs`, `RealmManager.Snapshot.cs`) that a new server reopens;
  `/uptime` reads `worlds:upSince`. New realm state worth keeping goes in the snapshot; what it
  can't redo leaves the realm as it was rather than failing.
- **Failed starts:** `start_backend` records a new server as `starting` and stops it (SIGTERM, or
  Ctrl+Break on Windows) if it doesn't listen in time; a deploy stops a `starting` server a dead
  tool left. Never leave a world server running that the state doesn't list.
- **Client addresses:** the proxy sends message 250 (`PROXY_INFO`) before the Hello; only a server
  behind the proxy takes it.
- **Testing:** `iso_stack.py --slot N start --rolling`, then `rolling_restart.py --iso N deploy`
  (front 2052+N, slots 12052+N and 22052+N). Test world servers run as `dotnet WorldServer.dll`,
  never `WorldServer.exe`.

## How the world server works

- **Threads:** each world runs on a world thread (`core/worlds/RootWorldThread.cs`) at 100 ms
  ticks, sleeping until a message or the next tick. A world made from inside another (a dungeon, a
  vault) is a branch (`WorldBranch`) on its root's thread; `World.SignalIO` wakes it and
  `World.Post` runs code on a world's thread.
- **New worlds:** `WorldManager.CreateNewWorldAsync` from a world thread: the map and entities are
  built on a pool thread, then attached, and the callback runs on the caller's thread. Building
  inline froze the realm and its dungeons.
- **Sending:** messages from one pass of a thread's loop go out together (`BeginBatch` /
  `FlushBatch`, `core/connection/NetworkHandler.cs`), order kept.
- **Incoming messages:** ids in `networking/packets/MessageIds.cs`; handlers derive from
  `IMessageHandler` in `core/net/handlers/` and are found by reflection (no registration). An
  unknown id counts as packet spam (32 disconnect). Keep the clients' ids in step.
- **Text players read:** the server's text reaches everyone in English. For new features send ids
  and numbers and let the Rust client word them (client-rs/AGENTS.md, "Languages"), keeping English
  text as a fallback for older clients.
- **Players:** partial classes in `core/objects/player/`. Sight is `Player.SightRadius`, 15 tiles
  by default, up to 40 when the client asks (message 88, `SetSightRadius`); enemies tick only in
  active chunks near players.
- **Worlds:** `core/worlds/WorldManager.cs` creates them (its default case asks
  `impl/doomed/DoomedWorlds.Create` first, for the minigames). Definitions are in
  `Shared/resources/xml` (`Worlds.xml`, `Dungeons.xml`, `custom/DoomedRealmsWorlds.xml`); maps are
  `.jm` files in `Shared/resources/worlds/`.
- **Removing entities:** `World.RemoveFromWorld` removes any `entity is Enemy` from the enemy list,
  as `AddToWorld` adds them; don't check `ObjectDesc.Enemy` (monsters without `<Enemy/>`, like the
  herding sheep, would stay).
- **Merchants** return to any `IMerchantWorld` (the Nexus or the `/shop` world).
- **Shooting** (`PlayerShootHandler.cs`): rate-of-fire and projectile-count checks skip admins; the
  ability is timed by its `Cooldown`. The client rolls each shot's damage from the sequence it
  shares with `Client.Random`, so anything that refuses a shot must still use up its roll
  (`Player.SkipShotRoll`), and the server's own rolls never come from `Client.Random`.

## Content

- **Items, objects, grounds:** XML in `Shared/resources/xml`; most files have a client copy under
  `client/src/kabam/rotmg/assets/` (what the Rust client reads): edit both. Characters
  store items by type code, so never renumber an object. Some tools keep type codes stable for
  what they write (`tools/<name>/types.json`); a file whose first line says "Generated by
  tools/<script>" is rewritten by that script, so change the script and rerun it.
- **Behaviours:** `logic/db/BehaviorDb.<Area>.cs`, partial classes of `BehaviorDb`, built from
  `logic/behaviors/` and `logic/transitions/`.
- **The second behaviour engine:** some monsters run on `logic/doomed/DoomedRuntime.cs` (its own
  composites, attacks, movement, spawning, events and loot), defined in
  `logic/doomed/DoomedMonsters.cs` and registered in `BehaviorDb.DoomedMonsters.cs` with
  `InitDoomed`, which steps aside for an `Init` of the same monster: write a regular behaviour to
  replace one (`BehaviorDb.Tomb.cs` does for the Tomb and the Elder Tomb). Elder monsters without
  a behaviour share their regular one's (`DoomedAliases`, in `BehaviorDb.Initialize`).
- **Classes, pets, minigames, the forge and the Elder dungeons:** SETUP-LOCAL.md, "Classes,
  minigames and the economy". `tools/make_elder.py` builds the Elder tiles, monsters and maps.
- **Never spawn hostile monsters in the Nexus**: they kill low-level characters there.
- "Failed to add behavior ... Xml data not found" at start-up is long-standing upstream noise.

## Quests and achievements

- **Rules** in `Shared/quests/` (no game code, covered by `dotnet test source\Tests`), numbers in
  `Shared/resources/data/quests.json`; the game side is `WorldServer/core/quests/QuestTracker.cs`,
  one per player, fed through `Player.Quests.Event`.
- **State:** the `progress` field of `account.<id>` (JSON), saved on leaving; claims save before
  paying.
- **Testing:** `/questadmin time <hours>`, `add <event> [n] [flags]`, `done`, `reset` (admins, on a
  test stack only).

## Commands

- In `core/commands/` (`admin/`, `player/`, `supporter/`, `doomed/`): nested classes of the
  partial `Command` class, found by `CommandManager` by reflection. Each overrides
  `RankRequirement`; the account's `admin` flag passes every check.
- `/sellfame` and `/openarena` are named so to stay clear of `/sell` (the market) and `/arena`.

## Redis data

- **Accounts:** `account.<id>` hashes (`name`, `rank`, `admin`...); the `names` hash maps upper-case
  names to ids; characters are `char.<accountId>.<charId>`. Numbers are strings, item lists
  little-endian ushort arrays, `admin` a single byte 0x01. Ranks run 0 (regular) to 6 (admin) and
  apply from the next login.
- **Arena leaderboard:** the sorted set `doomed.arenaLeaderboard`; `/gear` claims: the set
  `doomed.gearClaimed`. (Keep these key names: they hold live data.)
- **Discord links** (`Shared/database/DiscordLinks.cs`): the account's `discordId`, `discordName`,
  `discordReward`, the hash `discord.accounts` and the sets `discord.rewarded` and
  `discord.unlinked`; only the account server writes them, straight to Redis.
- **Sprite Editor** (`Shared/database/SpriteSubmissions.cs`, `App/Controllers/SpritesController.cs`):
  `sprites.<id>` hashes (finished ones expire after 30 days), `sprites.nextId`, the sorted sets
  `sprites.pending` and `sprites.approved`, the lists `sprites.by.<account>`, and rate counters
  `sprites.wait.*`, `.day.*`, `.ip.*`, `.all.*`. What shipped is
  `resources/data/community_sprites.json`, written by `tools/community_sprites.py` only.

```
source\Redis\redis-cli.exe hget names MEAPY
source\Redis\redis-cli.exe -p 6380 hgetall account.1     # isolated test copy
```

- **Accounts:** Meapy (account 2) is the owner's: don't change it or its characters without
  asking. Tester (account 1) is the throwaway test account, best used on the isolated copy (6380).
