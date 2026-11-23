using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // The Crucible's caches (BehaviorDb.Crucible.cs, tools/build_crucible.py CACHES): while a
    // champion can be hurt, the tower drops a cache from its armoury beside the climbers now and
    // then: a keg, a jar, a cask, a crate, an urn, or rarely a reliquary. Break it (shots, blades,
    // abilities, pets, turrets: anything that hurts an enemy) and its charge launches at the
    // champion, arcing over to it, homing; where it lands the champion takes a share of its health
    // (past its defense, credited to whoever broke the cache) and the cache's affliction, which
    // breaks through the champions' immunities. Each kind is as tough as its payoff is big, so the
    // jars and urns break in a few blows of a blade, the kegs and crates need a group's fire.
    //
    // The Rust client draws them (particles/cache.rs): ShowEffect Relic looks 58 (the cache
    // dropping out of the sky onto pos1), 59 (its charge launched from pos1 at the champion, the
    // target, landing in `duration` ms; pos2.x the blast's reach, `source_id` who broke it) and 60
    // (a cache lost: the floor fell under it, its time ran out, or its champion fell first);
    // `object_type` the kind each time. An older client draws none of them, but the cache, the
    // damage and the champion's afflictions all show as they always would.
    internal static class CrucibleCache
    {
        public const int DropLook = 58, LaunchLook = 59, LostLook = 60;

        // what each kind does to the champion it lands on: its share of the champion's health, its
        // afflictions (forced past the champion's immunities), the word over the champion as it
        // lands, how far its blast is drawn, and whether it shakes the ground
        public sealed class Kind
        {
            public string Id;
            public uint Color;
            public double Share;
            public (ConditionEffectIndex Effect, int Ms)[] Effects;
            public string Callout;
            public float Radius;
            public bool Shake;
            public int Weight;
        }

        // The order is the client's (particles/cache.rs `Kind`) and the object types' health and
        // art are tools/build_crucible.py's CACHES: change all three together.
        public static readonly Kind[] Kinds =
        {
            // black powder: the biggest blow, and nothing else
            new Kind { Id = "Powder Keg", Color = 0xFFFF6A14, Share = 0.035, Effects = Array.Empty<(ConditionEffectIndex, int)>(),
                       Callout = "Blasted!", Radius = 3.2f, Shake = true, Weight = 22 },
            // a storm in a jar: lightning that stops its shots
            new Kind { Id = "Stormglass Jar", Color = 0xFF6AD8FF, Share = 0.01, Effects = new[] { (ConditionEffectIndex.Stunned, 2500) },
                       Callout = "Stunned!", Radius = 2.4f, Weight = 20 },
            // the Sanctum's cold deep: frozen where it stands, then slow
            new Kind { Id = "Frost Cask", Color = 0xFFA8E8FF, Share = 0.012,
                       Effects = new[] { (ConditionEffectIndex.Paralyzed, 3000), (ConditionEffectIndex.Slowed, 6000) },
                       Callout = "Frozen!", Radius = 2.6f, Weight = 18 },
            // the Bastion's spikes: its armour torn open for everyone
            new Kind { Id = "Rending Crate", Color = 0xFFD0D6E0, Share = 0.015, Effects = new[] { (ConditionEffectIndex.ArmorBroken, 6000) },
                       Callout = "Armor Broken!", Radius = 2.6f, Weight = 18 },
            // a hex: it takes a quarter more from everything, and fires half its shots
            new Kind { Id = "Hex Urn", Color = 0xFFC060FF, Share = 0.01,
                       Effects = new[] { (ConditionEffectIndex.Curse, 6000), (ConditionEffectIndex.Dazed, 5000) },
                       Callout = "Hexed!", Radius = 2.4f, Weight = 17 },
            // the Sovereign's own: rare, and all of the above but the frost
            new Kind { Id = "Gilded Reliquary", Color = 0xFFFFD04A, Share = 0.05,
                       Effects = new[] { (ConditionEffectIndex.Stunned, 1500), (ConditionEffectIndex.ArmorBroken, 8000), (ConditionEffectIndex.Curse, 8000) },
                       Callout = "Sovereign's Wrath!", Radius = 3.6f, Shake = true, Weight = 5 },
        };

        // how long a cache takes to fall onto the floor, how long it stands before it crumbles
        // (blinking its last seconds, the client's), and how fast its charge flies
        public const int DropMs = 700, LifeMs = 20000;
        private const float ChargeSpeed = 13; // tiles a second
        private const int MinFlightMs = 450, MaxFlightMs = 1400;
        private const int CalloutAfterMs = 400;

        // how far from its champion a cache still counts as its (the floors reach 33 at most)
        public const double ChampionRange = 45;

        private static readonly string[] Warning = { "Crumbling Floor", "Crucible Warning" };

        public static int KindOf(Entity e) => Array.FindIndex(Kinds, k => k.Id == e.ObjectDesc?.IdName);

        public static bool IsCache(Entity e) => KindOf(e) >= 0;

        // the champion fighting nearest `at`: the Crucible's champions are its Quest enemies
        public static Enemy ChampionNear(World world, float x, float y) =>
            world?.Enemies.Values
                .Where(e => !e.Dead && e.ObjectDesc.Quest && e.AllyOwnerId == -1 && e.DistTo(x, y) <= ChampionRange)
                .OrderBy(e => e.DistTo(x, y))
                .FirstOrDefault();

        // ground a cache may stand on: floor that can be walked, doesn't hurt, isn't about to fall,
        // with nothing standing on it
        public static bool Standable(World world, int x, int y)
        {
            var map = world.Map;
            if (!map.Contains(x, y))
                return false;
            var t = map[x, y];
            if (!t.Exists || t.TileId == 0xFF || t.ObjType != 0)
                return false;
            if (!world.GameServer.Resources.GameData.Tiles.TryGetValue(t.TileId, out var d))
                return false;
            return !d.NoWalk && !d.Damaging && !d.Sink && !Warning.Contains(d.ObjectId);
        }

        // ground still under a standing cache: a hazard washing over it (a wave of molten grate, the
        // tide) leaves it be; only the floor falling from under it takes it
        public static bool Grounded(World world, int x, int y)
        {
            var map = world.Map;
            if (!map.Contains(x, y))
                return false;
            var t = map[x, y];
            return t.Exists && t.TileId != 0xFF && world.GameServer.Resources.GameData.Tiles.TryGetValue(t.TileId, out var d) && !d.NoWalk;
        }

        // ...and away from the floor's edge: its square and the four beside it
        public static bool Placeable(World world, float x, float y)
        {
            var (ix, iy) = ((int)x, (int)y);
            return Standable(world, ix, iy) && Standable(world, ix + 1, iy) && Standable(world, ix - 1, iy) &&
                   Standable(world, ix, iy + 1) && Standable(world, ix, iy - 1);
        }

        public static void Fx(World world, int look, Position at, int kind, int targetId = -1, float radius = 0, int ms = 0, int sourceId = 0) =>
            world?.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = targetId, Pos1 = at, Pos2 = new Position(radius, look),
                Color = new ARGB(Kinds[kind].Color), Duration = ms, ObjectType = (ushort)kind, SourceId = sourceId,
            }, ref at);

        // a cache falls out of the sky onto `at`, and stands there once it lands (if the ground is
        // still there for it)
        public static void Drop(Entity champion, Position at, int kind)
        {
            var world = champion.World;
            Fx(world, DropLook, at, kind, champion.Id, 1.2f, DropMs);
            world.StartNewTimer(DropMs, (w, t) =>
            {
                if (w != champion.World || champion.Dead)
                    return;
                // the floor fell where it was coming down: it breaks on nothing
                if (!Grounded(w, (int)at.X, (int)at.Y))
                {
                    Fx(w, LostLook, at, kind, champion.Id, 1);
                    return;
                }
                var cache = Entity.Resolve(w.GameServer, Kinds[kind].Id);
                if (cache == null)
                    return;
                cache.Move(at.X, at.Y);
                cache.SetLifetime(LifeMs);
                w.EnterWorld(cache);
            });
        }

        // a cache gone without launching: a puff where it stood
        public static void Lose(Entity cache)
        {
            var kind = KindOf(cache);
            if (kind >= 0)
                Fx(cache.World, LostLook, new Position(cache.X, cache.Y), kind, cache.Id, 1);
            cache.World?.LeaveWorld(cache);
        }

        // a cache broken: its charge flies at the champion, and lands
        public static void Launch(Enemy cache)
        {
            var world = cache.World;
            var kind = KindOf(cache);
            if (world == null || kind < 0)
                return;
            var champion = ChampionNear(world, cache.X, cache.Y);
            if (champion == null)
                return;
            var from = new Position(cache.X, cache.Y);
            var breaker = cache.DamageCounter?.LastHitter;
            if (breaker == null || breaker.World != world || breaker.Dead)
                breaker = world.Players.Values.Where(p => !p.Dead).OrderBy(p => p.DistTo(cache.X, cache.Y)).FirstOrDefault();
            if (breaker == null)
                return;
            var k = Kinds[kind];
            var ms = (int)Math.Clamp(champion.DistTo(from.X, from.Y) / ChargeSpeed * 1000, MinFlightMs, MaxFlightMs);
            Fx(world, LaunchLook, from, kind, champion.Id, k.Radius, ms, breaker.Id);
            world.StartNewTimer(ms, (w, t) => Land(w, champion, breaker, kind, ref t));
        }

        private static void Land(World world, Enemy champion, Player breaker, int kind, ref TickTime time)
        {
            if (champion.Dead || champion.World != world)
                return;
            var k = Kinds[kind];
            // untouchable as it landed (a phase turning): the blow glances off, as every other does
            if (champion.HasConditionEffect(ConditionEffectIndex.Invulnerable) || champion.HasConditionEffect(ConditionEffectIndex.Invincible))
                return;
            if (k.Shake)
                Crucible.ShakeNear(world, new Position(champion.X, champion.Y), 20);
            var by = breaker.World == world ? breaker : null;
            foreach (var (effect, ms) in k.Effects)
            {
                champion.ForceConditionEffect(effect, ms);
                champion.DamageCounter?.DebuffedBy(by, ms);
            }
            var dmg = (int)Math.Round(champion.MaxHealth * k.Share);
            if (by != null)
                champion.Damage(by, ref time, dmg, true);
            // its word over the champion a moment after the blow's number, so the two don't overlap
            world.StartNewTimer(CalloutAfterMs, (w, t) =>
            {
                if (!champion.Dead && champion.World == w)
                    w.BroadcastIfVisible(new Notification { ObjectId = champion.Id, Message = k.Callout, Color = new ARGB(k.Color) }, champion);
            });
        }

        // the kind to drop: by weight, not the one just dropped
        public static int Pick(Random rng, int not)
        {
            var total = Kinds.Where((k, i) => i != not).Sum(k => k.Weight);
            var roll = rng.Next(total);
            for (var i = 0; i < Kinds.Length; i++)
            {
                if (i == not)
                    continue;
                roll -= Kinds[i].Weight;
                if (roll < 0)
                    return i;
            }
            return 0;
        }
    }

    // On a champion: every `everyMs` while it can be hurt (the first `firstMs` in), a cache falls
    // beside one climber on its floor, two with three or more of them, at most `MaxStanding` on
    // the floor at once. Each lands within reach of a blade, between 2.5 and 5.5 tiles from its
    // climber, on ground that can be stood on, and not on top of the champion. While the champion
    // is untouchable (a phase turning, Lockdown, a last stand's moment) its clock waits.
    internal class CrucibleCaches : Behavior
    {
        private const int MaxStanding = 3;
        private const double FromChampion = 5, Reach = 34;

        private readonly int _firstMs, _everyMs;

        public CrucibleCaches(int firstMs = 10000, int everyMs = 18000)
        {
            _firstMs = firstMs;
            _everyMs = everyMs;
        }

        private sealed class Clock
        {
            public int Cool;
            public int Last = -1;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) =>
            state = new Clock { Cool = _firstMs };

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Clock c || host.World == null)
                return;
            if (host.HasConditionEffect(ConditionEffectIndex.Invulnerable) || host.HasConditionEffect(ConditionEffectIndex.Invincible))
                return;
            c.Cool -= time.ElapsedMsDelta;
            if (c.Cool > 0)
                return;
            c.Cool = _everyMs;

            var world = host.World;
            var climbers = world.Players.Values.Where(p => !p.Dead && p.DistTo(host) <= Reach).OrderBy(_ => Random.Next()).ToList();
            if (climbers.Count == 0)
                return;
            var standing = world.Enemies.Values.Count(e => !e.Dead && CrucibleCache.IsCache(e) && e.DistTo(host) <= Reach);
            var n = Math.Min(climbers.Count >= 3 ? 2 : 1, MaxStanding - standing);
            var placed = new List<Position>();
            for (var i = 0; i < n; i++)
            {
                var near = climbers[i % climbers.Count];
                var spot = Spot(host, near, placed);
                if (spot == null)
                    continue;
                placed.Add(spot.Value);
                var kind = CrucibleCache.Pick(Random, c.Last);
                c.Last = kind;
                CrucibleCache.Drop(host, spot.Value, kind);
            }
        }

        // somewhere beside `near` a cache can stand
        private static Position? Spot(Entity champion, Player near, List<Position> placed)
        {
            var world = champion.World;
            var centre = champion.SpawnPoint ?? new Position(champion.X, champion.Y);
            for (var tries = 0; tries < 30; tries++)
            {
                var a = Random.NextDouble() * Math.PI * 2;
                var d = 2.5 + Random.NextDouble() * 3;
                var at = new Position((int)(near.X + Math.Cos(a) * d) + 0.5f, (int)(near.Y + Math.Sin(a) * d) + 0.5f);
                if (champion.DistTo(at.X, at.Y) < FromChampion || Dist(at, centre) > Reach - 2)
                    continue;
                if (placed.Any(p => Dist(p, at) < 3) ||
                    world.Enemies.Values.Any(e => CrucibleCache.IsCache(e) && e.DistTo(at.X, at.Y) < 3))
                    continue;
                if (CrucibleCache.Placeable(world, at.X, at.Y))
                    return at;
            }
            return null;
        }

        private static double Dist(Position a, Position b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    // On a cache: it holds while its champion can't be hurt (a shield over it, so its charge isn't
    // wasted on a blow that would glance off), crumbles if its champion falls first, the floor
    // falls from under it, or its time runs out, and launches its charge when it's broken.
    internal class CacheCharge : Behavior
    {
        private sealed class Watch
        {
            public int Age;
            public int Next;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = new Watch();

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Watch w || host.World == null || host.Dead)
                return;
            w.Age += time.ElapsedMsDelta;
            if (w.Age < w.Next)
                return;
            w.Next = w.Age + 200;
            var champion = CrucibleCache.ChampionNear(host.World, host.X, host.Y);
            if (champion == null || w.Age >= CrucibleCache.LifeMs || !CrucibleCache.Grounded(host.World, (int)host.X, (int)host.Y))
            {
                CrucibleCache.Lose(host);
                return;
            }
            if (champion.HasConditionEffect(ConditionEffectIndex.Invulnerable) || champion.HasConditionEffect(ConditionEffectIndex.Invincible))
                host.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 400);
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            if (host is Enemy cache)
                CrucibleCache.Launch(cache);
        }
    }
}
