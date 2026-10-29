using System.Collections.Generic;
using Shared.database.market;
using Shared.resources;
using WorldServer.core.net.stats;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects.vendors
{
    // A stall of the Nexus's market square showing one player's Marketplace listing, as a merchant
    // shows its item: walking up to it and buying is a Marketplace purchase (Marketplace.Buy), so
    // every client can buy from the stalls, an old one too. MarketStalls picks what each shows.
    public sealed class MarketStall : SellableObject
    {
        // the merchant's object, as the shops' stalls use
        public const ushort StallType = 0x01ca;

        private readonly StatTypeValue<int> _type;
        private readonly StatTypeValue<int> _count;
        private readonly StatTypeValue<int> _minsLeft;
        private readonly StatTypeValue<int> _discount;

        public int ListingId { get; private set; }

        public MarketStall(GameServer gameServer) : base(gameServer, StallType)
        {
            _type = new StatTypeValue<int>(this, StatDataType.MerchandiseType, -1);
            _count = new StatTypeValue<int>(this, StatDataType.MerchandiseCount, 1);
            _minsLeft = new StatTypeValue<int>(this, StatDataType.MerchandiseMinsLeft, int.MaxValue);
            _discount = new StatTypeValue<int>(this, StatDataType.MerchandiseDiscount, -1);
            Currency = CurrencyType.Fame;
        }

        // Shows this listing (its item, price and the minutes it has left).
        public void Show(DbMarketData listing, int now)
        {
            ListingId = listing.Id;
            _type.SetValue(listing.ItemType);
            Price = listing.Price;
            _minsLeft.SetValue(System.Math.Max(1, (listing.TimeLeft - now) / 60));
        }

        public override void Buy(Player player)
        {
            var (ok, message) = GameServer.Marketplace.Buy(player, ListingId, Price);
            player.Client.SendPacket(new BuyResultMessage { Result = ok ? 0 : 1, ResultString = message });
        }

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            stats[StatDataType.MerchandiseType] = _type.GetValue();
            stats[StatDataType.MerchandiseCount] = _count.GetValue();
            stats[StatDataType.MerchandiseMinsLeft] = _minsLeft.GetValue();
            stats[StatDataType.MerchandiseDiscount] = _discount.GetValue();
            base.ExportStats(stats, isOtherPlayer);
        }
    }
}
