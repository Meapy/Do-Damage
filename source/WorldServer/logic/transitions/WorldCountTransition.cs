using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    // Once the world's count `name` (World.Counts, CountOnDeath) has reached `count`. With `of`, the
    // count is capped by how many of those objects the world has (still standing, or counted
    // already), worked out once per world: a random layout that kept 18 Cavern Crystals needs 18,
    // not 20. `share` then waits for that part of it (a drill charging step by step).
    internal class WorldCountTransition : Transition
    {
        private readonly string _name;
        private readonly int _count;
        private readonly string _of;
        private readonly double _share;

        public WorldCountTransition(string name, int count, string targetState, string of = null, double share = 1) : base(targetState)
        {
            _name = name;
            _count = count;
            _of = of;
            _share = share;
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            if (world == null)
                return false;
            world.Counts.TryGetValue(_name, out var n);
            return n >= Math.Ceiling(Needed(world, _name, _count, _of) * _share);
        }

        public static int Needed(World world, string name, int count, string of)
        {
            if (of == null)
                return count;
            var key = "needed " + name;
            if (world.Counts.TryGetValue(key, out var needed))
                return needed;
            world.Counts.TryGetValue(name, out var done);
            var standing = world.Enemies.Values.Count(e => !e.Dead && string.Equals(e.ObjectDesc.IdName, of, StringComparison.OrdinalIgnoreCase));
            needed = Math.Clamp(done + standing, 1, count);
            world.Counts[key] = needed;
            return needed;
        }
    }
}
