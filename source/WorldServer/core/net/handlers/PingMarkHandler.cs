using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // A ping from the middle mouse button (Player.Pings.cs): its kind (0-4), where (a world
    // position and the height of its floor) and the object it marks (0: none). Kind 255, once in
    // each world, says the client can show pings.
    public class PingMarkHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.PING_MARK;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var kind = rdr.ReadByte();
            var x = rdr.ReadSingle();
            var y = rdr.ReadSingle();
            var z = rdr.ReadSingle();
            var target = rdr.ReadInt32();
            var player = client.Player;
            if (player?.World == null)
                return;
            if (kind == 255)
                player.ShowPings();
            else
                player.Ping(kind, x, y, z, target, time.TotalElapsedMs);
        }
    }
}
