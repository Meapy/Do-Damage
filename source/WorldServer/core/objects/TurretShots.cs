using System;
using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // Monsters' shots against the Mechanic's turrets (Turret.cs). Clients only tell the server when
    // a shot hits their own player, so the server follows each hostile monster's shot fired near a
    // turret itself, along the path the clients draw (ValidatedProjectile.GetPosition), in STEP_MS
    // steps: a wall or the edge of the ground stops it, and within HIT_RADIUS of a turret it hits
    // (Turret.Hurt, with its conditions; through it if it pierces). The Rust client stops the shot
    // on the turret too (combat.rs `hit_target`) and shows the hit from the DamageMessage.
    // World.cs hands it the shots (Fired) and ticks it.
    public sealed class TurretShots
    {
        // as the Rust client's combat.rs HIT_RADIUS
        private const float HIT_RADIUS = 0.5f;
        private const int STEP_MS = 20;
        // the most followed at once in a world, so a bullet storm can't run away with a tick
        private const int MAX_SHOTS = 2000;
        // the background of a world in open air (the Rust client's BACKGROUND_OPEN_AIR)
        private const byte OPEN_AIR = 9;

        private sealed class Shot
        {
            public Player.ValidatedProjectile Proj;
            public Entity Owner;
            public long Born;
            public int Checked;
            public HashSet<int> Hit;
        }

        private readonly World world;
        private readonly List<Shot> shots = new();

        public TurretShots(World world) => this.world = world;

        public void Fired(EnemyShootMessage shoot, Entity host)
        {
            var desc = shoot.ProjectileDesc;
            if (desc == null || host is not Enemy en || en.AllyOwnerId != -1 || shots.Count >= MAX_SHOTS)
                return;
            // only what could reach a turret
            var reach = desc.Speed * desc.LifetimeMS / 10000f + (desc.Parametric ? desc.Magnitude : 0) + 1;
            if (Turret.TurretsNear(world, shoot.StartingPos.X, shoot.StartingPos.Y, reach).Count == 0)
                return;
            var now = Environment.TickCount64;
            for (var i = 0; i < Math.Max(1, (int)shoot.NumShots); i++)
                shots.Add(new Shot
                {
                    Proj = new Player.ValidatedProjectile(shoot.OwnerId, shoot.BulletId + i, shoot.StartingPos,
                        shoot.Angle + shoot.AngleInc * i, shoot.ObjectType, shoot.Damage, Player.DamageType.Player, desc, shoot.Spawned),
                    Owner = host,
                    Born = now,
                });
        }

        public void Tick()
        {
            if (shots.Count == 0)
                return;
            var now = Environment.TickCount64;
            shots.RemoveAll(s => !Follow(s, (int)Math.Min(int.MaxValue, now - s.Born)));
        }

        // false when it's done: flown its time, stopped by a wall or spent on a turret
        private bool Follow(Shot s, int elapsed)
        {
            var desc = s.Proj.ProjectileDesc;
            var end = Math.Min(elapsed, (int)desc.LifetimeMS);
            while (s.Checked < end)
            {
                s.Checked = Math.Min(end, s.Checked + STEP_MS);
                var at = s.Proj.GetPosition(s.Checked, s.Proj.BulletId, desc, 1f);
                if (Blocked(at.X, at.Y, desc))
                    return false;
                foreach (var t in Turret.TurretsNear(world, at.X, at.Y, HIT_RADIUS * 1.5f))
                {
                    if (Math.Abs(t.X - at.X) > HIT_RADIUS || Math.Abs(t.Y - at.Y) > HIT_RADIUS)
                        continue;
                    if (s.Hit != null && s.Hit.Contains(t.Id))
                        continue;
                    t.Hurt(s.Proj.Damage, desc.ArmorPiercing, s.Owner, s.Proj.BulletId);
                    if (!t.Gone && desc.Effects != null)
                        t.ApplyConditionEffect(desc.Effects);
                    if (!desc.MultiHit)
                        return false;
                    (s.Hit ??= new HashSet<int>()).Add(t.Id);
                }
            }
            return elapsed < desc.LifetimeMS;
        }

        // what stops a monster's shot, as the Rust client has it: off the map, no ground, or an
        // object that takes up its square (unless the shot passes cover and it's only cover)
        private bool Blocked(float x, float y, ProjectileDesc desc)
        {
            if (!world.Map.Contains((int)x, (int)y))
                return true;
            var tile = world.GroundTile((int)x, (int)y);
            // no ground stops it, except in open air (the Crucible's sky), where it flies over the gap
            if (!tile.Exists || tile.TileId == 0xff)
                return world.Background != OPEN_AIR;
            if (tile.ObjType == 0 || tile.ObjDesc == null)
                return false;
            var od = tile.ObjDesc;
            return od.EnemyOccupySquare || (!desc.PassesCover && od.OccupySquare);
        }
    }
}
