using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Oryx's Sanctuary (tools/build_oryx_sanctuary.py): stage 5 of the Oryx chain, Oryx the Celestial
    // Mad God. Players fight
    // through his nobles and court to a gate that opens once the court is dead, then face Oryx the
    // Mad God 3 in the Sanctum. His attacks use the "White" projectiles until half health and the
    // stronger "Exalted" ones after. He drops the portal to the Hash Cellar (Oryx 4).
    // His taunts go to chat only (no bubbles over his attacks). He stands at the Sanctum's centre. His missiles chase the players, his wheels charge about,
    // his bombs flee from them, and before the Kneel ring and the spinners he turns invulnerable
    // and dashes back to the centre.
    partial class BehaviorDb
    {
        private static readonly string[] SanctuaryCourt =
        {
            "Oryx Officer", "Oryx Sergeant", "Oryx Major", "Oryx Deacon", "Oryx Cleric", "Oryx Cardinal",
            "Oryx Noble", "Oryx Aristocrat"
        };

        private _ OryxSanctuary = () => Behav()
            .Init("Oryx the Mad God 3",
                new State(
                    new BossStats(180000, 45, 1.0, 0.3),
                    new DropPortalOnDeath("Locked Wine Cellar Portal", 1.0, timeout: 600, xAdjustment: 2,
                        name: "Locked: Hash Cellar", lockedHint: "This portal is locked. Use a Hash Cellar Incantation beside it to open it."),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120, xAdjustment: -2),
                    new AnnounceOnDeath("{PL_LIST} broke Oryx the Celestial Mad God in his Sanctuary! His spirit sinks into the Hash Cellar, sealed by a Hash Cellar Incantation..."),
                    new State("Dormant",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(17, "Awaken 1")
                    ),
                    new State("Awaken 1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFFFFF, 0.5, 12),
                        new Taunt(true, "So. You have butchered my court and walked into my Sanctum.") { Bubble = false },
                        new TimedTransition(3000, "Awaken 2")
                    ),
                    new State("Awaken 2",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "Twice you have humbled me. There will be no third time.") { Bubble = false },
                        new TimedTransition(3000, "Awaken 3")
                    ),
                    new State("Awaken 3",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "KNEEL, AND BE ERASED!") { Bubble = false },
                        new TimedTransition(1500, "Missiles")
                    ),

                    // ---- first half: the White attacks ----
                    new State("Phase 1",
                        new HpLessTransition(0.5, "Protectors"),
                        new State("Missiles",
                            new Prioritize(new StayCloseToSpawn(0.6, 14), new Follow(0.45, 25, 4)),
                            new Shoot(25, 5, 14, projectileIndex: 0, predictive: 0.3, coolDown: 1400),
                            new Shoot(25, 3, 8, projectileIndex: 1, coolDown: 1000, coolDownOffset: 500),
                            new Shoot(25, projectileIndex: 4, coolDown: 1800),
                            new Shoot(25, projectileIndex: 5, coolDown: 1800),
                            new Shoot(25, projectileIndex: 6, coolDown: 1800),
                            new TimedTransition(8000, "Wheels")
                        ),
                        // he charges about, leaving his wheels where he passed for eight seconds,
                        // and keeps firing his big balls at the players as he goes
                        new State("Wheels",
                            new Taunt("Mind your step.") { Bubble = false },
                            new Charge(1.6, 15, 1600),
                            new Shoot(0, fixedAngle: 0, projectileIndex: 2, coolDown: 250),
                            new Shoot(25, 6, projectileIndex: 9, coolDown: 1500),
                            new TimedTransition(8000, "Kneel")
                        ),
                        // a telegraphed stunning ring: be more than eight tiles away when it lands
                        new State("Kneel",
                            new ReturnToSpawn(2.5),
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Flash(0xFFFFFFFF, 0.4, 6),
                            new Taunt("KNEEL!") { Bubble = false },
                            new TimedTransition(2500, "Kneel Blast")
                        ),
                        new State("Kneel Blast",
                            new Shoot(0, 36, fixedAngle: 0, projectileIndex: 7, coolDown: 10000),
                            new Shoot(0, 12, fixedAngle: 15, projectileIndex: 3, coolDown: 10000),
                            new TimedTransition(500, "Bombs")
                        ),
                        new State("Bombs",
                            new Taunt("My artifacts burn brighter than your sun!") { Bubble = false },
                            new Prioritize(new StayCloseToSpawn(0.6, 13), new StayBack(0.45, 9), new Wander(0.25)),
                            new TossObject("O3 Bomb Artifact", 12, coolDown: 1600),
                            new Shoot(25, 2, 30, projectileIndex: 13, coolDown: 900),
                            new TimedTransition(8000, "Recenter")
                        ),
                        new State("Recenter",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new ReturnToSpawn(2.5),
                            new TimedTransition(1200, "Spinners")
                        ),
                        new State("Spinners",
                            new ReturnToSpawn(1),
                            new Shoot(0, 6, fixedAngle: 0, rotateAngle: 10, projectileIndex: 14, coolDown: 450),
                            new Shoot(25, projectileIndex: 16, predictive: 0.5, coolDown: 1200),
                            new TimedTransition(7000, "Missiles")
                        )
                    ),

                    // ---- at half health: he hides behind his three Protectors ----
                    new State("Protectors",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(2.5),
                        new Flash(0xFF8000FF, 0.5, 8),
                        new Taunt(true, "Protectors! Shield your master!") { Bubble = false },
                        new Spawn("O3 Protector 1", 1, 1, 90000000),
                        new Spawn("O3 Protector 2", 1, 1, 90000000),
                        new Spawn("O3 Protector 3", 1, 1, 90000000),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 13, projectileIndex: 19, coolDown: 2200),
                        new Shoot(25, 3, 12, projectileIndex: 8, coolDown: 1600),
                        new State("Wait for Protectors",
                            new TimedTransition(2000, "Watch Protectors")
                        ),
                        new State("Watch Protectors",
                            new EntitiesNotExistsTransition(40, "Exalt", "O3 Protector 1", "O3 Protector 2", "O3 Protector 3")
                        )
                    ),
                    new State("Exalt",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFD700, 0.3, 10),
                        new Taunt(true, "Enough games. Behold the EXALTED form of your god!") { Bubble = false },
                        new TimedTransition(3000, "X Missiles")
                    ),

                    // ---- second half: the Exalted attacks, faster ----
                    new State("Phase 2",
                        new HpLessTransition(0.2, "Final"),
                        new State("X Missiles",
                            new Prioritize(new StayCloseToSpawn(0.7, 14), new Follow(0.55, 25, 3)),
                            new Shoot(25, 5, 14, projectileIndex: 30, predictive: 0.4, coolDown: 1200),
                            new Shoot(25, 3, 8, projectileIndex: 31, coolDown: 900, coolDownOffset: 450),
                            new Shoot(25, projectileIndex: 34, coolDown: 1500),
                            new Shoot(25, projectileIndex: 35, coolDown: 1500),
                            new Shoot(25, projectileIndex: 36, coolDown: 1500),
                            new TimedTransition(7000, "X Wheels")
                        ),
                        new State("X Wheels",
                            new Charge(1.9, 15, 1300),
                            new Shoot(0, fixedAngle: 0, projectileIndex: 32, coolDown: 200),
                            new Shoot(25, 8, projectileIndex: 39, coolDown: 1400),
                            new TimedTransition(7000, "X Kneel")
                        ),
                        new State("X Kneel",
                            new ReturnToSpawn(2.5),
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Flash(0xFFFFD700, 0.3, 7),
                            new Taunt("KNEEL BEFORE YOUR GOD!") { Bubble = false },
                            new TimedTransition(2000, "X Kneel Blast")
                        ),
                        new State("X Kneel Blast",
                            new Shoot(0, 36, fixedAngle: 0, projectileIndex: 37, coolDown: 10000),
                            new Shoot(0, 12, fixedAngle: 15, projectileIndex: 33, coolDown: 10000),
                            new TimedTransition(500, "X Bombs")
                        ),
                        new State("X Bombs",
                            new Prioritize(new StayCloseToSpawn(0.7, 13), new StayBack(0.55, 9), new Wander(0.3)),
                            new TossObject("O3 Bomb Artifact", 12, coolDown: 1100),
                            new TossObject("O3 Bomb Artifact", 12, coolDown: 1100, coolDownOffset: 550, probability: 0.6),
                            new Shoot(25, 2, 30, projectileIndex: 43, coolDown: 800),
                            new TimedTransition(7000, "X Recenter")
                        ),
                        new State("X Recenter",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new ReturnToSpawn(2.5),
                            new TimedTransition(1000, "X Spinners")
                        ),
                        new State("X Spinners",
                            new ReturnToSpawn(1),
                            new Shoot(0, 6, fixedAngle: 0, rotateAngle: 11, projectileIndex: 44, coolDown: 400),
                            new Shoot(0, 6, fixedAngle: 30, rotateAngle: -11, projectileIndex: 44, coolDown: 800, coolDownOffset: 400),
                            new Shoot(25, projectileIndex: 46, predictive: 0.6, coolDown: 1000),
                            new TimedTransition(7000, "X Missiles")
                        )
                    ),

                    // ---- below 20%: he hunts the players down ----
                    new State("Final",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(2.5),
                        new Flash(0xFFFF0000, 0.3, 10),
                        new Taunt(true, "I AM ETERNAL! I AM THE REALM ITSELF!") { Bubble = false },
                        new TimedTransition(3000, "Rage")
                    ),
                    new State("Rage",
                        new Follow(0.5, 20, 4),
                        new Flash(0xFFFF0000, 1, 9000000),
                        new Shoot(25, 10, projectileIndex: 40, coolDown: 2200),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 14, projectileIndex: 44, coolDown: 500),
                        new Shoot(25, projectileIndex: 45, predictive: 0.5, coolDown: 1700),
                        new Shoot(25, 3, 15, projectileIndex: 41, coolDown: 1300, coolDownOffset: 650),
                        new TossObject("O3 Bomb Artifact", 12, coolDown: 2000),
                        new Taunt(0.5, 7000, "Your gods have abandoned you!", "There is no escape from my Sanctum!", "I will unmake you!") { Bubble = false }
                    )
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 0.4),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Oryx", 0.07)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Speed", 0.55),
                    new ItemLoot("Potion of Dexterity", 0.55),
                    new ItemLoot("Potion of Vitality", 0.55),
                    new ItemLoot("Potion of Wisdom", 0.55),
                    new ItemLoot("Shattered Rune of Oryx", 0.25),
                    new ItemLoot("Oryx Scroll", 0.02),
                    new ItemLoot("Doomed Cellar Incantation", 0.05),
                    new ItemLoot("Oryx's Escutcheon", 0.025),
                    new ItemLoot("Ring of Oryx", 0.004),
                    new ItemLoot("Oryx Hide Armor", 0.004),
                    new ItemLoot("Oryx Hide Robe", 0.004),
                    new ItemLoot("Oryx Scale Armor", 0.004),
                    new ItemLoot("Oryx Tooth Sword", 0.004),
                    new ItemLoot("Doom Dirk", 0.004),
                    new ItemLoot("Flying Doom Daggers", 0.004),
                    new ItemLoot("Tome of Noble Assault", 0.004)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(13, ItemType.Weapon, 0.12),
                    new TierLoot(14, ItemType.Weapon, 0.03),
                    new TierLoot(13, ItemType.Armor, 0.06),
                    new TierLoot(14, ItemType.Armor, 0.1),
                    new TierLoot(15, ItemType.Armor, 0.02),
                    new TierLoot(6, ItemType.Ability, 0.14),
                    new TierLoot(6, ItemType.Ring, 0.12),
                    new TierLoot(7, ItemType.Ability, 0.008),
                    new TierLoot(7, ItemType.Ring, 0.008)
                )
            )

            // a visible bomb that bursts into a ring of spikes 1.5 seconds after it lands
            .Init("O3 Bomb Artifact",
                new State(
                    new State("Fuse",
                        new SetAltTexture(0, 1, 250, true),
                        new TimedTransition(1500, "Burst")
                    ),
                    new State("Burst",
                        new Spawn("O3 Meteor", 1, 1, 90000000),
                        new Decay(100)
                    )
                )
            )
            .Init("O3 Meteor",
                new State(
                    new Shoot(0, 8, fixedAngle: 0, projectileIndex: 0, coolDown: 10000),
                    new Decay(300)
                )
            )

            // Protectors circle Oryx and must all die before he can be hurt again
            .Init("O3 Protector 1", Protector(1, 0))
            .Init("O3 Protector 2", Protector(2, 1))
            .Init("O3 Protector 3", Protector(3, 2))

            // ---- the gate: opens once the court is dead ----
            .Init("O3 Gate Deleter 1",
                new State(
                    new State("Wait",
                        new TimedTransition(3000, "Watch")
                    ),
                    new State("Watch",
                        new EntitiesNotExistsTransition(70, "Open", SanctuaryCourt)
                    ),
                    new State("Open",
                        new RemoveObject("O3 Wall Gate", 8),
                        new TimedTransition(1000, "Done")
                    ),
                    new State("Done")
                )
            )

            // ---- the nobles in the Gallery ----
            .Init("Oryx Noble",
                new State(
                    new BossStats(7000, 15, 0.9, 0.25),
                    new State("Idle", new PlayerWithinTransition(11, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.6, 11, 4), new Wander(0.3)),
                        new Shoot(11, 3, 20, projectileIndex: 0, coolDown: 1200)
                    )
                ),
                CourtLoot()
            )
            .Init("Oryx Aristocrat",
                new State(
                    new BossStats(8000, 20, 1.2, 0.25),
                    new State("Idle", new PlayerWithinTransition(11, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.7, 11, 2), new Wander(0.3)),
                        new Shoot(8, 2, 30, projectileIndex: 0, coolDown: 900),
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 20, projectileIndex: 1, coolDown: 2500)
                    )
                ),
                CourtLoot()
            )

            // ---- the officers (west side of the Court) ----
            .Init("Oryx Officer",
                new State(
                    new BossStats(13000, 30, 0.9, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Volley")),
                    new State("Volley",
                        new Prioritize(new StayCloseToSpawn(0.5, 7), new Follow(0.5, 12, 4)),
                        new Shoot(12, 3, 12, projectileIndex: 2, coolDown: 800),
                        new Shoot(12, projectileIndex: 4, predictive: 0.5, coolDown: 1200),
                        new TimedTransition(5000, "Spike")
                    ),
                    new State("Spike",
                        new Flash(0xFFFF0000, 0.3, 3),
                        new Shoot(12, projectileIndex: 5, coolDown: 1500, coolDownOffset: 900),
                        new Shoot(0, 8, fixedAngle: 0, projectileIndex: 3, coolDown: 2000, coolDownOffset: 900),
                        new TimedTransition(3000, "Volley")
                    )
                ),
                CourtLoot()
            )
            // fires four stakes down one line at four speeds, and boomerang shockwaves
            .Init("Oryx Sergeant",
                new State(
                    new BossStats(12000, 35, 0.9, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 7), new Follow(0.45, 12, 5)),
                        new Shoot(12, projectileIndex: 0, coolDown: 1500),
                        new Shoot(12, projectileIndex: 1, coolDown: 1500),
                        new Shoot(12, projectileIndex: 2, coolDown: 1500),
                        new Shoot(12, projectileIndex: 3, coolDown: 1500),
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 30, projectileIndex: 5, coolDown: 3500, coolDownOffset: 1000)
                    )
                ),
                CourtLoot()
            )
            .Init("Oryx Major",
                new State(
                    new BossStats(13000, 35, 0.9, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Charge")),
                    new State("Charge",
                        new Prioritize(new StayCloseToSpawn(1.2, 8), new Charge(1.4, 10, 2500)),
                        new Shoot(6, 5, 12, projectileIndex: 0, coolDown: 1000),
                        new Shoot(12, 2, 16, projectileIndex: 2, coolDown: 1400),
                        new TimedTransition(5000, "Stand")
                    ),
                    new State("Stand",
                        new Flash(0xFF888888, 0.3, 3),
                        new Shoot(0, 8, fixedAngle: 22, projectileIndex: 5, coolDown: 3000, coolDownOffset: 800),
                        new Shoot(12, projectileIndex: 4, coolDown: 1000),
                        new TimedTransition(3000, "Charge")
                    )
                ),
                CourtLoot()
            )

            // ---- the clergy (east side) ----
            .Init("Oryx Deacon",
                new State(
                    new BossStats(10000, 15, 1.1, 0.25),
                    new HealEntity(12, "Oryx Cleric", 600, 3000),
                    new HealEntity(12, "Oryx Cardinal", 600, 3000),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new StayBack(0.5, 7), new Wander(0.3)),
                        new Shoot(12, 3, 14, projectileIndex: 0, coolDown: 1000),
                        new Shoot(12, projectileIndex: 1, predictive: 0.6, coolDown: 1600)
                    )
                ),
                CourtLoot()
            )
            // its shockwaves leave at five speeds along one line, a short-ranged cone
            .Init("Oryx Cleric",
                new State(
                    new BossStats(11000, 10, 1.1, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.6, 12, 3)),
                        new Shoot(10, 3, 15, projectileIndex: 1, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 2, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 3, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 4, coolDown: 1600),
                        new Shoot(10, 3, 15, projectileIndex: 5, coolDown: 1600),
                        new Shoot(3, 6, projectileIndex: 0, coolDown: 900)
                    )
                ),
                CourtLoot()
            )
            .Init("Oryx Cardinal",
                new State(
                    new BossStats(11000, 20, 1.1, 0.25),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new Orbit(0.5, 4, 12, "Oryx Cleric"), new Wander(0.3)),
                        new Shoot(12, 5, 15, projectileIndex: 0, coolDown: 1400)
                    )
                ),
                CourtLoot()
            );

        private static State Protector(int n, int slot) =>
            new State(
                new BossStats(15000, 30, 1.5, 0.25),
                // no wandering off: they circle him wherever he is in the Sanctum
                new Orbit(1.0, 5 + slot, 45, "Oryx the Mad God 3", speedVariance: 0, radiusVariance: 0, orbitClockwise: n % 2 == 0),
                new Shoot(12, 3, 15, projectileIndex: 0, coolDown: 1000, coolDownOffset: slot * 300),
                new Shoot(12, projectileIndex: 1, predictive: 0.5, coolDown: 2400, coolDownOffset: slot * 500)
            );

        private static MobDrops[] CourtLoot() => new MobDrops[]
        {
            new ItemLoot("Potion of Defense", 0.05),
            new ItemLoot("Potion of Attack", 0.05),
            new ItemLoot("Potion of Vitality", 0.02),
            new ItemLoot("Potion of Wisdom", 0.02),
            new ItemLoot("Greater Potion of Life", 0.008),
            new TierLoot(12, ItemType.Weapon, 0.04),
            new TierLoot(13, ItemType.Armor, 0.03),
            new TierLoot(5, ItemType.Ability, 0.03)
        };
    }
}
