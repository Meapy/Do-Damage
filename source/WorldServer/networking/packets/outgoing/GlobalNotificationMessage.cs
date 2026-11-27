using Shared;

namespace WorldServer.networking.packets.outgoing
{
    public sealed class GlobalNotificationMessage : OutgoingMessage
    {
        private readonly int Type;
        private readonly string Text;

        // Type 0: a dungeon key (its colour in Text). Type 1: a gold discount runs, Text
        // "<percent off>|<seconds left>|<its name>": the Rust client shows a banner (goldsale.rs);
        // older clients take only key colours from this message, so they ignore it.
        public const int GoldSale = 1;

        public override MessageId MessageId => MessageId.GLOBAL_NOTIFICATION;

        public static GlobalNotificationMessage GoldSaleBanner(int percentOff, System.TimeSpan left, string name) =>
            new GlobalNotificationMessage(GoldSale, $"{percentOff}|{(int)left.TotalSeconds}|{name}");

        public GlobalNotificationMessage(int type, string text)
        {
            Type = type;
            Text = text;
        }

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Type);
            wtr.WriteUTF16(Text);
        }
    }
}
