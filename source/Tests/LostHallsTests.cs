using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Shared.resources;
using Shared.terrain;
using WorldServer.core;
using WorldServer.logic;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // The Lost Halls (tools/build_lost_halls.py, BehaviorDb.LostHallsRooms.cs): the random layouts
    // its rooms make, and the monsters its map places.
    public class LostHallsTests
    {
        private static readonly Lazy<(Resources Res, BehaviorDb Db)> Data = new Lazy<(Resources, BehaviorDb)>(() =>
        {
            var res = new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false);
            var server = (GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
            typeof(GameServer).GetProperty(nameof(GameServer.Resources)).SetValue(server, res);
            var db = new BehaviorDb(server);
            db.Initialize();
            return (res, db);
        });

        private readonly ITestOutputHelper _out;

        public LostHallsTests(ITestOutputHelper output) => _out = output;

        // every layout builds (none falls back to the fixed map), has its spawn and one Colossus room,
        // and the Colossus room can be walked to from the spawn
        [Fact]
        public void LayoutsReachTheColossus()
        {
            var data = Data.Value.Res.GameData;
            var world = data.GetWorld("Lost Halls");
            Assert.NotNull(world.RandomLayout);
            var map = world.MapJM[0];
            var source = data.GetWorldData(map);
            var leaders = new[] { "LH Commander of the Crusade", "LH Champion of Oryx", "LH Grotto Blob", "LH Tormented Golem",
                "LH Spectral Sentry S", "LH Agonized Titan", "LH Greater Void Shade", "LH Grenade Trap", "LH Pot 1" };
            for (var seed = 0; seed < 40; seed++)
            {
                var layout = DungeonGenerator.Generate(data, map, source, world.RandomLayout, new Random(seed));
                Assert.True(layout != null, $"seed {seed}: no layout");
                var tiles = DungeonGenerator.Import(layout);
                var w = tiles.GetLength(0);
                var h = tiles.GetLength(1);
                var spawns = new List<(int, int)>();
                var bosses = new List<(int, int)>();
                var kinds = new HashSet<string>();
                for (var x = 0; x < w; x++)
                    for (var y = 0; y < h; y++)
                    {
                        if (tiles[x, y].Region == TileRegion.Spawn)
                            spawns.Add((x, y));
                        if (tiles[x, y].TileObj == "LH Monster Spawner")
                            bosses.Add((x, y));
                        if (leaders.Contains(tiles[x, y].TileObj))
                            kinds.Add(tiles[x, y].TileObj);
                    }
                Assert.NotEmpty(spawns);
                Assert.Single(bosses);

                bool Walkable(int x, int y)
                {
                    var t = tiles[x, y];
                    if (t.TileId == 0xFF || (data.Tiles.TryGetValue(t.TileId, out var td) && td.NoWalk))
                        return false;
                    if (string.IsNullOrEmpty(t.TileObj) || !data.IdToObjectType.TryGetValue(t.TileObj, out var type))
                        return true;
                    var od = data.ObjectDescs[type];
                    return !(od.Class is "Wall" or "ConnectedWall" or "CaveWall" || (od.FullOccupy && od.Static && !od.Enemy));
                }
                var seen = new bool[w, h];
                var queue = new Queue<(int x, int y)>(spawns);
                foreach (var (x, y) in spawns)
                    seen[x, y] = true;
                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && !seen[nx, ny] && Walkable(nx, ny))
                        {
                            seen[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                }
                Assert.True(seen[bosses[0].Item1, bosses[0].Item2], $"seed {seed}: the Colossus room can't be reached");
                _out.WriteLine($"seed {seed}: {w}x{h}, {kinds.Count} kinds of room ({string.Join(", ", kinds.OrderBy(k => k))})");
            }
        }

        // every monster the map places has a behaviour, and the Colossus room has its controller and
        // its four Marble Defenders
        [Fact]
        public void MapsPlaceMonstersWithBehaviours()
        {
            var (res, db) = Data.Value;
            var data = res.GameData;
            var expect = new Dictionary<string, string[]>
            {
                ["Lost Halls"] = new[] { "LH Monster Spawner", "LH Marble Defender Activator N", "LH Marble Defender Activator E",
                    "LH Marble Defender Activator S", "LH Marble Defender Activator W" },
            };
            foreach (var (name, needed) in expect)
            {
                var tiles = DungeonGenerator.Import(data.GetWorldData(data.GetWorld(name).MapJM[0]));
                var placed = new HashSet<string>();
                var spawn = false;
                foreach (var t in tiles)
                {
                    spawn |= t.Region == TileRegion.Spawn;
                    if (!string.IsNullOrEmpty(t.TileObj))
                        placed.Add(t.TileObj);
                }
                Assert.True(spawn, $"{name} has no spawn");
                foreach (var id in needed)
                    Assert.True(placed.Contains(id), $"{name} doesn't place {id}");
                foreach (var id in placed)
                {
                    var od = data.ObjectDescs[data.IdToObjectType[id]];
                    if (od.Enemy || id.StartsWith("LH Marble Defender Activator") || id == "LH Monster Spawner")
                        Assert.True(db.Definitions.ContainsKey(od.ObjectType), $"{name}: {id} has no behaviour");
                }
            }
        }
    }
}
