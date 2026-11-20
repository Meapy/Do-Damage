using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;

namespace Shared.database.leaderboard
{
    /// The players who came first in an ended Arena season: everyone in its #1 solo run and its
    /// #1 group run. They may glow hot pink (LookChoice.GlowArena), for good. The seasons are over,
    /// so the names are read once per world server and kept; a run keeps names, not accounts, so
    /// an account renamed since finds its place again only under the name it ran with.
    public static class ArenaFirsts
    {
        /// The seasons whose #1s glow.
        public static readonly int[] Seasons = { 1 };

        private static readonly object _lock = new object();
        private static HashSet<int> _accounts;

        /// The names in the #1 solo and #1 group run of each season in Seasons.
        public static List<string> Names(IDatabase db) =>
            Seasons.Select(ArenaSeasons.Get).Where(s => s != null)
                .SelectMany(s => new[] { true, false }.SelectMany(solo => DbArenaRuns.Top(db, s, 1, solo)))
                .SelectMany(r => r.Players)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// Whether the account came first (reads Redis the first time).
        public static bool Holds(Database db, int accountId)
        {
            lock (_lock)
            {
                _accounts ??= Names(db.Conn).Select(db.ResolveId).Where(id => id != 0).ToHashSet();
                return _accounts.Contains(accountId);
            }
        }
    }
}
