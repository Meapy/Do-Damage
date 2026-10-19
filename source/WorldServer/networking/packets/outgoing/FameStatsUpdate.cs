using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The character's fame records as they stand (FameStats: id and count each, as the death
    // screen counts them), for clients that asked (FAME_STATS_SUBSCRIBE).
    public class FameStatsUpdate : OutgoingMessage
    {
        public (byte Id, int Value)[] Stats { get; set; }

        public override MessageId MessageId => MessageId.FAME_STATS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((byte)Stats.Length);
            foreach (var (id, value) in Stats)
            {
                wtr.Write(id);
                wtr.Write(value);
            }
        }
    }
}
