using System;
using System.Threading;
using NLog;
using Shared.champions;
using Shared.quests;

namespace App
{
    // The Hall of Champions' rankings (Shared/champions): the account server ranks every account
    // every ChampionBoards.Interval, keeps the result for /champions/board, and writes who holds a
    // reward into Redis for the world servers. One account server runs, so one ranking does.
    // /championsadmin refresh (champions:refresh in Redis) asks for one at once; it's checked
    // every 15 s.
    public sealed class ChampionsService
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan Check = TimeSpan.FromSeconds(15);

        private readonly CoreService _core;
        private readonly ChampionSweep _sweep;
        private readonly Timer _timer;
        private int _running;
        private DateTime _next = DateTime.UtcNow.AddSeconds(5);

        // the latest ranking (null until the first is done)
        public volatile ChampionStandings Current;
        public DateTime Next => _next;

        public ChampionsService(CoreService core)
        {
            _core = core;
            var path = $"{core.Resources.ResourcePath}/data/quests.json";
            QuestData quests;
            try { quests = QuestData.Load(path); }
            catch (Exception e)
            {
                Log.Error($"Champions: can't read {path}: {e.Message}");
                quests = new QuestData();
            }
            _sweep = new ChampionSweep(core.Database.Conn, quests);
            _timer = new Timer(_ => Tick(), null, Check, Check);
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
                return;
            try
            {
                var db = _core.Database.Conn;
                var asked = db.KeyDelete(DbChampions.Refresh);
                if (!asked && DateTime.UtcNow < _next)
                    return;
                _next = DateTime.UtcNow + ChampionBoards.Interval;
                var started = DateTime.UtcNow;
                var standings = _sweep.Run(DbChampions.ReadClockHours(db), asked);
                DbChampions.Write(db, standings);
                Current = standings;
                Log.Info($"Champions: ranked in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms, {standings.Holders.Count} accounts hold a reward" + (asked ? " (asked for)" : ""));
            }
            catch (Exception e)
            {
                Log.Error($"Champions: ranking failed: {e}");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }
}
