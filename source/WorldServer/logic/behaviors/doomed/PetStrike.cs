using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors.doomed
{
    // Doomed Realms pet attacks: every cooldown, deals a random amount of damage between min and
    // max to the nearest enemy within range, credited to the pet's owner. Doomed Realms pets that
    // fired projectiles are ported to this too, using that projectile's damage. With a bleed, the
    // enemy also bleeds bleedDamage over bleedMs (Enemy.Bleed).
    internal class PetStrike : Behavior
    {
        private readonly int _min;
        private readonly int _max;
        private readonly double _range;
        private readonly int _bleedDamage;
        private readonly int _bleedMs;
        private Cooldown _coolDown;

        public PetStrike(int min, int max, Cooldown coolDown, double range = 10, int bleedDamage = 0, int bleedMs = 0)
        {
            _bleedDamage = bleedDamage;
            _bleedMs = bleedMs;
            _min = min;
            _max = Math.Max(min, max);
            _coolDown = coolDown.Normalize();
            _range = range;
        }

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

            var target = host.GetNearestEntity(_range, false,
                e => e is Enemy enemy && !enemy.HasConditionEffect(ConditionEffectIndex.Invincible) &&
                     !enemy.HasConditionEffect(ConditionEffectIndex.Invulnerable) && !enemy.HasConditionEffect(ConditionEffectIndex.Stasis)) as Enemy;
            if (target == null)
                return; // try again next tick

            state = _coolDown.Next(Random);
            var color = new ARGB(0xffff0000);
            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Trail,
                TargetObjectId = host.Id,
                Pos1 = new Position { X = target.X, Y = target.Y },
                Color = color
            }, host);
            host.World.BroadcastIfVisible(new ShowEffect() { EffectType = EffectType.Potion, TargetObjectId = target.Id, Color = color }, target);
            target.Damage(owner, ref time, Random.Next(_min, _max + 1), false);
            if (_bleedDamage > 0 && !target.Dead)
                target.Bleed(owner, _bleedDamage, _bleedMs);
        }
    }
}
