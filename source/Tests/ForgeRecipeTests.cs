using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Shared.resources;
using WorldServer.core;
using WorldServer.logic;
using WorldServer.logic.loot;
using Xunit;

namespace Tests
{
    // The Divine Forge's recipes as the server builds them from the real game data
    // (ForgeRecipes.All): the loot pools and the behaviours read as GameServer starts them.
    [Collection("BehaviorDb")]
    public class ForgeRecipeTests
    {
        private static readonly Lazy<(Resources Res, (string Result, int Fame, bool Advanced, string[] Items)[] All)> Data =
            new Lazy<(Resources, (string, int, bool, string[])[])>(() =>
            {
                var res = new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false);
                var server = (GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
                typeof(GameServer).GetProperty(nameof(GameServer.Resources)).SetValue(server, res);
                var db = new BehaviorDb(server);
                typeof(GameServer).GetProperty(nameof(GameServer.BehaviorDb)).SetValue(server, db);
                Loot.Initialize(server);
                db.Initialize();
                // internal to the world server: called by name
                var forge = typeof(GameServer).Assembly.GetType("WorldServer.core.commands.doomed.ForgeRecipes");
                var all = ((string, int, bool, string[])[])forge.GetMethod("All").Invoke(null, new object[] { server });
                return (res, all);
            });

        // the Enthroned Boots: any ring that isn't one of Oryx 5's own, and two Ascended Accessory
        // Shards (2026-11-06; it was a tier 8 ring and one shard)
        [Fact]
        public void EnthronedBootsTakeAnyRingAndTwoShards()
        {
            var (res, all) = Data.Value;
            var items = res.GameData.Items.Values.GroupBy(i => i.ObjectId).ToDictionary(g => g.Key, g => g.First());
            var boots = all.Where(r => r.Result == "Enthroned Boots").ToList();
            Assert.True(boots.Count > 10, $"only {boots.Count} recipes");
            foreach (var r in boots)
            {
                Assert.Equal(3, r.Items.Length);
                Assert.Equal(2, r.Items.Count(i => i == "Ascended Accessory Shard"));
                var ring = items[r.Items.Single(i => i != "Ascended Accessory Shard")];
                Assert.Equal(9, ring.SlotType);
                Assert.False(ring.Ascended, ring.ObjectId);
            }
            var tiers = boots.Select(r => items[r.Items[0]].Tier).Distinct().ToList();
            Assert.Contains(8, tiers);
            Assert.True(tiers.Count > 3, "rings of many tiers: " + string.Join(", ", tiers));
            Assert.Equal(50, items["Enthroned Boots"].ActivateOnEquips.Single(b => b.Key == 22).Value);
        }

        // a look swap (Sun's Judgement into a Doom Bow, the Staff of Iceblast into the Staff of
        // Extreme Prejudice) doesn't count for quests or the community goal; forging the same item
        // from other items, or anything else, still does
        [Fact]
        public void LookSwapsAreReskins()
        {
            var (_, all) = Data.Value;
            var forge = typeof(GameServer).Assembly.GetType("WorldServer.core.commands.doomed.ForgeRecipes");
            var isReskin = forge.GetMethod("IsReskin");
            bool Reskin((string, int, bool, string[]) r) => (bool)isReskin.Invoke(null, new object[] { r });

            var swaps = all.Where(Reskin).ToList();
            Assert.Contains(swaps, r => r.Result == "Doom Bow" && r.Items.SequenceEqual(new[] { "Sun's Judgement" }));
            Assert.Contains(swaps, r => r.Result == "Staff of Extreme Prejudice" && r.Items.SequenceEqual(new[] { "Staff of Iceblast" }));
            Assert.Contains(swaps, r => r.Result == "Sun's Judgement");
            Assert.All(swaps, r => Assert.Single(r.Items));

            Assert.Contains(all, r => r.Result == "Doom Bow" && r.Items.Length == 2 && !Reskin(r));
            Assert.Contains(all, r => r.Result == "Reforged Doom Bow" && !Reskin(r));
            Assert.Contains(all, r => r.Result == "Infernal Potion of Life" && !Reskin(r));
        }

        // the other Ascended items still take one shard and an item of their tier
        [Fact]
        public void OtherAscendedItemsTakeOneShard()
        {
            var (_, all) = Data.Value;
            var sword = all.Where(r => r.Result == "Enthroned Sword").ToList();
            Assert.NotEmpty(sword);
            Assert.All(sword, r => Assert.Equal(new[] { "Ascended Weapon Shard" }, r.Items.Skip(1)));
        }
    }
}
