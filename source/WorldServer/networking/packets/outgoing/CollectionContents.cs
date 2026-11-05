using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Collection (CollectionHandler.cs): every piece of gear there is to collect, the ones the
    // account has had, and where the untiered gear comes from: each place's name, the object drawn
    // for it (its portal), its difficulty and its items.
    public class CollectionContents : OutgoingMessage
    {
        public ushort[] All { get; set; }
        public ushort[] Have { get; set; }
        public (string Name, ushort Icon, byte Level, ushort[] Items)[] Groups { get; set; }

        public override MessageId MessageId => MessageId.COLLECTION;

        public override void Write(NetworkWriter wtr)
        {
            WriteTypes(wtr, All);
            WriteTypes(wtr, Have);
            wtr.Write((short)Groups.Length);
            foreach (var (name, icon, level, items) in Groups)
            {
                wtr.WriteUTF16(name);
                wtr.Write(icon);
                wtr.Write(level);
                WriteTypes(wtr, items);
            }
        }

        private static void WriteTypes(NetworkWriter wtr, ushort[] types)
        {
            wtr.Write((short)types.Length);
            foreach (var t in types)
                wtr.Write(t);
        }
    }
}
