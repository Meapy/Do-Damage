using WorldServer.core.objects;
using Xunit;

namespace Tests
{
    public class PingTests
    {
        [Fact]
        public void PingsKeepTheirGap()
        {
            var times = new long[Player.PingBurst];
            var count = 0;
            Assert.True(Player.TakePing(times, ref count, 1000));
            Assert.False(Player.TakePing(times, ref count, 1000 + Player.PingGapMs - 1));
            Assert.True(Player.TakePing(times, ref count, 1000 + Player.PingGapMs));
        }

        [Fact]
        public void ABurstWaitsForItsWindow()
        {
            var times = new long[Player.PingBurst];
            var count = 0;
            long t = 0;
            for (var i = 0; i < Player.PingBurst; i++, t += Player.PingGapMs)
                Assert.True(Player.TakePing(times, ref count, t));
            // the burst is spent until the first of it is a window old
            Assert.False(Player.TakePing(times, ref count, t));
            Assert.False(Player.TakePing(times, ref count, Player.PingWindowMs - 1));
            Assert.True(Player.TakePing(times, ref count, Player.PingWindowMs));
            // the next frees up when the second of the burst is a window old
            Assert.False(Player.TakePing(times, ref count, Player.PingWindowMs + Player.PingGapMs - 1));
            Assert.True(Player.TakePing(times, ref count, Player.PingWindowMs + Player.PingGapMs));
        }
    }
}
