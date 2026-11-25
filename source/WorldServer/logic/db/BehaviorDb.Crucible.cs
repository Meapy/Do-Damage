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
    //
    // Every champion also carries CrucibleCaches: while it can be hurt, the tower drops caches
    // beside the climbers to break, each launching its charge at the champion for a share of its
    // health and an affliction past its immunities (behaviors/CrucibleCaches.cs).
    partial class BehaviorDb
    {
        private const double CrucibleSight = 40;
        private const uint IronGlow = 0xFFFF9A2A, TideGlow = 0xFF46C2B4, LavaGlow = 0xFFFF6A14, BrassGlow = 0xFFF2D282,
            ChronoGlow = 0xFF4AF0FF, SovereignGlow = 0xFFFFD04A, VoidGlow = 0xFFE040FF;

        private _ CrucibleTower = () => Behav()
            // ================================================================================
            // I. The Iron Bastion: Ferrogrim, the Iron Warden
            // Each phase has at most one thing on the floor, shown first; the danger is in his shots.
            // - 100% to 70%: his kit (shard fans, an armour-breaking hammer wave, iron rings, the Iron
            //   Wall: a line of shards to sidestep, Rending Volley, hounds), then the Shield Charge
            //   (he braces with a flash, rams at you and bursts a double ring where he stops), then
            //   Hammerfall: he slams the floor, two rings of molten grate race out
            //   (three wide gaps each), and he throws a cross of hammer waves that turns each throw
            //   and a double ring of shards, the second in the first's gaps.
            // - 70%: Lockdown (a survival phase): grates rise in a cross, cutting the floor into
            //   quarters, an Iron Sentinel in each; he waits untouchable in the middle until the four
            //   are broken (or 30 s pass), turning out a four-armed spiral of shards and his anvil's
            //   cross of hammer waves.
            // - 70% to 35%: Grinding Blades: two slow blades of the floor turn round him while he
            //   fights on and leaps on players; every so often a slam: the double ring and a hammer
            //   wave at each of you.
            // - 35%: Iron Tempest: his hammer whirls, dragging two blades round the star fort the
            //   other way; two spirals of shards turning opposite ways weave a lattice round him,
            //   hounds run at you; three Bastion Banners rise, and while any
            //   stands he is armoured.
            // - 12%: Last Bastion: the floor falls back to the inner keep, square, and he holds its
            //   middle; its walls close in on him one after another, molten, each with two gates on
            //   opposite sides that keep their place as it closes: slip out through one. He throws
            //   his anvil's cross and double rings at whoever is caught inside with him.
            .Init("Bastion Heart",
                new State(
                    new State("Idle"),
                    new State("Fight",
                        new ArenaForm(),
                        new State("Hammerfall", new FloorWave("Molten Grate", G(13.5), 6.5, 3, 80, everyMs: 3000)),
                        new State("Lockdown",
                            // the drawbridges raised: nobody reaches him on his dais
                            new Rampart("Bastion Grate", G(13.5), (dx, dy) => Drawbridge(ArenaForms.Design(dx), ArenaForms.Design(dy))),
                            new SpawnRing("Iron Sentinel", 4, G(14), 45, aroundSpawn: true),
                            new RemoveEntitiesOnExit(20, "Iron Sentinel")
                        ),
                        new State("Blades", new SweepingArms("Grinding Blade", 2, G(3), G(15.5), 2.2, 22)),
                        // the whirlwind drags two blades round the star fort, against his spirals
                        new State("Tempest", new SweepingArms("Grinding Blade", 2, G(4), G(17), 2.2, -22)),
                        // the keep's walls closing in, one at a time, two gates in each
                        new State("Bastion", new ClosingWalls("Molten Grate", G(9), G(2), G(1.6), 2, 3.2, everyMs: 6500))
                    )
                )
            )
            .Init("Ferrogrim, the Iron Warden",
                new State(
                    new BossStats(150000, 60, 1.0, 0.35),
                    new CrucibleCaches(), // the tower's caches beside the climbers (CrucibleCaches.cs)
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
                            new Prioritize(new StayCloseToSpawn(0.6, Gi(6)), new Follow(0.5, CrucibleSight, 3)),
                            IronKit(1.0),
                            new TimedTransition(7000, "Shield Charge")
                        ),
                        // he braces behind his shield (a flash: get out of his line), rams at you
                        // and bursts a double ring of shards where he stops
                        new State("Shield Charge",
                            new State("SC Brace",
                                new Flash(IronGlow, 0.15, 5),
                                new Taunt(0.5, "Stand aside!") { Bubble = false },
                                new TimedTransition(900, "SC Run")
                            ),
                            new State("SC Run",
                                new Charge(2.6, (float)CrucibleSight, 6000),
                                // rivets shaken off his armour as he runs
                                new Shoot(0, 6, 60, fixedAngle: 0, rotateAngle: 30, projectileIndex: 2, coolDown: 300),
                                new TimedTransition(1100, "SC Burst")
                            ),
                            new State("SC Burst",
                                DoubleRing(12, 2, 9000, 250),
                                new TimedTransition(700, "Hammerfall")
                            )
                        ),
                        new State("Hammerfall",
                            new CrucibleCue(1, shake: true),
                            new CrucibleSlam(4, 260, IronGlow),
                            new Order(40, "Bastion Heart", "Hammerfall"),
                            new Taunt(0.5, "Feel the anvil!") { Bubble = false },
                            new ReturnToSpawn(0.8),
                            AnvilCross(1400),
                            DoubleRing(12, 2, 2400, 300),
                            new Shoot(CrucibleSight, 5, 12, projectileIndex: 0, predictive: 0.4, coolDown: 2000),
                            new TimedTransition(8000, "Guard Fight")
                        )
                    ),
                    new State("Lockdown",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(2, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Bastion Heart", "Lockdown"),
                        new Taunt(true, "The gates are shut. Break my sentinels, if you can.") { Bubble = false },
                        // a four-armed spiral of shards turning slowly out over the quarters
                        new Shoot(0, 4, 90, fixedAngle: 0, rotateAngle: 9, projectileIndex: 2, coolDown: 320),
                        // and his anvil's cross of hammer waves through it, so the quarters aren't safe
                        AnvilCross(2000),
                        // and his hammer thrown over the walls, spinning back to his hand
                        new Shoot(CrucibleSight, 1, projectileIndex: 3, predictive: 0.5, coolDown: 2800, coolDownOffset: 1400),
                        new State("Lockdown Rise", new TimedTransition(2000, "Lockdown Hold")),
                        new State("Lockdown Hold",
                            new EntityNotExistsTransition("Iron Sentinel", 30, "Blades Rise"),
                            new TimedTransition(30000, "Blades Rise")
                        )
                    ),
                    new State("Blades Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(16, 2),
                        new Order(40, "Bastion Heart", "Blades"),
                        new CrucibleCue(3),
                        new Taunt(true, "Then the Bastion itself will grind you down.") { Bubble = false },
                        new TimedTransition(1800, "Blades")
                    ),
                    new State("Blades",
                        new HpLessTransition(0.35, "Tempest Rise"),
                        new State("Blades Fight",
                            new Order(40, "Bastion Heart", "Blades"),
                            new Prioritize(new StayCloseToSpawn(0.7, Gi(8)), new Follow(0.6, CrucibleSight, 3)),
                            IronKit(0.9),
                            new LeapStrike(CrucibleSight, 2.8, 320, 1500, 5000, 2, 14),
                            new TimedTransition(9000, "Blades Slam")
                        ),
                        new State("Blades Slam",
                            new CrucibleSlam(4, 260, IronGlow),
                            new ReturnToSpawn(0.8),
                            DoubleRing(16, 2, 2000, 300),
                            AnvilCross(1600),
                            new Shoot(CrucibleSight, 1, projectileIndex: 1, coolDown: 2400),
                            // two hammers thrown wide of you, coming back
                            new Shoot(CrucibleSight, 2, 40, projectileIndex: 3, coolDown: 3500, coolDownOffset: 1200),
                            new TimedTransition(7000, "Blades Fight")
                        )
                    ),
                    new State("Tempest Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(16, 2),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(4, shake: true),
                        new Flash(IronGlow, 0.2, 10),
                        new Taunt(true, "IRON TEMPEST!") { Bubble = false },
                        new Order(40, "Bastion Heart", "Tempest"),
                        new TimedTransition(2200, "Tempest")
                    ),
                    new State("Tempest",
                        new HpLessTransition(0.12, "Bastion Rise"),
                        new RemoveEntitiesOnExit(40, "Bastion Banner"),
                        new SetAltTexture(1),
                        new Order(40, "Bastion Heart", "Tempest"),
                        new SpawnRing("Bastion Banner", 4, G(17), 45, aroundSpawn: true), // on the star fort's bastions
                        new Prioritize(new StayCloseToSpawn(0.5, Gi(6)), new Follow(0.45, CrucibleSight, 2)),
                        // the whirling hammer: two spirals of shards turning opposite ways weave a
                        // lattice to slip through, three hammers thrown at each of you now and
                        // then, spinning out and back
                        new Shoot(0, 3, 120, fixedAngle: 0, rotateAngle: 13, projectileIndex: 2, coolDown: 480),
                        new Shoot(0, 3, 120, fixedAngle: 60, rotateAngle: -13, projectileIndex: 2, coolDown: 480, coolDownOffset: 240),
                        new Shoot(CrucibleSight, 3, 30, projectileIndex: 3, predictive: 0.8, coolDown: 1800),
                        Rushers("Iron Hound", 0.9),
                        new State("Tempest Banners",
                            new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                            new State("TB Wait", new TimedTransition(1000, "TB Hold")),
                            new State("TB Hold", new EntityNotExistsTransition("Bastion Banner", 30, "Tempest Bare"))
                        ),
                        new State("Tempest Bare",
                            new Taunt(true, "My standards... no matter. I need no banner.") { Bubble = false },
                            new LeapStrike(CrucibleSight, 2.8, 320, 1500, 6000, 2, 14)
                        )
                    ),
                    new State("Bastion Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(18, 2),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(24, shake: true),
                        new Flash(IronGlow, 0.15, 14),
                        new Taunt(true, "The last wall stands. So do I.") { Bubble = false },
                        new Order(40, "Bastion Heart", "Bastion"),
                        new TimedTransition(2600, "Last Bastion")
                    ),
                    new State("Last Bastion",
                        new SetAltTexture(1),
                        new Order(40, "Bastion Heart", "Bastion"),
                        // he holds the keep's middle, where its walls close in on him
                        new StayCloseToSpawn(0.6, Gi(2)),
                        IronKit(0.85),
                        AnvilCross(2200),
                        DoubleRing(14, 2, 3400, 300),
                        new Shoot(CrucibleSight, 1, projectileIndex: 3, predictive: 0.8, coolDown: 2000, coolDownOffset: 1000)
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
            // the tower's caches (CrucibleCaches.cs): broken, each launches its charge at the champion
            .Init("Powder Keg", new State(new CacheCharge()))
            .Init("Stormglass Jar", new State(new CacheCharge()))
            .Init("Frost Cask", new State(new CacheCharge()))
            .Init("Rending Crate", new State(new CacheCharge()))
            .Init("Hex Urn", new State(new CacheCharge()))
            .Init("Gilded Reliquary", new State(new CacheCharge()))
            // (the white bags' allies: Ferrogrim's Bastion Turret is a Mechanic's turret, Turret.cs,
            // with no behaviour; Nerissa's Tidepearl is a Druid's pearl, TidePearl.cs)
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
            //   and the Tidal Lanes in turn: a slow wave sweeps across the floor from side to side,
            //   shown first at its edge with two wide dry lanes left through it, while she throws
            //   rings of swaying orbs, each turned half a gap from the last.
            // - 66%: Rising Tide (a survival phase): the sea comes in from the edge until only the
            //   five coral isles are dry; the deep water drags and stings, and three riptide eels
            //   hunt in it; she turns out a flower of tide orbs and two slow arms of bubbles.
            // - 66% to 33%: the Whirlpool: two slow arms of surging tide spiral round her, and her
            //   bubbles spiral out the same way.
            // - 33%: Maelstrom: the whirlpool turns the other way, the lanes stay quiet; two spirals
            //   of bubbles turning opposite ways cross round her.
            // - 12%: Tsunami: the floor becomes a lagoon, her islet in the middle of deep water, a
            //   reef round it; one great wave after another rolls over the whole floor from a new
            //   side, each with a single dry lane through it, shown first at its edge: find the
            //   lane. Two eels hunt the lagoon, and she rings out tide orbs from her islet.
            .Init("Sanctum Heart",
                new State(
                    new State("Idle"),
                    new State("Fight",
                        new ArenaForm(),
                        new State("Lanes", new TideSweep("Surging Tide", G(15), 3, 7.5, 2, 3.5, warnMs: 1700, everyMs: 5500)),
                        // the wheel's ring and channels are the deep sea (ArenaForms), the eels in it
                        new State("Flood",
                            new SpawnRing("Riptide Eel", 3, G(12.5), 0, aroundSpawn: true),
                            new RemoveEntitiesOnExit(25, "Riptide Eel")
                        ),
                        new State("Whirlpool", new SweepingArms("Surging Tide", 2, G(3), G(17), 2.2, 24, curve: 10)),
                        new State("Maelstrom", new SweepingArms("Surging Tide", 2, G(3), G(16.5), 2.2, -28, curve: -11)),
                        // great waves, one lane through each
                        new State("Tsunami",
                            new TideSweep("Surging Tide", G(15.5), 4, 4.5, 1, 5, warnMs: 1800, everyMs: 6500),
                            new SpawnRing("Riptide Eel", 2, G(6), 45, aroundSpawn: true),
                            new RemoveEntitiesOnExit(25, "Riptide Eel")
                        )
                    )
                )
            )
            .Init("Nerissa, the Tidecaller",
                new State(
                    new BossStats(180000, 45, 1.0, 0.35),
                    new CrucibleCaches(), // the tower's caches beside the climbers (CrucibleCaches.cs)
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
                            new Prioritize(new StayCloseToSpawn(0.7, Gi(9)), new Wander(0.5)),
                            TideKit(1.0),
                            new TimedTransition(7000, "Lanes")
                        ),
                        new State("Lanes",
                            new CrucibleCue(5),
                            new Order(40, "Sanctum Heart", "Lanes"),
                            new Taunt(0.5, "Swim, if you can.") { Bubble = false },
                            new ReturnToSpawn(0.8),
                            new Shoot(CrucibleSight, 3, 10, projectileIndex: 3, predictive: 0.4, coolDown: 1500),
                            new Shoot(0, 10, 36, fixedAngle: 0, rotateAngle: 18, projectileIndex: 1, coolDown: 1600),
                            // wave crests rolling out and washing back
                            new Shoot(0, 6, 60, fixedAngle: 30, rotateAngle: 20, projectileIndex: 5, coolDown: 2800, coolDownOffset: 1400),
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
                        // a flower of swaying tide orbs, each ring turned, and two slow arms of bubbles
                        new Shoot(0, 8, 45, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 1, coolDown: 1400),
                        new Shoot(0, 2, 180, fixedAngle: 90, rotateAngle: 11, projectileIndex: 2, coolDown: 380),
                        new Shoot(CrucibleSight, 3, 10, projectileIndex: 0, predictive: 0.3, coolDown: 2200),
                        // the tide's crests out over the isles and back, an undertow now and then
                        new Shoot(0, 4, 90, fixedAngle: 45, rotateAngle: 15, projectileIndex: 5, coolDown: 3200, coolDownOffset: 1600),
                        new Shoot(CrucibleSight, 1, projectileIndex: 4, coolDown: 4500, coolDownOffset: 2000),
                        new TimedTransition(20000, "Whirl")
                    ),
                    new State("Whirl",
                        new HpLessTransition(0.33, "Maelstrom Rise"),
                        new CrucibleCue(7),
                        new Order(40, "Sanctum Heart", "Whirlpool"),
                        new Prioritize(new StayCloseToSpawn(0.7, Gi(6)), new Wander(0.5)),
                        TideKit(1.0),
                        // her bubbles spiral out with the whirlpool
                        new Shoot(0, 2, 180, fixedAngle: 0, rotateAngle: 10, projectileIndex: 2, coolDown: 450),
                        // three crests at you, washing back
                        new Shoot(CrucibleSight, 3, 25, projectileIndex: 5, coolDown: 3400, coolDownOffset: 1700),
                        new Spawn("Riptide Eel", 2, 0, 12000)
                    ),
                    new State("Maelstrom Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(12, 1),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(8, shake: true),
                        new Flash(TideGlow, 0.2, 10),
                        new Taunt(true, "The whole sea, then. MAELSTROM!") { Bubble = false },
                        new TimedTransition(2200, "Maelstrom")
                    ),
                    new State("Maelstrom",
                        new HpLessTransition(0.12, "Tsunami Rise"),
                        new SetAltTexture(1),
                        new Order(40, "Sanctum Heart", "Maelstrom"),
                        new Prioritize(new StayCloseToSpawn(0.8, Gi(7)), new Wander(0.6)),
                        TideKit(0.9),
                        // two spirals of bubbles turning opposite ways, crossing round her
                        new Shoot(0, 3, 120, fixedAngle: 0, rotateAngle: 12, projectileIndex: 2, coolDown: 500),
                        new Shoot(0, 3, 120, fixedAngle: 60, rotateAngle: -12, projectileIndex: 2, coolDown: 500, coolDownOffset: 250),
                        // quick tridents thrown where you're going
                        new Shoot(CrucibleSight, 3, 8, projectileIndex: 3, predictive: 0.8, coolDown: 1600, coolDownOffset: 800)
                    ),
                    new State("Tsunami Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(14, 1),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(25, shake: true),
                        new Flash(TideGlow, 0.15, 14),
                        new Taunt(true, "Then the sea itself will stand up and fall on you.") { Bubble = false },
                        new Order(40, "Sanctum Heart", "Tsunami"),
                        new TimedTransition(2600, "Tsunami")
                    ),
                    new State("Tsunami",
                        new SetAltTexture(1),
                        new Order(40, "Sanctum Heart", "Tsunami"),
                        // on her islet
                        new StayCloseToSpawn(0.6, Gi(2)),
                        TideKit(0.85),
                        // rings of swaying tide orbs, each turned half a gap, out over the lagoon
                        new Shoot(0, 12, 30, fixedAngle: 0, rotateAngle: 15, projectileIndex: 1, coolDown: 2000),
                        new Shoot(CrucibleSight, 3, 8, projectileIndex: 3, predictive: 0.8, coolDown: 1800, coolDownOffset: 900),
                        // a ring of crests rolling out over the lagoon and back to her islet
                        new Shoot(0, 8, 45, fixedAngle: 22.5, projectileIndex: 5, coolDown: 4000, coolDownOffset: 2000)
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
            // - 100% to 70%: magma, cinder fans and rings; Lava Veins: two at a time crack out across
            //   the floor at the players, glowing a full second first, burning, then cooling to slag;
            //   the Anvil Drop (he leaps onto a marked spot).
            // - 70%: Meteor Storm (a survival phase): meteors rain over the floor, each marked as it
            //   falls, and molten golems climb out of the forge; he erupts in rings of slow magma.
            // - 70% to 35%: Hammer: his hammer falls where each of you stands (marked first); he
            //   throws walls of cinders in a turning cross, with leaps and golems.
            // - 35%: The Crucible Pours: the floor's edge cracks and falls away into the sky, for the
            //   rest of the fight, embers raining over what's left; then Molten Core: a meteor on each of you now and then, and a
            //   spiral of magma.
            // - 12%: Molten Sea: the crucible fills. Everything but an island of slag-rimmed floor
            //   glows, then floods with molten metal, and the island drifts across it: get onto it
            //   and stay on it; it drains, and fills again with the island somewhere else. He
            //   stands in the melt, throwing magma and slag at you.
            .Init("Foundry Heart",
                new State(
                    new State("Idle"),
                    new State("Fight",
                        new ArenaForm(),
                        new State("Veins", new LavaVeins("Forge Lava", "Cooling Slag", 2, (int)G(11), G(15), everyMs: 3800, growMs: 60, warnMs: 1100)),
                        new State("Meteors",
                            new Starfall(Starfall.Area.Scatter, G(15), 2.2, 340, 900, color: LavaGlow)
                        ),
                        // the anvil rings: his hammer falls where each of you stands, marked first
                        new State("Hammer", new Starfall(Starfall.Area.Players, CrucibleSight, 2, 300, 2400, color: LavaGlow)),
                        // the forge falls away but for its crucible (ArenaForms)
                        new State("Broken",
                            // as the forge falls, its embers rain over what's left
                            new State("Pour", new Starfall(Starfall.Area.Scatter, G(9), 1.8, 280, 600, color: LavaGlow)),
                            new State("Molten",
                                new Starfall(Starfall.Area.Players, CrucibleSight, 1.8, 300, 3200, color: LavaGlow)
                            ),
                            // the crucible fills but for a drifting island (the spout and all)
                            new State("Sea", new SafeIsland("Forge Lava", "Cooling Slag", G(14.5), G(3.2), G(5.5), G(1)))
                        )
                    )
                )
            )
            .Init("Pyrrhax, the Forge Tyrant",
                new State(
                    new BossStats(210000, 55, 1.0, 0.35),
                    new CrucibleCaches(), // the tower's caches beside the climbers (CrucibleCaches.cs)
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
                        new Prioritize(new StayCloseToSpawn(0.6, Gi(8)), new Follow(0.5, CrucibleSight, 4)),
                        ForgeKit(1.0),
                        // a fan of meteor fragments now and then, weaving as they burn
                        new Shoot(CrucibleSight, 3, 20, projectileIndex: 5, coolDown: 3300, coolDownOffset: 2600),
                        new LeapStrike(CrucibleSight, 3, 350, 1400, 7500, 2, 16)
                    ),
                    new State("Storm",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(10, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Foundry Heart", "Meteors"),
                        new Flash(LavaGlow, 0.25, 10),
                        new Taunt(true, "The sky is a forge too. LOOK UP.") { Bubble = false },
                        new Spawn("Molten Golem", 3, 0.5, 3500),
                        // he erupts: rings of slow magma, each turned half a gap, cinders between
                        new Shoot(0, 10, 36, fixedAngle: 0, rotateAngle: 18, projectileIndex: 3, coolDown: 1800),
                        new Shoot(0, 10, 36, fixedAngle: 9, rotateAngle: 18, projectileIndex: 2, coolDown: 1800, coolDownOffset: 900),
                        // the meteors' fragments flung out in a turning star, a slag boulder at you
                        new Shoot(0, 5, 72, fixedAngle: 0, rotateAngle: 29, projectileIndex: 5, coolDown: 1200, coolDownOffset: 600),
                        new Shoot(CrucibleSight, 1, projectileIndex: 4, predictive: 0.5, coolDown: 3500, coolDownOffset: 1700),
                        new TimedTransition(14000, "Hammer")
                    ),
                    new State("Hammer",
                        new HpLessTransition(0.35, "Pour"),
                        new Order(40, "Foundry Heart", "Hammer"),
                        new Prioritize(new StayCloseToSpawn(0.7, Gi(8)), new Follow(0.6, CrucibleSight, 4)),
                        ForgeKit(1.0),
                        // walls of three cinders side by side, in a cross that turns 30 degrees a throw
                        new ShootFormation(0, 4, new[] { 0.0, -0.9, 0.0, 0.0, 0.0, 0.9 }, projectileIndex: 2,
                            fixedAngle: 0, rotateAngle: 30, coolDown: 1600),
                        new LeapStrike(CrucibleSight, 3, 350, 1400, 7000, 2, 16),
                        new Spawn("Molten Golem", 2, 0, 10000)
                    ),
                    new State("Pour",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(11, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Foundry Heart", "Pour"),
                        new Flash(LavaGlow, 0.2, 14),
                        new Taunt(true, "Into the crucible with you!") { Bubble = false },
                        new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 1, coolDown: 1500),
                        // the melt pouring off him in two arms, fragments at you
                        new Shoot(0, 2, 180, fixedAngle: 0, rotateAngle: 14, projectileIndex: 3, coolDown: 450),
                        new Shoot(CrucibleSight, 3, 18, projectileIndex: 5, coolDown: 1800, coolDownOffset: 900),
                        new TimedTransition(7000, "Molten Rise")
                    ),
                    new State("Molten Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(16, 2),
                        new SetAltTexture(1),
                        new CrucibleCue(12),
                        new Taunt(true, "Burn with me. MOLTEN CORE!") { Bubble = false },
                        new TimedTransition(1800, "Molten")
                    ),
                    new State("Molten",
                        new HpLessTransition(0.12, "Sea Rise"),
                        new SetAltTexture(1),
                        new Order(40, "Foundry Heart", "Molten"),
                        new Prioritize(new StayCloseToSpawn(0.6, Gi(5)), new Follow(0.5, CrucibleSight, 3)),
                        ForgeKit(0.9),
                        new Shoot(0, 3, 120, fixedAngle: 0, rotateAngle: 13, projectileIndex: 3, coolDown: 600),
                        // cinders flung where you're going
                        new Shoot(CrucibleSight, 3, 10, projectileIndex: 1, predictive: 0.8, coolDown: 1500, coolDownOffset: 700)
                    ),
                    new State("Sea Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(18, 2),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(26, shake: true),
                        new Flash(LavaGlow, 0.15, 14),
                        new Taunt(true, "The crucible is full. Swim in it!") { Bubble = false },
                        new Order(40, "Foundry Heart", "Sea"),
                        new TimedTransition(2600, "Sea")
                    ),
                    new State("Sea",
                        new SetAltTexture(1),
                        new Order(40, "Foundry Heart", "Sea"),
                        // he stands in the melt
                        new StayCloseToSpawn(0.6, Gi(2)),
                        ForgeKit(0.85),
                        // a slow spiral of magma, and heavy slag lobbed where you're going
                        new Shoot(0, 2, 180, fixedAngle: 0, rotateAngle: 11, projectileIndex: 3, coolDown: 700),
                        new Shoot(CrucibleSight, 1, projectileIndex: 4, predictive: 0.8, coolDown: 2400, coolDownOffset: 1200)
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
            //   floor is a clock and its two hands sweep round it, the broad hour hand slowly and the
            //   thin minute hand three times as fast; clockwork sentries
            //   now and then.
            // - 70%: Midnight Chime (a survival phase): the clock's hours light in patterns, half at
            //   a time, each glowing first and then burning, with a long rest between; he sweeps a
            //   clock hand of blades round himself and rings out the hours in gears.
            // - 70% to 35%: Time Fracture: the broken clock strikes half its hours at random; six Hour
            //   Echoes, untouchable copies, keep its hours: each blinks onto an hour about to burn and
            //   lights it, the warning spreading out from it for nearly three seconds, and they never
            //   shoot. He blinks about with a lighter kit and no rushers.
            // - 35%: Reversal: the clock's hands run backwards and faster, and so do his: two hands of gears
            //   sweeping back round him.
            // - 12%: Zero Hour: the dial is cut into its twelve hours again and counts down: every
            //   third hour burns at once, the next three a step round each time, while both hands
            //   sweep slowly round over them. He blinks about and throws his kit faster.
            .Init("Orrery Heart",
                new State(
                    new State("Idle"),
                    new State("Fight",
                        new ArenaForm(),
                        new State("Hands", ClockHands(G(14.5), 1)),
                        new State("Chime", new SectorChime("Chime Light", 12, G(3), G(15.8), new[]
                        {
                            "101010101010", "010101010101", "110011001100", "001100110011", "111000111000", "000111000111",
                        }, warnMs: 1200, gapMs: 800)),
                        // the fractured clock strikes at random: half its hours at a time, each lit by an
                        // Hour Echo blinking onto it, with time to get off it
                        new State("Fracture", new SectorChime("Chime Light", 12, G(3), G(16.5), null, warnMs: 2800, litMs: 1100, gapMs: 1400,
                            casters: "Hour Echo", color: ChronoGlow)),
                        new State("Reversal", ClockHands(G(17.5), -1.15)),
                        // the hours counting down in thirds, the hands slowly over them
                        new State("Zero", new FloorLayers(
                            new SectorChime("Chime Light", 12, G(3), G(15.8), new[] { "100100100100", "010010010010", "001001001001" },
                                warnMs: 1400, litMs: 1300, gapMs: 1300),
                            ClockHands(G(15.8), 0.7)))
                    )
                )
            )
            .Init("Chronarch, the Hour Thief",
                new State(
                    new BossStats(240000, 60, 1.0, 0.35),
                    new CrucibleCaches(), // the tower's caches beside the climbers (CrucibleCaches.cs)
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
                        new Prioritize(new StayCloseToSpawn(0.5, Gi(5)), new Wander(0.3)),
                        ClockKit(1.0),
                        // blades that run out and back again
                        new Shoot(CrucibleSight, 3, 18, projectileIndex: 5, coolDown: 3000, coolDownOffset: 1500),
                        new Spawn("Clockwork Sentry", 2, 0, 12000)
                    ),
                    new State("Midnight",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new CrucibleCue(14, shake: true),
                        new ReturnToSpawn(1.5),
                        new Order(40, "Orrery Heart", "Chime"),
                        new Flash(BrassGlow, 0.25, 12),
                        new Taunt(true, "Midnight. Hear the bells toll for you.") { Bubble = false },
                        // a clock hand of blades sweeping round him, and the twelve hours in gears
                        new Shoot(0, 1, fixedAngle: -90, rotateAngle: 9, projectileIndex: 1, coolDown: 160),
                        new Shoot(0, 12, 30, fixedAngle: 0, projectileIndex: 3, coolDown: 2600),
                        new Shoot(CrucibleSight, 3, 15, projectileIndex: 5, coolDown: 2200, coolDownOffset: 1100),
                        new TimedTransition(16000, "Fracture")
                    ),
                    new State("Fracture",
                        new HpLessTransition(0.35, "Reversal Rise"),
                        new CrucibleCue(15, shake: true),
                        new Order(40, "Orrery Heart", "Fracture"),
                        new SpawnRing("Hour Echo", 6, G(9), 0, aroundSpawn: true), // the keepers of its hours
                        new RemoveEntitiesOnExit(40, "Hour Echo"),
                        new Taunt(true, "Which of me is now? All of us.") { Bubble = false },
                        new State("Fracture Fight",
                            new Prioritize(new StayCloseToSpawn(0.6, Gi(9)), new Wander(0.4)),
                            ClockKit(1.2, rushers: false),
                            // a ring of blades out and back, turning a little each time
                            new Shoot(0, 6, 60, fixedAngle: 0, rotateAngle: 12, projectileIndex: 5, coolDown: 2600, coolDownOffset: 800),
                            new TimedTransition(5000, "Fracture Blink")
                        ),
                        new State("Fracture Blink",
                            new BlinkAround(G(4), G(11)),
                            new Flash(ChronoGlow, 0.2, 2),
                            new Shoot(0, 12, fixedAngle: 0, projectileIndex: 0, coolDown: 90000000, coolDownOffset: 300),
                            new TimedTransition(500, "Fracture Fight")
                        )
                    ),
                    new State("Reversal Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(14, 3),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(16, shake: true),
                        new Flash(ChronoGlow, 0.2, 10),
                        new Taunt(true, "Back. Again. Back. Again.") { Bubble = false },
                        new TimedTransition(2200, "Reversal")
                    ),
                    new State("Reversal",
                        new HpLessTransition(0.12, "Zero Rise"),
                        new SetAltTexture(1),
                        new Order(40, "Orrery Heart", "Reversal"),
                        new Prioritize(new StayCloseToSpawn(0.5, Gi(5)), new Wander(0.4)),
                        ClockKit(0.9),
                        // two hands of gears sweeping backwards round him
                        new Shoot(0, 2, 180, fixedAngle: -90, rotateAngle: -8, projectileIndex: 3, coolDown: 220),
                        // hand-blades thrown where you'll be
                        new Shoot(CrucibleSight, 2, 10, projectileIndex: 1, predictive: 0.8, coolDown: 1400, coolDownOffset: 700),
                        // and everything comes back: a ring of blades rewinding, turning backwards
                        new Shoot(0, 8, 45, fixedAngle: 22.5, rotateAngle: -11, projectileIndex: 5, coolDown: 2800, coolDownOffset: 1400)
                    ),
                    new State("Zero Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(16, 3),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new CrucibleCue(27, shake: true),
                        new Flash(ChronoGlow, 0.15, 14),
                        new Taunt(true, "Zero hour. There is no after.") { Bubble = false },
                        new Order(40, "Orrery Heart", "Zero"),
                        new TimedTransition(2600, "Zero")
                    ),
                    new State("Zero",
                        new SetAltTexture(1),
                        new Order(40, "Orrery Heart", "Zero"),
                        new State("Zero Fight",
                            new Prioritize(new StayCloseToSpawn(0.5, Gi(4)), new Wander(0.4)),
                            ClockKit(0.8, rushers: false),
                            // the twelve hours rung out in gears
                            new Shoot(0, 12, 30, fixedAngle: 15, projectileIndex: 3, coolDown: 2400),
                            new Shoot(CrucibleSight, 2, 20, projectileIndex: 5, coolDown: 2000, coolDownOffset: 1000),
                            new TimedTransition(6000, "Zero Blink")
                        ),
                        new State("Zero Blink",
                            new BlinkAround(G(3), G(8)),
                            new Flash(ChronoGlow, 0.2, 2),
                            new Shoot(0, 12, fixedAngle: 0, projectileIndex: 0, coolDown: 90000000, coolDownOffset: 300),
                            new TimedTransition(500, "Zero Fight")
                        )
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
            // the keepers of the fractured clock's hours: they stand still and never shoot, the Heart
            // (SectorChime) blinks each onto an hour to light it
            .Init("Hour Echo",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Flash(ChronoGlow, 0.4, 1)
                )
            )
            .Init("Clockwork Sentry",
                new State(
                    new Prioritize(new Follow(0.7, 18, 3), new Wander(0.4)),
                    new Shoot(12, 1, projectileIndex: 0, predictive: 0.3, coolDown: 1100)
                )
            )
            // the champions' rushers (Rushers in their kits)
            .Init("Iron Hound", Rusher(1.4))
            .Init("Brine Darter", Rusher(1.6))
            .Init("Cinder Imp", Rusher(1.5))
            .Init("Clockwork Mite", Rusher(1.7))
            .Init("Gilded Wisp", Rusher(1.6))

            // ================================================================================
            // V. The Apex: Vaelith, the Crucible Sovereign
            // - 100% to 80%: Sovereign's Decree: lances, rings of flame feathers that sway, a
            //   spiral of stars, gold strikes falling on the players.
            // - 80%, 65%, 50%, 35%: the four echoes, one after another: the Apex reshapes itself
            //   into each fallen champion's floor, made over (ApexForm: a fortress with pits in its
            //   yard, isles in a deep sea, a cross over the sky with furnaces floating off it, an
            //   orrery), with one of its hazards, slow, and that champion's ghost fights from the
            //   rim: iron (two blades), tide (sweeps), forge (lava veins), hours (the clock's hands).
            // - 20%: The Crucible Breaks (a survival phase): the Apex shatters, a disc of its middle
            //   left with its shards floating round it, the four ghosts together, gold raining, a
            //   flower of flame feathers.
            // - Then Unbound to the end: the Apex re-forms as a halo of void marble round the void
            //   itself (Vaelith on the halo, set on it if the void opens under him), two slow arms
            //   of flame turning across it, double rings of void embers.
            // - 8%: Final Aria: he sings the tower down. Three arms of flame turn the other way
            //   round the halo, gold strikes fall on each of you, and he throws a crown of stars,
            //   four arms spiralling out, with his lances through it.
            .Init("Apex Heart",
                new State(
                    new State("Idle"),
                    new State("Fight",
                        new ArenaForm(reach: 25, spreadMs: 2600),
                        new State("Decree",
                            new Starfall(Starfall.Area.Players, CrucibleSight, 1.7, 320, 3200, color: SovereignGlow)
                        ),
                        // each echo brings back one thing of its floor, slowly
                        new State("Iron",
                            new State("Iron Turn", new TimedTransition(3800, "Iron Go")),
                            new State("Iron Go", new SweepingArms("Grinding Blade", 2, G(3), G(19), 2.2, 22))
                        ),
                        new State("Tide",
                            new State("Tide Turn", new TimedTransition(3800, "Tide Go")),
                            new State("Tide Go", new TideSweep("Surging Tide", G(20), 3, 7.5, 2, 3.5, warnMs: 1700, everyMs: 6500))
                        ),
                        new State("Forge",
                            new State("Forge Turn", new TimedTransition(3800, "Forge Go")),
                            new State("Forge Go", new LavaVeins("Forge Lava", "Cooling Slag", 2, (int)G(14), G(21), everyMs: 3800, growMs: 60, warnMs: 1100))
                        ),
                        new State("Hours",
                            new State("Hours Turn", new TimedTransition(3800, "Hours Go")),
                            new State("Hours Go", ClockHands(G(21), 1))
                        ),
                        new State("Broken",
                            new State("Breaks",
                                new Starfall(Starfall.Area.Scatter, G(8), 1.8, 300, 1400, color: SovereignGlow)
                            ),
                            new State("Unbound", new SweepingArms("Sovereign Flame", 2, G(5), G(16), 2.2, 24)),
                            // three arms the other way, and gold falling on each of you
                            new State("Aria",
                                new SweepingArms("Sovereign Flame", 3, G(5), G(16), 2.0, -20),
                                new Starfall(Starfall.Area.Players, CrucibleSight, 1.7, 320, 2800, color: SovereignGlow)
                            )
                        )
                    )
                )
            )
            .Init("Vaelith, the Crucible Sovereign",
                new State(
                    new BossStats(340000, 70, 1.0, 0.35),
                    new CrucibleCaches(), // the tower's caches beside the climbers (CrucibleCaches.cs)
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
                        new Prioritize(new StayCloseToSpawn(0.7, Gi(8)), new Follow(0.6, CrucibleSight, 4)),
                        SovereignKit(1.0),
                        // halo shards looping round where he stood: don't follow him in close
                        new Shoot(0, 6, 60, fixedAngle: 0, rotateAngle: 15, projectileIndex: 6, coolDown: 3400, coolDownOffset: 1700)
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
                        new SpawnRing("Echo of Ferrogrim", 1, G(7), 225, aroundSpawn: true),
                        new SpawnRing("Echo of Nerissa", 1, G(7), 315, aroundSpawn: true),
                        new SpawnRing("Echo of Pyrrhax", 1, G(7), 45, aroundSpawn: true),
                        new SpawnRing("Echo of Chronarch", 1, G(7), 135, aroundSpawn: true),
                        new RemoveEntitiesOnExit(40, "Echo of Ferrogrim", "Echo of Nerissa", "Echo of Pyrrhax", "Echo of Chronarch"),
                        // a flower of flame feathers, each ring turned half a gap, four arms of
                        // stars turning through it, quills at you and his halo shards looping
                        new Shoot(0, 10, 36, fixedAngle: 0, rotateAngle: 18, projectileIndex: 1, coolDown: 1800),
                        new Shoot(0, 4, 90, fixedAngle: 45, rotateAngle: -8, projectileIndex: 2, coolDown: 450),
                        new Shoot(CrucibleSight, 3, 12, projectileIndex: 4, predictive: 0.5, coolDown: 2200, coolDownOffset: 1100),
                        new Shoot(0, 8, 45, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 6, coolDown: 3000, coolDownOffset: 1500),
                        new TimedTransition(13000, "Unbound Rise")
                    ),
                    new State("Unbound Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(14, 3),
                        new SetAltTexture(2),
                        new CrucibleCue(23, shake: true),
                        new Flash(VoidGlow, 0.2, 12),
                        new Taunt(true, "I am the Crucible. I. Do. Not. BREAK.") { Bubble = false },
                        new Order(40, "Apex Heart", "Unbound"),
                        new TimedTransition(2500, "Unbound")
                    ),
                    new State("Unbound",
                        new HpLessTransition(0.08, "Aria Rise"),
                        new SetAltTexture(2),
                        new Order(40, "Apex Heart", "Unbound"),
                        // on the halo (the void in its middle is sky: ArenaForm sets him on the halo
                        // if he's left over it), after the players round it
                        new Prioritize(new StayCloseToSpawn(0.6, Gi(13)), new Follow(0.5, CrucibleSight, 3)),
                        SovereignKit(0.9),
                        // a double ring of void embers, the second in the first's gaps, and now and
                        // then a fan of quick feathers at you
                        DoubleRing(12, 3, 3000, 350),
                        new Shoot(CrucibleSight, 7, 9, projectileIndex: 4, predictive: 0.7, coolDown: 2200, coolDownOffset: 1000)
                    ),
                    new State("Aria Rise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        RiseBurst(18, 2),
                        new SetAltTexture(2),
                        new CrucibleCue(28, shake: true),
                        new Flash(SovereignGlow, 0.15, 16),
                        new Taunt(true, "Hear it, then. The last song this tower will ever sing.") { Bubble = false },
                        new Order(40, "Apex Heart", "Aria"),
                        new TimedTransition(3000, "Aria")
                    ),
                    new State("Aria",
                        new SetAltTexture(2),
                        new Order(40, "Apex Heart", "Aria"),
                        new Prioritize(new StayCloseToSpawn(0.6, Gi(13)), new Follow(0.5, CrucibleSight, 3)),
                        SovereignKit(0.85),
                        // the crown of stars: four arms spiralling out, and lances through it
                        new Shoot(0, 4, 90, fixedAngle: 0, rotateAngle: 9, projectileIndex: 2, coolDown: 300),
                        new Shoot(CrucibleSight, 5, 8, projectileIndex: 0, predictive: 0.7, coolDown: 1800, coolDownOffset: 900),
                        new Shoot(0, 6, 60, fixedAngle: 30, rotateAngle: 20, projectileIndex: 6, coolDown: 3000, coolDownOffset: 1500)
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

        // a distance on the floors as designed, grown as they are (ArenaForms.Grow: the floors are
        // bigger than they were drawn, so their hazards and champions reach as far)
        private static double G(double squares) => squares * ArenaForms.Grow;
        private static int Gi(int squares) => (int)Math.Round(squares * ArenaForms.Grow);

        // Chronarch's clock: its broad hour hand sweeping slowly, its narrower minute hand a little
        // over twice as fast, `pace` times (negative: backwards); both hurt whoever they cross
        private static FloorLayers ClockHands(double outer, double pace) => new FloorLayers(
            new SweepingArms("Hour Hand", 1, G(2.5), outer, 2.8, 14 * pace),
            new SweepingArms("Hour Hand", 1, G(2.5), outer, 2.0, 32 * pace));

        // Lockdown's raised drawbridges (the towers form's), in its design squares
        // (a gate two squares deep across each bridge, not the whole bridge: grown, that was a solid
        // block of grates five wide and six long)
        private static bool Drawbridge(int dx, int dy) =>
            Math.Min(Math.Abs(dx), Math.Abs(dy)) <= 1 && Math.Max(Math.Abs(dx), Math.Abs(dy)) is >= 7 and <= 8;

        // ---- the champions' kits, `pace` < 1 faster ----

        // Every kit carries its champion's afflictions on the shots aimed at you (tools/build_crucible.py,
        // CHAMPIONS) and ends with its signature, a slower shot worth stepping round.

        // bleeding shard fans, the hammer wave that breaks armour and dazes, iron rings; Rending
        // Volley: a quick tight spray of shards that leaves you bleeding
        private static IStateChildren IronKit(double pace) => new State(
            new Shoot(CrucibleSight, 5, 12, projectileIndex: 0, predictive: 0.4, coolDown: (int)(1250 * pace)),
            new Shoot(CrucibleSight, 1, projectileIndex: 1, coolDown: (int)(2300 * pace), coolDownOffset: 800),
            new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 2, coolDown: (int)(2900 * pace), coolDownOffset: 1600),
            new Shoot(CrucibleSight, 7, 5, projectileIndex: 4, predictive: 0.3, coolDown: (int)(3400 * pace), coolDownOffset: 2300),
            Wall(0, 7, (int)(3800 * pace), 1200), // Iron Wall: a line of bleeding shards
            Rushers("Iron Hound", pace)
        );

        // a wall: `width` shots side by side, aimed at you and flying as one line, so it isn't dodged
        // by standing still between them but by getting out of its way
        private static IStateChildren Wall(int projectileIndex, int width, int coolDown, int coolDownOffset) =>
            new ShootFormation(CrucibleSight, 1,
                Enumerable.Range(0, width).SelectMany(i => new[] { 0.0, (i - (width - 1) / 2.0) * 0.9 }).ToArray(),
                projectileIndex: projectileIndex, coolDown: coolDown, coolDownOffset: coolDownOffset);

        // a few of the champion's rushers sent out round the floor now and then (at most three about
        // at once): they run at you and bite up close, so they're to be shot on the way in
        private static IStateChildren Rushers(string name, double pace) =>
            new TossObject(name, G(9), coolDown: (int)(9000 * pace), coolDownOffset: 4000, minRange: G(6), maxRange: G(12),
                densityRange: 40, maxDensity: 3);

        // a rusher: quick, after the nearest player, biting at close range (its champion's
        // affliction on it, tools/build_crucible.py MINIONS)
        private static State Rusher(double speed) => new State(
            new Prioritize(new Follow(speed, CrucibleSight, 1), new Wander(0.5)),
            new Shoot(3, 3, 20, projectileIndex: 0, coolDown: 800)
        );

        // a champion rising into a new phase throws out a ring of its shots and, 0.7 s after, a second
        // in the first's gaps (once each time it rises)
        private static IStateChildren RiseBurst(int count, int projectileIndex) => new State(
            new Shoot(0, count, 360.0 / count, fixedAngle: 0, projectileIndex: projectileIndex, coolDown: 90000000, coolDownOffset: 500),
            new Shoot(0, count, 360.0 / count, fixedAngle: 180.0 / count, projectileIndex: projectileIndex, coolDown: 90000000, coolDownOffset: 1200)
        );

        // Ferrogrim's anvil: four hammer waves in a cross, turned 45 degrees each throw (+, x, +...)
        private static IStateChildren AnvilCross(int coolDown) =>
            new Shoot(0, 4, 90, fixedAngle: 0, rotateAngle: 45, projectileIndex: 1, coolDown: coolDown);

        // a ring of `count` shots and, `delayMs` after, a second in the first's gaps: step in
        // between the two
        private static IStateChildren DoubleRing(int count, int projectileIndex, int coolDown, int delayMs) => new State(
            new Shoot(0, count, 360.0 / count, fixedAngle: 0, projectileIndex: projectileIndex, coolDown: coolDown),
            new Shoot(0, count, 360.0 / count, fixedAngle: 180.0 / count, projectileIndex: projectileIndex, coolDown: coolDown, coolDownOffset: delayMs)
        );

        // barbed tridents (bleeding), slowing tide orbs, bubbles; Undertow: three great slow orbs
        // that quiet you and drag you under for a moment
        private static IStateChildren TideKit(double pace) => new State(
            new Shoot(CrucibleSight, 3, 10, projectileIndex: 0, predictive: 0.4, coolDown: (int)(1100 * pace)),
            new Shoot(0, 10, fixedAngle: 0, rotateAngle: 13, projectileIndex: 1, coolDown: (int)(2500 * pace), coolDownOffset: 900),
            new Shoot(CrucibleSight, 5, 20, projectileIndex: 2, coolDown: (int)(3200 * pace), coolDownOffset: 1800),
            new Shoot(CrucibleSight, 3, 24, projectileIndex: 4, coolDown: (int)(4200 * pace), coolDownOffset: 2600),
            Wall(1, 7, (int)(4000 * pace), 1400), // a breaker: a swaying line of tide orbs
            Rushers("Brine Darter", pace)
        );

        // magma that leaves you weak, cinders that burn you sick (no healing), rings of cinders;
        // Slag Ball: three heavy slow globs that set you in slag and crack your armour
        private static IStateChildren ForgeKit(double pace) => new State(
            new Shoot(CrucibleSight, 1, projectileIndex: 0, predictive: 0.5, coolDown: (int)(1600 * pace)),
            new Shoot(CrucibleSight, 5, 9, projectileIndex: 1, predictive: 0.3, coolDown: (int)(1000 * pace), coolDownOffset: 500),
            new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 2, coolDown: (int)(2700 * pace), coolDownOffset: 1500),
            new Shoot(CrucibleSight, 3, 28, projectileIndex: 4, predictive: 0.3, coolDown: (int)(4000 * pace), coolDownOffset: 2000),
            Wall(1, 7, (int)(3800 * pace), 1300), // a sheet of cinders
            Rushers("Cinder Imp", pace)
        );

        // gear rings, hand-blades that cut (bleeding), chrono orbs that slow and daze; Stolen
        // Moment: two quick orbs that silence you and freeze you in place
        // (no rushers in Time Fracture, where the floor needs watching)
        private static IStateChildren ClockKit(double pace, bool rushers = true)
        {
            var kit = new System.Collections.Generic.List<IStateChildren>
            {
                new Shoot(0, 12, fixedAngle: 0, rotateAngle: 7.5, projectileIndex: 0, coolDown: (int)(2200 * pace)),
                new Shoot(CrucibleSight, 3, 12, projectileIndex: 1, predictive: 0.4, coolDown: (int)(1150 * pace), coolDownOffset: 600),
                new Shoot(CrucibleSight, 1, projectileIndex: 2, coolDown: (int)(3100 * pace), coolDownOffset: 1700),
                new Shoot(CrucibleSight, 2, 14, projectileIndex: 4, predictive: 0.5, coolDown: (int)(3600 * pace), coolDownOffset: 2400),
                Wall(1, 5, (int)(3400 * pace), 1100), // a rank of hand-blades
            };
            if (rushers)
                kit.Add(Rushers("Clockwork Mite", pace));
            return new State(kit.ToArray());
        }

        // lances that leave you exposed, swaying rings of flame feathers, a spiral of stars; Final
        // Decree: three huge slow lances through armour that stun and shatter it
        private static IStateChildren SovereignKit(double pace) => new State(
            new Shoot(CrucibleSight, 3, 8, projectileIndex: 0, predictive: 0.5, coolDown: (int)(1000 * pace)),
            new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 1, coolDown: (int)(2400 * pace), coolDownOffset: 1300),
            new Shoot(0, 2, 180, fixedAngle: 0, rotateAngle: 11, projectileIndex: 2, coolDown: (int)(560 * pace)),
            new Shoot(CrucibleSight, 3, 22, projectileIndex: 5, coolDown: (int)(4800 * pace), coolDownOffset: 3000),
            Wall(0, 7, (int)(3600 * pace), 1500), // a phalanx of lances
            Rushers("Gilded Wisp", pace)
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
                new TossObject(ghost, G(11), 270, coolDown: 90000000),
                new RemoveEntitiesOnExit(40, ghost),
                new Prioritize(new StayCloseToSpawn(0.6, Gi(5)), new Wander(0.4)),
                SovereignKit(1.15),
                // a turning ring of stars while the ghost fights
                new Shoot(0, 8, 45, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 2, coolDown: 2800, coolDownOffset: 1400)
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
            new Prioritize(new StayCloseToSpawn(0.3, Gi(2)), new Wander(0.2)),
            new Shoot(CrucibleSight, 3, 14, projectileIndex: 0, predictive: 0.3, coolDown: coolDown),
            new Shoot(0, 8, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 0, coolDown: coolDown * 2, coolDownOffset: coolDown / 2)
        );
    }
}
