using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing.market;

namespace WorldServer.core.net.handlers.market
{
    // The first protocol's purchase (Rust clients before 2.3.3): Marketplace.Buy.
    public class MarketBuyHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_BUY;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var id = rdr.ReadInt32();
            var player = client.Player;
            if (player == null)
                return;
            var (ok, message) = client.GameServer.Marketplace.Buy(player, id, 0);
            client.SendPacket(new MarketBuyResult
            {
                Code = ok ? MarketBuyResult.BOUGHT : MarketBuyResult.ERROR,
                Description = message,
                OfferId = id
            });
        }
    }
}
