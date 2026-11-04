using System.Collections.Generic;
using Shared;
using WorldServer.networking.packets.outgoing.quests;

namespace WorldServer.networking.packets.outgoing.community
{
    public sealed class CommunityTierInfo
    {
        public int At;
        public bool Claimed;
        public List<QuestReward> Rewards = new List<QuestReward>();
    }

    public sealed class CommunityGoalInfo
    {
        public string Key;
        public string Title;
        public string Text;
        public ushort Icon;
        public bool Special;
        public int StartsAt;
        public int EndsAt;
        public bool Running;
        public int Goal;
        public int Total;
        public int Participants;
        public int Minimum;
        public int Mine;
        public int Rank;
        public List<CommunityTierInfo> Tiers = new List<CommunityTierInfo>();
        public byte TopCount;
        public bool TopClaimed;
        public List<QuestReward> TopRewards = new List<QuestReward>();
        public List<(string Name, int Amount)> Top = new List<(string, int)>();
    }

    // The community goals window (after COMMUNITY_REQUEST and each claim): the server's time and
    // when the week ends (Unix seconds), the tiers' names, then each goal running or ended
    // recently: its run's key, what it is and the object it shows, its dates, the total and how
    // many helped, the share an account needs, ours and our place (0: none), each tier's total,
    // whether we've claimed it and its rewards, then the top's size, prize and whether we've
    // claimed it, and the top contributors.
    public class CommunityDataMessage : OutgoingMessage
    {
        public int Now;
        public int WeekEndsAt;
        public List<string> TierNames = new List<string>();
        public List<CommunityGoalInfo> Goals = new List<CommunityGoalInfo>();

        public override MessageId MessageId => MessageId.COMMUNITY_DATA;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Now);
            wtr.Write(WeekEndsAt);
            wtr.Write((byte)TierNames.Count);
            foreach (var n in TierNames)
                wtr.WriteUTF16(n);
            wtr.Write((byte)Goals.Count);
            foreach (var g in Goals)
            {
                wtr.WriteUTF16(g.Key);
                wtr.WriteUTF16(g.Title ?? "");
                wtr.WriteUTF16(g.Text ?? "");
                wtr.Write(g.Icon);
                wtr.Write(g.Special);
                wtr.Write(g.StartsAt);
                wtr.Write(g.EndsAt);
                wtr.Write(g.Running);
                wtr.Write(g.Goal);
                wtr.Write(g.Total);
                wtr.Write(g.Participants);
                wtr.Write(g.Minimum);
                wtr.Write(g.Mine);
                wtr.Write(g.Rank);
                wtr.Write((byte)g.Tiers.Count);
                foreach (var t in g.Tiers)
                {
                    wtr.Write(t.At);
                    wtr.Write(t.Claimed);
                    QuestReward.WriteList(wtr, t.Rewards);
                }
                wtr.Write(g.TopCount);
                wtr.Write(g.TopClaimed);
                QuestReward.WriteList(wtr, g.TopRewards);
                wtr.Write((byte)g.Top.Count);
                foreach (var (name, amount) in g.Top)
                {
                    wtr.WriteUTF16(name ?? "");
                    wtr.Write(amount);
                }
            }
        }
    }

    // A popup or the window's answer: a claim paid (0) or refused (1), a tier reached by the
    // server (2), a goal completed (3), the week's new goals (4).
    public class CommunityNotice : OutgoingMessage
    {
        public const byte CLAIMED = 0, REFUSED = 1, TIER = 2, COMPLETE = 3, NEW_WEEK = 4;

        public byte Kind;
        public string Title;
        public string Text;

        public override MessageId MessageId => MessageId.COMMUNITY_NOTICE;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Kind);
            wtr.WriteUTF16(Title ?? "");
            wtr.WriteUTF16(Text ?? "");
        }
    }
}
