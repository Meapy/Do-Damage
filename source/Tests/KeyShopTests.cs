using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Shared.resources;
using Xunit;

namespace Tests
{
    // /shop keys (MerchantLists.KeyShopPrices): every key it sells opens a dungeon that has a world,
    // and it sells the regular and Elder keys but none of the Oryx chain's.
    public class KeyShopTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private static List<(string Key, int Price)> Keys()
        {
            var type = typeof(WorldServer.core.GameServer).Assembly.GetType("WorldServer.core.objects.vendors.MerchantLists");
            var prop = type.GetProperty("KeyShopPrices", BindingFlags.Public | BindingFlags.Static);
            return ((IEnumerable<(string, int)>)prop.GetValue(null)).ToList();
        }

        [Fact]
        public void EveryKeyOpensADungeonWithAWorld()
        {
            var data = Res.Value.GameData;
            var portals = data.Portals.Values.GroupBy(p => p.IdName).ToDictionary(g => g.Key, g => g.First());
            var problems = new List<string>();
            foreach (var (key, price) in Keys())
            {
                if (price <= 0 || price > ushort.MaxValue)
                    problems.Add($"{key}: price {price}");
                if (!data.IdToObjectType.TryGetValue(key, out var t) || !data.Items.TryGetValue(t, out var item))
                {
                    problems.Add($"{key}: no such item");
                    continue;
                }
                // a key creates its portal; an incantation turns a locked portal into the open one
                var portal = item.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.CreatePortal)?.Id
                    ?? item.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.ChangeObject)?.NewId;
                if (portal == null || !portals.TryGetValue(portal, out var desc))
                    problems.Add($"{key}: opens no portal ({portal})");
                else if (data.GetWorld(desc.DungeonName) == null)
                    problems.Add($"{key}: {portal} leads to no world ({desc.DungeonName})");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void SellsTheElderKeysButNotTheOryxChain()
        {
            var keys = Keys().Select(k => k.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            foreach (var elder in new[] { "Elder Pirate Cave Key", "Elder Tomb Key", "Elder Draconis Key",
                         "Elder Ivory Wyvern Key", "Elder Shatters Key", "Elder Lost Halls Key" })
                Assert.Contains(elder, keys);
            foreach (var regular in new[] { "Pirate Cave Key", "Lost Halls Key", "Shatters Key", "Doom Crystal Key" })
                Assert.Contains(regular, keys);
            foreach (var oryx in new[] { "Oryx's Castle Key", "Oryx Chamber Key", "Oryx Chicken Chamber Key",
                         "Wine Cellar Incantation", "Christmas Key" })
                Assert.DoesNotContain(oryx, keys);
            // the Elder dungeons that have a key, all sold
            var data = Res.Value.GameData;
            var elderKeys = data.Items.Values.Select(i => i.ObjectId)
                .Where(id => id.StartsWith("Elder ") && id.EndsWith(" Key")).Distinct().ToList();
            Assert.Empty(elderKeys.Except(keys));
        }
    }
}
