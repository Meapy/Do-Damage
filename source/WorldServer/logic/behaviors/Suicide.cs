using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.logic;

namespace WorldServer.logic.behaviors
{
    internal class Suicide : Behavior
    {
        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            // an ally's effect (a GameObject, not an Enemy) just leaves, as with Decay: throwing here
            // left it in the world, throwing and logging every tick until the world closed
            if (host is Enemy enemy)
                enemy.Death(ref time);
            else
                host.World.LeaveWorld(host);
        }
    }
}
