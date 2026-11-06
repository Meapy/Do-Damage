using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Shared.resources;
using WorldServer.core;
using WorldServer.logic;
using WorldServer.logic.loot;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // The Elder dungeons' loot, read from the real behaviours (BehaviorDb, the Doomed Realms ones too).
    [Collection("BehaviorDb")]
    public class ElderDungeonsTests
    {
        private static readonly Lazy<Audit> Data = new Lazy<Audit>(() => new Audit());
        private readonly ITestOutputHelper _out;

        public ElderDungeonsTests(ITestOutputHelper output) => _out = output;

        // Each Elder dungeon's boss (or what it leaves, a balloon or coffin) and its Potion of Max.
        public static readonly TheoryData<string, string> MaxPotions = new TheoryData<string, string>
        {
            { "Elder Tomb Attacker", "Potion of Max Life" },
            { "Elder Tomb Defender", "Potion of Max Life" },
            { "Elder Tomb Support", "Potion of Max Life" },
            { "Elder Dreadstump the Pirate King", "Potion of Max Attack" },
            { "Elder Arachna the Spider Queen", "Potion of Max Defense" },
            { "Elder Stheno the Snake Queen", "Potion of Max Speed" },
            { "Elder Septavius the Ghost God", "Potion of Max Wisdom" },
            { "Elder Archdemon Malphas", "Potion of Max Vitality" },
            { "Elder Thessal the Mermaid Goddess", "Potion of Max Mana" },
            { "Elder Masked Party God", "Potion of Max Speed" },
            { "Elder Mixcoatl the Masked God", "Potion of Max Dexterity" },
            { "Elder Coffin", "Potion of Max Attack" },
            { "Elder Limon the Sprite God", "Potion of Max Dexterity" },
            { "Elder Davy Jones", "Potion of Max Wisdom" },
            { "Elder lod Blue Loot Balloon", "Potion of Max Wisdom" },
            { "Elder lod Red Loot Balloon", "Potion of Max Vitality" },
            { "Elder lod Black Loot Balloon", "Potion of Max Dexterity" },
            { "Elder lod Green Loot Balloon", "Potion of Max Speed" },
            { "Elder lod Ivory Loot", "Potion of Max Life" },
            { "Elder lod Ivory Loot", "Potion of Max Mana" },
        };

        // A rare drop: there, but not most kills (Doomed Realms gave some bosses one nearly every kill)
        [Theory]
        [MemberData(nameof(MaxPotions))]
        public void EveryElderBossDropsItsPotionOfMax(string boss, string potion)
        {
            Assert.True(Data.Value.Loot.TryGetValue(boss, out var drops), $"{boss} has no loot");
            var chances = drops.Where(d => d.Item == potion).Select(d => d.Probabilty).ToList();
            Assert.True(chances.Count > 0, $"{boss} doesn't drop a {potion}");
            Assert.All(chances, c => Assert.InRange(c, 0.005, 0.1));
        }

        // Players can teleport to each other in every Elder dungeon, as in the regular ones
        [Fact]
        public void EveryElderDungeonAllowsTeleport()
        {
            var worlds = Data.Value.Data.AllWorlds.Where(w => w.IdName.Contains("Elder")).ToList();
            Assert.True(worlds.Count >= 14, $"only {worlds.Count} Elder worlds");
            Assert.All(worlds, w => Assert.True(w.AllowTeleport, $"{w.IdName} disables teleport"));
        }

        // Not a check: every Elder monster's drops, for reading over (dotnet test --filter
        // ListElderLoot --logger "console;verbosity=detailed").
        [Fact]
        public void ListElderLoot()
        {
            foreach (var (monster, drops) in Data.Value.Loot.Where(m => m.Key.StartsWith("Elder ")).OrderBy(m => m.Key))
                _out.WriteLine($"{monster}: {string.Join("; ", drops.Select(d => d.Item != null ? $"{d.Item} {d.Probabilty} (t{d.Threshold})" : $"T{d.Tier} {d.ItemType} {d.Probabilty}"))}");
        }

        // Not a check: Elder monsters with no behaviour, and the Elder portals and keys.
        [Fact]
        public void ListElderAudit()
        {
            var a = Data.Value;
            var data = a.Data;
            foreach (var d in data.ObjectDescs.Values.Where(d => d.IdName.Contains("Elder") && d.Enemy && !a.Defined.Contains(d.ObjectType)).OrderBy(d => d.IdName))
                _out.WriteLine($"no behaviour: {d.IdName}");
            foreach (var p in data.Portals.Values.Where(p => p.IdName.Contains("Elder")).OrderBy(p => p.IdName))
                _out.WriteLine($"portal: {p.IdName} -> {p.DungeonName} {(data.AllWorlds.Any(w => w.IdName == p.DungeonName) ? "" : "MISSING WORLD")}");
            foreach (var i in data.Items.Values.Where(i => i.ObjectId.Contains("Elder") && i.ObjectId.Contains("Key")).OrderBy(i => i.ObjectId))
                _out.WriteLine($"key: {i.ObjectId}: {string.Join(", ", i.ActivateEffects.Select(e => $"{e.Effect} {e.Id} {e.ObjectId} {e.DungeonName}"))}");
            foreach (var w in data.AllWorlds.Where(w => w.IdName.Contains("Elder")).OrderBy(w => w.IdName))
                _out.WriteLine($"world: {w.IdName} teleport={w.AllowTeleport} maps={string.Join(",", w.MapJM)}");
        }

        private sealed class Audit
        {
            public readonly Dictionary<string, List<LootDef>> Loot = new Dictionary<string, List<LootDef>>();
            public readonly HashSet<ushort> Defined = new HashSet<ushort>();
            public XmlData Data;

            public Audit()
            {
                var resources = Path.Combine(AppContext.BaseDirectory, "resources");
                var res = new Resources(resources, true, false);
                var server = (GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
                typeof(GameServer).GetProperty(nameof(GameServer.Resources)).SetValue(server, res);
                var db = new BehaviorDb(server);
                db.Initialize();
                Data = res.GameData;
                Defined.UnionWith(db.Definitions.Keys);

                var field = typeof(MobDrops).GetField("LootDefs", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var (type, def) in db.Definitions)
                {
                    if (def.Item2 == null)
                        continue;
                    var defs = def.Item2.SelectMany(d => (IList<LootDef>)field.GetValue(d)).ToList();
                    Loot[res.GameData.ObjectTypeToId[type]] = defs;
                }
            }
        }
    }
}
