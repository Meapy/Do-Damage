using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.resources;
using WorldServer.core.worlds;
using WorldServer.core.worlds.realm2;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // A Realm 2's map (Realm2Generator): one continent in the sea, nothing of the old starry void,
    // and every bit of land a player can stand on reachable on foot from where they arrive. With
    // R2_PREVIEW set to a folder, each seed's map is written there as a picture (a .ppm, one pixel a
    // tile, coloured by ground) to look at.
    public class Realm2MapTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private readonly ITestOutputHelper _out;

        public Realm2MapTests(ITestOutputHelper output) => _out = output;

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(42)]
        [InlineData(1234)]
        [InlineData(99991)]
        public void OneContinentAllReachable(int seed)
        {
            var data = Res.Value.GameData;
            var (c, layout) = new Realm2Generator(data, seed).Generate();
            var abyss = data.IdToTileType["R2 Abyss"];

            int w = c.W, h = c.H, voidTiles = 0, land = 0, walkable = 0;
            var spawn = (-1, -1);
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    var t = c.Tiles[x, y];
                    if (t.TileId == abyss)
                        voidTiles++;
                    if (layout.AreaOf[x, y] >= 0)
                        land++;
                    if (Walk(data, t.TileId))
                        walkable++;
                    if (t.Region == TileRegion.Spawn)
                        spawn = (x, y);
                }
            if (voidTiles > 0)
                Assert.Fail($"seed {seed}: {voidTiles} tiles of the void");
            if (spawn.Item1 < 0)
                Assert.Fail($"seed {seed}: nowhere to arrive");

            // walk out from the spawn: everything walkable (bar the odd speck the scenery hems in)
            var seen = new bool[w, h];
            var queue = new Queue<(int, int)>();
            queue.Enqueue(spawn);
            seen[spawn.Item1, spawn.Item2] = true;
            var reached = 0;
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                reached++;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && !seen[nx, ny] && Walk(data, c.Tiles[nx, ny].TileId))
                    {
                        seen[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
            }
            var areas = string.Join(", ", layout.Areas.Select(a => $"{a.Kind} {a.Tiles}"));
            _out.WriteLine($"seed {seed}: {w}x{h}, land {land}, walkable {walkable}, reached {reached}; {areas}; kept {layout.Untouched.Count}");
            if (reached < walkable * 0.98)
                Assert.Fail($"seed {seed}: only {reached} of {walkable} walkable tiles reachable from the spawn");
            if (layout.Untouched.Count < 2)
                Assert.Fail($"seed {seed}: {layout.Untouched.Count} stretches kept from event lands");

            var dir = Environment.GetEnvironmentVariable("R2_PREVIEW");
            if (!string.IsNullOrEmpty(dir))
            {
                Picture(data, c, Path.Combine(dir, $"realm2-{seed}.ppm"));
                DrawLands(data, c, layout, seed);
                Picture(data, c, Path.Combine(dir, $"realm2-{seed}-lands.ppm"));
            }
        }

        // for the picture: eleven event lands drawn in, roughly where the realm puts them (spread
        // along the Godlands, apart, off the stretches kept as they are)
        private static void DrawLands(XmlData data, Realm2Canvas c, Realm2Layout layout, int seed)
        {
            var rand = new Random(seed);
            var placed = new List<WorldServer.core.structures.IntPoint>();
            var span = layout.GodlandsTo - layout.GodlandsFrom;
            for (var n = 0; n < Realm2Themes.LandsToClose; n++)
            {
                var target = layout.GodlandsFrom + span * (n + 0.6) / Realm2Themes.LandsToClose;
                for (var tries = 0; tries < 4000; tries++)
                {
                    int x = rand.Next(68, layout.Width - 68), y = rand.Next(68, layout.Height - 68);
                    var slack = 45 + tries / 100 * 10;
                    if (Math.Abs(layout.Along(x, y) - target) > slack || layout.AreaAt(x, y)?.Kind != Realm2Themes.Godlands)
                        continue;
                    if (placed.Any(p => Realm2Canvas.Dist(p, x, y) < (tries < 2000 ? 90 : 60))
                        || layout.Untouched.Any(u => Realm2Canvas.Dist(u.At, x, y) < 62 + u.R))
                        continue;
                    var at = new WorldServer.core.structures.IntPoint(x, y);
                    placed.Add(at);
                    var theme = Realm2Themes.Events[n % Realm2Themes.Events.Length];
                    var painter = new Realm2EventPainter(data, rand.Next(), theme, at, 50);
                    painter.Paint((px, py) => layout.AreaAt(px, py) != null, null, false);
                    var pc = painter.Canvas;
                    for (var px = pc.X0; px < pc.X0 + pc.W; px++)
                        for (var py = pc.Y0; py < pc.Y0 + pc.H; py++)
                            if (c.Inside(px, py) && pc.IsSet(px, py))
                                c.At(px, py) = pc.At(px, py);
                    break;
                }
            }
        }

        private static bool Walk(XmlData data, ushort id) =>
            data.Tiles.TryGetValue(id, out var d) && !d.NoWalk && d.MaxDamage == 0;

        private static void Picture(XmlData data, Realm2Canvas c, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var f = File.Create(path);
            var head = System.Text.Encoding.ASCII.GetBytes($"P6 {c.W} {c.H} 255\n");
            f.Write(head);
            var px = new byte[c.W * c.H * 3];
            for (var y = 0; y < c.H; y++)
                for (var x = 0; x < c.W; x++)
                {
                    var t = c.Tiles[x, y];
                    var name = data.Tiles.TryGetValue(t.TileId, out var d) ? d.ObjectId : "";
                    var (r, g, b) = Colour(name);
                    if (t.TileObj != null)
                        (r, g, b) = ((byte)(r * 0.55), (byte)(g * 0.55), (byte)(b * 0.55));
                    var i = (y * c.W + x) * 3;
                    px[i] = r;
                    px[i + 1] = g;
                    px[i + 2] = b;
                }
            f.Write(px);
        }

        private static (byte, byte, byte) Colour(string name) => name switch
        {
            "R2 Abyss" => (8, 6, 20),
            "Dark Water" => (22, 52, 120),
            "Shallow Water" => (58, 110, 200),
            "Water" => (40, 90, 180),
            "R2 Pool Water" => (70, 150, 210),
            "Rock" => (128, 128, 128),
            "Snowy Rock" => (215, 220, 230),
            "Bright Grass" => (90, 170, 70),
            "Grass" => (60, 150, 50),
            "R2 Bridge" or "R2 Bridge Rail" => (150, 100, 50),
            _ => Hashed(name),
        };

        private static (byte, byte, byte) Hashed(string name)
        {
            var h = (uint)(name ?? "").Aggregate(17, (a, ch) => a * 31 + ch);
            return ((byte)(80 + h % 150), (byte)(80 + (h / 150) % 150), (byte)(40 + (h / 22500) % 120));
        }
    }
}
