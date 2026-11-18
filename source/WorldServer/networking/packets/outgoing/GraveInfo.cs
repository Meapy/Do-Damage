using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // Sent with a DeathMessage when the player stays at their grave as a ghost (Player.Grave.cs):
    // how long the ghost may stay, ms.
    public class GraveInfo : OutgoingMessage
    {
        private readonly int Ms;

        public override MessageId MessageId => MessageId.GRAVE_INFO;

        public GraveInfo(int ms) => Ms = ms;

        public override void Write(NetworkWriter wtr) => wtr.Write(Ms);
    }
}
