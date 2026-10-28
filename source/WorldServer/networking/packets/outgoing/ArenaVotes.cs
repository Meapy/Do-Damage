using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Arena's votes between waves (DoomedBattleArena), for clients that asked (ARENA_VOTE 255):
    // whether voting is open, the wave the next fight starts at, the most waves it may combine and
    // how many it combines as the votes stand, how many Next votes start it and how many players
    // a combine needs, whether it's starting (the countdown was cut), then each player's votes:
    // Next or not, and their combine pick (0 for none).
    public sealed class ArenaVotes : OutgoingMessage
    {
        public bool Open { get; set; }
        public int NextWave { get; set; }
        public byte MaxCombine { get; set; }
        public byte Combine { get; set; }
        public byte NextNeeded { get; set; }
        public byte CombineNeeded { get; set; }
        public bool Starting { get; set; }
        public (int ObjectId, string Name, bool Next, byte Pick)[] Entries { get; set; }

        public override MessageId MessageId => MessageId.ARENA_VOTES;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Open);
            wtr.Write(NextWave);
            wtr.Write(MaxCombine);
            wtr.Write(Combine);
            wtr.Write(NextNeeded);
            wtr.Write(CombineNeeded);
            wtr.Write(Starting);
            wtr.Write((byte)Entries.Length);
            foreach (var (id, name, next, pick) in Entries)
            {
                wtr.Write(id);
                wtr.WriteUTF16(name ?? "");
                wtr.Write(next);
                wtr.Write(pick);
            }
        }
    }
}
