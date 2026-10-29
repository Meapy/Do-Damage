using System.Collections.Generic;
using System.Linq;
using Shared.champions;
using Shared.resources;
using Xunit;

namespace Tests
{
    public class ChampionsTests
    {
        private static ChampionRanked C(int id, long value, long tiebreak = 0) =>
            new ChampionRanked { AccountId = id, Name = $"P{id}", Value = value, Tiebreak = tiebreak };

        [Fact]
        public void TiersAreFirstTheNextTwoAndTheRestOfTheTopTen()
        {
            Assert.Equal(-1, ChampionBoards.TierOf(0));
            Assert.Equal(0, ChampionBoards.TierOf(1));
            Assert.Equal(1, ChampionBoards.TierOf(2));
            Assert.Equal(1, ChampionBoards.TierOf(3));
            Assert.Equal(2, ChampionBoards.TierOf(4));
            Assert.Equal(2, ChampionBoards.TierOf(10));
            Assert.Equal(-1, ChampionBoards.TierOf(11));
        }

        [Fact]
        public void RanksByValueThenTiebreakThenOlderAccountAndSkipsNothing()
        {
            var ranked = ChampionBoards.Rank(new[] { C(5, 100), C(3, 100), C(9, 100, 7), C(1, 0), C(2, 250) });
            Assert.Equal(new[] { 2, 9, 3, 5 }, ranked.Select(r => r.AccountId));
            Assert.Equal(new[] { 1, 2, 3, 4 }, ranked.Select(r => r.Rank));
        }

        [Fact]
        public void OnlyTheTopTenHoldRewards()
        {
            var board = ChampionBoards.Rank(Enumerable.Range(1, 15).Select(i => C(i, 100 - i)));
            var boards = ChampionBoards.All.Select((_, i) => i == 0 ? board : new List<ChampionRanked>()).ToList();
            var holders = ChampionBoards.Holders(boards);
            Assert.Equal(10, holders.Count);
            Assert.Equal(1, holders[1].RankOn(ChampionTab.Fame));
            Assert.Equal(10, holders[10].RankOn(ChampionTab.Fame));
            Assert.False(holders.ContainsKey(11));
        }

        [Fact]
        public void PlacesRoundTripThroughRedis()
        {
            var p = ChampionPlaces.None.With(ChampionTab.Fame, 1).With(ChampionTab.WeeklyDungeons, 7);
            Assert.Equal("0:1,5:7", p.Encode());
            Assert.Equal(p, ChampionPlaces.Decode(p.Encode()));
            Assert.False(ChampionPlaces.Decode("").Any);
            // nonsense is ignored rather than thrown
            Assert.Equal(0, ChampionPlaces.Decode("x:1,99:2,3:-1").RankOn(ChampionTab.Arena));
        }

        [Fact]
        public void EachBoardGivesItsOwnReward()
        {
            Assert.Equal(ChampionBoards.All.Length, ChampionBoards.All.Select(b => b.Reward).Distinct().Count());
            var p = ChampionPlaces.None.With(ChampionTab.WeeklyFame, 2).With(ChampionTab.Arena, 1);
            Assert.Equal(0.06, p.Experience, 10);
            Assert.Equal(0.15, p.ArenaFame, 10);
            Assert.Equal(0, p.Loot(true));
            Assert.Equal("+6% experience and fame", ChampionBoards.Of(ChampionTab.WeeklyFame).RewardText(1));
            Assert.Equal("Bronze glow", ChampionBoards.Of(ChampionTab.Stars).RewardText(2));
        }

        [Fact]
        public void DungeonLootOnlyCountsInDungeonsAndTheTotalIsCapped()
        {
            var p = ChampionPlaces.None.With(ChampionTab.Fame, 1).With(ChampionTab.WeeklyDungeons, 2);
            Assert.Equal(0.03, p.Loot(false), 10);
            // 3% + 2% in a dungeon, capped at 4%
            Assert.Equal(ChampionBoards.LootCap, p.Loot(true), 10);
            // the Legends' 1% stacks on top of the cap
            Assert.Equal(0.05, LootBoosts.Total(false, true, 0, 0, p.Loot(true)), 10);
        }

        [Fact]
        public void ColoursComeFromTheBestPlace()
        {
            Assert.Equal(ChampionBoards.Gold, ChampionPlaces.None.With(ChampionTab.Stars, 1).Glow);
            Assert.Equal(ChampionBoards.Silver, ChampionPlaces.None.With(ChampionTab.Achievements, 3).NameColor);
            Assert.Equal(ChampionBoards.Bronze, ChampionPlaces.None.With(ChampionTab.Stars, 9).Glow);
            Assert.Equal(0, ChampionPlaces.None.With(ChampionTab.Stars, 9).NameColor);
        }

        [Fact]
        public void StarsFollowTheClassFameGoals()
        {
            Assert.Equal(0, ChampionSweep.StarsFor(19));
            Assert.Equal(1, ChampionSweep.StarsFor(20));
            Assert.Equal(3, ChampionSweep.StarsFor(799));
            Assert.Equal(5, ChampionSweep.StarsFor(5000));
        }

        [Fact]
        public void DescribesEveryBoardHeld()
        {
            var lines = ChampionPlaces.None.With(ChampionTab.Fame, 4).With(ChampionTab.Arena, 2).Describe().ToList();
            Assert.Equal(new[] { "#4 on Fame: +1% loot", "#2 on Arena: +10% fame from Arena waves" }, lines);
        }
    }
}
