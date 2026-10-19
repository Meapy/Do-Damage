using System;
using System.Linq;
using System.Text;
using Shared;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

// Commands ported from the Doomed Realms server (wserver/realm/commands).
// Rank mapping from Doomed Realms: player commands stay Regular, the donor-rank
// commands (rank 5 "Hero") need Supporter, and staff commands need Admin.
namespace WorldServer.core.commands.doomed
{
    internal static class DoomedHelpers
    {
        // StatsManager order: MaxHP, MaxMP, Attack, Defense, Speed, Dexterity, Vitality, Wisdom
        public static readonly string[] StatNames = { "HP", "MP", "Attack", "Defense", "Speed", "Dexterity", "Vitality", "Wisdom" };

        public static Player FindOnline(Player player, string name)
        {
            var client = player.GameServer.ConnectionManager.FindClient(name.Trim());
            return client?.Player?.World == null ? null : client.Player;
        }

        public static void Notify(Player target, uint color, string message)
        {
            target.World.BroadcastIfVisible(new Notification()
            {
                ObjectId = target.Id,
                Color = new ARGB(color),
                Message = message
            }, target);
        }
    }

    #region Everyone

    internal class SWhoCommand : Command
    {
        public override string CommandName => "swho";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var players = player.GameServer.ConnectionManager.Clients.Keys
                .Where(c => c.Player?.World != null && c.Player.CanBeSeenBy(player))
                .GroupBy(c => c.Player.World.DisplayName ?? c.Player.World.IdName)
                .OrderBy(g => g.Key);
            var total = 0;
            foreach (var world in players)
            {
                var names = world.Select(c => c.Player.Name).OrderBy(n => n).ToArray();
                total += names.Length;
                player.SendInfo($"{world.Key} ({names.Length}): {string.Join(", ", names)}");
            }
            player.SendInfo($"{total} player{(total == 1 ? "" : "s")} online.");
            return true;
        }
    }

    internal class GetQuestCommand : Command
    {
        public override string CommandName => "getquest";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (player.Quest == null)
            {
                player.SendError("You don't have a quest.");
                return false;
            }
            player.SendInfo($"Quest: {player.Quest.ObjectDesc.DisplayId ?? player.Quest.ObjectDesc.IdName} at ({(int)player.Quest.X}, {(int)player.Quest.Y}).");
            return true;
        }
    }

    internal class RulesCommand : Command
    {
        public override string CommandName => "rules";

        protected override bool Process(Player player, TickTime time, string args)
        {
            player.SendInfo("Server rules:");
            player.SendInfo("1: Show respect");
            player.SendInfo("2: No duping (ban)");
            player.SendInfo("3: No spam or caps");
            player.SendInfo("4: No item slot glitches (ban)");
            player.SendInfo("5: No begging");
            player.SendInfo("6: No selling items while trading");
            return true;
        }
    }

    internal class StatsCommand : Command
    {
        public override string CommandName => "stats";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var sb = new StringBuilder($"Level {player.Level}, HP {player.Health}/{player.Stats[0]}, MP {player.Mana}/{player.Stats[1]}");
            for (var i = 2; i < DoomedHelpers.StatNames.Length; i++)
                sb.Append($", {DoomedHelpers.StatNames[i]} {player.Stats[i]}");
            player.SendInfo(sb.ToString());
            return true;
        }
    }

    internal class NothingCommand : Command
    {
        public override string CommandName => "nothing";

        protected override bool Process(Player player, TickTime time, string args)
        {
            DoomedHelpers.Notify(player, 0xFFFFFFFF, "Nothing");
            return true;
        }
    }

    #endregion

    #region Supporter (Doomed Realms rank 5)

    internal class StaffChatCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Supporter;
        public override string CommandName => "d";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                player.SendInfo("Usage: /d <message> (seen by supporters and staff)");
                return false;
            }
            foreach (var client in player.GameServer.ConnectionManager.Clients.Keys)
                if (client.Player != null && (client.Account.Rank >= (int)RankingType.Supporter || client.Account.Admin))
                    client.SendPacket(new Text()
                    {
                        BubbleTime = 0,
                        NumStars = -1,
                        Name = "#Doomed Chat",
                        Txt = $"{player.Name}: {args}"
                    });
            return true;
        }
    }

    internal class SayCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Supporter;
        public override string CommandName => "say";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                player.SendInfo("Usage: /say <message>");
                return false;
            }
            DoomedHelpers.Notify(player, 0xFF00FF00, args.Length > 60 ? args.Substring(0, 60) : args);
            return true;
        }
    }

    internal class StarsCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Supporter;
        public override string CommandName => "stars";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (!int.TryParse(args, out var stars) || stars < 0 || stars > Player.STAFF_STARS)
            {
                player.SendInfo($"Usage: /stars <0-{Player.STAFF_STARS}> (until you reconnect; {Player.STAFF_STARS} is staff's green star)");
                return false;
            }
            player.Stars = stars;
            return true;
        }
    }

    #endregion

    #region Admin

    internal class LevelCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "level";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (!int.TryParse(args, out var level) || level < 1 || level > 20)
            {
                player.SendInfo("Usage: /level <1-20>");
                return false;
            }
            player.Level = level;
            player.Experience = Player.GetLevelExp(level);
            player.SendInfo($"Level set to {level}.");
            return true;
        }
    }

    internal class SummonCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "summon";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                player.SendInfo("Usage: /summon <player>");
                return false;
            }
            var target = DoomedHelpers.FindOnline(player, args);
            if (target == null || target == player)
            {
                player.SendError("Player not found!");
                return false;
            }
            if (target.World == player.World)
            {
                target.TeleportPosition(time, player.X, player.Y, true);
                return true;
            }
            target.SendInfo($"You are being summoned by {player.Name}.");
            player.World.Invite(target.Client.Account.AccountId); // even into an arena game that has started
            target.Client.Reconnect(new Reconnect()
            {
                Host = "",
                GameId = player.World.Id,
                Name = player.World.DisplayName
            });
            player.SendInfo($"Summoned {target.Name}.");
            return true;
        }
    }

    internal class GodCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "god";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (player.HasConditionEffect(ConditionEffectIndex.Invincible))
            {
                player.RemoveCondition(ConditionEffectIndex.Invincible);
                player.SendInfo("Godmode off.");
            }
            else
            {
                player.ApplyPermanentConditionEffect(ConditionEffectIndex.Invincible);
                player.SendInfo("Godmode on.");
            }
            return true;
        }
    }

    internal class ClearInventoryCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "ci";

        protected override bool Process(Player player, TickTime time, string args)
        {
            // slots 0-3 are equipment, 4-11 the inventory, 12+ the backpack
            var last = player.HasBackpack ? Math.Min(20, player.Inventory.Length) : Math.Min(12, player.Inventory.Length);
            for (var i = 4; i < last; i++)
                player.Inventory[i] = null;
            player.SendInfo("Inventory cleared.");
            return true;
        }
    }

    internal abstract class CurrencyCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        protected abstract CurrencyType Currency { get; }

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (!int.TryParse(args, out var amount) || amount == 0)
            {
                player.SendInfo($"Usage: /{CommandName} <amount> (negative to remove)");
                return false;
            }
            var acc = player.Client.Account;
            var trans = player.GameServer.Database.Conn.CreateTransaction();
            player.GameServer.Database.UpdateCurrency(acc, amount, Currency, trans)
                .ContinueWith(t =>
                {
                    if (Currency == CurrencyType.Gold)
                        player.Credits = acc.Credits;
                    else
                        player.CurrentFame = acc.Fame;
                });
            trans.Execute(CommandFlags.FireAndForget);
            player.SendInfo($"{(amount > 0 ? "Added" : "Removed")} {Math.Abs(amount)} {CommandName}.");
            return true;
        }
    }

    internal class FameCommand : CurrencyCommand
    {
        public override string CommandName => "fame";
        protected override CurrencyType Currency => CurrencyType.Fame;
    }

    internal class GoldCommand : CurrencyCommand
    {
        public override string CommandName => "gold";
        protected override CurrencyType Currency => CurrencyType.Gold;
    }

    internal abstract class SetStatCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        protected abstract int Stat { get; }

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (!int.TryParse(args, out var value) || value < 0 || value > 10000)
            {
                player.SendInfo($"Usage: /{CommandName} <0-10000> (sets your base {DoomedHelpers.StatNames[Stat]})");
                return false;
            }
            player.Stats.Base[Stat] = value;
            player.SendInfo($"Base {DoomedHelpers.StatNames[Stat]} set to {value}.");
            return true;
        }
    }

    internal class SetHpCommand : SetStatCommand { public override string CommandName => "hp"; protected override int Stat => 0; }
    internal class SetMpCommand : SetStatCommand { public override string CommandName => "mp"; protected override int Stat => 1; }
    internal class SetAttCommand : SetStatCommand { public override string CommandName => "att"; protected override int Stat => 2; }
    internal class SetDefCommand : SetStatCommand { public override string CommandName => "def"; protected override int Stat => 3; }
    internal class SetSpdCommand : SetStatCommand { public override string CommandName => "spd"; protected override int Stat => 4; }
    internal class SetDexCommand : SetStatCommand { public override string CommandName => "dex"; protected override int Stat => 5; }
    internal class SetVitCommand : SetStatCommand { public override string CommandName => "vit"; protected override int Stat => 6; }
    internal class SetWisCommand : SetStatCommand { public override string CommandName => "wis"; protected override int Stat => 7; }

    internal class AddEffCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "addeff";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (!Enum.TryParse(args.Trim(), true, out ConditionEffectIndex effect))
            {
                player.SendError("Invalid effect! Examples: Invincible, Speedy, Invisible, Damaging, Berserk.");
                return false;
            }
            player.ApplyPermanentConditionEffect(effect);
            player.SendInfo($"Added '{effect}'.");
            return true;
        }
    }

    internal class RemEffCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "remeff";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (!Enum.TryParse(args.Trim(), true, out ConditionEffectIndex effect))
            {
                player.SendError("Invalid effect!");
                return false;
            }
            player.RemoveCondition(effect);
            player.SendInfo($"Removed '{effect}'.");
            return true;
        }
    }

    internal class OSayCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "osay";

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                player.SendInfo("Usage: /osay <message>");
                return false;
            }
            player.GameServer.ChatManager.Oryx(player.World, args);
            return true;
        }
    }

    internal class KillPlayerCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "kill";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var parts = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                player.SendInfo("Usage: /kill <player> [cause] (permanent death)");
                return false;
            }
            var target = DoomedHelpers.FindOnline(player, parts[0]);
            if (target == null)
            {
                player.SendError("Player not found!");
                return false;
            }
            target.Death(parts.Length > 1 ? parts[1] : "Server Admin");
            player.SendInfo($"Killed {target.Name}.");
            return true;
        }
    }

    internal class RankCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "rank";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var parts = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var rank) || rank < 0 || rank > (int)RankingType.Admin)
            {
                player.SendInfo("Usage: /rank <account name> <0-6> (0 Regular, 1 Donator, 2 Supporter, 3 Sponsor, 4 Sandbox, 5 Moderator, 6 Admin)");
                return false;
            }
            var db = player.GameServer.Database;
            var id = db.ResolveId(parts[0]);
            if (id == 0)
            {
                player.SendError("Account not found!");
                return false;
            }
            if (id == player.AccountId)
            {
                player.SendError("You can't change your own rank.");
                return false;
            }
            var online = player.GameServer.ConnectionManager.FindClient(id);
            var acc = online?.Account ?? db.GetAccount(id);
            acc.Rank = rank;
            acc.FlushAsync();
            player.SendInfo($"{parts[0]} is now rank {rank} ({(RankingType)rank}). It applies from their next login.");
            return true;
        }
    }

    // Opens a new instance of a dungeon and sends you there. Several Doomed Realms areas have no
    // natural entrance (Battle Arena, Kitchen, Mine, Gauntlet, ...).
    internal class DungeonCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "dungeon";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var worlds = player.GameServer.Resources.GameData.AllWorlds.ToList();
            var name = args.Trim();
            if (name.Length == 0)
            {
                player.SendInfo("Usage: /dungeon <name>. Doomed Realms dungeons: " +
                    string.Join(", ", worlds.Where(w => w.MapJM.Any(m => m.StartsWith("Doomed/"))).Select(w => w.IdName).OrderBy(n => n)));
                return false;
            }
            var world = worlds.FirstOrDefault(w => string.Equals(w.IdName, name, StringComparison.OrdinalIgnoreCase))
                        ?? worlds.FirstOrDefault(w => w.IdName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (world == null)
            {
                player.SendError($"No dungeon called '{name}'.");
                return false;
            }
            var from = player.World;
            player.GameServer.WorldManager.CreateNewWorldAsync(world.IdName, from, instance =>
            {
                if (instance == null)
                    player.SendError($"Couldn't open {world.IdName}.");
                else if (player.World == from && player.Client != null)
                    player.Reconnect(instance);
            });
            return true;
        }
    }

    internal class IpCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "ip";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var target = string.IsNullOrWhiteSpace(args) ? null : DoomedHelpers.FindOnline(player, args);
            if (target == null)
            {
                player.SendError("Usage: /ip <online player>");
                return false;
            }
            player.SendInfo($"{target.Name}: {target.Client.IpAddress}");
            return true;
        }
    }

    #endregion
}
