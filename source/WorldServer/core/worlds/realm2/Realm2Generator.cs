using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using Shared.terrain;
using WorldServer.core.structures;

namespace WorldServer.core.worlds.realm2
{
    /// <summary>One of a Realm 2's lands: the first two when it opens, an event's when the event comes.</summary>
    public sealed class Realm2Area
    {
        public int Index;
        public Realm2Theme Theme;           // an event's land; null for the first two
        public string Kind;                 // the theme's key, or Realm2Themes.Starter / Midlands
        public IntPoint Center;
        public double Radius;
        public int Tiles;
        public string Link;                 // how an event's land joins the land it grew from
        // the midlands' clearings for the Lucky gods; an event land's arena
        public List<(string Event, IntPoint At)> Events = new List<(string, IntPoint)>();

        public string Name => Theme?.Name ?? (Kind == Realm2Themes.Starter ? "the shore and lowlands" : "the midlands and highlands");
    }

    /// <summary>A Realm 2's lands, and which land each tile is in (-1 for the void).</summary>
    public sealed class Realm2Layout
    {
        public int Width, Height;
        public int[,] AreaOf;
        public List<Realm2Area> Areas = new List<Realm2Area>();
        // the way out from the shore: event lands grow ever further along it
        public double AxisX, AxisY;
        public IntPoint Start;

        public Realm2Area AreaAt(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height && AreaOf[x, y] >= 0 ? Areas[AreaOf[x, y]] : null;

        /// <summary>How far along the way out a point is.</summary>
        public double Along(double x, double y) => (x - Start.X) * AxisX + (y - Start.Y) * AxisY;
    }

    /// <summary>
    /// Makes a new Realm 2's map (SETUP-LOCAL.md, "Realm 2"): in a starry void, the shore and
    /// lowlands where players arrive at one end, and the midlands and highlands next to them (with
    /// clearings for the Lucky gods). The event lands aren't here: each grows out of the lands
    /// already there when its event comes (Realm2EventPainter), so the realm spreads as it's played.
    /// </summary>
    public sealed class Realm2Generator
    {
        public const int Width = 1152, Height = 896;

        private readonly XmlData _data;
        private readonly int _seed;
        private Realm2Canvas _c;
        private Realm2Layout _layout;
        private Random Rand => _c.Rand;

        public Realm2Generator(XmlData data, int seed)
        {
            _data = data;
            _seed = seed;
        }

        public (Realm2Canvas Canvas, Realm2Layout Layout) Generate()
        {
            _c = new Realm2Canvas(_data, _seed, 0, 0, Width, Height, "R2 Abyss");
            _layout = new Realm2Layout { Width = Width, Height = Height, AreaOf = new int[Width, Height] };
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    _layout.AreaOf[x, y] = -1;

            // the way out: across the map or corner to corner (not the short way), give or take; the
            // shore at its back edge
            var ways = new[] { 0, 1, 3, 4, 5, 7 };
            var angle = ways[Rand.Next(ways.Length)] * Math.PI / 4 + (Rand.NextDouble() - 0.5) * 0.4;
            double ax = Math.Cos(angle), ay = Math.Sin(angle);
            _layout.AxisX = ax;
            _layout.AxisY = ay;
            var half = (Math.Abs(ax) * Width + Math.Abs(ay) * Height) / 2;
            var start = new IntPoint((int)(Width / 2 - ax * (half - 80)), (int)(Height / 2 - ay * (half - 80)));
            start = new IntPoint(Math.Clamp(start.X, 85, Width - 85), Math.Clamp(start.Y, 85, Height - 85));
            _layout.Start = start;

            var starter = AddArea(Realm2Themes.Starter, start, 50);
            var side = (Rand.NextDouble() - 0.5) * 50;
            var mid = AddArea(Realm2Themes.Midlands, new IntPoint((int)(start.X + ax * 100 - ay * side), (int)(start.Y + ay * 100 + ax * side)), 48);

            Blob(starter, Edge(Rand, starter.Radius));
            Blob(mid, Edge(Rand, mid.Radius));
            Neck(starter, mid);

            foreach (var area in _layout.Areas)
                Paint(area);
            Coast(starter);
            LuckyGlades(mid);
            Scenery();
            SpawnRegion(starter, mid);
            Count();
            return (_c, _layout);
        }

        private Realm2Area AddArea(string kind, IntPoint at, double r)
        {
            var area = new Realm2Area { Index = _layout.Areas.Count, Kind = kind, Center = at, Radius = r };
            _layout.Areas.Add(area);
            return area;
        }

        private void Count()
        {
            foreach (var area in _layout.Areas)
                area.Tiles = 0;
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    if (_layout.AreaOf[x, y] >= 0)
                        _layout.Areas[_layout.AreaOf[x, y]].Tiles++;
        }

        // ---- shapes

        /// <summary>A land's edge at an angle: its radius with a slow wobble.</summary>
        internal static Func<double, double> Edge(Random rand, double radius)
        {
            var p1 = rand.NextDouble() * Math.PI * 2;
            var p2 = rand.NextDouble() * Math.PI * 2;
            var k1 = 2 + rand.Next(2);
            var k2 = 5 + rand.Next(3);
            return a => radius * (1 + 0.12 * Math.Sin(k1 * a + p1) + 0.06 * Math.Sin(k2 * a + p2));
        }

        private void Blob(Realm2Area area, Func<double, double> edge)
        {
            var reach = (int)(area.Radius * 1.25) + 4;
            for (var x = area.Center.X - reach; x <= area.Center.X + reach; x++)
                for (var y = area.Center.Y - reach; y <= area.Center.Y + reach; y++)
                {
                    if (!_c.Inside(x, y))
                        continue;
                    var dx = x - area.Center.X;
                    var dy = y - area.Center.Y;
                    var d = Math.Sqrt(dx * dx + dy * dy) + (_c.Noise(x, y, 0.25, 7) - 0.5) * 4;
                    if (d < edge(Math.Atan2(dy, dx)) && _layout.AreaOf[x, y] < 0)
                        _layout.AreaOf[x, y] = area.Index;
                }
        }

        // a broad neck of land between the starter lands and the midlands, half of each
        private void Neck(Realm2Area a, Realm2Area b)
        {
            double ax = a.Center.X, ay = a.Center.Y;
            var len = Realm2Canvas.Dist(b.Center, ax, ay);
            double ux = (b.Center.X - ax) / len, uy = (b.Center.Y - ay) / len;
            for (var x = (int)Math.Min(ax, b.Center.X) - 30; x <= Math.Max(ax, b.Center.X) + 30; x++)
                for (var y = (int)Math.Min(ay, b.Center.Y) - 30; y <= Math.Max(ay, b.Center.Y) + 30; y++)
                {
                    if (!_c.Inside(x, y) || _layout.AreaOf[x, y] >= 0)
                        continue;
                    var t = (x - ax) * ux + (y - ay) * uy;
                    var side = Math.Abs((x - ax) * -uy + (y - ay) * ux);
                    if (t < 0 || t > len || side > 18 + (_c.Noise(x, y, 0.1, 31) - 0.5) * 8)
                        continue;
                    _layout.AreaOf[x, y] = (t < len / 2 ? a : b).Index;
                }
        }

        // ---- grounds

        private void Paint(Realm2Area area)
        {
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    if (_layout.AreaOf[x, y] == area.Index)
                    {
                        var terrain = LandAt(area, x, y);
                        _c.Set(x, y, Realm2Themes.Land(terrain).Ground, terrain);
                    }
        }

        // The starter lands: shore round the edge, the lowlands' sand, plains and forest inside.
        // The midlands: mid sand, plains and forest outside, the highlands' within.
        private TerrainType LandAt(Realm2Area area, int x, int y)
        {
            var d = Realm2Canvas.Dist(area.Center, x, y) / area.Radius + (_c.Noise(x, y, 0.15, area.Index * 7 + 3) - 0.5) * 0.15;
            var biome = _c.Noise(x, y, 0.05, area.Index * 7 + 11);
            if (area.Kind == Realm2Themes.Starter)
            {
                if (d > 0.86)
                    return TerrainType.ShoreSand;
                if (d > 0.74)
                    return TerrainType.ShorePlains;
                return biome < 0.38 ? TerrainType.LowSand : biome < 0.66 ? TerrainType.LowPlains : TerrainType.LowForest;
            }
            if (d > 0.5)
                return biome < 0.3 ? TerrainType.MidSand : biome < 0.65 ? TerrainType.MidPlains : TerrainType.MidForest;
            return biome < 0.3 ? TerrainType.HighSand : biome < 0.6 ? TerrainType.HighPlains : TerrainType.HighForest;
        }

        // the starter lands' shore meets the sea, not the void
        private void Coast(Realm2Area area)
        {
            var abyss = _c.GroundId("R2 Abyss");
            var reach = (int)(area.Radius * 1.25) + 10;
            for (var x = area.Center.X - reach; x <= area.Center.X + reach; x++)
                for (var y = area.Center.Y - reach; y <= area.Center.Y + reach; y++)
                {
                    if (!_c.Inside(x, y) || _layout.AreaOf[x, y] >= 0 || _c.At(x, y).TileId != abyss)
                        continue;
                    if (NearArea(x, y, area.Index, 4))
                        _c.Set(x, y, "Shallow Water");
                    else if (NearArea(x, y, area.Index, 9))
                        _c.Set(x, y, "Dark Water");
                }
        }

        private bool NearArea(int x, int y, int index, int r)
        {
            for (var dx = -r; dx <= r; dx += 2)
                for (var dy = -r; dy <= r; dy += 2)
                    if (_c.Inside(x + dx, y + dy) && _layout.AreaOf[x + dx, y + dy] == index && dx * dx + dy * dy <= r * r)
                        return true;
            return false;
        }

        // the Lucky gods' clearings in the midlands, either side of its heart
        private void LuckyGlades(Realm2Area area)
        {
            var angle = Math.Atan2(_layout.AxisY, _layout.AxisX) + Math.PI / 2;
            foreach (var (name, off) in new[] { ("Lucky Ent God", 0.0), ("Lucky Djinn", Math.PI) })
            {
                var at = new IntPoint(area.Center.X + (int)(Math.Cos(angle + off) * area.Radius * 0.45),
                                      area.Center.Y + (int)(Math.Sin(angle + off) * area.Radius * 0.45));
                for (var x = at.X - 7; x <= at.X + 7; x++)
                    for (var y = at.Y - 7; y <= at.Y + 7; y++)
                        if (_c.Inside(x, y) && Realm2Canvas.Dist(at, x, y) <= 7)
                            _c.Reserve(x, y);
                area.Events.Add((name, at));
            }
        }

        // ---- scenery, as the realm has it

        private void Scenery()
        {
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                {
                    if (_layout.AreaOf[x, y] < 0 || _c.IsReserved(x, y) || _c.At(x, y).TileObj != null || !_c.Walkable(x, y))
                        continue;
                    var decor = Realm2Themes.Land(_c.At(x, y).Terrain).Decor;
                    if (decor.Length > 0 && Rand.NextDouble() < 0.06)
                        _c.At(x, y).TileObj = decor[Rand.Next(decor.Length)];
                }
        }

        // where players arrive: a clearing in the starter lands, on the side away from the midlands
        private void SpawnRegion(Realm2Area start, Realm2Area next)
        {
            var dx = start.Center.X - next.Center.X;
            var dy = start.Center.Y - next.Center.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            var c = new IntPoint(start.Center.X + (int)(dx / len * start.Radius * 0.35), start.Center.Y + (int)(dy / len * start.Radius * 0.35));
            for (var x = c.X - 4; x <= c.X + 4; x++)
                for (var y = c.Y - 4; y <= c.Y + 4; y++)
                {
                    if (!_c.Inside(x, y) || Realm2Canvas.Dist(c, x, y) > 4)
                        continue;
                    _c.Set(x, y, "Grass", TerrainType.LowPlains);
                    if (Realm2Canvas.Dist(c, x, y) <= 2)
                        _c.At(x, y).Region = TileRegion.Spawn;
                    _c.At(x, y).TileObj = null;
                    _layout.AreaOf[x, y] = start.Index;
                    _c.Reserve(x, y);
                }
        }
    }
}
