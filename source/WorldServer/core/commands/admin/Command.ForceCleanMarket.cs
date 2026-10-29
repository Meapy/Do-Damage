using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // Ends every Marketplace listing: each item goes to its seller's Marketplace box.
        public class ForceCleanMarket : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "forcecleanmarket";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var n = player.GameServer.Marketplace.Sweep(force: true);
                player.SendInfo($"Ended {n} listing{(n == 1 ? "" : "s")}: their items are in their sellers' Marketplace boxes.");
                return true;
            }
        }
    }
}
