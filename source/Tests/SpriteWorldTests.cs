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
    // The Sprite World and the Elder Sprite World: islands over Space, laid out anew for every visit
    // (<RandomLayout> in Dungeons.xml and DoomedRealmsWorlds.xml).
    public class SpriteWorldTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private readonly ITestOutputHelper _out;

        public SpriteWorldTests(ITestOutputHelper output) => _out = output;

        // every layout builds (none falls back to the fixed map), keeps one spawn and one Limon, and
        // Limon's island can be walked to from the spawn
        [Theory]
        [InlineData("Sprite World", "Limon the Sprite God")]
        [InlineData("Elder Sprite World", "Elder Limon the Sprite God")]
        public void LayoutsReachLimon(string worldName, string boss)
        {
            var data = Res.Value.GameData;
            var world = data.GetWorld(worldName);
            Assert.NotNull(world.RandomLayout);
            var map = world.MapJM[0];
            var source = data.GetWorldData(map);
            var different = new HashSet<string>();
            for (var seed = 0; seed < 40; seed++)
            {
                var layout = DungeonGenerator.Generate(data, map, source, world.RandomLayout, new Random(seed));
                Assert.True(layout != null, $"seed {seed}: no layout");
                var tiles = DungeonGenerator.Import(layout);
                var w = tiles.GetLength(0);
                var h = tiles.GetLength(1);
                var spawns = new List<(int x, int y)>();
                var bosses = new List<(int x, int y)>();
                for (var x = 0; x < w; x++)
                    for (var y = 0; y < h; y++)
                    {
                        if (tiles[x, y].Region == TileRegion.Spawn)
                            spawns.Add((x, y));
                        if (tiles[x, y].TileObj == boss)
                            bosses.Add((x, y));
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
                // Limon flies: he stands on the island, so a square next to him is walkable
                var (bx, by) = bosses[0];
                var near = false;
                for (var i = -1; i <= 1; i++)
                    for (var j = -1; j <= 1; j++)
                        near |= bx + i >= 0 && by + j >= 0 && bx + i < w && by + j < h && seen[bx + i, by + j];
                Assert.True(near, $"seed {seed}: Limon's island can't be reached");

                var picture = Draw(data, tiles);
                different.Add(picture);
                if (seed < 2)
                    _out.WriteLine($"seed {seed}: {w}x{h}\n{picture}");
            }
            Assert.True(different.Count > 30, $"only {different.Count} different layouts in 40");
        }

        // the platform Limon leaves where he dies (BehaviorDb.SpriteWorld.cs) can be stood on and
        // doesn't carry the player along like the conveyors
        [Theory]
        [InlineData("White Alpha Square")]
        [InlineData("Elder White Alpha Square")]
        public void PlatformIsStill(string ground)
        {
            var data = Res.Value.GameData;
            var tile = data.Tiles[data.IdToTileType[ground]];
            Assert.False(tile.NoWalk);
            Assert.False(tile.Push);
        }

        private static string Draw(XmlData data, TerrainTile[,] tiles)
        {
            var w = tiles.GetLength(0);
            var h = tiles.GetLength(1);
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            bool Floor(TerrainTile t) => t.TileId != 0xFF && !(data.Tiles.TryGetValue(t.TileId, out var td) && td.NoWalk);
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    if (Floor(tiles[x, y]))
                    {
                        x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
                        x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                    }
            var sb = new StringBuilder();
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var t = tiles[x, y];
                    var obj = t.TileObj ?? "";
                    sb.Append(t.Region == TileRegion.Spawn ? 'S'
                        : obj.Contains("Limon the") ? 'L'
                        : obj.Contains("Portal") ? 'P'
                        : obj.Contains("Tree") ? 'T'
                        : obj.Length > 0 ? 'm'
                        : !Floor(t) ? ' '
                        : data.Tiles.TryGetValue(t.TileId, out var td) && td.Push ? '>' : '.');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
