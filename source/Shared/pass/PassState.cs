using System.Collections.Generic;
using Newtonsoft.Json;

namespace Shared.pass
{
    // An account's Realm Pass, kept as JSON in the account's "pass" field (DbAccount.Pass): this
    // season's XP and claims, and the cosmetics the account owns for good (auras, name colours,
    // pet effects). Short names keep it small.
    public sealed class PassState
    {
        [JsonProperty("v")] public int Version { get; set; } = 1;
        // the season (Season.Key) the XP and claims are for; null before the first
        [JsonProperty("s")] public string Season { get; set; }
        [JsonProperty("xp")] public int Xp { get; set; }
        [JsonProperty("g")] public bool Gilded { get; set; }
        // tiers claimed, a bit each (bit 0: tier 1)
        [JsonProperty("fc")] public long FreeClaimed { get; set; }
        [JsonProperty("gc")] public long GildedClaimed { get; set; }
        // Wayfarer's Caches opened this season
        [JsonProperty("cache")] public int CachesOpened { get; set; }
        // the day (QuestPeriods.DayKey) the first dungeon of the day last paid its bonus
        [JsonProperty("first")] public int FirstDungeonDay { get; set; } = int.MinValue;
        // fame towards the next pass XP (PassXpTable.FamePer)
        [JsonProperty("fame")] public int FameCarry { get; set; }
        // the auras and name colours owned, by id, and the ones worn (0: none)
        [JsonProperty("auras")] public List<int> Auras { get; set; } = new List<int>();
        [JsonProperty("aura")] public int Aura { get; set; }
        [JsonProperty("colors")] public List<int> Colors { get; set; } = new List<int>();
        [JsonProperty("color")] public int Color { get; set; }
        // the pet effects owned, by id, and the one worn (0: none; since 2.6.6)
        [JsonProperty("pets")] public List<int> PetEffects { get; set; } = new List<int>();
        [JsonProperty("pet")] public int PetEffect { get; set; }
        // the season (its key) whose filled tiers' old marks were cleared (SeasonDef.Filled)
        [JsonProperty("filled")] public string FilledFor { get; set; }
        // the season (its key) whose XP was scaled to a changed XP per tier (SeasonDef.XpPerTierWas)
        [JsonProperty("rescaled")] public string RescaledFor { get; set; }
        // seasons whose Gilded track the account had (for the record and the Wayfarer's greetings)
        [JsonProperty("past")] public List<string> GildedSeasons { get; set; } = new List<string>();
        // the Gilded pass's [Gilded] chat title turned off (kept from season to season)
        [JsonProperty("notitle")] public bool HideTitle { get; set; }

        public bool FreeTaken(int tier) => (FreeClaimed & (1L << tier)) != 0;
        public bool GildedTaken(int tier) => (GildedClaimed & (1L << tier)) != 0;

        public string ToJson() => JsonConvert.SerializeObject(this);

        public static PassState FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new PassState();
            try
            {
                var s = JsonConvert.DeserializeObject<PassState>(json) ?? new PassState();
                s.Auras ??= new List<int>();
                s.Colors ??= new List<int>();
                s.PetEffects ??= new List<int>();
                s.GildedSeasons ??= new List<string>();
                return s;
            }
            catch (JsonException)
            {
                return new PassState();
            }
        }
    }
}
