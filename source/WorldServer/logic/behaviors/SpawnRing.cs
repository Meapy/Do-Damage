using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // On entering its state, puts `count` of `child` evenly round a circle of `radius` tiles, the
    // first at angleOffset degrees (0 is east, 90 south): round the host, or round its spawn point
    // (the middle of its arena, wherever it stands). Spots that aren't walkable are skipped.
    internal class SpawnRing : Behavior
    {
        private readonly ushort _child;
        private readonly int _count;
        private readonly double _radius;
        private readonly double _angleOffset;
        private readonly bool _aroundSpawn;

        public SpawnRing(string child, int count, double radius, double angleOffset = 0, bool aroundSpawn = false)
        {
            _child = GetObjType(child);
            _count = count;
            _radius = radius;
            _angleOffset = angleOffset * Math.PI / 180;
            _aroundSpawn = aroundSpawn;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var (cx, cy) = _aroundSpawn ? (host.SpawnPoint.Value.X, host.SpawnPoint.Value.Y) : (host.X, host.Y);
            for (var i = 0; i < _count; i++)
            {
                var a = _angleOffset + 2 * Math.PI * i / _count;
                var x = cx + (float)(Math.Cos(a) * _radius);
                var y = cy + (float)(Math.Sin(a) * _radius);
                if (!host.World.IsPassable(x, y, true))
                    continue;
                var entity = Entity.Resolve(host.GameServer, _child);
                entity.Move(x, y);
                entity.SummonedBy(host);
                host.World.EnterWorld(entity);
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        { }
    }
}
