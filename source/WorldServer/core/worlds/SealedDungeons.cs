using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldServer.core.worlds
{
    // Dungeons closed to players for now (admins still get in). For each: the portal that leads
    // there, the item that opens that portal (refused before it's spent, and not sold), and what a
    // player is told. Remove an entry to open its dungeon again.
    public static class SealedDungeons
    {
        private static readonly (string Dungeon, string Portal, string Item, string Why)[] Sealed =
        {
            // Oryx the Mad God 5: being reworked (his dungeon is rebuilt, his attacks and drops are next)
            ("Oryx's Elysium", "Oryx's Elysium Portal", "Doomed Cellar Incantation",
                "Oryx's Elysium is sealed for now: Oryx the Ascended God is being reworked."),
        };

        public static string WhyDungeon(string dungeon) =>
            Sealed.FirstOrDefault(s => string.Equals(s.Dungeon, dungeon, StringComparison.OrdinalIgnoreCase)).Why;

        public static string WhyPortal(string portalId) =>
            Sealed.FirstOrDefault(s => string.Equals(s.Portal, portalId, StringComparison.OrdinalIgnoreCase)).Why;

        public static bool IsSealedItem(string itemId) =>
            Sealed.Any(s => string.Equals(s.Item, itemId, StringComparison.OrdinalIgnoreCase));
    }
}
