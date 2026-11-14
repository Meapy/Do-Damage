using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    // Fires the first time the host's health falls below `threshold` (a share of its maximum), and
    // never again for that host: a boss's one-off phase change, from wherever it is in its fight.
    internal class HpLessOnceTransition : Transition
    {
        //State storage: true once fired

        private readonly double threshold;

        public HpLessOnceTransition(double threshold, string targetState)
            : base(targetState)
        {
            this.threshold = threshold;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is true || host is not Enemy enemy || (double)enemy.Health / enemy.MaxHealth >= threshold)
                return false;
            state = true;
            return true;
        }
    }
}
