using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shared.resources;
using WorldServer.core.resurrection;
using Xunit;

namespace Tests
{
    // An Empowered boss's phases (EmpoweredPhases.cs, EmpoweredSignatures.cs) and the shots they fire
    // (tools/build_resurrection.py).
    public class EmpoweredPhaseTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private static string[] Bosses()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "resources", "data", "resurrection.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("bosses").EnumerateArray().Select(b => b.GetProperty("boss").GetString()).ToArray();
        }

        [Fact]
        public void EveryEmpoweredBossHasThePhasesAndTheirShots()
        {
            var data = Res.Value.GameData;
            foreach (var boss in Bosses())
            {
                var phases = EmpoweredPhases.For(new BossInfo { Boss = boss });
                Assert.Contains(phases, p => p.Name == "Soul Harvest");
                Assert.Contains(phases, p => p.Name == "Last Breath");
                Assert.All(phases, p => Assert.InRange(p.Below, 0.01, 0.99));
                Assert.All(phases, p => Assert.NotNull(p.Run));
                // they open in order, the highest health first
                Assert.Equal(phases.OrderByDescending(p => p.Below).Select(p => p.Name), phases.Select(p => p.Name));

                var desc = data.ObjectDescs[data.IdToObjectType["Empowered " + boss]];
                foreach (var id in new[] { Arena.Bolt, Arena.FastBolt, Arena.Orb, Arena.Shard })
                    Assert.True(desc.Projectiles.ContainsKey(id), $"Empowered {boss} has no shot {id}");
            }
        }

        // the raid bosses and the Trench's: two phases of their own each, with their signature shots
        [Fact]
        public void TheRaidBossesHaveSignaturePhases()
        {
            var data = Res.Value.GameData;
            var all = Bosses();
            var names = EmpoweredSignatures.Bosses.SelectMany(b => EmpoweredSignatures.For(b).Select(p => p.Name)).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.Equal(EmpoweredSignatures.Bosses.Length * 2, names.Count);
            foreach (var boss in EmpoweredSignatures.Bosses)
            {
                Assert.Contains(boss, all);
                Assert.Equal(6, EmpoweredPhases.For(new BossInfo { Boss = boss }).Count);
                var desc = data.ObjectDescs[data.IdToObjectType["Empowered " + boss]];
                var shots = boss == "Elder lod Ivory Wyvern" ? 4 : 2;
                for (var i = 0; i < shots; i++)
                    Assert.True(desc.Projectiles.ContainsKey(Arena.Sig0 + i), $"Empowered {boss} has no signature shot {i}");
            }
            // the rest keep the shared phases only
            Assert.Equal(4, EmpoweredPhases.For(new BossInfo { Boss = "Elder Dreadstump the Pirate King" }).Count);
        }

        private static int[] SoulDamages()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "resources", "data", "resurrection.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("bosses").EnumerateArray().Select(b => b.GetProperty("soulDamage").GetInt32()).ToArray();
        }

        // no telegraphed blast takes a full-health Wizard or Priest (670 health) in one hit, even
        // with no defense and the boss enraged; the phases' heaviest blast is 2.2 times the soul bolt
        [Fact]
        public void NoBlastKillsAFullHealthPlayer()
        {
            var worst = Bosses().Max(b => Empowered.TuningFor(b).EnragedDamage);
            foreach (var soul in SoulDamages())
                Assert.InRange(Arena.BlastDamage(soul, 2.2) * worst, 0, 520);
        }

        // Last Breath's rings of marks leave at least a tile between their marks to step through
        [Fact]
        public void LastBreathLeavesAWayThrough()
        {
            foreach (var (radius, count) in EmpoweredPhases.LastBreathRings)
                Assert.True(2 * Math.PI * radius / count - 2 * EmpoweredPhases.LastBreathMark >= 1.0,
                    $"Last Breath's ring at {radius} tiles has no gap");
        }

        // Empowered Malus fights softer and slower than the others, and his ritual souls can be
        // broken in time
        [Fact]
        public void EmpoweredMalusIsSofter()
        {
            var malus = Empowered.TuningFor("Elder LH Malus 2");
            var other = Empowered.TuningFor("Elder LH Void Entity");
            Assert.Equal(Empowered.DefaultTuning, other);
            Assert.True(malus.Damage < other.Damage && malus.EnragedDamage < other.EnragedDamage);
            Assert.True(malus.Haste < other.Haste && malus.EnragedHaste < other.EnragedHaste);
            Assert.True(malus.Damage > 1 && malus.Haste > 1, "Empowered Malus is still harder than the Elder one");
            Assert.InRange(EmpoweredSignatures.RitualSoulShare, 0.005, 0.015);
        }
    }
}
