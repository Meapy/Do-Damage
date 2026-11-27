using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.objects.connection;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// The Hall of Judgement. A god of the Elder Tomb (Bes, Nut or Geb) raised by a Necromantic
    /// Relic won't fight in the tomb where the other two still stand: the first to rise tears the
    /// boss room's wall open on its side towards the dark, and a causeway assembles out of nothing
    /// across it, square by square, braziers bursting alight along its edges, to a great round hall
    /// (41 squares across) that rises at its end: its floor spreading from the causeway, its wall
    /// (two squares thick) and six pillars rising, the causeway walled along both sides behind
    /// its front, fire hurled to twelve braziers round the hall, and a great sun flaring out in
    /// light on its floor. The god walks there, untouchable, the players behind it, and the fight
    /// starts once it stands by the sun; every god raised after walks the same way and joins it,
    /// the three circling the sun together as they circle their room's anchor, so all three can
    /// be fought at once. Those left in the tomb are fought as before.
    ///
    /// The tomb keeps dark round its layout for this (<c>&lt;RandomLayout margin="56"&gt;</c>);
    /// <see cref="TombCourtPlan"/> finds the way and the spot. With no room for it (the fixed map)
    /// the god fights where it rose, as before. The gods orbit a still copy of the tomb's own boss
    /// anchor in the hall's middle, as they orbit the one in their room, and their artifacts follow
    /// them (BehaviorDb.Tomb.cs). Everything is tiles, the map's objects and effects every client
    /// draws, so only the world server redeploys.
    /// </summary>
    public static class TombCourt
    {
        public const string Dungeon = "Elder Tomb of the Ancients";
        private const string AnchorId = "Elder Tomb Boss Anchor";
        private const string WallId = "Elder Tomb Wall";
        private const string BrazierId = "Elder Brazier";
        private const string FloorId = "Elder Sandstone Tile";

        // the show, in ms from the rising
        private const int GateMs = 700, RejoinMs = 1150, CausewayFrom = 950, MsPerSquare = 85, HallMs = 3000,
            FireFlight = 1, Bucket = 100;
        // the god's walk: it waits out its rising, then walks this fast (squares a second), and
        // stops this far short of the hall's middle
        private const int WalkFrom = 2700;
        private const float WalkSpeed = 5f, StopShort = 4f;

        public const uint Sand = 0xFFE3C07A, Gold = 0xFFFFD34F, Fire = 0xFFFF8A2A, Dusk = 0xFFB98A4C;

        private static readonly ConditionalWeakTable<World, Court> Courts = new ConditionalWeakTable<World, Court>();

        private static readonly string[] Leave =
        {
            "Not here. A god is judged in its own hall... FOLLOW ME!",
            "This tomb is too small for a god reborn. To the Hall of Judgement!",
            "Walk the causeway with me, mortals. Your hearts will be weighed!",
        };
        private static readonly string[] Arrive =
        {
            "Here your hearts are weighed... and found WANTING!",
            "The hall remembers its gods. KNEEL!",
            "Judgement begins. None of you walks back across the dark!",
        };

        private static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];

        /// <summary>Whether a raised boss walks to the Hall of Judgement (the Elder Tomb's gods).</summary>
        public static bool Leads(BossInfo info) => info?.Dungeon == Dungeon;

        /// <summary>
        /// A god just raised (<see cref="BossResurrection.RaiseAt"/>): the hall is built the first
        /// time, and the god walks to it. Nothing happens where there's no room for it.
        /// </summary>
        public static void Summon(Enemy boss, BossInfo info)
        {
            var world = boss?.World;
            if (world == null || boss.Empowered == null || !Leads(info))
                return;
            if (!Courts.TryGetValue(world, out var court))
            {
                court = Court.Open(world, boss);
                Courts.Add(world, court);
            }
            court.Walk(boss);
        }

        internal sealed class Court
        {
            private readonly World _world;
            private readonly TombCourtPlan _plan;
            private ushort _wall, _brazier;
            private readonly ushort[] _light = new ushort[7];
            // the gods' anchor in the hall's middle, which they all circle
            private StaticObject _anchor;
            // where each god walking there or standing in the hall comes to a stop
            private readonly Dictionary<Enemy, int> _thrones = new Dictionary<Enemy, int>();
            private DateTime _began;
            // the gods walking there or standing in it
            private readonly List<Enemy> _gods = new List<Enemy>();

            private Court(World world, TombCourtPlan plan)
            {
                _world = world;
                _plan = plan;
            }

            private bool Ready => _plan != null && _anchor != null;

            public static Court Open(World world, Enemy boss)
            {
                var data = world.GameServer.Resources.GameData;
                var court = new Court(world, null);
                if (!data.IdToObjectType.TryGetValue(WallId, out var wall) || !data.IdToObjectType.TryGetValue(BrazierId, out var brazier)
                    || !data.IdToObjectType.TryGetValue(AnchorId, out var anchorType) || !data.IdToTileType.TryGetValue(FloorId, out var floor))
                    return court;
                var light = new ushort[7];
                light[0] = floor;
                for (var i = 1; i <= 6; i++)
                    if (!data.IdToTileType.TryGetValue($"{FloorId} Lit {i}", out light[i]))
                        return court;

                // the room: round the tomb's own anchor (where the gods orbit), nearest the god
                var anchor = world.Enemies.Values.Where(e => e.ObjectType == anchorType).OrderBy(e => e.DistTo(boss)).FirstOrDefault();
                if (anchor == null)
                    return court;
                var map = world.Map;
                CourtCell Cell(int x, int y)
                {
                    var t = map[x, y];
                    if (t.ObjType == wall)
                        return CourtCell.Wall;
                    if (t.TileId == 0xFF && t.ObjType == 0)
                        return CourtCell.Void;
                    return data.Tiles.TryGetValue(t.TileId, out var desc) && !desc.NoWalk ? CourtCell.Floor : CourtCell.Solid;
                }
                var plan = TombCourtPlan.Make(map.Width, map.Height, Cell, (int)anchor.X, (int)anchor.Y, out var why);
                if (plan == null)
                {
                    StaticLogger.Instance.Info($"No Hall of Judgement in {world.IdName}: {why}. The god fights where it rose.");
                    return court;
                }
                StaticLogger.Instance.Info($"The Hall of Judgement rises in {world.IdName} at {plan.Centre}: a causeway {plan.EntryS:0} squares long " +
                    $"({plan.Causeway.Count} squares, clearance {plan.Clearance}), {plan.Gate.Count} walls opened");
                court = new Court(world, plan) { _wall = wall, _brazier = brazier };
                Array.Copy(light, court._light, 7);
                court.Build(boss, anchorType);
                return court;
            }

            // --- the building ---------------------------------------------------------------

            private int CausewayAt(double s) => CausewayFrom + (int)(Math.Max(0, s) * MsPerSquare);
            private int HallFrom => CausewayAt(_plan.EntryS);
            private int HallAt(double fromEntry) => HallFrom + (int)(fromEntry / (2 * TombCourtPlan.WallR) * HallMs);
            private int SunFrom => HallFrom + HallMs + 1500;
            // when the floor under a point s along the line is there to stand on
            private int ReadyAt(double s) => s < _plan.EntryS ? CausewayAt(s) + 250 : HallAt(s - _plan.EntryS) + 250;

            private void Build(Enemy boss, ushort anchorType)
            {
                var plan = _plan;
                var world = _world;
                _began = DateTime.UtcNow;
                var c = new Position(plan.Centre.X + 0.5f, plan.Centre.Y + 0.5f);

                // the gods' anchor in the middle: the tomb's own, still (no behaviour of its own);
                // all three circle it, as they circle the one in their room
                _anchor = new StaticObject(world.GameServer, anchorType, null, true, false, false);
                _anchor.SwitchTo(null);
                _anchor.Move(c.X, c.Y);
                world.EnterWorld(_anchor);

                var steps = new Steps();
                var final = new HashSet<(int X, int Y)>(plan.HallWalls.Concat(plan.Pillars.SelectMany(p => p)).Concat(plan.CausewayWalls.Keys));
                bool WallThere(int x, int y) =>
                    final.Contains((x, y)) || !plan.Gate.Contains((x, y)) && world.Map.Contains(x, y) && world.Map[x, y].ObjType == _wall;
                string Conn(int x, int y) => ConnectionComputer.GetConnString((dx, dy) => WallThere(x + dx, y + dy));
                ushort Lit((int X, int Y) q) => _light[plan.Light.TryGetValue(q, out var l) ? l : 0];

                // the god turns to the wall that will open; the souls rush to it
                var gateMid = plan.PointAt(0);
                steps.Add(200, w =>
                {
                    Crucible.ShakeNear(w, new Position(boss.X, boss.Y), 26);
                    foreach (var (x, y) in plan.Gate)
                        Fx(w, x + 0.5f, y + 0.5f, EffectType.GroundMark, Gold, new Position(x + 0.5f, y + 0.5f), new Position(0.9f, 0), duration: GateMs - 200);
                    Fx(w, (float)gateMid.X, (float)gateMid.Y, EffectType.Stream, BossResurrection.SoulGreen,
                        new Position(boss.X, boss.Y), new Position((float)gateMid.X, (float)gateMid.Y));
                    BossResurrection.Say(boss, "Escaping Souls", "The wall... the wall gives way for our master!", false);
                });

                // the wall bursts open, and the walls beside it close round the opening
                steps.Add(GateMs, w =>
                {
                    Crucible.ShakeNear(w, new Position((float)gateMid.X, (float)gateMid.Y), 30);
                    foreach (var q in plan.Gate)
                    {
                        var t = w.Map[q.X, q.Y];
                        if (t.ObjType != _wall)
                            continue;
                        Unplace(t);
                        SetGround(w, t, Lit(q));
                        Fx(w, q.X + 0.5f, q.Y + 0.5f, EffectType.Diffuse, Sand, new Position(q.X + 0.5f, q.Y + 0.5f), new Position(q.X + 2f, q.Y + 0.5f));
                    }
                    foreach (var q in plan.Rejoin)
                        if (w.Map[q.X, q.Y].ObjType == _wall)
                            Unplace(w.Map[q.X, q.Y]);
                    Fx(w, (float)gateMid.X, (float)gateMid.Y, EffectType.Diffuse, Gold, new Position((float)gateMid.X, (float)gateMid.Y), new Position((float)gateMid.X + 4.5f, (float)gateMid.Y));
                });
                steps.Add(RejoinMs, w =>
                {
                    foreach (var q in plan.Rejoin)
                    {
                        var t = w.Map[q.X, q.Y];
                        if (t.ObjType == 0)
                            Place(w, t, _wall, Conn(q.X, q.Y));
                    }
                });

                // the causeway assembles out of the dark, from the wall to the hall: each square dark
                // stone first, then its light
                foreach (var (q, s) in plan.Causeway)
                {
                    var when = CausewayAt(s);
                    var lit = Lit(q);
                    steps.Add(when, w =>
                    {
                        var t = w.Map[q.X, q.Y];
                        if (t.TileId == 0xFF && t.ObjType == 0)
                            SetGround(w, t, _light[0]);
                    });
                    if (lit != _light[0])
                        steps.Add(when + 450, w => SetGround(w, w.Map[q.X, q.Y], lit));
                }
                // its front: a gold mark racing ahead, sand bursting where it is laid, and the souls'
                // stream from the god to it
                for (var s = 0.0; s < plan.EntryS; s += 1)
                {
                    var (fx, fy, nx, ny) = plan.PointAt(s);
                    var (ax, ay, _, _) = plan.PointAt(Math.Min(plan.EntryS, s + 3));
                    var k = (int)s;
                    steps.Add(CausewayAt(s), w =>
                    {
                        Fx(w, (float)fx, (float)fy, EffectType.GroundMark, Gold, new Position((float)ax, (float)ay), new Position(1.4f, 0), duration: 3 * MsPerSquare + 60);
                        var side = k % 2 == 0 ? 1 : -1;
                        Fx(w, (float)fx, (float)fy, EffectType.Diffuse, k % 3 == 0 ? Gold : Sand,
                            new Position((float)(fx + nx * side * 1.5), (float)(fy + ny * side * 1.5)), new Position((float)(fx + nx * side * 1.5) + 1.2f, (float)(fy + ny * side * 1.5)));
                        if (k % 4 == 0 && boss.World == w)
                            Fx(w, (float)fx, (float)fy, EffectType.Stream, BossResurrection.SoulGreen, new Position(boss.X, boss.Y), new Position((float)fx, (float)fy));
                        if (k % 6 == 0)
                            Crucible.ShakeNear(w, new Position((float)fx, (float)fy), 14);
                    });
                }
                // its walls rise along both sides just behind the front
                var m = 0;
                foreach (var (q, s) in plan.CausewayWalls)
                {
                    var burst = m++ % 3 == 0;
                    steps.Add(CausewayAt(s) + 220, w =>
                    {
                        var t = w.Map[q.X, q.Y];
                        if (t.TileId != 0xFF || t.ObjType != 0)
                            return;
                        SetGround(w, t, _light[0]);
                        Place(w, t, _wall, Conn(q.X, q.Y));
                        if (burst)
                            Fx(w, q.X + 0.5f, q.Y + 0.5f, EffectType.Diffuse, Sand, new Position(q.X + 0.5f, q.Y + 0.5f), new Position(q.X + 1.3f, q.Y + 0.5f));
                    });
                }
                // braziers burst alight along it as the front passes
                foreach (var (x, y, s) in plan.CausewayBraziers)
                    steps.Add(CausewayAt(s) + 300, w => Ignite(w, x, y, 1.8f));

                // the hall: its rim traced in gold on the dark, then the floor spreading from the
                // causeway, the wall rising behind its front, the pillars, and the fire
                steps.Add(Math.Max(1, HallFrom - 900), w =>
                {
                    Crucible.ShakeNear(w, c, 34);
                    var i = 0;
                    foreach (var (x, y) in plan.HallWalls)
                        if (i++ % 3 == 0)
                        {
                            var at = Math.Sqrt((x + 0.5 - plan.Entry.X) * (x + 0.5 - plan.Entry.X) + (y + 0.5 - plan.Entry.Y) * (y + 0.5 - plan.Entry.Y));
                            Fx(w, x + 0.5f, y + 0.5f, EffectType.GroundMark, Gold, new Position(x + 0.5f, y + 0.5f), new Position(0.9f, 0), duration: HallAt(at) - (HallFrom - 900) + 150);
                        }
                    Fx(w, c.X, c.Y, EffectType.Concentrate, Gold, c, new Position(c.X + 13f, c.Y));
                });
                foreach (var (q, d) in plan.Hall)
                {
                    var when = HallAt(d);
                    var lit = plan.Sun.Contains(q) ? _light[1] : Lit(q); // the sun flares last
                    steps.Add(when, w =>
                    {
                        var t = w.Map[q.X, q.Y];
                        if (t.TileId == 0xFF && t.ObjType == 0)
                            SetGround(w, t, _light[0]);
                    });
                    if (lit != _light[0])
                        steps.Add(when + 400, w => SetGround(w, w.Map[q.X, q.Y], lit));
                }
                var rng = new Random(plan.Centre.X * 7919 + plan.Centre.Y);
                foreach (var g in plan.Hall.GroupBy(kv => HallAt(kv.Value) / Bucket))
                {
                    var picks = g.OrderBy(_ => rng.Next()).Take(5).Select(kv => kv.Key).ToList();
                    steps.Add(g.Key * Bucket, w =>
                    {
                        foreach (var (x, y) in picks)
                            Fx(w, x + 0.5f, y + 0.5f, EffectType.Diffuse, rng.Next(3) == 0 ? Gold : Sand, new Position(x + 0.5f, y + 0.5f), new Position(x + 1.6f, y + 0.5f));
                    });
                }
                var n = 0;
                foreach (var q in plan.HallWalls)
                {
                    var at = Math.Sqrt((q.X + 0.5 - plan.Entry.X) * (q.X + 0.5 - plan.Entry.X) + (q.Y + 0.5 - plan.Entry.Y) * (q.Y + 0.5 - plan.Entry.Y));
                    var burst = n++ % 2 == 0;
                    steps.Add(HallAt(at) + 180, w =>
                    {
                        var t = w.Map[q.X, q.Y];
                        if (t.TileId != 0xFF || t.ObjType != 0)
                            return;
                        SetGround(w, t, _light[0]);
                        Place(w, t, _wall, Conn(q.X, q.Y));
                        if (burst)
                            Fx(w, q.X + 0.5f, q.Y + 0.5f, EffectType.Diffuse, Sand, new Position(q.X + 0.5f, q.Y + 0.5f), new Position(q.X + 1.4f, q.Y + 0.5f));
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
                    steps.Add(HallFrom + HallMs + 150 + i * 220, w =>
                    {
                        foreach (var q in block)
                        {
                            var t = w.Map[q.X, q.Y];
                            if (t.ObjType != 0)
                                continue;
                            Place(w, t, _wall, Conn(q.X, q.Y));
                        }
                        Crucible.ShakeNear(w, new Position(px, py), 16);
                        Fx(w, px, py, EffectType.Diffuse, Sand, new Position(px, py), new Position(px + 3f, py));
                        Fx(w, px, py, EffectType.Diffuse, Gold, new Position(px, py), new Position(px + 1.6f, py));
                    });
                }
                // fire hurled from the middle to each brazier round the hall, in turn
                var b = 0;
                foreach (var (x, y, _) in plan.HallBraziers.OrderBy(h => Math.Abs(Math.IEEERemainder(h.Angle - plan.EntryAngle, Math.PI * 2))))
                {
                    var when = HallFrom + HallMs + 400 + b++ * 140;
                    var to = new Position(x + 0.5f, y + 0.5f);
                    steps.Add(when, w => FxThrow(w, c, to, Fire, FireFlight));
                    steps.Add(when + FireFlight * 1000, w => Ignite(w, x, y, 2.2f));
                }
                // the sun flares out on the floor from the middle, and the hall stands
                foreach (var q in plan.Sun)
                {
                    var r = Math.Sqrt((q.X + 0.5 - c.X) * (q.X + 0.5 - c.X) + (q.Y + 0.5 - c.Y) * (q.Y + 0.5 - c.Y));
                    var lit = Lit(q);
                    steps.Add(SunFrom + (int)(r * 70), w => SetGround(w, w.Map[q.X, q.Y], lit));
                }
                steps.Add(SunFrom, w =>
                {
                    Crucible.ShakeNear(w, c, 30);
                    // the sun flares, and the light rolls out from the middle to the rim
                    if (_anchor.World == w)
                    {
                        BossResurrection.Effect(_anchor, EffectType.AreaBlast, Gold, new Position(8, 0));
                        BossResurrection.Notify(_anchor, "The Hall of Judgement", Gold);
                    }
                    for (var k = 0; k < 6; k++)
                        Fx(w, c.X, c.Y, EffectType.Diffuse, k % 2 == 0 ? Gold : Fire, c, new Position(c.X + 3f + k * 3.2f, c.Y));
                });

                steps.Run(world);
                var msg = $"{Empowered.DisplayName(boss)} opens a way across the dark to the Hall of Judgement!";
                world.ForeachPlayer(p => p.SendInfo(msg));
            }

            // --- the walk ----------------------------------------------------------------

            public void Walk(Enemy boss)
            {
                if (_plan == null || _anchor == null || boss.Empowered == null)
                    return;
                var plan = _plan;
                _gods.RemoveAll(g => g.Dead || g.World != _world);
                foreach (var left in _thrones.Keys.Where(g => !_gods.Contains(g)).ToList())
                    _thrones.Remove(left);
                // the first free spot round the middle, across from the way in first (a fourth god,
                // raised by /empower, shares the first); from there it circles the middle with the others
                var throne = Enumerable.Range(0, plan.Thrones.Count).FirstOrDefault(i => !_thrones.ContainsValue(i));
                var (route, lineStart) = Route(boss, plan.Thrones[throne]);
                if (route.Count < 2)
                    return;
                _gods.Add(boss);
                _thrones[boss] = throne;
                boss.Empowered.Marching = true;

                // how far along the route each point is, and where on the line the route joins it
                var along = new List<float> { 0 };
                for (var i = 1; i < route.Count; i++)
                    along.Add(along[^1] + Dist(route[i - 1], route[i]));
                // how far along the route the line's start is (where s is LineS[0])
                var lineFrom = along[lineStart] - (float)plan.LineS[0];
                var total = along[^1];
                var gone = 0f;
                var waited = 0;
                var nextWisp = 0;
                var lit = new HashSet<int>();
                var spoke = false;

                _world.StartNewTimer(0, (w, t) =>
                {
                    if (boss.World != w || boss.Dead)
                        return true;
                    waited += t.ElapsedMsDelta;
                    boss.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 600);
                    if (waited < WalkFrom)
                        return false;
                    if (!spoke)
                    {
                        spoke = true;
                        BossResurrection.Say(boss, Empowered.DisplayName(boss), Pick(Leave), true);
                        BossResurrection.Say(boss, "Escaping Souls", "Follow our master across the dark!", false);
                    }

                    // never ahead of the floor
                    var since = (int)(DateTime.UtcNow - _began).TotalMilliseconds;
                    var step = WalkSpeed * t.ElapsedMsDelta / 1000f;
                    var next = Math.Min(total, gone + step);
                    var s = next - lineFrom;
                    if (s <= 0 || ReadyAt(s + 1.5) <= since)
                        gone = next;
                    var at = PointOn(route, along, gone);
                    boss.Move(at.X, at.Y);

                    // the souls trail it, and the braziers flare as it passes
                    if ((nextWisp -= t.ElapsedMsDelta) <= 0)
                    {
                        nextWisp = 450;
                        Fx(w, at.X, at.Y, EffectType.Diffuse, BossResurrection.SoulGreen, new Position(at.X, at.Y), new Position(at.X + 1.3f, at.Y));
                    }
                    for (var i = 0; i < plan.CausewayBraziers.Count; i++)
                    {
                        var (bx, by, bs) = plan.CausewayBraziers[i];
                        if (gone - lineFrom >= bs && lit.Add(i))
                            Fx(w, bx + 0.5f, by + 0.5f, EffectType.Diffuse, Fire, new Position(bx + 0.5f, by + 0.5f), new Position(bx + 2.2f, by + 0.5f));
                    }

                    if (gone < total)
                        return false;
                    Arrived(boss);
                    return true;
                });
            }

            // the god stands in the hall: judgement
            private void Arrived(Enemy boss)
            {
                boss.Empowered.Marching = false;
                boss.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 900);
                var name = Empowered.DisplayName(boss);
                var c = new Position(_plan.Centre.X + 0.5f, _plan.Centre.Y + 0.5f);
                Crucible.ShakeNear(_world, c, 30);
                BossResurrection.Effect(boss, EffectType.AreaBlast, Gold, new Position(5, 0));
                BossResurrection.Effect(boss, EffectType.Flashing, BossResurrection.SoulViolet, new Position(0.2f, 6));
                BossResurrection.Notify(boss, "JUDGEMENT", 0xFFFF4FD8);
                foreach (var (x, y, _) in _plan.HallBraziers)
                    Fx(_world, x + 0.5f, y + 0.5f, EffectType.Diffuse, Fire, new Position(x + 0.5f, y + 0.5f), new Position(x + 2.6f, y + 0.5f));
                var other = _gods.FirstOrDefault(g => g != boss && !g.Dead && g.World == _world && g.Empowered is { Marching: false });
                BossResurrection.Say(boss, name, other != null ? $"{Empowered.DisplayName(other)}, I stand beside you once more!" : Pick(Arrive), true);
                var msg = $"{name} stands in the Hall of Judgement. The fight begins!";
                _world.ForeachPlayer(p => p.SendInfo(msg));
            }

            // from where the god rose to the causeway (the tomb's floor, round its pillars), the
            // causeway to just short of the hall's middle, then its spot round it
            private (List<Position> Way, int LineStart) Route(Enemy boss, (double X, double Y) throne)
            {
                var plan = _plan;
                var map = _world.Map;
                var (sx, sy) = ((int)boss.X, (int)boss.Y);
                var (gx, gy) = ((int)plan.Line[0].X, (int)plan.Line[0].Y);
                var way = new List<Position>();
                bool Passable(int x, int y) => _world.IsPassable(x + 0.5, y + 0.5) || plan.Gate.Contains((x, y));

                var from = new Dictionary<(int, int), (int, int)> { [(sx, sy)] = (sx, sy) };
                var todo = new Queue<(int X, int Y)>();
                todo.Enqueue((sx, sy));
                while (todo.Count > 0 && from.Count < 40000 && !from.ContainsKey((gx, gy)))
                {
                    var (x, y) = todo.Dequeue();
                    for (var dx = -1; dx <= 1; dx++)
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || !map.Contains(nx, ny) || from.ContainsKey((nx, ny)) || !Passable(nx, ny))
                                continue;
                            if (dx != 0 && dy != 0 && (!Passable(nx, y) || !Passable(x, ny)))
                                continue;
                            from[(nx, ny)] = (x, y);
                            todo.Enqueue((nx, ny));
                        }
                }
                way.Add(new Position(boss.X, boss.Y));
                if (from.ContainsKey((gx, gy)))
                {
                    var cells = new List<(int X, int Y)>();
                    for (var q = (gx, gy); q != (sx, sy); q = from[q])
                        cells.Add(q);
                    cells.Reverse();
                    // pulled straight across the floor
                    var at = (X: sx, Y: sy);
                    for (var i = 0; i < cells.Count;)
                    {
                        var j = cells.Count - 1;
                        while (j > i && !Clear(at, cells[j], Passable))
                            j--;
                        at = cells[j];
                        if (at != (gx, gy))
                            way.Add(new Position(at.X + 0.5f, at.Y + 0.5f));
                        i = j + 1;
                    }
                }
                // the line, from inside the room to short of the middle
                var lineStart = way.Count;
                var stop = plan.Length - StopShort;
                for (var i = 0; i < plan.Line.Count; i++)
                {
                    if (plan.LineS[i] >= stop)
                    {
                        var (x, y, _, _) = plan.PointAt(stop);
                        way.Add(new Position((float)x, (float)y));
                        break;
                    }
                    way.Add(new Position((float)plan.Line[i].X, (float)plan.Line[i].Y));
                }
                // then across the hall to its spot round the middle
                way.Add(new Position((float)throne.X, (float)throne.Y));
                return (way, lineStart);
            }

            private static bool Clear((int X, int Y) a, (int X, int Y) b, Func<int, int, bool> walk)
            {
                double ax = a.X + 0.5, ay = a.Y + 0.5, bx = b.X + 0.5, by = b.Y + 0.5;
                var n = (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / 0.25);
                for (var i = 0; i <= n; i++)
                {
                    var t = n == 0 ? 0 : i / (double)n;
                    double px = ax + (bx - ax) * t, py = ay + (by - ay) * t;
                    // the god is wide: the squares a little to each side too
                    foreach (var (ox, oy) in new[] { (0.0, 0.0), (0.35, 0.35), (-0.35, -0.35), (0.35, -0.35), (-0.35, 0.35) })
                        if (!walk((int)Math.Floor(px + ox), (int)Math.Floor(py + oy)))
                            return false;
                }
                return true;
            }

            private static float Dist(Position a, Position b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

            private static Position PointOn(List<Position> route, List<float> along, float d)
            {
                for (var i = 1; i < route.Count; i++)
                {
                    if (d > along[i] && i < route.Count - 1)
                        continue;
                    var len = Math.Max(1e-6f, along[i] - along[i - 1]);
                    var t = Math.Clamp((d - along[i - 1]) / len, 0, 1);
                    return new Position(route[i - 1].X + (route[i].X - route[i - 1].X) * t, route[i - 1].Y + (route[i].Y - route[i - 1].Y) * t);
                }
                return route[^1];
            }

            // --- squares and effects -------------------------------------------------------

            private void Ignite(World w, int x, int y, float size)
            {
                var t = w.Map[x, y];
                if (t.ObjType == 0 && t.TileId != 0xFF)
                    Place(w, t, _brazier, null);
                Fx(w, x + 0.5f, y + 0.5f, EffectType.Diffuse, Fire, new Position(x + 0.5f, y + 0.5f), new Position(x + 0.5f + size, y + 0.5f));
                Fx(w, x + 0.5f, y + 0.5f, EffectType.Diffuse, Gold, new Position(x + 0.5f, y + 0.5f), new Position(x + 0.5f + size * 0.5f, y + 0.5f));
                Fx(w, x + 0.5f, y + 0.5f, EffectType.Concentrate, Fire, new Position(x + 0.5f, y + 0.5f), new Position(x + 2.5f, y + 0.5f));
            }

            internal static void SetGround(World w, WmapTile t, ushort ground)
            {
                if (!t.Exists || t.TileId == ground)
                    return;
                t.TileId = ground;
                if (w.GameServer.Resources.GameData.Tiles.TryGetValue(ground, out var desc))
                    t.TileDesc = desc;
                t.UpdateCount++;
            }

            // something standing on a square: a new id, so the clients take it for a new object,
            // and joined to its neighbours (a wall) as the finished hall will have them
            internal static void Place(World w, WmapTile t, ushort type, string cfg)
            {
                t.ObjType = type;
                t.ObjDesc = w.GameServer.Resources.GameData.ObjectDescs[type];
                t.ObjCfg = cfg;
                t.ObjId = w.GetNextEntityId();
                t.ForgetObjectDef();
                t.UpdateCount++;
            }

            // nothing standing there any more: its id stays, so the clients are told it's gone
            internal static void Unplace(WmapTile t)
            {
                t.ObjType = 0;
                t.ObjDesc = null;
                t.ForgetObjectDef();
                t.UpdateCount++;
            }
        }

        // an effect at a spot, sent to those near it, centred on themselves where the effect needs
        // an object the client knows (only the spots matter to these)
        internal static void Fx(World w, float x, float y, EffectType type, uint color, Position pos1, Position pos2 = default, int duration = 0)
        {
            foreach (var p in w.Players.Values)
                if ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) < World.CULL_RANGE * World.CULL_RANGE)
                    p.Client?.SendPacket(new ShowEffect
                    {
                        EffectType = type,
                        TargetObjectId = p.Id,
                        Pos1 = pos1,
                        Pos2 = pos2,
                        Color = new ARGB(color),
                        Duration = duration,
                    });
        }

        // something thrown from one spot to another (no object it starts from)
        internal static void FxThrow(World w, Position from, Position to, uint color, int seconds)
        {
            foreach (var p in w.Players.Values)
                if (Math.Min(p.DistTo(to.X, to.Y), p.DistTo(from.X, from.Y)) < World.CULL_RANGE)
                    p.Client?.SendPacket(new ShowEffect
                    {
                        EffectType = EffectType.Throw,
                        TargetObjectId = -1,
                        Pos1 = to,
                        Pos2 = from,
                        Color = new ARGB(color),
                        Duration = seconds,
                    });
        }

        // the steps of the show, run in buckets of a few timers; after each, everyone near is sent
        // the squares changed (players are otherwise sent them only as they step onto another)
        internal sealed class Steps
        {
            private readonly SortedDictionary<int, List<Action<World>>> _at = new SortedDictionary<int, List<Action<World>>>();

            public void Add(int when, Action<World> what)
            {
                var b = Math.Max(1, when / Bucket * Bucket);
                if (!_at.TryGetValue(b, out var list))
                    _at[b] = list = new List<Action<World>>();
                list.Add(what);
            }

            public void Run(World world)
            {
                foreach (var (when, list) in _at)
                    world.StartNewTimer(when, (w, t) =>
                    {
                        foreach (var what in list)
                            what(w);
                        foreach (var p in w.Players.Values)
                            p.UpdateTiles();
                    });
            }
        }
    }
}
