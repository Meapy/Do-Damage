using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Hive: the Queen Bee's dungeon, after Suestwo's player-made design (UGC1, TheHive.xml). Its
    // map is tools/build_hive.py's. The rooms hold Fat Bees (a quarter come back angry when they
    // die), Fat Angry Bees that make you bleed, Small Bees that charge and sting, Mini Hives that
    // keep sending them out until broken, and Maggot Eggs that crack at the first hit, letting out
    // larvae that grow into Fat Bees if left alone. The Queen darts about her room, stinging in
    // rings and dazing with honey, resting unhurt between attacks, while she lobs slowing honey
    // bombs and more eggs at you. Bees and larvae that hatch during the fight (the "2" Fat Bees)
    // give no experience or loot, so the Queen's eggs can't be farmed.
    partial class BehaviorDb
    {
        private _ Hive = () => Behav()
        .Init("TH Queen Bee",
            new State(
                new ScaleHP2(20),
                new StayCloseToSpawn(1.1, 7),
                new State("Start",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(7, "Go")
                    ),
                new State("Fight",
                    new Grenade(3, 100, range: 7, coolDown: 5000, effect: ConditionEffectIndex.Slowed, effectDuration: 2000, color: 0xffdf00),
                    new TossObject("TH Maggot Egg1", range: 5, coolDown: 3000, densityRange: 12, maxDensity: 8),
                    // a ring of stingers as she darts about
                    new State("Go",
                        new Wander(1),
                        new Shoot(6, count: 8, shootAngle: 45, projectileIndex: 0, coolDown: 1500),
                        new TimedTransition(3000, "Rest1")
                        ),
                    new State("Rest1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0x00ff00, 0.2, 10),
                        new Prioritize(
                            new Follow(0.4, 9, 2),
                            new Wander(0.4)
                            ),
                        new TimedTransition(2000, "DazeEm")
                        ),
                    // three dazing globs of honey at a time
                    new State("DazeEm",
                        new Wander(1),
                        new Shoot(6, count: 3, shootAngle: 35, projectileIndex: 1, coolDown: 600),
                        new TimedTransition(3500, "Rest2")
                        ),
                    new State("Rest2",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0x00ff00, 0.2, 10),
                        new Prioritize(
                            new Follow(0.4, 9, 2),
                            new Wander(0.4)
                            ),
                        new TimedTransition(2000, "Go")
                        )
                    )
                ),
            new Threshold(0.05,
                new ItemLoot("Potion of Dexterity", 1),
                new ItemLoot("TH Honey bottle", 1),
                new ItemLoot("TH Honey bottle", 1),
                new ItemLoot("HoneyScepter", 0.085),
                new ItemLoot("Orb of Sweet Demise", 0.085),
                new TierLoot(5, ItemType.Weapon, 0.07),
                new TierLoot(6, ItemType.Weapon, 0.06),
                new TierLoot(7, ItemType.Weapon, 0.015),
                new TierLoot(6, ItemType.Armor, 0.14),
                new TierLoot(7, ItemType.Armor, 0.015)
                ),
            new Threshold(0.05,
                LootTemplates.MountainDrop()
                ),
            new ItemLoot("Bee Generator", 0.03)
            )
        // the bees in the rooms
        .Init("TH Fat Bees",
            FatBee("TH Red Fat Bees"),
            new Threshold(0.5,
                new TierLoot(3, ItemType.Armor, 0.07),
                new TierLoot(4, ItemType.Armor, 0.015),
                new TierLoot(5, ItemType.Armor, 0.015)
                )
            )
        .Init("TH Red Fat Bees",
            AngryBee(),
            new Threshold(0.5,
                new TierLoot(3, ItemType.Weapon, 0.07),
                new TierLoot(4, ItemType.Weapon, 0.06),
                new TierLoot(5, ItemType.Weapon, 0.015)
                )
            )
        // the same bees hatched during the fight, or come back angry: no experience, no loot
        .Init("TH Fat Bees 2",
            FatBee("TH Red Fat Bees 2")
            )
        .Init("TH Red Fat Bees 2", AngryBee())
        // Small Bees charge in and sting on contact; near the Queen they circle her
        .Init("TH Small Bees",
            new State(
                new Prioritize(
                    new Orbit(1.25, 4, 10, "TH Queen Bee"),
                    new Charge(1.35, 10, coolDown: 600),
                    new Wander(1.25)
                    ),
                new Shoot(1, count: 1, projectileIndex: 0, coolDown: 1000)
                )
            )
        // a Mini Hive keeps sending out Small Bees; broken down to a quarter, it bursts, letting out
        // the last of them
        .Init("TH Mini Hive",
            new State(
                new TransformOnDeath("TH Small Bees", 2, 3),
                new State("Swarm",
                    new Spawn("TH Small Bees", maxChildren: 6, initialSpawn: 0, coolDown: 3000),
                    new HpLessTransition(0.25, "Burst")
                    ),
                new State("Burst",
                    new Suicide()
                    )
                ),
            new ItemLoot("TH Honey bottle", 0.5),
            new ItemLoot("TH Honey bottle", 0.5),
            new ItemLoot("TH Honey bottle", 0.5)
            )
        // eggs crack at the first hit
        .Init("TH Maggot Egg1", MaggotEgg())
        .Init("TH Maggot Egg2", MaggotEgg())
        .Init("TH Maggot Egg3", MaggotEgg())
        // larvae circle the Queen if she's near, nip, and grow into Fat Bees after 10 seconds
        .Init("TH Maggots",
            new State(
                new State("Crawl",
                    new Prioritize(
                        new Orbit(0.75, 10, 10, "TH Queen Bee"),
                        new Wander(0.85)
                        ),
                    new Shoot(6, count: 1, projectileIndex: 0, coolDown: 1000),
                    new TimedTransition(10000, "Grow")
                    ),
                new State("Grow",
                    new Transform("TH Fat Bees 2")
                    )
                )
            );

        // a quarter come back angry when they die
        private static State FatBee(string angry) =>
            new State(
                new TransformOnDeath(angry, probability: 0.25),
                new Wander(0.9),
                new Shoot(6, count: 1, projectileIndex: 0, predictive: 0.5, coolDown: 1000)
                );

        // chases you for a while (8 s, then a 2 s rest), making you bleed
        private static State AngryBee() =>
            new State(
                new Prioritize(
                    new Follow(0.8, 5, 2, duration: 8000, coolDown: 2000),
                    new Wander(0.8)
                    ),
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 1000)
                );

        private static State MaggotEgg() =>
            new State(
                new TransformOnDeath("TH Maggots", 2, 3),
                new State("Wait",
                    new HpLessTransition(0.98, "Crack")
                    ),
                new State("Crack",
                    new Suicide()
                    )
                );
    }
}
