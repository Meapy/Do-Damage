using System;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // Drifts slowly between random points within `radius` tiles of where it was placed, over land
    // and sea alike (it floats: no tile stops it). Scenery: Oryx's Elysium's clouds.
    internal class Drift : Behavior
    {
        private readonly double _speed;
        private readonly double _radius;

        public Drift(double tilesPerSecond, double radius)
        {
            _speed = tilesPerSecond;
            _radius = radius;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var spawn = host.SpawnPoint.Value;
            if (state is not Position target || host.DistTo(ref target) < 0.2)
            {
                var a = Random.NextDouble() * 2 * Math.PI;
                var d = _radius * Math.Sqrt(Random.NextDouble());
                target = new Position(spawn.X + (float)(Math.Cos(a) * d), spawn.Y + (float)(Math.Sin(a) * d));
            }
            state = target;

            var dist = host.DistTo(ref target);
            var step = (float)Math.Min(dist, _speed * time.DeltaTime);
            host.Move(host.X + (target.X - host.X) / dist * step, host.Y + (target.Y - host.Y) / dist * step);
        }
    }
}
