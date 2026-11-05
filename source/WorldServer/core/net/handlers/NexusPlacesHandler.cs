using Shared;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    // The Nexus's places for the minimap (NexusPlaces.cs); nothing elsewhere.
    public class NexusPlacesHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.NEXUS_PLACES_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            if (client.Player?.World is NexusWorld nexus)
                client.SendPacket(new NexusPlacesMessage { Places = nexus.Places() });
        }
    }
}
