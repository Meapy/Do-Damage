using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Scrying Glass (core/spectate/SpectatorHub.cs, MessageIds SPECTATE): who it shows now.
    // A new serial starts the picture afresh (a new world, or someone else); TargetId 0 shows no
    // one. The world's name, its map's name, size (as MapInfo's, the larger side) and background.
    public sealed class SpectateInfo : OutgoingMessage
    {
        public ushort Serial { get; set; }
        public int TargetId { get; set; }
        public string Name { get; set; }
        public string World { get; set; }
        public string Map { get; set; }
        public short Size { get; set; }
        public byte Background { get; set; }
        public ushort ObjectType { get; set; }

        public override MessageId MessageId => MessageId.SPECTATE_INFO;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Serial);
            wtr.Write(TargetId);
            wtr.WriteUTF16(Name ?? "");
            wtr.WriteUTF16(World ?? "");
            wtr.WriteUTF16(Map ?? "");
            wtr.Write(Size);
            wtr.Write(Background);
            wtr.Write(ObjectType);
        }
    }

    // A message of what the watched player sees, for the picture of that serial: the message's
    // own id, then its body as the player gets it.
    public sealed class SpectateFeed : OutgoingMessage
    {
        private readonly ushort _serial;
        private readonly OutgoingMessage _inner;

        public SpectateFeed(ushort serial, OutgoingMessage inner)
        {
            _serial = serial;
            _inner = inner;
        }

        public override MessageId MessageId => MessageId.SPECTATE_FEED;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(_serial);
            wtr.Write((byte)_inner.MessageId);
            _inner.Write(wtr);
        }
    }

    // Where the shown player is in the list of those who can be watched (from 1; 0 when no one),
    // how many there are, how many have the big view open, and whether this viewer picked them
    // (the big view's arrows) or the glass did.
    public sealed class SpectateStatus : OutgoingMessage
    {
        public byte Index { get; set; }
        public byte Count { get; set; }
        public ushort Watchers { get; set; }
        public bool Picked { get; set; }

        public override MessageId MessageId => MessageId.SPECTATE_STATUS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Index);
            wtr.Write(Count);
            wtr.Write(Watchers);
            wtr.Write(Picked);
        }
    }

    // A line of the big view's chat: who said it (empty for the glass's own news), their rank
    // stars and name colour, and the text. Kind 0 is a player's line, 1 someone came to watch,
    // 2 someone left (Name is who; Text empty), so the client words those.
    public sealed class SpectateChat : OutgoingMessage
    {
        public byte Kind { get; set; }
        public string Name { get; set; }
        public int Stars { get; set; }
        public int NameColor { get; set; }
        public string Text { get; set; }

        public override MessageId MessageId => MessageId.SPECTATE_CHAT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Kind);
            wtr.WriteUTF16(Name ?? "");
            wtr.Write(Stars);
            wtr.Write(NameColor);
            wtr.WriteUTF16(Text ?? "");
        }
    }
}
