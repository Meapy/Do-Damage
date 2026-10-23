using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ AncientRuins = () => Behav()
        .Init("Ruins Cactus",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true)
                )
            )
        // invisible traps placed around each cactus
        .Init("Ruins Cactus Burst",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("Idle",
                    new PlayerWithinTransition(2.5, "Burst")
                    ),
                new State("Burst",
                    new Shoot(3, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 45, coolDown: 2000),
                    new NoPlayerWithinTransition(3.5, "Idle")
                    )
                )
            )
        .Init("Ruins Elemental",
            new State(
                new StayCloseToSpawn(0.5, 6),
                new Wander(0.4),
                new Shoot(8, count: 1, projectileIndex: 0, predictive: 0.3, coolDown: 1500),
                new Shoot(6, count: 3, shootAngle: 120, projectileIndex: 1, coolDown: 4000)
                ),
            new ItemLoot("Health Potion", 0.05)
            )
        .Init("Ruins Scorpion",
            new State(
                new Follow(0.8, 8, 1),
                new Wander(0.3),
                new Shoot(4, count: 1, projectileIndex: 1, coolDown: 800),
                new Shoot(7, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 1500)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("Ruins Vulture",
            new State(
                new Follow(1, 10, 3),
                new Wander(0.5),
                new Shoot(9, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1200)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("Ruins Antlion",
            new State(
                new StayCloseToSpawn(0.5, 3),
                new Wander(0.2),
                new Shoot(5, count: 2, shootAngle: 10, projectileIndex: 0, coolDown: 900)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("Ruins Warrior",
            new State(
                new Follow(0.7, 9, 1),
                new Wander(0.3),
                new Shoot(4, count: 2, shootAngle: 15, projectileIndex: 0, coolDown: 700)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("Ruins Paladin",
            new State(
                new Follow(0.6, 9, 2),
                new Wander(0.3),
                new Shoot(5, count: 3, shootAngle: 12, projectileIndex: 0, coolDown: 900)
                )
            )
        .Init("Ruins Knight",
            new State(
                new Follow(0.5, 9, 1),
                new Wander(0.3),
                new Shoot(4, count: 1, projectileIndex: 0, coolDown: 600)
                )
            )
        // the monoliths call up Terracotta guards while players are near
        .Init("Ruins Monolith",
            new State(
                new State("Idle",
                    new PlayerWithinTransition(10, "Summon")
                    ),
                new State("Summon",
                    new Spawn("Ruins Paladin", 1, 0, coolDown: 6000),
                    new Spawn("Ruins Knight", 1, 0, coolDown: 9000),
                    new NoPlayerWithinTransition(14, "Idle")
                    )
                ),
            new ItemLoot("Magic Potion", 0.1)
            )
        .Init("Ruins Boss Spirit",
            new State(
                new Orbit(0.5, 4, 15, "Ruins Boss Golem", speedVariance: 0.1, radiusVariance: 0.5)
                )
            )
        .Init("Ruins Boss Sand Bomb",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("Idle"),
                new State("Active",
                    new EntityNotExistsTransition("Ruins Boss Golem", 30, "Idle"),
                    new State("Charge",
                        new Flash(0xFFAA00, 0.25, 6),
                        new TimedTransition(1500, "Blast")
                        ),
                    new State("Blast",
                        new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, coolDown: 5000),
                        new TimedTransition(3500, "Charge")
                        )
                    )
                )
            )
        // its spirits shield it, and it wakes the sand bombs around its chamber
        .Init("Ruins Boss Golem",
            new State(
                new ScaleHP2(20),
                new DropPortalOnDeath("Realm Portal", 1.0),
                new State("Idle",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(8, "Awaken")
                    ),
                new State("Awaken",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("WHO DISTURBS THE SANDS?"),
                    new Flash(0xD2B48C, 0.5, 4),
                    new TimedTransition(2000, "Crush")
                    ),
                new State("Crush",
                    new Order(20, "Ruins Boss Sand Bomb", "Active"),
                    new Follow(0.4, 10, 2),
                    new Shoot(10, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1200),
                    new Shoot(10, count: 1, projectileIndex: 3, predictive: 0.3, coolDown: 2000),
                    new Shoot(8, count: 2, shootAngle: 20, projectileIndex: 1, coolDown: 1600),
                    new HpLessTransition(0.66, "Spirits")
                    ),
                new State("Spirits",
                    new Order(20, "Ruins Boss Sand Bomb", "Active"),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("Spirits of the desert, shield me!"),
                    new ReturnToSpawn(1),
                    new Spawn("Ruins Boss Spirit", 3, 1),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 6, fixedAngle: 22.5, coolDown: 3000),
                    new TimedTransition(1500, "Spirits Wait")
                    ),
                new State("Spirits Wait",
                    new Order(20, "Ruins Boss Sand Bomb", "Active"),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 6, fixedAngle: 22.5, coolDown: 3000),
                    new Shoot(12, count: 2, shootAngle: 20, projectileIndex: 5, coolDown: 1500),
                    new EntityNotExistsTransition("Ruins Boss Spirit", 30, "Quake")
                    ),
                new State("Quake",
                    new Order(20, "Ruins Boss Sand Bomb", "Active"),
                    new StayCloseToSpawn(0.5, 5),
                    new Wander(0.3),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 6, fixedAngle: 0, coolDown: 2500),
                    new Shoot(12, count: 2, shootAngle: 20, projectileIndex: 5, coolDown: 1000),
                    new Shoot(12, count: 1, projectileIndex: 7, predictive: 0.3, coolDown: 2000),
                    new HpLessTransition(0.33, "Rage")
                    ),
                new State("Rage",
                    new Order(20, "Ruins Boss Sand Bomb", "Active"),
                    new Taunt("I WILL BURY YOU!"),
                    new Flash(0xFF0000, 0.5, 6),
                    new Follow(0.6, 12, 2),
                    new Shoot(12, count: 5, shootAngle: 12, projectileIndex: 8, coolDown: 1500),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 10, fixedAngle: 0, coolDown: 3000),
                    new Shoot(10, count: 2, shootAngle: 30, projectileIndex: 11, coolDown: 1800),
                    new Shoot(6, count: 3, shootAngle: 20, projectileIndex: 1, coolDown: 800)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 1),
                new ItemLoot("Potion of Vitality", 0.5),
                new ItemLoot("Sandstone Seal", 0.01)
                )
            );
    }
}
