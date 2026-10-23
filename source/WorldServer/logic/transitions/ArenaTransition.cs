using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    // Goes on when the host is in one of the arenas (the public /arena and the Battle Arenas,
    // ArenaPools), or with inArena false when it isn't: a boss's first state picks its arena version.
    internal class ArenaTransition : Transition
    {
        //State storage: none

        private readonly bool _inArena;

        public ArenaTransition(string targetState, bool inArena = true)
            : base(targetState)
        {
            _inArena = inArena;
        }

        public static bool InArena(Entity host) => host.World?.IdName.Contains("Arena") ?? false;

        protected override bool TickCore(Entity host, TickTime time, ref object state) => InArena(host) == _inArena;
    }
}
