using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The Scrying Glass (core/spectate/SpectatorHub.cs): watching, its big view and its arrows.
    public class SpectateHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.SPECTATE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var action = rdr.ReadByte();
            client.GameServer.Spectators.Act(client, action);
        }
    }

    // A line in the big view's chat.
    public class SpectateSayHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.SPECTATE_SAY;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var text = rdr.ReadUTF16();
            if (text.Length <= 512)
                client.GameServer.Spectators.Chat(client, text, ref time);
        }
    }
}
