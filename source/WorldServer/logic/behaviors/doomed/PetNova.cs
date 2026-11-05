using System;
using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors.doomed
{
    // A pet's area attack (a stomp, a stink cloud, a zap): every cooldown, picks the nearest enemy
    // within range and hits it and every other enemy within radius of it for a random amount
    // between min and max, credited to the pet's owner as PetStrike's hits are, with an optional
    // condition on each enemy hit. The client draws a line from the pet and a nova on the target.
    internal class PetNova : Behavior
    {
        private readonly int _min;
        private readonly int _max;
        private readonly double _range;
        private readonly double _radius;
        private readonly ConditionEffectIndex? _effect;
        private readonly int _effectMs;
        private readonly uint _color;
        private Cooldown _coolDown;

        public PetNova(int min, int max, Cooldown coolDown, double range = 10, double radius = 2.5,
            ConditionEffectIndex? effect = null, int effectMs = 0, uint color = 0xffff0000)
        {
            _min = min;
            _max = Math.Max(min, max);
            _coolDown = coolDown.Normalize();
            _range = range;
            _radius = radius;
            _effect = effect;
            _effectMs = effectMs;
            _color = color;
        }

        private static bool Hittable(Entity e) =>
            e is Enemy enemy && enemy.AllyOwnerId == -1 && !enemy.HasConditionEffect(ConditionEffectIndex.Invincible) &&
            !enemy.HasConditionEffect(ConditionEffectIndex.Invulnerable) && !enemy.HasConditionEffect(ConditionEffectIndex.Stasis);

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = _coolDown.Next(Random);

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state;
            if (cool > 0)
            {
                state = cool - time.ElapsedMsDelta;
                return;
            }

            var owner = (host as Pet)?.PlayerOwner;
            if (owner?.World == null || owner.World != host.World || owner.Dead)
            {
                state = _coolDown.Next(Random);
                return;
            }

            var target = host.GetNearestEntity(_range, false, Hittable) as Enemy;
            if (target == null)
                return; // try again next tick

            state = _coolDown.Next(Random);
            var color = new ARGB(_color);
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Trail,
                TargetObjectId = host.Id,
                Pos1 = new Position { X = target.X, Y = target.Y },
                Color = color
            }, host);
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.AreaBlast,
                TargetObjectId = target.Id,
                Pos1 = new Position { X = (float)_radius },
                Color = color
            }, target);

            // the enemies hit, gathered first: a hit can kill one and take it out of the world
            var hit = new List<Enemy>();
            foreach (var e in host.World.EnemiesCollision.HitTest(target.X, target.Y, _radius))
                if (e is Enemy enemy && Hittable(enemy) && enemy.SqDistTo(target) <= _radius * _radius)
                    hit.Add(enemy);
            if (!hit.Contains(target))
                hit.Add(target);

            var effs = _effect.HasValue && _effectMs > 0
                ? new[] { new ConditionEffect(_effect.Value, _effectMs) }
                : Array.Empty<ConditionEffect>();
            foreach (var enemy in hit)
                if (!enemy.Dead && enemy.World != null)
                    enemy.Damage(owner, ref time, (int)(Random.Next(_min, _max + 1) * ((host as Pet)?.DamageScale ?? 1)), false, false, effs);
        }
    }
}
