using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The Black Between's gear (tools/build_black_between.py, the Mysterious One's white bags): what
    // each does beyond its numbers, by its <RelicPower>. BetweenAbility runs when the ability is
    // used, BetweenShot when its own projectile is fired (the quiver's arrow), BetweenHit when one of
    // the ability's own shots hits an enemy, DecoyTick for the Prism's decoys.
    //
    // Their ground effects are the bosses' (ShowEffect GroundMark): a circle on the floor filling in
    // until what's coming lands, then a burst there. (AreaBlast is always drawn on the object it's
    // sent with, the player, wherever it's meant to be.)
    partial class Player
    {
        private const uint Gold = 0xFFFFD24A, Violet = 0xFFB040FF, Rift = 0xFF6A40C0;
        // the Vial's three splashes, darker and bluer each time
        private static readonly uint[] DuskShades = { 0xFFC060FF, 0xFF9A48F0, 0xFF6A50E0 };
        // a thrown trap or vial lands in this long
        private const int ThrowMs = 1000;

        // the Vial's own looks (ShowEffect Relic, drawn by the Rust client from 2.6.1:
        // client-rs/app/src/particles/relic.rs, looks 43-45): the vial's flight, a splash (objectType:
        // 0 the vial's, 1 the four round it, 2 the four beyond) and the dusk running out to the next
        // splash (objectType its heading in degrees, radius how far). Sent before the plain throw and
        // bursts older clients draw, which the new client then leaves out.
        private const int DuskThrow = 43, DuskSplash = 44, DuskFlow = 45;

        private void DuskFx(int look, Position at, float radius, uint color, int ms = 0, int objectType = 0) =>
            World?.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at, Pos2 = new Position(radius, look),
                Color = new ARGB(color), Duration = ms, ObjectType = (ushort)objectType, SourceId = Id,
            }, ref at);

        // the dusk running from `from` to `to`, getting there in `ms`
        private void DuskRun(Position from, Position to, uint color, int ms)
        {
            var (dx, dy) = (to.X - from.X, to.Y - from.Y);
            var deg = (int)MathF.Round(MathF.Atan2(dy, dx) * 180 / MathF.PI);
            DuskFx(DuskFlow, from, MathF.Sqrt(dx * dx + dy * dy), color, ms, (deg % 360 + 360) % 360);
        }

        // the quiver's arrow in flight: which one, and whether it has burst
        private int _cycloneFlight;
        private bool _cycloneBurst;

        private void Mark(Position at, float radius, int ms, uint color, int style = 0) =>
            World?.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.GroundMark, TargetObjectId = Id, Pos1 = at,
                Pos2 = new Position(radius, style), Color = new ARGB(color), Duration = ms,
            }, ref at);

        private void Burst(Position at, float radius, uint color) =>
            World?.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.Diffuse, TargetObjectId = Id, Pos1 = at, Pos2 = new Position(at.X + radius, at.Y), Color = new ARGB(color) }, ref at);

        // the item flies from the player to `to`, landing in ThrowMs (Duration is in seconds)
        private void Throw(Position to, uint color) =>
            World?.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.Throw, TargetObjectId = Id, Pos1 = to, Color = new ARGB(color), Duration = ThrowMs / 1000 }, this);

        // the ability's projectile with this index, fired by the server from `from` (it counts as
        // the ability's, so its hits come back through BetweenHit)
        private void ShootFrom(Item item, int index, Position from, IEnumerable<float> angles)
        {
            if (World == null || item == null || item.Projectiles.Length <= index)
                return;
            var desc = item.Projectiles[index];
            var shots = new List<OutgoingMessage>();
            foreach (var angle in angles)
            {
                var roll = Random.Shared.Next(desc.MinDamage, desc.MaxDamage + 1);
                shots.Add(new ServerPlayerShoot(Id, GetNextBulletId(1, true), item.ObjectType, from, angle, (int)(roll * Stats.GetAttackMult()), desc));
            }
            World.BroadcastIfVisible(shots, ref from);
        }

        private static IEnumerable<float> Ring(int n, float start = 0) =>
            Enumerable.Range(0, n).Select(i => start + i * MathF.PI * 2 / n);

        private static float RandomTurn() => (float)(Random.Shared.NextDouble() * Math.PI * 2);

        // after `ms`, if we're still in this world
        private void After(int ms, Action<World, TickTime> then)
        {
            var world = World;
            world?.StartNewTimer(ms, (w, t) =>
            {
                if (World == world)
                    then(w, t);
            });
        }

        private void BetweenAbility(Position pos, ref TickTime time)
        {
            if (World == null)
                return;
            var item = Inventory[1];
            switch (item?.RelicPower)
            {
                // thrown; where it lands, three stages, each marked as it comes: Aurel's lightning,
                // Vesper's veil, their father's rift
                case "TrinityTrap":
                {
                    var at = pos;
                    Throw(at, Gold);
                    Mark(at, 3.5f, ThrowMs + 300, Gold);
                    After(ThrowMs + 300, (w, t) =>
                    {
                        Burst(at, 3.5f, Gold);
                        foreach (var e in EnemiesNear(at.X, at.Y, 3.5f))
                        {
                            w.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.Lightning, TargetObjectId = Id, Pos1 = new Position(e.X, e.Y), Pos2 = new Position(90, 0), Color = new ARGB(Gold) }, this);
                            RelicDamage(e, 300, ref t, false);
                            e.ApplyConditionEffect(ConditionEffectIndex.Paralyzed, 2000);
                        }
                        Mark(at, 3.5f, 600, Violet);
                    });
                    After(ThrowMs + 900, (w, t) =>
                    {
                        Burst(at, 3.5f, Violet);
                        foreach (var e in EnemiesNear(at.X, at.Y, 3.5f))
                        {
                            RelicDamage(e, 300, ref t, false);
                            e.ApplyConditionEffect(ConditionEffectIndex.Slowed, 4000);
                        }
                        // the rift, opening as the Mysterious One's does
                        Mark(at, 4, 600, Rift, 1);
                    });
                    After(ThrowMs + 1500, (w, t) =>
                    {
                        Burst(at, 4, Rift);
                        foreach (var e in EnemiesNear(at.X, at.Y, 4))
                        {
                            RelicDamage(e, 200, ref t, false);
                            e.Bleed(this, 450, 3000);
                        }
                    });
                    break;
                }
                // the Cloak: where its wearer lands (the teleport's target), the ground bursts, ten
                // sunsparks fly out and they're armored for 2 s
                case "PlanarRift":
                {
                    var at = pos;
                    Mark(at, 3, 150, Gold);
                    Burst(at, 3, Gold);
                    foreach (var e in EnemiesNear(at.X, at.Y, 3))
                        RelicDamage(e, 400, ref time, false);
                    ShootFrom(item, 0, at, Ring(10, RandomTurn()));
                    ApplyConditionEffect(ConditionEffectIndex.Armored, 2000);
                    break;
                }
                // the Vial, thrown: a splash, then four round it at half, then four beyond at half
                // again, each ring a little apart and a shade darker
                case "CascadeDusk":
                {
                    var at = pos;
                    DuskFx(DuskThrow, at, 2.5f, DuskShades[0], ThrowMs);
                    Throw(at, DuskShades[0]);
                    Mark(at, 2.5f, ThrowMs, DuskShades[0]);
                    After(ThrowMs, (w, t) =>
                    {
                        Splash(at, 2.5f, 1f, DuskShades[0], 0, ref t);
                        foreach (var a in Ring(4))
                        {
                            DuskRun(at, Out(at, a, 3.5f), DuskShades[1], 500);
                            Mark(Out(at, a, 3.5f), 3, 500, DuskShades[1]);
                        }
                    });
                    After(ThrowMs + 500, (w, t) =>
                    {
                        foreach (var a in Ring(4))
                        {
                            Splash(Out(at, a, 3.5f), 3, 0.5f, DuskShades[1], 1, ref t);
                            // each runs on to the two beyond it either side
                            foreach (var side in new[] { -MathF.PI / 4, MathF.PI / 4 })
                                DuskRun(Out(at, a, 3.5f), Out(at, a + side, 6.5f), DuskShades[2], 500);
                        }
                        foreach (var a in Ring(4, MathF.PI / 4))
                            Mark(Out(at, a, 6.5f), 3, 500, DuskShades[2]);
                    });
                    After(ThrowMs + 1000, (w, t) =>
                    {
                        foreach (var a in Ring(4, MathF.PI / 4))
                            Splash(Out(at, a, 6.5f), 3, 0.25f, DuskShades[2], 2, ref t);
                    });
                    break;
                }
            }
        }

        private static Position Out(Position at, float angle, float dist) =>
            new Position(at.X + MathF.Cos(angle) * dist, at.Y + MathF.Sin(angle) * dist);

        // one splash of the Vial: 400 and 3,000 over 6 s at full strength
        private void Splash(Position at, float radius, float scale, uint color, int stage, ref TickTime time)
        {
            DuskFx(DuskSplash, at, radius, color, 0, stage);
            Burst(at, radius, color);
            foreach (var e in EnemiesNear(at.X, at.Y, radius))
            {
                RelicDamage(e, (int)(400 * scale), ref time, false);
                e.Bleed(this, (int)(3000 * scale), 6000);
            }
        }

        // the ability's own projectile was fired (PlayerShoot from slot 1): the quiver's arrow
        // throws sunsparks to either side as it flies, and bursts where it comes down unless it
        // burst on an enemy first
        private void BetweenShot(Item item, Position start, float angle, ProjectileDesc desc)
        {
            if (World == null || item?.RelicPower != "SolarCyclone" || desc.BulletType != 0)
                return;
            var flight = ++_cycloneFlight;
            _cycloneBurst = false;
            var life = (int)desc.LifetimeMS;
            var perMs = desc.Speed / 10000f;
            Position At(int ms) => new Position(start.X + MathF.Cos(angle) * perMs * ms, start.Y + MathF.Sin(angle) * perMs * ms);
            const int Every = 200;
            void Step(int ms)
            {
                After(Every, (w, t) =>
                {
                    if (flight != _cycloneFlight || _cycloneBurst || Inventory[1] != item)
                        return;
                    var now = ms + Every;
                    if (now >= life)
                    {
                        CycloneBurst(item, At(life), ref t);
                        return;
                    }
                    var spin = now / (float)Every * 0.7f;
                    ShootFrom(item, 1, At(now), new[] { angle + MathF.PI / 2 + spin, angle - MathF.PI / 2 + spin });
                    Step(now);
                });
            }
            Step(0);
        }

        private void CycloneBurst(Item item, Position at, ref TickTime time)
        {
            _cycloneBurst = true;
            Mark(at, 3, 120, Gold);
            Burst(at, 3, Gold);
            foreach (var e in EnemiesNear(at.X, at.Y, 3))
                RelicDamage(e, 2000, ref time, false);
            ShootFrom(item, 1, at, Ring(8, RandomTurn()));
        }

        // one of the ability's own shots hit an enemy for dmg
        private void BetweenHit(Enemy enemy, int dmg, ProjectileDesc desc, ref TickTime time)
        {
            if (_inRelic || World == null)
                return;
            var item = Inventory[1];
            switch (item?.RelicPower)
            {
                // the Cyclone Arrow (projectile 0, not its sunsparks) bursts on the first enemy it hits
                case "SolarCyclone":
                    if (desc.BulletType == 0 && !_cycloneBurst)
                        CycloneBurst(item, new Position(enemy.X, enemy.Y), ref time);
                    break;
                // each dawn sword heals its wielder 6% of what it does, shown over them
                case "DawnHelm":
                {
                    var heal = Math.Min(Math.Max(1, dmg * 6 / 100), Stats[0] - Health);
                    if (heal <= 0)
                        break;
                    Health += heal;
                    World.BroadcastIfVisible(new Notification { ObjectId = Id, Message = "+" + heal, Color = new ARGB(0xFF00FF00) }, this);
                    break;
                }
            }
        }

        // a Prism decoy, every tick it lasts: each one, once a second, casts Vesper's three dusk
        // bolts at the nearest enemy
        internal void DecoyTick(Decoy decoy)
        {
            var item = Inventory[1];
            if (World == null || decoy.World != World || item?.RelicPower != "DuskVeil" || Now < decoy.NextCastAt)
                return;
            var target = EnemiesNear(decoy.X, decoy.Y, 11).OrderBy(e => e.DistTo(decoy)).FirstOrDefault();
            if (target == null)
                return;
            decoy.NextCastAt = Now + 1000;
            var a = MathF.Atan2(target.Y - decoy.Y, target.X - decoy.X);
            ShootFrom(item, 0, new Position(decoy.X, decoy.Y), new[] { a - 0.18f, a, a + 0.18f });
        }
    }
}
