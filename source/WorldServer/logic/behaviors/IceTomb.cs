using System;
using System.Collections.Generic;
using System.Linq;
using Shared.crucible;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.objects.connection;
using WorldServer.core.resurrection;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // The Ice Tomb's own (BehaviorDb.IceTomb.cs, tools/build_ice_tomb.py): the tomb that changes
    // round those who walk it. The lake's ice cracks under whoever stands still on it; a bridge of
    // ice freezes across each chasm as someone comes to its edge; the Sealed Door lights a lantern
    // for each seal broken and, with all three, opens on the dark, where the Frozen Court assembles
    // (IceCourt.cs). The Court's phases are named across everyone's screen as the Crucible's are.
    internal static class IceTomb
    {
        public const string World = "Ice Tomb";
        public static readonly string[] Bosses = { "Kaldrun, the Rime Warden", "Ysolde, the Aurora Oracle", "Hrimgar, the Frost Reaver" };
        public static readonly string[] Seals = { "Seal of the Warden", "Seal of the Oracle", "Seal of the Reaver" };
        public static readonly string[] Lanterns = { "Warden's Lantern", "Oracle's Lantern", "Reaver's Lantern" };
        public const uint Ice = 0xFF9FE8FF, Frost = 0xFFDCF6FF, Aurora = 0xFF4AE8A4, Steel = 0xFF9AB4D4, Deep = 0xFF2C5A86;

        public static ushort Tile(Entity host, string id) =>
            host.GameServer.Resources.GameData.IdToTileType.TryGetValue(id, out var t) ? t : (ushort)0;

        public static ushort Object(Entity host, string id) =>
            host.GameServer.Resources.GameData.IdToObjectType.TryGetValue(id, out var t) ? t : (ushort)0;

        public static void Ground(World w, int x, int y, ushort ground)
        {
            if (ground == 0 || !w.Map.Contains(x, y))
                return;
            var t = w.Map[x, y];
            if (w.GameServer.Resources.GameData.Tiles.TryGetValue(t.TileId, out var d) && d.Damaging)
                w.GroundPaintedOver(x, y, t.TileId);
            TombCourt.Court.SetGround(w, t, ground);
        }

        public static void Fx(World w, float x, float y, EffectType type, uint color, float size, int duration = 0) =>
            TombCourt.Fx(w, x, y, type, color, new Position(x, y), type == EffectType.GroundMark ? new Position(size, 0) : new Position(x + size, y), duration);

        // a moment of the Court's fight for everyone in the tomb: its name across their screens
        // (the Crucible's banners: CrucibleMessage, the Rust client's crucible.rs, which shows the
        // floors from EncounterFloor on as encounters of their own)
        public static void Cue(World world, byte cue, ushort champion)
        {
            if (world == null)
                return;
            var m = new CrucibleMessage
            {
                Kind = CrucibleMessage.Cue,
                FloorNumber = CrucibleCues.IceTombFloor,
                CueId = cue,
                Champion = champion,
                Text = CrucibleCues.Name(cue),
            };
            foreach (var p in world.Players.Values)
                p.Client?.SendPacket(m);
        }

        public static void Title(World world)
        {
            if (world == null)
                return;
            var m = new CrucibleMessage { Kind = CrucibleMessage.Floor, FloorNumber = CrucibleCues.IceTombFloor, Text = "The Frozen Court" };
            foreach (var p in world.Players.Values)
                p.Client?.SendPacket(m);
        }
    }

    // A phase of the Court begins: its name across everyone's screens, the champion's over it (the
    // host, or `champion` by name), and with `shake` the ground shaking.
    internal class EncounterCue : Behavior
    {
        private readonly byte _cue;
        private readonly string _champion;
        private readonly bool _shake;

        public EncounterCue(byte cue, string champion = null, bool shake = false)
        {
            _cue = cue;
            _champion = champion;
            _shake = shake;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var type = _champion == null ? host.ObjectType : IceTomb.Object(host, _champion);
            IceTomb.Cue(host.World, _cue, type);
            if (_shake)
                Crucible.Shake(host.World);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }

    // The Frozen Court's title card, for everyone in the tomb, as the first of them walks in.
    internal class EncounterTitle : Behavior
    {
        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => IceTomb.Title(host.World);

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }

    // The Frozen Lake (on its invisible Lake Heart): the ice cracks under whoever stands on one
    // square for `standMs`, the square and those round it showing `crack` (harmless: move), then
    // breaks into `water` (slow, and the cold burns) after `crackMs`, and freezes over again after
    // `openMs`. Whoever keeps moving never goes through.
    internal class ThinIce : Behavior
    {
        private readonly string _ice, _crack, _water;
        private readonly double _reach;
        private readonly int _standMs, _crackMs, _openMs;

        public ThinIce(string ice, string crack, string water, double reach = 16, int standMs = 650, int crackMs = 1100, int openMs = 7000)
        {
            _ice = ice;
            _crack = crack;
            _water = water;
            _reach = reach;
            _standMs = standMs;
            _crackMs = crackMs;
            _openMs = openMs;
        }

        private sealed class Lake
        {
            public HashSet<(int X, int Y)> Squares;
            public ushort Ice, Crack, Water;
            // square -> (what it is now: 1 cracking, 2 open water; ms left in it)
            public readonly Dictionary<(int X, int Y), (int Stage, int Ms)> Changing = new();
            // player -> (the square they stand on, how long)
            public readonly Dictionary<int, ((int X, int Y) At, int Ms)> Standing = new();
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var l = new Lake { Ice = IceTomb.Tile(host, _ice), Crack = IceTomb.Tile(host, _crack), Water = IceTomb.Tile(host, _water) };
            // the lake: the ice joined to the square under the heart
            l.Squares = new HashSet<(int, int)>();
            var map = host.World.Map;
            var start = ((int)host.X, (int)host.Y);
            var todo = new Stack<(int X, int Y)>();
            todo.Push(start);
            while (todo.Count > 0 && l.Squares.Count < 1200)
            {
                var q = todo.Pop();
                if (l.Squares.Contains(q) || !map.Contains(q.X, q.Y) || map[q.X, q.Y].TileId != l.Ice)
                    continue;
                if (Math.Abs(q.X - start.Item1) > _reach || Math.Abs(q.Y - start.Item2) > _reach)
                    continue;
                l.Squares.Add(q);
                todo.Push((q.X + 1, q.Y)); todo.Push((q.X - 1, q.Y)); todo.Push((q.X, q.Y + 1)); todo.Push((q.X, q.Y - 1));
            }
            state = l;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Lake l || l.Ice == 0 || host.World == null)
                return;
            var w = host.World;
            var dt = time.ElapsedMsDelta;
            var changed = false;

            // squares already going: cracking to open water, open water freezing over
            foreach (var (q, (stage, ms)) in l.Changing.ToList())
            {
                var left = ms - dt;
                if (left > 0)
                {
                    l.Changing[q] = (stage, left);
                    continue;
                }
                if (stage == 1)
                {
                    IceTomb.Ground(w, q.X, q.Y, l.Water);
                    l.Changing[q] = (2, _openMs + Random.Shared.Next(1500));
                    if ((q.X + q.Y) % 3 == 0)
                        IceTomb.Fx(w, q.X + 0.5f, q.Y + 0.5f, EffectType.Diffuse, IceTomb.Deep, 1.4f);
                }
                else
                {
                    IceTomb.Ground(w, q.X, q.Y, l.Ice);
                    l.Changing.Remove(q);
                    if ((q.X * 7 + q.Y) % 4 == 0)
                        IceTomb.Fx(w, q.X + 0.5f, q.Y + 0.5f, EffectType.Diffuse, IceTomb.Frost, 0.9f);
                }
                changed = true;
            }

            // whoever stands still on the ice
            var seen = new HashSet<int>();
            foreach (var p in w.Players.Values)
            {
                if (p.Dead || p.DistTo(host) > _reach + 1)
                    continue;
                var at = ((int)p.X, (int)p.Y);
                if (!l.Squares.Contains(at))
                    continue;
                seen.Add(p.Id);
                var ms = l.Standing.TryGetValue(p.Id, out var s) && s.At == at ? s.Ms + dt : 0;
                l.Standing[p.Id] = (at, ms);
                if (ms < _standMs || l.Changing.ContainsKey(at))
                    continue;
                // it cracks: the square and the eight round it
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var q = (at.Item1 + dx, at.Item2 + dy);
                        if (!l.Squares.Contains(q) || l.Changing.ContainsKey(q) || w.Map[q.Item1, q.Item2].TileId != l.Ice)
                            continue;
                        IceTomb.Ground(w, q.Item1, q.Item2, l.Crack);
                        l.Changing[q] = (1, _crackMs + Random.Shared.Next(300));
                        changed = true;
                    }
                IceTomb.Fx(w, p.X, p.Y, EffectType.Diffuse, IceTomb.Frost, 1.8f);
            }
            foreach (var id in l.Standing.Keys.Where(id => !seen.Contains(id)).ToList())
                l.Standing.Remove(id);
            if (changed)
                Floor.Refresh(host);
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is Lake l && host.World != null)
            {
                foreach (var q in l.Changing.Keys)
                    IceTomb.Ground(host.World, q.X, q.Y, l.Ice);
                Floor.Refresh(host);
            }
            state = null;
        }
    }

    // A chasm (on its invisible Chasm Heart, in its middle): as someone comes to either edge of it
    // by the heart, a bridge of ice freezes across from their side, a row every `rowMs`: three
    // squares of ice between rims of rime, the chasm's mist cleared where it lies, frost bursting
    // up along its front. Once across, it stays.
    internal class IceBridge : Behavior
    {
        private readonly string _abyss, _bridge, _rim, _mist;
        private readonly int _rowMs;
        private readonly double _notice;

        public IceBridge(string abyss, string bridge, string rim, string mist, int rowMs = 300, double notice = 5.5)
        {
            _abyss = abyss;
            _bridge = bridge;
            _rim = rim;
            _mist = mist;
            _rowMs = rowMs;
            _notice = notice;
        }

        private sealed class Span
        {
            // the rows of the bridge, in order across, each its squares (and whether a rim)
            public List<List<(int X, int Y, bool Rim)>> Rows = new();
            public ushort Bridge, Rim, Mist;
            public int Next = -1, Step = 1, Ms;
            public bool Done;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var w = host.World;
            var abyss = IceTomb.Tile(host, _abyss);
            var s = new Span { Bridge = IceTomb.Tile(host, _bridge), Rim = IceTomb.Tile(host, _rim), Mist = IceTomb.Object(host, _mist) };
            int hx = (int)host.X, hy = (int)host.Y;
            bool Chasm(int x, int y) => w.Map.Contains(x, y) && w.Map[x, y].TileId == abyss;
            // which way the chasm runs: the way it reaches further from the heart
            int Run(int dx, int dy)
            {
                var n = 0;
                while (n < 30 && Chasm(hx + dx * (n + 1), hy + dy * (n + 1)))
                    n++;
                return n;
            }
            var eastWest = Run(1, 0) + Run(-1, 0) >= Run(0, 1) + Run(0, -1);
            // the bridge crosses it the other way: from one edge to the other, through the heart
            var (ax, ay) = eastWest ? (0, 1) : (1, 0);   // across
            var (sx, sy) = eastWest ? (1, 0) : (0, 1);   // sideways along the bridge's rows
            var back = 0;
            while (back < 20 && Chasm(hx - ax * (back + 1), hy - ay * (back + 1)))
                back++;
            var fwd = 0;
            while (fwd < 20 && Chasm(hx + ax * (fwd + 1), hy + ay * (fwd + 1)))
                fwd++;
            for (var k = -back; k <= fwd; k++)
            {
                var row = new List<(int, int, bool)>();
                for (var j = -2; j <= 2; j++)
                {
                    int x = hx + ax * k + sx * j, y = hy + ay * k + sy * j;
                    if (Chasm(x, y) || (x == hx && y == hy))
                        row.Add((x, y, Math.Abs(j) == 2));
                }
                s.Rows.Add(row);
            }
            state = s;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Span s || s.Done || s.Rows.Count == 0 || host.World == null)
                return;
            var w = host.World;
            if (s.Next < 0)
            {
                // someone at an edge? (by the middle of the first row or the last)
                (float X, float Y) Mid(List<(int X, int Y, bool Rim)> row) =>
                    row.Count == 0 ? (host.X, host.Y) : ((float)row.Average(q => q.X) + 0.5f, (float)row.Average(q => q.Y) + 0.5f);
                var a = Mid(s.Rows[0]);
                var b = Mid(s.Rows[^1]);
                var near = w.Players.Values.Where(p => !p.Dead).Select(p => (A: p.DistTo(a.X, a.Y), B: p.DistTo(b.X, b.Y)))
                    .Where(d => Math.Min(d.A, d.B) <= _notice).ToList();
                if (near.Count == 0)
                    return;
                var fromA = near.Min(d => d.A) <= near.Min(d => d.B);
                s.Next = fromA ? 0 : s.Rows.Count - 1;
                s.Step = fromA ? 1 : -1;
                s.Ms = 600;
                // the frost runs out over the dark first, along where it will lie
                foreach (var row in s.Rows)
                    foreach (var (x, y, rim) in row)
                        if (!rim)
                            IceTomb.Fx(w, x + 0.5f, y + 0.5f, EffectType.GroundMark, IceTomb.Ice, 0.7f, 600 + s.Rows.Count * _rowMs);
                Crucible.ShakeNear(w, new Position(host.X, host.Y), 14);
                return;
            }
            s.Ms -= time.ElapsedMsDelta;
            if (s.Ms > 0)
                return;
            s.Ms = _rowMs;
            var r = s.Rows[s.Next];
            foreach (var (x, y, rim) in r)
            {
                IceTomb.Ground(w, x, y, rim ? s.Rim : s.Bridge);
                var t = w.Map[x, y];
                if (t.ObjType != 0 && t.ObjType == s.Mist)
                    TombCourt.Court.Unplace(t);
            }
            var (mx, my) = ((float)r.Average(q => q.X) + 0.5f, (float)r.Average(q => q.Y) + 0.5f);
            IceTomb.Fx(w, mx, my, EffectType.Diffuse, IceTomb.Frost, 2.6f);
            IceTomb.Fx(w, mx, my, EffectType.Diffuse, IceTomb.Ice, 1.4f);
            Floor.Refresh(host);
            s.Next += s.Step;
            if (s.Next < 0 || s.Next >= s.Rows.Count)
            {
                s.Done = true;
                Crucible.ShakeNear(w, new Position(mx, my), 12);
                IceTomb.Fx(w, host.X, host.Y, EffectType.Concentrate, IceTomb.Frost, 4f);
            }
        }
    }

    // The Sealed Door (its invisible host in the door room): a lantern lit for each seal broken
    // (the map's square turned to its lit one), told to everyone; with all three, and someone in
    // the room, its wall opens on the dark and the Frozen Court assembles (IceCourt.Raise).
    internal class SealedDoor : Behavior
    {
        private sealed class Door
        {
            public readonly bool[] Lit = new bool[3];
            public bool Opened;
            public int Cool;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = new Door();

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Door d || d.Opened || host.World == null)
                return;
            d.Cool -= time.ElapsedMsDelta;
            if (d.Cool > 0)
                return;
            d.Cool = 500;
            var w = host.World;
            for (var i = 0; i < 3; i++)
            {
                if (d.Lit[i])
                    continue;
                var seal = IceTomb.Object(host, IceTomb.Seals[i]);
                if (w.Enemies.Values.Any(e => e.ObjectType == seal && !e.Dead))
                    continue;
                d.Lit[i] = true;
                Light(host, i);
                var left = d.Lit.Count(l => !l);
                var msg = left == 0
                    ? $"The {IceTomb.Seals[i]} shatters. All three lanterns burn at the Sealed Door: the way to the Frozen Court is open."
                    : $"The {IceTomb.Seals[i]} shatters, and a lantern lights at the Sealed Door. {left} seal{(left == 1 ? "" : "s")} left.";
                w.ForeachPlayer(p => p.SendInfo(msg));
            }
            if (d.Lit.All(l => l) && w.Players.Values.Any(p => !p.Dead && p.DistTo(host) <= 14))
            {
                d.Opened = true;
                IceTomb.Cue(w, CrucibleCues.WayOpens, 0);
                IceCourt.Raise(w, host);
            }
        }

        private static void Light(Entity host, int i)
        {
            var w = host.World;
            var unlit = IceTomb.Object(host, IceTomb.Lanterns[i]);
            var lit = IceTomb.Object(host, IceTomb.Lanterns[i] + " Lit");
            if (unlit == 0 || lit == 0)
                return;
            int hx = (int)host.X, hy = (int)host.Y;
            for (var y = hy - 14; y <= hy + 14; y++)
                for (var x = hx - 14; x <= hx + 14; x++)
                {
                    if (!w.Map.Contains(x, y) || w.Map[x, y].ObjType != unlit)
                        continue;
                    TombCourt.Court.Place(w, w.Map[x, y], lit, null);
                    var color = i switch { 0 => IceTomb.Steel, 1 => IceTomb.Aurora, _ => IceTomb.Ice };
                    IceTomb.Fx(w, x + 0.5f, y + 0.5f, EffectType.Concentrate, color, 3f);
                    IceTomb.Fx(w, x + 0.5f, y + 0.5f, EffectType.Diffuse, color, 2f);
                    foreach (var p in w.Players.Values)
                        p.UpdateTiles();
                    return;
                }
        }
    }

    // Ysolde's Aurora's Grace: every `everyMs` while it lasts she heals each of the Court still
    // standing (herself too) `share` of their health, unless she's stunned (a Stormglass Jar
    // breaks the channel), drawn as the aurora flowing from her to each.
    internal class CourtGrace : Behavior
    {
        private readonly double _share;
        private readonly int _everyMs;

        public CourtGrace(double share, int everyMs)
        {
            _share = share;
            _everyMs = everyMs;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = _everyMs;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state - time.ElapsedMsDelta;
            if (cool > 0)
            {
                state = cool;
                return;
            }
            state = _everyMs;
            if (host.World == null || host.HasConditionEffect(ConditionEffectIndex.Stunned))
                return;
            foreach (var name in IceTomb.Bosses)
            {
                var type = IceTomb.Object(host, name);
                foreach (var e in host.World.Enemies.Values.Where(e => e.ObjectType == type && !e.Dead && e.DistTo(host) <= 40))
                {
                    var amount = (int)(e.MaxHealth * _share);
                    e.Health = Math.Min(e.MaxHealth, e.Health + amount);
                    host.World.BroadcastIfVisible(new ShowEffect
                    {
                        EffectType = EffectType.Stream,
                        TargetObjectId = host.Id,
                        Pos1 = new Position(e.X, e.Y),
                        Color = new ARGB(IceTomb.Aurora),
                    }, host);
                    host.World.BroadcastIfVisible(new ShowEffect
                    {
                        EffectType = EffectType.Potion,
                        TargetObjectId = e.Id,
                        Color = new ARGB(IceTomb.Aurora),
                    }, e);
                }
            }
        }
    }
}
