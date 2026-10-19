using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The client wants everyone who hit its quest, with their classes (QuestDamageFull
    // messages instead of QuestDamage), from now on.
    public class QuestDamageFullSubscribeHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.QUEST_DAMAGE_FULL_SUBSCRIBE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            if (client.Player == null)
                return;
            client.Player.WantsQuestDamage = true;
            client.Player.WantsFullQuestDamage = true;
        }
    }
}
