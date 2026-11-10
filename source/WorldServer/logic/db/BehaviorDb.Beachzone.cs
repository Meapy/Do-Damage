using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // the Beachzone host has no attacks: it dances around the party until it's beaten
        private _ Beachzone = () => BeachzoneBehaviours(Behav(), false);
        private _ ElderBeachzone = () => BeachzoneBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs)
        private static Ctor BeachzoneBehaviours(Ctor db, bool elder)
        {
            string E(string id) => ElderDefines(id, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            return db
            .Init(E("Masked Party God"),
                new State(
                    // the regular dungeon leads to its Elder version half the time
                    new DropPortalOnDeath("Elder Beach Portal", elder ? 0 : 0.5, xAdjustment: 3),
                    new DropPortalOnDeath("Realm Portal", 1.0),
                    new ScaleHP2(20),
                    new State("Party",
                        new StayCloseToSpawn(0.4, 6),
                        new Wander(0.4),
                        new Taunt(0.5, new Cooldown(10000, 0), "Party at the beach!", "Who brought the snacks?", "Surf's up!"),
                        new HpLessTransition(0.25, "Tired")
                        ),
                    new State("Tired",
                        new Taunt("Okay, okay, the party's over..."),
                        new StayCloseToSpawn(0.2, 3)
                        )
                    ),
                new Threshold(0.01,
                    new ItemLoot("Aceaku's Pet Generator", elder ? 0.06 : 0.03),
                    new ItemLoot("Beach Ball Generator", elder ? 0.06 : 0.03),
                    new ItemLoot("Potion of Speed", 1),
                    new ItemLoot("Potion of Dexterity", 0.5)
                    ), Elder("Masked Party God"),
                // the Elder dungeon's Potion of Max
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Speed", 0.05),
                    new ItemLoot("Elder Beach Key", 0.01)));
        }
    }
}
