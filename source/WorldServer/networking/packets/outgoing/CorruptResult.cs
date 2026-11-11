using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The answer to CORRUPT: whether it worked, the inventory slot, what went wrong, how far each of
    // the four values moved (damage, range, rate of fire, stats) and whether the shots are reversed.
    public class CorruptResult : OutgoingMessage
    {
        public bool Success { get; set; }
        public byte Slot { get; set; }
        public string Message { get; set; }
        public sbyte[] Changes { get; } = new sbyte[4];
        public bool Reversed { get; set; }

        public override MessageId MessageId => MessageId.CORRUPT_RESULT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Success);
            wtr.Write(Slot);
            wtr.WriteUTF16(Message ?? "");
            foreach (var c in Changes)
                wtr.Write(c);
            wtr.Write(Reversed);
        }
    }
}
