using System.Linq;
using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    // The Collection window (the Rust client, the Collector in the vault): the gear there is to
    // collect, what the account has had, and where the untiered gear comes from (Player.Collection.cs).
    public class CollectionHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.COLLECTION_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var player = client.Player;
            if (player == null)
                return;
            var catalogue = objects.Player.Catalogue(player.GameServer);
            player.Collect(player.Inventory.ToArray());
            client.SendPacket(new CollectionContents
            {
                All = catalogue.All,
                Have = catalogue.All.Where(player.Collection.Contains).ToArray(),
                Groups = catalogue.Groups
            });
        }
    }
}
