using System;
using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Black Between (tools/build_black_between.py), the Mysterious One's dungeon: the voice that
    // interrupts Oryx at the end of Realm 2, in person. On a platform floating in the void he tries
    // those who silenced a realm against his daughter Vesper, the Dusk Star (purple and dark, a
    // mage), his son Aurel, the Dawn Blade (gold and bright, a swordsman), and then himself, with
    // both of their powers. Meant to be harder than Oryx 5, for players in full Enthroned gear.
    //
    // The Observatory Heart, invisible at the platform's centre, runs the arena: the bosses order it
    // into each phase, and it raises the walls of light, turns the floor and sends the starfall, so
    // all of it is centred on the platform and comes and goes with its states. The bosses come down
    // at the centre too (it spawns them there), so their spawn point is the platform's middle. Once
    // the fight starts the causeway is gone into the void until the Mysterious One falls, all but
    // the pad at its far end where players arrive (the map's Spawn region and the Nexus Portal):
    // anyone on the causeway is moved onto the platform first, and whoever arrives later (a /help,
    // the Rift) lands on the pad, safe, but can't cross to the fight.
    //
    // Survival phases (invulnerable, survive them): every boss's first silences everyone and puts
    // their pets in stasis; its last sickens them as well (Survival).
    //
    // Sizes from the map: the platform's floor reaches PlatformR tiles from the middle, the Veil's
    // diamond Diamond tiles to each point. The bosses see the whole platform (Sight).
    partial class BehaviorDb
    {
        private const double PlatformR = 17;
        private const int Diamond = 8;
        // once the Mysterious One breaks it, the platform's edge is gone down to this
        private const double BrokenR = 12.5;
        // far enough to reach anyone on the platform from anywhere on it
        private const double Sight = 36;
        // the causeway's pad (tools/build_black_between.py, SPAWN_ROWS less a row, to its end): this
        // many tiles south of the middle and on; and where on the platform the causeway's mouth is
        private const int CausewayPad = 38;
        private const double CausewayMouth = 14;
        private const uint Gold = 0xFFFFD24A;
        private const uint Violet = 0xFFB040FF;

        private _ BlackBetween = () => Behav()
            // ---- the arena ----
            .Init("Observatory Heart",
                new State(
                    new State("Waiting"),
                    new State("Fight",
                        // the causeway goes into the void from the first trial to the last, all
                        // but its pad; anyone on it is moved onto the platform first
                        new ClearTheCauseway(PlatformR, CausewayPad - 0.5, CausewayMouth),
                        new PaintFloor("Empty", 50, (dx, dy) => dy > 0 && dy < CausewayPad && dx * dx + dy * dy > PlatformR * PlatformR),
                        new State("Summon Vesper",
                            new Spawn("Vesper, the Dusk Star", 1, 1, givesNoXp: false),
                            new TimedTransition(500, "Trial I")
                        ),
                        new State("Trial I",
                            new EntityNotExistsTransition("Vesper, the Dusk Star", 60, "Interlude"),
                            new State("T1 Calm"),
                            // the Fourfold Veil: outside the diamond the floor burns, the cross is walls,
                            // and starshards fall into the four quadrants from Vesper and her copies
                            HeartVeil("T1 Veil", 300),
                            HeartVeil("T1 Veil Fast", 200),
                            // the Gravity Well: rings closing in from the edge
                            new State("T1 Well",
                                new SpawnRing("Gravity Point", 24, PlatformR - 1.5, aroundSpawn: true),
                                new RemoveEntitiesOnExit(30, "Gravity Point")
                            )
                        ),
                        // starfall over the whole platform while Aurel comes down
                        // (an Order acts every tick its state lasts, so each call to another object is
                        // a state of its own, shorter than whatever it sets off)
                        new State("Interlude",
                            new State("Interlude Call",
                                new Order(60, "Mysterious One Herald", "Interlude Speech"),
                                new TimedTransition(150, "Interlude Rain")
                            ),
                            new State("Interlude Rain",
                                new Starfall(Starfall.Area.Scatter, PlatformR - 1, 1.6, 330, 250, perVolley: 2, color: Gold),
                                new TimedTransition(10000, "Summon Aurel")
                            )
                        ),
                        new State("Summon Aurel",
                            new Spawn("Aurel, the Dawn Blade", 1, 1, givesNoXp: false),
                            new TimedTransition(500, "Trial II")
                        ),
                        new State("Trial II",
                            new EntityNotExistsTransition("Aurel, the Dawn Blade", 60, "Before III")
                        ),
                        new State("Before III",
                            new State("Before III Call",
                                new Order(60, "Mysterious One Herald", "Descend"),
                                new TimedTransition(150, "Before III Wait")
                            ),
                            new State("Before III Wait", new TimedTransition(4000, "Summon Father"))
                        ),
                        new State("Summon Father",
                            new Spawn("The Mysterious One", 1, 1, givesNoXp: false),
                            new TimedTransition(500, "Trial III")
                        ),
                        new State("Trial III",
                            new EntityNotExistsTransition("The Mysterious One", 60, "Done"),
                            new State("T3 Calm"),
                            // from 70% the platform's edge is gone for the rest of the fight
                            new State("T3 Broken",
                                new LavaRing("Black Between", PlatformR, PlatformR, BrokenR, 3, 12000),
                                new State("T3B Crumble",
                                    new Starfall(Starfall.Area.Scatter, BrokenR, 1.8, 360, 400, color: Violet, throwColor: Gold, twoTone: true,
                                        origins: new[] { "The Mysterious One" })
                                ),
                                new State("T3B Calm"),
                                // the Eclipse: Vesper's veil, starshards from her spectres
                                new State("T3B Eclipse",
                                    new GatherIntoQuadrants(Diamond, BrokenR + 2),
                                    new PaintFloor("Veil of Dusk", BrokenR, (dx, dy) => Math.Abs(dx) + Math.Abs(dy) > Diamond),
                                    new SpawnRing("Spectral Vesper", 4, Diamond, aroundSpawn: true),
                                    new RemoveEntitiesOnExit(30, "Spectral Vesper"),
                                    new Starfall(Starfall.Area.Diamond, Diamond, 1.6, 360, 350, color: Violet,
                                        effect: ConditionEffectIndex.Quiet, effectMs: 1500, origins: new[] { "Spectral Vesper" })
                                ),
                                // the Black Between: only the Constellation Path is safe
                                new State("T3B Black",
                                    // the whole platform, rim and all, whether or not its edge has burnt yet
                                    new ConstellationPath("Black Between", "Constellation Path", PlatformR + 1)
                                ),
                                // his daughter's spectres on the rim, starshards at the players
                                new State("T3B Rain",
                                    new SpawnRing("Spectral Vesper", 2, BrokenR - 1, 90, aroundSpawn: true),
                                    new RemoveEntitiesOnExit(30, "Spectral Vesper"),
                                    new Starfall(Starfall.Area.Players, Sight, 1.7, 360, 650, color: Violet,
                                        effect: ConditionEffectIndex.Quiet, effectMs: 1500, origins: new[] { "Spectral Vesper" })
                                )
                            )
                        )
                    ),
                    new State("Done")
                )
            )
            .Init("Gravity Point",
                new State(
                    new ShootFromCenter("Observatory Heart", new[] { 0 }, inward: true, coolDown: 2600, coolDownOffset: 400)
                )
            )

            // ---- the Mysterious One's herald: the voice, walking out of the Rift ----
            // He wakes when someone reaches the middle of the platform, walks out of the Rift and sets
            // the trials, then goes back and stands on it, watching; he speaks between the trials, and
            // before the third he is gone and the Mysterious One comes down in the middle.
            .Init("Mysterious One Herald",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    // he stands 14 tiles north of the middle: anyone near the middle wakes him
                    new State("Dormant", new PlayerWithinTransition(19, "Step Out")),
                    new State("Step Out",
                        new Flash(0xFFB040FF, 0.3, 4),
                        new MoveLine(0.5, 90, 4),
                        new Taunt(true, "This is interesting...") { Bubble = false },
                        new TimedTransition(3500, "Speak 1")
                    ),
                    new State("Speak 1",
                        new Taunt(true, "So you are the ones who silenced the Mad God's realm. I heard every step.") { Bubble = false },
                        new TimedTransition(4000, "Speak 2")
                    ),
                    new State("Speak 2",
                        new Taunt(true, "No one has needed a name for me in a very long time. Keep calling me a voice, if you like.") { Bubble = false },
                        new TimedTransition(4000, "Speak 3")
                    ),
                    new State("Speak 3",
                        new Taunt(true, "Three trials. My daughter, who keeps the dark. My son, who keeps the light. And then... me.") { Bubble = false },
                        new TimedTransition(4500, "Speak 4")
                    ),
                    new State("Speak 4",
                        new Taunt(true, "Vesper. Show them the dark.") { Bubble = false },
                        new TimedTransition(800, "Call Vesper")
                    ),
                    new State("Call Vesper",
                        new Order(60, "Observatory Heart", "Summon Vesper"),
                        new TimedTransition(150, "To Rift")
                    ),
                    // back, and up onto the Rift itself (two tiles north of where he began)
                    new State("To Rift",
                        new ReturnToSpawn(0.6),
                        new TimedTransition(4500, "Onto Rift")
                    ),
                    new State("Onto Rift",
                        new MoveLine(0.4, 270, 2),
                        new TimedTransition(3000, "Watching")
                    ),
                    new State("Watching"),
                    new State("Interlude Speech",
                        new Taunt(true, "She held you longer than most. Aurel... your turn.") { Bubble = false },
                        new TimedTransition(500, "Watching")
                    ),
                    new State("Descend",
                        new Flash(0xFFFFD24A, 0.3, 8),
                        new Taunt(true, "Then you will face what they came from.") { Bubble = false },
                        new TimedTransition(3000, "Gone")
                    ),
                    new State("Gone", new Suicide())
                )
            )

            // ---- Trial I: Vesper, the Dusk Star ----
            // - 100% to 75%: Dusk Volley (a fan of bolts that weave as they fly) and the Umbral Ring
            //   (orbs that bow as they spread).
            // - 75%: the Fourfold Veil (14 s, a first survival phase).
            // - 75% to 50%: the Starfall Waltz as well: she blinks round the platform, leaving dusk
            //   mines that burst, and throws more about the arena.
            // - 50%: the Veil again, faster (16 s, her last survival phase).
            // - 50% to 20%: the Gravity Well in turn with her kit.
            // - Below 20%: Nightfall: darkness on everyone.
            // From her last Veil on, two rings of orbs at once, turning opposite ways.
            .Init("Vesper, the Dusk Star",
                new State(
                    new BossStats(288000, 80, 1.0, 0.35),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFB040FF, 0.3, 6),
                        new Taunt(true, "As you wish, Father.") { Bubble = false },
                        new TimedTransition(2500, "Dusk")
                    ),
                    new State("Dusk",
                        new HpLessTransition(0.75, "Veil 1 Gather"),
                        new Prioritize(new StayCloseToSpawn(0.6, 8), new Wander(0.4)),
                        DuskVolley(1000),
                        UmbralRing(3000)
                    ),
                    VeilPhase("Veil 1", "T1 Veil", 14000, "Waltz Stand", false),
                    new State("Waltz",
                        new HpLessTransition(0.5, "Veil 2 Gather"),
                        new State("Waltz Stand",
                            new Prioritize(new StayCloseToSpawn(0.6, 12), new Wander(0.4)),
                            DuskVolley(1000),
                            UmbralRing(3000),
                            new TimedTransition(3500, "Waltz Blink")
                        ),
                        // a mine where she stood and three more about the arena
                        S("Waltz Blink", ScatterMines(),
                            new TossObject("Dusk Mine", 0.5, 0, coolDown: 90000000),
                            new TimedTransition(100, "Waltz Away")
                        ),
                        new State("Waltz Away",
                            new BlinkAround(6, PlatformR - 3),
                            new Flash(0xFFB040FF, 0.2, 2),
                            new TimedTransition(400, "Waltz Stand")
                        )
                    ),
                    VeilPhase("Veil 2", "T1 Veil Fast", 16000, "Well Kit", true),
                    // from her last Veil on: two rings at once, one turning clockwise, one the other way
                    // and the Gravity Well's orbs never stop coming in from the edge, and her dusk
                    // mines keep falling about the arena
                    new State("Late",
                    new Shoot(0, 18, fixedAngle: 0, rotateAngle: 10, projectileIndex: 1, coolDown: 2400),
                    MinesEvery(4000, 0),
                    MinesEvery(4000, 1300),
                    MinesEvery(4000, 2600),
                    new Shoot(0, 18, fixedAngle: 10, rotateAngle: -10, projectileIndex: 1, coolDown: 2400, coolDownOffset: 1200),
                    new State("Well",
                        new HpLessTransition(0.2, "Nightfall"),
                        new State("Well Kit",
                            new Order(60, "Observatory Heart", "T1 Well"),
                            new Prioritize(new StayCloseToSpawn(0.6, 8), new Wander(0.4)),
                            DuskVolley(900),
                            new TimedTransition(8000, "Well Pull")
                        ),
                        new State("Well Pull",
                            new ReturnToSpawn(1.0),
                            new Flash(0xFF5020A0, 0.3, 4),
                            new Taunt(0.5, "Everything falls inward, in the end.") { Bubble = false },
                            new Order(60, "Observatory Heart", "T1 Well"),
                            DuskVolley(1400),
                            new TimedTransition(9000, "Well Kit")
                        )
                    ),
                    new State("Nightfall",
                        new Order(60, "Observatory Heart", "T1 Well"),
                        new Taunt(true, "Then let it be night.") { Bubble = false },
                        new ConditionAura(Sight, ConditionEffectIndex.Darkness, 1500, 1000),
                        new Prioritize(new StayCloseToSpawn(0.7, 10), new Wander(0.5)),
                        DuskVolley(800)
                    )
                    )
                )
            )
            .Init("Vesper's Veil Copy",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    // every 2.5 s: the starshards are the Veil's danger, the copies only harry
                    new State("Live",
                        new Shoot(Sight, 3, 15, projectileIndex: 0, predictive: 0.3, coolDown: 2500, coolDownOffset: 1000)
                    ),
                    new State("Gone", new Suicide())
                )
            )
            .Init("Dusk Mine",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new State("Arm", new TimedTransition(1500, "Burst")),
                    new State("Burst",
                        new Shoot(0, 8, fixedAngle: 22.5, projectileIndex: 0, coolDown: 90000000),
                        new TimedTransition(200, "Gone")
                    ),
                    new State("Gone", new Suicide())
                )
            )
            // ---- Trial II: Aurel, the Dawn Blade (AltTexture 1: the Solar Cyclone; 2: empty-handed,
            // his sword flying) ----
            // All through his fight he calls divine lightning down all over the arena and on the
            // players (gold marks, then a bolt from him).
            // - 100% to 70%: Dawn Slash (three piercing crescents, breaking armour), the Sunburst ring,
            //   a weaving fan of sunsparks and a bowing ring of crescents (armour-breaking), and the
            //   Zenith Leap: he lands where a player is heading and his echo on another (alone: across
            //   the arena), which is which at random, and the echo strikes once and is gone.
            // - 70%: the Solar Cyclone: three dashes rim to rim, each through a player and marked
            //   first, a twin tornado crossing each at right angles (a first survival phase).
            // - 70% to 40%: Radiance: his kit, and the Sunwheel in turn (he stands and spins out four
            //   spiralling arms of crescents).
            // - 40%: the Sword Lanes (his last survival phase): he stands in the middle, immune, and his
            //   sword flies across the arena along every second column, up and down in turn, then
            //   along every second row, west and east in turn; then again on the other half of the
            //   lanes.
            // - 40% to 15%: his kit with the Cyclone every so often; below 15%, High Noon.
            .Init("Aurel, the Dawn Blade",
                new State(
                    new BossStats(336000, 100, 1.0, 0.35),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFD24A, 0.3, 6),
                        new Taunt(true, "Sister... I'll finish this.") { Bubble = false },
                        new Shoot(0, 16, fixedAngle: 0, projectileIndex: 1, coolDown: 90000000),
                        new TimedTransition(2500, "Dawn")
                    ),
                    S("Fight", DivineLightning(900),
                        new State("Dawn",
                            new HpLessTransition(0.7, "Cyclone 1"),
                            new Prioritize(new StayCloseToSpawn(0.9, 11), new Follow(0.75, Sight, 3)),
                            DawnSlash(1400),
                            Sunburst(3000),
                            DawnFan(1700),
                            CrescentRing(4200),
                            new LeapStrike(Sight, 2.5, 420, 1500, 6000, 3, echo: "Aurel's Echo")
                        ),
                        Cyclone("Cyclone 1", "Radiance Chase", 3, Survival(false).Concat(DivineLightning(900)).ToArray()),
                        new State("Radiance",
                            new SetAltTexture(0),
                            new HpLessTransition(0.4, "Sword Rise"),
                            new State("Radiance Chase",
                                new Prioritize(new StayCloseToSpawn(0.9, 11), new Follow(0.8, Sight, 3)),
                                DawnSlash(1200),
                                Sunburst(2600),
                                DawnFan(1500),
                                CrescentRing(3800),
                                new TimedTransition(6000, "Sunwheel")
                            ),
                            new State("Sunwheel",
                                new Flash(0xFFFFD24A, 0.25, 4),
                                new Taunt(0.4, "Turn, wheel of the dawn!") { Bubble = false },
                                new Shoot(0, 4, 90, fixedAngle: 0, rotateAngle: 14, projectileIndex: 4, coolDown: 180),
                                new TimedTransition(3500, "Radiance Chase")
                            )
                        ),
                        new State("Sword",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new State("Sword Rise",
                                new RealmHeal(2000),
                                new ReturnToSpawn(1.5),
                                new Flash(0xFFFFD24A, 0.25, 8),
                                new Taunt(true, "Stand where my sword is not.") { Bubble = false },
                                new TimedTransition(2000, "Sword Lanes")
                            ),
                            S("Sword Lanes", Survival(true).Concat(DivineLightning(900)).ToArray(),
                                new SetAltTexture(2),
                                new SwordLanes(PlatformR, 5),
                                // and he fights on from the middle meanwhile
                                DawnSlash(1400),
                                Sunburst(3000),
                                DawnFan(1700),
                                CrescentRing(4200),
                                new TimedTransition(16500, "Noon Rise")
                            )
                        ),
                        new State("Noon Rise",
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new SetAltTexture(0),
                            new ReturnToSpawn(1.5),
                            new Taunt(true, "Again. From the top.") { Bubble = false },
                            new TimedTransition(1500, "Noon Fight")
                        ),
                        new State("Noon",
                            new HpLessTransition(0.15, "High Noon"),
                            new State("Noon Fight",
                                new SetAltTexture(0),
                                new Prioritize(new StayCloseToSpawn(0.9, 11), new Follow(0.8, Sight, 3)),
                                DawnSlash(1200),
                                Sunburst(2600),
                                DawnFan(1500),
                                CrescentRing(3800),
                                new LeapStrike(Sight, 2.5, 420, 1500, 7000, 3, echo: "Aurel's Echo"),
                                new TimedTransition(6000, "Noon Sunwheel")
                            ),
                            new State("Noon Sunwheel",
                                new Flash(0xFFFFD24A, 0.25, 4),
                                new Shoot(0, 4, 90, fixedAngle: 0, rotateAngle: 14, projectileIndex: 4, coolDown: 180),
                                DawnFan(1500),
                                new TimedTransition(3500, "Noon Cyclone")
                            ),
                            Cyclone("Noon Cyclone", "Noon Fight", 2)
                        ),
                        new State("High Noon",
                            // armored to the end
                            new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                            new State("High Noon Fight",
                                new SetAltTexture(0),
                                new Taunt(0.6, "HIGH NOON!") { Bubble = false },
                                new Prioritize(new StayCloseToSpawn(0.9, 11), new Follow(0.85, Sight, 3)),
                                DawnSlash(1100),
                                DawnFan(1300),
                                // a beam of sunsparks turning round him like a clock's hand, both ways
                                new Shoot(0, 2, 180, fixedAngle: 0, rotateAngle: 12, projectileIndex: 1, coolDown: 160),
                                new TimedTransition(8000, "High Noon Cyclone")
                            ),
                            Cyclone("High Noon Cyclone", "High Noon Fight", 2)
                        )
                    )
                )
            )

            // the echo of his leap (LeapStrike moves it and lands it): then it strikes as he does,
            // once, and is gone. Not invulnerable, so nothing gives it away
            .Init("Aurel's Echo",
                new State(
                    new State("Leaping", new TimedTransition(1800, "Strike")),
                    new State("Strike",
                        new Shoot(Sight, 3, 20, projectileIndex: 0, coolDown: 90000000),
                        new Shoot(0, 12, fixedAngle: 0, projectileIndex: 1, coolDown: 90000000, coolDownOffset: 400),
                        new Shoot(Sight, 5, 12, projectileIndex: 2, predictive: 0.3, coolDown: 90000000, coolDownOffset: 800),
                        new TimedTransition(1400, "Gone")
                    ),
                    new State("Gone", new Suicide())
                )
            )

            // his twin tornado: moved and fired by his Cyclone (CycloneDash), gone when it ends
            .Init("Aurel's Twin Cyclone",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new State("Live"),
                    new State("Gone", new Suicide())
                )
            )

            // ---- Trial III: the Mysterious One ----
            // - 100% to 70%: Twin Heritage: his violet eye (Vesper's weaving volleys and bowing rings,
            //   starshards at you, dusk mines) and his gold eye (Aurel's crescents, divine lightning,
            //   a spectral Cyclone) in turn; always a slow ring of Eclipse Orbs.
            // - 70%: the Platform Breaks (15 s, a first survival phase): its edge burns away to BrokenR
            //   for the rest of the fight, meteors and a spectral Cyclone meanwhile.
            // - 70% to 40%: Twin Heritage and the Eclipse in turn.
            // - 40%: the Black Between (22 s, his last survival phase): everyone is put on the
            //   Constellation Path's start and must follow it while he fires on them.
            // - 40% to 10%: everything faster, his children in turn: Vesper's spectres casting
            //   starshards, then Aurel's sword flying its lanes and a spectral Cyclone; and every 15 s
            //   his black hole opens round him while all of that goes on.
            // - 10%: Supernova: back to the middle, then his rift opens round him, 9 tiles across in 5 s.
            //   Then the last 10% down to 5%, where he fades instead of dying: he speaks, sinks into his
            //   rift and is gone. His violet eye's rifts and his gold eye's lightning come all through.
            .Init("The Mysterious One",
                new State(
                    new BossStats(504000, 110, 1.0, 0.35),
                    // no pets in his trial: they're held in stasis all through it
                    new PetAura(Sight, ConditionEffectIndex.Stasis, 1000, 500),
                    new AnnounceOnDeath("{PL_LIST} passed the Mysterious One's trials in the Black Between, and he withdrew into the void. \"I will return.\""),
                    // once he's gone, the Rift he came out of (on the north rim, 17 tiles north of the
                    // middle, where he fades) is a portal to what's to come: locked, for now
                    new RemoveObjectOnDeath("Rift of the Between", 20),
                    // and the Cosmic Anvil, three squares south of the middle, for his Unstable Essence
                    new PlaceOnDeath("Cosmic Anvil", 0, 3),
                    new DropPortalOnDeath("Rift of the Between: Part II", 1.0, 0, 0, -17, name: "Part II: Coming Soon",
                        lockedHint: "The Between stirs beyond the Rift... Part II is coming soon."),
                    new State("Arrive",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFFFFF, 0.3, 8),
                        new Taunt(true, "Dusk and dawn. Both mine. Both through me.") { Bubble = false },
                        new TimedTransition(3000, "Heritage Dusk")
                    ),
                    new State("Heritage",
                        new HpLessTransition(0.7, "Break"),
                        new Prioritize(new StayCloseToSpawn(0.5, 4), new Wander(0.3)),
                        EclipseRing(3200),
                        HeritageDusk("Heritage Dusk", "Heritage Dawn", 6000, 1.0),
                        HeritageDawn("Heritage Dawn", "Heritage Dusk", 6000, 1.0)
                    ),
                    S("Break", Survival(false),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new Flash(0xFFFF4040, 0.3, 10),
                        new Taunt(true, "This platform was a kindness. I take it back.") { Bubble = false },
                        new Order(60, "Observatory Heart", "T3B Crumble"),
                        new TossObject("Spectral Aurel", 2, 0, coolDown: 6000, coolDownOffset: 2000),
                        new Shoot(0, 10, fixedAngle: 0, rotateAngle: 13, projectileIndex: 5, coolDown: 2400),
                        new TimedTransition(15000, "Eclipse Heritage")
                    ),
                    new State("Eclipse",
                        new HpLessTransition(0.4, "Black Heal"),
                        new State("Eclipse Heritage",
                            new Order(60, "Observatory Heart", "T3B Calm"),
                            new Prioritize(new StayCloseToSpawn(0.5, 4), new Wander(0.3)),
                            EclipseRing(3000),
                            HeritageDusk("E Dusk", "E Dawn", 5000, 0.9),
                            HeritageDawn("E Dawn", "E Fall", 5000, 0.9),
                            new State("E Fall", new TimedTransition(1, "Eclipse Gather"))
                        ),
                        // everyone into the diamond a moment before the veil's floor burns
                        new State("Eclipse Gather",
                            new GatherIntoQuadrants(Diamond, PlatformR + 2),
                            new ReturnToSpawn(1.5),
                            new Flash(0xFFB040FF, 0.3, 4),
                            new Taunt(0.5, "Her veil. My stars.") { Bubble = false },
                            new TimedTransition(1500, "Eclipse Fall")
                        ),
                        new State("Eclipse Fall",
                            new ReturnToSpawn(1.5),
                            new Order(60, "Observatory Heart", "T3B Eclipse"),
                            new Shoot(0, 8, fixedAngle: 22.5, rotateAngle: 9, projectileIndex: 4, coolDown: 2800),
                            new TimedTransition(14000, "Eclipse Heritage")
                        )
                    ),
                    // before each sickening phase: his heal, from their realm
                    new State("Black Heal",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(60, "Observatory Heart", "T3B Calm"),
                        new RealmHeal(1800),
                        new TimedTransition(1800, "Black")
                    ),
                    // he fires on them all the while they follow the path
                    S("Black", Survival(true),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new Taunt(true, "Now... see where I live. Stand in the light, while it lasts.") { Bubble = false },
                        new Order(60, "Observatory Heart", "T3B Black"),
                        // no tornadoes while they walk the path: any spectral Cyclone still about is gone
                        new Order(60, "Spectral Aurel", "Gone"),
                        new Shoot(0, 10, fixedAngle: 0, rotateAngle: 13, projectileIndex: 5, coolDown: 1900, coolDownOffset: 2500),
                        new Shoot(Sight, 3, 16, projectileIndex: 0, coolDown: 1300, coolDownOffset: 3000),
                        new Starfall(Starfall.Area.Players, Sight, 1.3, 260, 2300, color: Gold, lightning: true, fillMs: 1200,
                            coolDownOffset: 3500),
                        new TimedTransition(22500, "Black End")
                    ),
                    new State("Black End",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(60, "Observatory Heart", "T3B Calm"),
                        new TimedTransition(1500, "Children Heritage")
                    ),
                    new State("Children",
                        new HpLessTransition(0.1, "Nova Heal"),
                        new Prioritize(new StayCloseToSpawn(0.6, 4), new Wander(0.3)),
                        EclipseRing(2600),
                        BlackHole(15000, 6000),
                        new State("Children Heritage",
                            new Order(60, "Observatory Heart", "T3B Calm"),
                            HeritageDusk("C Dusk", "C Dawn", 4000, 0.8),
                            HeritageDawn("C Dawn", "C Daughter", 4000, 0.8),
                            new State("C Daughter", new TimedTransition(1, "Children Daughter"))
                        ),
                        new State("Children Daughter",
                            new Taunt(0.4, "Vesper.") { Bubble = false },
                            new Order(60, "Observatory Heart", "T3B Rain"),
                            DuskVolleyFather(1100),
                            new TimedTransition(7000, "Children Son")
                        ),
                        // his son's sword along its lanes, over what's left of the platform
                        new State("Children Son",
                            new Order(60, "Observatory Heart", "T3B Calm"),
                            new Taunt(0.4, "Aurel.") { Bubble = false },
                            new SwordLanes(BrokenR, 6, 1),
                            new TossObject("Spectral Aurel", 2, 0, coolDown: 90000000),
                            new TimedTransition(8000, "Children Heritage")
                        )
                    ),
                    new State("Nova Heal",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(60, "Observatory Heart", "T3B Calm"),
                        new RealmHeal(1800),
                        new TimedTransition(1800, "Nova Gather")
                    ),
                    // back to the middle first, then his rift opens round him there
                    S("Nova Gather", Survival(true),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(60, "Observatory Heart", "T3B Calm"),
                        new ReturnToSpawn(2.5, 0.3),
                        new Flash(0xFFB040FF, 0.25, 8),
                        new Taunt(true, "Everything I am, all at once. RUN.") { Bubble = false },
                        new TimedTransition(2500, "Nova")
                    ),
                    // and it keeps shooting while it opens: his volleys, rings and starshards
                    S("Nova", Survival(true),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFB040FF, 0.25, 12),
                        new Starfall(Starfall.Area.Host, 0, 9, 1600, 90000000, color: Violet, fillMs: 5000, style: 1),
                        EclipseRing(1800),
                        new Shoot(Sight, 5, 12, projectileIndex: 0, predictive: 0.3, coolDown: 900),
                        new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 1, coolDown: 2200),
                        new Starfall(Starfall.Area.Players, Sight, 1.6, 340, 1400, color: Violet,
                            effect: ConditionEffectIndex.Quiet, effectMs: 1500),
                        new TimedTransition(6000, "Last")
                    ),
                    new State("Last",
                        new HpLessTransition(0.05, "Fading"),
                        new Prioritize(new StayCloseToSpawn(0.7, 6), new Follow(0.6, Sight, 4)),
                        EclipseRing(2400),
                        BlackHole(12000, 4000),
                        HeritageDusk("L Dusk", "L Dawn", 4000, 0.75),
                        HeritageDawn("L Dawn", "L Dusk", 4000, 0.75)
                    ),
                    // he doesn't die: at 5% he stops, speaks, sinks into his rift and is gone (only then
                    // does his portal come, and the announcement)
                    new State("Fading",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(60, "Observatory Heart", "T3B Calm"),
                        new State("Fading Words",
                            new ReturnToSpawn(2.5, 0.3),
                            new Flash(0xFFFFFFFF, 0.4, 6),
                            new Taunt(true, "Enough. What you fought was only the shadow I cast into this realm.") { Bubble = false },
                            new TimedTransition(4000, "Fading Promise")
                        ),
                        new State("Fading Promise",
                            new Taunt(true, "The Between will remember you. And I... will return.") { Bubble = false },
                            new Starfall(Starfall.Area.Host, 0, 3, 0, 90000000, color: Violet, fillMs: 4500, style: 1),
                            new Flash(0xFFB040FF, 0.3, 14),
                            new TimedTransition(1500, "Fading Away")
                        ),
                        new State("Fading Away",
                            new ChangeSize(-6, 0),
                            new TimedTransition(3200, "Gone")
                        ),
                        new State("Gone", new Suicide())
                    )
                ),
                // his potions, as many as Oryx 5's, life and mana only; the Max potions as the Elder
                // bosses drop them, at half their rate; and Unstable Essence, nine tenths as often
                // as one of Oryx 5's Ascended shards, for the Cosmic Anvil
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Greater Potion of Life", 0.5),
                    new ItemLoot("Greater Potion of Mana", 0.5),
                    new ItemLoot("Unstable Essence", 0.1125)
                ),
                new Threshold(0.03,
                    new ItemLoot("Potion of Life", 0.7),
                    new ItemLoot("Potion of Mana", 0.7),
                    new ItemLoot("Potion of Life", 0.7),
                    new ItemLoot("Potion of Mana", 0.7),
                    new ItemLoot("Potion of Max Life", 0.025),
                    new ItemLoot("Potion of Max Mana", 0.025),
                    new ItemLoot("Potion of Max Attack", 0.025),
                    new ItemLoot("Potion of Max Defense", 0.025),
                    new ItemLoot("Potion of Max Speed", 0.025),
                    new ItemLoot("Potion of Max Dexterity", 0.025),
                    new ItemLoot("Potion of Max Vitality", 0.025),
                    new ItemLoot("Potion of Max Wisdom", 0.025),
                    // the family's gear, white bags: about one in a hundred each
                    new ItemLoot("Quiver of the Solar Cyclone", 0.01),
                    new ItemLoot("Trinity Trap", 0.01),
                    new ItemLoot("Helm of the Dawn Blade", 0.01),
                    new ItemLoot("Prism of the Dusk Veil", 0.01),
                    new ItemLoot("Cloak of the Planar Rift", 0.01),
                    new ItemLoot("Vial of Cascading Dusk", 0.01)
                )
            )

            // ---- in Oryx's Elysium: when Oryx 5 falls (OpenTheBetween), he steps out of his Rift onto
            // ground he turns to the void, speaks, and watches over the Rift while it stays open ----
            .Init("Mysterious One Visitant",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PaintFloor("Void Scar", 4.5, (dx, dy) => true),
                    new State("Step Out",
                        new Flash(0xFFB040FF, 0.3, 6),
                        new MoveLine(0.5, 90, 2.5),
                        new Taunt(true, "This is interesting...") { Bubble = false },
                        new TimedTransition(3500, "Speak 1")
                    ),
                    new State("Speak 1",
                        new Taunt(true, "So. You are the ones who have been silencing this realm, one god at a time.") { Bubble = false },
                        new TimedTransition(4500, "Speak 2")
                    ),
                    new State("Speak 2",
                        new Taunt(true, "Oryx only kept this place. I am what it was kept from.") { Bubble = false },
                        new TimedTransition(4500, "Speak 3")
                    ),
                    new State("Speak 3",
                        new Flash(0xFFFFD24A, 0.3, 4),
                        new Taunt(true, "My Rift is open. Step into the Between, if you would meet my children... and me.") { Bubble = false },
                        new TimedTransition(3000, "Watching")
                    ),
                    new State("Watching")
                )
            )
            .Init("Spectral Vesper",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Shoot(Sight, 3, 15, projectileIndex: 0, predictive: 0.3, coolDown: 1600)
                )
            )
            // a spectre of Aurel's Cyclone (AltTexture 1): three dashes across the whole arena, then gone
            .Init("Spectral Aurel",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new State("Spin",
                        new SetAltTexture(1),
                        new CycloneDash(PlatformR, 15, 900, 3, 0, contactDamage: 220, centreOn: "Observatory Heart"),
                        new TimedTransition(11000, "Gone")
                    ),
                    new State("Gone", new Suicide())
                )
            )

            // ---- the Constellation Approach: the Sentinels drift over the causeway (scenery, as
            // Oryx 5's clouds: they don't shoot, and nothing hits them) ----
            .Init("Starbound Sentinel",
                new State(
                    new Drift(0.3, 2)
                )
            );

        private static readonly string[] VesperNames = { "Vesper" };

        // a state of these children and more
        private static State S(string name, IStateChildren[] some, params IStateChildren[] more) =>
            new State(name, some.Concat(more).ToArray());

        // a survival phase's toll on everyone on the platform: the first of a boss's silences them
        // and puts their pets in stasis; the last sickens them as well
        private static IStateChildren[] Survival(bool last)
        {
            var toll = new IStateChildren[]
            {
                new ConditionAura(Sight, ConditionEffectIndex.Silenced, 1000, 500),
                new PetAura(Sight, ConditionEffectIndex.Stasis, 1000, 500),
            };
            return last ? toll.Append(new ConditionAura(Sight, ConditionEffectIndex.Sick, 1000, 500)).ToArray() : toll;
        }

        // the Heart's Fourfold Veil: everyone into the diamond first, then outside it the floor burns
        // and starshards fall into it every `coolDown` ms, thrown by Vesper and her copies at its four
        // points (violet marks)
        private static State HeartVeil(string name, int coolDown) =>
            new State(name,
                new GatherIntoQuadrants(Diamond, PlatformR + 2),
                new PaintFloor("Veil of Dusk", PlatformR, (dx, dy) => Math.Abs(dx) + Math.Abs(dy) > Diamond),
                new Starfall(Starfall.Area.Diamond, Diamond, 1.6, 380, coolDown, color: Violet,
                    effect: ConditionEffectIndex.Quiet, effectMs: 1500, origins: VesperNames)
            );

        // Vesper's Fourfold Veil: she goes to the middle, splits (copies east, south and west), goes
        // to the north point herself, and the Heart raises the veil (heartState) for `ms`.
        private static State VeilPhase(string name, string heartState, int ms, string next, bool last) =>
            new State(name,
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                // everyone into the diamond as she gathers, a few seconds before the floor burns
                // (and again as it does, for anyone who wandered out)
                new State(name + " Gather",
                    // before her last Veil (a sickening one), her father's heal
                    new RealmHeal(last ? 3000 : 0),
                    new GatherIntoQuadrants(Diamond, PlatformR + 2),
                    new ReturnToSpawn(1.2),
                    new Flash(0xFFB040FF, 0.25, 6),
                    new Taunt(true, "Four of me. One of you. Choose your corner.") { Bubble = false },
                    new TimedTransition(1800, name + " Split")
                ),
                // to the north point first: the walls would stand in her way
                new State(name + " Split",
                    new TossObject("Vesper's Veil Copy", Diamond, 0, coolDown: 90000000),
                    new TossObject("Vesper's Veil Copy", Diamond, 90, coolDown: 90000000),
                    new TossObject("Vesper's Veil Copy", Diamond, 180, coolDown: 90000000),
                    new MoveLine(1.5, 270, Diamond),
                    new TimedTransition(1200, name + " Hold")
                ),
                S(name + " Hold", Survival(last),
                    new Order(60, "Observatory Heart", heartState),
                    new TimedTransition(ms, name + " End")
                ),
                new State(name + " End",
                    new Order(60, "Vesper's Veil Copy", "Gone"),
                    new Order(60, "Observatory Heart", "T1 Calm"),
                    new ReturnToSpawn(1.0),
                    new TimedTransition(1500, next)
                )
            );

        // three more dusk mines thrown about the arena
        private static IStateChildren[] ScatterMines() => new IStateChildren[]
        {
            new TossObject("Dusk Mine", Sight, coolDown: 90000000, minAngle: 0, maxAngle: 360, minRange: 4, maxRange: 13),
            new TossObject("Dusk Mine", Sight, coolDown: 90000000, minAngle: 0, maxAngle: 360, minRange: 4, maxRange: 13),
            new TossObject("Dusk Mine", Sight, coolDown: 90000000, minAngle: 0, maxAngle: 360, minRange: 4, maxRange: 13),
        };

        private static Behavior MinesEvery(int ms, int offset) =>
            new TossObject("Dusk Mine", Sight, coolDown: ms, coolDownOffset: offset, minAngle: 0, maxAngle: 360, minRange: 4, maxRange: 13);

        private static Behavior DuskVolley(int coolDown) => new Shoot(Sight, 5, 12, projectileIndex: 0, predictive: 0.3, coolDown: coolDown);
        private static Behavior UmbralRing(int coolDown) => new Shoot(0, 18, fixedAngle: 0, rotateAngle: 10, projectileIndex: 1, coolDown: coolDown);
        private static Behavior DawnSlash(int coolDown) => new Shoot(Sight, 3, 20, projectileIndex: 0, coolDown: coolDown);
        private static Behavior Sunburst(int coolDown) => new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 1, coolDown: coolDown);
        // in his sister's manner: five sunsparks weaving at you, and a ring of crescents bowing out
        // as they spread, breaking armour
        private static Behavior DawnFan(int coolDown) =>
            new Shoot(Sight, 5, 12, projectileIndex: 6, predictive: 0.3, coolDown: coolDown, coolDownOffset: 700);
        private static Behavior CrescentRing(int coolDown) =>
            new Shoot(0, 14, fixedAngle: 0, rotateAngle: -12, projectileIndex: 7, coolDown: coolDown, coolDownOffset: 1500);
        private static Behavior EclipseRing(int coolDown) => new Shoot(0, 8, fixedAngle: 0, rotateAngle: 9, projectileIndex: 4, coolDown: coolDown);
        private static Behavior DuskVolleyFather(int coolDown) => new Shoot(Sight, 5, 12, projectileIndex: 0, predictive: 0.3, coolDown: coolDown);

        // Aurel's divine lightning: gold marks all over the arena, four at a time; a bolt from him
        // strikes each as it fills, paralysing a moment (none aimed under the players)
        private static IStateChildren[] DivineLightning(int coolDown) =>
            new IStateChildren[]
            {
                new Starfall(Starfall.Area.Scatter, PlatformR - 1, 1.3, 280, coolDown, perVolley: 4, color: Gold, lightning: true, fillMs: 1000,
                    effect: ConditionEffectIndex.Paralyzed, effectMs: 600),
            };

        // Aurel's Solar Cyclone: `passes` dashes across the whole arena, his sprite the Cyclone's,
        // and his twin tornado crossing each at right angles (with a survival phase's toll when it is
        // one)
        private static State Cyclone(string name, string next, int passes, IStateChildren[] toll = null) =>
            new State(name,
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                S(name + " Spin", toll ?? new IStateChildren[0],
                    new SetAltTexture(1),
                    new Flash(0xFFFFD24A, 0.2, 4),
                    new CycloneDash(PlatformR, 16, 800, passes, 2, centreOn: "Observatory Heart", twin: "Aurel's Twin Cyclone"),
                    new CycloneDoneTransition(name + " Down"),
                    // only should something hold it up
                    new TimedTransition(passes * 6000 + 2000, name + " Down"),
                    // his twin, once (a Spawn left running sends another whenever the twin is away)
                    new State(name + " Twin",
                        new Spawn("Aurel's Twin Cyclone", 1, 1),
                        new TimedTransition(100, name + " Turning")
                    ),
                    new State(name + " Turning")
                ),
                new State(name + " Down",
                    new SetAltTexture(0),
                    new Order(60, "Aurel's Twin Cyclone", "Gone"),
                    new TimedTransition(1000, next)
                )
            );

        // the father's violet eye: his daughter's weaving volleys and bowing rings, starshards at the
        // players, and now and then a dusk mine thrown out
        private static State HeritageDusk(string name, string next, int ms, double pace) =>
            new State(name,
                new Flash(0xFFB040FF, 0.3, 2),
                new Shoot(Sight, 5, 12, projectileIndex: 0, predictive: 0.3, coolDown: (int)(1000 * pace)),
                new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 1, coolDown: (int)(2600 * pace)),
                new Starfall(Starfall.Area.Players, Sight, 1.6, 340, (int)(1600 * pace), color: Violet,
                    effect: ConditionEffectIndex.Quiet, effectMs: 1500),
                new TossObject("Dusk Mine", Sight, coolDown: (int)(2500 * pace), minAngle: 0, maxAngle: 360, minRange: 3, maxRange: 10),
                Portals(pace),
                new TimedTransition(ms, next)
            );

        // his gold eye: his son's crescents and sunburst, divine lightning, and a spectre of the Cyclone
        private static State HeritageDawn(string name, string next, int ms, double pace) =>
            new State(name,
                new Flash(0xFFFFD24A, 0.3, 2),
                new Shoot(Sight, 3, 20, projectileIndex: 2, coolDown: (int)(1300 * pace)),
                new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 3, coolDown: (int)(2400 * pace)),
                new Starfall(Starfall.Area.Scatter, BrokenR, 1.3, 280, (int)(900 * pace), perVolley: 2, color: Gold, lightning: true, fillMs: 1000),
                new TossObject("Spectral Aurel", 2, 0, coolDown: 90000000),
                Portals(pace),
                new TimedTransition(ms, next)
            );

        // his black hole in the thick of his attacks: his rift opening round where he stands, 6.5 tiles
        // across in 3.5 s, every `coolDown`
        private static Behavior BlackHole(int coolDown, int offset) =>
            new Starfall(Starfall.Area.Host, 0, 6.5, 900, coolDown, color: Violet, fillMs: 3500, style: 1, coolDownOffset: offset);

        // his portals: small rifts opening about the arena, two at a time, bursting as they open
        private static Behavior Portals(double pace) =>
            new Starfall(Starfall.Area.Scatter, BrokenR, 2.2, 380, (int)(2600 * pace), perVolley: 2, color: Violet, style: 1, fillMs: 1800,
                effect: ConditionEffectIndex.Darkness, effectMs: 1500);
    }
}
