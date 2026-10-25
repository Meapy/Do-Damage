using System;
using System.Collections.Generic;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Makes the host, an invisible turret placed beside a pillar (the nearest `pillar` tile), fire
    // from that pillar while `boss` is below `hpBelow` of its health: Oryx 3's four Sanctum pillars,
    // a turret each (tools/build_oryx_sanctuary.py), so the shots aren't his and his sprite doesn't
    // turn to each volley. Each fires a pair in at the boss's spawn point, swaying a little every
    // volley, and aims a single shot at the nearest player near its pillar. Each names two of the
    // host's projectiles: the first above half the boss's health, the second (his Exalted copy)
    // below. A shot leaves a tile out from the pillar (one fired inside the pillar's square would stop
    // there). The turrets are static objects, which tick together every tick, so they keep in step.
    // Once the boss is dead they stay quiet.
    internal class ShootFromPillars : Behavior
    {
        private const int MiddleCoolDown = 2000;
        private const int AimCoolDown = 3600;
        private const double AimRange = 12;
        private const double PillarReach = 3;

        private readonly ushort _boss;
        private readonly ushort _pillar;
        private readonly double _hpBelow;
        private readonly int[] _middle, _aimed;
        private readonly double _damage;

        public ShootFromPillars(string boss, string pillar, double hpBelow, int[] middle, int[] aimed, double damage = 1)
        {
            _boss = GetObjType(boss);
            _pillar = GetObjType(pillar);
            _hpBelow = hpBelow;
            _middle = middle;
            _aimed = aimed;
            _damage = damage;
        }

        private sealed class Turret
        {
            public Enemy Boss;
            public bool Done;
            public float X, Y;
            public double Inward;
            public int MiddleCool, AimCool, Volleys;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = (Turret)state;
            if (s == null)
            {
                // the boss stands in the middle, dormant, as the dungeon starts
                if (!(host.GetNearestEntity(40, _boss) is Enemy boss) || !FindPillar(host, boss, s = new Turret { Boss = boss }))
                    return;
                state = s;
            }
            if (s.Done)
                return;
            if (s.Boss.Dead || s.Boss.World == null)
            {
                s.Done = true;
                return;
            }
            var hp = (double)s.Boss.Health / s.Boss.MaxHealth;
            if (hp >= _hpBelow)
                return;

            var dt = time.ElapsedMsDelta;
            var exalted = hp < 0.5 ? 1 : 0;

            s.MiddleCool -= dt;
            if (s.MiddleCool <= 0)
            {
                s.MiddleCool = MiddleCoolDown;
                var sway = (s.Volleys++ % 3 - 1) * 6 * Math.PI / 180;
                Fire(host, s, s.Inward + sway, 2, 20, _middle[exalted]);
            }

            s.AimCool -= dt;
            if (s.AimCool <= 0)
            {
                s.AimCool = AimCoolDown;
                var target = NearestPlayer(host, s);
                if (target != null)
                    Fire(host, s, Math.Atan2(target.Y - s.Y, target.X - s.X), 1, 0, _aimed[exalted]);
            }
        }

        // its pillar is the one nearest it, and its shots go in toward the boss's spawn point
        private bool FindPillar(Entity host, Enemy boss, Turret s)
        {
            var map = host.World.Map;
            var r = (int)PillarReach;
            var best = double.MaxValue;
            for (var y = (int)host.Y - r; y <= (int)host.Y + r; y++)
                for (var x = (int)host.X - r; x <= (int)host.X + r; x++)
                {
                    if (!map.Contains(x, y) || map[x, y].ObjType != _pillar)
                        continue;
                    var (px, py) = (x + 0.5f, y + 0.5f);
                    var d = (px - host.X) * (px - host.X) + (py - host.Y) * (py - host.Y);
                    if (d < best)
                        (best, s.X, s.Y) = (d, px, py);
                }
            if (best == double.MaxValue)
                return false;
            var spawn = boss.SpawnPoint.Value;
            s.Inward = Math.Atan2(spawn.Y - s.Y, spawn.X - s.X);
            return true;
        }

        private static Player NearestPlayer(Entity host, Turret s)
        {
            Player nearest = null;
            var best = AimRange * AimRange;
            foreach (var player in host.World.Players.Values)
            {
                if (!player.IsVisibleToEnemy())
                    continue;
                var d = (player.X - s.X) * (player.X - s.X) + (player.Y - s.Y) * (player.Y - s.Y);
                if (d < best)
                    (nearest, best) = (player, d);
            }
            return nearest;
        }

        private void Fire(Entity host, Turret s, double angle, int count, double spreadDeg, int index)
        {
            if (!host.ObjectDesc.Projectiles.TryGetValue(index, out var desc))
                return;
            var inc = (float)(spreadDeg * Math.PI / 180);
            var dmg = Random.Next(desc.MinDamage, desc.MaxDamage) * s.Boss.DamageScale * _damage;
            host.World.BroadcastEnemyShootIfVisible(new EnemyShootMessage()
            {
                Spawned = host.Spawned,
                BulletId = host.GetNextBulletId(count),
                OwnerId = host.Id,
                StartingPos = new Position(s.X + (float)Math.Cos(angle), s.Y + (float)Math.Sin(angle)),
                Angle = (float)angle - inc * (count - 1) / 2,
                Damage = (short)dmg,
                BulletType = (byte)desc.BulletType,
                AngleInc = inc,
                NumShots = (byte)count,
                ObjectType = host.ObjectType,
                ProjectileDesc = desc,
            }, host);
        }
    }
}
