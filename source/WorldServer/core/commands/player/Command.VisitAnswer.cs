using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // /yes and /no: answer the /visit request waiting for you, as its box's buttons do
        // (Player.Visits.cs); on a client too old to show the box, the only way.
        internal class VisitYes : Command
        {
            public override string CommandName => "yes";

            protected override bool Process(Player player, TickTime time, string args)
            {
                player.AnswerVisitByChat(true);
                return true;
            }
        }

        internal class VisitNo : Command
        {
            public override string CommandName => "no";

            protected override bool Process(Player player, TickTime time, string args)
            {
                player.AnswerVisitByChat(false);
                return true;
            }
        }
    }
}
