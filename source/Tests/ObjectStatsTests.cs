using System.Collections.Generic;
using System.IO;
using Shared;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using Xunit;

namespace Tests
{
    // On 2026-11-20 a Marketplace stall turning to a gold listing sent its currency as a
    // CurrencyType, which ObjectStats.Write threw on, ending the Nexus's thread for good.
    public class ObjectStatsTests
    {
        private static KeyValuePair<StatDataType, object>[] RoundTrip(params KeyValuePair<StatDataType, object>[] stats)
        {
            var s = new MemoryStream();
            var wtr = new NetworkWriter(s);
            new ObjectStats(7, 1, 2, stats).Write(wtr);
            wtr.Flush();
            s.Position = 0;
            var back = ObjectStats.Read(new NetworkReader(s));
            Assert.Equal(7, back.Id);
            Assert.Equal(s.Length, s.Position);
            return back.Stats;
        }

        private static KeyValuePair<StatDataType, object> Stat(StatDataType t, object v) => new(t, v);

        [Fact]
        public void AnEnumStatGoesOutAsItsNumber()
        {
            var back = RoundTrip(Stat(StatDataType.MerchandiseCurrency, CurrencyType.Gold), Stat(StatDataType.MerchandisePrice, 50));
            Assert.Equal((int)CurrencyType.Gold, back[0].Value);
            Assert.Equal(50, back[1].Value);
        }

        [Fact]
        public void AnUnsupportedStatIsSentAsZeroNotThrown()
        {
            var back = RoundTrip(Stat(StatDataType.MerchandisePrice, 1.5), Stat(StatDataType.MerchandiseCount, 3));
            Assert.Equal(0, back[0].Value);
            Assert.Equal(3, back[1].Value);
        }
    }
}
