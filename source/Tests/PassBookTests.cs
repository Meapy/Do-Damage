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
            var g = book.Gain(2500, book.Season.StartsAt);
            Assert.False(g.Momentum);
            Assert.Equal(2, g.TierAfter);
            Assert.Equal(500, book.XpIntoTier);
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
            Assert.Equal(2, book.ReadyCount);     // tiers 1 and 3 have free rewards
            var c = book.Claim(0);
            Assert.True(c.Ok);
            Assert.Equal(100, c.Rewards.Sum(r => r.Fame));
            Assert.False(book.Claim(0).Ok);
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
            Assert.DoesNotContain(left, r => r.Fame == 100);
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
