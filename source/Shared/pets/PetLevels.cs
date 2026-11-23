using System;

namespace Shared.pets
{
    // Pet levels (Player.PetPen.cs, the Pen Keeper's window, PetLevelsTests): every pet hatches at
    // level 1 and grows by being fed (PetFood) up to its size's last level. Its damage, healing and
    // look grow with it, the same way for every pet:
    // - damage: 60% of its kit's (the numbers in its behaviour and projectiles, its generator's
    //   petDps) at level 1, up to 100% at its last (since 2.6.5; only Huge pets go on to
    //   120%, Power);
    // - healing: its generator's petHp and petMp (each pet its own) at level 1, up to 140% at its
    //   last (Huge 160%), as often;
    // - drawn up to a sixth bigger (Huge a quarter).
    // A pet at its last level can be grown to the next size (GrowPrice, GrownSize): it stays at
    // 100% until it's Huge, then climbs on to 120%, so growing never weakens it.
    // On top of that, every pet, levelled or not, does EffectivePercent (90%) of that
    // damage, healing and bleeding (since 2.6.4).
    // Feeding costs fame, more the stronger the pet is: 3 for each 100 food it eats at level 1,
    // 15% more at each level after (FameCost). From level 1 to its last that's about 185 fame for a
    // Tiny pet, 690 Small, 1,780 Medium, 3,710 Large and 6,820 Huge.
    // The Rust client works the same numbers out for its tooltips (client-rs/app/src/petlevels.rs):
    // change both together.
    public static class PetLevels
    {
        public static readonly string[] Sizes = { "Tiny", "Small", "Medium", "Large", "Huge" };
        // each size's last level
        private static readonly int[] MaxLevels = { 10, 15, 20, 25, 30 };

        public const float DamageAtFirst = 0.6f, DamageAtLast = 1.2f;
        public const float HealAtLast = 1.6f;
        public const float GrowAtLast = 1.25f;
        // a whole percent, so heals and bleeds round the same here, in the client and in the wiki
        // (0.9 isn't exact in a float: 15 x 0.9f falls just short of 13.5)
        public const int EffectivePercent = 90;
        public const float Effectiveness = EffectivePercent / 100f;

        /// <summary>A pet's heal of HP or MP, or its bleed, as it really does it: times
        /// EffectivePercent, rounded half away from zero, in whole numbers.</summary>
        public static int Effective(int amount) => (amount * EffectivePercent + (amount < 0 ? -50 : 50)) / 100;

        /// <summary>The size's index in Sizes, -1 for none (an unsized pet doesn't level).</summary>
        public static int SizeIndex(string size) => size == null ? -1 : Array.IndexOf(Sizes, size);

        /// <summary>The last level a pet of this size reaches (1 for an unsized pet).</summary>
        public static int MaxLevel(string size)
        {
            var i = SizeIndex(size);
            return i < 0 ? 1 : MaxLevels[i];
        }

        /// <summary>The food it takes from `level` to the next: 30 x level^1.5, to the nearest 5.</summary>
        public static int XpToNext(int level) => level < 1 ? 0 : (int)Math.Round(30 * Math.Pow(level, 1.5) / 5) * 5;

        /// <summary>The food a pet has eaten when it reaches `level`.</summary>
        public static int XpAt(int level)
        {
            var xp = 0;
            for (var l = 1; l < level; l++)
                xp += XpToNext(l);
            return xp;
        }

        /// <summary>The level `xp` of food makes, no higher than `max`.</summary>
        public static int LevelFor(int xp, int max)
        {
            var level = 1;
            while (level < max && xp >= XpAt(level + 1))
                level++;
            return level;
        }

        // how far along a pet is, 0 at level 1 to 1 at its last
        private static float Along(int level, int max) => max <= 1 ? 1 : Math.Clamp((level - 1) / (float)(max - 1), 0, 1);

        // the share of the climb from 60% to 120% damage that 100% is: only Huge pets go past
        public const float FullShare = (1 - DamageAtFirst) / (DamageAtLast - DamageAtFirst);

        /// <summary>How far up its power a pet of last level `max` is at `level`, 0 to 1 (120%
        /// damage). `baseMax` is its stone's own last level (0: `max`, a pet never grown). Every
        /// size but Huge stops at 100% damage (FullShare), at its stone's last level, and a grown
        /// pet stays there until it's Huge; a Huge pet climbs on to 120% at its last
        /// (from 100% at its stone's last level, if it was grown), so growing never weakens a pet.</summary>
        public static float Power(int level, int max, int baseMax = 0)
        {
            if (max <= 1)
                return 1;
            if (baseMax <= 1 || baseMax > max)
                baseMax = max;
            var oryx = max >= MaxLevels[^1];
            if (oryx && baseMax == max)
                return Along(level, max);
            if (!oryx || level <= baseMax)
                return FullShare * Along(level, baseMax);
            return FullShare + (1 - FullShare) * Math.Clamp((level - baseMax) / (float)(max - baseMax), 0, 1);
        }

        /// <summary>Its damage, times its kit's (see Power).</summary>
        public static float DamageScale(int level, int max, int baseMax = 0) =>
            max <= 1 ? 1 : DamageAtFirst + (DamageAtLast - DamageAtFirst) * Power(level, max, baseMax);

        /// <summary>Its healing, times its generator's.</summary>
        public static float HealScale(int level, int max, int baseMax = 0) => max <= 1 ? 1 : 1 + (HealAtLast - 1) * Power(level, max, baseMax);

        /// <summary>How big it's drawn, times its own size.</summary>
        public static float DrawScale(int level, int max, int baseMax = 0) => max <= 1 ? 1 : 1 + (GrowAtLast - 1) * Power(level, max, baseMax);

        /// <summary>A heal at `level`: the generator's amount, grown.</summary>
        public static int Heal(int amount, int level, int max, int baseMax = 0) => (int)Math.Round(amount * HealScale(level, max, baseMax));

        public const double FamePerFood = 0.03, FameGrowth = 0.15;

        /// <summary>The fame `food` costs a pet that has eaten `xp`, as it climbs: each bit of food
        /// at the level the pet is at as it eats it, only what it eats (none past its last
        /// level), rounded up.</summary>
        public static int FameCost(int xp, int food, int max)
        {
            if (max <= 1 || food <= 0)
                return 0;
            var end = Math.Min((long)xp + food, XpAt(max));
            var fame = 0.0;
            for (long at = xp; at < end;)
            {
                var level = LevelFor((int)at, max);
                var next = Math.Min(end, XpAt(level + 1));
                fame += (next - at) * FamePerFood * (1 + FameGrowth * (level - 1));
                at = next;
            }
            return (int)Math.Ceiling(fame - 1e-9);
        }

        // The pen: the Pen Keeper feeds only pets of the sizes the account's pen takes
        // (DbAccount.PenSize), Tiny at first; each bigger size is bought with fame or gold.
        private static readonly int[] PenFame = { 0, 2000, 5000, 10000, 20000 };
        private static readonly int[] PenGold = { 0, 200, 400, 700, 1000 };

        /// <summary>What making the pen take pets of size `size` (1 Small to 4 Huge) costs, in
        /// fame and in gold; (0, 0) past the last.</summary>
        public static (int Fame, int Gold) PenPrice(int size) =>
            size >= 1 && size < Sizes.Length ? (PenFame[size], PenGold[size]) : (0, 0);

        // Growing a pet (since 2.6.5): one at its size's last level can be grown to the next size,
        // for fame or gold, once per size (Tiny to Small 1,000 or 100 ... Large to Huge 25,000 or
        // 1,000). It keeps its kit and what it has eaten; its last level becomes the new size's
        // and it feeds on (if the pen takes its new size), at 100% (Power) until it's Huge
        // and climbs to 120%. It's drawn the new size's height.
        private static readonly int[] GrowFame = { 0, 1000, 3000, 8500, 25000 };
        private static readonly int[] GrowGold = { 0, 100, 200, 450, 1000 };

        /// <summary>What growing a pet to size `size` (1 Small to 4 Huge) costs, in fame and in
        /// gold; (0, 0) past the last.</summary>
        public static (int Fame, int Gold) GrowPrice(int size) =>
            size >= 1 && size < Sizes.Length ? (GrowFame[size], GrowGold[size]) : (0, 0);

        /// <summary>The size of a pet of its stone's size `size`, grown `grown` sizes (no bigger
        /// than Huge; an unsized pet stays unsized).</summary>
        public static string GrownSize(string size, int grown)
        {
            var i = SizeIndex(size);
            return i < 0 ? size : Sizes[Math.Clamp(i + Math.Max(0, grown), 0, Sizes.Length - 1)];
        }

        /// <summary>The food for a pet from before levels (2.6.4): one raised by the Pen Keeper
        /// (a Superburger or an Ambrosia) is at its last level; any other two thirds of the way,
        /// about as strong as it was.</summary>
        public static int MigratedXp(int oldRaise, int max)
        {
            if (max <= 1)
                return 0;
            if (oldRaise > 0)
                return XpAt(max);
            return XpAt((int)Math.Round(1 + (max - 1) * 2 / 3.0));
        }
    }
}
