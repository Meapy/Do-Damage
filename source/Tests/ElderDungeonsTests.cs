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
            { "Elder shtrs Loot Balloon Bridge", "Potion of Max Defense" },
            { "Elder shtrs Loot Balloon Mage", "Potion of Max Mana" },
            { "Elder shtrs Loot Balloon King", "Potion of Max Life" },
        };

        public static readonly TheoryData<string, string, double> DungeonKeys = new TheoryData<string, string, double>
        {
            { "Elder Dreadstump the Pirate King", "Elder Pirate Cave Key", 0.01 },
            { "Elder Active Sarcophagus", "Elder Tomb Key", 0.01 },
            { "Elder Arachna the Spider Queen", "Elder Spider Den Key", 0.01 },
            { "Elder Stheno the Snake Queen", "Elder Snake Pit Key", 0.1 },
            { "Elder Septavius the Ghost God", "Elder Undead Lair Key", 0.009 },
            { "Elder Limon the Sprite God", "Elder Sprite World Key", 0.1 },
            { "Elder Archdemon Malphas", "Elder Abyss Key", 0.01 },
            { "Elder lod Blue Loot Balloon", "Elder Draconis Key", 0.01 },
            { "Elder Thessal the Mermaid Goddess", "Elder Ocean Trench Key", 0.01 },
            { "Elder Lord Ruthven", "Elder Manor Key", 0.01 },
            { "Elder Mixcoatl the Masked God", "Elder Jungle Key", 0.01 },
            { "Elder Davy Jones", "Elder Davy Key", 0.009 },
            { "Elder Masked Party God", "Elder Beach Key", 0.01 },
            { "Elder lod Ivory Loot", "Elder Ivory Wyvern Key", 0.01 },
            { "Elder shtrs Loot Balloon King", "Elder Shatters Key", 0.02 },
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

        [Theory]
        [MemberData(nameof(DungeonKeys))]
        public void EveryElderDungeonDropsItsOwnKeyAtItsRegularRate(string boss, string key, double chance)
        {
            Assert.True(Data.Value.Loot.TryGetValue(boss, out var drops), $"{boss} has no loot");
            Assert.Contains(chance, drops.Where(d => d.Item == key).Select(d => d.Probabilty));
        }

        [Theory]
        [InlineData("Elder lod Blue Loot Balloon", "Elder Ivory Wyvern Key")]
        [InlineData("Elder lod Ivory Loot", "Elder Draconis Key")]
        public void ElderDungeonLootDoesNotIncludeAnotherDungeonsKey(string boss, string key)
        {
            Assert.True(Data.Value.Loot.TryGetValue(boss, out var drops), $"{boss} has no loot");
            Assert.DoesNotContain(drops, d => d.Item == key);
        }

        // Players can teleport to each other in every Elder dungeon, as in the regular ones
        [Fact]
        public void EveryElderDungeonAllowsTeleport()
        {
            var worlds = Data.Value.Data.AllWorlds.Where(w => w.IdName.Contains("Elder")).ToList();
            Assert.True(worlds.Count >= 15, $"only {worlds.Count} Elder worlds");
            Assert.All(worlds, w => Assert.True(w.AllowTeleport, $"{w.IdName} disables teleport"));
        }

        // The Shatters runs on hidden objects that watch each other by name (switches, gates, bridge
        // closers, timers, chest spawners): each needs its Elder copy and that copy its behaviour,
        // or the Elder Shatters' gates open at once and its bosses leave no loot.
        [Fact]
        public void EveryShattersObjectHasItsElderVersion()
        {
            var a = Data.Value;
            var data = a.Data;
            // (the Avatar of the Forgotten King's own, in the realm, have no Elder version)
            var avatars = new[] { "shtrs Defense System", "shtrs shadowmans", "shtrs eyeswarmer", "shtrs Pillar 1", "shtrs Pillar 2", "shtrs Pillar 3", "shtrs Pillar 4" };
            var regular = data.ObjectDescs.Values.Where(d => (d.IdName.StartsWith("shtrs ") || d.IdName == "Tooky Shatters Master")
                && a.Defined.Contains(d.ObjectType) && !avatars.Contains(d.IdName)).ToList();
            Assert.True(regular.Count >= 60, $"only {regular.Count} Shatters objects with a behaviour");
            foreach (var d in regular)
            {
                Assert.True(data.IdToObjectType.TryGetValue("Elder " + d.IdName, out var elder), $"{d.IdName} has no Elder version");
                Assert.True(a.Defined.Contains(elder), $"Elder {d.IdName} has no behaviour");
            }
            foreach (var ground in new[] { "shtrs Bridge", "shtrs Pure Evil", "shtrs Shattered Floor", "shtrs Disaster Floor", "Dark Cobblestone", "Hot Lava" })
                Assert.True(data.IdToTileType.ContainsKey("Elder " + ground), $"no Elder {ground}");
            Assert.True(data.IdToTileType.ContainsKey("Elder shtrs Bridge Lit 1"));

            // the three bosses are quest monsters (the arrow, their health at the top of the screen),
            // and so is the Cursed Crown that turns into the King: without it no quest monster is
            // left once the Archmage falls, and the dungeon would count as cleared there
            foreach (var id in new[] { "shtrs Bridge Sentinel", "shtrs Twilight Archmage", "shtrs The Forgotten King", "shtrs The Cursed Crown" })
            {
                Assert.True(data.ObjectDescs[data.IdToObjectType[id]].Quest, $"{id} is no quest");
                Assert.True(data.ObjectDescs[data.IdToObjectType["Elder " + id]].Quest, $"Elder {id} is no quest");
                if (!id.EndsWith("Crown"))
                    Assert.True(data.ObjectDescs[data.IdToObjectType["Empowered Elder " + id]].Quest, $"Empowered Elder {id} is no quest");
            }

            var portal = data.Portals.Values.Single(p => p.IdName == "Elder Shatters Portal");
            Assert.Equal("The Elder Shatters", portal.DungeonName);
            Assert.Contains(data.AllWorlds, w => w.IdName == "The Elder Shatters");
            Assert.Contains(data.Items.Values.Single(i => i.ObjectId == "Elder Shatters Key").ActivateEffects, e => e.Id == "Elder Shatters Portal");
        }

        // The Elder Shatters' bosses carry no loot themselves (a chest spawner leaves their balloon):
        // raised by a Necromantic Relic, with the spawner gone, the Empowered form drops the
        // balloon's loot and its own item.
        [Theory]
        [InlineData("Elder shtrs Bridge Sentinel", "Potion of Max Defense", "Snare of the Bridge Sentinel", "Bracer of the Elder Guardian")]
        [InlineData("Elder shtrs Twilight Archmage", "Potion of Max Mana", "Twilight Cataclysm", "The Eclipsed Gemstone")]
        [InlineData("Elder shtrs The Forgotten King", "Potion of Max Life", "Shroud of the Forgotten King", "The Remembered Crown")]
        public void TheShattersEmpoweredBossesDropTheirBalloonsLoot(string boss, string potion, string item, string ring)
        {
            var a = Data.Value;
            Assert.True(a.Data.IdToObjectType.ContainsKey(boss + " Remains"), $"{boss} leaves no remains");
            Assert.True(a.Loot.TryGetValue("Empowered " + boss, out var drops), $"Empowered {boss} has no loot");
            foreach (var id in new[] { potion, item, ring })
                Assert.Contains(drops, d => d.Item == id);
            Assert.Contains(drops, d => d.Item == null && d.Tier >= 13);
            foreach (var id in new[] { item, ring })
                Assert.False(string.IsNullOrEmpty(a.Data.Items.Values.Single(i => i.ObjectId == id).RelicPower), $"{id} has no power");
        }

        // Each Elder Shatters boss has a phase the regular one doesn't. The King's replaces his death
        // at a tenth of his health: the transition into it sits on his root state and fires every
        // tick from then on, so the phase must be one state without children (switching to the
        // state he is in does nothing; with children it would restart them every tick) that ends
        // in his death by itself.
        [Theory]
        [InlineData("shtrs Bridge Sentinel", "Judgement")]
        [InlineData("shtrs Twilight Archmage", "Eclipse")]
        [InlineData("shtrs The Forgotten King", "Remembered")]
        public void TheElderShattersBossesHaveTheirOwnPhase(string boss, string phase)
        {
            var a = Data.Value;
            State Root(string id) => a.States[a.Data.IdToObjectType[id]];
            static IEnumerable<State> All(State s) => new[] { s }.Concat(s.States.SelectMany(All));
            static IEnumerable<string> Targets(State s) => s.Transitions.SelectMany(t => t.TargetState).Select(t => t.Name);

            var regular = Root(boss);
            var elder = Root("Elder " + boss);
            Assert.DoesNotContain(phase, All(regular).SelectMany(Targets));
            Assert.Contains(phase, All(elder).SelectMany(Targets));
            var state = All(elder).Single(s => s.Name == phase);
            Assert.Empty(state.States);
            Assert.NotEmpty(state.Transitions);     // it leads on: never a dead end

            if (phase == "Remembered")
            {
                Assert.Equal(new[] { "Death" }, Targets(regular));
                Assert.Equal(new[] { "Remembered" }, Targets(elder));
                Assert.Equal(new[] { "Suicide" }, Targets(state));
            }
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
            public readonly Dictionary<ushort, State> States = new Dictionary<ushort, State>();
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
                    States[type] = def.Item1;
                    if (def.Item2 == null)
                        continue;
                    var defs = def.Item2.SelectMany(d => (IList<LootDef>)field.GetValue(d)).ToList();
                    Loot[res.GameData.ObjectTypeToId[type]] = defs;
                }
            }
        }
    }
}
