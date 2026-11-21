using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Crucible's climb (CrucibleWorld), to everyone in it, for the Rust client's banners, its
    // floor tracker and its sky (crucible.rs, cruciblesky.rs). Kind (below); the floor (1 to 5, 0
    // the Threshold); a cue (a phase of the fight, Shared.crucible.CrucibleCues); the champion's
    // object type; seconds (a countdown, a floor's time or the run's); and the English of what's
    // shown, for a client that doesn't know the cue.
    public sealed class CrucibleMessage : OutgoingMessage
    {
        // Lobby: the Threshold's countdown (seconds; 0 waiting for players). Ascend: the climb to
        // `floor` begins (the client's warp; players move halfway through). Floor: its champion
        // arrives. Cue: a phase begins. Cleared: the floor's champion fell (seconds: the floor's
        // time). Rest: the rest before the next ascent (seconds left). Victory: the Sovereign fell
        // (seconds: the whole climb). Status: where the climb is, for whoever just arrived
        // (seconds: since it began).
        public const byte Lobby = 0, Ascend = 1, Floor = 2, Cue = 3, Cleared = 4, Rest = 5, Victory = 6, Status = 7;

        public byte Kind { get; set; }
        public byte FloorNumber { get; set; }
        public byte CueId { get; set; }
        public ushort Champion { get; set; }
        public int Seconds { get; set; }
        public string Text { get; set; } = "";

        public override MessageId MessageId => MessageId.CRUCIBLE;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Kind);
            wtr.Write(FloorNumber);
            wtr.Write(CueId);
            wtr.Write(Champion);
            wtr.Write(Seconds);
            wtr.WriteUTF16(Text ?? "");
        }
    }
}
