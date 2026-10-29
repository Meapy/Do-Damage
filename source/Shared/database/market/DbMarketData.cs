using Newtonsoft.Json;
using Shared.resources;

namespace Shared.database.market
{
    // A Marketplace listing, as the `market` hash holds it (MarketStore): priced in fame.
    public class DbMarketData
    {
        [JsonProperty("currency")] public CurrencyType Currency;
        [JsonProperty("marketId")] public int Id;
        [JsonProperty("itemType")] public ushort ItemType;
        [JsonProperty("price")] public int Price;
        [JsonProperty("sellerId")] public int SellerId;
        [JsonProperty("sellerName")] public string SellerName;
        [JsonProperty("startTime")] public int StartTime;
        // when it ends (Unix time), despite the name
        [JsonProperty("timeLeft")] public int TimeLeft;
        // the item's ItemData JSON (imprints, a stack's count), or null
        [JsonProperty("itemData")] public string ItemData;
    }
}
