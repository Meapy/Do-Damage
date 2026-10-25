using System;
using System.Globalization;
using System.Linq;
using Shared;
using Shared.quests;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // Testing quests on a test server: move the quests' clock (resets, daily rewards), count
        // events on yourself, finish your quests, or start your own progress afresh.
        internal class QuestAdminCommand : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "questadmin";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var tracker = player.Quests;
                var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tracker == null || parts.Length == 0)
                {
                    player.SendInfo("Usage: /questadmin time <hours> | add <event> [amount] [god boss oryx cube realmEvent elder group d=<difficulty> value=<n> name=<name>] | done | reset");
                    return true;
                }
                switch (parts[0].ToLowerInvariant())
                {
                    case "time":
                        if (parts.Length < 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                            return false;
                        QuestTracker.ClockOffsetHours = hours;
                        player.SendInfo($"The quests' clock on this server is now {hours} hours ahead: {QuestTracker.Now:yyyy-MM-dd HH:mm} UTC.");
                        break;
                    case "add":
                        if (parts.Length < 2 || !QuestEvents.All.Contains(parts[1]))
                        {
                            player.SendError($"Events: {string.Join(", ", QuestEvents.All)}");
                            return false;
                        }
                        var amount = parts.Length > 2 && int.TryParse(parts[2], out var n) ? n : 1;
                        var e = new QuestEvent(parts[1], amount);
                        foreach (var flag in parts.Skip(2))
                            switch (flag)
                            {
                                case "god": e.God = true; break;
                                case "boss": e.Boss = true; break;
                                case "oryx": e.Oryx = true; break;
                                case "cube": e.Cube = true; break;
                                case "realmEvent": e.RealmEvent = true; break;
                                case "elder": e.Elder = true; break;
                                case "group": e.Group = true; break;
                                default:
                                    if (flag.StartsWith("d=") && int.TryParse(flag[2..], out var d)) e.Difficulty = d;
                                    else if (flag.StartsWith("value=") && int.TryParse(flag[6..], out var v)) e.Value = v;
                                    else if (flag.StartsWith("name=")) e.Name = flag[5..].Replace('_', ' ');
                                    break;
                            }
                        tracker.Event(e);
                        player.SendInfo($"Counted {parts[1]} x{amount}.");
                        break;
                    case "done":
                        foreach (var (_, slot, def) in tracker.Book.Current())
                            if (!slot.Claimed)
                                slot.Progress = def.Goal;
                        tracker.Save();
                        if (tracker.Subscribed)
                            tracker.SendData();
                        player.SendInfo("Every quest you have is done.");
                        break;
                    case "reset":
                        tracker.ResetState();
                        player.SendInfo("Your quests, daily rewards and achievements start afresh.");
                        break;
                    default:
                        return false;
                }
                return true;
            }
        }
    }
}
