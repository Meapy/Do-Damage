using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    // Once the world's count `name` (World.Counts, CountOnDeath) has reached `count`.
    internal class WorldCountTransition : Transition
    {
        private readonly string _name;
        private readonly int _count;

        public WorldCountTransition(string name, int count, string targetState) : base(targetState)
        {
            _name = name;
            _count = count;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state) =>
            host.World != null && host.World.Counts.TryGetValue(_name, out var n) && n >= _count;
    }
}
