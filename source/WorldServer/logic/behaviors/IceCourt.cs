using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.objects.connection;
using WorldServer.core.resurrection;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // The Frozen Court, raised out of the dark when the Ice Tomb's three seals are broken (the
    // Sealed Door, IceTomb.cs). The door room's wall cracks and bursts open on the dark the tomb
    // keeps round its layout (<RandomLayout margin="56">), a bridge of ice freezes out across it
    // square by square, braziers of blue fire flaring along its edges, and at its end a great round
    // hall rises: its floor of frosted marble spreading from where the bridge meets it, its wall of
    // ice rising behind, six pillars bursting up, cold fire thrown from the middle to twelve
    // braziers, and a snowflake of the aurora flaring out on the floor. Then the Court's three
    // thrones rise, the Heart of Winter comes down into the middle, and Kaldrun, Ysolde and
    // Hrimgar rise in their blocks of ice, one by one. Break the Heart and they wake.
    //
    // The way and the spot are TombCourtPlan's (the Hall of Judgement's): the same causeway and
    // hall, built in the tomb's own ice. With no room for it, the Court gathers in the door room.
    public static class IceCourt
    {
        private const string Wall = "Ice Tomb Wall", Brazier = "Frost Brazier", Marble = "Court Marble", Rim = "Court Rim",
            Sigil = "Court Sigil", Bridge = "Ice Bridge", BridgeRim = "Ice Bridge Rim", Heart = "Court Heart", WinterHeart = "Heart of Winter";
        private static readonly string[] Thrones = { "Warden's Throne", "Oracle's Throne", "Reaver's Throne" };

        // the show, in ms from the opening
        private const int GateMs = 1900, RejoinMs = 2400, CausewayFrom = 2100, MsPerSquare = 120, HallMs = 3800;
        private const double ThroneR = 13.5, BossR = 10;

        internal static void Raise(World world, Entity door)
        {
            var data = world.GameServer.Resources.GameData;
            ushort Obj(string id) => data.IdToObjectType.TryGetValue(id, out var t) ? t : (ushort)0;
            ushort Gnd(string id) => data.IdToTileType.TryGetValue(id, out var t) ? t : (ushort)0;
            var wall = Obj(Wall);
            var sconce = Obj("Ice Tomb Sconce Wall");
            var map = world.Map;
            CourtCell Cell(int x, int y)
            {
                var t = map[x, y];
                if (t.ObjType != 0 && (t.ObjType == wall || t.ObjType == sconce))
                    return CourtCell.Wall;
                if (t.TileId == 0xFF && t.ObjType == 0)
                    return CourtCell.Void;
                return data.Tiles.TryGetValue(t.TileId, out var d) && !d.NoWalk ? CourtCell.Floor : CourtCell.Solid;
            }
            var plan = TombCourtPlan.Make(map.Width, map.Height, Cell, (int)door.X, (int)door.Y, out var why);
            if (plan == null)
            {
                StaticLogger.Instance.Info($"No room for the Frozen Court in {world.IdName}: {why}. It gathers in the Sealed Door's room.");
                Gather(world, new Position(door.X, door.Y), 0, 5, 7);
                return;
            }
            StaticLogger.Instance.Info($"The Frozen Court rises in {world.IdName} at {plan.Centre}: a bridge {plan.EntryS:0} squares long");
            Build(world, plan, door, wall, Obj(Brazier), Gnd(Marble), Gnd(Rim), Gnd(Sigil), Gnd(Bridge), Gnd(BridgeRim));
        }

        private static void Build(World world, TombCourtPlan plan, Entity door, ushort wall, ushort brazier,
            ushort marble, ushort rim, ushort sigil, ushort bridge, ushort bridgeRim)
        {
            var steps = new TombCourt.Steps();
            var c = new Position(plan.Centre.X + 0.5f, plan.Centre.Y + 0.5f);
            int CausewayAt(double s) => CausewayFrom + (int)(Math.Max(0, s) * MsPerSquare);
            var hallFrom = CausewayAt(plan.EntryS);
            int HallAt(double fromEntry) => hallFrom + (int)(fromEntry / (2 * TombCourtPlan.WallR) * HallMs);
            var standing = hallFrom + HallMs;
            var sigilFrom = standing + 2300;

            var final = new HashSet<(int X, int Y)>(plan.HallWalls.Concat(plan.Pillars.SelectMany(p => p)));
            bool WallThere(int x, int y) =>
                final.Contains((x, y)) || !plan.Gate.Contains((x, y)) && world.Map.Contains(x, y) && world.Map[x, y].ObjType == wall;
            string Conn(int x, int y) => ConnectionComputer.GetConnString((dx, dy) => WallThere(x + dx, y + dy));

            // the floor each new square of the hall gets: the rim near its wall, the aurora's snowflake
            ushort HallGround((int X, int Y) q)
            {
                double dx = q.X + 0.5 - c.X, dy = q.Y + 0.5 - c.Y, r = Math.Sqrt(dx * dx + dy * dy);
                if (r > TombCourtPlan.FloorR - 1.6)
                    return rim;
                return Snowflake(dx, dy, r) ? sigil : marble;
            }

            // the wall cracks: frost spreading over it, the door's light streaming to it
            var gateMid = plan.PointAt(0);
            steps.Add(150, w =>
            {
                Crucible.ShakeNear(w, new Position((float)gateMid.X, (float)gateMid.Y), 26);
                foreach (var (x, y) in plan.Gate)
                    IceTomb.Fx(w, x + 0.5f, y + 0.5f, EffectType.GroundMark, IceTomb.Ice, 0.9f, GateMs - 150);
                if (door.World == w)
                    TombCourt.Fx(w, door.X, door.Y, EffectType.Stream, IceTomb.Frost, new Position(door.X, door.Y),
                        new Position((float)gateMid.X, (float)gateMid.Y));
            });
            steps.Add(1000, w => Crucible.ShakeNear(w, new Position((float)gateMid.X, (float)gateMid.Y), 26));
            // it bursts, and the walls beside it close round the opening
            steps.Add(GateMs, w =>
            {
                Crucible.ShakeNear(w, new Position((float)gateMid.X, (float)gateMid.Y), 30);
                foreach (var q in plan.Gate)
                {
                    var t = w.Map[q.X, q.Y];
                    if (t.ObjType != wall)
                        continue;
                    TombCourt.Court.Unplace(t);
                    TombCourt.Court.SetGround(w, t, bridge);
                    IceTomb.Fx(w, q.X + 0.5f, q.Y + 0.5f, EffectType.Diffuse, IceTomb.Frost, 2f);
                }
                foreach (var q in plan.Rejoin)
                    if (w.Map[q.X, q.Y].ObjType == wall)
                        TombCourt.Court.Unplace(w.Map[q.X, q.Y]);
                IceTomb.Fx(w, (float)gateMid.X, (float)gateMid.Y, EffectType.Diffuse, IceTomb.Ice, 5f);
            });
            steps.Add(RejoinMs, w =>
            {
                foreach (var q in plan.Rejoin)
                {
                    var t = w.Map[q.X, q.Y];
                    if (t.ObjType == 0)
                        TombCourt.Court.Place(w, t, wall, Conn(q.X, q.Y));
                }
            });

            // the bridge freezes out over the dark: ice down its middle, rime along its edges
            foreach (var (q, s) in plan.Causeway)
            {
                var (dist, _) = plan.ToLine(q.X + 0.5, q.Y + 0.5);
                var g = dist >= TombCourtPlan.Half - 0.8 ? bridgeRim : bridge;
                steps.Add(CausewayAt(s), w =>
                {
                    var t = w.Map[q.X, q.Y];
                    if (t.TileId == 0xFF && t.ObjType == 0)
                        TombCourt.Court.SetGround(w, t, g);
                });
            }
            // its front: a mark racing ahead over the dark, frost bursting where it freezes
            for (var s = 0.0; s < plan.EntryS; s += 1)
            {
                var (fx, fy, nx, ny) = plan.PointAt(s);
                var (ax, ay, _, _) = plan.PointAt(Math.Min(plan.EntryS, s + 3));
                var k = (int)s;
                steps.Add(CausewayAt(s), w =>
                {
                    TombCourt.Fx(w, (float)fx, (float)fy, EffectType.GroundMark, IceTomb.Ice, new Position((float)ax, (float)ay),
                        new Position(1.5f, 0), duration: 3 * MsPerSquare + 80);
                    var side = k % 2 == 0 ? 1 : -1;
                    IceTomb.Fx(w, (float)(fx + nx * side * 1.6), (float)(fy + ny * side * 1.6), EffectType.Diffuse,
                        k % 3 == 0 ? IceTomb.Ice : IceTomb.Frost, 1.3f);
                    if (k % 6 == 0)
                        Crucible.ShakeNear(w, new Position((float)fx, (float)fy), 14);
                });
            }
            // braziers of blue fire flare along its edges as the front passes
            foreach (var (x, y, s) in plan.CausewayBraziers)
                steps.Add(CausewayAt(s) + 350, w => Ignite(w, x, y, brazier, 1.8f));

            // the hall: its rim traced in frost on the dark, then the floor spreading from the bridge
            steps.Add(Math.Max(1, hallFrom - 1000), w =>
            {
                Crucible.ShakeNear(w, c, 34);
                var i = 0;
                foreach (var (x, y) in plan.HallWalls)
                    if (i++ % 3 == 0)
                    {
                        var at = Math.Sqrt((x + 0.5 - plan.Entry.X) * (x + 0.5 - plan.Entry.X) + (y + 0.5 - plan.Entry.Y) * (y + 0.5 - plan.Entry.Y));
                        TombCourt.Fx(w, x + 0.5f, y + 0.5f, EffectType.GroundMark, IceTomb.Ice, new Position(x + 0.5f, y + 0.5f),
                            new Position(0.9f, 0), duration: HallAt(at) - (hallFrom - 1000) + 150);
                    }
                IceTomb.Fx(w, c.X, c.Y, EffectType.Concentrate, IceTomb.Frost, 14f);
            });
            foreach (var (q, d) in plan.Hall)
            {
                var g = HallGround(q);
                steps.Add(HallAt(d), w =>
                {
                    var t = w.Map[q.X, q.Y];
                    if (t.TileId == 0xFF && t.ObjType == 0)
                        TombCourt.Court.SetGround(w, t, g == sigil ? marble : g); // the snowflake flares last
                });
            }
            var rng = new Random(plan.Centre.X * 7919 + plan.Centre.Y);
            foreach (var grp in plan.Hall.GroupBy(kv => HallAt(kv.Value) / 100))
            {
                var picks = grp.OrderBy(_ => rng.Next()).Take(5).Select(kv => kv.Key).ToList();
                steps.Add(grp.Key * 100, w =>
                {
                    foreach (var (x, y) in picks)
                        IceTomb.Fx(w, x + 0.5f, y + 0.5f, EffectType.Diffuse, rng.Next(3) == 0 ? IceTomb.Ice : IceTomb.Frost, 1.6f);
                });
            }
            var n = 0;
            foreach (var q in plan.HallWalls)
            {
                var at = Math.Sqrt((q.X + 0.5 - plan.Entry.X) * (q.X + 0.5 - plan.Entry.X) + (q.Y + 0.5 - plan.Entry.Y) * (q.Y + 0.5 - plan.Entry.Y));
                var burst = n++ % 2 == 0;
                steps.Add(HallAt(at) + 200, w =>
                {
                    var t = w.Map[q.X, q.Y];
                    if (t.TileId != 0xFF || t.ObjType != 0)
                        return;
                    TombCourt.Court.SetGround(w, t, marble);
                    TombCourt.Court.Place(w, t, wall, Conn(q.X, q.Y));
                    if (burst)
                        IceTomb.Fx(w, q.X + 0.5f, q.Y + 0.5f, EffectType.Diffuse, IceTomb.Frost, 1.4f);
                });
            }
            // the pillars burst up out of the floor, one after another round the hall
            for (var i = 0; i < plan.Pillars.Count; i++)
            {
                var block = plan.Pillars[i];
                if (block.Count == 0)
                    continue;
                var px = (float)block.Average(q => q.X) + 0.5f;
                var py = (float)block.Average(q => q.Y) + 0.5f;
                steps.Add(standing + 200 + i * 260, w =>
                {
                    foreach (var q in block)
                    {
                        var t = w.Map[q.X, q.Y];
                        if (t.ObjType == 0)
                            TombCourt.Court.Place(w, t, wall, Conn(q.X, q.Y));
                    }
                    Crucible.ShakeNear(w, new Position(px, py), 16);
                    IceTomb.Fx(w, px, py, EffectType.Diffuse, IceTomb.Frost, 3f);
                    IceTomb.Fx(w, px, py, EffectType.Diffuse, IceTomb.Ice, 1.6f);
                });
            }
            // cold fire thrown from the middle to each brazier round the hall, in turn
            var b = 0;
            foreach (var (x, y, _) in plan.HallBraziers.OrderBy(h => Math.Abs(Math.IEEERemainder(h.Angle - plan.EntryAngle, Math.PI * 2))))
            {
                var when = standing + 600 + b++ * 150;
                var to = new Position(x + 0.5f, y + 0.5f);
                steps.Add(when, w => TombCourt.FxThrow(w, c, to, IceTomb.Ice, 1));
                steps.Add(when + 1000, w => Ignite(w, x, y, brazier, 2.2f));
            }
            // the aurora's snowflake flares out on the floor from the middle
            foreach (var (q, _) in plan.Hall)
            {
                if (HallGround(q) != sigil)
                    continue;
                var r = Math.Sqrt((q.X + 0.5 - c.X) * (q.X + 0.5 - c.X) + (q.Y + 0.5 - c.Y) * (q.Y + 0.5 - c.Y));
                steps.Add(sigilFrom + (int)(r * 75), w => TombCourt.Court.SetGround(w, w.Map[q.X, q.Y], sigil));
            }
            steps.Add(sigilFrom, w =>
            {
                Crucible.ShakeNear(w, c, 30);
                for (var k = 0; k < 6; k++)
                    TombCourt.Fx(w, c.X, c.Y, EffectType.Diffuse, k % 2 == 0 ? IceTomb.Aurora : IceTomb.Frost, c, new Position(c.X + 3f + k * 2.6f, c.Y));
            });
            // the Court gathers: thrones, the Heart of Winter, the three in their ice
            var across = plan.EntryAngle + Math.PI;
            Gather(world, c, across, BossR, ThroneR, sigilFrom + 1500, steps);
            steps.Add(sigilFrom + 6200, w =>
                w.ForeachPlayer(p => p.SendInfo("The Frozen Court has risen across the dark. Break the Heart of Winter, and the Court wakes.")));
            steps.Run(world);
            world.ForeachPlayer(p => p.SendInfo("The Sealed Door cracks... something rises out of the dark beyond it."));
        }

        // the Court's three thrones round `c`, the Court Heart and the Heart of Winter in the middle,
        // and the three, encased, before their thrones: the first `across` (across from the way in),
        // the others a third of the way round each side
        private static void Gather(World world, Position c, double across, double bossR, double throneR, int from = 0, TombCourt.Steps steps = null)
        {
            var own = steps == null;
            steps ??= new TombCourt.Steps();
            var data = world.GameServer.Resources.GameData;
            for (var i = 0; i < 3; i++)
            {
                var a = across + (i - 1) * Math.PI * 2 / 3;
                var tx = (int)Math.Floor(c.X + Math.Cos(a) * throneR);
                var ty = (int)Math.Floor(c.Y + Math.Sin(a) * throneR);
                var throne = data.IdToObjectType.TryGetValue(Thrones[i], out var tt) ? tt : (ushort)0;
                steps.Add(from + i * 250, w =>
                {
                    if (throne == 0 || !w.Map.Contains(tx, ty))
                        return;
                    var t = w.Map[tx, ty];
                    if (t.ObjType == 0 && t.TileId != 0xFF)
                        TombCourt.Court.Place(w, t, throne, null);
                    IceTomb.Fx(w, tx + 0.5f, ty + 0.5f, EffectType.Diffuse, IceTomb.Frost, 2.2f);
                    Crucible.ShakeNear(w, new Position(tx + 0.5f, ty + 0.5f), 12);
                });
            }
            steps.Add(from + 900, w =>
            {
                Spawn(w, Heart, c);
                Spawn(w, WinterHeart, c);
                IceTomb.Fx(w, c.X, c.Y, EffectType.Concentrate, IceTomb.Frost, 6f);
                IceTomb.Fx(w, c.X, c.Y, EffectType.Diffuse, IceTomb.Ice, 4f);
                Crucible.ShakeNear(w, c, 24);
            });
            for (var i = 0; i < 3; i++)
            {
                var a = across + (i - 1) * Math.PI * 2 / 3;
                var at = new Position(c.X + (float)(Math.Cos(a) * bossR), c.Y + (float)(Math.Sin(a) * bossR));
                var name = IceTomb.Bosses[i];
                steps.Add(from + 1800 + i * 700, w =>
                {
                    Spawn(w, name, at);
                    IceTomb.Fx(w, at.X, at.Y, EffectType.Diffuse, IceTomb.Frost, 3f);
                    IceTomb.Fx(w, at.X, at.Y, EffectType.Concentrate, IceTomb.Ice, 3f);
                    Crucible.ShakeNear(w, at, 20);
                });
            }
            if (own)
                steps.Run(world);
        }

        private static void Spawn(World w, string name, Position at)
        {
            var e = Entity.Resolve(w.GameServer, name);
            if (e == null)
                return;
            e.Move(at.X, at.Y);
            w.EnterWorld(e);
        }

        private static void Ignite(World w, int x, int y, ushort brazier, float size)
        {
            var t = w.Map[x, y];
            if (brazier != 0 && t.ObjType == 0 && t.TileId != 0xFF)
                TombCourt.Court.Place(w, t, brazier, null);
            IceTomb.Fx(w, x + 0.5f, y + 0.5f, EffectType.Diffuse, IceTomb.Ice, size);
            IceTomb.Fx(w, x + 0.5f, y + 0.5f, EffectType.Diffuse, IceTomb.Frost, size * 0.5f);
        }

        // the aurora's snowflake on the hall's floor: six arms out from the middle, a branch off
        // each either side two thirds of the way out, a ring round the middle
        public static bool Snowflake(double dx, double dy, double r)
        {
            if (r < 1.6 || Math.Abs(r - 4.2) < 0.55)
                return true;
            if (r > 14.5)
                return false;
            var a = Math.Atan2(dy, dx);
            for (var k = 0; k < 6; k++)
            {
                var arm = k * Math.PI / 3 + Math.PI / 6;
                double ux = Math.Cos(arm), uy = Math.Sin(arm);
                var along = dx * ux + dy * uy;
                var off = Math.Abs(-dx * uy + dy * ux);
                if (along > 2 && off < 0.65)
                    return true;
                // the branches: from 9 squares out, 4 long, at 60 degrees off the arm
                foreach (var side in new[] { -1, 1 })
                {
                    var ba = arm + side * Math.PI / 3;
                    double bx = dx - ux * 9, by = dy - uy * 9;
                    double bux = Math.Cos(ba), buy = Math.Sin(ba);
                    var bAlong = bx * bux + by * buy;
                    var bOff = Math.Abs(-bx * buy + by * bux);
                    if (bAlong > 0 && bAlong < 4 && bOff < 0.6)
                        return true;
                }
            }
            return false;
        }
    }
}
