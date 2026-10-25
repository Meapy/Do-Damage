using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The quest window (the Rust client): it asks for its data, and from then on gets what
    // changes (QuestTracker).
    public class QuestsRequestHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.QUESTS_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time) => client.Player?.Quests?.Subscribe();
    }

    // Claim a quest, a period's bonus, the daily reward or an achievement, swap a quest, or pick a
    // name colour (QuestTracker.CLAIM_QUEST and the rest), from anywhere: the server checks it all.
    public class QuestClaimHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.QUEST_CLAIM;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var kind = rdr.ReadByte();
            var id = rdr.ReadUTF16();
            if (id.Length > 64)
                return;
            client.Player?.Quests?.Claim(kind, id);
        }
    }
}
