using Shared;
using WorldServer.core.net.datas;
using WorldServer.core.structures;

namespace WorldServer.networking.packets.outgoing
{
    public enum EffectType
    {
        Potion = 1,
        Teleport = 2,
        Stream = 3,
        Throw = 4,
        AreaBlast = 5,      //radius=pos1.x
        Dead = 6,
        Trail = 7,
        Diffuse = 8,        //radius=dist(pos1,pos2)
        Flow = 9,
        Trap = 10,          //radius=pos1.x
        Lightning = 11,     //particleSize=pos2.x
        Concentrate = 12,   //radius=dist(pos1,pos2)
        BlastWave = 13,     //origin=pos1, radius = pos2.x
        Earthquake = 14,
        Flashing = 15,      //period=pos1.x, numCycles=pos1.y
        BeachBall = 16,
        Inspired = 17,
        // a circle on the ground (centre pos1, radius pos2.x) filling in from its edge over Duration
        // ms: where something is about to land. Clients before 2.4.7 don't draw it.
        GroundMark = 40,
        // a Necromantic item's power at work (Player.RelicPowers.cs RelicFx): which look in pos2.y,
        // centred on pos1, radius pos2.x, lasting Duration ms, following or flowing to the target
        // object. Clients before 2.5.4 don't draw it.
        Relic = 41
    }

    public class ShowEffect : OutgoingMessage
    {
        public EffectType EffectType { get; set; }
        public int TargetObjectId { get; set; }
        public Position Pos1 { get; set; }
        public Position Pos2 { get; set; }
        public ARGB Color { get; set; }
        public int Duration { get; set; }
        public ushort ObjectType { get; set; }
        // the player whose power it is (a Necromantic item's: Player.RelicPowers.cs), so clients
        // can fade or hide other players' effects; written only when set, after the fields older
        // clients read, which leave it unread (Rust clients from 2.5.5 read it)
        public int SourceId { get; set; }

        public override MessageId MessageId => MessageId.SHOWEFFECT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((byte)EffectType);
            wtr.Write(TargetObjectId);
            Pos1.Write(wtr);
            Pos2.Write(wtr);
            Color.Write(wtr);
            wtr.Write(Duration);
            wtr.Write(ObjectType);
            if (SourceId != 0)
                wtr.Write(SourceId);
        }
    }
}
