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
            { "Elder LH Marble Colossus", "Potion of Max Defense" },
            { "Elder LH Malus 2", "Potion of Max Attack" },
            { "Elder LH Void Entity", "Potion of Max Life" },
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
            { "Elder LH Marble Colossus", "Elder Lost Halls Key", 0.01 },
            { "Elder LH Malus 2", "Elder Lost Halls Key", 0.01 },
            { "Elder LH Void Entity", "Elder Lost Halls Key", 0.02 },
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
            Assert.True(worlds.Count >= 18, $"only {worlds.Count} Elder worlds");
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

        // The Elder Lost Halls, and the Elder Cultist Hideout and Elder Void it leads to: every object
        // of the three whose behaviour the regular dungeons define has its Elder copy with a
        // behaviour (the Colossus room's controller, the pillar summoners, the rocks, the Void's
        // controller), or the Elder Colossus would never rise, or rise without its fight.
        [Fact]
        public void EveryLostHallsObjectHasItsElderVersion()
        {
            var a = Data.Value;
            var data = a.Data;
            // (the Lost Sentry is the realm's, where the Lost Halls' portal comes from)
            var regular = data.ObjectDescs.Values.Where(d => d.IdName.StartsWith("LH ") && a.Defined.Contains(d.ObjectType)
                && !d.IdName.StartsWith("LH Lost Sentry")).ToList();
            Assert.True(regular.Count >= 95, $"only {regular.Count} Lost Halls objects with a behaviour");
            foreach (var d in regular)
            {
                Assert.True(data.IdToObjectType.TryGetValue("Elder " + d.IdName, out var elder), $"{d.IdName} has no Elder version");
                Assert.True(a.Defined.Contains(elder), $"Elder {d.IdName} has no behaviour");
            }
            foreach (var ground in new[] { "LH Main Tile", "LH Hall Tile", "LH Cult Main Tile", "LH Void Platform" })
                Assert.True(data.IdToTileType.ContainsKey("Elder " + ground), $"no Elder {ground}");

            // the way in (a portal the three regular bosses leave half the time, and a key), and
            // the ways on from the Elder Colossus room
            foreach (var (portal, world) in new[] { ("Elder Lost Halls Portal", "Elder Lost Halls"), ("Elder LH Cult Portal", "Elder Cultist Hideout"),
                ("Elder LH Void Portal", "The Elder Void") })
            {
                Assert.Equal(world, data.Portals.Values.Single(p => p.IdName == portal).DungeonName);
                Assert.Contains(data.AllWorlds, w => w.IdName == world);
            }
            Assert.Contains(data.Items.Values.Single(i => i.ObjectId == "Elder Lost Halls Key").ActivateEffects, e => e.Id == "Elder Lost Halls Portal");
            foreach (var boss in new[] { "LH Marble Colossus", "LH Malus 2", "LH Void Entity" })
            {
                bool Leads(string id) => All(a.States[data.IdToObjectType[id]]).SelectMany(s => s.Behaviors)
                    .Any(b => b.GetType().Name == "DropPortalOnDeath" && Field<ushort>(b, "_target") == data.IdToObjectType["Elder Lost Halls Portal"]
                        && Field<float>(b, "_probability") > 0);
                Assert.True(Leads(boss), $"{boss} doesn't lead to the Elder Lost Halls");
                Assert.False(Leads("Elder " + boss), $"Elder {boss} leads back into the Elder Lost Halls");
            }

            // the Elder controller raises the Elder Colossus or opens the Elder hideout, and leaves
            // the Elder Void's mark, which the Vial of Pure Darkness turns into the Elder Void's
            // portal (Player.UseItem.cs, ChangeObjectTarget)
            var spawns = All(a.States[data.IdToObjectType["Elder LH Monster Spawner"]]).SelectMany(s => s.Behaviors)
                .Where(b => b.GetType().Name == "Spawn").Select(b => data.ObjectTypeToId[Field<ushort>(b, "_children")]).ToList();
            Assert.Equal(new[] { "Elder LH Cult Portal", "Elder LH Marble Colossus", "Elder LH Vial Effect" }, spawns.OrderBy(s => s));
            var vial = data.Items.Values.Single(i => i.ObjectId == "Vial of Pure Darkness").ActivateEffects.Single(e => e.Effect == ActivateEffects.ChangeObject);
            Assert.Equal("Lost Halls", vial.OnlyInArea);
            Assert.True(data.IdToObjectType.ContainsKey("Elder " + vial.TargetId) && data.IdToObjectType.ContainsKey("Elder " + vial.NewId));
        }

        private static IEnumerable<State> All(State s) => new[] { s }.Concat(s.States.SelectMany(All));

        private static T Field<T>(object o, string name)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null)
                    return (T)Convert.ChangeType(f.GetValue(o), typeof(T));
            }
            throw new MissingFieldException(o.GetType().Name, name);
        }

        // The Elder Lost Halls is laid out anew each visit, as the regular one: every layout builds
        // and its Colossus room (the Elder controller) is there.
        [Fact]
        public void ElderLostHallsLayoutsHoldTheColossusRoom()
        {
            var data = Data.Value.Data;
            var world = data.GetWorld("Elder Lost Halls");
            Assert.NotNull(world.RandomLayout);
            var map = world.MapJM[0];
            var source = data.GetWorldData(map);
            for (var seed = 0; seed < 20; seed++)
            {
                var layout = Shared.terrain.DungeonGenerator.Generate(data, map, source, world.RandomLayout, new Random(seed));
                Assert.True(layout != null, $"seed {seed}: no layout");
                var tiles = Shared.terrain.DungeonGenerator.Import(layout);
                var objs = tiles.Cast<Shared.terrain.TerrainTile>().Select(t => t.TileObj).Where(o => !string.IsNullOrEmpty(o)).ToList();
                Assert.Single(objs.Where(o => o == "Elder LH Monster Spawner"));
                Assert.Equal(4, objs.Count(o => o.StartsWith("Elder LH Marble Defender Activator")));
                Assert.Contains("Elder Brazier", objs);
                Assert.DoesNotContain(objs, o => data.IdToObjectType.TryGetValue(o, out var t) && data.ObjectDescs[t].Enemy && !o.StartsWith("Elder "));
                Assert.Contains(tiles.Cast<Shared.terrain.TerrainTile>(), t => t.Region == TileRegion.Spawn);
            }
        }

        // Raised by a Necromantic Relic, each of the three drops its Elder loot again and an item
        // of its own, and each of the six items has its power.
        [Theory]
        [InlineData("Elder LH Marble Colossus", "Potion of Max Defense", "Seal of the Risen Colossus", "Lodestone of the Halls")]
        [InlineData("Elder LH Malus 2", "Potion of Max Attack", "Skull of the Final Ritual", "Ring of the Blood Pact")]
        [InlineData("Elder LH Void Entity", "Potion of Max Life", "Bow of the Event Horizon", "The Hollow Sourcestone")]
        public void TheLostHallsBossesCanBeRaised(string boss, string potion, string item, string ring)
        {
            var a = Data.Value;
            Assert.True(a.Data.IdToObjectType.ContainsKey(boss + " Remains"), $"{boss} leaves no remains");
            Assert.True(a.Loot.TryGetValue("Empowered " + boss, out var drops), $"Empowered {boss} has no loot");
            foreach (var id in new[] { potion, item, ring })
                Assert.Contains(drops, d => d.Item == id);
            Assert.Contains(drops, d => d.Item == null && d.Tier >= 14);
            foreach (var id in new[] { item, ring })
                Assert.False(string.IsNullOrEmpty(a.Data.Items.Values.Single(i => i.ObjectId == id).RelicPower), $"{id} has no power");
            // the regular boss drops neither
            Assert.DoesNotContain(a.Loot[boss.Substring("Elder ".Length)], d => d.Item == ring || d.Item == potion || d.Item == "Elder Lost Halls Key");
        }

        // Each of the three has a phase the regular one doesn't, entered from a state the regular
        // one has too. The Colossus's and Malus' lead back into the fight; the Void Entity's is its
        // last.
        [Theory]
        [InlineData("LH Marble Colossus", "Reckoning", "C-3")]
        [InlineData("LH Malus 2", "Pentagram", "Argus")]
        [InlineData("LH Void Entity", "Singularity", null)]
        public void TheElderLostHallsBossesHaveTheirOwnPhase(string boss, string phase, string next)
        {
            var a = Data.Value;
            static IEnumerable<string> Targets(State s) => s.Transitions.SelectMany(t => t.TargetState).Select(t => t.Name);
            var regular = a.States[a.Data.IdToObjectType[boss]];
            var elder = a.States[a.Data.IdToObjectType["Elder " + boss]];
            // (the regular one holds the state too, with nothing leading to it)
            Assert.DoesNotContain(phase, All(regular).SelectMany(Targets));
            Assert.Contains(phase, All(elder).SelectMany(Targets));
            var state = All(elder).Single(s => s.Name == phase);
            Assert.Empty(state.States);
            Assert.Equal(next == null ? Array.Empty<string>() : new[] { next }, Targets(state));
            // the Empowered form fights as the Elder one does
            Assert.Same(elder, a.States[a.Data.IdToObjectType["Empowered Elder " + boss]]);
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
