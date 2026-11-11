using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // pD Blockades need no behaviour: they are destructible enemies that wall off the rooms.
        // The colonies and the boss have defense in the thousands (10% damage at most), so they
        // break their own armour (ArmorBroken sets defense to 0) when they are meant to be hurt.
        private _ ParasiteChambers = () => Behav()
        .Init("pD Swords",
            new State(
                new Follow(0.8, 8, 1),
                new Wander(0.3),
                new Shoot(4, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 900)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("pD Wands",
            new State(
                new StayBack(0.5, 5),
                new Wander(0.3),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 500)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("pD Bows",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(7, count: 3, shootAngle: 12, projectileIndex: 0, coolDown: 1000)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("pD Daggers",
            new State(
                new Follow(0.9, 8, 1),
                new Wander(0.3),
                new Shoot(5, count: 1, projectileIndex: 0, coolDown: 500)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("pD Staves",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(8, count: 2, shootAngle: 8, projectileIndex: 0, coolDown: 700)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("pD Katanas",
            new State(
                new Follow(0.8, 8, 1),
                new Wander(0.3),
                new Shoot(4, count: 2, shootAngle: 10, projectileIndex: 0, coolDown: 700)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("pD Stalker",
            new State(
                new Follow(1, 10, 2),
                new Wander(0.4),
                new Shoot(9, count: 2, shootAngle: 10, projectileIndex: 0, coolDown: 1500)
                ),
            new ItemLoot("Health Potion", 0.05)
            )
        .Init("pD Walker",
            new State(
                new Follow(0.5, 9, 3),
                new Wander(0.3),
                new Shoot(8, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 1800)
                ),
            new ItemLoot("Health Potion", 0.05)
            )
        .Init("pD Maw",
            new State(
                new StayCloseToSpawn(0.3, 3),
                new Wander(0.2),
                new Shoot(8, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1400),
                new Shoot(6, count: 2, shootAngle: 30, projectileIndex: 1, coolDown: 2000),
                new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 2, fixedAngle: 0, coolDown: 4000)
                ),
            new ItemLoot("Health Potion", 0.05)
            )
        .Init("pD Tongue",
            new State(
                new Follow(0.6, 9, 2),
                new Wander(0.3),
                new Shoot(7, count: 5, shootAngle: 10, projectileIndex: 0, coolDown: 1600)
                ),
            new ItemLoot("Health Potion", 0.05)
            )
        .Init("pD Blob",
            new State(
                new StayCloseToSpawn(0.4, 4),
                new Wander(0.4),
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 1500)
                )
            )
        .Init("pD Blob Guardian",
            new State(
                new StayCloseToSpawn(0.4, 3),
                new Wander(0.3),
                new Shoot(7, count: 3, shootAngle: 30, projectileIndex: 0, coolDown: 1800)
                ),
            new ItemLoot("Health Potion", 0.02)
            )
        .Init("pD Blob Scout",
            new State(
                new Follow(0.8, 8, 2),
                new Wander(0.4),
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 1200)
                )
            )
        .Init("pD Painling",
            new State(
                new Follow(0.6, 8, 2),
                new Wander(0.3),
                new Shoot(7, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 1500)
                ),
            new ItemLoot("Magic Potion", 0.05)
            )
        // invincible pests that circle their nests
        .Init("pD Pterasite",
            new State(
                new Orbit(0.8, 3, 10, "pD Pterasite Bomb", speedVariance: 0.2, radiusVariance: 1),
                new Wander(0.4),
                new Shoot(5, count: 1, projectileIndex: 0, coolDown: 1500)
                )
            )
        .Init("pD Pterasite Bomb",
            new State(
                new State("Idle",
                    new PlayerWithinTransition(5, "Burst")
                    ),
                new State("Burst",
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, coolDown: 3000),
                    new NoPlayerWithinTransition(7, "Idle")
                    )
                )
            )
        .Init("pD Obstacle Explosion",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("Idle",
                    new PlayerWithinTransition(3, "Pulse")
                    ),
                new State("Pulse",
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, coolDown: 3000),
                    new NoPlayerWithinTransition(5, "Idle")
                    )
                )
            )
        .Init("pD Swarm Colony",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.ArmorBroken, true),
                new State("Idle",
                    new PlayerWithinTransition(10, "Swarm")
                    ),
                new State("Swarm",
                    new Shoot(0, count: 3, shootAngle: 120, projectileIndex: 3, fixedAngle: 0, coolDown: 2500),
                    new Shoot(0, count: 3, shootAngle: 120, projectileIndex: 1, fixedAngle: 60, coolDown: 4000),
                    new Shoot(8, count: 1, projectileIndex: 2, coolDown: 1000),
                    new NoPlayerWithinTransition(14, "Idle")
                    )
                ),
            new Threshold(0.05,
                new ItemLoot("Potion of Vitality", 0.1)
                )
            )
        .Init("pD Blood Slurp",
            new State(
                new StayCloseToSpawn(0.5, 4),
                new Wander(0.4),
                new Shoot(7, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 1500),
                new Shoot(5, count: 1, projectileIndex: 1, coolDown: 900)
                ),
            new ItemLoot("Magic Potion", 0.1)
            )
        // the treasure room's chest can only be broken once its Blood Slurps are dead
        .Init("pD Infested Chest",
            new State(
                new State("Init",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new TimedTransition(2000, "Guarded")
                    ),
                new State("Guarded",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new EntityNotExistsTransition("pD Blood Slurp", 12, "Open")
                    ),
                new State("Open")
                ),
            new Threshold(0.01,
                new ItemLoot("Potion of Life", 0.5),
                new ItemLoot("Potion of Mana", 0.5),
                new ItemLoot("pD Tome", 0.004),
                new ItemLoot("pD Scepter", 0.004)
                )
            )
        .Init("pD Boss Support",
            new State(
                new State("Idle"),
                new State("Attack",
                    new EntityNotExistsTransition("pD Boss", 30, "Idle"),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, coolDown: 2000),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 22.5, coolDown: 2000, coolDownOffset: 1000),
                    new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 5, fixedAngle: 30, coolDown: 4000)
                    )
                )
            )
        // the Nightmare Colony is part of the wall: shielded while its spawn fights, exposed in between
        .Init("pD Boss",
            new State(
                new ScaleHP2(20),
                new DropPortalOnDeath("Realm Portal", 1.0),
                new State("Idle",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(12, "Awaken")
                    ),
                new State("Awaken",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("The colony hungers..."),
                    new Flash(0x8B0000, 0.5, 4),
                    new TimedTransition(2500, "Shielded")
                    ),
                new State("Shielded",
                    new Order(15, "pD Boss Support", "Attack"),
                    new Spawn("pD Painling", 2, 1, coolDown: 8000),
                    new Shoot(15, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: 2000),
                    new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 3, fixedAngle: 0, coolDown: 3500),
                    new TimedTransition(12000, "Exposed")
                    ),
                new State("Exposed",
                    new Order(15, "pD Boss Support", "Attack"),
                    new ConditionEffectBehavior(ConditionEffectIndex.ArmorBroken),
                    new Taunt("RAAAGH! Feed, my children!"),
                    new Flash(0xFF0000, 0.5, 4),
                    new Shoot(15, count: 3, shootAngle: 10, projectileIndex: 1, coolDown: 800),
                    new Shoot(15, count: 3, shootAngle: 10, projectileIndex: 2, coolDown: 800, coolDownOffset: 400),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 5, fixedAngle: 15, coolDown: 3000),
                    new HpLessTransition(0.3, "Frenzy"),
                    new TimedTransition(8000, "Shielded")
                    ),
                new State("Frenzy",
                    new Order(15, "pD Boss Support", "Attack"),
                    new ConditionEffectBehavior(ConditionEffectIndex.ArmorBroken),
                    new Taunt("THE COLONY WILL CONSUME YOU!"),
                    new Spawn("pD Painling", 3, 1, coolDown: 6000),
                    new Shoot(15, count: 5, shootAngle: 12, projectileIndex: 1, coolDown: 1000),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 4, fixedAngle: 0, coolDown: 2500),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 5, fixedAngle: 15, coolDown: 3000)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Potion of Life", 1),
                new ItemLoot("Potion of Mana", 1),
                new ItemLoot("pD Tome", 0.005),
                new ItemLoot("pD Scepter", 0.005),
                // (no pD Armor: an <Admin/> item, staff only)
                new ItemLoot("pD Spell", 0.005)
                )
            );
    }
}
