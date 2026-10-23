using System.Collections.Concurrent;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // Staff only: the Rust client aims your shots at the nearest enemy while this is on. The
        // client turns it on only when the server says so, in a Text from "*AutoAim*" (a name no
        // player can have), so players can't: not with the command, nor with the client's --aim
        // auto test flag, which sends it. It stays on for the account until /autoaim again or a
        // world server restart, and is sent again whenever the character enters a world.
        internal class AutoAim : Command
        {
            public const string Sender = "*AutoAim*";
            private static readonly ConcurrentDictionary<int, bool> On = new ConcurrentDictionary<int, bool>();

            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "autoaim";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var on = !On.ContainsKey(player.AccountId);
                if (on)
                    On[player.AccountId] = true;
                else
                    On.TryRemove(player.AccountId, out _);
                Send(player, on);
                player.SendInfo(on
                    ? "Auto-aim on: your shots go at the nearest enemy. /autoaim again to turn it off."
                    : "Auto-aim off.");
                return true;
            }

            // Player.Init: an admin's auto-aim, again, for a client that has just signed in
            public static void Resend(Player player)
            {
                var account = player.Client.Account;
                if (On.ContainsKey(player.AccountId) && (account.Admin || account.Rank >= (int)RankingType.Admin))
                    Send(player, true);
            }

            private static void Send(Player player, bool on) => player.Client.SendPacket(new Text()
            {
                BubbleTime = 0,
                NumStars = -1,
                Name = Sender,
                Txt = on ? "on" : "off"
            });
        }
    }
}
