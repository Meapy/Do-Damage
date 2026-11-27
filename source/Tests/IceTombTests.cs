using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shared.resources;
using Shared.terrain;
using WorldServer.core.resurrection;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // The Ice Tomb (tools/build_ice_tomb.py, BehaviorDb.IceTomb.cs, IceTomb.cs, IceCourt.cs): every
    // layout keeps its three seals and the Sealed Door, each once; each can be walked to once the
    // chasms' bridges have frozen across (and each chasm keeps its heart, which freezes them); and
    // the Sealed Door's room always has room in the dark for the Frozen Court.
    public class IceTombTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private static readonly string[] Goals = { "Seal of the Warden", "Seal of the Oracle", "Seal of the Reaver", "Sealed Door" };

        private readonly ITestOutputHelper _out;

        public IceTombTests(ITestOutputHelper output) => _out = output;

        private static CourtCell Cell(XmlData data, TerrainTile t)
        {
            if (t.TileObj is "Ice Tomb Wall" or "Ice Tomb Sconce Wall")
                return CourtCell.Wall;
            if (t.TileId == 0xFF && string.IsNullOrEmpty(t.TileObj))
                return CourtCell.Void;
            return data.Tiles.TryGetValue(t.TileId, out var d) && !d.NoWalk ? CourtCell.Floor : CourtCell.Solid;
        }

        // walkable once the bridges stand: a chasm's mist and its heart are where the bridge lies
        private static bool Solid(XmlData data, TerrainTile t)
        {
            if (t.TileObj is "Chasm Mist" or "Chasm Heart")
                return false;
            if (t.TileId == 0xFF || (data.Tiles.TryGetValue(t.TileId, out var td) && td.NoWalk))
                return true;
            if (string.IsNullOrEmpty(t.TileObj) || !data.IdToObjectType.TryGetValue(t.TileObj, out var type))
                return false;
            var od = data.ObjectDescs[type];
            return !od.Enemy && (od.Class is "Wall" or "ConnectedWall" or "CaveWall" || (od.FullOccupy && od.Static));
        }

        [Fact]
        public void EveryLayoutKeepsItsSealsAndItsCourt()
        {
            var data = Res.Value.GameData;
            var world = data.GetWorld("Ice Tomb");
            Assert.NotNull(world.RandomLayout);
            Assert.True(world.RandomLayout.Margin >= 50, "the tomb keeps dark round it for the Court");
            var map = world.MapJM[0];
            var source = data.GetWorldData(map);
            var different = new HashSet<string>();
            const int seeds = 30;
            for (var seed = 0; seed < seeds; seed++)
            {
                var layout = DungeonGenerator.Generate(data, map, source, world.RandomLayout, new Random(seed));
                Assert.True(layout != null, $"seed {seed}: no layout");
                var tiles = DungeonGenerator.Import(layout);
                int w = tiles.GetLength(0), h = tiles.GetLength(1);
                var all = Enumerable.Range(0, w).SelectMany(x => Enumerable.Range(0, h).Select(y => (x, y))).ToList();
                var counts = all.Select(q => tiles[q.x, q.y].TileObj).Where(o => !string.IsNullOrEmpty(o))
                    .GroupBy(o => o).ToDictionary(g => g.Key, g => g.Count());
                foreach (var goal in Goals)
                    Assert.True(counts.GetValueOrDefault(goal) == 1, $"seed {seed}: {counts.GetValueOrDefault(goal)} {goal}");
                // every chasm that came along has its heart
                Assert.True(counts.GetValueOrDefault("Chasm Mist") == 0 || counts.GetValueOrDefault("Chasm Heart") > 0, $"seed {seed}: a chasm without its heart");

                // walk from the spawn
                var seen = new bool[w, h];
                var queue = new Queue<(int x, int y)>();
                foreach (var q in all.Where(q => tiles[q.x, q.y].Region == TileRegion.Spawn))
                {
                    seen[q.x, q.y] = true;
                    queue.Enqueue(q);
                }
                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && !seen[nx, ny] && !Solid(data, tiles[nx, ny]))
                        {
                            seen[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                }
                foreach (var goal in Goals)
                {
                    var at = all.First(q => tiles[q.x, q.y].TileObj == goal);
                    var near = Enumerable.Range(-2, 5).Any(i => Enumerable.Range(-2, 5).Any(j => seen[at.x + i, at.y + j]));
                    if (!near)
                        Assert.Fail($"seed {seed}: the {goal} can't be reached\n{Draw(tiles, null)}");
                }

                // the Frozen Court's way and hall, from the Sealed Door
                var door = all.First(q => tiles[q.x, q.y].TileObj == "Sealed Door");
                var plan = TombCourtPlan.Make(w, h, (x, y) => Cell(data, tiles[x, y]), door.x, door.y, out var why);
                if (plan == null)
                    Assert.Fail($"seed {seed}: no room for the Frozen Court: {why}\n{Draw(tiles, null)}");
                foreach (var q in plan.Causeway.Keys.Concat(plan.Hall.Keys).Concat(plan.HallWalls))
                    Assert.Equal(CourtCell.Void, Cell(data, tiles[q.X, q.Y]));
                Assert.True(plan.EntryS >= TombCourtPlan.MinWalk, $"seed {seed}: a bridge of only {plan.EntryS:0.0}");
                different.Add(Draw(tiles, null));
                if (seed < 2)
                    _out.WriteLine($"seed {seed}: {w}x{h}, bridge {plan.EntryS:0.0} squares\n{Draw(tiles, plan)}");
            }
            Assert.True(different.Count > seeds * 2 / 3, $"only {different.Count} different layouts in {seeds}");
        }

        [Fact]
        public void TheSnowflakeIsLaidInTheCourt()
        {
            var squares = 0;
            for (var x = -19; x <= 19; x++)
                for (var y = -19; y <= 19; y++)
                {
                    var r = Math.Sqrt(x * x + y * y);
                    if (r <= TombCourtPlan.FloorR && WorldServer.logic.behaviors.IceCourt.Snowflake(x, y, r))
                        squares++;
                }
            Assert.InRange(squares, 80, 400);
        }

        private static string Draw(TerrainTile[,] tiles, TombCourtPlan plan)
        {
            var sb = new StringBuilder();
            for (var y = 0; y < tiles.GetLength(1); y += 2)
            {
                for (var x = 0; x < tiles.GetLength(0); x++)
                {
                    var t = tiles[x, y];
                    var c = plan != null && (plan.Hall.ContainsKey((x, y)) || plan.Causeway.ContainsKey((x, y))) ? '+'
                        : t.TileObj switch
                        {
                            "Sealed Door" => 'D',
                            "Seal of the Warden" or "Seal of the Oracle" or "Seal of the Reaver" => 'S',
                            "Chasm Mist" => '~',
                            "Ice Tomb Wall" or "Ice Tomb Sconce Wall" or "Glacier Wall" => '#',
                            _ => t.Region == TileRegion.Spawn ? 'P' : t.TileId == 0xFF ? ' ' : '.',
                        };
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
