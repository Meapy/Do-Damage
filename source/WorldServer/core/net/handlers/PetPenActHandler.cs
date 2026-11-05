using Shared;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The Pen Keeper's window (the Rust client): an act and the pet as it is after it (Player.PetPen.cs).
    // Only in the Nexus, where he stands.
    public class PetPenActHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.PET_PEN_ACT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var action = rdr.ReadByte();
            var slot = rdr.ReadByte();
            var player = client.Player;
            if (player?.World == null)
                return;
            if (action != 0 && player.World is not NexusWorld)
            {
                client.SendPacket(player.PetPenState(false, "Only the Pen Keeper in the Nexus does that."));
                return;
            }
            client.SendPacket(player.PetPenAct(action, slot));
        }
    }
}
