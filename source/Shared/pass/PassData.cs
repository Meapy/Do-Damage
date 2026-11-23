using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Shared.quests;

namespace Shared.pass
{
    // The Realm Pass, as resources/data/pass.json defines it (its "_about" says how): monthly
    // seasons of tiers with a free and a Gilded track, what earns pass XP, the Wayfarer's Caches
    // past the last tier, and the cosmetics (auras, name colours) seasons give. The world server
    // reads it at start; PassBook does the work.

    // One reward: fame or gold to the account, an item (to the vault's gift chest), a skin (by
    // its id: tools/draw_pass_skins.py draws them), an aura, a name colour, a pet effect, or a
    // character slot.
    public sealed class PassReward
    {
        public int Fame { get; set; }
        public int Gold { get; set; }
        public string Item { get; set; }
        public int Count { get; set; } = 1;
        public string Skin { get; set; }
        public int Aura { get; set; }
        public int Color { get; set; }
        public int PetFx { get; set; }
        public int Slot { get; set; }

        public bool IsCosmetic => Skin != null || Aura > 0 || Color > 0 || PetFx > 0;

        public override string ToString() =>
            Fame > 0 ? $"{Fame} fame"
            : Gold > 0 ? $"{Gold} gold"
            : Skin != null ? $"the {Skin} skin"
            : Aura > 0 ? $"aura {Aura}"
            : Color > 0 ? $"name colour {Color}"
            : PetFx > 0 ? $"pet effect {PetFx}"
            : Slot > 0 ? $"{Slot} character slot{(Slot == 1 ? "" : "s")}"
            : Count > 1 ? $"{Count}x {Item}" : Item;
    }

    // An aura, name colour or pet effect a season gives: its number (kept for good: accounts own
    // them by it), its name and colour.
    public sealed class PassCosmetic
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }

        [JsonIgnore] public int ColorValue => ParseColor(Color);

        public static int ParseColor(string c) => string.IsNullOrEmpty(c) ? 0 : Convert.ToInt32(c.Replace("0x", "").Replace("#", ""), 16);
    }

    // A line of the Wayfarer's Cache: its weight among the rest, and what it gives.
    public sealed class CacheLine
    {
        public double Weight { get; set; } = 1;
        public List<PassReward> Rewards { get; set; } = new List<PassReward>();
    }

    // What earns pass XP (PassBook.Xp), by source; the client words each.
    public sealed class PassXpTable
    {
        public int Login { get; set; } = 100;
        public int QuestDaily { get; set; } = 150;
        public int QuestWeekly { get; set; } = 400;
        public int QuestMonthly { get; set; } = 1000;
        public int BonusDaily { get; set; } = 150;
        public int BonusWeekly { get; set; } = 400;
        public int BonusMonthly { get; set; } = 1000;
        public int FirstDungeon { get; set; } = 200;
        public int Dungeon { get; set; } = 25;
        public int DungeonStep { get; set; } = 15;
        public int Elder { get; set; } = 50;
        public int Boss { get; set; } = 10;
        public int Oryx { get; set; } = 100;
        public int RealmClosed { get; set; } = 60;
        public int ArenaWave { get; set; } = 5;
        // one pass XP for this much base fame
        public int FamePer { get; set; } = 5;
    }

    // What a Gilded pass does for the community goals' loot (CommunityGoals.Claim): its rolls are
    // made this many Overdrive levels luckier, and each stage claimed that rolls loot rolls this
    // many more times. It can also rally one community goal a run (CommunityGoals.Rally): until
    // the run ends everyone's contributions to it count RallyMultiplier times, and the one who
    // rallied shares the goal's top prize whatever their place.
    public sealed class PassCommunityBoost
    {
        public int Luck { get; set; } = 1;
        public int Rolls { get; set; } = 1;
        public int RallyMultiplier { get; set; } = 2;
    }

    public sealed class SeasonDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        // the client's look for it: frost, ember or star
        public string Theme { get; set; }
        // UTC days (yyyy-MM-dd), both included; a rotation season has none
        public string From { get; set; }
        public string Until { get; set; }
        public string Accent { get; set; }
        public string Dark { get; set; }
        public string Light { get; set; }
        // each tier's rewards on each track (an empty list: nothing on that track there)
        public List<List<PassReward>> Free { get; set; } = new List<List<PassReward>>();
        public List<List<PassReward>> Gilded { get; set; } = new List<List<PassReward>>();
        // free tiers (from 1) that were empty and were given rewards while the season ran: an
        // empty tier is marked claimed when its Gilded reward is, so those marks are cleared once
        // (PassBook.Refresh) and the new rewards can be claimed
        public List<int> Filled { get; set; } = new List<int>();

        [JsonIgnore] public DateTime StartsAt => Day(From) ?? DateTime.MinValue;
        [JsonIgnore] public DateTime EndsAt => Day(Until)?.AddDays(1) ?? DateTime.MinValue;

        public List<PassReward> FreeAt(int tier) => tier < Free.Count ? Free[tier] ?? new List<PassReward>() : new List<PassReward>();
        public List<PassReward> GildedAt(int tier) => tier < Gilded.Count ? Gilded[tier] ?? new List<PassReward>() : new List<PassReward>();

        public static DateTime? Day(string s) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }

    // The season running at some time: its definition, and when this run of it starts and ends.
    // A season defined with dates runs once (Key its id); after the last of them, the rotation's
    // come round a calendar month each (Key "<id>-yyyy-MM", Round 2 and on).
    public sealed class Season
    {
        public SeasonDef Def;
        public string Key;
        public int Round = 1;
        public DateTime StartsAt;
        public DateTime EndsAt;

        public string Name => Round <= 1 ? Def.Name : $"{Def.Name} {Roman(Round)}";

        private static string Roman(int n)
        {
            var parts = new[] { (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
            var s = "";
            foreach (var (v, r) in parts)
                while (n >= v)
                {
                    s += r;
                    n -= v;
                }
            return s;
        }
    }

    public sealed class PassData
    {
        public int Price { get; set; } = 1000;
        public int Tiers { get; set; } = 30;
        public int XpPerTier { get; set; } = 1000;
        // past the last tier: a Wayfarer's Cache each this much XP
        public int BonusXp { get; set; } = 1500;
        // the pace: every tier by this share of the season; behind it, XP gains Momentum percent more
        public double Pace { get; set; } = 0.75;
        public int Momentum { get; set; } = 50;
        // what a cosmetic the account owns already pays instead, in fame
        public int RepeatFame { get; set; } = 250;
        public PassXpTable Xp { get; set; } = new PassXpTable();
        public PassCommunityBoost Community { get; set; } = new PassCommunityBoost();
        public List<CacheLine> Cache { get; set; } = new List<CacheLine>();
        public List<PassCosmetic> Auras { get; set; } = new List<PassCosmetic>();
        public List<PassCosmetic> Colors { get; set; } = new List<PassCosmetic>();
        // the effects round a pet (since 2.6.6; the Rust client's petfx.rs draws them by id)
        public List<PassCosmetic> PetEffects { get; set; } = new List<PassCosmetic>();
        public List<SeasonDef> Seasons { get; set; } = new List<SeasonDef>();
        public List<string> Rotation { get; set; } = new List<string>();

        public SeasonDef Def(string id) => Seasons.FirstOrDefault(s => s.Id == id);
        public PassCosmetic Aura(int id) => Auras.FirstOrDefault(a => a.Id == id);
        public PassCosmetic Color(int id) => Colors.FirstOrDefault(c => c.Id == id);
        public PassCosmetic PetEffect(int id) => PetEffects.FirstOrDefault(c => c.Id == id);

        // The season running at `now`, or null (before the first, or none defined).
        public Season At(DateTime now)
        {
            var dated = Seasons.Where(s => s.From != null && s.Until != null).OrderBy(s => s.StartsAt).ToList();
            var on = dated.FirstOrDefault(s => now >= s.StartsAt && now < s.EndsAt);
            if (on != null)
                return new Season { Def = on, Key = on.Id, StartsAt = on.StartsAt, EndsAt = on.EndsAt };
            var rotation = Rotation.Select(Def).Where(d => d != null).ToList();
            if (dated.Count == 0 || rotation.Count == 0)
                return null;
            var last = dated.Last().EndsAt;
            if (now < last)
                return null;
            // calendar months from the one the last dated season ended in (it may end mid-month:
            // that month's rest belongs to the first rotation season too)
            var first = new DateTime(last.Year, last.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var months = QuestPeriods.MonthKey(now) - QuestPeriods.MonthKey(first);
            var def = rotation[months % rotation.Count];
            var start = first.AddMonths(months);
            // how many times this one has run: once with dates, and once each time round
            var round = 1 + (dated.Any(s => s.Id == def.Id) ? 1 : 0) + months / rotation.Count;
            return new Season
            {
                Def = def,
                Key = $"{def.Id}-{now:yyyy-MM}",
                Round = round,
                StartsAt = months == 0 ? last : start,
                EndsAt = start.AddMonths(1),
            };
        }

        // The season after the one at `now` (for the window's "next season" line), or null.
        public Season After(DateTime now)
        {
            var s = At(now);
            return s == null ? null : At(s.EndsAt.AddMinutes(1));
        }

        public static PassData Parse(string json) =>
            JsonConvert.DeserializeObject<PassData>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore })
            ?? new PassData();

        public static PassData Load(string path) => Parse(File.ReadAllText(path));

        // Problems with the file, for the log. Rewards of items or skins that don't exist are dropped.
        public List<string> Validate(Func<string, bool> itemExists, Func<string, bool> skinExists)
        {
            var problems = new List<string>();
            void Check(string where, List<PassReward> rewards)
            {
                if (rewards == null)
                    return;
                foreach (var r in rewards.ToList())
                {
                    if (r.Item != null && !itemExists(r.Item))
                    {
                        problems.Add($"{where}: no item \"{r.Item}\"");
                        rewards.Remove(r);
                    }
                    else if (r.Skin != null && !skinExists(r.Skin))
                    {
                        problems.Add($"{where}: no skin \"{r.Skin}\"");
                        rewards.Remove(r);
                    }
                    else if (r.Aura > 0 && Aura(r.Aura) == null)
                        problems.Add($"{where}: no aura {r.Aura}");
                    else if (r.Color > 0 && Color(r.Color) == null)
                        problems.Add($"{where}: no name colour {r.Color}");
                    else if (r.PetFx > 0 && PetEffect(r.PetFx) == null)
                        problems.Add($"{where}: no pet effect {r.PetFx}");
                }
            }
            foreach (var s in Seasons)
            {
                if ((s.From == null) != (s.Until == null) || s.From != null && (s.StartsAt == DateTime.MinValue || s.EndsAt <= s.StartsAt))
                    problems.Add($"season {s.Id}: its dates aren't yyyy-MM-dd, from before until");
                if (s.Free.Count != Tiers || s.Gilded.Count != Tiers)
                    problems.Add($"season {s.Id}: {s.Free.Count} free and {s.Gilded.Count} Gilded tiers, not {Tiers}");
                for (var t = 0; t < s.Free.Count; t++)
                    Check($"season {s.Id} free tier {t + 1}", s.Free[t]);
                for (var t = 0; t < s.Gilded.Count; t++)
                    Check($"season {s.Id} Gilded tier {t + 1}", s.Gilded[t]);
            }
            foreach (var dup in Seasons.GroupBy(s => s.Id).Where(g => g.Count() > 1))
                problems.Add($"season id {dup.Key} is used twice");
            var dated = Seasons.Where(s => s.From != null).OrderBy(s => s.StartsAt).ToList();
            for (var i = 1; i < dated.Count; i++)
                if (dated[i].StartsAt < dated[i - 1].EndsAt)
                    problems.Add($"seasons {dated[i - 1].Id} and {dated[i].Id} overlap");
            foreach (var id in Rotation.Where(id => Def(id) == null))
                problems.Add($"rotation: no season {id}");
            for (var i = 0; i < Cache.Count; i++)
                Check($"cache line {i + 1}", Cache[i].Rewards);
            foreach (var dup in Auras.GroupBy(a => a.Id).Where(g => g.Count() > 1))
                problems.Add($"aura id {dup.Key} is used twice");
            foreach (var dup in Colors.GroupBy(a => a.Id).Where(g => g.Count() > 1))
                problems.Add($"name colour id {dup.Key} is used twice");
            foreach (var dup in PetEffects.GroupBy(a => a.Id).Where(g => g.Count() > 1))
                problems.Add($"pet effect id {dup.Key} is used twice");
            if (Tiers > 63)
                problems.Add($"{Tiers} tiers: a season's claims hold 63 at most");
            return problems;
        }
    }
}
