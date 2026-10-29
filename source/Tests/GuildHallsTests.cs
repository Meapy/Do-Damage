using System;
using Shared.champions;
using Shared.database.guild;
using Shared.resources;
using Xunit;

namespace Tests
{
    public class GuildHallsTests
    {
        [Fact]
        public void EachHallHasMoreVaultAndTheUpgradesEndAtTheFourth()
        {
            Assert.Equal(1, GuildHalls.HallOf(0));
            Assert.Equal(4, GuildHalls.HallOf(3));
            Assert.Equal(4, GuildHalls.HallOf(9));
            Assert.Equal("Guild Hall 1", GuildHalls.WorldName(1));
            for (var level = 2; level <= GuildHalls.Levels; level++)
                Assert.True(GuildHalls.VaultSlots(level) > GuildHalls.VaultSlots(level - 1));
            Assert.Equal(16, GuildHalls.VaultSlots(1));
            Assert.Equal(10_000, GuildHalls.UpgradePrice(1));
            Assert.Equal(0, GuildHalls.UpgradePrice(GuildHalls.Levels));
        }

        [Fact]
        public void InitiatesOnlyPutAndLeadersUpgrade()
        {
            Assert.False(GuildHalls.MayTake((int)GuildRank.Initiate));
            Assert.True(GuildHalls.MayTake((int)GuildRank.Member));
            Assert.False(GuildHalls.MayUpgrade((int)GuildRank.Officer));
            Assert.True(GuildHalls.MayUpgrade((int)GuildRank.Leader));
            Assert.Equal("Founder", GuildHalls.RankName(40));
        }

        [Fact]
        public void GuildDeadStartsAfreshEachYear()
        {
            Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), DbChampions.YearReset(new DateTime(2026, 10, 29, 13, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), DbChampions.YearReset(new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Utc)));
            Assert.True(ChampionBoards.Of(ChampionTab.GuildDead).Yearly);
            Assert.False(ChampionBoards.Of(ChampionTab.GuildLiving).Yearly);
            Assert.Equal("champions:guildDead:2026", DbChampions.GuildDeadKey(2026));
        }

        [Fact]
        public void BeingInAGuildAddsOnePercentLoot()
        {
            Assert.Equal(0.01, LootBoosts.Total(false, false, 0, 0, 0, GuildHalls.BuffsOf(1).Loot), 10);
            Assert.Equal(0.02, LootBoosts.Total(false, true, 0, 0, 0, GuildHalls.BuffsOf(1).Loot), 10);
            Assert.Equal(0, GuildHalls.BuffsOf(0).Loot);
        }

        [Fact]
        public void EachHallGivesMoreBuffs()
        {
            for (var level = 2; level <= GuildHalls.Levels; level++)
            {
                HallBuffs was = GuildHalls.BuffsOf(level - 1), now = GuildHalls.BuffsOf(level);
                Assert.True(now.Loot > was.Loot);
                Assert.True(now.Experience > was.Experience);
                Assert.True(now.Health >= was.Health && now.Vitality >= was.Vitality);
            }
            Assert.False(GuildHalls.BuffsOf(1).HasStats);
            Assert.Equal("+1.5% loot, +5% experience, +20 health and mana", GuildHalls.BuffsOf(2).ToString());
            Assert.Equal("+1% loot", GuildHalls.BuffsOf(1).ToString());
            Assert.Equal("+2% loot, +10% experience, +40 health and mana, +2 vitality and wisdom", GuildHalls.BuffsOf(3).ToString());
        }
    }
}
