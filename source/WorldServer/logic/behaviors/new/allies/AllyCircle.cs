using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // An ally circling the spot where it appeared, `radius` tiles out at `degreesPerSecond`, starting
    // at its SavedAngle (ObjectToss spreads several round the circle). The Trap of the Vile Spirit's
    // ghost ships.
    internal class AllyCircle : Behavior
    {
        private readonly float _radius;
        private readonly float _speed;

        public AllyCircle(double radius, double degreesPerSecond)
        {
            _radius = (float)radius;
            _speed = (float)(degreesPerSecond * Math.PI / 180);
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = null;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var centre = host.SpawnPoint.Value;
            var angle = state as float? ?? host.SavedAngle ?? 0;
            angle += _speed * time.ElapsedMsDelta / 1000f;
            state = angle;
            host.Move(centre.X + MathF.Cos(angle) * _radius, centre.Y + MathF.Sin(angle) * _radius);
        }
    }
}
