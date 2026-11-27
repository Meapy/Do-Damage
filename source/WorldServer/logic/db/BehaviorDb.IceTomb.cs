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
                new ItemLoot("Snowblind Wand", 0.0025),
                new ItemLoot("Tome of Frigid Protection", 0.0025),
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
                        new Grenade(3, 150, 10, coolDown: 3200, color: SteelGlow)
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
                        new Spawn("Aurora Wisp", 3, 0, coolDown: 9000)
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
                        new State("Calm 2", new TimedTransition(5500, "Aurora")),
                        new State("Aurora",
                            new EntityNotExistsTransition(Ysolde, 40, "Calm 3"),
                            new EncounterCue(32, Ysolde),
                            new SweepingArms("Aurora Light", 3, 3, 18, 2.2, 15, warnMs: 2000, warnTile: "Rime Warning"),
                            new TimedTransition(14000, "Calm 3")
                        ),
                        new State("Calm 3", new TimedTransition(5500, "Fissures")),
                        new State("Fissures",
                            new EntityNotExistsTransition(Hrimgar, 40, "Calm 4"),
                            new EncounterCue(34, Hrimgar, shake: true),
                            new LavaVeins("Frost Fissure", "Frozen Scar", 3, 13, 18, everyMs: 3800, warnMs: 900, warnTile: "Rime Warning"),
                            new TimedTransition(12000, "Calm 4")
                        ),
                        new State("Calm 4", new TimedTransition(5500, "Blizzard")),
                        new State("Blizzard",
                            new EntityNotExistsTransition(Ysolde, 40, "Calm 5"),
                            new EncounterCue(33, Ysolde),
                            new TideSweep("Blizzard", 18, 3, 3.5, 2, 4.5, warnMs: 1800, everyMs: 6000, warnTile: "Rime Warning"),
                            new TimedTransition(14000, "Calm 5")
                        ),
                        new State("Calm 5", new TimedTransition(5500, "Hailstorm")),
                        new State("Hailstorm",
                            new EntityNotExistsTransition(Hrimgar, 40, "Calm 6"),
                            new EncounterCue(35, Hrimgar),
                            new Starfall(Starfall.Area.Players, 22, 1.8, 170, 1300, perVolley: 2, color: FrostGlow,
                                effect: ConditionEffectIndex.Slowed, effectMs: 1500, fillMs: 1600),
                            new TimedTransition(12000, "Calm 6")
                        ),
                        new State("Calm 6", new TimedTransition(5500, "Avalanche"))
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

            // Kaldrun, the Rime Warden: he holds the middle, circling the Heart close. Glacial lances
            // that break armour, rings of iron shards, slowing rime; at 60% the Glacial Bulwark: he
            // walls himself in ice, untouchable, lobbing boulders over it, until the four Rime Pillars
            // round him are broken (or 25 s pass); then snowflake spirals join his kit. Last of the
            // three, below 15%: Permafrost.
            .Init(Kaldrun,
                new State(
                    new BossStats(90000, 55, 1.0, 0.35),
                    Encased(),
                    new State("Fight",
                        new Prioritize(new Orbit(0.45, 6, CourtSight, "Court Heart", radiusVariance: 0.3)),
                        WardenKit(),
                        new HpLessTransition(0.6, "Bulwark")
                    ),
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
                    new State("Fight 2",
                        new Prioritize(new Orbit(0.5, 6, CourtSight, "Court Heart", radiusVariance: 0.3)),
                        WardenKit(),
                        new Shoot(0, 3, 120, 4, fixedAngle: 0, rotateAngle: 11, coolDown: 650),
                        new HpLessTransition(0.15, "Fight 3")
                    ),
                    new State("Fight 3",
                        new Prioritize(new Orbit(0.5, 6, CourtSight, "Court Heart", radiusVariance: 0.3)),
                        WardenKit(),
                        new Shoot(0, 3, 120, 4, fixedAngle: 0, rotateAngle: 11, coolDown: 650),
                        new EntitiesNotExistsTransition(40, "Permafrost", Ysolde, Hrimgar)
                    ),
                    new State("Permafrost",
                        new EncounterCue(38, shake: true),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Permafrost"),
                        new Taunt(true, "Then let the whole Court freeze, and you with it!"),
                        new Prioritize(new Orbit(0.35, 2, CourtSight, "Court Heart")),
                        new Shoot(CourtSight, 5, 10, 0, predictive: 0.4, coolDown: 1600),
                        new Shoot(0, 14, 360.0 / 14, 1, fixedAngle: 0, rotateAngle: 12, coolDown: 2600),
                        new Shoot(0, 4, 90, 4, fixedAngle: 0, rotateAngle: 9, coolDown: 500)
                    ),
                    new TransformOnDeath("Kaldrun's Shade")
                ),
                IceTombTreasure(),
                new Threshold(0.01, new ItemLoot("Lil' Kaldrun Generator", 0.02))
            )
            // Ysolde, the Aurora Oracle: she keeps her distance on a wide circle round the Heart,
            // weaving ribbons of the aurora (the violet ones quiet you), rings of sparks, slow souls
            // that darken; at 50% and every 30 s after, Aurora's Grace: 10 s healing each of the
            // Court 1.5% every 2 s, unless she's stunned (a Stormglass Jar). Last below 15%: Polar
            // Night.
            .Init(Ysolde,
                new State(
                    new BossStats(70000, 20, 1.0, 0.35),
                    Encased(),
                    new State("Fight",
                        new Prioritize(new Orbit(0.55, 11, CourtSight, "Court Heart", radiusVariance: 0.2, orbitClockwise: true)),
                        OracleKit(),
                        new HpLessTransition(0.5, "Grace")
                    ),
                    new State("Grace",
                        new EncounterCue(36),
                        new Taunt(true, "Rise, my Court. The lights have not gone out yet."),
                        new Flash(AuroraGlow, 0.3, 10),
                        new Prioritize(new Orbit(0.3, 11, CourtSight, "Court Heart", orbitClockwise: true)),
                        new CourtGrace(0.015, 2000),
                        new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 2000),
                        new TimedTransition(10000, "Fight 2")
                    ),
                    new State("Fight 2",
                        new Prioritize(new Orbit(0.55, 11, CourtSight, "Court Heart", radiusVariance: 0.2, orbitClockwise: true)),
                        OracleKit(),
                        new Shoot(CourtSight, 1, projectileIndex: 3, predictive: 0.3, coolDown: 2600),
                        new TimedTransition(30000, "Grace"),
                        new HpLessTransition(0.15, "Fight 3")
                    ),
                    new State("Fight 3",
                        new Prioritize(new Orbit(0.55, 11, CourtSight, "Court Heart", radiusVariance: 0.2, orbitClockwise: true)),
                        OracleKit(),
                        new EntitiesNotExistsTransition(40, "Polar Night", Kaldrun, Hrimgar)
                    ),
                    new State("Polar Night",
                        new EncounterCue(39, shake: true),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Polar Night"),
                        new Taunt(true, "The lights go out. In the dark, there is only the cold."),
                        new Prioritize(new Orbit(0.3, 3, CourtSight, "Court Heart", orbitClockwise: true)),
                        new Shoot(CourtSight, 3, 14, 1, predictive: 0.3, coolDown: 1500),
                        new Shoot(0, 2, 180, 0, fixedAngle: 0, rotateAngle: 13, coolDown: 350),
                        new Shoot(CourtSight, 1, projectileIndex: 3, predictive: 0.3, coolDown: 2200)
                    ),
                    new TransformOnDeath("Ysolde's Shade")
                ),
                IceTombTreasure(),
                new Threshold(0.01, new ItemLoot("Lil' Ysolde Generator", 0.02))
            )
            // Hrimgar, the Frost Reaver: he hunts you round the hall, axes thrown to bleed you and
            // fangs, then leaps on someone and bursts a ring of hailstones where he lands; at 50% his
            // Frenzy (faster, and a spiral of rime through the leaps). Last below 15%: the Shattered
            // Lake.
            .Init(Hrimgar,
                new State(
                    new BossStats(80000, 35, 1.0, 0.35),
                    Encased(),
                    new State("Fight",
                        ReaverCycle(1.0),
                        new HpLessTransition(0.5, "Frenzy")
                    ),
                    new State("Frenzy",
                        new EncounterCue(37, shake: true),
                        new Taunt(true, "HA! NOW we fight!"),
                        new Flash(0xFFFF6060, 0.2, 6),
                        new State("F",
                            ReaverCycle(0.75),
                            new Shoot(0, 2, 180, 3, fixedAngle: 0, rotateAngle: 17, coolDown: 700),
                            new HpLessTransition(0.15, "Fight 3")
                        )
                    ),
                    new State("Fight 3",
                        ReaverCycle(0.75),
                        new EntitiesNotExistsTransition(40, "Shattered Lake", Kaldrun, Ysolde)
                    ),
                    new State("Shattered Lake",
                        new EncounterCue(40, shake: true),
                        new SetAltTexture(1),
                        new Order(40, "Court Heart", "Shattered Lake"),
                        new Taunt(true, "The ice breaks! Swim, little ones!"),
                        new Prioritize(new Follow(0.5, CourtSight, 4)),
                        new Shoot(CourtSight, 2, 16, 0, predictive: 0.4, coolDown: 1100),
                        new Shoot(CourtSight, 3, 14, 1, predictive: 0.4, coolDown: 1500),
                        new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 3000)
                    ),
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

        private static IStateChildren[] WardenKitArr() => new IStateChildren[]
        {
            new Shoot(CourtSight, 3, 12, 0, predictive: 0.4, coolDown: 1900),
            new Shoot(0, 12, 30, 1, fixedAngle: 0, rotateAngle: 15, coolDown: 3000, coolDownOffset: 1500),
            new Shoot(CourtSight, 1, projectileIndex: 2, predictive: 0.6, coolDown: 1100),
        };

        private static State WardenKit() => new State(WardenKitArr());

        private static State OracleKit() => new State(
            new Shoot(CourtSight, 3, 14, 0, predictive: 0.3, coolDown: 1500),
            new Shoot(CourtSight, 2, 24, 1, coolDown: 2600, coolDownOffset: 800),
            new Shoot(0, 10, 36, 2, fixedAngle: 0, rotateAngle: 18, coolDown: 3000, coolDownOffset: 1500)
        );

        // Hrimgar's hunt: he runs you down throwing axes and fangs, then leaps, and his landing bursts
        // a ring of hailstones. `pace` shortens it all (his Frenzy).
        private static State ReaverCycle(double pace) => new State(
            new State("Hunt",
                new Prioritize(new Follow(0.6 / pace, CourtSight, 4), new Orbit(0.5, 8, CourtSight, "Court Heart")),
                new Shoot(10, 2, 16, 0, predictive: 0.3, coolDown: (int)(1000 * pace)),
                new Shoot(14, 3, 14, 1, predictive: 0.4, coolDown: (int)(1400 * pace)),
                new TimedTransition((int)(5500 * pace), "Leap Brace")
            ),
            new State("Leap Brace",
                new Flash(FrostGlow, 0.15, 3),
                new TimedTransition(700, "Leap")
            ),
            new State("Leap",
                new Charge(2.4, (float)CourtSight, 4000),
                new TimedTransition(1000, "Land")
            ),
            new State("Land",
                new Shoot(0, 14, 360.0 / 14, 2, fixedAngle: 0, coolDown: 99999),
                new Shoot(0, 8, 45, 4, fixedAngle: 22.5, coolDown: 99999, coolDownOffset: 300),
                new TimedTransition(900, "Hunt")
            )
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
                new Prioritize(new Orbit(0.25, 14, 40, "Court Heart", radiusVariance: 0.1)),
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
