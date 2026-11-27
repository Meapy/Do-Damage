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
        public string Link;                 // an event land joined by a bridge or a pool (older maps')
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

    /// <summary>A Realm 2's lands, and which land each tile is in (-1 for the sea, its lakes and rivers).</summary>
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
        public const double UntouchedRadius = 34;

        public Realm2Area AreaAt(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height && AreaOf[x, y] >= 0 ? Areas[AreaOf[x, y]] : null;

        /// <summary>How far along the way out a point is.</summary>
        public double Along(double x, double y) => (x - Start.X) * AxisX + (y - Start.Y) * AxisY;

        public Realm2Area Godlands => Areas.First(a => a.Kind == Realm2Themes.Godlands);
    }

    /// <summary>
    /// Makes a new Realm 2's map (SETUP-LOCAL.md, "Realm 2"): one continent in the sea, crossed
    /// from one end to the other by the way out. At its back, the shore and lowlands where players
    /// arrive; then the midlands and highlands (with clearings for the Lucky gods); then the
    /// Godlands, filling the rest out to the far coast, cut by mountain lakes and the rivers that run
    /// from them to the sea, with snow thickening towards the far end and a few islands off the coast
    /// joined by bridges and pools. The event lands aren't here: each is drawn into the Godlands when
    /// its event comes (Realm2EventPainter), spilling out over the coast where it meets it.
    /// </summary>
    public sealed class Realm2Generator
    {
        /// <summary>
        /// Which map a seed makes: a snapshot written under another (RealmSnapshot.Map) has its
        /// lands somewhere this map doesn't, so they aren't drawn again.
        /// </summary>
        public const int Version = 2;

        public const int Width = 640, Height = 480;
        private const int StarterDepth = 58;    // how far along the way out the lowlands reach from the spawn
        private const int MidlandsDepth = 150;  // and the midlands
        private const int HighlandsFrom = 108;

        private readonly XmlData _data;
        private readonly int _seed;
        private Realm2Canvas _c;
        private Realm2Layout _layout;
        private int[,] _sea;                    // tiles to the nearest water off the land (capped)
        private int[,] _ocean;                  // and to the sea round it
        private Random Rand => _c.Rand;

        public Realm2Generator(XmlData data, int seed)
        {
            _data = data;
            _seed = seed;
        }

        public (Realm2Canvas Canvas, Realm2Layout Layout) Generate()
        {
            _c = new Realm2Canvas(_data, _seed, 0, 0, Width, Height, "Dark Water");
            _layout = new Realm2Layout { Width = Width, Height = Height, AreaOf = new int[Width, Height] };

            // the way out: across the map or corner to corner (not the short way), give or take; the
            // spawn near the coast at its back
            var ways = new[] { 0, 1, 3, 4, 5, 7 };
            var angle = ways[Rand.Next(ways.Length)] * Math.PI / 4 + (Rand.NextDouble() - 0.5) * 0.4;
            double ax = Math.Cos(angle), ay = Math.Sin(angle);
            _layout.AxisX = ax;
            _layout.AxisY = ay;
            var half = (Math.Abs(ax) * Width + Math.Abs(ay) * Height) / 2;
            var start = new IntPoint((int)(Width / 2 - ax * (half - 70)), (int)(Height / 2 - ay * (half - 70)));
            start = new IntPoint(Math.Clamp(start.X, 70, Width - 70), Math.Clamp(start.Y, 70, Height - 70));
            _layout.Start = start;

            var starter = AddArea(Realm2Themes.Starter, start, StarterDepth);
            var side = (Rand.NextDouble() - 0.5) * 40;
            var midAt = (StarterDepth + MidlandsDepth) / 2.0;
            var mid = AddArea(Realm2Themes.Midlands, new IntPoint((int)(start.X + ax * midAt - ay * side), (int)(start.Y + ay * midAt + ax * side)), 60);
            var gods = AddArea(Realm2Themes.Godlands, start, 0);

            Continent(starter, mid, gods);
            MeasureSea();
            _ocean = _sea;
            Lakes(gods);
            Rivers(gods);
            MeasureSea();
            Paint();
            Shallows();
            Islands(gods);
            LuckyGlades(mid);
            Scenery();
            SpawnRegion(starter);

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
            gods.Center = GodlandsPoint((_layout.GodlandsFrom + _layout.GodlandsTo) / 2) ?? gods.Center;
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

        internal static Func<double, double> Edge(Random rand, double radius)
        {
            var p1 = rand.NextDouble() * Math.PI * 2;
            var p2 = rand.NextDouble() * Math.PI * 2;
            var k1 = 2 + rand.Next(2);
            var k2 = 5 + rand.Next(3);
            return a => radius * (1 + 0.12 * Math.Sin(k1 * a + p1) + 0.06 * Math.Sin(k2 * a + p2));
        }

        // ---- the land

        // The continent: an island stretched along the way out, its coast wandering in bays and
        // headlands (and kept off the map's edge), split along the way out into the lowlands, the
        // midlands and the Godlands with ragged borders.
        private void Continent(Realm2Area starter, Realm2Area mid, Realm2Area gods)
        {
            double ax = _layout.AxisX, ay = _layout.AxisY;
            var half = (Math.Abs(ax) * Width + Math.Abs(ay) * Height) / 2;
            double cx = Width / 2.0, cy = Height / 2.0;
            double length = half - 24, breadth = 172;
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                {
                    double dx = x - cx, dy = y - cy;
                    var u = (dx * ax + dy * ay) / length;
                    var v = (-dx * ay + dy * ax) / breadth;
                    var m = Math.Sqrt(u * u + v * v);
                    var coast = 1 + (_c.Noise(x, y, 0.009, 3) - 0.5) * 0.55 + (_c.Noise(x, y, 0.035, 5) - 0.5) * 0.14;
                    var inland = Math.Min(Math.Min(x, Width - 1 - x), Math.Min(y, Height - 1 - y));
                    if (m > coast || inland < 12 + _c.Noise(x, y, 0.018, 7) * 34 + _c.Noise(x, y, 0.07, 9) * 6)
                    {
                        _layout.AreaOf[x, y] = -1;
                        continue;
                    }
                    var along = _layout.Along(x, y) + (_c.Noise(x, y, 0.02, 11) - 0.5) * 36 + (_c.Noise(x, y, 0.09, 13) - 0.5) * 6;
                    _layout.AreaOf[x, y] = (along < StarterDepth ? starter : along < MidlandsDepth ? mid : gods).Index;
                }
            // only the one land: what the coast cut off from the spawn's goes back to the sea (the
            // spawn moved in from the coast first, if the coast came over it)
            while (_layout.AreaOf[starter.Center.X, starter.Center.Y] < 0)
                starter.Center = _layout.Start = new IntPoint((int)(starter.Center.X + ax * 4), (int)(starter.Center.Y + ay * 4));
            var seen = new bool[Width, Height];
            var queue = new Queue<(int, int)>();
            queue.Enqueue((starter.Center.X, starter.Center.Y));
            seen[starter.Center.X, starter.Center.Y] = true;
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && ny >= 0 && nx < Width && ny < Height && !seen[nx, ny] && _layout.AreaOf[nx, ny] >= 0)
                    {
                        seen[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
            }
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    if (!seen[x, y])
                        _layout.AreaOf[x, y] = -1;
        }

        // Mountain lakes in the Godlands: deep water ringed by shallows, never on the way out's
        // middle line, so the land always goes on round them.
        private void Lakes(Realm2Area gods)
        {
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                {
                    if (_layout.AreaOf[x, y] != gods.Index)
                        continue;
                    var across = Math.Abs((x - _layout.Start.X) * -_layout.AxisY + (y - _layout.Start.Y) * _layout.AxisX);
                    if (across < 22)
                        continue;
                    if (_c.Noise(x, y, 0.028, 47) > 0.7)
                        _layout.AreaOf[x, y] = -1;
                }
        }

        // how far each tile of land is from the water off it
        private void MeasureSea()
        {
            const int cap = 200;
            _sea = new int[Width, Height];
            var queue = new Queue<(int, int)>();
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    if (_layout.AreaOf[x, y] < 0)
                        queue.Enqueue((x, y));
                    else
                        _sea[x, y] = cap;
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                var d = _sea[x, y] + 1;
                if (d >= cap)
                    continue;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && ny >= 0 && nx < Width && ny < Height && _sea[nx, ny] > d)
                    {
                        _sea[nx, ny] = d;
                        queue.Enqueue((nx, ny));
                    }
            }
        }

        private int Sea(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? _sea[x, y] : 0;
        private int Ocean(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? _ocean[x, y] : 0;

        // Two or three rivers from the heart of the Godlands, winding down through the lands (and
        // any lake in their way) to the sea: shallow enough to wade (slowly), with a deep channel where they run widest.
        private readonly HashSet<(int, int)> _river = new HashSet<(int, int)>();

        private void Rivers(Realm2Area gods)
        {
            var count = 2 + Rand.Next(2);
            for (var tries = 0; tries < 40 && count > 0; tries++)
            {
                int x = Rand.Next(60, Width - 60), y = Rand.Next(60, Height - 60);
                if (_layout.AreaOf[x, y] != gods.Index || Ocean(x, y) < 40
                    || _river.Any(r => Math.Abs(r.Item1 - x) + Math.Abs(r.Item2 - y) < 60))
                    continue;
                count--;
                // downhill: towards the nearest water, wandering as it goes
                double px = x, py = y, dir = Rand.NextDouble() * Math.PI * 2;
                var width = 1.6;
                for (var step = 0; step < 700; step++)
                {
                    var (gx, gy) = Downhill((int)px, (int)py);
                    var want = gx == 0 && gy == 0 ? dir : Math.Atan2(gy, gx);
                    var turn = Math.Atan2(Math.Sin(want - dir), Math.Cos(want - dir));
                    dir += turn * 0.12 + (_c.Noise(px, py, 0.05, 71 + count) - 0.5) * 0.5;
                    px += Math.Cos(dir);
                    py += Math.Sin(dir);
                    width = Math.Min(3.4, width + 0.006);
                    var ix = (int)px;
                    var iy = (int)py;
                    if (ix < 1 || iy < 1 || ix >= Width - 1 || iy >= Height - 1)
                        break;
                    for (var dx = -4; dx <= 4; dx++)
                        for (var dy = -4; dy <= 4; dy++)
                            if (dx * dx + dy * dy <= width * width)
                                _river.Add((ix + dx, iy + dy));
                    if (Ocean(ix, iy) == 0)
                        break;
                }
            }
            foreach (var (x, y) in _river)
                if (x >= 0 && y >= 0 && x < Width && y < Height)
                    _layout.AreaOf[x, y] = -1;
        }

        private (int, int) Downhill(int x, int y)
        {
            int bx = 0, by = 0, best = Ocean(x, y);
            for (var dx = -6; dx <= 6; dx += 3)
                for (var dy = -6; dy <= 6; dy += 3)
                    if (Ocean(x + dx, y + dy) < best)
                    {
                        best = Ocean(x + dx, y + dy);
                        bx = dx;
                        by = dy;
                    }
            return (bx, by);
        }

        // ---- grounds

        private void Paint()
        {
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    if (_layout.AreaOf[x, y] >= 0)
                        PaintTile(_layout.Areas[_layout.AreaOf[x, y]], x, y);
        }

        private void PaintTile(Realm2Area area, int x, int y)
        {
            var terrain = LandAt(area, x, y);
            if (terrain == TerrainType.Mountains)
            {
                // the realm's grey Godlands: rock, the odd meadow, snow drifting in as the land
                // climbs towards the far end (and thick on the peaks there), dark scree by the water
                var share = Math.Clamp((_layout.Along(x, y) - MidlandsDepth) / Math.Max(1, Width * 0.6), 0, 1);
                var n = _c.Noise(x, y, 0.05, 61);
                var g = _c.Noise(x, y, 0.09, 63);
                var ground = g > 0.8 - share * 0.06 && share < 0.7 ? "Bright Grass"
                    : n > 0.74 - share * 0.16 ? "Snowy Rock"
                    : Sea(x, y) <= 2 + (int)(_c.Noise(x, y, 0.2, 65) * 2) ? "Dark Sand"
                    : "Rock";
                _c.Set(x, y, ground, terrain);
                return;
            }
            _c.Set(x, y, Realm2Themes.Land(terrain).Ground, terrain);
        }

        // The starter lands: shore along the coast, the lowlands' sand, plains and forest inland.
        // The midlands: mid sand, plains and forest, the highlands' further out.
        private TerrainType LandAt(Realm2Area area, int x, int y)
        {
            if (area.Kind == Realm2Themes.Godlands)
                return TerrainType.Mountains;
            var biome = _c.Noise(x, y, 0.05, area.Index * 7 + 11);
            var wobble = (_c.Noise(x, y, 0.15, area.Index * 7 + 3) - 0.5) * 3;
            if (area.Kind == Realm2Themes.Starter)
            {
                if (Sea(x, y) + wobble < 6)
                    return TerrainType.ShoreSand;
                if (Sea(x, y) + wobble < 10)
                    return TerrainType.ShorePlains;
                return biome < 0.38 ? TerrainType.LowSand : biome < 0.66 ? TerrainType.LowPlains : TerrainType.LowForest;
            }
            var along = _layout.Along(x, y) + (_c.Noise(x, y, 0.04, 17) - 0.5) * 24;
            if (along < HighlandsFrom)
                return biome < 0.3 ? TerrainType.MidSand : biome < 0.65 ? TerrainType.MidPlains : TerrainType.MidForest;
            return biome < 0.3 ? TerrainType.HighSand : biome < 0.6 ? TerrainType.HighPlains : TerrainType.HighForest;
        }

        // The water: the sea deep beyond a band of shallows along every coast, the lakes and rivers
        // shallow at their edges and deep where they're widest. The shallows can be waded, slowly.
        private void Shallows()
        {
            var land = new int[Width, Height];
            var queue = new Queue<(int, int)>();
            const int cap = 8;
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    if (_layout.AreaOf[x, y] >= 0)
                        queue.Enqueue((x, y));
                    else
                        land[x, y] = cap;
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                var d = land[x, y] + 1;
                if (d >= cap)
                    continue;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && ny >= 0 && nx < Width && ny < Height && land[nx, ny] > d)
                    {
                        land[nx, ny] = d;
                        queue.Enqueue((nx, ny));
                    }
            }
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                {
                    if (_layout.AreaOf[x, y] >= 0)
                        continue;
                    var river = _river.Contains((x, y));
                    var shallow = river ? 5 : 2 + (int)(_c.Noise(x, y, 0.12, 81) * 3);
                    _c.Set(x, y, land[x, y] <= shallow ? "Shallow Water" : "Dark Water");
                }
        }

        // a few islands of the Godlands off its coast, each joined by a bridge or a pool
        private void Islands(Realm2Area gods)
        {
            var made = 0;
            var want = 3 + Rand.Next(3);
            for (var tries = 0; tries < 400 && made < want; tries++)
            {
                var ir = 9 + Rand.Next(7);
                int x0 = Rand.Next(ir + 3, Width - ir - 3), y0 = Rand.Next(ir + 3, Height - ir - 3);
                var at = new IntPoint(x0, y0);
                if (_layout.Along(x0, y0) < MidlandsDepth || !Clear(at, ir + 5))
                    continue;
                var towards = new IntPoint(Width / 2, Height / 2);
                var land = NearestLand(at, towards, gods.Index, ir + 5, ir + 30);
                if (land is not IntPoint to)
                    continue;
                var edge = Edge(Rand, ir);
                for (var x = at.X - ir - 3; x <= at.X + ir + 3; x++)
                    for (var y = at.Y - ir - 3; y <= at.Y + ir + 3; y++)
                        if (_c.Inside(x, y) && Realm2Canvas.Dist(at, x, y) < edge(Math.Atan2(y - at.Y, x - at.X)))
                        {
                            _layout.AreaOf[x, y] = gods.Index;
                            _c.Set(x, y, _c.Noise(x, y, 0.1, 91) > 0.6 ? "Bright Grass" : "Rock", TerrainType.Mountains);
                        }
                for (var x = at.X - ir - 6; x <= at.X + ir + 6; x++)
                    for (var y = at.Y - ir - 6; y <= at.Y + ir + 6; y++)
                        if (_c.Inside(x, y) && _layout.AreaOf[x, y] < 0 && Realm2Canvas.Dist(at, x, y) < ir + 3 + _c.Noise(x, y, 0.2, 93) * 3)
                            _c.Set(x, y, "Shallow Water");
                _c.Crossing(at.X, at.Y, to.X, to.Y, Rand.NextDouble() < 0.35, (x, y) => _layout.AreaOf[x, y] < 0);
                made++;
            }
        }

        private bool Clear(IntPoint at, int r)
        {
            for (var x = at.X - r; x <= at.X + r; x += 2)
                for (var y = at.Y - r; y <= at.Y + r; y += 2)
                    if (!_c.Inside(x, y) || _layout.AreaOf[x, y] >= 0)
                        return false;
            return true;
        }

        private IntPoint? NearestLand(IntPoint from, IntPoint towards, int index, int skip, int reach)
        {
            var len = Realm2Canvas.Dist(towards, from.X, from.Y);
            for (var t = skip; t < Math.Min(len, reach); t++)
            {
                var x = (int)(from.X + (towards.X - from.X) * t / len);
                var y = (int)(from.Y + (towards.Y - from.Y) * t / len);
                if (_c.Inside(x, y) && _layout.AreaOf[x, y] >= 0)
                    return _layout.AreaOf[x, y] == index ? new IntPoint(x, y) : null;
            }
            return null;
        }

        // the Lucky gods' clearings in the midlands, either side of the way out
        private void LuckyGlades(Realm2Area area)
        {
            var angle = Math.Atan2(_layout.AxisY, _layout.AxisX) + Math.PI / 2;
            foreach (var (name, off) in new[] { ("Lucky Ent God", 0.0), ("Lucky Djinn", Math.PI) })
            {
                var at = area.Center;
                for (var d = area.Radius * 0.6; d >= 0; d -= 3)
                {
                    var p = new IntPoint(area.Center.X + (int)(Math.Cos(angle + off) * d), area.Center.Y + (int)(Math.Sin(angle + off) * d));
                    if (_layout.AreaOf[p.X, p.Y] == area.Index && Sea(p.X, p.Y) >= 10)
                    {
                        at = p;
                        break;
                    }
                }
                for (var x = at.X - 7; x <= at.X + 7; x++)
                    for (var y = at.Y - 7; y <= at.Y + 7; y++)
                        if (_c.Inside(x, y) && Realm2Canvas.Dist(at, x, y) <= 7)
                            _c.Reserve(x, y);
                area.Events.Add((name, at));
            }
        }

        // ---- scenery, as the realm has it

        private static readonly string[] GodlandsDecor = { "Rock Grey", "Rock Grey", "Rock Grey", "Fir Tree 1", "Fir Tree 2", "Small Fir Tree 1", "Pink Flowers", "White Flowers", "Large Mushroom" };
        private static readonly string[] SnowDecor = { "Rock Grey", "Fir Tree 1", "Fir Tree 2", "Small Fir Tree 1", "Small Fir Tree 1" };

        private void Scenery()
        {
            var snow = _c.GroundId("Snowy Rock");
            for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                {
                    if (_layout.AreaOf[x, y] < 0 || _c.IsReserved(x, y) || _c.At(x, y).TileObj != null || !_c.Walkable(x, y))
                        continue;
                    var terrain = _c.At(x, y).Terrain;
                    if (terrain == TerrainType.Mountains)
                    {
                        if (Rand.NextDouble() < 0.02)
                        {
                            var set = _c.At(x, y).TileId == snow ? SnowDecor : GodlandsDecor;
                            _c.At(x, y).TileObj = set[Rand.Next(set.Length)];
                        }
                        continue;
                    }
                    var decor = Realm2Themes.Land(terrain).Decor;
                    if (decor.Length > 0 && Rand.NextDouble() < 0.06)
                        _c.At(x, y).TileObj = decor[Rand.Next(decor.Length)];
                }
        }

        // where players arrive: a clearing in the lowlands, a little back from the start towards the
        // shore, on dry land
        private void SpawnRegion(Realm2Area start)
        {
            var c = start.Center;
            for (var back = 18; back >= -30; back -= 3)
            {
                var p = new IntPoint((int)(start.Center.X - _layout.AxisX * back), (int)(start.Center.Y - _layout.AxisY * back));
                if (p.X > 8 && p.Y > 8 && p.X < Width - 8 && p.Y < Height - 8 && _layout.AreaOf[p.X, p.Y] == start.Index && Sea(p.X, p.Y) >= 8)
                {
                    c = p;
                    break;
                }
            }
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

        // ---- the stretches kept as they are

        // the Godlands' point at `along` furthest from the water, near the way out's middle line
        private IntPoint? GodlandsPoint(double along)
        {
            IntPoint? best = null;
            var score = double.MinValue;
            var gods = _layout.Godlands.Index;
            for (var s = -200; s <= 200; s += 4)
            {
                var x = (int)(_layout.Start.X + _layout.AxisX * along - _layout.AxisY * s);
                var y = (int)(_layout.Start.Y + _layout.AxisY * along + _layout.AxisX * s);
                if (x < 0 || y < 0 || x >= Width || y >= Height || _layout.AreaOf[x, y] != gods)
                    continue;
                var v = Math.Min(Sea(x, y), 16) - Math.Abs(s) * 0.02;
                if (v > score)
                {
                    score = v;
                    best = new IntPoint(x, y);
                }
            }
            return best;
        }

        // Three stretches of the Godlands kept as they are, about a third, two thirds and all the way
        // along it.
        private void KeepUntouched()
        {
            var span = _layout.GodlandsTo - _layout.GodlandsFrom;
            foreach (var share in new[] { 0.3, 0.6, 0.88 })
                if (GodlandsPoint(_layout.GodlandsFrom + span * share) is IntPoint at
                    && _layout.Untouched.All(u => Realm2Canvas.Dist(u.At, at.X, at.Y) > Realm2Layout.UntouchedRadius * 2))
                    _layout.Untouched.Add((at, Realm2Layout.UntouchedRadius));
        }
    }
}
