using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.database.leaderboard
{
    /// One climb of the Crucible: how many floors it cleared, how long it took to the last of them,
    /// who is on it, and whether it began as a group.
    public class DbCrucibleRun
    {
        public int Floors;
        public int Seconds;
        public long Time;
        public string[] Players;
        public bool Group;

        /// Begun alone; the leaderboard keeps solo and group climbs apart (a group's is a group
        /// climb even when one of it is left on the board).
        public bool Solo => !Group;
    }

    /// The Crucible's climbs (CrucibleWorld adds them; /crucible, the account server's
    /// /crucible/leaderboard, the Nexus's Crucible Leaderboard and the Hall of Champions read
    /// them): the sorted set `crucible.climbs`, scored by floors cleared plus a fraction below 0.1
    /// that's larger the quicker the climb, so the quickest of the highest ranks first. A member is
    /// "<unix ms>|<seconds>|<solo or group>|<players>", players joined by ", ": those there when its
    /// last floor fell who never fell themselves. A climb an admin was in isn't added. The board
    /// began again with 2.6.5: the climbs from before (admins' among them, and everyone who began
    /// one) are kept in `crucible.runs`, which nothing reads.
    public static class DbCrucibleRuns
    {
        public const string Key = "crucible.climbs";
        private const int MAX_SECONDS = 999_999;

        public static double Score(int floors, int seconds) =>
            floors + (MAX_SECONDS + 1 - Math.Clamp(seconds, 0, MAX_SECONDS)) / 10_000_000.0;

        public static string Member(long time, int seconds, IEnumerable<string> players, bool group) =>
            $"{time}|{Math.Clamp(seconds, 0, MAX_SECONDS)}|{(group ? "group" : "solo")}|{string.Join(", ", players.Select(p => (p ?? "").Replace("|", "")))}";

        public static void Add(IDatabase db, int floors, int seconds, IEnumerable<string> players, bool group) =>
            db.SortedSetAdd(Key, Member(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), seconds, players, group), Score(floors, seconds), CommandFlags.FireAndForget);

        public static List<DbCrucibleRun> Top(IDatabase db, int count) =>
            db.SortedSetRangeByRankWithScores(Key, 0, Math.Max(1, count) - 1, Order.Descending)
                .Select(e => Parse(e.Element, e.Score)).ToList();

        /// The best solo (or group) climbs, best first, from the top `scan`.
        public static List<DbCrucibleRun> Top(IDatabase db, int count, bool solo, int scan = 1000) =>
            Top(db, scan).Where(r => r.Solo == solo).Take(Math.Max(1, count)).ToList();

        /// How many solo (or group) climbs, of the top `scan`, rank above one of `floors` in
        /// `seconds`: 0 when it would be the best.
        public static int RankOf(IDatabase db, int floors, int seconds, bool group, int scan = 1000)
        {
            var score = Score(floors, seconds);
            return Top(db, scan).Count(r => r.Group == group && Score(r.Floors, r.Seconds) > score);
        }

        public static DbCrucibleRun Parse(string member, double score)
        {
            var parts = member.Split('|');
            var run = new DbCrucibleRun { Floors = (int)Math.Floor(score), Seconds = -1, Players = Array.Empty<string>() };
            if (parts.Length >= 3)
            {
                long.TryParse(parts[0], out run.Time);
                int.TryParse(parts[1], out run.Seconds);
                var kinded = parts.Length >= 4 && parts[2] is "solo" or "group";
                run.Players = string.Join("|", parts.Skip(kinded ? 3 : 2)).Split(", ", StringSplitOptions.RemoveEmptyEntries);
                run.Group = kinded ? parts[2] == "group" : run.Players.Length > 1;
            }
            return run;
        }
    }
}
