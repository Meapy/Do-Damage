using System;

namespace Shared.resources
{
    // Fame in a world with a FameRate or KillFameRate below 1 (WorldResource, World: 0.5 and 0.75
    // in the Arena and the Admin Arena). A character's base fame is its experience over 1000 (Player.CalculateFame),
    // so cutting the experience cuts the fame, and the death bonuses (FameStats), which are shares
    // of the base fame, with it.
    public static class FameRates
    {
        // the level from which experience only makes fame
        public const int MaxLevel = 20;

        // at the cap, every kill's experience (so its fame) is this much more
        public const double KillFameBonus = 1.1;

        // At the cap a boss (a <Quest/> monster: the Oryx chain, event bosses, dungeon bosses) gives
        // this many times its whole experience (its health over 10, times its XpMult) rather than the
        // per-kill cap of a tenth of a level (half for the player's own quest), up to BossMaxExperience:
        // Oryx 2, 75,000 health in the XML, then gives 15 fame, where the cap left him at 1.
        public const float BossExperience = 2;
        public const float BossMaxExperience = 50 * 1000;

        // a boss's experience at the cap, before any other bonus
        public static float Boss(float xp) => Math.Min(xp * BossExperience, BossMaxExperience);

        // a kill's experience: under the cap it levels the character as anywhere else; at the cap,
        // where it's only fame, KillFameBonus more, and that share of it
        public static float Experience(float xp, int level, double rate) =>
            level < MaxLevel ? xp : (float)(xp * KillFameBonus * rate);

        // fame given straight to the account (an Arena wave's): that share, rounded half up, so a
        // reward of any fame still gives some
        public static int Fame(int fame, double rate) =>
            (int)Math.Round(fame * rate, MidpointRounding.AwayFromZero);
    }
}
