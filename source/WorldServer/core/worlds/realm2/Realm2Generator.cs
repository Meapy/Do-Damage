using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using Shared.terrain;
using WorldServer.core.structures;

namespace WorldServer.core.worlds.realm2
{
    /// <summary>One area of a Realm 2 as it was laid out.</summary>
    public sealed class Realm2Area
    {
        public int Index;
        public Realm2Theme Theme;           // null for the starter lands and the midlands
        public string Kind;                 // the theme's key, or Realm2Themes.Starter / Midlands
        public IntPoint Center;
        public double Radius;
        public int Tiles;
        // where its events stand: one for an event area (its arena), two in the midlands (the Lucky gods)
        public List<(string Event, IntPoint At)> Events = new List<(string, IntPoint)>();
        public string Name => Theme?.Name ?? (Kind == Realm2Themes.Starter ? "the shore and lowlands" : "the midlands and highlands");
    }

    /// <summary>What the generator made: the areas, and which area each tile is in (-1 for none).</summary>
    public sealed class Realm2Layout
    {
        public int Width, Height;
        public int[,] AreaOf;
        public List<Realm2Area> Areas = new List<Realm2Area>();
        public List<string> Links = new List<string>();     // how each area joins the next, for /realmstatus
        public int EventCount => Areas.Sum(a => a.Events.Count);

        public Realm2Area AreaAt(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height && AreaOf[x, y] >= 0 ? Areas[AreaOf[x, y]] : null;
    }

    /// <summary>
    /// Makes a new Realm 2 (SETUP-LOCAL.md, "Realm 2"): a chain of thirteen areas across a void, in
    /// a random winding path over a grid. First the starter lands (shore and lowlands) and the
    /// midlands (mid and high lands, with the Lucky Ent God and Lucky Djinn), joined by land; then
    /// the eleven event areas in a random order, each joined to the next by a bridge, a pool or
    /// land. Each area is a blob with a ragged edge; an event area has its arena at its heart,
    /// dressed for its boss.
    /// </summary>
    public sealed class Realm2Generator
    {
        private const int Cols = 5, Rows = 4;
        private const int Cell = 136;               // tiles between neighbouring areas' centres
        private const int Margin = 28;
        private const double BaseRadius = 46;        // an area's radius before its edge's wobble
        private const int Jitter = 6;
        public const int ArenaRadius = 14;
        private const int ArenaClear = 19;           // no scenery or walls this close to an arena's centre

        private readonly XmlData _data;
        private readonly Random _rand;
        private readonly int _seed;
        private TerrainTile[,] _tiles;
        private int _w, _h;
        private Realm2Layout _layout;
        private readonly Dictionary<string, TerrainTile> _blank = new Dictionary<string, TerrainTile>();
        private bool[,] _reserved;                   // links and arenas: no scenery

        public Realm2Generator(XmlData data, int seed)
        {
            _data = data;
            _seed = seed;
            _rand = new Random(seed);
        }

        public (TerrainTile[,] Tiles, Realm2Layout Layout) Generate()
        {
            _w = Cols * Cell + Margin * 2;
            _h = Rows * Cell + Margin * 2;
            _tiles = new TerrainTile[_w, _h];
            _reserved = new bool[_w, _h];
            _layout = new Realm2Layout { Width = _w, Height = _h, AreaOf = new int[_w, _h] };
            for (var x = 0; x < _w; x++)
                for (var y = 0; y < _h; y++)
                {
                    _tiles[x, y] = Ground("R2 Abyss");
                    _layout.AreaOf[x, y] = -1;
                }

            var cells = Path(2 + Realm2Themes.Events.Length);
            var themes = Realm2Themes.Events.OrderBy(_ => _rand.Next()).ToList();
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                var area = new Realm2Area
                {
                    Index = i,
                    Theme = i < 2 ? null : themes[i - 2],
                    Kind = i == 0 ? Realm2Themes.Starter : i == 1 ? Realm2Themes.Midlands : themes[i - 2].Key,
                    Center = new IntPoint(Margin + c.X * Cell + Cell / 2 + _rand.Next(-Jitter, Jitter + 1),
                                          Margin + c.Y * Cell + Cell / 2 + _rand.Next(-Jitter, Jitter + 1)),
                    Radius = BaseRadius + _rand.Next(-3, 4),
                };
                _layout.Areas.Add(area);
            }

            foreach (var area in _layout.Areas)
                Shape(area);
            if (_layout.Areas.Count > 1)
                _layout.Links.Add("land");
            for (var i = 1; i < _layout.Areas.Count - 1; i++)
            {
                var roll = _rand.NextDouble();
                _layout.Links.Add(roll < 0.5 ? "bridge" : roll < 0.75 ? "pool" : "land");
            }

            foreach (var area in _layout.Areas)
                Paint(area);
            // links after painting: land joins with each side's ground, bridges and pools over the void
            for (var i = 0; i < _layout.Links.Count; i++)
                Link(_layout.Areas[i], _layout.Areas[i + 1], _layout.Links[i]);
            Coast(_layout.Areas[0]);
            foreach (var area in _layout.Areas)
            {
                if (area.Theme != null)
                    Arena(area);
                else if (area.Kind == Realm2Themes.Midlands)
                    LuckyGlades(area);
                Walls(area);
                Scenery(area);
            }
            SpawnRegion(_layout.Areas[0], cells.Count > 1 ? _layout.Areas[1] : null);

            foreach (var area in _layout.Areas)
                area.Tiles = 0;
            for (var x = 0; x < _w; x++)
                for (var y = 0; y < _h; y++)
                    if (_layout.AreaOf[x, y] >= 0)
                        _layout.Areas[_layout.AreaOf[x, y]].Tiles++;
            return (_tiles, _layout);
        }

        // ---- the chain: a random self-avoiding walk over the grid, as long as asked

        private List<IntPoint> Path(int length)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var start = new IntPoint(_rand.Next(Cols), _rand.Next(Rows));
                var path = new List<IntPoint> { start };
                if (Extend(path, length))
                    return path;
            }
            // a snake through the grid never fails
            var snake = new List<IntPoint>();
            for (var y = 0; y < Rows; y++)
                for (var i = 0; i < Cols; i++)
                    snake.Add(new IntPoint(y % 2 == 0 ? i : Cols - 1 - i, y));
            return snake.Take(length).ToList();
        }

        private static readonly IntPoint[] Steps = { new IntPoint(1, 0), new IntPoint(-1, 0), new IntPoint(0, 1), new IntPoint(0, -1) };

        private bool Extend(List<IntPoint> path, int length)
        {
            if (path.Count == length)
                return true;
            var last = path[^1];
            foreach (var s in Steps.OrderBy(_ => _rand.Next()))
            {
                var next = new IntPoint(last.X + s.X, last.Y + s.Y);
                if (next.X < 0 || next.Y < 0 || next.X >= Cols || next.Y >= Rows || path.Contains(next))
                    continue;
                path.Add(next);
                if (Extend(path, length))
                    return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        // ---- shapes

        // the area's edge at an angle: its radius with a slow wobble and a ragged fringe
        private Func<double, double> Edge(Realm2Area area)
        {
            var p1 = _rand.NextDouble() * Math.PI * 2;
            var p2 = _rand.NextDouble() * Math.PI * 2;
            var k1 = 2 + _rand.Next(2);
            var k2 = 5 + _rand.Next(3);
            return a => area.Radius * (1 + 0.12 * Math.Sin(k1 * a + p1) + 0.06 * Math.Sin(k2 * a + p2));
        }

        private void Shape(Realm2Area area)
        {
            var edge = Edge(area);
            var reach = (int)(area.Radius * 1.25) + 2;
            for (var x = area.Center.X - reach; x <= area.Center.X + reach; x++)
                for (var y = area.Center.Y - reach; y <= area.Center.Y + reach; y++)
                {
                    if (!Inside(x, y))
                        continue;
                    var dx = x - area.Center.X;
                    var dy = y - area.Center.Y;
                    var d = Math.Sqrt(dx * dx + dy * dy) + (Noise(x, y, 0.25, 7) - 0.5) * 4;
                    if (d < edge(Math.Atan2(dy, dx)) && _layout.AreaOf[x, y] < 0)
                        _layout.AreaOf[x, y] = area.Index;
                }
        }

        // ---- grounds

        private void Paint(Realm2Area area)
        {
            for (var x = 0; x < _w; x++)
                for (var y = 0; y < _h; y++)
                    if (_layout.AreaOf[x, y] == area.Index)
                        PaintTile(area, x, y);
        }

        private void PaintTile(Realm2Area area, int x, int y)
        {
            if (area.Theme == null)
            {
                var terrain = LandAt(area, x, y);
                var t = Ground(Realm2Themes.Land(terrain).Ground);
                t.Terrain = terrain;
                _tiles[x, y] = t;
                return;
            }
            var theme = area.Theme;
            var n = Noise(x, y, 0.07, area.Index * 13 + 1);
            var name = n > 0.62 ? theme.Patches : theme.Ground;
            if (theme.Pools != null && Noise(x, y, 0.11, area.Index * 13 + 5) > 0.78 && Dist(area.Center, x, y) > ArenaClear + 2)
                name = theme.Pools;
            _tiles[x, y] = Ground(name);
        }

        // The starter lands: shore round the edge, the lowlands' sand, plains and forest inside.
        // The midlands: mid sand, plains and forest outside, the highlands' within.
        private TerrainType LandAt(Realm2Area area, int x, int y)
        {
            var d = Dist(area.Center, x, y) / area.Radius + (Noise(x, y, 0.15, area.Index * 7 + 3) - 0.5) * 0.15;
            var biome = Noise(x, y, 0.05, area.Index * 7 + 11);
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
            var reach = (int)(area.Radius * 1.25) + 10;
            for (var x = area.Center.X - reach; x <= area.Center.X + reach; x++)
                for (var y = area.Center.Y - reach; y <= area.Center.Y + reach; y++)
                {
                    if (!Inside(x, y) || _layout.AreaOf[x, y] >= 0 || _tiles[x, y].TileId != Ground("R2 Abyss").TileId)
                        continue;
                    if (NearArea(x, y, area.Index, 4))
                        _tiles[x, y] = Ground("Shallow Water");
                    else if (NearArea(x, y, area.Index, 9))
                        _tiles[x, y] = Ground("Dark Water");
                }
        }

        private bool NearArea(int x, int y, int index, int r)
        {
            for (var dx = -r; dx <= r; dx += 2)
                for (var dy = -r; dy <= r; dy += 2)
                    if (Inside(x + dx, y + dy) && _layout.AreaOf[x + dx, y + dy] == index && dx * dx + dy * dy <= r * r)
                        return true;
            return false;
        }

        // ---- links

        private void Link(Realm2Area a, Realm2Area b, string kind)
        {
            double ax = a.Center.X, ay = a.Center.Y, bx = b.Center.X, by = b.Center.Y;
            var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            double ux = (bx - ax) / len, uy = (by - ay) / len;
            var minX = (int)Math.Min(ax, bx) - 30;
            var maxX = (int)Math.Max(ax, bx) + 30;
            var minY = (int)Math.Min(ay, by) - 30;
            var maxY = (int)Math.Max(ay, by) + 30;

            for (var x = minX; x <= maxX; x++)
                for (var y = minY; y <= maxY; y++)
                {
                    if (!Inside(x, y))
                        continue;
                    var t = (x - ax) * ux + (y - ay) * uy;              // along the line, from a
                    var side = Math.Abs((x - ax) * -uy + (y - ay) * ux); // away from it
                    if (t < 0 || t > len)
                        continue;
                    var owner = _layout.AreaOf[x, y];
                    switch (kind)
                    {
                        case "land":
                        {
                            // a broad neck of land, each half the ground of the nearer area
                            var width = 16 + (Noise(x, y, 0.1, a.Index * 31) - 0.5) * 8;
                            if (side > width || owner >= 0)
                                break;
                            var near = t < len / 2 ? a : b;
                            _layout.AreaOf[x, y] = near.Index;
                            PaintTile(near, x, y);
                            break;
                        }
                        case "bridge":
                        {
                            if (owner >= 0)
                                break;
                            if (side <= 2.5)
                            {
                                _tiles[x, y] = Ground("R2 Bridge");
                                _reserved[x, y] = true;
                            }
                            else if (side <= 3.5)
                            {
                                _tiles[x, y] = Ground("R2 Bridge Rail");
                                _reserved[x, y] = true;
                            }
                            break;
                        }
                        case "pool":
                        {
                            // an oval pool across the gap that reaches a few tiles into both
                            var mid = len / 2;
                            var along = (t - mid) / (mid - Math.Min(a.Radius, b.Radius) * 0.8 + 6);
                            var across = side / (11 + (Noise(x, y, 0.2, b.Index * 17) - 0.5) * 6);
                            if (along * along + across * across > 1)
                                break;
                            _tiles[x, y] = Ground("R2 Pool Water");
                            _reserved[x, y] = true;
                            if (owner < 0)
                                _layout.AreaOf[x, y] = (t < mid ? a : b).Index;
                            break;
                        }
                    }
                }
        }

        // ---- arenas: each event area's heart, dressed for its boss

        private void Arena(Realm2Area area)
        {
            var c = area.Center;
            var theme = area.Theme;
            var arena = theme.Arena;
            Disc(c, ArenaRadius, arena, area);
            for (var x = c.X - ArenaClear; x <= c.X + ArenaClear; x++)
                for (var y = c.Y - ArenaClear; y <= c.Y + ArenaClear; y++)
                    if (Inside(x, y) && Dist(c, x, y) <= ArenaClear)
                    {
                        _reserved[x, y] = true;
                        if (IsPool(x, y, theme))
                            _tiles[x, y] = Ground(theme.Ground);
                    }

            switch (theme.Key)
            {
                case "cube":
                    // a prism floor with a grid of bright lines, eight monoliths round it
                    Ring(c, ArenaRadius + 2, 8, "R2 Cube Monolith", area);
                    Ring(c, ArenaRadius - 4, 8, "R2 Floating Cube", area, 0.4);
                    Cross(c, ArenaRadius, "R2 Cube Grid Bright");
                    break;
                case "penta":
                    // a pentagram scored into dark soil; the Pentaract's towers stand on its points
                    Disc(c, ArenaRadius, "R2 Dark Soil", area);
                    Circle(c, ArenaRadius - 0.5, "R2 Ritual Floor");
                    for (var i = 0; i < 5; i++)
                    {
                        var a1 = Math.PI * 2 * i / 5 - Math.PI / 2;
                        var a2 = Math.PI * 2 * (i + 2) / 5 - Math.PI / 2;
                        Line(c.X + Math.Cos(a1) * 13, c.Y + Math.Sin(a1) * 13, c.X + Math.Cos(a2) * 13, c.Y + Math.Sin(a2) * 13, "R2 Ritual Floor");
                    }
                    Ring(c, ArenaRadius + 3, 10, "R2 Dead Tree", area);
                    Ring(c, ArenaRadius + 3, 5, "R2 Eye Stalk", area, 0.2);
                    break;
                case "lost":
                    // a flagstone square in ruins: broken pillars round it, graves outside
                    Square(c, ArenaRadius - 2, "R2 Lost Flagstones");
                    Ring(c, ArenaRadius, 12, "R2 Broken Pillar", area);
                    Ring(c, ArenaRadius + 4, 9, "R2 Gravestone", area, 0.3);
                    break;
                case "hermit":
                    // a sandy islet in a lagoon (the Hermit God brings his own island too)
                    Annulus(c, ArenaRadius - 3, ArenaRadius + 2, "R2 Tide Pool");
                    Ring(c, ArenaRadius + 4, 10, "R2 Coral", area);
                    Ring(c, ArenaRadius + 4, 6, "R2 Giant Shell", area, 0.25);
                    break;
                case "ship":
                    // a wreck's deck over the shallows, masts and barrels about it
                    Disc(c, ArenaRadius + 2, "R2 Fog Shallows", area);
                    Rect(c.X - 11, c.Y - 6, c.X + 11, c.Y + 6, "R2 Wreck Planks");
                    Rect(c.X - 6, c.Y - 9, c.X + 6, c.Y + 9, "R2 Wreck Planks");
                    Place(c.X - 8, c.Y, "R2 Broken Mast");
                    Place(c.X + 8, c.Y, "R2 Broken Mast");
                    Ring(c, ArenaRadius + 4, 8, "R2 Barrel", area);
                    Ring(c, ArenaRadius + 4, 4, "R2 Anchor", area, 0.4);
                    break;
                case "sphinx":
                    // a tomb's sandstone court with an obelisk at each corner
                    Square(c, ArenaRadius - 1, "R2 Sandstone Tiles");
                    foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                        Place(c.X + dx * (ArenaRadius - 1), c.Y + dy * (ArenaRadius - 1), "R2 Obelisk");
                    Ring(c, ArenaRadius + 4, 8, "R2 Broken Statue", area, 0.2);
                    break;
                case "skull":
                    // a charred altar ring, skull totems and braziers round it, spikes beyond
                    Circle(c, ArenaRadius - 1, "R2 Bone Dust");
                    Ring(c, ArenaRadius + 1, 8, "R2 Skull Totem", area);
                    Ring(c, ArenaRadius + 1, 8, "R2 Bone Brazier", area, 0.39);
                    Ring(c, ArenaRadius + 5, 12, "R2 Blood Spike", area, 0.1);
                    break;
                case "garden":
                    // a stone court with paths in a cross, lanterns along them, blossom all round
                    Disc(c, ArenaRadius, "R2 Moss Grass", area);
                    Cross(c, ArenaRadius, "R2 Garden Stones", 1);
                    Disc(c, 4, "R2 Garden Stones", area);
                    foreach (var (dx, dy) in new[] { (-3, -7), (3, -7), (-3, 7), (3, 7), (-7, -3), (-7, 3), (7, -3), (7, 3) })
                        Place(c.X + dx, c.Y + dy, "R2 Stone Lantern");
                    Ring(c, ArenaRadius + 2, 14, "R2 Blossom Tree", area);
                    break;
                case "dragon":
                    // a crater of glowing cracks in a ring of lava, crossed by four causeways
                    Annulus(c, ArenaRadius + 1, ArenaRadius + 3, "R2 Lava");
                    Cross(c, ArenaRadius + 4, "R2 Obsidian Ground", 2);
                    Ring(c, ArenaRadius + 6, 8, "R2 Obsidian Spire", area, 0.4);
                    Ring(c, ArenaRadius - 3, 6, "R2 Ember Vent", area);
                    break;
                case "kingdom":
                    // the Avatar's arena is its own map (placed when it comes); fallen glory round it
                    Ring(c, ArenaRadius + 4, 10, "R2 Forgotten Statue", area);
                    Ring(c, ArenaRadius + 4, 10, "R2 Soul Brazier", area, 0.31);
                    break;
                case "bees":
                    // the nest's honeycomb ground, pools of honey and a ring of giant flowers
                    Ring(c, ArenaRadius - 5, 6, "R2 Small Hive", area, 0.5);
                    Annulus(c, ArenaRadius + 1, ArenaRadius + 2, "R2 Honey Pool");
                    Ring(c, ArenaRadius + 4, 14, "R2 Giant Flower", area);
                    Ring(c, ArenaRadius + 4, 7, "R2 Sunflower", area, 0.22);
                    break;
            }
            area.Events.Add((theme.Event, c));
        }

        // the Lucky gods' clearings in the midlands, either side of its heart
        private void LuckyGlades(Realm2Area area)
        {
            var angle = _rand.NextDouble() * Math.PI * 2;
            foreach (var (name, off) in new[] { ("Lucky Ent God", 0.0), ("Lucky Djinn", Math.PI) })
            {
                var at = new IntPoint(area.Center.X + (int)(Math.Cos(angle + off) * area.Radius * 0.45),
                                      area.Center.Y + (int)(Math.Sin(angle + off) * area.Radius * 0.45));
                for (var x = at.X - 7; x <= at.X + 7; x++)
                    for (var y = at.Y - 7; y <= at.Y + 7; y++)
                        if (Inside(x, y) && Dist(at, x, y) <= 7)
                            _reserved[x, y] = true;
                area.Events.Add((name, at));
            }
        }

        // ---- walls: ruined runs of the area's wall, here and there

        private void Walls(Realm2Area area)
        {
            if (area.Theme == null)
                return;
            var runs = 6 + _rand.Next(5);
            for (var i = 0; i < runs; i++)
            {
                var angle = _rand.NextDouble() * Math.PI * 2;
                var dist = ArenaClear + 6 + _rand.NextDouble() * (area.Radius * 0.75 - ArenaClear - 6);
                var x = area.Center.X + Math.Cos(angle) * dist;
                var y = area.Center.Y + Math.Sin(angle) * dist;
                // along the circle round the arena, a few tiles long, with a gap or two
                var length = 3 + _rand.Next(6);
                var dir = angle + Math.PI / 2 + (_rand.NextDouble() - 0.5) * 0.6;
                for (var s = 0; s < length; s++)
                {
                    var px = (int)Math.Round(x + Math.Cos(dir) * s);
                    var py = (int)Math.Round(y + Math.Sin(dir) * s);
                    if (_rand.NextDouble() < 0.15)
                        continue;
                    if (Inside(px, py) && _layout.AreaOf[px, py] == area.Index && !_reserved[px, py] && Walkable(px, py))
                        _tiles[px, py].TileObj = area.Theme.Wall;
                }
            }
        }

        // ---- scenery

        private void Scenery(Realm2Area area)
        {
            for (var x = 0; x < _w; x++)
                for (var y = 0; y < _h; y++)
                {
                    if (_layout.AreaOf[x, y] != area.Index || _reserved[x, y] || _tiles[x, y].TileObj != null || !Walkable(x, y))
                        continue;
                    if (area.Theme == null)
                    {
                        var decor = Realm2Themes.Land(_tiles[x, y].Terrain).Decor;
                        if (decor.Length > 0 && _rand.NextDouble() < 0.06)
                            _tiles[x, y].TileObj = decor[_rand.Next(decor.Length)];
                        continue;
                    }
                    // thicker in groves, thinner in clearings
                    var density = area.Theme.DecorDensity * (0.4 + Noise(x, y, 0.09, area.Index * 5 + 2) * 1.4);
                    if (_rand.NextDouble() < density)
                        _tiles[x, y].TileObj = Pick(area.Theme.Decor);
                }
        }

        private string Pick((string Id, double Weight)[] table)
        {
            var roll = _rand.NextDouble() * table.Sum(t => t.Weight);
            foreach (var (id, weight) in table)
            {
                roll -= weight;
                if (roll <= 0)
                    return id;
            }
            return table[^1].Id;
        }

        // where players arrive: a clearing in the starter lands, on the side away from the midlands
        private void SpawnRegion(Realm2Area start, Realm2Area next)
        {
            var c = start.Center;
            if (next != null)
            {
                var dx = c.X - next.Center.X;
                var dy = c.Y - next.Center.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                c = new IntPoint(c.X + (int)(dx / len * start.Radius * 0.35), c.Y + (int)(dy / len * start.Radius * 0.35));
            }
            for (var x = c.X - 4; x <= c.X + 4; x++)
                for (var y = c.Y - 4; y <= c.Y + 4; y++)
                {
                    if (!Inside(x, y) || Dist(c, x, y) > 4)
                        continue;
                    var t = Ground("Grass");
                    t.Terrain = TerrainType.LowPlains;
                    if (Dist(c, x, y) <= 2)
                        t.Region = TileRegion.Spawn;
                    _tiles[x, y] = t;
                    _layout.AreaOf[x, y] = start.Index;
                    _reserved[x, y] = true;
                }
        }

        // ---- drawing helpers

        private TerrainTile Ground(string name)
        {
            if (!_blank.TryGetValue(name, out var t))
            {
                if (!_data.IdToTileType.TryGetValue(name, out var id))
                    throw new ArgumentException($"Realm 2: no ground '{name}'");
                t = new TerrainTile { TileId = id, Name = "", Terrain = TerrainType.None, Region = TileRegion.None };
                _blank[name] = t;
            }
            return t;
        }

        private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h;

        private bool Walkable(int x, int y)
        {
            var id = _tiles[x, y].TileId;
            return _data.Tiles.TryGetValue(id, out var desc) && !desc.NoWalk && desc.MaxDamage == 0;
        }

        private bool IsPool(int x, int y, Realm2Theme theme) => theme.Pools != null && _tiles[x, y].TileId == Ground(theme.Pools).TileId;

        private static double Dist(IntPoint c, double x, double y) => Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y));

        private void Set(int x, int y, string ground)
        {
            if (!Inside(x, y))
                return;
            var t = Ground(ground);
            t.TileObj = null;
            _tiles[x, y] = t;
        }

        private void Disc(IntPoint c, double r, string ground, Realm2Area area)
        {
            for (var x = (int)(c.X - r); x <= c.X + r; x++)
                for (var y = (int)(c.Y - r); y <= c.Y + r; y++)
                    if (Inside(x, y) && Dist(c, x, y) <= r)
                    {
                        Set(x, y, ground);
                        _layout.AreaOf[x, y] = area.Index;
                    }
        }

        private void Annulus(IntPoint c, double r0, double r1, string ground)
        {
            for (var x = (int)(c.X - r1); x <= c.X + r1; x++)
                for (var y = (int)(c.Y - r1); y <= c.Y + r1; y++)
                    if (Inside(x, y) && Dist(c, x, y) >= r0 && Dist(c, x, y) <= r1)
                        Set(x, y, ground);
        }

        private void Circle(IntPoint c, double r, string ground) => Annulus(c, r - 0.5, r + 0.5, ground);

        private void Square(IntPoint c, int half, string ground) => Rect(c.X - half, c.Y - half, c.X + half, c.Y + half, ground);

        private void Rect(int x0, int y0, int x1, int y1, string ground)
        {
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    Set(x, y, ground);
        }

        private void Cross(IntPoint c, int reach, string ground, int half = 0)
        {
            for (var i = -reach; i <= reach; i++)
                for (var j = -half; j <= half; j++)
                {
                    Set(c.X + i, c.Y + j, ground);
                    Set(c.X + j, c.Y + i, ground);
                }
        }

        private void Line(double x0, double y0, double x1, double y1, string ground)
        {
            var steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2);
            for (var i = 0; i <= steps; i++)
            {
                var f = (double)i / steps;
                Set((int)Math.Round(x0 + (x1 - x0) * f), (int)Math.Round(y0 + (y1 - y0) * f), ground);
            }
        }

        private void Place(int x, int y, string obj)
        {
            if (Inside(x, y) && Walkable(x, y))
                _tiles[x, y].TileObj = obj;
        }

        private void Ring(IntPoint c, double r, int count, string obj, Realm2Area area, double turn = 0)
        {
            for (var i = 0; i < count; i++)
            {
                var a = Math.PI * 2 * (i + turn) / count;
                var x = (int)Math.Round(c.X + Math.Cos(a) * r);
                var y = (int)Math.Round(c.Y + Math.Sin(a) * r);
                if (Inside(x, y) && _layout.AreaOf[x, y] == area.Index)
                    Place(x, y, obj);
            }
        }

        /// <summary>
        /// The tiles in the map format the worlds load (WorldMapExporter's), keyed through a
        /// dictionary: WorldMapExporter looks each tile up in a list, slow for a map this size.
        /// </summary>
        public static byte[] Export(TerrainTile[,] tiles)
        {
            var w = tiles.GetLength(0);
            var h = tiles.GetLength(1);
            var dict = new List<TerrainTile>();
            var index = new Dictionary<(ushort, string, TerrainType, TileRegion), short>();
            var dat = new byte[w * h * 3];
            var i = 0;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var t = tiles[x, y];
                    var key = (t.TileId, t.TileObj ?? "", t.Terrain, t.Region);
                    if (!index.TryGetValue(key, out var n))
                    {
                        n = (short)dict.Count;
                        index[key] = n;
                        dict.Add(t);
                    }
                    dat[i] = (byte)(n & 0xff);
                    dat[i + 1] = (byte)(n >> 8);
                    dat[i + 2] = t.Elevation;
                    i += 3;
                }

            var ms = new System.IO.MemoryStream();
            using (var wtr = new System.IO.BinaryWriter(ms))
            {
                wtr.Write((short)dict.Count);
                foreach (var t in dict)
                {
                    wtr.Write(t.TileId);
                    wtr.Write(t.TileObj ?? "");
                    wtr.Write("");
                    wtr.Write((byte)t.Terrain);
                    wtr.Write((byte)t.Region);
                }
                wtr.Write(w);
                wtr.Write(h);
                wtr.Write(dat);
            }
            var buff = Ionic.Zlib.ZlibStream.CompressBuffer(ms.ToArray());
            var ret = new byte[buff.Length + 1];
            Buffer.BlockCopy(buff, 0, ret, 1, buff.Length);
            ret[0] = 2;
            return ret;
        }

        // smooth value noise in [0, 1], a few octaves
        private double Noise(double x, double y, double freq, int salt)
        {
            double sum = 0, amp = 1, norm = 0;
            for (var o = 0; o < 3; o++)
            {
                sum += amp * Value(x * freq, y * freq, salt + o * 101);
                norm += amp;
                amp *= 0.5;
                freq *= 2;
            }
            return sum / norm;
        }

        private double Value(double x, double y, int salt)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var a = Hash(x0, y0, salt);
            var b = Hash(x0 + 1, y0, salt);
            var c = Hash(x0, y0 + 1, salt);
            var d = Hash(x0 + 1, y0 + 1, salt);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        private double Hash(int x, int y, int salt)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + (salt + _seed) * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
            }
        }
    }
}
