using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // The Crucible's floors changing shape through their champions' fights (ArenaForm, on each
    // Heart's "Fight" state in BehaviorDb.Crucible.cs): for each of a Heart's states, what each square
    // round its middle is (`ms` into the form: the forms that once turned still take it; the game asks for 0). A state with no form here (or null) is
    // the floor as the map has it.
    public static class ArenaForms
    {
        // what a form puts on a square: its ground (null: the sky), what stands on it, and whether it
        // is only to be seen (a shard floating off the floor, nobody is thrown onto it)
        public readonly record struct Look(string Ground, string Obj = null, bool Decor = false);

        public static readonly Dictionary<string, Dictionary<string, string>> ByHeart = new()
        {
            // Ferrogrim: the keep, then its towers with the drawbridges raised, an iron gear, a star fort
            ["Bastion Heart"] = new() { ["Lockdown"] = "towers", ["Blades"] = "gear",["Tempest"] = "starfort" },
            // Nerissa: the atoll, then a wheel of isles and channels, arms of coral, a ring round a vortex
            ["Sanctum Heart"] = new() { ["Flood"] = "wheel", ["Whirlpool"] = "spiral", ["Maelstrom"] = "maelstrom" },
            // Pyrrhax: the cross, then a grate over the sky, an anvil, the crucible itself
            ["Foundry Heart"] = new() { ["Meteors"] = "grate", ["Hammer"] = "anvil", ["Broken"] = "crucible" },
            // Chronarch: the orrery, then twelve hours as petals, an hourglass, a coil
            ["Orrery Heart"] = new() { ["Chime"] = "petals", ["Fracture"] = "hourglass", ["Reversal"] = "coil" },
            // Vaelith: each echo's floor in turn, then shattered, then a halo round the void
            ["Apex Heart"] = new()
            {
                ["Decree"] = null, ["Iron"] = "fort", ["Tide"] = "isles", ["Forge"] = "cross",
                ["Hours"] = "orrery", ["Breaks"] = "shards", ["Unbound"] = "halo",
            },
        };

        // The forms below are drawn at the floors' design size and grown this many times onto the
        // floor (each square takes the nearest design square's look; a pillar stands only on the
        // square nearest its own), as tools/build_crucible.py (GROW) grows the floors themselves:
        // change both together.
        public const double Grow = 1.4;

        // The forms that keep moving once they've formed, so a floor changes through a phase too:
        // their squares crack a moment before they go (ArenaForm). Some turn by their own drawing
        // (`ms` into the form: the gear, the whirlpool's arms...); the rest in Spin turn whole round
        // the middle, this many degrees a second. Lockdown's towers (its grates stand on the
        // bridges), the petals (each an hour the chime strikes) and the Apex's fort and cross (their
        // corners would swing past the floor's reach) hold still.
        private static readonly HashSet<string> TurnsItself = new() { "gear", "spiral", "maelstrom", "hourglass", "coil", "orrery" };

        public static readonly Dictionary<string, double> Spin = new()
        {
            ["starfort"] = 3, ["wheel"] = 2.5, ["grate"] = 2.5, ["anvil"] = 3, ["crucible"] = 4,
            ["shards"] = 5, ["halo"] = 3,
        };

        public static bool Moves(string form) => form != null && (TurnsItself.Contains(form) || Spin.ContainsKey(form));

        // rounded half up, as build_crucible.py's near()
        private static int Near(double v) => (int)Math.Floor(v + 0.5);

        // a floor square's offset from its middle, in the design's squares
        public static int Design(int d) => Near(d / Grow);

        public static IEnumerable<string> All => ByHeart.Values.SelectMany(f => f.Values).Where(f => f != null).Distinct();

        internal static string FormOf(Entity host)
        {
            if (!ByHeart.TryGetValue(host.ObjectDesc.IdName, out var forms))
                return null;
            for (var st = host.CurrentState; st != null; st = st.Parent)
                if (forms.TryGetValue(st.Name, out var f))
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

        private static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);

        // ---- Ferrogrim's Iron Bastion ----

        // Lockdown: the yard falls in, leaving a dais in the middle, a tower at each corner, the walls
        // between them and four drawbridges (Rampart raises grates across them)
        private static bool Towers(int dx, int dy)
        {
            var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
            var (minor, major) = (Math.Min(ax, ay), Math.Max(ax, ay));
            if (Hyp(dx, dy) <= 5.5 || Math.Max(Math.Abs(ax - 10), Math.Abs(ay - 10)) <= 3)
                return true;
            return major >= 11 && major <= 13 || minor <= 1 && major <= 11;
        }

        // Grinding Blades: an iron gear, ten teeth and five holes, turning five degrees a second
        private static bool Gear(int dx, int dy, int ms)
        {
            var r = Hyp(dx, dy);
            if (r > 15.5)
                return false;
            var a = Math.Atan2(dy, dx);
            var turn = ms / 1000.0 * Math.PI / 36;
            if (r > 12)
            {
                for (var k = 0; k < 10; k++)
                    if (Ang(a, turn + 2 * Math.PI * k / 10) * r <= 1.7)
                        return true;
                return false;
            }
            for (var k = 0; k < 5; k++)
            {
                var h = turn + 2 * Math.PI * k / 5 + Math.PI / 5;
                if (Hyp(dx - Math.Cos(h) * 8, dy - Math.Sin(h) * 8) <= 2.3)
                    return false;
            }
            return true;
        }

        // Iron Tempest: a star fort, a keep in a dry moat crossed at its gates, the curtain round it,
        // a bastion pointing out from each corner and a ravelin before each gate
        private static bool StarFort(int dx, int dy)
        {
            var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
            var (minor, major) = (Math.Min(ax, ay), Math.Max(ax, ay));
            if (major <= 5)
                return true;
            if (major < 8)
                return minor <= 1;
            if (major <= 10 && minor <= 8)
                return true;
            var (u, v) = ((ax + ay) / Math.Sqrt(2), Math.Abs(ax - ay) / Math.Sqrt(2));
            if (u <= 22 && v <= (22 - u) * 0.62)
                return true;
            return major <= 14 && minor <= 14 - major;
        }

        // ---- Nerissa's Drowned Sanctum ----

        private static bool WheelIsle(int dx, int dy) =>
            Hyp(dx, dy) <= 4.2 || Hyp(Math.Abs(dx) - 9, Math.Abs(dy) - 9) <= 3.6;

        // Rising Tide: a wheel, five dry isles (one in the middle, four on the rim) in the sky, a ring
        // of deep sea round the rim, deep channels out to the isles and shallows between them
        private static bool Wheel(int dx, int dy)
        {
            if (WheelIsle(dx, dy))
                return true;
            var r = Hyp(dx, dy);
            if (r >= 11 && r <= 14.5)
                return true;
            if (r >= 11)
                return false;
            var a = Math.Atan2(dy, dx);
            for (var k = 0; k < 8; k++)
                if (Ang(a, Math.PI / 4 * k) * r <= 1.3)
                    return true;
            return false;
        }

        // the Whirlpool: three arms of coral wheeling round the middle, four degrees a second
        private static bool Spiral(int dx, int dy, int ms, out double off)
        {
            off = 99;
            var r = Hyp(dx, dy);
            if (r <= 5)
                return true;
            if (r > 17)
                return false;
            var a = Math.Atan2(dy, dx);
            var turn = ms / 1000.0 * Math.PI / 45;
            for (var k = 0; k < 3; k++)
                off = Math.Min(off, Ang(a, turn + 2 * Math.PI * k / 3 + 0.21 * (r - 5)) * r);
            return off <= (r <= 12 ? Math.Max(2.4, 0.5 * r) : Math.Max(2.2, 6 - (r - 12) * 0.9));
        }

        // Maelstrom: a ring of coral round the vortex, an islet in its eye, three shallows to it
        // wheeling round (six degrees a second)
        private static bool Maelstrom(int dx, int dy, int ms, out bool bridge)
        {
            bridge = false;
            var r = Hyp(dx, dy);
            var a = Math.Atan2(dy, dx);
            if (r <= 3.6)
                return true;
            if (r >= 8.5)
                return r <= 16 + 0.8 * Math.Sin(3 * a + 0.6) + 0.5 * Math.Sin(5 * a + 2.1);
            var turn = ms / 1000.0 * Math.PI / 30;
            for (var k = 0; k < 3; k++)
                if (Ang(a, turn + 2 * Math.PI * k / 3) * r <= 1.3)
                    return bridge = true;
            return false;
        }

        // ---- Pyrrhax's Molten Foundry ----

        // Meteor Storm: the floor a grate of iron beams over the sky, a plate in the middle, four
        // furnaces floating off it
        private static bool Grate(int dx, int dy, out bool furnace)
        {
            var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
            furnace = Math.Min(ax, ay) <= 2 && Hyp(Math.Min(ax, ay), Math.Max(ax, ay) - 19) <= 2.2;
            if (furnace)
                return true;
            if (Math.Max(ax, ay) > 15 || Hyp(dx, dy) > 19)
                return false;
            return Math.Max(ax, ay) <= 3 || ax % 5 <= 1 || ay % 5 <= 1;
        }

        // Hammer: an anvil, its face round the middle, the horn out west, the heel east and the hardy
        // hole through it
        private static bool Anvil(int dx, int dy)
        {
            var ay = Math.Abs(dy);
            if (dx >= -7 && dx <= 11 && ay <= 7)
                return !(dx >= 6 && dx <= 8 && ay <= 1);
            if (dx > 11 && dx <= 14)
                return ay <= 5;
            if (dx < -7 && dx >= -19)
                return ay <= 7 * Math.Pow((dx + 19) / 12.0, 0.75) + 0.3;
            return false;
        }

        // The Crucible Pours: the forge gone but for its crucible, its spout to the north, plates of
        // the forge drifting round it
        private static bool Crucible(int dx, int dy, out bool plate, out bool spout)
        {
            plate = spout = false;
            if (Hyp(dx, dy) <= 9.5)
                return true;
            if (dy >= -14 && dy <= -9 && Math.Abs(dx) <= 2.2 - (-9 - dy) * 0.3)
                return spout = true;
            for (var k = 0; k < 6; k++)
            {
                var a = Math.PI / 3 * k;
                if (Hyp(dx - Math.Cos(a) * 15, dy - Math.Sin(a) * 15) <= (k % 2 == 0 ? 2.4 : 1.8))
                    return plate = true;
            }
            return false;
        }

        // ---- Chronarch's Orrery ----

        // how far round its hour (Midnight Chime's: twelve o'clock up, clockwise) a square is, from
        // -0.5 to 0.5
        private static double HourOff(double a)
        {
            var hours = (a + Math.PI / 2) / (2 * Math.PI) * 12 + 0.5;
            return hours - Math.Floor(hours) - 0.5;
        }

        // Midnight Chime: the dial split into its twelve hours, petals round the hub, so each hour that
        // burns is a petal of its own
        private static bool Petals(int dx, int dy)
        {
            var r = Hyp(dx, dy);
            if (r <= 5.5)
                return true;
            var off = Math.Abs(HourOff(Math.Atan2(dy, dx)));
            if ((0.5 - off) * Math.PI / 6 * r <= 0.65)
                return false;
            return r <= 15.8 - 3.0 * (2 * off) * (2 * off);
        }

        // Time Fracture: an hourglass turning end over end, three degrees a second
        private static bool Hourglass(int dx, int dy, int ms, out double u, out double v)
        {
            var t = ms / 1000.0 * Math.PI / 60;
            u = dx * Math.Cos(t) + dy * Math.Sin(t);
            v = -dx * Math.Sin(t) + dy * Math.Cos(t);
            if (Math.Abs(v) > 16.5)
                return false;
            if (Math.Abs(v) >= 15)
                return Math.Abs(u) <= 12.5;
            return Math.Abs(u) <= 1.8 + Math.Abs(v) * 0.62;
        }

        // Reversal: a coil winding out from the hub to the dial's rim, turning backwards (twelve
        // degrees a second)
        private static bool Coil(int dx, int dy, int ms, out double along)
        {
            along = -1;
            var r = Hyp(dx, dy);
            if (r <= 5.2)
                return true;
            if (r > 17.5)
                return false;
            if (r >= 15.8)
                return true;
            const double pitch = 6;
            var turn = -ms / 1000.0 * Math.PI / 15;
            var d = (r - 4.0 - pitch / (2 * Math.PI) * (Math.Atan2(dy, dx) - turn)) % pitch;
            if (d < 0)
                d += pitch;
            along = d;
            return d < 3.4;
        }

        // ---- Vaelith's Apex ----

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
            "towers" => Towers(dx, dy),
            "gear" => Gear(dx, dy, ms),
            "starfort" => StarFort(dx, dy),
            "wheel" => Wheel(dx, dy),
            "spiral" => Spiral(dx, dy, ms, out _),
            "maelstrom" => Maelstrom(dx, dy, ms, out _),
            "grate" => Grate(dx, dy, out _),
            "anvil" => Anvil(dx, dy),
            "crucible" => Crucible(dx, dy, out _, out _),
            "petals" => Petals(dx, dy),
            "hourglass" => Hourglass(dx, dy, ms, out _, out _),
            "coil" => Coil(dx, dy, ms, out _),
            "fort" => Fort(dx, dy),
            "isles" => Math.Sqrt(dx * dx + dy * dy) <= 21,
            "cross" => Cross(dx, dy, out _),
            "orrery" => Orrery(dx, dy, ms, out _),
            "shards" => Shards(dx, dy, out _),
            "halo" => Halo(dx, dy, out _),
            _ => false,
        };

        // the whole form `ms` in, every square within `reach` of the middle that isn't sky; floor
        // that can't be walked to from the middle is left to the sky (`dropped` of it: a sliver at
        // the end of an arm, cut off as it turns)
        public static Dictionary<(int, int), Look> Layout(string form, int ms, int reach, out int dropped)
        {
            var all = new Dictionary<(int, int), Look>();
            for (var dy = -reach; dy <= reach; dy++)
                for (var dx = -reach; dx <= reach; dx++)
                {
                    var look = Want(form, dx, dy, ms);
                    if (look.Ground != null)
                        all[(dx, dy)] = look;
                }
            dropped = 0;
            var floor = all.Where(e => !e.Value.Decor).Select(e => e.Key).ToList();
            if (floor.Count == 0)
                return all;
            var start = floor.OrderBy(k => k.Item1 * k.Item1 + k.Item2 * k.Item2).First();
            var seen = new HashSet<(int, int)> { start };
            var todo = new Queue<(int, int)>(seen);
            while (todo.Count > 0)
            {
                var (x, y) = todo.Dequeue();
                foreach (var q in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (all.TryGetValue(q, out var l) && !l.Decor && seen.Add(q))
                        todo.Enqueue(q);
            }
            foreach (var k in floor)
                if (!seen.Contains(k))
                {
                    all.Remove(k);
                    dropped++;
                }
            return all;
        }

        // what the form puts on the floor's square (dx, dy) `ms` into it, grown from its design (Grow)
        // and turned as far as it has spun (Spin)
        public static Look Want(string form, int dx, int dy, int ms)
        {
            var turn = Spin.TryGetValue(form, out var degPerSec) ? degPerSec * ms / 1000.0 * Math.PI / 180 : 0;
            var (cos, sin) = (Math.Cos(turn), Math.Sin(turn));
            // back from the floor to the design as it was drawn, then to its nearest square
            var (ux, uy) = (dx / Grow, dy / Grow);
            var (sx, sy) = (Near(ux * cos + uy * sin), Near(-ux * sin + uy * cos));
            var look = Designed(form, sx, sy, 0 == turn ? ms : 0);
            // a pillar only on the floor's square nearest where its own has turned to
            if (look.Obj != null && (dx != Near((sx * cos - sy * sin) * Grow) || dy != Near((sx * sin + sy * cos) * Grow)))
                look = look with { Obj = null };
            return look;
        }

        private static Look Designed(string form, int dx, int dy, int ms)
        {
            if (!Inside(form, dx, dy, ms))
                return new Look(null);
            var edge = !Inside(form, dx + 1, dy, ms) || !Inside(form, dx - 1, dy, ms) || !Inside(form, dx, dy + 1, ms) || !Inside(form, dx, dy - 1, ms);
            var (ax, ay) = (Math.Abs(dx), Math.Abs(dy));
            var (minor, major) = (Math.Min(ax, ay), Math.Max(ax, ay));
            var r = Math.Sqrt(dx * dx + dy * dy);
            var a = Math.Atan2(dy, dx);
            switch (form)
            {
                case "towers":
                {
                    var obj = ax == 13 && ay == 13 ? "Bastion Pillar" : null;
                    if (edge)
                        return new Look("Bastion Rim", obj);
                    var inlay = Math.Abs(r - 3.5) < 0.5 || Math.Max(Math.Abs(ax - 10), Math.Abs(ay - 10)) == 1 && major < 11;
                    return new Look(inlay ? "Bastion Inlay" : "Bastion Flagstone", obj);
                }
                case "gear":
                {
                    if (edge)
                        return new Look("Bastion Rim");
                    return new Look(Math.Abs(r - 3.5) < 0.5 || Math.Abs(r - 11) < 0.5 ? "Bastion Inlay" : "Bastion Flagstone");
                }
                case "starfort":
                {
                    var obj = ax == 8 && ay == 8 ? "Bastion Pillar" : null;
                    if (edge)
                        return new Look("Bastion Rim", obj);
                    var inlay = major == 9 && minor >= 3 || major == 3 && minor <= 3 || ax == ay && major >= 12;
                    return new Look(inlay ? "Bastion Inlay" : "Bastion Flagstone", obj);
                }
                case "wheel":
                {
                    if (WheelIsle(dx, dy))
                    {
                        var coast = !WheelIsle(dx + 1, dy) || !WheelIsle(dx - 1, dy) || !WheelIsle(dx, dy + 1) || !WheelIsle(dx, dy - 1);
                        return new Look(coast ? "Coral Isle" : "Sanctum Mosaic", ax == 9 && ay == 9 ? "Coral Spire" : null);
                    }
                    if (r >= 11)
                        return new Look(r >= 13.6 ? "Sanctum Rim" : "Deep Tide");
                    // deep channels out to the isles, shallows between them
                    return new Look(Ang(a, Math.PI / 4) < 0.4 || Ang(a, 3 * Math.PI / 4) < 0.4 || Ang(a, -Math.PI / 4) < 0.4 || Ang(a, -3 * Math.PI / 4) < 0.4
                        ? "Deep Tide" : "Sanctum Shallows");
                }
                case "spiral":
                {
                    Spiral(dx, dy, ms, out var off);
                    if (edge)
                        return new Look("Coral Isle");
                    return new Look(off <= 0.6 ? "Sanctum Shallows" : "Sanctum Mosaic");
                }
                case "maelstrom":
                {
                    Maelstrom(dx, dy, ms, out var bridge);
                    if (bridge)
                        return new Look("Sanctum Shallows");
                    if (r <= 3.6)
                        return new Look(edge ? "Coral Isle" : "Sanctum Mosaic");
                    if (edge)
                        return new Look(r < 10 ? "Coral Isle" : "Sanctum Rim");
                    string spire = null;
                    for (var k = 0; k < 6; k++)
                    {
                        var ka = Math.PI / 3 * k;
                        if (dx == (int)Math.Round(Math.Cos(ka) * 12.5) && dy == (int)Math.Round(Math.Sin(ka) * 12.5))
                            spire = "Coral Spire";
                    }
                    var swirl = false;
                    for (var k = 0; k < 6; k++)
                        swirl |= Ang(a, Math.PI / 3 * k + 0.3 * r) * r <= 0.6;
                    return new Look(swirl ? "Sanctum Shallows" : "Sanctum Mosaic", spire);
                }
                case "grate":
                {
                    Grate(dx, dy, out var furnace);
                    if (furnace)
                        return new Look(edge ? "Foundry Rim" : "Foundry Plate", minor == 0 && major == 19 ? "Foundry Brazier" : null, Decor: true);
                    if (major == 15 || r > 18)
                        return new Look("Foundry Rim");
                    return new Look(ax % 5 <= 1 && ay % 5 <= 1 && major > 3 ? "Cooling Slag" : "Foundry Plate");
                }
                case "anvil":
                {
                    var obj = dx == 14 && ay == 5 ? "Foundry Brazier" : null;
                    if (edge)
                        return new Look("Foundry Rim", obj);
                    return new Look(dy == 0 && dx < -8 || ay == 4 && dx >= -5 && dx <= 3 ? "Cooling Slag" : "Foundry Plate", obj);
                }
                case "crucible":
                {
                    Crucible(dx, dy, out var plate, out var spout);
                    if (plate)
                        return new Look(edge ? "Foundry Rim" : "Foundry Plate", Hyp(Math.Abs(dx) - 7.5, Math.Abs(dy) - 13) < 0.8 ? "Foundry Brazier" : null, Decor: true);
                    if (edge)
                        return new Look("Foundry Rim");
                    return new Look(spout || Math.Abs(r - 7) < 0.5 ? "Cooling Slag" : "Foundry Plate");
                }
                case "petals":
                {
                    if (r > 13.5 && edge)
                        return new Look("Orrery Rim");
                    var mark = Math.Abs(HourOff(a)) * Math.PI / 6 * r < 0.6 && r >= 11.5 && r <= 13;
                    return new Look(mark || Math.Abs(r - 4) < 0.5 ? "Dial Numeral" : "Orrery Dial");
                }
                case "hourglass":
                {
                    Hourglass(dx, dy, ms, out var u, out var v);
                    if (Math.Abs(v) >= 15)
                        return new Look("Dial Numeral");
                    var sand = v > 8 && Math.Abs(u) < (v - 8) * 1.1 || Math.Abs(u) <= 0.5 && Math.Abs(v) <= 3;
                    return new Look(sand ? "Dial Numeral" : "Orrery Dial");
                }
                case "coil":
                {
                    Coil(dx, dy, ms, out var along);
                    if (r > 16.5 && edge)
                        return new Look("Orrery Rim");
                    return new Look(along >= 0 && Math.Abs(along - 1.7) < 0.5 || Math.Abs(r - 3) < 0.5 ? "Dial Numeral" : "Orrery Dial");
                }
                case "fort":
                {
                    string obj = null;
                    if (ax == 14 && ay == 14 || minor == 3 && major == 12)
                        obj = "Bastion Pillar";
                    if (edge)
                        return new Look("Bastion Rim", obj);
                    return new Look(major == 10 && minor >= 4 || major == 4 ? "Bastion Inlay" : "Bastion Flagstone", obj);
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

    // A floor changing shape through its champion's fight (BehaviorDb.Crucible.cs: on each Heart's
    // "Fight" state, ArenaForms has the shapes): each of the Heart's states has a form of its own,
    // and as the Heart moves into one, the floor turns into it in a wave out from the middle over
    // `spreadMs`: all the ground that will go cracks at once, then falls into the sky in that wave
    // (`warnMs` after the cracks for the middle, `warnMs` + `spreadMs` for the rim), ground that
    // stays takes the form's stone, ground that comes rises out of it, and pillars rise and fall with
    // their squares. Once formed, a moving form (ArenaForms.Moves) keeps turning until the next, in
    // steps rather than all the while, so the floor holds still between them: every `beatMs` it
    // moves on as far as it has turned since, what goes cracking `warnMs` before and what comes
    // rising as it does. `reach` is at design size (ArenaForms.Grow).
    // Whoever stands where the ground falls, or is left cut off from the rest of the floor
    // once it has formed (on a shard drifting off it, say), is thrown onto the nearest ground that is
    // part of it (`damage`), never under a pillar; with `moveFoes` monsters are set back on it too. Squares a hazard painter
    // has (SweepingArms, TideSweep...) are simply changed: it leaves a square another has changed,
    // and an object something else raised (Rampart's grates) is left standing. When the fight ends,
    // the floor goes back as the map has it.
    internal class ArenaForm : Behavior
    {
        private readonly int _reach, _spreadMs, _warnMs, _damage, _beatMs;
        private readonly bool _moveFoes;

        public ArenaForm(int reach = 22, int spreadMs = 2400, int warnMs = 1600, int damage = 50, bool moveFoes = true, int beatMs = 4000)
        {
            _reach = (int)Math.Ceiling(reach * ArenaForms.Grow);
            _spreadMs = spreadMs;
            _warnMs = warnMs;
            _damage = damage;
            _moveFoes = moveFoes;
            _beatMs = Math.Max(beatMs, warnMs + 500);
        }

        private sealed class S
        {
            public string Form = "";
            public int Now, Began;
            public Dictionary<(int, int), (ushort G, ushort O)> Map0;
            public Dictionary<(int, int), (ushort G, ushort O, bool Decor)> Shown;
            public readonly Dictionary<(int, int), (int At, int CrackAt, ushort G, ushort O, bool Crack, ushort WasO)> Pending = new();
            // the form's layout worked out
            public (string Form, int Ms, Dictionary<(int, int), ArenaForms.Look> Layout) Last;
            // the step a moving form was last set to
            public int Moved;
        }

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
            var form = ArenaForms.FormOf(host);
            if (form != s.Form)
            {
                // a new form: the change spreads out from the middle
                s.Form = form;
                s.Began = s.Now;
                foreach (var key in s.Map0.Keys)
                {
                    var (g, o, decor) = Target(host, s, key);
                    var r = Math.Sqrt(key.Item1 * key.Item1 + key.Item2 * key.Item2);
                    Schedule(s, key, g, o, decor, s.Now + _warnMs + (int)(Math.Min(1, r / _reach) * _spreadMs), s.Now, sky);
                }
                s.Moved = 0;
            }
            else if (ArenaForms.Moves(form))
            {
                // formed, it keeps turning in steps: `_warnMs` before each, it is set to where it
                // will be then, so what goes cracks that long before it falls and what comes rises
                // with the step
                var formed = s.Began + _spreadMs + _warnMs;
                var beat = (s.Now - formed + _warnMs) / _beatMs;
                if (s.Now - formed + _warnMs >= _beatMs && beat != s.Moved)
                {
                    s.Moved = beat;
                    var at = formed + beat * _beatMs;
                    foreach (var key in s.Map0.Keys)
                    {
                        var (g, o, decor) = Target(host, s, key, beat * _beatMs);
                        Schedule(s, key, g, o, decor, at, at - _warnMs, sky);
                    }
                }
            }
            Apply(host, s, time, sky);
        }

        private Dictionary<(int, int), ArenaForms.Look> Layout(S s, int ms)
        {
            if (s.Last.Layout == null || s.Last.Form != s.Form || s.Last.Ms != ms)
                s.Last = (s.Form, ms, ArenaForms.Layout(s.Form, ms, _reach, out _));
            return s.Last.Layout;
        }

        private (ushort G, ushort O, bool Decor) Target(Entity host, S s, (int, int) key, int ms = 0)
        {
            if (s.Form == null)
            {
                var m = s.Map0[key];
                return (m.G, m.O, false);
            }
            var look = Layout(s, ms).TryGetValue(key, out var l) ? l : default;
            return (look.Ground == null ? Ground(host, "Empty") : Ground(host, look.Ground), Obj(host, look.Obj), look.Decor);
        }

        // `at` the square becomes (g, o); going to the sky, it cracks from `crackAt`
        private static void Schedule(S s, (int, int) key, ushort g, ushort o, bool decor, int at, int crackAt, ushort sky)
        {
            var shown = s.Shown[key];
            if (shown.G == g && shown.O == o)
            {
                // already so, or on its way to it (a moving form asks again every step)
                s.Shown[key] = (g, o, decor);
                return;
            }
            var falls = g == sky && shown.G != sky;
            var wasO = s.Pending.TryGetValue(key, out var p) ? p.WasO : shown.O;
            s.Pending[key] = (at, crackAt, g, o, falls, wasO);
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
                if (p.Crack && s.Now >= p.CrackAt && s.Now < p.At)
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
                // an object this form (or the map) put here comes and goes with it; another's stays
                // unless its square falls away
                if (t.ObjType != p.O && (t.ObjType == p.WasO || t.ObjType == 0 || p.G == sky))
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

            // whoever stands where it fell, or (once the form has formed) anywhere cut off from the rest
            // of it, a shard floating off or a corner left behind: thrown onto the nearest ground that
            // is part of it
            var main = MainFloor(s, map, c, sky, crack);
            var settled = s.Now - s.Began > _warnMs + _spreadMs + 400;
            foreach (var p in world.Players.Values.ToList())
            {
                var to = Landing(s, map, c, main, settled, (int)p.X, (int)p.Y, sky, crack);
                if (to != null)
                {
                    p.TeleportPosition(time, to.Value, true);
                    Strike.Land(world, host, to.Value, 0.6, _damage, 0xFFFFD04A);
                    continue;
                }
                // their own square stays, but the sky came in on two sides of the corner of it they
                // stand in: the clients let nobody step from there (Player.isValidPosition), so they
                // are set in its middle, from where they can walk on
                if (s.Shown.ContainsKey(((int)p.X - (int)c.X, (int)p.Y - (int)c.Y)) && Cornered(map, p.X, p.Y, sky))
                    p.TeleportPosition(time, new Position((int)p.X + 0.5f, (int)p.Y + 0.5f), true);
            }
            if (!_moveFoes)
                return;
            foreach (var e in world.Enemies.Values.ToList())
            {
                if (e == host || e.Dead)
                    continue;
                var to = Landing(s, map, c, main, settled, (int)e.X, (int)e.Y, sky, crack);
                if (to != null)
                    e.Move(to.Value.X, to.Value.Y);
            }
        }

        // the clients' Player.isValidPosition for where (x, y) already is: whether the squares beside
        // the quarter of its square it's in (the sky, a FullOccupy object) would stop it there
        internal static bool Cornered(Wmap map, float x, float y, ushort sky)
        {
            bool Full(float fx, float fy)
            {
                var (sx, sy) = ((int)fx, (int)fy);
                if (fx < 0 || fy < 0 || !map.Contains(sx, sy))
                    return true;
                var t = map[sx, sy];
                return t.TileId == sky || t.TileId == 0xFF || t.ObjType != 0 && t.ObjDesc != null && t.ObjDesc.FullOccupy;
            }
            var (xf, yf) = (x - (int)x, y - (int)y);
            if (xf < 0.5f)
                return Full(x - 1, y) || yf < 0.5f && (Full(x, y - 1) || Full(x - 1, y - 1)) || yf > 0.5f && (Full(x, y + 1) || Full(x - 1, y + 1));
            if (xf > 0.5f)
                return Full(x + 1, y) || yf < 0.5f && (Full(x, y - 1) || Full(x + 1, y - 1)) || yf > 0.5f && (Full(x, y + 1) || Full(x + 1, y + 1));
            return yf < 0.5f && Full(x, y - 1) || yf > 0.5f && Full(x, y + 1);
        }

        private static bool Blocked(WmapTile t) =>
            t.ObjType != 0 && t.ObjDesc != null && (t.ObjDesc.FullOccupy || t.ObjDesc.OccupySquare);

        // the ground that can be walked to from the middle as the floor is now: not the sky, not a
        // piece only to be looked at, not under a pillar
        private static HashSet<(int, int)> MainFloor(S s, Wmap map, Position c, ushort sky, ushort crack)
        {
            bool Stands((int, int) k)
            {
                if (!s.Shown.TryGetValue(k, out var v) || v.Decor && !s.Pending.ContainsKey(k))
                    return false;
                var t = map[(int)c.X + k.Item1, (int)c.Y + k.Item2];
                return t.TileId != sky && !Blocked(t);
            }
            var main = new HashSet<(int, int)>();
            (int, int)? start = null;
            var bestD = int.MaxValue;
            foreach (var k in s.Shown.Keys)
            {
                var d = k.Item1 * k.Item1 + k.Item2 * k.Item2;
                if (d < bestD && Stands(k) && map[(int)c.X + k.Item1, (int)c.Y + k.Item2].TileId != crack)
                    (bestD, start) = (d, k);
            }
            if (start == null)
                return main;
            main.Add(start.Value);
            var todo = new Queue<(int, int)>(main);
            while (todo.Count > 0)
            {
                var (x, y) = todo.Dequeue();
                foreach (var q in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (!main.Contains(q) && Stands(q))
                    {
                        main.Add(q);
                        todo.Enqueue(q);
                    }
            }
            return main;
        }

        // for someone at (x, y): if it's sky, or cut off from the main floor once the form has
        // formed, the middle of the nearest square of the main floor that stays
        private static Position? Landing(S s, Wmap map, Position c, HashSet<(int, int)> main, bool settled, int x, int y, ushort sky, ushort crack)
        {
            var key = (x - (int)c.X, y - (int)c.Y);
            if (!s.Shown.ContainsKey(key) || !map.Contains(x, y))
                return null;
            if (map[x, y].TileId != sky && (!settled || main.Contains(key) || main.Count == 0))
                return null;
            (int, int)? best = null;
            var bestD = double.MaxValue;
            foreach (var k in main)
            {
                if (s.Pending.ContainsKey(k) || map[(int)c.X + k.Item1, (int)c.Y + k.Item2].TileId == crack)
                    continue;
                var d = (k.Item1 - key.Item1) * (k.Item1 - key.Item1) + (k.Item2 - key.Item2) * (k.Item2 - key.Item2);
                if (d < bestD)
                    (bestD, best) = (d, k);
            }
            return best == null ? null : new Position((int)c.X + best.Value.Item1 + 0.5f, (int)c.Y + best.Value.Item2 + 0.5f);
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            // the fight over: the floor as the map has it
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
