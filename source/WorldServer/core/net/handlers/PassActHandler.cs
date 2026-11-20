using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The Realm Pass window (the Rust client): asking for its data, claiming, the Gilded track,
    // the aura and name colour worn, the caches (PassTracker.REQUEST and the rest). The server
    // checks it all.
    public class PassActHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.PASS_ACT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var kind = rdr.ReadByte();
            var value = rdr.ReadInt32();
            client.Player?.Pass?.Act(kind, value);
        }
    }
}
