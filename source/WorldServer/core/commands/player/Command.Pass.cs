using System;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // The Realm Pass in the chat (the pass window, Y or the Wayfarer, claims it); /pass title
        // [on|off] shows or hides the Gilded pass's [Gilded] chat title.
        internal class PassCommand : Command
        {
            public override RankingType RankRequirement => RankingType.Regular;
            public override string CommandName => "pass";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var pass = player.Pass;
                if (pass == null)
                    return false;
                var book = pass.Book;
                if (!book.Active)
                {
                    player.SendInfo("<Realm Pass> No season is running.");
                    return true;
                }
                var words = (args ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0 && words[0] == "title")
                {
                    // "on" or "off", or the other way from now
                    var show = words.Length > 1 ? words[1] is "on" or "show" or "1" : book.State.HideTitle;
                    player.SendInfo($"<Realm Pass> {pass.SetTitle(show).Message}");
                    return true;
                }
                var left = book.Season.EndsAt - QuestTracker.Now;
                player.SendInfo($"<Realm Pass> {book.Season.Name}{(book.State.Gilded ? " (Gilded)" : "")}: tier {book.Tier} of {book.Data.Tiers}, {book.XpIntoTier:N0} / {book.XpForTier:N0} XP to the next. Ends in {(int)left.TotalDays}d {left.Hours}h.");
                if (book.Momentum(QuestTracker.Now))
                    player.SendInfo($"<Realm Pass> Momentum: you're behind the season's pace, so pass XP comes {book.Data.Momentum}% faster.");
                if (pass.ReadyCount > 0)
                    player.SendInfo($"<Realm Pass> {pass.ReadyCount} reward{(pass.ReadyCount == 1 ? " waits" : "s wait")}: press Y, or see the Wayfarer in the Nexus.");
                return true;
            }
        }
    }
}
