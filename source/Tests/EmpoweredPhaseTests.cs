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
    }
}
