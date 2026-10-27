using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The answer to IMPRINT: whether it worked, the inventory slot of the item, and what went wrong.
    public class ImprintResult : OutgoingMessage
    {
        public bool Success { get; set; }
        public byte Slot { get; set; }
        public string Message { get; set; }

        public override MessageId MessageId => MessageId.IMPRINT_RESULT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Success);
            wtr.Write(Slot);
            wtr.WriteUTF16(Message ?? "");
        }
    }
}
