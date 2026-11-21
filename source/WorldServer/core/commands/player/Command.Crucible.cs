using System;
using System.Linq;
using Shared.crucible;
using Shared.database.leaderboard;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl.crucible;
using WorldServer.core.worlds.impl.doomed;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // /crucible: into the climb the Nexus's Crucible portal leads to (as /arena), once it's open
        // to you; /crucible status: whether it is and the best climbs. Admins, in a Crucible:
        // /crucible floor <n> jumps the climb to that floor (a test, kept off the leaderboard);
        // /crucible equip <item> puts an item in the ability slot (testing the white bags).
        internal class CrucibleCommand : Command
        {
            public override string CommandName => "crucible";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var what = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
                if (what == "floor" && CrucibleAccess.IsAdmin(player.Client.Account))
                {
                    if (player.World is not CrucibleWorld climb || parts.Length < 2 || !int.TryParse(parts[1], out var floor) || !climb.ForceFloor(floor))
                    {
                        player.SendError("In a Crucible: /crucible floor <1-5>.");
                        return false;
                    }
                    player.SendInfo($"To floor {floor}.");
                    return true;
                }
                // admins, testing the white bags: one straight into the ability slot (an admin may
                // use any item there, whatever the class)
                if (what == "equip" && CrucibleAccess.IsAdmin(player.Client.Account))
                {
                    var name = string.Join(' ', parts.Skip(1));
                    var data = player.GameServer.Resources.GameData;
                    if (!data.IdToObjectType.TryGetValue(name, out var type) || !data.Items.TryGetValue(type, out var item))
                    {
                        player.SendError("/crucible equip <item id>");
                        return false;
                    }
                    player.Inventory[1] = item;
                    player.SendInfo($"{name} in your ability slot.");
                    return true;
                }
                if (what == "status" || what == "top")
                {
                    var best = CrucibleAccess.BestRound(player.GameServer, player.Client.Account);
                    var open = CrucibleAccess.MayEnter(player.Client, out var why);
                    player.SendInfo(open
                        ? $"<Crucible> Open to you ({(best >= CrucibleRules.UnlockRound ? $"your best Arena round: {best}" : CrucibleAccess.IsAdmin(player.Client.Account) ? "an admin" : "a Gilded Realm Pass")})."
                        : $"<Crucible> {why}");
                    if (open && !CrucibleAccess.IsAdmin(player.Client.Account))
                        player.SendInfo($"<Crucible> Climbs left today: {CrucibleAccess.ClimbsLeft(player.Client)} of {CrucibleAccess.DailyClimbs(player.Client)} (a new day at 00:00 UTC).");
                    var top = DbCrucibleRuns.Top(player.GameServer.Database.Conn, 3);
                    for (var i = 0; i < top.Count; i++)
                        player.SendInfo($"<Crucible> #{i + 1}: {(top[i].Floors >= CrucibleRules.Floors.Count ? "conquered" : $"floor {top[i].Floors}")} in {DbArenaRuns.FormatTime(top[i].Seconds)} by {string.Join(", ", top[i].Players)}");
                    return true;
                }
                if (player.World is CrucibleWorld)
                {
                    player.SendError("You are already in the Crucible.");
                    return false;
                }
                if (!CrucibleAccess.MayEnter(player.Client, out var locked))
                {
                    player.SendError(locked);
                    return false;
                }
                Join(player);
                return true;
            }

            private static void Join(Player player)
            {
                var nexus = player.GameServer.WorldManager.Nexus;
                nexus.Post(() =>
                {
                    var portal = nexus.Portals.Values.FirstOrDefault(p => p.ObjectDesc.IdName == CrucibleRules.PortalName);
                    if (portal == null)
                    {
                        player.SendError("The Crucible is closed.");
                        return;
                    }
                    DoomedWorlds.RefreshPortal(portal);
                    if (portal.WorldInstance != null)
                    {
                        if (portal.WorldInstance.IsPlayersMax())
                            player.SendError("The climb is full.");
                        else
                            player.Reconnect(portal.WorldInstance);
                        return;
                    }
                    if (portal.Opening != null)
                    {
                        player.SendInfo("The Crucible is opening: try again in a moment.");
                        return;
                    }
                    portal.Opening = new System.Collections.Generic.List<Player>();
                    player.GameServer.WorldManager.CreateNewWorldAsync(CrucibleRules.WorldName, nexus, built =>
                    {
                        var waiting = portal.Opening;
                        portal.Opening = null;
                        if (built == null)
                        {
                            player.SendError("[Bug] Unable to open the Crucible.");
                            return;
                        }
                        portal.WorldInstance = built;
                        player.Reconnect(built);
                        foreach (var p in waiting ?? new System.Collections.Generic.List<Player>())
                            if (p.Client != null && !p.Dead && p.World == nexus)
                                p.Reconnect(built);
                    });
                });
            }
        }
    }
}
