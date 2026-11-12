using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // The Cultist Hideout, entered from the Lost Halls (map: tools/build_cult.py). Followers of
        // Malus and his demons hold the corridors. In his hall The Cult (the controller below the
        // pentagram) calls Malus, who raises his four cultists on the star's points and wakes them
        // one at a time: Argus, Gaius, Basaran, Dirge. Then he summons the fire demon Molek, then
        // Balaam, who splits into his three heads when slain, and at last takes his cultists' powers
        // for himself (LH Malus 2), cycling through them. He drops the cult's gear and the Vial of
        // Pure Darkness, which opens the Void in the Lost Halls (BehaviorDb.Void.cs).
        private _ Cult = () => CultBehaviours(Behav(), false);
        // The Elder Cultist Hideout, entered from the Elder Lost Halls: the Elder cult, and Malus'
        // own phase, the Pentagram.
        private _ ElderCult = () => CultBehaviours(Behav(), true);

        private static Ctor CultBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            // what every follower and demon may drop
            MobDrops[] MinionLoot() =>
            [
                new ItemLoot("Potion of Attack", 0.02),
                new ItemLoot("Potion of Defense", 0.02),
                new TierLoot(11, ItemType.Weapon, 0.04),
                new TierLoot(12, ItemType.Armor, 0.04),
                new TierLoot(5, ItemType.Ability, 0.03),
                new TierLoot(5, ItemType.Ring, 0.03)
            ];

            // a follower: closes in on the nearest player, else keeps near its post
            State Follower(params IStateChildren[] attacks) =>
                new State(
                    [.. attacks,
                    new Prioritize(
                        new Follow(0.45, 9, 3),
                        new StayCloseToSpawn(0.3, 4),
                        new Wander(0.25)
                        )]
                    );

            // a cultist on the pentagram: a dark shape (invulnerable, shooting now and then) until
            // Malus wakes it, then itself, calling its followers to it
            State Cultist(string follower, string wake, string low, params IStateChildren[] attacks) =>
                new State(
                    new ScaleHP2(20),
                    new State("Dormant",
                        // not perm: leaving the state takes it off
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(1),
                        new ReturnToSpawn(0.6, 0.2),
                        new Shoot(14, 1, projectileIndex: 0, coolDown: 3500)
                        ),
                    new State("Awake",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 2000),
                        new SetAltTexture(0),
                        new Flash(0xFFFFFF, 0.3, 6),
                        new Taunt(true, wake),
                        new TimedTransition(2000, "Fight")
                        ),
                    new State("Fight",
                        [.. attacks,
                        new Spawn(follower, maxChildren: 2, initialSpawn: 0, coolDown: 15000),
                        new HpLessTransition(0.3, "Low")]
                        ),
                    new State("Low",
                        [.. attacks,
                        new Taunt(true, low),
                        new Spawn(follower, maxChildren: 3, initialSpawn: 0, coolDown: 10000)]
                        )
                    );

            return db
            // the followers, one colour for each cultist they serve
            .Init(E("LH Red Cultist"),
                Follower(
                    new Shoot(9, 3, 20, 0, coolDown: Cd(1600)),
                    new Shoot(9, 1, projectileIndex: 1, predictive: 0.5, coolDown: Cd(1100)),
                    new Shoot(9, 2, 12, 2, coolDown: Cd(2400), coolDownOffset: 600)
                    ),
                MinionLoot())
            .Init(E("LH Blue Cultist"),
                Follower(
                    new Shoot(8, 2, 12, 0, coolDown: Cd(900))
                    ),
                MinionLoot())
            .Init(E("LH Yellow Cultist"),
                Follower(
                    new Shoot(10, 3, 25, 0, coolDown: Cd(1500))
                    ),
                MinionLoot())
            .Init(E("LH White Cultist"),
                Follower(
                    new Shoot(9, 1, projectileIndex: 0, predictive: 0.6, coolDown: Cd(650))
                    ),
                MinionLoot())
            .Init(E("LH Purple Cultist"),
                Follower(
                    new Shoot(12, 6, 60, 0, fixedAngle: 0, rotateAngle: 20, coolDown: Cd(2000)),
                    new Shoot(12, 1, projectileIndex: 0, coolDown: Cd(1400))
                    ),
                MinionLoot())
            .Init(E("LH Black Cultist"),
                Follower(
                    // slow dark bolts that hang in the air
                    new Shoot(10, 8, 45, 0, fixedAngle: 22.5, rotateAngle: 11, coolDown: Cd(3200)),
                    new Shoot(10, 2, 15, 0, coolDown: Cd(1800))
                    ),
                MinionLoot())
            .Init(E("LH Green Cultist"),
                Follower(
                    new Shoot(8, 3, 15, 0, coolDown: Cd(1100)),
                    // Phaedra's followers tend the others
                    new HealGroup(8, "Cult Cultists", coolDown: 4000, healAmount: 500)
                    ),
                MinionLoot())

            // Malus' demons: fast, few hit points, no experience
            .Init(E("LH Fire Demon"),
                new State(
                    new Prioritize(
                        new Charge(1.4, 9, coolDown: 3000),
                        new Follow(0.8, 10, 1),
                        new Wander(0.4)
                        ),
                    new Shoot(8, 3, 25, 1, coolDown: Cd(900)),
                    new Shoot(4, 6, 60, 0, coolDown: Cd(2000))
                    ),
                MinionLoot())
            .Init(E("LH Ice Demon"),
                new State(
                    new Prioritize(
                        new Follow(0.6, 10, 4),
                        new Wander(0.4)
                        ),
                    new Shoot(9, 5, 14, 0, coolDown: Cd(1400))
                    ),
                MinionLoot())
            .Init(E("LH Lightning Demon"),
                new State(
                    new Prioritize(
                        new Orbit(0.9, 3.5, 10, speedVariance: 0.2, radiusVariance: 0.5),
                        new Wander(0.5)
                        ),
                    new Shoot(9, 2, 10, 0, predictive: 0.5, coolDown: Cd(1000))
                    ),
                MinionLoot())

            // the hall: The Cult calls Malus when a player comes near, and goes once he is slain
            .Init(E("LH Cult Controller"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("Idle",
                        new PlayerWithinTransition(13, "Greet")
                        ),
                    new State("Greet",
                        new Taunt(true, "Intruders in our hideout? The master will deal with you himself!"),
                        new TimedTransition(2500, "Call")
                        ),
                    new State("Call",
                        new TossObject(N("LH Malus"), 6, 270, coolDown: 9999999),
                        new TimedTransition(1000, "Fight")
                        ),
                    new State("Fight",
                        new EntityExistsTransition(N("LH Malus 2"), 60, "Final")
                        ),
                    new State("Final",
                        new EntitiesNotExistsTransition(60, "Gone", N("LH Malus"), N("LH Malus 2"))
                        ),
                    new State("Gone",
                        new Decay(0)
                        )
                    )
                )

            // Malus, while his cultists live: never hit, he leads the ritual from the centre
            .Init(E("LH Malus"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                    new State("Rise",
                        new SetAltTexture(1),
                        new Flash(0xFF0000, 0.4, 8),
                        new Taunt(true, "So, the Lost Halls let you through. My cult will be your end!"),
                        // his four cultists, on the star's points round him
                        new TossObject(N("LH Argus"), 3.5, 342, coolDown: 9999999),
                        new TossObject(N("LH Gaius"), 3.5, 54, coolDown: 9999999),
                        new TossObject(N("LH Basaran"), 3.5, 126, coolDown: 9999999),
                        new TossObject(N("LH Dirge"), 3.5, 198, coolDown: 9999999),
                        new TimedTransition(3500, "Ritual")
                        ),
                    new State("Ritual",
                        new SetAltTexture(0),
                        new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 15, coolDown: Cd(2800)),
                        new Shoot(16, 1, projectileIndex: 0, predictive: 0.4, coolDown: Cd(2200)),
                        new State("Argus",
                            new OrderOnce(15, N("LH Argus"), "Awake"),
                            new EntityNotExistsTransition(N("LH Argus"), 40, "Gaius")
                            ),
                        new State("Gaius",
                            new Taunt(true, "Argus has fallen... Gaius, strike them down!"),
                            new OrderOnce(15, N("LH Gaius"), "Awake"),
                            new EntityNotExistsTransition(N("LH Gaius"), 40, "Basaran")
                            ),
                        new State("Basaran",
                            new Taunt(true, "Basaran! Freeze the blood in their veins!"),
                            new OrderOnce(15, N("LH Basaran"), "Awake"),
                            new EntityNotExistsTransition(N("LH Basaran"), 40, "Dirge")
                            ),
                        new State("Dirge",
                            new Taunt(true, "Dirge, silence them forever!"),
                            new OrderOnce(15, N("LH Dirge"), "Awake"),
                            new EntityNotExistsTransition(N("LH Dirge"), 40, "Molek")
                            )
                        ),
                    new State("Molek",
                        new SetAltTexture(1),
                        new Taunt(true, "My cultists fail me. Molek, rise from the flames and devour them!"),
                        new Flash(0xFF4400, 0.3, 10),
                        new TossObject(N("LH Molek"), 6, 90, coolDown: 9999999),
                        // a tossed monster lands 1.5 s after the throw
                        new TimedTransition(2500, "Molek Fight")
                        ),
                    new State("Molek Fight",
                        new Shoot(0, 4, 90, 0, fixedAngle: 45, rotateAngle: 10, coolDown: Cd(3500)),
                        new EntityNotExistsTransition(N("LH Molek"), 60, "Balaam")
                        ),
                    new State("Balaam",
                        new Taunt(true, "Balaam, the three-headed! Show them what lies beyond!"),
                        new Flash(0x00FF66, 0.3, 10),
                        new TossObject(N("LH Balaam"), 6, 90, coolDown: 9999999),
                        new TimedTransition(2500, "Balaam Fight")
                        ),
                    new State("Balaam Fight",
                        new Shoot(0, 4, 90, 0, fixedAngle: 0, rotateAngle: 10, coolDown: Cd(3500)),
                        // his heads land 1.5 s after he dies
                        new State("Balaam Whole",
                            new EntityExistsTransition(N("LH Balaam 1"), 60, "Balaam Heads")
                            ),
                        new State("Balaam Heads",
                            new EntitiesNotExistsTransition(60, "Merge", N("LH Balaam 1"), N("LH Balaam 2"), N("LH Balaam 3"))
                            )
                        ),
                    new State("Merge",
                        new SetAltTexture(0),
                        new ReturnToSpawn(1, 0.2),
                        new Taunt(true, "Then I will take their power for myself!"),
                        new Flash(0xFF0000, 0.2, 20),
                        new TimedTransition(4000, "Become")
                        ),
                    new State("Become",
                        new Transform(N("LH Malus 2"))
                        )
                    )
                )

            // the four cultists: one each of unstable, paralysing, slowing and silencing bolts
            .Init(E("LH Argus"),
                Cultist(N("LH Purple Cultist"), "Argus answers, master!", "Master, Argus needs you!",
                    new Prioritize(
                        new Orbit(0.45, 5, 15, N("LH Malus"), speedVariance: 0.1, radiusVariance: 0.1),
                        new Wander(0.3)
                        ),
                    new Shoot(0, 4, 90, 0, fixedAngle: 0, rotateAngle: 12, coolDown: Cd(450)),
                    new Shoot(14, 3, 15, 0, coolDown: Cd(1600), coolDownOffset: 800)
                    ))
            .Init(E("LH Gaius"),
                Cultist(N("LH Yellow Cultist"), "Gaius will strike them down!", "The storm... fades...",
                    new Prioritize(
                        new Follow(0.5, 14, 5),
                        new StayCloseToSpawn(0.5, 8)
                        ),
                    new Shoot(14, 5, 12, 0, coolDown: Cd(1600)),
                    new Shoot(0, 10, 36, 0, fixedAngle: 18, coolDown: Cd(3200), coolDownOffset: 1600)
                    ))
            .Init(E("LH Basaran"),
                Cultist(N("LH Blue Cultist"), "Basaran's cold will still your hearts!", "My ice... is cracking!",
                    new Prioritize(
                        new StayCloseToSpawn(0.4, 4),
                        new Wander(0.3)
                        ),
                    new Shoot(0, 8, 45, 0, fixedAngle: 0, rotateAngle: 22.5, coolDown: Cd(900)),
                    new Shoot(14, 2, 8, 0, predictive: 0.5, coolDown: Cd(1100))
                    ))
            .Init(E("LH Dirge"),
                Cultist(N("LH White Cultist"), "Dirge sings your last song!", "My song... is ending...",
                    new Prioritize(
                        new Charge(1.2, 10, coolDown: 4000),
                        new Follow(0.5, 14, 4),
                        new StayCloseToSpawn(0.5, 8)
                        ),
                    new Shoot(12, 7, 10, 0, coolDown: Cd(1800)),
                    new Shoot(0, 12, 30, 0, fixedAngle: 15, coolDown: Cd(4000), coolDownOffset: 2000)
                    ))

            // Molek, the fire demon: charges about, leaving flames, and shakes the ground
            .Init(E("LH Molek"),
                new State(
                    new ScaleHP2(20),
                    new State("Arise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 2500),
                        new Taunt(true, "MOLEK HUNGERS!"),
                        new TimedTransition(2500, "Hunt")
                        ),
                    new State("Hunt",
                        new Prioritize(
                            new Charge(1.6, 12, coolDown: 3500),
                            new Follow(0.6, 14, 2)
                            ),
                        new TossObject(N("LH Molek Flame"), 3, coolDown: 2200),
                        new Shoot(10, 3, 18, 1, coolDown: Cd(1200)),
                        new Shoot(0, 8, 45, 2, fixedAngle: 0, rotateAngle: 22.5, coolDown: Cd(2400)),
                        new HpLessTransition(0.5, "Rage")
                        ),
                    new State("Rage",
                        new Taunt(true, "BURN, ALL OF YOU!"),
                        new Flash(0xFF2200, 0.3, 6),
                        new Prioritize(
                            new Charge(2, 12, coolDown: 2500),
                            new Follow(0.75, 14, 2)
                            ),
                        new TossObject(N("LH Molek Flame"), 3, coolDown: 1400),
                        new Shoot(10, 5, 15, 1, coolDown: Cd(1100)),
                        new Shoot(0, 12, 30, 2, fixedAngle: 0, rotateAngle: 15, coolDown: Cd(2000)),
                        new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 30, coolDown: Cd(3600))
                        )
                    ),
                new Threshold(0.01, MinionLoot())
                )
            .Init(E("LH Molek Flame"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    // it burns whoever stands in it and spits embers
                    new Shoot(1.2, 1, projectileIndex: 0, coolDown: Cd(250)),
                    new Shoot(0, 4, 90, 1, fixedAngle: 45, rotateAngle: 30, coolDown: Cd(1000)),
                    new Decay(6000)
                    )
                )

            // Balaam: green boomerangs, blue armour-piercing bolts, red orbs; splits in three at death
            .Init(E("LH Balaam"),
                new State(
                    new ScaleHP2(20),
                    new TossOnDeath(N("LH Balaam 1"), 1, 2),
                    new TossOnDeath(N("LH Balaam 2"), 1, 2),
                    new TossOnDeath(N("LH Balaam 3"), 1, 2),
                    new State("Arise",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 2500),
                        new Taunt(true, "We... are... many!"),
                        new TimedTransition(2500, "Fight")
                        ),
                    new State("Fight",
                        new Prioritize(
                            new Follow(0.4, 14, 5),
                            new Wander(0.3)
                            ),
                        new Shoot(12, 3, 30, 0, coolDown: Cd(1600)),
                        new Shoot(12, 2, 20, 1, predictive: 0.5, coolDown: Cd(1300), coolDownOffset: 600),
                        new Shoot(0, 4, 90, 2, fixedAngle: 0, rotateAngle: 20, coolDown: Cd(3000))
                        )
                    )
                )
            .Init(E("LH Balaam 1"), BalaamHead(0, "The green head hisses.", new Shoot(12, 4, 20, 0, coolDown: Cd(1400))))
            .Init(E("LH Balaam 2"), BalaamHead(1, "The blue head snarls.", new Shoot(12, 3, 12, 0, predictive: 0.5, coolDown: Cd(1200))))
            .Init(E("LH Balaam 3"), BalaamHead(2, "The red head roars.", new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 17, coolDown: Cd(2200))))

            // Malus with his cultists' powers: cycles through them, faster as he weakens
            .Init(E("LH Malus 2"),
                new State(
                    new ScaleHP2(20),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120),
                    // the regular Malus leads on to the Elder Lost Halls half the time
                    new DropPortalOnDeath("Elder Lost Halls Portal", elder ? 0 : 0.5, xAdjustment: 3, timeout: 120),
                    new StayCloseToSpawn(0.6, 9),
                    new State("Awaken",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                        new SetAltTexture(6, 11, 300),
                        new Taunt(true, "Argus, Gaius, Basaran, Dirge... your power is MINE!"),
                        new TimedTransition(3000, "Fire")
                        ),
                    new State("Phases",
                        new RemoveConditionalEffect(ConditionEffectIndex.Invulnerable),
                        new HpLessTransition(0.2, "Fury"),
                        new Spawn(N("LH Red Cultist"), 2, 0, coolDown: 20000),
                        // his own fire
                        new State("Fire",
                            new SetAltTexture(0),
                            new Wander(0.3),
                            new Shoot(0, 8, 45, 5, fixedAngle: 0, rotateAngle: 11, coolDown: Cd(1600)),
                            new Shoot(14, 3, 14, 0, predictive: 0.4, coolDown: Cd(1000)),
                            new TimedTransition(8000, elder ? "Pentagram" : "Argus")
                            ),
                        // Argus: spirals of unstable bolts
                        new State("Argus",
                            new SetAltTexture(2),
                            new Taunt("Argus' madness takes you!"),
                            new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 13, coolDown: Cd(300)),
                            new Shoot(14, 2, 30, 9, coolDown: Cd(1800)),
                            new TimedTransition(8000, "Gaius")
                            ),
                        // Gaius: paralysing bolts and boomerang lightning
                        new State("Gaius",
                            new SetAltTexture(4),
                            new Taunt("Gaius' storm!"),
                            new Follow(0.5, 14, 6),
                            new Shoot(14, 5, 12, 2, coolDown: Cd(1400)),
                            new Shoot(14, 3, 25, 7, coolDown: Cd(1800), coolDownOffset: 700),
                            new TimedTransition(8000, "Basaran")
                            ),
                        // Basaran: rings of ice
                        new State("Basaran",
                            new SetAltTexture(3),
                            new Taunt("Basaran's cold!"),
                            new ReturnToSpawn(0.6, 1),
                            new Shoot(0, 10, 36, 3, fixedAngle: 0, rotateAngle: 18, coolDown: Cd(1100)),
                            new Shoot(14, 1, projectileIndex: 6, predictive: 0.6, coolDown: Cd(1500)),
                            new TimedTransition(8000, "Dirge")
                            ),
                        // Dirge: silencing shotguns
                        new State("Dirge",
                            new SetAltTexture(5),
                            new Taunt("Dirge's lament!"),
                            new Charge(1.3, 10, coolDown: 3500),
                            new Shoot(12, 7, 10, 4, coolDown: Cd(1600)),
                            new Shoot(0, 8, 45, 8, fixedAngle: 22.5, coolDown: Cd(2400), coolDownOffset: 1200),
                            new TimedTransition(8000, "Fire")
                            ),
                        // Elder only, after his own fire each time round: back on the star's centre, Molek's flames land on its five
                        // points and the five between, and two five-armed spirals turn through them
                        new State("Pentagram",
                            new SetAltTexture(0),
                            new Taunt(true, "The star is drawn in fire. Stand in it, and burn with it!"),
                            new ReturnToSpawn(1, 0.5),
                            new Flash(0xFF0000, 0.3, 6),
                            new TossObject(N("LH Molek Flame"), 6, 270, coolDown: 9999999, coolDownOffset: 500),
                            new TossObject(N("LH Molek Flame"), 6, 342, coolDown: 9999999, coolDownOffset: 500),
                            new TossObject(N("LH Molek Flame"), 6, 54, coolDown: 9999999, coolDownOffset: 500),
                            new TossObject(N("LH Molek Flame"), 6, 126, coolDown: 9999999, coolDownOffset: 500),
                            new TossObject(N("LH Molek Flame"), 6, 198, coolDown: 9999999, coolDownOffset: 500),
                            new TossObject(N("LH Molek Flame"), 3, 306, coolDown: 9999999, coolDownOffset: 2500),
                            new TossObject(N("LH Molek Flame"), 3, 18, coolDown: 9999999, coolDownOffset: 2500),
                            new TossObject(N("LH Molek Flame"), 3, 90, coolDown: 9999999, coolDownOffset: 2500),
                            new TossObject(N("LH Molek Flame"), 3, 162, coolDown: 9999999, coolDownOffset: 2500),
                            new TossObject(N("LH Molek Flame"), 3, 234, coolDown: 9999999, coolDownOffset: 2500),
                            new Shoot(0, 5, 72, 5, fixedAngle: 270, rotateAngle: 9, coolDownOffset: 2000, coolDown: 450),
                            new Shoot(0, 5, 72, 1, fixedAngle: 306, rotateAngle: -6, coolDownOffset: 2000, coolDown: 700),
                            new TimedTransition(9000, "Argus")
                            )
                        ),
                    // the last fifth: all of them at once, and his followers come
                    new State("Fury",
                        new SetAltTexture(0),
                        new Taunt(true, "I will not fall to the likes of you!"),
                        new Flash(0xFF0000, 0.3, 8),
                        new Wander(0.35),
                        new Spawn(N("LH Red Cultist"), 2, 0, coolDown: 15000),
                        new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 13, coolDown: Cd(400)),
                        new Shoot(14, 5, 12, 2, coolDown: Cd(1600)),
                        new Shoot(0, 10, 36, 3, fixedAngle: 18, coolDown: Cd(2000), coolDownOffset: 1000),
                        new Shoot(12, 3, 12, 4, coolDown: Cd(1300), coolDownOffset: 500),
                        new Shoot(0, 6, 60, 5, fixedAngle: 0, rotateAngle: 20, coolDown: Cd(2600))
                        )
                    ),
                new Threshold(0.005,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 0.5),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    // one vial for the group half the time (it opens one Void), and a small chance for each
                    new OnePlayerLoot("Vial of Pure Darkness", 0.5, 0.03),
                    new TierLoot(12, ItemType.Weapon, 0.3),
                    new TierLoot(13, ItemType.Weapon, 0.15),
                    new TierLoot(14, ItemType.Weapon, 0.04),
                    new TierLoot(13, ItemType.Armor, 0.2),
                    new TierLoot(14, ItemType.Armor, 0.04),
                    new TierLoot(6, ItemType.Ability, 0.15),
                    new TierLoot(6, ItemType.Ring, 0.15)
                    ),
                new Threshold(0.02,
                    new ItemLoot("Staff of Unholy Sacrifice", elder ? 0.006 : 0.003),
                    new ItemLoot("Skull of Corrupted Souls", elder ? 0.006 : 0.003),
                    new ItemLoot("Ritual Robe", elder ? 0.006 : 0.003),
                    new ItemLoot("Bloodshed Ring", elder ? 0.006 : 0.003)
                    ),
                ElderLoot(0.02,
                    new ItemLoot("Ring of the Blood Pact", 0.01),
                    new ItemLoot("Potion of Max Attack", 0.05),
                    new ItemLoot("Elder Lost Halls Key", 0.01),
                    new TierLoot(14, ItemType.Weapon, 0.1),
                    new TierLoot(15, ItemType.Armor, 0.05),
                    new TierLoot(7, ItemType.Ability, 0.03),
                    new TierLoot(7, ItemType.Ring, 0.03)
                    )
                );
        }

        // one of Balaam's heads: a single kind of bolt each, hunting players
        private static State BalaamHead(int head, string taunt, params IStateChildren[] attacks) =>
            new State(
                [new ScaleHP2(20),
                new State("Arise",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 1500),
                    new Taunt(true, taunt),
                    new TimedTransition(1500, "Fight")
                    ),
                new State("Fight",
                    [.. attacks,
                    new Prioritize(
                        new Follow(0.55, 14, 3 + head),
                        new Wander(0.35)
                        )]
                    )]
                );
    }
}
