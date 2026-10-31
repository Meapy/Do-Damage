using Shared;
using Xunit;

namespace Tests
{
    public class NexusAfkTests
    {
        private static ServerSettings Minutes(double m) => new ServerSettings { nexusAfkMinutes = m };

        [Fact]
        public void StandingStillPastTheLimitSendsYouHomeOnce()
        {
            var limit = NexusAfk.LimitMs(Minutes(20));
            Assert.Equal(20 * 60000, limit);
            var afk = new NexusAfk();
            var ticks = 0;
            while (!afk.Tick(100, limit))
                ticks++;
            Assert.Equal(20 * 60 * 10 - 1, ticks);
            Assert.False(afk.Tick(100, limit));
        }

        [Fact]
        public void MovingStartsTheTimerAgain()
        {
            var limit = NexusAfk.LimitMs(Minutes(1));
            var afk = new NexusAfk();
            for (var i = 0; i < 50; i++)
            {
                Assert.False(afk.Tick(1000, limit));
                if (i % 30 == 29)
                    afk.Moved();
            }
            Assert.Equal(20000, afk.StillMs);
        }

        [Fact]
        public void ZeroTurnsItOff()
        {
            Assert.Equal(0, NexusAfk.LimitMs(Minutes(0)));
            Assert.Equal(0, NexusAfk.LimitMs(null));
            var afk = new NexusAfk();
            Assert.False(afk.Tick(int.MaxValue, 0));
        }

        [Fact]
        public void TheMessageNamesTheLimit()
        {
            Assert.Equal("You were away for 20 minutes and have been returned to the character screen.", NexusAfk.Message(Minutes(20)));
            Assert.Contains("for a minute", NexusAfk.Message(Minutes(1)));
            Assert.Contains("for 0.5 minutes", NexusAfk.Message(Minutes(0.5)));
        }
    }
}
