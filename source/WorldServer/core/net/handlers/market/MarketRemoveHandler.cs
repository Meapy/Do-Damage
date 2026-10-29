using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing.market;

namespace WorldServer.core.net.handlers.market
{
    // The first protocol's taking a listing back (Rust clients before 2.3.3): Marketplace.Cancel.
    public class MarketRemoveHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_REMOVE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var id = rdr.ReadInt32();
            var player = client.Player;
            if (player == null)
                return;
            var (ok, message) = client.GameServer.Marketplace.Cancel(player, id);
            client.SendPacket(new MarketRemoveResult
            {
                Code = ok ? -1 : MarketRemoveResult.ITEM_DOESNT_EXIST,
                Description = message
            });
            MarketMyOffersHandler.Send(client);
        }
    }
}
