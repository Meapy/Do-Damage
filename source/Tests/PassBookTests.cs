using System;
using System.IO;
using System.Linq;
using Shared.pass;
using Shared.quests;
using Xunit;

namespace Tests
{
    // The Realm Pass's rules (PassBook): seasons and their rotation, XP and Momentum, claims on
    // both tracks, caches, rollover, and cosmetics, with the real data file.
    public class PassBookTests
    {
        private static PassData Data => PassData.Load(Path.Combine(AppContext.BaseDirectory, "resources", "data", "pass.json"));

        private static readonly DateTime Launch = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static PassBook Book(DateTime now, PassState state = null)
        {
            var book = new PassBook(Data, state ?? new PassState());
            book.Refresh(now, new Random(1));
            return book;
        }

        [Fact]
        public void DataFileIsValid()
        {
            var data = Data;
            Assert.Empty(data.Validate(_ => true, _ => true));
            Assert.All(data.Seasons, s => Assert.Equal(data.Tiers, s.Gilded.Count(g => g.Count > 0)));
            // every free tier has something too, since 2.6.6
            Assert.All(data.Seasons, s => Assert.Equal(data.Tiers, s.Free.Count(g => g.Count > 0)));
            // the Gilded track's last tier: the season's pet effect and the Burning Amulet
            Assert.All(data.Seasons, s =>
            {
                Assert.Contains(s.Gilded[^1], r => r.PetFx > 0);
                Assert.Contains(s.Gilded[^1], r => r.Item == "Burning Amulet");
            });
            // every pet effect is given somewhere
            Assert.All(data.PetEffects, fx => Assert.True(
                data.Seasons.Any(s => s.Free.Concat(s.Gilded).SelectMany(t => t).Any(r => r.PetFx == fx.Id))
                || data.Cache.Any(l => l.Rewards.Any(r => r.PetFx == fx.Id)), fx.Name));
            // a season's filled tiers have rewards on the free track
            Assert.All(data.Seasons, s => Assert.All(s.Filled, t => Assert.NotEmpty(s.FreeAt(t - 1))));
            // the Gilded track gives back some of its price, never all of it
            foreach (var s in data.Seasons)
            {
                var gold = s.Gilded.SelectMany(g => g).Sum(r => r.Gold);
                Assert.InRange(gold, 1, data.Price - 1);
            }
        }

        [Fact]
        public void SeasonsRunByDateThenRotateMonthly()
        {
            var data = Data;
            Assert.Null(data.At(new DateTime(2026, 11, 19, 23, 0, 0, DateTimeKind.Utc)));
            Assert.Equal("frostfall", data.At(Launch).Key);
            Assert.Equal("frostfall", data.At(new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Utc)).Key);
            Assert.Equal("emberwake", data.At(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Key);
            Assert.Equal("starfall", data.At(new DateTime(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc)).Key);
            var march = data.At(new DateTime(2027, 3, 10, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal("frostfall-2027-03", march.Key);
            Assert.Equal("Frostfall II", march.Name);
            Assert.Equal(new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), march.StartsAt);
            Assert.Equal(new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc), march.EndsAt);
            Assert.Equal("emberwake-2027-04", data.At(new DateTime(2027, 4, 2, 0, 0, 0, DateTimeKind.Utc)).Key);
            Assert.Equal("Frostfall III", data.At(new DateTime(2027, 6, 2, 0, 0, 0, DateTimeKind.Utc)).Name);
            Assert.Equal("emberwake", data.After(Launch).Key);
        }

        [Fact]
        public void XpFillsFlatTiersThenCaches()
        {
            var data = Data;
            // at the season's start nobody is behind, so no Momentum
            var book = Book(data.At(Launch).StartsAt);
            var g = book.Gain(2 * data.XpPerTier + 100, book.Season.StartsAt);
            Assert.False(g.Momentum);
            Assert.Equal(2, g.TierAfter);
            Assert.Equal(100, book.XpIntoTier);
            book.Gain(data.Tiers * data.XpPerTier, book.Season.StartsAt);
            Assert.Equal(data.Tiers, book.Tier);
            Assert.Equal(1, book.CachesReady);
        }

        [Fact]
        public void MomentumWhileBehindThePace()
        {
            var data = Data;
            var season = data.At(Launch);
            // half way through: the pace is at two thirds of the tiers
            var mid = season.StartsAt + TimeSpan.FromTicks((season.EndsAt - season.StartsAt).Ticks / 2);
            var book = Book(mid);
            Assert.True(book.Momentum(mid));
            var g = book.Gain(100, mid);
            Assert.True(g.Momentum);
            Assert.Equal(100 + 100 * data.Momentum / 100, g.Gained);
            // caught up: no more
            book.Gain(data.XpPerTier * data.Tiers * 2 / 3, mid);
            Assert.False(book.Momentum(mid));
        }

        [Fact]
        public void ClaimsFreeAlwaysAndGildedOnceBought()
        {
            var book = Book(Launch);
            book.Gain(3 * Data.XpPerTier, book.Season.StartsAt);
            Assert.Equal(3, book.ReadyCount);     // tiers 1, 2 and 3 have free rewards
            var c = book.Claim(0);
            Assert.True(c.Ok);
            Assert.Equal(100, c.Rewards.Sum(r => r.Fame));
            Assert.False(book.Claim(0).Ok);
            Assert.True(book.Claim(1).Ok);
            Assert.Equal(PassCode.NotReached, book.Claim(5).Code);
            // Gilded: the tiers reached are ready at once, tier 1's too
            Assert.True(book.CanUnlock(Data.Price).Ok);
            Assert.Equal(PassCode.PriceChanged, book.CanUnlock(Data.Price - 1).Code);
            book.MarkGilded();
            Assert.Equal(PassCode.AlreadyGilded, book.CanUnlock(Data.Price).Code);
            Assert.Equal(3, book.ReadyCount);
            var all = book.Claim(-1);
            Assert.True(all.Ok);
            Assert.Equal(3, all.Count);
            Assert.Contains(all.Rewards, r => r.Item == "Potion of Max Defense");
            Assert.Contains(all.Rewards, r => r.Skin == "Frostcrown Archmage");
            Assert.Equal(0, book.ReadyCount);
            Assert.Equal(PassCode.NothingToClaim, book.Claim(-1).Code);
        }

        [Fact]
        public void CosmeticsOwnedOnceThenFame()
        {
            var book = Book(Launch);
            var first = book.Grant(new[] { new PassReward { Aura = 2 }, new PassReward { Color = 1 } }.ToList());
            Assert.Equal(2, first.Count(r => r.IsCosmetic));
            Assert.Equal(2, book.State.Aura);       // the first aura is worn at once
            Assert.Equal(0, book.State.Color);
            var again = book.Grant(new[] { new PassReward { Aura = 2 } }.ToList());
            Assert.Equal(Data.RepeatFame, again.Single().Fame);
            Assert.True(book.WearColor(1).Ok);
            Assert.False(book.WearAura(4).Ok);
            Assert.True(book.WearAura(0).Ok);
            Assert.Equal(0, book.WornAura);
        }

        [Fact]
        public void PetEffectsOwnedOnceWornAtFirstThenFame()
        {
            var book = Book(Launch);
            Assert.Equal(PassCode.NotOwned, book.WearPetEffect(2).Code);
            var first = book.Grant(new[] { new PassReward { PetFx = 2 }, new PassReward { PetFx = 5 } }.ToList());
            Assert.Equal(2, first.Count(r => r.IsCosmetic));
            Assert.Equal(2, book.WornPetEffect);    // the first is put on at once
            Assert.Equal(Data.RepeatFame, book.Grant(new[] { new PassReward { PetFx = 2 } }.ToList()).Single().Fame);
            Assert.Equal(PassCode.PetFxWorn, book.WearPetEffect(5).Code);
            Assert.Equal(5, book.WornPetEffect);
            Assert.Equal(PassCode.PetFxOff, book.WearPetEffect(0).Code);
            Assert.Equal(0, book.WornPetEffect);
            var copy = PassState.FromJson(book.State.ToJson());
            Assert.Equal(new[] { 2, 5 }, copy.PetEffects);
            Assert.Empty(PassState.FromJson("{\"v\":1}").PetEffects);
        }

        [Fact]
        public void FilledFreeTiersCanBeClaimedOnce()
        {
            var data = Data;
            var def = data.Def("frostfall");
            Assert.Contains(2, def.Filled);
            // before the free track was filled: a Gilded claim of tier 2 marked the empty free tier too
            var state = new PassState { Season = "frostfall", Xp = 3 * data.XpPerTier, Gilded = true, FreeClaimed = 0b11, GildedClaimed = 0b11 };
            var book = Book(Launch, state);
            Assert.False(state.FreeTaken(1));
            Assert.True(state.FreeTaken(0));
            Assert.Equal("frostfall", state.FilledFor);
            var c = book.Claim(1);
            Assert.True(c.Ok);
            Assert.Contains(c.Rewards, r => r.Item == "Pet Biscuit");
            // only once: claimed again it stays claimed
            book.Refresh(Launch.AddDays(1), new Random(1));
            Assert.True(state.FreeTaken(1));
            // a season begun afresh has nothing to clear
            var fresh = Book(Launch);
            Assert.Equal("frostfall", fresh.State.FilledFor);
        }

        [Fact]
        public void GildedTitleTurnsOffAndOnAndOutlastsTheSeason()
        {
            var book = Book(Launch);
            Assert.Equal(PassCode.NotGilded, book.ShowTitle(false).Code);
            Assert.False(book.State.HideTitle);
            book.MarkGilded();
            Assert.Equal(PassCode.TitleHidden, book.ShowTitle(false).Code);
            Assert.True(PassState.FromJson(book.State.ToJson()).HideTitle);
            // the next season keeps the choice for when the pass is Gilded again
            var next = Book(Data.After(Launch).StartsAt, book.State);
            Assert.False(next.State.Gilded);
            Assert.True(next.State.HideTitle);
            next.MarkGilded();
            Assert.Equal(PassCode.TitleShown, next.ShowTitle(true).Code);
            Assert.False(next.State.HideTitle);
        }

        [Fact]
        public void GildedBoostsCommunityLoot()
        {
            var book = Book(Launch);
            Assert.Equal((0, 0), book.CommunityBoost);
            book.MarkGilded();
            Assert.Equal((Data.Community.Luck, Data.Community.Rolls), book.CommunityBoost);
            Assert.True(Data.Community.Luck > 0 && Data.Community.Rolls > 0);
        }

        [Fact]
        public void TheFirstDungeonOfEachDay()
        {
            var book = Book(Launch);
            Assert.True(book.TakeFirstDungeon(Launch));
            Assert.False(book.TakeFirstDungeon(Launch.AddHours(3)));
            Assert.True(book.TakeFirstDungeon(Launch.AddDays(1)));
        }

        [Fact]
        public void FameCarriesOver()
        {
            var book = Book(Launch);
            Assert.Equal(0, book.FameXp(4));
            Assert.Equal(1, book.FameXp(4));      // 8: one, 3 carried
            Assert.Equal(2, book.FameXp(7));      // 10: two
        }

        [Fact]
        public void AnEndedSeasonPaysWhatWasLeft()
        {
            var book = Book(Launch);
            book.Gain(Data.Tiers * Data.XpPerTier + Data.BonusXp, Launch);
            book.Claim(0);
            book.MarkGilded();
            book.Grant(new[] { new PassReward { Aura = 1 } }.ToList());
            var state = book.State;
            var next = new PassBook(Data, state);
            var left = next.Refresh(new DateTime(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc), new Random(3));
            Assert.Equal("emberwake", state.Season);
            Assert.Equal(0, state.Xp);
            Assert.False(state.Gilded);
            Assert.Contains("frostfall", state.GildedSeasons);
            // tier 1's free reward was claimed; the rest of both tracks, and the cache, come now
            Assert.Single(left, r => r.Fame == 100);   // tier 4's
            Assert.Contains(left, r => r.Skin == "Frostcrown Archmage");
            Assert.Contains(left, r => r.Slot == 1);
            // Snowfall was owned already: fame instead
            Assert.DoesNotContain(left, r => r.Aura == 1);
            Assert.Contains(state.Auras, a => a == 2);
            Assert.True(left.Count >= 30 + 14);
        }

        [Fact]
        public void StateSurvivesJson()
        {
            var book = Book(Launch);
            book.Gain(4321, Launch);
            book.MarkGilded();
            book.Claim(0);
            var copy = PassState.FromJson(book.State.ToJson());
            Assert.Equal(4321, copy.Xp);
            Assert.True(copy.Gilded);
            Assert.True(copy.FreeTaken(0) && copy.GildedTaken(0));
            Assert.Empty(PassState.FromJson("not json").Auras);
        }
    }
}
