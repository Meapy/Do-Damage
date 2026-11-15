using WorldServer.core.objects;
using WorldServer.core.resurrection;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.transitions
{
    internal class NoPlayerWithinTransition : Transition
    {
        //State storage: none

        private readonly double dist;

        public NoPlayerWithinTransition(double dist, string targetState)
            : base(targetState)
        {
            this.dist = dist;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            // an Empowered boss's fight sees cloaked players: it can't be held back by hiding
            return host.GetNearestEntity(dist, null, false, BossResurrection.InEmpoweredFight(host)) == null;
        }
    }
}
