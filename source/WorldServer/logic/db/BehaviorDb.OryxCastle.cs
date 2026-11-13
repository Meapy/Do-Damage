using System.Collections.Generic;
using System.Linq;
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
                CastleGuardian(true),
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
                CastleGuardian(false),
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
            // stands between the guardians and, while both live, calls their joint attacks: every 3 s
            // a third of the time it lets them be, else one of four formations for both at once
            .Init(TaskMaster,
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                    new State("Off"),
                    new State("Duo",
                        new EntityNotExistsTransition(GuardianRight, 60, "Solo"),
                        new EntityNotExistsTransition(GuardianLeft, 60, "Solo"),
                        new State("Rest", new TimedTransition(3000, "Pick")),
                        new State("Pick", new TimedRandomTransition(0, false, "Rest", "Rest", "Duo Circle", "Duo Side", "Duo Spin", "Duo Blast")),
                        TaskMasterCall("Duo Circle", 4000),
                        TaskMasterCall("Duo Side", 4600),
                        TaskMasterCall("Duo Spin", 6200),
                        TaskMasterCall("Duo Blast", 6000)
                    ),
                    new State("Solo",
                        new EntitiesNotExistsTransition(100, "Death", GuardianRight, GuardianLeft)
                    ),
                    new State("Death",
                        new Decay(0)
                    )
                )
            )
            // the guardian's thrown sword: knock it down to half and it flies back, bursting
            .Init("Oryx Guardian Sword",
                new State(
                    new State("Spin",
                        new Shoot(0, 5, 72, 0, fixedAngle: Auto(5, 72, 36), coolDown: 2000),
                        new Shoot(0, 5, 72, 0, fixedAngle: Auto(5, 72, 0), coolDownOffset: 500, coolDown: 2000),
                        new HpLessTransition(0.5, "Return")
                    ),
                    new State("Return",
                        new Grenade(4, 50, 10, 270, 90000),
                        new Shoot(0, 8, projectileIndex: 0, fixedAngle: 0, coolDown: 90000),
                        new TimedTransition(200, "Gone")
                    ),
                    new State("Gone", new Suicide())
                )
            )
            // sits directly above the wall between the guardian room and Janus' room (row 30), and
            // opens it once the Stone Guardians are dead, whatever else stands (it speaks as Janus,
            // its DisplayId)
            .Init("md dwGenerator",
                new State(
                    new State("Shut",
                        new State("Init",
                            new TimedTransition(2000, "Idle")
                        ),
                        new State("Idle",
                            new EntitiesNotExistsTransition(100, "Open", "Oryx Stone Guardian Right", "Oryx Stone Guardian Left")
                        )
                    ),
                    new State("Open",
                        new RemoveObject("Castle Brick Wall3", 6),
                        new Taunt(true, "The door yields. Come, then, and face me!") { Bubble = false },
                        new TimedTransition(1000, "Done")
                    ),
                    new State("Done")
                )
            )
            .Init("md Janus the Doorwarden",
                new State(
                    // stage 2: its boomerangs are 50 damage in the XML, far below the guardians before it
                    new BossStats(70000, 25, 2.4, 0.25),
                    // beside him: on his death spot his loot bags cover it, and the interact key
                    // doesn't enter a portal under a bag
                    new DropPortalOnDeath("Oryx Chamber Portal", 1.0, timeout: 0, xAdjustment: 2),
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
                    new ItemLoot("Oryx Chicken Chamber Key", 0.03),
                    new ItemLoot("Spiteful Scutum", 0.01)
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
            // tosses Quiet Bombs about while nobody is near, at you from 6 to 15 tiles, and silencing
            // rings closer in
            .Init("Oryx's Living Floor Fire Down", LivingFloor(15, true))
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
            // keeps a swarm of twelve; when you come close it sends them at you, and lobs grenades
            .Init("Oryx Insect Commander",
                new State(
                    new KeepMinions("Oryx Insect Minion", 12, 500),
                    new Grenade(3, 80, 5, coolDown: 1200),
                    new State("Chill",
                        new Wander(0.75),
                        new Order(12, "Oryx Insect Minion", "Roam"),
                        new PlayerWithinTransition(4, "Swarm")
                    ),
                    new State("Swarm",
                        new Order(12, "Oryx Insect Minion", "Swarm"),
                        new TimedTransition(4000, "Chill")
                    )
                )
            )
            // they bite only up close
            .Init("Oryx Insect Minion",
                new State(
                    new State("Roam",
                        new SetAltTexture(2),
                        new Prioritize(
                            new Protect(1.5, "Oryx Insect Commander", 4, 3.5, 3),
                            new Charge(3, 8, 1500),
                            new Buzz(2, 0.5),
                            new Wander(0.5)
                        ),
                        new Shoot(1.8, 6, 60, 0, coolDown: 1200)
                    ),
                    new State("Swarm",
                        new SetAltTexture(1),
                        new Prioritize(
                            new Charge(2, 2.25f, 2000),
                            new Circle(2, 2, 6),
                            new Wander(0.5)
                        ),
                        new Shoot(1.8, 6, 60, 1, coolDown: 500)
                    )
                )
            )
            // a suit stands still until you come within 4 tiles or hit it; it then follows slowly and
            // charges anyone who backs off. Once, at 45%, it guards a moment, shooting and healing.
            .Init("Oryx Suit of Armor",
                new State(
                    new State("Idle",
                        new PlayerWithinTransition(4, "Awake"),
                        new HpLessTransition(0.995, "Awake")
                    ),
                    new State("Awake",
                        new SetAltTexture(1),
                        new Shoot(4, 2, 17, 0, coolDown: 600),
                        new HpLessTransition(0.45, "Guard"),
                        new State("Near",
                            new Prioritize(new Follow(0.35, 6, 2), new Wander(0.35)),
                            new NoPlayerWithinTransition(5, "Far")
                        ),
                        new State("Far",
                            new Charge(2.25, 10, 100),
                            new PlayerWithinTransition(3, "Near")
                        )
                    ),
                    new State("Guard",
                        new Taunt(" ... "),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(5, 6, 60, 0, coolDown: 1000),
                        new HealSelf(500, 200),
                        new TimedTransition(4000, "Again")
                    ),
                    new State("Again",
                        new SetAltTexture(1),
                        new Shoot(4, 2, 17, 0, coolDown: 600),
                        new State("Again Near",
                            new Prioritize(new Follow(0.35, 6, 2), new Wander(0.35)),
                            new NoPlayerWithinTransition(6, "Again Far")
                        ),
                        new State("Again Far",
                            new Charge(2.25, 10, 100),
                            new PlayerWithinTransition(3, "Again Near")
                        )
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
            // Quiet Bombs at you from 6 to 13 tiles, silencing rings closer in
            .Init("Oryx's Living Floor", LivingFloor(13, false));

        private const string GuardianRight = "Oryx Stone Guardian Right";
        private const string GuardianLeft = "Oryx Stone Guardian Left";
        private const string TaskMaster = "Oryx Guardian TaskMaster";

        // the TaskMaster calls one joint attack, once, and waits it out
        private static State TaskMasterCall(string attack, int ms) =>
            new State(attack,
                new OrderOnce(30, GuardianRight, attack),
                new OrderOnce(30, GuardianLeft, attack),
                new TimedTransition(ms, "Rest")
            );

        // a Stone Guardian's attacks, as the original game's server had them (release 7.0,
        // oryxCastle.xml), under names starting p, each going back to `back` when done. The Right one
        // steps east to spin and spray, the Left one west.
        private static State[] GuardianAttacks(string p, string back, bool right, bool sword)
        {
            var outward = right ? 0 : 180;
            var states = new List<State>
            {
                new State(p + "Wander Attack",
                    new SetAltTexture(0),
                    new Wander(0.5),
                    new Shoot(0, 10, 36, 2, fixedAngle: Auto(10, 36, 0), coolDown: 2000),
                    new TimedTransition(3000, back)
                ),
                new State(p + "Charge Attack",
                    new SetAltTexture(0),
                    new Charge(1.5, 8, 100),
                    new Shoot(7, 5, 10, 0, predictive: 1, coolDown: 1000),
                    new TimedTransition(3000, back)
                ),
                // both circle the TaskMaster, firing pairs four ways
                new State(p + "Circle",
                    new SetAltTexture(0),
                    new Prioritize(
                        new Protect(1.5, TaskMaster, 20, 5, 4.25),
                        new Orbit(0.75, 4, 20, TaskMaster, 0, 0)
                    ),
                    new Shoot(0, 2, 10, 0, fixedAngle: Auto(2, 10, 0), coolDown: 1000),
                    new Shoot(0, 2, 10, 0, fixedAngle: Auto(2, 10, 90), coolDown: 1000),
                    new Shoot(0, 2, 10, 0, fixedAngle: Auto(2, 10, 180), coolDown: 1000),
                    new Shoot(0, 2, 10, 0, fixedAngle: Auto(2, 10, 270), coolDown: 1000),
                    new TimedTransition(4000, back)
                ),
                // to the TaskMaster, out 4 tiles, then round it slowly, spinning out two spiral arms
                new State(p + "Spin",
                    new SetAltTexture(0),
                    new Protect(2, TaskMaster, 20, 1, 0.3),
                    new TimedTransition(1000, p + "Spin Out")
                ),
                new State(p + "Spin Out",
                    new MoveLine(2, outward, 4),
                    new TimedTransition(400, p + "Spin Fire")
                ),
                new State(p + "Spin Fire",
                    Enumerable.Range(0, 9).SelectMany(i => new IStateChildren[]
                    {
                        new Shoot(0, 1, projectileIndex: 0, fixedAngle: i * 40, coolDownOffset: i * 200, coolDown: 1800),
                        new Shoot(0, 1, projectileIndex: 0, fixedAngle: 180 + i * 40, coolDownOffset: i * 200, coolDown: 1800)
                    }).Concat(new IStateChildren[]
                    {
                        new SetAltTexture(3, 4, 200, true),
                        new Orbit(0.4, 4, 20, TaskMaster, 0, 0),
                        new TimedTransition(4800, back)
                    }).ToArray()
                ),
                // to the TaskMaster, out 11 tiles, then a sweeping pair of arms and grenades at your feet
                new State(p + "Side",
                    new SetAltTexture(0),
                    new Protect(2, TaskMaster, 20, 1, 0.3),
                    new TimedTransition(1000, p + "Side Out")
                ),
                new State(p + "Side Out",
                    new MoveLine(2, outward, 11),
                    new Shoot(0, 10, 36, 2, fixedAngle: Auto(10, 36, 0), coolDown: 2000),
                    new TimedTransition(1000, p + "Side Fire")
                ),
                new State(p + "Side Fire",
                    new[] { (120, 0), (140, 200), (160, 400), (180, 800), (200, 1000), (220, 1200), (240, 1400),
                            (260, 1600), (280, 1800), (300, 2000), (320, 2200), (340, 2400) }
                    .Select(s => (IStateChildren)new Shoot(0, 2, s.Item1, 0, fixedAngle: 0, coolDownOffset: s.Item2, coolDown: 3000))
                    .Concat(new IStateChildren[]
                    {
                        new Shoot(0, 1, projectileIndex: 0, fixedAngle: 180, coolDownOffset: 2600, coolDown: 3000),
                        new Grenade(right ? 2.5 : 2, 50, 4.33, coolDown: 500),
                        new TimedTransition(2600, back)
                    }).ToArray()
                ),
                // to the TaskMaster, then heavy rings and grenades
                new State(p + "Blast",
                    new SetAltTexture(0),
                    new Protect(2, TaskMaster, 20, 1, 0.3),
                    new TimedTransition(1000, p + "Blast Step")
                ),
                new State(p + "Blast Step",
                    new MoveLine(2, outward, 0.2),
                    new TimedTransition(1000, p + "Blast Fire")
                ),
                new State(p + "Blast Fire",
                    new Shoot(0, 7, 26, 1, fixedAngle: Auto(7, 26, 0), coolDown: 1000),
                    new Shoot(0, 8, 22, 1, fixedAngle: Auto(8, 22, 0), coolDownOffset: 200, coolDown: 1000),
                    new Grenade(2.5, 50, 4.33, coolDown: 500),
                    new TimedTransition(4000, back)
                )
            };
            if (sword)
                states.AddRange(new[]
                {
                    // home, a step in, then it throws its sword and shelters until the sword is gone
                    new State(p + "Sword",
                        new SetAltTexture(0),
                        new ReturnToSpawn(2, 0.2),
                        new TimedTransition(1000, p + "Sword Step")
                    ),
                    new State(p + "Sword Step",
                        new MoveLine(1.5, right ? 180 : 0, 3),
                        new TimedTransition(600, p + "Sword Throw")
                    ),
                    new State(p + "Sword Throw",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(2),
                        new TossObject("Oryx Guardian Sword", 10, 90, 90000),
                        new TimedTransition(2000, p + "Sword Wait")
                    ),
                    // (at most 20 s, in case the sword can't be reached)
                    new State(p + "Sword Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(2),
                        new EntityNotExistsTransition("Oryx Guardian Sword", 30, p + "Sword Back"),
                        new TimedTransition(20000, p + "Sword Back")
                    ),
                    new State(p + "Sword Back",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(2),
                        new TimedTransition(1600, back)
                    )
                });
            return states.ToArray();
        }

        // stage 1 of the Oryx chain: the two Stone Guardians wake together when you come within 8
        // tiles; while both stand they mostly act alone, and join in the TaskMaster's formations. When
        // one falls the other shelters 5 s, healing, then fights alone, starting with its sword.
        private static State CastleGuardian(bool right)
        {
            var other = right ? GuardianLeft : GuardianRight;
            return new State(
                new BossStats(35000, 30, 0.75, 0.25),
                new State("Idle",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new SetAltTexture(1),
                    new PlayerWithinTransition(8, "Glow")
                ),
                new State("Glow",
                    new OrderOnce(30, other, "Glow"),
                    new OrderOnce(30, TaskMaster, "Duo"),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new SetAltTexture(1),
                    new Flash(0xFF888888, 1, 3),
                    new TimedTransition(3000, "Duo")
                ),
                new State("Duo",
                    new[]
                    {
                        (IStateChildren)new EntityNotExistsTransition(other, 60, "Alone"),
                        new State("Duo Choose",
                            new SetAltTexture(0),
                            new TimedRandomTransition(200, false, "Duo Wander Attack", "Duo Wander Attack", "Duo Wander Attack", "Duo Charge Attack")
                        )
                    }.Concat(GuardianAttacks("Duo ", "Duo Choose", right, false)).ToArray()
                ),
                new State("Alone",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new SetAltTexture(1),
                    new Flash(0xFF888888, 1, 5),
                    new HealSelf(500, right ? 1400 : 950),
                    new TimedTransition(5000, "Solo")
                ),
                new State("Solo",
                    new[]
                    {
                        (IStateChildren)new State("Solo Start", new TimedTransition(0, "Solo Sword")),
                        new State("Solo Choose",
                            new SetAltTexture(0),
                            new TimedRandomTransition(0, false,
                                "Solo Wander Attack", "Solo Wander Attack", "Solo Wander Attack", "Solo Wander Attack",
                                "Solo Charge Attack", "Solo Charge Attack", "Solo Charge Attack", "Solo Charge Attack",
                                "Solo Spin", "Solo Spin", "Solo Spin", "Solo Circle", "Solo Circle", "Solo Circle",
                                "Solo Side", "Solo Side", "Solo Sword", "Solo Sword", "Solo Sword", "Solo Sword")
                        )
                    }.Concat(GuardianAttacks("Solo ", "Solo Choose", right, true)).ToArray()
                )
            );
        }

        // the living floors: near, rings of silencing spells; from 6 tiles to `reach`, Quiet Bombs
        // thrown at you; further off, only the Fire Down floors toss bombs about. Each looks again
        // every 1.6 s (1.2 s close in).
        private static State LivingFloor(double reach, bool fireDown) =>
            new State(
                new State("Far",
                    fireDown ? new TossObject("Quiet Bomb", 10, 90, 1600, minRange: 9, maxRange: 12) : (IStateChildren)new SetAltTexture(0),
                    new State("Far Wait", new TimedTransition(1600, "Far Look")),
                    new State("Far Look",
                        new PlayerWithinTransition(6, "Close"),
                        new PlayerWithinTransition(reach, "Mid")
                    )
                ),
                new State("Mid",
                    new TossObject("Quiet Bomb", 12, coolDown: 1600),
                    new State("Mid Wait", new TimedTransition(1600, "Mid Look")),
                    new State("Mid Look",
                        new PlayerWithinTransition(6, "Close"),
                        new NoPlayerWithinTransition(reach, "Far")
                    )
                ),
                new State("Close",
                    new Shoot(10, 18, 20, 0, predictive: 1, coolDown: 1000),
                    new State("Close Wait", new TimedTransition(1200, "Close Look")),
                    new State("Close Look",
                        new NoPlayerWithinTransition(reach, "Far"),
                        new NoPlayerWithinTransition(6, "Mid")
                    )
                )
            );
    }
}