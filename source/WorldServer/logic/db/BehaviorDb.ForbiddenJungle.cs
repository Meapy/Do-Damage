#region

using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using Shared.resources;
using WorldServer.logic.transitions;

#endregion

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ ForbiddenJungle = () => ForbiddenJungleBehaviours(Behav(), false);
        private _ ElderForbiddenJungle = () => ForbiddenJungleBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs)
        private static Ctor ForbiddenJungleBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);

            return db
            .Init(E("Great Coil Snake"),
                new State(
                    new DropPortalOnDeath("Forbidden Jungle Portal", 65),
                    new Prioritize(
                        new StayCloseToSpawn(1, 5),
                        new Wander(1)
                        ),
                    new State("Waiting",
                        new PlayerWithinTransition(15, "Attacking")
                        ),
                    new State("Attacking",
                        new Shoot(10, projectileIndex: 0, coolDown: Cd(700), coolDownOffset: 600),
                        new Shoot(10, 10, 36, 1, coolDown: Cd(2000)),
                        new TossObject(N("Great Snake Egg"), 4, 0, 5000, coolDownOffset: 0),
                        new TossObject(N("Great Snake Egg"), 4, 90, 5000, 600),
                        new TossObject(N("Great Snake Egg"), 4, 180, 5000, 1200),
                        new TossObject(N("Great Snake Egg"), 4, 270, 5000, 1800),
                        new NoPlayerWithinTransition(30, "Waiting")
                        )
                    ), Elder("Great Coil Snake"))
            .Init(E("Great Snake Egg"),
                new State(
                    new TransformOnDeath(N("Great Temple Snake"), 1, 2),
                    new State("Wait",
                        new TimedTransition(2500, "Explode"),
                        new PlayerWithinTransition(2, "Explode")
                        ),
                    new State("Explode",
                        new Suicide()
                        )
                    ), Elder("Great Snake Egg"))
            .Init(E("Great Temple Snake"),
                new State(
                    new Prioritize(
                        new Follow(1.5),
                        new Wander(1)
                        ),
                    new Shoot(10, 2, 7, 0, coolDown: Cd(1000), coolDownOffset: 0),
                    new Shoot(10, 6, 60, 1, coolDown: Cd(2000), coolDownOffset: 600)
                    ), Elder("Great Temple Snake"));
        }

        // the four Boss Totems guard Mixcoatl and go quiet once it dies
        private _ ForbiddenJungleMixcoatl = () => ForbiddenJungleMixcoatlBehaviours(Behav(), false);
        private _ ElderForbiddenJungleMixcoatl = () => ForbiddenJungleMixcoatlBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs)
        private static Ctor ForbiddenJungleMixcoatlBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            return db
            .Init(E("Mixcoatl the Masked God"),
                new State(
                    // the regular dungeon leads to its Elder version half the time
                    new DropPortalOnDeath("Elder Jungle Portal", elder ? 0 : 0.5, xAdjustment: 3),
                    new ScaleHP2(20),
                    new DropPortalOnDeath("Realm Portal", 1.0),
                    new DropPortalOnDeath("Secluded Thicket Portal", 0.5, xAdjustment: 2),
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(8, "Wake")
                        ),
                    new State("Wake",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt("Who dares disturb the masked god?"),
                        new TimedTransition(1500, "Waves")
                        ),
                    new State("Waves",
                        new StayCloseToSpawn(0.5, 4),
                        new Wander(0.3),
                        new Shoot(10, count: 3, shootAngle: 20, projectileIndex: 1, coolDown: Cd(1000)),
                        new Shoot(0, count: 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, coolDown: Cd(2400)),
                        new HpLessTransition(0.5, "Fire")
                        ),
                    new State("Fire",
                        new Taunt("Burn!"),
                        new Follow(0.4, 10, 3),
                        new Shoot(10, count: 5, shootAngle: 12, projectileIndex: 2, coolDown: Cd(1200)),
                        new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 22.5, coolDown: Cd(2000))
                        )
                    ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Dexterity", 1),
                    new ItemLoot("Mark of Mixcoatl", 0.02)
                    ), Elder("Mixcoatl the Masked God"),
                // the Elder dungeon's Potion of Max
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Dexterity", 0.05),
                    new ItemLoot("Elder Jungle Key", 0.01)))
            .Init(E("Boss Totem"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("Init",
                        new TimedTransition(2000, "Wait")
                        ),
                    new State("Wait",
                        new PlayerWithinTransition(10, "Fire"),
                        new EntityNotExistsTransition(N("Mixcoatl the Masked God"), 30, "Off")
                        ),
                    new State("Fire",
                        new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 0, coolDown: Cd(3000)),
                        new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 45, coolDown: Cd(3000), coolDownOffset: 1500),
                        new EntityNotExistsTransition(N("Mixcoatl the Masked God"), 30, "Off")
                        ),
                    new State("Off")
                    ), Elder("Boss Totem"));
        }
    }
}
