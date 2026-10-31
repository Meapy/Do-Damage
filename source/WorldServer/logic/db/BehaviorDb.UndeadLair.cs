using Shared.resources;
using WorldServer.logic.loot;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ UndeadLair = () => UndeadLairBehaviours(Behav(), false);
        private _ ElderUndeadLair = () => UndeadLairBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs)
        private static Ctor UndeadLairBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);

            return db
                .Init(E("Septavius the Ghost God"),
                    new State(
                        // the regular dungeon leads to its Elder version half the time
                        new DropPortalOnDeath("Elder Undead Lair Portal", elder ? 0 : 0.5, xAdjustment: 3),
                        new DropPortalOnDeath("Realm Portal", probability: 1.0, timeout: null),
                        new State(
                            new PlayerWithinTransition(14, "transition1")
                            ),
                        new State(
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Flash(0x00FF00, 0.25, 12),
                            new Wander(0.1),
                            new State("transition1",
                                new TimedTransition(3000, "spiral")
                                ),
                            new State("transition2",
                                new TimedTransition(3000, "ring")
                                ),
                            new State("transition3",
                                new TimedTransition(3000, "quiet")
                                ),
                            new State("transition4",
                                new TimedTransition(3000, "quiet")
                                )
                            ),
                        new State("spiral",
                            new Spawn(N("Lair Ghost Archer"), 1, 1),
                            new Spawn(N("Lair Ghost Knight"), 2, 2),
                            new Spawn(N("Lair Ghost Mage"), 1, 1),
                            new Spawn(N("Lair Ghost Rogue"), 2, 2),
                            new Spawn(N("Lair Ghost Paladin"), 1, 1),
                            new Spawn(N("Lair Ghost Warrior"), 2, 2),
                            new Shoot(20, 3, fixedAngle: 0, coolDownOffset: 0, coolDown: Cd(1000)),
                            new Shoot(20, 3, fixedAngle: 5, coolDownOffset: 200, coolDown: Cd(1000)),
                            new Shoot(20, 3, fixedAngle: 10, coolDownOffset: 400, coolDown: Cd(1000)),
                            new Shoot(20, count: 5, shootAngle: 20, predictive: 1, coolDown: Cd(700)),

                            new Shoot(10, 3, fixedAngle: 72, coolDownOffset: 600, coolDown: Cd(1000)),
                            new Shoot(10, 3, fixedAngle: 96, coolDownOffset: 800, coolDown: Cd(1000)),
                            new TimedTransition(10000, "transition2")
                            ),
                        new State("ring",
                            new Wander(0.3),
                            new Shoot(10, 12, projectileIndex: 4, coolDown: Cd(2000)),
                            new TimedTransition(10000, "transition3")
                            ),
                        new State("quiet",
                            new Wander(0.1),
                            new Shoot(10, 8, projectileIndex: 1, coolDown: Cd(1000)),
                            new Shoot(10, 8, projectileIndex: 1, coolDownOffset: 500, angleOffset: 22.5, coolDown: Cd(1000)),
                            new Shoot(8, 3, shootAngle: 20, projectileIndex: 2, coolDown: Cd(2000)),
                            new TimedTransition(10000, "transition4")
                            ),
                        new State("spawn",
                            new Wander(0.1),
                            new Spawn(N("Ghost Mage of Septavius"), 2, 2),
                            new Spawn(N("Ghost Rogue of Septavius"), 2, 2),
                            new Spawn(N("Ghost Warrior of Septavius"), 2, 2),
                            new Reproduce(N("Ghost Mage of Septavius"), densityMax: 2, coolDown: Cd(1000)),
                            new Reproduce(N("Ghost Rogue of Septavius"), densityMax: 2, coolDown: Cd(1000)),
                            new Reproduce(N("Ghost Warrior of Septavius"), densityMax: 2, coolDown: Cd(1000)),
                            new Shoot(8, 3, shootAngle: 10, projectileIndex: 1, coolDown: Cd(1000)),
                            new TimedTransition(10000, "quiet")
                            )
                    ),
                new Threshold(0.01,
                    new TierLoot(11, ItemType.Weapon, 0.07),
                    new TierLoot(11, ItemType.Armor, 0.07),
                    new TierLoot(4, ItemType.Ring, 0.1),
                    new TierLoot(5, ItemType.Ring, 0.07),
                    new TierLoot(4, ItemType.Ability, 0.1),
                    new TierLoot(5, ItemType.Ability, 0.07),
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Doom Bow", 0.02),
                    new ItemLoot("Undead Lair Key", 0.009, 0, 0.03)
                    ), Elder("Septavius the Ghost God"))
            // the map's Doomed Realms helper would drop a second portal when Septavius dies
            .Init(E("Septavius Summoner"), new State(new Decay(0)))
            .Init(E("Ghost Mage of Septavius"),
                new State(
                    new Prioritize(
                        new Protect(0.625, N("Septavius the Ghost God"), protectionRange: 6),
                        new Follow(0.75, range: 7)
                        ),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ), Elder("Ghost Mage of Septavius"))
            .Init(E("Ghost Rogue of Septavius"),
                new State(
                    new Follow(0.75, range: 1),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ), Elder("Ghost Rogue of Septavius"))
            .Init(E("Ghost Warrior of Septavius"),
                new State(
                    new Follow(0.75, range: 1),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ), Elder("Ghost Warrior of Septavius"))
            .Init(E("Lair Ghost Archer"),
                new State(
                    new Prioritize(
                        new Protect(0.625, N("Septavius the Ghost God"), protectionRange: 6),
                        new Follow(0.75, range: 7)
                        ),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ), Elder("Lair Ghost Archer"))
            .Init(E("Lair Ghost Knight"),
                new State(
                    new Follow(0.75, range: 1),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ), Elder("Lair Ghost Knight"))
            .Init(E("Lair Ghost Mage"),
                new State(
                    new Prioritize(
                        new Protect(0.625, N("Septavius the Ghost God"), protectionRange: 6),
                        new Follow(0.75, range: 7)
                        ),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ), Elder("Lair Ghost Mage"))
            .Init(E("Lair Ghost Paladin"),
                new State(
                    new Follow(0.75, range: 1),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000)),
                    new HealSelf(coolDown: Cd(5000))
                    ), Elder("Lair Ghost Paladin"))
            .Init(E("Lair Ghost Rogue"),
                new State(
                    new Follow(0.75, range: 1),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ),
                new ItemLoot("Magic Potion", 0.25),
                new ItemLoot("Health Potion", 0.25), Elder("Lair Ghost Rogue"))
            .Init(E("Lair Ghost Warrior"),
                new State(
                    new Follow(0.75, range: 1),
                    new Wander(0.4),
                    new Shoot(8, 1, coolDown: Cd(1000))
                    ),
                new ItemLoot("Magic Potion", 0.25),
                new ItemLoot("Health Potion", 0.25), Elder("Lair Ghost Warrior"))

            .Init(E("Lair Skeleton"),
                new State(
                    new Shoot(6),
                    new Prioritize(
                        new Follow(1, range: 1),
                        new Wander(0.4)
                        )
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Skeleton"))
            .Init(E("Lair Skeleton King"),
                new State(
                    new Shoot(10, 3, shootAngle: 10),
                    new Prioritize(
                        new Follow(1, range: 7),
                        new Wander(0.4)
                        )
                    ),
                new TierLoot(5, ItemType.Armor, 0.2),
                new Threshold(0.5,
                    new TierLoot(6, ItemType.Weapon, 0.2),
                    new TierLoot(7, ItemType.Weapon, 0.1),
                    new TierLoot(8, ItemType.Weapon, 0.05),
                    new TierLoot(6, ItemType.Armor, 0.1),
                    new TierLoot(7, ItemType.Armor, 0.05),
                    new TierLoot(3, ItemType.Ring, 0.1),
                    new TierLoot(3, ItemType.Ability, 0.1)
                    ), Elder("Lair Skeleton King"))
            .Init(E("Lair Skeleton Mage"),
                new State(
                    new Shoot(10),
                    new Prioritize(
                        new Follow(1, range: 7),
                        new Wander(0.4)
                        )
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Skeleton Mage"))
            .Init(E("Lair Skeleton Swordsman"),
                new State(
                    new Shoot(5),
                        new Follow(1, range: 5),
                        new Wander(0.5)
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Skeleton Swordsman"))
            .Init(E("Lair Skeleton Veteran"),
                new State(
                    new Shoot(5),
                    new Prioritize(
                        new Follow(1, range: 5),
                        new Wander(0.4)
                        )
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Skeleton Veteran"))
            .Init(E("Lair Mummy"),
                new State(
                    new Shoot(10),
                    new Prioritize(
                        new Follow(0.9, range: 7),
                        new Wander(0.4)
                        )
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Mummy"))
            .Init(E("Lair Mummy King"),
                new State(
                    new Shoot(10),
                    new Prioritize(
                        new Follow(0.9, range: 7),
                        new Wander(0.4)
                        )
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Mummy King"))
            .Init(E("Lair Mummy Pharaoh"),
                new State(
                    new Shoot(10),
                    new Prioritize(
                        new Follow(0.9, range: 7),
                        new Wander(0.4)
                        )
                    ),
                new TierLoot(5, ItemType.Armor, 0.2),
                new Threshold(0.5,
                    new TierLoot(6, ItemType.Weapon, 0.2),
                    new TierLoot(7, ItemType.Weapon, 0.1),
                    new TierLoot(8, ItemType.Weapon, 0.05),
                    new TierLoot(6, ItemType.Armor, 0.1),
                    new TierLoot(7, ItemType.Armor, 0.05),
                    new TierLoot(3, ItemType.Ring, 0.1),
                    new TierLoot(3, ItemType.Ability, 0.1)
                    ), Elder("Lair Mummy Pharaoh"))

            .Init(E("Lair Big Brown Slime"),
                new State(
                    new Shoot(10, 3, shootAngle: 10, coolDown: Cd(500)),
                    new Wander(0.1),
                    new TransformOnDeath(N("Lair Little Brown Slime"), 1, 6, 1)
                    // new SpawnOnDeath(N("Lair Little Brown Slime"), 1.0, 6)
                    ), Elder("Lair Big Brown Slime"))
            .Init(E("Lair Little Brown Slime"),
                new State(
                    new Shoot(10, 3, shootAngle: 10, coolDown: Cd(500)),
                    new Protect(0.1, N("Lair Big Brown Slime"), acquireRange: 5, protectionRange: 2),
                    new Wander(0.1)
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Little Brown Slime"))
            .Init(E("Lair Big Black Slime"),
                new State(
                    new Shoot(10, coolDown: Cd(1000)),
                    new Wander(0.1),
                    new TransformOnDeath(N("Lair Little Black Slime"), 1, 4, 1)
                    //new SpawnOnDeath(N("Lair Medium Black Slime"), 1.0, 4)
                    ), Elder("Lair Big Black Slime"))
            .Init(E("Lair Medium Black Slime"),
                new State(
                    new Shoot(10, coolDown: Cd(1000)),
                    new Wander(0.1),
                    new TransformOnDeath(N("Lair Little Black Slime"), 1, 4, 1)
                    // new SpawnOnDeath(N("Lair Little Black Slime"), 1.0, 4)
                    ), Elder("Lair Medium Black Slime"))
            .Init(E("Lair Little Black Slime"),
                new State(
                    new Shoot(10, coolDown: Cd(1000)),
                    new Wander(0.1)
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Little Black Slime"))

            .Init(E("Lair Construct Giant"),
                new State(
                    new Prioritize(
                        new Follow(0.8, range: 7),
                        new Wander(0.4)
                        ),
                    new Shoot(10, 3, shootAngle: 20, coolDown: Cd(1000)),
                    new Shoot(10, projectileIndex: 1, coolDown: Cd(1000))
                    ),
                new TierLoot(5, ItemType.Armor, 0.2),
                new Threshold(0.5,
                    new TierLoot(6, ItemType.Weapon, 0.2),
                    new TierLoot(7, ItemType.Weapon, 0.1),
                    new TierLoot(8, ItemType.Weapon, 0.05),
                    new TierLoot(6, ItemType.Armor, 0.1),
                    new TierLoot(7, ItemType.Armor, 0.05),
                    new TierLoot(3, ItemType.Ring, 0.1),
                    new TierLoot(3, ItemType.Ability, 0.1)
                    ), Elder("Lair Construct Giant"))
            .Init(E("Lair Construct Titan"),
                new State(
                    new Prioritize(
                        new Follow(0.8, range: 7),
                        new Wander(0.4)
                        ),
                    new Shoot(10, 3, shootAngle: 20, coolDown: Cd(1000)),
                    new Shoot(10, 3, shootAngle: 20, projectileIndex: 1, coolDownOffset: 100, coolDown: Cd(2000))
                    ),
                new TierLoot(5, ItemType.Armor, 0.2),
                new Threshold(0.5,
                    new TierLoot(6, ItemType.Weapon, 0.2),
                    new TierLoot(7, ItemType.Weapon, 0.1),
                    new TierLoot(8, ItemType.Weapon, 0.05),
                    new TierLoot(6, ItemType.Armor, 0.1),
                    new TierLoot(7, ItemType.Armor, 0.05),
                    new TierLoot(3, ItemType.Ring, 0.1),
                    new TierLoot(3, ItemType.Ability, 0.1)
                    ), Elder("Lair Construct Titan"))

            .Init(E("Lair Brown Bat"),
                new State(
                    new Wander(0.1),
                    new Charge(3, 8, 2000),
                    new Shoot(3, coolDown: Cd(1000))
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Brown Bat"))
            .Init(E("Lair Ghost Bat"),
                new State(
                    new Wander(0.1),
                    new Charge(3, 8, 2000),
                    new Shoot(3, coolDown: Cd(1000))
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Ghost Bat"))

            .Init(E("Lair Reaper"),
                new State(
                    new Shoot(3),
                    new Follow(1.3, range: 1),
                    new Wander(0.1)
                    ),
                new TierLoot(5, ItemType.Armor, 0.2),
                new Threshold(0.5,
                    new TierLoot(6, ItemType.Weapon, 0.2),
                    new TierLoot(7, ItemType.Weapon, 0.1),
                    new TierLoot(8, ItemType.Weapon, 0.05),
                    new TierLoot(6, ItemType.Armor, 0.1),
                    new TierLoot(7, ItemType.Armor, 0.05),
                    new TierLoot(3, ItemType.Ring, 0.1),
                    new TierLoot(3, ItemType.Ability, 0.1)
                    ), Elder("Lair Reaper"))
            .Init(E("Lair Vampire"),
                new State(
                    new Shoot(10, coolDown: Cd(500)),
                    new Shoot(3, coolDown: Cd(1000)),
                    new Follow(1.3, range: 1),
                    new Wander(0.1)
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new ItemLoot("Health Potion", 0.05), Elder("Lair Vampire"))
            .Init(E("Lair Vampire King"),
                new State(
                    new Shoot(10, coolDown: Cd(500)),
                    new Shoot(3, coolDown: Cd(1000)),
                    new Follow(1.3, range: 1),
                    new Wander(0.1)
                    ),
                new TierLoot(5, ItemType.Armor, 0.2),
                new Threshold(0.5,
                    new TierLoot(6, ItemType.Weapon, 0.2),
                    new TierLoot(7, ItemType.Weapon, 0.1),
                    new TierLoot(8, ItemType.Weapon, 0.05),
                    new TierLoot(6, ItemType.Armor, 0.1),
                    new TierLoot(7, ItemType.Armor, 0.05),
                    new TierLoot(3, ItemType.Ring, 0.1),
                    new TierLoot(3, ItemType.Ability, 0.1)
                    ), Elder("Lair Vampire King"))

            .Init(E("Lair Grey Spectre"),
                new State(
                    new Wander(0.1),
                    new Shoot(10, coolDown: Cd(1000)),
                    new Grenade(2.5, 50, 8, coolDown: Cd(1000))
                    ), Elder("Lair Grey Spectre"))
            .Init(E("Lair Blue Spectre"),
                new State(
                    new Wander(0.1),
                    new Shoot(10, coolDown: Cd(1000)),
                    new Grenade(2.5, 70, 8, coolDown: Cd(1000))
                    ), Elder("Lair Blue Spectre"))
            .Init(E("Lair White Spectre"),
                new State(
                    new Wander(0.1),
                    new Shoot(10, coolDown: Cd(1000)),
                    new Grenade(2.5, 90, 8, coolDown: Cd(1000))
                    ),
                new Threshold(0.5,
                    new TierLoot(4, ItemType.Ability, 0.15)
                    ), Elder("Lair White Spectre"))
            .Init(E("Lair Burst Trap"),
                new State(
                    new State("FinnaBustANut",
                        new PlayerWithinTransition(3, "Aaa")
                        ),
                    new State("Aaa",
                        new Shoot(8.4, count: 12, projectileIndex: 0),
                        new Suicide()
                        )), Elder("Lair Burst Trap"))
            .Init(E("Lair Blast Trap"),
                new State(
                    new State("FinnaBustANut",
                        new PlayerWithinTransition(3, "Aaa")
                        ),
                    new State("Aaa",
                        new Shoot(25, projectileIndex: 0, count: 12, coolDown: Cd(3000)),
                        new Suicide()
                        )), Elder("Lair Blast Trap"));
        }
    }
}
