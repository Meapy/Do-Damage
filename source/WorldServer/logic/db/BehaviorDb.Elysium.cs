using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Oryx's Elysium (tools/build_elysium.py), Oryx 5's holy island. His Ascended Host hold the roads:
    // Oryx 3's court monsters, ascended, with divine shots and attacks of their own (their XML is
    // the script's). Each road leads to a guardian, the Gilded Sphinx atop the Pyramid and the Oasis
    // Seraph in the Oasis, who drops three Trees of Doom when it dies; with three more on each road,
    // that's twelve. Once no tree and no guardian is left, the Gate Watcher opens the Holy Gates to
    // his temple, where his three Ascended Wardens circle him until they fall (BehaviorDb.OryxDoomed.cs).
    partial class BehaviorDb
    {
        private _ Elysium = () => Behav()
            // a gold fan of wavering missiles, and a slow ring of halos turning between volleys
            .Init("Ascended Noble",
                new State(
                    new BossStats(17000, 25, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(11, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.6, 11, 4), new Wander(0.3)),
                        new Shoot(11, 3, 16, projectileIndex: 0, predictive: 0.3, coolDown: 1100),
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 15, projectileIndex: 1, coolDown: 2400, coolDownOffset: 600)
                    )
                ),
                ElysianLoot()
            )
            // circles you throwing dazing lances that come back, then stops to spin out halos
            .Init("Ascended Aristocrat",
                new State(
                    new BossStats(17000, 25, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(11, "Lances")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.7, 6), new Orbit(0.7, 5, 11, null), new Wander(0.3)),
                        new State("Lances",
                            new Shoot(9, 5, 12, projectileIndex: 0, coolDown: 1000),
                            new TimedTransition(4000, "Halos")
                        ),
                        new State("Halos",
                            new Flash(0xFF8CD8FF, 0.3, 3),
                            new Shoot(0, 8, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 1, coolDown: 600),
                            new TimedTransition(2400, "Lances")
                        )
                    )
                ),
                ElysianLoot()
            )
            // three lines of stakes at three speeds, the fastest breaking armour, and boomerang
            // shockwaves in a cross that turns
            .Init("Ascended Sergeant",
                new State(
                    new BossStats(17000, 40, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 7), new Follow(0.45, 12, 5)),
                        new Shoot(12, 3, 25, projectileIndex: 0, coolDown: 1600),
                        new Shoot(12, 3, 25, projectileIndex: 1, coolDown: 1600),
                        new Shoot(12, 3, 25, projectileIndex: 2, coolDown: 1600),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 20, projectileIndex: 3, coolDown: 2800, coolDownOffset: 1200)
                    )
                ),
                ElysianLoot()
            )
            // slowing orbs, a heavy aimed spike, and a slow ring of silencing bolts when it glows
            .Init("Ascended Officer",
                new State(
                    new BossStats(18000, 35, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Volley")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 7), new Follow(0.5, 12, 4)),
                        new State("Volley",
                            new Shoot(12, 5, 10, projectileIndex: 0, coolDown: 900),
                            new Shoot(12, projectileIndex: 1, predictive: 0.6, coolDown: 1500, coolDownOffset: 700),
                            new TimedTransition(5000, "Ring")
                        ),
                        new State("Ring",
                            new Flash(0xFFFFFFFF, 0.3, 3),
                            new Shoot(0, 10, fixedAngle: 0, projectileIndex: 2, coolDown: 1500, coolDownOffset: 600),
                            new Shoot(0, 10, fixedAngle: 18, projectileIndex: 2, coolDown: 1500, coolDownOffset: 1350),
                            new TimedTransition(3000, "Volley")
                        )
                    )
                ),
                ElysianLoot()
            )
            // charges behind armour-breaking waves that pass cover, then stands and lets fly two
            // rings of great slow bolts and spikes
            .Init("Ascended Major",
                new State(
                    new BossStats(18000, 45, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Charge")),
                    new State("Attack",
                        new State("Charge",
                            new Prioritize(new StayCloseToSpawn(1.2, 8), new Charge(1.5, 10, 2200)),
                            new Shoot(7, 5, 14, projectileIndex: 0, coolDown: 900),
                            new TimedTransition(4500, "Stand")
                        ),
                        new State("Stand",
                            new Flash(0xFFAA70F0, 0.3, 3),
                            new Shoot(0, 6, fixedAngle: 0, projectileIndex: 1, coolDown: 3000, coolDownOffset: 700),
                            new Shoot(0, 6, fixedAngle: 30, projectileIndex: 2, coolDown: 3000, coolDownOffset: 1400),
                            new Shoot(12, projectileIndex: 1, predictive: 0.4, coolDown: 1200),
                            new TimedTransition(3000, "Charge")
                        )
                    )
                ),
                ElysianLoot()
            )
            // keeps back, heals the host round it, and turns a four-way spiral of weakening beams
            .Init("Ascended Deacon",
                new State(
                    new BossStats(15000, 20, 1.0, 0.25),
                    new HealEntity(12, "Ascended Noble", 800, 3000),
                    new HealEntity(12, "Ascended Aristocrat", 800, 3000),
                    new HealEntity(12, "Ascended Cleric", 800, 3000),
                    new HealEntity(12, "Ascended Cardinal", 800, 3000),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new StayBack(0.5, 7), new Wander(0.3)),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 13, projectileIndex: 0, coolDown: 350),
                        new Shoot(12, projectileIndex: 1, predictive: 0.6, coolDown: 1800)
                    )
                ),
                ElysianLoot()
            )
            // a sunburst cone of shockwaves at four speeds, and a dazing nova for anyone up close
            .Init("Ascended Cleric",
                new State(
                    new BossStats(15000, 15, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.6, 12, 3)),
                        new Shoot(10, 5, 12, projectileIndex: 0, coolDown: 1500),
                        new Shoot(10, 5, 12, projectileIndex: 1, coolDown: 1500),
                        new Shoot(10, 5, 12, projectileIndex: 2, coolDown: 1500),
                        new Shoot(10, 5, 12, projectileIndex: 3, coolDown: 1500),
                        new Shoot(3.5, 12, projectileIndex: 4, coolDown: 1000)
                    )
                ),
                ElysianLoot()
            )
            // spears that come back and paralyse, and a fast straight spear aimed where you're going
            .Init("Ascended Cardinal",
                new State(
                    new BossStats(15000, 25, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new StayBack(0.5, 6), new Wander(0.3)),
                        new Shoot(12, 5, 15, projectileIndex: 0, coolDown: 1400),
                        new Shoot(14, projectileIndex: 1, predictive: 1, coolDown: 1000, coolDownOffset: 700)
                    )
                ),
                ElysianLoot()
            )

            // the guardians, for now: no attacks, and three Trees of Doom when they die
            .Init("Gilded Sphinx", Guardian())
            .Init("Oasis Seraph", Guardian())

            // the three who circle Oryx 5 in his temple, evenly spaced; when they fall, he wakes
            .Init("Ascended Warden",
                new State(
                    new BossStats(25000, 30, 1.0, 0),
                    new Orbit(0.6, 6, 20, "Oryx the Mad God 5", speedVariance: 0, radiusVariance: 0, orbitClockwise: true)
                )
            )

            // opens the Holy Gates once no Tree of Doom and no guardian is left on the island
            .Init("O5 Gate Watcher",
                new State(
                    new State("Wait",
                        new TimedTransition(3000, "Watch")
                    ),
                    new State("Watch",
                        new EntitiesNotExistsTransition(150, "Open", ElysiumGatekeepers)
                    ),
                    new State("Open",
                        new RemoveObject("O5 Holy Gate", 10),
                        new Order(40, "Oryx the Mad God 5", "Wardens"),
                        new TimedTransition(1000, "Done")
                    ),
                    new State("Done")
                )
            );

        // what keeps the Holy Gates shut
        private static readonly string[] ElysiumGatekeepers = { "Tree of Doom", "Gilded Sphinx", "Oasis Seraph" };

        private static State Guardian() =>
            new State(
                new BossStats(50000, 30, 1.0, 0),
                new TossOnDeath("Tree of Doom", 3, 8)
            );

        // a little better than the Inferno's (InfernalLoot)
        private static MobDrops[] ElysianLoot() => new MobDrops[]
        {
            new ItemLoot("Potion of Defense", 0.07),
            new ItemLoot("Potion of Attack", 0.07),
            new ItemLoot("Potion of Vitality", 0.04),
            new ItemLoot("Potion of Wisdom", 0.04),
            new ItemLoot("Greater Potion of Life", 0.012),
            new TierLoot(13, ItemType.Weapon, 0.05),
            new TierLoot(14, ItemType.Armor, 0.04),
            new TierLoot(5, ItemType.Ability, 0.05)
        };
    }
}
