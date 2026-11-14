using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Keeps the host within a circle round its spawn point, whatever moves it (Follow, Orbit,
    // Charge, Wander...). Put it in the host's outermost state: it pulls the host back in after
    // the movement behaviours have run, so a boss can't follow a player out of its room.
    internal class StayInRoom : Behavior
    {
        private readonly float _radius;

        public StayInRoom(double radius) => _radius = (float)radius;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var spawn = host.SpawnPoint.Value;
            var vect = new Vector2(host.X - spawn.X, host.Y - spawn.Y);
            var len = vect.Length();

            if (len <= _radius)
                return;

            vect.Normalize();
            host.ValidateAndMove(spawn.X + vect.X * _radius, spawn.Y + vect.Y * _radius);
        }
    }
}
