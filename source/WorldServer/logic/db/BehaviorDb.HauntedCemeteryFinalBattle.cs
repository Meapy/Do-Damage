using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;
using WorldServer.logic.behaviors.@new.movements;
//by GhostMaree
namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ HauntedCemeteryFinalBattle = () => HauntedCemeteryFinalBattleBehaviours(Behav(), false);
        private _ ElderCemeteryFinalBattle = () => HauntedCemeteryFinalBattleBehaviours(Behav(), true);

        private static Ctor HauntedCemeteryFinalBattleBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);
            // the Elder Skuld raised again (core/resurrection): his skulls keep to him too
            const string EmpoweredSkuld = "Empowered Elder Ghost of Skuld";
            string[] Skulds() => elder ? new[] { N("Ghost of Skuld"), EmpoweredSkuld } : new[] { "Ghost of Skuld" };
            // where the regular Skuld dies at a tenth of his health, the Elder one sings his Requiem
            var dying = elder ? "requiem" : "deadTask";
            return db
            .Init(E("Zombie Hulk"),
                new State(
                    new Wander(speed: 0.35),
                    new State("Attack2",
                        new SetAltTexture(0),
                        new StayBack(speed: 0.55, distance: 7, entity: null),
                        new Follow(speed: 0.3, acquireRange: 11, range: 5),
                        new Shoot(radius: 10, count: 1, projectileIndex: 1, coolDown: 400, coolDownOffset: 500),
                        new TimedRandomTransition(3000, true, "Attack1", "Attack3")
                    ),
                    new State("Attack1",
                        new SetAltTexture(1),
                        new Shoot(radius: 8, count: 3, shootAngle: 40, projectileIndex: 0, coolDown: 400, coolDownOffset: 250),
                        new Shoot(radius: 8, count: 2, shootAngle: 60, projectileIndex: 0, coolDown: 400, coolDownOffset: 250),
                        new Charge(speed: 1.1, range: 11, coolDown: 200),
                        new TimedRandomTransition(3000, true, "Attack2", "Attack3")
                    ),
                    new State("Attack3",
                        new SetAltTexture(0),
                        new Wander(speed: 0.4),
                        new Shoot(radius: 10, count: 3, shootAngle: 30, projectileIndex: 1, coolDown: 400, coolDownOffset: 500),
                        new TimedRandomTransition(3000, true, "Attack1", "Attack2")
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Kythal the Skull Generator", 0.03),
                    new TierLoot(5, ItemType.Weapon, 0.4),
                    new TierLoot(6, ItemType.Weapon, 0.4),
                    new TierLoot(5, ItemType.Armor, 0.4),
                    new TierLoot(6, ItemType.Armor, 0.4),
                    new TierLoot(3, ItemType.Ring, 0.25)
                )
            )
            .Init(E("Classic Ghost"),
                new State(
                    new Wander(speed: 0.4),
                    new Follow(speed: 0.55, acquireRange: 10, range: 3, duration: 1000, coolDown: 2000),
                    new Orbit(speed: 0.55, radius: 4, acquireRange: 7, target: null),
                    new Shoot(radius: 5, count: 4, shootAngle: 16, projectileIndex: 0, coolDown: 1000, coolDownOffset: 0)
                )
            )
            .Init(E("Werewolf"),
                new State(
                    new Spawn(children: N("Mini Werewolf"), maxChildren: 2, initialSpawn: 0, coolDown: 5400, givesNoXp: false),
                    new Spawn(children: N("Mini Werewolf"), maxChildren: 3, initialSpawn: 0, coolDown: 5400, givesNoXp: false),
                    new StayCloseToSpawn(speed: 0.8, range: 6),
                    new State("Circling",
                        new Shoot(radius: 10, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 1600),
                        new Prioritize(
                            new Orbit(speed: 0.4, radius: 5.4, acquireRange: 8, target: null),
                            new Wander(speed: 0.4)
                        ),
                        new TimedTransition(time: 3400, targetState: "Engaging")
                    ),
                    new State("Engaging",
                        new Shoot(radius: 5.5, count: 5, shootAngle: 13, projectileIndex: 0, coolDown: 1600),
                        new Follow(speed: 0.6, acquireRange: 10, range: 1),
                        new TimedTransition(time: 2600, targetState: "Circling")

                    )
                ),
                new ItemLoot("Black Wolf Generator", 0.01),
                new ItemLoot("Lil' Werewolf Generator", 0.01)
            )
            .Init(E("Mini Werewolf"),
                new State(
                    new Shoot(radius: 4, count: 1, projectileIndex: 0, coolDown: 1000),
                    new Prioritize(
                            new Follow(speed: 0.6, acquireRange: 15, range: 1),
                            new Protect(speed: 0.8, protectee: N("Werewolf"), acquireRange: 15, protectionRange: 6, reprotectRange: 3),
                            new Wander(speed: 0.4)
                    )
                ),
                new ItemLoot("Grey Wolf Generator", 0.005)
            )
            .Init(E("Ghost of Skuld"),
                new State(
                    new DropPortalOnDeath("Realm Portal", 1.0),
                    // the regular Skuld leads to the Elder Cemetery half the time
                    new DropPortalOnDeath("Elder Haunted Cemetery Portal", elder ? 0 : 0.5, xAdjustment: 3, timeout: 120),
                    // the Elder version gains less health per extra player: its base is far higher
                    new ScaleHP2(elder ? 15 : 20),
                    new State("wait1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt("Hello Heroes!"),
                        new TimedTransition(4500, "wait2")
                        ),
                    new State("wait2",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt("Surviving every wave of undead that came at you, its impressive!"),
                        new TimedTransition(4500, "wait3")
                        ),
                    new State("wait3",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt("You deserve a reward!"),
                        new TimedTransition(4500, "skuld1")
                        ),
                    new State("skuld1",
                        new SetAltTexture(1),
                        new Taunt(1.00, "Your reward is....A SWIFT DEATH!"),
                        new RingAttack(30, 20, 0, projectileIndex: 2, 10, 10, coolDown: 2000),
                        new TossObject(N("Flying Flame Skull"), range: 6, angle: 270, coolDown: 9999),
                        new TossObject(N("Flying Flame Skull"), range: 6, angle: 90, coolDown: 9999),
                        new TimedTransition(4000, "gotem")
                        ),
                    new State("gotem",
                        new Shoot(10, count: 7, shootAngle: 5, projectileIndex: 0, coolDown: 700),
                        new RingAttack(30, 1, 0, projectileIndex: 2, 0.4, 0, coolDown: 150),
                        new Shoot(10, count: 1, projectileIndex: 0, coolDown: 1200),
                        new Shoot(12, count: 1, fixedAngle: 0, projectileIndex: 2, coolDown: 4000),
                        new Shoot(12, count: 1, fixedAngle: 90, projectileIndex: 2, coolDown: 4000),
                        new Shoot(12, count: 1, fixedAngle: 180, projectileIndex: 2, coolDown: 4000),
                        new Shoot(12, count: 1, fixedAngle: 270, projectileIndex: 2, coolDown: 4000),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(8000, "telestart")
                        ),
                   new State("blam1",
                       new SetAltTexture(1),
                       new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                       new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele7")
                        ),
                                      new State("blam2",
                                          new SetAltTexture(1),
                        new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                        new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele5")
                        ),
                                new State("blam3",
                                    new SetAltTexture(1),
                        new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                        new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele4")
                        ),
                                new State("blam4",
                                    new SetAltTexture(1),
                        new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                        new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele6")
                        ),
                               new State("blam5",
                                   new SetAltTexture(1),
                        new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                        new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele2")
                        ),
                                        new State("blam6",
                                            new SetAltTexture(1),
                        new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                        new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele3")
                        ),
                                      new State("blam7",
                                          new SetAltTexture(1),
                                          new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                                          new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele8")
                        ),
                                      new State("blam8",
                                          new SetAltTexture(1),
                                          new Shoot(10, count: 7, shootAngle: 12, projectileIndex: 0, coolDown: 1000),
                                          new Shoot(10, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 2000),
                        new Shoot(8.4, count: 1, fixedAngle: 0, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 180, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 270, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 45, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 135, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 235, projectileIndex: 1, coolDown: 1400),
                        new Shoot(8.4, count: 1, fixedAngle: 315, projectileIndex: 1, coolDown: 1400),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(4500, "tele8")
                        ),
                    new State("telestart",
                        new SetAltTexture(1),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(2500, "tele1")
                        ),
                    new State("tele1",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 24, y: 18),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "blam2")
                        ),
                    new State("tele2",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 17, y: 26),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "blam3")
                        ),
                    new State("tele3",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 29, y: 19),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "blam4")
                        ),
                    new State("tele4",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 29, y: 29),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "blam5")
                        ),
                   new State("tele5",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 24, y: 18),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "blam6")
                        ),
                  new State("tele6",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 25, y: 35),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "blam7")
                        ),
                  new State("tele7",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 20, y: 29),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "blam8")
                        ),
                  new State("tele8",
                        new SetAltTexture(11),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new MoveTo(speed: 2, x: 24, y: 24),
                        new HpLessTransition(0.1, dying),
                        new TimedTransition(1000, "gotem")
                        ),
                    // the Elder Skuld's Requiem: back at the heart of the arena, his flame skulls
                    // round him and the dead rising in a ring, he turns a cross of giant ghosts one
                    // way and a cross of ghostly bolts the other until he truly falls
                    new State("requiem",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(11),
                        new MoveTo(speed: 2, x: 24, y: 24),
                        new Taunt(true, "Die? I am the keeper of the dead! RISE, all of you, and sing them their requiem!"),
                        new Flash(0xFF2FD29A, 0.2, 12),
                        new TimedTransition(2500, "requiem2")
                        ),
                    new State("requiem2",
                        new SetAltTexture(1),
                        new Flash(0xFF2FD29A, 0.5, 4),
                        new TossObject(N("Flying Flame Skull"), range: 4, angle: 30, coolDown: 99999),
                        new TossObject(N("Flying Flame Skull"), range: 4, angle: 150, coolDown: 99999),
                        new TossObject(N("Flying Flame Skull"), range: 4, angle: 270, coolDown: 99999),
                        new TossObject(N("Zombie Rise"), range: 7, angle: 0, coolDown: 9000),
                        new TossObject(N("Zombie Rise"), range: 7, angle: 90, coolDown: 9000),
                        new TossObject(N("Zombie Rise"), range: 7, angle: 180, coolDown: 9000),
                        new TossObject(N("Zombie Rise"), range: 7, angle: 270, coolDown: 9000),
                        new Shoot(30, count: 4, shootAngle: 90, projectileIndex: 2, fixedAngle: 0, rotateAngle: 11, coolDown: 800),
                        new Shoot(30, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 45, rotateAngle: -13, coolDown: 400),
                        new Shoot(12, count: 3, shootAngle: 14, projectileIndex: 1, coolDown: 2400, coolDownOffset: 1200)
                        ),
                    new State("deadTask",
                        new RemoveEntity(9999, N("Flying Flame Skull")),
                        new Suicide()
                        )
                    ),
                new Threshold(.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(0.001,
                    new ItemLoot("Reaper Generator", 0.03),
                    new ItemLoot("Pumpkin Head Generator", 0.03),
                    new ItemLoot("Pumpkman Generator", 0.03),
                    new ItemLoot("Potion of Vitality", 1),
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Resurrected Warrior's Armor", 0.008),
                    new ItemLoot("Plague Poison", elder ? 0.1 : 0.05),
                    new ItemLoot("Amulet of Resurrection", elder ? 0.004 : 0.002),
                    new ItemLoot("Soul's Guidance", elder ? 0.016 : 0.008),
                    new ItemLoot("Cemetery Key", 0.008)
                    ),
                ElderLoot(0.03,
                    new ItemLoot("Potion of Max Vitality", 0.025),
                    new ItemLoot("Elder Cemetery Key", 0.02)
                    ),
                ElderLoot(0.0001,
                    new TierLoot(13, ItemType.Weapon, .1),
                    new TierLoot(14, ItemType.Armor, .1),
                    new TierLoot(6, ItemType.Ability, .1),
                    new TierLoot(6, ItemType.Ring, .1)
                    )
            )
            .Init(E("Halloween Zombie Spawner"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new State("Leech"),
                    new State("1",
                        new Spawn(N("Zombie Rise"), maxChildren: 1),
                        new EntitiesNotExistsTransition(100, "2", Skulds())
                    ),
                    new State("2",
                        new Suicide()
                    )
                )
            )
            .Init(E("Zombie Rise"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new TransformOnDeath(N("Blue Zombie")),
                    new State("1",
                        new SetAltTexture(1),
                        new TimedTransition(750, "2")
                    ),
                    new State("2",
                        new SetAltTexture(2),
                        new TimedTransition(750, "3")
                    ),
                    new State("3",
                        new SetAltTexture(3),
                        new TimedTransition(750, "4")
                    ),
                    new State("4",
                        new SetAltTexture(4),
                        new TimedTransition(750, "5")
                    ),
                    new State("5",
                        new SetAltTexture(5),
                        new TimedTransition(750, "6")
                    ),
                    new State("6",
                        new SetAltTexture(6),
                        new TimedTransition(750, "7")
                    ),
                    new State("7",
                        new SetAltTexture(7),
                        new TimedTransition(750, "8")
                    ),
                    new State("8",
                        new SetAltTexture(8),
                        new TimedTransition(750, "9")
                    ),
                    new State("9",
                        new SetAltTexture(9),
                        new TimedTransition(750, "10")
                    ),
                    new State("10",
                        new SetAltTexture(10),
                        new TimedTransition(750, "11")
                    ),
                    new State("11",
                        new SetAltTexture(11),
                        new TimedTransition(750, "12")
                    ),
                    new State("12",
                        new SetAltTexture(12),
                        new TimedTransition(750, "13")
                    ),
                    new State("13",
                        new SetAltTexture(13),
                        new TimedTransition(750, "14")
                    ),
                    new State("14",
                        new Suicide()
                    )
                )
            )
            .Init(E("Blue Zombie"),
                new State(
                    new Follow(0.03, 100, 1),
                    new State("1",
                        new Shoot(10, 1, projectileIndex: 0, coolDown: 1000),
                        new EntitiesNotExistsTransition(100, "2", Skulds())
                    ),
                    new State("2",
                        new Suicide()
                    )
                )
            )
            .Init(E("Flying Flame Skull"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Orbit(1, 5, 20, target: N("Ghost of Skuld")),
                    elder ? new Orbit(1, 5, 20, target: EmpoweredSkuld) : null,
                    new State("1",
                        new Shoot(100, 10, shootAngle: 36, projectileIndex: 0, coolDown: 1000),
                        new EntitiesNotExistsTransition(100, "2", Skulds())
                    ),
                    new State("2",
                        new Suicide()
                    )
                )
            )
            ;
        }
    }
}
