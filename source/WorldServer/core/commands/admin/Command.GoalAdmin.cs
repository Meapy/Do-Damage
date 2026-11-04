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
        // start one afresh, or set a goal's scale (its next runs' size). The goals' clock is the
        // quests' (/questadmin time).
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
                            player.SendInfo($"{i + 1}. {rs.Run.Key}{(rs.Run.Running(quests.QuestTracker.Now) ? "" : " (ended)")}: {rs.Total:N0}/{rs.Goal:N0} ({rs.Run.Def.Goal:N0} as written), {rs.Participants} helping");
                        }
                        if (runs.Count == 0)
                            player.SendInfo("No goals.");
                        var scales = CommunityGoals.Scales();
                        if (scales.Count > 0)
                            player.SendInfo("Scales: " + string.Join(", ", scales.OrderBy(s => s.Key).Select(s => $"{s.Key} {s.Value:0.###}")));
                        return true;
                    case "scale":
                    {
                        if (parts.Length < 3 || CommunityGoals.Data.Goal(parts[1]) == null
                            || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) || x <= 0)
                            return false;
                        CommunityGoals.SetScale(parts[1], x);
                        player.SendInfo($"{parts[1]}'s next runs are {x:0.###} times its goal ({Shared.community.CommunityData.GoalOf(CommunityGoals.Data.Goal(parts[1]), x):N0}). A run already going keeps its goal; /goaladmin reset starts one again at it.");
                        return true;
                    }
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
                        player.SendInfo("Usage: /goaladmin list | add <amount> [goal number] | reset [goal number] | scale <goal id> <times>");
                        return true;
                }
            }
        }
    }
}
