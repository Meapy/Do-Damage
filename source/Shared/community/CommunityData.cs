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
    // prize more once it ends. The world server keeps the counts in Redis (WorldServer's
    // CommunityGoals) and reads this at start.
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
        public int Goal { get; set; }
        // what an account must add to share the tiers' rewards
        public int Minimum { get; set; } = 1;
        // each tier's rewards, in order (CommunityData.TierShares says when each is reached)
        public List<List<RewardDef>> Tiers { get; set; } = new List<List<RewardDef>>();
        // for the top CommunityData.TopCount contributors, once the goal has ended
        public List<RewardDef> Top { get; set; } = new List<RewardDef>();
        // a special goal: from and until these UTC days (yyyy-MM-dd, both included), besides the
        // week's
        public string From { get; set; }
        public string Until { get; set; }

        public string Describe() => QuestData.Fill(Text, Goal);

        public bool Counts(in QuestEvent e) => e.Type == Event && (Filter == null || Filter.Matches(e));

        [JsonIgnore] public DateTime? StartsAt => Day(From);
        [JsonIgnore] public DateTime? EndsAt => Day(Until)?.AddDays(1);

        internal static DateTime? Day(string s) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }

    // One run of a goal: a week's (or a special's) start and end. Its key names its counts in
    // Redis, so each run starts from nothing.
    public sealed class CommunityRun
    {
        public CommunityGoalDef Def { get; }
        public DateTime Start { get; }
        public DateTime End { get; }
        public bool Special { get; }

        public CommunityRun(CommunityGoalDef def, DateTime start, DateTime end, bool special)
        {
            Def = def;
            Start = start;
            End = end;
            Special = special;
        }

        public string Key => $"{Def.Id}@{Start:yyyyMMdd}";
        public bool Running(DateTime now) => now >= Start && now < End;
    }

    public sealed class CommunityData
    {
        // goals drawn each week (Monday 00:00 UTC, as the weekly quests)
        public int Slots { get; set; } = 3;
        // where each tier is reached, as shares of the goal
        public double[] TierShares { get; set; } = { 0.25, 0.5, 0.75, 1 };
        public string[] TierNames { get; set; } = { "Spark", "Flame", "Blaze", "Inferno" };
        public int TopCount { get; set; } = 3;
        // weeks a goal's rewards can still be claimed after it ends
        public int ClaimWeeks { get; set; } = 1;
        // the tiers' and the top's rewards of goals that don't give their own
        public List<List<RewardDef>> DefaultTiers { get; set; } = new List<List<RewardDef>>();
        public List<RewardDef> DefaultTop { get; set; } = new List<RewardDef>();
        public List<CommunityGoalDef> Goals { get; set; } = new List<CommunityGoalDef>();
        public List<CommunityGoalDef> Specials { get; set; } = new List<CommunityGoalDef>();

        public const int TOP_TIER = 255;

        public static DateTime WeekStart(DateTime utc) => QuestPeriods.NextReset(QuestPeriod.Weekly, utc).AddDays(-7);

        public CommunityGoalDef Goal(string id) => Goals.Concat(Specials).FirstOrDefault(g => g.Id == id);

        public int Tiers(CommunityGoalDef def) => Math.Min(TierShares.Length, def.Tiers.Count);

        // the total a tier is reached at
        public int Threshold(CommunityGoalDef def, int tier) => Math.Max(1, (int)Math.Ceiling(def.Goal * TierShares[Math.Min(tier, TierShares.Length - 1)]));

        // tiers reached by this total
        public int Reached(CommunityGoalDef def, long total)
        {
            var n = 0;
            while (n < Tiers(def) && total >= Threshold(def, n))
                n++;
            return n;
        }

        public string TierName(int tier) => tier < TierNames.Length ? TierNames[tier] : $"Tier {tier + 1}";

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
        // goals by its first day (as they were drawn then, kept by the server; Draw when not).
        public List<CommunityRun> Runs(DateTime now, Func<DateTime, List<string>> weekGoals = null)
        {
            weekGoals ??= Draw;
            var runs = new List<CommunityRun>();
            var week = WeekStart(now);
            for (var w = 0; w <= ClaimWeeks; w++)
            {
                var start = week.AddDays(-7 * w);
                foreach (var id in weekGoals(start))
                {
                    var def = Goals.FirstOrDefault(g => g.Id == id);
                    if (def != null)
                        runs.Add(new CommunityRun(def, start, start.AddDays(7), false));
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
            return runs.OrderBy(r => r.Running(now) ? 0 : 1).ThenBy(r => r.Special ? 0 : 1).ThenByDescending(r => r.End).ToList();
        }

        public static CommunityData Parse(string json)
        {
            var data = JsonConvert.DeserializeObject<CommunityData>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore })
                ?? new CommunityData();
            foreach (var g in data.Goals.Concat(data.Specials))
            {
                if (g.Tiers == null || g.Tiers.Count == 0)
                    g.Tiers = data.DefaultTiers;
                g.Top ??= new List<RewardDef>();
                if (g.Top.Count == 0)
                    g.Top = data.DefaultTop;
            }
            return data;
        }

        public static CommunityData Load(string path) => Parse(File.ReadAllText(path));

        // Problems with the file, for the log: unknown events, items that don't exist, goals
        // without a goal or with fewer tiers than TierShares. Rewards of items that don't exist
        // are dropped.
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
            Rewards("default top", DefaultTop);
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
                if (g.Top != DefaultTop)
                    Rewards($"goal {g.Id} top", g.Top);
            }
            foreach (var s in Specials)
                if (s.StartsAt == null || s.EndsAt == null)
                    problems.Add($"special {s.Id}: its dates aren't yyyy-MM-dd");
            foreach (var dup in Goals.Concat(Specials).GroupBy(g => g.Id).Where(g => g.Count() > 1))
                problems.Add($"goal id {dup.Key} is used twice");
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
