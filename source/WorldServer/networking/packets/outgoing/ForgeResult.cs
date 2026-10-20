using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The answer to FORGE or FORGE_SELL: whether it worked, the item forged (0 for a sale), the
    // fame paid or gained, and what went wrong.
    public class ForgeResult : OutgoingMessage
    {
        public bool Sold { get; set; }
        public bool Success { get; set; }
        public ushort Item { get; set; }
        public int Fame { get; set; }
        public string Message { get; set; }

        public override MessageId MessageId => MessageId.FORGE_RESULT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Sold);
            wtr.Write(Success);
            wtr.Write(Item);
            wtr.Write(Fame);
            wtr.WriteUTF16(Message ?? "");
        }
    }
}
