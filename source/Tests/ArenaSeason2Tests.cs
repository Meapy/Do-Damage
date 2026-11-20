using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Shared.database.leaderboard;
using Shared.resources;
using WorldServer.core;
using WorldServer.core.worlds.impl;
using WorldServer.logic;
using Xunit;

namespace Tests
{
    // The Arena's Season 2 (ArenaSeason2, ArenaPlanner) and its leaderboard (ArenaSeasons, DbArenaRuns).
    [Collection("BehaviorDb")]
    public class ArenaSeason2Tests
    {
        private static readonly Lazy<(XmlData Data, HashSet<ushort> Defined)> Game = new Lazy<(XmlData, HashSet<ushort>)>(() =>
        {
            var res = new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false);
            var server = (GameServer)RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
            typeof(GameServer).GetProperty(nameof(GameServer.Resources)).SetValue(server, res);
            var db = new BehaviorDb(server);
            db.Initialize();
            return (res.GameData, db.Definitions.Keys.ToHashSet());
        });

        [Fact]
        public void EveryMonsterIsAnEnemyWithABehaviour()
        {
            var (data, defined) = Game.Value;
            var missing = new List<string>();
            foreach (var name in ArenaSeason2.AllMonsters)
            {
                if (!data.IdToObjectType.TryGetValue(name, out var type))
                    missing.Add(name + " (no object)");
                else if (!defined.Contains(type))
                    missing.Add(name + " (no behaviour)");
                else if (!data.ObjectDescs[type].Enemy)
                    missing.Add(name + " (not an enemy)");
            }
            Assert.True(missing.Count == 0, string.Join("; ", missing));
        }

        [Fact]
        public void EveryEmpoweredBossCanBeRaised()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "resources", "data", "resurrection.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var raisable = doc.RootElement.GetProperty("bosses").EnumerateArray().Select(b => b.GetProperty("boss").GetString()).ToHashSet();
            foreach (var (boss, tier) in ArenaSeason2.EmpoweredPool)
            {
                Assert.Contains(boss, raisable);
                Assert.InRange(tier, ArenaSeason2.TierOf(ArenaSeason2.EmpoweredFromWave), 6);
            }
            // the Forgotten King's rising rebuilds the floor round him: never in the ring
            Assert.DoesNotContain(ArenaSeason2.EmpoweredPool, e => e.Boss.Contains("Forgotten King"));
        }

        [Fact]
        public void EveryTierHasItsEncounters()
        {
            for (var tier = 1; tier <= ArenaSeason2.TierFrom.Length; tier++)
                Assert.True(ArenaSeason2.OfTier(tier).Length >= 8, $"tier {tier}");
            Assert.Equal(ArenaSeason2.Encounters.Length, ArenaSeason2.Encounters.Select(e => e.Name).Distinct().Count());
            // a showdown for every tenth wave's tier
            foreach (var wave in new[] { 10, 20, 30, 40 })
                Assert.Contains(ArenaSeason2.Showdowns, s => s.Tier == ArenaSeason2.TierOf(wave));
            Assert.All(ArenaSeason2.Encounters.Concat(ArenaSeason2.Showdowns), e =>
            {
                Assert.NotEmpty(e.Bosses);
                Assert.NotEmpty(e.Minions);
            });
        }

        [Fact]
        public void TiersAndCapsRise()
        {
            Assert.Equal(1, ArenaSeason2.TierOf(1));
            Assert.Equal(2, ArenaSeason2.TierOf(6));
            Assert.Equal(3, ArenaSeason2.TierOf(12));
            Assert.Equal(4, ArenaSeason2.TierOf(29));
            Assert.Equal(5, ArenaSeason2.TierOf(30));
            Assert.Equal(6, ArenaSeason2.TierOf(77));
            var caps = Enumerable.Range(1, 80).Select(ArenaSeason2.HealthCap).ToList();
            for (var i = 1; i < caps.Count; i++)
                Assert.True(caps[i] >= caps[i - 1]);
            Assert.True(ArenaSeason2.HealthCap(60) > ArenaSeason2.HealthCap(40));
            Assert.Equal(0, ArenaSeason2.EmpoweredChance(11));
            Assert.InRange(ArenaSeason2.EmpoweredChance(12), 0.01, 0.05);
            Assert.InRange(ArenaSeason2.EmpoweredChance(90), 0.05, 0.08);
            Assert.Equal(1, ArenaSeason2.TwistChance(45));
        }

        // A hundred waves, many times over: the rounds keep to their rules.
        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(42)]
        [InlineData(1234)]
        public void PlannedRoundsKeepTheirRules(int seed)
        {
            var planner = new ArenaPlanner(new Random(seed));
            ArenaRound before = null;
            for (var wave = 1; wave <= 100; wave++)
            {
                var r = planner.Round(wave);
                Assert.Equal(wave, r.Wave);
                Assert.False(string.IsNullOrWhiteSpace(r.Title));
                Assert.Equal(ArenaSeason2.IsShowdown(wave), r.Format == ArenaFormat.Showdown);
                if (before != null && r.Format != ArenaFormat.Standard)
                    Assert.NotEqual(before.Format, r.Format);
                if (before != null && r.Twist != ArenaTwist.None)
                    Assert.NotEqual(before.Twist, r.Twist);
                if (wave < ArenaSeason2.TwistFromWave)
                    Assert.Equal(ArenaTwist.None, r.Twist);
                if (wave >= 40)
                    Assert.NotEqual(ArenaTwist.None, r.Twist);
                // a horde has no boss, a boss rush brings three one after another, any other round a boss at least
                switch (r.Format)
                {
                    case ArenaFormat.Horde:
                        Assert.Empty(r.AllBosses);
                        Assert.NotEmpty(planner.Minions(r, 1));
                        break;
                    case ArenaFormat.BossRush:
                        Assert.Empty(r.Bosses);
                        Assert.InRange(r.Relay.Count, 2, 3);
                        Assert.Equal(r.Relay.Count, r.Relay.Distinct().Count());
                        Assert.Empty(planner.Minions(r, 3));
                        break;
                    case ArenaFormat.Clash:
                        Assert.Equal(2, r.Bosses.Count);
                        Assert.False(r.Bosses.All(ArenaPools.IsOryx));
                        break;
                    default:
                        Assert.NotEmpty(r.Bosses);
                        break;
                }
                Assert.True(r.Bosses.Count(ArenaPools.IsOryx) <= 1);
                Assert.InRange(planner.Minions(r, 8).Length, 0, ArenaSeason2.MaxMinions);
                before = r;
            }
        }

        // the first rounds of each tier come from a shuffled pile: no encounter twice before the
        // others have come
        [Fact]
        public void APileDealsEveryEncounterOnce()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var planner = new ArenaPlanner(new Random(seed));
                var titles = new List<string>();
                // waves 1-5 are tier 1: standard or horde rounds, a title each from the pile
                for (var wave = 1; wave <= 5; wave++)
                    titles.Add(planner.Round(wave).Title);
                Assert.Equal(titles.Count, titles.Distinct().Count());
            }
        }

        [Fact]
        public void TheSameSeedPlansTheSameRounds()
        {
            var a = new ArenaPlanner(new Random(99));
            var b = new ArenaPlanner(new Random(99));
            for (var wave = 1; wave <= 60; wave++)
                Assert.Equal(a.Round(wave).Title, b.Round(wave).Title);
        }

        [Fact]
        public void EmpoweredBossesComeRarelyAndApart()
        {
            var planner = new ArenaPlanner(new Random(5));
            var risen = 0;
            var last = -100;
            for (var game = 0; game < 200; game++)
            {
                last = -100;
                for (var wave = 1; wave <= 40; wave++)
                {
                    var boss = planner.Empowered(wave, last);
                    if (boss == null)
                        continue;
                    Assert.True(wave >= ArenaSeason2.EmpoweredFromWave);
                    Assert.False(ArenaSeason2.IsShowdown(wave));
                    Assert.True(wave - last >= ArenaSeason2.EmpoweredGap);
                    Assert.Contains(boss, ArenaSeason2.EmpoweredFor(ArenaSeason2.TierOf(wave)));
                    last = wave;
                    risen++;
                }
            }
            // about one in a run to wave 40 or so: rare, but it happens
            Assert.InRange(risen, 60, 400);
        }

        [Fact]
        public void SeasonsKeepSeasonOne()
        {
            Assert.Equal("doomed.arenaLeaderboard", ArenaSeasons.Get(1).Key);
            Assert.Equal(DbArenaRuns.Key, ArenaSeasons.Get(1).Key);
            Assert.Equal(2, ArenaSeasons.Current.Number);
            Assert.NotEqual(ArenaSeasons.Get(1).Key, ArenaSeasons.Current.Key);
            Assert.Equal(1, ArenaSeasons.Parse("1").Number);
            Assert.Equal(ArenaSeasons.Current, ArenaSeasons.Parse(null));
            Assert.Equal(ArenaSeasons.Current, ArenaSeasons.Parse("99"));
        }

        [Fact]
        public void RunsOfEverySeasonParse()
        {
            // before times were kept
            var old = DbArenaRuns.Parse("1700000000000|Ann, Bo", 12);
            Assert.Equal((12, -1, 1700000000000L), (old.Wave, old.Seconds, old.Time));
            Assert.Equal(new[] { "Ann", "Bo" }, old.Players);
            // Season 1
            var s1 = DbArenaRuns.Parse("1760000000000|754|Battle Arena|Cy", DbArenaRuns.Score(23, 754));
            Assert.Equal((23, 754, "Arena", 0, ""), (s1.Wave, s1.Seconds, s1.Arena, s1.Empowered, s1.FellTo));
            Assert.True(s1.Solo);
            // Season 2
            var s2 = DbArenaRuns.Parse("2|1770000000000|1999|2|The Shatters: the Bridge|Di, Ed, Fy", DbArenaRuns.Score(34, 1999));
            Assert.Equal((34, 1999, 1770000000000L, 2, "The Shatters: the Bridge"), (s2.Wave, s2.Seconds, s2.Time, s2.Empowered, s2.FellTo));
            Assert.Equal(new[] { "Di", "Ed", "Fy" }, s2.Players);
            Assert.False(s2.Solo);
            // the quicker of two runs to the same wave ranks first
            Assert.True(DbArenaRuns.Score(20, 600) > DbArenaRuns.Score(20, 601));
            Assert.True(DbArenaRuns.Score(21, 99999) > DbArenaRuns.Score(20, 1));
        }
    }
}
