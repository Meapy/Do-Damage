using System;
using System.IO;
using System.Linq;
using Shared.community;
using Shared.quests;
using Xunit;

namespace Tests
{
    // CommunityData's rules: the weekly draw, the runs open at a time, tiers and Overdrive, loot
    // rolls, how a goal is resized after a run, and the real data file
    // (resources/data/community.json, copied beside the tests by the Shared project).
    public class CommunityDataTests
    {
        private static readonly CommunityData Data = CommunityData.Load(Path.Combine(AppContext.BaseDirectory, "resources", "data", "community.json"));

        // Wednesday 2026-10-21, mid-afternoon; its week began on Monday the 19th
        private static readonly DateTime Wed = new DateTime(2026, 10, 21, 15, 30, 0, DateTimeKind.Utc);
        private static readonly DateTime Monday = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void TheDataFileIsSound()
        {
            Assert.Empty(Data.Validate(_ => true));
            Assert.True(Data.Goals.Count >= Data.Slots * 2);
            // every goal has its own tier for each share, Overdrive rewards, a top prize and loot
            // with a rare enough prize to tell everyone of
            foreach (var g in Data.Goals)
            {
                Assert.Equal(Data.TierShares.Length, Data.Tiers(g));
                Assert.NotSame(Data.DefaultTiers, g.Tiers);
                Assert.NotSame(Data.DefaultBonus, g.Bonus);
                Assert.NotSame(Data.DefaultTop, g.Top);
                Assert.True(g.Loot.Count >= 8, g.Id);
                Assert.Contains(g.Loot, e => Data.Announced(e));
                Assert.True(g.Minimum >= 1, g.Id);
                Assert.NotNull(g.Icon);
            }
            // no two goals give the same top prize's item
            var prizes = Data.Goals.Select(g => string.Join(",", g.Top.Where(r => r.Item != null).Select(r => r.Item))).ToList();
            Assert.Equal(prizes.Count, prizes.Distinct().Count());
        }

        [Fact]
        public void EachWeekDrawsGroupsInTurn()
        {
            Assert.Equal(Monday, CommunityData.WeekStart(Wed));
            var groups = Data.Goals.Select(g => g.Group ?? g.Id).Distinct().Count();
            string GroupOf(string id) => Data.Goal(id).Group ?? id;
            var seen = new System.Collections.Generic.List<string>();
            // the same draw every time, `Slots` goals of different groups, and no group again until
            // the others have had their week
            for (var w = 0; w < groups / Data.Slots; w++)
            {
                var week = Monday.AddDays(7 * w);
                var drawn = Data.Draw(week);
                Assert.Equal(drawn, Data.Draw(week));
                Assert.Equal(Data.Slots, drawn.Count);
                Assert.Equal(Data.Slots, drawn.Select(GroupOf).Distinct().Count());
                foreach (var id in drawn)
                {
                    Assert.DoesNotContain(GroupOf(id), seen);
                    seen.Add(GroupOf(id));
                }
            }
        }

        [Fact]
        public void RunsAreThisWeeksAndLastWeeksToClaim()
        {
            var runs = Data.Runs(Wed);
            var running = runs.Where(r => r.Running(Wed)).ToList();
            Assert.Equal(Data.Slots, running.Count);
            Assert.All(running, r => Assert.Equal(Monday, r.Start));
            Assert.All(running, r => Assert.Equal(Monday.AddDays(7), r.End));
            // last week's, ended, still there to claim; running ones first
            var ended = runs.Where(r => !r.Running(Wed)).ToList();
            Assert.Equal(Data.Slots * Data.ClaimWeeks, ended.Count);
            Assert.All(ended, r => Assert.Equal(Monday, r.End));
            Assert.True(runs.TakeWhile(r => r.Running(Wed)).Count() == running.Count);
            // each run has its own key, by its goal and its first day
            Assert.Equal(runs.Count, runs.Select(r => r.Key).Distinct().Count());
            Assert.Contains("@20261019", running[0].Key);
            // the week's goals as the server kept them win over a new draw
            var kept = Data.Runs(Wed, week => week == Monday ? new System.Collections.Generic.List<string> { "oryx" } : Data.Draw(week));
            Assert.Equal("oryx", Assert.Single(kept, r => r.Running(Wed)).Def.Id);
        }

        [Fact]
        public void AGoalFinishedEarlyMakesWayForABonusGoal()
        {
            var drawn = Data.Draw(Monday);
            // only the last level finishes a goal
            var oryx = Data.Goal("oryx");
            Assert.False(Data.Finished(oryx, 15, 44));
            Assert.True(Data.Finished(oryx, 15, 45));
            // a bonus goal: none of this week's kinds, nor next week's or last week's, and the same
            // every time
            var next = Data.NextGoal(Monday, drawn);
            Assert.NotNull(next);
            Assert.Equal(next, Data.NextGoal(Monday, drawn));
            string GroupOf(string id) => Data.Goal(id).Group ?? id;
            Assert.DoesNotContain(GroupOf(next), drawn.Select(GroupOf));
            Assert.DoesNotContain(GroupOf(next), Data.Draw(Monday.AddDays(7)).Select(GroupOf));
            Assert.DoesNotContain(GroupOf(next), Data.Draw(Monday.AddDays(-7)).Select(GroupOf));
            // slot 1 finished on Tuesday noon: its goal ended then, the bonus runs to the reset
            var at = Monday.AddDays(1).AddHours(12);
            var runs = Data.Runs(Wed, null, week => week == Monday ? new[] { new EarlyStart(1, next, at) } : Array.Empty<EarlyStart>());
            var running = runs.Where(r => r.Running(Wed)).ToList();
            Assert.Equal(Data.Slots, running.Count);
            var bonus = Assert.Single(running, r => r.Bonus);
            Assert.Equal((next, at, Monday.AddDays(7), 1), (bonus.Def.Id, bonus.Start, bonus.End, bonus.Slot));
            Assert.Equal(Monday, bonus.Week);
            var done = Assert.Single(runs, r => r.Week == Monday && r.Slot == 1 && !r.Bonus);
            Assert.Equal((drawn[1], at), (done.Def.Id, done.End));
            Assert.False(done.Running(Wed));
            Assert.Equal(runs.Count, runs.Select(r => r.Key).Distinct().Count());
            Assert.True(done.Cut);
            Assert.False(bonus.Cut);
            Assert.Equal(1, bonus.Heat);
            // the faster it's finished, the harder: the target's pace (120% a week) or slower moves
            // nothing, 300% in a day and a half makes the bonus goal twice as hard or more, and
            // the heat builds
            Assert.Equal(1, Data.PaceStep(18, 15, 1), 6);
            Assert.Equal(1, Data.PaceStep(5, 15, 1), 6);
            Assert.InRange(Data.PaceStep(45, 15, 1.5 / 7), 2, Data.Early.MaxStep);
            Assert.Equal(Data.Early.MaxStep, Data.PaceStep(45, 15, 1.0 / 168), 6);
            var hot = Data.Heat(done, 45, 15, at);
            Assert.InRange(hot, 2, Data.Early.MaxStep);
            var hotter = Data.Runs(Wed, null, week => week == Monday ? new[] { new EarlyStart(1, next, at, hot) } : Array.Empty<EarlyStart>()).Single(r => r.Bonus);
            Assert.Equal(hot, hotter.Heat);
            Assert.Equal(Data.Early.MaxHeat, Data.Heat(hotter, 1000, 15, at.AddHours(1)), 6);
            // its goal's next run grows by the pace too, past adapt's usual step
            Assert.True(Data.NextScale(1, 45, 15, 1.5 / 7, true) > Data.Adapt.MaxStep);
            Assert.Equal(Data.Adapt.MaxStep, Data.NextScale(1, 45, 15, 1.5 / 7), 6);
            // a bonus run that fell short counts by its pace: 10 of 15 in 2 days is ahead of the target
            Assert.True(Data.NextScale(1, 10, 15, 2.0 / 7) > 1);
        }

        [Fact]
        public void AGoalStartedTwiceInAWeekRunsOnce()
        {
            // two goals finished at once, both given the same bonus goal in the same second (as
            // happened on 2026-11-05): the first slot keeps it, the second carries on as it was
            var drawn = Data.Draw(Monday);
            var next = Data.NextGoal(Monday, drawn);
            var at = Monday.AddDays(2);
            var twice = new[] { new EarlyStart(0, next, at, 3), new EarlyStart(1, next, at, 2.71) };
            var runs = Data.Runs(Wed, null, week => week == Monday ? twice : Array.Empty<EarlyStart>());
            Assert.Equal(runs.Count, runs.Select(r => r.Key).Distinct().Count());
            var bonus = Assert.Single(runs, r => r.Bonus);
            Assert.Equal((0, 3.0), (bonus.Slot, bonus.Heat));
            var second = Assert.Single(runs, r => r.Week == Monday && r.Slot == 1);
            Assert.Equal((drawn[1], Monday.AddDays(7)), (second.Def.Id, second.End));
            // a later bonus goal of its own for that slot then takes its place
            var other = Data.NextGoal(Monday, drawn.Append(next).ToList());
            Assert.NotEqual(next, other);
            var fixedRuns = Data.Runs(Wed, null, week => week == Monday ? twice.Append(new EarlyStart(1, other, at.AddMinutes(1))).ToArray() : Array.Empty<EarlyStart>());
            Assert.Equal(2, fixedRuns.Count(r => r.Bonus));
            Assert.Equal(at.AddMinutes(1), fixedRuns.Single(r => r.Week == Monday && r.Slot == 1 && !r.Bonus).End);
        }

        [Fact]
        public void SpecialsRunBetweenTheirDays()
        {
            var data = CommunityData.Parse(@"{
                ""defaultTiers"": [ [ { ""fame"": 1 } ], [ { ""fame"": 2 } ], [ { ""fame"": 3 } ], [ { ""fame"": 4 } ] ],
                ""goals"": [ { ""id"": ""a"", ""event"": ""kill"", ""goal"": 10 } ],
                ""specials"": [ { ""id"": ""halloween"", ""event"": ""dungeon"", ""goal"": 4, ""from"": ""2026-10-20"", ""until"": ""2026-10-22"" } ]
            }");
            Assert.Empty(data.Validate(_ => true).Where(p => !p.Contains("groups")));
            Assert.Equal(4, data.Tiers(data.Goal("halloween")));
            var special = data.Runs(Wed).Single(r => r.Special);
            Assert.True(special.Running(Wed));
            Assert.Equal(new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc), special.End);
            // claimable a week after it ends, then gone
            Assert.Contains(data.Runs(Wed.AddDays(8)), r => r.Special && !r.Running(Wed.AddDays(8)));
            Assert.DoesNotContain(data.Runs(Wed.AddDays(10)), r => r.Special);
            Assert.DoesNotContain(data.Runs(Wed.AddDays(-5)), r => r.Special);
        }

        [Fact]
        public void TiersAreReachedAtTheirShares()
        {
            var oryx = Data.Goal("oryx");
            Assert.Equal(250, oryx.Goal);
            Assert.Equal(new[] { 4, 8, 12, 15 }, Enumerable.Range(0, 4).Select(t => Data.Threshold(15, t)));
            Assert.Equal(0, Data.Reached(oryx, 15, 3));
            Assert.Equal(1, Data.Reached(oryx, 15, 4));
            Assert.Equal(3, Data.Reached(oryx, 15, 14));
            Assert.Equal(4, Data.Reached(oryx, 15, 999));
            // a run made harder moves every tier with it
            Assert.Equal(new[] { 5, 10, 15, 20 }, Enumerable.Range(0, 4).Select(t => Data.Threshold(20, t)));
            Assert.Equal(3, Data.Reached(oryx, 20, 15));
            // what counts: any form of Oryx, not another boss
            Assert.True(oryx.Counts(new QuestEvent(QuestEvents.Kill) { Oryx = true, Boss = true }));
            Assert.False(oryx.Counts(new QuestEvent(QuestEvents.Kill) { Boss = true }));
            Assert.Equal("Defeat Oryx the Mad God 250 times as a realm, in any of his forms.", oryx.Describe());
            Assert.Equal("Defeat Oryx the Mad God 20 times as a realm, in any of his forms.", oryx.Describe(20));
        }

        [Fact]
        public void OverdriveGoesPastTheGoal()
        {
            var oryx = Data.Goal("oryx");
            Assert.Equal(8, Data.BonusLevels(oryx));
            // a quarter of the goal more each: 19, 23, 27, 30 ... 45 for 300%
            Assert.Equal(new[] { 19, 23, 27, 30, 34, 38, 42, 45 }, Enumerable.Range(1, 8).Select(k => Data.BonusThreshold(15, k)));
            Assert.Equal(0, Data.BonusReached(oryx, 15, 18));
            Assert.Equal(1, Data.BonusReached(oryx, 15, 19));
            Assert.Equal(8, Data.BonusReached(oryx, 15, 10_000));
            // the stages: four tiers, then the levels, and nothing past the last
            Assert.Equal(4, Data.Stage(oryx, 15, 15));
            Assert.Equal(6, Data.Stage(oryx, 15, 23));
            Assert.Equal(19, Data.NextAt(oryx, 15, 4));
            Assert.Equal(-1, Data.NextAt(oryx, 15, 12));
            Assert.Equal("Blaze", Data.StageName(oryx, 2));
            Assert.Equal("Overdrive III", Data.StageName(oryx, 6));
            // a small goal's levels never share a total
            var tiny = Enumerable.Range(1, 8).Select(k => Data.BonusThreshold(1, k)).ToList();
            Assert.Equal(tiny.Count, tiny.Distinct().Count());
            // claims: a level's rewards roll at that level, the top's at the highest
            Assert.Equal(3, Data.ClaimRewards(oryx, CommunityData.BONUS_TIER + 2).Level);
            Assert.Same(oryx.Bonus, Data.ClaimRewards(oryx, CommunityData.BONUS_TIER).Rewards);
            Assert.Equal(8, Data.ClaimRewards(oryx, CommunityData.TOP_TIER).Level);
            Assert.Equal(0, Data.ClaimRewards(oryx, 1).Level);
        }

        [Fact]
        public void LootRollsByWeightAndOverdriveLiftsTheRare()
        {
            var oryx = Data.Goal("oryx");
            var base0 = Data.Odds(oryx, 0);
            Assert.Equal(1, base0.Sum(o => o.Chance), 6);
            double Rare(int level) => Data.Odds(oryx, level).Where(o => Data.RarityOf(o.Entry) >= 2).Sum(o => o.Chance);
            // each level makes the rare likelier
            for (var k = 1; k <= 8; k++)
                Assert.True(Rare(k) > Rare(k - 1), $"level {k}");
            // rolls: each {"roll": n} becomes n items of the goal's loot; the rest is paid as written
            var rng = new Random(7);
            var rolled = new System.Collections.Generic.List<LootEntry>();
            var paid = Data.Resolve(oryx, new[] { new RewardDef { Fame = 100 }, new RewardDef { Roll = 3 } }, 0, rng, rolled);
            Assert.Equal(4, paid.Count);
            Assert.Equal(100, paid[0].Fame);
            Assert.Equal(3, rolled.Count);
            Assert.All(paid.Skip(1), r => Assert.Contains(oryx.Loot, e => e.Item == r.Item));
            // many rolls land close to the odds
            var counts = Enumerable.Range(0, 20000).Select(_ => Data.Roll(oryx, 0, rng)).GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
            foreach (var (e, chance) in base0)
                Assert.InRange((counts.TryGetValue(e, out var n) ? n : 0) / 20000.0, chance * 0.8 - 0.002, chance * 1.2 + 0.002);
        }

        [Fact]
        public void ReelStopsOnThePrizeAndShowsTheGoalsLoot()
        {
            var oryx = Data.Goal("oryx");
            var rng = new Random(3);
            var prize = oryx.Loot.OrderByDescending(Data.RarityOf).First();
            var reel = Data.Reel(oryx, 2, prize, 40, 34, rng);
            Assert.Equal(40, reel.Count);
            Assert.Same(prize, reel[34]);
            Assert.All(reel, e => Assert.Contains(e, oryx.Loot));
            // more than one line of loot on it, so it turns
            Assert.True(reel.Distinct().Count() > 1);
        }

        [Fact]
        public void GoalsGrowAfterAnEasyWeekAndShrinkAfterAHardOne()
        {
            var oryx = Data.Goal("oryx");
            Assert.Equal(250, CommunityData.GoalOf(oryx, 1));
            Assert.Equal(330, CommunityData.GoalOf(oryx, 1.333));
            // round numbers: whole below 100, two digits above
            Assert.Equal(1100, CommunityData.Nice(1130));
            Assert.Equal(12000, CommunityData.Nice(11_960));
            Assert.Equal(1, CommunityData.Nice(0.2));
            // done right at the target: unchanged; well past it: harder; short of it: easier
            Assert.Equal(1, Data.NextScale(1, 18, 15), 6);
            Assert.True(Data.NextScale(1, 30, 15) > 1.2);
            Assert.True(Data.NextScale(1, 9, 15) < 0.9);
            // a step is held to a bound, and the scale to its range
            Assert.Equal(Data.Adapt.MaxStep, Data.NextScale(1, 1000, 15), 6);
            Assert.Equal(Data.Adapt.MinStep, Data.NextScale(1, 0, 15), 6);
            Assert.Equal(Data.Adapt.MaxScale, Data.NextScale(Data.Adapt.MaxScale, 1000, 15), 6);
            Assert.Equal(Data.Adapt.MinScale, Data.NextScale(Data.Adapt.MinScale, 0, 15), 6);
            // weeks of the same result settle on a goal the realm ends near the target of
            var scale = 1.0;
            for (var w = 0; w < 30; w++)
            {
                var goal = CommunityData.GoalOf(oryx, scale);
                scale = Data.NextScale(scale, 550, goal);
            }
            var settled = CommunityData.GoalOf(oryx, scale);
            Assert.InRange(550.0 / settled, 1.05, 1.35);
        }
    }
}
