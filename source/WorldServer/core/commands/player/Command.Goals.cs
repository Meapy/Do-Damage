using Shared;
using System;
using System.Linq;
using WorldServer.core.community;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // The week's community goals in the chat (the Warcaller in the Nexus shows them, and
        // claims their rewards).
        internal class GoalsCommand : Command
        {
            public override RankingType RankRequirement => RankingType.Regular;
            public override string CommandName => "goals";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var data = CommunityGoals.Data;
                var running = CommunityGoals.Running;
                if (running.Count == 0)
                {
                    player.SendInfo("No community goals are running right now.");
                    return true;
                }
                var end = running[0].Run.End - QuestTracker.Now;
                player.SendInfo($"Community goals (new ones in {(int)end.TotalDays}d {end.Hours}h):");
                foreach (var rs in running)
                {
                    var def = rs.Run.Def;
                    var reached = data.Reached(def, rs.Total);
                    var tier = reached == 0 ? "no tier yet" : $"{data.TierName(reached - 1)} reached";
                    var mine = CommunityGoals.ShareOf(player, rs);
                    player.SendInfo($"  {def.Title}: {def.Describe()} [{rs.Total:N0}/{def.Goal:N0}, {tier}, {rs.Participants:N0} helping, you {mine:N0}]");
                }
                player.SendInfo("Everyone who helps shares each tier's reward: claim them from the Warcaller by the Nexus spawn.");
                return true;
            }
        }
    }
}
