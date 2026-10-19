using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ OryxCastle = () => Behav()
            .Init("Oryx Stone Guardian Right",
                new State(
                    // stage 1 of the Oryx chain; in the Doomed Cellar they are Oryx 5's honour guard
                    new BossStats(60000, 45, 1.6, 0.3, world: "Doomed Cellar"),
                    new BossStats(35000, 30, 0.75, 0.25),
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(14, "Start")
                    ),
                    new State("Start",
                        new Order(30, "Oryx Stone Guardian Left", "Start"),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xC0C0C0, 0.5, 3),
                        new TimedTransition(1500, "Attack")
                    ),
                    new State("Attack",
                        new StayCloseToSpawn(0.5, range: 3),
                        new Wander(0.25),
                        new Shoot(15, count: 5, shootAngle: 7, projectileIndex: 0, coolDown: 1200),
                        new Shoot(0, count: 12, fixedAngle: 0, rotateAngle: 15, projectileIndex: 1, coolDown: 3000),
                        new HpLessTransition(0.75, "Attack2")
                    ),
                    new State("Attack2",
                        new Follow(0.75, 10, 0),
                        new Shoot(15, count: 12, shootAngle: 12, projectileIndex: 0, coolDown: 2500),
                        new Shoot(10, count: 4, shootAngle: 90, angleOffset: 0, projectileIndex: 2, coolDown: 1000),
                        new Shoot(10, count: 4, shootAngle: 90, angleOffset: 18, projectileIndex: 2, coolDown: 1000, coolDownOffset: 200),
                        new Shoot(10, count: 4, shootAngle: 90, angleOffset: 36, projectileIndex: 2, coolDown: 1000, coolDownOffset: 400),
                        new Shoot(10, count: 4, shootAngle: 90, angleOffset: 54, projectileIndex: 2, coolDown: 1000, coolDownOffset: 600),
                        new Shoot(10, count: 4, shootAngle: 90, angleOffset: 72, projectileIndex: 2, coolDown: 1000, coolDownOffset: 800),
                        new HpLessTransition(0.50, "Attack3")
                    ),
                    new State("Attack3",
                        new Orbit(1.0, 6, target: "Oryx Stone Guardian Left", acquireRange: 10, speedVariance: 0, radiusVariance: 0),
                        new Shoot(15, count: 3, shootAngle: 7, projectileIndex: 0, coolDown: 400),
                        new HpLessTransition(0.25, "Attack4")
                    ),
                    new State("Attack4",
                        new Follow(0.5, 10, 0),
                        new Shoot(0, count: 3, fixedAngle: 0, rotateAngle: 11, projectileIndex: 0, coolDown: 200)
                    )
                 ),
                 new Threshold(0.01,
                    new ItemLoot("Potion of Defense", 0.3),
                    new ItemLoot("Potion of Attack", 0.15),
                    new ItemLoot("Ancient Stone Sword", 0.003)
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Defense", 0.4),
                    new ItemLoot("Potion of Attack", 0.2),
                    new ItemLoot("Ancient Stone Sword", 0.005)
                ),
                new Threshold(.005,
                    new TierLoot(10, ItemType.Weapon, 0.05),
                    new TierLoot(11, ItemType.Weapon, 0.03),
                    new TierLoot(11, ItemType.Armor, 0.05),
                    new TierLoot(12, ItemType.Armor, 0.03),
                    new TierLoot(4, ItemType.Ability, 0.05),
                    new TierLoot(4, ItemType.Ring, 0.05)
                )
            )
            .Init("Oryx Stone Guardian Left",
                new State(
                    new BossStats(60000, 45, 1.6, 0.3, world: "Doomed Cellar"),
                    new BossStats(35000, 30, 0.75, 0.25),
                    new State("Start",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xC0C0C0, 0.5, 3),
                        new TimedTransition(1500, "Attack")
                    ),
                    new State("Attack",
                        new Follow(0.35, 10, 0),
                        new Shoot(15, count: 2, shootAngle: 12, projectileIndex: 0, coolDown: 900),
                        new HpLessTransition(0.75, "Attack2")
                    ),
                    new State("Attack2",
                        new Charge(speed: 2, range: 20, coolDown: 1800),
                        new Shoot(0, count: 16, fixedAngle: 0, rotateAngle: 11, projectileIndex: 1, coolDown: 5000),
                        new HpLessTransition(0.50, "Return1")
                    ),
                    new State("Return1",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new ReturnToSpawn(speed: 1.40),
                        new TimedTransition(2000, "Attack3")
                    ),
                    new State("Attack3",
                        new Charge(speed: 2, range: 20, coolDown: 1800),
                        new Shoot(15, count: 4, shootAngle: 12, projectileIndex: 0, coolDown: 3500),
                        new Shoot(15, count: 4, shootAngle: 12, projectileIndex: 1, coolDown: 3500, coolDownOffset: 300),
                        new Shoot(15, count: 4, shootAngle: 12, projectileIndex: 2, coolDown: 3500, coolDownOffset: 600),
                        new HpLessTransition(0.25, "Attack4")
                    ),
                    new State("Attack4",
                        new StayCloseToSpawn(0.5, range: 6),
                        new Wander(0.25),
                        new Shoot(15, count: 3, shootAngle: 14, projectileIndex: 1, coolDown: 1000),
                        new Shoot(0, count: 10, fixedAngle: 0, rotateAngle: 11, projectileIndex: 2, coolDown: 3000)
                    )
                 ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Defense", 0.4),
                    new ItemLoot("Potion of Attack", 0.2)
                ),
                new Threshold(.005,
                    new TierLoot(10, ItemType.Weapon, 0.05),
                    new TierLoot(11, ItemType.Weapon, 0.03),
                    new TierLoot(11, ItemType.Armor, 0.05),
                    new TierLoot(12, ItemType.Armor, 0.03),
                    new TierLoot(4, ItemType.Ability, 0.05),
                    new TierLoot(4, ItemType.Ring, 0.05)
                )
            )
            .Init("Oryx Guardian TaskMaster",
                new State(
                    new State("Idle",
                        new EntitiesNotExistsTransition(100, "Death", "Oryx Stone Guardian Right", "Oryx Stone Guardian Left")
                    ),
                    new State("Death",
                        new Decay(0)
                    )
                )
            )
            // sits directly above the wall between the guardian room and Janus' room (row 30)
            .Init("md dwGenerator",
                new State(
                    new State("Init",
                        new TimedTransition(2000, "Idle")
                    ),
                    new State("Idle",
                        new EntitiesNotExistsTransition(100, "Open", "Oryx Stone Guardian Right", "Oryx Stone Guardian Left")
                    ),
                    new State("Open",
                        new RemoveObject("Castle Brick Wall3", 6),
                        new TimedTransition(1000, "Done")
                    ),
                    new State("Done")
                )
            )
            .Init("md Janus the Doorwarden",
                new State(
                    // stage 2: its boomerangs are 50 damage in the XML, far below the guardians before it
                    new BossStats(70000, 25, 2.4, 0.25),
                    new DropPortalOnDeath("Oryx Chamber Portal", 1.0, timeout: 0),
                    new State("Idle",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(8, "Wake")
                    ),
                    new State("Wake",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Taunt("None shall pass through the door of the Mad God!"),
                        new Flash(0x9B30FF, 0.5, 4),
                        new TimedTransition(2500, "Boomerangs")
                    ),
                    new State("Boomerangs",
                        new StayCloseToSpawn(0.5, 4),
                        new Wander(0.3),
                        new Shoot(12, count: 3, shootAngle: 20, projectileIndex: 0, coolDown: 1200),
                        new Shoot(0, count: 8, shootAngle: 45, projectileIndex: 1, fixedAngle: 0, coolDown: 3500),
                        new HpLessTransition(0.4, "Rage"),
                        new TimedTransition(9000, "Keys")
                    ),
                    new State("Keys",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1),
                        new Taunt("Keys of Light and Darkness, guard this door!"),
                        new Spawn("md LightKey", 1, 1),
                        new Spawn("md DarkKey", 1, 1),
                        new TimedTransition(1500, "Keys Wait")
                    ),
                    new State("Keys Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(0, count: 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 45, coolDown: 2500),
                        new EntitiesNotExistsTransition(30, "Boomerangs", "md LightKey", "md DarkKey")
                    ),
                    new State("Rage",
                        new Taunt("You will never reach my master!"),
                        new Flash(0xFF0000, 0.5, 6),
                        new Follow(0.4, 10, 3),
                        new Shoot(12, count: 5, shootAngle: 15, projectileIndex: 0, coolDown: 900),
                        new Shoot(0, count: 12, shootAngle: 30, projectileIndex: 1, fixedAngle: 0, coolDown: 3000)
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Defense", 0.8),
                    new ItemLoot("Potion of Attack", 0.8),
                    new ItemLoot("Potion of Wisdom", 0.4),
                    new ItemLoot("Potion of Vitality", 0.3),
                    new ItemLoot("Wine Cellar Incantation", 0.04),
                    new ItemLoot("Oryx Chicken Chamber Key", 0.03)
                ),
                new Threshold(.005,
                    new TierLoot(10, ItemType.Weapon, 0.06),
                    new TierLoot(11, ItemType.Weapon, 0.04),
                    new TierLoot(4, ItemType.Ability, 0.06),
                    new TierLoot(11, ItemType.Armor, 0.06),
                    new TierLoot(12, ItemType.Armor, 0.03),
                    new TierLoot(4, ItemType.Ring, 0.06)
                )
            )
            .Init("md LightKey",
                new State(
                    new BossStats(3000, 40, 1.6, 0.25),
                    new Orbit(0.6, 4, 15, "md Janus the Doorwarden", speedVariance: 0.1, radiusVariance: 0.5),
                    new Shoot(10, count: 2, shootAngle: 10, projectileIndex: 0, coolDown: 1000)
                )
            )
            .Init("md DarkKey",
                new State(
                    new BossStats(6000, 20, 1.6, 0.25),
                    new Orbit(0.4, 6, 15, "md Janus the Doorwarden", speedVariance: 0.1, radiusVariance: 0.5, orbitClockwise: true),
                    new Shoot(10, count: 3, shootAngle: 15, projectileIndex: 1, coolDown: 1600)
                )
            )
            .Init("Oryx's Living Floor Fire Down",
                new State(
                    new State("Idle",
                        new PlayerWithinTransition(20, "Toss")
                    ),
                    new State("Toss",
                        new TossObject("Quiet Bomb", 10, coolDown: 1000),
                        new NoPlayerWithinTransition(21, "Idle"),
                        new PlayerWithinTransition(5, "Shoot and Toss")
                    ),
                    new State("Shoot and Toss",
                        new NoPlayerWithinTransition(21, "Idle"),
                        new NoPlayerWithinTransition(6, "Toss"),
                        new Shoot(0, 18, fixedAngle: 0, coolDown: new Cooldown(750, 250)),
                        new TossObject("Quiet Bomb", 10, coolDown: 1000)
                    )
                )
            )
            .Init("Oryx Knight",
                new State(
                      new State("waiting for u bae <3",
                          new PlayerWithinTransition(10, "tim 4 rekkings")
                          ),
                      new State("tim 4 rekkings",
                          new Prioritize(
                              new Wander(0.2),
                              new Follow(0.6, 10, 3, -1, 0)
                             ),
                          new Shoot(10, 3, 20, 0, coolDown: 350),
                          new TimedTransition(5000, "tim 4 singular rekt")
                          ),
                      new State("tim 4 singular rekt",
                          new Prioritize(
                                 new Wander(0.2),
                              new Follow(0.7, 10, 3, -1, 0)
                              ),
                          new Shoot(10, 1, projectileIndex: 0, coolDown: 50),
                          new Shoot(10, 1, projectileIndex: 1, coolDown: 1000),
                          new Shoot(10, 1, projectileIndex: 2, coolDown: 450),
                          new TimedTransition(2500, "tim 4 rekkings")
                         )
                  )
            )
            .Init("Oryx Insect Commander",
                new State(
                      new State("lol jordan is a nub",
                          new Prioritize(
                              new Wander(0.2)
                              ),
                          new Reproduce("Oryx Insect Minion", 10, 20, 1),
                          new Shoot(10, 1, projectileIndex: 0, coolDown: 900)
                         )
                  )
            )
            .Init("Oryx Insect Minion",
                new State(
                      new State("its SWARMING time",
                          new Prioritize(
                              new Wander(0.2),
                              new StayCloseToSpawn(0.4, 8),
                                 new Follow(0.8, 10, 1, -1, 0)
                              ),
                          new Shoot(10, 5, projectileIndex: 0, coolDown: 1500),
                          new Shoot(10, 1, projectileIndex: 0, coolDown: 230)
                          )
                  )
            )
            .Init("Oryx Suit of Armor",
                new State(
                      new State("idle",
                          new PlayerWithinTransition(8, "attack me pl0x")
                          ),
                      new State("attack me pl0x",
                          new TimedTransition(1500, "jordan is stanking")
                          ),
                      new State("jordan is stanking",
                          new Prioritize(
                               new Wander(0.2),
                               new Follow(0.4, 10, 2, -1, 0)
                              ),
                          new SetAltTexture(1),
                          new Shoot(10, 2, 15, 0, coolDown: 600),
                          new HpLessTransition(0.2, "heal")
                          ),
                      new State("heal",
                          new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                          new SetAltTexture(0),
                          new Shoot(10, 6, projectileIndex: 0, coolDown: 200),
                          new HealSelf(coolDown: 2000, amount: 200),
                          new TimedTransition(1500, "jordan is stanking")
                         )
                  )
            )
            .Init("Oryx Eye Warrior",
                new State(
                    new State("swaggin",
                        new PlayerWithinTransition(10, "penispiddle")
                        ),
                    new State("penispiddle",
                          new Prioritize(
                              new Follow(0.6, 10, 0, -1, 0)
                              ),
                          new Shoot(10, 5, projectileIndex: 0, coolDown: 1000),
                          new Shoot(10, 1, projectileIndex: 1, coolDown: 500)
                         )
                  )
            )
            .Init("Oryx Brute",
                new State(
                      new State("swaggin",
                          new PlayerWithinTransition(10, "piddle")
                        ),
                      new State("piddle",
                          new Prioritize(
                              new Wander(0.2),
                              new Follow(0.4, 10, 1, -1, 0)
                              ),
                          new Shoot(10, 5, projectileIndex: 1, coolDown: 1000),
                          new Reproduce("Oryx Eye Warrior", 10, 4, 2),
                          new TimedTransition(5000, "charge")
                          ),
                      new State("charge",
                          new Prioritize(
                              new Wander(0.3),
                              new Follow(1.2, 10, 1, -1, 0)
                              ),
                          new Shoot(10, 5, projectileIndex: 1, coolDown: 1000),
                          new Shoot(10, 5, projectileIndex: 2, coolDown: 750),
                          new Reproduce("Oryx Eye Warrior", 10, 4, 2),
                          new Shoot(10, 3, 10, projectileIndex: 0, coolDown: 300),
                          new TimedTransition(4000, "piddle")
                         )
                  )
            )
            .Init("Quiet Bomb",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("Idle",
                        new State("Tex1",
                            new TimedTransition(250, "Tex2")
                        ),
                        new State("Tex2",
                            new SetAltTexture(1),
                            new TimedTransition(250, "Tex3")
                        ),
                        new State("Tex3",
                            new SetAltTexture(0),
                            new TimedTransition(250, "Tex4")
                        ),
                        new State("Tex4",
                            new SetAltTexture(1),
                            new TimedTransition(250, "Explode")
                        )
                    ),
                    new State("Explode",
                        new SetAltTexture(0),
                        new Shoot(0, 18, fixedAngle: 0),
                        new Suicide()
                    )
                )
            );

        // the castle's pets swarm whoever comes close; its living floors silence and weaken
        private _ OryxCastleMinions = () => Behav()
            .Init("Oryx Pet",
                new State(
                    new State("Idle",
                        new Wander(0.2),
                        new PlayerWithinTransition(7, "Chase")
                        ),
                    new State("Chase",
                        new Follow(1, 9, 0),
                        new Shoot(1.5, count: 1, projectileIndex: 0, coolDown: 500),
                        new NoPlayerWithinTransition(12, "Idle")
                        )
                    )
                )
            .Init("Oryx's Living Floor",
                new State(
                    new State("Idle",
                        new PlayerWithinTransition(3, "Curse")
                        ),
                    new State("Curse",
                        new Shoot(4, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 45, coolDown: 2500),
                        new NoPlayerWithinTransition(5, "Idle")
                        )
                    )
                );
    }
}