using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // When the host leaves its state (its pattern ends, or a health threshold moves it on), every
    // entity of the given kinds within range vanishes: Oryx 5's Firmament stars go with the
    // Firmament.
    internal class RemoveEntitiesOnExit : Behavior
    {
        private readonly double _range;
        private readonly ushort[] _types;

        public RemoveEntitiesOnExit(double range, params string[] names)
        {
            _range = range;
            _types = names.Select(GetObjType).ToArray();
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            foreach (var type in _types)
                foreach (var e in host.GetNearestEntities(_range, type).ToList())
                    host.World.LeaveWorld(e);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        { }
    }
}
