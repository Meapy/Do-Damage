using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ CnidarianReef = () => Behav()

            .Init("CR Royal Cnidarian",
                new State(
                    new DropPortalOnDeath("Realm Portal", 1.0),
                    new State("Invunerable",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                        new TimedTransition(400, "PreIdle1")
                    ),
                    new State("PreIdle1",
                        new SetAltTexture(0),
                        new TimedTransition(400, "PreIdle2")
                    ),
                    new State("PreIdle2",
                        new SetAltTexture(1),
                        new TimedTransition(400, "PreIdle3")
                    ),
                    new State("PreIdle3",
                        new SetAltTexture(2),
                        new TimedTransition(400, "PreIdle4")
                    ),
                    new State("PreIdle4",
                        new SetAltTexture(3),
                        new TimedTransition(400, "PreIdle5")
                    ),
                    new State("PreIdle5",
                        new SetAltTexture(4),
                        new TimedTransition(400, "PreIdle6")
                    ),
                    new State("PreIdle6",
                        new SetAltTexture(0),
                        new TimedTransition(400, "PreIdle7")
                    ),
                    new State("PreIdle7",
                        new SetAltTexture(1),
                        new TimedTransition(400, "PreIdle8")
                    ),
                    new State("PreIdle8",
                        new SetAltTexture(2),
                        new TimedTransition(400, "PreIdle9")
                    ),
                    new State("PreIdle9",
                        new SetAltTexture(3),
                        new TimedTransition(400, "PreIdle10")
                    ),
                    new State("PreIdle10",
                        new SetAltTexture(4),
                        new TimedTransition(400, "Idle1")
                    ),
                    new State("Idle1",
                        new SetAltTexture(0),
                        new TimedTransition(400, "Idle2")
                    ),
                    new State("Idle2",
                        new SetAltTexture(1),
                        new TimedTransition(400, "Idle3")
                    ),
                    new State("Idle3",
                        new SetAltTexture(2),
                        new TimedTransition(400, "Idle4")
                    ),
                    new State("Idle4",
                        new SetAltTexture(3),
                        new TimedTransition(400, "Idle5")
                    ),
                    new State("Idle5",
                        new SetAltTexture(4),
                        new TimedTransition(400, "Idle6")
                    ),
                    new State("Idle6",
                        new SetAltTexture(0),
                        new TimedTransition(400, "Idle7")
                    ),
                    new State("Idle7",
                        new SetAltTexture(1),
                        new TimedTransition(400, "Idle8")
                    ),
                    new State("Idle8",
                        new SetAltTexture(2),
                        new TimedTransition(400, "Idle9")
                    ),
                    new State("Idle9",
                        new SetAltTexture(3),
                        new TimedTransition(400, "Idle10")
                    ),
                    new State("Idle10",
                        new SetAltTexture(4),
                        new TimedTransition(400, "FlashIdle1")
                    ),
                    new State("FlashIdle1",
                        new SetAltTexture(0),
                        new Flash(0xFF4F4F, 0.5, 1),
                        new TimedTransition(500, "FlashIdle2")
                    ),
                    new State("FlashIdle2",
                        new SetAltTexture(1),
                        new Flash(0xFF4F4F, 0.5, 1),
                        new TimedTransition(500, "FlashIdle3")
                    ),
                    new State("FlashIdle3",
                        new SetAltTexture(2),
                        new Flash(0xFF4F4F, 0.5, 1),
                        new TimedTransition(500, "FlashIdle4")
                    ),
                    new State("FlashIdle4",
                        new SetAltTexture(3),
                        new Flash(0xFF4F4F, 0.5, 1),
                        new TimedTransition(500, "FlashIdle5")
                    ),
                    new State("FlashIdle5",
                        new SetAltTexture(4),
                        new Flash(0xFF4F4F, 0.5, 1),
                        new TimedTransition(500, "First Phase")
                    ),
                    // RealmEye's six phases: invulnerable while Gold Cnidarians (1, then 3, then 5)
                    // circle the reef; open in between, the minions around it changing what they do
                    new State("First Phase",
                        new RemoveConditionalEffect(ConditionEffectIndex.Invincible),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new SetAltTexture(0, 4, 500, true),
                        new Spawn("CR Gold Cnidarian", maxChildren: 1, initialSpawn: 1),
                        new Spawn("CR Blue Cnidarian", maxChildren: 3, initialSpawn: 1),
                        new Spawn("CR Green Cnidarian", maxChildren: 3, initialSpawn: 1),
                        new Spawn("CR Pink Cnidarian", maxChildren: 3, initialSpawn: 1),
                        new Order(30, "CR Blue Cnidarian", "Orbit"),
                        new Order(30, "CR Green Cnidarian", "Orbit"),
                        new Order(30, "CR Pink Cnidarian", "Orbit"),
                        new Shoot(10, count: 8, shootAngle: 45, projectileIndex: 1, coolDown: 1600),
                        new Shoot(10, count: 1, projectileIndex: 3, coolDown: 900),
                        new EntityNotExistsTransition("CR Gold Cnidarian", 50, "Second Phase")
                    ),
                    // open: shockwaves, pink bombs and a bubble shotgun with the minions on the outer
                    // ring, or bubble waves, weakening bombs and bolts with the minions as a wall
                    new State("Second Phase",
                        new SetAltTexture(0),
                        new HpLessTransition(0.67, "Third Phase"),
                        new State("Shockwaves",
                            new Order(30, "CR Blue Cnidarian", "Outer"),
                            new Order(30, "CR Green Cnidarian", "Outer"),
                            new Order(30, "CR Pink Cnidarian", "Outer"),
                            new Shoot(12, count: 8, shootAngle: 45, projectileIndex: 2, fixedAngle: 0, rotateAngle: 11, coolDown: 2000),
                            new Shoot(12, count: 3, shootAngle: 10, projectileIndex: 4, coolDown: 1500),
                            PinkBombs(6, 2500),
                            new TimedTransition(8000, "Wall")
                        ),
                        new State("Wall",
                            new Order(30, "CR Blue Cnidarian", "Wall"),
                            new Order(30, "CR Green Cnidarian", "Wall"),
                            new Order(30, "CR Pink Cnidarian", "Wall"),
                            new Shoot(12, count: 5, shootAngle: 12, projectileIndex: 2, coolDown: 1500),
                            new Shoot(12, count: 1, projectileIndex: 3, coolDown: 400),
                            new Grenade(3, 120, range: 8, coolDown: 2000, effect: ConditionEffectIndex.Weak, effectDuration: 3000, color: 0xA0A0FF),
                            new TimedTransition(8000, "Shockwaves")
                        )
                    ),
                    new State("Third Phase",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new SetAltTexture(0, 4, 500, true),
                        new Spawn("CR Gold Cnidarian", maxChildren: 3, initialSpawn: 1),
                        new Spawn("CR Blue Cnidarian", maxChildren: 2, initialSpawn: 1),
                        new Spawn("CR Green Cnidarian", maxChildren: 2, initialSpawn: 1),
                        new Spawn("CR Pink Cnidarian", maxChildren: 2, initialSpawn: 1),
                        new Order(30, "CR Blue Cnidarian", "Orbit"),
                        new Order(30, "CR Green Cnidarian", "Orbit"),
                        new Order(30, "CR Pink Cnidarian", "Orbit"),
                        new Shoot(10, count: 8, shootAngle: 45, projectileIndex: 1, coolDown: 1600),
                        new Shoot(10, count: 1, projectileIndex: 3, coolDown: 900),
                        new Shoot(12, count: 5, shootAngle: 10, projectileIndex: 4, coolDown: 1800),
                        new EntityNotExistsTransition("CR Gold Cnidarian", 50, "Fourth Phase")
                    ),
                    // open: a slowly turning five-way laser and dazing rings toward the vents
                    new State("Fourth Phase",
                        new SetAltTexture(0),
                        new Order(30, "CR Blue Cnidarian", "Closer"),
                        new Order(30, "CR Green Cnidarian", "Closer"),
                        new Order(30, "CR Pink Cnidarian", "Closer"),
                        new Shoot(0, count: 5, shootAngle: 72, projectileIndex: 4, fixedAngle: 0, rotateAngle: 6, coolDown: 500),
                        new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, coolDown: 1800),
                        PinkBombs(8, 3000, 9),
                        new HpLessTransition(0.17, "Fifth Phase")
                    ),
                    new State("Fifth Phase",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new SetAltTexture(0, 4, 500, true),
                        new Spawn("CR Gold Cnidarian", maxChildren: 5, initialSpawn: 1),
                        new Spawn("CR Blue Cnidarian", maxChildren: 2, initialSpawn: 1),
                        new Spawn("CR Green Cnidarian", maxChildren: 2, initialSpawn: 1),
                        new Spawn("CR Pink Cnidarian", maxChildren: 2, initialSpawn: 1),
                        new Order(30, "CR Blue Cnidarian", "Orbit"),
                        new Order(30, "CR Green Cnidarian", "Orbit"),
                        new Order(30, "CR Pink Cnidarian", "Orbit"),
                        new Shoot(0, count: 5, shootAngle: 72, projectileIndex: 4, fixedAngle: 0, rotateAngle: 6, coolDown: 500),
                        new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, coolDown: 1800),
                        new EntityNotExistsTransition("CR Gold Cnidarian", 50, "Sixth Phase")
                    ),
                    // open to the end: it chases, its minions can all be shot and more keep coming
                    new State("Sixth Phase",
                        new SetAltTexture(0),
                        new Order(30, "CR Blue Cnidarian", "Free"),
                        new Order(30, "CR Green Cnidarian", "Free"),
                        new Order(30, "CR Pink Cnidarian", "Free"),
                        new Prioritize(
                            new Follow(0.5, 15, 2),
                            new Wander(0.5)
                            ),
                        new StayCloseToSpawn(0.8, 8),
                        new Shoot(12, count: 1, projectileIndex: 2, predictive: 0.5, coolDown: 900),
                        new Shoot(12, count: 1, projectileIndex: 3, coolDown: 500),
                        new Grenade(4, 150, range: 8, coolDown: 2500, effect: ConditionEffectIndex.Weak, effectDuration: 3000, color: 0xA0A0FF),
                        new Spawn("CR Blue Cnidarian", maxChildren: 4, initialSpawn: 0, coolDown: 6000, spawnedByBehav: false),
                        new Spawn("CR Green Cnidarian", maxChildren: 4, initialSpawn: 0, coolDown: 6000, spawnedByBehav: false),
                        new Spawn("CR Pink Cnidarian", maxChildren: 4, initialSpawn: 0, coolDown: 6000, spawnedByBehav: false),
                        new HpLessTransition(0.02, "Die")
                    ),
                    // at the end it stops, sends its minions away and dies
                    new State("Die",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFFFFFF, 0.2, 8),
                        new RemoveEntity(40, "CR Blue Cnidarian"),
                        new RemoveEntity(40, "CR Green Cnidarian"),
                        new RemoveEntity(40, "CR Pink Cnidarian"),
                        new TimedTransition(1500, "Suicide")
                    ),
                    new State("Suicide",
                        new Suicide()
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Bottled Medusozoan", 0.03),
                    new ItemLoot("Mask of Cnidaria", 0.03),
                    new ItemLoot("Cnidaria Rod", 0.06),
                    new ItemLoot("Large Blue Wave Cloth", 0.5),
                    new ItemLoot("Small Blue Wave Cloth", 0.5),
                    new ItemLoot("Yellow Clothing Dye", 0.5),
                    new ItemLoot("Yellow Accessory Dye", 0.5),
                    new ItemLoot("Greater Potion of Attack", 0.5),
                    new ItemLoot("Greater Potion of Defense", 0.5),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Potion of Vitality", 0.08),
                    new ItemLoot("Tincture of Speed", 0.3),
                    new ItemLoot("Effusion of Speed", 0.1),
                    new ItemLoot("Reef Key", 0.1, 0, 0.03),
                    new TierLoot(10, ItemType.Weapon, 0.15),
                    new TierLoot(11, ItemType.Weapon, 0.08),
                    new TierLoot(10, ItemType.Armor, 0.15),
                    new TierLoot(11, ItemType.Armor, 0.08),
                    new TierLoot(5, ItemType.Ring, 0.1)
                ),
                new ItemLoot("Jelly Generator", 0.03)
            )
            .Init("CR Blue Cnidarian", Cnidarian(), new ItemLoot("Potion of Defense", 0.04))
            .Init("CR Green Cnidarian", Cnidarian(), new ItemLoot("Potion of Attack", 0.04))
            .Init("CR Pink Cnidarian", Cnidarian(), new ItemLoot("Potion of Mana", 0.04))
            // the gold ones are always open: they're the ones to shoot down
            .Init("CR Gold Cnidarian",
                new State(
                    new Orbit(0.8, 10, 15, "CR Royal Cnidarian"),
                    new Shoot(3.5, count: 1, projectileIndex: 1, coolDown: 1000)
                ),
                new ItemLoot("Potion of Speed", 0.05)
            );

        // a ring of `count` pink bombs lobbed out `range` tiles, sickening and silencing
        private static State PinkBombs(int count, int coolDown, double range = 6)
        {
            var bombs = new System.Collections.Generic.List<IStateChildren>();
            for (var i = 0; i < count; i++)
                bombs.Add(new Grenade(2, 100, range: range, fixedAngle: i * 360.0 / count, coolDown: coolDown, effect: ConditionEffectIndex.Silenced, effectDuration: 3000, color: 0xFF60C0));
            return new State(bombs.ToArray());
        }

        // the coloured minions: circling far out and only stinging those who come close; firing in on
        // the outer ring; a still wall around the boss; closer in; or, at the end, open to be shot down
        private static State Cnidarian() =>
            new State(
                new State("Orbit",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(0.6, 10, 15, "CR Royal Cnidarian"),
                    new Shoot(3, count: 1, projectileIndex: 1, coolDown: 1000)
                ),
                new State("Outer",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(0.6, 11, 15, "CR Royal Cnidarian"),
                    new Shoot(5, count: 1, projectileIndex: 1, coolDown: 700)
                ),
                new State("Wall",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(1, 5, 15, "CR Royal Cnidarian")
                ),
                new State("Closer",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(0.8, 8, 15, "CR Royal Cnidarian"),
                    new Shoot(5, count: 1, projectileIndex: 1, coolDown: 700)
                ),
                new State("Free",
                    new Prioritize(
                        new Orbit(0.8, 8, 15, "CR Royal Cnidarian"),
                        new Wander(0.5)
                        ),
                    new Shoot(5, count: 1, projectileIndex: 1, coolDown: 700)
                )
            );
    }
}
