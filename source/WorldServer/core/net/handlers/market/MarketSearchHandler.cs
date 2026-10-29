using System.Linq;
using Shared;
using WorldServer.core.net.datas;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing.market;

namespace WorldServer.core.net.handlers.market
{
    // The first protocol's search, for one item type (Rust clients before 2.3.3), from the cached
    // listings.
    public class MarketSearchHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_SEARCH;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var itemType = rdr.ReadInt32();
            if (client.Player == null || !client.GameServer.Marketplace.MayView(client.Player))
                return;
            var me = client.Account.AccountId;
            var offers = client.GameServer.Marketplace.Listings().Listings
                .Where(l => l.ItemType == itemType && l.SellerId != me)
                .Select(MarketData.From).ToArray();
            client.SendPacket(new MarketSearchResult
            {
                Results = offers,
                Description = offers.Length == 0 ? "No one is selling this." : ""
            });
        }
    }
}
