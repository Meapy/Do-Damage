using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // Someone's /visit, for a client that can show it (VISIT_ANSWER 255): the request's id, who
    // asks, where they'd join us and how many seconds there are to answer (0: the request has
    // gone, so hide it); then, for a client that can show summons (VISIT_ANSWER 254), whether it's
    // a /summon (to their world) rather than a /visit. Clients before 2.3.8 never get that byte.
    public sealed class VisitRequest : OutgoingMessage
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string World { get; set; }
        public byte Seconds { get; set; }
        public bool? Summon { get; set; }

        public override MessageId MessageId => MessageId.VISIT_REQUEST;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Id);
            wtr.WriteUTF16(Name ?? "");
            wtr.WriteUTF16(World ?? "");
            wtr.Write(Seconds);
            if (Summon.HasValue)
                wtr.Write(Summon.Value);
        }
    }
}
