using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class Wiki : Command
        {
            public override string CommandName => "wiki";

            protected override bool Process(Player player, TickTime time, string args)
            {
                player.SendInfo("The Do Damage wiki: https://www.dodmg.com/wiki");
                return true;
            }
        }
    }
}
