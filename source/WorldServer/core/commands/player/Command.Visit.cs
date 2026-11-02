using Shared;
using WorldServer.core.worlds.impl;
using WorldServer.core.objects;
using WorldServer.networking.packets.outgoing;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // /visit <player>, for everyone: ask to join them in the dungeon, Arena or other instance
        // they're in. They answer Yes or No in a corner of their screen (Player.Visits.cs), once
        // per instance, never back into one you've been in; /visit cancel takes the request back.
        // Admins go straight there, anywhere, as before.
        internal class Visit : Command
        {
            public override string CommandName => "visit";

            protected override bool Process(Player player, TickTime time, string name)
            {
                name = name?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    player.SendInfo("Usage: /visit <player name> to ask to join them in their dungeon; /visit cancel to take it back");
                    return true;
                }
                if (name.Equals("cancel", System.StringComparison.OrdinalIgnoreCase))
                {
                    player.WithdrawVisit();
                    return true;
                }

                var target = player.GameServer.ConnectionManager.FindClient(name);
                if (target?.Player?.World == null || !target.Player.CanBeSeenBy(player))
                {
                    player.SendError("Player not found!");
                    return false;
                }
                if (target == player.Client)
                {
                    player.SendError("That's you.");
                    return false;
                }

                var account = player.Client.Account;
                if (!account.Admin && account.Rank < (int)RankingType.Admin)
                    return player.AskToVisit(target);

                var owner = target.Player.World;
                owner.Invite(account.AccountId); // even into an arena game that has started
                player.Client.Reconnect(new Reconnect()
                {
                    Host = "",
                    GameId = owner.Id,
                    Name = owner.DisplayName
                });
                return true;
            }
        }
    }
}
