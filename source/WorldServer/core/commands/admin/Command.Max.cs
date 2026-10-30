using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class Max : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "max";

            protected override bool Process(Player player, TickTime time, string args)
            {
                // to the cap, with any Infernal potions' share past the class's maximum
                for (var i = 0; i < 8; i++)
                    player.Stats.Base[i] = player.StatCap(i);

                player.Level = 20;
                player.Experience = Player.GetLevelExp(20);

                player.SendInfo("Your stats have been maxed.");
                return true;
            }
        }
    }
}
