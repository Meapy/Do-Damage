using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;

namespace WorldServer.core.commands.doomed
{
    // Every recipe the Divine Forge knows: Doomed Realms' (DoomedEconomyData.Recipes), plus two of
    // each tiered item forging into the next tier up, for every slot type a class wears (rings by
    // their stat: two Rings of Attack make a Ring of Greater Attack). Tiered results cost at most
    // TieredCap fame, as the tier chains are there to save players trips, not to sink fame;
    // untiered results keep their Doomed Realms price. Built from the game data on first use, so
    // new tiered items get recipes without a change here.
    internal static class ForgeRecipes
    {
        public const int TieredCap = 2000;

        // The fame for a tiered result by tier, on the weapons' 0-14 scale (Doomed Realms' sword
        // chain: 10 for a Broad Sword up to 2000 for a Sword of Splendor).
        private static readonly int[] TierFame = { 0, 10, 30, 50, 70, 90, 100, 200, 300, 400, 500, 800, 1000, 2000, 2000 };

        private static (string Result, int Fame, bool Advanced, string[] Items)[] _all;
        private static readonly object Lock = new object();

        public static (string Result, int Fame, bool Advanced, string[] Items)[] All(XmlData data)
        {
            if (_all != null)
                return _all;
            lock (Lock)
                return _all ??= Build(data);
        }

        private static (string Result, int Fame, bool Advanced, string[] Items)[] Build(XmlData data)
        {
            var items = data.Items.Values.Where(i => !i.Untradeable).GroupBy(i => i.ObjectId).ToDictionary(g => g.Key, g => g.First());
            bool Tiered(string id) => items.TryGetValue(id, out var i) && i.Tier >= 0;

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
            return recipes.ToArray();
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
