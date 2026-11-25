using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// A wider platform for the Empowered Void Entity. Raised in the Elder Void, it tears a black
    /// hole open where it stands and drags the sea of Pure Evil back: the platform surfaces outwards
    /// ring by ring from its old rim (16 tiles) to 23, the evil, the deep and the depths rolling
    /// back before it, violet dust bursting along the front and streams of the void pouring out of
    /// the Entity to it; lightning marks the four new spots its projections will hold, and a
    /// shockwave rolls out to the new rim when it's done. The Entity's middle becomes the
    /// platform's, so it goes back there for its splits, and its projections land on the new
    /// landmarks, 25.5 tiles out (<see cref="ProjectionRange"/>). Tiles and the effects every client
    /// draws (the black hole is the Bow of the Event Horizon's look), so only the world server
    /// redeploys; the platform stays as wide for the rest of the visit.
    /// </summary>
    internal static class VoidArena
    {
        private const string Boss = "Elder LH Void Entity";
        private const string Platform = "Elder LH Void Platform";
        // the sea round the platform, nearest first: walkable and hurting more the further out,
        // then the abyss, which can't be walked
        private static readonly string[] Sea =
        {
            "Elder Pure Evil", "Elder LH Void Pure Evil Deep", "Elder LH Void Pure Evil Depths", "Elder shtrs Pure Evil",
        };
        // the map's invisible landmarks are Pure Evil to walk on: the nearest band
        private const string Landmark = "Elder LH Void Landmark";

        // the new rim and the bands beyond it, in tiles from the middle (the map is 65 across)
        public const double NewRim = 23.5;
        private static readonly double[] SeaEnds = { 26.5, 28.5, 30.5, 31.6 };
        /// <summary>The projections' distance on the regular platform (BehaviorDb.Void.cs) and the widened one.</summary>
        public const double RegularRange = 18.5, WidenedRange = 25.5;

        // the show, in ms from the rising (Empowered.RiseMs is 2500)
        private const int MarkMs = 300, FrontFrom = 700, FrontMs = 2300, FinaleMs = 3300, Bucket = 80;

        private const uint VoidPurple = 0xFF6A2BD9, VoidDeep = 0xFF2A0B5E, VoidPale = 0xFFB98CFF, VoidBlack = 0xFF120626;

        // the worlds whose platform has widened: their projections' range
        private static readonly ConditionalWeakTable<World, object> Widened = new ConditionalWeakTable<World, object>();

        public static bool Grows(BossInfo info) => info?.Boss == Boss;

        /// <summary>How far out the Void Entity throws its projections in its world.</summary>
        public static double ProjectionRange(Entity host) =>
            host?.World != null && Widened.TryGetValue(host.World, out _) ? WidenedRange : RegularRange;

        /// <summary>The platform widens round the Entity rising on it.</summary>
        public static void Grow(Enemy boss)
        {
            var world = boss.World;
            if (world == null)
                return;
            var data = world.GameServer.Resources.GameData;
            if (!data.IdToTileType.TryGetValue(Platform, out var platform))
                return;
            var sea = new ushort[Sea.Length];
            for (var i = 0; i < Sea.Length; i++)
                if (!data.IdToTileType.TryGetValue(Sea[i], out sea[i]))
                    return;

            // how far from the platform each ground is: 0 the platform, 1 to 4 the sea's bands
            // (the landmarks with the nearest), 5 nothing; a square only ever comes nearer
            var rank = new Dictionary<ushort, int> { [platform] = 0 };
            for (var i = 0; i < sea.Length; i++)
                rank[sea[i]] = i + 1;
            foreach (var kv in data.IdToTileType)
                if (kv.Key.StartsWith(Landmark) && !kv.Key.Contains(" Lit "))
                    rank[kv.Value] = 1;
            int RankOf(ushort tile) => tile == 0xFF ? 5 : rank.TryGetValue(tile, out var r) ? r : -1;

            // the platform's middle, as the map's script found it: the middle of its extent
            var map = world.Map;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (var y = 0; y < map.Height; y++)
                for (var x = 0; x < map.Width; x++)
                    if (map[x, y].TileId == platform)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
            if (minX > maxX)
                return;
            int mx = (minX + maxX) / 2, my = (minY + maxY) / 2;
            float cx = mx + 0.5f, cy = my + 0.5f;
            var oldRim = (maxX - minX) / 2.0 + 0.5;
            if (oldRim >= NewRim - 1)
                return; // already wide (raised a second time)
            double D(int x, int y) => Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));

            int BandAt(double d)
            {
                if (d <= NewRim)
                    return 0;
                for (var i = 0; i < SeaEnds.Length; i++)
                    if (d <= SeaEnds[i])
                        return i + 1;
                return 5;
            }
            ushort GroundOf(int band) => band == 0 ? platform : sea[band - 1];
            var reach = SeaEnds[^1];
            int Front(double d) => FrontFrom + (int)(Math.Clamp((d - oldRim) / (reach - oldRim), 0, 1) * FrontMs);

            // what changes: each square whose band comes nearer, when the front reaches it
            var changes = new List<(int X, int Y, ushort Ground, int When)>();
            var rim = new List<(int X, int Y)>();
            for (var y = 1; y < map.Height - 1; y++)
                for (var x = 1; x < map.Width - 1; x++)
                {
                    var t = map[x, y];
                    if (t.ObjType != 0)
                        continue;
                    var now = RankOf(t.TileId);
                    if (now < 0)
                        continue;
                    var d = D(x, y);
                    var band = BandAt(d);
                    if (band >= now)
                        continue;
                    changes.Add((x, y, GroundOf(band), Front(d)));
                    if (band == 0 && d > NewRim - 1)
                        rim.Add((x, y));
                }
            if (changes.Count == 0)
                return;

            Widened.AddOrUpdate(world, new object());
            // its splits and projections are thrown from the platform's middle
            boss.SetSpawnPoint(new Position(cx, cy));

            StaticLogger.Instance.Info($"The Empowered Void Entity's platform widens in {world.IdName}: {changes.Count(c => c.Ground == platform)} " +
                $"squares of platform, {changes.Count} changed, rim {oldRim:0.#} to {NewRim}");

            var steps = new SortedDictionary<int, List<Action<World>>>();
            void At(int when, Action<World> what)
            {
                var b = Math.Max(1, when / Bucket * Bucket);
                if (!steps.TryGetValue(b, out var list))
                    steps[b] = list = new List<Action<World>>();
                list.Add(what);
            }

            // first the pull: the ground shakes, a black hole opens under the Entity, the void
            // drawn into it from the whole platform, and the new rim traced in violet marks that
            // fill as the platform comes for them
            Crucible.Shake(world);
            Show(boss, EffectType.Relic, VoidPurple, new Position(boss.X, boss.Y), new Position(5.5f, 28), FinaleMs);
            Show(boss, EffectType.Concentrate, VoidPale, new Position(boss.X, boss.Y), new Position(boss.X + (float)oldRim, boss.Y));
            At(MarkMs, w =>
            {
                foreach (var (x, y) in rim.Where((q, i) => i % 3 == 0))
                    Show(boss, EffectType.GroundMark, VoidPurple, new Position(x + 0.5f, y + 0.5f), new Position(0.8f, 0),
                        duration: Math.Max(200, Front(D(x, y)) - MarkMs));
            });
            At(MarkMs + 250, w => Show(boss, EffectType.Concentrate, VoidPurple, new Position(boss.X, boss.Y), new Position(boss.X + (float)NewRim, boss.Y)));

            // the squares themselves
            foreach (var (x, y, ground, when) in changes)
                At(when, w =>
                {
                    var t = w.Map[x, y];
                    if (t.ObjType != 0 || t.TileId == ground)
                        return;
                    t.TileId = ground;
                    if (t.ObjId == 0)
                        t.ObjId = w.GetNextEntityId();
                    t.UpdateCount++;
                });

            // the front: violet dust bursting where the platform surfaces, and streams of the
            // void pouring out of the Entity to it
            var rng = new Random(mx * 7919 + my);
            foreach (var g in changes.Where(c => c.Ground == platform).GroupBy(c => c.When / Bucket * Bucket))
            {
                var picks = g.OrderBy(_ => rng.Next()).Take(9).Select(c => (c.X, c.Y)).ToList();
                var flows = picks.Take(2).ToList();
                var colors = picks.Select(_ => rng.Next(4) switch { 0 => VoidPale, 1 => VoidDeep, _ => VoidPurple }).ToList();
                At(g.Key, w =>
                {
                    for (var i = 0; i < picks.Count; i++)
                        Burst(boss, picks[i].X + 0.5f, picks[i].Y + 0.5f, 1.1f, colors[i]);
                });
                At(Math.Max(1, g.Key - 500), w =>
                {
                    foreach (var (x, y) in flows)
                        Show(boss, EffectType.Stream, VoidPurple, new Position(boss.X, boss.Y), new Position(x + 0.5f, y + 0.5f));
                });
            }
            // the sea rolling back: dark ripples on its new edge
            var edge = changes.Where(c => c.Ground == sea[0] && D(c.X, c.Y) < NewRim + 1.2).OrderBy(c => c.When).ToList();
            for (var i = 0; i < edge.Count; i += 3)
            {
                var (x, y, _, when) = edge[i];
                At(when + 60, w => Burst(boss, x + 0.5f, y + 0.5f, 0.9f, VoidBlack));
            }

            // the four new spots: lightning out of the Entity, and a rift that stays open a moment
            for (var k = 0; k < 4; k++)
            {
                var a = k * Math.PI / 2;
                var spot = new Position(cx + (float)(Math.Cos(a) * WidenedRange), cy + (float)(Math.Sin(a) * WidenedRange));
                At(FrontFrom + FrontMs * 2 / 3 + k * 90, w =>
                {
                    Show(boss, EffectType.Lightning, VoidPale, spot, new Position(1.6f, 0));
                    Show(boss, EffectType.GroundMark, VoidDeep, spot, new Position(1.6f, 0), duration: 900);
                    Burst(boss, spot.X, spot.Y, 2.4f, VoidPurple);
                });
            }

            // the platform is whole: the Entity's black hole bursts, a shockwave rolls out to the
            // new rim, rings of violet chasing it
            At(FinaleMs, w =>
            {
                Crucible.Shake(w);
                Show(boss, EffectType.AreaBlast, VoidPurple, new Position(4.5f, 0));
                Show(boss, EffectType.BlastWave, VoidPurple, new Position(boss.X, boss.Y), new Position((float)NewRim, 0));
                BossResurrection.Notify(boss, "The Void widens!", VoidPale);
            });
            for (var k = 0; k < 4; k++)
            {
                var radius = 6f + k * 5.5f;
                var color = k % 2 == 0 ? VoidPurple : VoidPale;
                At(FinaleMs + 80 + k * 110, w =>
                    Show(boss, EffectType.Diffuse, color, new Position(boss.X, boss.Y), new Position(boss.X + radius, boss.Y)));
            }
            At(FinaleMs + 450, w => Crucible.Shake(w));

            foreach (var (when, list) in steps)
                world.StartNewTimer(when, (w, t) =>
                {
                    foreach (var what in list)
                        what(w);
                    // players are sent tiles only as they step onto another square: send this
                    // step's to everyone now, standing still or not
                    foreach (var p in w.Players.Values)
                        p.UpdateTiles();
                });
        }

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
    }
}
