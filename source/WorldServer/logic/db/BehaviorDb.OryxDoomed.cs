using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;
using WorldServer.core.worlds;

namespace WorldServer.logic
{
    // Oryx the Mad God 4 in Oryx's Inferno, from Doomed Realms. This replaces the generated Doomed
    // Realms version (InitDoomed), which stuck in its first ring phase for good. Oryx 5 is Oryx the
    // Ascended God, in BehaviorDb.Elysium.cs; his old helpers stay here.
    partial class BehaviorDb
    {
        // Oryx 4 in an arena: Hatred, Artifacts, Moons and the Doom Ring in turn; p goes before the
        // states' names. (In his Inferno he fights Oryx 1's fight instead: OryxFight.)
        private static IStateChildren[] O4Cycle(string p)
        {
            var hatred = new State(p + "Hatred",
                new Taunt("Fools! I still have {HP} hitpoints!") { Bubble = false },
                new Wander(0.15),
                new StayCloseToSpawn(0.4, 6),
                // his Infernal Court, from the Burning Path (BehaviorDb.HashCellar.cs)
                new Spawn("Infernal Officer", 1, 1, 12000),
                new Spawn("Brimstone Major", 1, 0, 12000),
                new Shoot(25, 5, 10, projectileIndex: 4, coolDown: 1000),
                new Shoot(25, 3, 12, projectileIndex: 2, predictive: 0.6, coolDown: 800),
                new Shoot(25, 2, 6, projectileIndex: 3, coolDown: 1300, coolDownOffset: 600),
                new TimedTransition(12000, p + "Artifacts")
            );
            var rest = new IStateChildren[]
            {
                new State(p + "Artifacts",
                    new Taunt("My Artifacts will protect me!") { Bubble = false },
                    new Shoot(25, 3, 20, projectileIndex: 16, coolDown: 1800, coolDownOffset: 500),
                    new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 8, coolDown: 2200),
                    new Shoot(3, 10, projectileIndex: 7, coolDown: 700),
                    // four Infernal Elements circle him close, four more further out
                    new TossObject("Infernal Element", InfernalInner, 0, 90000001, 800),
                    new TossObject("Infernal Element", InfernalInner, 90, 90000001, 800),
                    new TossObject("Infernal Element", InfernalInner, 180, 90000001, 800),
                    new TossObject("Infernal Element", InfernalInner, 270, 90000001, 800),
                    new TossObject("Infernal Outer Element", InfernalOuter, 45, 90000001, 800),
                    new TossObject("Infernal Outer Element", InfernalOuter, 135, 90000001, 800),
                    new TossObject("Infernal Outer Element", InfernalOuter, 225, 90000001, 800),
                    new TossObject("Infernal Outer Element", InfernalOuter, 315, 90000001, 800),
                    new TimedTransition(14000, p + "Moons")
                ),
                // White Moons circle him; his flames punish anyone who gets close
                new State(p + "Moons",
                    new Taunt("I am the master of this existence!") { Bubble = false },
                    new Follow(0.35, 14, 6),
                    new TossObject("White Moon", 8, 0, 90000001),
                    new TossObject("White Moon", 8, 60, 90000001),
                    new TossObject("White Moon", 8, 120, 90000001),
                    new TossObject("White Moon", 8, 180, 90000001),
                    new TossObject("White Moon", 8, 240, 90000001),
                    new TossObject("White Moon", 8, 300, 90000001),
                    new Shoot(6, 3, 12, projectileIndex: 1, coolDown: 900),
                    new Shoot(25, 2, 10, projectileIndex: 5, predictive: 0.5, coolDown: 1400),
                    new TimedTransition(12000, p + "Doom Ring")
                ),
                // telegraphed: a weakening ring with gaps to stand in
                new State(p + "Doom Ring",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new ReturnToSpawn(1.5),
                    new Flash(0xFFFFFFFF, 0.3, 6),
                    new Taunt("Feel the weight of my doom!") { Bubble = false },
                    new State(p + "Doom Charge",
                        new TimedTransition(2000, p + "Doom Fire")
                    ),
                    new State(p + "Doom Fire",
                        new Shoot(0, 16, fixedAngle: 0, projectileIndex: 19, coolDown: 1500),
                        new Shoot(0, 16, fixedAngle: 11.25, projectileIndex: 19, coolDown: 1500, coolDownOffset: 750),
                        new TimedTransition(3000, p + "Hatred")
                    )
                )
            };
            return new IStateChildren[] { hatred }.Concat(rest).ToArray();
        }

        private _ OryxDoomed = () => Behav()
            // Stage 6, Oryx the Infernal Mad God: Oryx 1's fight (BehaviorDb.OryxChamber.cs), burning and
            // harder, with his Infernal Court. His taunts go to chat only (no bubbles over his attacks).
            .Init(O4,
                new State(new IStateChildren[]
                {
                    new BossStats(90000, 50, 1.0, 0.3, arena: true),
                    new BossStats(240000, 50, 1.0, 0.3),
                    new DropPortalOnDeath("Locked Oryx's Elysium Portal", 1.0, timeout: 600, xAdjustment: 2,
                        name: "Locked: Oryx's Elysium", lockedHint: SealedDungeons.WhyDungeon("Oryx's Elysium")
                            ?? "This portal is locked. Use an Oryx's Elysium Incantation beside it to open it."),
                    new AnnounceOnDeath(SealedDungeons.WhyDungeon("Oryx's Elysium") != null
                        ? "{PL_LIST} burned Oryx the Infernal Mad God out of his Inferno! He rises to his Elysium, sealed for now..."
                        : "{PL_LIST} burned Oryx the Infernal Mad God out of his Inferno! He rises to his Elysium, sealed by an Oryx's Elysium Incantation..."),
                    new State("Where",
                        new ArenaTransition("Arena Wake"),
                        new ArenaTransition("Dormant", false)
                    ),
                    // in an arena: his old cycle (an arena has no room for Oryx 1's fight)
                    ArenaWake("Here, too, the fire answers me.", "Arena Hatred"),
                    new State("Arena Cycle", O4Cycle("Arena ")),
                    new State("Dormant",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(15, "Awaken")
                    ),
                    new State("Awaken",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFF4400, 0.4, 10),
                        new Taunt(true, "Three times you have struck me down. Here, in the fire, I am stronger.") { Bubble = false },
                        new TimedTransition(4000, "Old Times")
                    )
                }.Concat(OryxFight(new OryxSpec
                {
                    // Oryx 1's fight, burning: less warning, his last stand from 10%, hellfire throughout
                    Pause = 4000,
                    AllIn = 0.10,
                    Bubble = false,
                    Inner = "Infernal Element",
                    Outer = "Infernal Outer Element",
                    AllInTaunt = new[] { "I HAVE HAD ENOUGH OF YOU! THE FIRE TAKES YOU ALL!" },
                    Heal = 5000,
                    Size = 220,
                    Fight = new IStateChildren[]
                    {
                        new Shoot(10, 2, 12, projectileIndex: 2, predictive: 0.5, coolDown: 2400)
                    },
                    // his Infernal Court comes with the minions (BehaviorDb.HashCellar.cs)
                    Minions = new IStateChildren[]
                    {
                        new KeepMinions("Infernal Officer", 1, 12000),
                        new KeepMinions("Brimstone Major", 1, 12000)
                    },
                    Hunt = new IStateChildren[]
                    {
                        new KeepMinions("Infernal Officer", 2, 8000),
                        new KeepMinions("Brimstone Major", 1, 10000),
                        new Shoot(25, 3, 12, projectileIndex: 2, predictive: 0.8, coolDown: 1400),
                        new Taunt(0.4, 6000, "DIE!!!", "ENOUGH!!!", "Your bones will feed my fire!") { Bubble = false }
                    }
                })).ToArray()
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Life", 0.5),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Greater Potion of Life", 0.08),
                    new ItemLoot("Greater Potion of Mana", 0.05),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Oryx", 0.14),
                    // his forge shards, each as common as the Potion of Oryx (ForgeStorage.cs)
                    new ItemLoot("Infernal Weapon Shard", 0.14),
                    new ItemLoot("Infernal Ability Shard", 0.14),
                    new ItemLoot("Infernal Armor Shard", 0.14),
                    new ItemLoot("Infernal Accessory Shard", 0.14)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Speed", 0.65),
                    new ItemLoot("Potion of Dexterity", 0.65),
                    new ItemLoot("Potion of Vitality", 0.65),
                    new ItemLoot("Potion of Wisdom", 0.65),
                    new ItemLoot("Doom Dirk", 0.012),
                    new ItemLoot("Flying Doom Daggers", 0.012),
                    new ItemLoot("Tome of Noble Assault", 0.012),
                    new ItemLoot("Gem of Adoration", 0.012),
                    new ItemLoot("Gem of Tenderness", 0.012),
                    new ItemLoot("Corruption Cutter", 0.012),
                    new ItemLoot("Cloak of Bloody Surprises", 0.012),
                    new ItemLoot("Doom Poison", 0.003),
                    // the Elysium Incantation that opens his locked portal, as Oryx 3's Inferno one
                    new OnePlayerLoot("Doomed Cellar Incantation", 0.5, 0.01),
                    // his Lil' Oryx, in a white bag (each Oryx drops his own)
                    new ItemLoot("Lil' Infernal Oryx Generator", 0.01)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(13, ItemType.Weapon, 0.1),
                    new TierLoot(14, ItemType.Weapon, 0.1),
                    new TierLoot(14, ItemType.Armor, 0.12),
                    new TierLoot(15, ItemType.Armor, 0.07),
                    new TierLoot(6, ItemType.Ability, 0.16),
                    new TierLoot(6, ItemType.Ring, 0.16),
                    new TierLoot(7, ItemType.Ability, 0.03),
                    new TierLoot(7, ItemType.Ring, 0.03)
                )
            )

            // ---- Oryx 4's helpers ----
            // White Moons serve both: round Oryx 1, or Oryx 4 in his Inferno (their White Planet attack:
            // invulnerable, far out and fast, as in the original), or round Oryx 4 in an arena (slower
            // and closer, and they can be killed)
            .Init("White Moon",
                new State(
                    new State("Which",
                        new ArenaTransition("Inferno"),
                        new ArenaTransition("Chamber", false)
                    ),
                    new State("Chamber",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        OryxOrbit(4, 13.4, 20),
                        new Shoot(0, 10, 36, 0, fixedAngle: -18, coolDownOffset: 1500, coolDown: 700),
                        new Shoot(0, 10, 36, 0, fixedAngle: 0, coolDownOffset: 1850, coolDown: 700),
                        new TimedTransition(19000, "Gone")
                    ),
                    new State("Inferno",
                        new BossStats(4000, 20, 1.4, 0.25),
                        new Prioritize(
                            new Orbit(0.7, 8, 20, O4, speedVariance: 0, radiusVariance: 0),
                            new Wander(0.3)),
                        new Shoot(3, 4, projectileIndex: 0, coolDown: 1000),
                        new Shoot(12, projectileIndex: 1, predictive: 0.4, coolDown: 1500),
                        new TimedTransition(12000, "Gone")
                    ),
                    // Oryx's last stand: a parting ring
                    // (its end inside it, as Oryx orders "Final" every tick: O1Final)
                    O1Final(5, 0),
                    new State("Gone", new Suicide())
                )
            )

            // Doomed Realms' own Oryx 5 isn't used any more (Oryx the Ascended God, BehaviorDb.Elysium.cs,
            // took his place): this steps in for its generated behaviour so it has no attacks or loot
            .Init("Oryx the Mad God 5", new State())

            // ---- Oryx 5's old helpers ----
            .Init("Knight of Doom",
                new State(
                    new BossStats(6000, 30, 0.6, 0.25),
                    new Prioritize(new Follow(1.1, 15, 1), new Wander(0.4)),
                    new Shoot(2.5, 3, 20, projectileIndex: 0, coolDown: 500)
                ),
                new ItemLoot("Potion of Defense", 0.02),
                new ItemLoot("Potion of Attack", 0.02)
            )
            // each living tree heals Oryx 5 while he fights
            .Init("Tree of Doom",
                new State(
                    new BossStats(3000, 10, 1.0, 0.25),
                    new HealEntity(30, "Oryx the Ascended God", 900, 2000)
                )
            )
            // a fireball for anyone who stands right beside one, mild enough that walking past is safe
            // (they lined the old Oryx 4 dungeon's path)
            .Init("Jungle Fire",
                new State(
                    new BossStats(400, 80, 2.0, 0),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new Shoot(2.5, projectileIndex: 1, coolDown: new Cooldown(3000, 1000))
                )
            );

    }
}
