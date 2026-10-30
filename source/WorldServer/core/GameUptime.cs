using System;
using StackExchange.Redis;
using WorldServer.utils;

namespace WorldServer.core
{
    /// <summary>
    /// How long the game has been up, for /uptime: since the last time no world server was running,
    /// not since this one started. A rolling deploy or scheduled restart starts the new server
    /// before the old one stops, so it keeps the time; stopping them all (or a crash) starts it again.
    ///   worlds:alive    refreshed every 15 s by each public world server, gone 60 s after the last
    ///                   one stops (at once when it stops cleanly without handing over)
    ///   worlds:upSince  unix seconds: when the first of an unbroken line of world servers started
    /// </summary>
    public sealed class GameUptime
    {
        private const string Alive = "worlds:alive";
        private const string UpSince = "worlds:upSince";
        private static readonly TimeSpan AliveFor = TimeSpan.FromSeconds(60);

        private readonly IDatabase _db;
        private DateTime _nextBeat;

        /// <summary>When the game came up (this server's start if Redis couldn't say).</summary>
        public DateTime Since { get; private set; } = DateTime.UtcNow;

        /// <summary>When this world server started.</summary>
        public DateTime ServerStarted { get; } = DateTime.UtcNow;

        public GameUptime(IDatabase db, bool enabled)
        {
            if (!enabled)
                return;
            _db = db;
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (!db.KeyExists(Alive))
                    db.StringSet(UpSince, now);
                else
                    db.StringSet(UpSince, now, when: When.NotExists);
                db.StringSet(Alive, 1, AliveFor);
                if (long.TryParse(db.StringGet(UpSince), out var since) && since <= now)
                    Since = DateTimeOffset.FromUnixTimeSeconds(since).UtcDateTime;
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Warn($"Uptime: {e.Message}");
            }
        }

        /// <summary>From the server's loop.</summary>
        public void Beat()
        {
            if (_db == null || DateTime.UtcNow < _nextBeat)
                return;
            _nextBeat = DateTime.UtcNow.AddSeconds(15);
            try { _db.StringSet(Alive, 1, AliveFor, flags: CommandFlags.FireAndForget); }
            catch (Exception) { }
        }

        /// <summary>Stopping without a server to take over: the game is down until the next starts.</summary>
        public void Stopped()
        {
            if (_db == null)
                return;
            try { _db.KeyDelete(Alive); }
            catch (Exception) { }
        }

        public static string Format(TimeSpan t) =>
            t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m"
            : t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m"
            : $"{t.Minutes}m {t.Seconds}s";
    }
}
