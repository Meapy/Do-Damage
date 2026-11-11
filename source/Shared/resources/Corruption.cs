using System;

namespace Shared.resources
{
    // Corruption (the Cosmic Anvil, CorruptHandler.cs): an Unstable Essence moves each of a piece of
    // Enthroned gear's four values by 1 to 3, up or down at even odds (a value below 0 goes up 60%
    // of the time), each kept between -6 and +6. On a weapon, damage and
    // range change by that many percent, rate of fire by twice as many; on any gear, every stat it
    // raises when worn by that much. Stored in ItemData.Corruption (damage, range, rate of fire,
    // stats); the Rust client's bsk_data::imprint mirrors it. A staff may also turn its shots round
    // (ItemData.Unique = Reversed): a tenth of the time, each corruption.
    public static class Corruption
    {
        public const int Damage = 0, Range = 1, RateOfFire = 2, Stats = 3;
        public const int Min = -6, Max = 6, Step = 3;
        // the chance a value goes up: even, and a tenth more for one in the red
        public const double Up = 0.5, UpFromRed = 0.6;
        public const int Reversed = 1;
        public const double ReversedChance = 0.1;

        // the four values, 0 for none
        public static int[] Of(int[] stored)
        {
            var c = new int[4];
            if (stored != null)
                for (var i = 0; i < 4 && i < stored.Length; i++)
                    c[i] = Math.Clamp(stored[i], Min, Max);
            return c;
        }

        public static int[] Roll(int[] before, Random random)
        {
            var after = new int[4];
            for (var i = 0; i < 4; i++)
            {
                var up = random.NextDouble() < (before[i] < 0 ? UpFromRed : Up);
                var by = random.Next(1, Step + 1);
                after[i] = Math.Clamp(before[i] + (up ? by : -by), Min, Max);
            }
            return after;
        }

        public static bool Any(int[] stored) => stored != null && Array.Exists(stored, v => v != 0);
    }
}
