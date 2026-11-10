using System.Collections.Generic;
using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking;

namespace WorldServer.core.commands
{
    internal static class HelpCall
    {
        public static Client[] Available(Player player) => player.GameServer.ConnectionManager.Clients.Keys
            .Where(client => client.Player?.World != null
                && client.Account != null
                && client.Account.AccountId != player.Client.Account.AccountId
                && client.Player.CanBeSeenBy(player)
                && !client.Account.IgnoreList.Contains(player.Client.Account.AccountId)
                && IsAvailable(client.Player))
            .ToArray();

        public static bool IsAvailable(Player player)
        {
            var kind = Player.VisitPlace(player.World, player).Kind;
            return kind is VisitRules.Kind.Nexus or VisitRules.Kind.Realm;
        }

        public static bool Call(Player player, IEnumerable<Client> targets, bool global = false)
        {
            player.AskForHelp(targets, global);
            return true;
        }

        public static bool CanCall(Player player)
        {
            if (player.Muted)
            {
                player.SendError("Muted. You cannot call for help at this time.");
                return false;
            }
            return true;
        }
    }

    public abstract partial class Command
    {
        internal class HelpCommand : Command
        {
            public override string CommandName => "help";

            protected override bool Process(Player player, TickTime time, string args) =>
                HelpCall.CanCall(player) && HelpCall.Call(player, HelpCall.Available(player), global: true);
        }

        internal class PartyHelpCommand : Command
        {
            public override string CommandName => "phelp";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (!HelpCall.CanCall(player))
                    return false;
                player.Client.Account.Reload("partyId");
                var partyId = player.Client.Account.PartyId;
                if (partyId == 0)
                {
                    player.SendError("You need to be in a party to use /phelp.");
                    return false;
                }
                var targets = HelpCall.Available(player).Where(client => client.Account.PartyId == partyId);
                return HelpCall.Call(player, targets);
            }
        }

        internal class GuildHelpCommand : Command
        {
            public override string CommandName => "ghelp";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (!HelpCall.CanCall(player))
                    return false;
                var guildId = player.Client.Account.GuildId;
                if (guildId == 0)
                {
                    player.SendError("You need to be in a guild to use /ghelp.");
                    return false;
                }
                var targets = HelpCall.Available(player).Where(client => client.Account.GuildId == guildId);
                return HelpCall.Call(player, targets);
            }
        }
    }
}
