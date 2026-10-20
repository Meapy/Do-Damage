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
    // TieredCap fame, as the tier chains are there to save players trips, not to sink fame;
    // untiered results keep their Doomed Realms price. Built from the game data on first use, so
    // new tiered items get recipes without a change here.
    //
    // The forge only makes what players can find: a recipe's result must be tradeable (not an
    // <Admin/> or Item.OpItems item, like the Crown) and dropped by some monster (its loot, the
    // tier pools it rolls, or the bosses' seasonal drops), and its ingredients tradeable.
    internal static class ForgeRecipes
    {
        public const int TieredCap = 2000;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // The fame for a tiered result by tier, on the weapons' 0-14 scale (Doomed Realms' sword
        // chain: 10 for a Broad Sword up to 2000 for a Sword of Splendor).
        private static readonly int[] TierFame = { 0, 10, 30, 50, 70, 90, 100, 200, 300, 400, 500, 800, 1000, 2000, 2000 };

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
            bool Tiered(string id) => items.TryGetValue(id, out var i) && i.Tier >= 0;
            bool Allowed((string Result, int Fame, bool Advanced, string[] Items) r) =>
                items.ContainsKey(r.Result) && dropped.Contains(r.Result) && r.Items.All(items.ContainsKey);

            var recipes = DoomedEconomyData.Recipes
                .Select(r => Tiered(r.Result) ? (r.Result, Math.Min(r.Fame, TieredCap), r.Advanced, r.Items) : r)
                .ToList();
            var known = new HashSet<string>(recipes.Select(Key));

            var slots = new HashSet<int>(data.Classes.Values.Where(c => c.SlotTypes != null).SelectMany(c => c.SlotTypes.Take(4)));
            slots.Remove(0);
            foreach (var slot in slots)
            {
                var byTier = items.Values.Where(i => i.SlotType == slot && i.Tier >= 0)
                    .GroupBy(i => i.Tier).ToDictionary(g => g.Key, g => g.OrderBy(i => i.ObjectType).ToList());
                if (byTier.Count == 0)
                    continue;
                // the top of the unbroken chain from the lowest tier, for the price scale
                var top = byTier.Keys.Min();
                while (byTier.ContainsKey(top + 1))
                    top++;
                foreach (var (tier, results) in byTier)
                {
                    if (!byTier.TryGetValue(tier - 1, out var below) || tier > top)
                        continue;
                    var scaled = (int)Math.Round(tier * 14.0 / Math.Max(top, 1));
                    var fame = Math.Min(TierFame[Math.Clamp(scaled, 1, TierFame.Length - 1)], TieredCap);
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
            Log.Info($"Forge: {all.Length} recipes; left out {left.Count} results nothing drops or that are OP: {string.Join(", ", left)}");
            return all;
        }

        // Every item some monster can drop: the named drops in its loot, every item of the tier
        // pools it rolls (Loot.GetItems), and the bosses' seasonal drops.
        private static HashSet<string> Dropped(GameServer server)
        {
            var defs = new List<LootDef>();
            foreach (var def in server.BehaviorDb.Definitions.Values)
                if (def.Item2 != null)
                    foreach (var drop in def.Item2)
                        drop.Populate(defs);
            var dropped = new HashSet<string>(Loot.WinterBossDrops.Select(d => d.Item));
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
