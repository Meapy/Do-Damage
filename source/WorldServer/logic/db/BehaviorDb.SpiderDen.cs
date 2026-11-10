using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ SpiderDen = () => SpiderDenBehaviours(Behav(), false);
        private _ ElderSpiderDen = () => SpiderDenBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs)
        private static Ctor SpiderDenBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            return db
            .Init(E("Arachna the Spider Queen"),
                new State(
                    // the regular dungeon leads to its Elder version half the time
                    new DropPortalOnDeath("Elder Spider Den Portal", elder ? 0 : 0.5, xAdjustment: 3),
                    new State("idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(12, "WEB!")
                        ),
                    new State("WEB!",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TossObject(N("Arachna Web Spoke 7"), 6, 0, 100000),
                        new TossObject(N("Arachna Web Spoke 8"), 6, 120, 100000),
                        new TossObject(N("Arachna Web Spoke 9"), 6, 240, 100000),
                        new TossObject(N("Arachna Web Spoke 1"), 10, 0, 100000),
                        new TossObject(N("Arachna Web Spoke 2"), 10, 60, 100000),
                        new TossObject(N("Arachna Web Spoke 3"), 10, 120, 100000),
                        new TossObject(N("Arachna Web Spoke 4"), 10, 180, 100000),
                        new TossObject(N("Arachna Web Spoke 5"), 10, 240, 100000),
                        new TossObject(N("Arachna Web Spoke 6"), 10, 300, 100000),
                        new TimedTransition(2000, "attack")
                        ),
                    new State("attack",
                        new Wander(.5),
                        new Shoot(3000, count: 12, projectileIndex: 0, fixedAngle: 22.5f),
                        new Shoot(10, 1, 0, defaultAngle: 0, angleOffset: 0, projectileIndex: 0, predictive: 1,
                            coolDown: Cd(1000), coolDownOffset: 0),
                        new Shoot(10, 1, 0, defaultAngle: 0, angleOffset: 0, projectileIndex: 1, predictive: 1,
                            coolDown: Cd(2000), coolDownOffset: 0)
                        )
                    ),
                new ItemLoot("Golden Dagger", 0.2),
                new ItemLoot("Spider's Eye Ring", 0.2),
                new ItemLoot("Poison Fang Dagger", 0.2),
                new Threshold(0.32,
                    new ItemLoot("Healing Ichor", 1)
                    ), Elder("Arachna the Spider Queen"),
                // the Elder dungeon's Potion of Max
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Defense", 0.05),
                    new ItemLoot("Elder Spider Den Key", 0.01)))
            .Init(E("Arachna Web Spoke 1"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 120, fixedAngle: 120, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 180, fixedAngle: 180, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 240, fixedAngle: 240, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 1"))
            .Init(E("Arachna Web Spoke 2"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 240, fixedAngle: 240, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 180, fixedAngle: 180, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 300, fixedAngle: 300, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 2"))
            .Init(E("Arachna Web Spoke 3"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 300, fixedAngle: 300, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 240, fixedAngle: 240, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 0, fixedAngle: 0, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 3"))
            .Init(E("Arachna Web Spoke 4"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 0, fixedAngle: 0, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 60, fixedAngle: 60, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 300, fixedAngle: 300, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 4"))
            .Init(E("Arachna Web Spoke 5"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 60, fixedAngle: 60, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 0, fixedAngle: 0, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 120, fixedAngle: 120, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 5"))
            .Init(E("Arachna Web Spoke 6"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 120, fixedAngle: 120, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 60, fixedAngle: 60, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 180, fixedAngle: 180, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 6"))
            .Init(E("Arachna Web Spoke 7"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 180, fixedAngle: 180, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 120, fixedAngle: 120, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 240, fixedAngle: 240, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 7"))
            .Init(E("Arachna Web Spoke 8"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 360, fixedAngle: 360, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 240, fixedAngle: 240, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 300, fixedAngle: 300, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 8"))
            .Init(E("Arachna Web Spoke 9"),
                new State(
                    new State(":D",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 0, fixedAngle: 0, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 60, fixedAngle: 60, coolDown: Cd(5)),
                        new Shoot(6, projectileIndex: 0, count: 1, shootAngle: 120, fixedAngle: 120, coolDown: Cd(5))
                        )
                    ), Elder("Arachna Web Spoke 9"))
            .Init(E("Black Den Spider"),
                new State(
                    new State("idle",
                        new Wander(0.5),
                        new Charge(2, 20f, 2000),
                        new Shoot(10, 1, 0, defaultAngle: 0, angleOffset: 0, projectileIndex: 0, predictive: 1,
                            coolDown: Cd(500), coolDownOffset: 0)
                        )
                    ),
                new ItemLoot("Healing Ichor", 0.2), Elder("Black Den Spider"))
            .Init(E("Black Spotted Den Spider"),
                new State(
                    new State("idle",
                        new Wander(0.5),
                        new Charge(2, 40f, 2000),
                        new Shoot(10, 1, 0, defaultAngle: 0, angleOffset: 0, projectileIndex: 0, predictive: 1,
                            coolDown: Cd(500), coolDownOffset: 0)
                        )
                    ),
                new ItemLoot("Healing Ichor", 0.2), Elder("Black Spotted Den Spider"))
            .Init(E("Brown Den Spider"),
                new State(
                    new State("idle",
                        new Wander(0.5),
                        new Follow(1, 0.3, 0),
                        new Shoot(10, 3, 20, angleOffset: 0 / 3, projectileIndex: 0, coolDown: Cd(500))
                        )
                    ),
                new ItemLoot("Healing Ichor", 0.2), Elder("Brown Den Spider"))
            .Init(E("Green Den Spider Hatchling"),
                new State(
                    new State("idle",
                        new Wander(0.5),
                        new Follow(0.8, 0.8, 0),
                        new Shoot(10, 1, 0, defaultAngle: 0, angleOffset: 0, projectileIndex: 0, predictive: 1,
                            coolDown: Cd(1000), coolDownOffset: 0)
                        )
                    ), Elder("Green Den Spider Hatchling"))
            .Init(E("Spider Egg Sac"),
                new State(
                    new TransformOnDeath(N("Green Den Spider Hatchling"), 2, 7),
                    new State("idle",
                        new PlayerWithinTransition(0.5, "suicide")
                        ),
                    new State("suicide",
                        new Suicide()
                        )
                    ), Elder("Spider Egg Sac"))
            .Init(E("Red Spotted Den Spider"),
                new State(
                    new State("idle",
                        new Wander(0.5),
                        new Follow(1.0, 0.8, 0),
                        new Shoot(10, 1, 0, defaultAngle: 0, angleOffset: 0, projectileIndex: 0, predictive: 1,
                            coolDown: Cd(500), coolDownOffset: 0)
                        )
                    ),
                new ItemLoot("Healing Ichor", 0.2), Elder("Red Spotted Den Spider"))
            .Init(E("Arachna Summoner"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("idle",
                        new EntitiesNotExistsTransition(300, "Death", N("Arachna the Spider Queen"))
                        ),
                    new State("Death",
                        new Suicide()
                        )
                    ), Elder("Arachna Summoner"));
        }
    }
}
