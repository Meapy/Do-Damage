using System.Collections.Generic;
using Shared;
using Shared.quests;

namespace WorldServer.networking.packets.outgoing.quests
{
    // The answer to QUEST_CLAIM: whether it worked, and what to tell the player.
    public class QuestResult : OutgoingMessage
    {
        public bool Success;
        public string Message;

        public override MessageId MessageId => MessageId.QUEST_RESULT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Success);
            wtr.WriteUTF16(Message ?? "");
        }
    }

    // A popup (QuestNotice's kinds: a quest done, an achievement earned, a rank, a bonus).
    public class QuestNoticeMessage : OutgoingMessage
    {
        public byte Kind;
        public string Title;
        public string Text;

        public override MessageId MessageId => MessageId.QUEST_NOTICE;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Kind);
            wtr.WriteUTF16(Title ?? "");
            wtr.WriteUTF16(Text ?? "");
        }
    }

    // Progress that changed since the last QUESTS_DATA or QUEST_PROGRESS: a quest (kind 0) or an
    // achievement (1) by its place in QUESTS_DATA's lists, and its progress now.
    public class QuestProgress : OutgoingMessage
    {
        public List<QuestChange> Changes = new List<QuestChange>();

        public override MessageId MessageId => MessageId.QUEST_PROGRESS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((short)Changes.Count);
            foreach (var c in Changes)
            {
                wtr.Write(c.Kind);
                wtr.Write((ushort)c.Index);
                wtr.Write(c.Progress);
            }
        }
    }
}
