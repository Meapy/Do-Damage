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

        // after each step: anything else the behaviour does (rescuing whoever stands on sky, say)
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
        // the ground shakes at most this often in a world, however many things shake it (a way
        // opening, a floor forming, a phase, a slam): every one of them at once was too much
        private const long ShakeEveryMs = 4000;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, System.Runtime.CompilerServices.StrongBox<long>> LastShake = new();

        private static bool MayShake(World world)
        {
            var last = LastShake.GetValue(world, _ => new System.Runtime.CompilerServices.StrongBox<long>(long.MinValue / 2));
            var now = Environment.TickCount64;
            if (now - last.Value < ShakeEveryMs)
                return false;
            last.Value = now;
            return true;
        }

        public static void Shake(World world)
        {
            if (world == null || !MayShake(world))
                return;
            foreach (var p in world.Players.Values)
                p.Client?.SendPacket(new ShowEffect { EffectType = EffectType.Earthquake, TargetObjectId = p.Id, Pos1 = new Position(), Pos2 = new Position(), Color = new ARGB(0xFFFFFFFF) });
        }

        // the ground shaking for those near a spot (the Meteor Mortar's shell, Player.CruciblePowers.cs)
        public static void ShakeNear(World world, Position at, float within = 12)
        {
            if (world == null || !MayShake(world))
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
}
