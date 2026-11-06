using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // The Nexus's XP ducks (NexusGuide): no attacks, and a Health or Magic Potion now and then,
        // so a new player can stock up without a dungeon (2026-11-06)
        private _ Nexus = () => Behav()
            .Init("NG XP Duck",
                new State(),
                new ItemLoot("Health Potion", 0.25),
                new ItemLoot("Magic Potion", 0.25)
            );
    }
}
