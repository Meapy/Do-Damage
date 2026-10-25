using System;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // Fires the host's own projectile from fixed spots, given relative to its spawn point, each in a
    // fixed direction (degrees), as though turrets stood there: one volley every `coolDown`, the volleys
    // taken in turn. Each spot is a tile's centre and must be open floor (a shot that starts in a wall
    // stops there). Oryx 5 sweeps the mouth of his arena's corridor this way in his Last Judgement.
    internal class ShootFromSpots : Behavior
    {
        private readonly int _projectile;
        private readonly int _coolDown;
        private readonly (int Dx, int Dy, double Angle)[][] _volleys;

        public ShootFromSpots(int projectileIndex, int coolDown, params (int Dx, int Dy, double Angle)[][] volleys)
        {
            _projectile = projectileIndex;
            _coolDown = coolDown;
            _volleys = volleys;
        }

        private sealed class Turn
        {
            public int Cool;
            public int Volleys;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = new Turn();

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = (Turn)state;
            if ((s.Cool -= time.ElapsedMsDelta) > 0)
                return;
            s.Cool = _coolDown;

            if (!(host is Enemy enemy) || !host.ObjectDesc.Projectiles.TryGetValue(_projectile, out var desc))
                return;
            var spawn = host.SpawnPoint.Value;
            foreach (var (dx, dy, angle) in _volleys[s.Volleys++ % _volleys.Length])
            {
                var dmg = Random.Next(desc.MinDamage, desc.MaxDamage) * enemy.DamageScale;
                host.World.BroadcastEnemyShootIfVisible(new EnemyShootMessage()
                {
                    Spawned = host.Spawned,
                    BulletId = host.GetNextBulletId(),
                    OwnerId = host.Id,
                    StartingPos = new Position((int)spawn.X + dx + 0.5f, (int)spawn.Y + dy + 0.5f),
                    Angle = (float)(angle * Math.PI / 180),
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
