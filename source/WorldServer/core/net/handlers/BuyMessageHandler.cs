using Shared;
using WorldServer.core.objects.vendors;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    public sealed class BuyMessageHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.BUY;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var objectId = rdr.ReadInt32();
            // the currency to pay in (CurrencyType), when the client sends one: a closed vault
            // chest sells for gold or fame
            int currency = -1;
            if (rdr.BaseStream.Length - rdr.BaseStream.Position >= 1)
                currency = rdr.ReadByte();

            var player = client.Player;
            if (player?.World == null)
                return;

            var obj = player.World.GetEntity(objectId) as SellableObject;
            if (currency >= 0)
                obj?.Buy(player, (Shared.resources.CurrencyType)currency);
            else
                obj?.Buy(player);

            if (obj == null)
                player.Client.SendPacket(new BuyResultMessage
                {
                    Result = 1,
                    ResultString = $"Purchase Error: {BuyResult.TransactionFailed.GetDescription()}"
                });
        }
    }
}
