using System;
using Shared;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // How high the client walks, in a world of stacked floors, sent before each Move there: the
    // Move is checked against the floor at that height (World.IsPassableAt).
    public class HeightHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.HEIGHT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var z = rdr.ReadSingle();
            var player = client.Player;
            var world = player?.World;
            if (world?.Levels == null || !float.IsFinite(z))
                return;
            player.Z = Math.Clamp(z, 0, world.Levels.Count * world.LevelHeight);
        }
    }
}
