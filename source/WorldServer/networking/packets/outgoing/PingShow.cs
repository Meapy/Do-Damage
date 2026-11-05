using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // Someone's ping, for a client that can show pings (PING_MARK 255): who sent it (object id
    // and name), its kind (PING_MARK's; 255 answers the client's 255, with nothing else in it),
    // where, the object it marks (0: none) and who the sender is to us (Player.PingRelation).
    public sealed class PingShow : OutgoingMessage
    {
        public int PlayerId { get; set; }
        public string Name { get; set; }
        public byte Kind { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int TargetId { get; set; }
        public byte Relation { get; set; }

        public override MessageId MessageId => MessageId.PING_SHOW;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(PlayerId);
            wtr.WriteUTF16(Name ?? "");
            wtr.Write(Kind);
            wtr.Write(X);
            wtr.Write(Y);
            wtr.Write(Z);
            wtr.Write(TargetId);
            wtr.Write(Relation);
        }
    }
}
