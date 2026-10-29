using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Shared.quests;
using StackExchange.Redis;

namespace Shared.champions
{
    // The Hall of Champions in Redis:
    // - champions:rewards, a hash: account id -> its places in any top 10 (ChampionPlaces.Encode),
    //   rewritten whole by each ranking (the world servers read it every minute, ChampionCache);
    // - champions:board, a string: the boards' top 10s as JSON (ChampionSnapshot), for /champions;
    // - champions:week:<week>:<board>, sorted sets: what each account did this week (account id ->
    //   count), added to by the world servers (ChampionCounter) and kept three weeks;
    // - champions:clockHours, a string: hours added to the weekly boards' clock, for testing the
    //   weekly reset on a test server (/championsadmin week), read by both servers;
    // - champions:refresh, set by /championsadmin refresh: the account server ranks again at once;
    // - champions:guildDead:<year>, sorted sets: each guild's dead fame that year (guild id -> fame),
    //   added to by Database.Death; champions:guildDead:seeded marks the first year seeded with each
    //   guild's dead fame until then (its totalFame), so the board didn't start empty.
    public static class DbChampions
    {
        public const string Rewards = "champions:rewards";
        public const string Board = "champions:board";
        public const string ClockHours = "champions:clockHours";
        public const string Refresh = "champions:refresh";
        private static readonly TimeSpan WeekKept = TimeSpan.FromDays(21);

        public static string WeekKey(int week, ChampionTab tab) => $"champions:week:{week}:{ChampionBoards.Of(tab).Id}";

        public static string GuildDeadKey(int year) => $"champions:guildDead:{year}";

        private const string GuildDeadSeeded = "champions:guildDead:seeded";

        // when the yearly boards start afresh: 1 January after `now`, midnight UTC
        public static DateTime YearReset(DateTime now) => new DateTime(now.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // A guild member's character died: its fame goes on the guild's dead fame for the year (the
        // champions clock's year, as the weekly boards').
        public static void AddGuildDead(IDatabase db, int guildId, long fame)
        {
            if (guildId <= 0 || fame <= 0)
                return;
            db.SortedSetIncrementAsync(GuildDeadKey(Now(ReadClockHours(db)).Year), guildId, fame, CommandFlags.FireAndForget);
        }

        // The first time the board runs: this year starts with every guild's dead fame so far.
        public static void SeedGuildDead(IDatabase db, int year)
        {
            if (!db.StringSet(GuildDeadSeeded, year, when: When.NotExists))
                return;
            var last = (int)db.StringGet("nextGuildId");
            for (var id = 1; id <= last; id++)
            {
                var fame = db.HashGet($"guild.{id}", "totalFame");
                if (fame.HasValue && long.TryParse(fame.ToString(), out var f) && f > 0)
                    db.SortedSetIncrement(GuildDeadKey(year), id, f);
            }
        }

        // the weekly boards' clock: now, moved by champions:clockHours on a test server
        public static DateTime Now(double clockHours) => DateTime.UtcNow.AddHours(clockHours);

        public static double ReadClockHours(IDatabase db)
        {
            var v = db.StringGet(ClockHours);
            return v.HasValue && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : 0;
        }

        public static void SetClockHours(IDatabase db, double hours)
        {
            if (hours == 0)
                db.KeyDelete(ClockHours);
            else
                db.StringSet(ClockHours, hours.ToString(CultureInfo.InvariantCulture));
        }

        // What an account did this week, added (fire and forget).
        // Adds to a weekly board. `wait`: until Redis has it (a player leaving, maybe as the
        // server stops: a fire-and-forget write can be dropped as the process exits).
        public static void AddWeekly(IDatabase db, int week, ChampionTab tab, int accountId, long amount, bool wait = false)
        {
            if (amount <= 0)
                return;
            var key = WeekKey(week, tab);
            if (wait)
            {
                db.SortedSetIncrement(key, accountId, amount);
                db.KeyExpire(key, WeekKept);
                return;
            }
            db.SortedSetIncrementAsync(key, accountId, amount, CommandFlags.FireAndForget);
            db.KeyExpireAsync(key, WeekKept, CommandFlags.FireAndForget);
        }

        public static Dictionary<int, ChampionPlaces> ReadRewards(IDatabase db) =>
            db.HashGetAll(Rewards).ToDictionary(e => (int)e.Name, e => ChampionPlaces.Decode(e.Value));

        public static ChampionSnapshot ReadBoard(IDatabase db)
        {
            var json = db.StringGet(Board);
            if (!json.HasValue)
                return null;
            try { return JsonConvert.DeserializeObject<ChampionSnapshot>(json); }
            catch (JsonException) { return null; }
        }

        // A ranking's results: the rewards hash replaced in one step, and the top 10s.
        public static void Write(IDatabase db, ChampionStandings standings)
        {
            var entries = standings.Holders.Select(p => new HashEntry(p.Key, p.Value.Encode())).ToArray();
            if (entries.Length == 0)
                db.KeyDelete(Rewards);
            else
            {
                var temp = Rewards + ":new";
                db.KeyDelete(temp);
                db.HashSet(temp, entries);
                db.KeyRename(temp, Rewards);
            }
            db.StringSet(Board, JsonConvert.SerializeObject(ChampionSnapshot.Of(standings)));
        }
    }

    // A ranking of every board.
    public sealed class ChampionStandings
    {
        public DateTime Updated;
        public int Week;
        public DateTime WeekReset;
        public DateTime YearReset;
        // the ranked accounts of each board, in ChampionTab order
        public List<ChampionRanked>[] Boards;
        public Dictionary<int, ChampionPlaces> Holders;

        public ChampionStandings(DateTime updated, DateTime weekNow, List<ChampionRanked>[] boards)
        {
            Updated = updated;
            Week = QuestPeriods.WeekKey(weekNow);
            WeekReset = QuestPeriods.NextReset(QuestPeriod.Weekly, weekNow);
            YearReset = DbChampions.YearReset(weekNow);
            Boards = boards;
            Holders = ChampionBoards.Holders(boards);
        }
    }

    // The boards' top 10s (and when), as the world servers read them for /champions.
    public sealed class ChampionSnapshot
    {
        public long Updated;
        public long WeekReset;
        public int Week;
        public List<List<Entry>> Boards = new List<List<Entry>>();

        public sealed class Entry
        {
            public int Rank;
            public int AccountId;
            public string Name;
            public long Value;
        }

        public static ChampionSnapshot Of(ChampionStandings s) => new ChampionSnapshot
        {
            Updated = QuestPeriods.Unix(s.Updated),
            WeekReset = QuestPeriods.Unix(s.WeekReset),
            Week = s.Week,
            Boards = s.Boards.Select(b => b.Take(ChampionBoards.Places)
                .Select(r => new Entry { Rank = r.Rank, AccountId = r.AccountId, Name = r.Name, Value = r.Value }).ToList()).ToList(),
        };
    }
}
