using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Shared.resources;
using WorldServer.core;
using WorldServer.logic.loot;

namespace WorldServer.core.commands.doomed
{
    // Every recipe the Divine Forge knows: Doomed Realms' (DoomedEconomyData.Recipes), plus two of
    // each tiered item forging into the next tier up, for every slot type a class wears (rings by
    // their stat: two Rings of Attack make a Ring of Greater Attack). Tiered results cost at most
    // TieredCap fame, as the tier chains are there to save players trips, not to sink fame, and an
    // ability's two-for-one costs its tier's price whatever the Doomed Realms list says (every
    // ability kind then costs what the rings do); untiered results keep their Doomed Realms price. Built from the game data on first use, so
    // new tiered items get recipes without a change here.
    //
    // The forge only makes what players can find: a recipe's result must be tradeable (not an
    // <Admin/> or Item.OpItems item, like the Crown), dropped by some monster (its loot or the
    // tier pools it rolls) or one of the ForgeOnly items, and not a seasonal drop (SeasonalThreshold.Items,
    // Loot.WinterBossDrops: they come back each year without having been rebalanced), and its
    // ingredients tradeable.
    internal static class ForgeRecipes
    {
        public const int TieredCap = 2000;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // The fame for a tiered result by tier, on the weapons' 0-14 scale (Doomed Realms' sword
        // chain: 10 for a Broad Sword up to 2000 for a Sword of Splendor).
        private static readonly int[] TierFame = { 0, 10, 30, 50, 70, 90, 100, 200, 300, 400, 500, 800, 1000, 2000, 2000 };

        // Items only the forge makes, which no monster drops (so they skip that rule), and items
        // with the same numbers swapping looks (the Dirk of Cronus and Phantasm Dirk; the Doom Bow,
        // Sun's Judgement and Bow of the Morning Star; the Thousand Shot and Sunny Side Bow; the Staff
        // of the Cosmic Whole and Frosty's Walking Stick; the Staff of Extreme Prejudice and Staff of
        // Iceblast; the Wand of the Bulwark and Eternal Snowflake Wand; the Wand of Evocation and
        // Winter's Breath Wand): result, fame, needs a Forge Amulet, ingredients.
        private static readonly (string Result, int Fame, bool Advanced, string[] Items)[] ForgeOnly =
        {
            ("Reforged Forgotten Crown", 6000, false, new[] { "The Forgotten Crown", "The Forgotten Crown" }),
            ("Reforged Dirk of Cronus", 6000, false, new[] { "Dirk of Cronus", "Dirk of Cronus" }),
            ("Dagger of the Terrible Talon", 200, false, new[] { "Obsidian Dagger", "Obsidian Dagger" }),
            ("Phantasm Dirk", 100, false, new[] { "Dirk of Cronus" }),
            ("Dirk of Cronus", 100, false, new[] { "Phantasm Dirk" }),
            ("Reforged Doom Bow", 6000, false, new[] { "Doom Bow", "Doom Bow" }),
            ("Sun's Judgement", 100, false, new[] { "Doom Bow" }),
            ("Doom Bow", 100, false, new[] { "Sun's Judgement" }),
            ("Bow of the Morning Star", 100, false, new[] { "Doom Bow" }),
            ("Doom Bow", 100, false, new[] { "Bow of the Morning Star" }),
            ("Sunny Side Bow", 100, false, new[] { "Thousand Shot" }),
            ("Thousand Shot", 100, false, new[] { "Sunny Side Bow" }),
            ("Frosty's Walking Stick", 100, false, new[] { "Staff of the Cosmic Whole" }),
            ("Staff of the Cosmic Whole", 100, false, new[] { "Frosty's Walking Stick" }),
            ("Staff of Iceblast", 100, false, new[] { "Staff of Extreme Prejudice" }),
            ("Staff of Extreme Prejudice", 100, false, new[] { "Staff of Iceblast" }),
            ("Reforged Crystal Wand", 6000, false, new[] { "Crystal Wand", "Crystal Wand" }),
            ("Eternal Snowflake Wand", 100, false, new[] { "Wand of the Bulwark" }),
            ("Wand of the Bulwark", 100, false, new[] { "Eternal Snowflake Wand" }),
            ("Winter's Breath Wand", 100, false, new[] { "Wand of Evocation" }),
            ("Wand of Evocation", 100, false, new[] { "Winter's Breath Wand" }),
        };

        private static (string Result, int Fame, bool Advanced, string[] Items)[] _all;
        private static readonly object Lock = new object();

        public static (string Result, int Fame, bool Advanced, string[] Items)[] All(GameServer server)
        {
            if (_all != null)
                return _all;
            lock (Lock)
                return _all ??= Build(server);
        }

        private static (string Result, int Fame, bool Advanced, string[] Items)[] Build(GameServer server)
        {
            var data = server.Resources.GameData;
            var items = data.Items.Values.Where(i => !i.Untradeable).GroupBy(i => i.ObjectId).ToDictionary(g => g.Key, g => g.First());
            var dropped = Dropped(server);
            var seasonal = new HashSet<string>(SeasonalThreshold.Items.Concat(Loot.WinterBossDrops.Select(d => d.Item)));
            bool Tiered(string id) => items.TryGetValue(id, out var i) && i.Tier >= 0;
            var forgeOnly = new HashSet<string>(ForgeOnly.Select(r => r.Result));
            bool Allowed((string Result, int Fame, bool Advanced, string[] Items) r) =>
                items.ContainsKey(r.Result) && (dropped.Contains(r.Result) || forgeOnly.Contains(r.Result))
                && !seasonal.Contains(r.Result) && r.Items.All(items.ContainsKey);

            var slots = new HashSet<int>(data.Classes.Values.Where(c => c.SlotTypes != null).SelectMany(c => c.SlotTypes.Take(4)));
            slots.Remove(0);
            var abilitySlots = new HashSet<int>(data.Classes.Values.Where(c => c.SlotTypes != null && c.SlotTypes.Length > 1)
                .Select(c => c.SlotTypes[1]));
            // each slot's items by tier, and the fame for each tier it can forge: the price scale runs
            // to the top of the unbroken chain from the lowest tier
            var chains = new Dictionary<int, (Dictionary<int, List<Item>> ByTier, Dictionary<int, int> Fame)>();
            var weaponSlots = new HashSet<int>(TierLoot.GetSlotTypes(ItemType.Weapon));
            foreach (var slot in slots)
            {
                var byTier = items.Values.Where(i => i.SlotType == slot && i.Tier >= 0)
                    .GroupBy(i => i.Tier).ToDictionary(g => g.Key, g => g.OrderBy(i => i.ObjectType).ToList());
                if (byTier.Count == 0)
                    continue;
                var top = byTier.Keys.Min();
                while (byTier.ContainsKey(top + 1))
                    top++;
                // the weapons' scale still ends at T14, as it did before their T15s: those cost the top price
                var scaleTop = weaponSlots.Contains(slot) ? Math.Min(top, 14) : top;
                var fames = new Dictionary<int, int>();
                foreach (var tier in byTier.Keys.Where(t => t <= top && byTier.ContainsKey(t - 1)))
                {
                    var scaled = (int)Math.Round(tier * 14.0 / Math.Max(scaleTop, 1));
                    fames[tier] = Math.Min(TierFame[Math.Clamp(scaled, 1, TierFame.Length - 1)], TieredCap);
                }
                chains[slot] = (byTier, fames);
            }

            // An ability's two of the tier below costs its tier's price on the scale, as every ability
            // kind (and ring) has the same eight tiers: the Doomed Realms list's own prices for them
            // went up and down from tier to tier.
            int? AbilityChainFame((string Result, int Fame, bool Advanced, string[] Items) r)
            {
                if (!items.TryGetValue(r.Result, out var res) || res.Tier < 1 || !abilitySlots.Contains(res.SlotType)
                    || r.Items.Length != 2 || r.Items[0] != r.Items[1] || !items.TryGetValue(r.Items[0], out var from)
                    || from.SlotType != res.SlotType || from.Tier != res.Tier - 1
                    || !chains.TryGetValue(res.SlotType, out var chain) || !chain.Fame.TryGetValue(res.Tier, out var fame))
                    return null;
                return fame;
            }

            var recipes = DoomedEconomyData.Recipes
                .Select(r => AbilityChainFame(r) is int fame ? (r.Result, fame, r.Advanced, r.Items)
                    : Tiered(r.Result) ? (r.Result, Math.Min(r.Fame, TieredCap), r.Advanced, r.Items) : r)
                .Concat(ForgeOnly)
                .ToList();
            var known = new HashSet<string>(recipes.Select(Key));

            foreach (var (slot, (byTier, fames)) in chains)
            {
                foreach (var (tier, results) in byTier)
                {
                    if (!fames.TryGetValue(tier, out var fame))
                        continue;
                    var below = byTier[tier - 1];
                    foreach (var result in results)
                    {
                        var from = Below(result, below);
                        var r = (result.ObjectId, fame, false, new[] { from.ObjectId, from.ObjectId });
                        if (known.Add(Key(r)))
                            recipes.Add(r);
                    }
                }
            }
            var left = recipes.Where(r => !Allowed(r)).Select(r => r.Result).Distinct().OrderBy(r => r).ToList();
            var all = recipes.Where(Allowed).ToArray();
            Log.Info($"Forge: {all.Length} recipes; left out {left.Count} results that are OP, seasonal or dropped by nothing: {string.Join(", ", left)}");
            return all;
        }

        // Every item some monster can drop: the named drops in its loot and every item of the tier
        // pools it rolls (Loot.GetItems).
        private static HashSet<string> Dropped(GameServer server)
        {
            var defs = new List<LootDef>();
            foreach (var def in server.BehaviorDb.Definitions.Values)
                if (def.Item2 != null)
                    foreach (var drop in def.Item2)
                        drop.Populate(defs);
            var dropped = new HashSet<string>();
            var pools = new Loot();
            foreach (var d in defs)
            {
                if (d.Item != null)
                    dropped.Add(d.Item);
                else if (pools.GetItems(d.ItemType, d.Tier) is { } pool)
                    dropped.UnionWith(pool.Select(i => i.ObjectId));
            }
            return dropped;
        }

        // The item a tier down that forges into `result`: the only one, or the one whose name ends
        // the same (rings by stat, a staff for a staff), else the oldest.
        private static Item Below(Item result, List<Item> below)
        {
            if (below.Count == 1)
                return below[0];
            var last = LastWord(result.ObjectId);
            return below.FirstOrDefault(i => LastWord(i.ObjectId) == last) ?? below[0];
        }

        private static string LastWord(string id) => id.Substring(id.LastIndexOf(' ') + 1);

        private static string Key((string Result, int Fame, bool Advanced, string[] Items) r) =>
            r.Result + "=" + string.Join("+", r.Items.OrderBy(i => i, StringComparer.Ordinal));
    }
}
