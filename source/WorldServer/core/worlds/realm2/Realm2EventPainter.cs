using System;
using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.structures;

namespace WorldServer.core.worlds.realm2
{
    /// <summary>
    /// Draws an event's land when its event comes (RealmManager.Realm2.cs): a blob of its own
    /// grounds, scenery and ruined walls over the Godlands (or out in the void, joined by a bridge or
    /// a pool), with an arena at its heart dressed for its boss. What it doesn't draw is left unset,
    /// so the world keeps it.
    /// </summary>
    public sealed class Realm2EventPainter
    {
        public const int ArenaRadius = 14;
        private const int ArenaClear = 19;          // no scenery or walls this close to the arena's centre
        private const int Pad = 36;                 // room round the land for a crossing

        private readonly Realm2Canvas _c;
        private readonly Realm2Theme _theme;
        private readonly IntPoint _at;
        private readonly double _radius;
        private readonly bool[,] _own;               // the land's own tiles

        public Realm2Canvas Canvas => _c;
        public List<IntPoint> Tiles { get; } = new List<IntPoint>();
        // the Lost Lands' sentry shrine, where the Lost Sentry stands (RealmManager.WakeSentry)
        public IntPoint? Shrine { get; private set; }

        public Realm2EventPainter(XmlData data, int seed, Realm2Theme theme, IntPoint at, double radius)
        {
            _theme = theme;
            _at = at;
            _radius = radius;
            var half = (int)(radius * 1.25) + Pad;
            _c = new Realm2Canvas(data, seed, at.X - half, at.Y - half, half * 2 + 1, half * 2 + 1, null);
            _own = new bool[_c.W, _c.H];
        }

        private bool Own(int x, int y) => _c.Inside(x, y) && _own[x - _c.X0, y - _c.Y0];

        /// <summary>
        /// Draws the land. `land` says which world tiles are land already; `link` is the land to cross
        /// to when this one is out in the void (bridge or pool), or null.
        /// </summary>
        public void Paint(Func<int, int, bool> land, IntPoint? linkTo, bool pool)
        {
            var edge = Realm2Generator.Edge(_c.Rand, _radius);
            var reach = (int)(_radius * 1.25) + 2;
            for (var x = _at.X - reach; x <= _at.X + reach; x++)
                for (var y = _at.Y - reach; y <= _at.Y + reach; y++)
                {
                    if (!_c.Inside(x, y))
                        continue;
                    var dx = x - _at.X;
                    var dy = y - _at.Y;
                    var d = Math.Sqrt(dx * dx + dy * dy) + (_c.Noise(x, y, 0.25, 9) - 0.5) * 4;
                    if (d >= edge(Math.Atan2(dy, dx)))
                        continue;
                    _own[x - _c.X0, y - _c.Y0] = true;
                    Tiles.Add(new IntPoint(x, y));
                    PaintTile(x, y);
                }

            if (linkTo is IntPoint to)
                _c.Crossing(_at.X, _at.Y, to.X, to.Y, pool, (x, y) => !Own(x, y) && !land(x, y));

            Arena();
            Extras();
            Walls();
            Scenery();
        }

        private void PaintTile(int x, int y)
        {
            var n = _c.Noise(x, y, 0.07, 1);
            var name = n > 0.62 ? _theme.Patches : _theme.Ground;
            if (_theme.Pools != null && _c.Noise(x, y, 0.11, 5) > 0.78 && Realm2Canvas.Dist(_at, x, y) > ArenaClear + 2)
                name = _theme.Pools;
            _c.Set(x, y, name);
        }

        // ---- the arena

        private void Arena()
        {
            var c = _at;
            _c.Disc(c, ArenaRadius, _theme.Arena);
            for (var x = c.X - ArenaClear; x <= c.X + ArenaClear; x++)
                for (var y = c.Y - ArenaClear; y <= c.Y + ArenaClear; y++)
                    if (_c.Inside(x, y) && Realm2Canvas.Dist(c, x, y) <= ArenaClear)
                    {
                        _c.Reserve(x, y);
                        if (_theme.Pools != null && _c.At(x, y).TileId == _c.GroundId(_theme.Pools))
                            _c.Set(x, y, _theme.Ground);
                    }

            switch (_theme.Key)
            {
                case "cube":
                    // a prism floor with a cross of bright lines, eight monoliths round it
                    _c.Ring(c, ArenaRadius + 2, 8, "R2 Cube Monolith");
                    _c.Ring(c, ArenaRadius - 4, 8, "R2 Floating Cube", 0.4);
                    _c.Cross(c, ArenaRadius, "R2 Cube Grid Bright");
                    break;
                case "penta":
                    // a pentagram scored into dark soil; the Pentaract's towers stand on its points
                    _c.Disc(c, ArenaRadius, "R2 Dark Soil");
                    _c.Circle(c, ArenaRadius - 0.5, "R2 Ritual Floor");
                    for (var i = 0; i < 5; i++)
                    {
                        var a1 = Math.PI * 2 * i / 5 - Math.PI / 2;
                        var a2 = Math.PI * 2 * (i + 2) / 5 - Math.PI / 2;
                        _c.Line(c.X + Math.Cos(a1) * 13, c.Y + Math.Sin(a1) * 13, c.X + Math.Cos(a2) * 13, c.Y + Math.Sin(a2) * 13, "R2 Ritual Floor");
                    }
                    _c.Ring(c, ArenaRadius + 3, 10, "R2 Dead Tree");
                    _c.Ring(c, ArenaRadius + 3, 5, "R2 Eye Stalk", 0.2);
                    break;
                case "lost":
                    // a flagstone square in ruins: broken pillars round it, graves outside
                    _c.Square(c, ArenaRadius - 2, "R2 Lost Flagstones");
                    _c.Ring(c, ArenaRadius, 12, "R2 Broken Pillar");
                    _c.Ring(c, ArenaRadius + 4, 9, "R2 Gravestone", 0.3);
                    break;
                case "hermit":
                    // a sandy islet in a lagoon (the Hermit God brings his own island too)
                    _c.Annulus(c, ArenaRadius - 3, ArenaRadius + 2, "R2 Tide Pool");
                    _c.Ring(c, ArenaRadius + 4, 10, "R2 Coral");
                    _c.Ring(c, ArenaRadius + 4, 6, "R2 Giant Shell", 0.25);
                    break;
                case "ship":
                    // a wreck's deck over the shallows, masts and barrels about it
                    _c.Disc(c, ArenaRadius + 2, "R2 Fog Shallows");
                    _c.Rect(c.X - 11, c.Y - 6, c.X + 11, c.Y + 6, "R2 Wreck Planks");
                    _c.Rect(c.X - 6, c.Y - 9, c.X + 6, c.Y + 9, "R2 Wreck Planks");
                    _c.Place(c.X - 8, c.Y, "R2 Broken Mast");
                    _c.Place(c.X + 8, c.Y, "R2 Broken Mast");
                    _c.Ring(c, ArenaRadius + 4, 8, "R2 Barrel");
                    _c.Ring(c, ArenaRadius + 4, 4, "R2 Anchor", 0.4);
                    break;
                case "sphinx":
                    // a tomb's sandstone court with an obelisk at each corner
                    _c.Square(c, ArenaRadius - 1, "R2 Sandstone Tiles");
                    foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                        _c.Place(c.X + dx * (ArenaRadius - 1), c.Y + dy * (ArenaRadius - 1), "R2 Obelisk");
                    _c.Ring(c, ArenaRadius + 4, 8, "R2 Broken Statue", 0.2);
                    break;
                case "skull":
                    // a charred altar ring, skull totems and braziers round it, spikes beyond
                    _c.Circle(c, ArenaRadius - 1, "R2 Bone Dust");
                    _c.Ring(c, ArenaRadius + 1, 8, "R2 Skull Totem");
                    _c.Ring(c, ArenaRadius + 1, 8, "R2 Bone Brazier", 0.39);
                    _c.Ring(c, ArenaRadius + 5, 12, "R2 Blood Spike", 0.1);
                    break;
                case "garden":
                    // a stone court with paths in a cross, lanterns along them, blossom all round
                    _c.Disc(c, ArenaRadius, "R2 Moss Grass");
                    _c.Cross(c, ArenaRadius, "R2 Garden Stones", 1);
                    _c.Disc(c, 4, "R2 Garden Stones");
                    foreach (var (dx, dy) in new[] { (-3, -7), (3, -7), (-3, 7), (3, 7), (-7, -3), (-7, 3), (7, -3), (7, 3) })
                        _c.Place(c.X + dx, c.Y + dy, "R2 Stone Lantern");
                    _c.Ring(c, ArenaRadius + 2, 14, "R2 Blossom Tree");
                    break;
                case "dragon":
                    // the hoard in a ring of dragonfire, crossed by four causeways; crags and nests
                    // of eggs round it
                    _c.Annulus(c, ArenaRadius + 1, ArenaRadius + 3, "R2 Lava");
                    _c.Cross(c, ArenaRadius + 4, "R2 Obsidian Ground", 2);
                    _c.Ring(c, ArenaRadius + 6, 8, "R2 Obsidian Spire", 0.4);
                    _c.Ring(c, ArenaRadius + 8, 6, "R2 Dragon Nest", 0.2);
                    _c.Ring(c, ArenaRadius - 3, 6, "R2 Gold Hoard");
                    break;
                case "kingdom":
                    // the Avatar's arena is its own map (placed with it); fallen glory round it
                    _c.Ring(c, ArenaRadius + 4, 10, "R2 Forgotten Statue");
                    _c.Ring(c, ArenaRadius + 4, 10, "Magic Blue Flame", 0.31);
                    break;
                case "bees":
                    // the nest's honeycomb ground, pools of honey and a ring of giant flowers
                    _c.Ring(c, ArenaRadius - 5, 6, "R2 Small Hive", 0.5);
                    _c.Annulus(c, ArenaRadius + 1, ArenaRadius + 2, "R2 Honey Pool");
                    _c.Ring(c, ArenaRadius + 4, 14, "R2 Giant Flower");
                    _c.Ring(c, ArenaRadius + 4, 7, "R2 Sunflower", 0.22);
                    break;
            }
        }

        // ---- what some lands have beyond their arena

        private void Extras()
        {
            switch (_theme.Key)
            {
                case "lost":
                    SentryShrine();
                    break;
                case "sphinx":
                    // patches of sinking sand, as in the Tomb, slowing and sinking whoever wades in
                    for (var i = 0; i < 7 + _c.Rand.Next(4); i++)
                        if (OwnSpot(ArenaClear + 3, _radius * 0.85) is IntPoint p)
                            _c.Disc(p, 1.6 + _c.Rand.NextDouble() * 1.8, "R2 Sinking Sand",
                                (x, y) => Own(x, y) && !_c.IsReserved(x, y) && Realm2Canvas.Dist(_at, x, y) > ArenaClear + 1);
                    break;
                case "garden":
                    GardenRuins();
                    break;
            }
        }

        // a free spot of the land's own, between these distances from its heart
        private IntPoint? OwnSpot(double from, double to)
        {
            for (var tries = 0; tries < 60; tries++)
            {
                var a = _c.Rand.NextDouble() * Math.PI * 2;
                var d = from + _c.Rand.NextDouble() * Math.Max(1, to - from);
                var p = new IntPoint((int)Math.Round(_at.X + Math.Cos(a) * d), (int)Math.Round(_at.Y + Math.Sin(a) * d));
                if (Own(p.X, p.Y) && !_c.IsReserved(p.X, p.Y))
                    return p;
            }
            return null;
        }

        // A flagstone platform out in the Lost Lands, its broken pillars and blue crystals round the
        // Lost Sentry that guards it, apart from the Lord's arena.
        private void SentryShrine()
        {
            for (var tries = 0; tries < 40; tries++)
            {
                if (OwnSpot(_radius * 0.5, _radius * 0.7) is not IntPoint p)
                    return;
                if (!Own(p.X - 6, p.Y) || !Own(p.X + 6, p.Y) || !Own(p.X, p.Y - 6) || !Own(p.X, p.Y + 6))
                    continue;
                _c.Square(p, 4, "R2 Lost Flagstones");
                _c.Cross(p, 6, "R2 Lost Flagstones", 1);
                for (var x = p.X - 7; x <= p.X + 7; x++)
                    for (var y = p.Y - 7; y <= p.Y + 7; y++)
                        if (_c.Inside(x, y) && Own(x, y))
                            _c.Reserve(x, y);
                foreach (var (dx, dy) in new[] { (-4, -4), (4, -4), (-4, 4), (4, 4) })
                    _c.Place(p.X + dx, p.Y + dy, "R2 Broken Pillar");
                foreach (var (dx, dy) in new[] { (-2, -6), (2, -6), (-2, 6), (2, 6), (-6, -2), (-6, 2), (6, -2), (6, 2) })
                    _c.Place(p.X + dx, p.Y + dy, "R2 Lost Crystal");
                Shrine = p;
                return;
            }
        }

        // The Jade Garden's forest keeps ruins: stone paths wandering out from the court, and here and
        // there the broken walls of an old shrine with mossy pillars, a toppled lantern and moss inside.
        private void GardenRuins()
        {
            var paths = 3 + _c.Rand.Next(2);
            for (var i = 0; i < paths; i++)
            {
                var a = Math.PI * 2 * (i + _c.Rand.NextDouble() * 0.6) / paths;
                double x = _at.X + Math.Cos(a) * (ArenaRadius - 1), y = _at.Y + Math.Sin(a) * (ArenaRadius - 1);
                var length = _radius * (0.55 + _c.Rand.NextDouble() * 0.35);
                for (var s = 0; s < length; s++)
                {
                    a += (_c.Rand.NextDouble() - 0.5) * 0.35;
                    x += Math.Cos(a);
                    y += Math.Sin(a);
                    for (var w = -1; w <= 0; w++)
                    {
                        int px = (int)Math.Round(x - Math.Sin(a) * w), py = (int)Math.Round(y + Math.Cos(a) * w);
                        if (Own(px, py) && _c.Walkable(px, py))
                        {
                            _c.Set(px, py, _c.Rand.NextDouble() < 0.8 ? "R2 Garden Stones" : "R2 Moss Grass");
                            _c.Reserve(px, py);
                        }
                    }
                }
            }

            var ruins = 3 + _c.Rand.Next(3);
            for (var i = 0; i < ruins; i++)
            {
                if (OwnSpot(ArenaClear + 6, _radius * 0.85) is not IntPoint p)
                    continue;
                int hw = 2 + _c.Rand.Next(3), hh = 2 + _c.Rand.Next(2);
                if (!Own(p.X - hw - 1, p.Y - hh - 1) || !Own(p.X + hw + 1, p.Y + hh + 1))
                    continue;
                for (var x = p.X - hw; x <= p.X + hw; x++)
                    for (var y = p.Y - hh; y <= p.Y + hh; y++)
                    {
                        _c.Set(x, y, _c.Rand.NextDouble() < 0.7 ? "R2 Garden Stones" : "R2 Moss Grass");
                        _c.Reserve(x, y);
                        var edge = x == p.X - hw || x == p.X + hw || y == p.Y - hh || y == p.Y + hh;
                        var corner = (x == p.X - hw || x == p.X + hw) && (y == p.Y - hh || y == p.Y + hh);
                        if (corner)
                            _c.Place(x, y, "R2 Mossy Pillar");
                        else if (edge && _c.Rand.NextDouble() < 0.55)
                            _c.Place(x, y, "R2 Garden Wall");
                    }
                _c.Place(p.X, p.Y, _c.Rand.NextDouble() < 0.5 ? "R2 Toppled Lantern" : "R2 Mossy Pillar");
            }
        }

        // ---- walls: ruined runs of the land's wall, here and there

        private void Walls()
        {
            var runs = 6 + _c.Rand.Next(5);
            for (var i = 0; i < runs; i++)
            {
                var angle = _c.Rand.NextDouble() * Math.PI * 2;
                var dist = ArenaClear + 6 + _c.Rand.NextDouble() * (_radius * 0.75 - ArenaClear - 6);
                var x = _at.X + Math.Cos(angle) * dist;
                var y = _at.Y + Math.Sin(angle) * dist;
                var length = 3 + _c.Rand.Next(6);
                var dir = angle + Math.PI / 2 + (_c.Rand.NextDouble() - 0.5) * 0.6;
                for (var s = 0; s < length; s++)
                {
                    var px = (int)Math.Round(x + Math.Cos(dir) * s);
                    var py = (int)Math.Round(y + Math.Sin(dir) * s);
                    if (_c.Rand.NextDouble() < 0.15)
                        continue;
                    if (Own(px, py) && !_c.IsReserved(px, py) && _c.Walkable(px, py))
                        _c.At(px, py).TileObj = _theme.Wall;
                }
            }
        }

        // ---- scenery, thicker in groves and thinner in clearings

        private void Scenery()
        {
            foreach (var p in Tiles)
            {
                if (_c.IsReserved(p.X, p.Y) || _c.At(p.X, p.Y).TileObj != null || !_c.Walkable(p.X, p.Y))
                    continue;
                var density = _theme.DecorDensity * (0.4 + _c.Noise(p.X, p.Y, 0.09, 2) * 1.4);
                if (_c.Rand.NextDouble() < density)
                    _c.At(p.X, p.Y).TileObj = _c.Pick(_theme.Decor);
            }
        }
    }
}
