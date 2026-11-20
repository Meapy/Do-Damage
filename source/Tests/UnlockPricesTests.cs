using Shared.database;
using Xunit;

namespace Tests
{
    public class UnlockPricesTests
    {
        [Theory]
        [InlineData(1000, 0, 1000)]
        [InlineData(1000, 4, 1000)]
        [InlineData(1000, 5, 2000)]
        [InlineData(1000, 9, 2000)]
        [InlineData(1000, 10, 4000)]
        [InlineData(500, 15, 4000)]
        [InlineData(500, -3, 500)]
        public void FamePriceDoublesEveryFiveBoughtWithFame(int basePrice, int bought, int price) =>
            Assert.Equal(price, UnlockPrices.FamePrice(basePrice, bought));

        [Fact]
        public void FamePriceStopsGrowingInsteadOfOverflowing()
        {
            Assert.Equal(1000 << 20, UnlockPrices.FamePrice(1000, 1000));
            Assert.Equal(int.MaxValue, UnlockPrices.FamePrice(int.MaxValue, 100));
        }

        [Fact]
        public void GoldPricesAreFlat()
        {
            Assert.Equal(500, UnlockPrices.SlotGold);
            Assert.Equal(250, UnlockPrices.VaultGold);
        }
    }
}
