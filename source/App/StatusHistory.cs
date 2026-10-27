using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Timers;

namespace App
{
    // The website's server status history: once a minute, how many players are online and whether
    // a public world server is up (listed, so taking players), kept in Redis so it lasts through
    // deploys and restarts.
    //   status:players  sorted set, score the minute (unix time / 60), member "<minute>:<players>";
    //                   the last 24 hours, for the peak.
    //   status:up       hash, a UTC day ("yyyy-MM-dd") to the minutes it was up; 30 days kept.
    //   status:since    the first minute ever sampled: minutes before it are "no data", minutes
    //                   after it that weren't sampled (the account server down) count as down.
    //   status:minute:<minute>  taken by whoever samples that minute, so two account servers (one
    //                   restarting) never count a minute twice.
    public sealed class StatusHistory
    {
        public const int Days = 7;
        private const int KeepDays = 30;

        private readonly CoreService _core;
        private readonly Timer _timer = new Timer(20000);

        public StatusHistory(CoreService core)
        {
            _core = core;
            // The first sample waits for a timer tick, once the world servers have announced
            // themselves to this account server.
            _timer.Elapsed += (sender, e) => Sample();
            _timer.Start();
        }

        private IDatabase Db => _core.Database.Conn;

        private static long MinuteOf(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds() / 60;
        private static string DayOf(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Players online and whether a public world server is up, right now.
        public (bool Up, int Players) Current()
        {
            var servers = _core.GetServerList().Where(_ => !_.AdminOnly).ToList();
            var players = 0;
            foreach (var s in servers)
                if (s.UsageText.Contains('/') && int.TryParse(s.UsageText.Split('/')[0], out var n))
                    players += n;
            return (servers.Count > 0, players);
        }

        private void Sample()
        {
            try
            {
                var now = DateTime.UtcNow;
                var minute = MinuteOf(now);
                if (!Db.StringSet($"status:minute:{minute}", 1, TimeSpan.FromMinutes(5), When.NotExists))
                    return;

                var (up, players) = Current();
                Db.StringSet("status:since", minute, when: When.NotExists);
                Db.SortedSetAdd("status:players", $"{minute}:{players}", minute);
                Db.SortedSetRemoveRangeByScore("status:players", double.NegativeInfinity, minute - 24 * 60, Exclude.Stop);
                if (up)
                    Db.HashIncrement("status:up", DayOf(now));

                if (minute % 60 == 0)
                {
                    var oldest = DayOf(now.AddDays(-KeepDays));
                    var old = Db.HashKeys("status:up").Where(_ => string.CompareOrdinal(_, oldest) < 0).ToArray();
                    if (old.Length > 0)
                        Db.HashDelete("status:up", old);
                }
            }
            catch (Exception e)
            {
                _core.Logger.Warn(e, "Status sample failed");
            }
        }

        // Players now, the 24-hour peak and the last Days UTC days, oldest first, each with the
        // fraction of it that was up (null: no data yet).
        public object Report()
        {
            var now = DateTime.UtcNow;
            var nowMinute = MinuteOf(now);
            var (up, players) = Current();

            var peak = players;
            foreach (var e in Db.SortedSetRangeByScore("status:players", nowMinute - 24 * 60, double.PositiveInfinity))
            {
                var s = e.ToString();
                if (int.TryParse(s.Substring(s.IndexOf(':') + 1), out var n) && n > peak)
                    peak = n;
            }

            var since = Db.StringGet("status:since");
            var sinceMinute = since.HasValue ? (long)since : nowMinute + 1;
            var upMinutes = Db.HashGetAll("status:up").ToDictionary(_ => _.Name.ToString(), _ => (long)_.Value);

            var days = new List<object>();
            for (var i = Days - 1; i >= 0; i--)
            {
                var day = now.Date.AddDays(-i);
                var from = Math.Max(MinuteOf(day), sinceMinute);
                // The minute now isn't over (and may not be sampled yet), so it isn't counted.
                var to = Math.Min(MinuteOf(day.AddDays(1)), nowMinute);
                var date = DayOf(day);
                double? fraction = null;
                if (to > from)
                    fraction = Math.Min(1.0, (upMinutes.TryGetValue(date, out var m) ? m : 0) / (double)(to - from));
                days.Add(new { date, up = fraction.HasValue ? Math.Round(fraction.Value, 4) : (double?)null });
            }

            return new { online = up, players, peak24h = peak, days };
        }
    }
}
