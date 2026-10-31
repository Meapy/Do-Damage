#region

using Shared.resources;
using WorldServer.logic.loot;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

#endregion

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ DavyJones = () => DavyJonesBehaviours(Behav(), false);
        private _ ElderDavyJones = () => DavyJonesBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs)
        private static Ctor DavyJonesBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);

            return db
            .Init(E("Davy Jones"),
                new State(
                    // the regular dungeon leads to its Elder version half the time
                    new DropPortalOnDeath("Elder Davy Portal", elder ? 0 : 0.5, xAdjustment: 3),
                    new ScaleHP2(20),
                    new DropPortalOnDeath("Realm Portal", probability: 1.0, timeout: null),
                    new State("Waiting",
                        new PlayerWithinTransition(10, "Floating")
                        ),
                    new State("Floating",
                        new StayCloseToSpawn(.1, 3),
                        new ChangeSize(100, 100),
                        new SetAltTexture(1),
                        new SetAltTexture(3),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(200, "buffer")
                        ),
                    new State("buffer",
                        new StayCloseToSpawn(.1, 3),
                        new Wander(0.3),
                        new Shoot(10, 5, 10, 0, coolDown: Cd(1000)),
                        new Shoot(10, 1, 10, 1, coolDown: Cd(2000)),
                        new EntityNotExistsTransition(N("Ghost Lanturn Off"), 30, "Vunerable")
                        ),
                    new State("CheckOffLanterns",
                        new SetAltTexture(2),
                        new StayCloseToSpawn(.1, 3),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new EntityNotExistsTransition(N("Ghost Lanturn Off"), 30, "Vunerable")
                        ),
                    new State("Vunerable",
                        new SetAltTexture(5),
                        new StayCloseToSpawn(.1, 0),
                        new TimedTransition(2500, "deactivate")
                        ),
                    new State("deactivate",
                        new SetAltTexture(4),
                        new StayCloseToSpawn(.1, 0),
                        new EntityNotExistsTransition(N("Ghost Lanturn On"), 30, "Floating")
                        )
                    ),
                new Threshold(0.03,
                    new ItemLoot("Spirit Dagger", 0.012),
                    new ItemLoot("Spectral Cloth Armor", 0.01),
                    new ItemLoot("Captain's Ring", 0.01),
                    new ItemLoot("Ghostly Prism", 0.02)
                    ),
                new Threshold(0.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(0.005,
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Davy's Key", 0.009, 0, 0.03),
                    new ItemLoot("Ruby Gemstone", 0.02),
                    new ItemLoot("Golden Chalice", 0.025),
                    new ItemLoot("Pearl Necklace", 0.035)
                    ), Elder("Davy Jones"))
            .Init(E("Ghost Lanturn Off"),
                new State(
                    new State("default",
                        new HpLessTransition(0.25, "transform")
                        ),
                    new State("transform",
                        new Transform(N("Ghost Lanturn On"))
                        )
                    ), Elder("Ghost Lanturn Off"))
            .Init(E("Ghost Lanturn On"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new EntitiesNotExistsTransition(40, "Wait", N("Ghost Lanturn Off"))
                        ),
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(4000, "deactivate")
                        ),
                    new State("deactivate",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new EntitiesNotExistsTransition(40, "shoot", N("Ghost Lanturn Off")),
                        new TimedTransition(10000, "gone")
                        ),
                    new State("shoot",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(10, 6, coolDown: Cd(9000001), coolDownOffset: 100),
                        new TimedTransition(1000, "gone")
                        ),
                    new State("gone",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Transform(N("Ghost Lanturn Off"))
                        )
                    ), Elder("Ghost Lanturn On"))

            .Init(E("Lost Soul"),
                new State(
                    new State("Default",
                        new Prioritize(
                            new Orbit(2, 3, 20, N("Ghost of Roger")),
                            new Wander(0.3)
                            ),
                        new PlayerWithinTransition(4, "Default1")
                        ),
                    new State("Default1",
                        new Charge(4, 8, coolDown: Cd(2000)),
                        new TimedTransition(2200, "Blammo")
                        ),
                    new State("Blammo",
                        new Shoot(10, count: 6, projectileIndex: 0, coolDown: Cd(2000)),
                        new Suicide()
                        )
                    ), Elder("Lost Soul")).Init(E("Ghost of Roger"),
                new State(
                    new State("spawn",
                        new Spawn(N("Lost Soul"), 3, 1, 5000),
                        new TimedTransition(100, "Attack")
                        ),
                    new State("Attack",
                        new Shoot(13, 1, 0, 0, coolDown: Cd(10)),
                        new TimedTransition(20, "Attack2")
                        ),
                    new State("Attack2",
                        new Shoot(13, 1, 0, 0, coolDown: Cd(10)),
                        new TimedTransition(20, "Attack3")
                        ),
                    new State("Attack3",
                        new Shoot(13, 1, 0, 0, coolDown: Cd(10)),
                        new TimedTransition(20, "Wait")
                        ),
                    new State("Wait",
                        new TimedTransition(1000, "Attack")
                        )
                    ), Elder("Ghost of Roger"))
            .Init(E("Purple Key"),
                new State(
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new PlayerWithinTransition(1, "Cycle")
                        ),
                    new State("Cycle",
                        new RemoveObject("GhostShip PurpleDoor Lf", 200),
                        new RemoveObject("GhostShip PurpleDoor Rt", 200),
                        new Taunt(true, "Purple Key has been found!"),
                        new Suicide()
                        )
                    ), Elder("Purple Key"))
            .Init(E("Red Key"),
                new State(
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new PlayerWithinTransition(1, "Cycle")
                        ),
                    new State("Cycle",
                        new RemoveObject("GhostShip RedDoor Lf", 200),
                        new RemoveObject("GhostShip RedDoor Rt", 200),
                        new Taunt(true, "Red Key has been found!"),
                        new Suicide()

                        )
                    ), Elder("Red Key"))
            .Init(E("Green Key"),
                new State(
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new PlayerWithinTransition(1, "Cycle")
                        ),
                    new State("Cycle",
                        new RemoveObject("GhostShip GreenDoor Lf", 200),
                        new RemoveObject("GhostShip GreenDoor Rt", 200),
                        new Taunt(true, "Green Key has been found!"),
                        new Suicide()
                        )
                    ), Elder("Green Key"))
            .Init(E("Yellow Key"),
                new State(
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new PlayerWithinTransition(1, "Cycle")
                        ),
                    new State("Cycle",
                        new Taunt(true, "Yellow Key has been found!"),
                        new RemoveObjectOnDeath("GhostShip YellowDoor Lf", 200),
                        new RemoveObjectOnDeath("GhostShip YellowDoor Rt", 200),
                        new Suicide()
                        )
                    ), Elder("Yellow Key"))
            .Init(E("Lil' Ghost Pirate"),
                new State(
                    new ChangeSize(30, 120),
                    new Shoot(10, count: 1, projectileIndex: 0, coolDown: Cd(2000)),
                    new State("Default",
                        new Prioritize(
                            new Follow(1, 8, 1),
                            new Wander(0.3)
                            ),
                        new TimedTransition(2850, "Default1")
                        ),
                    new State("Default1",
                        new StayBack(0.2, 3),
                        new TimedTransition(1850, "Default")
                        )
                    ), Elder("Lil' Ghost Pirate"))
            .Init(E("Zombie Pirate Sr"),
                new State(
                    new Shoot(10, count: 1, projectileIndex: 0, coolDown: Cd(2000)),
                    new State("Default",
                        new Prioritize(
                            new Follow(1, 8, 1),
                            new Wander(0.3)
                            ),
                        new TimedTransition(2850, "Default1")
                        ),
                    new State("Default1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new Prioritize(
                            new Follow(1.2, 8, 1),
                            new Wander(0.3)
                            ),
                        new TimedTransition(2850, "Default")
                        )
                    ), Elder("Zombie Pirate Sr"))
            .Init(E("Zombie Pirate Jr"),
                new State(
                    new Shoot(10, count: 1, projectileIndex: 0, coolDown: Cd(2500)),
                    new State("Default",
                        new Prioritize(
                            new Follow(1.2, 8, 1),
                            new Wander(0.3)
                            ),
                        new TimedTransition(2850, "Default1")
                        ),
                    new State("Default1",
                        new Swirl(0.2, 3),
                        new TimedTransition(1850, "Default")
                        )
                    ), Elder("Zombie Pirate Jr"))
            .Init(E("Captain Summoner"),
                new State(
                    new State("Default",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible)
                        )
                    ), Elder("Captain Summoner"))
            .Init(E("GhostShip Rat"),
                new State(
                    new State("Default",
                        new Shoot(10, count: 1, projectileIndex: 0, coolDown: Cd(1750)),
                        new Prioritize(
                            new Follow(1, 8, 1),
                            new Wander(0.3)
                            )
                        )
                    ), Elder("GhostShip Rat"))
            .Init(E("Violent Spirit"),
                new State(
                    new State("Default",
                        new ChangeSize(35, 120),
                        new Shoot(10, count: 3, projectileIndex: 0, coolDown: Cd(1750)),
                        new Prioritize(
                            new Follow(1, 8, 1),
                            new Wander(0.3)
                            )
                        )
                    ), Elder("Violent Spirit"))
            .Init(E("School of Ghostfish"),
                new State(
                    new State("Default",
                        new Shoot(10, count: 3, shootAngle: 18, projectileIndex: 0, coolDown: Cd(4000)),
                        new Wander(0.3)
                        )
                    ), Elder("School of Ghostfish"));
        }
    }
}
