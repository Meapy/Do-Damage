namespace Shared.isc.data
{
    // Something happened to this account's Marketplace listings (a sale, an expiry): its fame or
    // its news changed in Redis.
    public struct MarketMsg
    {
        public int AccountId;
    }
}
