using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Turns round the nearest `target` at a fixed rate, keeping the distance it started at: every
    // entity revolving at the same rate keeps its place in the shape they make (Oryx 5's Firmament),
    // however far out it is, and goes with the target when it moves; or, with aroundSpawn, round
    // the point where the target spawned (his Halo Orbs, round the edge of his arena). Orbit instead
    // moves at one speed, so the further ones fall behind.
    internal class Revolve : Behavior
    {
        private readonly ushort _target;
        private readonly double _rate;
        private readonly bool _aroundSpawn;

        public Revolve(string target, double degreesPerSecond, bool clockwise = true, bool aroundSpawn = false)
        {
            _target = GetObjType(target);
            _rate = degreesPerSecond * Math.PI / 180 * (clockwise ? 1 : -1);
            _aroundSpawn = aroundSpawn;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var center = host.GetNearestEntity(40, _target);
            if (center == null)
                return;

            var (cx, cy) = _aroundSpawn ? (center.SpawnPoint.Value.X, center.SpawnPoint.Value.Y) : (center.X, center.Y);
            var radius = state as double? ?? Math.Max(0.5, (double)host.DistTo(cx, cy));
            state = radius;
            var angle = Math.Atan2(host.Y - cy, host.X - cx) + _rate * time.DeltaTime;
            host.Move((float)(cx + Math.Cos(angle) * radius), (float)(cy + Math.Sin(angle) * radius));
        }
    }
}
