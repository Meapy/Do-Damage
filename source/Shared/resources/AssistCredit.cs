using System;

namespace Shared.resources
{
    // Assist credit: healing or buffing players who fight an enemy, or debuffing the enemy, earns
    // a share of its loot as damage does (DamageCounter, Loots.Handle). A heal or buff covers the
    // player for a while; each hit they land meanwhile gives their helper part of it as assist
    // credit. The enemy's TotalDamage stays damage only, so damage dealers' shares don't change.
    public static class AssistCredit
    {
        // the share of a covered player's hit their helper earns
        public const double HelpRate = 0.5;

        // the share of a hit on a debuffed enemy its debuffer earns (when someone else lands it)
        public const double DebuffRate = 0.25;

        // a heal covers for up to this long: the whole of it for healing FullHealShare of the
        // player's max or more, less for less, nothing for nothing healed (overheal)
        public const int HealWindowMs = 5000;
        public const double FullHealShare = 0.1;

        // a buff or debuff covers for its duration, but no longer than this
        public const int MaxEffectWindowMs = 10000;

        // assist credit counts for at most this share of the enemy's total damage
        public const double MaxShare = 0.2;

        public static int HealWindow(int healed, int maxHp)
        {
            if (healed <= 0 || maxHp <= 0)
                return 0;
            return (int)(HealWindowMs * Math.Min(1, healed / (maxHp * FullHealShare)));
        }

        public static int EffectWindow(int durationMs) => Math.Clamp(durationMs, 0, MaxEffectWindowMs);

        // what a player counts for in the loot: their damage and their assist credit, capped at
        // MaxShare of the total and at the top damage dealer's damage (unless they are that dealer)
        public static int Credit(int damage, double assist, int totalDamage, int topDamage)
        {
            if (assist <= 0 || totalDamage <= 0)
                return damage;
            var credit = damage + (int)Math.Min(assist, totalDamage * MaxShare);
            return Math.Min(credit, Math.Max(damage, topDamage));
        }
    }
}
