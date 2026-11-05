using System;
using System.Collections.Concurrent;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // /warcaller takes the player to the Warcaller on the Arena island's court, who shows the
        // community goals and pays their rewards, as /forge and /market do to theirs: in front of
        // him at once in the Nexus, or from anywhere else to the Nexus, arriving in front of him
        // (LoadHandler asks ArrivalSpot).
        internal class WarcallerCommand : Command
        {
            public override string CommandName => "warcaller";

            // accounts on their way to the Warcaller, and until when the trip counts
            private static readonly ConcurrentDictionary<int, DateTime> PendingArrivals = new ConcurrentDictionary<int, DateTime>();

            protected override bool Process(Player player, TickTime time, string args)
            {
                var nexus = player.GameServer.WorldManager.Nexus;
                if (player.World != nexus)
                {
                    PendingArrivals[player.AccountId] = DateTime.UtcNow.AddMinutes(1);
                    player.Reconnect(nexus);
                    return true;
                }
                var spot = Spot(nexus);
                if (spot == null)
                {
                    player.SendInfo("The Warcaller stands on the Nexus's Arena island, east of the spawn.");
                    return true;
                }
                // no cooldown, as /nexus has none: it's a walk across the Nexus
                player.TeleportPosition(time, spot.Value, true);
                return true;
            }

            // where a player arriving in a world is placed if they typed /warcaller on the way, or null
            public static Position? ArrivalSpot(int accountId, World target)
            {
                if (PendingArrivals.IsEmpty || !PendingArrivals.TryRemove(accountId, out var until) || until < DateTime.UtcNow)
                    return null;
                return target == target.GameServer.WorldManager.Nexus ? Spot(target) : null;
            }

            private static Position? Spot(World nexus) => (nexus as NexusWorld)?.Warcaller?.Spot();
        }
    }
}
