using System.Collections.Generic;
using System.Linq;
using Shared.crucible;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.behaviors.doomed;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Ice Tomb (tools/build_ice_tomb.py, logic/behaviors/IceTomb.cs and IceCourt.cs): a tomb of
    // the frozen dead, laid out anew every visit. Its rooms change round those who walk them: the
    // sarcophagi crack open as someone comes near, the lake's ice breaks under whoever stands still,
    // a bridge of ice freezes across each chasm. Three seal rooms each hold a Seal of Winter, warded
    // while its keeper lives; each seal broken lights a lantern at the Sealed Door, and with all
    // three the door's wall opens and the Frozen Court assembles out of the dark beyond it.
    //
    // The Court: Kaldrun, the Rime Warden; Ysolde, the Aurora Oracle; Hrimgar, the Frost Reaver.
    // They wake together when the Heart of Winter in the middle is broken, and fight together, as the
    // Tomb of the Ancients' three do. The invisible Court Heart in the middle turns the floor (the
    // Crucible's moving floors, CrucibleFloor), one phase at a time with a calm between, each phase
    // one of the living's own: Kaldrun's Avalanche, Ysolde's Aurora Veil and Blizzard, Hrimgar's Frost
    // Fissures and Hailstorm (a fallen one's phases leave the turn). Each announces itself across
    // everyone's screen (EncounterCue). The Heart also drops the Crucible's caches beside the players
    // (frost, storm, hex and rending: CrucibleCaches), each launching its charge at the nearest of the
    // Court. Each of them has a turn of its own as its health falls (Kaldrun's Glacial Bulwark at
    // 60%, Ysolde's Aurora's Grace at 50%, Hrimgar's Frenzy at 50%); one that falls leaves its shade
    // circling the hall; and the last of them standing, below 15%, makes its last stand, the whole
    // floor turned against the players (Permafrost, Polar Night, the Shattered Lake).
    partial class BehaviorDb
    {
        private const double CourtSight = 28;
        // the caches the Court's hall drops (CrucibleCaches): the tomb's frost, storm, hex and rending
        private static readonly string[] TombCaches = { "Frost Cask", "Stormglass Jar", "Hex Urn", "Rending Crate" };
        // one to a line: the wiki (tools/wiki_data.py) reads them
        private const string Kaldrun = "Kaldrun, the Rime Warden";
        private const string Ysolde = "Ysolde, the Aurora Oracle";
        private const string Hrimgar = "Hrimgar, the Frost Reaver";
        private const uint FrostGlow = 0xFF9FE8FF, AuroraGlow = 0xFF4AE8A4, SteelGlow = 0xFF9AB4D4, VioletGlow = 0xFF9060D8;
        // the Tomb of the Ancients' blue shield, as the Court turns
        private const uint BlueShield = 0xFF4A7CFF;

        // The tomb's treasure, once the old Ice Tomb Chest's: each of the Court drops it at half the
        // chest's chances (the tomb is longer and its three harder than the old chest's were).
        private static MobDrops IceTombTreasure() =>
            new Threshold(.01,
                new ItemLoot("Ring of the Northern Light", 0.01),
                new ItemLoot("Enchanted Ice Shard", 0.01),
                new ItemLoot("Frimarra", 0.01),
                new ItemLoot("Freezing Quiver", 0.0025),
                new ItemLoot("Arctic Bow", 0.0025),
                new ItemLoot("Bow of Eternal Frost", 0.0025),
                new ItemLoot("Frosty's Walking Stick", 0.0025),
                new ItemLoot("Staff of Iceblast", 0.0025),
                new ItemLoot("Eternal Snowflake Wand", 0.0025),
                new ItemLoot("Frozen Wand", 0.0025),
                new ItemLoot("Platinum Sword", 0.0025),
                new ItemLoot("Snowblind Wand", 0.0025),
                new ItemLoot("Tome of Frigid Protection", 0.0025),
                new ItemLoot("Edict of the Frozen Court", 0.003),
                new ItemLoot("Rimejaw", 0.003),
                new ItemLoot("Ice Crown", 0.0012),
                new ItemLoot("Potion of Life", 0.2),
                new ItemLoot("Potion of Mana", 0.3),
                new ItemLoot("Potion of Defense", 1),
                new ItemLoot("Potion of Vitality", 0.5),
                new ItemLoot("Potion of Wisdom", 0.5),
                new TierLoot(12, ItemType.Weapon, 0.12),
                new TierLoot(13, ItemType.Armor, 0.12),
                new TierLoot(6, ItemType.Ability, 0.1),
                new TierLoot(6, ItemType.Ring, 0.08)
            );

        private static MobDrops KeeperLoot() =>
            new Threshold(.01,
                new ItemLoot("Potion of Defense", 0.5),
                new ItemLoot("Potion of Vitality", 0.5),
                new ItemLoot("Potion of Life", 0.05),
                new TierLoot(11, ItemType.Weapon, 0.15),
                new TierLoot(12, ItemType.Armor, 0.15),
                new TierLoot(5, ItemType.Ring, 0.1)
            );

        private static MobDrops TombMinionLoot() =>
            new Threshold(.1,
                new TierLoot(9, ItemType.Weapon, 0.04),
                new TierLoot(9, ItemType.Armor, 0.04),
                new ItemLoot("Potion of Defense", 0.01),
                new ItemLoot("Potion of Vitality", 0.01)
            );

        private _ FrozenTomb = () => Behav()
            // ================================================================================
            // the tomb's dead
            .Init("Frozen Sarcophagus",
                new State(
                    new State("Sealed",
                        new PlayerWithinTransition(5, "Crack")
                    ),
                    new State("Crack",
                        new SetAltTexture(1),
                        new Flash(FrostGlow, 0.15, 4),
                        new TimedTransition(900, "Open")
                    ),
                    new State("Open",
                        new SetAltTexture(2),
                        new Spawn("Rimebound Mummy", 1, 1, coolDown: 999999),
                        new TimedTransition(500, "Empty")
                    ),
                    new State("Empty")
                )
            )
            .Init("Rimebound Mummy",
                new State(
                    new Prioritize(new Follow(0.45, 9, 2), new Wander(0.2)),
                    new Shoot(9, 3, 14, 0, predictive: 0.3, coolDown: 1400),
                    new Shoot(3.5, 8, 45, 1, coolDown: 2600)
                ),
                TombMinionLoot()
            )
            .Init("Frost Wraith",
                new State(
                    new Prioritize(new Orbit(0.7, 5, 11, speedVariance: 0.2, radiusVariance: 0.6), new Wander(0.35)),
                    new Shoot(10, 2, 18, 0, predictive: 0.4, coolDown: 1200)
                ),
                TombMinionLoot()
            )
            .Init("Glacial Scarab",
                new State(
                    new State("Scuttle",
                        new Prioritize(new Follow(1.1, 8, 1), new Wander(0.5)),
                        new Shoot(2.2, 2, 30, 0, coolDown: 600),
                        new TimedTransition(2400, "Pause", true)
                    ),
                    new State("Pause",
                        new Wander(0.2),
                        new TimedTransition(900, "Scuttle")
                    )
                )
            )
            .Init("Frostfang Hound",
                new State(
                    new State("Stalk",
                        new Prioritize(new Follow(0.9, 11, 4), new Wander(0.4)),
                        new Shoot(4, 1, projectileIndex: 0, coolDown: 700),
                        new TimedTransition(2600, "Lunge", true)
                    ),
                    new State("Lunge",
                        new Charge(1.8, 9, 2000),
                        new Shoot(3, 3, 20, 0, coolDown: 500),
                        new TimedTransition(900, "Stalk")
                    )
                ),
                TombMinionLoot()
            )
            .Init("Glacier Golem",
                new State(
                    new Prioritize(new Follow(0.3, 10, 3), new Wander(0.15)),
                    new Shoot(10, 3, 20, 0, predictive: 0.3, coolDown: 2200),
                    new Shoot(0, 10, 36, 1, fixedAngle: 0, rotateAngle: 18, coolDown: 3000),
                    new Grenade(2.5, 120, 8, coolDown: 4000, color: FrostGlow)
                ),
                TombMinionLoot()
            )
            .Init("Icicle Gargoyle",
                new State(
                    new State("Watch", new PlayerWithinTransition(10, "Fire")),
                    new State("Fire",
                        new Shoot(12, 1, projectileIndex: 0, predictive: 0.5, coolDown: 1500),
                        new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 15, coolDown: 600),
                        new TimedTransition(6000, "Rest")
                    ),
                    new State("Rest",
                        new Shoot(12, 1, projectileIndex: 0, predictive: 0.5, coolDown: 1500),
                        new TimedTransition(2500, "Fire")
                    )
                ),
                TombMinionLoot()
            )
            .Init("Aurora Wisp",
                new State(
                    new Prioritize(new Orbit(0.9, 4, 12, speedVariance: 0.3, radiusVariance: 0.5), new Wander(0.5)),
                    new Shoot(9, 1, projectileIndex: 0, predictive: 0.4, coolDown: 900),
                    // the aurora mends the tomb's dead round it
                    new HealGroup(6, "Ice Tomb Dead", 3000, 300)
                )
            )
            .Init("Frozen Revenant",
                new State(
                    new Prioritize(new Follow(0.5, 10, 2), new Wander(0.25)),
                    new Shoot(6, 1, projectileIndex: 0, predictive: 0.3, coolDown: 1100),
                    new Shoot(9, 5, 14, 1, coolDown: 2400)
                ),
                TombMinionLoot()
            )
            // under the ice it can't be hurt; it surfaces through it to fire, then goes under again
            .Init("Rime Serpent",
                new State(
                    new State("Below",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(1),
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new Wander(0.4)),
                        new PlayerWithinTransition(11, "Rise")
                    ),
                    new State("Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(1),
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new Follow(0.6, 11, 4)),
                        new TimedTransition(2500, "Surface", true)
                    ),
                    new State("Surface",
                        new SetAltTexture(0),
                        new Flash(FrostGlow, 0.2, 2),
                        new Shoot(12, 3, 16, 0, predictive: 0.4, coolDown: 900),
                        new Shoot(0, 8, 45, 1, fixedAngle: 22.5, coolDown: 1800),
                        new TimedTransition(3600, "Below")
                    )
                ),
                TombMinionLoot()
            )
            // the lake, the chasms, the door
            .Init("Lake Heart", new State(new ThinIce("Lake Ice", "Cracking Ice", "Frigid Water")))
            .Init("Chasm Heart", new State(new IceBridge("Tomb Abyss", "Ice Bridge", "Ice Bridge Rim", "Chasm Mist")))
            .Init("Sealed Door", new State(new SealedDoor()))
            // the Court Gate (SealedDoor): only there to lead the way, never to be broken
            .Init(IceTomb.CourtGate, new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Flash(FrostGlow, 0.6, 999)
            ))

            // ================================================================================
            // the seals and their keepers
            .Init("Seal of the Warden", Seal("Rime Colossus"))
            .Init("Seal of the Oracle", Seal("Aurora Matron"))
            .Init("Seal of the Reaver", Seal("Frostfang Alpha"))
            // a giant of the glacier, a sarcophagus lid for a shield: boulders and armour-breaking
            // shards; from half its health it slams the floor and turns out a spiral of snowflakes
            .Init("Rime Colossus",
                new State(
                    new BossStats(30000, 40, 1.0, 0.3),
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(11, "Fight")
                    ),
                    new State("Fight",
                        new Taunt(true, "The Warden's seal holds. I hold it."),
                        new Prioritize(new StayCloseToSpawn(0.35, 7), new Follow(0.35, 14, 3)),
                        new Shoot(14, 3, 16, 0, predictive: 0.3, coolDown: 2000),
                        new Shoot(0, 12, 30, 1, fixedAngle: 0, rotateAngle: 15, coolDown: 2600),
                        new HpLessTransition(0.5, "Slam")
                    ),
                    new State("Slam",
                        new Flash(SteelGlow, 0.2, 4),
                        new Prioritize(new StayCloseToSpawn(0.35, 7), new Follow(0.35, 14, 3)),
                        new Shoot(14, 3, 16, 0, predictive: 0.3, coolDown: 1800),
                        new Shoot(0, 12, 30, 1, fixedAngle: 0, rotateAngle: 15, coolDown: 2600),
                        new Shoot(0, 3, 120, 2, fixedAngle: 0, rotateAngle: 13, coolDown: 450),
                        new Grenade(3, 150, 10, coolDown: 3200, color: SteelGlow),
                        new HpLessTransition(0.25, "Calving")
                    ),
                    // the glacier breaks round him: armoured, boulders raining, a ring of iron
                    // shards, and two golems of the glacier walking out of the ice
                    new State("Calving",
                        new Taunt(true, "The glacier breaks. Let it bury you!"),
                        new Flash(SteelGlow, 0.2, 8),
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Spawn("Glacier Golem", 2, 1, coolDown: 999999),
                        new Prioritize(new StayCloseToSpawn(0.5, 7), new Follow(0.45, 14, 2)),
                        new Shoot(14, 5, 12, 0, predictive: 0.3, coolDown: 1700),
                        new Shoot(0, 3, 120, 2, fixedAngle: 0, rotateAngle: 13, coolDown: 400),
                        new Shoot(0, 16, 22.5, 1, fixedAngle: 11.25, coolDown: 2600),
                        new Grenade(3, 160, 10, coolDown: 2200, color: SteelGlow)
                    )
                ),
                KeeperLoot()
            )
            // a veiled shade with a lantern of green fire: she circles her seal, weaving ribbons of
            // the aurora, calling wisps; from half her health her violet ribbons quiet you
            .Init("Aurora Matron",
                new State(
                    new BossStats(24000, 20, 1.0, 0.3),
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(11, "Fight")
                    ),
                    new State("Fight",
                        new Taunt(true, "The Oracle sleeps. Let her dream a while longer."),
                        new Prioritize(new Orbit(0.5, 6, 20, "Seal of the Oracle")),
                        new Shoot(14, 3, 14, 0, predictive: 0.3, coolDown: 1500),
                        new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 2800),
                        new Spawn("Aurora Wisp", 3, 0, coolDown: 9000),
                        new HpLessTransition(0.5, "Night")
                    ),
                    new State("Night",
                        new Flash(VioletGlow, 0.2, 4),
                        new Prioritize(new Orbit(0.6, 6, 20, "Seal of the Oracle")),
                        new Shoot(14, 3, 14, 0, predictive: 0.3, coolDown: 1400),
                        new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 12, coolDown: 500),
                        new Spawn("Aurora Wisp", 3, 0, coolDown: 9000),
                        new HpLessTransition(0.25, "Eclipse")
                    ),
                    // she hides in the dark of her wisps: untouchable while any lives (20 s at most),
                    // a turning cross of violet ribbons the while; then the night deepens
                    new State("Eclipse",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new State("Call",
                            new Taunt(true, "Dark, little lights. Hold back the dawn for me."),
                            new Flash(VioletGlow, 0.3, 6),
                            new Spawn("Aurora Wisp", 4, 1, coolDown: 999999),
                            new TimedTransition(1500, "Veil")
                        ),
                        new State("Veil",
                            new Prioritize(new Orbit(0.7, 4, 20, "Seal of the Oracle")),
                            new Shoot(0, 4, 90, 1, fixedAngle: 45, rotateAngle: 9, coolDown: 450),
                            new Shoot(14, 3, 14, 0, predictive: 0.3, coolDown: 1700),
                            new EntitiesNotExistsTransition(18, "Deep Night", "Aurora Wisp"),
                            new TimedTransition(20000, "Deep Night")
                        )
                    ),
                    new State("Deep Night",
                        new Flash(VioletGlow, 0.2, 4),
                        new Prioritize(new Orbit(0.7, 6, 20, "Seal of the Oracle")),
                        new Shoot(14, 3, 14, 0, predictive: 0.3, coolDown: 1200),
                        new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 12, coolDown: 420),
                        new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 2600)
                    )
                ),
                KeeperLoot()
            )
            // the leader of the pack: it runs you down, bites to bleed, howls up hounds
            .Init("Frostfang Alpha",
                new State(
                    new BossStats(26000, 25, 1.0, 0.3),
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(11, "Fight")
                    ),
                    new State("Fight",
                        new Taunt(true, "*a howl that shakes the ice from the walls*"),
                        new State("Circle",
                            new Prioritize(new StayCloseToSpawn(0.6, 9), new Orbit(0.8, 5, 14)),
                            new Shoot(12, 3, 14, 1, predictive: 0.4, coolDown: 1300),
                            new TimedTransition(3500, "Pounce")
                        ),
                        new State("Pounce",
                            new Flash(FrostGlow, 0.15, 3),
                            new Charge(2.4, 12, 3000),
                            new Shoot(3, 4, 18, 0, coolDown: 400),
                            new TimedTransition(1100, "Howl")
                        ),
                        new State("Howl",
                            new Shoot(0, 12, 30, 2, fixedAngle: 0, coolDown: 9999),
                            new Spawn("Frostfang Hound", 3, 1, coolDown: 9000),
                            new TimedTransition(900, "Circle")
                        ),
                        new HpLessTransition(0.3, "Blood Moon")
                    ),
                    // the pack's last hunt: a howl that calls them all, then three pounces in a row,
                    // the last bursting into a ring of rime
                    new State("Blood Moon",
                        new Taunt(true, "*the Alpha's eyes burn red under the aurora*"),
                        new Flash(0xFFFF6060, 0.2, 6),
                        new State("Call",
                            new Shoot(0, 16, 22.5, 2, fixedAngle: 0, coolDown: 9999),
                            new Spawn("Frostfang Hound", 4, 1, coolDown: 9999),
                            new TimedTransition(1000, "Chase")
                        ),
                        new State("Chase",
                            new Prioritize(new StayCloseToSpawn(0.9, 10), new Orbit(1.0, 4, 14)),
                            new Shoot(12, 4, 12, 1, predictive: 0.4, coolDown: 1000),
                            new TimedTransition(2600, "Pounce 1")
                        ),
                        new State("Pounce 1",
                            new Flash(0xFFFF6060, 0.12, 2),
                            new Charge(2.6, 12, 2000),
                            new Shoot(3, 5, 16, 0, coolDown: 300),
                            new TimedTransition(850, "Pounce 2")
                        ),
                        new State("Pounce 2",
                            new Charge(2.6, 12, 2000),
                            new Shoot(3, 5, 16, 0, coolDown: 300),
                            new TimedTransition(850, "Pounce 3")
                        ),
                        new State("Pounce 3",
                            new Charge(2.6, 12, 2000),
                            new Shoot(3, 5, 16, 0, coolDown: 300),
                            new Shoot(0, 12, 30, 1, fixedAngle: 15, coolDown: 9999, coolDownOffset: 600),
                            new TimedTransition(1100, "Call")
                        )
                    )
                ),
                KeeperLoot()
            )

            // ================================================================================
            // the Frozen Court
            .Init("Heart of Winter",
                new State(
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(5000, "Beat")
                    ),
                    new State("Beat",
                        new HpLessTransition(0.8, "1")
                    ),
                    new State("1", new SetAltTexture(1), new HpLessTransition(0.6, "2")),
                    new State("2", new SetAltTexture(2), new HpLessTransition(0.4, "3")),
                    new State("3", new SetAltTexture(3), new HpLessTransition(0.2, "4")),
                    new State("4", new SetAltTexture(4), new HpLessTransition(0.08, "5")),
                    new State("5", new SetAltTexture(5))
                )
            )
            // the Court's floor, ordered round in turns; its caches; the last stands
            .Init("Court Heart",
                new State(
                    new State("Wait",
                        new PlayerWithinTransition(17, "Title")
                    ),
                    new State("Title",
                        new EncounterTitle(),
                        new EntityNotExistsTransition("Heart of Winter", 30, "Wake")
                    ),
                    new State("Wake",
                        new EncounterCue(CrucibleCues.CourtWakes, Kaldrun, shake: true),
                        new TimedTransition(3500, "Fight")
                    ),
                    new State("Fight",
                        new CrucibleCaches(14000, 17000, TombCaches),
                        new EntitiesNotExistsTransition(40, "Silent", Kaldrun, Ysolde, Hrimgar),
                        new State("Calm 1", new TimedTransition(7000, "Avalanche")),
                        new State("Avalanche",
                            new EntityNotExistsTransition(Kaldrun, 40, "Calm 2"),
                            new EncounterCue(30, Kaldrun, shake: true),
                            new FloorWave("Avalanche", 18, 5, 3, 70, everyMs: 3400),
                            new TimedTransition(12000, "Calm 2")
                        ),
                        new State("Calm 2", new TimedTransition(4500, "Aurora")),
                        new State("Aurora",
                            new EntityNotExistsTransition(Ysolde, 40, "Calm 3"),
                            new EncounterCue(32, Ysolde),
                            new SweepingArms("Aurora Light", 3, 3, 18, 2.2, 15, warnMs: 2000, warnTile: "Rime Warning"),
                            new TimedTransition(14000, "Calm 3")
                        ),
                        new State("Calm 3", new TimedTransition(4500, "Fissures")),
                        new State("Fissures",
                            new EntityNotExistsTransition(Hrimgar, 40, "Calm 4"),
                            new EncounterCue(34, Hrimgar, shake: true),
                            new LavaVeins("Frost Fissure", "Frozen Scar", 3, 13, 18, everyMs: 3800, warnMs: 900, warnTile: "Rime Warning"),
                            new TimedTransition(12000, "Calm 4")
                        ),
                        new State("Calm 4", new TimedTransition(4500, "Blizzard")),
                        new State("Blizzard",
                            new EntityNotExistsTransition(Ysolde, 40, "Calm 5"),
                            new EncounterCue(33, Ysolde),
                            new TideSweep("Blizzard", 18, 3, 3.5, 2, 4.5, warnMs: 1800, everyMs: 6000, warnTile: "Rime Warning"),
                            new TimedTransition(14000, "Calm 5")
                        ),
                        new State("Calm 5", new TimedTransition(4500, "Hailstorm")),
                        new State("Hailstorm",
                            new EntityNotExistsTransition(Hrimgar, 40, "Calm 6"),
                            new EncounterCue(35, Hrimgar),
                            new Starfall(Starfall.Area.Players, 22, 1.8, 170, 1300, perVolley: 2, color: FrostGlow,
                                effect: ConditionEffectIndex.Slowed, effectMs: 1500, fillMs: 1600),
                            new TimedTransition(12000, "Calm 6")
                        ),
                        new State("Calm 6", new TimedTransition(4500, "Calving")),
                        // Kaldrun's Calving Glacier: arms of avalanche turning round the hall while
                        // waves of it roll out from the middle; step through a wave's gap, keep ahead
                        // of the arms
                        new State("Calving",
                            new EntityNotExistsTransition(Kaldrun, 40, "Calm 7"),
                            new EncounterCue(44, Kaldrun, shake: true),
                            new FloorLayers(
                                new SweepingArms("Avalanche", 4, 4, 18, 2.0, 14, warnMs: 2000, warnTile: "Rime Warning"),
                                new FloorWave("Avalanche", 18, 4.5, 3, 70, everyMs: 4600)),
                            new TimedTransition(13000, "Calm 7")
                        ),
                        new State("Calm 7", new TimedTransition(4500, "Frozen Hours")),
                        // Ysolde's Frozen Hours: the hall cut into six hours, the aurora burning in
                        // them by turns
                        new State("Frozen Hours",
                            new EntityNotExistsTransition(Ysolde, 40, "Calm 8"),
                            new EncounterCue(45, Ysolde),
                            new SectorChime("Aurora Light", 6, 3, 18, new[] { "101010", "010101", "110110", "011011", "100100" },
                                warnMs: 1400, litMs: 1300, gapMs: 800, warnTile: "Rime Warning"),
                            new TimedTransition(14000, "Calm 8")
                        ),
                        new State("Calm 8", new TimedTransition(4500, "Wolf Moon")),
                        // Hrimgar's Wolf Moon: his hounds loosed in the hall, rime cracking out under
                        // whoever they chase
                        new State("Wolf Moon",
                            new EntityNotExistsTransition(Hrimgar, 40, "Calm 9"),
                            new EncounterCue(46, Hrimgar, shake: true),
                            new Spawn("Frostfang Hound", 3, 1, coolDown: 999999),
                            new LavaVeins("Frost Fissure", "Frozen Scar", 2, 10, 18, everyMs: 4400, warnMs: 900, warnTile: "Rime Warning"),
                            new TimedTransition(12000, "Calm 9")
                        ),
                        new State("Calm 9", new TimedTransition(4500, "Avalanche"))
                    ),
                    // the last of them, below 15%: the whole floor against the players
                    new State("Permafrost",
                        new CrucibleCaches(6000, 15000, TombCaches),
                        new ClosingWalls("Permafrost", 17, 3, 1.4, 2, 3.6, everyMs: 7000, warnTile: "Rime Warning"),
                        new EntitiesNotExistsTransition(40, "Silent", Kaldrun, Ysolde, Hrimgar)
                    ),
                    new State("Polar Night",
                        new CrucibleCaches(6000, 15000, TombCaches),
                        new SectorChime("Polar Dark", 8, 2.5, 18, new[] { "10101010", "01010101", "11001100", "00110011" },
                            warnMs: 1500, litMs: 1500, gapMs: 1100, warnTile: "Rime Warning"),
                        new EntitiesNotExistsTransition(40, "Silent", Kaldrun, Ysolde, Hrimgar)
                    ),
                    new State("Shattered Lake",
                        new CrucibleCaches(6000, 15000, TombCaches),
                        new SafeIsland("Frigid Water", "Ice Floe Rim", 18, 5, 8, 1.0, warnMs: 2500, pourMs: 6000, restMs: 4500,
                            warnTile: "Rime Warning"),
                        new EntitiesNotExistsTransition(40, "Silent", Kaldrun, Ysolde, Hrimgar)
                    ),
                    new State("Silent",
                        new EncounterCue(CrucibleCues.CourtFalls, Kaldrun),
                        new RemoveEntitiesOnExit(40, "Kaldrun's Shade"),
                        new TimedTransition(1000, "Gone")
                    ),
                    new State("Gone")
                )
            )

            // The Court fights as the Tomb of the Ancients' three do, harder, on the Crucible's
            // floor: each new turn as its health falls opens with a taunt and 3 s behind a blue
            // shield (CourtTurn: it can't be hurt), its relics come out at 40% and 25% to circle it
            // (two, then one more), and below 6% it rages, red, running you down. Each keeps its
            // own turn among them (Kaldrun's Glacial Bulwark at 60%, Ysolde's Aurora's Grace from
            // 50%, Hrimgar's Frenzy at 50%), and the last of them standing, below 15%, makes its
            // last stand, the whole floor turned against the players, and rages in it below 6%.
            // They circle the Heart as the Tomb's three circle their anchor (at its pace, 3 to 4: an
            // Orbit's speed is its walking speed), Kaldrun 5 tiles out, Hrimgar 6, Ysolde 7 the
            // other way round.
            //
            // Kaldrun, the Rime Warden (Bes): he holds the middle, circling the Heart close. Glacial
            // lances that break armour, rings of iron shards, slowing rime; at 80% the Rime Wall
            // (rings of snowflakes); at 60% the Glacial Bulwark: he walls himself in ice,
            // untouchable, lobbing boulders over it, until the four Rime Pillars round him are
            // broken (or 25 s pass); then snowflake spirals join his kit. Armoured with his relics
            // out. Last of the three, below 15%: Permafrost.
            .Init(Kaldrun,
                new State(
                    new BossStats(90000, 55, 1.0, 0.35),
                    Encased(),
                    new State("Fight", Kit(
                        new Prioritize(new Orbit(3, 5, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                        WardenKit(),
                        new HpLessTransition(0.8, "Rime Wall")
                    )),
                    CourtTurn("Rime Wall", "Impudence! The Court of Winter does not answer to the warm.", Kit(
                        WardenKit(),
                        new Prioritize(new Orbit(3, 5, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                        new Shoot(0, 20, 18, 4, fixedAngle: 0, coolDown: 6000),
                        new HpLessTransition(0.6, "Bulwark"))),
                    new State("Bulwark",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new State("Return",
                            new EncounterCue(31, shake: true),
                            new Taunt(true, "Behind the glacier, I wait. Break the pillars, if your arms are strong enough."),
                            new ReturnToSpawn(1.4),
                            new TimedTransition(1800, "Raise")
                        ),
                        new State("Raise",
                            new Rampart("Court Ice Wall", 5, (dx, dy) => dx * dx + dy * dy >= 9 && dx * dx + dy * dy <= 18),
                            new SpawnRing("Rime Pillar", 4, 8, 45, aroundSpawn: true),
                            new RemoveEntitiesOnExit(25, "Rime Pillar"),
                            new Grenade(3, 170, 14, coolDown: 1700, color: SteelGlow),
                            new Shoot(CourtSight, 1, projectileIndex: 3, predictive: 0.5, coolDown: 2400),
                            new State("Rising", new TimedTransition(800, "Hold")),
                            new State("Hold",
                                new EntitiesNotExistsTransition(16, "Fight 2", "Rime Pillar"),
                                new TimedTransition(25000, "Fight 2")
                            )
                        )
                    ),
                    new State("Fight 2", Kit(
                        new Prioritize(new Orbit(3, 5, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                        WardenKit(),
                        WardenSpiral(650),
                        new HpLessTransition(0.4, "Relics")
                    )),
                    CourtTurn("Relics", "My relics shall hold where the glacier broke!", Kit(
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Prioritize(new Orbit(3, 5, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                        WardenKit(),
                        WardenSpiral(650),
                        new Spawn("Rime Relic", 2, 0),
                        new HpLessTransition(0.25, "Relics 2"))),
                    CourtTurn("Relics 2", "Ice upon ice. You will not reach me.", Kit(
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Prioritize(new Orbit(3, 5, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                        WardenKit(),
                        WardenSpiral(600),
                        new Shoot(0, 20, 18, 4, fixedAngle: 9, coolDown: 6000),
                        new Spawn("Rime Relic", 1, 0),
                        new HpLessTransition(0.15, "Fight 3"))),
                    new State("Fight 3", Kit(
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Prioritize(new Orbit(3, 5, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                        WardenKit(),
                        WardenSpiral(600),
                        new EntitiesNotExistsTransition(40, "Permafrost", Ysolde, Hrimgar),
                        new HpLessTransition(0.06, "Rage")
                    )),
                    new State("Permafrost", Kit(
                        new EncounterCue(38, shake: true),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Permafrost"),
                        new Taunt(true, "Then let the whole Court freeze, and you with it!"),
                        new Prioritize(new Orbit(2, 3, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                        PermafrostKit(),
                        new HpLessTransition(0.06, "Last Rage")
                    )),
                    CourtTurn("Rage", "The end of your path is here, under the ice!", Kit(WardenRage(),
                        new EntitiesNotExistsTransition(40, "Last Rage", Ysolde, Hrimgar))),
                    CourtTurn("Last Rage", "Freeze! FREEZE, and be still forever!", Kit(WardenRage(),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Permafrost"))),
                    new TransformOnDeath("Kaldrun's Shade")
                ),
                IceTombTreasure(),
                new Threshold(0.01, new ItemLoot("Lil' Kaldrun Generator", 0.02))
            )
            // Ysolde, the Aurora Oracle (Nut): she circles the Heart the other way, outside the
            // others, weaving ribbons of the aurora (the violet ones quiet you), rings of sparks, slow
            // souls that darken; at 80% her Ribbons (a turning cross of violet ones); at 50% and
            // every 30 s after, Aurora's Grace: 10 s healing each of the Court 1.5% every 2 s,
            // unless she's stunned (a Stormglass Jar). Last below 15%: Polar Night.
            .Init(Ysolde,
                new State(
                    new BossStats(70000, 20, 1.0, 0.35),
                    Encased(),
                    new State("Fight", Kit(
                        new Prioritize(new Orbit(4, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        OracleKit(),
                        new HpLessTransition(0.8, "Ribbons")
                    )),
                    CourtTurn("Ribbons", "Impudence! Let the lights take your voices.", Kit(
                        new Prioritize(new Orbit(4, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        OracleKit(),
                        new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 12, coolDown: 900),
                        new HpLessTransition(0.5, "Grace"))),
                    new State("Grace",
                        new EncounterCue(36),
                        new Taunt(true, "Rise, my Court. The lights have not gone out yet."),
                        new Flash(AuroraGlow, 0.3, 10),
                        new Prioritize(new Orbit(2, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        new CourtGrace(0.015, 2000),
                        new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 2000),
                        new TimedTransition(10000, "Fight 2")
                    ),
                    new State("Fight 2", Kit(
                        new Prioritize(new Orbit(4, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        OracleKit(),
                        new Shoot(CourtSight, 1, projectileIndex: 3, predictive: 0.3, coolDown: 2600),
                        new HpLessTransition(0.4, "Relics"),
                        GraceCycle("Fight 2"))),
                    CourtTurn("Relics", "My relics shall make your little lives end the swifter!", Kit(
                        new Prioritize(new Orbit(4, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        OracleKit(),
                        new Shoot(CourtSight, 1, projectileIndex: 3, predictive: 0.3, coolDown: 2400),
                        new Spawn("Aurora Relic", 2, 0),
                        new HpLessTransition(0.25, "Relics 2")),
                        GraceCycle("Relics")),
                    CourtTurn("Relics 2", "Brighter, little lights. Brighter!", Kit(
                        new Prioritize(new Orbit(4, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        OracleKit(),
                        new Shoot(CourtSight, 1, projectileIndex: 3, predictive: 0.3, coolDown: 2400),
                        new Shoot(0, 4, 90, 1, fixedAngle: 45, rotateAngle: 12, coolDown: 800),
                        new Spawn("Aurora Relic", 1, 0),
                        new HpLessTransition(0.15, "Fight 3")),
                        GraceCycle("Relics 2")),
                    new State("Fight 3", Kit(
                        new Prioritize(new Orbit(4, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        OracleKit(),
                        new Shoot(0, 4, 90, 1, fixedAngle: 45, rotateAngle: 12, coolDown: 800),
                        new EntitiesNotExistsTransition(40, "Polar Night", Kaldrun, Hrimgar),
                        new HpLessTransition(0.06, "Rage")
                    )),
                    new State("Polar Night", Kit(
                        new EncounterCue(39, shake: true),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Polar Night"),
                        new Taunt(true, "The lights go out. In the dark, there is only the cold."),
                        new Prioritize(new Orbit(2.5, 3, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
                        PolarNightKit(),
                        new HpLessTransition(0.06, "Last Rage")
                    )),
                    CourtTurn("Rage", "This cannot be! The dawn shall not come!", Kit(OracleRage(),
                        new EntitiesNotExistsTransition(40, "Last Rage", Kaldrun, Hrimgar))),
                    CourtTurn("Last Rage", "Then dark, and dark, and only dark!", Kit(OracleRage(),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Polar Night"))),
                    new TransformOnDeath("Ysolde's Shade")
                ),
                IceTombTreasure(),
                new Threshold(0.01, new ItemLoot("Lil' Ysolde Generator", 0.02))
            )
            // Hrimgar, the Frost Reaver (Geb): he circles the Heart, axes thrown to bleed you and
            // fangs, then leaps on someone and bursts a ring of hailstones where he lands; at 75%
            // the Pack (his hounds, as Geb's scarabs, and hailstones lobbed over), at 50% his Frenzy
            // (faster, and a spiral of rime through the leaps). Last below 15%: the Shattered Lake.
            .Init(Hrimgar,
                new State(
                    new BossStats(80000, 35, 1.0, 0.35),
                    Encased(),
                    new State("Fight", Kit(
                        new HpLessTransition(0.75, "Pack"),
                        ReaverCycle("Fight", 1.0))),
                    CourtTurn("Pack", "To me, my hounds! Run them down!", Kit(
                        new Spawn("Frostfang Hound", 3, 0, coolDown: 10000),
                        HailLobs(1500),
                        new HpLessTransition(0.5, "Frenzy")),
                        ReaverCycle("Pack", 0.9)),
                    new State("Frenzy", Kit(
                        new EncounterCue(37, shake: true),
                        new Taunt(true, "HA! NOW we fight!"),
                        new Flash(0xFFFF6060, 0.2, 6),
                        new Shoot(0, 2, 180, 3, fixedAngle: 0, rotateAngle: 17, coolDown: 700),
                        new HpLessTransition(0.4, "Relics"),
                        ReaverCycle("Frenzy", 0.75))),
                    CourtTurn("Relics", "My relics shall tear you from your flesh!", Kit(
                        new Shoot(0, 2, 180, 3, fixedAngle: 0, rotateAngle: 17, coolDown: 700),
                        HailLobs(1500),
                        new Spawn("Reaver Relic", 2, 0),
                        new HpLessTransition(0.25, "Relics 2")),
                        ReaverCycle("Relics", 0.75)),
                    CourtTurn("Relics 2", "More! Bring me MORE!", Kit(
                        new Shoot(0, 2, 180, 3, fixedAngle: 0, rotateAngle: 17, coolDown: 650),
                        HailLobs(1300),
                        new Spawn("Reaver Relic", 1, 0),
                        new HpLessTransition(0.15, "Fight 3")),
                        ReaverCycle("Relics 2", 0.75)),
                    new State("Fight 3", Kit(
                        new Shoot(0, 2, 180, 3, fixedAngle: 0, rotateAngle: 17, coolDown: 650),
                        new EntitiesNotExistsTransition(40, "Shattered Lake", Kaldrun, Ysolde),
                        new HpLessTransition(0.06, "Rage"),
                        ReaverCycle("Fight 3", 0.75))),
                    new State("Shattered Lake", Kit(
                        new EncounterCue(40, shake: true),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Shattered Lake"),
                        new Taunt(true, "The ice breaks! Swim, little ones!"),
                        new Prioritize(new Follow(1.2, CourtSight, 4)),
                        ShatteredKit(),
                        new HpLessTransition(0.06, "Last Rage")
                    )),
                    CourtTurn("Rage", "This cannot be! I am the hunt!", Kit(ReaverRage("Rage"),
                        new EntitiesNotExistsTransition(40, "Last Rage", Kaldrun, Ysolde))),
                    CourtTurn("Last Rage", "Down into the dark water, all of you!", Kit(ReaverRage("Last Rage"),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Shattered Lake"))),
                    new TransformOnDeath("Hrimgar's Shade")
                ),
                IceTombTreasure(),
                new Threshold(0.01, new ItemLoot("Lil' Hrimgar Generator", 0.02))
            )
            // a fallen one's shade: it lingers, circling the hall, a slow ring of frozen souls, until
            // the Court is gone
            .Init("Kaldrun's Shade", Shade(2600, 0))
            .Init("Ysolde's Shade", Shade(3000, 15))
            .Init("Hrimgar's Shade", Shade(2800, 30))
            // Kaldrun's Bulwark: break the four, and his ice falls
            .Init("Rime Pillar",
                new State(
                    new Flash(SteelGlow, 0.5, 999)
                )
            )
            // the Court's relics (the Tomb's artifacts): out at 40% and 25%, and in its rage
            .Init("Rime Relic", Relic(Kaldrun, 2400))
            .Init("Aurora Relic", Relic(Ysolde, 2200))
            .Init("Reaver Relic", Relic(Hrimgar, 2000))

            // ================================================================================
            // the Court's pets: each a little copy that fights as its boss does and, every 10 s,
            // takes on its last form for 4 s and throws out its spiral. Their shots and the numbers
            // in their generators are tools/build_ice_tomb.py's PETS: change both together.
            .Init("Lil' Kaldrun", ChampionPet(45,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Armored, 2000, 10000),
                    new PetShoot(10, 1, 0, 0, 0, 1400),
                    new PetShoot(10, 3, 12, 0, 1, 1200),
                    new PetNova(1100, 1500, 4000, 10, 2.5, ConditionEffectIndex.Slowed, 1500, FrostGlow),
                },
                new PetShoot(10, 3, 120, 0, 2, 300, rotateAngle: 17)))
            .Init("Lil' Ysolde", ChampionPet(55,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Healing, 2000, 10000),
                    new PetShoot(10, 3, 12, 0, 0, 1100),
                    new PetShoot(10, 8, 45, 0, 1, 2400, rotateAngle: 22.5),
                },
                new PetShoot(10, 3, 120, 0, 2, 300, rotateAngle: 17)))
            .Init("Lil' Hrimgar", ChampionPet(45,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Berserk, 2000, 10000),
                    new PetShoot(10, 2, 12, 0, 0, 1000),
                    new PetShoot(10, 3, 14, 0, 1, 900),
                    new PetNova(1000, 1400, 3500, 10, 2, ConditionEffectIndex.Bleeding, 2000, FrostGlow),
                },
                new PetShoot(10, 3, 120, 0, 2, 300, rotateAngle: 17)));

        // a seal: warded (it can't be hurt) while its keeper lives, then bare, cracking at half
        private static State Seal(string keeper) => new State(
            new State("Warded",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new SetAltTexture(0),
                new EntityNotExistsTransition(keeper, 30, "Bare")
            ),
            new State("Bare",
                new SetAltTexture(1),
                new Flash(FrostGlow, 0.2, 5),
                new HpLessTransition(0.5, "Cracked")
            ),
            new State("Cracked",
                new SetAltTexture(2)
            )
        );

        // the Court before the Heart of Winter breaks: frozen in its block of ice (AltTexture 2);
        // then it breaks out and, a moment later, fights
        private static State Encased() => new State("Encased",
            new State("Frozen",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new SetAltTexture(2),
                new EntityNotExistsTransition("Heart of Winter", 40, "Break Out")
            ),
            new State("Break Out",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new SetAltTexture(0),
                new Flash(FrostGlow, 0.2, 6),
                new Taunt(true, "Who wakes the Court of Winter?", "The Heart is broken... then we are free.", "Warm blood. It has been so long."),
                new TimedTransition(3000, "Fight")
            )
        );

        // a list of a state's children, arrays of them (the kits) flattened into it
        private static IStateChildren[] Kit(params object[] parts)
        {
            var all = new List<IStateChildren>();
            foreach (var p in parts)
                if (p is IEnumerable<IStateChildren> many)
                    all.AddRange(many);
                else
                    all.Add((IStateChildren)p);
            return all.ToArray();
        }

        // one of the Court's turns, the Tomb of the Ancients' way: a taunt, then 3 s behind a blue
        // shield (it can't be hurt), then on. Its own states (a cycle that runs through the turn)
        // follow the shield, the first of them first; state names must be the boss's own (a
        // behaviour's states share one list of names).
        private static State CourtTurn(string name, string taunt, params object[] parts)
        {
            var kit = Kit(parts);
            var states = kit.OfType<State>().ToList();
            if (states.Count == 0)
                states.Add(new State(name + " on"));
            var children = new List<IStateChildren> { new Taunt(true, taunt) };
            children.AddRange(kit.Where(c => !(c is State)));
            children.Add(new State(name + " shield",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Flash(BlueShield, 0.3, 5),
                new TimedTransition(3000, states[0].Name)));
            children.AddRange(states);
            return new State(name, children.ToArray());
        }

        // the Tomb's rage below 6%: a red glow that doesn't go out
        private static Flash Rage() => new Flash(0xFFFF0000, 1, 9000001);

        private static IStateChildren[] WardenKit() => new IStateChildren[]
        {
            new Shoot(CourtSight, 3, 12, 0, predictive: 0.4, coolDown: 1900),
            new Shoot(0, 12, 30, 1, fixedAngle: 0, rotateAngle: 15, coolDown: 3000, coolDownOffset: 1500),
            new Shoot(CourtSight, 1, projectileIndex: 2, predictive: 0.6, coolDown: 1100),
        };

        // a spiral of snowflakes, three arms
        private static Shoot WardenSpiral(int every) => new Shoot(0, 3, 120, 4, fixedAngle: 0, rotateAngle: 11, coolDown: every);

        private static IStateChildren[] PermafrostKit() => new IStateChildren[]
        {
            new Shoot(CourtSight, 5, 10, 0, predictive: 0.4, coolDown: 1600),
            new Shoot(0, 14, 360.0 / 14, 1, fixedAngle: 0, rotateAngle: 12, coolDown: 2600),
            new Shoot(0, 4, 90, 4, fixedAngle: 0, rotateAngle: 9, coolDown: 500),
        };

        private static IStateChildren[] WardenRage() => Kit(
            Rage(),
            new ConditionEffectBehavior(ConditionEffectIndex.Armored),
            new Prioritize(new Follow(1.2, CourtSight, 2), new Orbit(3, 5, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
            WardenKit(),
            PermafrostKit(),
            WardenSpiral(450),
            new Spawn("Rime Relic", 2, 0));

        private static IStateChildren[] OracleKit() => new IStateChildren[]
        {
            new Shoot(CourtSight, 3, 14, 0, predictive: 0.3, coolDown: 1500),
            new Shoot(CourtSight, 2, 24, 1, coolDown: 2600, coolDownOffset: 800),
            new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 3000, coolDownOffset: 1500),
        };

        // Aurora's Grace through one of Ysolde's turns: every 30 s, 10 s of it
        private static State[] GraceCycle(string turn) => new[]
        {
            new State(turn + " on", new TimedTransition(30000, turn + " grace")),
            new State(turn + " grace",
                new EncounterCue(36),
                new Flash(AuroraGlow, 0.3, 10),
                new CourtGrace(0.015, 2000),
                new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 2000),
                new TimedTransition(10000, turn + " on")),
        };

        private static IStateChildren[] PolarNightKit() => new IStateChildren[]
        {
            new Shoot(CourtSight, 3, 14, 1, predictive: 0.3, coolDown: 1500),
            new Shoot(0, 2, 180, 0, fixedAngle: 0, rotateAngle: 13, coolDown: 350),
            new Shoot(CourtSight, 1, projectileIndex: 3, predictive: 0.3, coolDown: 2200),
        };

        private static IStateChildren[] OracleRage() => Kit(
            Rage(),
            new Prioritize(new Follow(1.3, CourtSight, 5), new Orbit(4, 7, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5, orbitClockwise: true)),
            OracleKit(),
            PolarNightKit(),
            new Spawn("Aurora Relic", 2, 0));

        // Hrimgar's hunt: he circles the middle as Geb does, throwing axes and fangs, then leaps on
        // someone, and his landing bursts a ring of hailstones. `pace` shortens it all (his Frenzy);
        // `chase` runs players down instead (his rage); `turn` names its states.
        private static State[] ReaverCycle(string turn, double pace, bool chase = false) => new[]
        {
            new State(turn + " hunt",
                chase
                    ? new Prioritize(new Follow(1.5, CourtSight, 4), new Orbit(3 / pace, 6, CourtSight, "Court Heart"))
                    : new Prioritize(new Orbit(3 / pace, 6, CourtSight, "Court Heart", speedVariance: 0.2, radiusVariance: 0.5)),
                new Shoot(10, 2, 16, 0, predictive: 0.3, coolDown: (int)(1000 * pace)),
                new Shoot(14, 3, 14, 1, predictive: 0.4, coolDown: (int)(1400 * pace)),
                new TimedTransition((int)(5500 * pace), turn + " brace")
            ),
            new State(turn + " brace",
                new Flash(FrostGlow, 0.15, 3),
                new TimedTransition(700, turn + " leap")
            ),
            new State(turn + " leap",
                new Charge(2.4, (float)CourtSight, 4000),
                new TimedTransition(1000, turn + " land")
            ),
            new State(turn + " land",
                new Shoot(0, 14, 360.0 / 14, 2, fixedAngle: 0, coolDown: 99999),
                new Shoot(0, 8, 45, 4, fixedAngle: 22.5, coolDown: 99999, coolDownOffset: 300),
                new TimedTransition(900, turn + " hunt")
            ),
        };

        // hailstones lobbed over at whoever's near, as Geb's grenades
        private static Grenade HailLobs(int every) => new Grenade(3, 160, 12, coolDown: every, color: FrostGlow);

        private static IStateChildren[] ShatteredKit() => new IStateChildren[]
        {
            new Shoot(CourtSight, 2, 16, 0, predictive: 0.4, coolDown: 1100),
            new Shoot(CourtSight, 3, 14, 1, predictive: 0.4, coolDown: 1500),
            new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 3000),
        };

        private static IStateChildren[] ReaverRage(string turn) => Kit(
            Rage(),
            new Shoot(0, 2, 180, 3, fixedAngle: 0, rotateAngle: 17, coolDown: 550),
            new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 3000),
            HailLobs(1100),
            new Spawn("Reaver Relic", 2, 0),
            ReaverCycle(turn, 0.6, chase: true));

        // a relic of the Court (the Tomb's artifacts): it circles its maker close, firing, and
        // shatters when its maker falls. Kept within 2.4 of its maker, the Court within 7.5 of
        // the middle, so they clear the thrones (13.5) and pillars (15, IceCourt.cs)
        private static State Relic(string maker, int every) => new State(
            new Prioritize(new Orbit(2.5, 2, CourtSight, maker, speedVariance: 0.2, radiusVariance: 0.4)),
            new Shoot(12, 3, 14, 0, predictive: 0.3, coolDown: every),
            new State("Guard", new EntityNotExistsTransition(maker, 40, "Shatter")),
            new State("Shatter", new Suicide())
        );

        private static State Shade(int every, double turn) => new State(
            new State("Rise",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new EncounterCue(CrucibleCues.CourtMourns),
                new Flash(0xFFFFFFFF, 0.4, 3),
                new TimedTransition(1500, "Linger")
            ),
            new State("Linger",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Prioritize(new Orbit(1.2, 14, 40, "Court Heart", speedVariance: 0.1, radiusVariance: 0.1)),
                new Shoot(0, 8, 45, 0, fixedAngle: turn, rotateAngle: 11, coolDown: every),
                new EntitiesNotExistsTransition(45, "Fade", Kaldrun, Ysolde, Hrimgar)
            ),
            new State("Fade",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Flash(0xFFFFFFFF, 0.2, 5),
                new TimedTransition(1000, "Gone")
            ),
            new State("Gone", new Suicide())
        );
    }
}
