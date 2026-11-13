using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // The Fungal Cavern (RealmEye): break 25 of the Cavern Crystals around the caves to power the
        // Dwarf Miner's drill; it then drills through the wall to the round chamber at the north-east
        // while six mushroom war bands pour out of the alcoves beside it, and behind the wall waits the
        // Crystal Worm Mother. The map places the crystals, the drill, its wall and the Mother
        // (2026-11-13). The leaders of the six mushroom families drop the tribes' gear; the Mother
        // drops the dungeon's best.
        private _ FungalCavern = () => Behav()
        .Init("GC Mushroom Slammer Melee",
            new State(
                new Follow(0.7, 8, 1),
                new Wander(0.3),
                new Grenade(2.5, 100, range: 3, coolDown: 2000, color: 0x8B4513)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        // the alchemists patch up the mushrooms around them
        .Init("GC Mushroom Slammer Mage",
            new State(
                new StayCloseToSpawn(0.5, 4),
                new Wander(0.4),
                new HealEntity(8, "GC Mushroom", 250, coolDown: 3000)
                ),
            new ItemLoot("Magic Potion", 0.05)
            )
        .Init("GC Mushroom Circler Melee",
            new State(
                new Follow(0.7, 8, 2),
                new Wander(0.3),
                new Shoot(5, count: 1, projectileIndex: 0, coolDown: 800),
                new Shoot(4, count: 3, shootAngle: 20, projectileIndex: 2, coolDown: 3000)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Coward Melee",
            new State(
                new StayBack(0.6, 6),
                new Wander(0.3),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1200),
                new Shoot(8, count: 1, projectileIndex: 1, coolDown: 1200, coolDownOffset: 200),
                new Shoot(8, count: 1, projectileIndex: 2, coolDown: 1200, coolDownOffset: 400)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Charger Melee",
            new State(
                new Charge(2, 10, coolDown: 3000),
                new Wander(0.3),
                new Shoot(6, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1500),
                new Shoot(6, count: 1, projectileIndex: 1, coolDown: 1500, coolDownOffset: 300)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Charger Mage",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1000)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("GC Mushroom Spiral Melee",
            new State(
                new Follow(0.6, 8, 2),
                new Wander(0.3),
                new Shoot(8, count: 2, shootAngle: 10, projectileIndex: 0, coolDown: 900)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Spiral Mage",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1200),
                new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, coolDown: 3000)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("GC Cavecrawler Bat",
            new State(
                new Follow(1.2, 9, 1),
                new Wander(0.6),
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 900),
                new Shoot(0, count: 1, projectileIndex: 1, fixedAngle: 0, coolDown: 3000)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Cavecrawler Webslinger",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.3),
                new Shoot(10, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 2000),
                new Shoot(8, count: 1, projectileIndex: 1, coolDown: 1200)
                ),
            new ItemLoot("Magic Potion", 0.05)
            )
        .Init("GC Red Worm",
            new State(
                new StayCloseToSpawn(0.5, 8),
                new Wander(0.5),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1500),
                new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 45, coolDown: 2500)
                ),
            new ItemLoot("Health Potion", 0.1)
            )
        // the map places loose worm segments; they act as turrets
        .Init("GC Red Worm Body",
            new State(
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 2000)
                )
            )
        .Init("GC Red Worm Tail",
            new State(
                new Shoot(6, count: 1, projectileIndex: 0, coolDown: 2000)
                )
            )
        #region The mushroom families' leaders
        // Mushroom Berserker (green): charges, and gets wilder as he weakens
        .Init("GC Mushroom Charger Leader",
            new State(
                new State("Idle",
                    new Wander(0.3),
                    new PlayerWithinTransition(9, "Rally")
                    ),
                new State("Rally",
                    new Taunt("The tribe will not fall!"),
                    new Flash(0x00FF00, 0.5, 3),
                    new TimedTransition(1500, "Charge")
                    ),
                new State("Charge",
                    new Charge(2.5, 10, coolDown: 2500),
                    new StayCloseToSpawn(0.6, 8),
                    new Shoot(10, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: 1500),
                    new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, coolDown: 2500),
                    new HpLessTransition(0.4, "Frenzy")
                    ),
                new State("Frenzy",
                    new Taunt("RRAAAGH!"),
                    new Flash(0xFF0000, 0.5, 6),
                    new Charge(3, 10, coolDown: 1800),
                    new StayCloseToSpawn(0.6, 8),
                    new Shoot(10, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: 1000),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 22.5, coolDown: 2000),
                    new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 2, fixedAngle: 0, coolDown: 4000)
                    )
                ),
            MushroomLeaderLoot()
            )
        // Mushroom Chieftain (brown): slams and scatters quieting spores
        .Init("GC Mushroom Slammer Leader",
            new State(
                new Follow(0.6, 10, 2),
                new Wander(0.3),
                new State("Slam",
                    new Shoot(8, count: 3, shootAngle: 20, projectileIndex: 0, predictive: 0.5, coolDown: 1400),
                    new Grenade(3, 150, range: 6, coolDown: 2500, color: 0x8B4513),
                    new TimedTransition(5000, "Spores")
                    ),
                new State("Spores",
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, coolDown: 1500),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 2, fixedAngle: 22.5, coolDown: 1500, coolDownOffset: 500),
                    new Shoot(10, count: 2, shootAngle: 30, projectileIndex: 3, coolDown: 1500, coolDownOffset: 1000),
                    new TimedTransition(4000, "Slam")
                    )
                ),
            MushroomLeaderLoot()
            )
        // Mushroom Keeper (red): circles its prey, breaking armour
        .Init("GC Mushroom Circler Leader",
            new State(
                new Prioritize(
                    new Orbit(0.8, 4, 12),
                    new Wander(0.4)
                    ),
                new Shoot(10, count: 3, shootAngle: 12, projectileIndex: 0, coolDown: 900),
                new Shoot(10, count: 1, projectileIndex: 1, predictive: 0.7, coolDown: 2400)
                ),
            MushroomLeaderLoot()
            )
        // Mushroom Guardian (blue): stands its ground, silencing whoever comes close
        .Init("GC Mushroom Passive Leader",
            new State(
                new StayCloseToSpawn(0.4, 3),
                new Wander(0.2),
                new State("Watch",
                    new PlayerWithinTransition(8, "Guard")
                    ),
                new State("Guard",
                    new Shoot(10, count: 4, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                    new Shoot(10, count: 2, shootAngle: 40, projectileIndex: 1, coolDown: 2600),
                    new NoPlayerWithinTransition(12, "Watch")
                    )
                ),
            MushroomLeaderLoot()
            )
        // Mushroom Sorcerer (yellow): keeps away, filling the air with slow blasts and waves
        .Init("GC Mushroom Coward Leader",
            new State(
                new StayBack(0.6, 7),
                new Wander(0.3),
                new Shoot(12, count: 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, rotateAngle: 20, coolDown: 1600),
                new Shoot(10, count: 3, shootAngle: 15, projectileIndex: 3, coolDown: 2000),
                new Shoot(10, count: 2, shootAngle: 25, projectileIndex: 4, coolDown: 2000, coolDownOffset: 400),
                new Shoot(10, count: 1, projectileIndex: 5, predictive: 0.6, coolDown: 2000, coolDownOffset: 800)
                ),
            MushroomLeaderLoot()
            )
        // Mushroom Shaman (purple): weakening spore spirals
        .Init("GC Mushroom Spiral Leader",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.3),
                new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 0, rotateAngle: 15, coolDown: 400)
                ),
            MushroomLeaderLoot()
            )
        #endregion

        #region The other mushrooms the war bands bring
        .Init("GC Mushroom Circler Mage",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(9, count: 2, shootAngle: 15, projectileIndex: 0, coolDown: 1100)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("GC Mushroom Passive Melee",
            new State(
                new Follow(0.6, 8, 1.5),
                new Wander(0.3),
                new Shoot(4, count: 2, shootAngle: 20, projectileIndex: 0, coolDown: 900)
                ),
            new ItemLoot("Health Potion", 0.03)
            )
        .Init("GC Mushroom Passive Mage",
            new State(
                new StayCloseToSpawn(0.5, 5),
                new Wander(0.4),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1500),
                new Shoot(8, count: 1, projectileIndex: 2, coolDown: 1500, coolDownOffset: 300),
                new Shoot(8, count: 1, projectileIndex: 4, coolDown: 1500, coolDownOffset: 600)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        .Init("GC Mushroom Coward Mage",
            new State(
                new StayBack(0.6, 6),
                new Wander(0.3),
                new Shoot(10, count: 2, shootAngle: 20, projectileIndex: 0, coolDown: 1400),
                new Shoot(10, count: 1, projectileIndex: 1, coolDown: 1400, coolDownOffset: 700)
                ),
            new ItemLoot("Magic Potion", 0.03)
            )
        #endregion

        #region The Cavern Crystals and the drill
        // 25 broken power the drill; each bursts into quieting shards
        .Init("GC Crystal",
            new State(
                new CountOnDeath("Cavern Crystals"),
                new TransformOnDeath("GC Destroyed Crystal Explo")
                ),
            new Threshold(0.01,
                new ItemLoot("Ring of Decades", 0.003),
                new ItemLoot("Greater Potion of Wisdom", 0.05)
                )
            )
        .Init("GC Destroyed Crystal Explo",
            new State(
                new State("Burst",
                    new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 0, fixedAngle: 0, coolDown: 9999),
                    new TimedTransition(300, "Gone")
                    ),
                new State("Gone",
                    new Decay(0)
                    )
                )
            )
        // the drill before the wall (149, 26 on the map): it shows its charge, one step for every five
        // crystals, and once full drills for 40 s while war bands come out of the six alcoves, then
        // breaks the wall (OpenGate)
        .Init("GC Drill Charge",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("Charge0",
                    new WorldCountTransition("Cavern Crystals", 5, "Charge1")
                    ),
                new State("Charge1",
                    new SetAltTexture(1),
                    new WorldCountTransition("Cavern Crystals", 10, "Charge2")
                    ),
                new State("Charge2",
                    new SetAltTexture(2),
                    new WorldCountTransition("Cavern Crystals", 15, "Charge3")
                    ),
                new State("Charge3",
                    new SetAltTexture(3),
                    new WorldCountTransition("Cavern Crystals", 20, "Charge4")
                    ),
                new State("Charge4",
                    new SetAltTexture(4),
                    new WorldCountTransition("Cavern Crystals", 25, "Drilling")
                    ),
                Drilling(),
                new State("Break",
                    new OpenGate("GC Drill Wall", 4),
                    new TimedTransition(1000, "Done")
                    ),
                new State("Done")
                )
            )
        // the Dwarf Miner beside it tells the players how it goes
        .Init("GC Drill Dwarf",
            new State(
                new State("Wait",
                    new PlayerWithinTransition(8, "Ask")
                    ),
                new State("Ask",
                    new Taunt("My drill needs power! Break the Cavern Crystals in these caves and she'll cut through that wall."),
                    new WorldCountTransition("Cavern Crystals", 10, "Halfway")
                    ),
                new State("Halfway",
                    new Taunt(true, "The drill's charging up nicely. Keep breaking those crystals!"),
                    new WorldCountTransition("Cavern Crystals", 25, "Go")
                    ),
                new State("Go",
                    new Taunt(true, "That's the lot! Stand back, she's starting up... and here come the mushrooms!"),
                    new TimedTransition(41000, "Open")
                    ),
                new State("Open",
                    new Taunt(true, "We're through! Whatever's in there, it's all yours."),
                    new TimedTransition(100, "Done")
                    ),
                new State("Done")
                )
            )
        #endregion

        #region The Crystal Worm Mother (RealmEye: a head, 15 body segments of which some are cracked
        // weak points, and a tail; seven phases, with her mate and her young in between)
        .Init("GC Boss",
            new State(
                new ScaleHP2(20),
                new SpawnWorm(1.3f, 0, WormMotherBody),
                new State("Idle",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new CircleArena(0.5, 11),
                    new PlayerWithinTransition(12, "P1")
                    ),
                // her cracked segments are all that can be hurt
                new State("P1",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "The Crystal Worm Mother uncoils, her cracked segments glowing."),
                    new Order(40, "GC Boss Body", "Armored"),
                    new Order(40, "GC Boss Body Weakpoint", "Exposed"),
                    new Follow(0.35, 30, 3),
                    new Shoot(30, count: 5, shootAngle: 14, projectileIndex: 0, coolDown: 1500),
                    new EntitiesNotExistsTransition(40, "P1 Open", "GC Boss Body Weakpoint")
                    ),
                new State("P1 Open",
                    new Follow(0.4, 30, 3),
                    new Shoot(30, count: 5, shootAngle: 14, projectileIndex: 0, coolDown: 1200),
                    new Shoot(30, count: 3, shootAngle: 20, projectileIndex: 1, coolDown: 1200, coolDownOffset: 600),
                    new HpLessTransition(0.8, "P2")
                    ),
                // she circles; two segments must break before her head is open again
                RingPhase("P2", 2, 1, "P2 Open"),
                new State("P2 Open",
                    new Order(40, "GC Boss Body", "Armored"),
                    new Follow(0.45, 30, 3),
                    new Shoot(30, count: 5, shootAngle: 14, projectileIndex: 0, coolDown: 1200),
                    new HpLessTransition(0.6, "P3")
                    ),
                // the Crystal Worm Father comes up in the middle; she can't be hurt while he fights
                new State("P3",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "The ground shakes as the Crystal Worm Father rises!"),
                    new Order(40, "GC Boss Body", "Silent"),
                    new SpawnAtArenaCentre("GC Boss Support"),
                    new CircleArena(0.9, 11),
                    new Shoot(30, count: 2, shootAngle: 10, projectileIndex: 1, coolDown: 1600),
                    new State("P3 Father",
                        new TimedTransition(2000, "P3 Wait")
                        ),
                    new State("P3 Wait",
                        new EntitiesNotExistsTransition(60, "P3 Rest", "GC Boss Support")
                        )
                    ),
                new State("P3 Rest",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new CircleArena(0.6, 11),
                    new TimedTransition(3000, "P3 Ring")
                    ),
                RingPhase("P3 Ring", 4, 2, "P3 Open"),
                new State("P3 Open",
                    new Order(40, "GC Boss Body", "Armored"),
                    new Follow(0.5, 30, 3),
                    new Shoot(30, count: 6, shootAngle: 12, projectileIndex: 0, coolDown: 1100),
                    new HpLessTransition(0.4, "P4")
                    ),
                // the Crystal Worm Child: a worm of its own, its head open once its body is broken
                new State("P4",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "A Crystal Worm Child bursts from the floor!"),
                    new Order(40, "GC Boss Body", "Silent"),
                    new SpawnAtArenaCentre("GC Boss Child"),
                    new CircleArena(0.9, 11),
                    new Shoot(30, count: 3, shootAngle: 10, projectileIndex: 1, coolDown: 1600),
                    new State("P4 Child",
                        new TimedTransition(2000, "P4 Wait")
                        ),
                    new State("P4 Wait",
                        new EntitiesNotExistsTransition(60, "P4 Rest", "GC Boss Child")
                        )
                    ),
                new State("P4 Rest",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new CircleArena(0.6, 11),
                    new TimedTransition(3000, "P4 Ring")
                    ),
                RingPhase("P4 Ring", 4, 3, "P4 Open"),
                new State("P4 Open",
                    new Order(40, "GC Boss Body", "Armored"),
                    new Follow(0.55, 30, 3),
                    new Shoot(30, count: 6, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                    new HpLessTransition(0.2, "P5")
                    ),
                // fast round the shrinking arena, throwing yellow shards that come back, while every
                // segment left fires hard: all must break
                new State("P5",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Order(40, "GC Boss Body", "Frenzy"),
                    new CircleArena(1.5, 10),
                    new Shoot(30, count: 1, projectileIndex: 2, coolDown: 700),
                    new EntitiesNotExistsTransition(60, "P6", "GC Boss Body", "GC Boss Body Weakpoint")
                    ),
                // only her tail can be hurt now; she lobs red bombs and shotguns of red shards
                new State("P6",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Order(40, "GC Boss Tail", "Exposed"),
                    new Follow(0.5, 30, 2),
                    new Grenade(4, 125, range: 8, coolDown: 2000, color: 0xFF0000),
                    new Shoot(30, count: 3, shootAngle: 18, projectileIndex: 3, coolDown: 1600),
                    new Shoot(30, count: 3, shootAngle: 18, projectileIndex: 4, coolDown: 1600, coolDownOffset: 200),
                    new Shoot(30, count: 3, shootAngle: 18, projectileIndex: 5, coolDown: 1600, coolDownOffset: 400),
                    new EntitiesNotExistsTransition(60, "P7 Wait", "GC Boss Tail")
                    ),
                new State("P7 Wait",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "Only the Crystal Worm Mother's head remains!"),
                    new TimedTransition(2500, "P7")
                    ),
                new State("P7",
                    new Follow(0.6, 30, 2),
                    new Grenade(4, 125, range: 8, coolDown: 2500, color: 0xFF0000),
                    new Shoot(30, count: 3, shootAngle: 18, projectileIndex: 3, coolDown: 1400),
                    new Shoot(30, count: 3, shootAngle: 18, projectileIndex: 5, coolDown: 1400, coolDownOffset: 300),
                    new Shoot(30, count: 7, shootAngle: 8, projectileIndex: 0, coolDown: 500)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Greater Potion of Wisdom", 1),
                new ItemLoot("Potion of Life", 0.5),
                new ItemLoot("Potion of Wisdom", 1),
                new ItemLoot("Potion of Vitality", 0.5),
                new ItemLoot("Crystal Shield", 0.008),
                new ItemLoot("Sporous Spray Spell", 0.008),
                new ItemLoot("Tome of the Mushroom Tribes", 0.008),
                new ItemLoot("Cave Dweller Trap", 0.008),
                new ItemLoot("Fungal Breastplate", 0.008),
                new ItemLoot("Ring of Decades", 0.008),
                new ItemLoot("Shroom Generator", 0.03),
                new ItemLoot("Fungal Cavern Key", 0.1, 0, 0.03),
                new TierLoot(12, ItemType.Weapon, 0.15),
                new TierLoot(13, ItemType.Weapon, 0.06),
                new TierLoot(12, ItemType.Armor, 0.15),
                new TierLoot(13, ItemType.Armor, 0.06),
                new TierLoot(6, ItemType.Ability, 0.1),
                new TierLoot(6, ItemType.Ring, 0.1)
                )
            )
        // her body: shut and firing small bolts, open and firing rings, silent, or in a frenzy;
        // broken, it bursts into quieting shards
        .Init("GC Boss Body",
            new State(
                new WormSegment(1.3f),
                new TransformOnDeath("GC Boss Body Bomb"),
                new State("Armored",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Shoot(10, count: 1, projectileIndex: 0, coolDown: 1800)
                    ),
                new State("Exposed",
                    new Shoot(10, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, rotateAngle: 20, coolDown: 1600)
                    ),
                new State("Silent",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable)
                    ),
                new State("Frenzy",
                    new Shoot(10, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, rotateAngle: 15, coolDown: 600),
                    new Shoot(10, count: 1, projectileIndex: 2, coolDown: 2400)
                    )
                )
            )
        .Init("GC Boss Body Weakpoint",
            new State(
                new WormSegment(1.3f),
                new TransformOnDeath("GC Boss Body Bomb"),
                new State("Armored",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Shoot(10, count: 1, projectileIndex: 0, coolDown: 1800)
                    ),
                new State("Exposed",
                    new Shoot(10, count: 2, shootAngle: 20, projectileIndex: 1, coolDown: 1400)
                    )
                )
            )
        .Init("GC Boss Body Bomb",
            new State(
                new State("Burst",
                    new Shoot(0, count: 10, shootAngle: 36, projectileIndex: 0, fixedAngle: 0, coolDown: 9999),
                    new TimedTransition(300, "Gone")
                    ),
                new State("Gone",
                    new Decay(0)
                    )
                )
            )
        // her tail leaves a trail of slow, heavy shards and red blasts the whole fight
        .Init("GC Boss Tail",
            new State(
                new WormSegment(1.3f),
                new Shoot(3, count: 1, projectileIndex: 0, fixedAngle: 0, coolDown: 500),
                new Grenade(3, 150, range: 0, coolDown: 4000, color: 0xFF0000),
                new State("Armored",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable)
                    ),
                new State("Exposed")
                )
            )
        // the Crystal Worm Father: a storm of slow shards that fills the room; near death he sinks
        // back into the ground
        .Init("GC Boss Support",
            new State(
                new ScaleHP2(20),
                new StayCloseToSpawn(0.4, 3),
                new State("Storm",
                    new Shoot(30, count: 10, shootAngle: 36, projectileIndex: 0, fixedAngle: 0, rotateAngle: 13, coolDown: 900),
                    new Shoot(30, count: 6, shootAngle: 60, projectileIndex: 2, fixedAngle: 30, rotateAngle: -11, coolDown: 1500),
                    new HpLessTransition(0.5, "Dark")
                    ),
                new State("Dark",
                    new Shoot(30, count: 10, shootAngle: 36, projectileIndex: 0, fixedAngle: 0, rotateAngle: 13, coolDown: 900),
                    new Shoot(30, count: 6, shootAngle: 60, projectileIndex: 2, fixedAngle: 30, rotateAngle: -11, coolDown: 1500),
                    new Shoot(20, count: 3, shootAngle: 15, projectileIndex: 1, predictive: 0.5, coolDown: 1300),
                    new HpLessTransition(0.1, "Retreat")
                    ),
                new State("Retreat",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt(true, "The Crystal Worm Father sinks back into the ground."),
                    new Flash(0xFFFFFF, 0.3, 5),
                    new TimedTransition(1500, "Gone")
                    ),
                new State("Gone",
                    new Decay(0)
                    )
                )
            )
        .Init("GC Boss Child",
            new State(
                new ScaleHP2(20),
                new SpawnWorm(1.5f, 90, "GC Boss Child Body", "GC Boss Child Body", "GC Boss Child Body", "GC Boss Child Body", "GC Boss Child Body", "GC Boss Child Tail"),
                new State("Armored",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Follow(0.6, 25, 2),
                    new Shoot(25, count: 3, shootAngle: 15, projectileIndex: 0, coolDown: 1300),
                    new EntitiesNotExistsTransition(40, "Open", "GC Boss Child Body")
                    ),
                // a spiral of yellow shards that turn back
                new State("Open",
                    new StayCloseToSpawn(0.5, 4),
                    new Shoot(25, count: 6, shootAngle: 60, projectileIndex: 1, fixedAngle: 0, rotateAngle: 12, coolDown: 450)
                    )
                )
            )
        .Init("GC Boss Child Body",
            new State(
                new WormSegment(1.5f),
                new Shoot(10, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 45, rotateAngle: 15, coolDown: 1600)
                )
            )
        .Init("GC Boss Child Tail",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                new WormSegment(1.5f),
                new Shoot(8, count: 1, projectileIndex: 0, coolDown: 1500)
                )
            )
        #endregion
        ;

        // her body from the neck to the tail: 15 segments, the 5th and 11th cracked
        private static readonly string[] WormMotherBody =
        {
            "GC Boss Body", "GC Boss Body", "GC Boss Body", "GC Boss Body", "GC Boss Body Weakpoint",
            "GC Boss Body", "GC Boss Body", "GC Boss Body", "GC Boss Body", "GC Boss Body",
            "GC Boss Body Weakpoint", "GC Boss Body", "GC Boss Body", "GC Boss Body", "GC Boss Body",
            "GC Boss Tail"
        };

        // she circles, firing `shots` shards at a time; every body segment can be hurt, and `kills`
        // of them must break before her head opens again
        private static State RingPhase(string name, int kills, int shots, string next) =>
            new State(name,
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Order(40, "GC Boss Body", "Exposed"),
                new CircleArena(0.9, 11),
                new Shoot(30, count: shots, shootAngle: 10, projectileIndex: 1, coolDown: 1000),
                new KilledSinceEntryTransition(40, kills, next, "GC Boss Body")
                );

        // the drill at work: 40 s, while a war band (a leader and two of its tribe) comes out of each of
        // the six alcoves beside it, given as (range, angle) from the drill
        private static State Drilling()
        {
            var bands = new (string Family, double Range, double Angle)[]
            {
                ("Slammer", 19.65, 194.74), ("Circler", 13.93, 201.04), ("Passive", 8.6, 215.54),
                ("Coward", 19.65, 165.26), ("Charger", 13.93, 158.96), ("Spiral", 8.6, 144.46),
            };
            var children = new System.Collections.Generic.List<IStateChildren>
            {
                new SetAltTexture(5),
                new Flash(0xFF93CD, 1, 40),
                new TimedTransition(40000, "Break"),
            };
            foreach (var (family, range, angle) in bands)
            {
                children.Add(new TossObject($"GC Mushroom {family} Leader", range, angle, coolDown: 999999));
                children.Add(new TossObject($"GC Mushroom {family} Melee", range, angle + 3, coolDown: 999999, coolDownOffset: 300));
                children.Add(new TossObject($"GC Mushroom {family} Mage", range, angle - 3, coolDown: 999999, coolDownOffset: 600));
            }
            return new State("Drilling", children.ToArray());
        }

        // what the six leaders drop (RealmEye): the Magic Mushroom and the Fungal Breastplate
        private static Threshold MushroomLeaderLoot() =>
            new Threshold(0.01,
                new ItemLoot("Magic Mushroom", 0.05),
                new ItemLoot("Fungal Breastplate", 0.004),
                new ItemLoot("Tome of the Mushroom Tribes", 0.002),
                new ItemLoot("Potion of Wisdom", 0.3),
                new ItemLoot("Potion of Vitality", 0.15)
                );
    }
}
