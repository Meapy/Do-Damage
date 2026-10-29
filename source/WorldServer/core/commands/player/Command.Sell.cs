using System;
using System.Collections.Concurrent;
using Shared;
using WorldServer.core.market;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public partial class Command
    {
        // /sell <slot> <price> [days]: lists an inventory item on the Marketplace, as its window
        // does. Slots 1-8 are the inventory, 9-16 the backpack's.
        internal class Sell : Command
        {
            public override string CommandName => "sell";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[0], out var slot) || !int.TryParse(parts[1], out var price)
                    || slot < 1 || slot > 16)
                {
                    player.SendInfo("/sell <inventory slot 1-16> <price in fame> [days: 1, 3 or 7]: lists an item on the Marketplace.");
                    return false;
                }
                var days = 3;
                if (parts.Length > 2 && (!int.TryParse(parts[2], out days) || days < 1 || days > 7))
                {
                    player.SendError("A listing lasts 1 to 7 days.");
                    return false;
                }
                var (ok, message) = player.GameServer.Marketplace.Sell(player, slot + 3, price, days * 24);
                if (ok)
                    player.SendInfo(message);
                else
                    player.SendError(message);
                return ok;
            }
        }

        // /market takes the player to the Marketplace's Broker, as /forge does to the Divine Forge:
        // beside it at once in the Nexus, or from anywhere else to the Nexus, arriving beside it.
        internal class MarketCommand : Command
        {
            public override string CommandName => "market";

            // accounts on their way to the Marketplace, and until when the trip counts
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
                    player.SendInfo("The Marketplace is just south of the Nexus's spawn.");
                    return true;
                }
                player.TeleportPosition(time, spot.Value, true);
                return true;
            }

            // where a player arriving in a world is placed if they typed /market on the way, or null
            public static Position? ArrivalSpot(int accountId, World target)
            {
                if (PendingArrivals.IsEmpty || !PendingArrivals.TryRemove(accountId, out var until) || until < DateTime.UtcNow)
                    return null;
                return target == target.GameServer.WorldManager.Nexus ? Spot(target) : null;
            }

            // A free tile in front of (south of) the Market Broker, or the nearest free one.
            private static Position? Spot(World world)
            {
                var map = world.Map;
                for (var y = 0; y < map.Height; y++)
                    for (var x = 0; x < map.Width; x++)
                        if (map[x, y].ObjDesc?.Class == Marketplace.BrokerClass)
                        {
                            for (var r = 2; r <= 4; r++)
                                foreach (var (dx, dy) in new[] { (0, r), (-1, r), (1, r), (-r, 0), (r, 0) })
                                    if (map.Contains(x + dx, y + dy) && map[x + dx, y + dy].ObjType == 0 && world.IsPassable(x + dx + 0.5, y + dy + 0.5, true))
                                        return new Position(x + dx + 0.5f, y + dy + 0.5f);
                            return null;
                        }
                return null;
            }
        }
    }
}
