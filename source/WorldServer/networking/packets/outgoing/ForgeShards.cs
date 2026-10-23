using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The forge storage's shards and how many of each (ForgeStorage.cs): after FORGE_DATA_REQUEST
    // and whenever they change.
    public class ForgeShards : OutgoingMessage
    {
        public (ushort Item, int Count)[] Shards { get; set; }

        public override MessageId MessageId => MessageId.FORGE_SHARDS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((short)Shards.Length);
            foreach (var (item, count) in Shards)
            {
                wtr.Write(item);
                wtr.Write(count);
            }
        }
    }
}
