using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors.doomed
{
    // Doomed Realms pet projectile attacks (PetSimpleAttack, PetMultiAttack, PetRingAttack): every
    // cooldown, fires count of the pet's projectiles at the nearest enemy within range, shootAngle
    // degrees apart and turned by angleOffset. The shots are the owner's, like ally shots, so the
    // owner's client reports the hits.
    internal class PetShoot : Behavior
    {
        private readonly int _count;
        private readonly float _shootAngle;
        private readonly float _angleOffset;
        private readonly int _projectileIndex;
        private readonly double _range;
        private Cooldown _coolDown;

        public PetShoot(double range, int count, double shootAngle, double angleOffset, int projectileIndex, Cooldown coolDown)
        {
            _range = range;
            _count = Math.Max(1, count);
            _shootAngle = (float)(shootAngle * Math.PI / 180);
            _angleOffset = (float)(angleOffset * Math.PI / 180);
            _projectileIndex = projectileIndex;
            _coolDown = coolDown.Normalize();
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
            if (owner?.World == null || owner.World != host.World || owner.Dead ||
                !host.ObjectDesc.Projectiles.TryGetValue(_projectileIndex, out var desc))
            {
                state = _coolDown.Next(Random);
                return;
            }

            var target = host.GetNearestEntity(_range, false,
                e => e is Enemy enemy && enemy.AllyOwnerId == -1 && !enemy.HasConditionEffect(ConditionEffectIndex.Invincible) &&
                     !enemy.HasConditionEffect(ConditionEffectIndex.Invulnerable) && !enemy.HasConditionEffect(ConditionEffectIndex.Stasis));
            if (target == null)
                return; // try again next tick

            state = _coolDown.Next(Random);

            var count = host.HasConditionEffect(ConditionEffectIndex.Dazed) ? Math.Max(1, _count / 2) : _count;
            var angle = (float)Math.Atan2(target.Y - host.Y, target.X - host.X) - _shootAngle * (count - 1) / 2 + _angleOffset;
            // a full ring of an even count, centred on the target, has no shot at it: turn it half
            // a step so that one flies straight at it (tools/port_doomed_pets.shots_on_target)
            if (count % 2 == 0 && _shootAngle * count >= 2 * Math.PI - 0.01)
                angle += _shootAngle / 2;
            var bulletId = owner.GetNextBulletId(count, true);
            var damage = (int)(Random.Next(desc.MinDamage, desc.MaxDamage + 1) * ((host as Pet)?.DamageScale ?? 1));
            for (var i = 0; i < count; i++)
                host.World.BroadcastServerPlayerShoot(new ServerPlayerShoot(owner.Id, bulletId + i, host.ObjectType, host.Position, angle + _shootAngle * i, damage, desc), host);
        }
    }
}
