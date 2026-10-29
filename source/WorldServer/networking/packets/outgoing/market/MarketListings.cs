using Shared;
using Shared.database.character.inventory;
using Shared.database.market;

namespace WorldServer.networking.packets.outgoing.market
{
    // The Marketplace window's contents (MARKET_LISTINGS): the listings' version and the server's
    // clock; whether the listings are as the client already has them (then none follow); the tax
    // and listing limit; then the listings, the account's box and its history. Each item comes
    // with its stack count and imprints (Offensive, Defensive, Misc), read from its data.
    public class MarketListings : OutgoingMessage
    {
        public override MessageId MessageId => MessageId.MARKET_LISTINGS;

        public int Version;
        public int Now;
        public bool Unchanged;
        public byte Tax;
        public byte Limit;
        public DbMarketData[] Listings;
        public MarketBoxItem[] Box;
        public MarketHistoryEntry[] History;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Version);
            wtr.Write(Now);
            wtr.Write(Unchanged);
            wtr.Write(Tax);
            wtr.Write(Limit);
            wtr.Write((short)(Unchanged ? 0 : Listings.Length));
            if (!Unchanged)
                foreach (var l in Listings)
                {
                    wtr.Write(l.Id);
                    wtr.Write(l.ItemType);
                    wtr.Write(l.Price);
                    wtr.WriteUTF16(l.SellerName ?? "");
                    wtr.Write(l.StartTime);
                    wtr.Write(l.TimeLeft);
                    WriteData(wtr, l.ItemData);
                }
            wtr.Write((short)Box.Length);
            foreach (var b in Box)
            {
                wtr.Write(b.BoxId);
                wtr.Write(b.ItemType);
                WriteData(wtr, b.ItemData);
                wtr.Write(Why(b.Why));
                wtr.Write(b.Time);
            }
            wtr.Write((short)History.Length);
            foreach (var h in History)
            {
                wtr.Write(Kind(h.Kind));
                wtr.Write(h.ItemType);
                wtr.Write(h.Price);
                wtr.Write(h.Net);
                wtr.WriteUTF16(h.Other ?? "");
                wtr.Write(h.Time);
            }
        }

        // The stack's count (0 for a single item) and the three imprints.
        private static void WriteData(NetworkWriter wtr, string json)
        {
            ItemData data = null;
            if (!string.IsNullOrEmpty(json))
                try { data = ItemData.CreateData(json); }
                catch (Newtonsoft.Json.JsonException) { }
            wtr.Write((short)(data?.Stack ?? 0));
            for (var i = 0; i < 3; i++)
                wtr.Write((byte)(data?.Imprints != null && i < data.Imprints.Length ? data.Imprints[i] : 0));
        }

        // 1 bought, 2 taken back, 3 ended
        private static byte Why(string why) => why switch { "bought" => 1, "cancelled" => 2, "expired" => 3, _ => 0 };

        // 1 listed, 2 sold, 3 bought, 4 taken back, 5 ended
        private static byte Kind(string kind) => kind switch { "listed" => 1, "sold" => 2, "bought" => 3, "cancelled" => 4, "expired" => 5, _ => 0 };
    }

    // How a Marketplace action went (MARKET_RESULT): what to tell the player, and their fame now.
    public class MarketResult : OutgoingMessage
    {
        public override MessageId MessageId => MessageId.MARKET_RESULT;

        public bool Ok;
        public string Message;
        public int Fame;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Ok);
            wtr.WriteUTF16(Message ?? "");
            wtr.Write(Fame);
        }
    }
}
