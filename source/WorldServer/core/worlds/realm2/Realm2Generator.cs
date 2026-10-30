using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using Shared.terrain;
using WorldServer.core.structures;

namespace WorldServer.core.worlds.realm2
{
    /// <summary>One of a Realm 2's lands: its first three when it opens, an event's when the event comes.</summary>
    public sealed class Realm2Area
    {
        public int Index;
        public Realm2Theme Theme;           // an event's land; null for the first three
        public string Kind;                 // the theme's key, or Realm2Themes.Starter / Midlands / Godlands
        public IntPoint Center;
        public double Radius;
        public int Tiles;
        public string Link;                 // an event land out in the void: how it joins the rest
        public IntPoint? LinkTo;            // and where (RealmSnapshot)
        public int PainterSeed;             // an event land's (RealmSnapshot draws it again from it)
        // the midlands' clearings for the Lucky gods; an event land's arena
        public List<(string Event, IntPoint At)> Events = new List<(string, IntPoint)>();

        public string Name => Theme?.Name ?? Kind switch
        {
            Realm2Themes.Starter => "the shore and lowlands",
            Realm2Themes.Midlands => "the midlands and highlands",
            _ => "the Godlands",
        };
    }

    /// <summary>A Realm 2's lands, and which land each tile is in (-1 for the void).</summary>
    public sealed class Realm2Layout
    {
        public int Width, Height;
        public int[,] AreaOf;
        public List<Realm2Area> Areas = new List<Realm2Area>();
        // the way out from the shore: event lands come ever further along it
        public double AxisX, AxisY;
        public IntPoint Start;
        public double GodlandsFrom, GodlandsTo;     // how far along the way out the Godlands reach
        // stretches of the Godlands no event land is drawn over, so some always stay as they were
        public List<(IntPoint At, double R)> Untouched = new List<(IntPoint, double)>();
        public const double UntouchedRadius = 40;

        public Realm2Area AreaAt(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height && AreaOf[x, y] >= 0 ? Areas[AreaOf[x, y]] : null;

        /// <summary>How far along the way out a point is.</summary>
        public double Along(double x, double y) => (x - Start.X) * AxisX + (y - Start.Y) * AxisY;

        public Realm2Area Godlands => Areas.First(a => a.Kind == Realm2Themes.Godlands);
    }

    /// <summary>
    /// Makes a new Realm 2's map (SETUP-LOCAL.md, "Realm 2"): in a starry void, the shore and
    /// lowlands where players arrive, the midlands and highlands next to them (with clearings for
    /// the Lucky gods), and beyond them the Godlands, a broad land winding away from the shore with
    /// branches, chasms and islands off its edge joined by bridges and pools. The event lands
    /// aren't here: each is drawn into the Godlands when its event comes (Realm2EventPainter).
    /// </summary>
    public sealed class Realm2Generator
    {
        public const int Width = 1024, Height = 768;
        private const int Margin = 30;

        private readonly XmlData _data;
        private readonly int _seed;
        private Realm2Canvas _c;
        private Realm2Layout _layout;
        private Random Rand => _c.Rand;
        private readonly List<(IntPoint At, double R)> _spine = new List<(IntPoint, double)>();   // the Godlands' blobs

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
            var start = new IntPoint((int)(Width / 2 - ax * (half - 75)), (int)(Height / 2 - ay * (half - 75)));
            start = new IntPoint(Math.Clamp(start.X, 80, Width - 80), Math.Clamp(start.Y, 80, Height - 80));
            _layout.Start = start;

            var starter = AddArea(Realm2Themes.Starter, start, 50);
            var side = (Rand.NextDouble() - 0.5) * 50;
            var mid = AddArea(Realm2Themes.Midlands, new IntPoint((int)(start.X + ax * 100 - ay * side), (int)(start.Y + ay * 100 + ax * side)), 48);
            var gods = AddArea(Realm2Themes.Godlands, mid.Center, 0);

            Blob(starter, Edge(starter));
            Blob(mid, Edge(mid));
            Neck(starter, mid);
            GodlandsShape(gods, mid, ax, ay, half * 2);

            foreach (var area in _layout.Areas)
                Paint(area);
            Coast(starter);
            Islands(gods);
            LuckyGlades(mid);
            Scenery();
            SpawnRegion(starter, mid);

            Count();
            _layout.GodlandsFrom = double.MaxValue;
            _layout.GodlandsTo = double.MinValue;
            for (var x = 0; x < Width; x += 2)
                for (var y = 0; y < Height; y += 2)
                    if (_layout.AreaOf[x, y] == gods.Index)
                    {
                        var along = _layout.Along(x, y);
                        _layout.GodlandsFrom = Math.Min(_layout.GodlandsFrom, along);
                        _layout.GodlandsTo = Math.Max(_layout.GodlandsTo, along);
                    }
            KeepUntouched();
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

        internal static Func<double, double> Edge(Random rand, double radius)
        {
            var p1 = rand.NextDouble() * Math.PI * 2;
            var p2 = rand.NextDouble() * Math.PI * 2;
            var k1 = 2 + rand.Next(2);
            var k2 = 5 + rand.Next(3);
            return a => radius * (1 + 0.12 * Math.Sin(k1 * a + p1) + 0.06 * Math.Sin(k2 * a + p2));
        }

        private Func<double, double> Edge(Realm2Area area) => Edge(Rand, area.Radius);

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

        // The Godlands: a chain of overlapping blobs from the midlands out to the far edge, each a
        // turn left or right of the way out, with two or three branches off it, and chasms of void
        // through it away from its spine.
        private void GodlandsShape(Realm2Area gods, Realm2Area mid, double ax, double ay, double length)
        {
            var dir = Math.Atan2(ay, ax);
            var at = new IntPoint((int)(mid.Center.X + ax * 90), (int)(mid.Center.Y + ay * 90));
            var travelled = 0.0;
            while (travelled < length - 230)
            {
                var r = 64 + Rand.Next(22);
                _spine.Add((at, r));
                var turn = dir + (Rand.NextDouble() - 0.5) * 1.6;
                var step = 56 + Rand.Next(16);
                var next = new IntPoint((int)(at.X + Math.Cos(turn) * step), (int)(at.Y + Math.Sin(turn) * step));
                if (next.X < Margin + 90 || next.Y < Margin + 90 || next.X > Width - Margin - 90 || next.Y > Height - Margin - 90)
                    next = new IntPoint((int)(at.X + Math.Cos(dir) * step), (int)(at.Y + Math.Sin(dir) * step));
                if (next.X < Margin + 80 || next.Y < Margin + 80 || next.X > Width - Margin - 80 || next.Y > Height - Margin - 80)
                    break;
                travelled += (next.X - at.X) * ax + (next.Y - at.Y) * ay;
                at = next;
            }
            var trunk = _spine.Count;
            for (var b = 0; b < 2 + Rand.Next(2) && trunk > 2; b++)
            {
                var (from, _) = _spine[1 + Rand.Next(trunk - 1)];
                var bdir = dir + (Rand.Next(2) == 0 ? 1 : -1) * (1.0 + Rand.NextDouble() * 0.6);
                var p = from;
                for (var s = 0; s < 2 + Rand.Next(3); s++)
                {
                    p = new IntPoint((int)(p.X + Math.Cos(bdir) * 48), (int)(p.Y + Math.Sin(bdir) * 48));
                    if (p.X < Margin + 75 || p.Y < Margin + 75 || p.X > Width - Margin - 75 || p.Y > Height - Margin - 75)
                        break;
                    _spine.Add((p, 52 + Rand.Next(16)));
                    bdir += (Rand.NextDouble() - 0.5) * 0.8;
                }
            }

            gods.Center = _spine[_spine.Count / 2].At;
            for (var x = Margin; x < Width - Margin; x++)
                for (var y = Margin; y < Height - Margin; y++)
                {
                    if (_layout.AreaOf[x, y] >= 0)
                        continue;
                    var inside = false;
                    var nearSpine = false;
                    var wobble = (_c.Noise(x, y, 0.06, 41) - 0.5) * 22 + (_c.Noise(x, y, 0.25, 43) - 0.5) * 4;
                    foreach (var (c, r) in _spine)
                    {
                        var d = Realm2Canvas.Dist(c, x, y);
                        if (d < r + wobble)
                            inside = true;
                        if (d < 18)
                            nearSpine = true;
                    }
                    if (!inside)
                        continue;
                    // a chasm, never across the spine, so the way on stays open
                    if (!nearSpine && _c.Noise(x, y, 0.035, 47) > 0.71)
                        continue;
                    _layout.AreaOf[x, y] = gods.Index;
                }
        }

        // Three stretches of the Godlands kept as they are, about a third, two thirds and all the way
        // along it: the trunk's blobs nearest those points.
        private void KeepUntouched()
        {
            var span = _layout.GodlandsTo - _layout.GodlandsFrom;
            foreach (var share in new[] { 0.3, 0.6, 0.9 })
            {
                var want = _layout.GodlandsFrom + span * share;
                var (at, _) = _spine.OrderBy(s => Math.Abs(_layout.Along(s.At.X, s.At.Y) - want)).First();
                if (_layout.Untouched.All(u => Realm2Canvas.Dist(u.At, at.X, at.Y) > Realm2Layout.UntouchedRadius * 2))
                    _layout.Untouched.Add((at, Realm2Layout.UntouchedRadius));
            }
        }

        // a few islands of the Godlands out in the void, each joined by a bridge or a pool
        private void Islands(Realm2Area gods)
        {
            var made = 0;
            for (var tries = 0; tries < 80 && made < 3 + Rand.Next(2); tries++)
            {
                var (c, r) = _spine[Rand.Next(_spine.Count)];
                var a = Rand.NextDouble() * Math.PI * 2;
                var d = r + 40 + Rand.Next(20);
                var at = new IntPoint((int)(c.X + Math.Cos(a) * d), (int)(c.Y + Math.Sin(a) * d));
                var ir = 24 + Rand.Next(10);
                if (at.X < Margin + ir || at.Y < Margin + ir || at.X > Width - Margin - ir || at.Y > Height - Margin - ir)
                    continue;
                if (!Clear(at, ir + 10))
                    continue;
                var edge = Edge(Rand, ir);
                for (var x = at.X - ir - 6; x <= at.X + ir + 6; x++)
                    for (var y = at.Y - ir - 6; y <= at.Y + ir + 6; y++)
                        if (_c.Inside(x, y) && Realm2Canvas.Dist(at, x, y) < edge(Math.Atan2(y - at.Y, x - at.X)))
                        {
                            _layout.AreaOf[x, y] = gods.Index;
                            PaintTile(gods, x, y);
                        }
                // the crossing: from the island to the nearest land towards the spine
                var land = NearestLand(at, c, gods.Index, (int)(ir * 1.25) + 2);
                if (land is IntPoint to)
                    _c.Crossing(at.X, at.Y, to.X, to.Y, Rand.NextDouble() < 0.35, (x, y) => _layout.AreaOf[x, y] < 0);
                made++;
            }
        }

        private bool Clear(IntPoint at, int r)
        {
            for (var x = at.X - r; x <= at.X + r; x += 2)
                for (var y = at.Y - r; y <= at.Y + r; y += 2)
                    if (_c.Inside(x, y) && _layout.AreaOf[x, y] >= 0)
                        return false;
            return true;
        }

        private IntPoint? NearestLand(IntPoint from, IntPoint towards, int index, int skip)
        {
            var len = Realm2Canvas.Dist(towards, from.X, from.Y);
            for (var t = skip; t < len; t++)
            {
                var x = (int)(from.X + (towards.X - from.X) * t / len);
                var y = (int)(from.Y + (towards.Y - from.Y) * t / len);
                if (_c.Inside(x, y) && _layout.AreaOf[x, y] == index)
                    return new IntPoint(x, y);
            }
            return null;
        }

        // ---- grounds

        private void Paint(Realm2Area area)
        {
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    if (_layout.AreaOf[x, y] == area.Index)
                        PaintTile(area, x, y);
        }

        private void PaintTile(Realm2Area area, int x, int y)
        {
            var terrain = LandAt(area, x, y);
            if (terrain == TerrainType.Mountains)
            {
                // the realm's grey Godlands: rock, snowy here and there, the odd patch of grass
                var n = _c.Noise(x, y, 0.05, 61);
                var g = _c.Noise(x, y, 0.09, 63);
                _c.Set(x, y, g > 0.8 ? "Bright Grass" : n > 0.62 ? "Snowy Rock" : "Rock", terrain);
                return;
            }
            _c.Set(x, y, Realm2Themes.Land(terrain).Ground, terrain);
        }

        // The starter lands: shore round the edge, the lowlands' sand, plains and forest inside.
        // The midlands: mid sand, plains and forest outside, the highlands' within.
        private TerrainType LandAt(Realm2Area area, int x, int y)
        {
            if (area.Kind == Realm2Themes.Godlands)
                return TerrainType.Mountains;
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

        private static readonly string[] GodlandsDecor = { "Rock Grey", "Rock Grey", "Rock Grey", "Fir Tree 1", "Fir Tree 2", "Small Fir Tree 1", "Pink Flowers", "White Flowers", "Large Mushroom" };

        private void Scenery()
        {
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                {
                    if (_layout.AreaOf[x, y] < 0 || _c.IsReserved(x, y) || _c.At(x, y).TileObj != null || !_c.Walkable(x, y))
                        continue;
                    var terrain = _c.At(x, y).Terrain;
                    if (terrain == TerrainType.Mountains)
                    {
                        if (Rand.NextDouble() < 0.02)
                            _c.At(x, y).TileObj = GodlandsDecor[Rand.Next(GodlandsDecor.Length)];
                        continue;
                    }
                    var decor = Realm2Themes.Land(terrain).Decor;
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
