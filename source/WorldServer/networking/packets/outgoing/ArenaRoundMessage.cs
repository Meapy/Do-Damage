using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Arena's rounds (Season 2, ArenaSeason2), for clients that asked for its votes (ARENA_VOTE
    // 255). Kind: 0 the next fight, while the votes are open; 1 a fight beginning; 2 an Empowered
    // boss rising in it (one entry: its name and type); 3 a boss rush's next boss (likewise). Then
    // the season, the fight's first and last wave, and each round: its wave, its title (the
    // encounter's name), its shape (ArenaFormat) and twist (ArenaTwist), and its bosses' types.
    public sealed class ArenaRoundMessage : OutgoingMessage
    {
        public const byte Next = 0, Begins = 1, EmpoweredRises = 2, NextChallenger = 3;

        public byte Kind { get; set; }
        public byte Season { get; set; }
        public int Wave { get; set; }
        public int LastWave { get; set; }
        public (int Wave, string Title, byte Format, byte Twist, ushort[] Bosses)[] Rounds { get; set; }

        public override MessageId MessageId => MessageId.ARENA_ROUND;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Kind);
            wtr.Write(Season);
            wtr.Write(Wave);
            wtr.Write(LastWave);
            wtr.Write((byte)System.Math.Min(255, Rounds.Length));
            for (var i = 0; i < System.Math.Min(255, Rounds.Length); i++)
            {
                var (wave, title, format, twist, bosses) = Rounds[i];
                wtr.Write(wave);
                wtr.WriteUTF16(title ?? "");
                wtr.Write(format);
                wtr.Write(twist);
                var n = System.Math.Min(255, bosses.Length);
                wtr.Write((byte)n);
                for (var b = 0; b < n; b++)
                    wtr.Write(bosses[b]);
            }
        }
    }
}
