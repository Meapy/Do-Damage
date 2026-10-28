using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The client wants its realms' events (REALM_EVENTS) from now on in this world.
    public class RealmEventsSubscribeHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.REALM_EVENTS_SUBSCRIBE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            if (client.Player == null)
                return;
            client.Player.WantsRealmEvents = true;
            client.Player.RealmEventsSent = null;
        }
    }

    // The event the player's quest arrow should follow (an object id from REALM_EVENTS), or 0 for
    // whichever is nearest, as it was.
    public class QuestPickHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.QUEST_PICK;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var objectId = rdr.ReadInt32();
            var player = client.Player;
            if (player == null || player.World == null)
                return;
            player.PickQuest(objectId);
        }
    }
}
