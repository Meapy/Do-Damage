using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldServer.core.resurrection
{
    /// <summary>What a square of the map is, to <see cref="TombCourtPlan"/>.</summary>
    public enum CourtCell : byte
    {
        /// <summary>The dark between the rooms: nothing there.</summary>
        Void,
        /// <summary>Ground that can be walked (with or without something standing on it).</summary>
        Floor,
        /// <summary>The tomb's walls.</summary>
        Wall,
        /// <summary>Anything else.</summary>
        Solid,
    }

    /// <summary>
    /// Where the Hall of Judgement goes (<see cref="TombCourt"/>): the boss room's wall opens on its
    /// side towards the open dark, a causeway five squares wide runs from it across the dark, clear
    /// of every other room, and the round hall stands at its end, far enough out to be a walk. Pure
    /// geometry on the map's squares, so the tests can lay out tombs and check every one.
    /// </summary>
    public sealed class TombCourtPlan
    {
        /// <summary>The causeway: the squares whose middle is this near its middle line.</summary>
        public const double Half = 2.2;
        /// <summary>The hall's floor, out to this far from its middle, then its wall, two squares thick.</summary>
        public const double FloorR = 19.5, WallR = 21.9;
        public const double PillarR = 15.0, BrazierR = 17.5;
        /// <summary>Where the gods come to a stop, this far from the middle, before they circle it.</summary>
        public const double ThroneR = 5.5;
        /// <summary>The causeway's walls: the squares this much further from its middle line.</summary>
        public const double Parapet = 1.0;
        /// <summary>The dark kept round the hall's middle: its wall and a little more.</summary>
        public const int SiteClear = 25;
        /// <summary>The causeway is at least this long outside the hall.</summary>
        public const int MinWalk = 18;
        /// <summary>How far the room may be looked for round its anchor.</summary>
        public const int RoomReach = 20;
        // the dark kept round the causeway's middle line (from anything but the boss room), widest
        // first: two squares of dark at least between its edge and another room
        private static readonly int[] Clears = { 5, 4 };
        // how a brazier lights the floor: one level more within each (tools/make_elder.py LIGHT_REACH)
        private static readonly double[] LightReach = { 10, 8.5, 7, 5.5, 4, 2.5 };

        /// <summary>The middle line, from two squares inside the room, through the opened wall, to
        /// the hall's middle; and how far along it each point is (0 at the room's wall).</summary>
        public readonly List<(double X, double Y)> Line = new List<(double, double)>();
        public readonly List<double> LineS = new List<double>();
        public (int X, int Y) Centre;
        public (double X, double Y) Entry;
        /// <summary>How far along the line it meets the hall's wall, and the way to it from the middle.</summary>
        public double EntryS, EntryAngle;
        /// <summary>The room's walls broken open, and those beside them that are joined anew.</summary>
        public readonly HashSet<(int X, int Y)> Gate = new HashSet<(int, int)>();
        public readonly HashSet<(int X, int Y)> Rejoin = new HashSet<(int, int)>();
        /// <summary>The causeway's squares and how far along the line each is.</summary>
        public readonly Dictionary<(int X, int Y), double> Causeway = new Dictionary<(int, int), double>();
        /// <summary>The hall's floor and how far each square is from where the causeway meets it.</summary>
        public readonly Dictionary<(int X, int Y), double> Hall = new Dictionary<(int, int), double>();
        public readonly HashSet<(int X, int Y)> HallWalls = new HashSet<(int, int)>();
        public readonly List<HashSet<(int X, int Y)>> Pillars = new List<HashSet<(int, int)>>();
        public readonly List<(int X, int Y, double S)> CausewayBraziers = new List<(int, int, double)>();
        public readonly List<(int X, int Y, double Angle)> HallBraziers = new List<(int, int, double)>();
        /// <summary>Where each god comes to a stop, round the middle they all circle: across from the
        /// way in first, then either side.</summary>
        public readonly List<(double X, double Y)> Thrones = new List<(double, double)>();
        /// <summary>The walls along both sides of the causeway, and how far along it each is.</summary>
        public readonly Dictionary<(int X, int Y), double> CausewayWalls = new Dictionary<(int, int), double>();
        /// <summary>Every new floor square's light, 0 (dark) to 6 (brightest).</summary>
        public readonly Dictionary<(int X, int Y), int> Light = new Dictionary<(int, int), int>();
        /// <summary>The sun drawn in light on the hall's floor.</summary>
        public readonly HashSet<(int X, int Y)> Sun = new HashSet<(int, int)>();
        public readonly HashSet<(int X, int Y)> Room = new HashSet<(int, int)>();
        public int Clearance;

        public double Length => LineS[^1];

        /// <summary>
        /// The plan for the room round (<paramref name="ax"/>, <paramref name="ay"/>), its anchor, or
        /// null with the reason: the room can't be found, or no causeway leads from it to dark wide
        /// enough for the hall (the fixed map, with no dark kept round it).
        /// </summary>
        public static TombCourtPlan Make(int w, int h, Func<int, int, CourtCell> cell, int ax, int ay, out string why)
        {
            why = null;
            bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;
            CourtCell At(int x, int y) => In(x, y) ? cell(x, y) : CourtCell.Solid;

            // the room: the floor joined to its anchor, and its walls
            var room = new HashSet<(int X, int Y)>();
            var todo = new Queue<(int X, int Y)>();
            if (At(ax, ay) is CourtCell.Void or CourtCell.Wall)
            {
                why = "the anchor stands on no floor";
                return null;
            }
            room.Add((ax, ay));
            todo.Enqueue((ax, ay));
            while (todo.Count > 0)
            {
                var (x, y) = todo.Dequeue();
                foreach (var (nx, ny) in Four(x, y))
                    if (Math.Abs(nx - ax) <= RoomReach && Math.Abs(ny - ay) <= RoomReach
                        && At(nx, ny) is CourtCell.Floor or CourtCell.Solid && room.Add((nx, ny)))
                        todo.Enqueue((nx, ny));
            }
            var walls = new HashSet<(int X, int Y)>();
            foreach (var (x, y) in room)
                foreach (var n in Eight(x, y))
                    if (At(n.X, n.Y) == CourtCell.Wall)
                        walls.Add(n);
            if (room.Count < 100)
            {
                why = $"the room is only {room.Count} squares";
                return null;
            }
            bool Own((int X, int Y) q) => room.Contains(q) || walls.Contains(q);

            // how far each square is from anything but the dark: from anything but the room, and
            // from anything at all (the map's edge counts)
            var other = Distances(w, h, (x, y) => At(x, y) != CourtCell.Void && !Own((x, y)));
            var any = Distances(w, h, (x, y) => At(x, y) != CourtCell.Void);

            // where the wall can open: five walls in a straight line with the room behind them and the
            // dark in front, two squares deep
            var gates = new List<((int X, int Y) Wall, (int X, int Y) Dir)>();
            foreach (var wq in walls)
                foreach (var d in Dirs)
                {
                    var p = (X: d.Y, Y: d.X);
                    var ok = true;
                    for (var k = -2; k <= 2 && ok; k++)
                    {
                        var at = (X: wq.X + k * p.X, Y: wq.Y + k * p.Y);
                        ok = walls.Contains(at) && room.Contains((at.X - d.X, at.Y - d.Y))
                            && At(at.X + d.X, at.Y + d.Y) == CourtCell.Void && At(at.X + 2 * d.X, at.Y + 2 * d.Y) == CourtCell.Void;
                    }
                    if (ok)
                        gates.Add((wq, d));
                }
            if (gates.Count == 0)
            {
                why = "no wall of the room faces the dark";
                return null;
            }

            foreach (var clear in Clears)
            {
                var plan = Search(w, h, At, gates, other, any, clear);
                if (plan == null)
                    continue;
                plan.Clearance = clear;
                foreach (var q in room)
                    plan.Room.Add(q);
                plan.Lay(walls, room, At);
                return plan;
            }
            why = $"no causeway from {gates.Count} places on the room's wall reaches dark wide enough for the hall";
            return null;
        }

        // the way from an opening in the wall to a square with room for the hall round it, at least
        // MinWalk out: straight runs joined by square turns, as the tomb's own corridors are (each
        // turn costs as much as six squares, so there are few), so its walls run straight
        private static TombCourtPlan Search(int w, int h, Func<int, int, CourtCell> at,
            List<((int X, int Y) Wall, (int X, int Y) Dir)> gates, int[,] other, int[,] any, int clear)
        {
            const int Straight = 3, Step = 10, Turn = 60;
            var cost = new int[w * h * 4];
            Array.Fill(cost, int.MaxValue);
            var steps = new int[w * h * 4];
            var run = new int[w * h * 4]; // squares since the last turn
            var from = new int[w * h * 4];
            var gateOf = new int[w * h * 4];
            int State(int x, int y, int d) => (y * w + x) * 4 + d;
            bool Passes(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && at(x, y) == CourtCell.Void && other[x, y] >= clear && any[x, y] >= 3;

            var todo = new PriorityQueue<int, int>();
            for (var g = 0; g < gates.Count; g++)
            {
                // a way leaves the wall straight for three squares
                var (wq, d) = gates[g];
                var ok = true;
                for (var k = 1; k <= Straight && ok; k++)
                {
                    int x = wq.X + k * d.X, y = wq.Y + k * d.Y;
                    ok = x >= 0 && y >= 0 && x < w && y < h && at(x, y) == CourtCell.Void && other[x, y] >= clear;
                }
                if (!ok)
                    continue;
                var s = State(wq.X + Straight * d.X, wq.Y + Straight * d.Y, Array.IndexOf(Dirs, d));
                if (cost[s] == 0)
                    continue;
                cost[s] = 0;
                steps[s] = Straight;
                run[s] = Straight;
                from[s] = -1;
                gateOf[s] = g;
                todo.Enqueue(s, 0);
            }

            var goal = -1;
            var far = MinWalk + (int)Math.Ceiling(WallR) + 2;
            while (todo.TryDequeue(out var s, out var c))
            {
                if (c > cost[s])
                    continue;
                int d = s % 4, x = s / 4 % w, y = s / 4 / w;
                // the last run straight into the hall's middle, so the causeway meets its wall head on
                if (any[x, y] >= SiteClear && steps[s] >= far && run[s] > WallR + 1)
                {
                    goal = s;
                    break;
                }
                for (var nd = 0; nd < 4; nd++)
                {
                    if (Dirs[nd].X == -Dirs[d].X && Dirs[nd].Y == -Dirs[d].Y)
                        continue;
                    int nx = x + Dirs[nd].X, ny = y + Dirs[nd].Y;
                    if (!Passes(nx, ny))
                        continue;
                    var ns = State(nx, ny, nd);
                    var nc = c + Step + (nd != d ? Turn : 0);
                    if (nc >= cost[ns])
                        continue;
                    cost[ns] = nc;
                    steps[ns] = steps[s] + 1;
                    run[ns] = nd == d ? run[s] + 1 : 1;
                    from[ns] = s;
                    gateOf[ns] = gateOf[s];
                    todo.Enqueue(ns, nc);
                }
            }
            if (goal < 0)
                return null;

            var path = new List<(int X, int Y, int D)>();
            for (var s = goal; s >= 0; s = from[s])
                path.Add((s / 4 % w, s / 4 / w, s % 4));
            path.Reverse();
            var (gw, gd) = gates[gateOf[goal]];
            var plan = new TombCourtPlan { Centre = (path[^1].X, path[^1].Y) };
            // the corners: from two squares inside the room, each square where the way turns, the middle
            var corners = new List<(int X, int Y)> { (gw.X - 2 * gd.X, gw.Y - 2 * gd.Y) };
            for (var i = 1; i < path.Count; i++)
                if (path[i].D != path[i - 1].D)
                    corners.Add((path[i - 1].X, path[i - 1].Y));
            corners.Add((path[^1].X, path[^1].Y));
            var len = -2.0;
            foreach (var q in corners)
            {
                var p = (q.X + 0.5, q.Y + 0.5);
                if (plan.Line.Count > 0)
                {
                    var last = plan.Line[^1];
                    len += Math.Abs(p.Item1 - last.X) + Math.Abs(p.Item2 - last.Y);
                }
                plan.Line.Add(p);
                plan.LineS.Add(len);
            }
            return plan;
        }

        // the squares of it all: what breaks, the causeway, the hall, its pillars, braziers and light
        private void Lay(HashSet<(int X, int Y)> walls, HashSet<(int X, int Y)> room, Func<int, int, CourtCell> at)
        {
            double cx = Centre.X + 0.5, cy = Centre.Y + 0.5;
            double R(int x, int y) => Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));

            // where the line meets the hall's wall, walking back from the middle
            Entry = Line[^1];
            EntryS = LineS[^1];
            for (var i = Line.Count - 1; i > 0; i--)
            {
                var (ax, ay) = Line[i - 1];
                var (bx, by) = Line[i];
                double ra = Math.Sqrt((ax - cx) * (ax - cx) + (ay - cy) * (ay - cy));
                if (ra < WallR)
                    continue;
                // the point between them at WallR from the middle
                double lo = 0, hi = 1;
                for (var k = 0; k < 30; k++)
                {
                    var mid = (lo + hi) / 2;
                    double mx = ax + (bx - ax) * mid, my = ay + (by - ay) * mid;
                    if (Math.Sqrt((mx - cx) * (mx - cx) + (my - cy) * (my - cy)) >= WallR)
                        lo = mid;
                    else
                        hi = mid;
                }
                Entry = (ax + (bx - ax) * lo, ay + (by - ay) * lo);
                EntryS = LineS[i - 1] + (LineS[i] - LineS[i - 1]) * lo;
                break;
            }
            EntryAngle = Math.Atan2(Entry.Y - cy, Entry.X - cx);

            // the causeway, and the wall where it leaves the room
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var (x, y) in Line)
            {
                minX = Math.Min(minX, (int)x); maxX = Math.Max(maxX, (int)x);
                minY = Math.Min(minY, (int)y); maxY = Math.Max(maxY, (int)y);
            }
            var carved = new HashSet<(int X, int Y)>();
            for (var x = minX - 4; x <= maxX + 4; x++)
                for (var y = minY - 4; y <= maxY + 4; y++)
                {
                    var (dist, along) = ToLine(x + 0.5, y + 0.5);
                    if (dist > Half + Parapet || room.Contains((x, y)) || walls.Contains((x, y)) && dist > Half)
                        continue;
                    if (dist > Half)
                    {
                        // walls along its sides, out to the hall's own
                        if (at(x, y) == CourtCell.Void && R(x, y) > WallR && along > -1)
                            CausewayWalls[(x, y)] = Math.Max(0, along);
                        continue;
                    }
                    carved.Add((x, y));
                    if (walls.Contains((x, y)))
                        Gate.Add((x, y));
                    else if (at(x, y) == CourtCell.Void && R(x, y) > WallR)
                        Causeway[(x, y)] = Math.Max(0, along);
                }
            foreach (var (x, y) in Gate)
                foreach (var n in Eight(x, y))
                    if (walls.Contains(n) && !Gate.Contains(n))
                        Rejoin.Add(n);

            // the hall: its floor, and its wall but where the causeway comes in
            var reach = (int)Math.Ceiling(WallR) + 1;
            for (var x = Centre.X - reach; x <= Centre.X + reach; x++)
                for (var y = Centre.Y - reach; y <= Centre.Y + reach; y++)
                {
                    var r = R(x, y);
                    if (r > WallR)
                        continue;
                    var fromEntry = Math.Sqrt((x + 0.5 - Entry.X) * (x + 0.5 - Entry.X) + (y + 0.5 - Entry.Y) * (y + 0.5 - Entry.Y));
                    if (r <= FloorR || carved.Contains((x, y)))
                        Hall[(x, y)] = fromEntry;
                    else
                        HallWalls.Add((x, y));
                }

            // where the gods stop, in a triangle round the middle, the first across from the way in
            foreach (var deg in new[] { 180, 60, -60 })
            {
                var a = EntryAngle + deg * Math.PI / 180;
                Thrones.Add((cx + Math.Cos(a) * ThroneR, cy + Math.Sin(a) * ThroneR));
            }

            // six pillars round the rim, between the thrones and either side of the way in
            foreach (var deg in new[] { 30, -30, 90, -90, 150, -150 })
            {
                var a = EntryAngle + deg * Math.PI / 180;
                // two squares by two: every square of it can be seen (a 3x3's middle never can, and
                // shows as a black hole on top)
                var (px, py) = ((int)Math.Round(cx + Math.Cos(a) * PillarR - 1), (int)Math.Round(cy + Math.Sin(a) * PillarR - 1));
                var block = new HashSet<(int X, int Y)>();
                for (var dx = 0; dx <= 1; dx++)
                    for (var dy = 0; dy <= 1; dy++)
                        if (Hall.ContainsKey((px + dx, py + dy)))
                            block.Add((px + dx, py + dy));
                Pillars.Add(block);
            }
            var pillared = Pillars.SelectMany(p => p).ToHashSet();

            // the dark the new walls close in (between the causeway's walls and the hall's, or a room's
            // wall): walled up, so nothing shows as a hole
            var built = new HashSet<(int X, int Y)>(Causeway.Keys.Concat(Hall.Keys).Concat(HallWalls).Concat(CausewayWalls.Keys).Concat(Gate));
            int bx0 = built.Min(q => q.X) - 2, bx1 = built.Max(q => q.X) + 2, by0 = built.Min(q => q.Y) - 2, by1 = built.Max(q => q.Y) + 2;
            bool Open(int x, int y) => x >= bx0 && x <= bx1 && y >= by0 && y <= by1 && at(x, y) == CourtCell.Void && !built.Contains((x, y));
            var outside = new HashSet<(int X, int Y)>();
            var edge = new Queue<(int X, int Y)>();
            for (var x = bx0; x <= bx1; x++)
                foreach (var y in new[] { by0, by1 })
                    if (Open(x, y) && outside.Add((x, y)))
                        edge.Enqueue((x, y));
            for (var y = by0; y <= by1; y++)
                foreach (var x in new[] { bx0, bx1 })
                    if (Open(x, y) && outside.Add((x, y)))
                        edge.Enqueue((x, y));
            while (edge.Count > 0)
            {
                var (x, y) = edge.Dequeue();
                foreach (var n in Four(x, y))
                    if (Open(n.X, n.Y) && outside.Add(n))
                        edge.Enqueue(n);
            }
            for (var x = bx0; x <= bx1; x++)
                for (var y = by0; y <= by1; y++)
                    if (Open(x, y) && !outside.Contains((x, y)) && !room.Contains((x, y)))
                        CausewayWalls[(x, y)] = Math.Max(0, ToLine(x + 0.5, y + 0.5).Along);

            // twelve braziers round the hall, two between each pair of pillars
            for (var k = 0; k < 12; k++)
            {
                var a = EntryAngle + (15 + 30 * k) * Math.PI / 180;
                var q = ((int)Math.Floor(cx + Math.Cos(a) * BrazierR), (int)Math.Floor(cy + Math.Sin(a) * BrazierR));
                if (Hall.ContainsKey(q) && !pillared.Contains(q))
                    HallBraziers.Add((q.Item1, q.Item2, a));
            }
            // and along the causeway, on its edges, one side then the other
            var side = 1;
            for (var s = 4.0; s < EntryS - 3; s += 6.5, side = -side)
            {
                var (px, py, nx, ny) = PointAt(s);
                var q = ((int)Math.Floor(px + nx * side * 2), (int)Math.Floor(py + ny * side * 2));
                if (Causeway.ContainsKey(q))
                    CausewayBraziers.Add((q.Item1, q.Item2, s));
            }

            // the light: each brazier's, as the map's own (the brightest of them), on the dark floor
            var lights = CausewayBraziers.Select(b => (b.X + 0.5, b.Y + 0.5)).Concat(HallBraziers.Select(b => (b.X + 0.5, b.Y + 0.5))).ToList();
            foreach (var q in Causeway.Keys.Concat(Hall.Keys).Concat(Gate))
            {
                var level = 0;
                foreach (var (bx, by) in lights)
                {
                    var d = Math.Sqrt((q.X + 0.5 - bx) * (q.X + 0.5 - bx) + (q.Y + 0.5 - by) * (q.Y + 0.5 - by));
                    level = Math.Max(level, LightReach.Count(r => d <= r));
                }
                Light[q] = level;
            }
            // and the sun the gods circle, in broad bands of light on the dark stone: its bright disc, a
            // halo, a band of shadow, the blazing ring the gods walk round, and a glow fading out
            foreach (var q in Hall.Keys)
            {
                if (pillared.Contains(q))
                    continue;
                var r = R(q.X, q.Y);
                if (r > 9.6)
                    continue;
                var level = r <= 2.6 ? 6 : r <= 3.6 ? 4 : r <= 4.4 ? 1 : r <= 6.9 ? 6 : r <= 7.9 ? 4 : r <= 8.8 ? 2 : 1;
                Light[q] = level;
                if (level >= 4)
                    Sun.Add(q);
            }
        }

        /// <summary>A point on the line <paramref name="s"/> along it, and the way to its left there.</summary>
        public (double X, double Y, double NX, double NY) PointAt(double s)
        {
            for (var i = 1; i < Line.Count; i++)
            {
                if (s > LineS[i] && i < Line.Count - 1)
                    continue;
                var (ax, ay) = Line[i - 1];
                var (bx, by) = Line[i];
                var len = Math.Max(1e-9, LineS[i] - LineS[i - 1]);
                var t = Math.Clamp((s - LineS[i - 1]) / len, 0, 1);
                double dx = (bx - ax) / len, dy = (by - ay) / len;
                return (ax + (bx - ax) * t, ay + (by - ay) * t, -dy, dx);
            }
            var (x, y) = Line[^1];
            return (x, y, 0, 0);
        }

        /// <summary>How far a point is from the line (squares off it, the further of across and along),
        /// and how far along the line the nearest point is.</summary>
        public (double Dist, double Along) ToLine(double px, double py)
        {
            double best = double.MaxValue, along = 0;
            for (var i = 1; i < Line.Count; i++)
            {
                var (ax, ay) = Line[i - 1];
                var (bx, by) = Line[i];
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                var t = len2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
                double qx = ax + dx * t, qy = ay + dy * t;
                // squares off it each way, the further: the runs are straight, so the causeway is
                // square, with square corners
                var d = Math.Max(Math.Abs(px - qx), Math.Abs(py - qy));
                if (d < best)
                {
                    best = d;
                    along = LineS[i - 1] + (LineS[i] - LineS[i - 1]) * t;
                }
            }
            return (best, along);
        }

        private static readonly (int X, int Y)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private static IEnumerable<(int X, int Y)> Four(int x, int y)
        {
            yield return (x + 1, y);
            yield return (x - 1, y);
            yield return (x, y + 1);
            yield return (x, y - 1);
        }

        private static IEnumerable<(int X, int Y)> Eight(int x, int y)
        {
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0)
                        yield return (x + dx, y + dy);
        }

        // each square's distance (8 ways round) to the nearest square that's `solid`, or the map's edge
        private static int[,] Distances(int w, int h, Func<int, int, bool> solid)
        {
            var d = new int[w, h];
            var todo = new Queue<(int X, int Y)>();
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                {
                    d[x, y] = Math.Min(Math.Min(x + 1, y + 1), Math.Min(w - x, h - y));
                    if (solid(x, y))
                    {
                        d[x, y] = 0;
                        todo.Enqueue((x, y));
                    }
                }
            while (todo.Count > 0)
            {
                var (x, y) = todo.Dequeue();
                foreach (var (nx, ny) in Eight(x, y))
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && d[nx, ny] > d[x, y] + 1)
                    {
                        d[nx, ny] = d[x, y] + 1;
                        todo.Enqueue((nx, ny));
                    }
            }
            return d;
        }
    }
}
