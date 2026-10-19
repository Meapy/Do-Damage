using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Chicken Chamber (tools/build_chicken_chamber.py): RotMG's April Fools take on Oryx's
    // Chamber, a side door into the Oryx chain beside Oryx 1. Players fight through a coop of
    // chickens and eggs to the Giant Oryx Chicken, which drops the locked portal to the Wine Cellar
    // (the Wine Cellar Incantation works here: its onlyInArea names the Chicken Chamber) and a Realm
    // Portal. The objects are the prod XML's OryxChickenChamber.xml, whose ids are still the test
    // names they were given: "TestChicken 2" is the Giant Oryx Chicken, "TestChicken Small" the
    // Oryx Chicken Minion and "Test Egg" the Egg.
    partial class BehaviorDb
    {
        private _ ChickenChamber = () => Behav()
            .Init("TestChicken 2",
                new State(
                    // a little below Oryx 1, whose place in the chain it takes
                    new BossStats(90000, 35, 0.8, 0.3),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120, xAdjustment: -2),
                    new DropPortalOnDeath("Locked Wine Cellar Portal", 1.0, timeout: 600, xAdjustment: 2,
                        name: "Locked: Wine Cellar", lockedHint: "This portal is locked. Use a Wine Cellar Incantation beside it to open it."),
                    new AnnounceOnDeath("{PL_LIST} plucked the Giant Oryx Chicken in its Chamber! Oryx's Wine Cellar lies beyond, sealed by a Wine Cellar Incantation..."),
                    new State("Roost",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(11, "Wake")
                    ),
                    // it grows from a hen into the giant (the XML size is a small chicken)
                    new State("Wake",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ChangeSize(20, 140),
                        new Flash(0xFFFF69B4, 0.3, 8),
                        new Taunt("BAWK! Who dares disturb the master's prize hen?"),
                        new Shoot(0, 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 6, coolDown: 400),
                        new TimedTransition(3000, "Peck")
                    ),
                    new State("Peck",
                        new Prioritize(
                            new StayCloseToSpawn(0.5, 6),
                            new Wander(0.3)
                        ),
                        new Shoot(15, 5, 12, projectileIndex: 0, coolDown: 1200),
                        new Shoot(15, 3, 15, projectileIndex: 1, predictive: 0.5, coolDown: 1600, coolDownOffset: 600),
                        new Shoot(0, 4, fixedAngle: 45, rotateAngle: 22.5, projectileIndex: 3, coolDown: 3000, coolDownOffset: 1500),
                        new TossObject("Test Egg", 6, coolDown: 4500, coolDownOffset: 2000),
                        new Taunt(0.4, 9000, "Cluck... cluck... CLUCK!", "My eggs will hatch an army!", "Peck them to pieces, my chicks!"),
                        new HpLessTransition(0.66, "Molt")
                    ),
                    // a short invulnerable flurry of feathers and blasters while it lays a clutch
                    new State("Molt",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(0.8),
                        new Flash(0xFFFF69B4, 0.4, 10),
                        new Taunt("My feathers! You will pay for every one!"),
                        new Shoot(0, 16, fixedAngle: 0, rotateAngle: 11.25, projectileIndex: 6, coolDown: 300),
                        new Shoot(0, 8, fixedAngle: 0, rotateAngle: 15, projectileIndex: 4, coolDown: 900),
                        new TossObject("Test Egg", 5, 0, coolDown: 90000),
                        new TossObject("Test Egg", 5, 90, coolDown: 90000),
                        new TossObject("Test Egg", 5, 180, coolDown: 90000),
                        new TossObject("Test Egg", 5, 270, coolDown: 90000),
                        new TimedTransition(5000, "Rainbow")
                    ),
                    new State("Rainbow",
                        new Prioritize(
                            new StayCloseToSpawn(0.5, 8),
                            new Wander(0.4)
                        ),
                        new Shoot(0, 2, 180, projectileIndex: 4, fixedAngle: 0, rotateAngle: 13, coolDown: 250),
                        new Shoot(15, 3, 20, projectileIndex: 7, predictive: 0.6, coolDown: 1400),
                        new Shoot(0, 10, projectileIndex: 5, fixedAngle: 18, coolDown: 2500),
                        new TossObject("Test Egg", 7, coolDown: 6000),
                        new Taunt(0.4, 9000, "Taste the rainbow, {PLAYER}!", "The master's colours are mine now!"),
                        new HpLessTransition(0.33, "Grow")
                    ),
                    new State("Grow",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1),
                        new ChangeSize(20, 180),
                        new Flash(0xFFFF0000, 0.3, 8),
                        new Taunt("THE CHICKEN... GROWS... STRONGER!"),
                        new Shoot(0, 6, fixedAngle: 0, projectileIndex: 2, coolDown: 90000, coolDownOffset: 1200),
                        new Spawn("TestChicken Small", 3, 1),
                        new TimedTransition(3000, "Frenzy")
                    ),
                    // below a third it hunts players down with its chicks around it
                    new State("Frenzy",
                        new Prioritize(
                            new Follow(0.4, 15, 3),
                            new Wander(0.3)
                        ),
                        new Shoot(12, 1, projectileIndex: 2, predictive: 0.3, coolDown: 2400),
                        new Shoot(15, 5, 10, projectileIndex: 8, coolDown: 1500),
                        new Shoot(15, 4, 12, projectileIndex: 0, predictive: 0.8, coolDown: 900, coolDownOffset: 450),
                        new Shoot(0, 8, projectileIndex: 5, fixedAngle: 0, rotateAngle: 9, coolDown: 1800),
                        new Reproduce("TestChicken Small", 12, 4, coolDown: 6000),
                        new Taunt(0.5, 8000, "BAAAWK!!!", "You will be roasted, {PLAYER}!")
                    )
                ),
                // everyone who did 1% of the damage: the sure drops, as Oryx 1
                new Threshold(0.01,
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Life", 0.15)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Dexterity", 0.45),
                    new ItemLoot("Potion of Speed", 0.45),
                    new ItemLoot("Potion of Vitality", 0.25),
                    new ItemLoot("Potion of Wisdom", 0.25),
                    new ItemLoot("Chicken Leg of Doom", 0.05),
                    new ItemLoot("Anatis Staff", 0.05),
                    new ItemLoot("Apocalypse Feather", 0.015),
                    new ItemLoot("Oryx Sanctuary Key", 0.04)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(11, ItemType.Weapon, 0.12),
                    new TierLoot(12, ItemType.Weapon, 0.06),
                    new TierLoot(5, ItemType.Ability, 0.1),
                    new TierLoot(6, ItemType.Ability, 0.03),
                    new TierLoot(12, ItemType.Armor, 0.1),
                    new TierLoot(13, ItemType.Armor, 0.04),
                    new TierLoot(5, ItemType.Ring, 0.08),
                    new TierLoot(6, ItemType.Ring, 0.03)
                )
            )
            .Init("TestChicken Small",
                new State(
                    new BossStats(1500, 25, 0.7, 0.2),
                    new State("Idle",
                        new Wander(0.2),
                        new PlayerWithinTransition(9, "Attack")
                    ),
                    new State("Attack",
                        new Prioritize(
                            new Follow(0.6, 12, 2),
                            new Wander(0.4)
                        ),
                        new Shoot(3, 1, projectileIndex: 0, coolDown: 600),
                        new Shoot(10, 2, 10, projectileIndex: 1, coolDown: 1600),
                        new Shoot(0, 6, projectileIndex: 3, fixedAngle: 0, coolDown: 2500)
                    )
                ),
                new TierLoot(7, ItemType.Weapon, 0.1),
                new TierLoot(7, ItemType.Armor, 0.1),
                new ItemLoot("Magic Potion", 0.03)
            )
            // an egg cracks for a few seconds once a player is near, then hatches a chick unless it's
            // broken first (it cracks on its own: the XML's alt textures 1 to 4)
            .Init("Test Egg",
                new State(
                    new BossStats(2500, 0, 1.0, 0.2),
                    new State("Nest",
                        new PlayerWithinTransition(7, "Incubate")
                    ),
                    new State("Incubate",
                        new SetAltTexture(0, 4, 1500),
                        new TimedTransition(7500, "Hatch")
                    ),
                    new State("Hatch",
                        new Spawn("TestChicken Small", 1, 1),
                        new Suicide()
                    )
                )
            );
    }
}
