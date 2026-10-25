using System;
using System.Collections.Generic;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    // Fires once `share` of the `target` monsters in the world have died: it notes every one it sees
    // alive, so it counts from its state's entry (put it in a state the host enters as the world
    // starts, on a host that always ticks, such as a static object).
    internal class ShareKilledTransition : Transition
    {
        //State storage: the ids seen alive

        private readonly double _share;
        private readonly ushort _target;

        public ShareKilledTransition(double share, string targetState, string target)
            : base(targetState)
        {
            _share = share;
            _target = Behavior.GetObjType(target);
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var seen = (HashSet<int>)(state ??= new HashSet<int>());
            var alive = 0;
            foreach (var enemy in host.World.Enemies.Values)
                if (enemy.ObjectType == _target && !enemy.Dead)
                {
                    seen.Add(enemy.Id);
                    alive++;
                }
            return seen.Count > 0 && seen.Count - alive >= Math.Ceiling(seen.Count * _share);
        }
    }
}
