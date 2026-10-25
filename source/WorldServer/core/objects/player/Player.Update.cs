using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.player.data.container;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    partial class Player
    {
        // How far (in tiles) the player is sent tiles and objects. Clients whose view reaches
        // further (a large window, a low map scale) ask for more with a VIEW_RADIUS message, up to
        // MAX_VISIBILITY_RADIUS; everyone else keeps the default and its cost.
        public const int VISIBILITY_RADIUS = 15;
        public const int MAX_VISIBILITY_RADIUS = 40;

        public int SightRadius { get; private set; } = VISIBILITY_RADIUS;
        private HashSet<IntPoint> _sightPoints = SightPoints(VISIBILITY_RADIUS);
        private HashSet<IntPoint> _circleCircumferenceSightPoints = CircumferencePoints(VISIBILITY_RADIUS);

        public void SetSightRadius(int radius)
        {
            radius = Math.Clamp(radius, VISIBILITY_RADIUS, MAX_VISIBILITY_RADIUS);
            if (radius == SightRadius)
                return;
            SightRadius = radius;
            _sightPoints = SightPoints(radius);
            _circleCircumferenceSightPoints = CircumferencePoints(radius);
            _needsUpdateTiles = true;
        }

        public int TickId { get; private set; }
        public int TickTime { get; private set; }

        private UpdatedHashSet _newObjects;
        
        // the tiles in sight, worked out when the player steps onto another tile
        private readonly SightSet _activeTiles = new SightSet();
        // the line-of-sight tiles (worlds that block sight), before the map is looked up
        private readonly SightSet _los = new SightSet();
        private readonly HashSet<WmapTile> _newStaticObjects = new HashSet<WmapTile>();
        private readonly Dictionary<int, byte> _seenTiles = new Dictionary<int, byte>();
        private const int SeenArrayMax = 1024 * 1024;
        private byte[] _seenArray;
        private readonly Dictionary<Entity, Dictionary<StatDataType, object>> _statsUpdates = new Dictionary<Entity, Dictionary<StatDataType, object>>();
        private readonly Stack<Dictionary<StatDataType, object>> _statsPool = new Stack<Dictionary<StatDataType, object>>();
        
        private bool _needsUpdateTiles = true;
        
        public void InitializeUpdate()
        {
            _newObjects = new UpdatedHashSet(this);
        }

        public void GetDrops(Update update)
        {
            var drops = new List<int>();
            var staticDrops = new List<int>();

            foreach (var staticTile in _newStaticObjects)
            {
                if (staticTile.ObjType == 0)
                    if (staticTile.ObjId != 0)
                    {
                        update.Drops.Add(staticTile.ObjId);
                        staticDrops.Add(staticTile.ObjId);
                    }
            }

            foreach (var entity in _newObjects)
            {
                if (entity == Quest && !entity.Dead)
                    continue;

                if (entity.Dead)
                {
                    drops.Add(entity.Id);
                    update.Drops.Add(entity.Id);
                    continue;
                }

                if (entity is Player && (entity as Player).CanBeSeenBy(this))
                    continue;

                if (entity is Player || _activeTiles.Contains((int)entity.X, (int)entity.Y))
                    continue;

                drops.Add(entity.Id);
                update.Drops.Add(entity.Id);
            }

            if (drops.Count != 0)
                _newObjects.RemoveWhere(_ => drops.Contains(_.Id));
            if (staticDrops.Count != 0)
                _ = _newStaticObjects.RemoveWhere(_ => staticDrops.Contains(_.ObjId));
        }

        public void HandleStatChanges(object entity, StatChangedEventArgs statChange)
        {
            if (!(entity is Entity e) || e.Id != Id && statChange.UpdateSelfOnly)
                return;

            if (e.Id == Id && statChange.Stat == StatDataType.None)
                return;

            lock (_statsUpdates)
            {
                // every moving monster in sight comes here each tick: its dictionary comes back
                // to the pool once the tick is sent, rather than a new one each time
                if (!_statsUpdates.TryGetValue(e, out var stats))
                    _statsUpdates[e] = stats = _statsPool.Count > 0 ? _statsPool.Pop() : new Dictionary<StatDataType, object>();

                if (statChange.Stat != StatDataType.None)
                    stats[statChange.Stat] = statChange.Value;
            }
        }

        public void UpdateTiles() => _needsUpdateTiles = true;

        // the tile the sight was last worked out from
        private int _sightX = int.MinValue, _sightY = int.MinValue;

        /// <summary>
        /// The player moved: the tiles and objects in sight change only when it steps onto another
        /// tile (working them out is ~700 tiles at the default radius, 5,000 at the largest).
        /// </summary>
        public void UpdateTilesIfMoved()
        {
            if ((int)X != _sightX || (int)Y != _sightY)
                _needsUpdateTiles = true;
        }

        public void UpdateState(int dt)
        {
            TickId++;
            TickTime = dt;

            HandleUpdate();
            HandleNewTick();
        }

        private void HandleUpdate()
        {
            var update = new Update();
            if (_needsUpdateTiles)
            {
                GetNewTiles(update);
                // the map's own objects (trees, walls, signs) change only with the tiles
                GetNewStaticObjects(update);
                _needsUpdateTiles = false;
            }
            GetNewObjects(update);
            GetDrops(update);

            if (update.Tiles.Count == 0 && update.NewObjs.Count == 0 && update.Drops.Count == 0)
                return;
            Client.SendPacket(update);
        }

        public void GetNewTiles(Update update)
        {
            var px = _sightX = (int)X;
            var py = _sightY = (int)Y;
            _activeTiles.Reset(px, py);
            switch (World.Blocking)
            {
                case 1:
                    CalculateLineOfSight(px, py);
                    foreach (var point in _los.Points)
                        AddSightTile(update, point.X, point.Y);
                    break;
                case 2:
                    _pathPoints.Clear();
                    CalculatePath(_pathPoints);
                    foreach (var point in _pathPoints)
                        AddSightTile(update, point.X + px, point.Y + py);
                    break;
                default:
                    foreach (var point in _sightPoints)
                        AddSightTile(update, point.X + px, point.Y + py);
                    break;
            }
            FameCounter.TileSent(update.Tiles.Count); // adds the new amount to the tiles been sent
        }

        private void AddSightTile(Update update, int x, int y)
        {
            if (!_activeTiles.Add(x, y))
                return;

            var map = World.Map;
            var tile = map[x, y];
            if (!tile.Exists || tile.TileId == 0xFF)
                return;

            // what the client has of the tile: an array for maps up to 1024 x 1024 (dungeons, the
            // Arena, the Nexus: an index instead of a hash), the dictionary for realms (2048 x 2048
            // would be 4 MB a player)
            if (map.Width * map.Height <= SeenArrayMax)
            {
                var seen = _seenArray ??= new byte[map.Width * map.Height];
                var i = x + y * map.Width;
                if (seen[i] >= tile.UpdateCount)
                    return;
                seen[i] = tile.UpdateCount;
            }
            else
            {
                var hash = x << 16 | y;
                _ = _seenTiles.TryGetValue(hash, out var updateCount);
                if (updateCount >= tile.UpdateCount)
                    return;
                _seenTiles[hash] = tile.UpdateCount;
            }
            update.Tiles.Add(new TileData(x, y, tile.TileId));
        }

        private readonly HashSet<IntPoint> _pathPoints = new HashSet<IntPoint>();

        /// <summary>
        /// The tiles seen from (px, py) where objects block sight: a line to each tile round the
        /// edge of the sight, stopping at the first that blocks it (Bresenham, as DrawLine). Into
        /// a reused SightSet, without a callback per tile: in the Arena, with players stepping onto
        /// another tile every few ticks, this was most of the tick.
        /// </summary>
        private void CalculateLineOfSight(int px, int py)
        {
            _los.Reset(px, py);
            var map = World.Map;
            foreach (var point in _circleCircumferenceSightPoints)
            {
                int x = px, y = py;
                var w = point.X;
                var h = point.Y;
                int dx1 = Math.Sign(w), dy1 = Math.Sign(h), dx2 = Math.Sign(w), dy2 = 0;
                var longest = Math.Abs(w);
                var shortest = Math.Abs(h);
                if (!(longest > shortest))
                {
                    longest = Math.Abs(h);
                    shortest = Math.Abs(w);
                    dy2 = Math.Sign(h);
                    dx2 = 0;
                }

                var numerator = longest >> 1;
                for (var i = 0; i <= longest; i++)
                {
                    _los.Add(x, y);
                    if (map.Contains(x, y))
                    {
                        var t = map[x, y];
                        if (t.ObjType != 0 && t.ObjDesc != null && t.ObjDesc.BlocksSight)
                            break;
                    }

                    numerator += shortest;
                    if (!(numerator < longest))
                    {
                        numerator -= longest;
                        x += dx1;
                        y += dy1;
                    }
                    else
                    {
                        x += dx2;
                        y += dy2;
                    }
                }
            }
        }

        public void CalculatePath(HashSet<IntPoint> points)
        {
            var px = (int)X;
            var py = (int)Y;

            var pathMap = new bool[World.Map.Width, World.Map.Height];
            StepPath(points, pathMap, px, py, px, py);
        }

        private void StepPath(HashSet<IntPoint> points, bool[,] pathMap, int x, int y, int px, int py)
        {
            if (!World.Map.Contains(x, y))
                return;

            if (pathMap[x, y])
                return;
            pathMap[x, y] = true;

            var point = new IntPoint(x - px, y - py);
            if (!_sightPoints.Contains(point))
                return;
            _ = points.Add(point);

            var t = World.Map[x, y];
            if (!(t.ObjType != 0 && t.ObjDesc != null && t.ObjDesc.BlocksSight))
                foreach (var p in _surroundingPoints)
                    StepPath(points, pathMap, x + p.X, y + p.Y, px, py);
        }

        private void GetNewStaticObjects(Update update)
        {
            foreach (var point in _activeTiles.Points)
            {
                var pointX = point.X;
                var pointY = point.Y;

                var tile = World.Map[pointX, pointY];
                if (!tile.Exists)
                    continue;

                if (tile.ObjId != 0 && tile.ObjType != 0 && _newStaticObjects.Add(tile))
                    update.NewObjs.Add(tile.ToObjectDef(pointX, pointY));
            }
        }

        public void GetNewObjects(Update update)
        {
            var x = X;
            var y = Y;

            var players = World.GetPlayers();

            var count = 0;
            foreach (var player in players)
            {
                if ((player.AccountId == AccountId || player.Client.Account != null && player.CanBeSeenBy(this)) && _newObjects.Add(player))
                {
                    update.NewObjs.Add(player.ToDefinition(player.AccountId != AccountId));
                    count++;

                    if (count > 12) // 12 players per tick max
                        break;
                }
            }

            foreach (var entity in World.PlayersCollision.HitTest(x, y, SightRadius))
                if ((entity is Decoy || entity is Pet) && _newObjects.Add(entity))
                    update.NewObjs.Add(entity.ToDefinition());

            var intPoint = new IntPoint(0, 0);
            foreach (var entity in World.EnemiesCollision.HitTest(x, y, SightRadius))
            {
                if (entity.Dead || entity is Container)
                    continue;

                intPoint.X = (int)entity.X;
                intPoint.Y = (int)entity.Y;

                if (_activeTiles.Contains(intPoint.X, intPoint.Y) && _newObjects.Add(entity))
                    update.NewObjs.Add(entity.ToDefinition());
            }

            foreach (var entry in World.Containers)
            {
                var entity = entry.Value;
                var owners = entity.BagOwners;
                if (owners.Length > 0 && Array.IndexOf(owners, AccountId) == -1)
                    continue;

                intPoint.X = (int)entity.X;
                intPoint.Y = (int)entity.Y;
                if (_activeTiles.Contains(intPoint.X, intPoint.Y) && _newObjects.Add(entity))
                    update.NewObjs.Add(entity.ToDefinition());
            }

            foreach (var entity in World.Portals.Values)
            {
                intPoint.X = (int)entity.X;
                intPoint.Y = (int)entity.Y;

                if (_activeTiles.Contains(intPoint.X, intPoint.Y) && _newObjects.Add(entity))
                    update.NewObjs.Add(entity.ToDefinition());
            }

            foreach (var entity in World.SellableObjects.Values)
            {
                intPoint.X = (int)entity.X;
                intPoint.Y = (int)entity.Y;

                if (_activeTiles.Contains(intPoint.X, intPoint.Y) && _newObjects.Add(entity))
                    update.NewObjs.Add(entity.ToDefinition());
            }

            if (Quest != null && _newObjects.Add(Quest))
                update.NewObjs.Add(Quest.ToDefinition());
        }

        private void HandleNewTick()
        {
            var newTick = new NewTickMessage(TickId, TickTime);

            lock (_statsUpdates)
            {
                var statuses = new List<ObjectStats>(_statsUpdates.Count);
                foreach (var (entity, stats) in _statsUpdates)
                {
                    statuses.Add(new ObjectStats()
                    {
                        Id = entity.Id,
                        X = entity.X,
                        Y = entity.Y,
                        Stats = stats.Count == 0 ? NoStats : stats.ToArray()
                    });
                    stats.Clear();
                    if (_statsPool.Count < 512)
                        _statsPool.Push(stats);
                }
                newTick.Statuses = statuses;
                _statsUpdates.Clear();
            }

            Client.SendPacket(newTick);
            AwaitMove(TickId);
        }

        private static readonly KeyValuePair<StatDataType, object>[] NoStats = Array.Empty<KeyValuePair<StatDataType, object>>();

        public void CleanupPlayerUpdate()
        {
            _seenTiles.Clear();
            _seenArray = null;
            _activeTiles.Reset(0, 0);
            _newStaticObjects.Clear();
            _statsUpdates.Clear();
            _newObjects.Dispose();
        }

        private static readonly IntPoint[] _surroundingPoints = new IntPoint[5]
        {
            new IntPoint(0, 0),
            new IntPoint(1, 0),
            new IntPoint(0, 1),
            new IntPoint(-1, 0),
            new IntPoint(0, -1)
        };

        // Sight shapes per radius, shared by every player using that radius (read-only).
        private static readonly Dictionary<int, HashSet<IntPoint>> _sightPointsByRadius = new Dictionary<int, HashSet<IntPoint>>();
        private static readonly Dictionary<int, HashSet<IntPoint>> _circumferenceByRadius = new Dictionary<int, HashSet<IntPoint>>();

        private static HashSet<IntPoint> SightPoints(int radius)
        {
            lock (_sightPointsByRadius)
            {
                if (!_sightPointsByRadius.TryGetValue(radius, out var ret))
                    _sightPointsByRadius[radius] = ret = Cache(radius, false);
                return ret;
            }
        }

        private static HashSet<IntPoint> CircumferencePoints(int radius)
        {
            lock (_circumferenceByRadius)
            {
                if (!_circumferenceByRadius.TryGetValue(radius, out var ret))
                    _circumferenceByRadius[radius] = ret = Cache(radius, true);
                return ret;
            }
        }

        private static HashSet<IntPoint> Cache(int radius, bool circumferenceCheck)
        {
            var ret = new HashSet<IntPoint>();
            var radiusSqr = radius * radius;
            var innerSqr = (radius - 2) * (radius - 2);
            for (var x = -radius; x <= radius; x++)
                for (var y = -radius; y <= radius; y++)
                {
                    var flag = x * x + y * y <= radiusSqr;
                    if (circumferenceCheck)
                        flag &= x * x + y * y > innerSqr;
                    if (flag)
                        _ = ret.Add(new IntPoint(x, y));
                }

            return ret;
        }

        /// <summary>
        /// Tiles round a centre, out to the largest sight, as stamps in a reused array: adding and
        /// looking up is an index, where the hash sets they replace hashed every tile, and nothing
        /// is allocated when they're worked out again. Points lists what was added.
        /// </summary>
        private sealed class SightSet
        {
            private const int Size = 2 * MAX_VISIBILITY_RADIUS + 3;
            private readonly int[] _stamps = new int[Size * Size];
            private int _stamp = 1;
            private int _cx, _cy;
            public readonly List<IntPoint> Points = new List<IntPoint>();

            public void Reset(int cx, int cy)
            {
                _cx = cx;
                _cy = cy;
                Points.Clear();
                if (++_stamp == int.MaxValue)
                {
                    Array.Clear(_stamps);
                    _stamp = 1;
                }
            }

            private int Index(int x, int y)
            {
                var i = x - _cx + Size / 2;
                var j = y - _cy + Size / 2;
                return (uint)i < Size && (uint)j < Size ? i + j * Size : -1;
            }

            public bool Add(int x, int y)
            {
                var k = Index(x, y);
                if (k < 0 || _stamps[k] == _stamp)
                    return false;
                _stamps[k] = _stamp;
                Points.Add(new IntPoint(x, y));
                return true;
            }

            public bool Contains(int x, int y)
            {
                var k = Index(x, y);
                return k >= 0 && _stamps[k] == _stamp;
            }
        }
    }
}
