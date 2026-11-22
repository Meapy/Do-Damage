using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.utils;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// A bigger hall for the Empowered Marble Colossus. Its round room in the Elder Lost Halls
    /// (radius 16) breaks open as it rises and is built again half as wide once more, the way the
    /// Crucible builds its floors out of the sky: the ring of walls cracks apart round from the north,
    /// the marble floor assembles outwards from where it stood, dark at its edge, with the light
    /// flooding out behind it, the new walls rise along the rim as the floor reaches them, and the
    /// Colossus hurls fire to light the braziers on the new rim. All of it is tiles and the effects
    /// every client draws, so only the world server redeploys. The hall stays as big for the rest of
    /// the visit.
    /// </summary>
    internal static class ColossusArena
    {
        private const string Colossus = "Elder LH Marble Colossus";
        // the room's controller stands at its middle (BehaviorDb.LostHallsRooms.cs)
        private const string Spawner = "Elder LH Monster Spawner";
        private const string Wall = "Elder LH Main Wall";
        private const string Brazier = "Elder Brazier";
        // the floor's light, brightest first: the map lights the room from its middle
        private static readonly string[] Light =
        {
            "Elder LH Main Tile", "Elder LH Main Tile Lit 1", "Elder LH Main Tile Lit 2", "Elder LH Main Tile Lit 3",
            "Elder LH Main Tile Lit 4", "Elder LH Main Tile Lit 5", "Elder LH Main Tile Lit 6",
        };
        // where each light ends, as a share of the way to the rim (measured on the map's room)
        private static readonly double[] LightEnds = { 0.36, 0.49, 0.61, 0.72, 0.81, 0.88 };

        // the new hall: an oval this many tiles from the middle each way (the map's edge is 21 north,
        // the halls' next rooms 25 south)
        public const int Sideways = 24, North = 19, South = 21;
        // the old room's rim, where the new floor starts from
        private const double OldRim = 15.5;

        // the show, in ms from the rising (the Colossus rises in 2.5 s, Empowered.RiseMs)
        private const int CrackMs = 800, AssembleFrom = 500, AssembleMs = 2100, LightFrom = 700, LightMs = 2400,
            FireFlight = 2, FinaleMs = 3100, Bucket = 80;

        private const uint Marble = 0xFFF2EEE6, MarbleGold = 0xFFE9C46A, Fire = 0xFFFF8A2A, RoyalFire = 0xFFFFD34F;

        public static bool Grows(BossInfo info) => info?.Boss == Colossus;

        /// <summary>The hall widens round the Colossus rising in it; nothing happens away from its room.</summary>
        public static void Grow(Enemy boss)
        {
            var world = boss.World;
            if (world == null)
                return;
            var data = world.GameServer.Resources.GameData;
            var light = new ushort[Light.Length];
            for (var i = 0; i < Light.Length; i++)
                if (!data.IdToTileType.TryGetValue(Light[i], out light[i]))
                    return;
            if (!data.IdToObjectType.TryGetValue(Wall, out var wall))
                return;
            data.IdToObjectType.TryGetValue(Brazier, out var brazier);
            var spawner = world.Enemies.Values.Cast<Entity>().Concat(world.StaticObjects.Values)
                .Where(e => e.ObjectDesc?.IdName == Spawner && e.DistTo(boss) < 30)
                .OrderBy(e => e.DistTo(boss)).FirstOrDefault();
            if (spawner == null)
                return;
            var map = world.Map;
            int mx = (int)spawner.X, my = (int)spawner.Y;
            double cx = mx + 0.5, cy = my + 0.5;
            var halls = new HashSet<ushort>(data.IdToTileType.Where(kv => kv.Key.StartsWith("Elder LH Hall Tile")).Select(kv => kv.Value));
            bool Inside(int x, int y) => x >= 1 && y >= 1 && x < map.Width - 1 && y < map.Height - 1;
            bool IsVoid(int x, int y) => map[x, y].TileId == 0xFF && map[x, y].ObjType == 0;
            double R(int x, int y) => Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
            double E(int x, int y) => Oval(x + 0.5 - cx, y + 0.5 - cy);

            // The halls' rooms are shuffled each visit (the world's RandomLayout), so another may lie
            // a few tiles off: the hall grows only into the void, a wall's width from anything that
            // isn't its own. Its own: the room's floor, its ring of walls, and its way in as far as
            // the new rim (the floor, and the walls along it).
            var room = Flood(new[] { (mx, my) }, (x, y) => Inside(x, y) && !IsVoid(x, y) && map[x, y].ObjType != wall && R(x, y) < OldRim + 1);
            var ring = Around(room, (x, y) => Inside(x, y) && map[x, y].ObjType == wall && R(x, y) < OldRim + 2);
            var way = Flood(Beside(room).Where(q => !room.Contains(q)),
                (x, y) => Inside(x, y) && halls.Contains(map[x, y].TileId) && map[x, y].ObjType != wall && E(x, y) <= 1);
            var wayWalls = Around(way, (x, y) => Inside(x, y) && map[x, y].ObjType == wall && E(x, y) <= 1 && !ring.Contains((x, y)));
            bool Ours((int, int) q) => room.Contains(q) || ring.Contains(q) || way.Contains(q) || wayWalls.Contains(q);

            // the new floor: void inside the new rim with nothing but its own in the eight squares
            // round it, reached from the old walls
            bool Clear(int x, int y)
            {
                if (x < 2 || y < 2 || x >= map.Width - 2 || y >= map.Height - 2 || !IsVoid(x, y) || E(x, y) > 1)
                    return false;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if (map.Contains(x + dx, y + dy) && !IsVoid(x + dx, y + dy) && !Ours((x + dx, y + dy)))
                            return false;
                return true;
            }
            var floor = Flood(Beside(ring.Concat(wayWalls)), Clear);
            if (floor.Count == 0)
                return; // already wide (raised a second time), or no room to grow

            // a wall of its own breaks where there will be floor all round it; the rest stand
            bool Open((int X, int Y) q)
            {
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var n = (q.X + dx, q.Y + dy);
                        if (!Ours(n) && !floor.Contains(n))
                            return false;
                    }
                return true;
            }
            var breaking = ring.Concat(wayWalls).Where(Open).ToHashSet();
            // and new walls rise on the void round the new floor
            var rising = Around(floor, (x, y) => Inside(x, y) && IsVoid(x, y));

            StaticLogger.Instance.Info($"The Empowered Marble Colossus's hall grows in {world.IdName}: {floor.Count} squares of floor, " +
                $"{breaking.Count} walls broken, {rising.Count} risen, its way in {(way.Count > 0 ? "opened" : "not found")}");

            var plan = new Plan();
            var oldWalls = new List<(int X, int Y, double Angle)>();
            var oldBraziers = new List<(int X, int Y, double Angle)>();
            var reach = Math.Sqrt(Sideways * Sideways + South * South);
            int Front(int x, int y) => At(AssembleFrom, AssembleMs, R(x, y) - OldRim, reach - OldRim);
            double Angle(int x, int y) => Math.Atan2(x + 0.5 - cx, -(y + 0.5 - cy)); // 0 north, ±π south
            foreach (var (x, y) in breaking)
                if (ring.Contains((x, y)))
                    oldWalls.Add((x, y, Angle(x, y)));
                else
                    plan.Break((x, y), Front(x, y));
            foreach (var (x, y) in floor)
                plan.Floor((x, y), Front(x, y));
            foreach (var (x, y) in rising)
                plan.Rise((x, y), Front(x, y) + 150);
            // the light, out to the new rim: the room's floor, the new, and the way in where it opened
            foreach (var q in room.Concat(floor).Concat(breaking).Concat(way.Where(Open)))
            {
                var (x, y) = q;
                if (brazier != 0 && map[x, y].ObjType == brazier)
                    oldBraziers.Add((x, y, Angle(x, y)));
                plan.Relight(q, LightFor(E(x, y), x, y, light), At(LightFrom, LightMs, R(x, y), reach));
            }

            // first the shaking, and the new rim traced on the ground in gold where the floor will
            // reach: a ring of marks filling as the walls crack
            Crucible.Shake(world);
            var rimMarks = rising.Where((q, i) => i % 3 == 0).ToList();
            foreach (var (x, y) in rimMarks)
                Show(boss, EffectType.GroundMark, MarbleGold, new Position(x + 0.5f, y + 0.5f), new Position(0.8f, 0), duration: Front(x, y) + 150);
            // the Colossus gathers the halls' strength into itself
            Show(boss, EffectType.Concentrate, Marble, new Position(boss.X, boss.Y), new Position(boss.X + 6, boss.Y));

            // the ring of walls cracks apart from the north, both ways round to the south, lightning
            // from the Colossus chasing the cracks
            var n = 0;
            foreach (var (x, y, angle) in oldWalls.OrderBy(w => Math.Abs(w.Angle)))
            {
                var when = (int)(Math.Abs(angle) / Math.PI * CrackMs);
                var burst = n % 2 == 0;
                var bolt = n % 7 == 0;
                var color = n++ % 6 == 0 ? MarbleGold : Marble;
                plan.Add(when, w =>
                {
                    var t = w.Map[x, y];
                    if (t.ObjType != wall)
                        return;
                    Unplace(t); // its id stays: the clients are told it's gone
                    t.TileId = light[^1];
                    if (burst)
                        Burst(boss, x + 0.5f, y + 0.5f, 1.5f, color);
                    if (bolt)
                        Show(boss, EffectType.Lightning, MarbleGold, new Position(x + 0.5f, y + 0.5f), new Position(1.4f, 0));
                });
            }

            // the braziers on the old rim go out, and the Colossus hurls their fire to the new one:
            // the same angle, a tile and a half in from the new rim, or as near it as the new floor
            // reaches that way (another room close by); where it doesn't, the brazier stays
            foreach (var (x, y, angle) in oldBraziers)
            {
                (int X, int Y)? spot = null;
                for (var inset = 1.6; spot == null; inset += 0.5)
                {
                    var to = OnRim(cx, cy, angle, inset);
                    if (Math.Sqrt((to.X - cx) * (to.X - cx) + (to.Y - cy) * (to.Y - cy)) < OldRim + 1)
                        break;
                    var q = ((int)Math.Floor(to.X), (int)Math.Floor(to.Y));
                    if (floor.Contains(q) && !rising.Overlaps(Beside(new[] { q })))
                        spot = q;
                }
                if (spot is not (int tx, int ty))
                    continue;
                plan.Add(250, w =>
                {
                    var t = w.Map[x, y];
                    if (t.ObjType != brazier)
                        return;
                    Unplace(t);
                    Burst(boss, x + 0.5f, y + 0.5f, 1.2f, Fire);
                });
                plan.Add(400, w =>
                {
                    w.BroadcastIfVisible(new ShowEffect
                    {
                        EffectType = EffectType.Throw,
                        TargetObjectId = boss.Id,
                        Pos1 = new Position(tx + 0.5f, ty + 0.5f),
                        Pos2 = new Position(boss.X, boss.Y),
                        Color = new ARGB(Fire),
                        Duration = FireFlight,
                    }, boss);
                    Show(boss, EffectType.GroundMark, Fire, new Position(tx + 0.5f, ty + 0.5f), new Position(0.9f, 0), duration: FireFlight * 1000);
                });
                plan.Add(400 + FireFlight * 1000, w =>
                {
                    if (!w.Map.Contains(tx, ty))
                        return;
                    var t = w.Map[tx, ty];
                    if (t.ObjType != 0 || t.TileId == 0xFF)
                        return;
                    Place(w, t, brazier);
                    Burst(boss, tx + 0.5f, ty + 0.5f, 2.2f, Fire);
                    Burst(boss, tx + 0.5f, ty + 0.5f, 1.0f, RoyalFire);
                    Show(boss, EffectType.Concentrate, Fire, new Position(tx + 0.5f, ty + 0.5f), new Position(tx + 2.5f, ty + 0.5f));
                });
            }

            // the floor's front: bursts of marble dust along it as it goes, and streams of it
            // flowing out from the Colossus to where the floor is being laid
            var rng = new Random(mx * 7919 + my);
            foreach (var (when, squares) in plan.FrontByBucket())
            {
                var picks = squares.OrderBy(_ => rng.Next()).Take(8).ToList();
                var flows = picks.Take(2).ToList();
                plan.Add(when, w =>
                {
                    foreach (var (x, y) in picks)
                        Burst(boss, x + 0.5f, y + 0.5f, 1.1f, rng.Next(3) == 0 ? MarbleGold : Marble);
                });
                plan.Add(Math.Max(1, when - 600), w =>
                {
                    foreach (var (x, y) in flows)
                        Show(boss, EffectType.Stream, MarbleGold, new Position(boss.X, boss.Y), new Position(x + 0.5f, y + 0.5f));
                });
            }

            // the new walls rise with a line of sparks racing along the rim
            var m = 0;
            foreach (var (x, y) in rising.OrderBy(q => Front(q.X, q.Y)))
            {
                if (m++ % 2 != 0)
                    continue;
                var color = m % 4 == 1 ? MarbleGold : Marble;
                plan.Add(Front(x, y) + 160, w => Burst(boss, x + 0.5f, y + 0.5f, 0.9f, color));
            }

            // the hall is whole: the Colossus stamps, and a shockwave rolls out to the new walls
            plan.Add(FinaleMs, w =>
            {
                Crucible.Shake(w);
                Show(boss, EffectType.AreaBlast, MarbleGold, new Position(4, 0));
                BossResurrection.Notify(boss, "The halls widen!", MarbleGold);
            });
            for (var k = 0; k < 4; k++)
            {
                var radius = 6f + k * 5.5f;
                var color = k % 2 == 0 ? Marble : MarbleGold;
                plan.Add(FinaleMs + k * 110, w =>
                    Show(boss, EffectType.Diffuse, color, new Position(boss.X, boss.Y), new Position(boss.X + radius, boss.Y)));
            }
            plan.Add(FinaleMs + 450, w => Crucible.Shake(w));

            plan.Run(world, light[^1], wall);
        }

        // something standing on a square, as the map's own are (its description, for the walls'
        // blocking; a new id, so the clients take it for a new object)
        private static void Place(World world, WmapTile t, ushort type)
        {
            t.ObjType = type;
            t.ObjDesc = world.GameServer.Resources.GameData.ObjectDescs[type];
            t.ObjId = world.GetNextEntityId();
            t.UpdateCount++;
        }

        // nothing standing there any more: its id stays, so the clients are told it's gone
        private static void Unplace(WmapTile t)
        {
            t.ObjType = 0;
            t.ObjDesc = null;
            t.UpdateCount++;
        }

        // the squares joined to `from` through squares that pass, side to side (those of `from` that pass too)
        private static HashSet<(int X, int Y)> Flood(IEnumerable<(int X, int Y)> from, Func<int, int, bool> pass)
        {
            var seen = new HashSet<(int X, int Y)>();
            var todo = new Queue<(int X, int Y)>();
            foreach (var q in from)
                if (!seen.Contains(q) && pass(q.X, q.Y) && seen.Add(q))
                    todo.Enqueue(q);
            while (todo.Count > 0)
                foreach (var n in Beside(new[] { todo.Dequeue() }))
                    if (!seen.Contains(n) && pass(n.X, n.Y) && seen.Add(n))
                        todo.Enqueue(n);
            return seen;
        }

        // the four squares beside each of these
        private static IEnumerable<(int X, int Y)> Beside(IEnumerable<(int X, int Y)> squares) =>
            squares.SelectMany(q => new[] { (q.X + 1, q.Y), (q.X - 1, q.Y), (q.X, q.Y + 1), (q.X, q.Y - 1) });

        // the squares among the eight round any of these that pass (not these themselves)
        private static HashSet<(int X, int Y)> Around(HashSet<(int X, int Y)> squares, Func<int, int, bool> pass)
        {
            var found = new HashSet<(int X, int Y)>();
            foreach (var (x, y) in squares)
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if (!squares.Contains((x + dx, y + dy)) && pass(x + dx, y + dy))
                            found.Add((x + dx, y + dy));
            return found;
        }

        // how far out a square is, 1 on the new rim: an oval, wider than it's tall, longer south
        private static double Oval(double fx, double fy)
        {
            var ay = fy < 0 ? North + 0.5 : South + 0.5;
            var ax = Sideways + 0.5;
            return Math.Sqrt(fx * fx / (ax * ax) + fy * fy / (ay * ay));
        }

        // a point at `angle` (0 north) on the new rim, `inset` tiles in
        private static Position OnRim(double cx, double cy, double angle, double inset)
        {
            double dx = Math.Sin(angle), dy = -Math.Cos(angle);
            var scale = 1 / Oval(dx, dy); // the distance to the rim that way
            return Spot(cx, cy, angle, scale - inset);
        }

        private static Position Spot(double cx, double cy, double angle, double dist) =>
            new Position((float)(cx + Math.Sin(angle) * dist), (float)(cy - Math.Cos(angle) * dist));

        // a square's light in the new hall, a little uneven as the map's is
        private static ushort LightFor(double e, int x, int y, ushort[] light)
        {
            var h = (uint)(x * 73856093 ^ y * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            var jitter = ((h >> 8) % 1000 / 1000.0 - 0.5) * 0.08;
            var at = e + jitter;
            for (var i = 0; i < LightEnds.Length; i++)
                if (at < LightEnds[i])
                    return light[i];
            return light[^1];
        }

        private static int At(int from, int span, double along, double length) =>
            from + (int)(Math.Clamp(along / Math.Max(1, length), 0, 1) * span);

        private static void Burst(Enemy boss, float x, float y, float radius, uint color) =>
            Show(boss, EffectType.Diffuse, color, new Position(x, y), new Position(x + radius, y));

        private static void Show(Enemy boss, EffectType type, uint color, Position pos1, Position pos2 = default, int duration = 0) =>
            boss.World?.BroadcastIfVisible(new ShowEffect
            {
                EffectType = type,
                TargetObjectId = boss.Id,
                Pos1 = pos1,
                Pos2 = pos2,
                Color = new ARGB(color),
                Duration = duration,
            }, boss);

        // the squares changed and when, run in buckets of a few timers
        private sealed class Plan
        {
            private readonly Dictionary<(int X, int Y), int> _floor = new Dictionary<(int, int), int>();
            private readonly Dictionary<(int X, int Y), (ushort Light, int When)> _light = new Dictionary<(int, int), (ushort, int)>();
            private readonly Dictionary<(int X, int Y), int> _rise = new Dictionary<(int, int), int>();
            private readonly Dictionary<(int X, int Y), int> _break = new Dictionary<(int, int), int>();
            private readonly List<(int When, Action<World> Do)> _extra = new List<(int, Action<World>)>();

            public bool Empty => _floor.Count == 0 && _rise.Count == 0 && _break.Count == 0;

            public void Floor((int, int) q, int when) => _floor[q] = when;
            public void Rise((int, int) q, int when) => _rise[q] = when;
            public void Break((int, int) q, int when) => _break[q] = when;
            public void Relight((int, int) q, ushort light, int when) => _light[q] = (light, when);
            public void Add(int when, Action<World> what) => _extra.Add((when, what));

            public IEnumerable<(int When, List<(int X, int Y)> Squares)> FrontByBucket() =>
                _floor.GroupBy(kv => kv.Value / Bucket * Bucket).Select(g => (g.Key, g.Select(kv => kv.Key).ToList()));

            public void Run(World world, ushort dark, ushort wall)
            {
                var steps = new SortedDictionary<int, List<Action<World>>>();
                void At(int when, Action<World> what)
                {
                    var b = Math.Max(1, when / Bucket * Bucket);
                    if (!steps.TryGetValue(b, out var list))
                        steps[b] = list = new List<Action<World>>();
                    list.Add(what);
                }

                foreach (var ((x, y), when) in _floor)
                    At(when, w =>
                    {
                        var t = w.Map[x, y];
                        if (t.TileId != 0xFF || t.ObjType != 0)
                            return;
                        t.TileId = dark; // the edge of the floor is dark until the light reaches it
                        t.UpdateCount++;
                    });
                foreach (var ((x, y), (light, when)) in _light)
                {
                    var after = _floor.TryGetValue((x, y), out var built) || _break.TryGetValue((x, y), out built) ? Math.Max(when, built + 400) : when;
                    At(after, w =>
                    {
                        var t = w.Map[x, y];
                        if (t.TileId == light || t.TileId == 0xFF)
                            return;
                        t.TileId = light;
                        t.UpdateCount++;
                    });
                }
                foreach (var ((x, y), when) in _break)
                    At(when, w =>
                    {
                        var t = w.Map[x, y];
                        if (t.ObjType != wall)
                            return;
                        Unplace(t);
                        t.TileId = dark;
                    });
                foreach (var ((x, y), when) in _rise)
                    At(when, w =>
                    {
                        var t = w.Map[x, y];
                        if (t.TileId != 0xFF || t.ObjType != 0)
                            return;
                        t.TileId = dark; // ground under it, as the map's walls have
                        Place(w, t, wall);
                    });
                foreach (var (when, what) in _extra)
                    At(when, what);

                foreach (var (when, list) in steps)
                    world.StartNewTimer(when, (w, t) =>
                    {
                        foreach (var what in list)
                            what(w);
                        // players are sent tiles (and the walls on them) only as they step onto
                        // another square: send this step's to everyone now, standing still or not
                        foreach (var p in w.Players.Values)
                            p.UpdateTiles();
                    });
            }
        }
    }
}
