using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shared.resources;
using WorldServer.core.worlds.impl;
using WorldServer.utils;

namespace WorldServer.core.worlds
{
    public sealed class WorldManager
    {
        public GameServer GameServer { get; private set; }

        private int NextWorldId = 0;

        public NexusWorld Nexus { get; private set; }
        public TestWorld Test { get; private set; }

        private readonly ConcurrentDictionary<int, World> Worlds = new ConcurrentDictionary<int, World>();
        private readonly ConcurrentDictionary<int, World> Guilds = new ConcurrentDictionary<int, World>();
        private readonly ConcurrentDictionary<int, int> WorldToGuildId = new ConcurrentDictionary<int, int>();
        private readonly Dictionary<int, RootWorldThread> Threads = new Dictionary<int, RootWorldThread>();

        public IEnumerable<World> GetWorlds() => Worlds.Values;

        private readonly Random Random = new Random();

        public WorldManager(GameServer gameServer)
        {
            GameServer = gameServer;
            NextWorldId = gameServer.Configuration.serverSettings.worldIdBase;
        }

        public void Initialize()
        {
            CreateNexus();
        }

        public void CreateNexus()
        {
            var worldResource = GameServer.Resources.GameData.GetWorld("Nexus");
            if (worldResource == null)
                return;

            var world = Nexus = new NexusWorld(GameServer, -2, worldResource);
            var success = world.LoadMapFromData(worldResource);
            if (!success)
                throw new Exception("Unable to initialize Nexus.");
            world.Init();
            _ = Worlds.TryAdd(world.Id, world);
            lock (Threads)
            {
                Threads.Add(world.Id, new RootWorldThread(this, world));
            }
        }

        public void CreateNewRealmAsync(string name)
        {
            var worldResource = GameServer.Resources.GameData.GetWorld("Realm of the Mad God");
            if (worldResource == null)
                return;

            var nextId = Interlocked.Increment(ref NextWorldId);

            _ = Task.Factory.StartNew(() =>
            {
                var world = new RealmWorld(GameServer, nextId, worldResource);
                world.SetMaxPlayers(RealmPortalMonitor.MAX_PER_REALM);
                world.DisplayName = name;
                var success = world.LoadMapFromData(worldResource);
                if (!success)
                    return null;
                world.Init();
                _ = Worlds.TryAdd(world.Id, world);
                lock (Threads)
                {
                    Threads.Add(world.Id, new RootWorldThread(this, world));
                }
                GameServer.WorldManager.Nexus.PortalMonitor.AddPortal(world);
                return world;
            });
        }

        /// <summary>The Nexus's own Realm 2 (RealmPortalMonitor), built on a pool thread like a realm, its portal beside the realms'.</summary>
        public void CreateNewRealm2Async(string name, Action<World> failed)
        {
            var worldResource = GameServer.Resources.GameData.GetWorld("Realm 2");
            if (worldResource == null)
            {
                failed(null);
                return;
            }

            var nextId = Interlocked.Increment(ref NextWorldId);
            _ = Task.Run(() =>
            {
                try
                {
                    var world = new Realm2World(GameServer, nextId, worldResource);
                    world.SetMaxPlayers(RealmPortalMonitor.MAX_PER_REALM);
                    world.DisplayName = name;
                    if (!world.LoadMapFromData(worldResource))
                    {
                        failed(world);
                        return;
                    }
                    world.Init();
                    _ = Worlds.TryAdd(world.Id, world);
                    lock (Threads)
                    {
                        Threads.Add(world.Id, new RootWorldThread(this, world));
                    }
                    GameServer.WorldManager.Nexus.PortalMonitor.AddPortal(world);
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"Building the Nexus's Realm 2: {e}");
                    failed(null);
                }
            });
        }

        public World CreateNewWorld(string dungeonName, int? id = null, World parent = null)
        {
            var world = BuildWorld(dungeonName, id, parent);
            if (world != null)
                AttachWorld(world, parent);
            return world;
        }

        /// <summary>
        /// Builds a world (its map, random layout and entities) on a pool thread, so a slow build
        /// doesn't freeze the world the player is in and every dungeon under it; then, on
        /// <paramref name="from"/>'s world thread, adds it under that world and calls
        /// <paramref name="done"/> with it (null if it couldn't be built).
        /// </summary>
        public void CreateNewWorldAsync(string dungeonName, World from, Action<World> done)
        {
            _ = Task.Run(() =>
            {
                World world = null;
                try
                {
                    world = BuildWorld(dungeonName, null, from);
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"Building {dungeonName}: {e}");
                }

                from.Post(() =>
                {
                    // a realm (Realm 2, from its key) runs on its own thread, as the Nexus's realms do
                    if (world != null)
                        AttachWorld(world, world is RealmWorld ? null : from);
                    done(world);
                });
            });
        }

        /// <summary>A new world, loaded but not yet running (nothing else can see it).</summary>
        private World BuildWorld(string dungeonName, int? id, World parent)
        {
            //Console.WriteLine($"CreateNewWorld({dungeonName}, {id ?? null}, {(parent == null ? "null" : parent.IdName)}");
            var worldResource = GameServer.Resources.GameData.GetWorld(dungeonName);
            if (worldResource == null)
                return null;

            var nextId = id ?? Interlocked.Increment(ref NextWorldId);

            World world;
            switch (worldResource.Instance)
            {
                case WorldResourceInstanceType.Nexus:
                    world = new NexusWorld(GameServer, nextId, worldResource);
                    break;
                case WorldResourceInstanceType.Vault:
                    world = new VaultWorld(GameServer, nextId, worldResource, parent);
                    break;
                case WorldResourceInstanceType.Test:
                    world = new TestWorld(GameServer, nextId, worldResource);
                    break;
                case WorldResourceInstanceType.Realm2:
                    world = new Realm2World(GameServer, nextId, worldResource);
                    break;
                case WorldResourceInstanceType.Guild:
                    world = new GuildHallWorld(GameServer, nextId, worldResource, parent);
                    break;
                default:
                    // ported Doomed Realms minigames and shop
                    world = impl.doomed.DoomedWorlds.Create(GameServer, nextId, worldResource, parent)
                        ?? new World(GameServer, nextId, worldResource, parent);
                    break;
            }

            if (worldResource.Instance != WorldResourceInstanceType.Test) {
                var success = world.LoadMapFromData(worldResource);
                if (!success)
                    return null;
            }

            world.Init();
            return world;
        }

        /// <summary>Starts a built world: its own thread, or a branch of its parent's (on the parent's thread).</summary>
        private void AttachWorld(World world, World parent)
        {
            _ = Worlds.TryAdd(world.Id, world);
            // null parents are threaded as they get treated as the root
            if (parent == null)
                lock (Threads)
                {
                    Threads.Add(world.Id, new RootWorldThread(this, world));
                }
            parent?.WorldBranch.AddBranch(world);
        }

        /// <summary>
        /// A guild's hall at this level (1-4), its vault linked, under the Nexus: call it on the
        /// Nexus's thread (GuildHallWorld.Open). It becomes the guild's hall, replacing any older one.
        /// </summary>
        public GuildHallWorld CreateGuildHall(int guildId, int level)
        {
            if (BuildWorld(Shared.database.guild.GuildHalls.WorldName(level), null, Nexus) is not GuildHallWorld hall)
                return null;
            hall.Link(guildId, level);
            AttachWorld(hall, Nexus);
            Guilds[guildId] = hall;
            WorldToGuildId[hall.Id] = guildId;
            return hall;
        }

        public World GetWorld(int id) => Worlds.TryGetValue(id, out World ret) ? ret : null;
        public World GetGuild(int guildId) => Guilds.TryGetValue(guildId, out var ret) ? ret : null;
        public int GetGuildId(int gameId) => WorldToGuildId.TryGetValue(gameId, out var ret) ? ret : -1;

        public void RemoveWorld(World world)
        {
            // a realm gone some other way than closing still takes its Nexus portal with it, so the
            // portal never leads to a world that's gone (and the Nexus opens a new Realm 2)
            if (world is RealmWorld { HasNexusPortal: true })
                Nexus?.PortalMonitor.RemovePortal(world.Id);

            if (world is RealmWorld)
            {
                lock (Threads)
                {
                    var removed = Threads.Remove(world.Id);
                    if (!removed)
                        StaticLogger.Instance.Warn($"Unable to remove Thread: {world.Id} {world.IdName}");
                    else
                        StaticLogger.Instance.Warn($"Removed Thread: {world.Id} {world.IdName}");
                }
            }

            if (Worlds.TryRemove(world.Id, out _))
            {
                switch (world.InstanceType)
                {
                    case WorldResourceInstanceType.Guild:
                        // only while it's still the guild's hall (an upgrade's old one isn't)
                        if (WorldToGuildId.TryRemove(world.Id, out var guildId))
                            _ = Guilds.TryRemove(new KeyValuePair<int, World>(guildId, world));
                        break;
                }
            }

            world.OnRemovedFromWorldManager();
        }

        public void Dispose()
        {
            lock (Threads)
            {
                foreach (var thread in Threads.Values)
                    thread.Stop();
            }
        }

        public List<World> GetRealms() => Worlds.Values.Where(_ => _ is RealmWorld r && r.ListedInNexus && !r.RealmManager.DisableSpawning).ToList(); // todo mabye not have a tolist

        public RealmWorld GetRandomRealm()
        {
            var realms = GetRealms();
            if (realms.Count == 0)
                return null;
            return realms[Random.Next(realms.Count)] as RealmWorld;
        }
    }
}
