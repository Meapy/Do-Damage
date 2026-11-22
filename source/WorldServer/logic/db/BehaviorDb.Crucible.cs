using System;
using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.behaviors.doomed;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Crucible (tools/build_crucible.py, CrucibleWorld.cs, Shared.crucible.CrucibleRules): the
    // boss-only tower over the Arena, five floors and five champions, for players who reached round
    // 50 of the Arena (or hold a Gilded Realm Pass).
    //
    // Each floor's invisible Heart, at its centre, changes the floor: the champions Order it into
    // its states (each a CrucibleFloor from behaviors/Crucible.cs: arms turning, waves spreading,
    // the tide sweeping or rising, lava veins, a clock's hours lighting, the edge crumbling away),
    // and each state's floor goes back as it ends. A Heart's first state, Idle, is quiet: CrucibleWorld
    // puts it back there when its champion falls. Each champion announces its phases to everyone's
    // screen (CrucibleCue: the phase's name, CrucibleCues) and most survival phases shake the ground.
    //
    // The champions arrive at their floor's middle (CrucibleWorld spawns them there), so their spawn
    // point is the middle, and every one sees the whole floor (Sight). Health scales with players
    // (BossStats, 0.35 a player after the first, halved for the Crucible as in the other hardest
    // places, GroupScaling: each player after the first adds about a sixth).
    partial class BehaviorDb
    {
        private const double CrucibleSight = 30;
        private const uint IronGlow = 0xFFFF9A2A, TideGlow = 0xFF46C2B4, LavaGlow = 0xFFFF6A14, BrassGlow = 0xFFF2D282,
            ChronoGlow = 0xFF4AF0FF, SovereignGlow = 0xFFFFD04A, VoidGlow = 0xFFE040FF;

        private _ CrucibleTower = () => Behav()
            // ================================================================================
            // I. The Iron Bastion: Ferrogrim, the Iron Warden
            // - 100% to 70%: his kit (shard fans, a slow armour-breaking hammer wave, iron rings) and
            //   Hammerfall in turn: he slams the floor and molten rings spread out across it, each
            //   with two gaps to step through.
            // - 70%: Lockdown (a survival phase): grates rise in a cross, cutting the floor into
            //   quarters, an Iron Sentinel in each; he waits untouchable in the middle until the four
            //   are broken (or 30 s pass).
            // - 70% to 35%: Grinding Blades: three blades of the floor turn round him while he fights
            //   on, charging and leaping on players; every so often a Hammerfall with them.
            // - 35%: Iron Tempest: his hammer whirls; four blades turning the other way and molten
            //   rings; three Bastion Banners rise, and while any stands he is armoured.
            .Init("Bastion Heart",
                new State(
                    new State("Idle"),
                    new State("Hammerfall", new FloorWave("Molten Grate", 13.5, 7, 2, 70, everyMs: 2400)),
                    new State("Lockdown",
                        new Rampart("Bastion Grate", 13.5, (dx, dy) => (dx == 0 || dy == 0) && Math.Abs(dx) + Math.Abs(dy) >= 3),
                        new SpawnRing("Iron Sentinel", 4, 8, 45, aroundSpawn: true),
                        new RemoveEntitiesOnExit(20, "Iron Sentinel")
                    ),
                    new State("Blades", new SweepingArms("Grinding Blade", 3, 3, 13.5, 1.6, 26)),
                    new State("Blades Wave", new FloorLayers(
                        new SweepingArms("Grinding Blade", 3, 3, 13.5, 1.6, 26, warnMs: 0),
                        new FloorWave("Molten Grate", 13.5, 7, 2, 70, everyMs: 3000))),
                    new State("Tempest", new FloorLayers(
                        new FloorWave("Molten Grate", 13.5, 6, 3, 60, everyMs: 3200),
                        new SweepingArms("Grinding Blade", 4, 3, 13.5, 1.5, -34)))
                )
            )
            .Init("Ferrogrim, the Iron Warden",
                new State(
                    new BossStats(150000, 50, 1.0, 0.35),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(IronGlow, 0.3, 6),
                        new Taunt(true, "Another climber. The Bastion has not fallen in a thousand years.") { Bubble = false },
                        new TimedTransition(3000, "Guard")
                    ),
                    new State("Guard",
                        new HpLessTransition(0.7, "Lockdown"),
                        new State("Guard Fight",
                            new Order(40, "Bastion Heart", "Idle"),
                            new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.5, CrucibleSight, 3)),
                            IronKit(1.0),
                            new TimedTransition(7000, "Hammerfall")
                        ),
                        new State("Hammerfall",
                            new CrucibleCue(1, shake: true),
                            new CrucibleSlam(4, 260, IronGlow),
                            new Order(40, "Bastion Heart", "Hammerfall"),
                            new Taunt(0.5, "Feel the anvil!") { Bubble = false },
                            new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 2, coolDown: 2400),
                            new Shoot(CrucibleSight, 5, 12, projectileIndex: 0, predictive: 0.4, coolDown: 1600),
                            new TimedTransition(8000, "Guard Fight")
                        )
                    ),
                    new State("Lockdown",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(2, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Bastion Heart", "Lockdown"),
                        new Taunt(true, "The gates are shut. Break my sentinels, if you can.") { Bubble = false },
                        new Shoot(0, 8, fixedAngle: 0, rotateAngle: 11, projectileIndex: 2, coolDown: 1800),
                        new State("Lockdown Rise", new TimedTransition(2000, "Lockdown Hold")),
                        new State("Lockdown Hold",
                            new EntityNotExistsTransition("Iron Sentinel", 30, "Blades Rise"),
                            new TimedTransition(30000, "Blades Rise")
                        )
                    ),
                    new State("Blades Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(40, "Bastion Heart", "Blades"),
                        new CrucibleCue(3),
                        new Taunt(true, "Then the Bastion itself will grind you down.") { Bubble = false },
                        new TimedTransition(1800, "Blades")
                    ),
                    new State("Blades",
                        new HpLessTransition(0.35, "Tempest Rise"),
                        new State("Blades Fight",
                            new Order(40, "Bastion Heart", "Blades"),
                            new Prioritize(new StayCloseToSpawn(0.7, 8), new Follow(0.6, CrucibleSight, 3)),
                            IronKit(0.9),
                            new Charge(1.6, 12, 4500),
                            new LeapStrike(CrucibleSight, 2.8, 320, 1500, 7000, 2, 14),
                            new TimedTransition(9000, "Blades Slam")
                        ),
                        new State("Blades Slam",
                            new CrucibleSlam(4, 260, IronGlow),
                            new Order(40, "Bastion Heart", "Blades Wave"),
                            new Shoot(CrucibleSight, 5, 12, projectileIndex: 0, predictive: 0.4, coolDown: 1400),
                            new Shoot(CrucibleSight, 1, projectileIndex: 1, coolDown: 2400),
                            new TimedTransition(7000, "Blades Fight")
                        )
                    ),
                    new State("Tempest Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(4, shake: true),
                        new Flash(IronGlow, 0.2, 10),
                        new Taunt(true, "IRON TEMPEST!") { Bubble = false },
                        new Order(40, "Bastion Heart", "Tempest"),
                        new TimedTransition(2200, "Tempest")
                    ),
                    new State("Tempest",
                        new SetAltTexture(1),
                        new Order(40, "Bastion Heart", "Tempest"),
                        new SpawnRing("Bastion Banner", 3, 6, 90, aroundSpawn: true),
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new Follow(0.45, CrucibleSight, 2)),
                        // the whirling hammer throws its waves out in a turning spiral
                        new Shoot(0, 3, 120, fixedAngle: 0, rotateAngle: 17, projectileIndex: 3, coolDown: 420),
                        new Shoot(CrucibleSight, 5, 12, projectileIndex: 0, predictive: 0.4, coolDown: 1500),
                        new State("Tempest Banners",
                            new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                            new State("TB Wait", new TimedTransition(1000, "TB Hold")),
                            new State("TB Hold", new EntityNotExistsTransition("Bastion Banner", 30, "Tempest Bare"))
                        ),
                        new State("Tempest Bare",
                            new Taunt(true, "My standards... no matter. I need no banner.") { Bubble = false },
                            new LeapStrike(CrucibleSight, 2.8, 320, 1500, 6000, 2, 14)
                        )
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Defense", 0.5),
                    new ItemLoot("Potion of Attack", 0.5),
                    new ItemLoot("Greater Potion of Life", 0.15),
                    new ItemLoot("Ascended Armor Shard", 0.06),
                    new ItemLoot("Ferrogrim's Bastion Turret", 0.01), // the white bag (Player.CruciblePowers.cs)
                    // its pet, 1 in 100 (written twice that: Loots.PetDropRate halves every pet)
                    new ItemLoot("Lil' Ferrogrim Generator", 0.02)
                )
            )
            // the white bags' allies (Player.CruciblePowers.cs): the Tidepearl's substitute, a
            // volley of Nerissa's tridents (Ferrogrim's Bastion Turret is a Mechanic's turret,
            // Turret.cs, with no behaviour)
            .Init("Tidal Substitute",
                new State(
                    new State("fire", new AllyShoot(10, 5, shootAngle: 12, projectileIndex: 0, coolDown: 10000), new TimedTransition(500, "die")),
                    new State("die", new Suicide())
                )
            )
            .Init("Iron Sentinel",
                new State(
                    new Shoot(CrucibleSight, 3, 12, projectileIndex: 0, predictive: 0.3, coolDown: 1700),
                    new Shoot(0, 6, fixedAngle: 0, rotateAngle: 20, projectileIndex: 0, coolDown: 3200, coolDownOffset: 1000)
                )
            )
            .Init("Bastion Banner",
                new State(
                    new Shoot(0, 8, fixedAngle: 0, rotateAngle: 22, projectileIndex: 0, coolDown: 2600)
                )
            )

            // ================================================================================
            // II. The Drowned Sanctum: Nerissa, the Tidecaller
            // - 100% to 66%: trident volleys, rings of tide orbs that sway as they spread (and slow),
            //   and the Tidal Lanes in turn: waves sweep across the floor from side to side, each
            //   shown first at its edge with the dry lanes left through it.
            // - 66%: Rising Tide (a survival phase): the sea comes in from the edge until only the
            //   five coral isles are dry; the deep water drags and stings, and riptide eels hunt in it.
            // - 66% to 33%: the Whirlpool: three arms of surging tide spiral round her.
            // - 33%: Maelstrom: the whirlpool and the lanes together, and her kit faster.
            .Init("Sanctum Heart",
                new State(
                    new State("Idle"),
                    new State("Lanes", new TideSweep("Surging Tide", 15, 2.5, 7.5, 3, 2.4, everyMs: 4200)),
                    new State("Flood",
                        new Flood("Deep Tide", 15, (dx, dy) => Math.Sqrt(dx * dx + dy * dy) <= 2.8
                            || Math.Sqrt((Math.Abs(dx) - 7) * (Math.Abs(dx) - 7) + (Math.Abs(dy) - 7) * (Math.Abs(dy) - 7)) <= 2.4),
                        new SpawnRing("Riptide Eel", 4, 11, 0, aroundSpawn: true),
                        new RemoveEntitiesOnExit(25, "Riptide Eel")
                    ),
                    new State("Whirlpool", new SweepingArms("Surging Tide", 3, 3, 15, 1.7, 30, curve: 14)),
                    new State("Maelstrom", new FloorLayers(
                        new SweepingArms("Surging Tide", 3, 3, 15, 1.6, -36, curve: -16),
                        new TideSweep("Surging Tide", 15, 2, 8, 3, 2.6, everyMs: 6000)))
                )
            )
            .Init("Nerissa, the Tidecaller",
                new State(
                    new BossStats(180000, 45, 1.0, 0.35),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(TideGlow, 0.3, 6),
                        new Taunt(true, "The tide answers to me, little climbers. It always has.") { Bubble = false },
                        new TimedTransition(3000, "Current")
                    ),
                    new State("Current",
                        new HpLessTransition(0.66, "Rising"),
                        new State("Current Fight",
                            new Order(40, "Sanctum Heart", "Idle"),
                            new Prioritize(new StayCloseToSpawn(0.7, 9), new Wander(0.5)),
                            TideKit(1.0),
                            new TimedTransition(7000, "Lanes")
                        ),
                        new State("Lanes",
                            new CrucibleCue(5),
                            new Order(40, "Sanctum Heart", "Lanes"),
                            new Taunt(0.5, "Swim, if you can.") { Bubble = false },
                            new ReturnToSpawn(0.8),
                            new Shoot(CrucibleSight, 3, 10, projectileIndex: 3, predictive: 0.4, coolDown: 900),
                            new Shoot(0, 10, fixedAngle: 0, rotateAngle: 13, projectileIndex: 1, coolDown: 3000),
                            new TimedTransition(13000, "Current Fight")
                        )
                    ),
                    new State("Rising",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(6, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Sanctum Heart", "Flood"),
                        new Flash(TideGlow, 0.25, 12),
                        new Taunt(true, "Rise, sea. Take them.") { Bubble = false },
                        new Shoot(0, 14, fixedAngle: 0, rotateAngle: 9, projectileIndex: 2, coolDown: 1700),
                        new Shoot(CrucibleSight, 3, 10, projectileIndex: 0, predictive: 0.3, coolDown: 1600),
                        new TimedTransition(20000, "Whirl")
                    ),
                    new State("Whirl",
                        new HpLessTransition(0.33, "Maelstrom Rise"),
                        new CrucibleCue(7),
                        new Order(40, "Sanctum Heart", "Whirlpool"),
                        new Prioritize(new StayCloseToSpawn(0.7, 6), new Wander(0.5)),
                        TideKit(0.9),
                        new Spawn("Riptide Eel", 2, 0, 9000)
                    ),
                    new State("Maelstrom Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(8, shake: true),
                        new Flash(TideGlow, 0.2, 10),
                        new Taunt(true, "The whole sea, then. MAELSTROM!") { Bubble = false },
                        new TimedTransition(2200, "Maelstrom")
                    ),
                    new State("Maelstrom",
                        new SetAltTexture(1),
                        new Order(40, "Sanctum Heart", "Maelstrom"),
                        new Prioritize(new StayCloseToSpawn(0.8, 7), new Wander(0.6)),
                        TideKit(0.75),
                        new Shoot(0, 2, 180, fixedAngle: 0, rotateAngle: 14, projectileIndex: 2, coolDown: 360)
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Potion of Dexterity", 0.5),
                    new ItemLoot("Potion of Speed", 0.5),
                    new ItemLoot("Greater Potion of Mana", 0.15),
                    new ItemLoot("Ascended Accessory Shard", 0.06),
                    new ItemLoot("Nerissa's Tidepearl", 0.01), // the white bag (Player.CruciblePowers.cs)
                    // its pet, 1 in 100 (written twice that: Loots.PetDropRate halves every pet)
                    new ItemLoot("Lil' Nerissa Generator", 0.02)
                )
            )
            .Init("Riptide Eel",
                new State(
                    new Prioritize(new Follow(0.9, 14, 1), new Wander(0.5)),
                    new Shoot(8, 1, projectileIndex: 0, predictive: 0.4, coolDown: 900)
                )
            )

            // ================================================================================
            // III. The Ember Foundry: Pyrrhax, the Forge Tyrant
            // - 100% to 70%: magma, cinder fans and rings; Lava Veins crack out across the floor at
            //   the players, glowing first, burning, then cooling to slag; the Anvil Drop (he leaps
            //   onto a marked spot).
            // - 70%: Meteor Storm (a survival phase): meteors rain over the whole floor, marked as
            //   they fall, and molten golems climb out of the forge.
            // - 70% to 35%: the veins again, with leaps and golems.
            // - 35%: The Crucible Pours: the floor's edge cracks and falls away into the sky down to
            //   eight squares from the middle, for the rest of the fight; then Molten Core: veins on
            //   what's left, meteors, a spiral of magma.
            .Init("Foundry Heart",
                new State(
                    new State("Idle"),
                    new State("Veins", new LavaVeins("Forge Lava", "Cooling Slag", 3, 13, 15, everyMs: 3400)),
                    new State("Meteors",
                        new LavaVeins("Forge Lava", "Cooling Slag", 2, 10, 15, everyMs: 5000),
                        new Starfall(Starfall.Area.Scatter, 14, 2.2, 340, 420, perVolley: 2, color: LavaGlow)
                    ),
                    new State("Broken",
                        new Crumble(8, 16, fallMs: 5000, warnMs: 1600),
                        new State("Pour"),
                        new State("Molten",
                            new LavaVeins("Forge Lava", "Cooling Slag", 3, 7, 8, everyMs: 3000, burnMs: 2200),
                            new Starfall(Starfall.Area.Players, CrucibleSight, 1.8, 300, 2400, color: LavaGlow)
                        )
                    )
                )
            )
            .Init("Pyrrhax, the Forge Tyrant",
                new State(
                    new BossStats(210000, 55, 1.0, 0.35),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(LavaGlow, 0.3, 6),
                        new Taunt(true, "Fresh ore for the furnace. Let's see what you're made of.") { Bubble = false },
                        new TimedTransition(3000, "Smelt")
                    ),
                    new State("Smelt",
                        new HpLessTransition(0.7, "Storm"),
                        new CrucibleCue(9),
                        new Order(40, "Foundry Heart", "Veins"),
                        new Prioritize(new StayCloseToSpawn(0.6, 8), new Follow(0.5, CrucibleSight, 4)),
                        ForgeKit(1.0),
                        new LeapStrike(CrucibleSight, 3, 350, 1400, 7500, 2, 16)
                    ),
                    new State("Storm",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(10, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Foundry Heart", "Meteors"),
                        new Flash(LavaGlow, 0.25, 10),
                        new Taunt(true, "The sky is a forge too. LOOK UP.") { Bubble = false },
                        new Spawn("Molten Golem", 4, 0.5, 3500),
                        new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 2, coolDown: 2200),
                        new TimedTransition(14000, "Hammer")
                    ),
                    new State("Hammer",
                        new HpLessTransition(0.35, "Pour"),
                        new Order(40, "Foundry Heart", "Veins"),
                        new Prioritize(new StayCloseToSpawn(0.7, 8), new Follow(0.6, CrucibleSight, 4)),
                        ForgeKit(0.85),
                        new LeapStrike(CrucibleSight, 3, 350, 1400, 6000, 2, 16),
                        new Spawn("Molten Golem", 3, 0, 9000)
                    ),
                    new State("Pour",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(11, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Foundry Heart", "Pour"),
                        new Flash(LavaGlow, 0.2, 14),
                        new Taunt(true, "Into the crucible with you!") { Bubble = false },
                        new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 1, coolDown: 1500),
                        new TimedTransition(7000, "Molten Rise")
                    ),
                    new State("Molten Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(1),
                        new CrucibleCue(12),
                        new Taunt(true, "Burn with me. MOLTEN CORE!") { Bubble = false },
                        new TimedTransition(1800, "Molten")
                    ),
                    new State("Molten",
                        new SetAltTexture(1),
                        new Order(40, "Foundry Heart", "Molten"),
                        new Prioritize(new StayCloseToSpawn(0.6, 5), new Follow(0.5, CrucibleSight, 3)),
                        ForgeKit(0.8),
                        new Shoot(0, 3, 120, fixedAngle: 0, rotateAngle: 13, projectileIndex: 3, coolDown: 520)
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Vitality", 0.5),
                    new ItemLoot("Potion of Defense", 0.5),
                    new ItemLoot("Greater Potion of Life", 0.2),
                    new ItemLoot("Ascended Weapon Shard", 0.06),
                    new ItemLoot("Pyrrhax's Meteor Mortar", 0.01), // the white bag (Player.CruciblePowers.cs)
                    // its pet, 1 in 100 (written twice that: Loots.PetDropRate halves every pet)
                    new ItemLoot("Lil' Pyrrhax Generator", 0.02)
                )
            )
            .Init("Molten Golem",
                new State(
                    new Prioritize(new Follow(0.6, 20, 1), new Wander(0.4)),
                    new Shoot(10, 3, 15, projectileIndex: 0, coolDown: 1400)
                )
            )

            // ================================================================================
            // IV. The Clockwork Orrery: Chronarch, the Hour Thief
            // - 100% to 70%: gear rings, fans of hand-blades, slow chrono orbs; the Hour Hands: his
            //   floor is a clock, and its hour hand (broad, slow) and minute hand (thin, quick) sweep
            //   round it; clockwork sentries now and then.
            // - 70%: Midnight Chime (a survival phase): the clock's hours light in patterns, half at
            //   a time, each glowing first and then burning.
            // - 70% to 35%: Time Fracture: two Hour Echoes, untouchable copies, fight beside him
            //   and he blinks about the floor; the hands sweep on.
            // - 35%: Reversal: the hands run backwards and faster, the hours chime under them.
            .Init("Orrery Heart",
                new State(
                    new State("Idle"),
                    new State("Hands", new FloorLayers(
                        new SweepingArms("Hour Hand", 1, 2.5, 14.5, 2.4, 14),
                        new SweepingArms("Hour Hand", 1, 2.5, 14.5, 1.4, 42))),
                    new State("Chime", new SectorChime("Chime Light", 12, 3, 14.5, new[]
                    {
                        "101010101010", "010101010101", "110011001100", "001100110011", "111000111000", "000111000111",
                    })),
                    new State("Reversal", new FloorLayers(
                        new SectorChime("Chime Light", 12, 3, 14.5, null, warnMs: 1300, litMs: 1100, gapMs: 1400),
                        new SweepingArms("Hour Hand", 1, 2.5, 14.5, 2.2, -22, warnMs: 0),
                        new SweepingArms("Hour Hand", 1, 2.5, 14.5, 1.3, -60, warnMs: 0)))
                )
            )
            .Init("Chronarch, the Hour Thief",
                new State(
                    new BossStats(240000, 60, 1.0, 0.35),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(BrassGlow, 0.3, 6),
                        new Taunt(true, "You are late. Everyone is, eventually.") { Bubble = false },
                        new TimedTransition(3000, "Tick")
                    ),
                    new State("Tick",
                        new HpLessTransition(0.7, "Midnight"),
                        new CrucibleCue(13),
                        new Order(40, "Orrery Heart", "Hands"),
                        new Prioritize(new StayCloseToSpawn(0.5, 5), new Wander(0.3)),
                        ClockKit(1.0),
                        new Spawn("Clockwork Sentry", 3, 0, 10000)
                    ),
                    new State("Midnight",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(14, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Orrery Heart", "Chime"),
                        new Flash(BrassGlow, 0.25, 12),
                        new Taunt(true, "Midnight. Hear the bells toll for you.") { Bubble = false },
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 12, projectileIndex: 3, coolDown: 1200),
                        new TimedTransition(16000, "Fracture")
                    ),
                    new State("Fracture",
                        new HpLessTransition(0.35, "Reversal Rise"),
                        new CrucibleCue(15, shake: true),
                        new Order(40, "Orrery Heart", "Hands"),
                        new SpawnRing("Hour Echo", 2, 8, 0, aroundSpawn: true),
                        new RemoveEntitiesOnExit(40, "Hour Echo"),
                        new Taunt(true, "Which of me is now? All of us.") { Bubble = false },
                        new State("Fracture Fight",
                            new Prioritize(new StayCloseToSpawn(0.6, 9), new Wander(0.4)),
                            ClockKit(0.9),
                            new TimedTransition(5000, "Fracture Blink")
                        ),
                        new State("Fracture Blink",
                            new BlinkAround(4, 11),
                            new Flash(ChronoGlow, 0.2, 2),
                            new Shoot(0, 12, fixedAngle: 0, projectileIndex: 0, coolDown: 90000000, coolDownOffset: 300),
                            new TimedTransition(500, "Fracture Fight")
                        )
                    ),
                    new State("Reversal Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(16, shake: true),
                        new Flash(ChronoGlow, 0.2, 10),
                        new Taunt(true, "Back. Again. Back. Again.") { Bubble = false },
                        new TimedTransition(2200, "Reversal")
                    ),
                    new State("Reversal",
                        new SetAltTexture(1),
                        new Order(40, "Orrery Heart", "Reversal"),
                        new Prioritize(new StayCloseToSpawn(0.5, 5), new Wander(0.4)),
                        ClockKit(0.8),
                        new Shoot(0, 4, 90, fixedAngle: 0, rotateAngle: -12, projectileIndex: 3, coolDown: 600)
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Speed", 1),
                    new ItemLoot("Potion of Dexterity", 1),
                    new ItemLoot("Greater Potion of Life", 0.2),
                    new ItemLoot("Greater Potion of Mana", 0.2),
                    new ItemLoot("Ascended Ability Shard", 0.06),
                    new ItemLoot("Chronarch's Coat of Stolen Hours", 0.01), // the white bag (Player.CruciblePowers.cs)
                    // its pet, 1 in 100 (written twice that: Loots.PetDropRate halves every pet)
                    new ItemLoot("Lil' Chronarch Generator", 0.02)
                )
            )
            .Init("Hour Echo",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Prioritize(new StayCloseToSpawn(0.5, 3), new Wander(0.3)),
                    new Shoot(CrucibleSight, 3, 14, projectileIndex: 0, predictive: 0.3, coolDown: 2200),
                    new Shoot(0, 8, fixedAngle: 22.5, rotateAngle: 15, projectileIndex: 1, coolDown: 3400, coolDownOffset: 1500)
                )
            )
            .Init("Clockwork Sentry",
                new State(
                    new Prioritize(new Follow(0.7, 18, 3), new Wander(0.4)),
                    new Shoot(12, 1, projectileIndex: 0, predictive: 0.3, coolDown: 1100)
                )
            )

            // ================================================================================
            // V. The Apex: Vaelith, the Crucible Sovereign
            // - 100% to 80%: Sovereign's Decree: lances, rings of flame feathers that sway, a
            //   spiral of stars, gold strikes falling on the players.
            // - 80%, 65%, 50%, 35%: the four echoes, one after another: the Apex reshapes itself
            //   into each fallen champion's floor, made over (ApexForm: a fortress with pits in its
            //   yard, isles in a deep sea, a cross over the sky with furnaces floating off it, an
            //   orrery whose spokes turn), with its hazards, and that champion's ghost fights from
            //   the rim: iron (blades and molten rings), tide (sweeps), forge (lava veins and
            //   meteors), hours (the chime and the hands).
            // - 20%: The Crucible Breaks (a survival phase): the Apex shatters, a disc of its middle
            //   left with its shards floating round it, the four ghosts together, gold raining.
            // - Then Unbound to the end: the Apex re-forms as a halo of void marble round the void
            //   itself, flame arms turning and waves of fire across it, everything faster.
            .Init("Apex Heart",
                new State(
                    new State("Idle"),
                    new State("Fight",
                        new ApexForm(),
                        new State("Decree",
                            new Starfall(Starfall.Area.Players, CrucibleSight, 1.7, 320, 2600, color: SovereignGlow)
                        ),
                        new State("Iron",
                            new State("Iron Turn", new TimedTransition(3800, "Iron Go")),
                            new State("Iron Go", new FloorLayers(
                                new SweepingArms("Grinding Blade", 3, 3, 19, 1.6, 26),
                                new FloorWave("Molten Grate", 20, 7, 2, 70, everyMs: 3800)))
                        ),
                        new State("Tide",
                            new State("Tide Turn", new TimedTransition(3800, "Tide Go")),
                            new State("Tide Go", new TideSweep("Surging Tide", 20, 2.5, 8, 3, 2.6, everyMs: 4200))
                        ),
                        new State("Forge",
                            new State("Forge Turn", new TimedTransition(3800, "Forge Go")),
                            new State("Forge Go",
                                new LavaVeins("Forge Lava", "Cooling Slag", 3, 17, 21, everyMs: 3600),
                                new Starfall(Starfall.Area.Scatter, 18, 2.2, 340, 900, color: LavaGlow)
                            )
                        ),
                        new State("Hours",
                            new State("Hours Turn", new TimedTransition(3800, "Hours Go")),
                            new State("Hours Go", new FloorLayers(
                                new SectorChime("Chime Light", 12, 3, 21, null, warnMs: 1300, litMs: 1200, gapMs: 1800),
                                new SweepingArms("Hour Hand", 1, 2.5, 21, 2.2, 14, warnMs: 0)))
                        ),
                        new State("Broken",
                            new State("Breaks",
                                new Starfall(Starfall.Area.Scatter, 8, 1.8, 300, 700, color: SovereignGlow)
                            ),
                            new State("Unbound", new FloorLayers(
                                new SweepingArms("Sovereign Flame", 2, 5, 16, 1.6, 32),
                                new FloorWave("Sovereign Flame", 16, 5, 2, 80, everyMs: 4400)))
                        )
                    )
                )
            )
            .Init("Vaelith, the Crucible Sovereign",
                new State(
                    new BossStats(340000, 70, 1.0, 0.35),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(SovereignGlow, 0.3, 10),
                        new State("Arrive 1",
                            new Taunt(true, "Four champions fell to bring you here. Good.") { Bubble = false },
                            new TimedTransition(3000, "Arrive 2")
                        ),
                        new State("Arrive 2",
                            new Taunt(true, "I built this tower to find the worthy. Show me you are.") { Bubble = false },
                            new TimedTransition(2500, "Decree")
                        )
                    ),
                    new State("Decree",
                        new HpLessTransition(0.8, "Echo Iron"),
                        new CrucibleCue(17),
                        new Order(40, "Apex Heart", "Decree"),
                        new Prioritize(new StayCloseToSpawn(0.7, 8), new Follow(0.6, CrucibleSight, 4)),
                        SovereignKit(1.0)
                    ),
                    Echo("Echo Iron", "Echo Tide", 0.65, 18, "Iron", "Echo of Ferrogrim", "Iron, rise again!", IronGlow),
                    Echo("Echo Tide", "Echo Forge", 0.5, 19, "Tide", "Echo of Nerissa", "Tide, drown them once more!", TideGlow),
                    Echo("Echo Forge", "Echo Hours", 0.35, 20, "Forge", "Echo of Pyrrhax", "Forge, burn!", LavaGlow),
                    Echo("Echo Hours", "Breaks", 0.2, 21, "Hours", "Echo of Chronarch", "Hours, turn against them!", ChronoGlow),
                    new State("Breaks",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(22, shake: true),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new Order(40, "Apex Heart", "Breaks"),
                        new Flash(SovereignGlow, 0.2, 16),
                        new Taunt(true, "Enough! Then let it ALL fall!") { Bubble = false },
                        new SpawnRing("Echo of Ferrogrim", 1, 7, 225, aroundSpawn: true),
                        new SpawnRing("Echo of Nerissa", 1, 7, 315, aroundSpawn: true),
                        new SpawnRing("Echo of Pyrrhax", 1, 7, 45, aroundSpawn: true),
                        new SpawnRing("Echo of Chronarch", 1, 7, 135, aroundSpawn: true),
                        new RemoveEntitiesOnExit(40, "Echo of Ferrogrim", "Echo of Nerissa", "Echo of Pyrrhax", "Echo of Chronarch"),
                        new Shoot(0, 10, fixedAngle: 0, rotateAngle: 12, projectileIndex: 1, coolDown: 2000),
                        new TimedTransition(13000, "Unbound Rise")
                    ),
                    new State("Unbound Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(2),
                        new CrucibleCue(23, shake: true),
                        new Flash(VoidGlow, 0.2, 12),
                        new Taunt(true, "I am the Crucible. I. Do. Not. BREAK.") { Bubble = false },
                        new Order(40, "Apex Heart", "Unbound"),
                        new TimedTransition(2500, "Unbound")
                    ),
                    new State("Unbound",
                        new SetAltTexture(2),
                        new Order(40, "Apex Heart", "Unbound"),
                        new Prioritize(new StayCloseToSpawn(0.6, 4), new Follow(0.5, CrucibleSight, 3)),
                        SovereignKit(0.8),
                        new Shoot(0, 12, fixedAngle: 0, rotateAngle: 9, projectileIndex: 3, coolDown: 2600),
                        new Shoot(CrucibleSight, 7, 9, projectileIndex: 4, predictive: 0.3, coolDown: 2000, coolDownOffset: 1000)
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Greater Potion of Life", 0.5),
                    new ItemLoot("Greater Potion of Mana", 0.5),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Speed", 1),
                    new ItemLoot("Potion of Dexterity", 1),
                    new ItemLoot("Potion of Vitality", 1),
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Potion of Oryx", 0.25),
                    new ItemLoot("Potion of Power", 0.05),
                    new ItemLoot("Vaelith's Final Aria", 0.01), // the white bag: the Sovereign's lute (Player.CruciblePowers.cs)
                    new ItemLoot("Ascended Weapon Shard", 0.15),
                    new ItemLoot("Ascended Ability Shard", 0.15),
                    new ItemLoot("Ascended Armor Shard", 0.15),
                    new ItemLoot("Ascended Accessory Shard", 0.15),
                    // her pet, 1 in 100 (written twice that: Loots.PetDropRate halves every pet)
                    new ItemLoot("Lil' Vaelith Generator", 0.02)
                )
            )
            // the fallen champions' ghosts on the Apex: untouchable, each firing its own shot
            .Init("Echo of Ferrogrim", EchoGhost(2400))
            .Init("Echo of Nerissa", EchoGhost(2600))
            .Init("Echo of Pyrrhax", EchoGhost(2200))
            .Init("Echo of Chronarch", EchoGhost(2800))

            // ================================================================================
            // The champions' pets: each champion drops a little copy of itself (1 in 100), which
            // fights as the champion does and, every 10 s, takes on the champion's last form for 4 s
            // and throws out its spiral. Their shots, and the numbers in their generators' tooltips
            // (damage a second, healing, what else they do), are tools/build_crucible.py's PETS:
            // change both together.
            // shard fans, his armour-breaking hammer wave, iron rings, Hammerfall; Iron Tempest's
            // spiral of hammer waves; and his bastion round you
            .Init("Lil' Ferrogrim", ChampionPet(45,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Armored, 2000, 10000),
                    new PetShoot(10, 5, 6, 0, 0, 1200),
                    new PetShoot(10, 1, 0, 0, 1, 2600),
                    new PetShoot(10, 12, 30, 0, 2, 3200),
                    new PetNova(1250, 1550, 4000, 10, 2.5, ConditionEffectIndex.ArmorBroken, 1500, IronGlow),
                },
                new PetShoot(10, 3, 120, 0, 3, 300, rotateAngle: 17)))
            // trident volleys, her swaying ring of tide orbs turning, bubbles, a tidal wave; the
            // Maelstrom's two arms of bubbles; and the tide's healing for you
            .Init("Lil' Nerissa", ChampionPet(45,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Healing, 2000, 8000),
                    new PetShoot(10, 3, 6, 0, 0, 1100),
                    new PetShoot(10, 10, 36, 0, 1, 2800, rotateAngle: 13),
                    new PetShoot(10, 5, 20, 0, 2, 3600),
                    new PetNova(1450, 1750, 4500, 10, 3, ConditionEffectIndex.Slowed, 1000, TideGlow),
                },
                new PetShoot(10, 2, 180, 0, 3, 300, rotateAngle: 14)))
            // his magma ball, cinder fans and rings, a meteor; Molten Core's spiral of magma; and
            // the forge's heat in you
            .Init("Lil' Pyrrhax", ChampionPet(45,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Berserk, 2000, 10000),
                    new PetShoot(10, 1, 0, 0, 0, 1800),
                    new PetShoot(10, 5, 6, 0, 1, 1100),
                    new PetShoot(10, 16, 22.5, 0, 2, 3000),
                    new PetNova(1150, 1350, 3500, 10, 2.2, null, 0, LavaGlow),
                },
                new PetShoot(10, 3, 120, 0, 3, 400, rotateAngle: 13)))
            // his gear ring turning, hand-blade fans, a slowing chrono orb, the midnight chime;
            // Reversal's four arms of gears turning back; and a stolen moment for you
            .Init("Lil' Chronarch", ChampionPet(45,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Speedy, 2000, 8000),
                    new PetShoot(10, 12, 30, 0, 0, 2400, rotateAngle: 7.5),
                    new PetShoot(10, 3, 6, 0, 1, 1300),
                    new PetShoot(10, 1, 0, 0, 2, 3500),
                    new PetNova(1750, 2100, 4000, 10, 2.5, ConditionEffectIndex.Slowed, 1500, ChronoGlow),
                },
                new PetShoot(10, 4, 90, 0, 3, 500, rotateAngle: -12)))
            // his lances, a swaying ring of flame feathers turning, his spiral of stars, gold
            // striking; Unbound's fans of void embers; and the Sovereign's decree in your strikes
            .Init("Lil' Vaelith", ChampionPet(60,
                new IStateChildren[]
                {
                    new PetOwnerEffect(ConditionEffectIndex.Damaging, 2000, 10000),
                    new PetShoot(10, 3, 4, 0, 0, 1000),
                    new PetShoot(10, 16, 22.5, 0, 1, 2600, rotateAngle: 11),
                    new PetShoot(10, 2, 180, 0, 2, 420, rotateAngle: 11),
                    new PetNova(900, 1100, 3000, 10, 2.5, null, 0, SovereignGlow),
                },
                new PetShoot(10, 7, 4, 0, 3, 800)));

        // ---- the champions' kits, `pace` < 1 faster ----

        private static IStateChildren IronKit(double pace) => new State(
            new Shoot(CrucibleSight, 5, 12, projectileIndex: 0, predictive: 0.4, coolDown: (int)(1400 * pace)),
            new Shoot(CrucibleSight, 1, projectileIndex: 1, coolDown: (int)(2600 * pace), coolDownOffset: 800),
            new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 2, coolDown: (int)(3200 * pace), coolDownOffset: 1600)
        );

        private static IStateChildren TideKit(double pace) => new State(
            new Shoot(CrucibleSight, 3, 10, projectileIndex: 0, predictive: 0.4, coolDown: (int)(1200 * pace)),
            new Shoot(0, 10, fixedAngle: 0, rotateAngle: 13, projectileIndex: 1, coolDown: (int)(2800 * pace), coolDownOffset: 900),
            new Shoot(CrucibleSight, 5, 20, projectileIndex: 2, coolDown: (int)(3600 * pace), coolDownOffset: 1800)
        );

        private static IStateChildren ForgeKit(double pace) => new State(
            new Shoot(CrucibleSight, 1, projectileIndex: 0, predictive: 0.5, coolDown: (int)(1800 * pace)),
            new Shoot(CrucibleSight, 5, 9, projectileIndex: 1, predictive: 0.3, coolDown: (int)(1100 * pace), coolDownOffset: 500),
            new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 2, coolDown: (int)(3000 * pace), coolDownOffset: 1500)
        );

        private static IStateChildren ClockKit(double pace) => new State(
            new Shoot(0, 12, fixedAngle: 0, rotateAngle: 7.5, projectileIndex: 0, coolDown: (int)(2400 * pace)),
            new Shoot(CrucibleSight, 3, 12, projectileIndex: 1, predictive: 0.4, coolDown: (int)(1300 * pace), coolDownOffset: 600),
            new Shoot(CrucibleSight, 1, projectileIndex: 2, coolDown: (int)(3500 * pace), coolDownOffset: 1700)
        );

        private static IStateChildren SovereignKit(double pace) => new State(
            new Shoot(CrucibleSight, 3, 8, projectileIndex: 0, predictive: 0.5, coolDown: (int)(1100 * pace)),
            new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 1, coolDown: (int)(2600 * pace), coolDownOffset: 1300),
            new Shoot(0, 2, 180, fixedAngle: 0, rotateAngle: 11, projectileIndex: 2, coolDown: (int)(420 * pace))
        );

        // one of the Sovereign's echoes: the Apex turns into the fallen champion's floor, the
        // champion's ghost fights from the rim, and the Sovereign fights on from the middle
        private static State Echo(string name, string next, double below, byte cue, string heart, string ghost, string taunt, uint glow) =>
            new State(name,
                new HpLessTransition(below, next),
                new CrucibleCue(cue, shake: true),
                new Order(40, "Apex Heart", heart),
                new Flash(glow, 0.25, 6),
                new Taunt(true, taunt) { Bubble = false },
                new TossObject(ghost, 11, 270, coolDown: 90000000),
                new RemoveEntitiesOnExit(40, ghost),
                new Prioritize(new StayCloseToSpawn(0.6, 5), new Wander(0.4)),
                SovereignKit(1.15)
            );

        // a champion's pet: it follows and heals its owner (heal HP and MP every 1.5 s) and fights
        // with `kit` all the while; every 10 s it takes on the champion's last form (AltTexture 1)
        // for 4 s and adds `signature`, the spiral the champion throws out in its last phase
        private static State ChampionPet(int heal, IStateChildren[] kit, Behavior signature) => new State(
            new IStateChildren[]
            {
                new PetFollow(),
                new PetHeal(false, heal, heal, 1500),
                new PetHeal(true, heal, heal, 1500),
                new State("Calm",
                    new SetAltTexture(0),
                    new TimedTransition(6000, "Signature")
                ),
                new State("Signature",
                    new SetAltTexture(1),
                    signature,
                    new TimedTransition(4000, "Calm")
                ),
            }.Concat(kit).ToArray()
        );

        private static State EchoGhost(int coolDown) => new State(
            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
            new Flash(0xFFFFFFFF, 0.4, 3),
            new Prioritize(new StayCloseToSpawn(0.3, 2), new Wander(0.2)),
            new Shoot(CrucibleSight, 3, 14, projectileIndex: 0, predictive: 0.3, coolDown: coolDown),
            new Shoot(0, 8, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 0, coolDown: coolDown * 2, coolDownOffset: coolDown / 2)
        );
    }
}
