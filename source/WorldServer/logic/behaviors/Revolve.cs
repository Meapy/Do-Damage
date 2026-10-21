using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Turns round the nearest `target` at a fixed rate, keeping the distance it started at: every
    // entity revolving at the same rate keeps its place in the shape they make (Oryx 5's Firmament,
    // his Halo Orbs), however far out it is, and goes with the target when it moves. Orbit instead
    // moves at one speed, so the further ones fall behind.
    internal class Revolve : Behavior
    {
        private readonly ushort _target;
        private readonly double _rate;

        public Revolve(string target, double degreesPerSecond, bool clockwise = true)
        {
            _target = GetObjType(target);
            _rate = degreesPerSecond * Math.PI / 180 * (clockwise ? 1 : -1);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var center = host.GetNearestEntity(40, _target);
            if (center == null)
                return;

            var radius = state as double? ?? Math.Max(0.5, (double)host.DistTo(center));
            state = radius;
            var angle = Math.Atan2(host.Y - center.Y, host.X - center.X) + _rate * time.DeltaTime;
            host.Move((float)(center.X + Math.Cos(angle) * radius), (float)(center.Y + Math.Sin(angle) * radius));
        }
    }
}
