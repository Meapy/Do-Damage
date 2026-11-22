using System;
using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // A Mechanic's turret (Player.Turrets.cs builds it where its blueprint was aimed). It stands
    // where it landed and fires its object's projectile 0 at the nearest enemy in reach (how far the
    // shot flies), `shots` side by side every `fireMs` (the blueprint's allyShots and allyMs),
    // leading a moving target, and keeps to its target while it's in reach. While its Mechanic is
    // within LINK_RANGE it's overclocked: it fires OVERCLOCK_PERCENT as often. When its time is up,
    // or a newer turret past the blueprint's `amount` takes its place, it blows up: `totalDamage`
    // to every enemy within `radius`. Its Mechanic leaving the world or dying packs it away.
    //
    // The shots are its Mechanic's (ServerPlayerShoot, ids from the Mechanic's ability range, as
    // AllyShoot's), so their hits count as theirs. Its look is the Rust client's
    // (client-rs/app/src/particles/turret.rs, ShowEffect Relic looks 54 to 57): thrown as a crate
    // (54, sent by Player.Turrets.cs), built (55, with its reach in pos2.x), already up when a
    // player came into view (56) and its blast (57, the radius in pos2.x); 55 and 56 carry the time
    // left in Duration, its Mechanic in SourceId and its blast's radius in hundredths of a tile in
    // ObjectType, for the plate the client draws under it and the link to its Mechanic. Older
    // clients see the turret and its shots.
    //
    // A turret may also slam the ground (Ferrogrim's Bastion Turret: Player.Turrets.cs gives it its
    // `Slam`, the numbers in Player.CruciblePowers.cs): every EveryMs, Damage to the enemies within
    // Radius (its Mechanic's, as its shots are), their armor broken for BrokenMs, and the players
    // within it Armored until the next; its look is the Crucible's (ShowEffect Relic `Look`,
    // objectType 1).
    internal class Turret : StaticObject
    {
        public const int LOOK_THROW = 54;
        public const int LOOK_BUILD = 55;
        public const int LOOK_SEEN = 56;
        public const int LOOK_BLAST = 57;

        // how near its Mechanic overclocks it, and how much faster it fires then (the Rust client
        // draws the link to the same range: turret.rs LINK_RANGE)
        public const float LINK_RANGE = 7;
        public const int OVERCLOCK_PERCENT = 150;

        // how far apart its barrels' shots fly, tiles; how far ahead of it they start
        private const float BARREL_GAP = 0.32f;
        private const float MUZZLE = 0.5f;   // the barrel tip of the head the Rust client turns (draw_turrets.py)
        // the furthest it leads a moving target, tiles
        private const float MAX_LEAD = 3;

        // each tier's colour (its blueprint's Tier, 0 to 8), in its effects; the Rust client draws
        // each tier's plate by it (turret.rs `style`): scrap, iron, steel, leaf, sea, hellfire,
        // light, jade and storm; WAR_COLOR the War Turret's brass
        public static readonly uint[] COLORS =
        {
            0xFFB07840, 0xFF9AA8B8, 0xFF58B8FF, 0xFF7CD050, 0xFF40E0D0,
            0xFFFF4020, 0xFFFFE890, 0xFF40D8A0, 0xFF9A8CFF,
        };
        public const uint WAR_COLOR = 0xFFFFB030;

        public static uint ColorOf(Item item) => item.Tier >= 0 && item.Tier < COLORS.Length ? COLORS[item.Tier] : WAR_COLOR;

        private readonly Player owner;
        private readonly ProjectileDesc shot;
        private readonly int shots;
        private readonly int fireMs;
        private readonly int lifetime;
        private readonly int blastDamage;
        private readonly float blastRadius;
        private readonly float reach;
        private readonly uint color;
        private readonly HashSet<int> shown = new();
        private int t;
        private int cool;
        private int look;
        private bool raised;
        private Enemy target;
        private float lastX, lastY, vx, vy;

        // blown up or packed away (or never landed): out of its Mechanic's count
        public bool Gone { get; private set; }

        public sealed record Slam(float Radius, int Damage, int EveryMs, int BrokenMs, int Look, uint Color);

        private Slam slam;
        private int slamCool;

        public void SetSlam(Slam s)
        {
            slam = s;
            slamCool = s.EveryMs / 2;   // the first a moment after it stands
        }

        public Turret(Player owner, ushort objType, ActivateEffect eff, uint color)
            : base(owner.GameServer, objType, null, true, false, false)
        {
            this.owner = owner;
            this.color = color;
            shot = ObjectDesc.Projectiles.TryGetValue(0, out var desc) ? desc : null;
            shots = Math.Clamp(eff.AllyShots, 1, 6);
            fireMs = Math.Max(100, eff.AllyMs);
            lifetime = eff.DurationMS > 0 ? eff.DurationMS : 15000;
            blastDamage = eff.TotalDamage;
            blastRadius = eff.Radius;
            // how far its shot flies (speed is tenths of a tile a second)
            reach = shot == null ? 0 : shot.Speed * shot.LifetimeMS / 10000f;
            cool = 400;   // a moment to unfold before the first shot
        }

        public bool Linked => owner.World == World && owner.SqDistTo(this) <= LINK_RANGE * LINK_RANGE;

        private ShowEffect Look(int which, float radius) => new()
        {
            EffectType = EffectType.Relic,
            TargetObjectId = Id,
            Pos1 = new Position(X, Y),
            Pos2 = new Position(radius, which),
            Color = new ARGB(color),
            Duration = Math.Max(1, lifetime - t),
            ObjectType = (ushort)Math.Clamp(blastRadius * 100, 0, ushort.MaxValue),
            SourceId = owner.Id,
        };

        public override void Tick(ref TickTime time)
        {
            if (World == null || Gone)
                return;
            // its Mechanic gone: packed away
            if (owner.World != World || owner.Dead)
            {
                Gone = true;
                World.LeaveWorld(this);
                return;
            }

            ShowLooks();

            t += time.ElapsedMsDelta;
            if (t >= lifetime)
            {
                Detonate(ref time);
                return;
            }

            Track(time.ElapsedMsDelta);
            if (slam != null && (slamCool -= time.ElapsedMsDelta) <= 0)
            {
                slamCool = Math.Max(slamCool, -slam.EveryMs) + slam.EveryMs;
                SlamGround(ref time);
            }
            cool -= time.ElapsedMsDelta;
            if (cool <= 0)
            {
                if (Fire())
                {
                    var every = Linked ? fireMs * 100 / OVERCLOCK_PERCENT : fireMs;
                    cool = Math.Max(cool, -every) + every;
                }
                else
                    cool = 0;
            }

            base.Tick(ref time);
        }

        // a player sees it: within their sight, which is what the client is sent objects in
        private bool Sees(Player p)
        {
            var r = p.SightRadius + 1;
            return p.SqDistTo(this) < r * r;
        }

        // the look: to everyone who sees it as it's built, then to whoever comes to see it (again:
        // a player who went out of sight lost it with the turret)
        private void ShowLooks()
        {
            if (!raised)
            {
                raised = true;
                var build = Look(LOOK_BUILD, reach);
                foreach (var p in World.Players.Values)
                    if (Sees(p))
                    {
                        p.Client.SendPacket(build);
                        shown.Add(p.Id);
                    }
                return;
            }
            if (t / 500 < look)
                return;
            look = t / 500 + 1;
            ShowEffect seen = null;
            var here = new HashSet<int>();
            foreach (var p in World.Players.Values)
            {
                if (!Sees(p))
                    continue;
                here.Add(p.Id);
                if (!shown.Contains(p.Id))
                    p.Client.SendPacket(seen ??= Look(LOOK_SEEN, reach));
            }
            shown.Clear();
            shown.UnionWith(here);
        }

        private static bool Shootable(Enemy e) =>
            !e.Dead && e.World != null && e.AllyOwnerId == -1 && e.ObjectDesc.Enemy
            && !e.HasConditionEffect(ConditionEffectIndex.Invincible);

        // keeps to its target while it's in reach, else takes the nearest; and how fast it's moving
        private void Track(int dt)
        {
            if (target == null || !Shootable(target) || target.World != World || target.SqDistTo(this) > reach * reach)
            {
                target = this.GetNearestEntity(reach, false, e => e is Enemy en && Shootable(en)) as Enemy;
                if (target != null)
                {
                    (lastX, lastY) = (target.X, target.Y);
                    (vx, vy) = (0, 0);
                }
                return;
            }
            if (dt <= 0)
                return;
            // tiles a millisecond, smoothed over a few ticks
            var (nx, ny) = ((target.X - lastX) / dt, (target.Y - lastY) / dt);
            vx = vx * 0.5f + nx * 0.5f;
            vy = vy * 0.5f + ny * 0.5f;
            (lastX, lastY) = (target.X, target.Y);
        }

        private bool Fire()
        {
            if (shot == null || target == null || HasConditionEffect(ConditionEffectIndex.Stunned))
                return false;

            // lead it by where it will be when the shot gets there
            var dist = (float)Math.Sqrt(target.SqDistTo(this));
            var flight = dist / (shot.Speed / 10000f);
            var (lx, ly) = (vx * flight, vy * flight);
            var lead = (float)Math.Sqrt(lx * lx + ly * ly);
            if (lead > MAX_LEAD)
                (lx, ly) = (lx * MAX_LEAD / lead, ly * MAX_LEAD / lead);
            var (tx, ty) = (target.X + lx, target.Y + ly);
            if ((tx - X) * (tx - X) + (ty - Y) * (ty - Y) > reach * reach)
                (tx, ty) = (target.X, target.Y);
            var angle = (float)Math.Atan2(ty - Y, tx - X);

            var (cos, sin) = ((float)Math.Cos(angle), (float)Math.Sin(angle));
            var bulletId = owner.GetNextBulletId(shots, true);
            var damage = Random.Shared.Next(shot.MinDamage, shot.MaxDamage + 1);
            var shoots = new List<ServerPlayerShoot>(shots);
            for (var i = 0; i < shots; i++)
            {
                // side by side across the line of fire, so all of a volley reaches what it's aimed at
                var side = (i - (shots - 1) / 2f) * BARREL_GAP;
                var at = new Position(X + cos * MUZZLE - sin * side, Y + sin * MUZZLE + cos * side);
                shoots.Add(new ServerPlayerShoot(owner.Id, bulletId + i, ObjectType, at, angle, damage, shot));
            }
            World.BroadcastServerPlayerShoot(shoots, this);
            return true;
        }

        private void SlamGround(ref TickTime time)
        {
            var at = new Position(X, Y);
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at, Pos2 = new Position(slam.Radius, slam.Look),
                Color = new ARGB(slam.Color), ObjectType = 1, SourceId = owner.Id,
            }, this);
            var now = time;
            var s = slam;
            this.AOE(s.Radius, false, e =>
            {
                if (e is Enemy en && Shootable(en))
                {
                    en.Damage(owner, ref now, s.Damage, false);
                    if (!en.Dead)
                        en.ApplyConditionEffect(ConditionEffectIndex.ArmorBroken, s.BrokenMs);
                }
            });
            foreach (var p in World.Players.Values)
                if (!p.Dead && p.SqDistTo(this) <= s.Radius * s.Radius)
                    p.ApplyConditionEffect(ConditionEffectIndex.Armored, s.EveryMs + 200);
        }

        // its blast: `blastDamage` to the enemies within `blastRadius`, its look to everyone in view
        public void Detonate(ref TickTime time)
        {
            if (Gone)
                return;
            Gone = true;
            if (World == null)
                return;
            World.BroadcastIfVisible(Look(LOOK_BLAST, blastRadius), this);
            if (blastDamage > 0 && owner.World == World)
            {
                var now = time;
                this.AOE(blastRadius, false, e =>
                {
                    if (e is Enemy en && Shootable(en))
                        en.Damage(owner, ref now, blastDamage, false);
                });
            }
            World.LeaveWorld(this);
        }

        // thrown but never landed (its Mechanic left the world while it flew)
        public void Cancel() => Gone = true;
    }
}
