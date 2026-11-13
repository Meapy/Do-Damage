using Pipelines.Sockets.Unofficial.Arenas;
using Shared.database;
using Shared.resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using WorldServer.core.objects;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.vendors;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.core.worlds.census;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.worlds
{
    public class World
    {
        public const int CULL_RANGE = 30;
        
        public const int NEXUS_ID = -2;
        public const int TEST_ID = -4;

        private int NextEntityId;

        public int Id { get; }
        public string IdName { get; set; }
        public string DisplayName { get; set; }
        public WorldResourceInstanceType InstanceType { get; private set; }
        public bool Persist { get; protected set; }
        public int MaxPlayers { get; protected set; }
        public bool CreateInstance { get; private set; }
        public bool IsRealm { get; set; }
        public bool AllowTeleport { get; protected set; }
        public byte Background { get; protected set; }
        public byte Blocking { get; protected set; }
        public string Music { get; set; }
        public byte Difficulty { get; protected set; }
        public bool Deleted { get; protected set; }
        public bool DisableShooting { get; set; }
        public bool DisableAbilities { get; set; }
        // a lethal hit here sends the player to the Nexus instead of killing them (arenas)
        public bool DefeatToNexus { get; protected set; }
        // each drop's chance is multiplied by this here, after the boosts (Loot.Handle): 0.5 in the arenas
        public double LootRate { get; protected set; } = 1;
        // the share of the usual fame paid here (FameRates): 0.5 in the arenas
        public double FameRate { get; protected set; } = 1;
        // the share of the usual experience, so fame, from kills at level 20 (FameRates): 0.75 in the arenas
        public double KillFameRate { get; protected set; } = 1;
        private long Lifetime { get; set; }

        public bool isWeekend { get; set; } = false;

        public readonly Wmap Map;
        public readonly GameServer GameServer;
        public bool ShowDisplays { get; protected set; }

        public CollisionMap<Entity> EnemiesCollision { get; private set; }
        public CollisionMap<Entity> PlayersCollision { get; private set; }
        public Dictionary<int, Player> Players { get; private set; } = new Dictionary<int, Player>();
        public Dictionary<int, Enemy> Enemies { get; private set; } = new Dictionary<int, Enemy>();
        public Dictionary<int, Enemy> Quests { get; private set; } = new Dictionary<int, Enemy>();
        public Dictionary<int, StaticObject> StaticObjects { get; private set; } = new Dictionary<int, StaticObject>();
        public Dictionary<int, Container> Containers { get; private set; } = new Dictionary<int, Container>();
        public Dictionary<int, Portal> Portals { get; private set; } = new Dictionary<int, Portal>();
        public Dictionary<int, Pet> Pets { get; private set; } = new Dictionary<int, Pet>();
        public Dictionary<int, SellableObject> SellableObjects { get; private set; } = new Dictionary<int, SellableObject>();

        private readonly List<Entity> EntitiesToAdd = new List<Entity>();
        // the map's objects between loading and the first tick, for a world's Init to adjust
        protected IReadOnlyList<Entity> PendingEntities => EntitiesToAdd;
        private readonly List<Entity> EntitiesToRemove = new List<Entity>();

        private readonly List<WorldTimer> Timers = new List<WorldTimer>();

        public WorldBranch WorldBranch { get; private set; }
        public World ParentWorld { get; set; }

        // used for behaviour system
        // Hashset to have non duplicates
        private readonly HashSet<string> Labels = new HashSet<string>();

        public World(GameServer gameServer, int id, WorldResource resource, World parent = null)
        {
            GameServer = gameServer;
            Map = new Wmap(this);

            Id = id;
            IdName = resource.DisplayName;
            DisplayName = resource.DisplayName;
            Difficulty = resource.Difficulty;
            Background = resource.Background;
            MaxPlayers = resource.Capacity;
            InstanceType = resource.Instance;
            Persist = resource.Persists;
            ShowDisplays = Id == -2 || resource.ShowDisplays;
            Blocking = resource.VisibilityType;
            MinSightRadius = resource.MinSightRadius;
            AllowTeleport = resource.AllowTeleport;
            DisableShooting = resource.DisableShooting;
            DisableAbilities = resource.DisableAbilities;
            DefeatToNexus = resource.DefeatToNexus;
            LootRate = resource.LootRate;
            FameRate = resource.FameRate;
            KillFameRate = resource.KillFameRate;
            CreateInstance = resource.CreateInstance;

            IsRealm = false;

            if (resource.Music.Count > 0)
                Music = resource.Music[Random.Shared.Next(0, resource.Music.Count)];
            else
                Music = "sorc";

            WorldBranch = new WorldBranch(this);
            ParentWorld = parent;

            var day = DateTime.Now.DayOfWeek;
            if (day != DayOfWeek.Saturday && day != DayOfWeek.Sunday) { }
            else isWeekend = true;
        }

        // the least sight radius players get here (WorldResource.MinSightRadius)
        public int MinSightRadius { get; private set; }

        public bool HasLabel(string labelName) => Labels.Contains(labelName.ToLower());
        public bool SetLabel(string labelName) => Labels.Equals(labelName.ToLower());
        public bool RemoveLabel(string labelName) => Labels.Remove(labelName.ToLower());

        public virtual bool AllowedAccess(Client client) => true;

        // accounts brought here by /summon or /visit, until when: they may enter even when
        // AllowedAccess would turn them away (an arena game that has already started)
        private readonly ConcurrentDictionary<int, DateTime> _invited = new ConcurrentDictionary<int, DateTime>();

        public void Invite(int accountId) => _invited[accountId] = DateTime.UtcNow.AddSeconds(60);

        // every account that has had a character here (or been let in by a player's /visit), so
        // /visit never takes anyone back into an instance they've already been in
        private readonly ConcurrentDictionary<int, byte> _beenIn = new ConcurrentDictionary<int, byte>();

        public bool HasBeenIn(int accountId) => _beenIn.ContainsKey(accountId);

        public void MarkBeenIn(int accountId) => _beenIn[accountId] = 0;

        // AllowedAccess, or an invitation that hasn't run out
        public bool CanEnter(Client client)
        {
            if (_invited.TryGetValue(client.Account.AccountId, out var until))
            {
                if (DateTime.UtcNow < until)
                    return true;
                _invited.TryRemove(client.Account.AccountId, out _);
            }
            return AllowedAccess(client);
        }

        public void Broadcast(OutgoingMessage outgoingMessage)
        {
            foreach (var player in Players.Values)
                player.Client.SendPacket(outgoingMessage);
        }

        public void BroadcastIfVisible(List<OutgoingMessage> outgoingMessages, ref Position worldPosData)
        {
            foreach (var outgoingMessage in outgoingMessages)
                BroadcastIfVisible(outgoingMessage, ref worldPosData);
        }

        public void BroadcastIfVisible(OutgoingMessage outgoingMessage, ref Position worldPosData)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(ref worldPosData) < CULL_RANGE * CULL_RANGE)
                {
                    if (outgoingMessage is ServerPlayerShoot)
                        player.ServerPlayerShoot(outgoingMessage as ServerPlayerShoot);
                    player.Client.SendPacket(outgoingMessage);
                }
        }

        public void BroadcastIfVisible(OutgoingMessage outgoingMessage, Entity host)
        {
            if (outgoingMessage is EnemyShootMessage enemyShoot)
                enemyShoot.Damage = GameServer.DamageEvent.Apply(host, enemyShoot.Damage);
            else if (outgoingMessage is AoeMessage aoe)
                aoe.Damage = GameServer.DamageEvent.Apply(host, aoe.Damage);
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    if (outgoingMessage is EnemyShootMessage)
                        player.ProcessEnemyShoot(outgoingMessage as EnemyShootMessage);
                    player.Client.SendPacket(outgoingMessage);
                }
        }

        public void BroadcastEnemyShootIfVisible(EnemyShootMessage enemyShoot, Entity host)
        {
            enemyShoot.Damage = GameServer.DamageEvent.Apply(host, enemyShoot.Damage);
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    player.ProcessEnemyShoot(enemyShoot);
                    player.Client.SendPacket(enemyShoot);
                }
        }

        public void BroadcastServerPlayerShoot(ServerPlayerShoot serverPlayerShoot, Entity host)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    player.ServerPlayerShoot(serverPlayerShoot);
                    player.Client.SendPacket(serverPlayerShoot);
                }
        }

        public void BroadcastServerPlayerShoot(List<ServerPlayerShoot> serverPlayerShoots, Entity host)
        {
            foreach (var player in Players.Values)
                if (player.SqDistTo(host) < CULL_RANGE * CULL_RANGE)
                {
                    foreach (var serverPlayerShoot in serverPlayerShoots)
                        player.ServerPlayerShoot(serverPlayerShoot);
                    player.Client.SendPackets(serverPlayerShoots);
                }
        }

        public void BroadcastIfVisibleExclude(List<OutgoingMessage> outgoingMessage, Entity broadcaster, Entity exclude)
        {
            foreach (var player in Players.Values)
                if (player.Id != exclude.Id && player.SqDistTo(broadcaster) <= CULL_RANGE * CULL_RANGE)
                    player.Client.SendPackets(outgoingMessage);
        }

        public void BroadcastIfVisibleExclude(OutgoingMessage outgoingMessage, Entity broadcaster, Entity exclude)
        {
            foreach (var player in Players.Values)
                if (player.Id != exclude.Id && player.SqDistTo(broadcaster) <= CULL_RANGE * CULL_RANGE)
                    player.Client.SendPacket(outgoingMessage);
        }

        public void BroadcastToPlayer(OutgoingMessage outgoingMessage, int playerId)
        {
            foreach (var player in Players.Values)
                if (player.Id == playerId)
                {
                    player.Client.SendPacket(outgoingMessage);
                    break;
                }
        }

        public void BroadcastToPlayers(OutgoingMessage outgoingMessage, List<int> playerIds)
        {
            foreach (var player in Players.Values)
                if (playerIds.Contains(player.Id))
                    player.Client.SendPacket(outgoingMessage);
        }

        // the Arena overrides this to hear "GO" (DoomedBattleArena)
        public virtual void ChatReceived(Player player, string text)
        {
            foreach (var en in Enemies)
                en.Value.OnChatTextReceived(player, text);
            foreach (var en in StaticObjects)
                en.Value.OnChatTextReceived(player, text);
        }

        public Player CreateNewPlayer(Client client, ushort type, float x, float y)
        {
            var entity = new Player(GameServer, client, type);
            entity.Id = GetNextEntityId();
            entity.Init(this);
            entity.Move(x, y);
            EntitiesToAdd.Add(entity);
            return entity;
        }

        /// <summary>A new entity at (x, y); on stacked floors at height `z`, or with none given on
        /// the ground there (PlaceZ, when it joins).</summary>
        public Entity CreateNewEntity(string idName, float x, float y, float? z = null) => !GameServer.Resources.GameData.IdToObjectType.TryGetValue(idName, out var type) ? null : CreateNewEntity(type, x, y, z);
        public Entity CreateNewEntity(ushort objectType, float x, float y, float? z = null)
        {
            var entity = Entity.Resolve(GameServer, objectType);
            if (entity == null)
            {
                // unable to identify the entity return null;
                return null;
            }
            entity.Id = GetNextEntityId();
            entity.Init(this);
            entity.Move(x, y);
            if (Levels != null)
                entity.Z = z ?? PlaceZ(x, y);
            EntitiesToAdd.Add(entity);
            return entity;
        }

        public string GetDisplayName() => DisplayName != null && DisplayName.Length > 0 ? DisplayName : IdName;

        public void GetPlayerCount(ref int count) => WorldBranch.GetPlayerCount(ref count);

        public Entity GetEntity(int id)
        {
            if (Players.TryGetValue(id, out var ret1))
                return ret1;

            if (Enemies.TryGetValue(id, out var ret2))
                return ret2;

            if (StaticObjects.TryGetValue(id, out var ret3))
                return ret3;

            if (Containers.TryGetValue(id, out var ret4))
                return ret4;

            if (Portals.TryGetValue(id, out var ret5))
                return ret5;

            if (SellableObjects.TryGetValue(id, out var ret6))
                return ret6;

            return null;
        }

        public int GetNextEntityId() => Interlocked.Increment(ref NextEntityId);

        public IEnumerable<Player> GetPlayers() => Players.Values;

        public Position? GetRegionPosition(TileRegion region)
        {
            if (Map.Regions.All(t => t.Value != region))
                return null;

            var reg = Map.Regions.Single(t => t.Value == region);

            return new Position() { X = reg.Key.X, Y = reg.Key.Y };
        }

        // on stacked floors, those of the floor that has them (its height comes from where they are, AddToWorld)
        public virtual KeyValuePair<IntPoint, TileRegion>[] GetSpawnPoints() =>
            (Levels?.FirstOrDefault(l => l.Regions.Values.Contains(TileRegion.Spawn)) ?? Map).Regions.Where(t => t.Value == TileRegion.Spawn).ToArray();

        // Where an arriving player is placed: the centre of a random spawn tile they can stand on,
        // one with nothing on it (Oryx's Chamber's Nexus Portals stand on spawn tiles) where there is
        // one. Not the tile's corner, which on a spawn tile beside a wall (Oryx's Chamber has
        // several) puts them half inside it, where the client won't let them walk. A map with no
        // spawn tiles (Cnidarian Reef) gets the open tile nearest its centre rather than (0, 0).
        public virtual Position GetPlayerSpawnPosition()
        {
            var points = GetSpawnPoints();
            var open = points.Where(t => IsPassable(t.Key.X + 0.5, t.Key.Y + 0.5, true)).ToArray();
            var free = open.Where(t => GroundTile(t.Key.X, t.Key.Y).ObjType == 0).ToArray();
            if (free.Length > 0)
                points = free;
            else if (open.Length > 0)
                points = open;
            if (points.Length > 0)
            {
                var at = points[Random.Shared.Next(points.Length)].Key;
                return new Position(at.X + 0.5f, at.Y + 0.5f);
            }

            var best = new Position(0f, 0f);
            var bestDist = double.MaxValue;
            double cx = Map.Width / 2.0, cy = Map.Height / 2.0;
            for (var y = 0; y < Map.Height; y++)
                for (var x = 0; x < Map.Width; x++)
                {
                    var dist = (x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy);
                    if (dist < bestDist && IsPassable(x + 0.5, y + 0.5, true))
                    {
                        best = new Position(x + 0.5f, y + 0.5f);
                        bestDist = dist;
                    }
                }
            return best;
        }

        public virtual KeyValuePair<IntPoint, TileRegion>[] GetRegionPoints(TileRegion region) => Map.Regions.Where(t => t.Value == region).ToArray();

        /// <summary>The region's squares on every floor (Map's alone in a flat world), each with the
        /// height of the ground there (its floor's, plus a Lift).</summary>
        public List<(IntPoint Point, float Z)> RegionTiles(TileRegion region)
        {
            var found = new List<(IntPoint, float)>();
            var levels = Levels ?? new List<Wmap> { Map };
            for (var level = 0; level < levels.Count; level++)
                foreach (var r in levels[level].Regions)
                    if (r.Value == region)
                        found.Add((r.Key, Levels == null ? 0 : SurfaceOf(level, levels[level][r.Key.X, r.Key.Y])));
            return found;
        }

        /// <summary>The middle of the region's first square and the ground's height there (a marker
        /// such as Nexus_Bot), or null if the map has none.</summary>
        public (float X, float Y, float Z)? Marker(TileRegion region)
        {
            var tiles = RegionTiles(region);
            if (tiles.Count == 0)
                return null;
            var (p, z) = tiles[0];
            return (p.X + 0.5f, p.Y + 0.5f, z);
        }

        public Player GetUniqueNamedPlayer(string name)
        {
            if (Database.GuestNames.Contains(name))
                return null;

            foreach (var i in Players.Values)
                if (i.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!i.NameChosen && !(this is TestWorld))
                        GameServer.Database.ReloadAccount(i.Client.Account);

                    if (i.Client.Account.NameChosen)
                        return i;
                    break;
                }

            return null;
        }

        // --- stacked floors -------------------------------------------------------------------
        // A world of floors one above another (`<Levels height="3"/>` in its XML: the Stair Tower,
        // the Nexus islands): Levels[0] is Map, each next one LevelHeight tiles above it, all the
        // same size ("<map> 1.jm", "<map> 2.jm"...). A square's floor for an entity is the one
        // whose ground (its height plus the ground's Lift, so stairs climb) is nearest the
        // entity's Z, within Step: further than that is a wall or a drop. Null in every other
        // world, where all of this is Map as before.
        public List<Wmap> Levels { get; private set; }
        public float LevelHeight { get; private set; }
        public const float Step = 0.75f;

        /// <summary>Makes this a world of stacked floors, Map the lowest.</summary>
        protected void StackLevels(float height)
        {
            Levels = new List<Wmap> { Map };
            LevelHeight = height;
        }

        /// <summary>A floor above the others, from a map the size of Map, with its monsters and
        /// objects standing on it.</summary>
        protected bool AddLevel(string map)
        {
            var data = GameServer.Resources.GameData.GetWorldData(map);
            if (data == null)
                return false;
            var level = new Wmap(this) { Z = Levels.Count * LevelHeight };
            NextEntityId += level.Load(new MemoryStream(data), NextEntityId);
            Levels.Add(level);
            level.CreateEntities(z: level.Z);
            return true;
        }

        /// <summary>Which floor (index into Levels) holds square (x, y) at height z; -1 for none.</summary>
        public int LevelAt(int x, int y, float z)
        {
            var best = -1;
            var nearest = Step;
            for (var i = 0; i < Levels.Count; i++)
            {
                var t = Levels[i][x, y];
                if (!t.Exists || t.TileId == 0xFF)
                    continue;
                var d = MathF.Abs(SurfaceOf(i, t) - z);
                if (d <= nearest)
                {
                    best = i;
                    nearest = d;
                }
            }
            return best;
        }

        private float SurfaceOf(int level, WmapTile t) =>
            level * LevelHeight + (GameServer.Resources.GameData.Tiles.TryGetValue(t.TileId, out var desc) ? desc.Lift : 0);

        private static bool HasGround(WmapTile t) => t.Exists && t.TileId != 0xFF;

        /// <summary>The lowest floor with ground at (x, y) that can be walked on (else the lowest
        /// with any), -1 for none (Map's, 0, in a flat world).</summary>
        public int GroundLevel(int x, int y)
        {
            if (Levels == null)
                return 0;
            if (!Map.Contains(x, y))
                return -1;
            var any = -1;
            for (var i = 0; i < Levels.Count; i++)
            {
                var t = Levels[i][x, y];
                if (!HasGround(t))
                    continue;
                if (GameServer.Resources.GameData.Tiles.TryGetValue(t.TileId, out var desc) && !desc.NoWalk)
                    return i;
                if (any < 0)
                    any = i;
            }
            return any;
        }

        /// <summary>The square something placed at (x, y) with no height stands on: Map's, or on
        /// stacked floors the lowest floor's with ground there.</summary>
        public WmapTile GroundTile(int x, int y)
        {
            if (Levels == null)
                return Map[x, y];
            var level = GroundLevel(x, y);
            return level < 0 ? Map[x, y] : Levels[level][x, y];
        }

        /// <summary>The height to stand at (x, y) for something put there: on stacked floors the
        /// ground of the floor within reach of `near` (Step), else of the lowest floor there that
        /// can be walked on; 0 in a flat world. Where there is no ground at all, `near`.</summary>
        public float PlaceZ(float x, float y, float near = 0)
        {
            if (Levels == null)
                return 0;
            var level = LevelAt((int)x, (int)y, near);
            // not into a floor that can't be walked on (a tower's inside, under its top)
            if (level < 0 || (GameServer.Resources.GameData.Tiles.TryGetValue(Levels[level][(int)x, (int)y].TileId, out var desc) && desc.NoWalk))
                level = GroundLevel((int)x, (int)y);
            return level < 0 ? near : SurfaceOf(level, Levels[level][(int)x, (int)y]);
        }

        /// <summary>The ground of floor `level` at (x, y), or null where it has none.</summary>
        public float? FloorZ(int level, int x, int y)
        {
            if (Levels == null)
                return level == 0 && Map.Contains(x, y) ? 0 : null;
            if (level < 0 || level >= Levels.Count || !Map.Contains(x, y) || !HasGround(Levels[level][x, y]))
                return null;
            return SurfaceOf(level, Levels[level][x, y]);
        }

        // every floor's squares, sent to each player who comes in (LAYERS), worked out once: the
        // floors above the map never change their ground. The map's own come too (level 0), as it
        // was then (UPDATE sends its changes as ever): the client works out the islands' rock
        // from all of a floor's squares, and the map's otherwise only come near the player, with
        // nothing to tell sky (never sent) from squares not sent yet
        private List<(byte, short, short, ushort)> _layerTiles;

        private LayersMessage Layers()
        {
            if (_layerTiles == null)
            {
                _layerTiles = new List<(byte, short, short, ushort)>();
                for (var level = 0; level < Levels.Count; level++)
                {
                    var map = Levels[level];
                    for (var y = 0; y < map.Height; y++)
                        for (var x = 0; x < map.Width; x++)
                        {
                            var tile = map[x, y];
                            if (tile.TileId != 0xFF)
                                _layerTiles.Add(((byte)level, (short)x, (short)y, tile.TileId));
                        }
                }
                if (_layerTiles.Count > short.MaxValue)
                    StaticLogger.Instance.Error($"{IdName}: {_layerTiles.Count} squares on its floors, more than LAYERS carries");
            }
            return new LayersMessage { Height = LevelHeight, Tiles = _layerTiles };
        }

        /// <summary>The square (x, y) for something at height z: Map's, or on stacked floors the
        /// floor's at that height (none, Exists false, if there is no floor there).</summary>
        public WmapTile TileFor(int x, int y, float z)
        {
            if (Levels == null)
                return Map[x, y];
            var level = LevelAt(x, y, z);
            return level < 0 ? default : Levels[level][x, y];
        }

        /// <summary>The height of the ground at (x, y) for something at height z (z if there is none).</summary>
        public float SurfaceZ(float x, float y, float z)
        {
            if (Levels == null)
                return 0;
            var level = LevelAt((int)x, (int)y, z);
            return level < 0 ? z : SurfaceOf(level, Levels[level][(int)x, (int)y]);
        }

        /// <summary>IsPassable for a player at height z.</summary>
        public bool IsPassableAt(double x, double y, float z)
        {
            if (Levels == null)
                return IsPassable(x, y);
            if (!Map.Contains((int)x, (int)y))
                return false;
            var tile = TileFor((int)x, (int)y, z);
            if (!tile.Exists || GameServer.Resources.GameData.Tiles[tile.TileId].NoWalk)
                return false;
            return !(tile.ObjType != 0 && tile.ObjDesc != null && (tile.ObjDesc.FullOccupy || tile.ObjDesc.EnemyOccupySquare));
        }

        public bool IsPassable(double x, double y, bool spawning = false)
        {
            var x_ = (int)x;
            var y_ = (int)y;

            if (!Map.Contains(x_, y_))
                return false;

            // on stacked floors, the lowest floor with ground here (GroundTile)
            var tile = GroundTile(x_, y_);

            var tileDesc = GameServer.Resources.GameData.Tiles[tile.TileId];
            if (tileDesc.NoWalk)
                return false;

            if (tile.ObjType != 0 && tile.ObjDesc != null)
                if (tile.ObjDesc.FullOccupy || tile.ObjDesc.EnemyOccupySquare || spawning && tile.ObjDesc.OccupySquare)
                    return false;

            return true;
        }

        /// <summary>Whether a player could walk from one spot to the other: through open squares only,
        /// side to side (a gap between two corners stops a player too), not across walls, objects that
        /// take up their square (the Oryx stages' gates) or ground nobody may walk on. Searches the
        /// whole map, so only for small worlds (the Oryx stages, CanTeleportAbilityTo).</summary>
        public bool WalkReachable(double fromX, double fromY, double toX, double toY)
        {
            int fx = (int)fromX, fy = (int)fromY, tx = (int)toX, ty = (int)toY;
            if (!Map.Contains(fx, fy) || !Map.Contains(tx, ty))
                return false;
            if (fx == tx && fy == ty)
                return true;

            var tiles = GameServer.Resources.GameData.Tiles;
            bool Open(int x, int y)
            {
                var tile = GroundTile(x, y);
                if (!tile.Exists || tiles[tile.TileId].NoWalk)
                    return false;
                var desc = tile.ObjType != 0 ? tile.ObjDesc : null;
                return desc == null || !(desc.FullOccupy || desc.OccupySquare || desc.EnemyOccupySquare);
            }
            if (!Open(tx, ty))
                return false;

            var width = Map.Width;
            var seen = new bool[width * Map.Height];
            var queue = new Queue<(int X, int Y)>();
            seen[fy * width + fx] = true;
            queue.Enqueue((fx, fy));
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                for (var d = 0; d < 4; d++)
                {
                    var nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    var ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (!Map.Contains(nx, ny) || seen[ny * width + nx])
                        continue;
                    seen[ny * width + nx] = true;
                    if (!Open(nx, ny))
                        continue;
                    if (nx == tx && ny == ty)
                        return true;
                    queue.Enqueue((nx, ny));
                }
            }
            return false;
        }

        public bool IsPlayersMax() => Players.Count >= MaxPlayers;

        public void EnterWorld(Entity entity)
        {
            if (entity.Id == -1)
                entity.Id = GetNextEntityId();
            if(entity.World == null)
                entity.Init(this);
            EntitiesToAdd.Add(entity);
        }

        public virtual void LeaveWorld(Entity entity) => entity.Expunge();

        public virtual void AddToWorld(Entity entity)
        {
            // on stacked floors, on the ground where it was put: the floor within reach of its
            // height, else the lowest there (a player arriving at the spawn, a merchant at its stall)
            if (Levels != null)
            {
                entity.Z = PlaceZ(entity.X, entity.Y, entity.Z);
                if (entity is Player p)
                    p.Client.SendPacket(Layers());
            }
            if (entity is Player)
            {
                Visited = true;
                if ((entity as Player).Client?.Account != null)
                    MarkBeenIn((entity as Player).Client.Account.AccountId);
                Players.TryAdd(entity.Id, entity as Player);
                PlayersCollision.Insert(entity, entity.X, entity.Y);
            }
            else if (entity is SellableObject)
                SellableObjects.TryAdd(entity.Id, entity as SellableObject);
            else if (entity is Enemy)
            {
                Enemies.TryAdd(entity.Id, entity as Enemy);
                EnemiesCollision.Insert(entity, entity.X, entity.Y);
                if (entity.ObjectDesc.Quest)
                    Quests.TryAdd(entity.Id, entity as Enemy);
            }
            else if (entity is Container)
                Containers.TryAdd(entity.Id, entity as Container);
            else if (entity is Portal)
                Portals.TryAdd(entity.Id, entity as Portal);
            else if (entity is StaticObject)
            {
                StaticObjects.TryAdd(entity.Id, entity as StaticObject);
                if (entity is Decoy)
                    PlayersCollision.Insert(entity, entity.X, entity.Y);
                else
                    EnemiesCollision.Insert(entity, entity.X, entity.Y);
            }
            else if (entity is Pet)
            {
                Pets.TryAdd(entity.Id, entity as Pet);
                PlayersCollision.Insert(entity, entity.X, entity.Y);
            }
        }

        private void RemoveFromWorld(Entity entity)
        {
            if (entity is Player player)
            {
                Players.Remove(entity.Id);
                PlayersCollision.Remove(entity);

                // if in trade, cancel it...
                if (player != null && player.TradeTarget != null)
                    player.CancelTrade();

                if (player != null && player.Pet != null)
                    LeaveWorld(player.Pet);
            }
            else if (entity is SellableObject)
                SellableObjects.Remove(entity.Id);
            else if (entity is Enemy) // as AddToWorld: characters without <Enemy/> (e.g. sheep) are Enemies too
            {
                Enemies.Remove(entity.Id);
                EnemiesCollision.Remove(entity);
                if (entity.ObjectDesc.Quest)
                    Quests.Remove(entity.Id);
            }
            else if (entity is Container)
                Containers.Remove(entity.Id);
            else if (entity is Portal)
                Portals.Remove(entity.Id);
            else if (entity is StaticObject)
            {
                StaticObjects.Remove(entity.Id);
                if (entity is Decoy)
                    PlayersCollision.Remove(entity);
                else
                    EnemiesCollision.Remove(entity);
            }
            else if (entity is Pet)
            {
                Pets.Remove(entity.Id);
                PlayersCollision.Remove(entity);
            }
        }

        public void ForeachPlayer(Action<Player> action)
        {
            foreach (var player in Players.Values)
                action?.Invoke(player);
        }

        public void ObjsWithin(Entity host, double radius, List<Entity> enemies)
        {
            var radSqr = radius * radius;
            foreach (var enemy in EnemiesCollision.HitTest(host.X, host.Y, radius))
            {
                if (enemy.SqDistTo(host.X, host.Y) >= radSqr)
                    continue;
                enemies.Add(enemy);
            }
        }

        public void WorldAnnouncement(string message)
        {
            var announcement = string.Concat("<ANNOUNCMENT> ", message);
            foreach (var player in Players.Values)
                player.SendInfo(announcement);
        }

        protected void FromWorldMap(Stream dat)
        {
            NextEntityId += Map.Load(dat, NextEntityId);
            InitMap();
        }

        public virtual bool LoadMapFromData(WorldResource worldResource)
        {
            var map = worldResource.MapJM[Random.Shared.Next(0, worldResource.MapJM.Count)];
            var mapEvent = GameServer.Configuration.serverSettings.mapEvent;
            // a world of floors has no event copies (the Nexus islands, for now)
            var stacked = worldResource.LevelHeight > 0 && map.EndsWith(".jm");
            if (!string.IsNullOrEmpty(mapEvent) && map.EndsWith(".jm") && !stacked)
            {
                var eventMap = $"{map[..^3]} {mapEvent}.jm";
                if (GameServer.Resources.GameData.GetWorldData(eventMap) != null)
                    map = eventMap;
            }
            var data = GameServer.Resources.GameData.GetWorldData(map);
            if (data == null)
                return false;
            if (worldResource.RandomLayout != null)
                data = Shared.terrain.DungeonGenerator.Generate(GameServer.Resources.GameData, map, data, worldResource.RandomLayout, Random.Shared) ?? data;
            FromWorldMap(new MemoryStream(data));
            // the floors over it (<Levels height="..."/>): "<map> 1.jm", "<map> 2.jm"... while there are more
            if (stacked)
            {
                StackLevels(worldResource.LevelHeight);
                for (var i = 1; AddLevel($"{map[..^3]} {i}.jm"); i++)
                {
                }
            }
            return true;
        }

        public virtual void Init()
        {
        }

        private void InitMap()
        {
            var w = Map.Width;
            var h = Map.Height;

            EnemiesCollision = new CollisionMap<Entity>(0, w, h);
            PlayersCollision = new CollisionMap<Entity>(1, w, h);
            Map.CreateEntities();
        }

        public Entity FindPlayerTarget(Entity host)
        {
            Entity closestObj = null;
            var closestSqDist = double.MaxValue;
            foreach (var obj in host.World.Players.Values)
            {
                if (!(obj as IPlayer).IsVisibleToEnemy())
                    continue;

                var sqDist = obj.SqDistTo(host);
                if (sqDist < closestSqDist)
                {
                    closestSqDist = sqDist;
                    closestObj = obj;
                }
            }
            return closestObj;
        }

        // Set when a player's message arrives, so the world thread can sleep between ticks
        // instead of polling for messages.
        private readonly System.Threading.ManualResetEventSlim IOSignal = new System.Threading.ManualResetEventSlim(false, 0);

        // A branch world runs on its root's thread, which waits on the root's signal.
        public void SignalIO()
        {
            if (ParentWorld != null)
                ParentWorld.SignalIO();
            else
                IOSignal.Set();
        }

        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> Posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        /// <summary>Runs <paramref name="action"/> on this world's thread, before its players' next messages.</summary>
        public void Post(Action action)
        {
            Posted.Enqueue(action);
            SignalIO();
        }

        public void WaitForIO(int timeoutMs)
        {
            IOSignal.Wait(timeoutMs);
            IOSignal.Reset();
        }

        public void ProcessPlayerIO(ref TickTime time)
        {
            while (Posted.TryDequeue(out var action))
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"[{IdName} {Id}] {e}");
                }

            foreach (var player in Players.Values)
                player.HandleIO(ref time);
            WorldBranch.HandleIO(ref time);
        }

        public bool Update(ref TickTime time)
        {
            try
            {
                Lifetime += time.ElapsedMsDelta;
                WorldBranch.Update(ref time);
                if (IsPastLifetime(ref time))
                    return true;
                UpdateLogic(ref time);
                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine($"World Tick: {e.Message} \n trace: {e.StackTrace}");
                return false;
            }
        }

        public WorldTimer StartNewTimer(int timeMs, Action<World, TickTime> callback)
        {
            var ret = new WorldTimer(timeMs, callback);
            Timers.Add(ret);
            return ret;
        }

        public WorldTimer StartNewTimer(int timeMs, Func<World, TickTime, bool> callback)
        {
            var ret = new WorldTimer(timeMs, callback);
            Timers.Add(ret);
            return ret;
        }

        // Every dungeon gives a way back to the realm: once its last boss (quest monster) is dead and
        // nothing it dropped was a realm portal, one appears where it died. The check waits two
        // seconds, for bosses that change form and for portals their death behaviours drop.
        // quest monsters (bosses) killed here: with none left, the dungeon is cleared (NexusGuide)
        public int QuestKills { get; private set; }
        // for quests (QuestTracker.DungeonCleared): the accounts near a boss here when it died, the
        // strongest boss's health (the dungeon's difficulty), and whether it was counted
        public readonly HashSet<int> BossCredit = new HashSet<int>();
        // what has happened here, for behaviours that wait on several things at once
        // (MarkWorldOnDeath: the Candyland's Cupcake comes once all five kinds of its bosses have fallen)
        public readonly HashSet<string> Marks = new HashSet<string>();
        // counts of things done here (CountOnDeath: the Fungal Cavern's broken Cavern Crystals)
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        public int StrongestBossHp { get; set; }
        private bool _clearCounted;
        // for the Herald's week (WeeklyStats): a player came in, and an Oryx chain stage's boss fell
        public bool Visited { get; private set; }
        public bool StageBossDown { get; private set; }

        public void BossDied(Enemy boss)
        {
            QuestKills++;
            if (OryxChain.StageBoss.TryGetValue(IdName, out var stageBoss) && boss.ObjectDesc.IdName == stageBoss)
            {
                StageBossDown = true;
                if (stageBoss == OryxChain.LastBoss)
                    WeeklyStats.Count(Shared.database.leaderboard.DbWeeklyStats.ChainsCompleted);
            }
            if (IsRealm || InstanceType != WorldResourceInstanceType.Dungeon || IdName.Contains("Arena") || IdName.Contains("Zombies"))
                return;
            // the Black Between: his children fall on the way, and only the Mysterious One's going
            // clears it; he leaves his own way out (BehaviorDb.BlackBetween.cs), so no Realm Portal
            var blackBetween = IdName == "The Black Between";
            if (blackBetween && boss.ObjectDesc.IdName != "The Mysterious One")
                return;
            // the Candyland: its bosses come again and again; only the Cupcake, which comes once all five
            // kinds have fallen, clears it and opens the way out (RealmEye)
            if (IdName == "Candyland Hunting Grounds" && boss.ObjectDesc.IdName != "Cupcake")
                return;
            var (x, y) = (boss.X, boss.Y);
            StartNewTimer(2000, (world, t) =>
            {
                if (world.Quests.Count == 0 && !world._clearCounted)
                {
                    world._clearCounted = true;
                    quests.QuestTracker.DungeonCleared(world);
                }
                // the Oryx chain leads on, not out: each stage's boss leaves the next one's portal,
                // and the last leads home to the Nexus
                if (world.Quests.Count > 0 || OryxChain.IsStage(world) || blackBetween
                    || world.Portals.Values.Any(p => p.ObjectDesc.IdName is "Realm Portal" or "Random Realm Portal"))
                    return;
                var portal = Entity.Resolve(world.GameServer, "Realm Portal");
                if (portal == null)
                    return;
                portal.Move(x, y);
                world.EnterWorld(portal);
                StaticLogger.Instance.Info($"[{world.IdName} {world.Id}] {boss.ObjectDesc.IdName} left no realm portal; one opened at ({(int)x}, {(int)y}).");
            });
        }

        /// <summary>Where this world's ticks spend their time (logged as "Tick stats").</summary>
        public readonly TickProfile Profile = new TickProfile();

        private long _branchLastTick, _branchLastReport;

        /// <summary>
        /// A branch (a dungeon or vault on its root's thread) finished a tick that began at
        /// <paramref name="started"/> (a Stopwatch timestamp): count it, and every 30 s while anyone
        /// is here log its Tick stats, as RootWorldThread does for root worlds.
        /// </summary>
        public void ReportBranchTick(long started)
        {
            var now = Stopwatch.GetTimestamp();
            var workMs = Stopwatch.GetElapsedTime(started, now).TotalMilliseconds;
            var gapMs = _branchLastTick == 0 ? RootWorldThread.TICK_TIME_MS : Stopwatch.GetElapsedTime(_branchLastTick, started).TotalMilliseconds;
            _branchLastTick = started;
            if (workMs > 200)
                StaticLogger.Instance.Warn($"[{IdName} {Id}] Slow tick: {workMs:0} ms, {Players.Count} players");
            Profile.Tick(workMs, gapMs);
            if (_branchLastReport == 0)
                _branchLastReport = now;
            else if (Stopwatch.GetElapsedTime(_branchLastReport, now).TotalMilliseconds >= 30000)
            {
                var report = Profile.Report();
                if (Players.Count > 0)
                    StaticLogger.Instance.Info($"[{IdName} {Id}] Tick stats, {Players.Count} players, {Enemies.Count} enemies: {report}");
                _branchLastReport = now;
            }
        }

        /// <summary>
        /// Wake monsters only out to just past each player's sight (2 chunks, 32-47 tiles, or 3
        /// for a sight radius over 20) instead of 3 chunks round every player and pet. Realms use
        /// it: most of their thousands of monsters are idle, far from anyone.
        /// </summary>
        protected virtual bool WakesBySight => false;

        private IEnumerable<(float X, float Y, int Radius)> SightCentres()
        {
            // enough chunks (16 tiles) round the player's own chunk to cover its sight from anywhere in
            // it: 1 for the default 15 tiles (16 to 31 tiles awake), 2 up to 32, 3 for more
            foreach (var player in Players.Values)
                yield return (player.X, player.Y, (player.SightRadius + CollisionMap<Entity>.CHUNK_SIZE - 1) / CollisionMap<Entity>.CHUNK_SIZE);
        }

        protected virtual void UpdateLogic(ref TickTime time)
        {
            Profile.Mark(TickProfile.Phase.World);
            foreach (var player in Players.Values)
            {
                var started = Stopwatch.GetTimestamp();
                player.Tick(ref time);
                if (player.Dead)
                    EntitiesToRemove.Add(player);
                // one player holding up everyone's tick: whose, and where
                var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (ms > 200)
                    StaticLogger.Instance.Warn($"[{IdName} {Id}] Slow player tick: {player.Name} {ms:0} ms at ({player.X:0}, {player.Y:0})");
            }
            Profile.Mark(TickProfile.Phase.Players);

            foreach (var sellable in SellableObjects.Values)
            {
                sellable.Tick(ref time);
                if (sellable.Dead)
                    EntitiesToRemove.Add(sellable);
            }

            foreach (var stat in StaticObjects.Values)
            {
                stat.Tick(ref time);
                if (stat.Dead)
                    EntitiesToRemove.Add(stat);
            }

            foreach (var portal in Portals.Values)
            {
                portal.Tick(ref time);
                if (portal.Dead)
                    EntitiesToRemove.Add(portal);
            }

            foreach (var container in Containers.Values)
            {
                container.Tick(ref time);
                if (container.Dead)
                    EntitiesToRemove.Add(container);
            }

            foreach (var pet in Pets.Values)
            {
                pet.Tick(ref time);
                if (pet.Dead)
                    EntitiesToRemove.Add(pet);
            }

            Profile.Mark(TickProfile.Phase.Objects);

            if (EnemiesCollision != null)
            {
                var active = WakesBySight ? EnemiesCollision.GetChunksAround(SightCentres()) : EnemiesCollision.GetActiveChunks(PlayersCollision);
                foreach (var entity in active)
                {
                    // static objects share this map for collision but tick in their own loop above:
                    // ticking them here too ran their behaviour timers twice as fast near players
                    if (entity is not Enemy)
                        continue;
                    entity.Tick(ref time);
                    if (entity.Dead)
                        EntitiesToRemove.Add(entity);
                }
                Profile.CountActive(active.Count);
            }
            else
            {
                foreach (var entity in Enemies.Values)
                {
                    entity.Tick(ref time);
                    if (entity.Dead)
                        EntitiesToRemove.Add(entity);
                }
            }

            Profile.Mark(TickProfile.Phase.Enemies);

            for (var i = Timers.Count - 1; i >= 0; i--)
                try
                {
                    if (Timers[i].Tick(this, ref time))
                        Timers.RemoveAt(i);
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"{e.Message}\n{e.StackTrace}");
                    Timers.RemoveAt(i);
                }

            Profile.Mark(TickProfile.Phase.Timers);

            foreach (var entity in EntitiesToAdd)
                AddToWorld(entity);
            EntitiesToAdd.Clear();

            foreach (var removed in EntitiesToRemove)
                RemoveFromWorld(removed);
            EntitiesToRemove.Clear();

            Profile.Mark(TickProfile.Phase.AddRemove);

            foreach (var player in Players.Values)
                player.UpdateState(time.ElapsedMsDelta);
            Profile.Mark(TickProfile.Phase.Updates);
        }

        public void FlagForClose()
        {
            _forceLifetimeExpire = true;
        }

        private bool _forceLifetimeExpire = false;

        private bool IsPastLifetime(ref TickTime time)
        {
            if (WorldBranch.HasBranches())
                return false;

            if (Players.Count > 0)
                return false;

            if (_forceLifetimeExpire)
                return true;

            if (Persist)
                return false;

            if (Deleted)
                return false;

            if (Lifetime >= 60000)
                return true;
            return false;
        }

        public void OnRemovedFromWorldManager()
        {
            Map.Clear();
        }
    }
}
