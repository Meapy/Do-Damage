using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // Ends the Marketplace listings whose time is up now, rather than at the next minute.
        public class SweepMarket : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "sweepmarket";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var n = player.GameServer.Marketplace.Sweep();
                player.SendInfo($"Ended {n} listing{(n == 1 ? "" : "s")}.");
                return true;
            }
        }
    }
}
