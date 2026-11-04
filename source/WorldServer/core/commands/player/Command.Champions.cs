using System;
using System.Globalization;
using System.Linq;
using Shared;
using Shared.champions;
using Shared.quests;
using WorldServer.core.champions;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // The Hall of Champions in chat, for any client: each board's top 3 and your own places.
        // The Champions Monument in the Nexus shows it all in the Rust client.
        internal class ChampionsCommand : Command
        {
            public override string CommandName => "champions";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var board = ChampionCache.Board;
                if (board == null || board.Boards.Count == 0)
                {
                    player.SendInfo("The Hall of Champions hasn't been ranked yet. Try again in a few minutes.");
                    return true;
                }
                player.SendInfo("The Hall of Champions (the monument south of the Nexus spawn):");
                for (var i = 0; i < ChampionBoards.All.Length && i < board.Boards.Count; i++)
                {
                    var def = ChampionBoards.All[i];
                    var top = board.Boards[i].Take(3).Select(e => $"#{e.Rank} {e.Name} ({e.Value:N0})");
                    var list = string.Join(", ", top);
                    player.SendSubtext($"   {def.Name}: {(list.Length == 0 ? "nobody yet" : list)}");
                }
                var mine = player.Champion.Describe().ToList();
                if (mine.Count == 0)
                    player.SendInfo($"You aren't in any board's top {ChampionBoards.Places} yet. Each board's top {ChampionBoards.Places} get a reward while they stay there.");
                else
                    foreach (var line in mine)
                        player.SendHelp($"You're {line}");
                var reset = DateTimeOffset.FromUnixTimeSeconds(board.WeekReset).UtcDateTime - DbChampions.Now(ChampionCache.ClockHours);
                player.SendSubtext($"   The weekly boards start afresh in {(int)reset.TotalDays}d {reset.Hours}h (Monday, midnight UTC).");
                return true;
            }
        }

        // Testing the Hall of Champions on a test server: rank again now, move the weekly boards'
        // clock (champions:clockHours, for every server), or count fame and dungeons for this
        // week on yourself.
        internal class ChampionsAdminCommand : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "championsadmin";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var db = player.GameServer.Database.Conn;
                var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (parts.FirstOrDefault()?.ToLowerInvariant())
                {
                    case "refresh":
                        db.StringSet(DbChampions.Refresh, 1);
                        player.SendInfo("The account server ranks everyone again within 15 seconds; this server reads it within a minute (or /championsadmin reload).");
                        return true;
                    case "reload":
                        player.ChampionCounter.Flush(player.GameServer.Database, player.AccountId);
                        ChampionCache.Refresh();
                        player.SendInfo($"Read the boards again: {player.Champion.Encode()} -> {ChampionCache.PlacesOf(player.AccountId).Encode()}");
                        return true;
                    case "week":
                        if (parts.Length < 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                            return false;
                        DbChampions.SetClockHours(db, hours);
                        ChampionCache.Refresh();
                        db.StringSet(DbChampions.Refresh, 1);
                        player.SendInfo($"The weekly boards' clock is now {hours} hours ahead: week {ChampionCache.Week}. Ranking again.");
                        return true;
                    case "add":
                        if (parts.Length < 3 || !long.TryParse(parts[2], out var n))
                            return false;
                        var tab = parts[1].ToLowerInvariant() switch { "fame" => ChampionTab.WeeklyFame, "dungeons" => ChampionTab.WeeklyDungeons, _ => (ChampionTab?)null };
                        if (tab == null)
                            return false;
                        // counted as the game counts it (written within a few seconds, or on leaving)
                        player.ChampionCounter.Count(new QuestEvent(tab == ChampionTab.WeeklyFame ? QuestEvents.Fame : QuestEvents.Dungeon) { Amount = (int)Math.Clamp(n, 0, int.MaxValue) });
                        player.SendInfo($"Added {n} to your {ChampionBoards.Of(tab.Value).Name} this week.");
                        return true;
                    default:
                        player.SendInfo("Usage: /championsadmin refresh | reload | week <hours> | add <fame|dungeons> <n>");
                        return true;
                }
            }
        }
    }
}
