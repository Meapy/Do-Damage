using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Shared.community;
using Shared.quests;
using StackExchange.Redis;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking.packets.outgoing;
using WorldServer.networking.packets.outgoing.community;
using WorldServer.networking.packets.outgoing.quests;

namespace WorldServer.core.community
{
    // Community goals in the game (Shared.community.CommunityData): what everyone does counts
    // here as it happens (QuestTracker.Event hands over each player's events), from any world
    // thread, and goes to Redis every RefreshSeconds, which also reads back what the other world
    // server counted (both add to the same keys during a rolling deploy). The server that takes
    // a goal's total over a tier's line says so to its players. Claims check and record in Redis
    // before paying, so a tier is never paid twice, whichever server is asked.
    //
    // Redis, for a run (CommunityRun.Key, "<goal>@<yyyyMMdd>"): `community:<run>` holds its total,
    // `community:<run>:who` each account's share (a sorted set), `community:<run>:claimed` the
    // claims ("<account>:<tier>", 255 the top's prize). `community:week:<yyyyMMdd>` keeps the
    // week's goals as they were drawn, so changing the pool only changes the weeks to come.
    public static class CommunityGoals
    {
        private const int RefreshSeconds = 5;
        private const int TopShown = 5;
        private const uint CHAT_COLOR = 0xFFB35C;
        public const string NPC_NAME = "#The Warcaller";

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static GameServer _server;
        private static IDatabase _db;
        private static Timer _timer;
        private static int _refreshing;

        public static CommunityData Data { get; private set; } = new CommunityData();

        // counted since the last refresh: each run's total, and each account's share
        private static readonly ConcurrentDictionary<string, long> PendingTotals = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<(string Run, int Account), long> PendingShares = new ConcurrentDictionary<(string, int), long>();
        // the shared events (QuestEvent.Occurrence) each run has counted since the last refresh:
        // everyone a kill counts for is handed it in the same pass, so that's long enough
        private static readonly ConcurrentDictionary<(string, long), byte> Seen = new ConcurrentDictionary<(string, long), byte>();
        private static readonly ConcurrentDictionary<int, string> Names = new ConcurrentDictionary<int, string>();
        private static readonly ConcurrentDictionary<DateTime, List<string>> Weeks = new ConcurrentDictionary<DateTime, List<string>>();

        public sealed class RunState
        {
            public CommunityRun Run;
            public long Total;
            public long Participants;
            public List<(int Account, string Name, long Amount)> Top = new List<(int, string, long)>();
        }

        // the runs as last read from Redis; replaced whole, so any thread can read it
        private static volatile RunState[] _runs = Array.Empty<RunState>();
        private static volatile RunState[] _running = Array.Empty<RunState>();

        public static IReadOnlyList<RunState> Runs => _runs;
        public static IReadOnlyList<RunState> Running => _running;

        public static void Start(GameServer server)
        {
            _server = server;
            _db = server.Database.Conn;
            var path = $"{server.Resources.ResourcePath}/data/community.json";
            try
            {
                Data = CommunityData.Load(path);
            }
            catch (Exception e)
            {
                Log.Error($"Community goals: can't read {path}: {e.Message}");
                Data = new CommunityData();
            }
            var items = server.Resources.GameData.IdToObjectType;
            foreach (var problem in Data.Validate(items.ContainsKey))
                Log.Warn($"Community goals: {problem}");
            Log.Info($"Community goals: {Data.Goals.Count} in the pool, {Data.Specials.Count} specials");
            Refresh();
            _timer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(RefreshSeconds), TimeSpan.FromSeconds(RefreshSeconds));
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            // a refresh the timer started first: let it finish, then send what's left
            for (var i = 0; i < 100 && Volatile.Read(ref _refreshing) == 1; i++)
                Thread.Sleep(50);
            Refresh();
        }

        private static string TotalKey(string run) => $"community:{run}";
        private static string WhoKey(string run) => $"community:{run}:who";
        private static string ClaimedKey(string run) => $"community:{run}:claimed";
        private static string WeekKey(DateTime week) => $"community:week:{week:yyyyMMdd}";
        private static string ClaimId(int account, int tier) => $"{account}:{tier}";
        // a run's keys outlive the claims by some weeks
        private static TimeSpan KeepFor(CommunityRun run) => run.End.AddDays(7 * (Data.ClaimWeeks + 4)) - DateTime.UtcNow;

        // One event of a player's (QuestTracker.Event): it adds to each running goal that counts
        // it, to the total once however many share it.
        public static void Count(Player player, in QuestEvent e)
        {
            var running = _running;
            if (running.Length == 0 || e.Amount <= 0)
                return;
            var account = player.AccountId;
            long amount = e.Amount;
            foreach (var rs in running)
            {
                if (!rs.Run.Def.Counts(e))
                    continue;
                var key = rs.Run.Key;
                PendingShares.AddOrUpdate((key, account), amount, (_, v) => v + amount);
                if (e.Occurrence == 0 || Seen.TryAdd((key, e.Occurrence), 0))
                    PendingTotals.AddOrUpdate(key, amount, (_, v) => v + amount);
            }
        }

        // Staff testing (/goaladmin add): as if the player had done it `amount` times alone.
        public static void Add(Player player, RunState rs, long amount)
        {
            var key = rs.Run.Key;
            PendingShares.AddOrUpdate((key, player.AccountId), amount, (_, v) => v + amount);
            PendingTotals.AddOrUpdate(key, amount, (_, v) => v + amount);
        }

        // Staff testing (/goaladmin reset): a run starts again from nothing.
        public static void Reset(RunState rs)
        {
            var key = rs.Run.Key;
            PendingTotals.TryRemove(key, out _);
            foreach (var k in PendingShares.Keys.Where(k => k.Run == key).ToList())
                PendingShares.TryRemove(k, out _);
            _db.KeyDelete(new RedisKey[] { TotalKey(key), WhoKey(key), ClaimedKey(key) });
        }

        // A week's goals: as drawn and kept when the week began, else drawn now and kept.
        private static List<string> WeekGoals(DateTime week)
        {
            if (Weeks.TryGetValue(week, out var ids))
                return ids;
            var key = WeekKey(week);
            var drawn = Data.Draw(week);
            // nothing to keep (no pool): a later start with one draws the week then
            if (drawn.Count == 0)
                return drawn;
            try
            {
                // the first server to see the week keeps its draw; the others read it (two at
                // once draw the same, from the same data)
                var kept = ((string)_db.StringGet(key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (kept.Count > 0)
                    drawn = kept;
                else
                {
                    _db.StringSet(key, string.Join(",", drawn), TimeSpan.FromDays(7 * (Data.ClaimWeeks + 6)));
                    Log.Info($"Community goals: the week of {week:yyyy-MM-dd} has {string.Join(", ", drawn)}");
                }
            }
            catch (Exception e)
            {
                // not kept: drawn again next time
                Log.Warn($"Community goals: couldn't keep the week's goals: {e.Message}");
                return drawn;
            }
            Weeks[week] = drawn;
            return drawn;
        }

        // Sends what's counted, reads the totals back, and says which tiers were reached.
        private static void Refresh()
        {
            if (_db == null || Interlocked.Exchange(ref _refreshing, 1) == 1)
                return;
            try
            {
                var now = QuestTracker.Now;
                var runs = Data.Runs(now, WeekGoals);
                var byKey = runs.ToDictionary(r => r.Key);
                Seen.Clear();
                var totals = Take(PendingTotals);
                var shares = Take(PendingShares);

                var batch = _db.CreateBatch();
                var tasks = new List<Task>();
                var added = new List<(CommunityRun Run, long Delta, Task<long> After)>();
                foreach (var (key, n) in totals)
                {
                    if (!byKey.TryGetValue(key, out var run))
                        continue;
                    var t = batch.HashIncrementAsync(TotalKey(key), "total", n);
                    added.Add((run, n, t));
                    tasks.Add(t);
                    tasks.Add(batch.KeyExpireAsync(TotalKey(key), KeepFor(run)));
                }
                foreach (var ((key, account), n) in shares)
                {
                    if (!byKey.TryGetValue(key, out var run))
                        continue;
                    tasks.Add(batch.SortedSetIncrementAsync(WhoKey(key), account, n));
                    tasks.Add(batch.KeyExpireAsync(WhoKey(key), KeepFor(run)));
                }
                var reads = runs.Select(r => (
                    Run: r,
                    Total: batch.HashGetAsync(TotalKey(r.Key), "total"),
                    Count: batch.SortedSetLengthAsync(WhoKey(r.Key)),
                    Top: batch.SortedSetRangeByRankWithScoresAsync(WhoKey(r.Key), 0, TopShown - 1, Order.Descending))).ToList();
                tasks.AddRange(reads.SelectMany(r => new Task[] { r.Total, r.Count, r.Top }));
                batch.Execute();
                if (!Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10)))
                    Log.Warn("Community goals: Redis was slow to answer");

                var states = new List<RunState>();
                foreach (var (run, total, count, top) in reads)
                {
                    var rs = new RunState
                    {
                        Run = run,
                        Total = total.IsCompletedSuccessfully && total.Result.TryParse(out long t) ? t : 0,
                        Participants = count.IsCompletedSuccessfully ? count.Result : 0,
                    };
                    if (top.IsCompletedSuccessfully)
                        foreach (var e in top.Result)
                            if (int.TryParse(e.Element, out var acc))
                                rs.Top.Add((acc, NameOf(acc), (long)e.Score));
                    states.Add(rs);
                }
                var before = _running;
                _runs = states.ToArray();
                _running = states.Where(s => s.Run.Running(now)).ToArray();

                foreach (var (run, delta, after) in added)
                    if (after.IsCompletedSuccessfully)
                        Crossed(run, after.Result - delta, after.Result);
                // a new week: a server that was running last week says so
                var weekly = _running.Where(r => !r.Run.Special).Select(r => r.Run.Key).ToList();
                if (weekly.Count > 0 && before.Any(b => !b.Run.Special) && !before.Any(b => weekly.Contains(b.Run.Key)))
                    Broadcast(CommunityNotice.NEW_WEEK, "New community goals!",
                        $"This week: {string.Join(", ", _running.Select(r => r.Run.Def.Title))}. See the Warcaller in the Nexus.");
            }
            catch (Exception e)
            {
                Log.Warn(e, "Community goals: couldn't refresh");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        // A total went from `from` to `to`: the highest tier it passed is announced (one line for
        // a jump over several), here only (the other server's players hear it from theirs, if it
        // was that one's add).
        private static void Crossed(CommunityRun run, long from, long to)
        {
            var def = run.Def;
            var had = Data.Reached(def, from);
            var has = Data.Reached(def, to);
            if (has <= had)
                return;
            var tier = has - 1;
            var name = Data.TierName(tier);
            Log.Info($"Community goals: {def.Id} reached tier {has} ({to}/{def.Goal})");
            if (has == Data.Tiers(def))
                Broadcast(CommunityNotice.COMPLETE, $"{def.Title}: complete!",
                    $"Together we did it: {to:N0} of {def.Goal:N0}! Everyone who helped can claim every reward from the Warcaller.");
            else
                Broadcast(CommunityNotice.TIER, $"{def.Title}: {name} reached!",
                    $"{to:N0} of {def.Goal:N0} so far. Everyone who helped can claim {(has - had > 1 ? "its rewards" : $"the {name} reward")} from the Warcaller.");
        }

        // To everyone on this server: a popup for the clients that show them, a chat line for all.
        public static void Broadcast(byte kind, string title, string text)
        {
            if (_server == null)
                return;
            foreach (var client in _server.ConnectionManager.Clients.Keys)
            {
                var player = client.Player;
                if (player == null || client.Account == null)
                    continue;
                if (player.CommunitySubscribed)
                    client.SendPacket(new CommunityNotice() { Kind = kind, Title = title, Text = text });
                client.SendPacket(new Text() { Name = NPC_NAME, ObjectId = -1, NumStars = -1, BubbleTime = 0, Recipient = "", Txt = $"{title} {text}", TextColor = (int)CHAT_COLOR, NameColor = (int)CHAT_COLOR });
            }
        }

        private static string NameOf(int account)
        {
            if (Names.TryGetValue(account, out var name))
                return name;
            try
            {
                name = _db.HashGet($"account.{account}", "name");
            }
            catch
            {
                return "?";
            }
            name = string.IsNullOrEmpty(name) ? "?" : name;
            Names[account] = name;
            return name;
        }

        private static Dictionary<T, long> Take<T>(ConcurrentDictionary<T, long> from)
        {
            var taken = new Dictionary<T, long>();
            foreach (var key in from.Keys.ToList())
                if (from.TryRemove(key, out var n) && n != 0)
                    taken[key] = n;
            return taken;
        }

        private static long Pending(string run, int account) => PendingShares.TryGetValue((run, account), out var n) ? n : 0;

        public static RunState Find(string key) => _runs.FirstOrDefault(r => r.Run.Key == key);

        // A player's place in each run: their share (with what's not sent yet), their rank (0 for
        // none) and the claims they've made. One trip to Redis for them all.
        private static Dictionary<string, (long Share, int Rank, HashSet<int> Claimed)> Mine(int account, IReadOnlyList<RunState> runs)
        {
            var batch = _db.CreateBatch();
            var reads = runs.Select(rs =>
            {
                var key = rs.Run.Key;
                var tiers = Enumerable.Range(0, Data.Tiers(rs.Run.Def)).Append(CommunityData.TOP_TIER)
                    .Select(t => (Tier: t, Task: batch.SetContainsAsync(ClaimedKey(key), ClaimId(account, t)))).ToList();
                return (Key: key, Score: batch.SortedSetScoreAsync(WhoKey(key), account), Rank: batch.SortedSetRankAsync(WhoKey(key), account, Order.Descending), Tiers: tiers);
            }).ToList();
            batch.Execute();
            var all = reads.SelectMany(r => new Task[] { r.Score, r.Rank }.Concat(r.Tiers.Select(t => (Task)t.Task))).ToArray();
            Task.WaitAll(all, TimeSpan.FromSeconds(5));
            var mine = new Dictionary<string, (long, int, HashSet<int>)>();
            foreach (var r in reads)
            {
                var share = (long)(r.Score.IsCompletedSuccessfully ? r.Score.Result ?? 0 : 0) + Pending(r.Key, account);
                var rank = r.Rank.IsCompletedSuccessfully && r.Rank.Result.HasValue ? (int)r.Rank.Result.Value + 1 : 0;
                var claimed = r.Tiers.Where(t => t.Task.IsCompletedSuccessfully && t.Task.Result).Select(t => t.Tier).ToHashSet();
                mine[r.Key] = (share, share > 0 ? rank : 0, claimed);
            }
            return mine;
        }

        // How much the player has added to a run (the Warcaller's greeting).
        public static long ShareOf(Player player, RunState rs)
        {
            try
            {
                return (long)(_db.SortedSetScore(WhoKey(rs.Run.Key), player.AccountId) ?? 0) + Pending(rs.Run.Key, player.AccountId);
            }
            catch
            {
                return Pending(rs.Run.Key, player.AccountId);
            }
        }

        // Rewards waiting for the player: tiers reached that they've added enough to and not
        // claimed, and the top's prize of an ended goal they placed in.
        private static List<(RunState Run, int Tier)> Claimable(RunState rs, (long Share, int Rank, HashSet<int> Claimed) me, DateTime now)
        {
            var list = new List<(RunState, int)>();
            var def = rs.Run.Def;
            if (me.Share < Math.Max(1, def.Minimum))
                return list;
            var total = rs.Total + (PendingTotals.TryGetValue(rs.Run.Key, out var p) ? p : 0);
            for (var t = 0; t < Data.Reached(def, total); t++)
                if (!me.Claimed.Contains(t))
                    list.Add((rs, t));
            if (!rs.Run.Running(now) && def.Top.Count > 0 && me.Rank > 0 && me.Rank <= Data.TopCount && !me.Claimed.Contains(CommunityData.TOP_TIER))
                list.Add((rs, CommunityData.TOP_TIER));
            return list;
        }

        // How many rewards wait for the player in all (the Warcaller's greeting).
        public static int WaitingFor(Player player)
        {
            var runs = _runs;
            if (runs.Length == 0 || player.Client?.Account == null || player.Client.Account.Guest)
                return 0;
            try
            {
                var mine = Mine(player.AccountId, runs);
                var now = QuestTracker.Now;
                return runs.Sum(rs => Claimable(rs, mine[rs.Run.Key], now).Count);
            }
            catch
            {
                return 0;
            }
        }

        public static void SendData(Player player)
        {
            var client = player.Client;
            if (client?.Account == null)
                return;
            var now = QuestTracker.Now;
            var runs = _runs;
            var types = player.GameServer.Resources.GameData.IdToObjectType;
            Dictionary<string, (long Share, int Rank, HashSet<int> Claimed)> mine;
            try
            {
                mine = client.Account.Guest ? new Dictionary<string, (long, int, HashSet<int>)>() : Mine(player.AccountId, runs);
            }
            catch (Exception e)
            {
                Log.Warn($"Community goals: couldn't read {player.Name}'s shares: {e.Message}");
                return;
            }
            var msg = new CommunityDataMessage()
            {
                Now = QuestPeriods.Unix(now),
                WeekEndsAt = QuestPeriods.Unix(CommunityData.WeekStart(now).AddDays(7)),
                TierNames = Enumerable.Range(0, Data.TierShares.Length).Select(Data.TierName).ToList(),
            };
            foreach (var rs in runs)
            {
                var def = rs.Run.Def;
                var me = mine.TryGetValue(rs.Run.Key, out var m) ? m : (0, 0, new HashSet<int>());
                var total = rs.Total + (PendingTotals.TryGetValue(rs.Run.Key, out var p) ? p : 0);
                var info = new CommunityGoalInfo
                {
                    Key = rs.Run.Key,
                    Title = def.Title,
                    Text = def.Describe(),
                    Icon = def.Icon != null && types.TryGetValue(def.Icon, out var icon) ? icon : (ushort)0,
                    Special = rs.Run.Special,
                    StartsAt = QuestPeriods.Unix(rs.Run.Start),
                    EndsAt = QuestPeriods.Unix(rs.Run.End),
                    Running = rs.Run.Running(now),
                    Goal = def.Goal,
                    Total = (int)Math.Min(total, int.MaxValue),
                    Participants = (int)Math.Min(rs.Participants, int.MaxValue),
                    Minimum = Math.Max(1, def.Minimum),
                    Mine = (int)Math.Min(me.Share, int.MaxValue),
                    Rank = me.Rank,
                    TopCount = (byte)Math.Clamp(Data.TopCount, 0, 255),
                    TopClaimed = me.Claimed.Contains(CommunityData.TOP_TIER),
                    TopRewards = Rewards(def.Top, types),
                    Top = rs.Top.Select(t => (t.Name, (int)Math.Min(t.Amount, int.MaxValue))).ToList(),
                };
                for (var t = 0; t < Data.Tiers(def); t++)
                    info.Tiers.Add(new CommunityTierInfo { At = Data.Threshold(def, t), Claimed = me.Claimed.Contains(t), Rewards = Rewards(def.Tiers[t], types) });
                msg.Goals.Add(info);
            }
            client.SendPacket(msg);
        }

        private static List<QuestReward> Rewards(IEnumerable<RewardDef> defs, IDictionary<string, ushort> types) => defs
            .Select(r => r.Item == null
                ? new QuestReward { Kind = 0, Amount = r.Fame }
                : new QuestReward { Kind = 1, Item = types.TryGetValue(r.Item, out var t) ? t : (ushort)0, Amount = Math.Max(1, r.Count) })
            .Where(r => r.Kind == 0 || r.Item != 0)
            .ToList();

        public const byte CLAIM_ALL = 254;

        // A claim from the window: a tier of a run, its top's prize, or everything waiting
        // (CLAIM_ALL, with no run for every goal). Each is recorded in Redis before it's paid.
        public static void Claim(Player player, string key, byte tier)
        {
            var client = player.Client;
            var account = client?.Account;
            if (account == null)
                return;
            void Answer(bool ok, string text) =>
                client.SendPacket(new CommunityNotice() { Kind = ok ? CommunityNotice.CLAIMED : CommunityNotice.REFUSED, Title = ok ? "Claimed!" : "", Text = text });
            if (account.Guest)
            {
                Answer(false, "Register your account to collect rewards.");
                return;
            }
            if (player.World is TestWorld)
            {
                Answer(false, "Not here.");
                return;
            }
            var now = QuestTracker.Now;
            var runs = string.IsNullOrEmpty(key) ? _runs.ToList() : _runs.Where(r => r.Run.Key == key).ToList();
            if (runs.Count == 0)
            {
                Answer(false, "That goal has ended.");
                SendData(player);
                return;
            }
            var mine = Mine(player.AccountId, runs);
            var wanted = runs.SelectMany(rs => Claimable(rs, mine[rs.Run.Key], now))
                .Where(c => tier == CLAIM_ALL || c.Tier == tier)
                .ToList();
            if (wanted.Count == 0)
            {
                var rs = runs[0];
                var me = mine[rs.Run.Key];
                Answer(false, me.Share < Math.Max(1, rs.Run.Def.Minimum)
                    ? $"Help with \"{rs.Run.Def.Title}\" first: {Math.Max(1, rs.Run.Def.Minimum):N0} to share its rewards."
                    : "Nothing to claim there yet.");
                SendData(player);
                return;
            }
            var rewards = new List<RewardDef>();
            var names = new List<string>();
            foreach (var (rs, t) in wanted)
            {
                // the claim is kept first: only one claim of it ever pays
                if (!_db.SetAdd(ClaimedKey(rs.Run.Key), ClaimId(player.AccountId, t)))
                    continue;
                _db.KeyExpire(ClaimedKey(rs.Run.Key), KeepFor(rs.Run));
                var def = rs.Run.Def;
                rewards.AddRange(t == CommunityData.TOP_TIER ? def.Top : def.Tiers[t]);
                names.Add(t == CommunityData.TOP_TIER ? $"{def.Title} (top {Data.TopCount})" : $"{def.Title} ({Data.TierName(t)})");
            }
            if (names.Count > 0)
            {
                QuestTracker.PayRewards(player, account, rewards);
                Log.Info($"Community goals: {player.Name} claimed {string.Join(", ", names)}: {string.Join(", ", rewards)}");
                var fame = rewards.Sum(r => r.Fame);
                var items = rewards.Where(r => r.Item != null).Sum(r => Math.Max(1, r.Count));
                var paid = string.Join(" and ", new[] { fame > 0 ? $"{fame:N0} fame" : null, items > 0 ? $"{items} item{(items == 1 ? "" : "s")}" : null }.Where(p => p != null));
                Answer(true, names.Count == 1 ? $"{names[0]}: {paid}." : $"{names.Count} rewards: {paid}.");
            }
            else
                Answer(false, "Already claimed.");
            SendData(player);
        }
    }
}
