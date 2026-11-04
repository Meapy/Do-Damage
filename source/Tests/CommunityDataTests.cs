using System;
using System.IO;
using System.Linq;
using Shared.community;
using Shared.quests;
using Xunit;

namespace Tests
{
    // CommunityData's rules: the weekly draw, the runs open at a time, tiers, and the real data file
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
            // every goal has a tier for each share, and a top prize, its own or the default
            foreach (var g in Data.Goals)
            {
                Assert.Equal(Data.TierShares.Length, Data.Tiers(g));
                Assert.NotEmpty(g.Top);
                Assert.True(g.Minimum >= 1, g.Id);
                Assert.NotNull(g.Icon);
            }
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
            Assert.Equal(15, oryx.Goal);
            Assert.Equal(new[] { 4, 8, 12, 15 }, Enumerable.Range(0, 4).Select(t => Data.Threshold(oryx, t)));
            Assert.Equal(0, Data.Reached(oryx, 3));
            Assert.Equal(1, Data.Reached(oryx, 4));
            Assert.Equal(3, Data.Reached(oryx, 14));
            Assert.Equal(4, Data.Reached(oryx, 999));
            // what counts: any form of Oryx, not another boss
            Assert.True(oryx.Counts(new QuestEvent(QuestEvents.Kill) { Oryx = true, Boss = true }));
            Assert.False(oryx.Counts(new QuestEvent(QuestEvents.Kill) { Boss = true }));
            Assert.Equal("Defeat Oryx the Mad God 15 times as a realm, in any of his forms.", oryx.Describe());
        }
    }
}
