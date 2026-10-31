using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The forge storage's shards and how many of each (ForgeStorage.cs): after FORGE_DATA_REQUEST
    // and whenever they change. Then the potion storage's stat potions, which the Infernal potions'
    // recipes take (appended: an older client stops reading before them).
    public class ForgeShards : OutgoingMessage
    {
        public (ushort Item, int Count)[] Shards { get; set; }
        public (ushort Item, int Count)[] Potions { get; set; } = System.Array.Empty<(ushort, int)>();

        public override MessageId MessageId => MessageId.FORGE_SHARDS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((short)Shards.Length);
            foreach (var (item, count) in Shards)
            {
                wtr.Write(item);
                wtr.Write(count);
            }
            wtr.Write((short)Potions.Length);
            foreach (var (item, count) in Potions)
            {
                wtr.Write(item);
                wtr.Write(count);
            }
        }
    }
}
