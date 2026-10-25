using System.Collections.Generic;
using Shared;

namespace WorldServer.networking.packets.outgoing.quests
{
    // A reward as the window shows it: fame (kind 0, amount) or an item (kind 1, its type and how
    // many).
    public struct QuestReward
    {
        public byte Kind;
        public ushort Item;
        public int Amount;

        public void Write(NetworkWriter wtr)
        {
            wtr.Write(Kind);
            wtr.Write(Item);
            wtr.Write(Amount);
        }

        public static void WriteList(NetworkWriter wtr, List<QuestReward> rewards)
        {
            wtr.Write((byte)rewards.Count);
            foreach (var r in rewards)
                r.Write(wtr);
        }
    }

    public sealed class QuestRank
    {
        public string Name;
        public int Points;
        public int Color;
    }

    public sealed class QuestPeriodInfo
    {
        public byte Period;
        public int ResetsAt;
        public byte RerollsLeft;
        public bool BonusClaimed;
        public List<QuestReward> Bonus = new List<QuestReward>();
    }

    public sealed class QuestInfo
    {
        public byte Period;
        public string Id;
        public string Title;
        public string Text;
        public int Progress;
        public int Goal;
        public bool Claimed;
        public List<QuestReward> Rewards = new List<QuestReward>();
    }

    public sealed class AchievementInfo
    {
        public string Id;
        public string Category;
        public string Title;
        public string Text;
        public byte Claimed;
        public int Progress;
        public List<int> Goals = new List<int>();
        public int NextPoints;
        public List<QuestReward> NextRewards = new List<QuestReward>();
    }

    // Everything the quest window shows (QuestTracker.BuildData), after QUESTS_REQUEST, a claim,
    // or a reset: the time, the rank and its colours, each period's reset and bonus, the quests,
    // the daily reward track and the achievements. QUEST_PROGRESS then updates the progress of
    // quests and achievements by their place in these lists.
    public class QuestsData : OutgoingMessage
    {
        public int Now;
        public byte Rank;
        public int Points;
        public byte ColorRank;  // 255: none chosen
        public List<QuestRank> Ranks = new List<QuestRank>();
        public List<QuestPeriodInfo> Periods = new List<QuestPeriodInfo>();
        public List<QuestInfo> Quests = new List<QuestInfo>();
        public byte LoginDay;   // the track's next day, from 0
        public bool LoginReady;
        public int LoginStreak;
        public int LoginTotal;
        public int LoginNextAt;
        public List<(bool Milestone, List<QuestReward> Rewards)> LoginDays = new List<(bool, List<QuestReward>)>();
        public List<AchievementInfo> Achievements = new List<AchievementInfo>();

        public override MessageId MessageId => MessageId.QUESTS_DATA;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Now);
            wtr.Write(Rank);
            wtr.Write(Points);
            wtr.Write(ColorRank);
            wtr.Write((byte)Ranks.Count);
            foreach (var r in Ranks)
            {
                wtr.WriteUTF16(r.Name);
                wtr.Write(r.Points);
                wtr.Write(r.Color);
            }
            wtr.Write((byte)Periods.Count);
            foreach (var p in Periods)
            {
                wtr.Write(p.Period);
                wtr.Write(p.ResetsAt);
                wtr.Write(p.RerollsLeft);
                wtr.Write(p.BonusClaimed);
                QuestReward.WriteList(wtr, p.Bonus);
            }
            wtr.Write((byte)Quests.Count);
            foreach (var q in Quests)
            {
                wtr.Write(q.Period);
                wtr.WriteUTF16(q.Id);
                wtr.WriteUTF16(q.Title);
                wtr.WriteUTF16(q.Text);
                wtr.Write(q.Progress);
                wtr.Write(q.Goal);
                wtr.Write(q.Claimed);
                QuestReward.WriteList(wtr, q.Rewards);
            }
            wtr.Write(LoginDay);
            wtr.Write(LoginReady);
            wtr.Write(LoginStreak);
            wtr.Write(LoginTotal);
            wtr.Write(LoginNextAt);
            wtr.Write((byte)LoginDays.Count);
            foreach (var (milestone, rewards) in LoginDays)
            {
                wtr.Write(milestone);
                QuestReward.WriteList(wtr, rewards);
            }
            wtr.Write((byte)Achievements.Count);
            foreach (var a in Achievements)
            {
                wtr.WriteUTF16(a.Id);
                wtr.WriteUTF16(a.Category);
                wtr.WriteUTF16(a.Title);
                wtr.WriteUTF16(a.Text);
                wtr.Write(a.Claimed);
                wtr.Write(a.Progress);
                wtr.Write((byte)a.Goals.Count);
                foreach (var g in a.Goals)
                    wtr.Write(g);
                wtr.Write(a.NextPoints);
                QuestReward.WriteList(wtr, a.NextRewards);
            }
        }
    }
}
