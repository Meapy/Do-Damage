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
    // Each run's goal is fixed the first time a server sees it: the goal as written times the
    // goal's scale (`community:scale`, goal id to scale). When a week's run ends, the first server
    // to see it so settles it once, moving the scale by how far the realm got (NextScale), so the
    // goal's next run is harder after a week that sailed past it and easier after one that fell
    // short.
    //
    // Redis, for a run (CommunityRun.Key, "<goal>@<yyyyMMdd>"): `community:<run>` holds its total,
    // its goal and whether it's settled,
    // `community:<run>:who` each account's share (a sorted set), `community:<run>:claimed` the
    // claims ("<account>:<tier>", 255 the top's prize). `community:week:<yyyyMMdd>` keeps the
    // week's goals as they were drawn, so changing the pool only changes the weeks to come.
    //
    // A week's goal that reaches its last Overdrive level finishes early (CommunityEarly): the
    // first server to see it so marks it `finished` and adds a bonus goal for its slot to
    // `community:week:<yyyyMMdd>:early` ("<slot>,<goal>,<unix start>"), which ends that run and
    // runs until the weekly reset as usual. Each server announces the bonus goals it sees start.
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
        // each run's goal, once fixed in Redis, and the runs settled (or found settled)
        private static readonly ConcurrentDictionary<string, int> RunGoals = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, byte> Settled = new ConcurrentDictionary<string, byte>();
        // the runs seen finished and still running, and when this server last tried to start their
        // bonus goal (tried again a minute later while the run still runs)
        private static readonly ConcurrentDictionary<string, DateTime> Finished = new ConcurrentDictionary<string, DateTime>();
        // each week's bonus goals, as last read
        private static readonly ConcurrentDictionary<DateTime, IReadOnlyList<EarlyStart>> EarlyStarts = new ConcurrentDictionary<DateTime, IReadOnlyList<EarlyStart>>();
        private const string ScaleKey = "community:scale";

        public sealed class RunState
        {
            public CommunityRun Run;
            // this run's goal (the goal as written times its scale when it began)
            public int Goal;
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
        private static string EarlyKey(DateTime week) => $"community:week:{week:yyyyMMdd}:early";
        private static TimeSpan WeekKeptFor => TimeSpan.FromDays(7 * (Data.ClaimWeeks + 6));
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

        // Staff testing (/goaladmin reset): a run starts again from nothing, its goal fixed again
        // at the goal's scale now.
        public static void Reset(RunState rs)
        {
            var key = rs.Run.Key;
            PendingTotals.TryRemove(key, out _);
            foreach (var k in PendingShares.Keys.Where(k => k.Run == key).ToList())
                PendingShares.TryRemove(k, out _);
            _db.KeyDelete(new RedisKey[] { TotalKey(key), WhoKey(key), ClaimedKey(key) });
            RunGoals.TryRemove(key, out _);
            Settled.TryRemove(key, out _);
            Finished.TryRemove(key, out _);
        }

        // The goals' scales (1 for a goal never settled).
        public static Dictionary<string, double> Scales()
        {
            var scales = new Dictionary<string, double>();
            foreach (var e in _db.HashGetAll(ScaleKey))
                if (double.TryParse(e.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
                    scales[e.Name] = v;
            return scales;
        }

        // Staff testing (/goaladmin scale): a goal's runs from now on are `scale` times its goal.
        public static void SetScale(string id, double scale) =>
            _db.HashSet(ScaleKey, id, scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

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
                    _db.StringSet(key, string.Join(",", drawn), WeekKeptFor);
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

        // A week's bonus goals (CommunityEarly): read again each refresh until an hour after the
        // week ends (when no more can start), then kept.
        private static IReadOnlyList<EarlyStart> WeekEarly(DateTime week, DateTime now)
        {
            if (EarlyStarts.TryGetValue(week, out var kept) && now > week.AddDays(7).AddHours(1))
                return kept;
            try
            {
                var list = new List<EarlyStart>();
                foreach (var v in _db.ListRange(EarlyKey(week)))
                {
                    var parts = ((string)v ?? "").Split(',');
                    if (parts.Length >= 3 && int.TryParse(parts[0], out var slot) && long.TryParse(parts[2], out var unix))
                    {
                        var heat = parts.Length > 3 && double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 1;
                        list.Add(new EarlyStart(slot, parts[1], DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime, heat));
                    }
                }
                EarlyStarts[week] = list;
                return list;
            }
            catch (Exception e)
            {
                Log.Warn($"Community goals: couldn't read the week's bonus goals: {e.Message}");
                return kept ?? Array.Empty<EarlyStart>();
            }
        }

        // A week's goal reached its last level: one server at a time (a lock for a minute) ends it
        // now and starts a bonus goal in its slot until the weekly reset (unless too little is
        // left). A run that still runs a minute later (its bonus goal lost, or left out as a second
        // start of a goal) is tried again. `picked` holds the goals started this pass, so several
        // finished at once each get their own.
        private static void FinishEarly(RunState rs, List<CommunityRun> runs, DateTime now, HashSet<string> picked)
        {
            var run = rs.Run;
            Finished[run.Key] = DateTime.UtcNow;
            // too late in the week: none this week (not tried again)
            if ((run.End - now).TotalHours < Data.Early.MinHours)
            {
                Finished[run.Key] = DateTime.MaxValue;
                return;
            }
            try
            {
                var at = QuestPeriods.Unix(now);
                if (!_db.StringSet($"{TotalKey(run.Key)}:finishing", at, TimeSpan.FromMinutes(1), When.NotExists))
                    return;
                _db.HashSet(TotalKey(run.Key), "finished", at);
                var week = run.Week;
                // every goal of the week, with the bonus goals as Redis has them now and those just
                // picked
                EarlyStarts.TryRemove(week, out _);
                var used = runs.Where(r => !r.Special && r.Week == week).Select(r => r.Def.Id)
                    .Concat(WeekEarly(week, now).Select(e => e.Id)).Concat(picked).ToHashSet();
                var next = Data.NextGoal(week, used, WeekGoals(week.AddDays(-7)));
                if (next == null)
                {
                    Finished[run.Key] = DateTime.MaxValue;
                    Log.Info($"Community goals: {run.Key} finished early, and no goal is left for a bonus");
                    return;
                }
                // the faster it went, the harder the bonus goal
                var heat = Data.Heat(run, rs.Total, rs.Goal, now);
                picked.Add(next);
                _db.ListRightPush(EarlyKey(week), $"{run.Slot},{next},{at},{heat.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
                _db.KeyExpire(EarlyKey(week), WeekKeptFor);
                EarlyStarts.TryRemove(week, out _);
                Log.Info($"Community goals: {run.Key} finished early at {rs.Total:N0} of {rs.Goal:N0}; {next} starts as a bonus, {heat:0.##} times its size");
            }
            catch (Exception e)
            {
                Log.Warn($"Community goals: couldn't finish {run.Key} early: {e.Message}");
            }
        }

        // Sends what's counted, reads the totals back, and says which tiers were reached.
        private static void Refresh()
        {
            if (_db == null || Interlocked.Exchange(ref _refreshing, 1) == 1)
                return;
            try
            {
                var now = QuestTracker.Now;
                var runs = Data.Runs(now, WeekGoals, week => WeekEarly(week, now));
                // one run a key (a key twice would be bad data: the first is kept)
                runs = runs.GroupBy(r => r.Key).Select(g => g.First()).ToList();
                var byKey = runs.ToDictionary(r => r.Key);
                Seen.Clear();
                var totals = Take(PendingTotals);
                var shares = Take(PendingShares);
                // runs not seen yet: their goal is fixed now, at the goal's scale, unless another
                // server (or an earlier run of this one) already did
                var unfixed = runs.Where(r => !RunGoals.ContainsKey(r.Key)).ToList();
                var scales = unfixed.Count > 0 ? Scales() : new Dictionary<string, double>();

                var batch = _db.CreateBatch();
                var tasks = new List<Task>();
                var fixing = new List<(CommunityRun Run, Task<bool> First, Task<RedisValue> Goal)>();
                foreach (var run in unfixed)
                {
                    // a bonus goal after quick finishes is harder by their heat
                    var goal = run.Special ? run.Def.Goal : CommunityData.GoalOf(run.Def, (scales.TryGetValue(run.Def.Id, out var sc) ? sc : 1) * run.Heat);
                    var first = batch.HashSetAsync(TotalKey(run.Key), "goal", goal, When.NotExists);
                    tasks.Add(first);
                    tasks.Add(batch.KeyExpireAsync(TotalKey(run.Key), KeepFor(run)));
                    var t = batch.HashGetAsync(TotalKey(run.Key), "goal");
                    fixing.Add((run, first, t));
                    tasks.Add(t);
                }
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

                foreach (var (run, first, goal) in fixing)
                    if (goal.IsCompletedSuccessfully && goal.Result.TryParse(out int g) && g > 0)
                    {
                        RunGoals[run.Key] = g;
                        // a run no server saw while it ran (one from before community goals, or
                        // before a long stop) says nothing of the goal: it's never settled
                        if (first.IsCompletedSuccessfully && first.Result && !run.Running(now))
                        {
                            _db.HashSet(TotalKey(run.Key), "settled", "unseen", When.NotExists);
                            Settled[run.Key] = 0;
                        }
                        Log.Info($"Community goals: {run.Key} is {g:N0} ({run.Def.Goal:N0} as written)");
                    }

                var states = new List<RunState>();
                foreach (var (run, total, count, top) in reads)
                {
                    var rs = new RunState
                    {
                        Run = run,
                        Goal = RunGoals.TryGetValue(run.Key, out var fixedGoal) ? fixedGoal : run.Def.Goal,
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
                        Crossed(states.First(st => st.Run == run), after.Result - delta, after.Result);
                foreach (var rs in states)
                    if (!rs.Run.Running(now) && !rs.Run.Special && RunGoals.ContainsKey(rs.Run.Key) && !Settled.ContainsKey(rs.Run.Key))
                        Settle(rs);
                if (Data.Early.Enabled)
                {
                    var picked = new HashSet<string>();
                    foreach (var rs in _running)
                        if (!rs.Run.Special && RunGoals.ContainsKey(rs.Run.Key) && Data.Finished(rs.Run.Def, rs.Goal, rs.Total)
                            && (!Finished.TryGetValue(rs.Run.Key, out var tried) || DateTime.UtcNow - tried > TimeSpan.FromMinutes(1)))
                            FinishEarly(rs, runs, now, picked);
                }
                // a new week, or a bonus goal begun: a server that was running before says so
                var weekly = _running.Where(r => !r.Run.Special).ToList();
                var had = before.Select(b => b.Run.Key).ToHashSet();
                var fresh = weekly.Where(r => !had.Contains(r.Run.Key)).ToList();
                if (fresh.Count > 0 && before.Any(b => !b.Run.Special))
                {
                    if (fresh.Any(r => !r.Run.Bonus))
                        Broadcast(CommunityNotice.NEW_WEEK, "New community goals!",
                            $"This week: {string.Join(", ", _running.Select(r => r.Run.Def.Title))}. See the Warcaller in the Nexus.");
                    else
                        foreach (var rs in fresh)
                        {
                            var done = states.FirstOrDefault(s => !s.Run.Special && s.Run.Slot == rs.Run.Slot && s.Run.End == rs.Run.Start);
                            Broadcast(CommunityNotice.NEW_WEEK, $"Bonus goal: {rs.Run.Def.Title}!",
                                $"{(done != null ? $"\"{done.Run.Def.Title}\" was finished early" : "A goal was finished early")}, so a new one takes its place until the weekly reset. See the Warcaller in the Nexus.");
                        }
                }
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

        // A total went from `from` to `to`: the highest tier or Overdrive level it passed is
        // announced (one line for a jump over several), here only (the other server's players hear
        // it from theirs, if it was that one's add).
        private static void Crossed(RunState rs, long from, long to)
        {
            var def = rs.Run.Def;
            var goal = rs.Goal;
            var had = Data.Stage(def, goal, from);
            var has = Data.Stage(def, goal, to);
            if (has <= had)
                return;
            var tiers = Data.Tiers(def);
            var name = Data.StageName(def, has - 1);
            Log.Info($"Community goals: {def.Id} reached {name} ({to}/{goal})");
            if (has > tiers)
            {
                var pct = (int)(to * 100 / Math.Max(1, goal));
                var last = has - tiers >= Data.BonusLevels(def);
                Broadcast(CommunityNotice.TIER, $"{def.Title}: {name}!",
                    last
                        ? $"{to:N0} of {goal:N0}, {pct}%: the realm has gone as far as it goes! Everyone who helped can claim every bonus from the Warcaller."
                        : $"{to:N0} of {goal:N0}, {pct}% and climbing! Everyone who helped can claim a bonus reward from the Warcaller.");
            }
            else if (has == tiers)
                Broadcast(CommunityNotice.COMPLETE, $"{def.Title}: complete!",
                    $"Together we did it: {to:N0} of {goal:N0}! Everyone who helped can claim every reward from the Warcaller. Keep going for {Data.BonusName}!");
            else
                Broadcast(CommunityNotice.TIER, $"{def.Title}: {name} reached!",
                    $"{to:N0} of {goal:N0} so far. Everyone who helped can claim {(has - had > 1 ? "its rewards" : $"the {name} reward")} from the Warcaller.");
        }

        // A week's run has ended: the first server to see it so moves the goal's scale by how far
        // the realm got, once.
        private static void Settle(RunState rs)
        {
            var run = rs.Run;
            try
            {
                if (!_db.HashSet(TotalKey(run.Key), "settled", rs.Total, When.NotExists))
                {
                    Settled[run.Key] = 0;
                    return;
                }
                var id = run.Def.Id;
                var scales = Scales();
                var old = scales.TryGetValue(id, out var sc) ? sc : 1;
                // by its pace over a week (a run finished early or a bonus run is shorter), against
                // its goal without the heat
                var next = Data.NextScale(old, rs.Total, Math.Max(1, (int)Math.Round(rs.Goal / run.Heat)), run.Weeks, run.Cut);
                SetScale(id, next);
                Settled[run.Key] = 0;
                Log.Info($"Community goals: {run.Key} ended at {rs.Total:N0} of {rs.Goal:N0}; {id}'s scale {old:0.###} -> {next:0.###} (next goal {CommunityData.GoalOf(run.Def, next):N0})");
            }
            catch (Exception e)
            {
                // tried again next refresh
                Log.Warn($"Community goals: couldn't settle {run.Key}: {e.Message}");
            }
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
                var tiers = Enumerable.Range(0, Data.Tiers(rs.Run.Def))
                    .Concat(Enumerable.Range(CommunityData.BONUS_TIER, Data.BonusLevels(rs.Run.Def)))
                    .Append(CommunityData.TOP_TIER)
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
            var total = TotalOf(rs);
            for (var t = 0; t < Data.Reached(def, rs.Goal, total); t++)
                if (!me.Claimed.Contains(t))
                    list.Add((rs, t));
            for (var k = 0; k < Data.BonusReached(def, rs.Goal, total); k++)
                if (!me.Claimed.Contains(CommunityData.BONUS_TIER + k))
                    list.Add((rs, CommunityData.BONUS_TIER + k));
            if (!rs.Run.Running(now) && def.Top.Count > 0 && me.Rank > 0 && me.Rank <= Data.TopCount && !me.Claimed.Contains(CommunityData.TOP_TIER))
                list.Add((rs, CommunityData.TOP_TIER));
            return list;
        }

        // A run's total with what's counted here and not sent yet.
        public static long TotalOf(RunState rs) => rs.Total + (PendingTotals.TryGetValue(rs.Run.Key, out var p) ? p : 0);

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
                BonusName = Data.BonusName,
                Rarities = Data.Rarities.ToList(),
            };
            foreach (var rs in runs)
            {
                var def = rs.Run.Def;
                var me = mine.TryGetValue(rs.Run.Key, out var m) ? m : (0, 0, new HashSet<int>());
                var total = TotalOf(rs);
                var info = new CommunityGoalInfo
                {
                    Key = rs.Run.Key,
                    Title = def.Title,
                    Text = def.Describe(rs.Goal),
                    Icon = def.Icon != null && types.TryGetValue(def.Icon, out var icon) ? icon : (ushort)0,
                    Special = rs.Run.Special,
                    StartsAt = QuestPeriods.Unix(rs.Run.Start),
                    EndsAt = QuestPeriods.Unix(rs.Run.End),
                    Running = rs.Run.Running(now),
                    Goal = rs.Goal,
                    BaseGoal = def.Goal,
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
                    info.Tiers.Add(new CommunityTierInfo { At = Data.Threshold(rs.Goal, t), Claimed = me.Claimed.Contains(t), Rewards = Rewards(def.Tiers[t], types) });
                var bonus = Rewards(def.Bonus, types);
                for (var k = 1; k <= Data.BonusLevels(def); k++)
                    info.Bonus.Add(new CommunityTierInfo { At = Data.BonusThreshold(rs.Goal, k), Claimed = me.Claimed.Contains(CommunityData.BONUS_TIER + k - 1), Rewards = bonus });
                var best = Data.Odds(def, Math.Max(1, Data.BonusTiers)).ToDictionary(o => o.Entry, o => o.Chance);
                foreach (var (e, chance) in Data.Odds(def, 0))
                    if (types.TryGetValue(e.Item, out var item))
                        info.Loot.Add(new CommunityLootInfo
                        {
                            Item = item,
                            Count = (byte)Math.Clamp(e.Count, 1, 255),
                            Rarity = (byte)Data.RarityOf(e),
                            Chance = PerTenThousand(chance),
                            Best = PerTenThousand(best.TryGetValue(e, out var b) ? b : chance),
                        });
                msg.Goals.Add(info);
            }
            client.SendPacket(msg);
        }

        private static ushort PerTenThousand(double chance) => (ushort)Math.Clamp((int)Math.Round(chance * 10000), chance > 0 ? 1 : 0, 10000);

        // As the window shows them: fame (0), an item (1), or rolls of the goal's loot (2).
        private static List<QuestReward> Rewards(IEnumerable<RewardDef> defs, IDictionary<string, ushort> types) => defs
            .Select(r => r.Roll > 0
                ? new QuestReward { Kind = 2, Amount = r.Roll }
                : r.Item == null
                ? new QuestReward { Kind = 0, Amount = r.Fame }
                : new QuestReward { Kind = 1, Item = types.TryGetValue(r.Item, out var t) ? t : (ushort)0, Amount = Math.Max(1, r.Count) })
            .Where(r => r.Kind != 1 || r.Item != 0)
            .ToList();

        public const byte CLAIM_ALL = 254;
        // every Overdrive level a goal has waiting
        public const byte CLAIM_BONUS = 253;

        private static string ClaimName(CommunityGoalDef def, int tier) =>
            tier == CommunityData.TOP_TIER ? $"{def.Title} (top {Data.TopCount})"
            : tier >= CommunityData.BONUS_TIER ? $"{def.Title} ({Data.BonusLevelName(tier - CommunityData.BONUS_TIER + 1)})"
            : $"{def.Title} ({Data.TierName(tier)})";

        // A claim from the window: a tier of a run, an Overdrive level (or all it has waiting,
        // CLAIM_BONUS), its top's prize, or everything waiting (CLAIM_ALL, with no run for every
        // goal). Each is recorded in Redis
        // before it's paid; loot is rolled as it's paid, and a rare enough roll is told to all.
        public static void Claim(Player player, string key, byte tier)
        {
            var client = player.Client;
            var account = client?.Account;
            if (account == null)
                return;
            void Answer(bool ok, string text, List<CommunityRollInfo> rolls = null) =>
                client.SendPacket(new CommunityNotice() { Kind = ok ? CommunityNotice.CLAIMED : CommunityNotice.REFUSED, Title = ok ? "Claimed!" : "", Text = text, Rolls = rolls });
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
                .Where(c => tier == CLAIM_ALL || c.Tier == tier || tier == CLAIM_BONUS && c.Tier >= CommunityData.BONUS_TIER && c.Tier < CommunityData.TOP_TIER)
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
            var rolled = new List<(CommunityGoalDef Def, LootEntry Entry, int Level)>();
            foreach (var (rs, t) in wanted)
            {
                // the claim is kept first: only one claim of it ever pays
                if (!_db.SetAdd(ClaimedKey(rs.Run.Key), ClaimId(player.AccountId, t)))
                    continue;
                _db.KeyExpire(ClaimedKey(rs.Run.Key), KeepFor(rs.Run));
                var def = rs.Run.Def;
                var (defs, level) = Data.ClaimRewards(def, t);
                var rolls = new List<LootEntry>();
                rewards.AddRange(Data.Resolve(def, defs, level, Random.Shared, rolls));
                rolled.AddRange(rolls.Select(e => (def, e, level)));
                names.Add(ClaimName(def, t));
            }
            if (names.Count > 0)
            {
                QuestTracker.PayRewards(player, account, rewards);
                Log.Info($"Community goals: {player.Name} claimed {string.Join(", ", names)}: {string.Join(", ", rewards)}");
                var fame = rewards.Sum(r => r.Fame);
                var items = QuestTracker.ItemList(player, rewards);
                var paid = string.Join(" and ", new[] { fame > 0 ? $"{fame:N0} fame" : null, items.Length > 0 ? items : null }.Where(p => p != null));
                if (paid.Length == 0)
                    paid = "nothing this time";
                // the best roll, by name, for the player; the rarest ones for everyone
                var top = rolled.OrderByDescending(r => Data.RarityOf(r.Entry)).Select(r => r.Entry).FirstOrDefault();
                var bestText = top != null && Data.RarityOf(top) >= 2 ? $" Best roll: {top} ({Capital(Data.Rarities[Data.RarityOf(top)])})!" : "";
                Answer(true, (names.Count == 1 ? $"{names[0]}: {paid}." : $"{names.Count} rewards: {paid}.") + bestText, Reels(player, rolled));
                foreach (var (def, e, _) in rolled.Where(r => Data.Announced(r.Entry)))
                    Broadcast(CommunityNotice.LOOT, $"{player.Name} struck it lucky!",
                        $"{Capital(Data.Rarities[Data.RarityOf(e)])} loot from \"{def.Title}\": {e}!");
            }
            else
                Answer(false, "Already claimed.");
            SendData(player);
        }

        // the client's reel: it scrolls REEL_LENGTH lines of the goal's loot and stops on the prize
        // at REEL_STOP, as the death spin's does
        private const int REEL_LENGTH = 40, REEL_STOP = 34;

        // Each roll of a claim as the client's reel shows it (rolls of items the game doesn't know
        // are left out; they were paid as nothing too).
        private static List<CommunityRollInfo> Reels(Player player, List<(CommunityGoalDef Def, LootEntry Entry, int Level)> rolled)
        {
            var types = player.GameServer.Resources.GameData.IdToObjectType;
            var rolls = new List<CommunityRollInfo>();
            foreach (var (def, e, level) in rolled)
            {
                if (!types.TryGetValue(e.Item, out var item))
                    continue;
                var roll = new CommunityRollInfo { Item = item, Count = (byte)Math.Clamp(e.Count, 1, 255), Rarity = (byte)Data.RarityOf(e), Stop = REEL_STOP };
                foreach (var line in Data.Reel(def, level, e, REEL_LENGTH, REEL_STOP, Random.Shared))
                {
                    var known = types.TryGetValue(line.Item, out var t);
                    roll.Reel.Add((known ? t : item, (byte)Data.RarityOf(known ? line : e)));
                }
                rolls.Add(roll);
            }
            return rolls;
        }

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
