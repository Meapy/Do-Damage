using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The forge's recipes (the result, its fame, whether it needs a Forge Amulet, the ingredients)
    // and what it pays for items, by item type, for the Divine Forge's window (FORGE_DATA_REQUEST).
    public class ForgeData : OutgoingMessage
    {
        public (ushort Result, int Fame, bool Advanced, ushort[] Items)[] Recipes { get; set; }
        public (ushort Item, int Fame)[] Prices { get; set; }

        public override MessageId MessageId => MessageId.FORGE_DATA;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((short)Recipes.Length);
            foreach (var (result, fame, advanced, items) in Recipes)
            {
                wtr.Write(result);
                wtr.Write(fame);
                wtr.Write(advanced);
                wtr.Write((byte)items.Length);
                foreach (var item in items)
                    wtr.Write(item);
            }
            wtr.Write((short)Prices.Length);
            foreach (var (item, fame) in Prices)
            {
                wtr.Write(item);
                wtr.Write(fame);
            }
        }
    }
}
