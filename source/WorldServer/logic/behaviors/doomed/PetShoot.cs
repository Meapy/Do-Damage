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
    //
    // With rotateAngle, the pattern turns instead of aiming, as a boss's spiral does (the boss pets
    // of the Black Between and the Crucible): the first volley is aimed at the enemy, and each one
    // after it turns rotateAngle degrees further on from the last. It still fires only while an
    // enemy is in range.
    internal class PetShoot : Behavior
    {
        private readonly int _count;
        private readonly float _shootAngle;
        private readonly float _angleOffset;
        private readonly float _rotateAngle;
        private readonly int _projectileIndex;
        private readonly double _range;
        private Cooldown _coolDown;

        public PetShoot(double range, int count, double shootAngle, double angleOffset, int projectileIndex, Cooldown coolDown,
            double rotateAngle = 0)
        {
            _range = range;
            _count = Math.Max(1, count);
            _shootAngle = (float)(shootAngle * Math.PI / 180);
            _angleOffset = (float)(angleOffset * Math.PI / 180);
            _rotateAngle = (float)(rotateAngle * Math.PI / 180);
            _projectileIndex = projectileIndex;
            _coolDown = coolDown.Normalize();
        }

        // the time to the next volley, and where a turning pattern points (null until its first)
        private sealed class ShootState
        {
            public int Cool;
            public float? Turn;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) =>
            state = new ShootState { Cool = _coolDown.Next(Random) };

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            // a pet in stasis (the Black Between's survival phases) does nothing
            if (host.HasConditionEffect(ConditionEffectIndex.Stasis))
                return;
            var s = state as ShootState ?? (ShootState)(state = new ShootState());
            if (s.Cool > 0)
            {
                s.Cool -= time.ElapsedMsDelta;
                return;
            }

            var owner = (host as Pet)?.PlayerOwner;
            if (owner?.World == null || owner.World != host.World || owner.Dead ||
                !host.ObjectDesc.Projectiles.TryGetValue(_projectileIndex, out var desc))
            {
                s.Cool = _coolDown.Next(Random);
                return;
            }

            var target = host.GetNearestEntity(_range, false,
                e => e is Enemy enemy && enemy.AllyOwnerId == -1 && !enemy.HasConditionEffect(ConditionEffectIndex.Invincible) &&
                     !enemy.HasConditionEffect(ConditionEffectIndex.Invulnerable) && !enemy.HasConditionEffect(ConditionEffectIndex.Stasis));
            if (target == null)
            {
                // a turning pattern aims afresh at the next enemy it finds
                s.Turn = null;
                return; // try again next tick
            }

            s.Cool = _coolDown.Next(Random);

            var count = host.HasConditionEffect(ConditionEffectIndex.Dazed) ? Math.Max(1, _count / 2) : _count;
            var aim = (float)Math.Atan2(target.Y - host.Y, target.X - host.X);
            if (_rotateAngle != 0)
            {
                aim = s.Turn.HasValue ? s.Turn.Value + _rotateAngle : aim;
                s.Turn = aim % (float)(2 * Math.PI);
            }
            var angle = aim - _shootAngle * (count - 1) / 2 + _angleOffset;
            // a full ring of an even count, centred on the target, has no shot at it: turn it half
            // a step so that one flies straight at it
            if (_rotateAngle == 0 && count % 2 == 0 && _shootAngle * count >= 2 * Math.PI - 0.01)
                angle += _shootAngle / 2;
            var bulletId = owner.GetNextBulletId(count, true);
            var damage = Pet.Hit(host, Random.Next(desc.MinDamage, desc.MaxDamage + 1));
            for (var i = 0; i < count; i++)
                host.World.BroadcastServerPlayerShoot(new ServerPlayerShoot(owner.Id, bulletId + i, host.ObjectType, host.Position, angle + _shootAngle * i, damage, desc), host);
        }
    }
}
