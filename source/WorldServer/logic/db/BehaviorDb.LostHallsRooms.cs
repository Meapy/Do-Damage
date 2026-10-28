using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Lost Halls' rooms (tools/build_lost_halls.py places them; the world's RandomLayout joins
    // them anew each visit) and the Colossus room's controller. The Marble Colossus's own fight is
    // in BehaviorDb.LostHalls.cs; the Cultist Hideout and the Void are worlds of their own, entered
    // through the portals this controller leaves.
    partial class BehaviorDb
    {
        // what a room's lesser monsters drop: the halls' tier, a little under the Colossus's
        private static MobDrops[] HallsMinion() => new MobDrops[]
        {
            new TierLoot(11, ItemType.Weapon, 0.04),
            new TierLoot(12, ItemType.Weapon, 0.02),
            new TierLoot(12, ItemType.Armor, 0.04),
            new TierLoot(13, ItemType.Armor, 0.015),
            new TierLoot(5, ItemType.Ability, 0.03),
            new TierLoot(5, ItemType.Ring, 0.03),
            new ItemLoot("Potion of Defense", 0.03),
            new ItemLoot("Potion of Attack", 0.03)
        };

        // a room's leader (the Commander, the Champion, the Grotto Blob, the Tormented Golem...)
        private static MobDrops[] HallsLeader(params MobDrops[] extra) => new MobDrops[]
        {
            new Threshold(0.03,
                new TierLoot(12, ItemType.Weapon, 0.1),
                new TierLoot(13, ItemType.Weapon, 0.04),
                new TierLoot(13, ItemType.Armor, 0.08),
                new TierLoot(14, ItemType.Armor, 0.02),
                new TierLoot(5, ItemType.Ability, 0.1),
                new TierLoot(6, ItemType.Ability, 0.03),
                new TierLoot(6, ItemType.Ring, 0.04),
                new ItemLoot("Potion of Attack", 0.25),
                new ItemLoot("Potion of Defense", 0.25),
                new ItemLoot("Potion of Speed", 0.15),
                new ItemLoot("Potion of Dexterity", 0.15),
                new ItemLoot("Potion of Vitality", 0.15),
                new ItemLoot("Potion of Wisdom", 0.15)
            )
        }.Concat(extra).ToArray();

        private _ LostHallsRooms = () => Behav()

            // --- the Colossus room -----------------------------------------------------------
            // Its four Marble Defenders wake when a player comes in. Once they have fallen the
            // Marble Colossus rises, or, one time in four, the way to the Cultist Hideout opens
            // instead (as in the original, where the Hideout takes the Colossus's place). When the
            // Colossus is dead its remains are left torn open into the Void: a Vial of Pure
            // Darkness (Malus drops them) used there opens the Void's portal.
            .Init("LH Monster Spawner",
                new State(
                    new State("Wait",
                        new PlayerWithinTransition(10, "Wake")
                    ),
                    new State("Wake",
                        new Order(12, "LH Marble Defender Activator N", "Wake"),
                        new Order(12, "LH Marble Defender Activator E", "Wake"),
                        new Order(12, "LH Marble Defender Activator S", "Wake"),
                        new Order(12, "LH Marble Defender Activator W", "Wake"),
                        new TimedTransition(3000, "Defend")
                    ),
                    new State("Defend",
                        new EntitiesNotExistsTransition(20, "Roll", "LH Marble Defender",
                            "LH Marble Defender Activator N", "LH Marble Defender Activator E",
                            "LH Marble Defender Activator S", "LH Marble Defender Activator W")
                    ),
                    // at once: World.BossDied opens a Realm Portal 2 seconds after the last
                    // Defender (a quest monster) unless another boss is here by then
                    new State("Roll",
                        new TimedRandomTransition(0, false, "Colossus", "Colossus", "Colossus", "Cult")
                    ),
                    new State("Colossus",
                        new Spawn("LH Marble Colossus", 1, 1, givesNoXp: false),
                        new TimedTransition(3000, "Watch")
                    ),
                    new State("Watch",
                        new EntityNotExistsTransition("LH Marble Colossus", 40, "Rift")
                    ),
                    new State("Rift",
                        new Spawn("LH Vial Effect", 1, 1),
                        new TimedTransition(1000, "Done")
                    ),
                    new State("Cult",
                        new Taunt(true, "The Colossus does not stir... a hidden passage grinds open beneath the halls."),
                        new Spawn("LH Cult Portal", 1, 1),
                        new TimedTransition(1000, "Done")
                    ),
                    new State("Done")
                )
            )
            // the torn place where the Colossus fell: a Vial of Pure Darkness used within 25
            // tiles turns it into the Void's portal (the vial's ChangeObject); it closes after 5
            // minutes
            .Init("LH Vial Effect",
                new State(
                    new State("Open",
                        new Taunt(true, "The Colossus's remains tear open into the Void. A Vial of Pure Darkness would open the way."),
                        new TimedTransition(240000, "Closing")
                    ),
                    new State("Closing",
                        new Taunt(true, "The tear into the Void is closing."),
                        new Decay(60000)
                    )
                )
            )
            .Init("LH Marble Defender Activator N", DefenderStatue())
            .Init("LH Marble Defender Activator E", DefenderStatue())
            .Init("LH Marble Defender Activator S", DefenderStatue())
            .Init("LH Marble Defender Activator W", DefenderStatue())
            .Init("LH Marble Defender",
                new State(
                    new ScaleHP2(20),
                    new State("Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xffffff, 0.3, 6),
                        new Taunt(0.5, "Intruders in the halls!", "None shall disturb the Colossus."),
                        new TimedTransition(2000, "Guard")
                    ),
                    new State("Guard",
                        new StayCloseToSpawn(0.6, 9),
                        new Follow(0.55, 12, 3),
                        new Shoot(12, 3, 16, 0, predictive: 0.4, coolDown: 1800),
                        new Shoot(12, 12, 30, 1, coolDown: 4200, coolDownOffset: 1200),
                        new HpLessTransition(0.4, "Fury")
                    ),
                    new State("Fury",
                        new StayCloseToSpawn(0.8, 9),
                        new Follow(0.8, 12, 2),
                        new Shoot(12, 5, 14, 0, predictive: 0.4, coolDown: 1400),
                        new Shoot(12, 12, 30, 1, rotateAngle: 15, coolDown: 3000)
                    )
                ),
                new Threshold(0.03,
                    new TierLoot(13, ItemType.Weapon, 0.05),
                    new TierLoot(13, ItemType.Armor, 0.05),
                    new TierLoot(6, ItemType.Ability, 0.04),
                    new TierLoot(6, ItemType.Ring, 0.04),
                    new ItemLoot("Potion of Defense", 0.5),
                    new ItemLoot("Potion of Vitality", 0.3)
                )
            )

            // --- the Crusade -----------------------------------------------------------------
            .Init("LH Crusade Soldier",
                new State(
                    new Prioritize(
                        new Follow(0.6, 9, 2),
                        new Wander(0.3)
                    ),
                    new Shoot(8, 3, 12, 0, predictive: 0.3, coolDown: 1300)
                ),
                HallsMinion()
            )
            .Init("LH Crusade Shipwright",
                new State(
                    new Prioritize(
                        new StayBack(0.5, 5),
                        new Wander(0.3)
                    ),
                    new Shoot(10, 2, 8, 0, predictive: 0.5, coolDown: 1100)
                ),
                HallsMinion()
            )
            .Init("LH Crusade Explorer",
                new State(
                    new Prioritize(
                        new Follow(0.7, 10, 4),
                        new Wander(0.4)
                    ),
                    new Shoot(10, 1, projectileIndex: 0, coolDown: 900),
                    new Shoot(10, 6, 60, 0, coolDown: 3000, coolDownOffset: 500)
                ),
                HallsMinion()
            )
            .Init("LH Commander of the Crusade",
                new State(
                    new ScaleHP2(15),
                    new State("Wait",
                        new PlayerWithinTransition(9, "Fight")
                    ),
                    new State("Fight",
                        new Taunt(true, "For the Crusade! Drive them from the halls!"),
                        new Prioritize(
                            new StayCloseToSpawn(0.5, 6),
                            new Follow(0.5, 10, 3)
                        ),
                        new Shoot(10, 5, 10, 0, predictive: 0.3, coolDown: 1600),
                        new Shoot(10, 10, 36, 0, coolDown: 3600, coolDownOffset: 800),
                        new HpLessTransition(0.5, "Rally")
                    ),
                    new State("Rally",
                        new Taunt("Soldiers, to me!"),
                        new Prioritize(
                            new StayCloseToSpawn(0.6, 6),
                            new Follow(0.6, 10, 2)
                        ),
                        new Shoot(10, 7, 9, 0, predictive: 0.3, coolDown: 1300),
                        new Shoot(10, 12, 30, 0, rotateAngle: 15, coolDown: 2600)
                    )
                ),
                HallsLeader()
            )

            // --- Oryx's army -----------------------------------------------------------------
            .Init("LH Oryx Swordsman",
                new State(
                    new Prioritize(
                        new Charge(1.8, 8, 3500),
                        new Follow(0.7, 9, 1),
                        new Wander(0.3)
                    ),
                    new Shoot(4, 3, 25, 0, coolDown: 900)
                ),
                HallsMinion()
            )
            .Init("LH Oryx Armorbearer",
                new State(
                    new Prioritize(
                        new Protect(0.6, "LH Champion of Oryx", 12, 5, 2),
                        new Follow(0.5, 8, 2),
                        new Wander(0.3)
                    ),
                    new Shoot(5, 5, 18, 0, coolDown: 1500)
                ),
                HallsMinion()
            )
            .Init("LH Oryx Admiral",
                new State(
                    new Prioritize(
                        new StayBack(0.5, 6),
                        new Wander(0.3)
                    ),
                    new Shoot(9, 1, projectileIndex: 0, predictive: 0.4, coolDown: 2200),
                    new Shoot(10, 3, 10, 1, coolDown: 1200, coolDownOffset: 600)
                ),
                HallsMinion()
            )
            .Init("LH Champion of Oryx",
                new State(
                    new ScaleHP2(15),
                    new State("Wait",
                        new PlayerWithinTransition(9, "Fight")
                    ),
                    new State("Fight",
                        new Taunt(true, "Oryx's champion does not yield!"),
                        new Prioritize(
                            new StayCloseToSpawn(0.6, 6),
                            new Follow(0.7, 10, 2)
                        ),
                        new Shoot(8, 3, 15, 0, predictive: 0.3, coolDown: 1000),
                        new Shoot(8, 8, 45, 0, coolDown: 2800, coolDownOffset: 1400),
                        new HpLessTransition(0.4, "Frenzy")
                    ),
                    new State("Frenzy",
                        new Taunt("You will kneel before Oryx!"),
                        new Prioritize(
                            new StayCloseToSpawn(0.9, 6),
                            new Charge(2, 9, 2500),
                            new Follow(0.9, 10, 1)
                        ),
                        new Shoot(8, 5, 12, 0, predictive: 0.3, coolDown: 800),
                        new Shoot(8, 12, 30, 0, rotateAngle: 10, coolDown: 2200)
                    )
                ),
                HallsLeader()
            )

            // --- the Grotto ------------------------------------------------------------------
            // the bats and rats leave their teeth where they bite: still shots that stay a while
            .Init("LH Grotto Bat",
                new State(
                    new Prioritize(
                        new Swirl(1.2, 3, 10),
                        new Wander(0.6)
                    ),
                    new Shoot(3, 1, projectileIndex: 0, coolDown: 1200)
                ),
                HallsMinion()
            )
            .Init("LH Grotto Rat",
                new State(
                    new Prioritize(
                        new Follow(1.1, 10, 0.5),
                        new Wander(0.6)
                    ),
                    new Shoot(3, 1, projectileIndex: 0, coolDown: 1400)
                ),
                HallsMinion()
            )
            .Init("LH Grotto Slime",
                new State(
                    new Prioritize(
                        new Follow(0.4, 8, 2),
                        new Wander(0.3)
                    ),
                    new Shoot(6, 8, 45, 0, coolDown: 1800)
                ),
                HallsMinion()
            )
            .Init("LH Grotto Blob",
                new State(
                    new ScaleHP2(15),
                    new State("Wait",
                        new PlayerWithinTransition(9, "Fight")
                    ),
                    new State("Fight",
                        new Prioritize(
                            new StayCloseToSpawn(0.4, 5),
                            new Follow(0.4, 10, 3)
                        ),
                        new Shoot(10, 10, 36, 0, rotateAngle: 12, coolDown: 1100),
                        new Spawn("LH Grotto Slime", 3, 0, 9000),
                        new HpLessTransition(0.35, "Burst")
                    ),
                    new State("Burst",
                        new Flash(0x40ff40, 0.3, 4),
                        new Prioritize(
                            new StayCloseToSpawn(0.6, 5),
                            new Follow(0.6, 10, 2)
                        ),
                        new Shoot(10, 14, 25.7, 0, rotateAngle: 9, coolDown: 800),
                        new Spawn("LH Grotto Slime", 4, 0, 6000)
                    )
                ),
                HallsLeader()
            )

            // --- the Golems ------------------------------------------------------------------
            // the Tormented Golem can't be hurt while another golem of its room still stands
            .Init("LH Golem of Anger",
                new State(
                    new Prioritize(
                        new Follow(0.5, 9, 3),
                        new Wander(0.3)
                    ),
                    new Shoot(8, 3, 14, 0, predictive: 0.3, coolDown: 1000),
                    new Shoot(10, 1, projectileIndex: 1, predictive: 0.6, coolDown: 2400, coolDownOffset: 1200)
                ),
                HallsMinion()
            )
            .Init("LH Golem of Fear",
                new State(
                    new Prioritize(
                        new StayBack(0.5, 6),
                        new Wander(0.3)
                    ),
                    new Shoot(12, 1, projectileIndex: 0, predictive: 0.8, coolDown: 1400),
                    new Shoot(8, 5, 20, 1, coolDown: 3600, coolDownOffset: 1800)
                ),
                HallsMinion()
            )
            .Init("LH Golem of Sorrow",
                new State(
                    new Prioritize(
                        new Follow(0.4, 9, 4),
                        new Wander(0.3)
                    ),
                    new Shoot(9, 2, 20, 0, coolDown: 1200),
                    new Shoot(9, 6, 60, 1, coolDown: 2600, coolDownOffset: 1300)
                ),
                HallsMinion()
            )
            .Init("LH Tormented Golem",
                new State(
                    new ScaleHP2(15),
                    new State("Bound",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(10, 4, 90, 0, rotateAngle: 22.5, coolDown: 1600),
                        new EntitiesNotExistsTransition(14, "Free", "LH Golem of Anger", "LH Golem of Fear", "LH Golem of Sorrow")
                    ),
                    new State("Free",
                        new Taunt("Release... me..."),
                        new Prioritize(
                            new StayCloseToSpawn(0.5, 6),
                            new Follow(0.5, 10, 2)
                        ),
                        new Shoot(10, 8, 45, 0, rotateAngle: 22.5, coolDown: 1200),
                        new Shoot(10, 3, 12, 0, predictive: 0.4, coolDown: 1800, coolDownOffset: 600)
                    )
                ),
                HallsLeader(
                    new Threshold(0.03,
                        new ItemLoot("TricksterST0", 0.01),
                        new ItemLoot("TricksterST1", 0.01),
                        new ItemLoot("TricksterST2", 0.01),
                        new ItemLoot("TricksterST3", 0.01)
                    )
                )
            )

            // --- the Ghost Bridges -----------------------------------------------------------
            // spectral sentries float over the pit, out of reach: run the cross between them
            .Init("LH Spectral Sentry S",
                new State(
                    new BackAndForth(0.5, 5),
                    new Shoot(14, 3, 120, 0, rotateAngle: 17, coolDown: 900),
                    new Shoot(14, 2, 180, 1, rotateAngle: -11, coolDown: 1300, coolDownOffset: 400),
                    new Shoot(14, 1, projectileIndex: 2, predictive: 0.5, coolDown: 2200, coolDownOffset: 900)
                )
            )
            .Init("LH Spectral Sentry E",
                new State(
                    new BackAndForth(0.5, 5),
                    new Shoot(14, 3, 120, 0, rotateAngle: -17, coolDown: 900),
                    new Shoot(14, 2, 180, 1, rotateAngle: 11, coolDown: 1300, coolDownOffset: 400),
                    new Shoot(14, 1, projectileIndex: 2, predictive: 0.5, coolDown: 2200, coolDownOffset: 900)
                )
            )

            // --- the Agonized Titan ----------------------------------------------------------
            // fires every cultist's curse at once round itself, then a slow, heavy bullet
            .Init("LH Agonized Titan",
                new State(
                    new ScaleHP2(20),
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(8, "Wake")
                    ),
                    new State("Wake",
                        new Taunt(true, "The agony... it never ends..."),
                        new Flash(0xff0000, 0.4, 5),
                        new TimedTransition(2000, "Curses")
                    ),
                    new State("Curses",
                        new StayCloseToSpawn(0.3, 3),
                        new Shoot(12, 1, projectileIndex: 0, predictive: 0.6, coolDown: 2400),
                        new Shoot(12, 4, 90, 1, rotateAngle: 10, coolDown: 1400),
                        new Shoot(12, 4, 90, 3, rotateAngle: 10, angleOffset: 45, coolDown: 1400, coolDownOffset: 700),
                        new Shoot(12, 6, 60, 5, coolDown: 3000, coolDownOffset: 1500),
                        new HpLessTransition(0.5, "Torment")
                    ),
                    new State("Torment",
                        new Taunt("Make it stop!"),
                        new StayCloseToSpawn(0.5, 4),
                        new Wander(0.3),
                        new Shoot(12, 2, 15, 0, predictive: 0.6, coolDown: 1800),
                        new Shoot(12, 6, 60, 2, rotateAngle: 14, coolDown: 1200),
                        new Shoot(12, 6, 60, 4, rotateAngle: 14, angleOffset: 30, coolDown: 1200, coolDownOffset: 600),
                        new Shoot(12, 8, 45, 6, coolDown: 2600),
                        new Shoot(12, 8, 45, 7, angleOffset: 22.5, coolDown: 2600, coolDownOffset: 1300)
                    )
                ),
                HallsLeader(
                    new Threshold(0.03,
                        new ItemLoot("Potion of Life", 0.1),
                        new ItemLoot("Potion of Mana", 0.1)
                    )
                )
            )

            // --- the Void rifts --------------------------------------------------------------
            .Init("LH Void Fragment",
                new State(
                    new Prioritize(
                        new Follow(0.8, 9, 1),
                        new Wander(0.5)
                    ),
                    new Shoot(5, 4, 90, 0, rotateAngle: 20, coolDown: 900)
                ),
                HallsMinion()
            )
            .Init("LH Void Shade",
                new State(
                    new Prioritize(
                        new Orbit(0.6, 4, 10, "LH Greater Void Shade"),
                        new Follow(0.6, 9, 3),
                        new Wander(0.4)
                    ),
                    new Shoot(8, 3, 15, 0, predictive: 0.3, coolDown: 1000)
                ),
                HallsMinion()
            )
            .Init("LH Greater Void Shade",
                new State(
                    new ScaleHP2(15),
                    new Prioritize(
                        new StayCloseToSpawn(0.5, 5),
                        new Follow(0.5, 10, 3)
                    ),
                    new Shoot(10, 8, 45, 0, rotateAngle: 11, coolDown: 1600),
                    new Shoot(10, 2, 10, 0, predictive: 0.5, coolDown: 900, coolDownOffset: 450),
                    new Spawn("LH Void Fragment", 3, 0, 8000)
                ),
                HallsLeader()
            )

            // --- the trap halls --------------------------------------------------------------
            // a pressure plate: a player stepping close sets off a blast round it 1.5 seconds later
            .Init("LH Grenade Trap",
                new State(
                    new State("Armed",
                        new PlayerWithinTransition(1.5, "Click")
                    ),
                    new State("Click",
                        new Flash(0xff4000, 0.15, 6),
                        new Grenade(2.5, 140, 0, fixedAngle: 0, coolDown: 10000),
                        new TimedTransition(3500, "Armed")
                    )
                )
            )
            // arrow slits along the walls, each firing across the room while a player is near
            .Init("LH Wall Trap N", ArrowTrap(90))
            .Init("LH Wall Trap S", ArrowTrap(270))
            .Init("LH Wall Trap W", ArrowTrap(0))
            .Init("LH Wall Trap E", ArrowTrap(180))

            // --- the pot room ----------------------------------------------------------------
            // seven treasure pots: break them for their loot, but one in three holds an Evil Spirit
            .Init("LH Pot 1", TreasurePot(), PotLoot())
            .Init("LH Pot 2", TreasurePot(), PotLoot())
            .Init("LH Pot 3", TreasurePot(), PotLoot())
            .Init("LH Pot 4", TreasurePot(), PotLoot())
            .Init("LH Pot 5", TreasurePot(), PotLoot())
            .Init("LH Pot 6", TreasurePot(), PotLoot())
            .Init("LH Pot 7", TreasurePot(), PotLoot())
            .Init("LH Evil Spirit 1",
                new State(
                    new Prioritize(
                        new Follow(1.1, 12, 1),
                        new Wander(0.6)
                    ),
                    new Shoot(5, 3, 20, 0, coolDown: 700)
                ),
                new ItemLoot("Potion of Wisdom", 0.15),
                new ItemLoot("Potion of Vitality", 0.15)
            )
        ;

        private static State DefenderStatue() =>
            new State(
                new State("Idle"),
                new State("Wake",
                    new Flash(0xffffff, 0.3, 6),
                    new Spawn("LH Marble Defender", 1, 1, givesNoXp: false),
                    new Decay(500)
                )
            );

        private static State ArrowTrap(double angle) =>
            new State(
                new State("Idle",
                    new PlayerWithinTransition(9, "Fire")
                ),
                new State("Fire",
                    new Shoot(20, 1, fixedAngle: angle, coolDown: 1800),
                    new NoPlayerWithinTransition(11, "Idle")
                )
            );

        private static State TreasurePot() =>
            new State(
                new TransformOnDeath("LH Evil Spirit 1", 1, 1, 0.33)
            );

        private static MobDrops[] PotLoot() => new MobDrops[]
        {
            new ItemLoot("Potion of Attack", 0.12),
            new ItemLoot("Potion of Defense", 0.12),
            new ItemLoot("Potion of Speed", 0.12),
            new ItemLoot("Potion of Dexterity", 0.12),
            new ItemLoot("Potion of Vitality", 0.12),
            new ItemLoot("Potion of Wisdom", 0.12),
            new ItemLoot("Potion of Life", 0.03),
            new ItemLoot("Potion of Mana", 0.03),
            new TierLoot(6, ItemType.Ring, 0.02),
            new ItemLoot("TricksterST0", 0.003),
            new ItemLoot("TricksterST1", 0.003),
            new ItemLoot("TricksterST2", 0.003),
            new ItemLoot("TricksterST3", 0.003)
        };
    }
}
