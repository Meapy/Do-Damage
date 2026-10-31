using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // Players start in the south clearing (Spawn region added to the map), fight the two Adult
        // Basilisks, and find Tezcacoatl in the lightning arena north of the El Dorado temple.
        private _ SecludedThicket = () => Behav()
        .Init("Adult Basilisk 3",
            AdultBasilisk(),
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 0.5),
                new ItemLoot("Basilisk Hide Armor", 0.05)
                )
            )
        .Init("Adult Basilisk 6",
            AdultBasilisk(),
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 0.5),
                new ItemLoot("Basilisk Hide Armor", 0.05)
                )
            )
        // Tezcacoatl's brood (Forbidden Jungle objects)
        .Init("Basilisk",
            new State(
                new Follow(0.7, 9, 2),
                new Wander(0.3),
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 1000),
                new Shoot(5, count: 3, shootAngle: 20, projectileIndex: 1, coolDown: 2000)
                )
            )
        .Init("Basilisk Baby",
            new State(
                new Follow(0.9, 8, 1),
                new Wander(0.4),
                new Shoot(5, count: 1, projectileIndex: 0, coolDown: 800)
                )
            )
        // the braziers at the temple's south door open both of its brick doorways
        .Init("Thicket Brazier",
            new State(
                new State("Init",
                    new TimedTransition(2000, "Wait")
                    ),
                new State("Wait",
                    new EntitiesNotExistsTransition(40, "Open", "Adult Basilisk 3", "Adult Basilisk 6")
                    ),
                new State("Open",
                    new RemoveObject("El Dorado Temple Bricks", 30),
                    new TimedTransition(1000, "Done")
                    ),
                new State("Done")
                )
            )
        .Init("Tezcacoatl the Great Basilisk",
            new State(
                new ScaleHP2(20),
                new DropPortalOnDeath("Realm Portal", 1.0),
                new State("Idle",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(10, "Awaken")
                    ),
                new State("Awaken",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("Hssss... who trespasses in my temple?"),
                    new Flash(0x00FFAA, 0.5, 4),
                    new TimedTransition(2000, "Coil")
                    ),
                new State("Coil",
                    new StayCloseToSpawn(0.5, 6),
                    new Wander(0.3),
                    new Shoot(12, count: 5, shootAngle: 12, projectileIndex: 0, coolDown: 1600),
                    new Shoot(12, count: 1, projectileIndex: 9, predictive: 0.3, coolDown: 1200),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 5, fixedAngle: 0, coolDown: 4000),
                    new HpLessTransition(0.7, "Brood")
                    ),
                new State("Brood",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("My brood will feast!"),
                    new ReturnToSpawn(1),
                    new Spawn("Basilisk", 3, 1),
                    new Spawn("Basilisk Baby", 4, 1),
                    new TimedTransition(2000, "Strike")
                    ),
                new State("Strike",
                    new Follow(0.4, 10, 4),
                    new Shoot(12, count: 3, shootAngle: 20, projectileIndex: 2, coolDown: 1000),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 6, fixedAngle: 0, coolDown: 3000),
                    new Shoot(5, count: 4, shootAngle: 90, projectileIndex: 7, coolDown: 2500),
                    new HpLessTransition(0.4, "Fury")
                    ),
                new State("Fury",
                    new Taunt("The storm answers me!"),
                    new Flash(0xFFFF00, 0.5, 6),
                    new StayCloseToSpawn(0.6, 8),
                    new Wander(0.4),
                    new Shoot(12, count: 5, shootAngle: 10, projectileIndex: 4, coolDown: 1200),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 10, fixedAngle: 22.5, coolDown: 4500),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 11, fixedAngle: 0, coolDown: 3000),
                    new Shoot(12, count: 2, shootAngle: 15, projectileIndex: 20, coolDown: 1500)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 1),
                new ItemLoot("Potion of Life", 0.5),
                new ItemLoot("Tezcacoatl's Tail", 0.05),
                new ItemLoot("Mask of Lightning", 0.005),
                new ItemLoot("Lightning in a Bottle", 0.02),
                new ItemLoot("Staff of the Crystal Serpent", 0.02),
                new ItemLoot("Cracked Crystal Skull", 0.005)
                )
            );

        private static State AdultBasilisk() =>
            new State(
                new State("Idle",
                    new PlayerWithinTransition(9, "Hunt")
                    ),
                new State("Hunt",
                    new Follow(0.6, 10, 2),
                    new Shoot(2, count: 1, projectileIndex: 0, coolDown: 700),
                    new Shoot(4, count: 3, shootAngle: 30, projectileIndex: 1, coolDown: 1500),
                    new Shoot(10, count: 1, projectileIndex: 2, predictive: 0.3, coolDown: 1800),
                    new Shoot(0, count: 1, projectileIndex: 8, fixedAngle: 0, coolDown: 5000),
                    new HpLessTransition(0.5, "Enraged")
                    ),
                new State("Enraged",
                    new Flash(0xFF0000, 0.5, 4),
                    new Follow(0.8, 10, 2),
                    new Shoot(2, count: 1, projectileIndex: 0, coolDown: 600),
                    new Shoot(8, count: 3, shootAngle: 10, projectileIndex: 5, coolDown: 1500),
                    new Shoot(8, count: 3, shootAngle: 10, projectileIndex: 6, coolDown: 1500, coolDownOffset: 500),
                    new Shoot(8, count: 2, shootAngle: 20, projectileIndex: 4, coolDown: 2000),
                    new Shoot(0, count: 1, projectileIndex: 8, fixedAngle: 0, coolDown: 4000)
                    )
                );
    }
}
