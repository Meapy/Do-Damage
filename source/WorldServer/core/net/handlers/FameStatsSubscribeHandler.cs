using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The client wants its character's fame records (FameStats messages) from now on.
    public class FameStatsSubscribeHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.FAME_STATS_SUBSCRIBE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            if (client.Player != null)
                client.Player.WantsFameStats = true;
        }
    }
}
