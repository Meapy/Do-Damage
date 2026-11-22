using System.Collections.Generic;
using Shared;

namespace WorldServer.networking.packets.outgoing.guide
{
    // The Gilded Realm Pass's dungeon guide (Player.Guide.cs): the shortest walk from the player to
    // the dungeon's boss, as the tiles along it (every second one, as a tile's x and y), the
    // boss's type and place, and how many tiles the walk is. An empty path takes the guide away
    // (the boss died, or no way is known).
    public class DungeonGuide : OutgoingMessage
    {
        public ushort ObjectType;
        public float X, Y;
        public int Length;
        public List<(ushort X, ushort Y)> Points = new List<(ushort, ushort)>();

        public override MessageId MessageId => MessageId.DUNGEON_GUIDE;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(ObjectType);
            wtr.Write(X);
            wtr.Write(Y);
            wtr.Write(Length);
            wtr.Write((short)Points.Count);
            foreach (var (x, y) in Points)
            {
                wtr.Write(x);
                wtr.Write(y);
            }
        }
    }
}
