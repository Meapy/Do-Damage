using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Oryx = () => Behav()
            // Stage 4 of the Oryx chain (castle guardians, Janus, Oryx 1, 2, 3, 4 and 5). BossStats puts
            // every stage on one difficulty curve; SETUP-LOCAL.md has the table.
            .Init("Oryx the Mad God 2",
                new State(
                    new BossStats(60000, 40, 1.0, 0.3, arena: true),
                    new BossStats(130000, 40, 1.0, 0.3),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120, xAdjustment: -2),
                    // open: Oryx 3 needs no incantation
                    new DropPortalOnDeath("Oryx's Sanctuary Portal", 1.0, timeout: 600, xAdjustment: 2),
                    new DropPortalOnDeath("Lost Halls Portal", 0.1, timeout: 120, yAdjustment: 2),
                    new AnnounceOnDeath("{PL_LIST} drove Oryx the Mad God out of his Wine Cellar! He flees to his Sanctuary, and the way lies open..."),
                    new State("Where",
                        new ArenaTransition("Arena Wake"),
                        new ArenaTransition("Attack", false)
                    ),
                    // in an arena: only his first phase, with fewer henchmen
                    ArenaWake("Puny mortals! You will not leave this arena alive!", "Arena Attack"),
                    new State("Arena Attack", Oryx2Attack(3)),
                    new State("Attack",
                        Oryx2Attack(5).Append(new HpLessTransition(.55, "Guards")).ToArray()
                    ),
                    // halfway: a short invulnerable call for help, under rings of silencing and weakening spinners
                    new State("Guards",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFF7F00FF, 0.4, 10),
                        new Taunt("Guards! GUARDS! Rid my cellar of these vermin!"),
                        new ReturnToSpawn(1),
                        new Spawn("Henchman of Oryx", 3, initialSpawn: 1, coolDown: 1000),
                        new Spawn("Vintner of Oryx", 3, initialSpawn: 1, coolDown: 1000),
                        new Shoot(0, 8, fixedAngle: 0, rotateAngle: 7, projectileIndex: 13, coolDown: 600),
                        new Shoot(0, 8, fixedAngle: 22, rotateAngle: -7, projectileIndex: 14, coolDown: 600, coolDownOffset: 300),
                        new TimedTransition(6000, "Attack2")
                    ),
                    new State("Attack2",
                        new Wander(.08),
                        new Shoot(25, projectileIndex: 0, count: 8, shootAngle: 45, coolDown: 1300, coolDownOffset: 500),
                        new Shoot(25, projectileIndex: 1, count: 3, shootAngle: 10, coolDown: 900),
                        new Shoot(25, projectileIndex: 2, count: 3, shootAngle: 10, predictive: 0.5, coolDown: 900),
                        new Shoot(25, projectileIndex: 4, count: 3, shootAngle: 10, predictive: 0.6, coolDown: 900),
                        new Shoot(25, projectileIndex: 6, count: 3, shootAngle: 10, predictive: 1, coolDown: 900),
                        new Shoot(10, projectileIndex: 2, count: 5, shootAngle: 18, coolDown: 1800),
                        new Taunt(1, 8000, "You will drown in my wine, {PLAYER}!"),
                        new Spawn("Henchman of Oryx", 5, coolDown: 5000),
                        new HpLessTransition(.2, "prepareRage")
                    ),
                    new State("prepareRage",
                        new Follow(.1, 15, 3),
                        new Taunt("Can't... keep... henchmen... alive... anymore! ARGHHH!!!"),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(25, 30, fixedAngle: 0, projectileIndex: 13, coolDown: 4000, coolDownOffset: 4000),
                        new Shoot(25, 30, fixedAngle: 30, projectileIndex: 14, coolDown: 4000, coolDownOffset: 4000),
                        new TimedTransition(10000, "rage")
                    ),
                    new State("rage",
                        new Follow(.1, 15, 3),
                        new Shoot(25, 30, projectileIndex: 13, coolDown: 90000001, coolDownOffset: 8000),
                        new Shoot(25, 30, projectileIndex: 14, coolDown: 90000001, coolDownOffset: 8500),
                        new Shoot(25, projectileIndex: 0, count: 8, shootAngle: 45, coolDown: 1500, coolDownOffset: 1500),
                        new Shoot(25, projectileIndex: 1, count: 3, shootAngle: 10, coolDown: 1000, coolDownOffset: 1000),
                        new Shoot(25, projectileIndex: 2, count: 3, shootAngle: 10, predictive: 0.2, coolDown: 1000,
                            coolDownOffset: 1000),
                        new Shoot(25, projectileIndex: 3, count: 2, shootAngle: 10, predictive: 0.4, coolDown: 1000,
                            coolDownOffset: 1000),
                        new Shoot(25, projectileIndex: 4, count: 3, shootAngle: 10, predictive: 0.6, coolDown: 1000,
                            coolDownOffset: 1000),
                        new Shoot(25, projectileIndex: 5, count: 2, shootAngle: 10, predictive: 0.8, coolDown: 1000,
                            coolDownOffset: 1000),
                        new Shoot(25, projectileIndex: 6, count: 3, shootAngle: 10, predictive: 1, coolDown: 1000,
                            coolDownOffset: 1000),
                        new TossObject("Monstrosity Scarab", 7, 0, coolDown: 1000),
                        new Taunt(1, 6000, "Puny mortals! My {HP} HP will annihilate you!")
                    )
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 0.6),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Vitality", 0.6),
                    new ItemLoot("Potion of Wisdom", 0.6),
                    new ItemLoot("Potion of Speed", 0.35),
                    new ItemLoot("Potion of Dexterity", 0.35),
                    new ItemLoot("Hash Cellar Incantation", 0.04),
                    new ItemLoot("Seal of Blasphemous Prayer", 0.01),
                    new ItemLoot("Doom Bow", 0.008)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(12, ItemType.Weapon, 0.12),
                    new TierLoot(13, ItemType.Weapon, 0.06),
                    new TierLoot(5, ItemType.Ability, 0.1),
                    new TierLoot(6, ItemType.Ability, 0.06),
                    new TierLoot(12, ItemType.Armor, 0.1),
                    new TierLoot(13, ItemType.Armor, 0.07),
                    new TierLoot(5, ItemType.Ring, 0.08),
                    new TierLoot(6, ItemType.Ring, 0.06)
                )
            )
            // Stage 3: cycles through five phases; below 20% he grows and chases.
            .Init("Oryx the Mad God 1",
                new State(
                    new BossStats(100000, 35, 1.0, 0.3),
                    // open: Oryx 2 needs no incantation
                    new DropPortalOnDeath("Wine Cellar Portal", 1.0, timeout: 600, xAdjustment: 2),
                    new AnnounceOnDeath("{PL_LIST} defeated Oryx the Mad God in his Chamber! He retreats to his Wine Cellar, and the way lies open..."),
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(12, "Intro")
                    ),
                    new State("Intro",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFF389E13, 0.5, 5),
                        new Taunt("You dare enter my Chamber? Fools! I still have {HP} hitpoints!"),
                        new TimedTransition(3000, "Slow")
                    ),
                    new State("Fight",
                        new HpLessTransition(.2, "rage"),
                        new State("Slow",
                            new Taunt("Minions! Take them!"),
                            new Wander(0.1),
                            new StayCloseToSpawn(0.3, 6),
                            new Spawn("Minion of Oryx", 5, 0.6, 15000),
                            new Reproduce("Minion of Oryx", 10, 5, 3000),
                            new Shoot(25, 4, 10, 4, coolDown: 1200),
                            new Shoot(12, 3, 15, 2, predictive: 0.5, coolDown: 900),
                            new TimedTransition(14000, "Dance 1")
                        ),
                        // invulnerable: a ring of silencing elements and a slow four-arm spiral to weave through
                        new State("Dance 1",
                            new Flash(0xFF389E13, 0.5, 20),
                            new Taunt("BE SILENT!!!"),
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new ReturnToSpawn(1.5),
                            new Shoot(0, 4, fixedAngle: 0, rotateAngle: 12, projectileIndex: 17, coolDown: 320),
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
                            new TimedTransition(10000, "artifacts")
                        ),
                        new State("artifacts",
                            new Taunt("My Artifacts will protect me!"),
                            new Flash(0xFF389E13, 0.5, 6),
                            new Shoot(25, 3, 20, projectileIndex: 16, coolDown: 2000, coolDownOffset: 500),
                            new Shoot(0, 10, fixedAngle: 0, rotateAngle: 18, projectileIndex: 8, coolDown: 2500),
                            new Shoot(3, 10, projectileIndex: 7, coolDown: 800),
                            new TossObject("Guardian Element 1", 1, 0, 90000001, 1000),
                            new TossObject("Guardian Element 1", 1, 90, 90000001, 1000),
                            new TossObject("Guardian Element 1", 1, 180, 90000001, 1000),
                            new TossObject("Guardian Element 1", 1, 270, 90000001, 1000),
                            new TossObject("Guardian Element 2", 9, 0, 90000001, 1000),
                            new TossObject("Guardian Element 2", 9, 90, 90000001, 1000),
                            new TossObject("Guardian Element 2", 9, 180, 90000001, 1000),
                            new TossObject("Guardian Element 2", 9, 270, 90000001, 1000),
                            new TimedTransition(14000, "gaze")
                        ),
                        // stay out of his face: his flames reach eight tiles
                        new State("gaze",
                            new Taunt("All who look upon my face shall die."),
                            new Follow(0.4, 12, 4),
                            new Shoot(8, 3, 12, projectileIndex: 1, coolDown: 700),
                            new Shoot(25, 2, 10, 2, predictive: 0.7, coolDown: 1100),
                            new TimedTransition(8000, "Dance 2")
                        ),
                        new State("Dance 2",
                            new Flash(0xFF389E13, 0.5, 18),
                            new Taunt("Time for more dancing!"),
                            new ReturnToSpawn(1.5),
                            new Shoot(0, 4, 90, projectileIndex: 8, fixedAngle: 0, rotateAngle: 22.5, coolDown: 200),
                            new TossObject("Ring Element", 9, 24, 320000),
                            new TossObject("Ring Element", 9, 72, 320000),
                            new TossObject("Ring Element", 9, 120, 320000),
                            new TossObject("Ring Element", 9, 168, 320000),
                            new TossObject("Ring Element", 9, 216, 320000),
                            new TossObject("Ring Element", 9, 264, 320000),
                            new TossObject("Ring Element", 9, 312, 320000),
                            new TossObject("Ring Element", 9, 360, 320000),
                            new TimedTransition(9000, "Slow")
                        )
                    ),
                    new State("rage",
                        new ChangeSize(10, 200),
                        new Taunt(.3, "I HAVE HAD ENOUGH OF YOU!!!",
                            "ENOUGH!!!",
                            "DIE!!!"),
                        new Spawn("Minion of Oryx", 10, 0, 350000),
                        new Reproduce("Minion of Oryx", 10, 5, 1500),
                        new Shoot(7, count: 2, coolDown: 1500, projectileIndex: 1, shootAngle: 10, coolDownOffset: 2000),
                        new Shoot(25, count: 5, coolDown: 1500, projectileIndex: 16, shootAngle: 10, coolDownOffset: 2000),
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 9, projectileIndex: 14, coolDown: 1200),
                        new Follow(0.6, range: 1, coolDown: 0),
                        new Flash(0xFFFF0000, 0.5, 9000001)
                    )
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Life", 0.15)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Dexterity", 0.45),
                    new ItemLoot("Potion of Speed", 0.45),
                    new ItemLoot("Potion of Vitality", 0.25),
                    new ItemLoot("Potion of Wisdom", 0.25)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(11, ItemType.Weapon, 0.12),
                    new TierLoot(12, ItemType.Weapon, 0.06),
                    new TierLoot(5, ItemType.Ability, 0.1),
                    new TierLoot(6, ItemType.Ability, 0.03),
                    new TierLoot(12, ItemType.Armor, 0.1),
                    new TierLoot(13, ItemType.Armor, 0.04),
                    new TierLoot(5, ItemType.Ring, 0.08),
                    new TierLoot(6, ItemType.Ring, 0.03)
                )
            )
            .Init("Ring Element",
                new State(
                    new State(
                        new Shoot(50, 12, projectileIndex: 0, coolDown: 700, coolDownOffset: 200),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(20000, "Despawn")
                        ),
                    new State("Despawn", //new Decay(time:0)
                        new Suicide()
                        )
                    )
            )
            .Init("Minion of Oryx",
                new State(
                    new Wander(0.4),
                    new Shoot(3, 3, 10, 0, coolDown: 1000),
                    new Shoot(3, 3, projectileIndex: 1, shootAngle: 10, coolDown: 1000)
                    ),
                new TierLoot(7, ItemType.Weapon, 0.2),
                new ItemLoot("Magic Potion", 0.03)
            )
            .Init("Guardian Element 1",
                new State(
                    new State(
                        new Prioritize(new Orbit(1, 1, target: "Oryx the Mad God 1", radiusVariance: 0), new Orbit(1, 2, target: "Oryx the Mad God 4", radiusVariance: 0)),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(25, 3, 10, 0, coolDown: 1000),
                        new TimedTransition(10000, "Grow")
                        ),
                    new State("Grow",
                        new ChangeSize(100, 200),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Prioritize(new Orbit(1, 1, target: "Oryx the Mad God 1", radiusVariance: 0), new Orbit(1, 2, target: "Oryx the Mad God 4", radiusVariance: 0)),
                        new Shoot(3, 1, 10, 0, coolDown: 700),
                        new TimedTransition(10000, "Despawn")
                        ),
                    new State("Despawn",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Prioritize(new Orbit(1, 1, target: "Oryx the Mad God 1", radiusVariance: 0), new Orbit(1, 2, target: "Oryx the Mad God 4", radiusVariance: 0)),
                        new ChangeSize(100, 100),
                        new Suicide()
                        )
                    )
            )
            .Init("Guardian Element 2",
                new State(
                    new State(
                        new Prioritize(new Orbit(1.3, 9, target: "Oryx the Mad God 1", radiusVariance: 0), new Orbit(1.3, 9, target: "Oryx the Mad God 4", radiusVariance: 0)),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(25, 3, 10, 0, coolDown: 1000),
                        new TimedTransition(20000, "Despawn")
                        ),
                    new State("Despawn", new Suicide()
                        )
                    )
            )
        .Init("Henchman of Oryx",
                new State(
                    new State("Attack",
                        new Prioritize(
                            new Orbit(.2, 2, target: "Oryx the Mad God 2", radiusVariance: 1),
                            new Wander(.3)
                            ),
                    new Shoot(15, predictive: 1, coolDown: 2500),
                    new Shoot(10, count: 3, shootAngle: 10, projectileIndex: 1, coolDown: 2500),
                    new Spawn("Vintner of Oryx", maxChildren: 1, initialSpawn: 1, coolDown: 5000),
                    //  new Spawn("Bile of Oryx", maxChildren: 1, initialSpawn: 1, coolDown: 5000),
                    new Spawn("Aberrant of Oryx", maxChildren: 1, initialSpawn: 1, coolDown: 5000),
                    new Spawn("Monstrosity of Oryx", maxChildren: 1, initialSpawn: 1, coolDown: 5000),
                    new Spawn("Abomination of Oryx", maxChildren: 1, initialSpawn: 1, coolDown: 5000)
                            ),
                    new State("Suicide",
                        new Decay(0)
                             )
                            )
                            )
            .Init("Monstrosity of Oryx",
                new State(
                    new State("Wait", new PlayerWithinTransition(15, "Attack")),
                    new State("Attack",
                        new TimedTransition(10000, "Wait"),
                    new Prioritize(
                        new Orbit(.1, 6, target: "Oryx the Mad God 2", radiusVariance: 3),
                        new Follow(.1, acquireRange: 15),
                        new Wander(.2)
                        ),
                     new TossObject("Monstrosity Scarab", coolDown: 10000, range: 1, angle: 0, coolDownOffset: 1000)
                     )
                     ))
            .Init("Monstrosity Scarab",
                new State(
                    new State("Attack",
                    new State("Charge",
                        new Prioritize(
                            new Charge(range: 25, coolDown: 1000),
                            new Wander(.3)
                            ),
                        new PlayerWithinTransition(1, "Boom")
                        ),
                    new State("Boom",
                        new Shoot(1, count: 16, shootAngle: 360 / 16, fixedAngle: 0),
                        new Decay(0)
                       )
                       )
                       )
                       )
            .Init("Vintner of Oryx",
                new State(
                    new State("Attack",
                        new Prioritize(
                            new Protect(1, "Oryx the Mad God 2", protectionRange: 4, reprotectRange: 3),
                            new Charge(speed: 1, range: 15, coolDown: 2000),
                            new Protect(1, "Henchman of Oryx"),
                            new StayBack(1, 15),
                            new Wander(1)
                        ),
                        new Shoot(10, coolDown: 250)
                        )
                        ))
         .Init("Aberrant of Oryx",
            new State(
                new Prioritize(
                    new Protect(.2, "Oryx the Mad God 2"),
                    new Wander(.7)
                    ),
                new State("Wait", new PlayerWithinTransition(15, "Attack")),
                new State("Attack",
                new TimedTransition(10000, "Wait"),
                new State("Randomize",
                    new TimedTransition(100, "Toss1", randomized: true),
                    new TimedTransition(100, "Toss2", randomized: true),
                    new TimedTransition(100, "Toss3", randomized: true),
                    new TimedTransition(100, "Toss4", randomized: true),
                    new TimedTransition(100, "Toss5", randomized: true),
                    new TimedTransition(100, "Toss6", randomized: true),
                    new TimedTransition(100, "Toss7", randomized: true),
                    new TimedTransition(100, "Toss8", randomized: true)
                   ),
                new State("Toss1",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 0),
                    new TimedTransition(4900, "Randomize")
                    ),
                new State("Toss2",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 45),
                    new TimedTransition(4900, "Randomize")
                    ),
                new State("Toss3",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 90),
                    new TimedTransition(4900, "Randomize")
                    ),
                new State("Toss4",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 135),
                    new TimedTransition(4900, "Randomize")
                    ),
                new State("Toss5",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 180),
                    new TimedTransition(4900, "Randomize")
                    ),
                new State("Toss6",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 225),
                    new TimedTransition(4900, "Randomize")
                    ),
                new State("Toss7",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 270),
                    new TimedTransition(4900, "Randomize")
                    ),
                new State("Toss8",
                    new TossObject("Aberrant Blaster", coolDown: 40000, range: 5, angle: 315),
                    new TimedTransition(4900, "Randomize")
                    ))
                    ))
        .Init("Aberrant Blaster",
            new State(
                new State("Wait",
                    new PlayerWithinTransition(3, "Boom")
                    ),
                new State("Boom",
                    new Shoot(10, count: 5, shootAngle: 7),
                    new Decay(0)
                    )
                    )
                    )
        .Init("Bile of Oryx",
            new State(
                new Prioritize(
                    new Protect(.4, "Oryx the Mad God 2", protectionRange: 5, reprotectRange: 4),
                    new Wander(.5)
                    )//,
                     //new Spawn("Purple Goo", maxChildren: 20, initialSpawn: 0, coolDown: 1000)
                )
                )
        .Init("Abomination of Oryx",
            new State(
                new State("Shoot",
                    new Shoot(1, 3, shootAngle: 5, projectileIndex: 0),
                    new Shoot(1, 5, shootAngle: 5, projectileIndex: 1),
                    new Shoot(1, 7, shootAngle: 5, projectileIndex: 2),
                    new Shoot(1, 5, shootAngle: 5, projectileIndex: 3),
                    new Shoot(1, 3, shootAngle: 5, projectileIndex: 4),
                    new TimedTransition(1000, "Wait")
                    ),
                new State("Wait",
                    new PlayerWithinTransition(2, "Shoot")),
                new Prioritize(
                    new Charge(3, 10, 3000),
                    new Wander(.5))
                    )
                    );

        // Wine Cellar puddles: anyone standing in one is sickened and slowed
        // Oryx 2's first phase: his volleys, and henchmen up to this many
        private static IStateChildren[] Oryx2Attack(int henchmen) => new IStateChildren[]
        {
            new Wander(.05),
            new Shoot(25, projectileIndex: 0, count: 8, shootAngle: 45, coolDown: 1500, coolDownOffset: 1500),
            new Shoot(25, projectileIndex: 1, count: 3, shootAngle: 10, coolDown: 1000, coolDownOffset: 1000),
            new Shoot(25, projectileIndex: 2, count: 3, shootAngle: 10, predictive: 0.2, coolDown: 1000,
                coolDownOffset: 1000),
            new Shoot(25, projectileIndex: 3, count: 2, shootAngle: 10, predictive: 0.4, coolDown: 1000,
                coolDownOffset: 1000),
            new Shoot(25, projectileIndex: 4, count: 3, shootAngle: 10, predictive: 0.6, coolDown: 1000,
                coolDownOffset: 1000),
            new Shoot(25, projectileIndex: 5, count: 2, shootAngle: 10, predictive: 0.8, coolDown: 1000,
                coolDownOffset: 1000),
            new Shoot(25, projectileIndex: 6, count: 3, shootAngle: 10, predictive: 1, coolDown: 1000,
                coolDownOffset: 1000),
            new Taunt(1, 6000, "Puny mortals! My {HP} HP will annihilate you!"),
            new Spawn("Henchman of Oryx", henchmen, coolDown: 5000)
        };

        private _ WineCellarGoo = () => Behav()
            .Init("Purple Goo",
                new State(
                    new Shoot(1, count: 1, projectileIndex: 0, fixedAngle: 0, coolDown: 800)
                    )
                );
    }
}