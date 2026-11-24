using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Pen Keeper's answer (Player.PetPen.cs): the pet (0 for none), its size (0 Tiny to 3
    // Large, 255 any other; BaseSize the same, from when pets were raised), the shards he keeps for
    // the account and how many an Ambrosia takes, whether the last act worked and what to say about
    // it (empty for a plain ask). Then, from 2.6.4 (older clients stop before it), the pet's level
    // (Shared.pets.PetLevels): its level and last, the food it has eaten, the food at its level and
    // at the next, and what it does now and at its last level: damage a second, HP and MP healed
    // and how often (ms); then the biggest pets the account's pen takes (PetLevels.Sizes' index)
    // and what growing it to the next size costs in fame and in gold (0 at the last). Then, from
    // 2.6.5, the pet's size as grown (PetLevels.Sizes' index, 255 none) and what growing the pet
    // to the next size costs in fame and in gold (0 unless it's at its last level and can grow).
    // Then, from 2.6.7, how the pet is tuned (PetLevels.Tune: steps of damage, HP and MP healing)
    // and what a step costs in fame (0 unless it's Huge and at its last level).
    public class PetPenData : OutgoingMessage
    {
        public ushort Pet { get; set; }
        public byte BaseSize { get; set; }
        public byte Size { get; set; }
        public int Shards { get; set; }
        public byte ShardsNeeded { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; } = "";

        public byte Level { get; set; }
        public byte MaxLevel { get; set; }
        public int Xp { get; set; }
        public int XpLevel { get; set; }
        public int XpNext { get; set; }
        public int Dps { get; set; }
        public int DpsMax { get; set; }
        public short Hp { get; set; }
        public short HpMax { get; set; }
        public short HpMs { get; set; }
        public short Mp { get; set; }
        public short MpMax { get; set; }
        public short MpMs { get; set; }
        public byte PenSize { get; set; }
        public int PenFame { get; set; }
        public int PenGold { get; set; }
        public byte PetSize { get; set; } = 255;
        public int GrowFame { get; set; }
        public int GrowGold { get; set; }
        public sbyte TuneDamage { get; set; }
        public sbyte TuneHp { get; set; }
        public sbyte TuneMp { get; set; }
        public int TuneFame { get; set; }

        public override MessageId MessageId => MessageId.PET_PEN;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Pet);
            wtr.Write(BaseSize);
            wtr.Write(Size);
            wtr.Write(Shards);
            wtr.Write(ShardsNeeded);
            wtr.Write(Ok);
            wtr.WriteUTF16(Message);
            wtr.Write(Level);
            wtr.Write(MaxLevel);
            wtr.Write(Xp);
            wtr.Write(XpLevel);
            wtr.Write(XpNext);
            wtr.Write(Dps);
            wtr.Write(DpsMax);
            wtr.Write(Hp);
            wtr.Write(HpMax);
            wtr.Write(HpMs);
            wtr.Write(Mp);
            wtr.Write(MpMax);
            wtr.Write(MpMs);
            wtr.Write(PenSize);
            wtr.Write(PenFame);
            wtr.Write(PenGold);
            wtr.Write(PetSize);
            wtr.Write(GrowFame);
            wtr.Write(GrowGold);
            wtr.Write(TuneDamage);
            wtr.Write(TuneHp);
            wtr.Write(TuneMp);
            wtr.Write(TuneFame);
        }
    }
}
