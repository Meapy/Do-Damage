using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.database.leaderboard
{
    /// One finished arena game, as the arena leaderboard shows it.
    public class DbArenaRun
    {
        public int Wave;
        /// From the first wave to the end; -1 for runs recorded before times were kept.
        public int Seconds;
        /// When it ended, in unix ms.
        public long Time;
        /// "Arena", "Battle Arena" or "Free Battle Arena"; empty for older runs.
        public string Arena;
        public string[] Players;
    }

    /// The arena leaderboard: a sorted set of runs (the world server's arenas add them; /leaderboard
    /// and the account server's /arena/leaderboard read them). The score is the wave reached plus a
    /// fraction below 0.1 that is larger the quicker the run, so of two runs to the same wave the
    /// quicker one ranks first. A member is "<unix ms>|<seconds>|<arena>|<players>", players joined
    /// by ", "; runs from before times were kept are "<unix ms>|<players>" with a whole score.
    public static class DbArenaRuns
    {
        public const string Key = "doomed.arenaLeaderboard";
        private const int MAX_SECONDS = 999_999;

        public static void Add(IDatabase db, int wave, int seconds, string arena, IEnumerable<string> players)
        {
            seconds = Math.Clamp(seconds, 0, MAX_SECONDS);
            var names = string.Join(", ", players.Select(p => p.Replace("|", "")));
            var member = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{seconds}|{arena}|{names}";
            var score = wave + (MAX_SECONDS + 1 - seconds) / 10_000_000.0;
            db.SortedSetAdd(Key, member, score, CommandFlags.FireAndForget);
        }

        public static List<DbArenaRun> Top(IDatabase db, int count) =>
            db.SortedSetRangeByRankWithScores(Key, 0, Math.Max(1, count) - 1, Order.Descending)
                .Select(e => Parse(e.Element, e.Score)).ToList();

        public static DbArenaRun Parse(string member, double score)
        {
            var parts = member.Split('|');
            var run = new DbArenaRun { Wave = (int)Math.Floor(score), Seconds = -1, Arena = "" };
            long.TryParse(parts[0], out run.Time);
            string players;
            if (parts.Length >= 4 && int.TryParse(parts[1], out var seconds))
            {
                run.Seconds = seconds;
                run.Arena = parts[2];
                players = string.Join("|", parts.Skip(3));
            }
            else
                players = parts.Length > 1 ? string.Join("|", parts.Skip(1)) : "";
            run.Players = players.Split(", ", StringSplitOptions.RemoveEmptyEntries);
            return run;
        }

        /// "12:05" or "1:02:05"; "?" when it wasn't kept.
        public static string FormatTime(int seconds)
        {
            if (seconds < 0)
                return "?";
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }
    }
}
