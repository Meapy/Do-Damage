using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shared.quests
{
    // Quests, daily login rewards and achievements, as resources/data/quests.json defines them
    // (its "_about" says how). The world server reads it at start; QuestBook does the work.
    public enum QuestPeriod : byte { Daily = 0, Weekly = 1, Monthly = 2 }

    // What can happen in the game that a quest or achievement counts (QuestEvent.Type).
    public static class QuestEvents
    {
        public const string Kill = "kill";              // a monster died near you (Name, God, Boss...)
        public const string Dungeon = "dungeon";        // you were there when a dungeon was cleared
        public const string RealmClosed = "realmClosed";// you were in a realm when it closed
        public const string ArenaWave = "arenaWave";    // an Arena wave cleared (Value: the wave)
        public const string Potion = "potion";          // a stat potion drunk (not stored: storage gives it back)
        public const string Fame = "fame";              // base fame earned (Amount)
        public const string Level = "level";            // a character level gained
        public const string Forge = "forge";            // an item forged
        public const string WhiteBag = "whiteBag";      // a white bag item dropped for you
        public const string Trade = "trade";            // a trade completed
        public const string Login = "login";            // a daily reward collected
        public const string Quest = "quest";            // a quest claimed
        public const string Classes = "classes";        // classes at level 20 (Value)
        public const string MaxedStats = "maxedStats";  // stats maxed on the character (Value)

        public static readonly string[] All =
            { Kill, Dungeon, RealmClosed, ArenaWave, Potion, Fame, Level, Forge, WhiteBag, Trade, Login, Quest, Classes, MaxedStats };
    }

    // One thing that happened: a type, how much it adds, a value for "max" counters, and what it
    // was (a monster's or dungeon's name and kind) for filters.
    public struct QuestEvent
    {
        public string Type;
        public int Amount;
        public int Value;
        public string Name;
        public bool God, Boss, Oryx, Cube, RealmEvent, Elder, Group;
        public int Difficulty;

        public QuestEvent(string type, int amount = 1) : this()
        {
            Type = type;
            Amount = amount;
            Value = amount;
        }
    }

    public sealed class RewardDef
    {
        public int Fame { get; set; }
        public string Item { get; set; }
        public int Count { get; set; } = 1;

        public override string ToString() => Fame > 0 ? $"{Fame} fame" : Count > 1 ? $"{Count}x {Item}" : Item;
    }

    // What an event must be to count (every condition set must hold).
    public sealed class EventFilter
    {
        public bool God { get; set; }
        public bool Boss { get; set; }
        public bool Oryx { get; set; }
        public bool Cube { get; set; }
        public bool RealmEvent { get; set; }
        public bool Elder { get; set; }
        public bool Group { get; set; }
        public int MinDifficulty { get; set; }
        public string[] Names { get; set; }

        public bool Matches(in QuestEvent e)
        {
            if (God && !e.God || Boss && !e.Boss || Oryx && !e.Oryx || Cube && !e.Cube)
                return false;
            if (RealmEvent && !e.RealmEvent || Elder && !e.Elder || Group && !e.Group)
                return false;
            if (e.Difficulty < MinDifficulty)
                return false;
            if (Names != null && Names.Length > 0)
            {
                var name = e.Name;
                if (name == null || !Names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            return true;
        }
    }

    public sealed class QuestDef
    {
        public string Id { get; set; }
        public string Period { get; set; }
        public string Group { get; set; }
        public double Weight { get; set; } = 1;
        public string Title { get; set; }
        public string Text { get; set; }
        public string Event { get; set; }
        public string Mode { get; set; }
        public EventFilter Filter { get; set; }
        public int Goal { get; set; }
        public List<RewardDef> Rewards { get; set; } = new List<RewardDef>();

        [JsonIgnore] public QuestPeriod PeriodKind { get; set; }
        [JsonIgnore] public bool IsMax => Mode == "max";
        public string Describe() => (Text ?? "").Replace("{n}", Goal.ToString("N0"));

        public bool Counts(in QuestEvent e) => e.Type == Event && (Filter == null || Filter.Matches(e));
    }

    public sealed class AchievementDef
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public string Event { get; set; }
        public string Mode { get; set; }
        public EventFilter Filter { get; set; }
        public int[] Tiers { get; set; } = Array.Empty<int>();

        [JsonIgnore] public bool IsMax => Mode == "max";
        [JsonIgnore] public bool IsDistinct => Mode == "distinct";
        public string Describe(int goal) => (Text ?? "").Replace("{n}", goal.ToString("N0"));

        public bool Counts(in QuestEvent e) => e.Type == Event && (Filter == null || Filter.Matches(e));
    }

    public sealed class RankDef
    {
        public int Points { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }

        [JsonIgnore] public int ColorValue => string.IsNullOrEmpty(Color) ? 0 : Convert.ToInt32(Color.Replace("0x", ""), 16);
    }

    public sealed class QuestData
    {
        public Dictionary<string, int> Slots { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> Rerolls { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, List<RewardDef>> Bonus { get; set; } = new Dictionary<string, List<RewardDef>>();
        public int[] Difficulty { get; set; } = Array.Empty<int>();
        public string[] DifficultyNames { get; set; } = Array.Empty<string>();
        public string[] NotDungeons { get; set; } = Array.Empty<string>();
        public List<QuestDef> Quests { get; set; } = new List<QuestDef>();
        public List<List<RewardDef>> Login { get; set; } = new List<List<RewardDef>>();
        public int[] LoginMilestones { get; set; } = Array.Empty<int>();
        public List<AchievementDef> Achievements { get; set; } = new List<AchievementDef>();
        public int[] TierPoints { get; set; } = Array.Empty<int>();
        public List<List<RewardDef>> TierRewards { get; set; } = new List<List<RewardDef>>();
        public string[] TierNames { get; set; } = Array.Empty<string>();
        public List<RankDef> Ranks { get; set; } = new List<RankDef>();

        public static readonly string[] PeriodNames = { "daily", "weekly", "monthly" };

        public static string Name(QuestPeriod p) => PeriodNames[(int)p];

        public int SlotsOf(QuestPeriod p) => Slots.TryGetValue(Name(p), out var n) ? n : 3;
        public int RerollsOf(QuestPeriod p) => Rerolls.TryGetValue(Name(p), out var n) ? n : 0;
        public List<RewardDef> BonusOf(QuestPeriod p) => Bonus.TryGetValue(Name(p), out var r) ? r : new List<RewardDef>();

        public QuestDef Quest(string id) => Quests.FirstOrDefault(q => q.Id == id);
        public AchievementDef Achievement(string id) => Achievements.FirstOrDefault(a => a.Id == id);
        public IEnumerable<QuestDef> Pool(QuestPeriod p) => Quests.Where(q => q.PeriodKind == p);

        public int TierPointsOf(int tier) => TierPoints.Length == 0 ? 0 : TierPoints[Math.Min(tier, TierPoints.Length - 1)];
        public List<RewardDef> TierRewardsOf(int tier) => TierRewards.Count == 0 ? new List<RewardDef>() : TierRewards[Math.Min(tier, TierRewards.Count - 1)];
        public string TierName(int tier) => tier < TierNames.Length ? TierNames[tier] : $"Tier {tier + 1}";

        public bool IsMilestone(int day) => LoginMilestones.Contains(day);

        // A dungeon's difficulty (0 Beginner ... 5 Endgame) from its strongest boss's health, as
        // the wiki rates them.
        public int DifficultyOf(int bossHp)
        {
            var d = 0;
            while (d < Difficulty.Length && bossHp >= Difficulty[d])
                d++;
            return d;
        }

        public static QuestData Parse(string json)
        {
            var data = JsonConvert.DeserializeObject<QuestData>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore })
                ?? new QuestData();
            foreach (var q in data.Quests)
                q.PeriodKind = (QuestPeriod)Math.Max(0, Array.IndexOf(PeriodNames, q.Period ?? "daily"));
            return data;
        }

        public static QuestData Load(string path) => Parse(File.ReadAllText(path));

        // Problems with the file, for the log: unknown events, items that don't exist, duplicate
        // ids. Rewards of items that don't exist are dropped.
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
            foreach (var q in Quests)
            {
                if (!QuestEvents.All.Contains(q.Event))
                    problems.Add($"quest {q.Id}: unknown event \"{q.Event}\"");
                if (q.Goal <= 0)
                    problems.Add($"quest {q.Id}: no goal");
                Rewards($"quest {q.Id}", q.Rewards);
            }
            foreach (var dup in Quests.GroupBy(q => q.Id).Where(g => g.Count() > 1))
                problems.Add($"quest id {dup.Key} is used twice");
            foreach (var a in Achievements)
                if (!QuestEvents.All.Contains(a.Event))
                    problems.Add($"achievement {a.Id}: unknown event \"{a.Event}\"");
            foreach (var dup in Achievements.GroupBy(a => a.Id).Where(g => g.Count() > 1))
                problems.Add($"achievement id {dup.Key} is used twice");
            foreach (var (name, rewards) in Bonus)
                Rewards($"{name} bonus", rewards);
            for (var i = 0; i < Login.Count; i++)
                Rewards($"login day {i + 1}", Login[i]);
            for (var i = 0; i < TierRewards.Count; i++)
                Rewards($"tier {i + 1}", TierRewards[i]);
            foreach (QuestPeriod p in Enum.GetValues(typeof(QuestPeriod)))
                if (Pool(p).Select(q => q.Group ?? q.Id).Distinct().Count() < SlotsOf(p))
                    problems.Add($"{Name(p)}: fewer quest groups than slots");
            return problems;
        }
    }
}
