using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Shared.quests;
using Xunit;

namespace Tests
{
    // QuestBook's rules: resets, drawing, progress, claims and their refusals, with the real data
    // file (resources/data/quests.json, copied beside the tests by the Shared project).
    public class QuestBookTests
    {
        private static readonly QuestData Data = QuestData.Load(Path.Combine(AppContext.BaseDirectory, "resources", "data", "quests.json"));

        // Wednesday 2026-10-21, mid-afternoon
        private static readonly DateTime Wed = new DateTime(2026, 10, 21, 15, 30, 0, DateTimeKind.Utc);

        private static QuestBook NewBook(int account = 7) => new QuestBook(Data, new QuestState(), account);

        private static QuestEvent Kill(bool god = false, bool boss = false) => new QuestEvent(QuestEvents.Kill) { God = god, Boss = boss };

        // A book whose daily quests are these (for tests that need a particular quest).
        private static QuestBook WithDaily(params string[] ids)
        {
            var book = NewBook();
            book.Refresh(Wed);
            book.State.Of(QuestPeriod.Daily).Quests = ids.Select(id => new QuestSlot { Id = id }).ToList();
            return book;
        }

        [Fact]
        public void DataFileIsValid()
        {
            var problems = Data.Validate(_ => true);
            Assert.Empty(problems);
            Assert.Equal(28, Data.Login.Count);
            Assert.All(Data.Achievements, a => Assert.Equal(4, a.Tiers.Length));
            Assert.All(Data.Achievements, a => Assert.True(a.Tiers.SequenceEqual(a.Tiers.OrderBy(t => t)), a.Id));
            Assert.True(Data.Ranks.Last().Points <= Data.Achievements.Count * Data.TierPoints.Sum(), "the top rank can be reached");
        }

        [Fact]
        public void PeriodsResetAtUtcMidnightMondayAndTheFirst()
        {
            Assert.Equal(new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc), QuestPeriods.NextReset(QuestPeriod.Daily, Wed));
            Assert.Equal(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), QuestPeriods.NextReset(QuestPeriod.Weekly, Wed));
            Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), QuestPeriods.NextReset(QuestPeriod.Monthly, Wed));
            // a Monday resets to the next Monday, not itself
            var monday = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
            Assert.Equal(monday.AddDays(7), QuestPeriods.NextReset(QuestPeriod.Weekly, monday));
            // Sunday night and Monday morning are different weeks, Monday and Sunday the same
            Assert.NotEqual(QuestPeriods.WeekKey(monday.AddSeconds(-1)), QuestPeriods.WeekKey(monday));
            Assert.Equal(QuestPeriods.WeekKey(monday), QuestPeriods.WeekKey(monday.AddDays(6.99)));
            Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), QuestPeriods.NextReset(QuestPeriod.Monthly, new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Utc)));
            Assert.NotEqual(QuestPeriods.MonthKey(new DateTime(2026, 12, 31)), QuestPeriods.MonthKey(new DateTime(2027, 1, 1)));
            Assert.Equal(QuestPeriods.DayKey(Wed), QuestPeriods.DayKey(Wed.Date.AddHours(23.99)));
        }

        [Fact]
        public void EachPeriodDrawsItsSlotsWithNoTwoOfAGroup()
        {
            for (var account = 1; account < 200; account++)
            {
                var book = NewBook(account);
                Assert.True(book.Refresh(Wed));
                foreach (QuestPeriod p in Enum.GetValues(typeof(QuestPeriod)))
                {
                    var defs = book.State.Of(p).Quests.Select(s => Data.Quest(s.Id)).ToList();
                    Assert.Equal(Data.SlotsOf(p), defs.Count);
                    Assert.All(defs, d => Assert.Equal(p, d.PeriodKind));
                    Assert.Equal(defs.Count, defs.Select(d => d.Group ?? d.Id).Distinct().Count());
                }
            }
        }

        [Fact]
        public void DrawsAreTheSameForAnAccountAndVaryBetweenAccounts()
        {
            var a = NewBook(5);
            var b = NewBook(5);
            a.Refresh(Wed);
            b.Refresh(Wed);
            Assert.Equal(a.State.ToJson(), b.State.ToJson());
            var lists = Enumerable.Range(1, 50).Select(id =>
            {
                var book = NewBook(id);
                book.Refresh(Wed);
                return string.Join(",", book.State.Of(QuestPeriod.Daily).Quests.Select(s => s.Id));
            }).Distinct().Count();
            Assert.True(lists > 5, $"only {lists} different daily lists for 50 accounts");
        }

        [Fact]
        public void ANewDayDrawsNewDailiesAndKeepsTheWeek()
        {
            var book = NewBook();
            book.Refresh(Wed);
            var week = book.State.Of(QuestPeriod.Weekly).Quests[0];
            week.Progress = 5;
            book.State.Of(QuestPeriod.Daily).Quests[0].Progress = 3;
            Assert.False(book.Refresh(Wed.AddHours(8)));    // still the same day (23:30)
            Assert.Equal(3, book.State.Of(QuestPeriod.Daily).Quests[0].Progress);
            Assert.True(book.Refresh(Wed.AddHours(9)));     // past midnight
            Assert.All(book.State.Of(QuestPeriod.Daily).Quests, s => Assert.Equal(0, s.Progress));
            Assert.Same(week, book.State.Of(QuestPeriod.Weekly).Quests[0]);
            Assert.Equal(5, week.Progress);
        }

        [Fact]
        public void KillsCountTowardsMatchingQuestsUpToTheGoal()
        {
            var book = WithDaily("d_monsters", "d_gods", "d_events");
            var notices = new List<QuestNotice>();
            book.Apply(Kill(), notices);
            book.Apply(Kill(god: true), notices);
            var q = book.State.Of(QuestPeriod.Daily).Quests;
            Assert.Equal(2, q[0].Progress);     // any monster
            Assert.Equal(1, q[1].Progress);     // gods only
            Assert.Equal(0, q[2].Progress);     // not a realm event
            Assert.Empty(notices.Where(n => n.Kind == QuestNotice.QuestDone));
            for (var i = 0; i < 1000; i++)
                book.Apply(Kill(god: true), notices);
            Assert.Equal(Data.Quest("d_monsters").Goal, q[0].Progress);
            Assert.Equal(Data.Quest("d_gods").Goal, q[1].Progress);
            // once each (the week's and month's quests may count kills too)
            Assert.Single(notices, n => n.Text.StartsWith(Data.Quest("d_monsters").Title));
            Assert.Single(notices, n => n.Text.StartsWith(Data.Quest("d_gods").Title));
        }

        [Fact]
        public void DungeonFiltersUseDifficultyAndElder()
        {
            var book = WithDaily("d_dungeons", "d_medium");
            book.Apply(new QuestEvent(QuestEvents.Dungeon) { Name = "Pirate Cave", Difficulty = 0 }, null);
            var q = book.State.Of(QuestPeriod.Daily).Quests;
            Assert.Equal((1, 0), (q[0].Progress, q[1].Progress));
            book.Apply(new QuestEvent(QuestEvents.Dungeon) { Name = "Elder Abyss", Difficulty = 4, Elder = true }, null);
            Assert.Equal((2, 1), (q[0].Progress, q[1].Progress));
            Assert.Equal(1, book.AchievementOf("elder").Progress);
            Assert.Equal(2, book.AchievementOf("tourist").Progress);
            book.Apply(new QuestEvent(QuestEvents.Dungeon) { Name = "Pirate Cave" }, null);
            Assert.Equal(2, book.AchievementOf("tourist").Progress);   // the same dungeon again
            Assert.Equal(3, book.AchievementOf("dungeons").Progress);
        }

        [Fact]
        public void MaxQuestsTakeTheBestValue()
        {
            var book = NewBook();
            book.Refresh(Wed);
            book.State.Of(QuestPeriod.Weekly).Quests[0] = new QuestSlot { Id = "w_arena" };
            book.Apply(new QuestEvent(QuestEvents.ArenaWave) { Value = 9 }, null);
            book.Apply(new QuestEvent(QuestEvents.ArenaWave) { Value = 4 }, null);
            Assert.Equal(9, book.State.Of(QuestPeriod.Weekly).Quests[0].Progress);
            Assert.Equal(9, book.AchievementOf("arena").Progress);
        }

        [Fact]
        public void AQuestIsClaimedOnceAndOnlyWhenDone()
        {
            var book = WithDaily("d_events", "d_gods", "d_monsters");
            Assert.False(book.ClaimQuest("d_events", Wed).Ok);
            for (var i = 0; i < 3; i++)
                book.Apply(new QuestEvent(QuestEvents.Kill) { RealmEvent = true }, null);
            var claim = book.ClaimQuest("d_events", Wed);
            Assert.True(claim.Ok);
            Assert.Equal(Data.Quest("d_events").Rewards.Sum(r => r.Fame), claim.Rewards.Sum(r => r.Fame));
            Assert.False(book.ClaimQuest("d_events", Wed).Ok);
            Assert.Equal(1, book.AchievementOf("quests").Progress);
            // claimed quests stop counting, and a quest that isn't drawn can't be claimed
            book.Apply(new QuestEvent(QuestEvents.Kill) { RealmEvent = true }, null);
            Assert.Equal(3, book.State.Of(QuestPeriod.Daily).Quests[0].Progress);
            Assert.False(book.ClaimQuest("w_oryx", Wed).Ok);
        }

        [Fact]
        public void AFinishedQuestIsLostAtTheReset()
        {
            var book = WithDaily("d_events", "d_gods", "d_monsters");
            for (var i = 0; i < 3; i++)
                book.Apply(new QuestEvent(QuestEvents.Kill) { RealmEvent = true }, null);
            // the next day's list is new (if it draws the same quest again, it starts from nothing)
            Assert.False(book.ClaimQuest("d_events", Wed.AddDays(1)).Ok);
            Assert.All(book.State.Of(QuestPeriod.Daily).Quests, s => Assert.Equal(0, s.Progress));
        }

        [Fact]
        public void TheBonusNeedsEveryQuestOfThePeriodClaimed()
        {
            var book = WithDaily("d_events", "d_gods", "d_monsters");
            Assert.False(book.ClaimBonus(QuestPeriod.Daily, Wed).Ok);
            for (var i = 0; i < 1000; i++)
                book.Apply(new QuestEvent(QuestEvents.Kill) { RealmEvent = true, God = true }, null);
            Assert.True(book.ClaimQuest("d_events", Wed).Ok);
            Assert.True(book.ClaimQuest("d_gods", Wed).Ok);
            Assert.False(book.ClaimBonus(QuestPeriod.Daily, Wed).Ok);
            var last = book.ClaimQuest("d_monsters", Wed);
            Assert.Contains(last.Notices, n => n.Kind == QuestNotice.BonusReady);
            Assert.True(book.BonusReady(QuestPeriod.Daily));
            Assert.True(book.ClaimBonus(QuestPeriod.Daily, Wed).Ok);
            Assert.False(book.ClaimBonus(QuestPeriod.Daily, Wed).Ok);
        }

        [Fact]
        public void TheLoginRewardIsOncePerUtcDay()
        {
            var book = NewBook();
            Assert.True(book.LoginReady(Wed));
            var first = book.ClaimLogin(Wed);
            Assert.True(first.Ok);
            Assert.Equal(Data.Login[0].Sum(r => r.Fame), first.Rewards.Sum(r => r.Fame));
            Assert.False(book.LoginReady(Wed.AddHours(8)));
            Assert.False(book.ClaimLogin(Wed.AddHours(8)).Ok);
            Assert.True(book.ClaimLogin(Wed.AddHours(9)).Ok);   // after midnight
            Assert.Equal(2, book.State.Login.Streak);
            Assert.Equal(2, book.State.Login.Step);
            // missing a day ends the streak but not the track
            Assert.True(book.ClaimLogin(Wed.AddDays(3)).Ok);
            Assert.Equal(1, book.State.Login.Streak);
            Assert.Equal(3, book.State.Login.Step);
            Assert.Equal(3, book.AchievementOf("logins").Progress);
            // a clock that goes back doesn't give the reward again
            Assert.False(book.LoginReady(Wed.AddDays(1)));
            Assert.False(book.ClaimLogin(Wed.AddDays(1)).Ok);
        }

        [Fact]
        public void TheLoginTrackWrapsAfterItsLastDay()
        {
            var book = NewBook();
            for (var d = 0; d < Data.Login.Count; d++)
                Assert.True(book.ClaimLogin(Wed.AddDays(d)).Ok);
            Assert.Equal(0, book.State.Login.Step);
            Assert.Equal(Data.Login.Count, book.State.Login.Streak);
            var again = book.ClaimLogin(Wed.AddDays(Data.Login.Count));
            Assert.Equal("Day 1 reward collected!", again.Message);
        }

        [Fact]
        public void AchievementTiersAreClaimedOnceEach()
        {
            var book = NewBook();
            Assert.False(book.ClaimAchievement("oryx").Ok);
            var notices = new List<QuestNotice>();
            for (var i = 0; i < 10; i++)
                book.Apply(new QuestEvent(QuestEvents.Kill) { Oryx = true }, notices);
            Assert.Equal(2, notices.Count(n => n.Kind == QuestNotice.AchievementEarned));
            var claim = book.ClaimAchievement("oryx");
            Assert.True(claim.Ok);
            Assert.Equal(Data.TierRewardsOf(0).Sum(r => r.Fame) + Data.TierRewardsOf(1).Sum(r => r.Fame), claim.Rewards.Sum(r => r.Fame));
            Assert.Equal(2, book.AchievementOf("oryx").Claimed);
            Assert.False(book.ClaimAchievement("oryx").Ok);
            Assert.Equal(Data.TierPoints[0] + Data.TierPoints[1], book.Points);
        }

        [Fact]
        public void RanksUnlockNameColours()
        {
            var book = NewBook();
            Assert.False(book.ChooseColor(1).Ok);
            Assert.Equal(0, book.NameColor);
            // enough points for the second rank
            foreach (var a in Data.Achievements.Take(4))
                book.Apply(new QuestEvent(a.Event) { Value = a.Tiers[1], Amount = a.Tiers[1], Name = "x", God = true, Boss = true, Oryx = true, RealmEvent = true, Elder = true, Difficulty = 5, Group = true, Cube = true }, null);
            var ranks = Data.Achievements.Take(4).Select(a => book.ClaimAchievement(a.Id)).SelectMany(c => c.Notices).Count(n => n.Kind == QuestNotice.RankUp);
            Assert.True(ranks >= 1);
            Assert.True(book.Points >= Data.Ranks[1].Points);
            Assert.True(book.ChooseColor(1).Ok);
            Assert.Equal(Data.Ranks[1].ColorValue, book.NameColor);
            Assert.False(book.ChooseColor(Data.Ranks.Count - 1).Ok);
            Assert.True(book.ChooseColor(-1).Ok);
            Assert.Equal(0, book.NameColor);
        }

        [Fact]
        public void ASwapReplacesAnUnfinishedQuestOncePerPeriod()
        {
            var book = WithDaily("d_events", "d_gods", "d_monsters");
            var swap = book.Reroll("d_gods", Wed);
            Assert.True(swap.Ok, swap.Message);
            var ids = book.State.Of(QuestPeriod.Daily).Quests.Select(s => s.Id).ToList();
            Assert.DoesNotContain("d_gods", ids);
            Assert.Equal(3, ids.Distinct().Count());
            Assert.Equal(3, ids.Select(id => Data.Quest(id).Group).Distinct().Count());
            Assert.False(book.Reroll(ids[1], Wed).Ok);
            // a new day gives a new swap
            book.Refresh(Wed.AddDays(1));
            Assert.True(book.Reroll(book.State.Of(QuestPeriod.Daily).Quests[0].Id, Wed.AddDays(1)).Ok);
        }

        [Fact]
        public void AFinishedQuestCanNotBeSwapped()
        {
            var book = WithDaily("d_events", "d_gods", "d_monsters");
            for (var i = 0; i < 3; i++)
                book.Apply(new QuestEvent(QuestEvents.Kill) { RealmEvent = true }, null);
            Assert.False(book.Reroll("d_events", Wed).Ok);
        }

        [Fact]
        public void StateSurvivesJsonAndQuestsTheDataDroppedAreReplaced()
        {
            var book = WithDaily("d_events", "gone_quest", "d_monsters");
            book.Apply(Kill(), null);
            book.ClaimLogin(Wed);
            var json = book.State.ToJson();
            var back = new QuestBook(Data, QuestState.FromJson(json), 7);
            Assert.Equal(json, back.State.ToJson());
            Assert.True(back.Refresh(Wed));
            var ids = back.State.Of(QuestPeriod.Daily).Quests.Select(s => s.Id).ToList();
            Assert.Equal(3, ids.Count);
            Assert.DoesNotContain("gone_quest", ids);
            Assert.Equal(1, back.State.Of(QuestPeriod.Daily).Quests.First(s => s.Id == "d_monsters").Progress);
            Assert.False(back.ClaimLogin(Wed).Ok);
            Assert.NotNull(QuestState.FromJson("not json"));
        }

        [Fact]
        public void TextsFillInTheGoalAndPlurals()
        {
            Assert.Equal("Help defeat Oryx the Mad God 1 time.", Data.Achievement("oryx").Describe(1));
            Assert.Equal("Help defeat Oryx the Mad God 10 times.", Data.Achievement("oryx").Describe(10));
            Assert.Equal("Reach level 20 with 1 different class.", Data.Achievement("classes").Describe(1));
            Assert.Equal("Defeat 12,000 monsters in realms or dungeons.", Data.Quest("m_monsters").Describe());
            Assert.All(Data.Quests, q => Assert.DoesNotContain("{", q.Describe()));
        }

        [Fact]
        public void DifficultyFollowsBossHealth()
        {
            Assert.Equal(0, Data.DifficultyOf(5000));
            Assert.Equal(1, Data.DifficultyOf(6000));
            Assert.Equal(3, Data.DifficultyOf(60000));
            Assert.Equal(5, Data.DifficultyOf(1000000));
        }
    }
}
