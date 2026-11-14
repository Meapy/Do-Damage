using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // Runs its behaviours only while the host's health is below `threshold` (a share of its
    // maximum): entering them as it first drops below, leaving them if it heals back over. Oryx 4's
    // second phase (BehaviorDb.OryxDoomed.cs) keeps its hellfire sweep going this way, through
    // whichever attack he's in.
    internal class BelowHealth : Behavior
    {
        //State storage: whether the behaviours are running

        private readonly double threshold;
        private readonly Behavior[] behaviors;

        public BelowHealth(double threshold, params Behavior[] behaviors)
        {
            this.threshold = threshold;
            this.behaviors = behaviors;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = false;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (host is not Enemy enemy)
                return;
            var running = state is true;
            var below = (double)enemy.Health / enemy.MaxHealth < threshold;
            if (below && !running)
                foreach (var b in behaviors)
                    b.OnStateEntry(host, time);
            else if (!below && running)
                foreach (var b in behaviors)
                    b.OnStateExit(host, time);
            if (below)
                foreach (var b in behaviors)
                    b.Tick(host, time);
            state = below;
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is true)
                foreach (var b in behaviors)
                    b.OnStateExit(host, time);
            state = null;
        }
    }
}
