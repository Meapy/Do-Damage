using Ionic.Zlib;
using NLog;
using Shared.resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shared.terrain
{
    // Builds a new layout for a dungeon out of the rooms of its hand-made map. The map is cut into
    // rooms (wide areas, with the monsters and objects standing in them) and corridors (narrow
    // passages), then the rooms are joined again in a random tree with straight corridors: the
    // spawn room is the root and every goal room (boss, key room) ends a branch. Each instance
    // picks a size, which sets how many of the other rooms are used and how long the corridors get.
    public static class DungeonGenerator
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<string, Source> Sources = new ConcurrentDictionary<string, Source>(StringComparer.OrdinalIgnoreCase);

        private const int Attempts = 40;
        private const int AttachTries = 40;
        private const int CanvasSize = 640;
        private const int Margin = 4;
        private const int Spacing = 2;      // empty tiles kept around a room's walls
        private const int DoorDepth = 3;    // how far a corridor may tunnel into a room's bounds
        private const int ShortGap = 6;     // longest corridor gap in a small or medium layout

        public enum Size { Small, Medium, Large }

        // small 30%, medium 60%, large 10%
        public static Size PickSize(Random rand)
        {
            var roll = rand.Next(100);
            return roll < 30 ? Size.Small : roll < 90 ? Size.Medium : Size.Large;
        }

        private static readonly int[] Dx = { 1, 0, -1, 0 };
        private static readonly int[] Dy = { 0, 1, 0, -1 };

        // a new map in the same format as the converted .jm files, or null to use the fixed map
        public static byte[] Generate(XmlData data, string mapName, byte[] mapData, RandomLayoutDesc desc, Random rand)
        {
            try
            {
                var source = Sources.GetOrAdd(mapName, _ => Analyse(data, Import(mapData), desc, mapName));
                if (source == null)
                    return null;

                var size = PickSize(rand);
                var share = size switch { Size.Small => desc.Small, Size.Medium => desc.Medium, _ => desc.Large };
                var rooms = Math.Clamp((int)Math.Round(source.Fillers.Count * share), Math.Min(source.Fillers.Count, 2), source.Fillers.Count);
                var maxGap = size == Size.Large ? desc.MaxGap : Math.Max(desc.MinGap, Math.Min(desc.MaxGap, ShortGap));
                for (var i = 0; i < Attempts; i++)
                {
                    var tiles = new Builder(source, desc, rand, rooms, maxGap).Build();
                    if (tiles != null)
                    {
                        // where the goals ended up, for finding the boss on a test stack (/tppos)
                        var goals = new List<string>();
                        for (var x = 0; x < tiles.GetLength(0); x++)
                            for (var y = 0; y < tiles.GetLength(1); y++)
                                if (desc.Goals.Contains(tiles[x, y].TileObj ?? "", StringComparer.OrdinalIgnoreCase))
                                    goals.Add($"{tiles[x, y].TileObj} ({x}, {y})");
                        Log.Info("Random layout for '{0}': {1}, {2} of {3} rooms{4}.", mapName, size.ToString().ToLowerInvariant(), rooms, source.Fillers.Count,
                            goals.Count > 0 ? "; " + string.Join(", ", goals) : "");
                        return WorldMapExporter.Export(tiles);
                    }
                }
                Log.Warn("Random layout for '{0}' failed {1} times; using the fixed map.", mapName, Attempts);
            }
            catch (Exception e)
            {
                Log.Error(e, "Random layout for '{0}' failed; using the fixed map.", mapName);
            }
            return null;
        }

        public static TerrainTile[,] Import(byte[] mapData)
        {
            var ver = mapData[0];
            using var rdr = new BinaryReader(new ZlibStream(new MemoryStream(mapData, 1, mapData.Length - 1), CompressionMode.Decompress));

            var dict = new TerrainTile[rdr.ReadInt16()];
            for (var i = 0; i < dict.Length; i++)
            {
                dict[i] = new TerrainTile()
                {
                    TileId = rdr.ReadUInt16(),
                    TileObj = rdr.ReadString(),
                    Name = rdr.ReadString(),
                    Terrain = (TerrainType)rdr.ReadByte(),
                    Region = (TileRegion)rdr.ReadByte()
                };
                if (ver == 1)
                    dict[i].Elevation = rdr.ReadByte();
            }

            var w = rdr.ReadInt32();
            var h = rdr.ReadInt32();
            var tiles = new TerrainTile[w, h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var tile = dict[rdr.ReadInt16()];
                    if (ver == 2)
                        tile.Elevation = rdr.ReadByte();
                    tiles[x, y] = tile;
                }
            return tiles;
        }

        private sealed class Door
        {
            public int Perp;        // first lane, across the side
            public int[] Outer;     // per lane: the outermost floor of the room, along the side's direction
        }

        private sealed class Room
        {
            public int W, H;
            public TerrainTile[,] Tiles;
            public bool[,] Has;     // part of the room (floor, objects and the walls around them)
            public bool[,] Area;    // the room's floor
            public bool[,] Halo;    // Has grown by Spacing, offset by Spacing
            public bool Start, Goal;
            public readonly List<Door>[] Doors = new List<Door>[4];
        }

        private sealed class Source
        {
            public Room Start;
            public List<Room> Goals;
            public List<Room> Fillers;
            public TerrainTile Void, Floor;
            public TerrainTile? Wall;
            public Func<TerrainTile, bool> Walkable;
        }

        private static Source Analyse(XmlData data, TerrainTile[,] map, RandomLayoutDesc desc, string mapName)
        {
            var w = map.GetLength(0);
            var h = map.GetLength(1);

            bool IsWallObject(string id)
            {
                if (string.IsNullOrEmpty(id) || !data.IdToObjectType.TryGetValue(id, out var type) || !data.ObjectDescs.TryGetValue(type, out var od))
                    return false;
                return od.Class is "Wall" or "ConnectedWall" or "CaveWall" || (od.FullOccupy && od.Static && !od.Enemy);
            }
            bool NoWalk(TerrainTile t) => t.TileId == 0xFF || (data.Tiles.TryGetValue(t.TileId, out var td) && td.NoWalk);
            bool Walkable(TerrainTile t) => !NoWalk(t) && !IsWallObject(t.TileObj);
            // monsters that can stand anywhere (not blockers, which belong where they were placed)
            bool IsMonster(string id) => !string.IsNullOrEmpty(id) && data.IdToObjectType.TryGetValue(id, out var type)
                && data.ObjectDescs.TryGetValue(type, out var od) && od.Enemy && !od.OccupySquare && !od.EnemyOccupySquare && !od.FullOccupy;

            var open = new bool[w, h];
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    open[x, y] = Walkable(map[x, y]);
            bool O(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && open[x, y];

            // what the player can reach from the spawn
            var spawn = new List<(int, int)>();
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    if (map[x, y].Region == TileRegion.Spawn)
                        spawn.Add((x, y));
            if (spawn.Count == 0)
            {
                Log.Warn("Random layout: '{0}' has no spawn.", mapName);
                return null;
            }
            var reach = Flood(w, h, spawn, (x, y) => O(x, y) || map[x, y].Region == TileRegion.Spawn);

            // rooms: what survives shaving off everything within r of a wall, grown back by r
            var r = Math.Max(1, (desc.Passage + 1) / 2);
            var core = new bool[w, h];
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    var all = true;
                    for (var i = -r; i <= r && all; i++)
                        for (var j = -r; j <= r && all; j++)
                            all = O(x + i, y + j);
                    core[x, y] = all;
                }
            var opened = new bool[w, h];
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    if (!open[x, y])
                        continue;
                    for (var i = -r; i <= r && !opened[x, y]; i++)
                        for (var j = -r; j <= r && !opened[x, y]; j++)
                            opened[x, y] = x + i >= 0 && y + j >= 0 && x + i < w && y + j < h && core[x + i, y + j];
                }

            var label = new int[w, h];
            var areas = new List<List<(int x, int y)>>();
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    if (opened[x, y] && label[x, y] == 0)
                    {
                        var cells = Component(w, h, x, y, (a, b) => opened[a, b] && label[a, b] == 0);
                        areas.Add(cells);
                        foreach (var (a, b) in cells)
                            label[a, b] = areas.Count;
                    }

            // the corridors: dead ends belong to the one room they open into; the rest are dropped, but
            // the monsters standing in them move into a room next to them
            var corridorTiles = new List<TerrainTile>();
            var strays = new Dictionary<int, List<TerrainTile>>();
            var dropped = 0;
            var seen = new bool[w, h];
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    if (!open[x, y] || label[x, y] != 0 || seen[x, y] || !reach[x, y])
                        continue;
                    var cells = Component(w, h, x, y, (a, b) => open[a, b] && label[a, b] == 0 && !seen[a, b]);
                    var touches = new HashSet<int>();
                    foreach (var (a, b) in cells)
                    {
                        seen[a, b] = true;
                        for (var d = 0; d < 4; d++)
                        {
                            var nx = a + Dx[d];
                            var ny = b + Dy[d];
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h && label[nx, ny] != 0)
                                touches.Add(label[nx, ny]);
                        }
                    }
                    if (touches.Count == 1)
                    {
                        var id = touches.First();
                        areas[id - 1].AddRange(cells);
                        foreach (var (a, b) in cells)
                            label[a, b] = id;
                    }
                    else
                        foreach (var (a, b) in cells)
                        {
                            corridorTiles.Add(map[a, b]);
                            if (string.IsNullOrEmpty(map[a, b].TileObj))
                                continue;
                            if (touches.Count > 0 && IsMonster(map[a, b].TileObj))
                            {
                                var id = touches.Min();
                                if (!strays.ContainsKey(id))
                                    strays[id] = new List<TerrainTile>();
                                strays[id].Add(map[a, b]);
                            }
                            else
                                dropped++;
                        }
                }

            var source = new Source() { Goals = new List<Room>(), Fillers = new List<Room>(), Walkable = Walkable };

            // the background: what fills the map outside the dungeon
            var outside = new List<TerrainTile>();
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    if (!reach[x, y] && NoWalk(map[x, y]) && string.IsNullOrEmpty(map[x, y].TileObj))
                        outside.Add(map[x, y]);
            source.Void = outside.Count > 0 ? MostCommon(outside) : new TerrainTile() { TileId = 0xFF, TileObj = "", Name = "" };

            var wallTiles = new List<TerrainTile>();
            var floorTiles = new List<TerrainTile>();
            var goals = new HashSet<string>(desc.Goals, StringComparer.OrdinalIgnoreCase);
            var rooms = new List<Room>();
            var spawnRooms = new Dictionary<Room, int>();

            for (var a = 0; a < areas.Count; a++)
            {
                var cells = areas[a];
                if (cells.Count < 9 || !cells.Any(c => reach[c.x, c.y]))
                    continue;

                var minX = cells.Min(c => c.x) - 1;
                var minY = cells.Min(c => c.y) - 1;
                var room = new Room()
                {
                    W = cells.Max(c => c.x) + 2 - minX,
                    H = cells.Max(c => c.y) + 2 - minY
                };
                room.Tiles = new TerrainTile[room.W, room.H];
                room.Has = new bool[room.W, room.H];
                var area = room.Area = new bool[room.W, room.H];

                foreach (var (x, y) in cells)
                {
                    area[x - minX, y - minY] = true;
                    room.Has[x - minX, y - minY] = true;
                    room.Tiles[x - minX, y - minY] = map[x, y];
                    if (string.IsNullOrEmpty(map[x, y].TileObj) && map[x, y].Region == TileRegion.None)
                        floorTiles.Add(map[x, y]);
                    if (map[x, y].Region == TileRegion.Spawn)
                        spawnRooms[room] = spawnRooms.GetValueOrDefault(room) + 1;
                    if (goals.Contains(map[x, y].TileObj ?? ""))
                        room.Goal = true;
                }

                // pools and pits inside it come along
                var inside = new List<(int x, int y)>(cells);
                var pool = new Stack<(int x, int y)>(cells);
                while (pool.Count > 0)
                {
                    var (x, y) = pool.Pop();
                    for (var d = 0; d < 4; d++)
                    {
                        int nx = x + Dx[d], ny = y + Dy[d], lx = nx - minX, ly = ny - minY;
                        if (lx < 0 || ly < 0 || lx >= room.W || ly >= room.H || nx >= w || ny >= h || nx < 0 || ny < 0 || room.Has[lx, ly])
                            continue;
                        var t = map[nx, ny];
                        if (t.TileId == 0xFF || !NoWalk(t) || IsWallObject(t.TileObj))
                            continue;
                        room.Has[lx, ly] = true;
                        room.Tiles[lx, ly] = t;
                        inside.Add((nx, ny));
                        pool.Push((nx, ny));
                    }
                }

                // the walls around it; openings into other areas are walled up
                foreach (var (x, y) in inside)
                    for (var i = -1; i <= 1; i++)
                        for (var j = -1; j <= 1; j++)
                        {
                            int lx = x + i - minX, ly = y + j - minY;
                            if (lx < 0 || ly < 0 || lx >= room.W || ly >= room.H || room.Has[lx, ly] || x + i < 0 || y + j < 0 || x + i >= w || y + j >= h)
                                continue;
                            var t = map[x + i, y + j];
                            if (open[x + i, y + j])
                                continue;   // filled in once the wall tile is known
                            room.Has[lx, ly] = true;
                            room.Tiles[lx, ly] = t;
                            if (!string.IsNullOrEmpty(t.TileObj))
                                wallTiles.Add(t);
                        }

                if (strays.TryGetValue(a + 1, out var movers))
                {
                    var rand = new Random(a);
                    var free = cells.Where(c => string.IsNullOrEmpty(map[c.x, c.y].TileObj) && map[c.x, c.y].Region == TileRegion.None)
                        .OrderBy(_ => rand.Next()).ToList();
                    for (var i = 0; i < movers.Count && i < free.Count; i++)
                    {
                        ref var tile = ref room.Tiles[free[i].x - minX, free[i].y - minY];
                        tile.TileObj = movers[i].TileObj;
                        tile.Name = movers[i].Name;
                    }
                }

                for (var d = 0; d < 4; d++)
                    room.Doors[d] = FindDoors(room, area, d, desc.Corridor);
                rooms.Add(room);
            }

            if (spawnRooms.Count == 0)
            {
                Log.Warn("Random layout: the spawn of '{0}' isn't in a room.", mapName);
                return null;
            }

            source.Wall = wallTiles.Count == 0 ? null : MostCommon(wallTiles);
            source.Floor = corridorTiles.Count(t => string.IsNullOrEmpty(t.TileObj) && t.Region == TileRegion.None) > 0
                ? MostCommon(corridorTiles.Where(t => string.IsNullOrEmpty(t.TileObj) && t.Region == TileRegion.None))
                : MostCommon(floorTiles);

            // close the walled-up openings
            foreach (var room in rooms)
                for (var x = 0; x < room.W; x++)
                    for (var y = 0; y < room.H; y++)
                        if (!room.Has[x, y] && NextToFloor(room, x, y))
                        {
                            room.Has[x, y] = true;
                            room.Tiles[x, y] = source.Wall ?? source.Void;
                        }

            source.Start = spawnRooms.OrderByDescending(kv => kv.Value).First().Key;
            source.Start.Start = true;
            foreach (var room in rooms)
            {
                room.Halo = new bool[room.W + 2 * Spacing, room.H + 2 * Spacing];
                for (var x = 0; x < room.W; x++)
                    for (var y = 0; y < room.H; y++)
                        if (room.Has[x, y])
                            for (var i = 0; i <= 2 * Spacing; i++)
                                for (var j = 0; j <= 2 * Spacing; j++)
                                    room.Halo[x + i, y + j] = true;

                if (room.Start)
                    continue;
                if (room.Goal)
                    source.Goals.Add(room);
                else
                    source.Fillers.Add(room);
            }

            Log.Info("Random layout: '{0}' has {1} rooms ({2} goal rooms){3}.", mapName, rooms.Count, source.Goals.Count,
                dropped > 0 ? $", {dropped} objects in its corridors are left out" : "");
            if (desc.Goals.Length > 0 && source.Goals.Count == 0 && !source.Start.Goal)
            {
                Log.Warn("Random layout: no goal room found in '{0}'.", mapName);
                return null;
            }
            return source;
        }

        private static bool NextToFloor(Room room, int x, int y)
        {
            for (var i = -1; i <= 1; i++)
                for (var j = -1; j <= 1; j++)
                {
                    int nx = x + i, ny = y + j;
                    if (nx >= 0 && ny >= 0 && nx < room.W && ny < room.H && room.Area[nx, ny])
                        return true;
                }
            return false;
        }

        // places a corridor can leave the room through side d: lanes of floor reaching (nearly) the room's bounds
        private static List<Door> FindDoors(Room room, bool[,] area, int d, int width)
        {
            var horizontal = d % 2 == 0;
            var span = horizontal ? room.H : room.W;
            var depth = horizontal ? room.W : room.H;
            var outer = new int[span];
            var dist = new int[span];

            for (var p = 0; p < span; p++)
            {
                outer[p] = -1;
                for (var k = 0; k < depth; k++)
                {
                    // walk inwards from side d
                    var along = d < 2 ? depth - 1 - k : k;
                    var x = horizontal ? along : p;
                    var y = horizontal ? p : along;
                    if (area[x, y])
                    {
                        outer[p] = along;
                        dist[p] = k;
                        break;
                    }
                }
            }

            var best = Enumerable.Range(0, span).Where(p => outer[p] >= 0).Select(p => dist[p]).DefaultIfEmpty(int.MaxValue).Min();

            // lanes of floor reaching the side, within `slack` of its outermost floor; `flush` also wants
            // the floor on either side of the doorway that far out, so the corridor meets a straight wall
            List<Door> Find(int slack, bool flush)
            {
                var found = new List<Door>();
                for (var p = 1; p + width < span; p++)
                {
                    var ok = true;
                    for (var i = flush ? -1 : 0; i < (flush ? width + 1 : width) && ok; i++)
                        ok = outer[p + i] >= 0 && dist[p + i] <= best + slack;
                    if (ok)
                        found.Add(new Door() { Perp = p, Outer = Enumerable.Range(0, width).Select(i => outer[p + i]).ToArray() });
                }
                return found;
            }

            var doors = Find(0, true);
            if (doors.Count == 0)
                doors = Find(DoorDepth, false);
            return doors;
        }

        private static List<(int x, int y)> Component(int w, int h, int sx, int sy, Func<int, int, bool> member)
        {
            var cells = new List<(int, int)>();
            var stack = new Stack<(int, int)>();
            var mark = new HashSet<(int, int)>() { (sx, sy) };
            stack.Push((sx, sy));
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                cells.Add((x, y));
                for (var d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && !mark.Contains((nx, ny)) && member(nx, ny))
                    {
                        mark.Add((nx, ny));
                        stack.Push((nx, ny));
                    }
                }
            }
            return cells;
        }

        private static bool[,] Flood(int w, int h, List<(int, int)> starts, Func<int, int, bool> passable)
        {
            var seen = new bool[w, h];
            var queue = new Queue<(int, int)>();
            foreach (var (x, y) in starts)
            {
                seen[x, y] = true;
                queue.Enqueue((x, y));
            }
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                for (var d = 0; d < 4; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && !seen[nx, ny] && passable(nx, ny))
                    {
                        seen[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            return seen;
        }

        private static TerrainTile MostCommon(IEnumerable<TerrainTile> tiles) =>
            tiles.GroupBy(t => (t.TileId, t.TileObj)).OrderByDescending(g => g.Count()).First().First();

        private sealed class Builder
        {
            private readonly Source Source;
            private readonly RandomLayoutDesc Desc;
            private readonly Random Rand;
            private readonly int Rooms;     // how many of the source's fillers to use
            private readonly int MaxGap;
            private readonly int[,] Owner = new int[CanvasSize, CanvasSize];   // 0 empty, -1 corridor, else room id
            private readonly List<(Room room, int x, int y)> Placed = new List<(Room, int, int)>();
            private readonly Dictionary<Room, (int x, int y)> Pos = new Dictionary<Room, (int, int)>();
            private readonly List<(int x0, int y0, int x1, int y1)> Lanes = new List<(int, int, int, int)>();
            private readonly Dictionary<Room, List<(int d, int from, int to)>> UsedSides = new Dictionary<Room, List<(int, int, int)>>();

            public Builder(Source source, RandomLayoutDesc desc, Random rand, int rooms, int maxGap)
            {
                Source = source;
                Desc = desc;
                Rand = rand;
                Rooms = rooms;
                MaxGap = maxGap;
            }

            public TerrainTile[,] Build()
            {
                var start = Source.Start;
                if (!Fits(start, (CanvasSize - start.W) / 2, (CanvasSize - start.H) / 2))
                    return null;
                Place(start, (CanvasSize - start.W) / 2, (CanvasSize - start.H) / 2);

                var fillers = Source.Fillers.OrderBy(_ => Rand.Next()).Take(Rooms).ToList();
                var goals = Source.Goals.OrderBy(_ => Rand.Next()).ToList();
                var chains = Math.Max(1, goals.Count);
                var main = (int)Math.Round(fillers.Count * Desc.MainPath);
                var spurs = new List<Room>();

                var next = 0;
                for (var c = 0; c < chains; c++)
                {
                    var count = main / chains + (c < main % chains ? 1 : 0);
                    var chain = fillers.Skip(next).Take(count).ToList();
                    next += count;
                    if (c < goals.Count)
                        chain.Add(goals[c]);

                    var prev = start;
                    var placedInChain = new List<Room>();
                    foreach (var room in chain)
                    {
                        // keep growing the branch; when it's boxed in, branch off somewhere else
                        var anchors = new[] { prev }
                            .Concat(placedInChain.AsEnumerable().Reverse())
                            .Concat(Placed.Select(p => p.room).Where(p => !p.Goal).OrderBy(_ => Rand.Next()))
                            .Distinct()
                            .ToList();
                        var ok = anchors.Any(a => !a.Goal && Attach(a, room));
                        if (!ok)
                        {
                            if (room.Goal)
                                return null;
                            spurs.Add(room);
                            continue;
                        }
                        placedInChain.Add(room);
                        prev = room;
                    }
                }

                // every room picked has to fit, or its monsters would be missing
                spurs.AddRange(fillers.Skip(next));
                foreach (var room in spurs)
                    if (!Placed.Select(p => p.room).Where(p => !p.Goal).OrderBy(_ => Rand.Next()).ToList().Any(a => Attach(a, room)))
                        return null;

                return Render();
            }

            private bool Fits(Room room, int ox, int oy)
            {
                if (ox - Spacing < Margin || oy - Spacing < Margin || ox + room.W + Spacing >= CanvasSize - Margin || oy + room.H + Spacing >= CanvasSize - Margin)
                    return false;
                for (var x = 0; x < room.W + 2 * Spacing; x++)
                    for (var y = 0; y < room.H + 2 * Spacing; y++)
                        if (room.Halo[x, y] && Owner[ox - Spacing + x, oy - Spacing + y] != 0)
                            return false;
                return true;
            }

            private void Place(Room room, int ox, int oy)
            {
                Placed.Add((room, ox, oy));
                Pos[room] = (ox, oy);
                UsedSides[room] = new List<(int, int, int)>();
                for (var x = 0; x < room.W; x++)
                    for (var y = 0; y < room.H; y++)
                        if (room.Has[x, y])
                            Owner[ox + x, oy + y] = Placed.Count;
            }

            private bool SideFree(Room room, int d, int from, int to) =>
                !UsedSides[room].Any(u => u.d == d && from <= u.to + 2 && to >= u.from - 2);

            private bool Attach(Room a, Room b)
            {
                var (ax, ay) = Pos[a];
                for (var t = 0; t < AttachTries; t++)
                {
                    var d = Rand.Next(4);
                    var back = (d + 2) % 4;
                    var da = Pick(a.Doors[d]);
                    var db = Pick(b.Doors[back]);
                    if (da == null || db == null || !SideFree(a, d, da.Perp, da.Perp + Desc.Corridor - 1))
                        continue;

                    var gap = Rand.Next(Desc.MinGap, MaxGap + 1);
                    var horizontal = d % 2 == 0;
                    int bx, by;
                    if (horizontal)
                    {
                        by = ay + da.Perp - db.Perp;
                        bx = d == 0 ? ax + a.W + gap : ax - gap - b.W;
                    }
                    else
                    {
                        bx = ax + da.Perp - db.Perp;
                        by = d == 1 ? ay + a.H + gap : ay - gap - b.H;
                    }
                    if (!Fits(b, bx, by))
                        continue;

                    // the corridor's lanes, each from one room's floor to the other's
                    var aAlong = horizontal ? ax : ay;
                    var bAlong = horizontal ? bx : by;
                    var perp0 = (horizontal ? ay : ax) + da.Perp;
                    var lanes = new List<(int p, int from, int to)>();
                    for (var i = 0; i < Desc.Corridor; i++)
                    {
                        var p = aAlong + da.Outer[i];
                        var q = bAlong + db.Outer[i];
                        lanes.Add((perp0 + i, Math.Min(p, q) + 1, Math.Max(p, q) - 1));
                    }

                    var aFace = d == 0 ? ax + a.W : d == 1 ? ay + a.H : d == 2 ? ax - 1 : ay - 1;
                    var bFace = d == 0 ? bx - 1 : d == 1 ? by - 1 : d == 2 ? bx + b.W : by + b.H;
                    int gapFrom = Math.Min(aFace, bFace), gapTo = Math.Max(aFace, bFace);
                    int ida = Placed.FindIndex(p => p.room == a) + 1;

                    bool Free(int p, int along, bool inGap)
                    {
                        int x = horizontal ? along : p, y = horizontal ? p : along;
                        var o = Owner[x, y];
                        return o == 0 || (!inGap && o == ida);
                    }

                    var ok = true;
                    foreach (var (p, from, to) in lanes)
                        for (var s = from; s <= to && ok; s++)
                            ok = Free(p, s, s >= gapFrom && s <= gapTo);
                    for (var s = gapFrom; s <= gapTo && ok; s++)
                        for (var e = 1; e <= 2 && ok; e++)
                            ok = Free(perp0 - e, s, true) && Free(perp0 + Desc.Corridor - 1 + e, s, true);
                    if (!ok)
                        continue;

                    Place(b, bx, by);
                    UsedSides[a].Add((d, da.Perp, da.Perp + Desc.Corridor - 1));
                    UsedSides[b].Add((back, db.Perp, db.Perp + Desc.Corridor - 1));
                    foreach (var (p, from, to) in lanes)
                    {
                        Lanes.Add(horizontal ? (from, p, to, p) : (p, from, p, to));
                        for (var s = from; s <= to; s++)
                        {
                            int x = horizontal ? s : p, y = horizontal ? p : s;
                            if (Owner[x, y] == 0)
                                Owner[x, y] = -1;
                        }
                    }
                    for (var s = gapFrom; s <= gapTo; s++)
                        for (var e = 1; e <= 1; e++)
                        {
                            int p0 = perp0 - e, p1 = perp0 + Desc.Corridor - 1 + e;
                            if (horizontal) { Owner[s, p0] = -1; Owner[s, p1] = -1; }
                            else { Owner[p0, s] = -1; Owner[p1, s] = -1; }
                        }
                    return true;
                }
                return false;
            }

            // doors near the middle of a side are likelier
            private Door Pick(List<Door> doors)
            {
                if (doors.Count == 0)
                    return null;
                var mid = (doors.Count - 1) / 2.0;
                var weights = doors.Select((_, i) => 1 + 3 * (1 - Math.Abs(i - mid) / Math.Max(1, mid))).ToArray();
                var roll = Rand.NextDouble() * weights.Sum();
                for (var i = 0; i < doors.Count; i++)
                {
                    roll -= weights[i];
                    if (roll <= 0)
                        return doors[i];
                }
                return doors[^1];
            }

            private TerrainTile[,] Render()
            {
                var canvas = new TerrainTile[CanvasSize, CanvasSize];
                for (var x = 0; x < CanvasSize; x++)
                    for (var y = 0; y < CanvasSize; y++)
                        canvas[x, y] = Source.Void;

                var inRoom = new bool[CanvasSize, CanvasSize];
                foreach (var (room, ox, oy) in Placed)
                    for (var x = 0; x < room.W; x++)
                        for (var y = 0; y < room.H; y++)
                            if (room.Has[x, y])
                            {
                                canvas[ox + x, oy + y] = room.Tiles[x, y];
                                inRoom[ox + x, oy + y] = true;
                            }

                foreach (var (x0, y0, x1, y1) in Lanes)
                    for (var x = x0; x <= x1; x++)
                        for (var y = y0; y <= y1; y++)
                            if (!Source.Walkable(canvas[x, y]))
                                canvas[x, y] = Source.Floor;

                // walls along the corridors and wherever floor meets the background
                if (Source.Wall is TerrainTile wall)
                {
                    var add = new List<(int, int)>();
                    for (var x = 1; x < CanvasSize - 1; x++)
                        for (var y = 1; y < CanvasSize - 1; y++)
                        {
                            // background, including the empty corners rooms bring along
                            var t = canvas[x, y];
                            if (Source.Walkable(t) || !string.IsNullOrEmpty(t.TileObj) || (inRoom[x, y] && !t.Equals(Source.Void)))
                                continue;
                            for (var i = -1; i <= 1; i++)
                                for (var j = -1; j <= 1; j++)
                                    if (Source.Walkable(canvas[x + i, y + j]))
                                        goto found;
                            continue;
                        found:
                            add.Add((x, y));
                        }
                    foreach (var (x, y) in add)
                        canvas[x, y] = wall;

                    // background walled in on every side (the hollow middle of a block of wall) shows
                    // as a black hole: fill it with wall
                    bool Outside(int x, int y) => canvas[x, y].Equals(Source.Void);
                    var edge = new List<(int, int)>();
                    for (var i = 0; i < CanvasSize; i++)
                    {
                        edge.Add((i, 0)); edge.Add((i, CanvasSize - 1));
                        edge.Add((0, i)); edge.Add((CanvasSize - 1, i));
                    }
                    var open = Flood(CanvasSize, CanvasSize, edge.Where(c => Outside(c.Item1, c.Item2)).ToList(), Outside);
                    for (var x = 0; x < CanvasSize; x++)
                        for (var y = 0; y < CanvasSize; y++)
                            if (!open[x, y] && Outside(x, y))
                                canvas[x, y] = wall;
                }

                // connected walls (Tomb, cave walls) can store the shape they had in the original map;
                // drop it so the world server works out each wall's shape from its new neighbours
                for (var x = 0; x < CanvasSize; x++)
                    for (var y = 0; y < CanvasSize; y++)
                    {
                        var name = canvas[x, y].Name;
                        if (!string.IsNullOrEmpty(name) && name.Contains("conn:"))
                            canvas[x, y].Name = string.Join(";", name.Split(';').Where(p => p.Length > 0 && !p.StartsWith("conn:")));
                    }

                // everything still has to be reachable from the spawn
                var spawn = new List<(int, int)>();
                int minX = CanvasSize, minY = CanvasSize, maxX = 0, maxY = 0;
                for (var x = 0; x < CanvasSize; x++)
                    for (var y = 0; y < CanvasSize; y++)
                    {
                        if (!inRoom[x, y] && !Source.Walkable(canvas[x, y]) && canvas[x, y].Equals(Source.Void))
                            continue;
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        if (canvas[x, y].Region == TileRegion.Spawn)
                            spawn.Add((x, y));
                    }
                var reach = Flood(CanvasSize, CanvasSize, spawn, (x, y) => Source.Walkable(canvas[x, y]) || canvas[x, y].Region == TileRegion.Spawn);
                foreach (var (room, ox, oy) in Placed)
                    if (room.Goal && !Enumerable.Range(0, room.W).Any(x => Enumerable.Range(0, room.H).Any(y => reach[ox + x, oy + y])))
                        return null;

                minX -= Margin; minY -= Margin; maxX += Margin; maxY += Margin;
                var tiles = new TerrainTile[maxX - minX + 1, maxY - minY + 1];
                for (var x = minX; x <= maxX; x++)
                    for (var y = minY; y <= maxY; y++)
                        tiles[x - minX, y - minY] = canvas[x, y];
                return tiles;
            }
        }
    }
}
