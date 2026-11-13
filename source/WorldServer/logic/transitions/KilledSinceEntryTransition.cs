using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;
using WorldServer.utils;

namespace WorldServer.logic.transitions
{
    // Once `count` of the `targets` within `dist` have gone since the state was entered, or none is
    // left: the Crystal Worm Mother's head opens up after so many of her body segments are broken.
    internal class KilledSinceEntryTransition : Transition
    {
        private readonly double _dist;
        private readonly int _count;
        private readonly ushort[] _targets;

        public KilledSinceEntryTransition(double dist, int count, string targetState, params string[] targets) : base(targetState)
        {
            _dist = dist;
            _count = count;
            _targets = targets.Select(Behavior.GetObjType).ToArray();
        }

        private int Alive(Entity host) => _targets.Sum(t => host.GetNearestEntities(_dist, t).Count());

        // state: the number alive when the state was entered, and when this last looked; a gap of
        // more than a second means the state was left and entered again, so it counts afresh
        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var alive = Alive(host);
            if (state is not (int start, long seen) || time.TotalElapsedMs - seen > 1000)
                (start, seen) = (alive, 0);
            var done = alive == 0 || start - alive >= _count;
            state = done ? null : (start, time.TotalElapsedMs);
            return done;
        }
    }
}
