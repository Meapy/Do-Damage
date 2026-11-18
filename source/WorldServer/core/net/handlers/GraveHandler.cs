using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // Lingering at the grave (Player.Grave.cs): 255, once in each world, says the client can stay
    // as a ghost when it dies. Other values are kept for later and ignored.
    public class GraveHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.GRAVE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var action = rdr.ReadByte();
            var player = client.Player;
            if (player?.World == null)
                return;
            if (action == 255)
                player.AllowLinger();
        }
    }
}
