using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Oryx's Inferno's Infernal Court (tools/build_hash_cellar.py), who hold the Burning Path to
    // Oryx 4: Oryx 3's court monsters, burnt, with an Oryx 2 minion's tricks on top. Their XML is
    // their O3 base's with every shot a sixth stronger; projectiles 10 and up are the O2 minion's.
    partial class BehaviorDb
    {
        private static State OfficerAttack(string p, int shuriken) =>
            new State(p + "Attack",
                new Prioritize(new StayCloseToSpawn(0.6, 7), new Follow(0.55, 12, 4)),
                new Shoot(15, projectileIndex: shuriken, predictive: 1, coolDown: 2500),
                new Shoot(10, 3, 10, projectileIndex: 11, coolDown: 2500, coolDownOffset: 1250),
                new State(p + "Volley",
                    new Shoot(12, 3, 12, projectileIndex: 2, coolDown: 800),
                    new Shoot(12, projectileIndex: 4, predictive: 0.5, coolDown: 1200),
                    new TimedTransition(5000, p + "Spike")
                ),
                new State(p + "Spike",
                    new Flash(0xFFFF4400, 0.3, 3),
                    new Shoot(12, projectileIndex: 5, coolDown: 1500, coolDownOffset: 900),
                    new Shoot(0, 8, fixedAngle: 0, projectileIndex: 3, coolDown: 2000, coolDownOffset: 900),
                    new TimedTransition(3000, p + "Volley")
                )
            );

        private _ HashCellar = () => Behav()
            // the Officer's volleys and spikes, and the Henchman's confusing shuriken and bolts; those
            // Oryx 4 summons throw the shuriken that doesn't confuse (projectile 12)
            .Init("Infernal Officer",
                new State(
                    new BossStats(15000, 40, 1.0, 0.25),
                    new State("Idle",
                        new EntityExistsTransition("Oryx the Mad God 4", 30, "Court Volley"),
                        new PlayerWithinTransition(12, "Volley")
                    ),
                    OfficerAttack("", 10),
                    OfficerAttack("Court ", 12)
                ),
                InfernalLoot()
            )
            // the Major's charges, and the Abomination's cone of fire for anyone who gets close
            .Init("Brimstone Major",
                new State(
                    new BossStats(15000, 40, 1.0, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Charge")),
                    new State("Attack",
                        new Shoot(3, 3, 5, projectileIndex: 10, coolDown: 1000),
                        new Shoot(3, 5, 5, projectileIndex: 11, coolDown: 1000),
                        new Shoot(3, 7, 5, projectileIndex: 12, coolDown: 1000),
                        new Shoot(3, 5, 5, projectileIndex: 13, coolDown: 1000),
                        new Shoot(3, 3, 5, projectileIndex: 14, coolDown: 1000),
                        new State("Charge",
                            new Prioritize(new StayCloseToSpawn(1.2, 8), new Charge(1.5, 10, 2200)),
                            new Shoot(6, 5, 12, projectileIndex: 0, coolDown: 1000),
                            new Shoot(12, 2, 16, projectileIndex: 2, coolDown: 1300),
                            new TimedTransition(5000, "Stand")
                        ),
                        new State("Stand",
                            new Flash(0xFFFF4400, 0.3, 3),
                            new Shoot(0, 8, fixedAngle: 22, projectileIndex: 5, coolDown: 3000, coolDownOffset: 800),
                            new Shoot(12, projectileIndex: 4, coolDown: 900),
                            new TimedTransition(3000, "Charge")
                        )
                    )
                ),
                InfernalLoot()
            )
            // the Cleric's shockwaves, the Deacon's healing of the court, and the Aberrant's blasters
            .Init("Cinder Cleric",
                new State(
                    new BossStats(14000, 15, 1.1, 0.25),
                    new HealEntity(12, "Infernal Officer", 700, 3000),
                    new HealEntity(12, "Brimstone Major", 700, 3000),
                    new HealEntity(12, "Doom Cardinal", 700, 3000),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.6, 12, 3)),
                        new Shoot(10, 3, 15, projectileIndex: 1, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 2, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 3, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 4, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 5, coolDown: 1600),
                        new Shoot(3, 6, projectileIndex: 0, coolDown: 900),
                        new TossObject("Aberrant Blaster", 5, coolDown: 4000)
                    )
                ),
                InfernalLoot()
            )
            // the Cardinal's paralysing spears, the Henchman's shuriken, and a Vintner to guard it
            .Init("Doom Cardinal",
                new State(
                    new BossStats(14000, 25, 1.1, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new StayBack(0.5, 6), new Wander(0.3)),
                        new Shoot(12, 5, 15, projectileIndex: 0, coolDown: 1400),
                        new Shoot(15, projectileIndex: 10, predictive: 1, coolDown: 2000, coolDownOffset: 700),
                        new Spawn("Vintner of Oryx", 1, 1, 10000)
                    )
                ),
                InfernalLoot()
            )

            // Oryx 4's Artifacts: red Guardian Elements on fixed rings at one speed, so they stay
            // evenly spaced round him (the inner ones counter to the outer)
            .Init("Infernal Element",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(1, InfernalInner, 20, "Oryx the Mad God 4", speedVariance: 0, radiusVariance: 0, orbitClockwise: true),
                    new Shoot(25, 3, 10, projectileIndex: 0, coolDown: 1000),
                    new State("Live", new TimedTransition(14000, "Gone")),
                    new State("Gone", new Suicide())
                )
            )
            .Init("Infernal Outer Element",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(1.1, InfernalOuter, 25, "Oryx the Mad God 4", speedVariance: 0, radiusVariance: 0, orbitClockwise: false),
                    new Shoot(6, 2, 20, projectileIndex: 0, coolDown: 900),
                    new State("Live", new TimedTransition(14000, "Gone")),
                    new State("Gone", new Suicide())
                )
            );

        // the Infernal Elements' rings round Oryx 4, in tiles
        private const double InfernalInner = 2.5;
        private const double InfernalOuter = 9.5;

        // a little better than the Sanctuary court's (CourtLoot)
        private static MobDrops[] InfernalLoot() => new MobDrops[]
        {
            new ItemLoot("Potion of Defense", 0.06),
            new ItemLoot("Potion of Attack", 0.06),
            new ItemLoot("Potion of Vitality", 0.03),
            new ItemLoot("Potion of Wisdom", 0.03),
            new ItemLoot("Greater Potion of Life", 0.01),
            new TierLoot(13, ItemType.Weapon, 0.04),
            new TierLoot(13, ItemType.Armor, 0.04),
            new TierLoot(5, ItemType.Ability, 0.04),
            new ItemLoot("Bounty Hunter's Dagger", 0.001)
        };
    }
}
