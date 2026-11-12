using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class A : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "spawn15";

            protected override bool Process(Player player, TickTime time, string args)
            {
                // + 1 from automated realm creation system
                for (var i = 0; i < 14; i++)
                    player.World.GameServer.WorldManager.Nexus.PortalMonitor.CreateNewRealm();
                player.SendInfo("Creating 15 realms");
                return true;
            }
        }

        internal class B : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "remove15";

            protected override bool Process(Player player, TickTime time, string args)
            {
                foreach(var world in player.GameServer.WorldManager.GetWorlds())
                {
                    if(world is RealmWorld rw)
                        rw.CloseRealm();
                }
                player.SendInfo("Closing all realms");
                return true;
            }
        }

        // where this realm stands: events defeated and needed, the players it's scaled for
        internal class RealmStatus : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "realmstatus";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (!(player.World is RealmWorld rw))
                {
                    player.SendError("You must be in a realm.");
                    return true;
                }

                player.SendInfo(rw.RealmManager.Status());
                return true;
            }
        }

        // /realmevent <event> [here]: start one of the realm's events now, out of sight nearby (a word
        // of its id or name: thief, colossus, blight, cube...), or 8 tiles away with `here`;
        // with nothing, list them. Not in Realm 2,
        // whose events come with their lands (/realm2 next <land>).
        internal class RealmEvent : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "realmevent";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (!(player.World is RealmWorld rw) || rw.RealmManager.Layout != null)
                {
                    player.SendError("You must be in a realm (not Realm 2: there, /realm2 next <land>).");
                    return true;
                }
                args = args.Trim();
                var here = args.EndsWith(" here");
                player.SendInfo(rw.RealmManager.ForceEvent(here ? args[..^5].Trim() : args, here ? player : null));
                return true;
            }
        }

        internal class CloseRealm : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "closerealm";
            public override string Alias => "cr";

            protected override bool Process(Player player, TickTime time, string args)
            {
                if (!(player.World is RealmWorld gw))
                {
                    player.SendError("You must be in a realm to close it.");
                    return true;
                }

                if (!gw.CloseRealm())
                {
                    player.SendError("Realm has already closed its borders.");
                    return true;
                }

                player.SendInfo("You have closed the realm.");
                return true;
            }
        }
    }
}
