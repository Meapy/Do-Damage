using System;
using System.Collections.Generic;
using System.Threading;
using Shared.champions;
using Shared.quests;
using WorldServer.utils;

namespace WorldServer.core.champions
{
    // The Hall of Champions as this world server knows it (Shared/champions): who holds a reward
    // (champions:rewards) and the boards' top 10s (champions:board), read from Redis once a minute
    // on a pool thread, so players entering worlds and the loot code never wait on Redis.
    // Players pick up a change within a few seconds of it arriving (Player.Champion).
    public static class ChampionCache
    {
        private static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
        private static Timer _timer;
        private static GameServer _server;

        private static volatile Dictionary<int, ChampionPlaces> _places = new Dictionary<int, ChampionPlaces>();
        public static volatile ChampionSnapshot Board;
        // hours added to the weekly boards' clock (a test server's /championsadmin week)
        public static double ClockHours;
        // goes up whenever the places change, so players know to look again
        public static int Version;

        public static int Week => QuestPeriods.WeekKey(DbChampions.Now(ClockHours));

        public static void Start(GameServer server)
        {
            _server = server;
            _timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, Every);
        }

        public static ChampionPlaces PlacesOf(int accountId) =>
            _places.TryGetValue(accountId, out var p) ? p : ChampionPlaces.None;

        // Read Redis again now (on the calling thread: pool threads and admin commands only).
        public static void Refresh()
        {
            try
            {
                var db = _server.Database.Conn;
                var places = DbChampions.ReadRewards(db);
                Board = DbChampions.ReadBoard(db);
                ClockHours = DbChampions.ReadClockHours(db);
                if (!Same(places, _places))
                {
                    _places = places;
                    Interlocked.Increment(ref Version);
                }
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Warn($"Champions: couldn't read the boards: {e.Message}");
            }
        }

        private static bool Same(Dictionary<int, ChampionPlaces> a, Dictionary<int, ChampionPlaces> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (var (id, p) in a)
                if (!b.TryGetValue(id, out var q) || !p.Equals(q))
                    return false;
            return true;
        }
    }

    // What one player did this week for the weekly boards, kept here and added to Redis once a
    // minute and when they leave the world (Client.Save), rather than at every kill.
    public sealed class ChampionCounter
    {
        private long _fame, _dungeons;
        private float _flushIn = 60f;

        public void Count(QuestEvent e)
        {
            if (e.Type == QuestEvents.Fame)
                _fame += e.Amount;
            else if (e.Type == QuestEvents.Dungeon)
                _dungeons += e.Amount;
        }

        public void Tick(float dt, Shared.database.Database db, int accountId)
        {
            _flushIn -= dt;
            if (_flushIn > 0)
                return;
            _flushIn = 60f;
            Flush(db, accountId);
        }

        public void Flush(Shared.database.Database db, int accountId)
        {
            if (_fame == 0 && _dungeons == 0)
                return;
            var week = ChampionCache.Week;
            DbChampions.AddWeekly(db.Conn, week, ChampionTab.WeeklyFame, accountId, _fame);
            DbChampions.AddWeekly(db.Conn, week, ChampionTab.WeeklyDungeons, accountId, _dungeons);
            _fame = _dungeons = 0;
        }
    }
}
