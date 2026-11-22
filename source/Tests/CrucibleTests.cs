using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Shared.crucible;
using Shared.database.leaderboard;
using Shared.resources;
using WorldServer.core;
using WorldServer.logic;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // The Crucible (Shared.crucible.CrucibleRules, BehaviorDb.Crucible.cs, tools/build_crucible.py).
    [Collection("BehaviorDb")]
    public class CrucibleTests
    {
        private readonly ITestOutputHelper Output;

        public CrucibleTests(ITestOutputHelper output) => Output = output;

        private static readonly Lazy<(XmlData Data, BehaviorDb Db)> Game = new Lazy<(XmlData, BehaviorDb)>(() =>
        {
            var res = new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false);
            var server = (GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
            typeof(GameServer).GetProperty(nameof(GameServer.Resources)).SetValue(server, res);
            var db = new BehaviorDb(server);
            db.Initialize();
            return (res.GameData, db);
        });

        [Theory]
        [InlineData(0, false, false, false)]
        [InlineData(49, false, false, false)]
        [InlineData(50, false, false, true)]
        [InlineData(87, false, false, true)]
        [InlineData(3, true, false, true)]
        [InlineData(0, false, true, true)]
        public void RoundFiftyGildedOrAdminOpensIt(int best, bool gilded, bool admin, bool open) =>
            Assert.Equal(open, CrucibleRules.Unlocked(best, gilded, admin));

        [Fact]
        public void TheLockedHintSaysHowToGetIn()
        {
            var hint = CrucibleRules.LockedHint(31);
            Assert.Contains("50", hint);
            Assert.Contains("31", hint);
            Assert.Contains("Gilded", hint);
        }

        [Fact]
        public void FiveFloorsInOrderEachWithItsOwnHeartAndChampion()
        {
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, CrucibleRules.Floors.Select(f => f.Number));
            Assert.Equal(5, CrucibleRules.Floors.Select(f => f.Heart).Distinct().Count());
            Assert.Equal(5, CrucibleRules.Champions.Distinct().Count());
        }

        [Fact]
        public void EveryChampionIsABossWithABehaviour()
        {
            var (data, db) = Game.Value;
            foreach (var name in CrucibleRules.Champions)
            {
                Assert.True(data.IdToObjectType.TryGetValue(name, out var type), name + ": no object");
                Assert.True(db.Definitions.ContainsKey(type), name + ": no behaviour");
                var desc = data.ObjectDescs[type];
                Assert.True(desc.Enemy && desc.Quest, name + ": not a boss");
            }
        }

        // CrucibleWorld puts a floor's Heart back to Idle when its champion falls: each needs one
        [Fact]
        public void EveryHeartStartsIdle()
        {
            var (data, db) = Game.Value;
            foreach (var f in CrucibleRules.Floors)
            {
                Assert.True(data.IdToObjectType.TryGetValue(f.Heart, out var type), f.Heart + ": no object");
                Assert.True(db.Definitions.TryGetValue(type, out var def), f.Heart + ": no behaviour");
                Assert.Equal("Idle", def.Item1.States.First().Name);
            }
        }

        public static IEnumerable<object[]> ArenaFormMoments() =>
            WorldServer.logic.behaviors.ArenaForms.All.SelectMany(f =>
                (WorldServer.logic.behaviors.ArenaForms.Moves(f) ? new[] { 0, 7300, 23900, 41700, 66100 } : new[] { 0 })
                .Select(ms => new object[] { f, ms }));

        // Every floor reshapes itself through its champion's fight (ArenaForms): each form's ground
        // and pillars are in the game, it stays within the floor's reach, and everything that isn't
        // only to be looked at is one piece, so nobody is ever cut off (at the size they're grown to)
        [Theory]
        [MemberData(nameof(ArenaFormMoments))]
        public void EveryArenaFormIsWholeAndInTheGame(string form, int ms)
        {
            var (data, _) = Game.Value;
            const int reach = 36;
            var farthest = (int)Math.Ceiling(22 * WorldServer.logic.behaviors.ArenaForms.Grow);
            var layout = WorldServer.logic.behaviors.ArenaForms.Layout(form, ms, reach, out var dropped);
            Assert.True(dropped <= 16, $"{form} at {ms} ms: {dropped} squares can't be walked to");
            var floor = new HashSet<(int, int)>();
            var picture = new System.Text.StringBuilder();
            for (var dy = -reach; dy <= reach; dy++)
            {
                for (var dx = -reach; dx <= reach; dx++)
                {
                    if (!layout.TryGetValue((dx, dy), out var look))
                    {
                        picture.Append(' ');
                        continue;
                    }
                    Assert.True(data.IdToTileType.ContainsKey(look.Ground), $"{form}: no ground {look.Ground}");
                    Assert.True(look.Obj == null || data.IdToObjectType.ContainsKey(look.Obj), $"{form}: no object {look.Obj}");
                    Assert.True(Math.Max(Math.Abs(dx), Math.Abs(dy)) <= farthest, $"{form}: ({dx}, {dy}) is past the floor's reach");
                    picture.Append(look.Obj != null ? 'P' : look.Decor ? '+' : look.Ground[0]);
                    if (!look.Decor && look.Obj == null)
                        floor.Add((dx, dy));
                }
                picture.Append('\n');
            }
            Output.WriteLine(picture.ToString());
            Assert.NotEmpty(floor);
            var seen = new HashSet<(int, int)> { floor.First() };
            var todo = new Queue<(int, int)>(seen);
            while (todo.Count > 0)
            {
                var (x, y) = todo.Dequeue();
                foreach (var q in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (floor.Contains(q) && seen.Add(q))
                        todo.Enqueue(q);
            }
            Assert.True(seen.Count == floor.Count, $"{form} at {ms} ms: {floor.Count - seen.Count} squares cut off");
        }

        // every state a champion orders its Heart into is one the Heart has
        [Fact]
        public void EveryOrderedHeartStateExists()
        {
            var (data, db) = Game.Value;
            static IEnumerable<WorldServer.logic.State> All(WorldServer.logic.State s) => new[] { s }.Concat(s.States.SelectMany(All));
            foreach (var f in CrucibleRules.Floors)
            {
                var heart = db.Definitions[data.IdToObjectType[f.Heart]].Item1;
                var names = All(heart).Select(s => s.Name).ToHashSet();
                foreach (var formed in WorldServer.logic.behaviors.ArenaForms.ByHeart[f.Heart].Keys)
                    Assert.True(names.Contains(formed), $"{f.Heart} has a form for {formed}, a state it hasn't");
                var champion = db.Definitions[data.IdToObjectType[f.Champion]].Item1;
                foreach (var state in All(champion))
                    foreach (var order in state.Behaviors.Where(b => b.GetType().Name == "Order"))
                    {
                        var target = (string)order.GetType()
                            .GetField("_targetStateName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .GetValue(order);
                        Assert.True(names.Contains(target), $"{f.Champion} orders {f.Heart} into {target}, which it hasn't");
                    }
            }
        }

        [Fact]
        public void TheWorldAndItsPiecesAreThere()
        {
            var (data, _) = Game.Value;
            Assert.NotNull(data.GetWorld(CrucibleRules.WorldName));
            foreach (var name in new[] { CrucibleRules.PortalName, "Ascension Sigil", "Bastion Grate", "Nexus Portal" })
                Assert.True(data.IdToObjectType.ContainsKey(name), name);
            foreach (var tile in new[] { "Grinding Blade", "Molten Grate", "Surging Tide", "Deep Tide", "Forge Lava", "Cooling Slag",
                "Chime Light", "Hour Hand", "Sovereign Flame", "Crumbling Floor", "Crucible Warning", "Empty" })
                Assert.True(data.IdToTileType.ContainsKey(tile), tile);
        }

        [Fact]
        public void EveryCueHasAName()
        {
            for (byte c = 1; c <= 23; c++)
                Assert.False(string.IsNullOrEmpty(CrucibleCues.Name(c)), "cue " + c);
            Assert.Equal(CrucibleCues.Names.Count, CrucibleCues.Names.Values.Distinct().Count());
        }

        // one climb a day, two with a Gilded Realm Pass, counted by the UTC day; admins unlimited
        [Theory]
        [InlineData(100, 99, 1, false, false, 1)]
        [InlineData(100, 100, 1, false, false, 0)]
        [InlineData(100, 100, 1, true, false, 1)]
        [InlineData(100, 100, 2, true, false, 0)]
        [InlineData(100, 100, 5, false, true, int.MaxValue)]
        public void ClimbsADay(int today, int lastDay, int climbs, bool gilded, bool admin, int left) =>
            Assert.Equal(left, CrucibleRules.ClimbsLeft(today, lastDay, climbs, gilded, admin));

        // an event that doubles the climbs (ServerEvent.crucibleClimbs): two a day, four Gilded
        [Theory]
        [InlineData(100, 100, 1, false, 1)]
        [InlineData(100, 100, 2, false, 0)]
        [InlineData(100, 100, 2, true, 2)]
        [InlineData(100, 100, 4, true, 0)]
        [InlineData(100, 99, 4, true, 4)]
        public void AnEventMultipliesTheClimbs(int today, int lastDay, int climbs, bool gilded, int left)
        {
            Assert.Equal(left, CrucibleRules.ClimbsLeft(today, lastDay, climbs, gilded, false, 2));
            Assert.Equal(gilded ? 4 : 2, CrucibleRules.DailyClimbs(gilded, 2));
        }

        [Fact]
        public void TheSpentHintCountsTheEventsClimbs()
        {
            Assert.StartsWith("You've climbed the Crucible once today", CrucibleRules.SpentHint(false, TimeSpan.FromHours(3)));
            Assert.StartsWith("You've climbed the Crucible twice today", CrucibleRules.SpentHint(false, TimeSpan.FromHours(3), 2));
            Assert.StartsWith("You've climbed the Crucible 4 times today", CrucibleRules.SpentHint(true, TimeSpan.FromHours(3), 2));
        }

        [Fact]
        public void ADayIsAUtcDay()
        {
            Assert.Equal(CrucibleRules.Day(new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc)), CrucibleRules.Day(new DateTime(2026, 11, 21, 23, 59, 59, DateTimeKind.Utc)));
            Assert.Equal(CrucibleRules.Day(new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc)) + 1, CrucibleRules.Day(new DateTime(2026, 11, 22, 0, 0, 1, DateTimeKind.Utc)));
            Assert.Contains("Gilded", CrucibleRules.SpentHint(false, TimeSpan.FromHours(3)));
        }

        // the white bags: each its class's ability, with its power, from its floor's champion
        [Theory]
        [InlineData("Ferrogrim's Bastion Turret", 32, "BastionTurret", "Ferrogrim, the Iron Warden")]
        [InlineData("Nerissa's Tidepearl", 30, "Tidepearl", "Nerissa, the Tidecaller")]
        [InlineData("Pyrrhax's Meteor Mortar", 5005, "MeteorMortar", "Pyrrhax, the Forge Tyrant")]
        [InlineData("Chronarch's Coat of Stolen Hours", 37, "StolenHours", "Chronarch, the Hour Thief")]
        [InlineData("Vaelith's Final Aria", 28, "FinalAria", "Vaelith, the Crucible Sovereign")]
        public void EachChampionHasItsWhiteBag(string item, int slot, string power, string champion)
        {
            var (data, db) = Game.Value;
            Assert.True(data.IdToObjectType.TryGetValue(item, out var type), item);
            var it = data.Items[type];
            Assert.Equal(slot, it.SlotType);
            Assert.Equal(power, it.RelicPower);
            Assert.Equal(6, it.BagType);
            Assert.True(it.Soulbound);
            var loot = db.Definitions[data.IdToObjectType[champion]].Item2;
            Assert.Contains(item, string.Join("|", AllLoot(loot)));
            // the rules know it as its champion's (an event's crucibleWhiteChance covers it)
            Assert.Equal(item, CrucibleRules.Floors.Single(f => f.Champion == champion).WhiteBag);
            Assert.True(CrucibleRules.IsWhiteBag(item));
        }

        [Fact]
        public void OnlyTheFiveAreWhiteBags()
        {
            Assert.Equal(5, CrucibleRules.Floors.Select(f => f.WhiteBag).Distinct().Count());
            Assert.False(CrucibleRules.IsWhiteBag("Lil' Ferrogrim Generator"));
            Assert.False(CrucibleRules.IsWhiteBag("Potion of Life"));
            Assert.False(CrucibleRules.IsWhiteBag(null));
        }

        [Fact]
        public void TheWhiteBagsAlliesHaveBehaviours()
        {
            var (data, db) = Game.Value;
            Assert.True(db.Definitions.ContainsKey(data.IdToObjectType["Tidal Substitute"]));
        }

        // Ferrogrim's Bastion Turret is a Mechanic's turret (Turret.cs, Player.Turrets.cs): thrown
        // where aimed, its shots its own and no behaviour to fight it
        [Fact]
        public void TheBastionTurretIsAMechanicsTurret()
        {
            var (data, db) = Game.Value;
            var item = data.Items[data.IdToObjectType["Ferrogrim's Bastion Turret"]];
            var build = Assert.Single(item.ActivateEffects, e => e.Effect == ActivateEffects.BuildTower);
            Assert.Equal("Bastion Turret", build.ObjectId);
            Assert.True(build.AllyMs > 0);
            Assert.Equal(3, build.AllyShots);
            Assert.True(item.Cooldown >= 1);
            var turret = data.IdToObjectType["Bastion Turret"];
            Assert.False(db.Definitions.ContainsKey(turret));
            Assert.True(data.ObjectDescs[turret].Projectiles.ContainsKey(0));
        }

        // every item name a loot table could drop, by reflection (the loot classes keep them private)
        private static IEnumerable<string> AllLoot(object loot)
        {
            if (loot == null)
                yield break;
            if (loot is string s)
            {
                yield return s;
                yield break;
            }
            if (loot is System.Collections.IEnumerable list)
            {
                foreach (var o in list)
                    foreach (var n in AllLoot(o))
                        yield return n;
                yield break;
            }
            var t = loot.GetType();
            if (!t.Namespace?.StartsWith("WorldServer.logic") ?? true)
                yield break;
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                foreach (var n in AllLoot(f.GetValue(loot)))
                    yield return n;
        }

        [Fact]
        public void HigherFloorsThenQuickerClimbsRankFirst()
        {
            Assert.True(DbCrucibleRuns.Score(5, 3000) > DbCrucibleRuns.Score(4, 100));
            Assert.True(DbCrucibleRuns.Score(5, 600) > DbCrucibleRuns.Score(5, 900));
            var run = DbCrucibleRuns.Parse("1763700000000|734|Alpha, Beta", DbCrucibleRuns.Score(5, 734));
            Assert.Equal((5, 734), (run.Floors, run.Seconds));
            Assert.Equal(new[] { "Alpha", "Beta" }, run.Players);
        }
    }
}
