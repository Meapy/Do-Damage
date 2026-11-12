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
        // /realm2, for everyone: go into the Nexus's Realm 2, the one its portal leads to (never a new
        // one, so there's only ever the one; not those a Realm 2 Key opens). In a Realm 2, admins can
        // list its lands so far, /realm2 next to bring its next event (and land) now (/realm2 next
        // <land> for that land's: thief, star, blight...), or
        // /realm2 <land> (its key or a word of its name: midlands, godlands, cube, penta, garden...;
        // last for the newest) to go there.
        internal class Realm2 : Command
        {
            public override string CommandName => "realm2";

            /// <summary>Sends the player into the Nexus's Realm 2 (for /realm2 and /realm).</summary>
            internal static bool EnterNexusRealm2(Player player)
            {
                var open = player.GameServer.WorldManager.Nexus.PortalMonitor.Realm2();
                if (open == null)
                {
                    player.SendError("The Nexus's Realm 2 is still being built: try again in a moment.");
                    return false;
                }
                if (open.IsPlayersMax())
                {
                    player.SendError($"Realm 2 ({open.GetDisplayName()}) is full: try again in a moment.");
                    return false;
                }
                player.Reconnect(open);
                return true;
            }

            protected override bool Process(Player player, TickTime time, string args)
            {
                args = args.Trim();
                if (player.World is not Realm2World realm)
                    return EnterNexusRealm2(player);

                if (!player.Client.Account.Admin && player.Client.Account.Rank < (int)RankingType.Admin)
                {
                    player.SendInfo($"You're in Realm 2: {realm.GetDisplayName()}.");
                    return true;
                }

                var areas = realm.Layout.Areas;
                if (args == "next" || args.StartsWith("next "))
                {
                    player.SendInfo(realm.RealmManager.ForceLayoutEvent(args.Length > 4 ? args[5..].Trim() : null));
                    return true;
                }
                if (args.Length == 0)
                {
                    player.SendInfo(string.Join(", ", areas.Select(a => $"{a.Kind} ({a.Center.X}, {a.Center.Y})")));
                    return true;
                }

                var word = args.ToLowerInvariant();
                var area = args == "last" ? areas[^1] : areas.FirstOrDefault(a => a.Kind == word)
                    ?? areas.FirstOrDefault(a => a.Kind.StartsWith(word))
                    ?? areas.FirstOrDefault(a => a.Name.Contains(word));
                if (area == null)
                {
                    player.SendError($"No area '{args}': {string.Join(", ", areas.Select(a => a.Kind))}");
                    return false;
                }

                player.ResetNewbiePeriod();
                // beside an event land's arena; the heart of the others
                var off = area.Theme != null ? Realm2EventPainter.ArenaRadius + 6 : 0;
                // onto open ground: a land's scenery or walls there would send the move back
                int tx = area.Center.X, ty = area.Center.Y + off;
                for (var r = 0; r < 12 && !realm.IsPassable(tx, ty); r++)
                    for (var dx = -r; dx <= r; dx++)
                        foreach (var dy in new[] { -r, r })
                            if (realm.IsPassable(area.Center.X + dx, area.Center.Y + off + dy) && !realm.IsPassable(tx, ty))
                                (tx, ty) = (area.Center.X + dx, area.Center.Y + off + dy);
                player.TeleportPosition(time, tx + 0.5f, ty + 0.5f, true);
                player.SendInfo($"{area.Name}");
                return true;
            }
        }
    }
}
