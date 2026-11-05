using System.Collections.Generic;
using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // A world of stacked floors (World.Levels): how high each floor is above the one below, and
    // the ground of every floor above the map (the map's own comes in Update as usual), sent once
    // on entering. Only clients that know it go there (the Stair Tower is for admins).
    public class LayersMessage : OutgoingMessage
    {
        public float Height { get; set; }
        public List<(byte Level, short X, short Y, ushort Tile)> Tiles { get; set; } = new List<(byte, short, short, ushort)>();

        public override MessageId MessageId => MessageId.LAYERS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(Height);
            wtr.Write((short)Tiles.Count);
            foreach (var (level, x, y, tile) in Tiles)
            {
                wtr.Write(level);
                wtr.Write(x);
                wtr.Write(y);
                wtr.Write(tile);
            }
        }
    }
}
