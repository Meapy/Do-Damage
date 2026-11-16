using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shared.resources;
using WorldServer.core.objects.vendors;
using Xunit;

namespace Tests
{
    public class MerchantShopTests
    {
        [Fact]
        public void NexusSpecialsDoNotSellVaultOrCharacterSlotUnlockers()
        {
            var type = typeof(WorldServer.core.GameServer).Assembly.GetType("WorldServer.core.objects.vendors.MerchantLists");
            var field = type.GetField("Shops", BindingFlags.Public | BindingFlags.Static);
            var shops = (Dictionary<TileRegion, System.Tuple<List<ISellableItem>, CurrencyType, int>>)field.GetValue(null);
            var items = shops[TileRegion.Store_3].Item1.OfType<ShopItem>().Select(item => item.Name);

            Assert.DoesNotContain("Vault Chest Unlocker", items);
            Assert.DoesNotContain("Char Slot Unlocker", items);
        }
    }
}
