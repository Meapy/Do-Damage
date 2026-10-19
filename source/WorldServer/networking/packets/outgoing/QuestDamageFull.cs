using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // Everyone who hit the quest (name, class, damage; most first), its maximum health, our own
    // damage, and whether it has just died, for clients that asked (QUEST_DAMAGE_FULL_SUBSCRIBE).
    public class QuestDamageFull : OutgoingMessage
    {
        public int ObjectId { get; set; }
        public ushort ObjectType { get; set; }
        public int MaxHp { get; set; }
        public bool Dead { get; set; }
        public (string Name, ushort Class, int Damage)[] Hitters { get; set; }
        public int Mine { get; set; }

        public override MessageId MessageId => MessageId.QUEST_DAMAGE_FULL;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(ObjectId);
            wtr.Write(ObjectType);
            wtr.Write(MaxHp);
            wtr.Write(Dead);
            wtr.Write((byte)Hitters.Length);
            foreach (var (name, cls, damage) in Hitters)
            {
                wtr.WriteUTF16(name);
                wtr.Write(cls);
                wtr.Write(damage);
            }
            wtr.Write(Mine);
        }
    }
}
