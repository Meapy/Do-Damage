using StackExchange.Redis;
using System;
using System.Linq;

namespace Shared.database.leaderboard
{
    /// One Arena season: its number, its name and the sorted set its runs go in (DbArenaRuns).
    public sealed class ArenaSeason
    {
        public readonly int Number;
        public readonly string Name;
        public readonly string Key;

        public ArenaSeason(int number, string name, string key)
        {
            Number = number;
            Name = name;
            Key = key;
        }
    }

    /// The Arena's seasons. Each has its own leaderboard; the last one is the season being played,
    /// and the ones before it are kept as they ended, to look back at (the Arena Leaderboard's
    /// season tabs, `/leaderboard <season>`). Season 1 is every run from before seasons (its key is
    /// the one the leaderboard always had). A season's start is kept in Redis (StartsKey) the first
    /// time a world server runs it; the one before ended then.
    public static class ArenaSeasons
    {
        public static readonly ArenaSeason[] All =
        {
            new ArenaSeason(1, "The Mad God's Trial", "doomed.arenaLeaderboard"),
            new ArenaSeason(2, "Rise of the Fallen", "doomed.arenaLeaderboard.s2"),
        };

        /// The season being played.
        public static ArenaSeason Current => All[All.Length - 1];

        /// A hash: season number to when it began, unix ms.
        public const string StartsKey = "doomed.arenaSeasons";

        /// The season by its number; null for one there isn't.
        public static ArenaSeason Get(int number) => All.FirstOrDefault(s => s.Number == number);

        /// The season a request asks for ("2"), or the current one for none or one there isn't.
        public static ArenaSeason Parse(string number) =>
            int.TryParse(number, out var n) ? Get(n) ?? Current : Current;

        /// Notes that the current season has begun, if it hadn't (the world server, on starting).
        public static void Begin(IDatabase db)
        {
            db.HashSet(StartsKey, Current.Number, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), When.NotExists);
        }

        /// When each season began and ended (unix ms; 0 for unknown, or not ended).
        public static (long Start, long End)[] Spans(IDatabase db)
        {
            var starts = db.HashGetAll(StartsKey).ToDictionary(e => (int)e.Name, e => (long)e.Value);
            return All.Select((s, i) => (
                starts.TryGetValue(s.Number, out var start) ? start : 0L,
                i + 1 < All.Length && starts.TryGetValue(All[i + 1].Number, out var end) ? end : 0L)).ToArray();
        }
    }
}
