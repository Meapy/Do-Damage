using System.Linq;
using Shared;
using WorldServer.core.market;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing.market;

namespace WorldServer.core.net.handlers.market
{
    // The Marketplace window (Rust client 2.3.3, market.rs): see Marketplace.

    // The listings when they changed since the version the client has, its box and its history.
    public class MarketBrowseHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_BROWSE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var known = rdr.ReadInt32();
            var player = client.Player;
            if (player == null || !client.GameServer.Marketplace.MayView(player))
                return;
            var market = client.GameServer.Marketplace;
            var (version, listings) = market.Listings();
            var id = client.Account.AccountId;
            client.SendPacket(new MarketListings
            {
                Version = (int)version,
                Now = System.DateTime.UtcNow.ToUnixTimestamp(),
                Unchanged = known == (int)version && version >= 0,
                Tax = Marketplace.TaxPercent,
                Limit = Marketplace.MaxListings,
                Listings = listings.Take(Marketplace.MaxSent).ToArray(),
                Box = market.Store.Box(id).Select(b => b.Item).ToArray(),
                History = market.Store.History(id).ToArray()
            });
        }
    }

    // Lists an item: from an inventory slot, or a vault chest's slot.
    public class MarketSellHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_SELL;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var fromVault = rdr.ReadBoolean();
            var chest = rdr.ReadInt32();
            var slot = rdr.ReadByte();
            var price = rdr.ReadInt32();
            var hours = rdr.ReadInt32();
            var player = client.Player;
            if (player == null)
                return;
            var market = client.GameServer.Marketplace;
            var (ok, message) = fromVault ? market.SellFromVault(player, chest, slot, price, hours) : market.Sell(player, slot, price, hours);
            MarketActHandler.Answer(client, ok, message);
        }
    }

    // 1 buys a listing (at the price the client saw), 2 takes one back, 3 collects a box item
    // (id 0: all that fit).
    public class MarketActHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.MARKET_ACT;

        public const byte Buy = 1, Cancel = 2, Collect = 3;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var action = rdr.ReadByte();
            var id = rdr.ReadInt32();
            var price = rdr.ReadInt32();
            var player = client.Player;
            if (player == null)
                return;
            var market = client.GameServer.Marketplace;
            var (ok, message) = action switch
            {
                Buy => market.Buy(player, id, price),
                Cancel => market.Cancel(player, id),
                Collect => market.Collect(player, id),
                _ => (false, "Unknown action.")
            };
            Answer(client, ok, message);
        }

        public static void Answer(Client client, bool ok, string message) => client.SendPacket(new MarketResult
        {
            Ok = ok,
            Message = message,
            Fame = client.Player?.CurrentFame ?? 0
        });
    }
}
