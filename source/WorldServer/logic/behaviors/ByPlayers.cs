using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // Behaviours that each need at least so many players within `radius` of the host, counted as the
    // state begins (hidden admins don't count): Oryx 5's Host, fewer for a small party.
    internal class ByPlayers : Behavior
    {
        private readonly double _radius;
        private readonly (int Players, Behavior Behavior)[] _children;

        public ByPlayers(double radius, params (int Players, Behavior Behavior)[] children)
        {
            _radius = radius;
            _children = children;
        }

        protected internal override void Resolve(State parent)
        {
            foreach (var (_, b) in _children)
                b.Resolve(parent);
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var players = host.World.Players.Values.Count(p => !p.IsHidden && host.DistTo(p) <= _radius);
            var active = _children.Where(c => players >= c.Players).Select(c => c.Behavior).ToArray();
            foreach (var b in active)
                b.OnStateEntry(host, time);
            state = active;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is Behavior[] active)
                foreach (var b in active)
                    b.Tick(host, time);
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is Behavior[] active)
                foreach (var b in active)
                    b.OnStateExit(host, time);
        }
    }
}
