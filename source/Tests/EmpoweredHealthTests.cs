using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shared.resources;
using WorldServer.core.resurrection;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // An Empowered boss's health (Empowered.cs, EmpoweredHealth) and the dungeon bosses' quest flags.
    public class EmpoweredHealthTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private readonly ITestOutputHelper _out;

        public EmpoweredHealthTests(ITestOutputHelper output) => _out = output;

        [Theory]
        [InlineData(1600, 2.5)]
        [InlineData(20000, 2.5)]
        [InlineData(200000, 1.5)]
        [InlineData(800000, 1.25)]
        [InlineData(2000000, 1.25)]
        public void ScaleTapersWithTheElderBossHealth(int hp, double scale) =>
            Assert.Equal(scale, EmpoweredHealth.ScaleFor(hp), 3);

        [Fact]
        public void ScaleNeverRisesWithHealth()
        {
            var last = double.MaxValue;
            for (var hp = 1000; hp <= 1000000; hp += 1000)
            {
                var s = EmpoweredHealth.ScaleFor(hp);
                Assert.True(s <= last + 1e-9, $"{hp}: {s} after {last}");
                last = s;
            }
        }

        // ScaleHP2 as it runs: each player after the first adds percent of the base to the max
        // and the health. The Empowered boss keeps that share as it is, never multiplying the pool.
        [Theory]
        [InlineData(200000, 15, 5, 420000)]   // the Elder Ivory Wyvern: 300k and 30k a player
        [InlineData(138000, 15, 3, 270639)]   // the Elder Thessal
        [InlineData(1600, 20, 5, 5280)]       // the Elder Dreadstump
        public void PlayersAddTheirShareOnce(int elderHp, int percent, int players, int expected)
        {
            var h = new EmpoweredHealth(elderHp);
            var (max, health) = h.Update(elderHp, elderHp);
            for (var p = 2; p <= players; p++)
            {
                var add = percent * elderHp / 100;
                (max, health) = h.Update(max + add, health + add);
                (max, health) = h.Update(max, health); // the next tick: nothing changed
            }
            _out.WriteLine($"{elderHp} x{h.Scale:0.00}, {players} players: {max}");
            Assert.InRange(max, expected - 1, expected + 1);
            Assert.Equal(max, health);
        }

        [Fact]
        public void DamageDealtStaysDealtWhenAPlayerJoins()
        {
            var h = new EmpoweredHealth(200000);
            var (max, _) = h.Update(200000, 200000);
            Assert.Equal(300000, max);
            var health = 100000;
            (max, health) = h.Update(max + 30000, health + 30000);
            Assert.Equal(330000, max);
            Assert.Equal(130000, health);
        }

        [Fact]
        public void APlayerLeavingKeepsTheShare()
        {
            var h = new EmpoweredHealth(200000);
            var (max, _) = h.Update(200000, 200000);
            (max, var health) = h.Update(max + 60000, max + 60000);
            // BossStats: a counted player gone, the same fraction of a smaller pool
            (max, health) = h.Update(max - 60000, (max - 60000) / 2);
            Assert.Equal(300000, max);
            Assert.Equal(150000, health);
        }

        // what was raised before: 2.5 times the whole pool whenever the behaviour changed it
        [Fact]
        public void ListEmpoweredHealth()
        {
            var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "resources", "data", "resurrection.json")));
            var data = Res.Value.GameData;
            foreach (var b in json.RootElement.GetProperty("bosses").EnumerateArray())
            {
                var id = b.GetProperty("boss").GetString();
                if (!data.IdToObjectType.TryGetValue(id, out var type))
                    continue;
                var hp = data.ObjectDescs[type].MaxHP;
                _out.WriteLine($"{id}: {hp} -> {hp + (int)Math.Round(hp * (EmpoweredHealth.ScaleFor(hp) - 1))} (x{EmpoweredHealth.ScaleFor(hp):0.00}, was {hp * 2.5:0})");
            }
        }

        // every boss a Necromantic Relic raises is a quest, in its Elder and Empowered forms: the
        // arrow and its health at the top of the screen
        [Fact]
        public void EveryRaisableBossIsAQuest()
        {
            var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "resources", "data", "resurrection.json")));
            var data = Res.Value.GameData;
            // the Beach Zone's god is no quest in any version (its XML is the Doomed Realms port's)
            var known = new[] { "Elder Masked Party God", "Empowered Elder Masked Party God" };
            foreach (var b in json.RootElement.GetProperty("bosses").EnumerateArray())
                foreach (var id in new[] { b.GetProperty("boss").GetString(), b.GetProperty("empowered").GetString() }.Except(known))
                {
                    Assert.True(data.IdToObjectType.TryGetValue(id, out var type), $"no {id}");
                    Assert.True(data.ObjectDescs[type].Quest, $"{id} is no quest");
                }
        }

        // the Cultist Hideout: Malus is the quest in both his forms, not the invisible Cult below the
        // pentagram (which stays after he falls, so the dungeon never counted as cleared)
        [Theory]
        [InlineData("")]
        [InlineData("Elder ")]
        public void MalusIsTheCultsQuest(string prefix)
        {
            var data = Res.Value.GameData;
            bool Quest(string id) => data.ObjectDescs[data.IdToObjectType[prefix + id]].Quest;
            Assert.True(Quest("LH Malus"));
            Assert.True(Quest("LH Malus 2"));
            Assert.False(Quest("LH Cult Controller"));
            Assert.True(Quest("LH Marble Colossus"));
            Assert.True(Quest("LH Void Entity"));
        }
    }
}
