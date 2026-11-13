using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shared.resources;
using Shared.terrain;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // The dungeons laid out anew for every visit from the rooms of their maps (<RandomLayout> in
    // Dungeons.xml) that have set pieces of their own: every layout builds, keeps its boss and set
    // pieces, the boss can be walked to (through a gate only once it's open), and the layouts differ.
    public class RandomDungeonTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private readonly ITestOutputHelper _out;

        public RandomDungeonTests(ITestOutputHelper output) => _out = output;

        // world, goal, the gate in front of it (or none), the set pieces every layout keeps (id, at
        // least how many)
        public static IEnumerable<object[]> Dungeons() => new[]
        {
            new object[] { "Fungal Cavern", "GC Boss", "GC Drill Wall", "GC Drill Charge:1;GC Drill Dwarf:1;GC Crystal:20" },
            new object[] { "The Nest", "EH King Bee", null, "EH Beekeeper:1" },
            new object[] { "The Nest", "EH Beekeeper", "EH Destructible Wall", "EH King Bee:1;EH Destructible Wall:3;EH Waiting Floor:1" },
            new object[] { "Crystal Cavern", "mc", "CC Drill Wall", "CC Drill Charge:1;CC Drill Dwarf:1;CC Crystal:20" },
            new object[] { "Candyland Hunting Grounds", "Gigacorn", null, "Candyland Spawner:6;Gumball Machine:1" },
        };

        [Theory]
        [MemberData(nameof(Dungeons))]
        public void LayoutsWork(string worldName, string goal, string gate, string keep)
        {
            var data = Res.Value.GameData;
            var world = data.GetWorld(worldName);
            Assert.NotNull(world.RandomLayout);
            var map = world.MapJM[0];
            var source = data.GetWorldData(map);
            var wants = keep.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => (id: k.Split(':')[0], n: int.Parse(k.Split(':')[1]))).ToList();
            var different = new HashSet<string>();
            const int seeds = 30;
            for (var seed = 0; seed < seeds; seed++)
            {
                var layout = DungeonGenerator.Generate(data, map, source, world.RandomLayout, new Random(seed));
                Assert.True(layout != null, $"seed {seed}: no layout");
                var tiles = DungeonGenerator.Import(layout);
                var counts = tiles.Cast<TerrainTile>().Where(t => !string.IsNullOrEmpty(t.TileObj))
                    .GroupBy(t => t.TileObj).ToDictionary(g => g.Key, g => g.Count());
                Assert.Equal(1, counts.GetValueOrDefault(goal));
                foreach (var (id, n) in wants)
                    if (counts.GetValueOrDefault(id) < n)
                        _out.WriteLine(Draw(data, tiles, goal, gate));
                foreach (var (id, n) in wants)
                    Assert.True(counts.GetValueOrDefault(id) >= n, $"seed {seed}: {counts.GetValueOrDefault(id)} {id}, want {n}");

                Assert.True(Reaches(data, tiles, goal, gate, true), $"seed {seed}: {goal} can't be reached");
                if (gate != null && Reaches(data, tiles, goal, gate, false))
                    _out.WriteLine(Draw(data, tiles, goal, gate));
                if (gate != null)
                    Assert.False(Reaches(data, tiles, goal, gate, false), $"seed {seed}: {goal} can be reached round the {gate}");

                var picture = Draw(data, tiles, goal, gate);
                different.Add(picture);
                if (seed < 2)
                    _out.WriteLine($"seed {seed}: {tiles.GetLength(0)}x{tiles.GetLength(1)}, " +
                        string.Join(", ", wants.Select(w => $"{counts.GetValueOrDefault(w.id)} {w.id}")) + $"\n{picture}");
            }
            Assert.True(different.Count > seeds * 2 / 3, $"only {different.Count} different layouts in {seeds}");
        }

        private static bool Solid(XmlData data, TerrainTile t, string gate, bool open = false)
        {
            if (t.TileId == 0xFF || (data.Tiles.TryGetValue(t.TileId, out var td) && td.NoWalk))
                return true;
            if (string.IsNullOrEmpty(t.TileObj))
                return false;
            if (t.TileObj == gate)
                return !open;
            if (!data.IdToObjectType.TryGetValue(t.TileObj, out var type))
                return false;
            var od = data.ObjectDescs[type];
            return !od.Enemy && (od.Class is "Wall" or "ConnectedWall" or "CaveWall" || (od.FullOccupy && od.Static));
        }

        // can a player walk from the spawn to beside the goal, with `gate` standing or open
        private static bool Reaches(XmlData data, TerrainTile[,] tiles, string goal, string gate, bool open)
        {
            int w = tiles.GetLength(0), h = tiles.GetLength(1);
            var seen = new bool[w, h];
            var queue = new Queue<(int x, int y)>();
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    if (tiles[x, y].Region == TileRegion.Spawn)
                    {
                        seen[x, y] = true;
                        queue.Enqueue((x, y));
                    }
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && !seen[nx, ny] && !Solid(data, tiles[nx, ny], gate, open))
                    {
                        seen[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
            }
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    if (tiles[x, y].TileObj == goal)
                        for (var i = -2; i <= 2; i++)
                            for (var j = -2; j <= 2; j++)
                                if (x + i >= 0 && y + j >= 0 && x + i < w && y + j < h && seen[x + i, y + j])
                                    return true;
            return false;
        }

        private static string Draw(XmlData data, TerrainTile[,] tiles, string goal, string gate)
        {
            var sb = new StringBuilder();
            for (var y = 0; y < tiles.GetLength(1); y++)
            {
                for (var x = 0; x < tiles.GetLength(0); x++)
                {
                    var t = tiles[x, y];
                    var obj = t.TileObj ?? "";
                    sb.Append(t.Region == TileRegion.Spawn ? 'S'
                        : obj == goal ? 'B'
                        : obj == gate ? 'D'
                        : obj.Length > 0 && Solid(data, t, gate) ? '#'
                        : obj.Length > 0 ? 'o'
                        : Solid(data, t, gate) ? ' '
                        : '.');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
