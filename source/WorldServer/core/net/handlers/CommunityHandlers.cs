using System;
using Shared;
using WorldServer.core.community;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.net.handlers
{
    // The community goals window (the Rust client): it asks once in each world, and every few
    // seconds while it's open; from the first ask the player gets the server's popups too.
    public class CommunityRequestHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.COMMUNITY_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var player = client.Player;
            if (player == null)
                return;
            player.CommunitySubscribed = true;
            // each answer reads Redis: no more than one a second
            var now = DateTime.UtcNow;
            if ((now - player.CommunityAsked).TotalMilliseconds < 1000)
                return;
            player.CommunityAsked = now;
            CommunityGoals.SendData(player);
        }
    }

    // Claim a goal's tier (255 its top's prize, 254 all it has waiting); no key claims everything
    // waiting on every goal. The server checks it all.
    public class CommunityClaimHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.COMMUNITY_CLAIM;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var key = rdr.ReadUTF16();
            var tier = rdr.ReadByte();
            var player = client.Player;
            if (player == null || key.Length > 64)
                return;
            var now = DateTime.UtcNow;
            if ((now - player.CommunityClaimed).TotalMilliseconds < 300)
                return;
            player.CommunityClaimed = now;
            CommunityGoals.Claim(player, key, tier);
        }
    }
}
