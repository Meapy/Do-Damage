using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shared.database.leaderboard;
using Shared.quests;
using StackExchange.Redis;

namespace Shared.champions
{
    // Ranks every account on every board, for the account server (App/ChampionsService.cs) every
    // few minutes. Cheap on Redis: it reads accounts in pipelined batches, and keeps what it read,
    // so later rankings read again only the accounts seen since (their lastSeen moved: saving a
    // character, dying, a name change all come with a save) plus everything once an hour. The
    // weekly boards and the Arena's runs are read whole each time (sorted sets, small).
    public sealed class ChampionSweep
    {
        private const int Batch = 200;
        private const int FullEvery = 12;   // rankings between full reads (an hour)
        private const int ArenaRuns = 1000;
        // a guild member's living fame counts on Guild Living while they've played this recently
        private static readonly TimeSpan ActiveFor = TimeSpan.FromDays(14);

        private readonly IDatabase _db;
        private readonly QuestData _quests;
        private readonly Dictionary<int, Account> _accounts = new Dictionary<int, Account>();
        private int _runs;

        // What a ranking needs of one account.
        private sealed class Account
        {
            public long LastSeen;
            public bool Skip;              // a guest, banned, hidden or admin account, or one without a name
            public string Name;
            public int Stars;
            public long StarFame;          // its classes' best fame together (the Stars tiebreak)
            public int Points;
            public long BestFame;          // its best living character's fame
            public long AliveFame;         // all its living characters' fame together
            public int GuildId;
            public ushort ObjectType;
            public int Skin, Tex1, Tex2, Level;
        }

        public ChampionSweep(IDatabase db, QuestData quests)
        {
            _db = db;
            _quests = quests;
        }

        // full: read every account again, not only those seen since (asked for by
        // /championsadmin refresh, and once an hour anyway)
        public ChampionStandings Run(double clockHours, bool full = false)
        {
            full |= _runs++ % FullEvery == 0;
            // nextAccId is the newest account's id (Database.CreateNewAccount increments it first)
            var last = (int)_db.StringGet("nextAccId");
            for (var from = 1; from <= last; from += Batch)
                ReadAccounts(from, Math.Min(last + 1, from + Batch), full);

            var weekNow = DbChampions.Now(clockHours);
            var week = QuestPeriods.WeekKey(weekNow);
            var live = _accounts.Where(a => !a.Value.Skip).ToDictionary(a => a.Key, a => a.Value);

            var boards = new List<ChampionRanked>[ChampionBoards.All.Length];
            boards[(int)ChampionTab.Fame] = Rank(live.Select(a => (a.Key, a.Value.BestFame, 0L)));
            boards[(int)ChampionTab.Stars] = Rank(live.Select(a => (a.Key, (long)a.Value.Stars, a.Value.StarFame)));
            boards[(int)ChampionTab.Achievements] = Rank(live.Select(a => (a.Key, (long)a.Value.Points, 0L)));
            var runs = DbArenaRuns.Top(_db, ArenaRuns);
            boards[(int)ChampionTab.ArenaSolo] = Rank(ArenaBests(live, runs.Where(r => r.Solo).ToList()));
            boards[(int)ChampionTab.ArenaGroup] = ChampionBoards.Rank(ArenaGroups(live, runs.Where(r => !r.Solo).ToList()));
            DbChampions.SeedGuildDead(_db, weekNow.Year);
            var guilds = Guilds(live, weekNow).ToList();
            boards[(int)ChampionTab.GuildLiving] = ChampionBoards.Rank(guilds.Select(g => g.Living));
            boards[(int)ChampionTab.GuildDead] = ChampionBoards.Rank(guilds.Select(g => g.Dead));
            foreach (var tab in new[] { ChampionTab.WeeklyFame, ChampionTab.WeeklyDungeons })
                boards[(int)tab] = Rank(_db.SortedSetRangeByRankWithScores(DbChampions.WeekKey(week, tab), 0, -1, Order.Descending)
                    .Select(e => ((int)e.Element, (long)e.Score, 0L)).Where(e => live.ContainsKey(e.Item1)));
            return new ChampionStandings(DateTime.UtcNow, weekNow, boards);

            List<ChampionRanked> Rank(IEnumerable<(int Id, long Value, long Tiebreak)> values) =>
                ChampionBoards.Rank(values.Select(v => Entry(live, v.Id, v.Value, v.Tiebreak)));
        }

        private static ChampionRanked Entry(Dictionary<int, Account> live, int id, long value, long tiebreak)
        {
            var a = live[id];
            return new ChampionRanked
            {
                AccountId = id, Name = a.Name, Value = value, Tiebreak = tiebreak,
                ObjectType = a.ObjectType, Skin = a.Skin, Tex1 = a.Tex1, Tex2 = a.Tex2, Level = a.Level,
            };
        }

        // Each guild with a ranked member, a place on each guild board: Living, the living characters'
        // fame of its ranked members who played in the last two weeks (lastSeen; the others are
        // Away); Dead, the fame of every character that died in it this year (DbChampions.GuildDeadKey,
        // from Database.Death). Its character is its best member's; a tie goes to the bigger guild.
        private IEnumerable<(ChampionRanked Living, ChampionRanked Dead)> Guilds(Dictionary<int, Account> live, DateTime now)
        {
            var groups = live.Where(a => a.Value.GuildId > 0).GroupBy(a => a.Value.GuildId).ToList();
            if (groups.Count == 0)
                yield break;
            var batch = _db.CreateBatch();
            var heads = groups.Select(g => batch.HashGetAsync($"guild.{g.Key}", "name")).ToList();
            var deadFame = groups.Select(g => batch.SortedSetScoreAsync(DbChampions.GuildDeadKey(now.Year), g.Key)).ToList();
            batch.Execute();
            Task.WaitAll(heads.Concat<Task>(deadFame).ToArray());
            var active = new DateTimeOffset(now).ToUnixTimeSeconds() - (long)ActiveFor.TotalSeconds;
            for (var i = 0; i < groups.Count; i++)
            {
                var name = heads[i].Result;
                if (!name.HasValue || string.IsNullOrEmpty(name.ToString()))
                    continue;
                var roster = groups[i].Select(a =>
                    {
                        var away = a.Value.LastSeen < active;
                        var m = Entry(live, a.Key, away ? 0 : a.Value.AliveFame, 0);
                        m.Away = away;
                        return m;
                    })
                    .OrderByDescending(m => live[m.AccountId].BestFame).ThenBy(m => m.AccountId).ToList();
                var best = roster[0];
                ChampionRanked Place(long value) => new ChampionRanked
                {
                    AccountId = roster.Min(m => m.AccountId), GuildId = groups[i].Key, Name = name.ToString(),
                    Value = value, Tiebreak = roster.Count,
                    ObjectType = best.ObjectType, Skin = best.Skin, Tex1 = best.Tex1, Tex2 = best.Tex2, Level = best.Level,
                    Roster = roster,
                };
                yield return (Place(roster.Sum(m => m.Value)), Place((long)(deadFame[i].Result ?? 0)));
            }
        }

        // The Arena's group runs, a place for each group (the same players, in whatever order: its
        // best run), its members in the run's order. A run with a player who isn't ranked (a guest,
        // an admin, a name no account has now) isn't counted.
        private IEnumerable<ChampionRanked> ArenaGroups(Dictionary<int, Account> live, List<DbArenaRun> runs)
        {
            var byName = AccountsByName(live, runs);
            var seen = new HashSet<string>();
            foreach (var run in runs)
            {
                var ids = new List<int>();
                foreach (var p in run.Players)
                    if (byName.TryGetValue(p.ToUpperInvariant(), out var id) && !ids.Contains(id))
                        ids.Add(id);
                if (ids.Count < 2 || ids.Count != run.Players.Length || !seen.Add(string.Join(",", ids.OrderBy(i => i))))
                    continue;
                var quick = run.Seconds < 0 ? 0L : 1_000_000L - run.Seconds;
                var members = ids.Select(i => Entry(live, i, run.Wave, quick)).ToList();
                var lead = members[0];
                yield return new ChampionRanked
                {
                    AccountId = ids.Min(), Name = string.Join(", ", members.Select(m => m.Name)), Value = run.Wave, Tiebreak = quick,
                    ObjectType = lead.ObjectType, Skin = lead.Skin, Tex1 = lead.Tex1, Tex2 = lead.Tex2, Level = lead.Level,
                    Members = members,
                };
            }
        }

        // the ranked accounts among these runs' players, by upper-case name
        private Dictionary<string, int> AccountsByName(Dictionary<int, Account> live, List<DbArenaRun> runs)
        {
            var names = runs.SelectMany(r => r.Players).Select(n => n.ToUpperInvariant()).Distinct().ToArray();
            var byName = new Dictionary<string, int>();
            if (names.Length == 0)
                return byName;
            var ids = _db.HashGet("names", names.Select(n => (RedisValue)n).ToArray());
            for (var i = 0; i < names.Length; i++)
                if (ids[i].HasValue && int.TryParse(ids[i].ToString(), out var id) && live.ContainsKey(id))
                    byName[names[i]] = id;
            return byName;
        }

        // Each account's best of these (solo) Arena runs (DbArenaRuns, by the players' names): its
        // wave, and the quicker run of two to the same wave.
        private IEnumerable<(int, long, long)> ArenaBests(Dictionary<int, Account> live, List<DbArenaRun> runs)
        {
            var byName = AccountsByName(live, runs);
            var best = new Dictionary<int, (long Wave, long Quick)>();
            foreach (var run in runs)
                foreach (var p in run.Players)
                {
                    if (!byName.TryGetValue(p.ToUpperInvariant(), out var id))
                        continue;
                    var score = ((long)run.Wave, run.Seconds < 0 ? 0L : 1_000_000L - run.Seconds);
                    if (!best.TryGetValue(id, out var b) || score.CompareTo(b) > 0)
                        best[id] = score;
                }
            return best.Select(b => (b.Key, b.Value.Wave, b.Value.Quick));
        }

        private static readonly RedisValue[] AccountFields = { "lastSeen", "name", "guest", "banned", "hidden", "rank", "admin", "guildId" };
        private static readonly RedisValue[] CharFields = { "charType", "fame", "skin", "tex1", "tex2", "level" };

        private void ReadAccounts(int from, int to, bool full)
        {
            var heads = new Dictionary<int, Task<RedisValue[]>>();
            var batch = _db.CreateBatch();
            for (var id = from; id < to; id++)
                heads[id] = batch.HashGetAsync($"account.{id}", AccountFields);
            batch.Execute();
            Task.WaitAll(heads.Values.ToArray<Task>());

            // the accounts to read in full: new, seen since, or all of them now and then
            var changed = new List<(int Id, RedisValue[] Head)>();
            foreach (var (id, task) in heads)
            {
                var head = task.Result;
                if (!head[1].HasValue)
                {
                    _accounts.Remove(id);
                    continue;
                }
                var seen = (long)Num(head[0]);
                if (full || !_accounts.TryGetValue(id, out var known) || known.LastSeen != seen)
                    changed.Add((id, head));
            }
            if (changed.Count == 0)
                return;

            batch = _db.CreateBatch();
            var reads = changed.Select(c => (c.Id, c.Head,
                Progress: batch.HashGetAsync($"account.{c.Id}", "progress"),
                Alive: batch.SetMembersAsync($"alive.{c.Id}"),
                Classes: batch.HashGetAllAsync($"classStats.{c.Id}"))).ToList();
            batch.Execute();
            Task.WaitAll(reads.SelectMany(r => new Task[] { r.Progress, r.Alive, r.Classes }).ToArray());

            batch = _db.CreateBatch();
            var chars = reads.Select(r => r.Alive.Result.Select(c => batch.HashGetAsync($"char.{r.Id}.{BitConverter.ToInt32(c, 0)}", CharFields)).ToList()).ToList();
            batch.Execute();
            Task.WaitAll(chars.SelectMany(c => c).ToArray<Task>());

            for (var i = 0; i < reads.Count; i++)
            {
                var (id, head, progress, _, classes) = reads[i];
                var a = new Account
                {
                    LastSeen = (long)Num(head[0]),
                    Name = head[1].ToString(),
                    Skip = Flag(head[2]) || Flag(head[3]) || Flag(head[4]) || Num(head[5]) >= (int)RankingType.Admin || Flag(head[6])
                        || string.IsNullOrEmpty(head[1].ToString()),
                };
                if (!a.Skip)
                {
                    a.GuildId = (int)Num(head[7]);
                    (a.Stars, a.StarFame) = Stars(classes.Result);
                    a.Points = new QuestBook(_quests, QuestState.FromJson(progress.Result), id).Points;
                    foreach (var c in chars[i].Select(t => t.Result))
                    {
                        if (!c[0].HasValue)
                            continue;
                        var fame = (long)Num(c[1]);
                        var level = (int)Num(c[5]);
                        a.AliveFame += fame;
                        if (a.ObjectType != 0 && (fame < a.BestFame || (fame == a.BestFame && level <= a.Level)))
                            continue;
                        a.BestFame = fame;
                        a.ObjectType = (ushort)Num(c[0]);
                        a.Skin = (int)Num(c[2]);
                        a.Tex1 = (int)Num(c[3]);
                        a.Tex2 = (int)Num(c[4]);
                        a.Level = level;
                    }
                }
                _accounts[id] = a;
            }
        }

        // Player.GetStars: a star for each fame goal a class's best character reached (20, 150,
        // 400, 800, 2000).
        public static (int Stars, long Fame) Stars(HashEntry[] classStats)
        {
            int stars = 0;
            long fame = 0;
            foreach (var e in classStats)
            {
                int best;
                try { best = JsonConvert.DeserializeObject<Shared.database.character.DbClassStatsEntry>(e.Value).BestFame; }
                catch (JsonException) { continue; }
                stars += StarsFor(best);
                fame += best;
            }
            return (stars, fame);
        }

        public static int StarsFor(int bestFame) =>
            bestFame >= 2000 ? 5 : bestFame >= 800 ? 4 : bestFame >= 400 ? 3 : bestFame >= 150 ? 2 : bestFame >= 20 ? 1 : 0;

        private static double Num(RedisValue v) => v.HasValue && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

        // a RedisObject bool: one byte, 0 or 1 (RedisObject.SetValue)
        private static bool Flag(RedisValue v)
        {
            if (!v.HasValue)
                return false;
            var b = (byte[])v;
            return b.Length > 0 && b[0] != 0;
        }
    }
}
