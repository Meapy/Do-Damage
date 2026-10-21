using System;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // When the host dies, throws `count` of `child` out round it, evenly spaced `range` tiles away
    // (Oryx's Elysium's guardians drop their Trees of Doom). They land at once, not after a toss's
    // delay, so whatever waits for none of them to be left never sees a gap; one whose spot isn't
    // walkable lands where the host died.
    internal class TossOnDeath : Behavior
    {
        private readonly ushort _child;
        private readonly int _count;
        private readonly double _range;

        public TossOnDeath(string child, int count, double range)
        {
            _child = GetObjType(child);
            _count = count;
            _range = range;
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var world = host.World;
            for (var i = 0; i < _count; i++)
            {
                var angle = 2 * Math.PI * i / _count - Math.PI / 2;
                var x = host.X + (float)(_range * Math.Cos(angle));
                var y = host.Y + (float)(_range * Math.Sin(angle));
                if (!world.IsPassable(x, y, true))
                    (x, y) = (host.X, host.Y);

                world.Broadcast(new ShowEffect()
                {
                    EffectType = EffectType.Throw,
                    Color = new ARGB(0xffffd770),
                    TargetObjectId = host.Id,
                    Pos1 = new Position() { X = x, Y = y }
                });
                var entity = Entity.Resolve(host.GameServer, _child);
                entity.Move(x, y);
                world.EnterWorld(entity);
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        { }
    }
}
