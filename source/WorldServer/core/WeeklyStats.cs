using NLog;
using Shared.database.leaderboard;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace WorldServer.core
{
    // The server's week, for the Herald in the Nexus (DbWeeklyStats): counted here as it happens,
    // from any world thread, and added to Redis every FlushSeconds and when the server stops, so a
    // kill costs nothing more than a dictionary increment.
    public static class WeeklyStats
    {
        private const int FlushSeconds = 30;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<string, long> Counts = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<ushort, long> Kills = new ConcurrentDictionary<ushort, long>();
        private static IDatabase _db;
        private static Timer _timer;

        public static void Start(IDatabase db)
        {
            _db = db;
            _timer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(FlushSeconds), TimeSpan.FromSeconds(FlushSeconds));
        }

        public static void Count(string stat, long n = 1) => Counts.AddOrUpdate(stat, n, (_, v) => v + n);

        public static void Killed(ushort type) => Kills.AddOrUpdate(type, 1, (_, v) => v + 1);

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            Flush();
        }

        private static void Flush()
        {
            if (_db == null)
                return;
            var counts = Take(Counts);
            var kills = Take(Kills);
            if (counts.Count == 0 && kills.Count == 0)
                return;
            try
            {
                DbWeeklyStats.Add(_db, counts, kills);
            }
            catch (Exception e)
            {
                Log.Warn(e, "Weekly stats: couldn't save this batch");
            }
        }

        // what's been counted, taken out (anything counted meanwhile waits for the next flush)
        private static System.Collections.Generic.Dictionary<T, long> Take<T>(ConcurrentDictionary<T, long> from)
        {
            var taken = new System.Collections.Generic.Dictionary<T, long>();
            foreach (var key in from.Keys.ToList())
                if (from.TryRemove(key, out var n) && n != 0)
                    taken[key] = n;
            return taken;
        }
    }
}
