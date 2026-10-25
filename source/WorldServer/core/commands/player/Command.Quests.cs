using System;
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
        // Quests, the daily reward and achievements in the chat (the quest window, J, claims them).
        internal class QuestsCommand : Command
        {
            public override RankingType RankRequirement => RankingType.Regular;
            public override string CommandName => "quests";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (player.Quests == null)
                    return false;
                var book = player.Quests.Book;
                var now = QuestTracker.Now;
                book.Refresh(now);
                foreach (QuestPeriod p in Enum.GetValues(typeof(QuestPeriod)))
                {
                    var reset = QuestPeriods.NextReset(p, now) - now;
                    player.SendInfo($"{char.ToUpper(QuestData.Name(p)[0])}{QuestData.Name(p).Substring(1)} quests (new in {(int)reset.TotalHours}h {reset.Minutes}m):");
                    foreach (var (_, slot, def) in book.Current().Where(q => q.Period == p))
                    {
                        var state = slot.Claimed ? "claimed" : slot.Progress >= def.Goal ? "done, claim it!" : $"{slot.Progress:N0}/{def.Goal:N0}";
                        player.SendInfo($"  {def.Title}: {def.Describe()} [{state}]");
                    }
                }
                player.SendInfo(book.LoginReady(now)
                    ? $"Daily reward: day {book.State.Login.Step % Math.Max(1, book.Data.Login.Count) + 1} is ready to collect!"
                    : "Daily reward: collected today.");
                var points = book.Points;
                var tiers = book.Data.Achievements.Count(a => book.State.Achievements.TryGetValue(a.Id, out var st) && QuestBook.Earned(a, st.Progress) > st.Claimed);
                var rank = book.Data.Ranks.Count == 0 ? "" : book.Data.Ranks[book.RankOf(points)].Name;
                player.SendInfo($"Achievements: {rank}, {points} points{(tiers > 0 ? $", {tiers} to claim" : "")}.");
                player.SendInfo("Press J (or talk to the Tinkerer or the Chronicler by the spawn) to claim rewards.");
                return true;
            }
        }
    }
}
