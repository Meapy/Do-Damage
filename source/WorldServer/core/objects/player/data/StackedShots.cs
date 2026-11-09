using System;
using System.Collections.Generic;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        /// <summary>
        /// Stacked shots: monsters' bullets that fly on top of each other (two Shoots of one
        /// monster firing the same bullet on the same tick, or monsters standing in one spot), so
        /// the player sees one bullet. Each used to deal its full damage, so a bullet that looked
        /// like 120 could deal 600. A stack now hits as one shot: the strongest of its bullets.
        /// The client's `combat.rs` (`stacked_on`) works it out the same way, for the numbers it
        /// shows.
        /// </summary>
        public static class StackedShots
        {
            /// Fired within this long of each other (ms of the client's clock): the same server tick.
            public const int FIRED_WITHIN_MS = 40;
            /// Hit the player within this long of each other.
            public const int HIT_WITHIN_MS = 250;
            /// No further apart than this (tiles) where they are when the later one hits.
            public const double OVERLAP_TILES = 0.4;
            /// Fired no more than this far apart in direction (radians, about 4 degrees), so they
            /// fly together rather than cross.
            public const double SAME_ANGLE = 4 * Math.PI / 180;

            public static bool Stacked(ValidatedProjectile a, ValidatedProjectile b, int time)
            {
                if (a.ProjectileDesc.ObjectId != b.ProjectileDesc.ObjectId)
                    return false;
                if (Math.Abs(a.Time - b.Time) > FIRED_WITHIN_MS)
                    return false;
                var da = Math.Abs(a.Angle - b.Angle) % (2 * Math.PI);
                if (Math.Min(da, 2 * Math.PI - da) > SAME_ANGLE)
                    return false;
                var pa = a.GetPosition(Math.Max(0, time - a.Time), a.BulletId, a.ProjectileDesc, 1f);
                var pb = b.GetPosition(Math.Max(0, time - b.Time), b.BulletId, b.ProjectileDesc, 1f);
                var dx = pa.X - pb.X;
                var dy = pa.Y - pb.Y;
                return dx * dx + dy * dy <= OVERLAP_TILES * OVERLAP_TILES;
            }
        }

        /// A bullet that hurt the player lately, and the damage its stack has dealt so far.
        private sealed class RecentHit
        {
            public ValidatedProjectile Projectile;
            public int Time;
            public int Dealt;
        }

        private readonly List<RecentHit> RecentHits = new List<RecentHit>();

        /// How much of `dmg` a monster's bullet deals: all of it, unless it's stacked on a bullet
        /// that hit us a moment ago, then only what it deals beyond that stack's damage so far.
        private int UnstackedDamage(ValidatedProjectile projectile, int dmg, int time, out bool stacked)
        {
            RecentHits.RemoveAll(h => time - h.Time > StackedShots.HIT_WITHIN_MS || h.Time > time);
            foreach (var h in RecentHits)
                if (StackedShots.Stacked(h.Projectile, projectile, time))
                {
                    stacked = true;
                    var extra = Math.Max(0, dmg - h.Dealt);
                    h.Dealt += extra;
                    return extra;
                }
            stacked = false;
            RecentHits.Add(new RecentHit { Projectile = projectile, Time = time, Dealt = dmg });
            return dmg;
        }
    }
}
