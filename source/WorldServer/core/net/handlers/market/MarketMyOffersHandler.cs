using System.Linq;
using Shared;
using WorldServer.core.net.datas;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing.market;

namespace WorldServer.core.net.handlers.market
{
    // The first protocol's own listings (Rust clients before 2.3.3).
    public class MarketMyOffersHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_MY_OFFERS;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            if (client.Player == null || !client.GameServer.Marketplace.MayView(client.Player))
                return;
            Send(client);
        }

        public static void Send(Client client) => client.SendPacket(new MarketMyOffersResult
        {
            Results = client.GameServer.Marketplace.ListingsOf(client.Account.AccountId).Select(MarketData.From).ToArray()
        });
    }
}
