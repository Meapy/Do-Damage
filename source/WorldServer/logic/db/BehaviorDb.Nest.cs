using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Nest (RealmEye): the Killer Bee Queen's hive, reached from the realm's Killer Bee Nest
    // (RealmManager.OpenHive) or with The Nest Key. Its map is tools/build_nest.py's. The rooms hold
    // Soldier Bees, Killer Bees, Mini Killer Bee Nests, larvae and eggs; the Queen fights in eight
    // phases, calling her bees by colour, then three Killer Bee Nests that hatch Adolescent
    // Beehemoths to guard her to the end. Behind breakable blocks at the end of a dead-end room is
    // the Beekeeper's laboratory, where Oryx's old bee-maker fights in four phases. Left out: the
    // Killer Honey flooding the boss room.
    partial class BehaviorDb
    {
        private _ Nest = () => Behav()
        #region The rooms
        // Soldier Bees: each colour fights in its own way; any that reach the Queen's room guard her
        .Init("EH Large Yellow Bee",
            new State(
                new StayCloseToSpawn(0.6, 10),
                new State("Wait",
                    new Wander(0.3),
                    new PlayerWithinTransition(10, "Attack")
                    ),
                new State("Attack",
                    new Prioritize(
                        new Orbit(0.7, 5, 12, "EH King Bee"),
                        new Follow(0.6, 12, 3)
                        ),
                    new Shoot(8, count: 3, shootAngle: 12, projectileIndex: 0, coolDown: 900),
                    new Shoot(10, count: 1, projectileIndex: 1, predictive: 0.6, coolDown: 1800),
                    new Shoot(10, count: 6, shootAngle: 60, projectileIndex: 3, fixedAngle: 0, rotateAngle: 15, coolDown: 2400),
                    new Shoot(4, count: 4, shootAngle: 90, projectileIndex: 4, fixedAngle: 45, coolDown: 2000),
                    new HealEntity(12, "EH King Bee", 800, coolDown: 6000)
                    )
                ),
            SoldierBeeLoot("Yellow")
            )
        .Init("EH Large Red Bee",
            new State(
                new StayCloseToSpawn(0.6, 10),
                new State("Wait",
                    new Wander(0.3),
                    new PlayerWithinTransition(10, "Attack")
                    ),
                new State("Attack",
                    new Prioritize(
                        new Orbit(0.7, 5, 12, "EH King Bee"),
                        new Charge(1.6, 9, coolDown: 3000),
                        new Follow(0.7, 12, 2)
                        ),
                    new Shoot(6, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 700),
                    new Shoot(9, count: 1, projectileIndex: 1, predictive: 0.5, coolDown: 1600),
                    new Shoot(8, count: 8, shootAngle: 45, projectileIndex: 2, fixedAngle: 0, coolDown: 2800),
                    new Shoot(9, count: 2, shootAngle: 30, projectileIndex: 3, coolDown: 2000, coolDownOffset: 1000),
                    new HealEntity(12, "EH King Bee", 800, coolDown: 6000)
                    )
                ),
            SoldierBeeLoot("Red")
            )
        .Init("EH Large Blue Bee",
            new State(
                new StayCloseToSpawn(0.6, 10),
                new State("Wait",
                    new Wander(0.3),
                    new PlayerWithinTransition(10, "Attack")
                    ),
                new State("Attack",
                    new Prioritize(
                        new Orbit(0.7, 5, 12, "EH King Bee"),
                        new StayBack(0.6, 5),
                        new Orbit(0.6, 5, 12)
                        ),
                    new Shoot(7, count: 2, shootAngle: 10, projectileIndex: 0, coolDown: 800),
                    new Shoot(9, count: 1, projectileIndex: 1, predictive: 0.6, coolDown: 1600),
                    new Shoot(5, count: 6, shootAngle: 60, projectileIndex: 2, fixedAngle: 0, coolDown: 2200),
                    new Shoot(9, count: 5, shootAngle: 72, projectileIndex: 3, fixedAngle: 0, rotateAngle: 18, coolDown: 2000),
                    new HealEntity(12, "EH King Bee", 800, coolDown: 6000)
                    )
                ),
            SoldierBeeLoot("Blue")
            )
        // Killer Bees: quick and many
        .Init("EH Fat Bees", KillerBee(), new ItemLoot("EH Royal Jelly", 0.01))
        .Init("EH Red Fat Bees", KillerBee(), new ItemLoot("EH Royal Jelly", 0.01))
        .Init("EH Blue Fat Bees", KillerBee(), new ItemLoot("EH Royal Jelly", 0.01))
        .Init("EH Queen Yellow Bees", KillerBee(1.1, 900))
        .Init("EH Queen Blue Bees", KillerBee(1.1, 900))
        // the workers a nest sends out: they sting from close by, slowing
        .Init("EH Small Bees",
            new State(
                new Prioritize(
                    new Follow(1.3, 10, 0.5),
                    new Wander(0.6)
                    ),
                new Shoot(1.2, count: 1, projectileIndex: 0, coolDown: 600)
                )
            )
        // Mini Killer Bee Nests: send out workers and fire their hive shots
        .Init("EH Mini Hive",
            new State(
                new Spawn("EH Small Bees", maxChildren: 4, initialSpawn: 0, coolDown: 4000),
                new State("Wait",
                    new PlayerWithinTransition(9, "Attack")
                    ),
                new State("Attack",
                    new Shoot(5, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, coolDown: 2200),
                    new Shoot(10, count: 1, projectileIndex: 1, predictive: 0.5, coolDown: 1500),
                    new Shoot(10, count: 3, shootAngle: 120, projectileIndex: 2, fixedAngle: 0, rotateAngle: 20, coolDown: 600)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 0.3),
                new ItemLoot("EH Royal Jelly", 0.08)
                )
            )
        // larvae crawl and nip, each with its colour's condition
        .Init("EH Yellow Maggots", Larva())
        .Init("EH Red Maggots", Larva())
        .Init("EH Blue Maggots", Larva())
        // eggs hatch larvae when someone comes near
        .Init("EH Maggot Egg1", Egg("EH Yellow Maggots"))
        .Init("EH Maggot Egg2", Egg("EH Red Maggots"))
        .Init("EH Maggot Egg3", Egg("EH Blue Maggots"))
        #endregion

        #region The Killer Bee Queen
        .Init("EH King Bee",
            new State(
                new ScaleHP2(20),
                new State("Wait",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(8, "Wake")
                    ),
                new State("Wake",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "The Killer Bee Queen stirs. Her hive will not be robbed again!"),
                    new Flash(0xFFFFFF, 0.3, 8),
                    new TimedTransition(2500, "P1")
                    ),
                // round the room and round her pool, bombing and stinging
                new State("P1",
                    new HpLessTransition(0.9, "P2"),
                    new State("P1 Room",
                        new CircleArena(0.7, 10),
                        QueenP1Attacks(),
                        new TimedTransition(6000, "P1 Pond")
                        ),
                    new State("P1 Pond",
                        new CircleArena(0.7, 4.5),
                        QueenP1Attacks(),
                        new TimedTransition(6000, "P1 Room")
                        )
                    ),
                // her colours in turn, each bringing its Killer Bees; between them she can't be hurt
                ColourCycle("P2", false, 0.75, "P3"),
                // in the middle: a messy barrage of balls, and every 7 s a wave of armour-breaking glaives
                new State("P3",
                    new RemoveEntity(40, "EH Fat Bees"),
                    new RemoveEntity(40, "EH Red Fat Bees"),
                    new RemoveEntity(40, "EH Blue Fat Bees"),
                    new HpLessTransition(0.6, "P4"),
                    new State("P3 Centre",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFFF, 0.3, 6),
                        new ReturnToSpawn(1.2),
                        new TimedTransition(2000, "P3 Barrage")
                        ),
                    new State("P3 Barrage",
                        new Shoot(0, count: 16, shootAngle: 22.5, projectileIndex: 6, fixedAngle: 0, coolDown: 7000),
                        new Shoot(0, count: 3, shootAngle: 120, projectileIndex: 8, fixedAngle: 0, rotateAngle: 13, coolDown: 300),
                        new Shoot(0, count: 3, shootAngle: 120, projectileIndex: 19, fixedAngle: 60, rotateAngle: -13, coolDown: 300),
                        new Shoot(0, count: 2, shootAngle: 180, projectileIndex: 13, fixedAngle: 90, rotateAngle: 9, coolDown: 250),
                        new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 9, fixedAngle: 0, coolDown: 1600),
                        new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 23, fixedAngle: 45, coolDown: 2500),
                        WhiteBombs(8, 4000)
                        )
                    ),
                // slowly round the middle, four-way spreads of glaives at different angles
                new State("P4",
                    new HpLessTransition(0.45, "P5"),
                    new CircleArena(0.4, 3, clockwise: false),
                    new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 10, fixedAngle: 0, rotateAngle: 11, coolDown: 700),
                    new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 11, fixedAngle: 30, rotateAngle: 11, coolDown: 700, coolDownOffset: 230),
                    new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 12, fixedAngle: 60, rotateAngle: 11, coolDown: 700, coolDownOffset: 460)
                    ),
                // her colours again, harder
                ColourCycle("P5", true, 0.25, "P6"),
                // three Killer Bee Nests along the walls swell and hatch Adolescent Beehemoths, while
                // she turns a two-way stream of stingers in the middle
                new State("P6",
                    new RemoveEntity(40, "EH Fat Bees"),
                    new RemoveEntity(40, "EH Red Fat Bees"),
                    new RemoveEntity(40, "EH Blue Fat Bees"),
                    new RemoveEntity(40, "EH Queen Yellow Bees"),
                    new RemoveEntity(40, "EH Queen Blue Bees"),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new State("P6 Centre",
                        new Flash(0xFF0000, 0.3, 6),
                        new ReturnToSpawn(1.2),
                        new TossObject("EH Queen Red Hive", 11, 90, coolDown: 999999),
                        new TossObject("EH Queen Blue Hive", 11, 210, coolDown: 999999),
                        new TossObject("EH Queen Yellow Hive", 11, 330, coolDown: 999999),
                        new TimedTransition(2000, "P6 Stream")
                        ),
                    new State("P6 Stream",
                        new Shoot(0, count: 2, shootAngle: 180, projectileIndex: 0, fixedAngle: 90, rotateAngle: 4, coolDown: 150),
                        new EntitiesNotExistsTransition(40, "P7", "EH Queen Red Hive", "EH Queen Blue Hive", "EH Queen Yellow Hive"),
                        new TimedTransition(31000, "P7")
                        )
                    ),
                // her colours with the Beehemoths guarding and healing her
                ColourCycle("P7", true, 0.125, "P8", noBees: true),
                // the end: slow rings of everything, from the middle
                new State("P8",
                    new ReturnToSpawn(1),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 20, fixedAngle: 0, rotateAngle: 7, coolDown: 1500),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 21, fixedAngle: 15, rotateAngle: 7, coolDown: 1500, coolDownOffset: 500),
                    new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 19, fixedAngle: 0, rotateAngle: -9, coolDown: 1700, coolDownOffset: 1000),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 15, fixedAngle: 22.5, coolDown: 2500),
                    new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 10, fixedAngle: 0, rotateAngle: 10, coolDown: 2000, coolDownOffset: 700),
                    new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 11, fixedAngle: 30, rotateAngle: 10, coolDown: 2000, coolDownOffset: 1400),
                    new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 12, fixedAngle: 15, rotateAngle: 10, coolDown: 2000)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Greater Potion of Dexterity", 1),
                new ItemLoot("Greater Potion of Life", 0.5),
                new ItemLoot("Potion of Dexterity", 1),
                new ItemLoot("Potion of Defense", 1),
                new ItemLoot("EH Queen's Stinger Dagger", 0.01),
                new ItemLoot("EH Hivemaster Helm", 0.01),
                new ItemLoot("EH Royal Jelly", 0.3),
                new ItemLoot("Killer Bee Queen Pet Stone", 0.005),
                new ItemLoot("The Nest Key", 0.1, 0, 0.03),
                new TierLoot(12, ItemType.Weapon, 0.15),
                new TierLoot(13, ItemType.Weapon, 0.06),
                new TierLoot(12, ItemType.Armor, 0.15),
                new TierLoot(13, ItemType.Armor, 0.06),
                new TierLoot(6, ItemType.Ability, 0.1),
                new TierLoot(6, ItemType.Ring, 0.1)
                )
            )
        // her three Killer Bee Nests (phase 6): they swell for 29 s, sending out workers and firing,
        // then break open into an Adolescent Beehemoth each, leaving their shells firing till she dies
        #region The Beekeeper
        // RealmEye, Beekeeper: he wakes when a player comes in, is untouchable for a moment at the
        // start of each phase, and taunts anyone who leaves. 1: a slow chase with fireball streams,
        // spreads of bees and fire blasts, and flames where players stand (armour-breaking). 2: his
        // Killer Bees, which heal him, and four-way spirals while he circles the middle. 3: armoured
        // at the back of the room, flames everywhere, streams, spreads and lava bombs. 4: back in the
        // middle he heals and calls two Experimental Soldier Bees of one colour, which follow him
        // round the room and burst into rings when he dies.
        .Init("EH Beekeeper",
            new State(
                new ScaleHP2(20),
                new State("Wait",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(10, "Found")
                    ),
                new State("Found",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "Who goes there? Oryx...have you come for me at last?"),
                    new TimedTransition(2500, "P1")
                    ),
                new State("Fight",
                    new NoPlayerWithinTransition(25, "Lost"),
                    new State("P1",
                        new HpLessTransition(0.75, "P2"),
                        new State("P1 Start",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Taunt("You...you will not stop me again!"),
                            new TimedTransition(1500, "P1 Fight")
                            ),
                        new State("P1 Fight",
                            new Follow(0.35, 12, 3),
                            new Shoot(12, count: 1, projectileIndex: 0, coolDown: 250),
                            new Shoot(12, count: 5, shootAngle: 15, projectileIndex: 3, coolDown: 1800),
                            new Shoot(12, count: 3, shootAngle: 20, projectileIndex: 4, coolDown: 2400, coolDownOffset: 900),
                            new TossObject("EH Beekeeper Flame", 10, coolDown: 1200)
                            )
                        ),
                    new State("P2",
                        new HpLessTransition(0.5, "P3"),
                        new State("P2 Start",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Taunt("Enough! Come forth my insect children, and make them beg for death!"),
                            new ReturnToSpawn(1),
                            new TimedTransition(2000, "P2 Fight")
                            ),
                        new State("P2 Fight",
                            new CircleArena(0.5, 4),
                            new Spawn("EH Secret Fat Bees", maxChildren: 2, initialSpawn: 1, coolDown: 6000),
                            new Spawn("EH Secret Red Fat Bees", maxChildren: 2, initialSpawn: 1, coolDown: 6000),
                            new Spawn("EH Secret Blue Fat Bees", maxChildren: 2, initialSpawn: 1, coolDown: 6000),
                            new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 0, rotateAngle: 12, coolDown: 300),
                            new Shoot(12, count: 5, shootAngle: 15, projectileIndex: 3, coolDown: 2500)
                            )
                        ),
                    new State("P3",
                        new HpLessTransition(0.25, "P4"),
                        new State("P3 Start",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Taunt("I am the creator of a perfect insect race! You WILL kneel before me RIGHT NOW!"),
                            new ReturnToSpawn(1.2),
                            new TimedTransition(2000, "P3 Fight")
                            ),
                        new State("P3 Fight",
                            new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                            // to the back of the laboratory, below its monitor
                            new MoveTo3(0, -5, 0.6, once: true),
                            new Shoot(14, count: 2, shootAngle: 8, projectileIndex: 2, coolDown: 300),
                            new Shoot(14, count: 7, shootAngle: 12, projectileIndex: 3, coolDown: 1600),
                            new Shoot(14, count: 5, shootAngle: 18, projectileIndex: 4, coolDown: 2000, coolDownOffset: 800),
                            new TossObject("EH Beekeeper Flame", 12, coolDown: 600),
                            new TossObject("EH Beekeeper Flame Short", minRange: 2, maxRange: 9, minAngle: 0, maxAngle: 360, coolDown: 250),
                            new TossObject("EH Secret Bullet Spawner", 8, coolDown: 3500)
                            )
                        ),
                    new State("P4",
                        new State("P4 Start",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Taunt("Augh! No...I cannot...My experiments..."),
                            new ReturnToSpawn(1.2),
                            new HealSelf(1000, 3000),
                            new RemoveEntity(30, "EH Secret Fat Bees"),
                            new RemoveEntity(30, "EH Secret Red Fat Bees"),
                            new RemoveEntity(30, "EH Secret Blue Fat Bees"),
                            new TimedRandomTransition(3000, false, "P4 Yellow", "P4 Red", "P4 Blue")
                            ),
                        ExperimentalBees("P4 Yellow", "EH SLBY"),
                        ExperimentalBees("P4 Red", "EH SLBR"),
                        ExperimentalBees("P4 Blue", "EH SLBB"),
                        new State("P4 Queen",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new EntityExistsTransition("EH King Bee", 400, "P4 Queen Lives"),
                            new TimedTransition(100, "P4 Queen Dead")
                            ),
                        new State("P4 Queen Lives",
                            new Taunt(true, "I WILL NOT LET YOU DESTROY THE QUEEN!"),
                            new TimedTransition(0, "P4 Fight")
                            ),
                        new State("P4 Queen Dead",
                            new Taunt(true, "YOU WILL PAY FOR DESTROYING THE QUEEN!"),
                            new TimedTransition(0, "P4 Fight")
                            ),
                        new State("P4 Fight",
                            new CircleArena(0.7, 6),
                            new Shoot(14, count: 1, projectileIndex: 1, coolDown: 200),
                            new Shoot(14, count: 3, shootAngle: 15, projectileIndex: 4, coolDown: 1500)
                            )
                        )
                    ),
                new State("Lost",
                    new Taunt("Show yourself you lousy rat!"),
                    new ReturnToSpawn(1),
                    new PlayerWithinTransition(10, "P1")
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Greater Potion of Dexterity", 1),
                new ItemLoot("Greater Potion of Defense", 0.5),
                new ItemLoot("Potion of Dexterity", 1),
                new ItemLoot("EH Royal Jelly", 0.5),
                new ItemLoot("EH Yellow Bee Armor", 0.02),
                new ItemLoot("EH Red Bee Armor", 0.02),
                new ItemLoot("EH Blue Bee Armor", 0.02),
                new TierLoot(12, ItemType.Weapon, 0.2),
                new TierLoot(12, ItemType.Armor, 0.2),
                new TierLoot(6, ItemType.Ability, 0.12),
                new TierLoot(6, ItemType.Ring, 0.12)
                )
            )
        // his flames burn whoever stands in them, then go out
        .Init("EH Beekeeper Flame",
            new State(
                new Shoot(1.5, count: 1, projectileIndex: 0, coolDown: 200),
                new Decay(4500)
                )
            )
        .Init("EH Beekeeper Flame Short",
            new State(
                new Shoot(1.2, count: 1, projectileIndex: 0, coolDown: 200),
                new Decay(2500)
                )
            )
        // the lava bomb: a moment's warning, then a ring of blasts and bees
        .Init("EH Secret Bullet Spawner",
            new State(
                new State("Wait",
                    new TimedTransition(900, "Burst")
                    ),
                new State("Burst",
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, coolDown: 9999),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 2, fixedAngle: 22.5, coolDown: 9999),
                    new TimedTransition(100, "Gone")
                    ),
                new State("Gone",
                    new Suicide()
                    )
                )
            )
        // his Killer Bees heal him while they live
        .Init("EH Secret Fat Bees", SecretBee())
        .Init("EH Secret Red Fat Bees", SecretBee())
        .Init("EH Secret Blue Fat Bees", SecretBee())
        .Init("EH SLBY", ExperimentalBee())
        .Init("EH SLBR", ExperimentalBee())
        .Init("EH SLBB", ExperimentalBee())
        #endregion

        .Init("EH Queen Red Hive", QueenNest("EH QLB Red", "EH QRH Corpse"))
        .Init("EH Queen Blue Hive", QueenNest("EH QLB Blue", "EH QBH Corpse"))
        .Init("EH Queen Yellow Hive", QueenNest("EH QLB Yellow", "EH QYH Corpse"))
        .Init("EH QRH Corpse", NestShell())
        .Init("EH QBH Corpse", NestShell())
        .Init("EH QYH Corpse", NestShell())
        // the Adolescent Beehemoths guard and heal her; when she dies they flash and burst
        .Init("EH QLB Red", Beehemoth(), new ItemLoot("EH Red Bee Armor", 0.01))
        .Init("EH QLB Blue", Beehemoth(), new ItemLoot("EH Blue Bee Armor", 0.01))
        .Init("EH QLB Yellow", Beehemoth(), new ItemLoot("EH Yellow Bee Armor", 0.01))
        #endregion
        ;

        private static Threshold SoldierBeeLoot(string colour) =>
            new Threshold(0.01,
                new ItemLoot("Potion of Dexterity", 0.3),
                new ItemLoot("EH Royal Jelly", 0.1),
                new ItemLoot($"Large {colour} Bee Cloth", 0.03),
                new ItemLoot($"Small {colour} Bee Cloth", 0.03),
                new ItemLoot($"EH {colour} Bee Armor", 0.004)
                );

        private static State KillerBee(double speed = 1, int coolDown = 1100) =>
            new State(
                new Prioritize(
                    new Orbit(1, 4, 10, "EH King Bee"),
                    new Follow(speed, 10, 2),
                    new Wander(0.5)
                    ),
                new Shoot(8, count: 1, projectileIndex: 0, predictive: 0.4, coolDown: coolDown)
                );

        private static State Larva() =>
            new State(
                new Prioritize(
                    new Follow(0.4, 6, 0.5),
                    new Wander(0.2)
                    ),
                new Shoot(1.5, count: 1, projectileIndex: 0, coolDown: 800)
                );

        private static State Egg(string larva) =>
            new State(
                new State("Wait",
                    new PlayerWithinTransition(4, "Hatch")
                    ),
                new State("Hatch",
                    new Spawn(larva, maxChildren: 3, initialSpawn: 0.34, coolDown: 6000)
                    )
                );

        // what she throws and fires in her first phase: rings of quieting blue bombs, dazing white
        // bombs at players, and stacked spreads of three colours of stingers
        private static IStateChildren QueenP1Attacks()
        {
            var children = new List<IStateChildren>
            {
                new Shoot(10, count: 3, shootAngle: 12, projectileIndex: 1, coolDown: 1500),
                new Shoot(10, count: 3, shootAngle: 12, projectileIndex: 5, coolDown: 1500),
                new Shoot(10, count: 3, shootAngle: 12, projectileIndex: 2, coolDown: 1500),
                new Grenade(2, 100, range: 8, coolDown: 2000, effect: ConditionEffectIndex.Dazed, effectDuration: 2000, color: 0xFFFFFF),
            };
            for (var a = 0; a < 360; a += 60)
                children.Add(new Grenade(2, 80, range: 4, fixedAngle: a, coolDown: 3000, effect: ConditionEffectIndex.Quiet, effectDuration: 1000, color: 0x3060FF));
            return new State(children.ToArray());
        }

        private static State WhiteBombs(int count, int coolDown)
        {
            var bombs = new List<IStateChildren>();
            for (var i = 0; i < count; i++)
                bombs.Add(new Grenade(2, 120, range: 6, fixedAngle: i * 360.0 / count, coolDown: coolDown, effect: ConditionEffectIndex.Dazed, effectDuration: 2000, color: 0xFFFFFF));
            return new State(bombs.ToArray());
        }

        // her colours in turn, 8 s each with 2 s between in which she can't be hurt: red chases with
        // stingers and confusing bombs, blue chases with stingers, balls and quieting bombs, yellow
        // stays and fires stingers and balls with dazing bombs. Each brings its Killer Bees (not with
        // the Beehemoths guarding her); `hard` adds the later phases' glaives and hive shots.
        private static State ColourCycle(string name, bool hard, double hpTo, string next, bool noBees = false)
        {
            State Gap(string state, string after, string bees)
            {
                var children = new List<IStateChildren>
                {
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new TimedTransition(2000, $"{name} {after}"),
                };
                if (bees != null)
                    children.Add(new RemoveEntity(40, bees));
                return new State($"{name} {state}", children.ToArray());
            }

            var red = new List<IStateChildren>
            {
                new Flash(0xFF0000, 0.3, 4),
                new Follow(0.9, 20, 2),
                new Shoot(10, count: 5, shootAngle: 30, projectileIndex: 0, coolDown: 900),
                new Shoot(10, count: 1, projectileIndex: 1, predictive: 0.5, coolDown: 700),
                new TimedTransition(8000, $"{name} Gap Red"),
            };
            for (var a = 0; a < 360; a += 72)
                red.Add(new Grenade(2, 80, range: 4, fixedAngle: a, coolDown: 2500, effect: ConditionEffectIndex.Confused, effectDuration: 3000, color: 0xFF2020));
            var blue = new List<IStateChildren>
            {
                new Flash(0x3060FF, 0.3, 4),
                new Follow(0.7, 20, 3),
                new Shoot(10, count: 3, shootAngle: 15, projectileIndex: 5, coolDown: 1200),
                new Shoot(0, count: 2, shootAngle: 180, projectileIndex: 17, fixedAngle: 0, rotateAngle: 17, coolDown: 250),
                new Shoot(0, count: 1, projectileIndex: 8, fixedAngle: 0, rotateAngle: 137, coolDown: 150),
                new TimedTransition(8000, $"{name} Gap Blue"),
            };
            for (var a = 0; a < 360; a += 60)
                blue.Add(new Grenade(2, 80, range: 4, fixedAngle: a, coolDown: 3000, effect: ConditionEffectIndex.Quiet, effectDuration: 1000, color: 0x3060FF));
            var yellow = new List<IStateChildren>
            {
                new Flash(0xFFFF00, 0.3, 4),
                new StayCloseToSpawn(0.5, 3),
                new Shoot(10, count: 3, shootAngle: 20, projectileIndex: 3, coolDown: 1000),
                new Shoot(0, count: 1, projectileIndex: 18, fixedAngle: 0, rotateAngle: 151, coolDown: 150),
                new Grenade(2, 120, range: 7, coolDown: 900, effect: ConditionEffectIndex.Dazed, effectDuration: 2000, color: 0xFFFFFF),
                new TimedTransition(8000, $"{name} Gap Yellow"),
            };
            if (hard)
            {
                red.Add(new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 11, fixedAngle: 0, coolDown: 2000));
                red.Add(new Shoot(10, count: 3, shootAngle: 25, projectileIndex: 14, coolDown: 1500));
                blue.Add(new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 12, fixedAngle: 22.5, coolDown: 2000));
                blue.Add(new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 15, coolDown: 2200));
                yellow.Add(new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 10, fixedAngle: 0, rotateAngle: 15, coolDown: 1200));
                yellow.Add(new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 16, fixedAngle: 45, coolDown: 1500));
            }
            if (!noBees)
            {
                red.Add(new Spawn("EH Red Fat Bees", maxChildren: 3, initialSpawn: 1, coolDown: 3000));
                blue.Add(new Spawn(hard ? "EH Queen Blue Bees" : "EH Blue Fat Bees", maxChildren: 3, initialSpawn: 1, coolDown: 3000));
                yellow.Add(new Spawn(hard ? "EH Queen Yellow Bees" : "EH Fat Bees", maxChildren: 3, initialSpawn: 1, coolDown: 3000));
            }
            return new State(name,
                new HpLessTransition(hpTo, next),
                Gap("Start", "Red", null),
                new State($"{name} Red", red.ToArray()),
                Gap("Gap Red", "Blue", noBees ? null : "EH Red Fat Bees"),
                new State($"{name} Blue", blue.ToArray()),
                Gap("Gap Blue", "Yellow", noBees ? null : hard ? "EH Queen Blue Bees" : "EH Blue Fat Bees"),
                new State($"{name} Yellow", yellow.ToArray()),
                Gap("Gap Yellow", "Red", noBees ? null : hard ? "EH Queen Yellow Bees" : "EH Fat Bees")
                );
        }

        // the Beekeeper's fourth phase with two Experimental Soldier Bees of one colour
        private static State ExperimentalBees(string name, string bee) =>
            new State(name,
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new HealSelf(1000, 3000),
                new TossObject(bee, 4, 0, coolDown: 999999),
                new TossObject(bee, 4, 180, coolDown: 999999),
                new TimedTransition(2500, "P4 Queen")
                );

        private static State SecretBee() =>
            new State(
                new Prioritize(
                    new Orbit(1.2, 3, 15, "EH Beekeeper"),
                    new Follow(1, 10, 2),
                    new Wander(0.5)
                    ),
                new Shoot(8, count: 1, projectileIndex: 0, predictive: 0.4, coolDown: 1000),
                new HealEntity(15, "EH Beekeeper", 200, coolDown: 5000)
                );

        // can't be hurt; follows the Beekeeper, firing rings of its colour's stingers, glaives and
        // balls, and bursts into dense rings when he dies
        private static State ExperimentalBee() =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                new State("Follow",
                    new Prioritize(
                        new Orbit(0.9, 3, 30, "EH Beekeeper"),
                        new Wander(0.4)
                        ),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, rotateAngle: 11, coolDown: 1400),
                    new Shoot(10, count: 1, projectileIndex: 1, predictive: 0.5, coolDown: 1800),
                    new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 2, fixedAngle: 45, rotateAngle: 20, coolDown: 2600),
                    new Shoot(10, count: 3, shootAngle: 20, projectileIndex: 3, coolDown: 3000),
                    new EntityNotExistsTransition("EH Beekeeper", 60, "Burst")
                    ),
                new State("Burst",
                    new Flash(0xFFFFFF, 0.2, 8),
                    new TimedTransition(1500, "Die")
                    ),
                new State("Die",
                    new Shoot(0, count: 24, shootAngle: 15, projectileIndex: 0, fixedAngle: 0, coolDown: 9999),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 3, fixedAngle: 7.5, coolDown: 9999),
                    new Suicide()
                    )
                );

        private static State QueenNest(string beehemoth, string shell) =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                new State("Swell",
                    new ChangeSize(5, 160),
                    new Spawn("EH Small Bees", maxChildren: 3, initialSpawn: 0, coolDown: 5000),
                    new Shoot(10, count: 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, rotateAngle: 10, coolDown: 1800),
                    new TimedTransition(29000, "Hatch")
                    ),
                new State("Hatch",
                    new TossObject(beehemoth, 2, 270, coolDown: 999999),
                    new TimedTransition(300, "Shell")
                    ),
                new State("Shell",
                    new Transform(shell)
                    )
                );

        private static State NestShell() =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                new State("Fire",
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, rotateAngle: 12, coolDown: 2400),
                    new EntityNotExistsTransition("EH King Bee", 50, "Gone")
                    ),
                new State("Gone",
                    new Decay(0)
                    )
                );

        private static State Beehemoth() =>
            new State(
                new State("Guard",
                    new Prioritize(
                        new Orbit(0.8, 4, 30, "EH King Bee"),
                        new Wander(0.5)
                        ),
                    new Shoot(10, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1500),
                    new Shoot(10, count: 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 0, rotateAngle: 20, coolDown: 2000),
                    new HealEntity(30, "EH King Bee", 600, coolDown: 10000),
                    new HealSelf(15000, 3000),
                    new EntityNotExistsTransition("EH King Bee", 60, "Burst")
                    ),
                new State("Burst",
                    new Flash(0xFFFFFF, 0.2, 10),
                    new Wander(0.5),
                    new TimedTransition(2500, "Die")
                    ),
                new State("Die",
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 0, fixedAngle: 0, coolDown: 9999),
                    new Suicide()
                    )
                );
    }
}
