using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Shared.quests;

namespace Shared.community
{
    // Community goals, as resources/data/community.json defines them (its "_about" says how): what
    // the whole server does together in a week, such as defeating Oryx 60 times or clearing 400
    // dungeons. Each goal has tiers along the way, reached at a share of its goal; every account
    // that added at least its minimum claims each tier reached, and the top contributors claim a
    // prize more once it ends. Past the goal come the Overdrive levels, each a bonus reward. Each
    // goal rolls its own loot table, and the server sizes its next run by how the last went
    // (NextScale). The world server keeps the counts in Redis (WorldServer's CommunityGoals) and
    // reads this at start.
    public sealed class CommunityGoalDef
    {
        public string Id { get; set; }
        public string Group { get; set; }
        public double Weight { get; set; } = 1;
        public string Title { get; set; }
        public string Text { get; set; }
        // what the window shows beside it: an object's id (a monster, an item, a portal)
        public string Icon { get; set; }
        public string Event { get; set; }
        public EventFilter Filter { get; set; }
        // the goal as written: a run's goal is this times the goal's scale (CommunityData.GoalOf)
        public int Goal { get; set; }
        // what an account must add to share the tiers' rewards
        public int Minimum { get; set; } = 1;
        // each tier's rewards, in order (CommunityData.TierShares says when each is reached)
        public List<List<RewardDef>> Tiers { get; set; } = new List<List<RewardDef>>();
        // each Overdrive level's rewards, past the goal (CommunityData.BonusStep apart)
        public List<RewardDef> Bonus { get; set; } = new List<RewardDef>();
        // for the top CommunityData.TopCount contributors, once the goal has ended
        public List<RewardDef> Top { get; set; } = new List<RewardDef>();
        // what a {"roll": n} reward of this goal draws from
        public List<LootEntry> Loot { get; set; } = new List<LootEntry>();
        // a special goal: from and until these UTC days (yyyy-MM-dd, both included), besides the
        // week's. Its goal is never scaled.
        public string From { get; set; }
        public string Until { get; set; }

        public string Describe() => Describe(Goal);
        public string Describe(int goal) => QuestData.Fill(Text, goal);

        public bool Counts(in QuestEvent e) => e.Type == Event && (Filter == null || Filter.Matches(e));

        [JsonIgnore] public DateTime? StartsAt => Day(From);
        [JsonIgnore] public DateTime? EndsAt => Day(Until)?.AddDays(1);

        internal static DateTime? Day(string s) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }

    // One line of a goal's loot table: `count` of `item`, drawn by `weight`, of a rarity
    // (CommunityData.Rarities) that Overdrive levels make likelier (RarityLift).
    public sealed class LootEntry
    {
        public string Item { get; set; }
        public int Count { get; set; } = 1;
        public double Weight { get; set; } = 1;
        public string Rarity { get; set; } = "common";

        public override string ToString() => Count > 1 ? $"{Count}x {Item}" : Item;
    }

    // How a goal's next run is sized (CommunityData.NextScale): the goal's scale moves towards
    // the realm ending each run at `target` of its goal, by (done/target)^damping, at most
    // `minStep` to `maxStep` a run, and stays between `minScale` and `maxScale`.
    public sealed class CommunityAdapt
    {
        public bool Enabled { get; set; } = true;
        public double Target { get; set; } = 1.2;
        public double Damping { get; set; } = 0.5;
        public double MinStep { get; set; } = 0.75;
        public double MaxStep { get; set; } = 1.35;
        public double MinScale { get; set; } = 0.4;
        public double MaxScale { get; set; } = 4;
    }

    // Finishing a goal early (CommunityData.Early): once a week's goal has reached its last
    // Overdrive level, it ends there and a bonus goal takes its place until the weekly reset,
    // unless less than `minHours` are left. The faster it was finished, the harder things get
    // (CommunityData.Heat): its goal's next run grows by its pace, up to `maxStep` at once, and the
    // bonus goal is that much harder than its usual size, on top of the heat of the run it
    // replaced, up to `maxHeat`. So a chain of quick finishes gets harder each time, until the
    // weekly reset.
    public sealed class CommunityEarly
    {
        public bool Enabled { get; set; } = true;
        public double MinHours { get; set; } = 6;
        public double MaxStep { get; set; } = 3;
        public double MaxHeat { get; set; } = 8;
    }

    // A bonus goal started in a week (CommunityData.Runs): in place of the slot's goal, from
    // `Start` on, `Heat` times its usual size.
    public readonly record struct EarlyStart(int Slot, string Id, DateTime Start, double Heat = 1);

    // One run of a goal: a week's (or a special's) start and end. Its key names its counts in
    // Redis, so each run starts from nothing. A bonus run (CommunityEarly) starts in the middle of
    // its week, in its slot (the place among the week's goals; -1 for a special).
    public sealed class CommunityRun
    {
        public CommunityGoalDef Def { get; }
        public DateTime Start { get; }
        public DateTime End { get; }
        public bool Special { get; }
        public bool Bonus { get; }
        public int Slot { get; }
        // how much harder than its usual size (a bonus run after quick finishes; 1 otherwise)
        public double Heat { get; }

        public CommunityRun(CommunityGoalDef def, DateTime start, DateTime end, bool special, bool bonus = false, int slot = -1, double heat = 1)
        {
            Def = def;
            Start = start;
            End = end;
            Special = special;
            Bonus = bonus;
            Slot = slot;
            Heat = heat > 0 ? heat : 1;
        }

        // its length in weeks
        public double Weeks => Math.Max(1.0 / 168, (End - Start).TotalDays / 7);
        // ended before its week did: finished early
        public bool Cut => !Special && End < Week.AddDays(7);

        public string Key => Bonus ? $"{Def.Id}@{Start:yyyyMMddHHmmss}" : $"{Def.Id}@{Start:yyyyMMdd}";
        // the week it belongs to (its first day)
        public DateTime Week => CommunityData.WeekStart(Start);
        public bool Running(DateTime now) => now >= Start && now < End;
    }

    public sealed class CommunityData
    {
        // goals drawn each week (Monday 00:00 UTC, as the weekly quests)
        public int Slots { get; set; } = 3;
        // where each tier is reached, as shares of the goal
        public double[] TierShares { get; set; } = { 0.25, 0.5, 0.75, 1 };
        public string[] TierNames { get; set; } = { "Spark", "Flame", "Blaze", "Inferno" };
        // past the goal: an Overdrive level each BonusStep of the goal more, up to BonusTiers
        public double BonusStep { get; set; } = 0.25;
        public int BonusTiers { get; set; } = 8;
        public string BonusName { get; set; } = "Overdrive";
        public int TopCount { get; set; } = 3;
        // weeks a goal's rewards can still be claimed after it ends
        public int ClaimWeeks { get; set; } = 1;
        // the loot's rarities, commonest first, and how much likelier each Overdrive level makes
        // each: its weight times 1 + lift × level (the top's prize rolls at the highest level)
        public string[] Rarities { get; set; } = { "common", "uncommon", "rare", "epic", "legendary", "divine" };
        public double[] RarityLift { get; set; } = { 0, 0.1, 0.25, 0.4, 0.6, 0.8 };
        // rolls of this rarity or rarer are told to the whole server
        public string AnnounceFrom { get; set; } = "legendary";
        public CommunityAdapt Adapt { get; set; } = new CommunityAdapt();
        public CommunityEarly Early { get; set; } = new CommunityEarly();
        // the tiers', the Overdrive levels' and the top's rewards of goals that don't give their own
        public List<List<RewardDef>> DefaultTiers { get; set; } = new List<List<RewardDef>>();
        public List<RewardDef> DefaultBonus { get; set; } = new List<RewardDef>();
        public List<RewardDef> DefaultTop { get; set; } = new List<RewardDef>();
        public List<CommunityGoalDef> Goals { get; set; } = new List<CommunityGoalDef>();
        public List<CommunityGoalDef> Specials { get; set; } = new List<CommunityGoalDef>();

        public const int TOP_TIER = 255;
        // claims of Overdrive level k (1 first) are tier BONUS_TIER + k - 1
        public const int BONUS_TIER = 100;

        public static DateTime WeekStart(DateTime utc) => QuestPeriods.NextReset(QuestPeriod.Weekly, utc).AddDays(-7);

        public CommunityGoalDef Goal(string id) => Goals.Concat(Specials).FirstOrDefault(g => g.Id == id);

        public int Tiers(CommunityGoalDef def) => Math.Min(TierShares.Length, def.Tiers.Count);

        // Overdrive levels a goal has (none without bonus rewards)
        public int BonusLevels(CommunityGoalDef def) => def.Bonus.Count > 0 ? Math.Clamp(BonusTiers, 0, TOP_TIER - BONUS_TIER - 1) : 0;

        // A run's goal: the goal as written times its scale, to two significant digits.
        public static int GoalOf(CommunityGoalDef def, double scale) => Nice(def.Goal * (scale > 0 ? scale : 1));

        // A round number near `x`: whole below 100, else two significant digits (1,234 is 1,200).
        public static int Nice(double x)
        {
            if (double.IsNaN(x) || x < 1)
                return 1;
            if (x >= int.MaxValue / 2.0)
                return int.MaxValue / 2;
            if (x < 100)
                return (int)Math.Round(x, MidpointRounding.AwayFromZero);
            var unit = Math.Pow(10, Math.Floor(Math.Log10(x)) - 1);
            return (int)(Math.Round(x / unit, MidpointRounding.AwayFromZero) * unit);
        }

        // The total a tier is reached at, for a run's goal.
        public int Threshold(int goal, int tier) => Math.Max(1, (int)Math.Ceiling(goal * TierShares[Math.Min(tier, TierShares.Length - 1)]));

        // The total Overdrive level `level` (1 first) is reached at.
        public int BonusThreshold(int goal, int level) => Math.Max(goal + level, (int)Math.Ceiling(goal * (1 + BonusStep * level)));

        // tiers reached by this total
        public int Reached(CommunityGoalDef def, int goal, long total)
        {
            var n = 0;
            while (n < Tiers(def) && total >= Threshold(goal, n))
                n++;
            return n;
        }

        // Overdrive levels reached by this total (none before every tier is)
        public int BonusReached(CommunityGoalDef def, int goal, long total)
        {
            if (Reached(def, goal, total) < Tiers(def))
                return 0;
            var n = 0;
            while (n < BonusLevels(def) && total >= BonusThreshold(goal, n + 1))
                n++;
            return n;
        }

        // The tiers and Overdrive levels reached, in one count (Crossed).
        public int Stage(CommunityGoalDef def, int goal, long total) => Reached(def, goal, total) + BonusReached(def, goal, total);

        // Every tier and Overdrive level reached: nothing more to earn.
        public bool Finished(CommunityGoalDef def, int goal, long total) => Stage(def, goal, total) >= Tiers(def) + BonusLevels(def);

        // How much harder a run finished early makes things: its pace over a whole week (`total`
        // of `goal` in `weeks`) against the target, as NextScale moves a goal, from 1 to
        // Early.MaxStep.
        public double PaceStep(long total, int goal, double weeks)
        {
            if (goal <= 0)
                return 1;
            var pace = (double)total / goal / Math.Max(1.0 / 168, weeks);
            return Math.Clamp(Math.Pow(pace / Math.Max(0.01, Adapt.Target), Adapt.Damping), 1, Math.Max(1, Early.MaxStep));
        }

        // The heat of the bonus goal after `run` was finished at `total` of `goal` at `now`: the
        // run's own heat times its pace (PaceStep), at most Early.MaxHeat.
        public double Heat(CommunityRun run, long total, int goal, DateTime now) =>
            Math.Clamp(run.Heat * PaceStep(total, goal, (now - run.Start).TotalDays / 7), 1, Math.Max(1, Early.MaxHeat));

        // A bonus goal for a week that has `used` (its goals so far): by weight, one of a group in
        // neither this week, next week's draw nor last week's (`lastWeek`, as kept; drawn when not
        // given); else in neither this week nor next; else not in this week; else none. The same
        // on every server.
        public string NextGoal(DateTime week, ICollection<string> used, List<string> lastWeek = null)
        {
            HashSet<string> GroupsOf(IEnumerable<string> ids) => Goals.Where(g => ids.Contains(g.Id)).Select(GroupOf).ToHashSet();
            var usedGroups = GroupsOf(used);
            var nextGroups = GroupsOf(Draw(week.AddDays(7)));
            var lastGroups = GroupsOf(lastWeek ?? Draw(week.AddDays(-7)));
            var pool = Goals.Where(g => g.Weight > 0 && !usedGroups.Contains(GroupOf(g))).ToList();
            foreach (var avoid in new[] { nextGroups.Union(lastGroups).ToHashSet(), nextGroups })
                if (pool.Any(g => !avoid.Contains(GroupOf(g))))
                {
                    pool = pool.Where(g => !avoid.Contains(GroupOf(g))).ToList();
                    break;
                }
            if (pool.Count == 0)
                return null;
            var rng = new Random(Mix(QuestPeriods.WeekKey(week), used.Count, 15485863));
            // a group, then one of its goals by weight
            var groups = pool.Select(GroupOf).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
            var group = groups[rng.Next(groups.Count)];
            var goals = pool.Where(g => GroupOf(g) == group).ToList();
            var roll = rng.NextDouble() * goals.Sum(g => g.Weight);
            foreach (var g in goals)
            {
                roll -= g.Weight;
                if (roll < 0)
                    return g.Id;
            }
            return goals[^1].Id;
        }

        // The total of the next stage after `stage` (none past the last: -1).
        public int NextAt(CommunityGoalDef def, int goal, int stage)
        {
            var tiers = Tiers(def);
            if (stage < tiers)
                return Threshold(goal, stage);
            return stage - tiers < BonusLevels(def) ? BonusThreshold(goal, stage - tiers + 1) : -1;
        }

        public string TierName(int tier) => tier < TierNames.Length ? TierNames[tier] : $"Tier {tier + 1}";

        public string BonusLevelName(int level) => $"{BonusName} {Roman(level)}";

        // A stage's name: a tier's, or an Overdrive level's.
        public string StageName(CommunityGoalDef def, int stage) => stage < Tiers(def) ? TierName(stage) : BonusLevelName(stage - Tiers(def) + 1);

        public static string Roman(int n)
        {
            if (n <= 0 || n >= 40)
                return n.ToString();
            var s = "";
            foreach (var (v, r) in new[] { (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
                while (n >= v)
                {
                    s += r;
                    n -= v;
                }
            return s;
        }

        // A claim's rewards and the Overdrive level its rolls are made at: a tier (0), a level
        // (BONUS_TIER + k - 1, at k) or the top's prize (the highest level).
        public (List<RewardDef> Rewards, int Level) ClaimRewards(CommunityGoalDef def, int tier)
        {
            if (tier == TOP_TIER)
                return (def.Top, Math.Max(1, BonusTiers));
            if (tier >= BONUS_TIER)
                return (def.Bonus, tier - BONUS_TIER + 1);
            return (tier < def.Tiers.Count ? def.Tiers[tier] : new List<RewardDef>(), 0);
        }

        public int RarityOf(LootEntry e)
        {
            var i = Array.FindIndex(Rarities, r => string.Equals(r, e.Rarity, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? 0 : i;
        }

        private double Lift(int rarity) => rarity < RarityLift.Length ? RarityLift[rarity] : RarityLift.LastOrDefault();

        // Each loot entry's chance of a roll at an Overdrive level (0 for a tier's), summing to 1.
        public List<(LootEntry Entry, double Chance)> Odds(CommunityGoalDef def, int level)
        {
            var weights = def.Loot.Select(e => (Entry: e, W: Math.Max(0, e.Weight) * (1 + Lift(RarityOf(e)) * Math.Max(0, level)))).ToList();
            var sum = weights.Sum(w => w.W);
            return sum <= 0 ? new List<(LootEntry, double)>() : weights.Select(w => (w.Entry, w.W / sum)).ToList();
        }

        public LootEntry Roll(CommunityGoalDef def, int level, Random rng)
        {
            var odds = Odds(def, level);
            if (odds.Count == 0)
                return null;
            var roll = rng.NextDouble();
            foreach (var (e, chance) in odds)
            {
                roll -= chance;
                if (roll < 0)
                    return e;
            }
            return odds[^1].Entry;
        }

        // The reel the client scrolls past to show a roll (the Warcaller's, as the death spin's):
        // `length` lines of the goal's loot at the roll's odds, the rarer shown a little more often
        // to tease, with the prize at `stop`.
        public List<LootEntry> Reel(CommunityGoalDef def, int level, LootEntry prize, int length, int stop, Random rng)
        {
            var tease = Odds(def, level).Select(o => (o.Entry, W: o.Chance * (1 + 0.6 * RarityOf(o.Entry)))).Where(o => o.W > 0).ToList();
            var sum = tease.Sum(o => o.W);
            var reel = new List<LootEntry>(length);
            for (var i = 0; i < length; i++)
            {
                if (i == stop || sum <= 0)
                {
                    reel.Add(prize);
                    continue;
                }
                var roll = rng.NextDouble() * sum;
                var pick = tease[^1].Entry;
                foreach (var (e, w) in tease)
                {
                    roll -= w;
                    if (roll < 0)
                    {
                        pick = e;
                        break;
                    }
                }
                reel.Add(pick);
            }
            return reel;
        }

        // Rewards with their rolls made: each {"roll": n} becomes n items of the goal's loot; the
        // rolls made are listed in `rolled`.
        public List<RewardDef> Resolve(CommunityGoalDef def, IEnumerable<RewardDef> rewards, int level, Random rng, List<LootEntry> rolled = null)
        {
            var paid = new List<RewardDef>();
            foreach (var r in rewards)
            {
                if (r.Roll <= 0)
                {
                    paid.Add(r);
                    continue;
                }
                for (var i = 0; i < r.Roll; i++)
                {
                    var e = Roll(def, level, rng);
                    if (e == null)
                        continue;
                    rolled?.Add(e);
                    paid.Add(new RewardDef { Item = e.Item, Count = Math.Max(1, e.Count) });
                }
            }
            return paid;
        }

        public bool Announced(LootEntry e)
        {
            var from = Array.FindIndex(Rarities, r => string.Equals(r, AnnounceFrom, StringComparison.OrdinalIgnoreCase));
            return from >= 0 && RarityOf(e) >= from;
        }

        // The goal's next scale after a run that ended at `total` of `goal`, from `scale`: a run
        // done well past `Adapt.Target` makes the next harder, one that fell short easier.
        // A run shorter than a week (`weeks`: finished early, or a bonus run) counts by its pace
        // over a whole week, and one finished early (`cut`) can grow by up to Early.MaxStep.
        public double NextScale(double scale, long total, int goal, double weeks = 1, bool cut = false)
        {
            var a = Adapt;
            if (!a.Enabled || goal <= 0)
                return scale;
            scale = scale > 0 ? scale : 1;
            var done = Math.Max(0.02, (double)total / goal / Math.Clamp(weeks, 1.0 / 168, 1));
            var step = Math.Clamp(Math.Pow(done / Math.Max(0.01, a.Target), a.Damping), a.MinStep, cut ? Math.Max(a.MaxStep, Early.MaxStep) : a.MaxStep);
            return Math.Clamp(scale * step, a.MinScale, a.MaxScale);
        }

        private static string GroupOf(CommunityGoalDef g) => g.Group ?? g.Id;

        // The week's goals (`week` is its first day): `Slots` of the pool's groups in turn, so a
        // group comes back only once the others have had their week, and one goal of each group
        // by weight. The same on every server and every run.
        public List<string> Draw(DateTime week)
        {
            var groups = Goals.Where(g => g.Weight > 0).Select(GroupOf).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
            if (groups.Count == 0)
                return new List<string>();
            // a fixed order of the groups, then each week the next `Slots` of them
            var order = new Random(Mix(groups.Count, 7919)).Shuffled(groups);
            var key = QuestPeriods.WeekKey(week);
            var take = Math.Min(Slots, groups.Count);
            var picked = new List<string>();
            var rng = new Random(Mix(key, 104729));
            for (var i = 0; i < take; i++)
            {
                var group = order[Mod((long)key * take + i, order.Count)];
                var pool = Goals.Where(g => GroupOf(g) == group && g.Weight > 0).ToList();
                var roll = rng.NextDouble() * pool.Sum(g => g.Weight);
                var pick = pool[pool.Count - 1];
                foreach (var g in pool)
                {
                    roll -= g.Weight;
                    if (roll < 0)
                    {
                        pick = g;
                        break;
                    }
                }
                picked.Add(pick.Id);
            }
            return picked;
        }

        // The runs at `now`: this week's goals and the specials open, then those ended no more
        // than ClaimWeeks ago, whose rewards can still be claimed. `weekGoals` gives a week's
        // goals by its first day (as they were drawn then, kept by the server; Draw when not),
        // and `earlyStarts` the bonus goals started in it (CommunityEarly): each ends the run
        // before it in its slot.
        public List<CommunityRun> Runs(DateTime now, Func<DateTime, List<string>> weekGoals = null, Func<DateTime, IReadOnlyList<EarlyStart>> earlyStarts = null)
        {
            weekGoals ??= Draw;
            var runs = new List<CommunityRun>();
            var week = WeekStart(now);
            for (var w = 0; w <= ClaimWeeks; w++)
            {
                var start = week.AddDays(-7 * w);
                var end = start.AddDays(7);
                var drawn = weekGoals(start);
                // the week's bonus goals in the order they began, each goal once a week: a second
                // start of a goal already in the week is left out (its slot carries on as it was)
                var seen = drawn.ToHashSet();
                var early = (earlyStarts?.Invoke(start) ?? Array.Empty<EarlyStart>())
                    .Where(e => e.Slot >= 0 && e.Slot < drawn.Count && e.Start > start && e.Start < end)
                    .OrderBy(e => e.Start).ThenBy(e => e.Slot)
                    .Where(e => seen.Add(e.Id))
                    .ToList();
                for (var slot = 0; slot < drawn.Count; slot++)
                {
                    // the slot's goals in turn: the drawn one, then each bonus goal
                    var chain = new List<(string Id, DateTime Start, double Heat)> { (drawn[slot], start, 1) };
                    chain.AddRange(early.Where(e => e.Slot == slot).Select(e => (e.Id, e.Start, e.Heat)));
                    for (var k = 0; k < chain.Count; k++)
                    {
                        var def = Goals.FirstOrDefault(g => g.Id == chain[k].Id);
                        if (def != null)
                            runs.Add(new CommunityRun(def, chain[k].Start, k + 1 < chain.Count ? chain[k + 1].Start : end, false, k > 0, slot, chain[k].Heat));
                    }
                }
            }
            foreach (var s in Specials)
            {
                if (s.StartsAt is not DateTime from || s.EndsAt is not DateTime until)
                    continue;
                if (now >= from && now < until.AddDays(7 * ClaimWeeks))
                    runs.Add(new CommunityRun(s, from, until, true));
            }
            // the running ones first, specials before the week's, then the ended, newest first
            return runs.OrderBy(r => r.Running(now) ? 0 : 1).ThenBy(r => r.Special ? 0 : 1).ThenByDescending(r => r.End).ThenBy(r => r.Slot).ToList();
        }

        public static CommunityData Parse(string json)
        {
            var data = JsonConvert.DeserializeObject<CommunityData>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore })
                ?? new CommunityData();
            data.Adapt ??= new CommunityAdapt();
            data.Early ??= new CommunityEarly();
            foreach (var g in data.Goals.Concat(data.Specials))
            {
                if (g.Tiers == null || g.Tiers.Count == 0)
                    g.Tiers = data.DefaultTiers;
                g.Bonus ??= new List<RewardDef>();
                if (g.Bonus.Count == 0)
                    g.Bonus = data.DefaultBonus;
                g.Top ??= new List<RewardDef>();
                if (g.Top.Count == 0)
                    g.Top = data.DefaultTop;
                g.Loot ??= new List<LootEntry>();
            }
            return data;
        }

        public static CommunityData Load(string path) => Parse(File.ReadAllText(path));

        private static string Signature(IEnumerable<RewardDef> rewards) =>
            string.Join("+", rewards.Select(r => r.Roll > 0 ? $"roll{r.Roll}" : r.Item != null ? $"{r.Item}x{r.Count}" : $"fame{r.Fame}").OrderBy(s => s, StringComparer.Ordinal));

        // Problems with the file, for the log: unknown events, items that don't exist, goals
        // without a goal or with fewer tiers than TierShares, rolls with no loot to roll, unknown
        // rarities, and goals that give the same rewards as another. Rewards and loot of items
        // that don't exist are dropped.
        public List<string> Validate(Func<string, bool> itemExists)
        {
            var problems = new List<string>();
            void Rewards(string where, List<RewardDef> rewards)
            {
                if (rewards == null)
                    return;
                foreach (var r in rewards.Where(r => r.Item != null && !itemExists(r.Item)).ToList())
                {
                    problems.Add($"{where}: no item \"{r.Item}\"");
                    rewards.Remove(r);
                }
            }
            for (var t = 0; t < DefaultTiers.Count; t++)
                Rewards($"default tier {t + 1}", DefaultTiers[t]);
            Rewards("default bonus", DefaultBonus);
            Rewards("default top", DefaultTop);
            if (RarityLift.Length < Rarities.Length)
                problems.Add($"{Rarities.Length} rarities, {RarityLift.Length} lifts");
            foreach (var g in Goals.Concat(Specials))
            {
                if (!QuestEvents.All.Contains(g.Event))
                    problems.Add($"goal {g.Id}: unknown event \"{g.Event}\"");
                if (g.Goal <= 0)
                    problems.Add($"goal {g.Id}: no goal");
                if (g.Tiers.Count < TierShares.Length)
                    problems.Add($"goal {g.Id}: {g.Tiers.Count} tiers of rewards, {TierShares.Length} wanted");
                if (g.Tiers != DefaultTiers)
                    for (var t = 0; t < g.Tiers.Count; t++)
                        Rewards($"goal {g.Id} tier {t + 1}", g.Tiers[t]);
                if (g.Bonus != DefaultBonus)
                    Rewards($"goal {g.Id} bonus", g.Bonus);
                if (g.Top != DefaultTop)
                    Rewards($"goal {g.Id} top", g.Top);
                foreach (var e in g.Loot.Where(e => e.Item == null || !itemExists(e.Item)).ToList())
                {
                    problems.Add($"goal {g.Id} loot: no item \"{e.Item}\"");
                    g.Loot.Remove(e);
                }
                foreach (var e in g.Loot.Where(e => !Rarities.Any(r => string.Equals(r, e.Rarity, StringComparison.OrdinalIgnoreCase))))
                    problems.Add($"goal {g.Id} loot {e.Item}: unknown rarity \"{e.Rarity}\"");
                var rolls = g.Tiers.SelectMany(t => t).Concat(g.Bonus).Concat(g.Top).Any(r => r.Roll > 0);
                if (rolls && g.Loot.Count == 0)
                    problems.Add($"goal {g.Id}: its rewards roll loot, and it has none");
            }
            foreach (var s in Specials)
                if (s.StartsAt == null || s.EndsAt == null)
                    problems.Add($"special {s.Id}: its dates aren't yyyy-MM-dd");
            foreach (var dup in Goals.Concat(Specials).GroupBy(g => g.Id).Where(g => g.Count() > 1))
                problems.Add($"goal id {dup.Key} is used twice");
            // each goal its own: no two give the same tiers of their own, nor roll the same loot
            var all = Goals.Concat(Specials).ToList();
            foreach (var same in all.Where(g => g.Tiers != DefaultTiers).GroupBy(g => string.Join("|", g.Tiers.Select(Signature))).Where(s => s.Count() > 1))
                problems.Add($"goals {string.Join(", ", same.Select(g => g.Id))} give the same tiers");
            foreach (var same in all.Where(g => g.Loot.Count > 0).GroupBy(g => string.Join("|", g.Loot.Select(e => e.Item).OrderBy(i => i, StringComparer.Ordinal))).Where(s => s.Count() > 1))
                problems.Add($"goals {string.Join(", ", same.Select(g => g.Id))} roll the same loot");
            if (Goals.Select(GroupOf).Distinct().Count() < Slots)
                problems.Add($"fewer goal groups than the {Slots} drawn a week");
            return problems;
        }

        private static int Mod(long a, int b) => (int)(((a % b) + b) % b);

        // A seed that is the same on every run (string.GetHashCode isn't).
        private static int Mix(params int[] parts)
        {
            unchecked
            {
                var h = (int)2166136261;
                foreach (var v in parts)
                {
                    h = (h ^ v) * 16777619;
                    h ^= h >> 13;
                }
                return h & 0x7FFFFFFF;
            }
        }
    }

    internal static class ShuffleExtensions
    {
        public static List<T> Shuffled<T>(this Random rng, IEnumerable<T> items)
        {
            var list = items.ToList();
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }
}
