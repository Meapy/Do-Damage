using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Belladonna = () => Behav()
        .Init("vlntns Botany Bella",
            new State(
                new DropPortalOnDeath("Realm Portal", 1.0),
                new ScaleHP2(20),
                new State("Idle",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new PlayerWithinTransition(8, "Wake")
                    ),
                new State("Wake",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Taunt("Oh! Visitors in my garden? Do mind the thorns."),
                    new TimedTransition(2000, "Bloom")
                    ),
                new State("Bloom",
                    new Shoot(12, count: 3, shootAngle: 10, projectileIndex: 0, coolDown: 1200),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 2, fixedAngle: 0, coolDown: 3000),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 22.5, coolDown: 3000, coolDownOffset: 1500),
                    new HpLessTransition(0.7, "Thorns")
                    ),
                new State("Thorns",
                    new Taunt("You trample my flowers!"),
                    new StayCloseToSpawn(0.4, 3),
                    new Wander(0.3),
                    new Shoot(10, count: 4, shootAngle: 90, projectileIndex: 4, fixedAngle: 0, coolDown: 2000),
                    new Shoot(10, count: 4, shootAngle: 90, projectileIndex: 6, fixedAngle: 45, coolDown: 2000, coolDownOffset: 1000),
                    new Shoot(12, count: 1, projectileIndex: 11, predictive: 0.3, coolDown: 600),
                    new HpLessTransition(0.4, "Wilt")
                    ),
                new State("Wilt",
                    new Taunt("If my garden must wilt, you shall wilt with it!"),
                    new Flash(0xFF66CC, 0.5, 6),
                    new ReturnToSpawn(1),
                    new Shoot(0, count: 16, shootAngle: 22.5, projectileIndex: 8, fixedAngle: 0, coolDown: 4000),
                    new Shoot(12, count: 5, shootAngle: 12, projectileIndex: 3, coolDown: 1500),
                    new Shoot(12, count: 1, projectileIndex: 10, coolDown: 5000),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 13, fixedAngle: 0, coolDown: 1000),
                    new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 14, fixedAngle: 22.5, coolDown: 1000, coolDownOffset: 500)
                    )
                ),
            new Threshold(0.01,
                new ItemLoot("Potion of Defense", 1),
                new ItemLoot("Potion of Life", 0.3),
                new ItemLoot("Cupid's Bow", 0.01),
                new ItemLoot("Valentine's Mystery Item", 0.05)
                )
            );
    }
}
