using System;
using System.Linq;
using WorldServer.core.objects;

namespace WorldServer.logic.behaviors
{
    // How much of a boss's per-player health (ScaleHP2, BossStats) a group gets. In the hardest
    // dungeons (every Elder dungeon, the Black Between and the Crucible) each extra player adds half as much: their
    // bosses already have twice the health and more, and a full group faced a pool that grew with
    // every player past what the group's damage could keep up with.
    public static class GroupScaling
    {
        public const double HardShare = 0.5;

        public static double Share(Entity host) => IsHard(host?.World?.IdName) ? HardShare : 1;

        // "Elder Lost Halls", "The Elder Shatters", "Lair of Elder Draconis", "The Elder Void"...
        public static bool IsHard(string world) =>
            world != null && (world == "The Black Between" || world == "The Crucible"
                || world.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("Elder"));
    }
}
