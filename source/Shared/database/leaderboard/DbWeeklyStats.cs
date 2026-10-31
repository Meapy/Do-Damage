using Shared.quests;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.database.leaderboard
{
    /// The server's week, for the Herald in the Nexus (the account server's /stats/weekly): counts of
    /// what every player did together, and the monsters killed most. A week runs as the weekly quests'
    /// do (QuestPeriods, Monday 00:00 UTC). Kept in a hash of counts and a sorted set of kills by
    /// monster type, each keyed by the week's first day and dropped some weeks after it; the world
    /// servers add to them in batches (WorldServer's WeeklyStats), so several add safely.
    public static class DbWeeklyStats
    {
        // the counts, in the order the Herald shows them
        public const string Dungeons = "dungeons";
        public const string ChainsCompleted = "chainsCompleted";
        public const string ChainsFailed = "chainsFailed";
        public const string Deaths = "deaths";
        public const string Kills = "kills";
        public const string Events = "events";
        public const string RealmsClosed = "realmsClosed";
        public const string Potions = "potions";
        public const string Forged = "forged";
        public static readonly string[] All = { Dungeons, ChainsCompleted, ChainsFailed, Deaths, Kills, Events, RealmsClosed, Potions, Forged };

        private static readonly TimeSpan Keep = TimeSpan.FromDays(35);

        public static DateTime WeekStart(DateTime utc) => QuestPeriods.NextReset(QuestPeriod.Weekly, utc).AddDays(-7);

        private static string Suffix(DateTime utc) => WeekStart(utc).ToString("yyyyMMdd");
        private static string CountsKey(DateTime utc) => $"weeklyStats:{Suffix(utc)}";
        private static string KillsKey(DateTime utc) => $"weeklyKills:{Suffix(utc)}";

        /// Adds counts and kills (by monster type) to this week's.
        public static void Add(IDatabase db, IReadOnlyDictionary<string, long> counts, IReadOnlyDictionary<ushort, long> kills)
        {
            var now = DateTime.UtcNow;
            var batch = db.CreateBatch();
            var tasks = new List<System.Threading.Tasks.Task>();
            var counted = CountsKey(now);
            foreach (var (stat, n) in counts)
                tasks.Add(batch.HashIncrementAsync(counted, stat, n));
            var killed = KillsKey(now);
            foreach (var (type, n) in kills)
                tasks.Add(batch.SortedSetIncrementAsync(killed, type.ToString(), n));
            tasks.Add(batch.KeyExpireAsync(counted, Keep));
            tasks.Add(batch.KeyExpireAsync(killed, Keep));
            batch.Execute();
            System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
        }

        /// This week's counts (0 for those nothing added to yet), and the most killed monster types
        /// with their kills, most first.
        public static (Dictionary<string, long> Counts, (ushort Type, long Kills)[] Top) Read(IDatabase db, int top)
        {
            var now = DateTime.UtcNow;
            var counts = All.ToDictionary(s => s, _ => 0L);
            foreach (var e in db.HashGetAll(CountsKey(now)))
                if (long.TryParse(e.Value, out var n))
                    counts[e.Name] = n;
            var kills = db.SortedSetRangeByRankWithScores(KillsKey(now), 0, top - 1, Order.Descending)
                .Select(e => (ushort.TryParse(e.Element, out var t) ? t : (ushort)0, (long)e.Score))
                .Where(e => e.Item1 != 0)
                .ToArray();
            return (counts, kills);
        }
    }
}
