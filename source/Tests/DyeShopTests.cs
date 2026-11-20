using System;
using System.IO;
using System.Linq;
using Shared.resources;
using WorldServer.core.worlds.impl.doomed;
using Xunit;

namespace Tests
{
    // /shop dyes (MerchantLists.DyeShopPrices): every dye and cloth, the solid colours for fame and
    // the patterned cloths for gold, and /shop dyes <words> narrows it down.
    public class DyeShopTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        [Fact]
        public void SellsEveryDyeAndCloth()
        {
            var data = Res.Value.GameData;
            var sold = DoomedShop.Entries(data, "dyes");
            Assert.Equal(sold.Count, sold.Select(e => e.Item).Distinct().Count());
            var dyes = data.Items.Values.Where(i => i.Class == "Dye" && !i.ObjectId.Contains("Remover")).Select(i => i.ObjectId).ToList();
            Assert.Empty(dyes.Except(sold.Select(e => e.Item)));
            Assert.DoesNotContain(sold, e => e.Item.Contains("Remover"));
            Assert.Contains("dyes", DoomedShop.ShopNames);
        }

        [Fact]
        public void SolidColoursCostFameAndPatternsGold()
        {
            var sold = DoomedShop.Entries(Res.Value.GameData, "dyes").ToDictionary(e => e.Item);
            foreach (var solid in new[] { "Alice Blue Clothing Dye", "Black Accessory Dye", "Yellow Green Clothing Dye" })
                Assert.Equal(CurrencyType.Fame, sold[solid].Currency);
            foreach (var pattern in new[] { "Large Rainbow Cloth", "Small Blue Striped Cloth", "Large Oryx Cloth" })
                Assert.Equal(CurrencyType.Gold, sold[pattern].Currency);
            Assert.All(sold.Values, e => Assert.True(e.Price > 0));
        }

        [Fact]
        public void WordsNarrowTheShop()
        {
            var data = Res.Value.GameData;
            var blue = DoomedShop.Entries(data, "dyes", "dark blue");
            Assert.Contains(blue, e => e.Item == "Dark Blue Clothing Dye");
            // each word anywhere in the name: "dark blue" finds Dark Slate Blue too
            Assert.Contains(blue, e => e.Item == "Dark Slate Blue Accessory Dye");
            Assert.All(blue, e => Assert.True(e.Item.Contains("Dark") && e.Item.Contains("Blue"), e.Item));
            Assert.Empty(DoomedShop.Entries(data, "dyes", "no such colour"));
        }
    }
}
