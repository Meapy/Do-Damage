using Shared.resources;
using Xunit;

namespace Tests
{
    public class LootBoostsTests
    {
        [Fact]
        public void NoBoostsAddNothing()
        {
            Assert.Equal(0, LootBoosts.Total(false, false, 0, 0));
        }

        [Fact]
        public void BeingALegendAddsOnePercentagePoint()
        {
            Assert.Equal(0.01, LootBoosts.Total(false, true, 0, 0), 10);
            // added to the others, not multiplying them
            Assert.Equal(0.5 + 0.01 + 0.3 + 0.25, LootBoosts.Total(true, true, 0.3, 0.25), 10);
            Assert.Equal(0.01, LootBoosts.Total(true, true, 0.3, 0.25) - LootBoosts.Total(true, false, 0.3, 0.25), 10);
        }

        [Fact]
        public void TheLootDropPotionAddsHalf()
        {
            Assert.Equal(0.5, LootBoosts.Total(true, false, 0, 0), 10);
        }
    }
}
