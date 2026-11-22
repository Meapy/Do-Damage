using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl.crucible;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // The Crucible's floors that move (BehaviorDb.Crucible.cs): each floor's invisible Heart, at its
    // centre, runs these as the champions order it, so the floor changes round the centre and comes
    // back when the state ends. Every one is a CrucibleFloor: each step it works out which squares
    // round the host's spawn point should show which ground now, paints those that changed and
    // puts back those it no longer wants, remembering what each square was. Squares that aren't
    // walkable floor (the sky, a pillar's) are left alone, and a square another behaviour has
    // changed meanwhile is left as that one has it.
    internal abstract class CrucibleFloor : Behavior
    {
        private readonly int _stepMs;

        protected CrucibleFloor(int stepMs) => _stepMs = stepMs;

        protected internal sealed class Run
        {
            public int Elapsed;
            public int Next;
            public readonly Dictionary<(int X, int Y), (ushort Was, ushort Now)> Painted = new();
            public readonly Dictionary<(int X, int Y), ushort> Want = new();
            public Random Rng;
            public object Data;
        }

        protected static ushort Tile(Entity host, string id) =>
            id != null && host.GameServer.Resources.GameData.IdToTileType.TryGetValue(id, out var t) ? t : (ushort)0;

        // the squares round the centre within `outer`, with their distance and angle
        protected static List<(int Dx, int Dy, double R, double A)> Disc(double inner, double outer)
        {
            var list = new List<(int, int, double, double)>();
            var n = (int)Math.Ceiling(outer);
            for (var dy = -n; dy <= n; dy++)
                for (var dx = -n; dx <= n; dx++)
                {
                    var r = Math.Sqrt(dx * dx + dy * dy);
                    if (r >= inner && r <= outer)
                        list.Add((dx, dy, r, Math.Atan2(dy, dx)));
                }
            return list;
        }

        protected internal virtual void Begin(Entity host, Run r) { }

        // fills r.Want: square offset from the centre -> the ground it shows now
        protected internal abstract void Shape(Entity host, Run r);

        // after each step: anything else the behaviour does (Crumble rescues whoever stands on sky)
        protected internal virtual void After(Entity host, Run r, TickTime time) { }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var r = new Run { Rng = new Random(Random.Next()) };
            state = r;
            Begin(host, r);
            Apply(host, r, time);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Run r)
                return;
            r.Elapsed += time.ElapsedMsDelta;
            if (r.Elapsed < r.Next)
                return;
            r.Next = r.Elapsed + _stepMs;
            Apply(host, r, time);
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is Run r)
                Restore(host, r);
            state = null;
        }

        private void Apply(Entity host, Run r, TickTime time)
        {
            var map = host.World?.Map;
            if (map == null)
                return;
            r.Want.Clear();
            Shape(host, r);
            var c = host.SpawnPoint.Value;
            var (cx, cy) = ((int)c.X, (int)c.Y);
            var tiles = host.GameServer.Resources.GameData.Tiles;
            var changed = false;
            foreach (var (key, (was, now)) in r.Painted.ToList())
            {
                if (r.Want.TryGetValue(key, out var want) && want == now)
                    continue;
                var t = map[cx + key.X, cy + key.Y];
                if (!r.Want.ContainsKey(key))
                {
                    if (t.TileId == now)
                    {
                        t.TileId = was;
                        t.UpdateCount++;
                        changed = true;
                    }
                    r.Painted.Remove(key);
                }
            }
            foreach (var (key, want) in r.Want)
            {
                if (want == 0)
                    continue;
                var (x, y) = (cx + key.X, cy + key.Y);
                if (!map.Contains(x, y))
                    continue;
                var t = map[x, y];
                if (r.Painted.TryGetValue(key, out var p))
                {
                    if (p.Now == want)
                        continue;
                    if (t.TileId != p.Now)
                    {
                        r.Painted.Remove(key); // something else has it now
                        continue;
                    }
                    t.TileId = want;
                    t.UpdateCount++;
                    r.Painted[key] = (p.Was, want);
                    changed = true;
                    continue;
                }
                if (t.TileId == want || !tiles.TryGetValue(t.TileId, out var desc) || desc.NoWalk)
                    continue;
                if (t.ObjType != 0 && t.ObjDesc != null && (t.ObjDesc.FullOccupy || t.ObjDesc.OccupySquare))
                    continue;
                r.Painted[key] = (t.TileId, want);
                t.TileId = want;
                t.UpdateCount++;
                changed = true;
            }
            if (changed)
                Floor.Refresh(host);
            After(host, r, time);
        }

        private static void Restore(Entity host, Run r)
        {
            var map = host.World?.Map;
            if (map == null)
                return;
            var c = host.SpawnPoint.Value;
            foreach (var ((dx, dy), (was, now)) in r.Painted)
            {
                var t = map[(int)c.X + dx, (int)c.Y + dy];
                if (t.TileId != now)
                    continue;
                t.TileId = was;
                t.UpdateCount++;
            }
            r.Painted.Clear();
            Floor.Refresh(host);
        }
    }

    // Arms of `tile` turning round the centre, `width` squares across, from `inner` to `outer`
    // squares out, `degPerSec` (negative: the other way), bent `curve` degrees a square further
    // out (a spiral: Nerissa's Whirlpool). For the first `warnMs` they show `warnTile` and stand
    // still, so players see where they'll be. Ferrogrim's Grinding Blades, Chronarch's Hour Hands.
    internal class SweepingArms : CrucibleFloor
    {
        private readonly string _tile, _warnTile;
        private readonly int _arms;
        private readonly double _inner, _outer, _width, _degPerSec, _curve;
        private readonly int _warnMs;

        public SweepingArms(string tile, int arms, double inner, double outer, double width, double degPerSec,
            int warnMs = 1500, string warnTile = "Crucible Warning", double curve = 0, int stepMs = 150) : base(stepMs)
        {
            _tile = tile;
            _warnTile = warnTile;
            _arms = arms;
            _inner = inner;
            _outer = outer;
            _width = width;
            _degPerSec = degPerSec;
            _warnMs = warnMs;
            _curve = curve;
        }

        protected internal override void Begin(Entity host, Run r) => r.Data = (Disc(_inner, _outer), r.Rng.NextDouble() * 360);

        protected internal override void Shape(Entity host, Run r)
        {
            var (disc, start) = ((List<(int Dx, int Dy, double R, double A)>, double))r.Data;
            var warn = r.Elapsed < _warnMs;
            var tile = Tile(host, warn ? _warnTile : _tile);
            var turned = warn ? 0 : _degPerSec * (r.Elapsed - _warnMs) / 1000.0;
            var step = 2 * Math.PI / _arms;
            foreach (var (dx, dy, rr, a) in disc)
            {
                var arm = (start + turned + _curve * rr) * Math.PI / 180;
                var off = a - arm;
                off -= step * Math.Round(off / step);
                if (Math.Abs(off) * Math.Max(rr, 0.7) <= _width / 2)
                    r.Want[(dx, dy)] = tile;
            }
        }
    }

    // Rings of `tile` spreading out from the centre at `speed` squares a second, `thickness` thick,
    // one every `everyMs` (once, for 0), with `gaps` openings `gapDeg` wide in each (at random): a
    // shockwave to step through at its gaps. Ferrogrim's Hammerfall, the Sovereign's echoes.
    internal class FloorWave : CrucibleFloor
    {
        private readonly string _tile;
        private readonly double _reach, _speed, _thickness, _gapDeg;
        private readonly int _gaps, _everyMs;

        public FloorWave(string tile, double reach, double speed, int gaps, double gapDeg, int everyMs = 0, double thickness = 1.3)
            : base(100)
        {
            _tile = tile;
            _reach = reach;
            _speed = speed;
            _gaps = gaps;
            _gapDeg = gapDeg;
            _everyMs = everyMs;
            _thickness = thickness;
        }

        protected internal override void Begin(Entity host, Run r) => r.Data = (Disc(0.5, _reach), new List<double[]>());

        protected internal override void Shape(Entity host, Run r)
        {
            var (disc, gaps) = ((List<(int Dx, int Dy, double R, double A)>, List<double[]>))r.Data;
            var tile = Tile(host, _tile);
            var life = (int)(_reach / _speed * 1000) + 200;
            var rings = _everyMs <= 0 ? 1 : r.Elapsed / _everyMs + 1;
            while (gaps.Count < rings)
                gaps.Add(Enumerable.Range(0, _gaps).Select(_ => r.Rng.NextDouble() * 2 * Math.PI).ToArray());
            for (var k = 0; k < rings; k++)
            {
                var age = r.Elapsed - k * Math.Max(1, _everyMs);
                if (age < 0 || age > life)
                    continue;
                var radius = _speed * age / 1000.0;
                foreach (var (dx, dy, rr, a) in disc)
                {
                    if (Math.Abs(rr - radius) > _thickness / 2)
                        continue;
                    var open = gaps[k].Any(g =>
                    {
                        var off = a - g;
                        off -= 2 * Math.PI * Math.Round(off / (2 * Math.PI));
                        return Math.Abs(off) * 180 / Math.PI <= _gapDeg / 2;
                    });
                    if (!open)
                        r.Want[(dx, dy)] = tile;
                }
            }
        }
    }

    // A band of `tile` `band` squares deep sweeping across the floor (within `reach` of the centre)
    // at `speed` squares a second, with `gaps` lanes `gapWidth` squares wide left dry. Each sweep
    // first shows at its starting edge as `warnTile` for `warnMs`, holes and all, then rolls across;
    // a new one every `everyMs`, from another side. Nerissa's Tidal Lanes.
    internal class TideSweep : CrucibleFloor
    {
        private readonly string _tile, _warnTile;
        private readonly double _reach, _band, _speed, _gapWidth;
        private readonly int _gaps, _warnMs, _everyMs;

        public TideSweep(string tile, double reach, double band, double speed, int gaps, double gapWidth, int warnMs = 1400,
            int everyMs = 5500, string warnTile = "Crucible Warning") : base(120)
        {
            _tile = tile;
            _warnTile = warnTile;
            _reach = reach;
            _band = band;
            _speed = speed;
            _gaps = gaps;
            _gapWidth = gapWidth;
            _warnMs = warnMs;
            _everyMs = everyMs;
        }

        private sealed class Sweep
        {
            public double Ux, Uy;
            public double[] Gaps;
        }

        protected internal override void Begin(Entity host, Run r) => r.Data = (Disc(0, _reach), new List<Sweep>());

        protected internal override void Shape(Entity host, Run r)
        {
            var (disc, sweeps) = ((List<(int Dx, int Dy, double R, double A)>, List<Sweep>))r.Data;
            var n = r.Elapsed / _everyMs + 1;
            while (sweeps.Count < n)
            {
                // from a side it didn't come from last time
                var dir = r.Rng.Next(8) * Math.PI / 4;
                if (sweeps.Count > 0)
                {
                    var last = Math.Atan2(sweeps[^1].Uy, sweeps[^1].Ux);
                    while (Math.Abs(Math.Cos(dir - last)) > 0.8)
                        dir = r.Rng.Next(8) * Math.PI / 4;
                }
                sweeps.Add(new Sweep
                {
                    Ux = Math.Cos(dir),
                    Uy = Math.Sin(dir),
                    Gaps = Enumerable.Range(0, _gaps).Select(k => (-_reach + 2 * _reach * (k + 0.5) / _gaps) + (r.Rng.NextDouble() - 0.5) * _reach / _gaps).ToArray(),
                });
            }
            var water = Tile(host, _tile);
            var warn = Tile(host, _warnTile);
            var cross = (2 * _reach + _band) / _speed * 1000;
            for (var k = 0; k < sweeps.Count; k++)
            {
                var age = r.Elapsed - k * _everyMs;
                if (age < 0 || age > _warnMs + cross)
                    continue;
                var s = sweeps[k];
                var front = age < _warnMs ? -_reach + _band : -_reach + _band + _speed * (age - _warnMs) / 1000.0;
                var tile = age < _warnMs ? warn : water;
                foreach (var (dx, dy, _, _) in disc)
                {
                    var along = dx * s.Ux + dy * s.Uy;
                    if (along > front || along < front - _band)
                        continue;
                    var lateral = -dx * s.Uy + dy * s.Ux;
                    if (s.Gaps.Any(g => Math.Abs(lateral - g) <= _gapWidth / 2))
                        continue;
                    r.Want[(dx, dy)] = tile;
                }
            }
        }
    }

    // Veins of `tile` cracking out across the floor from the centre: `veins` at a time, every
    // `everyMs`, each a wandering line `length` squares long aimed at a player, growing a square
    // every `growMs`; each square glows (`warnTile`) for `warnMs`, burns for `burnMs`, then cools to
    // `coolTile` for `coolMs` and is floor again. Pyrrhax's Lava Veins.
    internal class LavaVeins : CrucibleFloor
    {
        private readonly string _tile, _coolTile, _warnTile;
        private readonly int _veins, _length, _growMs, _warnMs, _burnMs, _coolMs, _everyMs;
        private readonly double _reach;

        public LavaVeins(string tile, string coolTile, int veins, int length, double reach, int everyMs = 4000, int growMs = 70,
            int warnMs = 600, int burnMs = 2600, int coolMs = 1500, string warnTile = "Crucible Warning") : base(100)
        {
            _tile = tile;
            _coolTile = coolTile;
            _warnTile = warnTile;
            _veins = veins;
            _length = length;
            _reach = reach;
            _everyMs = everyMs;
            _growMs = growMs;
            _warnMs = warnMs;
            _burnMs = burnMs;
            _coolMs = coolMs;
        }

        protected internal override void Begin(Entity host, Run r) => r.Data = new List<(int Born, List<(int X, int Y)> Path)>();

        private List<(int X, int Y)> Walk(Entity host, Run r)
        {
            var c = host.SpawnPoint.Value;
            var players = host.World.Players.Values.Where(p => p.DistTo(c.X, c.Y) <= _reach + 2).ToList();
            var dir = players.Count > 0
                ? Math.Atan2(players[r.Rng.Next(players.Count)].Y - c.Y, players[r.Rng.Next(players.Count)].X - c.X)
                : r.Rng.NextDouble() * 2 * Math.PI;
            dir += (r.Rng.NextDouble() - 0.5) * 0.9;
            var path = new List<(int, int)>();
            double x = 0, y = 0;
            for (var i = 0; i < _length * 3 && path.Count < _length; i++)
            {
                dir += (r.Rng.NextDouble() - 0.5) * 0.8;
                x += Math.Cos(dir) * 0.7;
                y += Math.Sin(dir) * 0.7;
                if (x * x + y * y > _reach * _reach)
                    break;
                var sq = ((int)Math.Round(x), (int)Math.Round(y));
                if (sq != (0, 0) && !path.Contains(sq))
                    path.Add(sq);
                // now and then it forks: a short branch
                if (r.Rng.NextDouble() < 0.06)
                {
                    var bdir = dir + (r.Rng.Next(2) == 0 ? 1 : -1) * 0.9;
                    double bx = x, by = y;
                    for (var j = 0; j < 4; j++)
                    {
                        bx += Math.Cos(bdir) * 0.8;
                        by += Math.Sin(bdir) * 0.8;
                        var b = ((int)Math.Round(bx), (int)Math.Round(by));
                        if (bx * bx + by * by <= _reach * _reach && !path.Contains(b))
                            path.Add(b);
                    }
                }
            }
            return path;
        }

        protected internal override void Shape(Entity host, Run r)
        {
            var veins = (List<(int Born, List<(int X, int Y)> Path)>)r.Data;
            var n = r.Elapsed / _everyMs + 1;
            while (veins.Count / Math.Max(1, _veins) < n)
                for (var v = 0; v < _veins; v++)
                    veins.Add(((veins.Count / Math.Max(1, _veins)) * _everyMs, Walk(host, r)));
            var lava = Tile(host, _tile);
            var cool = Tile(host, _coolTile);
            var warn = Tile(host, _warnTile);
            foreach (var (born, path) in veins)
                for (var i = 0; i < path.Count; i++)
                {
                    var age = r.Elapsed - born - i * _growMs;
                    if (age < 0 || age > _warnMs + _burnMs + _coolMs)
                        continue;
                    var t = age < _warnMs ? warn : age < _warnMs + _burnMs ? lava : cool;
                    // lava wins over a cooling or glowing square of another vein
                    if (r.Want.TryGetValue(path[i], out var had) && had == lava)
                        continue;
                    r.Want[path[i]] = t;
                }
            veins.RemoveAll(v => r.Elapsed - v.Born - v.Path.Count * _growMs > _warnMs + _burnMs + _coolMs);
        }
    }

    // The floor between `inner` and `outer` cut into `sectors` slices like a clock's hours, lit by
    // turns: each pattern ("101010101010": which hours burn, from twelve o'clock round) glows as a
    // warning for `warnMs`, burns for `litMs`, then rests `gapMs` before the next. Patterns repeat
    // in order; an empty list picks half the hours at random each time. Chronarch's Midnight Chime.
    internal class SectorChime : CrucibleFloor
    {
        private readonly string _tile, _warnTile;
        private readonly int _sectors, _warnMs, _litMs, _gapMs;
        private readonly double _inner, _outer;
        private readonly string[] _patterns;

        public SectorChime(string tile, int sectors, double inner, double outer, string[] patterns, int warnMs = 1100, int litMs = 1300,
            int gapMs = 300, string warnTile = "Crucible Warning") : base(100)
        {
            _tile = tile;
            _warnTile = warnTile;
            _sectors = sectors;
            _inner = inner;
            _outer = outer;
            _patterns = patterns ?? Array.Empty<string>();
            _warnMs = warnMs;
            _litMs = litMs;
            _gapMs = gapMs;
        }

        protected internal override void Begin(Entity host, Run r) => r.Data = (Disc(_inner, _outer), new Dictionary<int, bool[]>());

        protected internal override void Shape(Entity host, Run r)
        {
            var (disc, picked) = ((List<(int Dx, int Dy, double R, double A)>, Dictionary<int, bool[]>))r.Data;
            var cycle = _warnMs + _litMs + _gapMs;
            var k = r.Elapsed / cycle;
            var at = r.Elapsed % cycle;
            if (at >= _warnMs + _litMs)
                return;
            if (!picked.TryGetValue(k, out var lit))
            {
                lit = new bool[_sectors];
                if (_patterns.Length > 0)
                {
                    var p = _patterns[k % _patterns.Length];
                    for (var i = 0; i < _sectors; i++)
                        lit[i] = i < p.Length && p[i] == '1';
                }
                else
                    foreach (var i in Enumerable.Range(0, _sectors).OrderBy(_ => r.Rng.Next()).Take(_sectors / 2))
                        lit[i] = true;
                picked[k] = lit;
                picked.Remove(k - 2);
            }
            var tile = Tile(host, at < _warnMs ? _warnTile : _tile);
            foreach (var (dx, dy, _, a) in disc)
            {
                // twelve o'clock is straight up (north, -y), going clockwise
                var hours = (a + Math.PI / 2) / (2 * Math.PI) * _sectors + 0.5;
                var s = (((int)Math.Floor(hours)) % _sectors + _sectors) % _sectors;
                if (lit[s])
                    r.Want[(dx, dy)] = tile;
            }
        }
    }

    // The floor's edge breaking away into the sky: every square further than `keep` from the centre
    // (to `reach`) cracks (`crackTile`) and then falls (to the sky, `Empty`), the outermost first,
    // over `fallMs`, each cracked `warnMs` before it goes. Whoever is still on a square that fell is
    // pulled back onto the floor that's left, taking `damage`. It stays broken until the state ends.
    // Pyrrhax's Crucible Pours, the Sovereign's Crucible Breaks.
    internal class Crumble : CrucibleFloor
    {
        private readonly double _keep, _reach;
        private readonly int _fallMs, _warnMs, _damage;
        private readonly string _crackTile;

        public Crumble(double keep, double reach, int fallMs = 4000, int warnMs = 1500, int damage = 300, string crackTile = "Crumbling Floor")
            : base(100)
        {
            _keep = keep;
            _reach = reach;
            _fallMs = fallMs;
            _warnMs = warnMs;
            _damage = damage;
            _crackTile = crackTile;
        }

        protected internal override void Begin(Entity host, Run r)
        {
            // each square's moment to fall: from the edge in, a little out of step, so it breaks
            // away in chunks rather than in perfect rings
            var at = new Dictionary<(int, int), int>();
            foreach (var (dx, dy, rr, _) in Disc(_keep, _reach + 1))
            {
                var k = (_reach + 1 - rr) / Math.Max(0.5, _reach + 1 - _keep);
                at[(dx, dy)] = _warnMs + (int)(k * _fallMs) + r.Rng.Next(450);
            }
            r.Data = at;
        }

        protected internal override void Shape(Entity host, Run r)
        {
            var crack = Tile(host, _crackTile);
            var sky = Tile(host, "Empty");
            foreach (var (sq, at) in (Dictionary<(int, int), int>)r.Data)
            {
                if (r.Elapsed >= at)
                    r.Want[sq] = sky;
                else if (r.Elapsed >= at - _warnMs)
                    r.Want[sq] = crack;
            }
        }

        protected internal override void After(Entity host, Run r, TickTime time)
        {
            var map = host.World.Map;
            var c = host.SpawnPoint.Value;
            var sky = Tile(host, "Empty");
            foreach (var p in host.World.Players.Values.ToList())
            {
                if (p.DistTo(c.X, c.Y) > _reach + 3 || !map.Contains((int)p.X, (int)p.Y) || map[(int)p.X, (int)p.Y].TileId != sky)
                    continue;
                // pulled in towards the centre, onto the floor that's left
                var d = Math.Max(0.01, p.DistTo(c.X, c.Y));
                var k = (float)(Math.Max(1, _keep - 1.5) / d);
                var to = new Position(c.X + (p.X - c.X) * k, c.Y + (p.Y - c.Y) * k);
                p.TeleportPosition(time, to, true);
                Strike.Land(host.World, host, to, 0.6, _damage, 0xFFFFB03A);
            }
        }
    }

    // The whole floor turning into another's: `floorTile` out to `radius`, `rimTile` at its edge,
    // spreading from the centre outwards, a ring every `msPerSquare`. The Sovereign calls up each
    // fallen champion's floor on the Apex; it goes back when the state ends.
    internal class Transmute : CrucibleFloor
    {
        private readonly string _floorTile, _rimTile;
        private readonly double _radius;
        private readonly int _msPerSquare;

        public Transmute(string floorTile, string rimTile, double radius, int msPerSquare = 90) : base(100)
        {
            _floorTile = floorTile;
            _rimTile = rimTile;
            _radius = radius;
            _msPerSquare = msPerSquare;
        }

        protected internal override void Begin(Entity host, Run r) => r.Data = Disc(0, _radius + 0.3);

        protected internal override void Shape(Entity host, Run r)
        {
            var floor = Tile(host, _floorTile);
            var rim = Tile(host, _rimTile);
            var reached = r.Elapsed / (double)_msPerSquare;
            foreach (var (dx, dy, rr, _) in (List<(int Dx, int Dy, double R, double A)>)r.Data)
                if (rr <= reached)
                    r.Want[(dx, dy)] = rr > _radius - 1 ? rim : floor;
        }
    }

    // Several moving floors at once, as one: each step every layer says what it wants and the later
    // layers win where they overlap, and the floor is painted once, so two never fight over a
    // square (and never leave one of the other's behind).
    internal class FloorLayers : CrucibleFloor
    {
        private readonly CrucibleFloor[] _layers;

        public FloorLayers(params CrucibleFloor[] layers) : base(100) => _layers = layers;

        protected internal override void Begin(Entity host, Run r)
        {
            var subs = new Run[_layers.Length];
            for (var i = 0; i < _layers.Length; i++)
            {
                subs[i] = new Run { Rng = new Random(r.Rng.Next()) };
                _layers[i].Begin(host, subs[i]);
            }
            r.Data = subs;
        }

        protected internal override void Shape(Entity host, Run r)
        {
            var subs = (Run[])r.Data;
            for (var i = 0; i < _layers.Length; i++)
            {
                subs[i].Elapsed = r.Elapsed;
                subs[i].Want.Clear();
                _layers[i].Shape(host, subs[i]);
                foreach (var (sq, tile) in subs[i].Want)
                    r.Want[sq] = tile;
            }
        }

        protected internal override void After(Entity host, Run r, TickTime time)
        {
            var subs = (Run[])r.Data;
            for (var i = 0; i < _layers.Length; i++)
                _layers[i].After(host, subs[i], time);
        }
    }

    // The tide coming in: every square within `reach` but those `dry(dx, dy)` keeps dry turns to
    // `tile`, from the edge inwards over `riseMs`, each glowing `warnMs` first. Nerissa's Rising Tide
    // leaves only the coral isles above water.
    internal class Flood : CrucibleFloor
    {
        private readonly string _tile, _warnTile;
        private readonly double _reach;
        private readonly Func<int, int, bool> _dry;
        private readonly int _riseMs, _warnMs;

        public Flood(string tile, double reach, Func<int, int, bool> dry, int riseMs = 3500, int warnMs = 1000, string warnTile = "Crucible Warning")
            : base(100)
        {
            _tile = tile;
            _warnTile = warnTile;
            _reach = reach;
            _dry = dry;
            _riseMs = riseMs;
            _warnMs = warnMs;
        }

        protected internal override void Begin(Entity host, Run r)
        {
            var at = new Dictionary<(int, int), int>();
            foreach (var (dx, dy, rr, _) in Disc(0, _reach))
                if (!_dry(dx, dy))
                    at[(dx, dy)] = _warnMs + (int)((_reach - rr) / _reach * _riseMs);
            r.Data = at;
        }

        protected internal override void Shape(Entity host, Run r)
        {
            var water = Tile(host, _tile);
            var warn = Tile(host, _warnTile);
            foreach (var (sq, at) in (Dictionary<(int, int), int>)r.Data)
            {
                if (r.Elapsed >= at)
                    r.Want[sq] = water;
                else if (r.Elapsed >= at - _warnMs)
                    r.Want[sq] = warn;
            }
        }
    }

    // Walls raised for as long as the state lasts: `objName` (a static object that blocks) on every
    // walkable square round the host's spawn where `where(dx, dy)` says, within `reach`. Whoever
    // stands where a wall will rise is moved to the nearest square beside it first. Ferrogrim's
    // Lockdown.
    internal class Rampart : Behavior
    {
        private readonly string _objName;
        private readonly double _reach;
        private readonly Func<int, int, bool> _where;

        public Rampart(string objName, double reach, Func<int, int, bool> where)
        {
            _objName = objName;
            _reach = reach;
            _where = where;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            var data = host.GameServer.Resources.GameData;
            if (!data.IdToObjectType.TryGetValue(_objName, out var type))
                return;
            var c = host.SpawnPoint.Value;
            var (cx, cy) = ((int)c.X, (int)c.Y);
            var r = (int)Math.Ceiling(_reach);
            var raised = new List<(int X, int Y)>();
            var walls = new HashSet<(int, int)>();
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > _reach * _reach || !_where(dx, dy))
                        continue;
                    var (x, y) = (cx + dx, cy + dy);
                    if (!world.Map.Contains(x, y))
                        continue;
                    var t = world.Map[x, y];
                    if (t.ObjType != 0 || !data.Tiles.TryGetValue(t.TileId, out var desc) || desc.NoWalk)
                        continue;
                    walls.Add((x, y));
                }
            // anyone in the way steps aside first
            foreach (var p in world.Players.Values.ToList())
            {
                if (!walls.Contains(((int)p.X, (int)p.Y)))
                    continue;
                var free = Enumerable.Range(1, 3).SelectMany(d => new[] { (d, 0), (-d, 0), (0, d), (0, -d), (d, d), (-d, -d), (d, -d), (-d, d) })
                    .Select(o => ((int)p.X + o.Item1, (int)p.Y + o.Item2))
                    .FirstOrDefault(s => !walls.Contains(s) && world.IsPassable(s.Item1 + 0.5, s.Item2 + 0.5));
                if (free != default)
                    p.TeleportPosition(time, new Position(free.Item1 + 0.5f, free.Item2 + 0.5f), true);
            }
            var desc0 = data.ObjectDescs[type];
            foreach (var (x, y) in walls)
            {
                var t = world.Map[x, y];
                t.ObjType = type;
                t.ObjDesc = desc0;
                t.ObjId = world.GetNextEntityId();
                t.UpdateCount++;
                raised.Add((x, y));
            }
            state = raised;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is List<(int X, int Y)> raised && host.World != null)
                foreach (var (x, y) in raised)
                {
                    var t = host.World.Map[x, y];
                    t.ObjType = 0;
                    t.ObjDesc = null;
                    t.UpdateCount++;
                }
            state = null;
        }
    }

    // A moment of the fight for everyone in the Crucible: the phase's name across their screens
    // (CrucibleWorld.Cue, the Rust client's crucible.rs), and with `shake` the ground shaking under
    // them all (ShowEffect Earthquake).
    internal class CrucibleCue : Behavior
    {
        private readonly byte _cue;
        private readonly bool _shake;

        public CrucibleCue(byte cue, bool shake = false)
        {
            _cue = cue;
            _shake = shake;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            if (host.World is CrucibleWorld crucible)
                crucible.Cue(host, _cue);
            if (_shake)
                Crucible.Shake(host.World);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }

    internal static class Crucible
    {
        public static void Shake(World world)
        {
            if (world == null)
                return;
            foreach (var p in world.Players.Values)
                p.Client?.SendPacket(new ShowEffect { EffectType = EffectType.Earthquake, TargetObjectId = p.Id, Pos1 = new Position(), Pos2 = new Position(), Color = new ARGB(0xFFFFFFFF) });
        }

        // the ground shaking for those near a spot (the Meteor Mortar's shell, Player.CruciblePowers.cs)
        public static void ShakeNear(World world, Position at, float within = 12)
        {
            if (world == null)
                return;
            foreach (var p in world.Players.Values)
                if (p.DistTo(at.X, at.Y) <= within)
                    p.Client?.SendPacket(new ShowEffect { EffectType = EffectType.Earthquake, TargetObjectId = p.Id, Pos1 = new Position(), Pos2 = new Position(), Color = new ARGB(0xFFFFFFFF) });
        }

        // a ring blast round the host: the BlastWave effect and the damage, for those within it
        public static void Slam(Entity host, double radius, int damage, uint color, ConditionEffectIndex effect = 0, int effectMs = 0)
        {
            var at = new Position(host.X, host.Y);
            host.World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.BlastWave,
                TargetObjectId = host.Id,
                Pos1 = at,
                Pos2 = new Position((float)radius, 0),
                Color = new ARGB(color),
            }, ref at);
            Strike.Land(host.World, host, at, radius, damage, color, effect, effectMs);
        }
    }

    // Slam: a blast round the host, once on entering the state (Crucible.Slam), shaking the ground.
    internal class CrucibleSlam : Behavior
    {
        private readonly double _radius;
        private readonly int _damage;
        private readonly uint _color;
        private readonly int _delayMs;

        public CrucibleSlam(double radius, int damage, uint color, int delayMs = 0)
        {
            _radius = radius;
            _damage = damage;
            _color = color;
            _delayMs = delayMs;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = _delayMs;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not int left || left < 0)
                return;
            left -= time.ElapsedMsDelta;
            if (left <= 0)
            {
                Crucible.Slam(host, _radius, _damage, _color);
                Crucible.Shake(host.World);
                state = -1;
                return;
            }
            state = left;
        }
    }

    // The Apex's forms (ApexForm): for each of its Heart's states, what each square round its middle
    // is (`ms` into the form: the orrery turns).
    public static class ApexForms
    {
        // what a form puts on a square: its ground (null: the sky), what stands on it, and whether it
        // is only to be seen (a shard floating off the floor, nobody is thrown onto it)
        public readonly record struct Look(string Ground, string Obj = null, bool Decor = false);

        public static readonly Dictionary<string, string> ByState = new()
        {
            ["Decree"] = null, ["Iron"] = "fort", ["Tide"] = "isles", ["Forge"] = "cross",
            ["Hours"] = "orrery", ["Breaks"] = "shards", ["Unbound"] = "halo",
        };

        internal static string FormOf(Entity host)
        {
            for (var st = host.CurrentState; st != null; st = st.Parent)
                if (ByState.TryGetValue(st.Name, out var f))
                    return f;
            return null;
        }

        private static double Ang(double a, double b)
        {
            var d = (a - b) % (2 * Math.PI);
            if (d < -Math.PI) d += 2 * Math.PI;
            if (d > Math.PI) d -= 2 * Math.PI;
            return Math.Abs(d);
        }

        // ---- the forms: is (dx, dy) floor in it, and what is it ----

        private static bool Fort(int dx, int dy)
        {
            var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
            if (Math.Max(Math.Abs(ax - 7), Math.Abs(ay - 7)) <= 1)
                return false; // the yard's pits
            if (Math.Sqrt((ax - 14) * (ax - 14) + (ay - 14) * (ay - 14)) <= 6)
                return true;
            return Math.Max(ax, ay) <= 14 && !(Math.Min(ax, ay) <= 2 && Math.Max(ax, ay) >= 12);
        }

        private static bool Isle(int dx, int dy, out bool middle)
        {
            middle = Math.Sqrt(dx * dx + dy * dy) <= 5.5;
            if (middle)
                return true;
            for (var k = 0; k < 6; k++)
            {
                var a = Math.PI / 3 * k + Math.PI / 6;
                double ix = Math.Cos(a) * 14, iy = Math.Sin(a) * 14;
                if ((dx - ix) * (dx - ix) + (dy - iy) * (dy - iy) <= 4.6 * 4.6)
                    return true;
            }
            return false;
        }

        private static bool Cross(int dx, int dy, out bool furnace)
        {
            var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
            furnace = Math.Sqrt((ax - 15) * (ax - 15) + (ay - 15) * (ay - 15)) <= 3.2;
            if (furnace)
                return true;
            if (Math.Max(ax, ay) <= 7)
                return true;
            var (minor, major) = (Math.Min(ax, ay), Math.Max(ax, ay));
            return minor <= 3 && (major <= 18 || Math.Sqrt(minor * minor + (major - 18) * (major - 18)) <= 3.4);
        }

        // the orrery at `ms` into its form: its spokes turning
        private static bool Orrery(int dx, int dy, int ms, out bool spoke)
        {
            spoke = false;
            var r = Math.Sqrt(dx * dx + dy * dy);
            var a = Math.Atan2(dy, dx);
            if (r <= 5 || r >= 8 && r <= 12 || r >= 15 && r <= 21)
                return true;
            if (r > 21)
                return false;
            var t = ms / 1000.0;
            var inner = r < 8;
            var turn = inner ? t * Math.PI / 20 : -t * Math.PI / 26; // 9 and about 7 degrees a second
            var count = inner ? 3 : 4;
            for (var k = 0; k < count; k++)
                if (Ang(a, turn + 2 * Math.PI * k / count) * r <= 1.4)
                    return spoke = true;
            return false;
        }

        private static bool Shards(int dx, int dy, out bool shard)
        {
            shard = false;
            if (Math.Sqrt(dx * dx + dy * dy) <= 8)
                return true;
            for (var k = 0; k < 8; k++)
            {
                var a = Math.PI / 4 * k + Math.PI / 8;
                var rr = 15 + (k % 3) * 1.5;
                double sx = Math.Cos(a) * rr, sy = Math.Sin(a) * rr;
                if ((dx - sx) * (dx - sx) + (dy - sy) * (dy - sy) <= (k % 2 == 0 ? 2.4 * 2.4 : 1.8 * 1.8))
                    return shard = true;
            }
            return false;
        }

        private static bool Halo(int dx, int dy, out bool obelisk)
        {
            obelisk = false;
            var r = Math.Sqrt(dx * dx + dy * dy);
            if (r >= 5 && r <= 16)
                return true;
            for (var k = 0; k < 8; k++)
            {
                var a = Math.PI / 4 * k;
                if (Math.Abs(dx - Math.Round(Math.Cos(a) * 19.5)) <= 0 && Math.Abs(dy - Math.Round(Math.Sin(a) * 19.5)) <= 0)
                    return obelisk = true;
            }
            return false;
        }

        private static bool Inside(string form, int dx, int dy, int ms) => form switch
        {
            "fort" => Fort(dx, dy),
            "isles" => Math.Sqrt(dx * dx + dy * dy) <= 21,
            "cross" => Cross(dx, dy, out _),
            "orrery" => Orrery(dx, dy, ms, out _),
            "shards" => Shards(dx, dy, out _),
            "halo" => Halo(dx, dy, out _),
            _ => false,
        };

        public static Look Want(string form, int dx, int dy, int ms)
        {
            if (!Inside(form, dx, dy, ms))
                return new Look(null);
            var edge = !Inside(form, dx + 1, dy, ms) || !Inside(form, dx - 1, dy, ms) || !Inside(form, dx, dy + 1, ms) || !Inside(form, dx, dy - 1, ms);
            var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
            var r = Math.Sqrt(dx * dx + dy * dy);
            var a = Math.Atan2(dy, dx);
            switch (form)
            {
                case "fort":
                {
                    string obj = null;
                    if (ax == 14 && ay == 14 || Math.Min(ax, ay) == 3 && Math.Max(ax, ay) == 12)
                        obj = "Bastion Pillar";
                    if (edge)
                        return new Look("Bastion Rim", obj);
                    return new Look(Math.Max(ax, ay) == 10 && Math.Min(ax, ay) >= 4 || Math.Max(ax, ay) == 4 ? "Bastion Inlay" : "Bastion Flagstone", obj);
                }
                case "isles":
                {
                    if (r > 20)
                        return new Look("Sanctum Rim");
                    if (!Isle(dx, dy, out var middle))
                    {
                        // shallows out from the middle isle to each of the others, the rest deep sea
                        for (var k = 0; k < 6; k++)
                            if (r < 11 && Ang(a, Math.PI / 3 * k + Math.PI / 6) * r <= 1.3)
                                return new Look("Sanctum Shallows");
                        return new Look("Deep Tide");
                    }
                    var coast = !Isle(dx + 1, dy, out _) || !Isle(dx - 1, dy, out _) || !Isle(dx, dy + 1, out _) || !Isle(dx, dy - 1, out _);
                    string spire = null;
                    for (var k = 0; k < 6; k++)
                    {
                        var ka = Math.PI / 3 * k + Math.PI / 6;
                        if (dx == (int)Math.Round(Math.Cos(ka) * 16) && dy == (int)Math.Round(Math.Sin(ka) * 16))
                            spire = "Coral Spire";
                    }
                    return new Look(coast ? "Coral Isle" : "Sanctum Mosaic", spire);
                }
                case "cross":
                {
                    Cross(dx, dy, out var furnace);
                    if (furnace)
                        return new Look(edge ? "Foundry Rim" : "Foundry Plate", ax == 15 && ay == 15 ? "Foundry Brazier" : null, Decor: true);
                    var (minor, major) = (Math.Min(ax, ay), Math.Max(ax, ay));
                    var obj = minor == 2 && major == 19 ? "Foundry Brazier" : null;
                    if (edge)
                        return new Look("Foundry Rim", obj);
                    var slag = minor == 0 && major >= 9 && major <= 19 || ax == ay && ax >= 2 && ax <= 6;
                    return new Look(slag ? "Cooling Slag" : "Foundry Plate", obj);
                }
                case "orrery":
                {
                    Orrery(dx, dy, ms, out var spoke);
                    if (spoke)
                        return new Look("Dial Numeral");
                    if (r > 20.3)
                        return new Look("Orrery Rim");
                    var numeral = Math.Abs(r - 18) < 0.6 && Ang(a, Math.Round(a / (Math.PI / 6)) * (Math.PI / 6)) * r < 0.6;
                    var pylon = r >= 9.5 && r <= 10.5 && Ang(a, Math.Round((a - Math.PI / 4) / (Math.PI / 2)) * (Math.PI / 2) + Math.PI / 4) * r < 0.5;
                    return new Look(numeral || Math.Abs(r - 4) < 0.5 ? "Dial Numeral" : "Orrery Dial", pylon ? "Orrery Pylon" : null);
                }
                case "shards":
                {
                    Shards(dx, dy, out var shard);
                    return new Look(edge ? "Apex Rim" : "Apex Marble", null, Decor: shard);
                }
                default: // halo
                {
                    Halo(dx, dy, out var obelisk);
                    if (obelisk)
                        return new Look("Unbound Rim", "Apex Obelisk", Decor: true);
                    var ray = r >= 7 && r <= 15 && Ang(a, Math.Round(a / (Math.PI / 4)) * (Math.PI / 4)) * r <= 0.5;
                    return new Look(edge ? "Unbound Rim" : ray ? "Apex Sigil" : "Unbound Marble");
                }
            }
        }
    }

    // The Apex changing shape through Vaelith's fight (BehaviorDb.Crucible.cs, the Apex Heart's
    // "Fight" state): each of the Heart's states has a form of its own, and as the Heart moves into
    // one, the floor turns into it in a wave out from the middle over `spreadMs`: ground that stays
    // takes the form's stone, ground that goes cracks (`warnMs` before) and falls into the sky,
    // ground that comes rises out of it, and pillars rise and fall with their squares.
    // - Decree: the Apex as the map has it (a star of marble);
    // - Iron: a fortress, a square keep with a round bastion at each corner, a gate in each side and
    //   four pits in its yard, pillars in the bastions and at the gates;
    // - Tide: an archipelago, a middle isle and six round it in a deep sea that slows and grinds
    //   (Deep Tide), shallows running out to them, coral spires on the isles;
    // - Forge: a forge hall in a cross, four long arms over open sky, slag down them, braziers at
    //   their ends, four furnaces floating off its corners;
    // - Hours: an orrery, a hub and two rings over the sky, joined by spokes that turn (the inner
    //   ones one way, the outer the other: the way across moves, cracking a moment before);
    // - Breaks: shattered, a disc of the middle left with shards of the Apex floating round it;
    // - Unbound: a halo of void marble round the void itself, rays of gold across it, obelisks
    //   floating round it.
    // Whoever stands where the ground falls is thrown onto the nearest that stays (`damage`).
    // Squares a hazard painter has (SweepingArms, TideSweep...) are simply changed: it leaves a
    // square another has changed. When the fight ends, the Apex goes back as the map has it.
    internal class ApexForm : Behavior
    {
        private readonly int _reach, _spreadMs, _warnMs, _damage;

        public ApexForm(int reach = 25, int spreadMs = 2600, int warnMs = 900, int damage = 120)
        {
            _reach = reach;
            _spreadMs = spreadMs;
            _warnMs = warnMs;
            _damage = damage;
        }

        private sealed class S
        {
            public string Form = "";
            public int Now, Began;
            public Dictionary<(int, int), (ushort G, ushort O)> Map0;
            public Dictionary<(int, int), (ushort G, ushort O, bool Decor)> Shown;
            public readonly Dictionary<(int, int), (int At, ushort G, ushort O, bool Crack)> Pending = new();
            public readonly Random Rng = new Random();
        }

        // ---- running it ----

        private static ushort Ground(Entity host, string id) =>
            id != null && host.GameServer.Resources.GameData.IdToTileType.TryGetValue(id, out var t) ? t : (ushort)0;

        private static ushort Obj(Entity host, string id) =>
            id != null && host.GameServer.Resources.GameData.IdToObjectType.TryGetValue(id, out var t) ? t : (ushort)0;

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var map = host.World?.Map;
            if (map == null || host.SpawnPoint == null)
                return;
            var c = host.SpawnPoint.Value;
            var s = new S { Map0 = new(), Shown = new() };
            for (var dy = -_reach; dy <= _reach; dy++)
                for (var dx = -_reach; dx <= _reach; dx++)
                {
                    var (x, y) = ((int)c.X + dx, (int)c.Y + dy);
                    if (!map.Contains(x, y))
                        continue;
                    var t = map[x, y];
                    s.Map0[(dx, dy)] = (t.TileId, t.ObjType);
                    s.Shown[(dx, dy)] = (t.TileId, t.ObjType, false);
                }
            state = s;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not S s || host.World == null)
                return;
            s.Now += time.ElapsedMsDelta;
            var sky = Ground(host, "Empty");
            var form = ApexForms.FormOf(host);
            if (form != s.Form)
            {
                // a new form: the change spreads out from the middle
                s.Form = form;
                s.Began = s.Now;
                foreach (var key in s.Map0.Keys)
                {
                    var (g, o, decor) = Target(host, s, key, 0);
                    var r = Math.Sqrt(key.Item1 * key.Item1 + key.Item2 * key.Item2);
                    Schedule(s, key, g, o, decor, s.Now + _warnMs + (int)(r / _reach * _spreadMs) + s.Rng.Next(220), sky);
                }
            }
            else if (form == "orrery" && s.Now - s.Began > _spreadMs + _warnMs)
            {
                // its spokes turning: what goes cracks a moment before, what comes is there at once
                var ms = s.Now - s.Began;
                foreach (var key in s.Map0.Keys)
                {
                    if (s.Pending.ContainsKey(key))
                        continue;
                    var later = Target(host, s, key, ms + _warnMs);
                    var now = Target(host, s, key, ms);
                    var shown = s.Shown[key];
                    if (later.G == sky && shown.G != sky)
                        Schedule(s, key, sky, 0, false, s.Now + _warnMs, sky);
                    else if (now.G != sky && shown.G == sky)
                        Schedule(s, key, now.G, now.O, now.Decor, s.Now, sky);
                }
            }
            Apply(host, s, time, sky);
        }

        private (ushort G, ushort O, bool Decor) Target(Entity host, S s, (int, int) key, int ms)
        {
            if (s.Form == null)
            {
                var m = s.Map0[key];
                return (m.G, m.O, false);
            }
            var look = ApexForms.Want(s.Form, key.Item1, key.Item2, ms);
            return (look.Ground == null ? Ground(host, "Empty") : Ground(host, look.Ground), Obj(host, look.Obj), look.Decor);
        }

        // `at` the square becomes (g, o); going to the sky, it cracks `_warnMs` before
        private void Schedule(S s, (int, int) key, ushort g, ushort o, bool decor, int at, ushort sky)
        {
            var shown = s.Shown[key];
            if (shown.G == g && shown.O == o)
            {
                s.Pending.Remove(key);
                s.Shown[key] = (g, o, decor);
                return;
            }
            var falls = g == sky && shown.G != sky;
            s.Pending[key] = (at, g, o, falls);
            s.Shown[key] = (g, o, decor);
        }

        private void Apply(Entity host, S s, TickTime time, ushort sky)
        {
            var world = host.World;
            var map = world.Map;
            var data = host.GameServer.Resources.GameData;
            var c = host.SpawnPoint.Value;
            var crack = Ground(host, "Crumbling Floor");
            var changed = false;
            foreach (var (key, p) in s.Pending.ToList())
            {
                var (x, y) = ((int)c.X + key.Item1, (int)c.Y + key.Item2);
                var t = map[x, y];
                if (p.Crack && s.Now >= p.At - _warnMs && s.Now < p.At)
                {
                    if (t.TileId != crack && t.TileId != sky)
                    {
                        t.TileId = crack;
                        t.UpdateCount++;
                        changed = true;
                    }
                    continue;
                }
                if (s.Now < p.At)
                    continue;
                s.Pending.Remove(key);
                if (t.TileId != p.G)
                {
                    t.TileId = p.G;
                    t.UpdateCount++;
                    changed = true;
                }
                if (t.ObjType != p.O)
                {
                    if (p.O == 0)
                        t.ObjType = 0; // its id stays, so the clients are told it's gone
                    else
                    {
                        t.ObjType = p.O;
                        t.ObjDesc = data.ObjectDescs[p.O];
                        t.ObjId = world.GetNextEntityId();
                    }
                    t.UpdateCount++;
                    changed = true;
                }
            }
            if (changed)
                Floor.Refresh(host);

            // whoever stands where it fell: thrown onto the nearest ground that stays
            foreach (var p in world.Players.Values.ToList())
            {
                var (px, py) = ((int)p.X, (int)p.Y);
                var key = (px - (int)c.X, py - (int)c.Y);
                if (!s.Shown.ContainsKey(key) || map[px, py].TileId != sky)
                    continue;
                (int, int)? best = null;
                var bestD = double.MaxValue;
                foreach (var (k, v) in s.Shown)
                {
                    if (v.G == sky || v.Decor || s.Pending.ContainsKey(k) || map[(int)c.X + k.Item1, (int)c.Y + k.Item2].TileId == sky)
                        continue;
                    var d = (k.Item1 - key.Item1) * (k.Item1 - key.Item1) + (k.Item2 - key.Item2) * (k.Item2 - key.Item2);
                    if (d < bestD)
                        (bestD, best) = (d, k);
                }
                if (best == null)
                    continue;
                var to = new Position((int)c.X + best.Value.Item1 + 0.5f, (int)c.Y + best.Value.Item2 + 0.5f);
                p.TeleportPosition(time, to, true);
                Strike.Land(world, host, to, 0.6, _damage, 0xFFFFD04A);
            }
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            // the fight over: the Apex as the map has it
            if (state is S s && host.World != null)
            {
                var map = host.World.Map;
                var data = host.GameServer.Resources.GameData;
                var c = host.SpawnPoint.Value;
                foreach (var ((dx, dy), (g, o)) in s.Map0)
                {
                    var t = map[(int)c.X + dx, (int)c.Y + dy];
                    if (t.TileId == g && t.ObjType == o)
                        continue;
                    t.TileId = g;
                    if (t.ObjType != o)
                    {
                        t.ObjType = o;
                        if (o != 0)
                        {
                            t.ObjDesc = data.ObjectDescs[o];
                            t.ObjId = host.World.GetNextEntityId();
                        }
                    }
                    t.UpdateCount++;
                }
                Floor.Refresh(host);
            }
            state = null;
        }
    }
}
