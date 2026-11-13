using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // Adds one to the world's count `name` (World.Counts) when the host dies: the Fungal Cavern's
    // drill counts the Cavern Crystals broken (WorldCountTransition).
    internal class CountOnDeath : Behavior
    {
        private readonly string _name;

        public CountOnDeath(string name) => _name = name;

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var counts = host.World?.Counts;
            if (counts == null)
                return;
            counts.TryGetValue(_name, out var n);
            counts[_name] = n + 1;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }
}
