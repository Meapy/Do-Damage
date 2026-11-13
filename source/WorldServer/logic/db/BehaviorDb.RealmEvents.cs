using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Three realm events of both realms (RealmManager's _events; Realm 2 brings each with a land of
    // its own), and those lands' monsters. tools/build_realm2.py draws them all and writes their XML;
    // the projectile indices here are its MOBS lists.
    //
    // - The Gilded Thief: a chase. It counts its hoard until someone comes near, then runs, blinks
    //   away in a puff of smoke leaving decoys that look just like it, and drops a coffer of its loot
    //   at each quarter of its health (a third of them mimics). Catch it within three minutes or it
    //   gets away with the rest, and the event ends without counting.
    // - The Starborn Colossus: a fallen star. Meteors rain round it as it wakes; moons orbit it and
    //   harden its skin until they are shot down, leaving it open for a while; at half health it goes
    //   supernova, and near the end it collapses into a storm of dark stars.
    // - The Blight Mother: a spreading rot. She seeds spore pods round her, each healing her while it
    //   stands and bursting into sporelings if left to ripen, so the group has to weed as it fights;
    //   she tears up her roots and stalks the players, then ripens every pod at once.
    partial class BehaviorDb
    {
        private _ RealmEvents = () => Behav()
        // ================================================================ the Gilded Thief
        .Init("Gilded Thief",
            new State(
                new DropPortalOnDeath("Treasure Cave Portal", 0.5),
                new State("Hoard",
                    new Taunt(0.5, new Cooldown(9000, 3000), "One for me... one for me... and one more for me!",
                        "Shiny, shiny, shiny...", "Nobody will ever find my hoard!", "Hee hee... all mine!"),
                    new PlayerWithinTransition(9, "Spotted"),
                    new HpLessTransition(0.995, "Spotted")
                    ),
                new State("Spotted",
                    new Taunt("Eep! You'll never take my gold!"),
                    new Flash(0xFFD24A, 0.15, 4),
                    new TimedTransition(800, "Chase")
                    ),
                new State("Chase",
                    // three minutes to catch it, from the moment it's seen
                    new TimedTransition(180000, "Escape"),
                    new Taunt(0.4, new Cooldown(14000, 4000), "Too slow!", "Catch me if you can!", "Finders keepers!",
                        "Hee hee hee!", "You'll have to be quicker than that!", "Is that the best you can do?"),
                    new State("Run1",
                        ThiefRun(0.75, 9),
                        new Shoot(10, 3, shootAngle: 15, projectileIndex: 0, coolDown: 900),
                        new Starfall(Starfall.Area.Players, 12, 2.5, 90, new Cooldown(3200, 600), color: 0xFFFFD24A),
                        new ThiefBlink(new Cooldown(8000, 2000), decoy: "Gilded Decoy"),
                        new HpLessTransition(0.75, "Drop1")
                        ),
                    new State("Drop1",
                        new Taunt("No! Not my coffer!"),
                        new TossObject2("Gilded Coffer", 2, coolDown: 99999, randomToss: true),
                        new TimedTransition(200, "Run2")
                        ),
                    new State("Run2",
                        ThiefRun(0.85, 9),
                        new Shoot(10, 4, shootAngle: 14, projectileIndex: 0, coolDown: 800),
                        new Shoot(9, 2, shootAngle: 10, projectileIndex: 1, predictive: 0.5, coolDown: 1600),
                        new Starfall(Starfall.Area.Players, 12, 2.5, 100, new Cooldown(2600, 600), color: 0xFFFFD24A),
                        new ThiefBlink(new Cooldown(7000, 2000), decoy: "Gilded Decoy"),
                        new HpLessTransition(0.5, "Drop2")
                        ),
                    new State("Drop2",
                        new Taunt("My precious coins! Stop that!"),
                        new TossObject2("Gilded Coffer", 2, coolDown: 99999, randomToss: true),
                        new TimedTransition(200, "Run3")
                        ),
                    new State("Run3",
                        ThiefRun(0.95, 10),
                        new Shoot(10, 5, shootAngle: 12, projectileIndex: 0, coolDown: 750),
                        new Shoot(9, 3, shootAngle: 10, projectileIndex: 1, predictive: 0.5, coolDown: 1400),
                        new Shoot(12, 1, projectileIndex: 2, predictive: 0.6, coolDown: 2500),
                        new Starfall(Starfall.Area.Players, 12, 2.5, 100, new Cooldown(2200, 500), perVolley: 2, color: 0xFFFFD24A),
                        new ThiefBlink(new Cooldown(6000, 1500), decoy: "Gilded Decoy"),
                        new HpLessTransition(0.25, "Drop3")
                        ),
                    new State("Drop3",
                        new Taunt("That's the last of it, I swear! ...Hee hee."),
                        new TossObject2("Gilded Coffer", 2, coolDown: 99999, randomToss: true),
                        new TimedTransition(200, "Frantic")
                        ),
                    new State("Frantic",
                        new Taunt("You'll pay for this! ...Well, actually, I'll pay. Ugh!"),
                        new Flash(0xFF4040, 0.3, 3),
                        ThiefRun(1.05, 10),
                        new Shoot(10, 6, shootAngle: 10, projectileIndex: 0, coolDown: 650),
                        new Shoot(0, 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, rotateAngle: 17, coolDown: 1100),
                        new Shoot(12, 2, shootAngle: 20, projectileIndex: 2, predictive: 0.6, coolDown: 2000),
                        new Starfall(Starfall.Area.Players, 12, 2.5, 110, new Cooldown(1800, 400), perVolley: 2, color: 0xFFFFD24A),
                        new ThiefBlink(new Cooldown(4500, 1500), decoy: "Gilded Decoy")
                        )
                    ),
                new State("Escape",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                    new Taunt(true, "So long, suckers! The rest of this gold is MINE! Ha ha ha!"),
                    new Flash(0xFFD24A, 0.2, 6),
                    new TimedTransition(1500, "Gone")
                    ),
                new State("Gone",
                    new Vanish()
                    )
                ),
            new Threshold(0.005, LootTemplates.BasicDrop()),
            new Threshold(0.005, LootTemplates.BasicPots()),
            new Threshold(0.01,
                new ItemLoot("Heart of Gold Prism", 0.006),
                new ItemLoot("Keychain Cutlass", 0.006),
                new ItemLoot("Dueling Daggers", 0.006),
                new ItemLoot("Potion of Life", 0.1),
                new ItemLoot("Potion of Mana", 0.1)
                )
            )
        // its decoys: as like it as can be, they run too, and go up in a burst of coins when hit
        .Init("Gilded Decoy",
            new State(
                new State("Fool",
                    ThiefRun(0.75, 9),
                    new Shoot(10, 3, shootAngle: 15, projectileIndex: 0, coolDown: 1100),
                    new HpLessTransition(0.6, "Pop"),
                    new TimedTransition(12000, "Fade")
                    ),
                new State("Pop",
                    new Taunt(0.6, "Fooled you!", "Wrong one!", "Hee hee, not me!", "Try again!"),
                    new Shoot(0, 12, shootAngle: 30, projectileIndex: 0, fixedAngle: 0, coolDown: 9999),
                    new TimedTransition(100, "Fade")
                    ),
                new State("Fade",
                    new Vanish()
                    )
                ))
        // a coffer of its loot; a third of them are mimics, which wake when someone comes close
        .Init("Gilded Coffer",
            new State(
                new State("Drop",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new TimedRandomTransition(1200, false, "Coffer", "Coffer", "Lurk")
                    ),
                new State("Coffer",
                    new TimedTransition(90000, "Lost")
                    ),
                new State("Lurk",
                    new PlayerWithinTransition(2.5, "Snap"),
                    new HpLessTransition(0.9, "Snap"),
                    new TimedTransition(90000, "Lost")
                    ),
                new State("Snap",
                    new Taunt("SURPRISE!"),
                    new Transform("Gilded Mimic")
                    ),
                new State("Lost",
                    new Vanish()
                    )
                ),
            new Threshold(0.01, LootTemplates.StrongerPots()),
            new Threshold(0.01,
                new TierLoot(5, ItemType.Ring, 0.15),
                new TierLoot(6, ItemType.Ring, 0.05))
            )
        .Init("Gilded Mimic",
            new State(
                new Prioritize(new StayCloseToSpawn(0.9, 12), new Follow(0.9, 12, 1), new Wander(0.4)),
                new Shoot(10, 3, shootAngle: 20, projectileIndex: 0, predictive: 0.4, coolDown: 1000),
                new Shoot(10, 8, shootAngle: 45, projectileIndex: 1, coolDown: 1600),
                new Taunt(0.3, new Cooldown(8000, 3000), "Chomp chomp!", "Who's the treasure now?", "Gold teeth, gold bite!")
                ),
            new Threshold(0.01, LootTemplates.StrongerPots()),
            new Threshold(0.01, LootTemplates.BasicDrop()))

        // ================================================================ the Starborn Colossus
        .Init("Starborn Colossus",
            new State(
                new DropPortalOnDeath("Mountain Temple Portal", 0.35),
                new OrderOnDeath(40, "Star Satellite", "Fall"),
                new State("Dormant",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Flash(0x5AD8FF, 1.5, 1000),
                    new PlayerWithinTransition(14, "Impact")
                    ),
                // the star wakes: meteors come down all round its crater
                new State("Impact",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "I FELL FROM THE HEAVENS... AND THE HEAVENS FALL WITH ME!"),
                    new Flash(0xEFFCFF, 0.25, 30),
                    new Starfall(Starfall.Area.Scatter, 15, 3, 120, new Cooldown(350, 100), color: 0xFFFFB040, throwColor: 0xFFEFFCFF),
                    new Starfall(Starfall.Area.Players, 15, 2.5, 100, new Cooldown(1600, 300), color: 0xFF5AD8FF),
                    new TimedTransition(8000, "Rise")
                    ),
                new State("Rise",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("Rise, my moons! Turn about me as the worlds once did!"),
                    new TossObject("Star Satellite", 6, angle: 0, coolDown: 99999),
                    new TossObject("Star Satellite", 6, angle: 90, coolDown: 99999),
                    new TossObject("Star Satellite", 6, angle: 180, coolDown: 99999),
                    new TossObject("Star Satellite", 6, angle: 270, coolDown: 99999),
                    new TimedTransition(1500, "Orbit")
                    ),
                new State("Orbit",
                    new Prioritize(new StayCloseToSpawn(0.4, 6), new Wander(0.25)),
                    new Shoot(16, 3, shootAngle: 16, projectileIndex: 0, predictive: 0.5, coolDown: 1700),
                    new Shoot(20, 2, shootAngle: 180, projectileIndex: 1, fixedAngle: 0, rotateAngle: 12, coolDown: 220),
                    new Starfall(Starfall.Area.Scatter, 14, 2.5, 110, new Cooldown(1800, 400), color: 0xFFFFB040, throwColor: 0xFFEFFCFF),
                    new HpLessTransition(0.5, "Gather"),
                    // its moons harden its skin; with all of them down it lies open a while, then raises more
                    new State("Shielded",
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new EntityNotExistsTransition("Star Satellite", 30, "Exposed")
                        ),
                    new State("Exposed",
                        new ConditionEffectBehavior(ConditionEffectIndex.ArmorBroken),
                        new Taunt("My moons... you DARE?!"),
                        new Flash(0xFF5A5A, 0.4, 20),
                        new TimedTransition(9000, "Remoon")
                        ),
                    new State("Remoon",
                        new TossObject("Star Satellite", 6, angle: 45, coolDown: 99999),
                        new TossObject("Star Satellite", 6, angle: 135, coolDown: 99999),
                        new TossObject("Star Satellite", 6, angle: 225, coolDown: 99999),
                        new TossObject("Star Satellite", 6, angle: 315, coolDown: 99999),
                        new TimedTransition(1000, "Shielded")
                        )
                    ),
                // half its light gone: it gathers what's left and bursts
                new State("Gather",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Order(40, "Star Satellite", "Fall"),
                    new Taunt(true, "BEHOLD... A SUPERNOVA!"),
                    new Flash(0xFFFFFF, 0.12, 20),
                    new TimedTransition(2500, "Supernova")
                    ),
                new State("Supernova",
                    new Shoot(0, 36, shootAngle: 10, projectileIndex: 2, fixedAngle: 0, coolDown: 99999),
                    new Shoot(0, 36, shootAngle: 10, projectileIndex: 1, fixedAngle: 5, coolDownOffset: 400, coolDown: 99999),
                    new Shoot(0, 36, shootAngle: 10, projectileIndex: 2, fixedAngle: 2.5, coolDownOffset: 800, coolDown: 99999),
                    new Starfall(Starfall.Area.Host, 0, 5, 180, new Cooldown(99999, 0), color: 0xFFEFFCFF),
                    new TossObject("Star Fragment", 5, angle: 30, coolDown: 99999),
                    new TossObject("Star Fragment", 5, angle: 150, coolDown: 99999),
                    new TossObject("Star Fragment", 5, angle: 270, coolDown: 99999),
                    new TimedTransition(1200, "Blaze")
                    ),
                new State("Blaze",
                    new Prioritize(new StayCloseToSpawn(0.5, 10), new Follow(0.35, 14, 5), new Wander(0.25)),
                    new Shoot(20, 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 0, rotateAngle: 9, coolDown: 180),
                    new Shoot(14, 5, shootAngle: 10, projectileIndex: 3, predictive: 0.6, coolDown: 1200),
                    new Shoot(16, 3, shootAngle: 16, projectileIndex: 0, predictive: 0.5, coolDown: 2200),
                    new Starfall(Starfall.Area.Scatter, 14, 2.5, 110, new Cooldown(1100, 300), color: 0xFFFFB040, throwColor: 0xFFEFFCFF),
                    new Spawn("Star Fragment", maxChildren: 4, initialSpawn: 0, coolDown: 6000),
                    new HpLessTransition(0.2, "Collapse")
                    ),
                // its last light: it falls in on itself, rings of dark stars rippling out
                new State("Collapse",
                    new Taunt(true, "MY LIGHT... IS FADING... THEN I WILL TAKE YOURS WITH ME!"),
                    new Flash(0x8D4FE0, 0.5, 40),
                    new Prioritize(new StayCloseToSpawn(0.5, 10), new Follow(0.3, 14, 4), new Wander(0.25)),
                    new Shoot(0, 16, shootAngle: 22.5, projectileIndex: 4, fixedAngle: 0, rotateAngle: 11, coolDown: 650),
                    new Shoot(16, 3, shootAngle: 12, projectileIndex: 0, predictive: 0.8, coolDown: 1300),
                    new Shoot(14, 4, shootAngle: 10, projectileIndex: 3, predictive: 0.6, coolDown: 1500),
                    new Starfall(Starfall.Area.Scatter, 13, 2.5, 120, new Cooldown(700, 200), color: 0xFF8D4FE0, throwColor: 0xFFEFFCFF)
                    )
                ),
            new Threshold(0.005, LootTemplates.BasicDrop()),
            new Threshold(0.005, LootTemplates.BasicPots()),
            new Threshold(0.01,
                new ItemLoot("Comet of Calamity", 0.006),
                new ItemLoot("Starlight Recurve", 0.006),
                new ItemLoot("Moonbeam Blade", 0.006),
                new ItemLoot("Potion of Wisdom", 0.5),
                new ItemLoot("Potion of Attack", 0.5)
                )
            )
        .Init("Star Satellite",
            new State(
                new State("Turn",
                    new Orbit(1.1, 6, 30, "Starborn Colossus", speedVariance: 0.1, radiusVariance: 0.5),
                    new Shoot(10, 2, shootAngle: 20, predictive: 0.3, coolDown: 1400),
                    new EntityNotExistsTransition("Starborn Colossus", 40, "Fall")
                    ),
                new State("Fall",
                    new Vanish()
                    )
                ))
        .Init("Star Fragment",
            new State(
                new Prioritize(new StayCloseToSpawn(1.0, 14), new Follow(1.0, 12, 1), new Wander(0.5)),
                new Shoot(8, 2, shootAngle: 15, coolDown: 900),
                new State("Burn",
                    new TimedTransition(20000, "Out")
                    ),
                new State("Out",
                    new Vanish()
                    )
                ))

        // ================================================================ the Blight Mother
        .Init("Blight Mother",
            new State(
                new DropPortalOnDeath("GC Gigantic Cave Portal", 0.35),
                new OrderOnDeath(40, "Blight Spore Pod", "Wither"),
                new OrderOnDeath(40, "Sporeling", "Wither"),
                new State("Dormant",
                    new PlayerWithinTransition(13, "Rooted"),
                    new HpLessTransition(0.995, "Rooted")
                    ),
                new State("Rooted",
                    new Taunt(true, "The blight... SPREADS! Every root, every leaf, every one of you... MINE!"),
                    new TossObject("Blight Spore Pod", 10, coolDown: 3500, minAngle: 0, maxAngle: 360, minRange: 5, maxRange: 12,
                        densityRange: 18, maxDensity: 7),
                    new Shoot(14, 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, rotateAngle: 20, coolDown: 1500),
                    new Shoot(12, 3, shootAngle: 20, projectileIndex: 1, predictive: 0.5, coolDown: 1300),
                    new Starfall(Starfall.Area.Players, 14, 3, 80, new Cooldown(4000, 800), color: 0xFF8AE040,
                        effect: ConditionEffectIndex.Sick, effectMs: 3000),
                    new HpLessTransition(0.6, "Uproot")
                    ),
                new State("Uproot",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("You cannot cut me out! I will take root in your FLESH!"),
                    new Flash(0x8AE040, 0.2, 10),
                    new Shoot(0, 20, shootAngle: 18, projectileIndex: 2, fixedAngle: 0, coolDown: 99999),
                    new TimedTransition(2000, "Stalk")
                    ),
                new State("Stalk",
                    new Prioritize(new StayCloseToSpawn(0.5, 14), new Follow(0.45, 14, 3), new Wander(0.3)),
                    new TossObject("Blight Spore Pod", 10, coolDown: 2600, minAngle: 0, maxAngle: 360, minRange: 4, maxRange: 10,
                        densityRange: 18, maxDensity: 9),
                    new Shoot(14, 8, shootAngle: 45, projectileIndex: 2, fixedAngle: 22, rotateAngle: 22, coolDown: 1800),
                    new Shoot(12, 5, shootAngle: 12, projectileIndex: 3, predictive: 0.5, coolDown: 1500),
                    new Shoot(14, 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, rotateAngle: -15, coolDown: 2000),
                    new Starfall(Starfall.Area.Players, 14, 3, 90, new Cooldown(3200, 600), color: 0xFF8AE040,
                        effect: ConditionEffectIndex.Sick, effectMs: 3000),
                    new HpLessTransition(0.25, "Fruit")
                    ),
                // every pod she has ripens at once
                new State("Fruit",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Order(40, "Blight Spore Pod", "Ripe"),
                    new Taunt(true, "RIPEN, MY CHILDREN! BURST!"),
                    new Flash(0xD050A0, 0.15, 12),
                    new TimedTransition(1800, "Fruiting")
                    ),
                new State("Fruiting",
                    new Prioritize(new StayCloseToSpawn(0.5, 14), new Follow(0.4, 14, 3), new Wander(0.3)),
                    new Shoot(0, 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, rotateAngle: 13, coolDown: 320),
                    new Shoot(12, 3, shootAngle: 15, projectileIndex: 1, predictive: 0.4, coolDown: 1200),
                    new Shoot(12, 4, shootAngle: 12, projectileIndex: 3, predictive: 0.5, coolDown: 1900),
                    new Spawn("Sporeling", maxChildren: 5, initialSpawn: 0, coolDown: 3500),
                    new TossObject("Blight Spore Pod", 10, coolDown: 3000, minAngle: 0, maxAngle: 360, minRange: 4, maxRange: 10,
                        densityRange: 18, maxDensity: 6)
                    )
                ),
            new Threshold(0.005, LootTemplates.BasicDrop()),
            new Threshold(0.005, LootTemplates.BasicPots()),
            new Threshold(0.01,
                new ItemLoot("Gaseous Glaive", 0.006),
                new ItemLoot("Corrosion Cutter", 0.006),
                new ItemLoot("Sewer Cocktail", 0.006),
                new ItemLoot("Potion of Vitality", 0.5),
                new ItemLoot("Potion of Defense", 0.5)
                )
            )
        // her pods heal her while they stand, swell, and burst into sporelings when ripe
        .Init("Blight Spore Pod",
            new State(
                new HealEntity(25, "Blight Mother", 350, new Cooldown(2000, 0)),
                new State("Grow",
                    new ChangeSize(5, 140),
                    new TimedTransition(14000, "Ripe")
                    ),
                new State("Ripe",
                    new Flash(0xD050A0, 0.15, 5),
                    new TimedTransition(900, "Burst")
                    ),
                new State("Burst",
                    new Shoot(0, 10, shootAngle: 36, fixedAngle: 0, coolDown: 99999),
                    new TossObject("Sporeling", 1, angle: 0, coolDown: 99999),
                    new TimedTransition(200, "Wither")
                    ),
                new State("Wither",
                    new Vanish(false)
                    )
                ))
        .Init("Sporeling",
            new State(
                new State("Creep",
                    new Prioritize(new StayCloseToSpawn(0.9, 16), new Follow(0.9, 12, 1), new Wander(0.5)),
                    new Shoot(8, 3, shootAngle: 20, predictive: 0.3, coolDown: 1000),
                    new TimedTransition(25000, "Wither")
                    ),
                new State("Wither",
                    new Vanish(false)
                    )
                ))

        // ================================================================ the Thieves' Bazaar
        .Init("R2 Hoard Rat",
            new State(
                R2Quip("Squeak! Mine!", "Shiny!", "Cheese? No, GOLD!", "Finders keepers!"),
                new Prioritize(new StayCloseToSpawn(1.3, 16), new Swirl(1.3, 3, 9), new Wander(0.7)),
                new Shoot(7, 2, shootAngle: 20, coolDown: 600)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Cutpurse",
            new State(
                new State("Sneak",
                    R2Keep(1.0, 5),
                    new Shoot(9, 2, shootAngle: 10, predictive: 0.6, coolDown: 800),
                    new Spawn("R2 Hoard Rat", maxChildren: 2, initialSpawn: 0, coolDown: 9000, givesNoXp: false),
                    new TimedTransition(4000, "Stab")
                    ),
                new State("Stab",
                    new Charge(2.4, 9, coolDown: 600),
                    new Shoot(5, 5, shootAngle: 18, coolDown: 500),
                    new TimedTransition(1400, "Sneak")
                    )
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Bazaar Bruiser",
            new State(
                R2Walk(0.6, 10, 2),
                new Shoot(9, 3, shootAngle: 25, coolDown: 1500),
                new Shoot(9, 8, shootAngle: 45, fixedAngle: 0, coolDown: 3000, coolDownOffset: 1500)
                ),
            R2Mob("Potion of Dexterity"))
        .Init("R2 Gold Gremlin",
            new State(
                R2Quip("Gold for me, lead for you!", "Pay up!", "No refunds!", "That'll cost you!"),
                R2Keep(1.1, 6),
                new Shoot(10, 3, shootAngle: 120, fixedAngle: 0, rotateAngle: 17, coolDown: 400),
                new Shoot(9, 1, predictive: 0.5, coolDown: 1000)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Bandit King",
            new State(
                new State("Hold",
                    R2Walk(0.7, 11, 4),
                    new Shoot(11, 3, shootAngle: 12, projectileIndex: 0, predictive: 0.5, coolDown: 900),
                    new Shoot(11, 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, rotateAngle: 15, coolDown: 600),
                    new Spawn("R2 Cutpurse", maxChildren: 2, initialSpawn: 0, coolDown: 12000, givesNoXp: false),
                    new TimedTransition(5000, "Raid"),
                    new HpLessTransition(0.3, "Desperate")
                    ),
                new State("Raid",
                    new Taunt(0.5, "Stand and deliver!", "Your gold or your life!"),
                    new Flash(0xD8A020, 0.25, 3),
                    new Charge(2.6, 11, coolDown: 900),
                    new Shoot(8, 8, shootAngle: 45, projectileIndex: 0, coolDown: 700),
                    new TimedTransition(2500, "Hold")
                    ),
                new State("Desperate",
                    new Taunt("You'll not take my crown!"),
                    new Prioritize(new Follow(1.0, 12, 2), new Wander(0.4)),
                    new Shoot(11, 4, shootAngle: 10, projectileIndex: 0, predictive: 0.6, coolDown: 600),
                    new Shoot(11, 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, rotateAngle: 13, coolDown: 450)
                    ),
                new DropPortalOnDeath("Pirate Cave Portal", 0.3)
                ),
            R2Quest("Potion of Speed", "Potion of Dexterity"))
        .Init("R2 Gilded Effigy",
            new State(
                new State("Watch",
                    new Prioritize(new StayCloseToSpawn(0.4, 10), new Wander(0.3)),
                    new Shoot(13, 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 0, rotateAngle: 10, coolDown: 300),
                    new Shoot(12, 2, shootAngle: 14, projectileIndex: 0, predictive: 0.4, coolDown: 1500),
                    new TimedTransition(6000, "Judge")
                    ),
                new State("Judge",
                    new Taunt(0.5, "Your greed is weighed... and found wanting."),
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                    new Flash(0xF8D860, 0.3, 4),
                    new Shoot(13, 16, projectileIndex: 0, coolDown: 1200),
                    new Starfall(Starfall.Area.Players, 13, 2.5, 110, new Cooldown(1400, 300), color: 0xFFFFD24A),
                    new TimedTransition(4000, "Watch")
                    ),
                new DropPortalOnDeath("Pirate Cave Portal", 0.3)
                ),
            R2Quest("Potion of Dexterity", "Potion of Speed"))

        // ================================================================ the Starfall Crater
        .Init("R2 Star Mote",
            new State(
                R2Quip("Twinkle twinkle!", "Wish upon me!", "I'm a star!", "Shine on!"),
                new Prioritize(new StayCloseToSpawn(1.4, 16), new Swirl(1.4, 2.5, 9), new Wander(0.8)),
                new Shoot(7, coolDown: 500)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Astral Sentinel",
            new State(
                new Prioritize(new StayCloseToSpawn(0.5, 12), new Follow(0.5, 10, 5), new Wander(0.3)),
                new Shoot(11, 5, shootAngle: 72, projectileIndex: 0, fixedAngle: 0, rotateAngle: 12, coolDown: 900),
                new Shoot(10, 2, shootAngle: 8, projectileIndex: 1, predictive: 0.6, coolDown: 1300),
                new Spawn("R2 Star Mote", maxChildren: 3, initialSpawn: 0, coolDown: 8000, givesNoXp: false)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Meteorling",
            new State(
                new State("Drift",
                    R2Walk(0.9, 10, 4),
                    new Shoot(9, 2, shootAngle: 12, predictive: 0.4, coolDown: 900),
                    new TimedTransition(3000, "Plunge")
                    ),
                new State("Plunge",
                    new Flash(0xFFB040, 0.2, 3),
                    new Charge(2.6, 10, coolDown: 700),
                    new Shoot(5, 8, shootAngle: 45, coolDown: 800),
                    new TimedTransition(1500, "Drift")
                    )
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Void Watcher",
            new State(
                new Prioritize(new StayCloseToSpawn(0.6, 16), new Orbit(0.6, 6, 11), new Wander(0.4)),
                new Shoot(11, 3, shootAngle: 14, projectileIndex: 0, predictive: 0.4, coolDown: 1100),
                new Shoot(11, 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, rotateAngle: 20, coolDown: 1800)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Star Prism",
            new State(
                new State("Refract",
                    new Prioritize(new StayCloseToSpawn(0.5, 10), new Orbit(0.5, 4, 12), new Wander(0.3)),
                    new Shoot(14, 3, shootAngle: 120, projectileIndex: 0, fixedAngle: 0, rotateAngle: 14, coolDown: 280),
                    new Shoot(12, 4, shootAngle: 10, projectileIndex: 1, predictive: 0.5, coolDown: 1100),
                    new TimedTransition(6000, "Shatter"),
                    new HpLessTransition(0.3, "Shatter")
                    ),
                new State("Shatter",
                    new Flash(0xEFFCFF, 0.2, 6),
                    new Shoot(14, 24, projectileIndex: 0, coolDown: 900),
                    new Spawn("R2 Star Mote", maxChildren: 4, initialSpawn: 0, coolDown: 5000, givesNoXp: false),
                    new TimedTransition(3000, "Refract")
                    ),
                new DropPortalOnDeath("Ice Cave Portal", 0.3)
                ),
            R2Quest("Potion of Wisdom", "Potion of Attack"))
        .Init("R2 Comet Wraith",
            new State(
                new State("Circle",
                    new Prioritize(new StayCloseToSpawn(0.9, 12), new Swirl(0.9, 6, 12), new Wander(0.4)),
                    new Shoot(12, 3, shootAngle: 15, projectileIndex: 0, predictive: 0.5, coolDown: 1000),
                    new Shoot(12, 2, shootAngle: 180, projectileIndex: 1, fixedAngle: 0, rotateAngle: 19, coolDown: 250),
                    new TimedTransition(5000, "Streak")
                    ),
                new State("Streak",
                    new Taunt(0.4, "Burn across the sky with me!"),
                    new Charge(3.0, 13, coolDown: 800),
                    new Starfall(Starfall.Area.Players, 12, 2, 100, new Cooldown(800, 200), color: 0xFFFFB040),
                    new Shoot(8, 6, shootAngle: 60, projectileIndex: 1, coolDown: 600),
                    new TimedTransition(2500, "Circle")
                    ),
                new DropPortalOnDeath("Ice Cave Portal", 0.3)
                ),
            R2Quest("Potion of Attack", "Potion of Wisdom"))

        // ================================================================ the Blightwood
        .Init("R2 Spore Puff",
            new State(
                R2Quip("Poof!", "Achoo!", "Breathe deep!", "Spores for everyone!"),
                new Prioritize(new StayCloseToSpawn(1.1, 16), new Swirl(1.1, 3, 9), new Wander(0.6)),
                new Shoot(7, 4, shootAngle: 90, coolDown: 900)
                ),
            R2Mob("Potion of Vitality"))
        .Init("R2 Rot Shambler",
            new State(
                R2Walk(0.45, 10, 2),
                new Shoot(10, 3, shootAngle: 20, predictive: 0.3, coolDown: 1400),
                new Spawn("R2 Spore Puff", maxChildren: 2, initialSpawn: 0, coolDown: 9000, givesNoXp: false)
                ),
            R2Mob("Potion of Vitality"))
        .Init("R2 Shroom Brute",
            new State(
                new State("March",
                    R2Walk(0.8, 10, 2),
                    new Shoot(8, 2, shootAngle: 12, coolDown: 900),
                    new TimedTransition(3500, "Bash")
                    ),
                new State("Bash",
                    new Charge(2.2, 9, coolDown: 500),
                    new Shoot(5, 6, shootAngle: 60, coolDown: 700),
                    new TimedTransition(1500, "March")
                    )
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Blight Webspinner",
            new State(
                R2Keep(0.9, 6),
                new Shoot(10, 3, shootAngle: 16, predictive: 0.4, coolDown: 1200),
                new Grenade(2.5, 70, 9, coolDown: 3500, effect: ConditionEffectIndex.Slowed, effectDuration: 2000, color: 0xFF8AE040)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Mycelial Horror",
            new State(
                new State("Spread",
                    new Prioritize(new StayCloseToSpawn(0.4, 10), new Wander(0.3)),
                    new Shoot(13, 5, shootAngle: 72, projectileIndex: 1, fixedAngle: 0, rotateAngle: 11, coolDown: 600),
                    new Shoot(12, 3, shootAngle: 14, projectileIndex: 0, predictive: 0.4, coolDown: 1300),
                    new Spawn("R2 Spore Puff", maxChildren: 3, initialSpawn: 0, coolDown: 8000, givesNoXp: false),
                    new TimedTransition(6000, "Bloom")
                    ),
                new State("Bloom",
                    new Taunt(0.4, "We are many. We are one. We are everywhere."),
                    new Flash(0x8AE040, 0.25, 6),
                    new Shoot(13, 20, projectileIndex: 1, coolDown: 1100),
                    new Starfall(Starfall.Area.Players, 13, 3, 90, new Cooldown(1500, 300), color: 0xFF8AE040,
                        effect: ConditionEffectIndex.Sick, effectMs: 2500),
                    new TimedTransition(3500, "Spread")
                    ),
                new DropPortalOnDeath("Toxic Sewers Portal", 0.3)
                ),
            R2Quest("Potion of Vitality", "Potion of Defense"))
        .Init("R2 Rotcap Elder",
            new State(
                new State("Brood",
                    R2Walk(0.5, 11, 3),
                    new Shoot(12, 3, shootAngle: 30, projectileIndex: 0, predictive: 0.3, coolDown: 1200),
                    new Shoot(12, 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, rotateAngle: 22, coolDown: 1600),
                    new TimedTransition(5000, "Slam")
                    ),
                new State("Slam",
                    new Taunt(0.4, "Return to the soil!"),
                    new Charge(2.0, 10, coolDown: 1000),
                    new Shoot(6, 12, projectileIndex: 0, coolDown: 900),
                    new TimedTransition(3000, "Brood")
                    ),
                new DropPortalOnDeath("Toxic Sewers Portal", 0.3)
                ),
            R2Quest("Potion of Defense", "Potion of Vitality"));

        // keeps away from players, inside its patch: the Gilded Thief and its decoys
        private static Prioritize ThiefRun(double speed, double distance) =>
            new Prioritize(new StayCloseToSpawn(speed * 1.2, 24), new StayBack(speed, distance), new Wander(speed * 0.5));
    }
}
