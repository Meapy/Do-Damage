using System.Linq;
using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing.market;

namespace WorldServer.core.net.handlers.market
{
    // The Marketplace's first protocol, which Rust clients before 2.3.3 still use: lists the items
    // in these inventory slots, each for the price (in fame, whatever currency it names).
    public class MarketAddHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_ADD;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var slots = new byte[rdr.ReadByte()];
            for (var i = 0; i < slots.Length; i++)
                slots[i] = rdr.ReadByte();
            var price = rdr.ReadInt32();
            rdr.ReadInt32();    // the currency: always fame
            var hours = rdr.ReadInt32();

            var player = client.Player;
            if (player == null)
                return;
            var market = client.GameServer.Marketplace;
            var refusal = market.Refusal(player);
            if (refusal == null && slots.Distinct().Count() != slots.Length)
                refusal = "Pick each item once.";
            if (refusal != null)
            {
                client.SendPacket(new MarketAddResult { Code = MarketAddResult.SLOT_IS_NULL, Description = refusal });
                return;
            }

            var listed = 0;
            string message = null;
            foreach (var slot in slots)
            {
                var (ok, why) = market.Sell(player, slot, price, hours, rate: false);
                if (ok)
                    listed++;
                else
                {
                    message = why;
                    break;
                }
            }
            client.SendPacket(new MarketAddResult
            {
                Code = listed > 0 && message == null ? -1 : MarketAddResult.SLOT_IS_NULL,
                Description = message ?? $"Listed {listed} item{(listed == 1 ? "" : "s")} on the Marketplace."
            });
        }
    }
}
