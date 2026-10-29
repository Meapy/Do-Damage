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
        public void EveryMemberOfAGroupHoldsItsPlace()
        {
            ChampionRanked Group(long wave, params int[] ids) => new ChampionRanked
            {
                AccountId = ids.Min(), Name = string.Join(", ", ids.Select(i => $"P{i}")), Value = wave,
                Members = ids.Select(i => C(i, wave)).ToList(),
            };
            var board = ChampionBoards.Rank(new[] { Group(20, 3, 4), Group(31, 1, 2), Group(25, 2, 5) });
            Assert.Equal(new[] { 31L, 25L, 20L }, board.Select(r => r.Value));
            Assert.True(board[0].Has("p2") && !board[0].Has("P3"));
            var boards = ChampionBoards.All.Select((_, i) => i == (int)ChampionTab.ArenaGroup ? board : new List<ChampionRanked>()).ToList();
            var holders = ChampionBoards.Holders(boards);
            Assert.Equal(5, holders.Count);
            Assert.Equal(1, holders[1].RankOn(ChampionTab.ArenaGroup));
            Assert.Equal(1, holders[2].RankOn(ChampionTab.ArenaGroup)); // in two groups: the better place
            Assert.Equal(2, holders[5].RankOn(ChampionTab.ArenaGroup));
            Assert.Equal(3, holders[4].RankOn(ChampionTab.ArenaGroup));
        }

        [Fact]
        public void PlacesRoundTripThroughRedis()
        {
            var p = ChampionPlaces.None.With(ChampionTab.Fame, 1).With(ChampionTab.WeeklyDungeons, 7);
            Assert.Equal("0:1,6:7", p.Encode());
            Assert.Equal(p, ChampionPlaces.Decode(p.Encode()));
            Assert.False(ChampionPlaces.Decode("").Any);
            // nonsense is ignored rather than thrown
            Assert.Equal(0, ChampionPlaces.Decode("x:1,99:2,3:-1").RankOn(ChampionTab.ArenaSolo));
        }

        [Fact]
        public void EachBoardGivesItsOwnReward()
        {
            // every reward comes from one board, but the Arena's (both Arena boards) and the loot boost
            // (Fame and the two guild boards)
            Assert.Equal(ChampionBoards.All.Length - 3, ChampionBoards.All.Select(b => b.Reward).Distinct().Count());
            var p = ChampionPlaces.None.With(ChampionTab.WeeklyFame, 2).With(ChampionTab.ArenaSolo, 1).With(ChampionTab.ArenaGroup, 4);
            Assert.Equal(0.06, p.Experience, 10);
            Assert.Equal(0.20, p.ArenaFame, 10);
            Assert.Equal(0, p.Loot(true));
            Assert.Equal("+6% experience and fame", ChampionBoards.Of(ChampionTab.WeeklyFame).RewardText(1));
            Assert.Equal("Bronze glow", ChampionBoards.Of(ChampionTab.Stars).RewardText(2));
        }

        [Fact]
        public void EveryMemberOfAGuildHoldsItsPlace()
        {
            var roster = new List<ChampionRanked>
            {
                new ChampionRanked { AccountId = 4, Name = "Ann" },
                new ChampionRanked { AccountId = 9, Name = "Bo" },
            };
            var guilds = ChampionBoards.Rank(new[]
            {
                new ChampionRanked { AccountId = 4, GuildId = 1, Name = "Lol", Value = 500, Roster = roster },
                new ChampionRanked { AccountId = 2, GuildId = 2, Name = "Other", Value = 900, Roster = new List<ChampionRanked> { new ChampionRanked { AccountId = 2, Name = "Cy" } } },
            });
            var boards = ChampionBoards.All.Select(_ => new List<ChampionRanked>()).ToList();
            boards[(int)ChampionTab.GuildLiving] = guilds;
            var holders = ChampionBoards.Holders(boards);
            Assert.Equal(2, holders[4].RankOn(ChampionTab.GuildLiving));
            Assert.Equal(2, holders[9].RankOn(ChampionTab.GuildLiving));
            Assert.Equal(1, holders[2].RankOn(ChampionTab.GuildLiving));
            Assert.Equal(0, holders[2].RankOn(ChampionTab.GuildDead));
            Assert.Equal(0.01, holders[9].Loot(false), 10);
            Assert.True(guilds[1].Has("bo"));
            Assert.False(guilds[1].Has("Lol"));
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
            var lines = ChampionPlaces.None.With(ChampionTab.Fame, 4).With(ChampionTab.ArenaGroup, 2).Describe().ToList();
            Assert.Equal(new[] { "#4 on Fame: +1% loot", "#2 on Arena Group: +10% fame from Arena waves" }, lines);
        }
    }
}
