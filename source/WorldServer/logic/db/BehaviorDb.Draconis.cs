using Shared.resources;
using WorldServer.logic.loot;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Draconis = () => DraconisBehaviours(Behav(), false);
        private _ ElderDraconis = () => DraconisBehaviours(Behav(), true);

        // The Lair of Draconis and its Elder version (BehaviorDb.Elder.cs). Four dragons sleep in
        // rooms round the altar, each behind a corridor of rubble. A dragon's soul stands before its
        // corridor: walking onto it takes you into the dragon's room. A slain dragon leaves a loot
        // balloon; popping it clears the corridor back to the altar. Once all four are dead the altar
        // opens the Ivory Wyvern (and, from the regular lair, the Elder lair half the time).
        private static Ctor DraconisBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            // where each soul sends you, in its dragon's room
            State Soul(string colour, float x, float y) =>
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("open",
                        new TeleportPlayer(1, x, y, true),
                        new EntityExistsTransition(N($"NM {colour} Dragon Dead"), 200, "gone")
                        ),
                    new State("gone",
                        new Decay(1000)
                        )
                    );

            // the corridor of rubble (one kind of wall per corridor) between a room and the altar
            State OpenWall(string rubble) =>
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("closed"),
                    new State("open",
                        new RemoveTileObject(rubble, 22)
                        )
                    );

            // the balloon a dragon leaves: popping it drops the loot and opens the way back
            State Balloon(string colour) =>
                new State(
                    new ScaleHP2(15),
                    new TransformOnDeath(N($"NM {colour} Dragon Dead")),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 3000),
                    new Flash(0xFFFFFF, 0.5, 1),
                    new OrderOnDeath(200, N($"NM {colour} Open Wall"), "open")
                    );

            State Marker() =>
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("1")
                    );

            // what every balloon drops besides its dragon's own
            MobDrops[] Common() =>
            [
                new Threshold(0.01, LootTemplates.StrongerDrop()),
                new Threshold(0.01, new ItemLoot("Draconis Key", 0.01)),
                ElderLoot(0.01,
                    new TierLoot(12, ItemType.Weapon, 0.08),
                    new TierLoot(13, ItemType.Weapon, 0.04),
                    new TierLoot(13, ItemType.Armor, 0.08),
                    new TierLoot(14, ItemType.Armor, 0.04),
                    new TierLoot(6, ItemType.Ability, 0.06),
                    new TierLoot(6, ItemType.Ring, 0.06),
                    new ItemLoot("Elder Draconis Key", 0.01)
                    )
            ];

            return db
            .Init(E("NM Altar of Draconis"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    // the regular lair leads to the Elder one half the time
                    new DropPortalOnDeath("Elder Draconis Portal", elder ? 0 : 0.5, xAdjustment: -3, timeout: 120),
                    new State("Idle",
                        new PlayerWithinTransition(10, "Greet")
                        ),
                    new State("Greet",
                        new Taunt(true, "Choose the Dragon Soul you wish to commune with! Step onto a soul to enter its dragon's lair."),
                        new TimedTransition(1000, "Waiting")
                        ),
                    new State("Waiting",
                        new EntitiesNotExistsTransition(200, "Done",
                            N("NM Blue Dragon God"), N("NM Red Dragon God"), N("NM Green Dragon God"), N("NM Black Dragon God"),
                            N("lod Blue Loot Balloon"), N("lod Red Loot Balloon"), N("lod Green Loot Balloon"), N("lod Black Loot Balloon"))
                        ),
                    new State("Done",
                        new Taunt(true, "The four dragon souls are freed. The Ivory Wyvern awaits you!"),
                        new TossObject("Ivory Wyvern Portal", 3, 0, coolDown: 999999),
                        new TimedTransition(2000, "Gone")
                        ),
                    new State("Gone",
                        new Decay(0)
                        )
                    )
                )
            .Init(E("NM Blue Dragon Soul"), Soul("Blue", 59.5f, 25.5f))
            .Init(E("NM Red Dragon Soul"), Soul("Red", 28.5f, 56.5f))
            .Init(E("NM Green Dragon Soul"), Soul("Green", 90.5f, 56.5f))
            .Init(E("NM Black Dragon Soul"), Soul("Black", 59.5f, 87.5f))
            .Init(E("NM Blue Open Wall"), OpenWall("Ice Cave Wall"))
            .Init(E("NM Red Open Wall"), OpenWall("Iron Brick"))
            .Init(E("NM Green Open Wall"), OpenWall("Jungle Temple Walls"))
            .Init(E("NM Black Open Wall"), OpenWall("Blue Wall"))
            .Init(E("NM Blue Dragon Dead"), Marker())
            .Init(E("NM Red Dragon Dead"), Marker())
            .Init(E("NM Green Dragon Dead"), Marker())
            .Init(E("NM Black Dragon Dead"), Marker())

            // Nikao the Azure Dragon: wakes its eggs' shards, then three phases of bullet rings
            .Init(E("lod Blue Loot Balloon"), Balloon("Blue"),
                [.. Common(),
                new Threshold(0.01,
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Potion of Wisdom", 0.5),
                    new ItemLoot("Large Blue Dragon Scale Cloth", 0.4),
                    new ItemLoot("Small Blue Dragon Scale Cloth", 0.4)
                    ),
                new Threshold(0.02,
                    new ItemLoot("Blue Dragon Gods Helm", 0.01)
                    ),
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Wisdom", 0.06),
                    new ItemLoot("Robe of Silk Water", 0.02)
                    )]
                )
            .Init(E("NM Blue Dragon God"),
                new State(
                    new ScaleHP2(15),
                    new StayCloseToSpawn(0.5, 12),
                    new TransformOnDeath(N("lod Blue Loot Balloon")),
                    new State("1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new SetAltTexture(1),
                        new PlayerWithinTransition(6, "2")
                        ),
                    new State("2",
                        new Taunt(true, "Do not let the tranquil surroundings fool you!"),
                        new Flash(0xFFFFFF, 0.2, 12),
                        new TimedTransition(3000, "3")
                        ),
                    new State("3",
                        new SetAltTexture(0),
                        new OrderOnce(99, N("NM Blue Minion Spawner"), "spawn"),
                        new TimedTransition(1500, "4")
                        ),
                    new State("4",
                        new RemoveConditionalEffect(ConditionEffectIndex.Invincible),
                        new HpLessTransition(0.75, "9"),
                        new Shoot(0, 4, 45, 1, 90, coolDown: Cd(2000)),
                        new Shoot(0, 4, 15, 2, 90, coolDown: Cd(2000)),
                        new Shoot(0, 6, 30, 6, 315, coolDown: Cd(2500)),
                        new Shoot(0, 6, 25, 3, 0, coolDown: Cd(4500), coolDownOffset: 1500),
                        new Shoot(0, 6, 15, 5, 315, coolDown: Cd(4500), coolDownOffset: 1500),
                        new Shoot(0, 6, 25, 3, 90, coolDown: Cd(4500), coolDownOffset: 3000),
                        new Shoot(0, 6, 15, 5, 270, coolDown: Cd(4500), coolDownOffset: 3000),
                        new Shoot(0, 6, 15, 5, 225, coolDown: Cd(4500), coolDownOffset: 4500),
                        new Shoot(0, 6, 15, 5, 135, coolDown: Cd(4500), coolDownOffset: 4500),
                        new State("5",
                            new TimedRandomTransition(500, false, "6", "7")
                            ),
                        new State("6",
                            new Wander(0.4),
                            new TimedTransition(4000, "5")
                            ),
                        new State("7",
                            new Follow(0.4, 20, 1),
                            new TimedTransition(4000, "5")
                            )
                        ),
                    new State("9",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 2000),
                        new HpLessTransition(0.25, "15"),
                        new Flash(0xFFFFFF, 0.2, 12),
                        new Shoot(0, 4, 45, 1, 90, coolDown: Cd(2000)),
                        new Shoot(0, 4, 15, 2, 90, coolDown: Cd(2000)),
                        new Shoot(0, 6, 30, 6, 315, coolDown: Cd(2500)),
                        new Shoot(24, 1, projectileIndex: 0, coolDown: Cd(1000)),
                        new Shoot(0, 6, 25, 3, 0, coolDown: Cd(4500), coolDownOffset: 1500),
                        new Shoot(0, 6, 15, 5, 315, coolDown: Cd(4500), coolDownOffset: 1500),
                        new Shoot(0, 6, 25, 3, 90, coolDown: Cd(4500), coolDownOffset: 3000),
                        new Shoot(0, 6, 15, 5, 270, coolDown: Cd(4500), coolDownOffset: 3000),
                        new Shoot(0, 6, 15, 5, 225, coolDown: Cd(4500), coolDownOffset: 4500),
                        new Shoot(0, 6, 15, 5, 135, coolDown: Cd(4500), coolDownOffset: 4500),
                        new State("10",
                            new ReturnToSpawn(0.7, 0.5),
                            new HpLessTransition(0.5, "11")
                            ),
                        new State("11",
                            new TimedRandomTransition(500, false, "12", "13")
                            ),
                        new State("12",
                            new Wander(0.5),
                            new TimedTransition(4000, "11")
                            ),
                        new State("13",
                            new Follow(0.5, 20, 1),
                            new TimedTransition(4000, "11")
                            )
                        ),
                    new State("15",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 2000),
                        new HpLessTransition(0.05, "16"),
                        new Wander(0.6),
                        new Shoot(0, 4, 45, 1, 90, coolDown: Cd(2000)),
                        new Shoot(0, 4, 15, 2, 90, coolDown: Cd(2000)),
                        new Shoot(0, 6, 30, 6, 315, coolDown: Cd(2500)),
                        new Shoot(24, 1, projectileIndex: 0, coolDown: Cd(1000)),
                        new Shoot(0, 6, 15, 5, 315, coolDown: Cd(4500), coolDownOffset: 1500),
                        new Shoot(0, 6, 15, 5, 270, coolDown: Cd(4500), coolDownOffset: 3000),
                        new Shoot(0, 6, 15, 5, 225, coolDown: Cd(4500), coolDownOffset: 4500),
                        new Shoot(0, 6, 15, 5, 135, coolDown: Cd(4500), coolDownOffset: 4500),
                        new Shoot(0, 24, 15, 3, 0, coolDown: Cd(2000))
                        ),
                    new State("16",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new ReturnToSpawn(0.6),
                        new Decay(3000)
                        )
                    )
                )
            .Init(E("NM Blue Minion Spawner"),
                new State(
                    new State("Waiting"),
                    new State("spawn",
                        new Spawn(N("NM Blue Dragon Minion"), 1, 0),
                        new TimedTransition(1000, "Waiting")
                        )
                    )
                )
            .Init(E("NM Blue Dragon Minion"),
                new State(
                    new Wander(0.2),
                    new Shoot(10, 1, projectileIndex: 2, coolDown: Cd(1000))
                    )
                )

            // Pyyr the Crimson Dragon: hides behind its hatchlings, then chases you over lava
            .Init(E("lod Red Loot Balloon"), Balloon("Red"),
                [.. Common(),
                new Threshold(0.01,
                    new ItemLoot("Potion of Vitality", 1),
                    new ItemLoot("Potion of Attack", 0.5),
                    new ItemLoot("Large Red Dragon Scale Cloth", 0.4),
                    new ItemLoot("Small Red Dragon Scale Cloth", 0.4)
                    ),
                new Threshold(0.02,
                    new ItemLoot("Red Dragon Gods Helm", 0.01)
                    ),
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Vitality", 0.06),
                    new ItemLoot("Momento of the Fire Dragon", 0.02)
                    )]
                )
            .Init(E("NM Red Dragon God"),
                new State(
                    new ScaleHP2(15),
                    new StayCloseToSpawn(0.5, 12),
                    new TransformOnDeath(N("lod Red Loot Balloon")),
                    new State("1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new SetAltTexture(1),
                        new PlayerWithinTransition(6, "2")
                        ),
                    new State("2",
                        new Taunt(true, "Burns!!! Pyyr will rend your flesh and char your bones!"),
                        new Flash(0xFFFFFF, 0.2, 12),
                        new TimedTransition(2500, "3")
                        ),
                    new State("3",
                        new SetAltTexture(0),
                        new TimedTransition(1000, "4")
                        ),
                    new State("4",
                        new OrderOnce(99, N("NM Red Minion Spawner"), "1"),
                        new OrderOnce(99, N("NM Red Fake Egg"), "1"),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 0),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 90),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 180),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 270),
                        new TimedTransition(200, "5")
                        ),
                    new State("5",
                        new EntityExistsTransition(N("NM Red Dragon Minion"), 100, "6"),
                        new TimedTransition(5000, "7")
                        ),
                    new State("6",
                        new EntitiesNotExistsTransition(100, "7", N("NM Red Dragon Minion")),
                        new Shoot(20, 6, 15, 4, coolDown: Cd(1500)),
                        new Shoot(0, 10, 36, 5, 10, coolDown: Cd(2000)),
                        new Shoot(0, 4, 90, 0, 0, coolDown: Cd(2000), coolDownOffset: 1000),
                        new Shoot(0, 4, 90, 0, 45, coolDown: Cd(2000), coolDownOffset: 2000)
                        ),
                    new State("7",
                        new RemoveConditionalEffect(ConditionEffectIndex.Invincible),
                        new HpLessTransition(0.5, "8"),
                        new Follow(0.5, 20, 1, 6000, 6000),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 6, coolDown: Cd(3000)),
                        new Shoot(20, 6, 15, 4, coolDown: Cd(1500)),
                        new Shoot(0, 10, 36, 5, 10, coolDown: Cd(2000)),
                        new Shoot(0, 6, 10, 2, 0, coolDown: Cd(2500)),
                        new Shoot(0, 6, 10, 2, 90, coolDown: Cd(2500)),
                        new Shoot(0, 6, 10, 2, 180, coolDown: Cd(2500)),
                        new Shoot(0, 6, 10, 2, 270, coolDown: Cd(2500)),
                        new Shoot(20, 3, 25, 1, coolDown: Cd(2000)),
                        new Shoot(20, 1, projectileIndex: 6, coolDown: Cd(1000)),
                        new Shoot(99, 4, 90, 3, 0, coolDownOffset: 200, coolDown: Cd(2000)),
                        new Shoot(99, 4, 90, 3, 20, coolDownOffset: 600, coolDown: Cd(2000)),
                        new Shoot(99, 4, 90, 3, 40, coolDownOffset: 1000, coolDown: Cd(2000)),
                        new Shoot(99, 4, 90, 3, 20, coolDownOffset: 1600, coolDown: Cd(2000))
                        ),
                    new State("8",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new ReturnToSpawn(1, 0),
                        new TimedTransition(4000, "9")
                        ),
                    new State("9",
                        new OrderOnce(99, N("NM Red Minion Spawner"), "1"),
                        new OrderOnce(99, N("NM Red Fake Egg"), "1"),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 45),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 135),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 225),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 3, 315),
                        new TimedTransition(200, "10")
                        ),
                    new State("10",
                        new EntityExistsTransition(N("NM Red Dragon Minion"), 100, "13"),
                        new TimedTransition(5000, "11")
                        ),
                    new State("13",
                        new EntitiesNotExistsTransition(100, "11", N("NM Red Dragon Minion")),
                        new Shoot(20, 6, 15, 4, coolDown: Cd(1500)),
                        new Shoot(0, 10, 36, 5, 10, coolDown: Cd(2000)),
                        new Shoot(0, 4, 90, 0, 0, coolDown: Cd(2000), coolDownOffset: 1000),
                        new Shoot(0, 4, 90, 0, 45, coolDown: Cd(2000), coolDownOffset: 2000)
                        ),
                    new State("11",
                        new RemoveConditionalEffect(ConditionEffectIndex.Invincible),
                        new HpLessTransition(0.05, "12"),
                        new Follow(0.5, 20, 1, 6000, 6000),
                        new TossObject(N("NM Red Dragon Lava Bomb"), 6, coolDown: Cd(2000)),
                        new Shoot(20, 6, 15, 4, coolDown: Cd(1500)),
                        new Shoot(0, 10, 36, 5, 10, coolDown: Cd(2000)),
                        new Shoot(0, 6, 10, 2, 0, coolDown: Cd(2500)),
                        new Shoot(0, 6, 10, 2, 90, coolDown: Cd(2500)),
                        new Shoot(0, 6, 10, 2, 180, coolDown: Cd(2500)),
                        new Shoot(0, 6, 10, 2, 270, coolDown: Cd(2500)),
                        new Shoot(20, 3, 25, 1, coolDown: Cd(2000)),
                        new Shoot(20, 1, projectileIndex: 6, coolDown: Cd(1000)),
                        new Shoot(99, 4, 90, 3, 0, coolDownOffset: 200, coolDown: Cd(2000)),
                        new Shoot(99, 4, 90, 3, 20, coolDownOffset: 600, coolDown: Cd(2000)),
                        new Shoot(99, 4, 90, 3, 40, coolDownOffset: 1000, coolDown: Cd(2000)),
                        new Shoot(99, 4, 90, 3, 20, coolDownOffset: 1600, coolDown: Cd(2000))
                        ),
                    new State("12",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new ReturnToSpawn(0.7),
                        new Decay(3000)
                        )
                    )
                )
            // a lava bomb lands, bursts, and leaves a pool of lava for a few seconds
            .Init(E("NM Red Dragon Lava Bomb"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("1",
                        new TimedTransition(1000, "2")
                        ),
                    new State("2",
                        new EnemyAOE(1.5, false, 60, 90, false, 0xFF0000),
                        new GroundTransform("Lava", 1),
                        new TimedTransition(6000, "3")
                        ),
                    new State("3",
                        new Decay(0)
                        )
                    )
                )
            .Init(E("NM Red Fake Egg"),
                new State(
                    new State("nothin"),
                    new State("1",
                        new Flash(0xFFFFFF, 0.2, 12),
                        new TimedTransition(2500, "nothin")
                        )
                    )
                )
            .Init(E("NM Red Minion Spawner"),
                new State(
                    new State("nothin"),
                    new State("1",
                        new Flash(0xFFFFFF, 0.2, 12),
                        new TimedTransition(2500, "2")
                        ),
                    new State("2",
                        new Spawn(N("NM Red Dragon Minion"), 1, 0),
                        new TimedTransition(100, "nothin")
                        )
                    )
                )
            .Init(E("NM Red Dragon Minion"),
                new State(
                    new Prioritize(
                        new Follow(0.4, 10, 3),
                        new Wander(0.3)
                        ),
                    new Shoot(8, 1, projectileIndex: 0, coolDown: Cd(1500))
                    )
                )

            // Feargus the Obsidian Dragon: one long fight, its shards waking as you pass
            .Init(E("lod Black Loot Balloon"), Balloon("Black"),
                [.. Common(),
                new Threshold(0.01,
                    new ItemLoot("Potion of Dexterity", 1),
                    new ItemLoot("Potion of Dexterity", 0.5),
                    new ItemLoot("Large Midnight Dragon Scale Cloth", 0.4),
                    new ItemLoot("Small Midnight Dragon Scale Cloth", 0.4)
                    ),
                new Threshold(0.02,
                    new ItemLoot("Black Dragon Gods Blade", 0.01)
                    ),
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Dexterity", 0.06),
                    new ItemLoot("Lightning Bow", 0.02),
                    // what Doomed Realms' Black Dragon God dropped in the old Elder lair
                    new ItemLoot("Celestial Blade", 0.01),
                    new ItemLoot("Ray Katana", 0.04),
                    new ItemLoot("Doku No Ken", 0.04),
                    new ItemLoot("Midnight Star", 0.04)
                    )]
                )
            .Init(E("NM Black Dragon God"),
                new State(
                    new ScaleHP2(15),
                    new StayCloseToSpawn(0.5, 14),
                    new TransformOnDeath(N("lod Black Loot Balloon")),
                    new State("1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new SetAltTexture(1),
                        new PlayerWithinTransition(6, "2")
                        ),
                    new State("2",
                        new Taunt(true, "Gaze into the darkness... Feargus will consume you!"),
                        new Flash(0xFFFFFF, 0.2, 12),
                        new TimedTransition(2500, "3")
                        ),
                    new State("3",
                        new SetAltTexture(0),
                        new TimedTransition(1000, "4")
                        ),
                    new State("4",
                        new RemoveConditionalEffect(ConditionEffectIndex.Invincible),
                        new HpLessTransition(0.05, "9"),
                        new Shoot(12, 5, 36, 0, coolDown: Cd(1200)),
                        new Shoot(12, 1, projectileIndex: 1, coolDown: Cd(500)),
                        new Shoot(12, 5, 36, 2, predictive: 1, coolDown: Cd(1200)),
                        new Shoot(22, 2, 45, 3, coolDown: Cd(1000)),
                        new Shoot(12, 5, 36, 5, coolDown: Cd(1500)),
                        new Shoot(0, 5, 25, 7, 270, coolDown: Cd(1000)),
                        new Grenade(4, 100, 14, coolDown: Cd(7000)),
                        new State("5",
                            new TimedRandomTransition(1000, false, "6", "7")
                            ),
                        new State("6",
                            new Follow(0.4, 10, 3),
                            new TimedTransition(4000, "5")
                            ),
                        new State("7",
                            new Wander(0.4),
                            new TimedTransition(4000, "5")
                            )
                        ),
                    new State("9",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new ReturnToSpawn(0.7),
                        new Decay(3000)
                        )
                    )
                )
            .Init(E("NM Black Dragon Minion"),
                new State(
                    new State("2",
                        new SetAltTexture(0),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new PlayerWithinTransition(4, "3")
                        ),
                    new State("3",
                        new SetAltTexture(1),
                        new Wander(0.3),
                        new Shoot(10, 1, projectileIndex: 0, coolDown: Cd(800)),
                        new NoPlayerWithinTransition(12, "2")
                        )
                    )
                )

            // Limoz the Veridian Dragon: shielded until its shields fall, then calls its hatchlings
            .Init(E("lod Green Loot Balloon"), Balloon("Green"),
                [.. Common(),
                new Threshold(0.01,
                    new ItemLoot("Potion of Speed", 1),
                    new ItemLoot("Potion of Defense", 0.5),
                    new ItemLoot("Large Green Dragon Scale Cloth", 0.4),
                    new ItemLoot("Small Green Dragon Scale Cloth", 0.4)
                    ),
                new Threshold(0.02,
                    new ItemLoot("Green Dragon Gods Helm", 0.01)
                    ),
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Speed", 0.06),
                    new ItemLoot("Armor of the Leaf", 0.02)
                    )]
                )
            .Init(E("NM Green Dragon God"),
                new State(
                    new ScaleHP2(15),
                    new StayCloseToSpawn(0.5, 12),
                    new TransformOnDeath(N("lod Green Loot Balloon")),
                    new State("1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new SetAltTexture(1),
                        new PlayerWithinTransition(6, "2")
                        ),
                    new State("2",
                        new Taunt(true, "Limoz is the nicest of the lot, but he still hates all sub creatures!"),
                        new Flash(0xFFFFFF, 0.2, 12),
                        new TimedTransition(2500, "3")
                        ),
                    new State("3",
                        new SetAltTexture(0),
                        new TimedTransition(1000, "4")
                        ),
                    new State("4",
                        new TossObject(N("NM Green Dragon Shield"), 3, 0),
                        new TossObject(N("NM Green Dragon Shield"), 3, 60),
                        new TossObject(N("NM Green Dragon Shield"), 3, 120),
                        new TossObject(N("NM Green Dragon Shield"), 3, 180),
                        new TossObject(N("NM Green Dragon Shield"), 3, 240),
                        new TossObject(N("NM Green Dragon Shield"), 3, 300),
                        new TimedTransition(0, "5")
                        ),
                    new State("5",
                        new EntityExistsTransition(N("NM Green Dragon Shield"), 20, "6"),
                        new TimedTransition(3000, "6")
                        ),
                    new State("6",
                        new HpLessTransition(0.5, "9"),
                        new Shoot(0, 12, 30, 0, 45, coolDown: Cd(1500)),
                        new Shoot(0, 2, 15, 6, 0, coolDown: Cd(1000)),
                        new Shoot(0, 2, 15, 6, 90, coolDown: Cd(1000)),
                        new Shoot(0, 2, 15, 6, 180, coolDown: Cd(1000)),
                        new Shoot(0, 2, 15, 6, 270, coolDown: Cd(1000)),
                        new Shoot(0, 10, 36, 5, 10, coolDown: Cd(2000)),
                        new Shoot(0, 2, 15, 4, coolDown: Cd(1000)),
                        new State("7",
                            new EntitiesNotExistsTransition(30, "8", N("NM Green Dragon Shield"))
                            ),
                        new State("8",
                            new RemoveConditionalEffect(ConditionEffectIndex.Invincible),
                            new Follow(0.4, 20, 1),
                            new Spawn(N("NM Green Dragon Minion"), 3, 0, coolDown: 6000)
                            )
                        ),
                    new State("9",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new ReturnToSpawn(1, 0),
                        new Flash(0xFFFFFF, 0.2, 12),
                        new TimedTransition(4000, "10")
                        ),
                    new State("10",
                        new TossObject(N("NM Green Dragon Shield"), 3, 30),
                        new TossObject(N("NM Green Dragon Shield"), 3, 90),
                        new TossObject(N("NM Green Dragon Shield"), 3, 150),
                        new TossObject(N("NM Green Dragon Shield"), 3, 210),
                        new TossObject(N("NM Green Dragon Shield"), 3, 270),
                        new TossObject(N("NM Green Dragon Shield"), 3, 330),
                        new TimedTransition(0, "11")
                        ),
                    new State("11",
                        new EntityExistsTransition(N("NM Green Dragon Shield"), 20, "12"),
                        new TimedTransition(3000, "12")
                        ),
                    new State("12",
                        new HpLessTransition(0.05, "15"),
                        new Shoot(0, 12, 30, 0, 45, coolDown: Cd(1500)),
                        new Shoot(0, 2, 15, 6, 0, coolDown: Cd(1000)),
                        new Shoot(0, 2, 15, 6, 90, coolDown: Cd(1000)),
                        new Shoot(0, 2, 15, 6, 180, coolDown: Cd(1000)),
                        new Shoot(0, 2, 15, 6, 270, coolDown: Cd(1000)),
                        new Shoot(0, 10, 36, 5, 10, coolDown: Cd(2000)),
                        new Shoot(0, 2, 15, 4, coolDown: Cd(1000)),
                        new State("13",
                            new EntitiesNotExistsTransition(30, "14", N("NM Green Dragon Shield"))
                            ),
                        new State("14",
                            new RemoveConditionalEffect(ConditionEffectIndex.Invincible),
                            new Follow(0.4, 20, 1),
                            new Spawn(N("NM Green Dragon Minion"), 3, 0, coolDown: 6000)
                            )
                        ),
                    new State("15",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new ReturnToSpawn(0.7),
                        new Decay(3000)
                        )
                    )
                )
            .Init(E("NM Green Dragon Minion"),
                new State(
                    new Prioritize(
                        new Protect(0.5, N("NM Green Dragon God"), 10, 7),
                        new Wander(0.3)
                        ),
                    new Shoot(10, 1, projectileIndex: 3, coolDown: Cd(1000))
                    )
                )
            .Init(E("NM Green Dragon Shield"),
                new State(
                    new Orbit(0.4, 3, 10, N("NM Green Dragon God")),
                    new Shoot(8, 1, projectileIndex: 0, coolDown: Cd(1500))
                    )
                );
        }
    }
}
