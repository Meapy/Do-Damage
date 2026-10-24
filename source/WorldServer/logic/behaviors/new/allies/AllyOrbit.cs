using System;
using System.Collections.Generic;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // An ally circling the player who summoned it, `radius` tiles out at `degreesPerSecond`, starting
    // wherever it stands from them. The Elysian Staff's halo.
    internal class AllyOrbit : Behavior
    {
        private readonly float _radius;
        private readonly float _speed;

        public AllyOrbit(double radius, double degreesPerSecond)
        {
            _radius = (float)radius;
            _speed = (float)(degreesPerSecond * Math.PI / 180);
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = null;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var player = host.World.Players.GetValueOrDefault(host.AllyOwnerId);
            if (player == null)
                return;
            var angle = state as float? ?? (float)Math.Atan2(host.Y - player.Y, host.X - player.X);
            angle += _speed * time.ElapsedMsDelta / 1000f;
            state = angle;
            host.Move(player.X + MathF.Cos(angle) * _radius, player.Y + MathF.Sin(angle) * _radius);
        }
    }
}
