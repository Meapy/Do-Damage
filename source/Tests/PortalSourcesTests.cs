using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using Shared.resources;
using WorldServer.core;
using WorldServer.logic;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // Every portal a monster can leave, read from the real behaviours (BehaviorDb, the Doomed Realms
    // ones too) and the set pieces they place: a portal that two of them place at the same time
    // (a DropPortalOnDeath beside a death set piece that holds the same portal, say) shows up twice
    // in the game. Sources in states the monster can't be in together don't count as a pair.
    public class PortalSourcesTests
    {
        private static readonly Lazy<Audit> Data = new Lazy<Audit>(() => new Audit());
        private readonly ITestOutputHelper _out;

        public PortalSourcesTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void NoMonsterPlacesTheSamePortalTwice()
        {
            var twice = Data.Value.Monsters
                .SelectMany(m => m.Value.GroupBy(s => s.Portal)
                    .Where(g => Clash(g.ToList()))
                    .Select(g => $"{m.Key}: {g.Key} from {string.Join(" and ", g.Select(s => s.Describe()))}"))
                .ToList();
            Assert.True(twice.Count == 0, "Portals placed twice:\n" + string.Join("\n", twice));
        }

        // Not a check: every monster's portals, for reading over (dotnet test --logger
        // "console;verbosity=detailed").
        [Fact]
        public void ListMonsterPortals()
        {
            foreach (var (monster, sources) in Data.Value.Monsters.OrderBy(m => m.Key))
                _out.WriteLine($"{monster}: {string.Join("; ", sources.Select(s => $"{s.Portal} ({s.Describe()})"))}");
        }

        [Fact]
        public void TheBossesThatLeftTwoLeaveOne()
        {
            var monsters = Data.Value.Monsters;
            Assert.Equal(new[] { "Davy Jones' Locker Portal" }, monsters["Ghost Ship"].Select(s => s.Portal));
            Assert.Equal(new[] { "Realm Portal" }, monsters["CR Royal Cnidarian"].Select(s => s.Portal));
            // one loot chest, whose Encore portal opens once the chest is opened
            Assert.Single(monsters["The Puppet Master"], s => s.Portal == "Puppet Encore Portal");
            // Oryx 1, the Giant Oryx Chicken and the Christmas Oryx open the Wine Cellar, Oryx 2 the Sanctuary
            foreach (var oryx in new[] { "Oryx the Mad God 1", "TestChicken 2", "Christmas Oryx" })
                Assert.Single(monsters[oryx], s => s.Portal.Contains("Wine Cellar"));
            Assert.Single(monsters["Oryx the Mad God 2"], s => s.Portal.Contains("Sanctuary"));
            Assert.Single(monsters["Oryx the Mad God 2"], s => s.Portal == "Realm Portal");
        }

        // two sources of one portal both fire: both on death in states the monster is in together
        // (one holds the other), or either placed during the fight
        private static bool Clash(List<Source> sources)
        {
            for (var i = 0; i < sources.Count; i++)
                for (var j = i + 1; j < sources.Count; j++)
                {
                    var (a, b) = (sources[i], sources[j]);
                    if (!a.OnDeath || !b.OnDeath || Holds(a.State, b.State) || Holds(b.State, a.State))
                        return true;
                }
            return false;
        }

        private static bool Holds(State outer, State inner)
        {
            for (var s = inner; s != null; s = s.Parent)
                if (s == outer)
                    return true;
            return false;
        }

        private sealed class Source
        {
            public string Portal;
            public string Via;         // the behaviour, and the set piece it came through
            public bool OnDeath;
            public State State;        // the state holding the behaviour

            public string Describe() => $"{Via}{(OnDeath ? " on death" : " during the fight")}"
                + (string.IsNullOrEmpty(State?.Name) ? "" : $" in state {State.Name}");
        }

        private sealed class Audit
        {
            public readonly Dictionary<string, List<Source>> Monsters = new Dictionary<string, List<Source>>();
            private readonly XmlData _data;
            private readonly string _worlds;
            private readonly Dictionary<ushort, (List<Source>, List<(ushort, string, State)>)> _direct = new Dictionary<ushort, (List<Source>, List<(ushort, string, State)>)>();
            private readonly Dictionary<string, List<string>> _mapPortals = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public Audit()
            {
                var resources = Path.Combine(AppContext.BaseDirectory, "resources");
                _worlds = Path.Combine(resources, "worlds");
                var res = new Resources(resources, true, false);
                _data = res.GameData;

                // BehaviorDb reads the game data through its GameServer; nothing else is needed
                var server = (GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
                typeof(GameServer).GetProperty(nameof(GameServer.Resources)).SetValue(server, res);
                var db = new BehaviorDb(server);
                db.Initialize();

                foreach (var (type, def) in db.Definitions)
                {
                    var found = new List<Source>();
                    var children = new List<(ushort, string, State)>();
                    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    Walk(def.Item1, null, false, found, children, seen);
                    if (def.Item2 != null)
                        Walk(def.Item2, null, true, found, children, seen);
                    _direct[type] = (found, children.Where(c => db.Definitions.ContainsKey(c.Item1) && c.Item1 != type).ToList());
                }
                foreach (var type in _direct.Keys)
                {
                    var all = WithChildren(type, new HashSet<ushort>());
                    if (all.Count > 0)
                        Monsters[_data.ObjectTypeToId[type]] = all;
                }
            }

            // a monster's portals and those of what it turns into or throws out on death (the Haunted
            // Cemetery's controllers turn into portals, a boss can turn into a chest that drops one),
            // the latter counted as on death in the state that holds the behaviour
            private List<Source> WithChildren(ushort type, HashSet<ushort> path)
            {
                var (found, children) = _direct[type];
                var all = new List<Source>(found);
                if (!path.Add(type))
                    return all;
                foreach (var (child, via, state) in children)
                    foreach (var s in WithChildren(child, path))
                        all.Add(new Source { Portal = s.Portal, Via = $"{via} into {_data.ObjectTypeToId[child]}, whose {s.Describe()},", OnDeath = true, State = state });
                path.Remove(type);
                return all;
            }

            private void Walk(object node, State state, bool onDeath, List<Source> found, List<(ushort, string, State)> children, HashSet<object> seen)
            {
                if (node == null || node is string || node.GetType().IsPrimitive || !seen.Add(node))
                    return;
                if (node is State s)
                {
                    foreach (var b in s.Behaviors)
                        Walk(b, s, onDeath, found, children, seen);
                    foreach (var child in s.States)
                        Walk(child, child, onDeath, found, children, seen);
                    return;
                }
                if (node is IEnumerable list)
                {
                    foreach (var item in list)
                        Walk(item, state, onDeath, found, children, seen);
                    return;
                }

                var t = node.GetType();
                if (t.Namespace == null || !t.Namespace.StartsWith("WorldServer.logic") && !t.Namespace.StartsWith("System"))
                    return;
                var name = t.Name;
                // the death behaviours (DropPortalOnDeath, PlaceMapAtDeath, TransformOnDeath, OnDeathBehavior,
                // the Doomed Realms DeathPortal and OnDeath) and the loot
                var death = onDeath || name.Contains("Death");
                // a drop that never happens (the Elder Tomb's 0% Realm Portal, from a shared builder)
                if (NeverHappens(node))
                    return;
                if (name == "RealmPortalDrop")
                    found.Add(new Source { Portal = "Realm Portal", Via = name, OnDeath = true, State = state });

                // what it makes on death that can leave a portal of its own (not the objects it orders,
                // removes or hurts)
                var spawns = death && !(name is "OrderOnDeath" or "RemoveObjectOnDeath" or "DestroyOnDeath" or "TransferDamageOnDeath");
                void Add(ushort id)
                {
                    if (_data.Portals.TryGetValue(id, out var desc))
                        found.Add(new Source { Portal = desc.IdName, Via = name, OnDeath = death, State = state });
                    else if (spawns)
                        children.Add((id, name, state));
                }

                for (var type = t; type != null && type != typeof(object); type = type.BaseType)
                    foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        var value = f.GetValue(node);
                        // an object type, or several (TossObject's children), by type or id
                        var ids = value switch
                        {
                            ushort id => new[] { id },
                            ushort[] many => many,
                            string text => _data.IdToObjectType.TryGetValue(text, out var id) ? new[] { id } : null,
                            string[] texts => texts.Where(_data.IdToObjectType.ContainsKey).Select(x => _data.IdToObjectType[x]).ToArray(),
                            _ => f.FieldType == typeof(ushort?) && value != null ? new[] { (ushort)value } : null
                        };
                        if (ids != null)
                            foreach (var id in ids)
                                Add(id);
                        else if (value is string map && map.EndsWith(".jm", StringComparison.OrdinalIgnoreCase))
                            foreach (var portal in MapPortals(map))
                                found.Add(new Source { Portal = portal, Via = $"{name} ({map})", OnDeath = death, State = state });
                        else if (value != null && !(value is State) && !(value is string))
                            Walk(value, state, death, found, children, seen);
                    }
            }

            // a chance field (DropPortalOnDeath's _probability, DeathPortal's percent) of 0
            private static bool NeverHappens(object node)
            {
                foreach (var f in node.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    var name = f.Name.TrimStart('_').ToLowerInvariant();
                    if ((name == "probability" || name == "percent") && Convert.ToDouble(f.GetValue(node)) == 0)
                        return true;
                }
                return false;
            }

            // the portals a set piece (.jm) holds
            private List<string> MapPortals(string path)
            {
                if (_mapPortals.TryGetValue(path, out var known))
                    return known;
                var portals = new List<string>();
                var file = Path.Combine(_worlds, path);
                if (File.Exists(file))
                    foreach (var entry in JObject.Parse(File.ReadAllText(file))["dict"] ?? new JArray())
                        foreach (var obj in entry["objs"] ?? new JArray())
                            if (_data.IdToObjectType.TryGetValue((string)obj["id"], out var id) && _data.Portals.TryGetValue(id, out var desc))
                                portals.Add(desc.IdName);
                return _mapPortals[path] = portals;
            }
        }
    }
}
