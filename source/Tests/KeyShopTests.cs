using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Shared.resources;
using WorldServer.core.objects.vendors;
using Xunit;

namespace Tests
{
    // /shop keys (MerchantLists.KeyShopPrices): every key it sells opens a dungeon that has a world,
    // and it sells the regular and Elder keys but none of the Oryx chain's.
    public class KeyShopTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private static List<(string Key, int Price)> Keys(string list = "KeyShopPrices")
        {
            var type = typeof(WorldServer.core.GameServer).Assembly.GetType("WorldServer.core.objects.vendors.MerchantLists");
            var prop = type.GetProperty(list, BindingFlags.Public | BindingFlags.Static);
            return ((IEnumerable<(string, int)>)prop.GetValue(null)).ToList();
        }

        private static List<string> ElderKeyItems() =>
            Res.Value.GameData.Items.Values.Select(i => i.ObjectId)
                .Where(id => id.StartsWith("Elder ") && id.EndsWith(" Key")).Distinct().ToList();

        [Theory]
        [InlineData("KeyShopPrices")]
        [InlineData("ElderKeyShopPrices")]
        public void EveryKeyOpensADungeonWithAWorld(string list)
        {
            var data = Res.Value.GameData;
            var portals = data.Portals.Values.GroupBy(p => p.IdName).ToDictionary(g => g.Key, g => g.First());
            var problems = new List<string>();
            foreach (var (key, price) in Keys(list))
            {
                if (price <= 0 || price > ushort.MaxValue)
                    problems.Add($"{key}: price {price}");
                if (!data.IdToObjectType.TryGetValue(key, out var t) || !data.Items.TryGetValue(t, out var item))
                {
                    problems.Add($"{key}: no such item");
                    continue;
                }
                // a key creates its portal; an incantation turns a locked portal into the open one
                var portal = item.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.CreatePortal)?.Id
                    ?? item.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.ChangeObject)?.NewId;
                if (portal == null || !portals.TryGetValue(portal, out var desc))
                    problems.Add($"{key}: opens no portal ({portal})");
                else if (data.GetWorld(desc.DungeonName) == null)
                    problems.Add($"{key}: {portal} leads to no world ({desc.DungeonName})");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void SellsTheRegularKeysButNotTheOryxChainOrAnyElderKey()
        {
            var keys = Keys().Select(k => k.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            foreach (var regular in new[] { "Pirate Cave Key", "Lost Halls Key", "Shatters Key", "Doom Crystal Key" })
                Assert.Contains(regular, keys);
            foreach (var oryx in new[] { "Oryx's Castle Key", "Oryx Chamber Key", "Oryx Chicken Chamber Key",
                         "Wine Cellar Incantation", "Christmas Key" })
                Assert.DoesNotContain(oryx, keys);
            // Elder keys are gold only
            Assert.Empty(keys.Where(k => k.StartsWith("Elder ")));
        }

        [Fact]
        public void EveryElderKeyIsSoldForTenToAHundredGoldOnly()
        {
            var elder = Keys("ElderKeyShopPrices");
            Assert.Equal(elder.Count, elder.Select(k => k.Key).Distinct().Count());
            // every Elder dungeon that has a key
            Assert.Empty(ElderKeyItems().Except(elder.Select(k => k.Key)));
            Assert.All(elder, k => Assert.InRange(k.Price, 10, 100));
            Assert.Equal(10, elder.Min(k => k.Price));
            Assert.Equal(100, elder.Max(k => k.Price));

            // and nowhere for fame: not the Bazaar's fame pavilions, not /shop's fame shops
            var asm = typeof(WorldServer.core.GameServer).Assembly;
            var shops = (Dictionary<TileRegion, Tuple<List<ISellableItem>, CurrencyType, int>>)asm
                .GetType("WorldServer.core.objects.vendors.MerchantLists")
                .GetField("Shops", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            foreach (var (region, shop) in shops)
            {
                var names = shop.Item1.OfType<ShopItem>().Select(i => i.Name).ToList();
                if (shop.Item2 == CurrencyType.Fame)
                    Assert.Empty(names.Where(n => n.StartsWith("Elder ") && n.EndsWith(" Key")));
            }
            Assert.Equal(CurrencyType.Gold, shops[TileRegion.Store_12].Item2);
            Assert.Equal(elder.Select(k => k.Key).OrderBy(n => n),
                shops[TileRegion.Store_12].Item1.OfType<ShopItem>().Select(i => i.Name).OrderBy(n => n));
            var shopItems = ((string Shop, string Item, int Price, CurrencyType Currency)[])asm
                .GetType("WorldServer.core.commands.doomed.DoomedEconomyData")
                .GetField("ShopItems", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Assert.Empty(shopItems.Where(i => i.Currency == CurrencyType.Fame && i.Item.StartsWith("Elder ") && i.Item.EndsWith(" Key")));
        }
    }
}
