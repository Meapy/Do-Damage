using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The client can show the Gilded pass's dungeon guide (1), or no longer wants it (0): a
    // Gilded player in a dungeon is then sent the way to its boss (Player.Guide.cs).
    public class GuideSubscribeHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.GUIDE_SUBSCRIBE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var on = rdr.ReadBoolean();
            client.Player?.SetGuide(on);
        }
    }
}
