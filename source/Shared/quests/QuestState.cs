using System.Collections.Generic;
using Newtonsoft.Json;

namespace Shared.quests
{
    // An account's quests, daily reward track and achievements, kept as JSON in the account's
    // "progress" field (DbAccount.Progress). Short names keep it small.
    public sealed class QuestState
    {
        [JsonProperty("v")] public int Version { get; set; } = 1;
        // the achievements were filled in from the account's earlier play (QuestSeed)
        [JsonProperty("seeded")] public bool Seeded { get; set; }
        [JsonProperty("periods")] public PeriodState[] Periods { get; set; } = { new PeriodState(), new PeriodState(), new PeriodState() };
        [JsonProperty("login")] public LoginState Login { get; set; } = new LoginState();
        [JsonProperty("ach")] public Dictionary<string, AchievementState> Achievements { get; set; } = new Dictionary<string, AchievementState>();
        // the rank whose name colour the player chose (-1: none, the default colour)
        [JsonProperty("color")] public int ColorRank { get; set; } = -1;

        public PeriodState Of(QuestPeriod p) => Periods[(int)p];

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static QuestState FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new QuestState();
            try
            {
                var s = JsonConvert.DeserializeObject<QuestState>(json) ?? new QuestState();
                if (s.Periods == null || s.Periods.Length != 3)
                    s.Periods = new[] { new PeriodState(), new PeriodState(), new PeriodState() };
                for (var i = 0; i < 3; i++)
                    s.Periods[i] ??= new PeriodState();
                s.Login ??= new LoginState();
                s.Achievements ??= new Dictionary<string, AchievementState>();
                return s;
            }
            catch (JsonException)
            {
                return new QuestState();
            }
        }
    }

    public sealed class PeriodState
    {
        // the period it's for (QuestPeriods.Key); -1 until the first is drawn
        [JsonProperty("key")] public int Key { get; set; } = -1;
        [JsonProperty("quests")] public List<QuestSlot> Quests { get; set; } = new List<QuestSlot>();
        [JsonProperty("bonus")] public bool BonusClaimed { get; set; }
        [JsonProperty("rerolls")] public int Rerolls { get; set; }
    }

    public sealed class QuestSlot
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("p")] public int Progress { get; set; }
        [JsonProperty("c")] public bool Claimed { get; set; }
    }

    public sealed class LoginState
    {
        // the day of the track the next reward is (0-based); it moves on only when one is claimed
        [JsonProperty("step")] public int Step { get; set; }
        // the day (QuestPeriods.DayKey) the last reward was claimed
        [JsonProperty("last")] public int LastDay { get; set; } = int.MinValue;
        // days in a row with a reward claimed, and all rewards claimed
        [JsonProperty("streak")] public int Streak { get; set; }
        [JsonProperty("total")] public int Total { get; set; }
    }

    public sealed class AchievementState
    {
        [JsonProperty("p")] public int Progress { get; set; }
        // tiers claimed
        [JsonProperty("t")] public int Claimed { get; set; }
        // what was counted already, for "distinct" achievements (dungeon names)
        [JsonProperty("set", NullValueHandling = NullValueHandling.Ignore)] public List<string> Seen { get; set; }
    }
}
