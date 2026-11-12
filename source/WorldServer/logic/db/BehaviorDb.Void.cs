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
        private _ VoidEntity = () => VoidBehaviours(Behav(), false);
        // The Elder Void, opened in the Elder Lost Halls: the Elder Void Entity, which has a last
        // phase of its own, the Singularity.
        private _ ElderVoidEntity = () => VoidBehaviours(Behav(), true);

        private static Ctor VoidBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            return db
            .Init(E("LH Void Controller"),
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
                        new Spawn(N("LH Void Entity"), maxChildren: 1, initialSpawn: 1, coolDown: 9999999, givesNoXp: false, spawnedByBehav: false),
                        new TimedTransition(1000, "Wait")
                        ),
                    new State("Wait",
                        new EntityNotExistsTransition(N("LH Void Entity"), 60, "Gone")
                        ),
                    new State("Gone",
                        new Decay(0)
                        )
                    )
                )
            .Init(E("LH Void Entity"),
                new State(
                    // the Elder version gains less health per extra player: its base is far higher
                    new ScaleHP2(elder ? 10 : 20),
                    new DropPortalOnDeath("Realm Portal", 1.0, timeout: 120),
                    // the regular Entity leads on to the Elder Lost Halls half the time
                    new DropPortalOnDeath("Elder Lost Halls Portal", elder ? 0 : 0.5, xAdjustment: 3, timeout: 120),
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
                        new Shoot(0, 6, 60, 0, fixedAngle: 0, rotateAngle: 9, coolDown: Cd(900)),
                        new Shoot(16, 3, 20, 3, coolDown: Cd(1800), coolDownOffset: 600),
                        new Shoot(0, 8, 45, 4, fixedAngle: 22.5, coolDown: Cd(3000), coolDownOffset: 1500),
                        new Spawn(N("LH Void Fragment"), maxChildren: 3, initialSpawn: 0, coolDown: 12000),
                        new HpLessTransition(0.75, "Split 1")
                        ),
                    new State("Split 1",
                        [new Taunt(true, "The Void surrounds you!"),
                        new Flash(0x4B0082, 0.4, 6),
                        new ReturnToSpawn(1, 0.2),
                        .. Projections(N),
                        new Shoot(0, 4, 90, 8, fixedAngle: 45, rotateAngle: 15, coolDown: Cd(1500)),
                        new TimedTransition(25000, "Phase 2")]
                        ),
                    new State("Phase 2",
                        new StayCloseToSpawn(0.5, 7),
                        new Prioritize(
                            new Follow(0.35, 16, 6),
                            new Wander(0.3)
                            ),
                        new Shoot(0, 10, 36, 10, fixedAngle: 0, rotateAngle: 12, coolDown: Cd(2200)),
                        new Shoot(16, 5, 15, 9, coolDown: Cd(1700), coolDownOffset: 800),
                        new Shoot(0, 4, 90, 14, fixedAngle: 0, rotateAngle: 20, coolDown: Cd(1200)),
                        new Spawn(N("LH Void Shade"), maxChildren: 2, initialSpawn: 0, coolDown: 15000),
                        new HpLessTransition(0.5, "Split 2")
                        ),
                    new State("Split 2",
                        [new Taunt(true, "There is no escape from nothing."),
                        new Flash(0x4B0082, 0.4, 6),
                        new ReturnToSpawn(1, 0.2),
                        .. Projections(N),
                        new Shoot(0, 6, 60, 15, fixedAngle: 0, rotateAngle: -12, coolDown: Cd(1600)),
                        new TimedTransition(25000, "Phase 3")]
                        ),
                    new State("Phase 3",
                        new StayCloseToSpawn(0.5, 7),
                        new Wander(0.3),
                        new Shoot(0, 12, 30, 6, fixedAngle: 0, rotateAngle: 7, coolDown: Cd(1400)),
                        new Shoot(16, 3, 25, 13, coolDown: Cd(1500), coolDownOffset: 700),
                        new Shoot(16, 1, projectileIndex: 19, predictive: 0.5, coolDown: Cd(2000)),
                        new Spawn(N("LH Greater Void Shade"), maxChildren: 1, initialSpawn: 0, coolDown: 20000),
                        new HpLessTransition(0.25, "Phase 4")
                        ),
                    new State("Phase 4",
                        new Taunt(true, "THE VOID CONSUMES ALL!"),
                        new Flash(0xFF00FF, 0.3, 8),
                        new ReturnToSpawn(0.6, 0.5),
                        new Shoot(0, 6, 60, 18, fixedAngle: 0, rotateAngle: 20, coolDown: Cd(3000)),
                        new Shoot(0, 8, 45, 12, fixedAngle: 22.5, rotateAngle: -9, coolDown: Cd(1300)),
                        new Shoot(16, 4, 20, 20, coolDown: Cd(1600), coolDownOffset: 800),
                        new Shoot(16, 1, projectileIndex: 11, coolDown: Cd(4000), coolDownOffset: 2000),
                        new Spawn(N("LH Void Fragment"), maxChildren: 3, initialSpawn: 0, coolDown: 10000),
                        elder ? new HpLessTransition(0.1, "Singularity") : null
                        ),
                    // Elder only, its last tenth: back at the centre it throws its projections out a
                    // third time and turns two spirals against each other, its laser on whoever
                    // stands still
                    new State("Singularity",
                        [new Taunt(true, "NOTHING REMAINS. NOT EVEN YOU."),
                        new Flash(0x000000, 0.4, 10),
                        new ReturnToSpawn(1, 0.2),
                        .. Projections(N),
                        new Shoot(0, 6, 60, 12, fixedAngle: 0, rotateAngle: 11, coolDown: 700),
                        new Shoot(0, 6, 60, 6, fixedAngle: 30, rotateAngle: -8, coolDown: 900),
                        new Shoot(16, 1, projectileIndex: 19, predictive: 0.5, coolDown: 1500),
                        new Spawn(N("LH Greater Void Shade"), maxChildren: 2, initialSpawn: 0, coolDown: 12000)]
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
                    new ItemLoot("Bow of the Void", elder ? 0.008 : 0.004),
                    new ItemLoot("Quiver of the Shadows", elder ? 0.008 : 0.004),
                    new ItemLoot("Armor of Nil", elder ? 0.008 : 0.004),
                    new ItemLoot("Sourcestone", elder ? 0.008 : 0.004)
                    ),
                ElderLoot(0.02,
                    new ItemLoot("The Hollow Sourcestone", 0.01),
                    new ItemLoot("Potion of Max Life", 0.05),
                    new ItemLoot("Elder Lost Halls Key", 0.02),
                    new TierLoot(14, ItemType.Weapon, 0.1),
                    new TierLoot(15, ItemType.Armor, 0.05),
                    new TierLoot(7, ItemType.Ability, 0.03),
                    new TierLoot(7, ItemType.Ring, 0.03)
                    )
                )
            // its projections on the four landmarks: never hit, they fire at the platform
            .Init(E("LH Void Entity N"), VoidProjection())
            .Init(E("LH Void Entity E"), VoidProjection())
            .Init(E("LH Void Entity S"), VoidProjection())
            .Init(E("LH Void Entity W"), VoidProjection())

            // the Entity's shades: they hunt players and curse them
            .Init(E("LH Void Fragment"),
                new State(
                    new Prioritize(
                        new Follow(1, 16, 1),
                        new Wander(0.5)
                        ),
                    new Shoot(3, 1, projectileIndex: 0, coolDown: Cd(600))
                    )
                )
            .Init(E("LH Void Shade"),
                new State(
                    new Prioritize(
                        new Follow(0.6, 16, 3),
                        new Wander(0.4)
                        ),
                    new Shoot(8, 3, 20, 0, coolDown: Cd(1200))
                    )
                )
            .Init(E("LH Greater Void Shade"),
                new State(
                    new Prioritize(
                        new Orbit(0.5, 6, 20, N("LH Void Entity"), speedVariance: 0.1, radiusVariance: 0.5),
                        new Wander(0.3)
                        ),
                    new Shoot(14, 2, 15, 0, predictive: 0.4, coolDown: Cd(1400))
                    )
                );
        }

        // the four projections, thrown onto the landmarks 18.5 tiles out once the Entity is back
        // at the centre
        private static IStateChildren[] Projections(System.Func<string, string> N) =>
            [
                new TossObject(N("LH Void Entity N"), 18.5, 270, coolDown: 9999999, coolDownOffset: 2000),
                new TossObject(N("LH Void Entity E"), 18.5, 0, coolDown: 9999999, coolDownOffset: 2000),
                new TossObject(N("LH Void Entity S"), 18.5, 90, coolDown: 9999999, coolDownOffset: 2000),
                new TossObject(N("LH Void Entity W"), 18.5, 180, coolDown: 9999999, coolDownOffset: 2000)
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
