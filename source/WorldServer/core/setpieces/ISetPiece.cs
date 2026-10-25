using System;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;

namespace WorldServer.core.setpieces
{
    public abstract class ISetPiece
    {
        public abstract int Size { get; }
        public virtual string Map { get; }
        public virtual string EntityName { get; }

        protected Random rand => Random.Shared;

        public virtual void RenderSetPiece(World world, IntPoint pos)
        {
            if (string.IsNullOrEmpty(Map))
                return;

            var data = world.GameServer.Resources.GameData.GetWorldData(Map);
            if (data == null)
            {
                Console.WriteLine($"[{GetType().Name}] Invalid RenderSetPiece {Map}");
                return;
            }
            // pos is the corner of the Size by Size square the realm (or /setpiece) keeps for it, and a
            // map is drawn centred on the point it's given
            SetPieces.RenderFromData(world, new IntPoint(pos.X + Size / 2, pos.Y + Size / 2), data);
        }
    }
}
