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
        // Oryx 4's cycle above 25%: Hatred, Silence, Artifacts, Moons and the Doom Ring in turn; p goes
        // before the states' names. In an arena it skips the Silence, ten invulnerable seconds.
        private static IStateChildren[] O4Cycle(string p, bool arena)
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
                new TimedTransition(12000, p + (arena ? "Artifacts" : "Silence"))
            );
            var silence = new State(p + "Silence",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new ReturnToSpawn(1.5),
                new Flash(0xFF389E13, 0.5, 20),
                new Taunt("BE SILENT!!!") { Bubble = false },
                new Shoot(0, 4, fixedAngle: 0, rotateAngle: 15, projectileIndex: 17, coolDown: 260),
                new TossObject("Ring Element", 9, 24, 320000),
                new TossObject("Ring Element", 9, 48, 320000),
                new TossObject("Ring Element", 9, 72, 320000),
                new TossObject("Ring Element", 9, 96, 320000),
                new TossObject("Ring Element", 9, 120, 320000),
                new TossObject("Ring Element", 9, 144, 320000),
                new TossObject("Ring Element", 9, 168, 320000),
                new TossObject("Ring Element", 9, 192, 320000),
                new TossObject("Ring Element", 9, 216, 320000),
                new TossObject("Ring Element", 9, 240, 320000),
                new TossObject("Ring Element", 9, 264, 320000),
                new TossObject("Ring Element", 9, 288, 320000),
                new TossObject("Ring Element", 9, 312, 320000),
                new TossObject("Ring Element", 9, 336, 320000),
                new TossObject("Ring Element", 9, 360, 320000),
                new TimedTransition(10000, p + "Artifacts")
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
            return (arena ? new IStateChildren[] { hatred } : new IStateChildren[] { hatred, silence }).Concat(rest).ToArray();
        }

        private _ OryxDoomed = () => Behav()
            // Stage 6, Oryx the Infernal Mad God: Oryx 1's moveset, burning and harder, with his Infernal
            // Court and White Moons. His taunts go to chat only (no bubbles over his attacks).
            .Init("Oryx the Mad God 4",
                new State(
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
                    // in an arena: only his cycle, without the Silence, and no Ascend or Rage
                    ArenaWake("Here, too, the fire answers me.", "Arena Hatred"),
                    new State("Arena Cycle", O4Cycle("Arena ", true)),
                    new State("Dormant",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(15, "Awaken")
                    ),
                    new State("Awaken",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFF4400, 0.4, 10),
                        new Taunt(true, "Three times you have struck me down. Here, in the fire, I am stronger.") { Bubble = false },
                        new TimedTransition(4000, "Hatred")
                    ),
                    new State("Cycle",
                        new IStateChildren[] { new HpLessTransition(0.25, "Ascend") }.Concat(O4Cycle("", false)).ToArray()
                    ),
                    new State("Ascend",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.5),
                        new ChangeSize(15, 220),
                        new Flash(0xFFFF0000, 0.3, 12),
                        new Taunt(true, "I HAVE HAD ENOUGH OF YOU! THE FIRE TAKES YOU ALL!") { Bubble = false },
                        new Shoot(0, 8, fixedAngle: 0, rotateAngle: 9, projectileIndex: 13, coolDown: 500),
                        new Shoot(0, 8, fixedAngle: 22, rotateAngle: -9, projectileIndex: 14, coolDown: 500, coolDownOffset: 250),
                        new TimedTransition(4000, "Rage")
                    ),
                    new State("Rage",
                        new Follow(0.6, 20, 3),
                        new Flash(0xFFFF0000, 1, 9000000),
                        new Spawn("Infernal Officer", 2, 0.5, 8000),
                        new Spawn("Brimstone Major", 1, 1, 10000),
                        new Shoot(25, 5, 10, projectileIndex: 16, coolDown: 1600),
                        new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 18, coolDown: 3200),
                        new Shoot(25, 3, 12, projectileIndex: 2, predictive: 0.8, coolDown: 700),
                        new Shoot(0, 6, fixedAngle: 0, rotateAngle: 12, projectileIndex: 14, coolDown: 1200),
                        new Taunt(0.4, 6000, "DIE!!!", "ENOUGH!!!", "Your bones will feed my fire!") { Bubble = false }
                    )
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
                    // his forge shards, each half as common as the Potion of Oryx (ForgeStorage.cs)
                    new ItemLoot("Infernal Weapon Shard", 0.07),
                    new ItemLoot("Infernal Ability Shard", 0.07),
                    new ItemLoot("Infernal Armor Shard", 0.07),
                    new ItemLoot("Infernal Accessory Shard", 0.07)
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
            .Init("Guardian Element 3", OryxInnerElement())
            .Init("Guardian Element 4", OryxInnerElement())
            .Init("Outer Guardian Element",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(1.1, 9.5, 20, "Oryx the Mad God 4", speedVariance: 0, radiusVariance: 0, orbitClockwise: true),
                    new Shoot(6, 2, 20, projectileIndex: 0, coolDown: 900),
                    new State("Live", new TimedTransition(14000, "Gone")),
                    new State("Gone", new Suicide())
                )
            )
            .Init("White Moon",
                new State(
                    new BossStats(4000, 20, 1.4, 0.25),
                    new Prioritize(
                        new Orbit(0.7, 8, 20, "Oryx the Mad God 4", speedVariance: 0, radiusVariance: 0),
                        new Wander(0.3)),
                    new Shoot(3, 4, projectileIndex: 0, coolDown: 1000),
                    new Shoot(12, projectileIndex: 1, predictive: 0.4, coolDown: 1500),
                    new State("Live", new TimedTransition(12000, "Gone")),
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
            .Init("Oryx Guardian Sword",
                new State(
                    new State("Idle", new PlayerWithinTransition(8, "Spin")),
                    new State("Spin",
                        new Wander(0.2),
                        new StayCloseToSpawn(0.4, 4),
                        new Shoot(0, 4, fixedAngle: 0, rotateAngle: 12, projectileIndex: 0, coolDown: 600),
                        new NoPlayerWithinTransition(12, "Idle")
                    )
                ),
                new ItemLoot("Potion of Defense", 0.05)
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

        private static State OryxInnerElement() =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Prioritize(
                    new Orbit(1, 1, target: "Oryx the Mad God 1", radiusVariance: 0),
                    new Orbit(1, 2, target: "Oryx the Mad God 4", radiusVariance: 0)),
                new Shoot(25, 3, 10, 0, coolDown: 1000),
                new State("Live", new TimedTransition(14000, "Gone")),
                new State("Gone", new Suicide())
            );
    }
}
