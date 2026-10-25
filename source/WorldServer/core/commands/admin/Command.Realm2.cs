using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.realm2;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // /realm2: open a new Realm 2 and go in (as its key does). In a Realm 2: list its areas in
        // order, or /realm2 <area> (its key or a word of its name: cube, penta, garden...) to go to
        // the edge of that area's arena.
        internal class Realm2 : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "realm2";

            protected override bool Process(Player player, TickTime time, string args)
            {
                args = args.Trim();
                if (player.World is not Realm2World realm)
                {
                    player.SendInfo("Opening a Realm 2...");
                    player.GameServer.WorldManager.CreateNewWorldAsync("Realm 2", player.World, world =>
                    {
                        if (world == null)
                            player.SendError("Couldn't build a Realm 2.");
                        else if (player.Client != null && !player.Dead)
                            player.Reconnect(world);
                    });
                    return true;
                }

                var areas = realm.Layout.Areas;
                if (args.Length == 0)
                {
                    player.SendInfo(string.Join(", ", areas.Select(a => $"{a.Kind} ({a.Center.X}, {a.Center.Y})")));
                    return true;
                }

                var area = areas.FirstOrDefault(a => a.Kind.StartsWith(args.ToLowerInvariant()))
                    ?? areas.FirstOrDefault(a => a.Name.Contains(args.ToLowerInvariant()));
                if (area == null)
                {
                    player.SendError($"No area '{args}': {string.Join(", ", areas.Select(a => a.Kind))}");
                    return false;
                }

                player.ResetNewbiePeriod();
                player.TeleportPosition(time, area.Center.X + 0.5f, area.Center.Y + Realm2Generator.ArenaRadius + 6.5f, true);
                player.SendInfo($"{area.Name}");
                return true;
            }
        }
    }
}
