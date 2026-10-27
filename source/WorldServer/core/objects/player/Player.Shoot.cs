using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared.resources;

namespace WorldServer.core.objects
{
    partial class Player
    {
        private int LastShootTime;
        private int? LastAbilityShootTime;
        private Dictionary<int, int> ProjectileCount = new Dictionary<int, int>();
        private Dictionary<int, int> AbilityProjectileCount = new Dictionary<int, int>();

        // The weapon (slot 0) and the ability (slot 1) are timed apart, as the client times them:
        // checked against the weapon's last shot, an ability's shots were refused while firing,
        // and so were the weapon's next shots after it.
        public bool IsValidShootTime(int time, Item item, int slot)
        {
            if (slot == 1)
            {
                // The client waits out the ability's cooldown between uses.
                if (LastAbilityShootTime == time)
                    return true;
                if (LastAbilityShootTime is int last && time < last + (int)(item.Cooldown * 1000))
                    return false;
                LastAbilityShootTime = time;
                return true;
            }

            if (LastShootTime == time)
                return true;
            var attackPeriod = (int)(1 / Stats.GetAttackFrequency() * 1 / (item.RateOfFire * WeaponImprints.RateMult));
            if (time < LastShootTime + attackPeriod)
                return false;
            LastShootTime = time;
            return true;
        }

        public bool IsValidNumShots(int time, Item item, int slot)
        {
            var counts = slot == 1 ? AbilityProjectileCount : ProjectileCount;
            foreach (var key in counts.Keys.Where(k => k < time - 1000).ToList())
                counts.Remove(key);

            if (!counts.ContainsKey(time))
                counts[time] = 0;

            return ++counts[time] <= item.NumProjectiles;
        }

        // The client rolls each shot's damage from the same sequence as Client.Random before the
        // server takes or refuses the shot, so a refused shot still uses up its roll. Otherwise
        // every roll after it is out of step: the client shows the wrong damage and predicts
        // kills that don't happen.
        public void SkipShotRoll(ProjectileDesc desc)
        {
            if (desc != null)
                Client.Random.NextIntRange((uint)desc.MinDamage, (uint)desc.MaxDamage);
        }

    }
}