using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl.crucible;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // Gives everyone their Crucible climbs back for today (UTC), online on any world server or
        // not (CrucibleAccess.ResetAll, Shared.crucible.CrucibleReset).
        internal class ResetCrucible : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "resetcrucible";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var n = CrucibleAccess.ResetAll(player.GameServer);
                player.SendInfo(n == 0
                    ? "Nobody has climbed the Crucible today: there were no climbs to give back."
                    : $"Gave {n} account{(n == 1 ? "" : "s")} their Crucible climbs back for today.");
                return true;
            }
        }
    }
}
