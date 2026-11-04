using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.database.leaderboard;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.objects;
using WorldServer.core.objects.inventory;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.impl.doomed;

// More commands ported from the Doomed Realms server (wserver/realm/commands): the forge and selling
// items for fame (now the Divine Forge's window, ForgeHandlers.cs), the shops, the minigame arenas and leaderboards, AFK, and some staff tools.
// Recipes, prices and shop lists are generated into DoomedEconomyData.cs.
namespace WorldServer.core.commands.doomed
{
    internal static class DoomedEconomy
    {
        // The Divine Forge in the Nexus (class Forge): forging and selling for fame happen in its
        // window in the Rust client, within this many tiles of it.
        public const string ForgeClass = "Forge";
        public const float ForgeReach = 5;

        public const string UseTheForge = "Forging and selling for fame have moved to the Divine Forge, in the Nexus's forge court, in the village east of the spawn just north of the shopping street (or type /forge): stand by it and open its window.";

        public static bool NearForge(Player player) => NearObject(player, ForgeClass);

        // The map holds the forge (and the Enchanter beside it) as a tile's object (it's static), so
        // look at the tiles around.
        public static bool NearObject(Player player, string objectClass)
        {
            var map = player.World.Map;
            var r = (int)MathF.Ceiling(ForgeReach);
            for (var y = (int)player.Y - r; y <= (int)player.Y + r; y++)
                for (var x = (int)player.X - r; x <= (int)player.X + r; x++)
                    if (map.Contains(x, y) && map[x, y].ObjDesc?.Class == objectClass &&
                        MathF.Sqrt((x + 0.5f - player.X) * (x + 0.5f - player.X) + (y + 0.5f - player.Y) * (y + 0.5f - player.Y)) <= ForgeReach)
                        return true;
            return false;
        }

        // The forge window's slots (inventory indexes 4-11, and 12-19 with a backpack): each once,
        // none empty. Forging may name none (everything from the forge storage and the vault).
        public static bool ValidSlots(Player player, byte[] slots, bool allowNone = false)
        {
            var end = player.HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
            return (allowNone || slots.Length > 0) && slots.Distinct().Count() == slots.Length &&
                   slots.All(s => s >= 4 && s < end && player.Inventory[s] != null);
        }

        // What a recipe takes beyond the items given: forge shards from the forge storage, and the
        // rest from the account's vault chests (ForgeVault).
        public struct ForgeSources
        {
            public Dictionary<string, int> Shards;
            public Dictionary<string, int> Vault;
        }

        // The recipe making `result` from these items (each ingredient used once), and where the
        // ingredients they leave out come from: forge shards from the forge storage first, then
        // anything from the vault (read once, into `vault`, when a recipe needs it). A client that
        // doesn't know the storage or the vault can still forge with what it holds.
        public static ((string Result, int Fame, bool Advanced, string[] Items) Recipe, ForgeSources From)? FindRecipe(
            Player player, string[] items, string result, ref ForgeVault vault, ref bool vaultRead)
        {
            var stored = ForgeStorage.Counts(player);
            foreach (var r in ForgeRecipes.All(player.GameServer))
            {
                if (r.Result != result || r.Items.Length < items.Length || r.Advanced)
                    continue;
                var left = r.Items.ToList();
                if (!items.All(i => left.Remove(i)))
                    continue;
                var from = new ForgeSources { Shards = new Dictionary<string, int>(), Vault = new Dictionary<string, int>() };
                foreach (var g in left.GroupBy(i => i))
                {
                    var n = g.Count();
                    var shard = ForgeStorage.Index(g.Key);
                    var fromStorage = shard >= 0 ? Math.Min(n, stored[shard]) : 0;
                    if (fromStorage > 0)
                        from.Shards[g.Key] = fromStorage;
                    if (n > fromStorage)
                        from.Vault[g.Key] = n - fromStorage;
                }
                if (from.Vault.Count > 0)
                {
                    if (!vaultRead)
                    {
                        vault = ForgeVault.Read(player);
                        vaultRead = true;
                    }
                    var read = vault;
                    if (read == null || from.Vault.Any(v => read.Count(v.Key) < v.Value))
                        continue;
                }
                return (r, from);
            }
            return null;
        }

        // Forges the items in these slots, with what else the recipe takes from the forge storage and
        // the vault, into `result`, the item the window showed: they must make that recipe. (The
        // advanced recipes needed a worn Forge Amulet, which is gone: none is forged.) The forged
        // item takes the first slot; with none, the first free inventory slot, or the vault when the
        // inventory is full. Returns what went wrong, or null.
        public static string Forge(Player player, byte[] slots, string result, out int fame)
        {
            fame = 0;
            if (!ValidSlots(player, slots, true))
                return "Put the ingredients in your inventory first.";
            var inv = player.Inventory;
            var data = player.GameServer.Resources.GameData;
            ForgeVault vault = null;
            var vaultRead = false;
            var found = FindRecipe(player, slots.Select(s => inv[s].ObjectId).ToArray(), result, ref vault, ref vaultRead);
            if (found == null)
                return vaultRead && vault == null
                    ? "You're missing some ingredients (your vault can't be used while you're in it)."
                    : "You don't have all the ingredients, in your inventory, forge storage or vault.";
            var (recipe, from) = found.Value;
            fame = recipe.Fame;
            if (player.CurrentFame < fame)
                return "Not enough fame to forge these items!";
            // an Infernal potion also takes its stat's potions from the potion storage
            var potionStat = ForgeRecipes.StoragePotionStat(result);
            var stored = player.Client.Account.StoredPotions;
            if (potionStat is int ps && (stored == null || ps >= stored.Length || stored[ps] < ForgeRecipes.InfernalPotionCost))
                return $"This needs {ForgeRecipes.InfernalPotionCost} {ForgeRecipes.StatPotions[ps]} in your potion storage "
                    + $"(you have {(stored != null && ps < stored.Length ? stored[ps] : 0)}).";
            if (!data.IdToObjectType.TryGetValue(result, out var type) || !data.Items.TryGetValue(type, out var item))
                return "That item doesn't exist here.";

            // where the forged item goes
            var slot = -1;
            if (slots.Length > 0)
                slot = slots[0];
            else
            {
                var end = player.HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
                for (var s = 4; s < end && slot == -1; s++)
                    if (inv[s] == null)
                        slot = s;
            }
            if (slot == -1 && !vaultRead)
            {
                vault = ForgeVault.Read(player);
                vaultRead = true;
            }
            var toVault = slot == -1;

            // the vault first: the one step that can find things changed, before anything else moves
            if (vault != null && from.Vault.Count > 0)
                vault.Take(from.Vault);
            if (toVault && (vault == null || !vault.Put(type)))
                return "Your inventory and vault are full: make room for the forged item.";
            if (vault != null && !vault.Save())
                return "Your vault changed while forging: nothing was used. Try again.";

            foreach (var s in slots)
            {
                inv[s] = null;
                inv.Data[s] = null;
            }
            if (!toVault)
                inv[slot] = item;
            ForgeStorage.Take(player, from.Shards);
            if (potionStat is int taken)
            {
                stored[taken] -= ForgeRecipes.InfernalPotionCost;
                player.Client.Account.StoredPotions = stored;
                player.Client.Account.FlushAsync();
                player.SendInfo($"Took {ForgeRecipes.InfernalPotionCost} {ForgeRecipes.StatPotions[taken]} from your potion storage.");
                ForgeStorage.Send(player);
            }
            // taken at once, so a second forge before the database answers can't spend it again
            player.CurrentFame -= fame;
            DoomedWorlds.GiveFame(player, -fame);
            if (from.Vault.Count > 0 || toVault)
            {
                var took = string.Join(", ", from.Vault.Select(v => v.Value > 1 ? $"{v.Value} {Name(player, v.Key)}" : Name(player, v.Key)));
                if (took.Length > 0)
                    player.SendInfo($"Took {took} from your vault.");
                if (toVault)
                    player.SendInfo($"Your inventory is full: {Name(player, result)} went into your vault.");
            }
            if (vault != null)
                ForgeVault.Send(player, vault);
            return null;
        }

        // Sells the items in these slots for their fame (DoomedEconomyData.SellPrices). Returns what
        // went wrong, or null.
        public static string Sell(Player player, byte[] slots, out int fame)
        {
            fame = 0;
            if (!ValidSlots(player, slots))
                return "Pick items to sell first.";
            var inv = player.Inventory;
            if (slots.Any(s => SellPrice(inv[s].ObjectId) <= 0))
                return "The forge doesn't buy some of those items.";
            fame = slots.Sum(s => SellPrice(inv[s].ObjectId));
            foreach (var s in slots)
            {
                inv[s] = null;
                inv.Data[s] = null;
            }
            player.CurrentFame += fame;
            DoomedWorlds.GiveFame(player, fame);
            return null;
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

    // /forge takes the player to the Divine Forge: beside it at once in the Nexus, or from anywhere
    // else to the Nexus, arriving beside it (LoadHandler asks ArrivalSpot). Like /nexus, it works
    // from anywhere. /forgelist and /sellfame point to the forge's window, which does what they did.
    internal class ForgeCommand : Command
    {
        public override string CommandName => "forge";

        // accounts on their way to the forge, and until when the trip counts
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> PendingArrivals = new System.Collections.Concurrent.ConcurrentDictionary<int, DateTime>();

        protected override bool Process(Player player, TickTime time, string args)
        {
            var nexus = player.GameServer.WorldManager.Nexus;
            if (player.World != nexus)
            {
                PendingArrivals[player.AccountId] = DateTime.UtcNow.AddMinutes(1);
                player.Reconnect(nexus);
                return true;
            }

            var spot = SpotBesideForge(nexus);
            if (spot == null)
            {
                player.SendInfo(DoomedEconomy.UseTheForge);
                return true;
            }
            // no cooldown, as /nexus has none: it's a walk across the Nexus
            player.TeleportPosition(time, spot.Value, true);
            return true;
        }

        // where a player arriving in a world is placed if they typed /forge on the way, or null
        public static Position? ArrivalSpot(int accountId, World target)
        {
            if (PendingArrivals.IsEmpty || !PendingArrivals.TryRemove(accountId, out var until) || until < DateTime.UtcNow)
                return null;
            return target == target.GameServer.WorldManager.Nexus ? SpotBesideForge(target) : null;
        }

        // A free tile two in front of the forge (south: its sprite stands above its tile), on the
        // carpet side of the hall; failing those, the nearest free tile within its reach. The hall
        // is walled all round it, so the nearest free tiles are always inside.
        private static Position? SpotBesideForge(World world)
        {
            var map = world.Map;
            int fx = -1, fy = -1;
            for (var y = 0; y < map.Height && fx < 0; y++)
                for (var x = 0; x < map.Width; x++)
                    if (map[x, y].ObjDesc?.Class == DoomedEconomy.ForgeClass)
                    {
                        fx = x;
                        fy = y;
                        break;
                    }
            if (fx < 0)
                return null;

            bool Free(int x, int y) => map.Contains(x, y) && map[x, y].ObjType == 0 && world.IsPassable(x + 0.5, y + 0.5, true);

            var front = new[] { 0, -1, 1 }.Where(dx => Free(fx + dx, fy + 2)).ToArray();
            if (front.Length > 0)
                return new Position(fx + front[Random.Shared.Next(front.Length)] + 0.5f, fy + 2.5f);

            var r = (int)DoomedEconomy.ForgeReach - 1;
            Position? best = null;
            var bestDist = double.MaxValue;
            for (var y = fy - r; y <= fy + r; y++)
                for (var x = fx - r; x <= fx + r; x++)
                {
                    var dist = (x - fx) * (x - fx) + (y - fy) * (y - fy);
                    if ((x != fx || y != fy) && dist <= r * r && dist < bestDist && Free(x, y))
                    {
                        best = new Position(x + 0.5f, y + 0.5f);
                        bestDist = dist;
                    }
                }
            return best;
        }
    }

    internal class ForgeListCommand : Command
    {
        public override string CommandName => "forgelist";

        protected override bool Process(Player player, TickTime time, string args)
        {
            player.SendInfo(DoomedEconomy.UseTheForge);
            return true;
        }
    }

    // Doomed Realms' /sell (betterSkillys' /sell is the market)
    internal class SellFameCommand : Command
    {
        public override string CommandName => "sellfame";

        protected override bool Process(Player player, TickTime time, string args)
        {
            player.SendInfo(DoomedEconomy.UseTheForge);
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
            var db = player.GameServer.Database.Conn;
            foreach (var solo in new[] { true, false })
            {
                var top = DbArenaRuns.Top(db, 10, solo);
                DoomedEconomy.SendLines(player, solo ? "Arena Leaderboard, solo:" : "Arena Leaderboard, groups:",
                    top.Count == 0 ? new[] { "None" } : top.Select((t, i) => $"{i + 1}. Wave {t.Wave} in {DbArenaRuns.FormatTime(t.Seconds)} - {string.Join(", ", t.Players)}"));
            }
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
                var best = StarterItem(player.GameServer.Resources.GameData, player.SlotTypes[i]);
                var current = player.Inventory[i];
                // untiered items (UTs) and items as good or better stay
                if (best == null || (Limited && current != null && (current.Tier < 0 || current.Tier >= best.Tier)))
                    continue;
                upgrades.Add((i, best));
            }
            var items = player.GameServer.Resources.GameData.Items;
            // the boosters aren't handed over but used at once (Player.StartStarterBoosts), so they
            // can't be kept, traded or sold
            var boosters = Limited
                ? BOOSTERS.Select(n => player.GameServer.Resources.GameData.IdToObjectType.TryGetValue(n, out var t) && items.TryGetValue(t, out var it) ? it : null)
                    .Where(it => it != null).ToList()
                : new List<Item>();
            if (upgrades.Count == 0 && boosters.Count == 0)
            {
                player.SendInfo(Limited ? "Your gear is already as good as the starter gear." : "No gear found for your class.");
                return false;
            }

            // the replaced items go to the backpack
            var replaced = Limited ? upgrades.Select(u => player.Inventory[u.Slot]).Where(it => it != null).ToList() : new List<Item>();
            var lastSlot = player.HasBackpack ? InventoryConstants.SWAP_FIRST : InventoryConstants.NUM_EQUIPMENT_SLOTS + InventoryConstants.NUM_INVENTORY_SLOTS;
            var free = Enumerable.Range(InventoryConstants.NUM_EQUIPMENT_SLOTS, lastSlot - InventoryConstants.NUM_EQUIPMENT_SLOTS)
                .Where(s => player.Inventory[s] == null).ToList();
            if (replaced.Count > free.Count)
            {
                player.SendError($"Make room in your inventory first: /gear needs {replaced.Count} free slots (for the items it replaces).");
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
            var used = player.StartStarterBoosts(boosters);

            static string Name(Item it) => string.IsNullOrEmpty(it.DisplayName) ? it.ObjectId : it.DisplayName;
            var names = string.Join(", ", upgrades.Select(u => Name(u.Item)));
            if (!Limited)
                player.SendInfo("Equipped: " + names);
            else
            {
                var gift = string.Join(" and ", used.Select(Name));
                player.SendInfo((upgrades.Count > 0 ? $"Equipped: {names}. " : "Your gear is already as good as the starter gear. ")
                    + (used.Count > 0 ? $"Used for you: {gift}. " : "") + "Starter gear is once per character.");
            }
            return true;
        }

        private static string ClaimKey(Player player) => $"{player.AccountId}.{player.Client.Character.CharId}";

        // Every item /gear hands out, for any class: the forge won't take them (ForgeRecipes), so
        // fresh characters' starter gear can't be forged up the tiers.
        public static HashSet<string> StarterItems(XmlData data)
        {
            var slots = data.Classes.Values.Where(c => c.SlotTypes != null).SelectMany(c => c.SlotTypes.Take(4)).Distinct();
            return new HashSet<string>(slots.Select(s => StarterItem(data, s)).Where(it => it != null).Select(it => it.ObjectId));
        }

        private static Item StarterItem(XmlData data, int slotType)
        {
            var candidates = data.Items.Values
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

    // Doomed Realms' /arena: an Arena portal for 30 seconds (/arena joins the Arena)
    internal class OpenArenaCommand : Command
    {
        public override RankingType RankRequirement => RankingType.Admin;
        public override string CommandName => "openarena";

        protected override bool Process(Player player, TickTime time, string args)
        {
            var portal = player.World.CreateNewEntity(DoomedWorlds.ArenaPortal, player.X, player.Y);
            if (portal == null)
            {
                player.SendError("The Arena portal doesn't exist.");
                return false;
            }
            player.World.StartNewTimer(30 * 1000, (w, t) => w.LeaveWorld(portal));
            player.GameServer.ChatManager.ServerAnnounce($"Arena opened by {player.Name}");
            return true;
        }
    }

    #endregion
}
