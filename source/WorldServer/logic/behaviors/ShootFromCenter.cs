using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.utils;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // Shoots along the line through the nearest `center` (Oryx 5), or with aroundSpawn through the
    // point where it spawned (the middle of his arena): away from it, or across to it (his Halo Orbs
    // and the Pillars of Heaven fire into his arena). With snapToAxis the
    // line is the nearest of east, south, west and north, so the pillars on the sides shoot straight
    // across and those above and below straight down or up. Each volley takes the next of
    // `projectileIndices` in turn.
    internal class ShootFromCenter : Behavior
    {
        private readonly ushort _center;
        private readonly bool _inward;
        private readonly bool _snapToAxis;
        private readonly int[] _projectiles;
        private readonly int _count;
        private readonly float _shootAngle;
        private readonly Cooldown _coolDown;
        private readonly int _coolDownOffset;
        private readonly bool _aroundSpawn;

        public ShootFromCenter(string center, int[] projectileIndices, bool inward = false, bool snapToAxis = false,
            int count = 1, double shootAngle = 0, Cooldown coolDown = new Cooldown(), int coolDownOffset = 0, bool aroundSpawn = false)
        {
            _aroundSpawn = aroundSpawn;
            _center = GetObjType(center);
            _projectiles = projectileIndices;
            _inward = inward;
            _snapToAxis = snapToAxis;
            _count = count;
            _shootAngle = (float)(shootAngle * Math.PI / 180);
            _coolDown = coolDown.Normalize();
            _coolDownOffset = coolDownOffset;
        }

        private sealed class VolleyState
        {
            public int Cool;
            public int Volleys;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) =>
            state = new VolleyState { Cool = _coolDownOffset };

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = (VolleyState)state;
            if (s.Cool > 0)
            {
                s.Cool -= time.ElapsedMsDelta;
                return;
            }
            s.Cool = _coolDown.Next(Random);

            var center = host.GetNearestEntity(40, _center);
            if (center == null || host.HasConditionEffect(ConditionEffectIndex.Stunned))
                return;
            var index = _projectiles[s.Volleys++ % _projectiles.Length];
            if (!host.ObjectDesc.Projectiles.TryGetValue(index, out var desc))
                return;

            var (cx, cy) = _aroundSpawn ? (center.SpawnPoint.Value.X, center.SpawnPoint.Value.Y) : (center.X, center.Y);
            var a = Math.Atan2(host.Y - cy, host.X - cx);
            if (_inward)
                a += Math.PI;
            if (_snapToAxis)
                a = Math.Round(a / (Math.PI / 2)) * (Math.PI / 2);

            var dmg = (double)Random.Next(desc.MinDamage, desc.MaxDamage);
            if (host is Enemy enemy)
                dmg *= enemy.DamageScale;

            host.World.BroadcastEnemyShootIfVisible(new EnemyShootMessage()
            {
                Spawned = host.Spawned,
                BulletId = host.GetNextBulletId(_count),
                OwnerId = host.Id,
                StartingPos = new Position(host.X, host.Y),
                Angle = (float)a - _shootAngle * (_count - 1) / 2,
                Damage = (short)dmg,
                BulletType = (byte)desc.BulletType,
                AngleInc = _shootAngle,
                NumShots = (byte)_count,
                ObjectType = host.ObjectType,
                ProjectileDesc = desc,
            }, host);
        }
    }
}
