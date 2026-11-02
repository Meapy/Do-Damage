using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The client can show visit requests (255, once in each world), or answers one: 1 lets the
    // visitor in, 0 doesn't (Player.Visits.cs).
    public class VisitAnswerHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.VISIT_ANSWER;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var id = rdr.ReadInt32();
            var answer = rdr.ReadByte();
            var player = client.Player;
            if (player?.World == null)
                return;
            if (answer == 255)
                player.WantsVisitRequests = true;
            else
                player.AnswerVisit(id, answer == 1);
        }
    }
}
