using System;
using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Oryx's Elysium (tools/build_elysium.py), Oryx 5's holy island. His Ascended Host hold the roads:
    // Oryx 3's court monsters, ascended, with divine shots and attacks of their own (their XML is
    // the script's). Each road leads to a guardian, the Gilded Sphinx atop the Pyramid and the Oasis
    // Seraph in the Oasis, who drops three Trees of Doom when it dies; with three more on each road,
    // that's twelve. Once no tree and no guardian is left, the Gate Watcher opens the Holy Gates to
    // his temple, where his three Ascended Wardens circle him until they fall, and Oryx the Ascended God
    // himself, with the helpers of his fight.
    partial class BehaviorDb
    {
        // Each of the Host arrives invulnerable and holds its fire for 2 s (Arrival), so one Oryx 5
        // summons doesn't land on a player shooting.
        private _ Elysium = () => Behav()
            // a gold fan of wavering missiles, and a slow ring of halos turning between volleys
            .Init("Ascended Noble",
                new State(
                    new BossStats(17000, 25, 1.0, 0.25),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(11, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.6, 11, 4), new Wander(0.3)),
                        new Shoot(11, 3, 16, projectileIndex: 0, predictive: 0.3, coolDown: 1100),
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 15, projectileIndex: 1, coolDown: 2400, coolDownOffset: 600)
                    )
                ),
                ElysianLoot()
            )
            // circles you throwing dazing lances that come back, then stops to spin out halos
            .Init("Ascended Aristocrat",
                new State(
                    new BossStats(17000, 25, 1.0, 0.25),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(11, "Lances")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.7, 6), new Orbit(0.7, 5, 11, null), new Wander(0.3)),
                        new State("Lances",
                            new Shoot(9, 5, 12, projectileIndex: 0, coolDown: 1000),
                            new TimedTransition(4000, "Halos")
                        ),
                        new State("Halos",
                            new Flash(0xFF8CD8FF, 0.3, 3),
                            new Shoot(0, 8, fixedAngle: 0, rotateAngle: 22.5, projectileIndex: 1, coolDown: 600),
                            new TimedTransition(2400, "Lances")
                        )
                    )
                ),
                ElysianLoot()
            )
            // three lines of stakes at three speeds, the fastest breaking armour, and boomerang
            // shockwaves in a cross that turns
            .Init("Ascended Sergeant",
                new State(
                    new BossStats(17000, 40, 1.0, 0.25),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 7), new Follow(0.45, 12, 5)),
                        new Shoot(12, 3, 25, projectileIndex: 0, coolDown: 1600),
                        new Shoot(12, 3, 25, projectileIndex: 1, coolDown: 1600),
                        new Shoot(12, 3, 25, projectileIndex: 2, coolDown: 1600),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 20, projectileIndex: 3, coolDown: 2800, coolDownOffset: 1200)
                    )
                ),
                ElysianLoot()
            )
            // slowing orbs, a heavy aimed spike, and a slow ring of silencing bolts when it glows
            .Init("Ascended Officer",
                new State(
                    new BossStats(18000, 35, 1.0, 0.25),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(12, "Volley")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 7), new Follow(0.5, 12, 4)),
                        new State("Volley",
                            new Shoot(12, 5, 10, projectileIndex: 0, coolDown: 900),
                            new Shoot(12, projectileIndex: 1, predictive: 0.6, coolDown: 1500, coolDownOffset: 700),
                            new TimedTransition(5000, "Ring")
                        ),
                        new State("Ring",
                            new Flash(0xFFFFFFFF, 0.3, 3),
                            new Shoot(0, 10, fixedAngle: 0, projectileIndex: 2, coolDown: 1500, coolDownOffset: 600),
                            new Shoot(0, 10, fixedAngle: 18, projectileIndex: 2, coolDown: 1500, coolDownOffset: 1350),
                            new TimedTransition(3000, "Volley")
                        )
                    )
                ),
                ElysianLoot()
            )
            // charges behind armour-breaking waves that pass cover, then stands and lets fly two
            // rings of great slow bolts and spikes
            .Init("Ascended Major",
                new State(
                    new BossStats(18000, 45, 1.0, 0.25),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(12, "Charge")),
                    new State("Attack",
                        new State("Charge",
                            new Prioritize(new StayCloseToSpawn(1.2, 8), new Charge(1.5, 10, 2200)),
                            new Shoot(7, 5, 14, projectileIndex: 0, coolDown: 900),
                            new TimedTransition(4500, "Stand")
                        ),
                        new State("Stand",
                            new Flash(0xFFAA70F0, 0.3, 3),
                            new Shoot(0, 6, fixedAngle: 0, projectileIndex: 1, coolDown: 3000, coolDownOffset: 700),
                            new Shoot(0, 6, fixedAngle: 30, projectileIndex: 2, coolDown: 3000, coolDownOffset: 1400),
                            new Shoot(12, projectileIndex: 1, predictive: 0.4, coolDown: 1200),
                            new TimedTransition(3000, "Charge")
                        )
                    )
                ),
                ElysianLoot()
            )
            // keeps back, heals the host round it, and turns a four-way spiral of weakening beams
            .Init("Ascended Deacon",
                new State(
                    new BossStats(15000, 20, 1.0, 0.25),
                    new HealEntity(12, "Ascended Noble", 800, 3000),
                    new HealEntity(12, "Ascended Aristocrat", 800, 3000),
                    new HealEntity(12, "Ascended Cleric", 800, 3000),
                    new HealEntity(12, "Ascended Cardinal", 800, 3000),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new StayBack(0.5, 7), new Wander(0.3)),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 13, projectileIndex: 0, coolDown: 350),
                        new Shoot(12, projectileIndex: 1, predictive: 0.6, coolDown: 1800)
                    )
                ),
                ElysianLoot()
            )
            // a sunburst cone of shockwaves at four speeds, and a dazing nova for anyone up close
            .Init("Ascended Cleric",
                new State(
                    new BossStats(15000, 15, 1.0, 0.25),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.6, 6), new Follow(0.6, 12, 3)),
                        new Shoot(10, 5, 12, projectileIndex: 0, coolDown: 1500),
                        new Shoot(10, 5, 12, projectileIndex: 1, coolDown: 1500),
                        new Shoot(10, 5, 12, projectileIndex: 2, coolDown: 1500),
                        new Shoot(10, 5, 12, projectileIndex: 3, coolDown: 1500),
                        new Shoot(3.5, 12, projectileIndex: 4, coolDown: 1000)
                    )
                ),
                ElysianLoot()
            )
            // spears that come back and paralyse, and a fast straight spear aimed where you're going
            .Init("Ascended Cardinal",
                new State(
                    new BossStats(15000, 25, 1.0, 0.25),
                    new State("Arrival",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle", new PlayerWithinTransition(12, "Attack")),
                    new State("Attack",
                        new Prioritize(new StayCloseToSpawn(0.5, 6), new StayBack(0.5, 6), new Wander(0.3)),
                        new Shoot(12, 5, 15, projectileIndex: 0, coolDown: 1400),
                        new Shoot(14, projectileIndex: 1, predictive: 1, coolDown: 1000, coolDownOffset: 700)
                    )
                ),
                ElysianLoot()
            )

            // ---- Oryx 5, Oryx the Ascended God (his shots: ORYX_SHOTS in tools/build_elysium.py) ----
            // Asleep behind the Holy Gates, then invulnerable while his Wardens live. After that:
            // - Judgement (100% to 60%): the Radiant Cross, the Firmament and the Divine Hunt in
            //   turn. At 80%, the Call of the Host: back to the middle to summon his Host, and from
            //   then on he picks each pattern at random.
            // - Transcendence (60%): back to the middle, heals everyone to full, then the same three
            //   patterns, Transcendent: a quicker Cross, blighting stars, and a Hunt that slows the
            //   whole arena, bleeds behind him and brings his Host up beneath him, though he's slower.
            // - Apotheosis (20%): his sprite turns gold and eight Halo Orbs circle the edge of the
            //   arena, firing in at its middle. Wrath: the Cross (aimed at you now) and the Hunt in
            //   turn, chasing all the while.
            // - Last Judgement (10%): invulnerable in the middle. The Holy Gates shut, the Pillars of
            //   Heaven ring the arena firing straight across it, and he throws the Spear of Heaven
            //   and fires the Radiant Cross, slowed right down. After 30 s, the Concession: the gates
            //   open and he can be killed.
            .Init("Oryx the Ascended God",
                new State(
                    new BossStats(320000, 55, 1.0, 0.35),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120, xAdjustment: 2),
                    new AnnounceOnDeath("{PL_LIST} cast down Oryx the Ascended God in his Elysium! The Realm is free... for now."),
                    new State("Dormant",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(18, "Wardens")
                    ),
                    new State("Wardens",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "My Holy Gates open. Come, then, and kneel... if my Wardens let you.") { Bubble = false },
                        new Shoot(15, 3, 12, projectileIndex: 0, predictive: 0.4, coolDown: 2000),
                        new EntitiesNotExistsTransition(40, "Awaken 1", "Ascended Warden")
                    ),
                    new State("Awaken 1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFE070, 0.4, 10),
                        new Taunt(true, "You struck down my Wardens. You stand in MY heaven now.") { Bubble = false },
                        new TimedTransition(3500, "Awaken 2")
                    ),
                    new State("Awaken 2",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt(true, "Kneel, and be JUDGED!") { Bubble = false },
                        new TimedTransition(2500, "I Cross")
                    ),

                    // ---- Judgement (100% to 80%): each pattern in turn ----
                    new State("Judgement",
                        new HpLessTransition(0.8, "Call of the Host"),
                        RadiantCross("I Cross", false, new TimedTransition(CrossTime, "I Firmament")),
                        Firmament("I Firmament", false, new TimedTransition(StarLife, "I Hunt")),
                        DivineHunt("I Hunt", false, false, ms => new TimedTransition(ms, "I Cross"))
                    ),
                    new State("Call of the Host",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new Flash(0xFFFFE070, 0.3, 10),
                        new Taunt(true, "Ascended Host! Descend, and defend your god!") { Bubble = false },
                        new TossObject("Ascended Officer", 7, 45, 90000000),
                        new TossObject("Ascended Cleric", 7, 135, 90000000),
                        new TossObject("Ascended Aristocrat", 7, 225, 90000000),
                        new TossObject("Ascended Sergeant", 7, 315, 90000000),
                        new TimedRandomTransition(4000, false, "J Cross", "J Firmament", "J Hunt")
                    ),
                    // ---- Judgement (80% to 60%): a pattern at random each time ----
                    new State("Judgement Unbound",
                        new HpLessTransition(0.6, "Transcendence"),
                        RadiantCross("J Cross", false, new TimedRandomTransition(CrossTime, false, "J Cross", "J Firmament", "J Hunt")),
                        Firmament("J Firmament", false, new TimedRandomTransition(StarLife, false, "J Cross", "J Firmament", "J Hunt")),
                        DivineHunt("J Hunt", false, false, ms => new TimedRandomTransition(ms, false, "J Cross", "J Firmament", "J Hunt"))
                    ),

                    // ---- Transcendence (60% to 20%) ----
                    new State("Transcendence",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new Flash(0xFFFFFFFF, 0.3, 16),
                        new Taunt(true, "I... have... TRANSCENDED! Rise, mortals, whole again, so you may be broken anew!") { Bubble = false },
                        new HealPlayer(30, 60000, 100000),
                        new TimedRandomTransition(5000, false, "T Cross", "T Firmament", "T Hunt")
                    ),
                    new State("Transcendent",
                        new HpLessTransition(0.2, "Apotheosis"),
                        RadiantCross("T Cross", true, new TimedRandomTransition(CrossTime, false, "T Cross", "T Firmament", "T Hunt")),
                        Firmament("T Firmament", true, new TimedRandomTransition(StarLife, false, "T Cross", "T Firmament", "T Hunt")),
                        DivineHunt("T Hunt", true, true, ms => new TimedRandomTransition(ms, false, "T Cross", "T Firmament", "T Hunt"))
                    ),

                    // ---- Apotheosis (20% to 10%) ----
                    new State("Apotheosis",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new SetAltTexture(1),
                        new Flash(0xFFFFE070, 0.25, 16),
                        new Taunt(true, "Behold my APOTHEOSIS! I am the light, and the light does not die!") { Bubble = false },
                        new SpawnRing("Halo Orb", 8, 14, aroundSpawn: true),
                        new TimedTransition(4000, "W Cross")
                    ),
                    new State("Wrath",
                        new HpLessTransition(0.1, "Last Judgement"),
                        // the Cross, aimed: one line of lances at you, arrowheads between
                        new State("W Cross",
                            new Follow(0.75, 25, 2),
                            new ShootFormation(25, 4, Lanes, 0, coolDown: 1300),
                            new ShootFormation(25, 4, Arrowhead, 1, angleOffset: 45, coolDown: 1300, coolDownOffset: 650),
                            new TimedTransition(6000, "W Hunt")
                        ),
                        DivineHunt("W Hunt", true, false, ms => new TimedTransition(ms, "W Cross"))
                    ),

                    // ---- Last Judgement (10%) ----
                    new State("Last Judgement",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(30, "Halo Orb", "Gone"),
                        new State("LJ Gather",
                            new ReturnToSpawn(2),
                            new Flash(0xFFFFFFFF, 0.25, 8),
                            new Taunt(true, "ENOUGH! The heavens themselves shall be your prison!") { Bubble = false },
                            new TimedTransition(2000, "LJ Fall")
                        ),
                        // the Holy Gates shut (rows 20 and 21 below him, three either side: GATE_ROWS and
                        // CORRIDOR in tools/build_elysium.py)
                        new State("LJ Fall",
                            new PlaceObject("O5 Holy Gate", -3, 20, 3, 21),
                            new SpawnRing("Pillar of Heaven", 24, 15, 7.5, aroundSpawn: true),
                            new TimedTransition(500, "LJ Siege")
                        ),
                        // the spear, and the Radiant Cross as it began (lanes and arrowheads, then turned),
                        // drifting slowly out to crowd the arena
                        new State("LJ Siege",
                            new Shoot(25, projectileIndex: 7, predictive: 0.4, coolDown: 2800, coolDownOffset: 1500),
                            new ShootFormation(25, 4, Lanes, 8, fixedAngle: 0, coolDown: 2400),
                            new ShootFormation(25, 4, Arrowhead, 9, fixedAngle: 45, coolDown: 2400),
                            new ShootFormation(25, 4, Lanes, 8, fixedAngle: 45, coolDown: 2400, coolDownOffset: 1200),
                            new ShootFormation(25, 4, Arrowhead, 9, fixedAngle: 0, coolDown: 2400, coolDownOffset: 1200),
                            new TimedTransition(30000, "Concession")
                        )
                    ),
                    new State("Concession",
                        new Order(30, "Pillar of Heaven", "Gone"),
                        new State("Concession Open",
                            new RemoveObject("O5 Holy Gate", 22),
                            new Flash(0xFFFFFFFF, 0.5, 6),
                            new Taunt(true, "I... concede. Strike, then, if you are worthy.") { Bubble = false },
                            new TimedTransition(1000, "Conceded")
                        ),
                        new State("Conceded")
                    )
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Mana", 0.5),
                    new ItemLoot("Greater Potion of Life", 0.2),
                    new ItemLoot("Greater Potion of Mana", 0.2),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Speed", 1),
                    new ItemLoot("Potion of Dexterity", 1),
                    new ItemLoot("Potion of Oryx", 0.25),
                    new ItemLoot("Potion of Power", 0.06)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Vitality", 0.7),
                    new ItemLoot("Potion of Wisdom", 0.7),
                    new ItemLoot("Potion of Attack", 0.3),
                    new ItemLoot("Potion of Defense", 0.3),
                    new ItemLoot("Potion of Speed", 0.3),
                    new ItemLoot("Potion of Dexterity", 0.3),
                    new ItemLoot("Shattered Rune of Oryx", 0.5),
                    new ItemLoot("Broken Oryx Armor", 0.4),
                    new ItemLoot("Purified Doom Saliva", 0.3),
                    new ItemLoot("Large Oryx Cloth", 0.15),
                    new ItemLoot("Small Oryx Cloth", 0.15),
                    new ItemLoot("Ring of Oryx", 0.02),
                    new ItemLoot("Oryx Hide Armor", 0.02),
                    new ItemLoot("Oryx Hide Robe", 0.02),
                    new ItemLoot("Oryx Scale Armor", 0.02),
                    new ItemLoot("Oryx Tooth Sword", 0.02),
                    new ItemLoot("Doom Dirk", 0.02),
                    new ItemLoot("Flying Doom Daggers", 0.02),
                    new ItemLoot("Tome of Noble Assault", 0.02),
                    new ItemLoot("Doom Poison", 0.01),
                    new ItemLoot("Sword of Dark Enchantment", 0.004),
                    new ItemLoot("Crown", 0.004)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(14, ItemType.Weapon, 0.25),
                    new TierLoot(15, ItemType.Armor, 0.2),
                    new TierLoot(6, ItemType.Ability, 0.25),
                    new TierLoot(6, ItemType.Ring, 0.25),
                    new TierLoot(7, ItemType.Ability, 0.05),
                    new TierLoot(7, ItemType.Ring, 0.05),
                    new TierLoot(8, ItemType.Ability, 0.006),
                    new TierLoot(8, ItemType.Ring, 0.006)
                )
            )

            // ---- his helpers, all invulnerable ----
            // the Firmament's stars, turning round him as one: they slow anyone who touches them and
            // shoot two motes each way, turning their cross a little each time
            .Init("Stilling Star", Star(0))
            .Init("Wounding Star", Star(300))
            .Init("Swift Star", Star(600))
            .Init("Blighting Star", Star(600))
            // the Halo Orbs of his Apotheosis: a ring turning round the edge of his arena, firing in at
            // its middle, so the arena closes in on you
            .Init("Halo Orb",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Revolve("Oryx the Ascended God", 20, aroundSpawn: true),
                    new ShootFromCenter("Oryx the Ascended God", new[] { 0 }, inward: true, count: 2, shootAngle: 20, coolDown: 1400,
                        aroundSpawn: true),
                    new State("Live"),
                    new State("Gone", new Suicide())
                )
            )
            // the Pillars of Heaven: rays straight across the arena (those at the sides across, those
            // above and below up or down), bleeding, blighting and breaking armour in turn
            .Init("Pillar of Heaven",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new ShootFromCenter("Oryx the Ascended God", new[] { 0, 1, 2 }, inward: true, snapToAxis: true,
                        coolDown: new Cooldown(1600, 500), coolDownOffset: 1000),
                    new State("Live"),
                    new State("Gone", new Suicide())
                )
            )

            // the guardians, for now: no attacks, and three Trees of Doom when they die
            .Init("Gilded Sphinx", Guardian())
            .Init("Oasis Seraph", Guardian())

            // the three who circle Oryx 5 in his temple, evenly spaced; when they fall, he wakes
            .Init("Ascended Warden",
                new State(
                    new BossStats(25000, 30, 1.0, 0),
                    new Orbit(0.6, 6, 20, "Oryx the Ascended God", speedVariance: 0, radiusVariance: 0, orbitClockwise: true)
                )
            )

            // opens the Holy Gates once no Tree of Doom and no guardian is left on the island
            .Init("O5 Gate Watcher",
                new State(
                    new State("Wait",
                        new TimedTransition(3000, "Watch")
                    ),
                    new State("Watch",
                        new EntitiesNotExistsTransition(150, "Open", ElysiumGatekeepers)
                    ),
                    new State("Open",
                        new RemoveObject("O5 Holy Gate", 10),
                        new Order(40, "Oryx the Ascended God", "Wardens"),
                        new TimedTransition(1000, "Done")
                    ),
                    new State("Done")
                )
            );

        // what keeps the Holy Gates shut
        private static readonly string[] ElysiumGatekeepers = { "Tree of Doom", "Gilded Sphinx", "Oasis Seraph" };

        // ---- Oryx the Ascended God's patterns ----
        private const int CrossTime = 8000;
        private const int StarLife = 12000;
        private const int HuntLeg = 10000;
        // the Radiant Cross: twin lances side by side, and arrowheads pointing outward
        private static readonly double[] Lanes = { 0, -0.55, 0, 0.55 };
        private static readonly double[] Arrowhead = { 0.7, 0, 0, -0.5, 0, 0.5, -0.7, -1.0, -0.7, 1.0 };

        // Pattern 1, the Radiant Cross: he follows the nearest player but keeps 6 tiles off, backing
        // away if you close in. Lances along four lines and arrowheads along the four between, then
        // the same turned 45 degrees, in turn; quicker once he has transcended.
        private static State RadiantCross(string name, bool transcended, Transition then)
        {
            var period = transcended ? 1600 : 2000;
            return new State(name,
                new Prioritize(new StayBack(transcended ? 0.8 : 0.7, 6), new Follow(transcended ? 0.65 : 0.55, 25, 6.5)),
                new ShootFormation(25, 4, Lanes, 0, fixedAngle: 0, coolDown: period),
                new ShootFormation(25, 4, Arrowhead, 1, fixedAngle: 45, coolDown: period),
                new ShootFormation(25, 4, Lanes, 0, fixedAngle: 45, coolDown: period, coolDownOffset: period / 2),
                new ShootFormation(25, 4, Arrowhead, 1, fixedAngle: 0, coolDown: period, coolDownOffset: period / 2),
                then
            );
        }

        // Pattern 2, the Firmament: he goes to the middle and raises twelve stars in a cross, three
        // along each diagonal, turning round him at 25 degrees a second: slowing stars nearest him,
        // bleeding ones next, and swift ones (blighting, once he has transcended) furthest out.
        private static State Firmament(string name, bool transcended, Transition then) =>
            new State(name,
                new State(name + " Rise",
                    new ReturnToSpawn(1.5),
                    new TimedTransition(2500, name + " Stars")
                ),
                new State(name + " Stars",
                    new ReturnToSpawn(1.5),
                    new RemoveEntitiesOnExit(30, "Stilling Star", "Wounding Star", "Swift Star", "Blighting Star"),
                    new SpawnRing("Stilling Star", 4, 3.5, 45, aroundSpawn: true),
                    new SpawnRing("Wounding Star", 4, 6, 45, aroundSpawn: true),
                    new SpawnRing(transcended ? "Blighting Star" : "Swift Star", 4, 8.5, 45, aroundSpawn: true),
                    then
                )
            );

        // Pattern 3, the Divine Hunt: he chases the nearest player for 10 s with a shotgun in front
        // (an arrowhead of spikes) and a slowing fan behind, throwing bleeding bombs round the arena.
        // Then he stops, invulnerable, names a player at random and hunts them for 10 s. Once he has
        // transcended he's slower, but the whole arena is slowed, the fan behind him bleeds and
        // (minions) his Host rise beneath him.
        private static State DivineHunt(string name, bool transcended, bool minions, Func<int, Transition> then)
        {
            IStateChildren[] Attack() => new IStateChildren[]
            {
                new Follow(transcended ? 0.6 : 0.85, 30, 1),
                new Shoot(10, projectileIndex: 2, coolDown: 1500),
                new Shoot(10, 2, 30, projectileIndex: 3, coolDown: 1500),
                new Shoot(10, 2, 60, projectileIndex: 4, coolDown: 1500),
                new Shoot(25, 9, 14, projectileIndex: transcended ? 6 : 5, angleOffset: 180, coolDown: 900, coolDownOffset: 450),
                new Grenade(2.5, transcended ? 140 : 110, 0, coolDown: transcended ? 2000 : 2600, effect: ConditionEffectIndex.Bleeding,
                    effectDuration: 3000, color: 0xFFE8C060, scatter: 14),
            }.Concat(transcended
                ? new IStateChildren[] { new ConditionAura(20, ConditionEffectIndex.Slowed, 1200, 1000) }
                : new IStateChildren[0]).ToArray();
            IStateChildren[] Rise() => minions
                ? new IStateChildren[] { new Spawn("Ascended Noble", 1, 1), new Spawn("Ascended Cardinal", 1, 1) }
                : new IStateChildren[0];

            return new State(name,
                new State(name + " Chase",
                    Attack().Concat(Rise()).Append<IStateChildren>(new TimedTransition(HuntLeg, name + " Pause")).ToArray()
                ),
                new State(name + " Prey",
                    new HuntRandomPlayer(30,
                        "{PLAYER}! I have chosen you.",
                        "{PLAYER}... you are judged.",
                        "Run, {PLAYER}. It changes nothing.",
                        "Your god comes for you, {PLAYER}!"),
                    new State(name + " Pause",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFE070, 0.25, 4),
                        new TimedTransition(1500, name + " Marked")
                    ),
                    new State(name + " Marked",
                        Attack().Append<IStateChildren>(then(HuntLeg)).ToArray()
                    )
                )
            );
        }

        private static State Star(int coolDownOffset) =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Revolve("Oryx the Ascended God", 25),
                new ConditionAura(1.2, ConditionEffectIndex.Slowed, 1500, 300),
                new Shoot(0, 4, fixedAngle: 0, rotateAngle: 15, projectileIndex: 0, coolDown: 1800, coolDownOffset: coolDownOffset),
                new Shoot(0, 4, fixedAngle: 0, rotateAngle: 15, projectileIndex: 0, coolDown: 1800, coolDownOffset: coolDownOffset + 250),
                new State("Live", new TimedTransition(StarLife - 500, "Gone")),
                new State("Gone", new Suicide())
            );

        private static State Guardian() =>
            new State(
                new BossStats(50000, 30, 1.0, 0),
                new TossOnDeath("Tree of Doom", 3, 8)
            );

        // a little better than the Inferno's (InfernalLoot)
        private static MobDrops[] ElysianLoot() => new MobDrops[]
        {
            new ItemLoot("Potion of Defense", 0.07),
            new ItemLoot("Potion of Attack", 0.07),
            new ItemLoot("Potion of Vitality", 0.04),
            new ItemLoot("Potion of Wisdom", 0.04),
            new ItemLoot("Greater Potion of Life", 0.012),
            new TierLoot(13, ItemType.Weapon, 0.05),
            new TierLoot(14, ItemType.Armor, 0.04),
            new TierLoot(5, ItemType.Ability, 0.05)
        };
    }
}
