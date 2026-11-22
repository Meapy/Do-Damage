using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.database.leaderboard
{
    /// One climb of the Crucible: how many floors it cleared, how long it took, who climbed.
    public class DbCrucibleRun
    {
        public int Floors;
        public int Seconds;
        public long Time;
        public string[] Players;
    }

    /// The Crucible's climbs (CrucibleWorld adds them, /crucible reads them): the sorted set
    /// `crucible.runs`, scored by floors cleared plus a fraction below 0.1 that's larger the quicker
    /// the climb, so the quickest of the highest ranks first. A member is
    /// "<unix ms>|<seconds>|<players>", players joined by ", ".
    public static class DbCrucibleRuns
    {
        public const string Key = "crucible.runs";
        private const int MAX_SECONDS = 999_999;

        public static double Score(int floors, int seconds) =>
            floors + (MAX_SECONDS + 1 - Math.Clamp(seconds, 0, MAX_SECONDS)) / 10_000_000.0;

        public static void Add(IDatabase db, int floors, int seconds, IEnumerable<string> players)
        {
            var names = string.Join(", ", players.Select(p => (p ?? "").Replace("|", "")));
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.SortedSetAdd(Key, $"{now}|{Math.Clamp(seconds, 0, MAX_SECONDS)}|{names}", Score(floors, seconds), CommandFlags.FireAndForget);
        }

        public static List<DbCrucibleRun> Top(IDatabase db, int count) =>
            db.SortedSetRangeByRankWithScores(Key, 0, Math.Max(1, count) - 1, Order.Descending)
                .Select(e => Parse(e.Element, e.Score)).ToList();

        /// How many climbs rank above one of `floors` in `seconds`: 0 when it would be the best.
        public static long RankOf(IDatabase db, int floors, int seconds) =>
            db.SortedSetLength(Key, Score(floors, seconds), double.PositiveInfinity, Exclude.Start);

        public static DbCrucibleRun Parse(string member, double score)
        {
            var parts = member.Split('|');
            var run = new DbCrucibleRun { Floors = (int)Math.Floor(score), Seconds = -1, Players = Array.Empty<string>() };
            if (parts.Length >= 3)
            {
                long.TryParse(parts[0], out run.Time);
                int.TryParse(parts[1], out run.Seconds);
                run.Players = string.Join("|", parts.Skip(2)).Split(", ", StringSplitOptions.RemoveEmptyEntries);
            }
            return run;
        }
    }
}
