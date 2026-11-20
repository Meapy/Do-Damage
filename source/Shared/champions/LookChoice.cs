namespace Shared.champions
{
    // Where a player's chat name colour and glow come from, as chosen in the client's Customize
    // window (QuestTracker's COLOR and GLOW claims), kept on the account (DbAccount.NameFrom,
    // GlowFrom). A choice whose source the player no longer has (a Hall of Champions place lost)
    // falls back to the usual order, and comes back with the place.
    public static class LookChoice
    {
        // the name colour: Auto is the Hall of Champions' (Achievements board) while the place is
        // held, else the one chosen (an achievement rank's colour, /namecolor); Chosen always
        // that one
        public const int NameAuto = 0, NameChosen = 1;

        // the glow: Auto is the player's own (/glow), then a champion's (Stars board), then the
        // Arena's (the #1 solo or group run of a past Arena season, ArenaFirsts), then a legend's
        // (a character on the Legends, the dead's board); or one of them; or none
        public const int GlowAuto = 0, GlowChampion = 1, GlowLegend = 2, GlowOwn = 3, GlowNone = 4, GlowArena = 5;

        public const int LegendGlow = 0xFF0000;
        // hot pink
        public const int ArenaGlow = 0xFF69B4;

        public static int NameColor(int from, int champion, int chosen) =>
            from != NameChosen && champion != 0 ? champion : chosen;

        public static int Glow(int from, int own, int champion, bool legend, bool arena = false)
        {
            switch (from)
            {
                case GlowNone: return 0;
                case GlowChampion when champion != 0: return champion;
                case GlowLegend when legend: return LegendGlow;
                case GlowOwn when own != 0: return own;
                case GlowArena when arena: return ArenaGlow;
            }
            return own != 0 ? own : champion != 0 ? champion : arena ? ArenaGlow : legend ? LegendGlow : 0;
        }

        // "auto", "champion", "legend", "own", "none", "arena": the GLOW claim's id; -1 if none of them
        public static int GlowNamed(string name) => name switch
        {
            "auto" => GlowAuto,
            "champion" => GlowChampion,
            "legend" => GlowLegend,
            "own" => GlowOwn,
            "none" => GlowNone,
            "arena" => GlowArena,
            _ => -1,
        };
    }
}
