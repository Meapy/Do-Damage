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
        private _ Cult = () => CultBehaviours(Behav());

        private static Ctor CultBehaviours(Ctor db)
        {
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
            .Init("LH Red Cultist",
                Follower(
                    new Shoot(9, 3, 20, 0, coolDown: 1600),
                    new Shoot(9, 1, projectileIndex: 1, predictive: 0.5, coolDown: 1100),
                    new Shoot(9, 2, 12, 2, coolDown: 2400, coolDownOffset: 600)
                    ),
                MinionLoot())
            .Init("LH Blue Cultist",
                Follower(
                    new Shoot(8, 2, 12, 0, coolDown: 900)
                    ),
                MinionLoot())
            .Init("LH Yellow Cultist",
                Follower(
                    new Shoot(10, 3, 25, 0, coolDown: 1500)
                    ),
                MinionLoot())
            .Init("LH White Cultist",
                Follower(
                    new Shoot(9, 1, projectileIndex: 0, predictive: 0.6, coolDown: 650)
                    ),
                MinionLoot())
            .Init("LH Purple Cultist",
                Follower(
                    new Shoot(12, 6, 60, 0, fixedAngle: 0, rotateAngle: 20, coolDown: 2000),
                    new Shoot(12, 1, projectileIndex: 0, coolDown: 1400)
                    ),
                MinionLoot())
            .Init("LH Black Cultist",
                Follower(
                    // slow dark bolts that hang in the air
                    new Shoot(10, 8, 45, 0, fixedAngle: 22.5, rotateAngle: 11, coolDown: 3200),
                    new Shoot(10, 2, 15, 0, coolDown: 1800)
                    ),
                MinionLoot())
            .Init("LH Green Cultist",
                Follower(
                    new Shoot(8, 3, 15, 0, coolDown: 1100),
                    // Phaedra's followers tend the others
                    new HealGroup(8, "Cult Cultists", coolDown: 4000, healAmount: 500)
                    ),
                MinionLoot())

            // Malus' demons: fast, few hit points, no experience
            .Init("LH Fire Demon",
                new State(
                    new Prioritize(
                        new Charge(1.4, 9, coolDown: 3000),
                        new Follow(0.8, 10, 1),
                        new Wander(0.4)
                        ),
                    new Shoot(8, 3, 25, 1, coolDown: 900),
                    new Shoot(4, 6, 60, 0, coolDown: 2000)
                    ),
                MinionLoot())
            .Init("LH Ice Demon",
                new State(
                    new Prioritize(
                        new Follow(0.6, 10, 4),
                        new Wander(0.4)
                        ),
                    new Shoot(9, 5, 14, 0, coolDown: 1400)
                    ),
                MinionLoot())
            .Init("LH Lightning Demon",
                new State(
                    new Prioritize(
                        new Orbit(0.9, 3.5, 10, speedVariance: 0.2, radiusVariance: 0.5),
                        new Wander(0.5)
                        ),
                    new Shoot(9, 2, 10, 0, predictive: 0.5, coolDown: 1000)
                    ),
                MinionLoot())

            // the hall: The Cult calls Malus when a player comes near, and goes once he is slain
            .Init("LH Cult Controller",
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
                        new TossObject("LH Malus", 6, 270, coolDown: 9999999),
                        new TimedTransition(1000, "Fight")
                        ),
                    new State("Fight",
                        new EntityExistsTransition("LH Malus 2", 60, "Final")
                        ),
                    new State("Final",
                        new EntitiesNotExistsTransition(60, "Gone", "LH Malus", "LH Malus 2")
                        ),
                    new State("Gone",
                        new Decay(0)
                        )
                    )
                )

            // Malus, while his cultists live: never hit, he leads the ritual from the centre
            .Init("LH Malus",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                    new State("Rise",
                        new SetAltTexture(1),
                        new Flash(0xFF0000, 0.4, 8),
                        new Taunt(true, "So, the Lost Halls let you through. My cult will be your end!"),
                        // his four cultists, on the star's points round him
                        new TossObject("LH Argus", 3.5, 342, coolDown: 9999999),
                        new TossObject("LH Gaius", 3.5, 54, coolDown: 9999999),
                        new TossObject("LH Basaran", 3.5, 126, coolDown: 9999999),
                        new TossObject("LH Dirge", 3.5, 198, coolDown: 9999999),
                        new TimedTransition(3500, "Ritual")
                        ),
                    new State("Ritual",
                        new SetAltTexture(0),
                        new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 15, coolDown: 2800),
                        new Shoot(16, 1, projectileIndex: 0, predictive: 0.4, coolDown: 2200),
                        new State("Argus",
                            new OrderOnce(15, "LH Argus", "Awake"),
                            new EntityNotExistsTransition("LH Argus", 40, "Gaius")
                            ),
                        new State("Gaius",
                            new Taunt(true, "Argus has fallen... Gaius, strike them down!"),
                            new OrderOnce(15, "LH Gaius", "Awake"),
                            new EntityNotExistsTransition("LH Gaius", 40, "Basaran")
                            ),
                        new State("Basaran",
                            new Taunt(true, "Basaran! Freeze the blood in their veins!"),
                            new OrderOnce(15, "LH Basaran", "Awake"),
                            new EntityNotExistsTransition("LH Basaran", 40, "Dirge")
                            ),
                        new State("Dirge",
                            new Taunt(true, "Dirge, silence them forever!"),
                            new OrderOnce(15, "LH Dirge", "Awake"),
                            new EntityNotExistsTransition("LH Dirge", 40, "Molek")
                            )
                        ),
                    new State("Molek",
                        new SetAltTexture(1),
                        new Taunt(true, "My cultists fail me. Molek, rise from the flames and devour them!"),
                        new Flash(0xFF4400, 0.3, 10),
                        new TossObject("LH Molek", 6, 90, coolDown: 9999999),
                        // a tossed monster lands 1.5 s after the throw
                        new TimedTransition(2500, "Molek Fight")
                        ),
                    new State("Molek Fight",
                        new Shoot(0, 4, 90, 0, fixedAngle: 45, rotateAngle: 10, coolDown: 3500),
                        new EntityNotExistsTransition("LH Molek", 60, "Balaam")
                        ),
                    new State("Balaam",
                        new Taunt(true, "Balaam, the three-headed! Show them what lies beyond!"),
                        new Flash(0x00FF66, 0.3, 10),
                        new TossObject("LH Balaam", 6, 90, coolDown: 9999999),
                        new TimedTransition(2500, "Balaam Fight")
                        ),
                    new State("Balaam Fight",
                        new Shoot(0, 4, 90, 0, fixedAngle: 0, rotateAngle: 10, coolDown: 3500),
                        // his heads land 1.5 s after he dies
                        new State("Balaam Whole",
                            new EntityExistsTransition("LH Balaam 1", 60, "Balaam Heads")
                            ),
                        new State("Balaam Heads",
                            new EntitiesNotExistsTransition(60, "Merge", "LH Balaam 1", "LH Balaam 2", "LH Balaam 3")
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
                        new Transform("LH Malus 2")
                        )
                    )
                )

            // the four cultists: one each of unstable, paralysing, slowing and silencing bolts
            .Init("LH Argus",
                Cultist("LH Purple Cultist", "Argus answers, master!", "Master, Argus needs you!",
                    new Prioritize(
                        new Orbit(0.45, 5, 15, "LH Malus", speedVariance: 0.1, radiusVariance: 0.1),
                        new Wander(0.3)
                        ),
                    new Shoot(0, 4, 90, 0, fixedAngle: 0, rotateAngle: 12, coolDown: 450),
                    new Shoot(14, 3, 15, 0, coolDown: 1600, coolDownOffset: 800)
                    ))
            .Init("LH Gaius",
                Cultist("LH Yellow Cultist", "Gaius will strike them down!", "The storm... fades...",
                    new Prioritize(
                        new Follow(0.5, 14, 5),
                        new StayCloseToSpawn(0.5, 8)
                        ),
                    new Shoot(14, 5, 12, 0, coolDown: 1600),
                    new Shoot(0, 10, 36, 0, fixedAngle: 18, coolDown: 3200, coolDownOffset: 1600)
                    ))
            .Init("LH Basaran",
                Cultist("LH Blue Cultist", "Basaran's cold will still your hearts!", "My ice... is cracking!",
                    new Prioritize(
                        new StayCloseToSpawn(0.4, 4),
                        new Wander(0.3)
                        ),
                    new Shoot(0, 8, 45, 0, fixedAngle: 0, rotateAngle: 22.5, coolDown: 900),
                    new Shoot(14, 2, 8, 0, predictive: 0.5, coolDown: 1100)
                    ))
            .Init("LH Dirge",
                Cultist("LH White Cultist", "Dirge sings your last song!", "My song... is ending...",
                    new Prioritize(
                        new Charge(1.2, 10, coolDown: 4000),
                        new Follow(0.5, 14, 4),
                        new StayCloseToSpawn(0.5, 8)
                        ),
                    new Shoot(12, 7, 10, 0, coolDown: 1800),
                    new Shoot(0, 12, 30, 0, fixedAngle: 15, coolDown: 4000, coolDownOffset: 2000)
                    ))

            // Molek, the fire demon: charges about, leaving flames, and shakes the ground
            .Init("LH Molek",
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
                        new TossObject("LH Molek Flame", 3, coolDown: 2200),
                        new Shoot(10, 3, 18, 1, coolDown: 1200),
                        new Shoot(0, 8, 45, 2, fixedAngle: 0, rotateAngle: 22.5, coolDown: 2400),
                        new HpLessTransition(0.5, "Rage")
                        ),
                    new State("Rage",
                        new Taunt(true, "BURN, ALL OF YOU!"),
                        new Flash(0xFF2200, 0.3, 6),
                        new Prioritize(
                            new Charge(2, 12, coolDown: 2500),
                            new Follow(0.75, 14, 2)
                            ),
                        new TossObject("LH Molek Flame", 3, coolDown: 1400),
                        new Shoot(10, 5, 15, 1, coolDown: 1100),
                        new Shoot(0, 12, 30, 2, fixedAngle: 0, rotateAngle: 15, coolDown: 2000),
                        new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 30, coolDown: 3600)
                        )
                    ),
                new Threshold(0.01, MinionLoot())
                )
            .Init("LH Molek Flame",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    // it burns whoever stands in it and spits embers
                    new Shoot(1.2, 1, projectileIndex: 0, coolDown: 250),
                    new Shoot(0, 4, 90, 1, fixedAngle: 45, rotateAngle: 30, coolDown: 1000),
                    new Decay(6000)
                    )
                )

            // Balaam: green boomerangs, blue armour-piercing bolts, red orbs; splits in three at death
            .Init("LH Balaam",
                new State(
                    new ScaleHP2(20),
                    new TossOnDeath("LH Balaam 1", 1, 2),
                    new TossOnDeath("LH Balaam 2", 1, 2),
                    new TossOnDeath("LH Balaam 3", 1, 2),
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
                        new Shoot(12, 3, 30, 0, coolDown: 1600),
                        new Shoot(12, 2, 20, 1, predictive: 0.5, coolDown: 1300, coolDownOffset: 600),
                        new Shoot(0, 4, 90, 2, fixedAngle: 0, rotateAngle: 20, coolDown: 3000)
                        )
                    )
                )
            .Init("LH Balaam 1", BalaamHead(0, "The green head hisses.", new Shoot(12, 4, 20, 0, coolDown: 1400)))
            .Init("LH Balaam 2", BalaamHead(1, "The blue head snarls.", new Shoot(12, 3, 12, 0, predictive: 0.5, coolDown: 1200)))
            .Init("LH Balaam 3", BalaamHead(2, "The red head roars.", new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 17, coolDown: 2200)))

            // Malus with his cultists' powers: cycles through them, faster as he weakens
            .Init("LH Malus 2",
                new State(
                    new ScaleHP2(20),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120),
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
                        new Spawn("LH Red Cultist", 2, 0, coolDown: 20000),
                        // his own fire
                        new State("Fire",
                            new SetAltTexture(0),
                            new Wander(0.3),
                            new Shoot(0, 8, 45, 5, fixedAngle: 0, rotateAngle: 11, coolDown: 1600),
                            new Shoot(14, 3, 14, 0, predictive: 0.4, coolDown: 1000),
                            new TimedTransition(8000, "Argus")
                            ),
                        // Argus: spirals of unstable bolts
                        new State("Argus",
                            new SetAltTexture(2),
                            new Taunt("Argus' madness takes you!"),
                            new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 13, coolDown: 300),
                            new Shoot(14, 2, 30, 9, coolDown: 1800),
                            new TimedTransition(8000, "Gaius")
                            ),
                        // Gaius: paralysing bolts and boomerang lightning
                        new State("Gaius",
                            new SetAltTexture(4),
                            new Taunt("Gaius' storm!"),
                            new Follow(0.5, 14, 6),
                            new Shoot(14, 5, 12, 2, coolDown: 1400),
                            new Shoot(14, 3, 25, 7, coolDown: 1800, coolDownOffset: 700),
                            new TimedTransition(8000, "Basaran")
                            ),
                        // Basaran: rings of ice
                        new State("Basaran",
                            new SetAltTexture(3),
                            new Taunt("Basaran's cold!"),
                            new ReturnToSpawn(0.6, 1),
                            new Shoot(0, 10, 36, 3, fixedAngle: 0, rotateAngle: 18, coolDown: 1100),
                            new Shoot(14, 1, projectileIndex: 6, predictive: 0.6, coolDown: 1500),
                            new TimedTransition(8000, "Dirge")
                            ),
                        // Dirge: silencing shotguns
                        new State("Dirge",
                            new SetAltTexture(5),
                            new Taunt("Dirge's lament!"),
                            new Charge(1.3, 10, coolDown: 3500),
                            new Shoot(12, 7, 10, 4, coolDown: 1600),
                            new Shoot(0, 8, 45, 8, fixedAngle: 22.5, coolDown: 2400, coolDownOffset: 1200),
                            new TimedTransition(8000, "Fire")
                            )
                        ),
                    // the last fifth: all of them at once, and his followers come
                    new State("Fury",
                        new SetAltTexture(0),
                        new Taunt(true, "I will not fall to the likes of you!"),
                        new Flash(0xFF0000, 0.3, 8),
                        new Wander(0.35),
                        new Spawn("LH Red Cultist", 2, 0, coolDown: 15000),
                        new Shoot(0, 4, 90, 1, fixedAngle: 0, rotateAngle: 13, coolDown: 400),
                        new Shoot(14, 5, 12, 2, coolDown: 1600),
                        new Shoot(0, 10, 36, 3, fixedAngle: 18, coolDown: 2000, coolDownOffset: 1000),
                        new Shoot(12, 3, 12, 4, coolDown: 1300, coolDownOffset: 500),
                        new Shoot(0, 6, 60, 5, fixedAngle: 0, rotateAngle: 20, coolDown: 2600)
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
                    new ItemLoot("Staff of Unholy Sacrifice", 0.003),
                    new ItemLoot("Skull of Corrupted Souls", 0.003),
                    new ItemLoot("Ritual Robe", 0.003),
                    new ItemLoot("Bloodshed Ring", 0.003)
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
