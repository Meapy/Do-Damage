using System;
using System.IO;
using System.Linq;
using Shared;
using Shared.resources;
using WorldServer.logic.loot;
using Xunit;

namespace Tests
{
    public class ServerEventsTests
    {
        private static DateTime Utc(int month, int day, int hour = 0, int minute = 0, int second = 0) =>
            new DateTime(2026, month, day, hour, minute, second, DateTimeKind.Utc);

        private static ServerEvent[] SweetTreats() => new[]
        {
            new ServerEvent { name = "Halloween Sweet Treats", start = Utc(10, 30), end = Utc(11, 7), potionChance = 2, potionAmount = 2 }
        };

        [Fact]
        public void RunsFromItsStartUntilJustBeforeItsEnd()
        {
            var events = SweetTreats();
            Assert.Empty(ServerEvents.Active(events, Utc(10, 29, 23, 59, 59)));
            Assert.Single(ServerEvents.Active(events, Utc(10, 30)));
            Assert.Single(ServerEvents.Active(events, Utc(11, 6, 23, 59, 59)));
            Assert.Empty(ServerEvents.Active(events, Utc(11, 7)));
            Assert.Empty(ServerEvents.Active(events, Utc(12, 25)));
        }

        [Fact]
        public void DoublesPotionChanceAndAmountOnlyWhileItRuns()
        {
            var events = SweetTreats();
            Assert.Equal(2, ServerEvents.PotionChance(events, Utc(11, 1)));
            Assert.Equal(2, ServerEvents.PotionAmount(events, Utc(11, 1)));
            Assert.Equal(1, ServerEvents.PotionChance(events, Utc(11, 7)));
            Assert.Equal(1, ServerEvents.PotionAmount(events, Utc(11, 7)));
            Assert.Equal(1, ServerEvents.PotionChance(null, Utc(11, 1)));
            Assert.Equal(1, ServerEvents.PotionAmount(new ServerEvent[0], Utc(11, 1)));
        }

        [Fact]
        public void TheChanceIsCappedAndStacksWithTheBoosts()
        {
            var chance = ServerEvents.PotionChance(SweetTreats(), Utc(11, 1));
            // a 16% potion, with no boosts: 32%
            Assert.Equal(0.32, Loot.Chance(0.16 * chance, 1, 1), 10);
            // with a 30% weekend and the Legends' 1%: the event's 32% times 1.31
            Assert.Equal(0.32 * (1 + LootBoosts.Total(false, true, 0.3, 0)), Loot.Chance(0.16 * chance, 1 + LootBoosts.Total(false, true, 0.3, 0), 1), 10);
            // never past a sure drop, then halved in an arena
            Assert.Equal(1, Loot.Chance(0.8 * chance, 1.5, 1), 10);
            Assert.Equal(0.5, Loot.Chance(0.8 * chance, 1.5, 0.5), 10);
        }

        [Fact]
        public void CoversTheStatPotionsOnly()
        {
            foreach (var stat in ServerEvents.Stats)
            {
                Assert.True(ServerEvents.IsStatPotion($"Potion of {stat}"));
                Assert.True(ServerEvents.IsStatPotion($"Potion of {stat} (SB)"));
                Assert.True(ServerEvents.IsStatPotion($"Greater Potion of {stat}"));
                Assert.False(ServerEvents.IsStatPotion($"Potion of Max {stat}"));
                Assert.False(ServerEvents.IsStatPotion($"Candy of Extreme {stat}"));
            }
            Assert.False(ServerEvents.IsStatPotion("Potion of Health1"));
            Assert.False(ServerEvents.IsStatPotion(null));
        }

        [Fact]
        public void TellsPlayersWhatItDoesAndUntilWhen()
        {
            Assert.Equal("Halloween Sweet Treats: potions drop twice as often, and twice as many, until the end of 6 November (UTC)!",
                SweetTreats()[0].Message());
        }

        [Fact]
        public void OverlappingEventsMultiply()
        {
            var events = SweetTreats().Append(new ServerEvent { name = "x", start = Utc(11, 1), end = Utc(11, 2), potionChance = 1.5 }).ToArray();
            Assert.Equal(3, ServerEvents.PotionChance(events, Utc(11, 1, 12)), 10);
            Assert.Equal(2, ServerEvents.PotionAmount(events, Utc(11, 1, 12)));
        }

        [Fact]
        public void TheWorldServersConfigHasTheEventInUtc()
        {
            var config = ServerConfig.ReadJson(File.ReadAllText(Path.Combine(SourceDir(), "WorldServer", "wServer.json")));
            var e = config.serverSettings.events.Single(x => x.name == "Halloween Sweet Treats");
            Assert.Equal(Utc(10, 30), e.start);
            Assert.Equal(Utc(11, 7), e.end);
            Assert.Equal(DateTimeKind.Utc, e.start.Kind);
            Assert.Equal(DateTimeKind.Utc, e.end.Kind);
            Assert.Equal(2, e.potionChance);
            Assert.Equal(2, e.potionAmount);
            Assert.True(e.ActiveAt(Utc(11, 3)));
            Assert.False(e.ActiveAt(Utc(11, 7)));
        }

        private static string SourceDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "betterSkillys.sln")))
                dir = dir.Parent;
            return dir.FullName;
        }
    }
}
