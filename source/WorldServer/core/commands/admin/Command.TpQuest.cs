using System;
using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        internal class TpQuest : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "tq";

            // /tq: to your quest; /tq <object id>: to the nearest object of that id in this world
            // (a dungeon's controller or a boss that isn't your quest yet, on a test stack)
            protected override bool Process(Player player, TickTime time, string args)
            {
                if (!string.IsNullOrWhiteSpace(args))
                {
                    var id = args.Trim();
                    var target = player.World.Enemies.Values.Cast<Entity>()
                        .Concat(player.World.StaticObjects.Values)
                        .Concat(player.World.Portals.Values)
                        .Where(e => string.Equals(e.ObjectDesc.IdName, id, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(e => player.DistTo(e))
                        .FirstOrDefault();
                    if (target == null)
                    {
                        player.SendError($"There is no {id} in this world.");
                        return false;
                    }
                    // something standing where no one can (a chasm's heart over its drop): the
                    // nearest square beside it that can be walked on
                    double tx = target.X, ty = target.Y;
                    if (player.World.Levels == null && !player.World.IsPassable(tx, ty))
                    {
                        var best = double.MaxValue;
                        for (var dx = -10; dx <= 10; dx++)
                            for (var dy = -10; dy <= 10; dy++)
                            {
                                double sx = (int)target.X + dx + 0.5, sy = (int)target.Y + dy + 0.5;
                                var d = dx * dx + dy * dy;
                                if (d < best && player.World.IsPassable(sx, sy))
                                    (best, tx, ty) = (d, sx, sy);
                            }
                    }
                    player.ResetNewbiePeriod();
                    player.TeleportPosition(time, (float)tx, (float)ty, z: target.Z);
                    player.SendInfo($"Teleported to {target.ObjectDesc.IdName}: ({tx}, {ty})");
                    return true;
                }

                if (player.Quest == null)
                {
                    player.SendError("Player does not have a quest!");
                    return false;
                }

                player.ResetNewbiePeriod();

                var x = player.Quest.X;
                var y = player.Quest.Y;

                if (player.Quest.ObjectDesc.IdName.Contains("Hermit"))
                    y += 6;

                player.TeleportPosition(time, x, y);
                player.SendInfo($"Teleported to Quest Location: ({x}, {y})");
                return true;
            }
        }
    }
}
