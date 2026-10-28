using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Shared.resources;
using WorldServer.core;
using WorldServer.logic;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // Checks over every monster's states, read from the real behaviours (BehaviorDb, the Doomed
    // Realms ones too).
    // BehaviorDb builds one at a time, so the classes that load it don't run side by side
    [Collection("BehaviorDb")]
    public class BehaviorStatesTests
    {
        private static readonly Lazy<BehaviorDb> Db = new Lazy<BehaviorDb>(Load);
        private readonly ITestOutputHelper _out;

        public BehaviorStatesTests(ITestOutputHelper output) => _out = output;

        private static BehaviorDb Load()
        {
            var res = new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false);
            // BehaviorDb reads the game data through its GameServer; nothing else is needed
            var server = (GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
            typeof(GameServer).GetProperty(nameof(GameServer.Resources)).SetValue(server, res);
            var db = new BehaviorDb(server);
            db.Initialize();
            return db;
        }

        private static IEnumerable<State> AllStates(State state) => new[] { state }.Concat(state.States.SelectMany(AllStates));

        private static T Field<T>(object o, string name) =>
            (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);

        private IEnumerable<(string Monster, State State)> Monsters()
        {
            var data = Db.Value.GameServer.Resources.GameData;
            foreach (var (type, def) in Db.Value.Definitions)
                foreach (var state in AllStates(def.Item1))
                    yield return (data.ObjectTypeToId.TryGetValue(type, out var id) ? id : $"0x{type:x4}", state);
        }

        // A state that sets an effect its parent state already holds takes it off when it's left
        // (ConditionEffectBehavior.OnStateExit), the parent's with it, so the parent's effect is
        // gone for good after the first visit. The Shatters' obelisk controller lost its
        // Invincible that way, got killed, and left the bridge obelisks invulnerable for ever.
        // The monsters below did it already and haven't been gone over; some need it (the Hive's
        // Queen Bee is only ever hurt because its root's Invulnerable goes when 'idle' is left),
        // so fix them one at a time, by playing the fight, rather than in ConditionEffectBehavior.
        private static readonly HashSet<string> Known = new HashSet<string>
        {
            "Air Elemental", "Air Power", "chasingHorror", "Daichi the Fallen", "Earth Elemental",
            "Earth Power", "EH Blue Guardian Bee", "EH Event Hive Corpse", "EH Event Hive", "EH Hive Bomb",
            "EH Red Guardian Bee", "EH Yellow Guardian Bee", "Elder Tomb Fire Turret",
            "Elder Tomb Frost Turret", "Elder Tomb Thunder Turret", "Fire Elemental", "Fire Power",
            "LH Colossus Pillar", "md1 Head of Shaitan", "shtrs Defense System", "TH Queen Bee",
            "Tomb Fire Turret", "Tomb Frost Turret", "Tomb Thunder Turret", "Water Elemental", "Water Power",
        };

        [Fact]
        public void NoStateSetsAnEffectItsParentHolds()
        {
            // ConditionEffectBehavior is internal to the world server
            static IEnumerable<Behavior> Effects(State s) => s.Behaviors.Where(b => b.GetType().Name == "ConditionEffectBehavior");
            static IEnumerable<ConditionEffectIndex> Held(State s) => Effects(s).Select(b => Field<ConditionEffectIndex>(b, "effect"));

            var found = new List<string>();
            foreach (var (monster, state) in Monsters().Where(m => !Known.Contains(m.Monster)))
                foreach (var b in Effects(state))
                {
                    if (Field<bool>(b, "perm"))
                        continue;
                    var effect = Field<ConditionEffectIndex>(b, "effect");
                    for (var p = state.Parent; p != null; p = p.Parent)
                        if (Held(p).Contains(effect))
                        {
                            found.Add($"{monster}: state '{state.Name}' sets {effect}, which '{p.Name}' holds");
                            break;
                        }
                }
            foreach (var f in found)
                _out.WriteLine(f);
            Assert.True(found.Count == 0, "States that take off their parent's effect when left:\n" + string.Join("\n", found));
        }
    }
}
