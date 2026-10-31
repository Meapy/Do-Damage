using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The forge's recipes (the result, its fame, whether it needs a Forge Amulet, the ingredients)
    // and what it pays for items, by item type, for the Divine Forge's window (FORGE_DATA_REQUEST).
    public class ForgeData : OutgoingMessage
    {
        public (ushort Result, int Fame, bool Advanced, ushort[] Items)[] Recipes { get; set; }
        public (ushort Item, int Fame)[] Prices { get; set; }
        // recipes that also take stat potions from the potion storage (the Infernal potions): the
        // result, the potion and how many (appended: an older client stops reading before them)
        public (ushort Result, ushort Potion, short Count)[] StorageCosts { get; set; } = System.Array.Empty<(ushort, ushort, short)>();

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
            wtr.Write((short)StorageCosts.Length);
            foreach (var (result, potion, count) in StorageCosts)
            {
                wtr.Write(result);
                wtr.Write(potion);
                wtr.Write(count);
            }
        }
    }
}
