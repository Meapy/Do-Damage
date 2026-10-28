using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // A realm's events for clients that asked (REALM_EVENTS_SUBSCRIBE): how many are defeated and
    // how many close it, the one the player picked for the quest arrow (0: the nearest), and each
    // running event (kind 0), side quest (1, the Lost Sentry) and Realm 2 beacon (2), with where it is.
    public class RealmEvents : OutgoingMessage
    {
        public int Defeated { get; set; }
        public int Needed { get; set; }
        public int Picked { get; set; }
        public (byte Kind, int ObjectId, ushort ObjectType, string Name, float X, float Y)[] Entries { get; set; }

        public override MessageId MessageId => MessageId.REALM_EVENTS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Defeated);
            wtr.Write(Needed);
            wtr.Write(Picked);
            wtr.Write((byte)Entries.Length);
            foreach (var (kind, id, type, name, x, y) in Entries)
            {
                wtr.Write(kind);
                wtr.Write(id);
                wtr.Write(type);
                wtr.WriteUTF16(name ?? "");
                wtr.Write(x);
                wtr.Write(y);
            }
        }
    }
}
