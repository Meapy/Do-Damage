using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // The Crystal Cavern (RealmEye): the Fungal Cavern's second part, through the portal the
        // Crystal Worm Mother leaves. Break 20 Cavern Crystals round its caves to power the Dwarf
        // Miner's drill, which cuts through to the Crystal Entity's chamber, a pentagon platform in
        // water. The caves hold Amethyst, Emerald, Ruby, Sapphire and Topaz Bats and Slimes, the
        // Crystallised beasts and Crystal Worm Children. The Entity fights in six phases: in the
        // first, third and fifth it can't be hurt and sends a guardian (the Lizard or the Cyclops,
        // the Scorpion or the Fish, then the Monstrosity); in between it fights itself, with Pulse
        // Locks that lock on to players and fire, faster each time; in the last it grows, armours
        // itself and fills the chamber with crystal buds, flowers and prisms. Its map is
        // tools/build_crystal_cavern.py's.
        private _ CrystalCavern = () => Behav()
        #region The caves
        .Init("Amethyst Bat", CaveBat(new Shoot(9, count: 2, shootAngle: 16, projectileIndex: 0, coolDown: 1200),
            new Shoot(9, count: 1, projectileIndex: 1, predictive: 0.6, coolDown: 1700, coolDownOffset: 600)))
        .Init("Emerald Bat", CaveBat(new Shoot(9, count: 1, projectileIndex: 0, predictive: 0.5, coolDown: 700)))
        .Init("Ruby Bat", CaveBat(
            // a line of five bolts at different speeds
            new Shoot(9, count: 1, projectileIndex: 0, coolDown: 2000),
            new Shoot(9, count: 1, projectileIndex: 1, coolDown: 2000),
            new Shoot(9, count: 1, projectileIndex: 2, coolDown: 2000),
            new Shoot(9, count: 1, projectileIndex: 3, coolDown: 2000),
            new Shoot(9, count: 1, projectileIndex: 4, coolDown: 2000)))
        .Init("Sapphire Bat", CaveBat(new Shoot(9, count: 3, shootAngle: 14, projectileIndex: 0, coolDown: 1300)))
        .Init("Topaze Bat", CaveBat(new Shoot(9, count: 1, projectileIndex: 0, coolDown: 900),
            new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, rotateAngle: 17, coolDown: 2400)))
        .Init("Amethyst Slime", CaveSlime())
        .Init("Emerald Slime", CaveSlime())
        .Init("Ruby Slime", CaveSlime())
        .Init("Saphhire Slime", CaveSlime())
        .Init("Topaze Slime", CaveSlime())
        // the Crystallised beasts
        .Init("Crystal Charger",
            new State(
                new Prioritize(
                    new Charge(2, 10, coolDown: 2500),
                    new Wander(0.4)
                    ),
                new Shoot(10, count: 5, shootAngle: 14, projectileIndex: 0, coolDown: 2500, coolDownOffset: 400)
                ),
            CaveLoot()
            )
        .Init("Crystal Watcher",
            new State(
                new Prioritize(
                    new StayBack(0.5, 7),
                    new Wander(0.3)
                    ),
                // three shards one behind another
                new Shoot(11, count: 1, projectileIndex: 1, coolDown: 1500),
                new Shoot(11, count: 1, projectileIndex: 2, coolDown: 1500),
                new Shoot(11, count: 1, projectileIndex: 3, coolDown: 1500)
                ),
            CaveLoot()
            )
        .Init("Crystal Splitter",
            new State(
                new Prioritize(
                    new Follow(0.6, 10, 4),
                    new Wander(0.4)
                    ),
                new Shoot(10, count: 2, shootAngle: 30, projectileIndex: 0, coolDown: 1000),
                new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 30, coolDown: 3000)
                ),
            CaveLoot()
            )
        .Init("Crystal Shrieker",
            new State(
                new StayCloseToSpawn(0.4, 4),
                new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 3, fixedAngle: 0, coolDown: 2400),
                new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 4, fixedAngle: 15, coolDown: 2400, coolDownOffset: 1200),
                new Shoot(10, count: 3, shootAngle: 20, projectileIndex: 8, coolDown: 1800)
                ),
            CaveLoot()
            )
        .Init("Crystal Boomer",
            new State(
                new Prioritize(
                    new Follow(0.5, 10, 5),
                    new Wander(0.4)
                    ),
                // shards that fly out and come back
                new Shoot(10, count: 1, projectileIndex: 1, coolDown: 1800),
                new Shoot(10, count: 2, shootAngle: 20, projectileIndex: 2, coolDown: 1800),
                new Shoot(10, count: 3, shootAngle: 30, projectileIndex: 3, coolDown: 1800)
                ),
            CaveLoot()
            )
        // a worm of its own: its body is broken before its head can be hurt
        .Init("CC Crystal Worm Child",
            new State(
                new SpawnWorm(1.5f, 90, "CC Crystal Worm Child Body", "CC Crystal Worm Child Body", "CC Crystal Worm Child Body",
                    "CC Crystal Worm Child Body", "CC Crystal Worm Child Tail"),
                new State("Armored",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Prioritize(
                        new Follow(0.6, 12, 2),
                        new Wander(0.4)
                        ),
                    new Shoot(10, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1400),
                    new EntitiesNotExistsTransition(30, "Open", "CC Crystal Worm Child Body")
                    ),
                new State("Open",
                    new StayCloseToSpawn(0.5, 4),
                    new Shoot(10, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, rotateAngle: 12, coolDown: 500)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 0.2),
                new ItemLoot("Potion of Vitality", 0.2)
                )
            )
        .Init("CC Crystal Worm Child Body",
            new State(
                new WormSegment(1.5f),
                new Shoot(8, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 45, rotateAngle: 15, coolDown: 1800)
                )
            )
        .Init("CC Crystal Worm Child Tail",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                new WormSegment(1.5f),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1500)
                )
            )
        #endregion

        #region The Cavern Crystals and the drill
        .Init("CC Crystal",
            new State(
                new CountOnDeath("Crystal Cavern Crystals"),
                new TransformOnDeath("CC Destroyed Crystal Explo")
                ),
            new Threshold(0.01,
                new ItemLoot("Greater Potion of Wisdom", 0.05),
                new ItemLoot("Potion of Mana", 0.05)
                )
            )
        .Init("CC Destroyed Crystal Explo",
            new State(
                new State("Burst",
                    new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 0, fixedAngle: 0, coolDown: 9999),
                    new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 1, fixedAngle: 18, coolDown: 9999),
                    new TimedTransition(300, "Gone")
                    ),
                new State("Gone",
                    new Decay(0)
                    )
                )
            )
        // before the wall: it shows its charge in five steps up to 20 crystals (fewer if the layout
        // kept fewer), then drills for 30 s while Crystallised beasts come out of the walls round
        // the room, and breaks the wall
        .Init("CC Drill Charge",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("Charge0",
                    new WorldCountTransition("Crystal Cavern Crystals", CavernCrystals, "Charge1", "CC Crystal", 0.2)
                    ),
                new State("Charge1",
                    new SetAltTexture(1),
                    new WorldCountTransition("Crystal Cavern Crystals", CavernCrystals, "Charge2", "CC Crystal", 0.4)
                    ),
                new State("Charge2",
                    new SetAltTexture(2),
                    new WorldCountTransition("Crystal Cavern Crystals", CavernCrystals, "Charge3", "CC Crystal", 0.6)
                    ),
                new State("Charge3",
                    new SetAltTexture(3),
                    new WorldCountTransition("Crystal Cavern Crystals", CavernCrystals, "Charge4", "CC Crystal", 0.8)
                    ),
                new State("Charge4",
                    new SetAltTexture(4),
                    new WorldCountTransition("Crystal Cavern Crystals", CavernCrystals, "Drilling", "CC Crystal", 1.0)
                    ),
                new State("Drilling",
                    new SetAltTexture(5),
                    new Flash(0x9FE8FF, 1, 30),
                    new TossObject("Crystal Charger", 7, 200, coolDown: 999999),
                    new TossObject("Crystal Watcher", 7, 340, coolDown: 999999),
                    new TossObject("Crystal Boomer", 6, 160, coolDown: 999999, coolDownOffset: 10000),
                    new TossObject("Crystal Splitter", 6, 20, coolDown: 999999, coolDownOffset: 10000),
                    new TossObject("Crystal Shrieker", 7, 90, coolDown: 999999, coolDownOffset: 20000),
                    new TimedTransition(30000, "Break")
                    ),
                new State("Break",
                    new OpenGate("CC Drill Wall", 4),
                    new TimedTransition(1000, "Done")
                    ),
                new State("Done")
                )
            )
        .Init("CC Drill Dwarf",
            new State(
                new State("Wait",
                    new PlayerWithinTransition(8, "Ask")
                    ),
                new State("Ask",
                    new Taunt("You made it down here too? Something's humming behind that wall. Break the crystals in these caves and my drill will get us through."),
                    new WorldCountTransition("Crystal Cavern Crystals", CavernCrystals, "Halfway", "CC Crystal", 0.5)
                    ),
                new State("Halfway",
                    new Taunt(true, "Halfway charged! Keep those crystals coming."),
                    new WorldCountTransition("Crystal Cavern Crystals", CavernCrystals, "Go", "CC Crystal")
                    ),
                new State("Go",
                    new Taunt(true, "Full power! Hold them off while she drills!"),
                    new TimedTransition(31000, "Open")
                    ),
                new State("Open",
                    new Taunt(true, "We're through. I don't like the look of that light..."),
                    new TimedTransition(100, "Done")
                    ),
                new State("Done")
                )
            )
        #endregion

        #region The Crystal Entity
        .Init("mc",
            new State(
                new ScaleHP2(20),
                new StayCloseToSpawn(0.5, 3),
                new State("Wait",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(12, "Greet")
                    ),
                new State("Greet",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "Come seeking treasures like the others? I'm afraid your efforts were in vain."),
                    new TimedTransition(3500, "Greet 2")
                    ),
                new State("Greet 2",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "The perfect beauty of crystals is sought by all, it seems. How dare you consider yourselves more worthy than us?!"),
                    new TimedTransition(3000, "P1")
                    ),
                // a guardian, the Lizard or the Cyclops, while it rings the chamber with prisms and
                // fires a fractal its spinning arrow points out first
                Guardian("P1", "P2", null, "Crystallised Lizard", "cc",
                    new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 1, fixedAngle: 0, rotateAngle: 9, coolDown: 2000),
                    new Shoot(0, count: 1, projectileIndex: 9, fixedAngle: 0, rotateAngle: 45, coolDown: 600),
                    new Shoot(14, count: 3, shootAngle: 12, projectileIndex: 3, coolDown: 3000, coolDownOffset: 600)
                    ),
                // vulnerable: Pulse Locks and wavy rings of lightning, to 75%
                Fight("P2", "P3", 0.75, "You have forced our hand...", "Pulse Lock", 2600,
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 2, fixedAngle: 0, rotateAngle: 11, coolDown: 1800),
                    new Shoot(14, count: 1, projectileIndex: 3, predictive: 0.4, coolDown: 900)
                    ),
                Guardian("P3", "P4", "A guardian shall be prepared to cleanse this muck.", "newCs", "Crystallised Fish",
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 22.5, rotateAngle: -9, coolDown: 2000),
                    new Shoot(14, count: 3, shootAngle: 12, projectileIndex: 3, coolDown: 2600)
                    ),
                // faster Pulse Locks and a three-way shotgun of purple shards, to 50%
                Fight("P4", "P5", 0.5, "We have ascended!", "Pulse Lock P2", 1900,
                    new Shoot(14, count: 3, shootAngle: 16, projectileIndex: 3, coolDown: 800),
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 2, fixedAngle: 15, rotateAngle: -11, coolDown: 2200),
                    new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 6, fixedAngle: 0, rotateAngle: 13, coolDown: 1200)
                    ),
                Guardian("P5", "P6", "Your very presence tarnishes this chamber!", "Crystallised Monstrosity", null,
                    new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 1, fixedAngle: 0, rotateAngle: 9, coolDown: 1800),
                    new Shoot(14, count: 3, shootAngle: 16, projectileIndex: 3, coolDown: 2000)
                    ),
                // the end: it grows and armours itself; the fastest Pulse Locks, buds and flowers of
                // crystal, and a barrage of prisms in every direction
                new State("P6",
                    new State("P6 Start",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "You cannot...shatter...PERFECTION!"),
                        new Flash(0xFFFFFF, 0.3, 8),
                        new ChangeSize(20, 160),
                        new TimedTransition(2500, "P6 Fight")
                        ),
                    new State("P6 Fight",
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new TossObject("Pulse Lock P3", minRange: 4, maxRange: 11, minAngle: 0, maxAngle: 360, coolDown: 1400),
                        new TossObject("Crystal Bud 1", minRange: 3, maxRange: 10, minAngle: 0, maxAngle: 360, coolDown: 2500, densityRange: 12, maxDensity: 5),
                        new TossObject("Crystal Flower", minRange: 5, maxRange: 10, minAngle: 0, maxAngle: 360, coolDown: 6000, densityRange: 12, maxDensity: 3),
                        new Shoot(0, count: 16, shootAngle: 22.5, projectileIndex: 1, fixedAngle: 0, rotateAngle: 7, coolDown: 1500),
                        new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 7, fixedAngle: 0, rotateAngle: 17, coolDown: 900),
                        new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 6, fixedAngle: 30, rotateAngle: -17, coolDown: 900, coolDownOffset: 450),
                        new Shoot(14, count: 3, shootAngle: 16, projectileIndex: 3, coolDown: 1100)
                        )
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Greater Potion of Life", 1),
                new ItemLoot("Greater Potion of Mana", 1),
                new ItemLoot("Greater Potion of Wisdom", 0.5),
                new ItemLoot("Potion of Life", 0.5),
                new ItemLoot("Crystallised Fang's Venom", 0.01),
                new ItemLoot("Crystallised Mist", 0.01),
                new ItemLoot("Mark of the Crystal Entity", 0.02),
                new ItemLoot("Crystal Cave Key", 0.1, 0, 0.03),
                new TierLoot(12, ItemType.Weapon, 0.2),
                new TierLoot(13, ItemType.Weapon, 0.08),
                new TierLoot(13, ItemType.Armor, 0.2),
                new TierLoot(14, ItemType.Armor, 0.08),
                new TierLoot(6, ItemType.Ability, 0.12),
                new TierLoot(6, ItemType.Ring, 0.12)
                )
            )
        // its guardians: each fights alone while the Entity can't be hurt
        .Init("Crystallised Lizard",
            new State(
                new ScaleHP2(20),
                new Prioritize(
                    new Follow(0.6, 16, 4),
                    new Wander(0.4)
                    ),
                new Shoot(14, count: 5, shootAngle: 12, projectileIndex: 0, coolDown: 1400),
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, rotateAngle: 15, coolDown: 3000)
                ),
            GuardianLoot()
            )
        .Init("cc",
            new State(
                new ScaleHP2(20),
                new Prioritize(
                    new Follow(0.4, 16, 3),
                    new Wander(0.3)
                    ),
                new Shoot(14, count: 1, projectileIndex: 1, predictive: 0.5, coolDown: 900),
                new Shoot(14, count: 3, shootAngle: 20, projectileIndex: 2, coolDown: 1600),
                // a storm of shards in lines, from slow to fast
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 3, fixedAngle: 0, coolDown: 4000),
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 4, fixedAngle: 0, coolDown: 4000),
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 5, fixedAngle: 0, coolDown: 4000),
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 6, fixedAngle: 0, coolDown: 4000),
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 7, fixedAngle: 0, coolDown: 4000),
                new Shoot(14, count: 2, shootAngle: 30, projectileIndex: 8, coolDown: 2200)
                ),
            GuardianLoot()
            )
        .Init("newCs",
            new State(
                new ScaleHP2(20),
                new Prioritize(
                    new Charge(1.6, 12, coolDown: 3000),
                    new Wander(0.5)
                    ),
                new Shoot(12, count: 3, shootAngle: 18, projectileIndex: 0, coolDown: 1200),
                new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, rotateAngle: 20, coolDown: 2500)
                ),
            GuardianLoot()
            )
        .Init("Crystallised Fish",
            new State(
                new ScaleHP2(20),
                new Prioritize(
                    new Orbit(0.9, 9, 30, "mc"),
                    new Wander(0.5)
                    ),
                new Shoot(12, count: 1, projectileIndex: 0, coolDown: 500),
                // shards that turn back, at three speeds
                new Shoot(12, count: 2, shootAngle: 20, projectileIndex: 1, coolDown: 2500),
                new Shoot(12, count: 2, shootAngle: 40, projectileIndex: 2, coolDown: 2500),
                new Shoot(12, count: 2, shootAngle: 60, projectileIndex: 3, coolDown: 2500),
                new Shoot(12, count: 5, shootAngle: 10, projectileIndex: 4, coolDown: 1800, coolDownOffset: 900)
                ),
            GuardianLoot()
            )
        .Init("Crystallised Monstrosity",
            new State(
                new ScaleHP2(20),
                new Prioritize(
                    new Follow(0.5, 16, 3),
                    new Wander(0.4)
                    ),
                new Shoot(14, count: 3, shootAngle: 25, projectileIndex: 0, coolDown: 2000),
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 22.5, rotateAngle: 11, coolDown: 3500)
                ),
            GuardianLoot()
            )
        // the Pulse Locks: each locks on to a player (its first shot does nothing but aim), then
        // fires at them; the later ones' shots are faster
        .Init("Pulse Lock", PulseLock(1100))
        .Init("Pulse Lock P2", PulseLock(800))
        .Init("Pulse Lock P3", PulseLock(600))
        // buds burst into rings of shards; flowers slow whoever stands by them
        .Init("Crystal Bud 1",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new Shoot(1.5, count: 1, projectileIndex: 0, coolDown: 400),
                new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, rotateAngle: 22.5, coolDown: 1800, coolDownOffset: 800),
                new Decay(9000)
                )
            )
        .Init("Crystal Flower",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new Shoot(3, count: 1, projectileIndex: 1, coolDown: 500),
                new Decay(12000)
                )
            )
        #endregion
        ;

        // how many Cavern Crystals power the Crystal Cavern's drill (RealmEye)
        private const int CavernCrystals = 20;

        private static State CaveBat(params IStateChildren[] shots)
        {
            var children = new List<IStateChildren>
            {
                new Prioritize(
                    new Follow(1.1, 10, 2),
                    new Wander(0.6)
                    ),
            };
            children.AddRange(shots);
            return new State(children.ToArray());
        }

        private static State CaveSlime() =>
            new State(
                new Prioritize(
                    new Follow(0.4, 8, 1),
                    new Wander(0.3)
                    ),
                new Shoot(7, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 1600)
                );

        private static Threshold CaveLoot() =>
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 0.05),
                new ItemLoot("Potion of Wisdom", 0.05)
                );

        private static Threshold GuardianLoot() =>
            new Threshold(0.01,
                new ItemLoot("Potion of Life", 0.2),
                new ItemLoot("Potion of Mana", 0.2),
                new ItemLoot("Greater Potion of Defense", 0.3),
                new TierLoot(12, ItemType.Weapon, 0.1),
                new TierLoot(12, ItemType.Armor, 0.1)
                );

        // a phase the Crystal Entity can't be hurt in: it sends one of two guardians (at random) and
        // fights alongside it until the guardian falls
        private static State Guardian(string name, string next, string taunt, string first, string second, params IStateChildren[] attacks)
        {
            var start = new List<IStateChildren>
            {
                new Flash(0x9FE8FF, 0.3, 6),
            };
            if (taunt != null)
                start.Add(new Taunt(true, taunt));
            start.Add(second == null
                ? new TimedTransition(2000, $"{name} {first}")
                : new TimedRandomTransition(2000, false, $"{name} {first}", $"{name} {second}"));

            State Summon(string guardian) =>
                new State($"{name} {guardian}",
                    new TossObject(guardian, 6, 270, coolDown: 999999),
                    new TimedTransition(1500, $"{name} Fight")
                    );

            var fight = new List<IStateChildren>
            {
                new EntitiesNotExistsTransition(40, next, first, second ?? first),
            };
            fight.AddRange(attacks);

            var states = new List<IStateChildren>
            {
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new State($"{name} Start", start.ToArray()),
                Summon(first),
            };
            if (second != null)
                states.Add(Summon(second));
            states.Add(new State($"{name} Fight", fight.ToArray()));
            return new State(name, states.ToArray());
        }

        // a phase it can be hurt in, with Pulse Locks every `lockEvery` ms
        private static State Fight(string name, string next, double hpTo, string taunt, string pulseLock, int lockEvery, params IStateChildren[] attacks)
        {
            var fight = new List<IStateChildren>
            {
                new TossObject(pulseLock, minRange: 4, maxRange: 11, minAngle: 0, maxAngle: 360, coolDown: lockEvery),
            };
            fight.AddRange(attacks);
            return new State(name,
                new HpLessTransition(hpTo, next),
                new State($"{name} Start",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, taunt),
                    new TimedTransition(1500, $"{name} Fight")
                    ),
                new State($"{name} Fight", fight.ToArray())
                );
        }

        private static State PulseLock(int coolDown) =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("Aim",
                    new Shoot(20, count: 1, projectileIndex: 0, coolDown: 9999),
                    new TimedTransition(900, "Fire")
                    ),
                new State("Fire",
                    new Shoot(20, count: 3, shootAngle: 12, projectileIndex: 1, coolDown: coolDown),
                    new TimedTransition(4000, "Gone")
                    ),
                new State("Gone",
                    new Decay(0)
                    )
                );
    }
}
