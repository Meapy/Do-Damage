using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // The map has no Crystal Worm Mother (she lives in the Crystal Cavern, which has no map
        // here), so the Mushroom Berserker is the dungeon's last fight and drops the exit portal.
        private _ FungalCavern = () => Behav()
        .Init("GC Mushroom Slammer Melee",
            new State(
                new Follow(0.7, 8, 1),
                new Wander(0.3),
                new Grenade(2.5, 100, range: 3, coolDown: 2000, color: 0x8B4513)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        // the alchemists patch up the mushrooms around them
        .Init("GC Mushroom Slammer Mage",
            new State(
                new StayCloseToSpawn(0.5, 4),
                new Wander(0.4),
                new HealEntity(8, "GC Mushroom", 250, coolDown: 3000)
                ),
            new ItemLoot("Magic Potion", 0.05)
            )
        .Init("GC Mushroom Circler Melee",
            new State(
                new Follow(0.7, 8, 2),
                new Wander(0.3),
                new Shoot(5, count: 1, projectileIndex: 0, coolDown: 800),
                new Shoot(4, count: 3, shootAngle: 20, projectileIndex: 2, coolDown: 3000)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Coward Melee",
            new State(
                new StayBack(0.6, 6),
                new Wander(0.3),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1200),
                new Shoot(8, count: 1, projectileIndex: 1, coolDown: 1200, coolDownOffset: 200),
                new Shoot(8, count: 1, projectileIndex: 2, coolDown: 1200, coolDownOffset: 400)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Charger Melee",
            new State(
                new Charge(2, 10, coolDown: 3000),
                new Wander(0.3),
                new Shoot(6, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1500),
                new Shoot(6, count: 1, projectileIndex: 1, coolDown: 1500, coolDownOffset: 300)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Charger Mage",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1000)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("GC Mushroom Spiral Melee",
            new State(
                new Follow(0.6, 8, 2),
                new Wander(0.3),
                new Shoot(8, count: 2, shootAngle: 10, projectileIndex: 0, coolDown: 900)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Spiral Mage",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1200),
                new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, coolDown: 3000)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("GC Cavecrawler Bat",
            new State(
                new Follow(1.2, 9, 1),
                new Wander(0.6),
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 900),
                new Shoot(0, count: 1, projectileIndex: 1, fixedAngle: 0, coolDown: 3000)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Cavecrawler Webslinger",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.3),
                new Shoot(10, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 2000),
                new Shoot(8, count: 1, projectileIndex: 1, coolDown: 1200)
                ),
            new ItemLoot("Magic Potion", 0.05)
            )
        .Init("GC Red Worm",
            new State(
                new StayCloseToSpawn(0.5, 8),
                new Wander(0.5),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1500),
                new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 45, coolDown: 2500)
                ),
            new ItemLoot("Health Potion", 0.1)
            )
        // the map places loose worm segments; they act as turrets
        .Init("GC Red Worm Body",
            new State(
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 2000)
                )
            )
        .Init("GC Red Worm Tail",
            new State(
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 2000)
                )
            )
        .Init("GC Mushroom Charger Leader",
            new State(
                new ScaleHP2(20),
                new DropPortalOnDeath("Realm Portal", 1.0),
                new State("Idle",
                    new PlayerWithinTransition(9, "Rally")
                    ),
                new State("Rally",
                    new Taunt("The tribe will not fall!"),
                    new Flash(0x00FF00, 0.5, 3),
                    new TimedTransition(1500, "Charge")
                    ),
                new State("Charge",
                    new Charge(2.5, 10, coolDown: 2500),
                    new StayCloseToSpawn(0.6, 8),
                    new Shoot(10, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: 1500),
                    new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, coolDown: 2500),
                    new HpLessTransition(0.4, "Frenzy")
                    ),
                new State("Frenzy",
                    new Taunt("RRAAAGH!"),
                    new Flash(0xFF0000, 0.5, 6),
                    new Charge(3, 10, coolDown: 1800),
                    new StayCloseToSpawn(0.6, 8),
                    new Shoot(10, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: 1000),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 22.5, coolDown: 2000),
                    new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 2, fixedAngle: 0, coolDown: 4000)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Shroom Generator", 0.03),
                new ItemLoot("Potion of Speed", 1),
                new ItemLoot("Potion of Vitality", 0.5),
                new ItemLoot("Tome of the Mushroom Tribes", 0.005),
                new ItemLoot("Sporous Spray Spell", 0.005),
                new ItemLoot("Fungal Breastplate", 0.005),
                new ItemLoot("Cave Dweller Trap", 0.005)
                )
            );
    }
}
