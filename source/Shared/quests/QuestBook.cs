using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.quests
{
    // A popup for the player: a quest done, an achievement tier earned, a new rank.
    public sealed class QuestNotice
    {
        public const byte QuestDone = 0, AchievementEarned = 1, LoginReady = 2, RankUp = 3, BonusReady = 4;

        public byte Kind;
        public string Title;
        public string Text;

        public QuestNotice(byte kind, string title, string text)
        {
            Kind = kind;
            Title = title;
            Text = text;
        }
    }

    // A quest's or achievement's progress that changed, by its place in the lists QuestBook
    // shows (Current, Data.Achievements): for QUEST_PROGRESS.
    public readonly struct QuestChange
    {
        public const byte Quest = 0, Achievement = 1;

        public readonly byte Kind;
        public readonly int Index;
        public readonly int Progress;

        public QuestChange(byte kind, int index, int progress)
        {
            Kind = kind;
            Index = index;
            Progress = progress;
        }
    }

    public sealed class ClaimResult
    {
        public bool Ok;
        public string Message;
        public List<RewardDef> Rewards = new List<RewardDef>();
        public List<QuestNotice> Notices = new List<QuestNotice>();

        public static ClaimResult Fail(string message) => new ClaimResult { Ok = false, Message = message };
    }

    // The rules, without the game: drawing each period's quests, counting events, and checking
    // and recording claims. The world server (QuestTracker) feeds it events and pays the rewards.
    public sealed class QuestBook
    {
        public readonly QuestData Data;
        public readonly QuestState State;
        // the account's id: each account draws its own quests
        private readonly int _seed;

        public QuestBook(QuestData data, QuestState state, int seed)
        {
            Data = data;
            State = state;
            _seed = seed;
        }

        // Draws new quests for each period that has reset since (and replaces quests the data
        // no longer has). True if anything changed.
        public bool Refresh(DateTime now)
        {
            var changed = false;
            foreach (QuestPeriod p in Enum.GetValues(typeof(QuestPeriod)))
            {
                var ps = State.Of(p);
                var key = QuestPeriods.Key(p, now);
                if (ps.Key != key)
                {
                    ps.Key = key;
                    ps.Quests = Draw(p, key, Data.SlotsOf(p), new HashSet<string>(), new HashSet<string>(), 0);
                    ps.BonusClaimed = false;
                    ps.Rerolls = 0;
                    changed = true;
                    continue;
                }
                var gone = ps.Quests.RemoveAll(s => Data.Quest(s.Id)?.PeriodKind != p);
                if (gone > 0 || ps.Quests.Count < Data.SlotsOf(p))
                {
                    var ids = new HashSet<string>(ps.Quests.Select(s => s.Id));
                    var groups = new HashSet<string>(ps.Quests.Select(s => GroupOf(Data.Quest(s.Id))));
                    ps.Quests.AddRange(Draw(p, key, Data.SlotsOf(p) - ps.Quests.Count, ids, groups, 100 + gone));
                    changed = true;
                }
            }
            return changed;
        }

        private static string GroupOf(QuestDef q) => q.Group ?? q.Id;

        // `count` quests of the period, by weight, no two of a group, none of `ids` or `groups`.
        private List<QuestSlot> Draw(QuestPeriod p, int key, int count, HashSet<string> ids, HashSet<string> groups, int salt)
        {
            var rng = new Random(Mix(_seed, (int)p, key, salt));
            var pool = Data.Pool(p).Where(q => !ids.Contains(q.Id) && !groups.Contains(GroupOf(q)) && q.Weight > 0).ToList();
            var drawn = new List<QuestSlot>();
            while (drawn.Count < count && pool.Count > 0)
            {
                var roll = rng.NextDouble() * pool.Sum(q => q.Weight);
                var pick = pool[pool.Count - 1];
                foreach (var q in pool)
                {
                    roll -= q.Weight;
                    if (roll < 0)
                    {
                        pick = q;
                        break;
                    }
                }
                drawn.Add(new QuestSlot { Id = pick.Id });
                pool.RemoveAll(q => GroupOf(q) == GroupOf(pick));
            }
            return drawn;
        }

        // A seed that is the same on every run (string.GetHashCode isn't).
        private static int Mix(params int[] parts)
        {
            unchecked
            {
                var h = (int)2166136261;
                foreach (var v in parts)
                {
                    h = (h ^ v) * 16777619;
                    h ^= h >> 13;
                }
                return h & 0x7FFFFFFF;
            }
        }

        // Every quest of the current periods, daily first: the order the window lists them in.
        public IEnumerable<(QuestPeriod Period, QuestSlot Slot, QuestDef Def)> Current()
        {
            foreach (QuestPeriod p in Enum.GetValues(typeof(QuestPeriod)))
                foreach (var slot in State.Of(p).Quests)
                {
                    var def = Data.Quest(slot.Id);
                    if (def != null)
                        yield return (p, slot, def);
                }
        }

        public AchievementState AchievementOf(string id)
        {
            if (!State.Achievements.TryGetValue(id, out var a))
                State.Achievements[id] = a = new AchievementState();
            return a;
        }

        // Tiers whose goal the progress has reached.
        public static int Earned(AchievementDef def, int progress) => def.Tiers.TakeWhile(t => progress >= t).Count();

        // Counts an event towards the current quests and the achievements; what changed, and a
        // notice for each quest finished and achievement tier earned.
        public List<QuestChange> Apply(in QuestEvent e, List<QuestNotice> notices)
        {
            var changes = new List<QuestChange>();
            var i = 0;
            foreach (var (_, slot, def) in Current())
            {
                var index = i++;
                if (slot.Claimed || !def.Counts(e))
                    continue;
                var before = slot.Progress;
                var after = def.IsMax ? Math.Max(before, e.Value) : (int)Math.Min((long)before + e.Amount, def.Goal);
                after = Math.Min(after, def.Goal);
                if (after == before)
                    continue;
                slot.Progress = after;
                changes.Add(new QuestChange(QuestChange.Quest, index, after));
                if (before < def.Goal && after >= def.Goal)
                    notices?.Add(new QuestNotice(QuestNotice.QuestDone, "Quest complete!", $"{def.Title}: claim your reward in the quest log."));
            }
            for (var k = 0; k < Data.Achievements.Count; k++)
            {
                var def = Data.Achievements[k];
                if (!def.Counts(e))
                    continue;
                var st = AchievementOf(def.Id);
                var before = st.Progress;
                if (def.IsDistinct)
                {
                    if (string.IsNullOrEmpty(e.Name))
                        continue;
                    st.Seen ??= new List<string>();
                    if (!st.Seen.Contains(e.Name))
                        st.Seen.Add(e.Name);
                    st.Progress = st.Seen.Count;
                }
                else if (def.IsMax)
                    st.Progress = Math.Max(st.Progress, e.Value);
                else
                    st.Progress = (int)Math.Min((long)st.Progress + e.Amount, int.MaxValue);
                if (st.Progress == before)
                    continue;
                changes.Add(new QuestChange(QuestChange.Achievement, k, st.Progress));
                for (var t = Earned(def, before); t < Earned(def, st.Progress); t++)
                    notices?.Add(new QuestNotice(QuestNotice.AchievementEarned, "Achievement earned!", $"{def.Title} ({Data.TierName(t)}): {def.Describe(def.Tiers[t])}"));
            }
            return changes;
        }

        // An achievement's progress from before achievements existed (QuestTracker reads it from
        // the account's characters): kept if higher, with no notices.
        public void Seed(string id, int progress)
        {
            if (Data.Achievement(id) == null)
                return;
            var st = AchievementOf(id);
            st.Progress = Math.Max(st.Progress, progress);
        }

        public int Points
        {
            get
            {
                var points = 0;
                foreach (var def in Data.Achievements)
                    if (State.Achievements.TryGetValue(def.Id, out var st))
                        for (var t = 0; t < Math.Min(st.Claimed, def.Tiers.Length); t++)
                            points += Data.TierPointsOf(t);
                return points;
            }
        }

        public int RankOf(int points)
        {
            var rank = 0;
            for (var i = 0; i < Data.Ranks.Count; i++)
                if (points >= Data.Ranks[i].Points)
                    rank = i;
            return rank;
        }

        // The chat name colour the chosen rank gives (0: the default).
        public int NameColor => State.ColorRank >= 0 && State.ColorRank < Data.Ranks.Count && State.ColorRank <= RankOf(Points)
            ? Data.Ranks[State.ColorRank].ColorValue : 0;

        public bool LoginReady(DateTime now) => Data.Login.Count > 0 && State.Login.LastDay != QuestPeriods.DayKey(now);

        public bool BonusReady(QuestPeriod p)
        {
            var ps = State.Of(p);
            return !ps.BonusClaimed && ps.Quests.Count > 0 && ps.Quests.All(s => s.Claimed) && Data.BonusOf(p).Count > 0;
        }

        // How many rewards wait to be claimed (the HUD's badge, and the NPCs' greetings).
        public int ReadyCount(DateTime now)
        {
            var n = Current().Count(q => !q.Slot.Claimed && q.Slot.Progress >= q.Def.Goal);
            n += Enum.GetValues(typeof(QuestPeriod)).Cast<QuestPeriod>().Count(BonusReady);
            n += LoginReady(now) ? 1 : 0;
            n += Data.Achievements.Count(a => State.Achievements.TryGetValue(a.Id, out var st) && Earned(a, st.Progress) > st.Claimed);
            return n;
        }

        public ClaimResult ClaimQuest(string id, DateTime now)
        {
            Refresh(now);
            var found = Current().FirstOrDefault(q => q.Slot.Id == id);
            if (found.Slot == null)
                return ClaimResult.Fail("That quest has ended.");
            if (found.Slot.Claimed)
                return ClaimResult.Fail("You already claimed that quest.");
            if (found.Slot.Progress < found.Def.Goal)
                return ClaimResult.Fail("That quest isn't finished yet.");
            found.Slot.Claimed = true;
            var result = new ClaimResult { Ok = true, Message = $"{found.Def.Title} complete!", Rewards = found.Def.Rewards.ToList() };
            WithRank(result, () => Apply(new QuestEvent(QuestEvents.Quest), result.Notices));
            if (BonusReady(found.Period))
                result.Notices.Add(new QuestNotice(QuestNotice.BonusReady, "Bonus unlocked!", $"Every {QuestData.Name(found.Period)} quest is done: claim the bonus in the quest log."));
            return result;
        }

        public ClaimResult ClaimBonus(QuestPeriod p, DateTime now)
        {
            Refresh(now);
            var ps = State.Of(p);
            if (ps.BonusClaimed)
                return ClaimResult.Fail("You already claimed this bonus.");
            if (!BonusReady(p))
                return ClaimResult.Fail($"Claim every {QuestData.Name(p)} quest first.");
            ps.BonusClaimed = true;
            return new ClaimResult { Ok = true, Message = $"{Capital(QuestData.Name(p))} bonus claimed!", Rewards = Data.BonusOf(p).ToList() };
        }

        public ClaimResult ClaimLogin(DateTime now)
        {
            var today = QuestPeriods.DayKey(now);
            var login = State.Login;
            if (Data.Login.Count == 0)
                return ClaimResult.Fail("There are no daily rewards.");
            if (login.LastDay == today)
                return ClaimResult.Fail("You already collected today's reward. Come back tomorrow!");
            var day = login.Step % Data.Login.Count;
            login.Streak = login.LastDay == today - 1 ? login.Streak + 1 : 1;
            login.LastDay = today;
            login.Step = (day + 1) % Data.Login.Count;
            login.Total++;
            var result = new ClaimResult { Ok = true, Message = $"Day {day + 1} reward collected!", Rewards = Data.Login[day].ToList() };
            WithRank(result, () => Apply(new QuestEvent(QuestEvents.Login), result.Notices));
            return result;
        }

        public ClaimResult ClaimAchievement(string id)
        {
            var def = Data.Achievement(id);
            if (def == null)
                return ClaimResult.Fail("There's no such achievement.");
            var st = AchievementOf(id);
            var earned = Earned(def, st.Progress);
            if (st.Claimed >= earned)
                return ClaimResult.Fail(st.Claimed >= def.Tiers.Length ? "You already claimed every tier." : "You haven't earned the next tier yet.");
            var result = new ClaimResult { Ok = true };
            WithRank(result, () =>
            {
                for (var t = st.Claimed; t < earned; t++)
                    result.Rewards.AddRange(Data.TierRewardsOf(t));
                st.Claimed = earned;
            });
            result.Message = $"{def.Title} ({Data.TierName(earned - 1)}) claimed!";
            return result;
        }

        // Swaps an unfinished quest for another of its period, a few times a period.
        public ClaimResult Reroll(string id, DateTime now)
        {
            Refresh(now);
            var found = Current().FirstOrDefault(q => q.Slot.Id == id);
            if (found.Slot == null)
                return ClaimResult.Fail("That quest has ended.");
            var ps = State.Of(found.Period);
            if (found.Slot.Claimed || found.Slot.Progress >= found.Def.Goal)
                return ClaimResult.Fail("Claim that quest instead: it's done.");
            if (ps.Rerolls >= Data.RerollsOf(found.Period))
                return ClaimResult.Fail($"You've used this {PeriodNoun(found.Period)}'s swap.");
            var ids = new HashSet<string>(ps.Quests.Select(s => s.Id));
            var groups = new HashSet<string>(ps.Quests.Where(s => s != found.Slot).Select(s => GroupOf(Data.Quest(s.Id) ?? new QuestDef { Id = s.Id })));
            var next = Draw(found.Period, ps.Key, 1, ids, groups, 1 + ps.Rerolls);
            if (next.Count == 0)
                return ClaimResult.Fail("There's no other quest to swap it for.");
            ps.Quests[ps.Quests.IndexOf(found.Slot)] = next[0];
            ps.Rerolls++;
            return new ClaimResult { Ok = true, Message = $"Swapped for {Data.Quest(next[0].Id).Title}." };
        }

        public ClaimResult ChooseColor(int rank)
        {
            if (rank < 0)
            {
                State.ColorRank = -1;
                return new ClaimResult { Ok = true, Message = "Your name has its usual colour again." };
            }
            if (rank >= Data.Ranks.Count || Data.Ranks[rank].ColorValue == 0)
                return ClaimResult.Fail("That rank has no colour.");
            if (rank > RankOf(Points))
                return ClaimResult.Fail($"Reach {Data.Ranks[rank].Name} first.");
            State.ColorRank = rank;
            return new ClaimResult { Ok = true, Message = $"Your name now shows in {Data.Ranks[rank].Name} colours." };
        }

        // Runs a change and adds a notice if it raised the rank.
        private void WithRank(ClaimResult result, Action change)
        {
            var before = RankOf(Points);
            change();
            var after = RankOf(Points);
            if (after > before)
                result.Notices.Add(new QuestNotice(QuestNotice.RankUp, $"Rank up: {Data.Ranks[after].Name}!",
                    Data.Ranks[after].ColorValue != 0 ? "A new name colour is yours: pick it under Achievements." : "Keep going!"));
        }

        public static string PeriodNoun(QuestPeriod p) => p switch { QuestPeriod.Daily => "day", QuestPeriod.Weekly => "week", _ => "month" };

        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
