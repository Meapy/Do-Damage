using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // The Void, opened in the Lost Halls with Malus' Vial of Pure Darkness (map:
        // tools/build_cult.py). A round platform in a sea of Pure Evil; the Void Controller at its
        // centre wakes the Void Entity when a player comes near. It fights in four phases, and
        // twice (at three quarters and at half its health) throws its projections onto the four
        // landmarks round the platform, from where they fire inwards for half a minute while the
        // Entity holds the centre. Its shades and fragments come and go throughout.
        private _ VoidEntity = () => Behav()
            .Init("LH Void Controller",
                new State(
                    new State("Idle",
                        new PlayerWithinTransition(18, "Wake")
                        ),
                    new State("Wake",
                        new Taunt(true, "Something stirs in the darkness..."),
                        new TimedTransition(3000, "Summon")
                        ),
                    new State("Summon",
                        // a boss of its own: experience and loot, which a behaviour's spawn has not
                        new Spawn("LH Void Entity", maxChildren: 1, initialSpawn: 1, coolDown: 9999999, givesNoXp: false, spawnedByBehav: false),
                        new TimedTransition(1000, "Wait")
                        ),
                    new State("Wait",
                        new EntityNotExistsTransition("LH Void Entity", 60, "Gone")
                        ),
                    new State("Gone",
                        new Decay(0)
                        )
                    )
                )
            .Init("LH Void Entity",
                new State(
                    new ScaleHP2(20),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120),
                    new State("Emerge",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        // rises out of the dark, growing
                        new SetAltTexture(18, 29, 250),
                        new Taunt(true, "You dare disturb the Void? Then be consumed by it!"),
                        new TimedTransition(3200, "Phase 1")
                        ),
                    new State("Phase 1",
                        new SetAltTexture(0),
                        new StayCloseToSpawn(0.4, 6),
                        new Wander(0.25),
                        new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 9, coolDown: 900),
                        new Shoot(16, 3, 20, 3, coolDown: 1800, coolDownOffset: 600),
                        new Shoot(0, 8, 45, 4, fixedAngle: 22.5, coolDown: 3000, coolDownOffset: 1500),
                        new Spawn("LH Void Fragment", maxChildren: 3, initialSpawn: 0, coolDown: 12000),
                        new HpLessTransition(0.75, "Split 1")
                        ),
                    new State("Split 1",
                        [new Taunt(true, "The Void surrounds you!"),
                        new Flash(0x4B0082, 0.4, 6),
                        new ReturnToSpawn(1, 0.2),
                        .. Projections(),
                        new Shoot(0, 4, 90, 8, fixedAngle: 45, rotateAngle: 15, coolDown: 1500),
                        new TimedTransition(25000, "Phase 2")]
                        ),
                    new State("Phase 2",
                        new StayCloseToSpawn(0.5, 7),
                        new Prioritize(
                            new Follow(0.35, 16, 6),
                            new Wander(0.3)
                            ),
                        new Shoot(0, 10, 36, 10, fixedAngle: 0, rotateAngle: 12, coolDown: 2200),
                        new Shoot(16, 5, 15, 9, coolDown: 1700, coolDownOffset: 800),
                        new Shoot(0, 4, 90, 14, fixedAngle: 0, rotateAngle: 20, coolDown: 1200),
                        new Spawn("LH Void Shade", maxChildren: 2, initialSpawn: 0, coolDown: 15000),
                        new HpLessTransition(0.5, "Split 2")
                        ),
                    new State("Split 2",
                        [new Taunt(true, "There is no escape from nothing."),
                        new Flash(0x4B0082, 0.4, 6),
                        new ReturnToSpawn(1, 0.2),
                        .. Projections(),
                        new Shoot(0, 6, 60, 15, fixedAngle: 0, rotateAngle: -12, coolDown: 1600),
                        new TimedTransition(25000, "Phase 3")]
                        ),
                    new State("Phase 3",
                        new StayCloseToSpawn(0.5, 7),
                        new Wander(0.3),
                        new Shoot(0, 12, 30, 6, fixedAngle: 0, rotateAngle: 7, coolDown: 1400),
                        new Shoot(16, 3, 25, 13, coolDown: 1500, coolDownOffset: 700),
                        new Shoot(16, 1, projectileIndex: 19, predictive: 0.5, coolDown: 2000),
                        new Spawn("LH Greater Void Shade", maxChildren: 1, initialSpawn: 0, coolDown: 20000),
                        new HpLessTransition(0.25, "Phase 4")
                        ),
                    new State("Phase 4",
                        new Taunt(true, "THE VOID CONSUMES ALL!"),
                        new Flash(0xFF00FF, 0.3, 8),
                        new ReturnToSpawn(0.6, 0.5),
                        new Shoot(0, 6, 60, 18, fixedAngle: 0, rotateAngle: 20, coolDown: 3000),
                        new Shoot(0, 8, 45, 12, fixedAngle: 22.5, rotateAngle: -9, coolDown: 1300),
                        new Shoot(16, 4, 20, 20, coolDown: 1600, coolDownOffset: 800),
                        new Shoot(16, 1, projectileIndex: 11, coolDown: 4000, coolDownOffset: 2000),
                        new Spawn("LH Void Fragment", maxChildren: 3, initialSpawn: 0, coolDown: 10000)
                        )
                    ),
                new Threshold(0.005,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 1),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Vitality", 0.5),
                    new ItemLoot("Potion of Wisdom", 0.5),
                    new TierLoot(13, ItemType.Weapon, 0.3),
                    new TierLoot(14, ItemType.Weapon, 0.1),
                    new TierLoot(14, ItemType.Armor, 0.15),
                    new TierLoot(6, ItemType.Ability, 0.2),
                    new TierLoot(6, ItemType.Ring, 0.2)
                    ),
                new Threshold(0.02,
                    new ItemLoot("Bow of the Void", 0.004),
                    new ItemLoot("Quiver of the Shadows", 0.004),
                    new ItemLoot("Armor of Nil", 0.004),
                    new ItemLoot("Sourcestone", 0.004)
                    )
                )
            // its projections on the four landmarks: never hit, they fire at the platform
            .Init("LH Void Entity N", VoidProjection())
            .Init("LH Void Entity E", VoidProjection())
            .Init("LH Void Entity S", VoidProjection())
            .Init("LH Void Entity W", VoidProjection())

            // the Entity's shades: they hunt players and curse them
            .Init("LH Void Fragment",
                new State(
                    new Prioritize(
                        new Follow(1, 16, 1),
                        new Wander(0.5)
                        ),
                    new Shoot(3, 1, projectileIndex: 0, coolDown: 600)
                    )
                )
            .Init("LH Void Shade",
                new State(
                    new Prioritize(
                        new Follow(0.6, 16, 3),
                        new Wander(0.4)
                        ),
                    new Shoot(8, 3, 20, 0, coolDown: 1200)
                    )
                )
            .Init("LH Greater Void Shade",
                new State(
                    new Prioritize(
                        new Orbit(0.5, 6, 20, "LH Void Entity", speedVariance: 0.1, radiusVariance: 0.5),
                        new Wander(0.3)
                        ),
                    new Shoot(14, 2, 15, 0, predictive: 0.4, coolDown: 1400)
                    )
                );

        // the four projections, thrown onto the landmarks 18.5 tiles out once the Entity is back
        // at the centre
        private static IStateChildren[] Projections() =>
            [
                new TossObject("LH Void Entity N", 18.5, 270, coolDown: 9999999, coolDownOffset: 2000),
                new TossObject("LH Void Entity E", 18.5, 0, coolDown: 9999999, coolDownOffset: 2000),
                new TossObject("LH Void Entity S", 18.5, 90, coolDown: 9999999, coolDownOffset: 2000),
                new TossObject("LH Void Entity W", 18.5, 180, coolDown: 9999999, coolDownOffset: 2000)
            ];

        private static State VoidProjection() =>
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new SetAltTexture(6),
                new Shoot(30, 1, projectileIndex: 1, predictive: 0.3, coolDown: 1800),
                new Shoot(30, 3, 12, 2, coolDown: 2600, coolDownOffset: 900),
                new Shoot(30, 1, projectileIndex: 0, coolDown: 6000, coolDownOffset: 3000),
                new Decay(25000)
                );
    }
}
