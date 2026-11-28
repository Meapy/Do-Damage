using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    /// <summary>
    /// Marks where a boss is about to come back into sight (Strike.Arrival): on entering the state, a
    /// ring of `color` at (`x`, `y`) in the world (from the host with `relative`) closing in over `ms`.
    /// For a boss that vanishes and moves while unseen (the Ghost of Skuld between his volleys), with
    /// the movement its own behaviour; bosses that jump in one go wait on their own mark
    /// (BlinkAround, MoveTo3's `warnMs`, ThiefBlink).
    /// </summary>
    internal class ArrivalMark : Behavior
    {
        private readonly float _x;
        private readonly float _y;
        private readonly int _ms;
        private readonly uint _color;
        private readonly bool _relative;
        private readonly double _radius;

        public ArrivalMark(float x, float y, int ms, uint color = 0xFFFFFFFF, bool relative = false, double radius = 1.2)
        {
            _x = x;
            _y = y;
            _ms = ms;
            _color = color;
            _relative = relative;
            _radius = radius;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var (x, y) = _relative ? (host.X + _x, host.Y + _y) : (_x, _y);
            // where MoveTo wouldn't take it (out of an Arena's ring), it stays put: nothing to mark
            if (host.World == null || !host.World.MonsterMayStand(x, y))
                return;
            Strike.Arrival(host, new Position(x, y), _ms, _color, _radius);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }
}
