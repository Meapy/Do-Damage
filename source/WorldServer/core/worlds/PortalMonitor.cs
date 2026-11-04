using Shared.resources;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds.impl;

namespace WorldServer.core.worlds
{
    public sealed class RealmPortalMonitor
    {
        public const int MAX_PER_REALM = 85;

        private static readonly List<string> Names = new List<string>()
        {
            "Lich", "Goblin", "Ghost",
            "Giant", "Gorgon","Blob",
            "Leviathan", "Unicorn", "Minotaur",
            "Cube", "Pirate", "Spider",
            "Snake", "Deathmage", "Gargoyle",
            "Scorpion", "Djinn", "Phoenix",
            "Satyr", "Drake", "Orc",
            "Flayer", "Cyclops", "Sprite",
            "Chimera", "Kraken", "Hydra",
            "Slime", "Ogre", "Hobbit",
            "Titan", "Medusa", "Golem",
            "Demon", "Skeleton", "Mummy",
            "Imp", "Bat", "Wyrm",
            "Spectre", "Reaper", "Beholder",
            "Dragon", "Harpy"
        };


        // the Nexus's Realm 2 is named apart from the realms
        private static readonly List<string> Realm2Names = new List<string>()
        {
            "Zenith", "Eclipse", "Tempest", "Aurora", "Solstice", "Equinox",
            "Nebula", "Halcyon", "Umbra", "Verdance", "Maelstrom", "Empyrean"
        };

        // The portals sink into the Nexus garden's floor (tools/build_nexus_garden.py): a cavern for
        // each realm on the shrine's `Realm Portals` tiles, Realm 2's vortex on its `Realm2 Portal`
        // tile. Drawn flat several tiles across by the Rust client (`GroundSize`).
        private const string REALM_PORTAL = "NG Realm Cavern";
        private const string REALM2_PORTAL = "NG Realm2 Vortex";

        private readonly object _access = new object();
        private readonly GameServer _gameServer;
        private readonly World _world;

        private readonly Dictionary<int, Portal> _portals = new Dictionary<int, Portal>();
        private readonly List<string> _actives = new List<string>();
        private bool _realm2Building;       // the Nexus's Realm 2 is being built
        private int _realm2Failures;

        public RealmPortalMonitor(GameServer manager, World world)
        {
            _gameServer = manager;
            _world = world;
        }

        public void CreateNewRealm()
        {
            lock (_access)
            {
                // the realms the last world server had come back first, by their names
                var restore = _gameServer.WorldManager.TakeRestore(false);
                var name = restore?.Name ?? Names[Random.Shared.Next(Names.Count)];
                _actives.Add(name);
                _gameServer.WorldManager.CreateNewRealmAsync(name, restore);
            }
        }

        public void AddPortal(World world)
        {
            lock (_access)
            {
                if (_portals.ContainsKey(world.Id))
                    return;

                var realm2 = world is Realm2World;
                var pos = realm2 ? Realm2Spot() : GetRandPosition();

                var portal = (Portal)_world.CreateNewEntity(realm2 ? REALM2_PORTAL : REALM_PORTAL, pos.X + 0.5f, pos.Y + 0.5f);
                portal.WorldInstance = world;
                portal.Name = $"{PortalName(world)} (0/{MAX_PER_REALM})";
                if (world is RealmWorld realm)
                    realm.HasNexusPortal = true;
                if (realm2)
                    _realm2Building = false;

                _portals.Add(world.Id, portal);
            }
        }

        public bool PortalIsOpen(int worldId)
        {
            lock (_access)
            {
                if (!_portals.ContainsKey(worldId))
                    return false;
                return _portals[worldId].Usable && !_portals[worldId].Locked;
            }
        }

        public void OpenPortal(int worldId)
        {
            lock (_access)
            {
                if (!_portals.ContainsKey(worldId))
                    return;

                var portal = _portals[worldId];
                if (!portal.Usable)
                    _portals[worldId].Usable = true;
            }
        }

        public void ClosePortal(int worldId)
        {
            lock (_access)
            {
                if (!_portals.ContainsKey(worldId))
                    return;

                var portal = _portals[worldId];
                if (portal.Usable)
                    portal.Usable = false;
            }
        }

        public void Update(ref TickTime time)
        {
            lock (_access)
            {
                CreateRealmIfExists();
                CreateRealm2IfNone();

                foreach (var p in _portals.Values)
                {
                    var count = 0;
                    p.WorldInstance.GetPlayerCount(ref count);

                    var updatedCount = $"{PortalName(p.WorldInstance)} ({Math.Min(count, p.WorldInstance.MaxPlayers)}/{p.WorldInstance.MaxPlayers})";

                    if (p.Name.Equals(updatedCount))
                        continue;
                    p.Name = updatedCount;
                }
            }
        }

        private void CreateRealmIfExists()
        {
            if (Names.Count == 0 || _actives.Count >= _gameServer.Configuration.serverSettings.maxRealms)
                return;

            var totalPlayers = _world.GameServer.ConnectionManager.GetPlayerCount();
            var realmsNeeded = 1 + totalPlayers / (MAX_PER_REALM + 15);
            if (_actives.Count < realmsNeeded || _gameServer.WorldManager.HasRestore(false))
                CreateNewRealm();
        }

        /// <summary>The Nexus's Realm 2, the one its portal leads to (null while it's being built).</summary>
        public Realm2World Realm2()
        {
            lock (_access)
                return _portals.Values.Select(p => p.WorldInstance).OfType<Realm2World>().FirstOrDefault(w => !w.Closed);
        }

        private static string PortalName(World world) => world is Realm2World ? $"Realm 2: {world.GetDisplayName()}" : world.GetDisplayName();

        // One Realm 2 always open from the Nexus (beside those its key opens): a new one once it closes
        private void CreateRealm2IfNone()
        {
            if (_realm2Building || _realm2Failures >= 3 || _portals.Values.Any(p => p.WorldInstance is Realm2World))
                return;
            var taken = _gameServer.WorldManager.GetWorlds().Select(w => w.DisplayName).ToHashSet();
            var free = Realm2Names.Where(n => !taken.Contains(n)).ToList();
            var restore = _gameServer.WorldManager.TakeRestore(true);
            var name = restore?.Name ?? (free.Count > 0 ? free[Random.Shared.Next(free.Count)] : Realm2Names[Random.Shared.Next(Realm2Names.Count)]);
            _realm2Building = true;
            _gameServer.WorldManager.CreateNewRealm2Async(name, _ =>
            {
                lock (_access)
                {
                    _realm2Building = false;
                    _realm2Failures++;
                }
            }, restore);
        }

        public void RemovePortal(int worldId)
        {
            lock (_access)
            {
                if (!_portals.TryGetValue(worldId, out var portal))
                    return;

                if (portal.WorldInstance is not Realm2World)
                {
                    var name = portal.WorldInstance.DisplayName;
                    _actives.Remove(name);
                    Names.Add(name);
                }

                _world.LeaveWorld(portal);
                _portals.Remove(worldId);
            }
        }

        // Realm 2's own tile, else beside the realms' portals
        private Position Realm2Spot()
        {
            foreach (var t in _world.Map.Regions.Where(t => t.Value == TileRegion.Realm2_Portal))
                if (!_portals.Values.Any(p => p.X == t.Key.X + 0.5f && p.Y == t.Key.Y + 0.5f))
                    return new Position(t.Key.X, t.Key.Y);
            return NextToRealms();
        }

        // the free realm portal spot nearest a realm's portal (beside Realm 1's), or any free one
        private Position NextToRealms()
        {
            var free = _world.Map.Regions.Where(t => t.Value == TileRegion.Realm_Portals)
                .Select(t => t.Key)
                .Where(k => !_portals.Values.Any(p => p.X == k.X + 0.5f && p.Y == k.Y + 0.5f))
                .ToList();
            if (free.Count == 0)
                return GetRandPosition();
            var realms = _portals.Values.Where(p => p.WorldInstance is not Realm2World).ToList();
            if (realms.Count == 0)
                return GetRandPosition();
            var best = free.OrderBy(k => realms.Min(p => Math.Abs(p.X - k.X - 0.5f) + Math.Abs(p.Y - k.Y - 0.5f) * 1.5f)).First();
            return new Position(best.X, best.Y);
        }

        // a free `Realm Portals` tile at random (the realms' caverns), or any of them when all are taken
        private Position GetRandPosition()
        {
            var tiles = _world.Map.Regions.Where(t => t.Value == TileRegion.Realm_Portals).Select(t => t.Key).ToList();
            if (tiles.Count == 0)
                return new Position(0, 0);
            var free = tiles.Where(k => !_portals.Values.Any(p => p.X == k.X + 0.5f && p.Y == k.Y + 0.5f)).ToList();
            var pick = (free.Count > 0 ? free : tiles)[Random.Shared.Next((free.Count > 0 ? free : tiles).Count)];
            return new Position(pick.X, pick.Y);
        }
    }
}
