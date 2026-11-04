using Shared;
using System;
using System.Linq;
using WorldServer.core.community;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // Testing community goals on a test server: list the runs, add to one as if you'd done it,
        // or start one afresh. The goals' clock is the quests' (/questadmin time).
        internal class GoalAdminCommand : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "goaladmin";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var runs = CommunityGoals.Runs;
                CommunityGoals.RunState Pick(int at) =>
                    parts.Length > at && int.TryParse(parts[at], out var i) && i >= 1 && i <= runs.Count ? runs[i - 1] : runs.FirstOrDefault();
                switch (parts.FirstOrDefault()?.ToLowerInvariant())
                {
                    case "list":
                        for (var i = 0; i < runs.Count; i++)
                        {
                            var rs = runs[i];
                            player.SendInfo($"{i + 1}. {rs.Run.Key}{(rs.Run.Running(quests.QuestTracker.Now) ? "" : " (ended)")}: {rs.Total:N0}/{rs.Run.Def.Goal:N0}, {rs.Participants} helping");
                        }
                        if (runs.Count == 0)
                            player.SendInfo("No goals.");
                        return true;
                    case "add":
                    {
                        if (parts.Length < 2 || !long.TryParse(parts[1], out var n))
                            return false;
                        var rs = Pick(2);
                        if (rs == null)
                            return false;
                        CommunityGoals.Add(player, rs, n);
                        player.SendInfo($"Added {n:N0} to {rs.Run.Key} (it goes to Redis within 5 seconds).");
                        return true;
                    }
                    case "reset":
                    {
                        var rs = Pick(1);
                        if (rs == null)
                            return false;
                        CommunityGoals.Reset(rs);
                        player.SendInfo($"{rs.Run.Key} starts again from nothing.");
                        return true;
                    }
                    default:
                        player.SendInfo("Usage: /goaladmin list | add <amount> [goal number] | reset [goal number]");
                        return true;
                }
            }
        }
    }
}
