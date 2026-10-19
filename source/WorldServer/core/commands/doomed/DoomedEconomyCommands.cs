using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Shared;
using Shared.database.leaderboard;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.objects;
using WorldServer.core.objects.inventory;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.impl.doomed;

// More commands ported from the Doomed Realms server (wserver/realm/commands): the forge, selling
// items for fame, the shops, the minigame arenas and leaderboards, AFK, and some staff tools.
// Recipes, prices and shop lists are generated into DoomedEconomyData.cs.
namespace WorldServer.core.commands.doomed
{
    internal static class DoomedEconomy
    {
        // what /yes or /no answers
        public sealed class Pending
        {
            public bool Selling;
            public int[] Slots;
            public string Result;
            public int Fame;
            public bool UsesAmulet;
        }

        private static readonly ConditionalWeakTable<Player, Pending> PendingDecisions = new ConditionalWeakTable<Player, Pending>();

        public static void SetPending(Player player, Pending pending)
        {
            PendingDecisions.Remove(player);
            if (pending != null)
                PendingDecisions.Add(player, pending);
        }

        public static Pending TakePending(Player player)
        {
            if (!PendingDecisions.TryGetValue(player, out var p))
                return null;
            PendingDecisions.Remove(player);
            return p;
        }

        public const string ForgeAmulet = "Forge Amulet";

        public static bool WearsForgeAmulet(Player player) => player.Inventory[3]?.ObjectId == ForgeAmulet;

        // "/forge 1 2" style slot numbers: 1-8 inventory, 9-16 backpack -> inventory indexes
        public static int[] ParseSlots(Player player, string args, out string error)
        {
            error = null;
            var slots = new List<int>();
            foreach (var a in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(a, out var n) || n < 1 || n > (player.HasBackpack ? 16 : 8))
                {
                    error = $"'{a}' isn't an inventory slot (1-{(player.HasBackpack ? 16 : 8)}).";
                    return null;
                }
                var slot = n + 3;
                if (slots.Contains(slot))
                {
                    error = "You cannot select the same slot twice!";
                    return null;
                }
                if (player.Inventory[slot] == null)
                {
                    error = $"Slot {n} is empty.";
                    return null;
                }
                slots.Add(slot);
            }
            return slots.ToArray();
        }

        // the recipe made of exactly these items (each ingredient used once)
        public static (string Result, int Fame, bool Advanced, string[] Items)? FindRecipe(string[] items, bool amulet)
        {
            foreach (var r in DoomedEconomyData.Recipes)
            {
                if (r.Items.Length != items.Length || (r.Advanced && !amulet))
                    continue;
                var left = r.Items.ToList();
                if (items.All(i => left.Remove(i)))
                    return r;
            }
            return null;
        }

        public static bool CanMake((string Result, int Fame, bool Advanced, string[] Items) r, Player player)
        {
            var have = Enumerable.Range(4, InventoryConstants.SWAP_FIRST - 4).Select(i => player.Inventory[i]?.ObjectId).Where(i => i != null).ToList();
            return r.Items.All(i => have.Remove(i)) && r.Fame <= player.CurrentFame;
        }

        public static int SellPrice(string itemId)
        {
            foreach (var p in DoomedEconomyData.SellPrices)
                if (p.Item == itemId)
                    return p.Fame;
            return 0;
        }

        public static string Name(Player player, string itemId) =>
            player.GameServer.Resources.GameData.IdToObjectType.TryGetValue(itemId, out var t) &&
            player.GameServer.Resources.GameData.Items.TryGetValue(t, out var item) && !string.IsNullOrEmpty(item.DisplayName)
                ? item.DisplayName : itemId;

        public static void SendLines(Player player, string title, IEnumerable<string> lines)
        {
            player.SendInfo(title);
            foreach (var l in lines)
                player.SendInfo(l);
        }

        public static bool SafeToPause(Player player, double radius) =>
            !player.World.Enemies.Values.Any(e => e.AllyOwnerId == -1 && e.ObjectDesc.Enemy && e.DistTo(player) <= radius);

        public static bool InArena(Player player) => player.World is DoomedBattleArena;
    }

    #region Everyone

    internal class ForgeCommand : Command
    {
        public override string CommandName => "forge";

        protected override bool Process(Player player, TickTime time, string args)
        {
            DoomedEconomy.SetPending(player, null);
            var slots = DoomedEconomy.ParseSlots(player, args, out var error);
            if (slots == null || slots.Length < 2)
            {
                player.SendHelp(error ?? "Usage: /forge <slot #1> <slot #2> etc. (inventory slots 1-8). /forgelist shows the recipes.");
                return false;
            }
            var amulet = DoomedEconomy.WearsForgeAmulet(player);
            player.SendInfo("Researching " + (amulet ? "advanced " : "") + "combinations...");
            var recipe = DoomedEconomy.FindRecipe(slots.Select(s => player.Inventory[s].ObjectId).ToArray(), amulet);
            if (recipe == null)
            {
                player.SendInfo("No combination found!");
                return false;
            }
            var cost = recipe.Value.Fame / (amulet ? 2 : 1);
            DoomedEconomy.SetPending(player, new DoomedEconomy.Pending()
            {
                Slots = slots,
                Result = recipe.Value.Result,
                Fame = cost,
                UsesAmulet = amulet
            });
            player.SendInfo($"It costs {cost} fame to forge these items into a {DoomedEconomy.Name(player, recipe.Value.Result)}. Are you sure? Type /yes or /no");
            return true;
        }
    }

    internal class ForgeListCommand : Command
    {
        public override string CommandName => "forgelist";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var amulet = DoomedEconomy.WearsForgeAmulet(player);
            var filter = args.Trim();
            var recipes = DoomedEconomyData.Recipes.Where(r => amulet || !r.Advanced).ToList();
            if (filter.Length == 0)
            {
                var ready = recipes.Where(r => DoomedEconomy.CanMake(r, player)).ToList();
                DoomedEconomy.SendLines(player, $"Forge: {recipes.Count} recipes" + (amulet ? " (Forge Amulet: advanced recipes, half price)" : "") +
                    $". You can make {ready.Count} now. Use /forgelist <item> to search, /forgelist all for all.",
                    ready.Select(r => Line(player, r, amulet)));
                return true;
            }
            var shown = filter.Equals("all", StringComparison.OrdinalIgnoreCase) ? recipes
                : recipes.Where(r => r.Result.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     r.Items.Any(i => i.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (shown.Count == 0)
            {
                player.SendInfo($"No forge recipe uses or makes '{filter}'.");
                return false;
            }
            DoomedEconomy.SendLines(player, $"Forge recipes ({shown.Count}):", shown.Select(r => Line(player, r, amulet)));
            return true;
        }

        private static string Line(Player player, (string Result, int Fame, bool Advanced, string[] Items) r, bool amulet) =>
            (DoomedEconomy.CanMake(r, player) ? "* " : "") + (r.Advanced ? "[advanced] " : "") + DoomedEconomy.Name(player, r.Result) +
            " = " + string.Join(" + ", r.Items.Select(i => DoomedEconomy.Name(player, i))) + $" ({r.Fame / (amulet ? 2 : 1)} fame)";
    }

    // Doomed Realms' /sell (betterSkillys' /sell is the market)
    internal class SellFameCommand : Command
    {
        public override string CommandName => "sellfame";

        protected override bool Process(Player player, TickTime time, string args)
        {
            DoomedEconomy.SetPending(player, null);
            var slots = DoomedEconomy.ParseSlots(player, args, out var error);
            if (slots == null || slots.Length == 0)
            {
                player.SendHelp(error ?? "Usage: /sellfame <slot #1> <slot #2> etc. (inventory slots 1-8)");
                return false;
            }
            var priced = slots.Where(s => DoomedEconomy.SellPrice(player.Inventory[s].ObjectId) > 0).ToArray();
            if (priced.Length == 0)
            {
                player.SendInfo("No prices for specified items!");
                return false;
            }
            var fame = priced.Sum(s => DoomedEconomy.SellPrice(player.Inventory[s].ObjectId));
            DoomedEconomy.SetPending(player, new DoomedEconomy.Pending() { Selling = true, Slots = priced, Fame = fame });
            player.SendInfo("Slots being sold: [" + string.Join(", ", priced.Select(s => s - 3)) + "]");
            player.SendInfo($"You gain {fame} fame from these items. Sell them? Type /yes or /no");
            return true;
        }
    }

    internal class YesCommand : Command
    {
        public override string CommandName => "yes";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var p = DoomedEconomy.TakePending(player);
            if (p == null)
            {
                player.SendInfo("Nothing to confirm.");
                return false;
            }
            var inv = player.Inventory;
            if (p.Selling)
            {
                var fame = 0;
                foreach (var s in p.Slots)
                    fame += inv[s] == null ? 0 : DoomedEconomy.SellPrice(inv[s].ObjectId);
                if (fame != p.Fame)
                {
                    player.SendError("Your items changed. Could not sell.");
                    return false;
                }
                foreach (var s in p.Slots)
                    inv[s] = null;
                DoomedWorlds.GiveFame(player, fame);
                player.SendInfo("Items sold!");
                return true;
            }

            // forging: the same items must still be there
            var items = p.Slots.Select(s => inv[s]?.ObjectId).ToArray();
            var recipe = items.Any(i => i == null) ? null : DoomedEconomy.FindRecipe(items, p.UsesAmulet);
            if (recipe == null || recipe.Value.Result != p.Result || (p.UsesAmulet && !DoomedEconomy.WearsForgeAmulet(player)))
            {
                player.SendError("Your items changed. Forge again.");
                return false;
            }
            if (player.CurrentFame < p.Fame)
            {
                player.SendInfo("Not enough fame to forge these items!");
                return false;
            }
            var data = player.GameServer.Resources.GameData;
            if (!data.IdToObjectType.TryGetValue(p.Result, out var type) || !data.Items.TryGetValue(type, out var result))
            {
                player.SendError("That item doesn't exist here.");
                return false;
            }
            foreach (var s in p.Slots)
                inv[s] = null;
            inv[p.Slots[0]] = result;
            DoomedWorlds.GiveFame(player, -p.Fame);
            if (p.UsesAmulet)
                inv[3] = null;
            player.SendInfo($"Your items have been forged into a {DoomedEconomy.Name(player, p.Result)}" + (p.UsesAmulet ? ", breaking your Forge Amulet." : "!"));
            return true;
        }
    }

    internal class NoCommand : Command
    {
        public override string CommandName => "no";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var p = DoomedEconomy.TakePending(player);
            player.SendInfo(p == null ? "Nothing to cancel." : p.Selling ? "Cancelled selling." : "Cancelled forging.");
            return true;
        }
    }

    internal class ShopCommand : Command
    {
        public override string CommandName => "shop";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var names = DoomedShop.ShopNames.ToList();
            var name = args.Trim();
            var shop = names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (shop == null)
            {
                player.SendInfo("Usage: /shop <name>. Shops: " + string.Join(", ", names));
                return name.Length == 0;
            }
            if (player.World is DoomedBattleArena || player.World is DoomedMinigame)
            {
                player.SendError("You can't leave a minigame for the shop.");
                return false;
            }
            var world = player.GameServer.WorldManager.CreateNewWorld("Shop", null, player.World) as DoomedShop;
            if (world == null)
            {
                player.SendError("The shop is closed.");
                return false;
            }
            world.SetShop(shop);
            world.DisplayName = "Shop: " + shop;
            player.Reconnect(world);
            return true;
        }
    }

    internal class ArenasCommand : Command
    {
        public override string CommandName => "arenas";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var arenas = player.GameServer.WorldManager.GetWorlds().OfType<DoomedBattleArena>().Where(w => w.Players.Count > 0).ToList();
            if (arenas.Count == 0)
            {
                player.SendInfo("Current arenas: none.");
                return true;
            }
            DoomedEconomy.SendLines(player, "Current arenas:",
                arenas.Select(a => $"{a.IdName}, wave {a.Wave} - {string.Join(", ", a.PlayerNames)}{(a.Joinable ? " (joinable)" : "")}"));
            return true;
        }
    }

    internal class LeaderboardCommand : Command
    {
        public override string CommandName => "leaderboard";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var top = DbArenaRuns.Top(player.GameServer.Database.Conn, 10);
            DoomedEconomy.SendLines(player, "Arena Leaderboard:",
                top.Count == 0 ? new[] { "None" } : top.Select((t, i) => $"{i + 1}. Wave {t.Wave} in {DbArenaRuns.FormatTime(t.Seconds)} - {string.Join(", ", t.Players)}"));
            return true;
        }
    }

    internal class GuildLeaderboardCommand : Command
    {
        public override string CommandName => "gleaderboard";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var db = player.GameServer.Database.Conn;
            var last = (int)db.StringGet("nextGuildId");
            var guilds = new List<(string Name, int Fame)>();
            for (var i = 1; i <= last; i++)
            {
                var v = db.HashGet($"guild.{i}", new RedisValue[] { "name", "fame" });
                if (!v[0].IsNullOrEmpty)
                    guilds.Add((v[0], v[1].IsNullOrEmpty ? 0 : (int)v[1]));
            }
            var top = guilds.OrderByDescending(g => g.Fame).Take(10).ToList();
            DoomedEconomy.SendLines(player, "Guilds:", top.Count == 0 ? new[] { "None" } : top.Select((g, i) => $"{i + 1}. {g.Name} - {g.Fame} Fame"));
            return true;
        }
    }

    internal class AfkCommand : Command
    {
        public override string CommandName => "afk";

        protected override bool Process(Player player, TickTime time, string args) => DoomedPause.Toggle(player, 8, true);
    }

    internal class PauseCommand : Command
    {
        public override string CommandName => "pause";

        protected override bool Process(Player player, TickTime time, string args) => DoomedPause.Toggle(player, 6, false);
    }

    internal static class DoomedPause
    {
        public static bool Toggle(Player player, double radius, bool afk)
        {
            if (DoomedEconomy.InArena(player))
            {
                player.SendError("You cannot pause in the arena");
                return false;
            }
            if (player.HasConditionEffect(ConditionEffectIndex.Paused))
            {
                player.RemoveCondition(ConditionEffectIndex.Paused);
                if (afk)
                    player.World.ForeachPlayer(p => DoomedWorlds.Notify(p, 0xff00ff00, player.Name + " is active"));
                player.SendInfo(afk ? "Active!" : "Game resumed.");
                return true;
            }
            if (!DoomedEconomy.SafeToPause(player, radius))
            {
                player.SendInfo(afk ? "Not safe to go AFK." : "Not safe to pause.");
                return false;
            }
            player.ApplyPermanentConditionEffect(ConditionEffectIndex.Paused);
            if (afk)
                player.World.ForeachPlayer(p => DoomedWorlds.Notify(p, 0xff00ff00, player.Name + " is AFK"));
            player.SendInfo(afk ? "AFK!" : "Game paused.");
            return true;
        }
    }

    #endregion

    #region Everyone

    // starter gear of the player's class: a mid tier item of each equipment slot, about half way up
    // its regular tiers (T7 weapon, T8 armor, T4 ability and ring). The regular tiers are the run of
    // consecutive tiers from the lowest, so special items with odd tiers (20, 22, 53) don't count,
    // and nothing above MAX_TIER is ever given.
    //
    // Everyone can use it, once per character, in the Nexus, below level MAX_LEVEL. It only
    // upgrades: a slot whose item is as good or better keeps it, and the items it replaces move to
    // the backpack, so it needs room for them. A character can't take it twice (the claim is in
    // Redis, GEAR_CLAIMS), so farming it means levelling nothing and making a new character each time.
    // With it come an XP Booster (50% more experience until level 20 or death) and a Loot Drop Potion
    // (more drops for 30 minutes), both soulbound, into the backpack (BOOSTERS).
    internal class GearCommand : Command
    {
        public const int MAX_LEVEL = 20;
        private const int MAX_TIER = 8;
        private const string GEAR_CLAIMS = "doomed.gearClaimed";   // set of "<account id>.<character id>"
        private static readonly string[] BOOSTERS = { "XP Booster", "Loot Drop Potion" };

        public override string CommandName => "gear";

        // /set (admins) equips the same gear anywhere, any number of times
        protected virtual bool Limited => true;

        protected override bool Process(Player player, TickTime time, string args)
        {
            if (Limited)
            {
                if (!(player.World is NexusWorld))
                {
                    player.SendError("/gear only works in the Nexus.");
                    return false;
                }
                if (player.Level >= MAX_LEVEL)
                {
                    player.SendError($"/gear is starter gear, for characters below level {MAX_LEVEL}.");
                    return false;
                }
                if (player.GameServer.Database.Conn.SetContains(GEAR_CLAIMS, ClaimKey(player)))
                {
                    player.SendError("This character has already had its starter gear (once per character).");
                    return false;
                }
            }

            var upgrades = new List<(int Slot, Item Item)>();
            for (var i = 0; i < 4; i++)
            {
                var best = StarterItem(player, player.SlotTypes[i]);
                var current = player.Inventory[i];
                // untiered items (UTs) and items as good or better stay
                if (best == null || (Limited && current != null && (current.Tier < 0 || current.Tier >= best.Tier)))
                    continue;
                upgrades.Add((i, best));
            }
            var items = player.GameServer.Resources.GameData.Items;
            var boosters = Limited
                ? BOOSTERS.Select(n => player.GameServer.Resources.GameData.IdToObjectType.TryGetValue(n, out var t) && items.TryGetValue(t, out var it) ? it : null)
                    .Where(it => it != null).ToList()
                : new List<Item>();
            if (upgrades.Count == 0 && boosters.Count == 0)
            {
                player.SendInfo(Limited ? "Your gear is already as good as the starter gear." : "No gear found for your class.");
                return false;
            }

            // the replaced items and the boosters go to the backpack
            var replaced = Limited ? upgrades.Select(u => player.Inventory[u.Slot]).Where(it => it != null).ToList() : new List<Item>();
            replaced.AddRange(boosters);
            var lastSlot = player.HasBackpack ? InventoryConstants.SWAP_FIRST : InventoryConstants.NUM_EQUIPMENT_SLOTS + InventoryConstants.NUM_INVENTORY_SLOTS;
            var free = Enumerable.Range(InventoryConstants.NUM_EQUIPMENT_SLOTS, lastSlot - InventoryConstants.NUM_EQUIPMENT_SLOTS)
                .Where(s => player.Inventory[s] == null).ToList();
            if (replaced.Count > free.Count)
            {
                player.SendError($"Make room in your inventory first: /gear needs {replaced.Count} free slots (for its boosters and the items it replaces).");
                return false;
            }
            if (Limited && !player.GameServer.Database.Conn.SetAdd(GEAR_CLAIMS, ClaimKey(player)))
            {
                player.SendError("This character has already had its starter gear (once per character).");
                return false;
            }

            for (var r = 0; r < replaced.Count; r++)
                player.Inventory[free[r]] = replaced[r];
            foreach (var (slot, item) in upgrades)
                player.Inventory[slot] = item;

            static string Name(Item it) => string.IsNullOrEmpty(it.DisplayName) ? it.ObjectId : it.DisplayName;
            var names = string.Join(", ", upgrades.Select(u => Name(u.Item)));
            if (!Limited)
                player.SendInfo("Equipped: " + names);
            else
            {
                var gift = string.Join(" and ", boosters.Select(Name));
                player.SendInfo((upgrades.Count > 0 ? $"Equipped: {names}. " : "Your gear is already as good as the starter gear. ")
                    + (boosters.Count > 0 ? $"In your backpack: {gift}. " : "") + "Starter gear is once per character.");
            }
            return true;
        }

        private static string ClaimKey(Player player) => $"{player.AccountId}.{player.Client.Character.CharId}";

        private static Item StarterItem(Player player, int slotType)
        {
            var candidates = player.GameServer.Resources.GameData.Items.Values
                .Where(it => it.SlotType == slotType && it.Tier >= 0 && !it.Consumable).ToList();
            if (candidates.Count == 0)
                return null;
            var tiers = candidates.Select(it => it.Tier).ToHashSet();
            var top = tiers.Min();
            while (tiers.Contains(top + 1))
                top++;
            var target = Math.Min(top / 2, MAX_TIER);
            return candidates.Where(it => it.Tier <= target)
                .OrderByDescending(it => it.Tier).ThenBy(it => it.ObjectType).FirstOrDefault();
        }
    }

    #endregion

    #region Admin

    internal class SetGearCommand : GearCommand
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "set";
        protected override bool Limited => false;
    }

    internal class GiveEffCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "giveeff";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                player.SendHelp("Usage: /giveeff <player> <effect>");
                return false;
            }
            var target = DoomedHelpers.FindOnline(player, parts[0]);
            if (target == null)
            {
                player.SendError($"{parts[0]} isn't online.");
                return false;
            }
            if (!Enum.TryParse(parts[1].Trim(), true, out ConditionEffectIndex effect))
            {
                player.SendError("Invalid effect!");
                return false;
            }
            target.ApplyPermanentConditionEffect(effect);
            player.SendInfo("Success!");
            return true;
        }
    }

    internal class MessageCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "message";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                player.SendHelp("Usage: /message <title> <message>");
                return false;
            }
            player.GameServer.ChatManager.ServerAnnounce($"[{parts[0]}] {parts[1]}");
            return true;
        }
    }

    // kills monsters so the players around get the experience (betterSkillys' /killall removes them)
    internal class KillAllXCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "killallx";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var name = args.Trim();
            if (name.Length == 0)
            {
                player.SendHelp("Usage: /killallx <monster name>");
                return false;
            }
            var targets = player.World.Enemies.Values
                .Where(e => e.AllyOwnerId == -1 && e.ObjectDesc.IdName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var e in targets)
                e.Damage(player, ref time, 10_000_000, true);
            player.SendInfo($"Killed {targets.Count}.");
            return true;
        }
    }

    internal class GraveCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "grave";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.Length > 0 ? parts[0] : player.World.Players.Values.Select(p => p.Name).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() ?? player.Name;
            var maxed = parts.Length > 1 && int.TryParse(parts[1], out var m) ? Math.Clamp(m, 0, 8) : Random.Shared.Next(0, 9);
            ushort type = maxed switch
            {
                8 => 0x072d, 7 => 0x072c, 6 => 0x072b, 5 => 0x072a, 4 => 0x0729, 3 => 0x0728, 2 => 0x0727, 1 => 0x0726,
                _ => 0x0725
            };
            var grave = new StaticObject(player.GameServer, type, maxed == 0 ? 300000 : (int?)null, true, maxed == 0, false);
            grave.Move(player.X, player.Y);
            grave.Name = name;
            player.World.EnterWorld(grave);
            return true;
        }
    }

    // Doomed Realms' /dragons: the Admin Room
    internal class DragonsCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "dragons";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var world = player.GameServer.WorldManager.CreateNewWorld("Admin Room", null, player.World);
            if (world == null)
            {
                player.SendError("Couldn't open the Admin Room.");
                return false;
            }
            player.Reconnect(world);
            return true;
        }
    }

    // Doomed Realms' /arena: a Battle Arena portal for 30 seconds (betterSkillys' /arena joins its own arena)
    internal class OpenArenaCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "openarena";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var free = args.Trim().Equals("free", StringComparison.OrdinalIgnoreCase);
            var portal = player.World.CreateNewEntity(free ? "Free Battle Arena Portal" : "Battle Arena Portal", player.X, player.Y);
            if (portal == null)
            {
                player.SendError("The Battle Arena portal doesn't exist.");
                return false;
            }
            player.World.StartNewTimer(30 * 1000, (w, t) => w.LeaveWorld(portal));
            player.GameServer.ChatManager.ServerAnnounce($"{(free ? "Free arena" : "Arena")} opened by {player.Name}");
            return true;
        }
    }

    #endregion
}
