using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Pen Keeper's answer (PetPen.cs): the pet (0 for none), its size as it hatched and now
    // (0 Tiny to 3 Large, 255 unknown), the shards he keeps for the account and how many a raise to
    // Large takes, whether the last act worked and what to say about it (empty for a plain ask).
    public class PetPenData : OutgoingMessage
    {
        public ushort Pet { get; set; }
        public byte BaseSize { get; set; }
        public byte Size { get; set; }
        public int Shards { get; set; }
        public byte ShardsNeeded { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; } = "";

        public override MessageId MessageId => MessageId.PET_PEN;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Pet);
            wtr.Write(BaseSize);
            wtr.Write(Size);
            wtr.Write(Shards);
            wtr.Write(ShardsNeeded);
            wtr.Write(Ok);
            wtr.WriteUTF16(Message);
        }
    }
}
