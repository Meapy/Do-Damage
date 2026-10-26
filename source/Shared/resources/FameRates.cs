using System;

namespace Shared.resources
{
    // Fame in a world with a FameRate below 1 (WorldResource, World.FameRate: 0.5 in the Arena and
    // the Admin Arena). A character's base fame is its experience over 1000 (Player.CalculateFame),
    // so cutting the experience cuts the fame, and the death bonuses (FameStats), which are shares
    // of the base fame, with it.
    public static class FameRates
    {
        // the level from which experience only makes fame
        public const int MaxLevel = 20;

        // a kill's experience: under the cap it levels the character as anywhere else; at the cap,
        // where it's only fame, that share of it
        public static float Experience(float xp, int level, double rate) =>
            level < MaxLevel ? xp : (float)(xp * rate);

        // fame given straight to the account (an Arena wave's): that share, rounded half up, so a
        // reward of any fame still gives some
        public static int Fame(int fame, double rate) =>
            (int)Math.Round(fame * rate, MidpointRounding.AwayFromZero);
    }
}
