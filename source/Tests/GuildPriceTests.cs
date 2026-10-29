using System;
using Shared;
using Shared.database.guild;
using Xunit;

namespace Tests
{
    public class GuildPriceTests
    {
        private static ServerSettings Until(string end) => new ServerSettings { freeGuildsUntil = end };

        [Fact]
        public void GuildsAreFreeUntilTheEventEndsThenCostTheNormalPrice()
        {
            var until = GuildPrice.FreeUntil(Until("2026-12-01T00:00:00Z"));
            Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), until);
            Assert.Equal(0, GuildPrice.At(until, new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(0, GuildPrice.At(until, new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc)));
            Assert.Equal(GuildPrice.Normal, GuildPrice.At(until, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Fact]
        public void NoEventWithoutADate()
        {
            Assert.Null(GuildPrice.FreeUntil(Until("")));
            Assert.Null(GuildPrice.FreeUntil(Until("soon")));
            Assert.Null(GuildPrice.FreeUntil(new ServerSettings()));
            Assert.Equal(1000, GuildPrice.At(null, DateTime.UtcNow));
        }

        [Fact]
        public void TheMessageNamesTheLastFreeDay()
        {
            Assert.Contains("until the end of 30 November (UTC)", GuildPrice.Message(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.Contains("until 1 December, 18:00 UTC", GuildPrice.Message(new DateTime(2026, 12, 1, 18, 0, 0, DateTimeKind.Utc)));
        }
    }
}
