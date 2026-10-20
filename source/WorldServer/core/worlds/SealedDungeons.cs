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
            // Oryx the Mad God 5: his drops are being reworked
            ("Doomed Cellar", "Doomed Cellar Portal", "Doomed Cellar Incantation",
                "The Doomed Cellar is sealed for now: Oryx the Mad God 5 is being reworked."),
        };

        public static string WhyDungeon(string dungeon) =>
            Sealed.FirstOrDefault(s => string.Equals(s.Dungeon, dungeon, StringComparison.OrdinalIgnoreCase)).Why;

        public static string WhyPortal(string portalId) =>
            Sealed.FirstOrDefault(s => string.Equals(s.Portal, portalId, StringComparison.OrdinalIgnoreCase)).Why;

        public static bool IsSealedItem(string itemId) =>
            Sealed.Any(s => string.Equals(s.Item, itemId, StringComparison.OrdinalIgnoreCase));
    }
}
