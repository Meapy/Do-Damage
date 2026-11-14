using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // The Scrying Glass in the Nexus shows players out adventuring (core/spectate): /glass off
        // keeps you off it, /glass on puts you back, /glass says which it is.
        internal class GlassCommand : Command
        {
            public override string CommandName => "glass";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var acc = player.Client.Account;
                switch (args.Trim().ToLowerInvariant())
                {
                    case "off":
                        acc.HideFromGlass = true;
                        acc.FlushAsync();
                        player.SendInfo("The Scrying Glass in the Nexus won't show you. Type /glass on to be shown again.");
                        return true;
                    case "on":
                        acc.HideFromGlass = false;
                        acc.FlushAsync();
                        player.SendInfo("The Scrying Glass in the Nexus may show you again while you're out adventuring.");
                        return true;
                    case "":
                        player.SendInfo(acc.HideFromGlass
                            ? "The Scrying Glass in the Nexus doesn't show you (/glass on to be shown)."
                            : "The Scrying Glass in the Nexus may show you while you're out adventuring (/glass off to hide).");
                        return true;
                    default:
                        player.SendError("Usage: /glass [on|off]");
                        return false;
                }
            }
        }
    }
}
