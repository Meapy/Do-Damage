using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;
using WorldServer.core.worlds;

namespace WorldServer.logic
{
    // The last two stages of the Oryx chain, from Doomed Realms: Oryx the Mad God 4 in the Hash Cellar
    // and Oryx the Mad God 5 in the Doomed Cellar. These replace the generated Doomed Realms versions
    // (InitDoomed), whose Oryx 4 stuck in its first ring phase for good.
    partial class BehaviorDb
    {
        private _ OryxDoomed = () => Behav()
            // Stage 6: Oryx 1's moveset, corrupted and harder, with Knights of Doom and White Moons
            .Init("Oryx the Mad God 4",
                new State(
                    new BossStats(240000, 50, 1.0, 0.3),
                    new DropPortalOnDeath("Locked Wine Cellar Portal", 1.0, timeout: 600, xAdjustment: 2,
                        name: "Locked: Doomed Cellar", lockedHint: SealedDungeons.WhyDungeon("Doomed Cellar")
                            ?? "This portal is locked. Use a Doomed Cellar Incantation beside it to open it."),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120, xAdjustment: -2),
                    new AnnounceOnDeath(SealedDungeons.WhyDungeon("Doomed Cellar") != null
                        ? "{PL_LIST} burned Oryx the Mad God out of the Hash Cellar! He flees to the Doomed Cellar, sealed for now..."
                        : "{PL_LIST} burned Oryx the Mad God out of the Hash Cellar! He makes his last stand in the Doomed Cellar, sealed by a Doomed Cellar Incantation..."),
                    new State("Dormant",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(15, "Awaken")
                    ),
                    new State("Awaken",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFF4400, 0.4, 10),
                        new Taunt(true, "Three times you have struck me down. Here, in the fire, I am stronger."),
                        new TimedTransition(4000, "Hatred")
                    ),
                    new State("Cycle",
                        new HpLessTransition(0.25, "Ascend"),
                        new State("Hatred",
                            new Taunt("Fools! I still have {HP} hitpoints!"),
                            new Wander(0.15),
                            new StayCloseToSpawn(0.4, 6),
                            // his Infernal Court, from the Burning Path (BehaviorDb.HashCellar.cs)
                            new Spawn("Infernal Officer", 1, 1, 12000),
                            new Spawn("Brimstone Major", 1, 0, 12000),
                            new Shoot(25, 5, 10, projectileIndex: 4, coolDown: 1000),
                            new Shoot(25, 3, 12, projectileIndex: 2, predictive: 0.6, coolDown: 800),
                            new Shoot(25, 2, 6, projectileIndex: 3, coolDown: 1300, coolDownOffset: 600),
                            new TimedTransition(12000, "Silence")
                        ),
                        new State("Silence",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new ReturnToSpawn(1.5),
                            new Flash(0xFF389E13, 0.5, 20),
                            new Taunt("BE SILENT!!!"),
                            new Shoot(0, 4, fixedAngle: 0, rotateAngle: 15, projectileIndex: 17, coolDown: 260),
                            new TossObject("Ring Element", 9, 24, 320000),
                            new TossObject("Ring Element", 9, 48, 320000),
                            new TossObject("Ring Element", 9, 72, 320000),
                            new TossObject("Ring Element", 9, 96, 320000),
                            new TossObject("Ring Element", 9, 120, 320000),
                            new TossObject("Ring Element", 9, 144, 320000),
                            new TossObject("Ring Element", 9, 168, 320000),
                            new TossObject("Ring Element", 9, 192, 320000),
                            new TossObject("Ring Element", 9, 216, 320000),
                            new TossObject("Ring Element", 9, 240, 320000),
                            new TossObject("Ring Element", 9, 264, 320000),
                            new TossObject("Ring Element", 9, 288, 320000),
                            new TossObject("Ring Element", 9, 312, 320000),
                            new TossObject("Ring Element", 9, 336, 320000),
                            new TossObject("Ring Element", 9, 360, 320000),
                            new TimedTransition(10000, "Artifacts")
                        ),
                        new State("Artifacts",
                            new Taunt("My Artifacts will protect me!"),
                            new Shoot(25, 3, 20, projectileIndex: 16, coolDown: 1800, coolDownOffset: 500),
                            new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 8, coolDown: 2200),
                            new Shoot(3, 10, projectileIndex: 7, coolDown: 700),
                            // four Infernal Elements circle him close, four more further out
                            new TossObject("Infernal Element", InfernalInner, 0, 90000001, 800),
                            new TossObject("Infernal Element", InfernalInner, 90, 90000001, 800),
                            new TossObject("Infernal Element", InfernalInner, 180, 90000001, 800),
                            new TossObject("Infernal Element", InfernalInner, 270, 90000001, 800),
                            new TossObject("Infernal Outer Element", InfernalOuter, 45, 90000001, 800),
                            new TossObject("Infernal Outer Element", InfernalOuter, 135, 90000001, 800),
                            new TossObject("Infernal Outer Element", InfernalOuter, 225, 90000001, 800),
                            new TossObject("Infernal Outer Element", InfernalOuter, 315, 90000001, 800),
                            new TimedTransition(14000, "Moons")
                        ),
                        // White Moons circle him; his flames punish anyone who gets close
                        new State("Moons",
                            new Taunt("I am the master of this existence!"),
                            new Follow(0.35, 14, 6),
                            new TossObject("White Moon", 8, 0, 90000001),
                            new TossObject("White Moon", 8, 60, 90000001),
                            new TossObject("White Moon", 8, 120, 90000001),
                            new TossObject("White Moon", 8, 180, 90000001),
                            new TossObject("White Moon", 8, 240, 90000001),
                            new TossObject("White Moon", 8, 300, 90000001),
                            new Shoot(6, 3, 12, projectileIndex: 1, coolDown: 900),
                            new Shoot(25, 2, 10, projectileIndex: 5, predictive: 0.5, coolDown: 1400),
                            new TimedTransition(12000, "Doom Ring")
                        ),
                        // telegraphed: a weakening ring with gaps to stand in
                        new State("Doom Ring",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new ReturnToSpawn(1.5),
                            new Flash(0xFFFFFFFF, 0.3, 6),
                            new Taunt("Feel the weight of my doom!"),
                            new State("Doom Charge",
                                new TimedTransition(2000, "Doom Fire")
                            ),
                            new State("Doom Fire",
                                new Shoot(0, 16, fixedAngle: 0, projectileIndex: 19, coolDown: 1500),
                                new Shoot(0, 16, fixedAngle: 11.25, projectileIndex: 19, coolDown: 1500, coolDownOffset: 750),
                                new TimedTransition(3000, "Hatred")
                            )
                        )
                    ),
                    new State("Ascend",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new ChangeSize(15, 220),
                        new Flash(0xFFFF0000, 0.3, 12),
                        new Taunt(true, "I HAVE HAD ENOUGH OF YOU! THE FIRE TAKES YOU ALL!"),
                        new Shoot(0, 8, fixedAngle: 0, rotateAngle: 9, projectileIndex: 13, coolDown: 500),
                        new Shoot(0, 8, fixedAngle: 22, rotateAngle: -9, projectileIndex: 14, coolDown: 500, coolDownOffset: 250),
                        new TimedTransition(4000, "Rage")
                    ),
                    new State("Rage",
                        new Follow(0.6, 20, 3),
                        new Flash(0xFFFF0000, 1, 9000000),
                        new Spawn("Infernal Officer", 2, 0.5, 8000),
                        new Spawn("Brimstone Major", 1, 1, 10000),
                        new Shoot(25, 5, 10, projectileIndex: 16, coolDown: 1600),
                        new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 18, coolDown: 3200),
                        new Shoot(25, 3, 12, projectileIndex: 2, predictive: 0.8, coolDown: 700),
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 12, projectileIndex: 14, coolDown: 1200),
                        new Taunt(0.4, 6000, "DIE!!!", "ENOUGH!!!", "Your bones will feed my fire!")
                    )
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 0.5),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Greater Potion of Life", 0.08),
                    new ItemLoot("Greater Potion of Mana", 0.05),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Oryx", 0.14)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Speed", 0.65),
                    new ItemLoot("Potion of Dexterity", 0.65),
                    new ItemLoot("Potion of Vitality", 0.65),
                    new ItemLoot("Potion of Wisdom", 0.65),
                    new ItemLoot("Shattered Rune of Oryx", 0.3),
                    new ItemLoot("Broken Oryx Armor", 0.25),
                    new ItemLoot("Large Oryx Cloth", 0.08),
                    new ItemLoot("Small Oryx Cloth", 0.08),
                    new ItemLoot("Ring of Oryx", 0.012),
                    new ItemLoot("Oryx Hide Armor", 0.012),
                    new ItemLoot("Oryx Hide Robe", 0.012),
                    new ItemLoot("Oryx Scale Armor", 0.012),
                    new ItemLoot("Oryx Tooth Sword", 0.012),
                    new ItemLoot("Doom Dirk", 0.012),
                    new ItemLoot("Flying Doom Daggers", 0.012),
                    new ItemLoot("Tome of Noble Assault", 0.012),
                    new ItemLoot("Doom Poison", 0.003)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(13, ItemType.Weapon, 0.1),
                    new TierLoot(14, ItemType.Weapon, 0.1),
                    new TierLoot(14, ItemType.Armor, 0.12),
                    new TierLoot(15, ItemType.Armor, 0.07),
                    new TierLoot(6, ItemType.Ability, 0.16),
                    new TierLoot(6, ItemType.Ring, 0.16),
                    new TierLoot(7, ItemType.Ability, 0.03),
                    new TierLoot(7, ItemType.Ring, 0.03)
                )
            )

            // Stage 7, the finale. Invulnerable until his Trees of Doom fall; the Stone Guardians from
            // the castle stand beside him (BossStats gives them Doomed Cellar stats).
            .Init("Oryx the Mad God 5",
                new State(
                    new BossStats(320000, 55, 0.8, 0.35),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120, xAdjustment: 2),
                    new AnnounceOnDeath("{PL_LIST} vanquished Oryx the Mad God in the Doomed Cellar! The Realm is free... for now."),
                    new State("Dormant",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(20, "Trees")
                    ),
                    new State("Trees",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "How DARE you follow me here! My Trees of Doom will keep me whole forever."),
                        new Shoot(15, 3, 12, projectileIndex: 0, predictive: 0.4, coolDown: 2000),
                        new State("Trees Wait", new TimedTransition(3000, "Trees Watch")),
                        // the twenty trees stand all over the cellar, in its gardens and side rooms too
                        new State("Trees Watch",
                            new Taunt(true, 20000, "My Trees of Doom still stand! Search my cellar for them, if you dare."),
                            new EntitiesNotExistsTransition(60, "Awaken 1", "Tree of Doom"))
                    ),
                    new State("Awaken 1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFF0000, 0.25, 30),
                        new Taunt(true, "You have killed me too many times. This is MY realm."),
                        new TimedTransition(3500, "Awaken 2")
                    ),
                    new State("Awaken 2",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "Now, I will rip your souls from my realm."),
                        new TimedTransition(3500, "Awaken 3")
                    ),
                    new State("Awaken 3",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "Prepare to meet your DOOM!"),
                        new TimedTransition(2000, "Inferno")
                    ),

                    new State("Doom",
                        new HpLessTransition(0.6, "Rite"),
                        new State("Inferno",
                            new Follow(0.3, 15, 6),
                            new Shoot(25, 3, 10, projectileIndex: 0, predictive: 0.5, coolDown: 900),
                            new Shoot(25, 5, 12, projectileIndex: 1, coolDown: 1600),
                            new Shoot(25, 2, 20, projectileIndex: 3, coolDown: 2000, coolDownOffset: 1000),
                            new TimedTransition(9000, "Blades")
                        ),
                        // blades are short-ranged and paralyze: keep your distance while he charges
                        new State("Blades",
                            new Taunt("Come closer..."),
                            new Charge(1.6, 12, 2000),
                            new Shoot(6, 7, 15, projectileIndex: 2, coolDown: 600),
                            new Shoot(25, 2, 10, projectileIndex: 0, coolDown: 1500),
                            new TimedTransition(7000, "Star Storm")
                        ),
                        new State("Star Storm",
                            new ReturnToSpawn(1),
                            new Shoot(0, 5, fixedAngle: 0, rotateAngle: 9, projectileIndex: 4, coolDown: 320),
                            new Shoot(0, 5, fixedAngle: 36, rotateAngle: -9, projectileIndex: 6, coolDown: 640, coolDownOffset: 320),
                            new TimedTransition(10000, "Summon")
                        ),
                        new State("Summon",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Flash(0xFF00AA00, 0.4, 8),
                            new Taunt("Knights of Doom, rise! Trees of Doom, grow!"),
                            new TossObject("Knight of Doom", 6, 45, 90000000, densityRange: 30, maxDensity: 5),
                            new TossObject("Knight of Doom", 6, 225, 90000000, densityRange: 30, maxDensity: 5),
                            new TossObject("Tree of Doom", 10, 0, 90000000, densityRange: 30, maxDensity: 8),
                            new TossObject("Tree of Doom", 10, 90, 90000000, densityRange: 30, maxDensity: 8),
                            new TossObject("Tree of Doom", 10, 180, 90000000, densityRange: 30, maxDensity: 8),
                            new TossObject("Tree of Doom", 10, 270, 90000000, densityRange: 30, maxDensity: 8),
                            new Shoot(0, 8, fixedAngle: 0, rotateAngle: 11, projectileIndex: 7, coolDown: 700),
                            new TimedTransition(5000, "Inferno")
                        )
                    ),
                    new State("Rite",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new Flash(0xFF000000, 0.3, 20),
                        new Taunt(true, "This day will be MINE!"),
                        new Shoot(0, 10, fixedAngle: 0, rotateAngle: 7, projectileIndex: 7, coolDown: 500),
                        new Shoot(0, 10, fixedAngle: 18, rotateAngle: -7, projectileIndex: 8, coolDown: 500, coolDownOffset: 250),
                        new Spawn("Knight of Doom", 4, 1, 90000000),
                        new TimedTransition(7000, "D Inferno")
                    ),

                    // below 60%: faster, and his Fire Boomerangs join in
                    new State("Deep Doom",
                        new HpLessTransition(0.2, "Last Stand"),
                        new State("D Inferno",
                            new Follow(0.35, 15, 6),
                            new Shoot(25, 3, 10, projectileIndex: 0, predictive: 0.6, coolDown: 750),
                            new Shoot(25, 5, 12, projectileIndex: 1, coolDown: 1400),
                            new Shoot(25, 2, 20, projectileIndex: 3, coolDown: 1700, coolDownOffset: 850),
                            new Shoot(0, 4, fixedAngle: 45, rotateAngle: 30, projectileIndex: 9, coolDown: 3000),
                            new TimedTransition(8000, "D Blades")
                        ),
                        new State("D Blades",
                            new Charge(1.8, 12, 1600),
                            new Shoot(6, 9, 13, projectileIndex: 2, coolDown: 500),
                            new Shoot(25, 2, 10, projectileIndex: 5, coolDown: 1200),
                            new TimedTransition(7000, "D Star Storm")
                        ),
                        new State("D Star Storm",
                            new ReturnToSpawn(1),
                            new Shoot(0, 6, fixedAngle: 0, rotateAngle: 9, projectileIndex: 4, coolDown: 300),
                            new Shoot(0, 6, fixedAngle: 30, rotateAngle: -9, projectileIndex: 6, coolDown: 600, coolDownOffset: 300),
                            new Shoot(25, projectileIndex: 9, coolDown: 2500),
                            new TimedTransition(10000, "D Summon")
                        ),
                        new State("D Summon",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Flash(0xFF00AA00, 0.4, 8),
                            new Taunt("Grow, my Trees! Feed your master!"),
                            new TossObject("Knight of Doom", 6, 0, 90000000, densityRange: 30, maxDensity: 5),
                            new TossObject("Knight of Doom", 6, 180, 90000000, densityRange: 30, maxDensity: 5),
                            new TossObject("Tree of Doom", 10, 45, 90000000, densityRange: 30, maxDensity: 8),
                            new TossObject("Tree of Doom", 10, 135, 90000000, densityRange: 30, maxDensity: 8),
                            new TossObject("Tree of Doom", 10, 225, 90000000, densityRange: 30, maxDensity: 8),
                            new TossObject("Tree of Doom", 10, 315, 90000000, densityRange: 30, maxDensity: 8),
                            new Shoot(0, 8, fixedAngle: 0, rotateAngle: 11, projectileIndex: 8, coolDown: 700),
                            new TimedTransition(5000, "D Inferno")
                        )
                    ),
                    new State("Last Stand",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new ChangeSize(15, 260),
                        new Flash(0xFFFF0000, 0.2, 20),
                        new Taunt(true, "I WILL DESTROY YOU! I AM ORYX, AND I DO NOT DIE!"),
                        new TimedTransition(4000, "Final Doom")
                    ),
                    new State("Final Doom",
                        new Follow(0.55, 25, 3),
                        new Flash(0xFFFF0000, 1, 9000000),
                        new Shoot(25, 3, 10, projectileIndex: 0, predictive: 0.7, coolDown: 800),
                        new Shoot(0, 8, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 9, coolDown: 3500),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 13, projectileIndex: 5, coolDown: 450),
                        new Shoot(6, 7, 15, projectileIndex: 2, coolDown: 900),
                        new Spawn("Knight of Doom", 3, 0, 8000),
                        new Taunt(0.4, 6000, "How can this BE!", "I will not fall to insects!", "My realm... MINE!")
                    )
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Mana", 0.5),
                    new ItemLoot("Greater Potion of Life", 0.2),
                    new ItemLoot("Greater Potion of Mana", 0.2),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Speed", 1),
                    new ItemLoot("Potion of Dexterity", 1),
                    new ItemLoot("Potion of Oryx", 0.25),
                    new ItemLoot("Potion of Power", 0.06)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Vitality", 0.7),
                    new ItemLoot("Potion of Wisdom", 0.7),
                    new ItemLoot("Potion of Attack", 0.3),
                    new ItemLoot("Potion of Defense", 0.3),
                    new ItemLoot("Potion of Speed", 0.3),
                    new ItemLoot("Potion of Dexterity", 0.3),
                    new ItemLoot("Shattered Rune of Oryx", 0.5),
                    new ItemLoot("Broken Oryx Armor", 0.4),
                    new ItemLoot("Purified Doom Saliva", 0.3),
                    new ItemLoot("Large Oryx Cloth", 0.15),
                    new ItemLoot("Small Oryx Cloth", 0.15),
                    new ItemLoot("Ring of Oryx", 0.02),
                    new ItemLoot("Oryx Hide Armor", 0.02),
                    new ItemLoot("Oryx Hide Robe", 0.02),
                    new ItemLoot("Oryx Scale Armor", 0.02),
                    new ItemLoot("Oryx Tooth Sword", 0.02),
                    new ItemLoot("Doom Dirk", 0.02),
                    new ItemLoot("Flying Doom Daggers", 0.02),
                    new ItemLoot("Tome of Noble Assault", 0.02),
                    new ItemLoot("Doom Poison", 0.01),
                    new ItemLoot("Sword of Dark Enchantment", 0.004),
                    new ItemLoot("Crown", 0.004)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(14, ItemType.Weapon, 0.25),
                    new TierLoot(15, ItemType.Armor, 0.2),
                    new TierLoot(6, ItemType.Ability, 0.25),
                    new TierLoot(6, ItemType.Ring, 0.25),
                    new TierLoot(7, ItemType.Ability, 0.05),
                    new TierLoot(7, ItemType.Ring, 0.05),
                    new TierLoot(8, ItemType.Ability, 0.006),
                    new TierLoot(8, ItemType.Ring, 0.006)
                )
            )

            // ---- Oryx 4's helpers ----
            .Init("Guardian Element 3", OryxInnerElement())
            .Init("Guardian Element 4", OryxInnerElement())
            .Init("Outer Guardian Element",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(1.1, 9.5, 20, "Oryx the Mad God 4", speedVariance: 0, radiusVariance: 0, orbitClockwise: true),
                    new Shoot(6, 2, 20, projectileIndex: 0, coolDown: 900),
                    new State("Live", new TimedTransition(14000, "Gone")),
                    new State("Gone", new Suicide())
                )
            )
            .Init("White Moon",
                new State(
                    new BossStats(4000, 20, 1.4, 0.25),
                    new Prioritize(
                        new Orbit(0.7, 8, 20, "Oryx the Mad God 4", speedVariance: 0, radiusVariance: 0),
                        new Wander(0.3)),
                    new Shoot(3, 4, projectileIndex: 0, coolDown: 1000),
                    new Shoot(12, projectileIndex: 1, predictive: 0.4, coolDown: 1500),
                    new State("Live", new TimedTransition(12000, "Gone")),
                    new State("Gone", new Suicide())
                )
            )

            // ---- Oryx 5's helpers ----
            .Init("Knight of Doom",
                new State(
                    new BossStats(6000, 30, 0.6, 0.25),
                    new Prioritize(new Follow(1.1, 15, 1), new Wander(0.4)),
                    new Shoot(2.5, 3, 20, projectileIndex: 0, coolDown: 500)
                ),
                new ItemLoot("Potion of Defense", 0.02),
                new ItemLoot("Potion of Attack", 0.02)
            )
            // each living tree heals Oryx 5 while he fights
            .Init("Tree of Doom",
                new State(
                    new BossStats(3000, 10, 1.0, 0.25),
                    new HealEntity(30, "Oryx the Mad God 5", 900, 2000)
                )
            )
            .Init("Oryx Guardian Sword",
                new State(
                    new State("Idle", new PlayerWithinTransition(8, "Spin")),
                    new State("Spin",
                        new Wander(0.2),
                        new StayCloseToSpawn(0.4, 4),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 12, projectileIndex: 0, coolDown: 600),
                        new NoPlayerWithinTransition(12, "Idle")
                    )
                ),
                new ItemLoot("Potion of Defense", 0.05)
            )
            // a fireball for anyone who stands right beside one, mild enough that walking past is safe
            // (they lined the old Hash Cellar's path)
            .Init("Jungle Fire",
                new State(
                    new BossStats(400, 80, 2.0, 0),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new Shoot(2.5, projectileIndex: 1, coolDown: new Cooldown(3000, 1000))
                )
            );

        private static State OryxInnerElement() =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Prioritize(
                    new Orbit(1, 1, target: "Oryx the Mad God 1", radiusVariance: 0),
                    new Orbit(1, 2, target: "Oryx the Mad God 4", radiusVariance: 0)),
                new Shoot(25, 3, 10, 0, coolDown: 1000),
                new State("Live", new TimedTransition(14000, "Gone")),
                new State("Gone", new Suicide())
            );
    }
}
