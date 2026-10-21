using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.utils;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // Fires a formation of shots along each of `directions` evenly spaced directions. Every shot
    // leaves from a point of its own, `offsets` being (forward, sideways) pairs in tiles from the
    // host, so the shots keep their shape as they fly: twin lanes side by side, an arrowhead. The
    // first direction is fixedAngle, else aimed at the host's target (the nearest player within
    // radius); rotateAngle turns the formation further with each volley.
    internal class ShootFormation : Behavior
    {
        private readonly float _radius;
        private readonly int _directions;
        private readonly double[] _offsets;
        private readonly int _projectileIndex;
        private readonly double? _fixedAngle;
        private readonly double _angleOffset;
        private readonly double _rotateAngle;
        private readonly Cooldown _coolDown;
        private readonly int _coolDownOffset;

        public ShootFormation(double radius, int directions, double[] offsets, int projectileIndex = 0,
            double? fixedAngle = null, double angleOffset = 0, double rotateAngle = 0,
            Cooldown coolDown = new Cooldown(), int coolDownOffset = 0)
        {
            if (offsets.Length % 2 != 0)
                throw new ArgumentException("offsets are (forward, sideways) pairs");
            _radius = (float)radius;
            _directions = directions;
            _offsets = offsets;
            _projectileIndex = projectileIndex;
            _fixedAngle = fixedAngle * Math.PI / 180;
            _angleOffset = angleOffset * Math.PI / 180;
            _rotateAngle = rotateAngle * Math.PI / 180;
            _coolDown = coolDown.Normalize();
            _coolDownOffset = coolDownOffset;
        }

        private sealed class FormationState
        {
            public int Cool;
            public int Volleys;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) =>
            state = new FormationState { Cool = _coolDownOffset };

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = (FormationState)state;
            if (s.Cool > 0)
            {
                s.Cool -= time.ElapsedMsDelta;
                return;
            }
            s.Cool = _coolDown.Next(Random);

            if (host.HasConditionEffect(ConditionEffectIndex.Stunned)
                || !host.ObjectDesc.Projectiles.TryGetValue(_projectileIndex, out var desc))
                return;

            double a;
            if (_fixedAngle != null)
                a = _fixedAngle.Value;
            else
            {
                var target = host.AttackTarget ?? host.GetNearestEntity(_radius, null);
                if (target == null)
                    return;
                a = Math.Atan2(target.Y - host.Y, target.X - host.X);
            }
            a += _angleOffset + _rotateAngle * s.Volleys++;

            for (var d = 0; d < _directions; d++)
            {
                var dir = a + 2 * Math.PI * d / _directions;
                var (cos, sin) = (Math.Cos(dir), Math.Sin(dir));
                for (var i = 0; i < _offsets.Length; i += 2)
                {
                    var (fwd, side) = (_offsets[i], _offsets[i + 1]);
                    var dmg = (double)Random.Next(desc.MinDamage, desc.MaxDamage);
                    if (host is Enemy enemy)
                        dmg *= enemy.DamageScale;
                    if (host.HasConditionEffect(ConditionEffectIndex.Weak))
                        dmg /= 2;

                    host.World.BroadcastEnemyShootIfVisible(new EnemyShootMessage()
                    {
                        Spawned = host.Spawned,
                        BulletId = host.GetNextBulletId(1),
                        OwnerId = host.Id,
                        StartingPos = new Position((float)(host.X + fwd * cos - side * sin), (float)(host.Y + fwd * sin + side * cos)),
                        Angle = (float)dir,
                        Damage = (short)dmg,
                        BulletType = (byte)desc.BulletType,
                        AngleInc = 0,
                        NumShots = 1,
                        ObjectType = host.ObjectType,
                        ProjectileDesc = desc,
                    }, host);
                }
            }
        }
    }
}
