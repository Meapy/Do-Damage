using Shared.resources;
using WorldServer.logic.loot;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        // the Elder Esben's frost
        private const uint Frost = 0xFF8FD8FF;

        private _ IceCave = () => IceCaveBehaviours(Behav(), false);
        private _ ElderIceCave = () => IceCaveBehaviours(Behav(), true);

        // The Ice Cave and the Inner Sanctum, and their Elder versions (BehaviorDb.Elder.cs): the
        // regular Esben leads to the Elder Ice Cave half the time, and the Elder Esben has phases
        // of his own (Frostfall, Unbound Rend, the Cornus Unbound) and leaves remains a
        // Necromantic Relic raises (core/resurrection, EmpoweredSignatures.Esben).
        private static Ctor IceCaveBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);
            // the Elder Esben's own (null in the regular fight: a state skips null children)
            IStateChildren Elder(IStateChildren b) => elder ? b : null;
            // Frostfall: icicles thrown down on the players while the crosses stand, slowing
            // what they hit (two at a time, every `ms`)
            IStateChildren Frostfall(int ms, int perVolley = 2) => Elder(new Starfall(Starfall.Area.Players, 18, 1.5, 120,
                coolDown: ms, perVolley: perVolley, color: Frost, effect: ConditionEffectIndex.Slowed, effectMs: 1500,
                fillMs: 1300, throwColor: 0xFFFFFFFF));
            return db
             .Init(E("Big Yeti"),
             new State(
                   new State("geticey",
                       new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(8, "leggo")
                        ),
                  new State("leggo",
                     new Follow(0.3, 8, 1),
                     new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                       new Taunt(0.50, "MRRRRRRTTTTTT!"),
                        new Taunt(0.25, "Mwruuuu!"),
                       new Flash(0xFF0000, 2, 18),
                       new Shoot(10, count: 1, projectileIndex: 0, predictive: 0.5, coolDown: 2500),
                       new Spawn(N("Mini Yeti"), 1, 4, coolDown: 10000)
                        )

                  )
            )
            .Init(E("Mini Yeti"),
                new State(
                    new Flash(0xFF0000, 2, 6),
                     new Orbit(0.3, 3, 20, N("Big Yeti"), speedVariance: 0.1),
                     new Shoot(8.4, count: 1, projectileIndex: 0, predictive: 1, coolDown: 1750),
                     new Shoot(8.4, count: 1, projectileIndex: 0, coolDown: 1200)
                    ),
                new ItemLoot("Penguin Generator", 0.01)
            )
           .Init(E("Snow Bat Mama"),
             new State(
                   new State("geticey",
                       new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(8, "leggo")
                        ),
                  new State("leggo",
                     new Follow(0.4, 8, 1),
                      new Shoot(8.4, count: 1, projectileIndex: 0, predictive: 1, coolDown: 2750),
                     new Shoot(8.4, count: 1, projectileIndex: 1, coolDown: 2300),
                      new Shoot(8.4, count: 1, projectileIndex: 2, coolDown: 1000),
                      new Shoot(8.4, count: 1, projectileIndex: 3, coolDown: 1250),
                       new Spawn(N("Snow Bat"), 1, 4, coolDown: 10000)
                        )),
               new ItemLoot("Snowy Owl Generator", 0.03)
            )
            // its sonar: a turning ring of six spinners every 1.6 s and one at the nearest player
            // (it fired six spinners that never moved every 25 ms, each lasting 5 s: a carpet of
            // hundreds of invisible-looking traps round every bat)
            .Init(E("Snow Bat"),
                new State(
                     new Wander(0.4),
                     new Follow(0.6, 9, 3),
                     new Shoot(8.4, count: 6, projectileIndex: 0, fixedAngle: 0, rotateAngle: 17, coolDown: 1600),
                     new Shoot(8.4, count: 1, projectileIndex: 0, predictive: 0.4, coolDown: 900, coolDownOffset: 450)
                    ),
                new ItemLoot("Green Penguin Generator", 0.005)
            )
        // a game object, not an enemy, so it can't die: it opens the Inner Sanctum portal itself
        // once every Big Yeti and Snow Bat Mama is dead
        .Init(E("ic boss spawner live"),
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("waiting",
                    new PlayerWithinTransition(1000, ":)")
                    ),
                new State(":)",
                    new Taunt("..kill the large ones....open the way....the are meaningless...."),
                    new EntitiesNotExistsTransition(1000, "open", N("Big Yeti"), N("Snow Bat Mama"))
                    ),
                new State("open",
                    new Taunt("Innocent souls. So delicious. You have sated me. Now come, I shall give you your reward."),
                    new Spawn(N("Inner Sanctum Portal"), 1, 1),
                    new TimedTransition(1000, "gone")
                    ),
                new State("gone",
                    new Decay(0)
                    )
                )
            )
        .Init(E("ic boss manager"),
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible)
                )
            )
        .Init(E("ic Esben the Unwilling"),
            new State(
                // the Elder version gains less health per extra player: its base is far higher
                new ScaleHP2(elder ? 15 : 20),
                // the regular Esben leads to the Elder Ice Cave half the time
                new DropPortalOnDeath("Elder Ice Cave Portal", elder ? 0 : 0.5, xAdjustment: 3, timeout: 120),
                // (the fight's states under one parent, so the step into the end at a tenth of his
                // health doesn't keep firing once he's there: the Elder end has several states)
                new State("fight",
                new HpLessTransition(0.1, elder ? "cornus" : "die"),
                new State("start",
                    new PlayerWithinTransition(50, "idle", true)
                    ),
                new State("idle",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new OrderOnce(100, N("ic boss purifier generator"), "spawn crosses"),
                    // raised by a Necromantic Relic (or /empower) the crosses' generator is long
                    // gone: the Empowered Esben fights the Risen cycle instead
                    Elder(new EntityNotExistsTransition(N("ic boss purifier generator"), 200, "risen1")),
                    // give the crosses time to appear before checking whether any are left
                    new TimedTransition(2000, "My cross never dead <3")
                    ),
                // The Risen cycle (Empowered): no crosses to hold him, Esben and the cornus fight
                // as one, round the heart of the sanctum. Unbound Rend (9 s), Icicle Crosses
                // (7 s, Armored, Frostfall), Frozen Rings (6 s), and round again
                Elder(new State("risen1",
                    new SetAltTexture(0),
                    new Taunt(true, "You dragged me back? Then watch what he does with me!"),
                    new Orbit(1.2, 6, 20, N("ic boss manager")),
                    new Shoot(14, 5, shootAngle: 12, projectileIndex: 4, predictive: 0.5, coolDown: 1000),
                    new Shoot(20, 12, projectileIndex: 2, fixedAngle: 0, rotateAngle: 11, coolDown: 2000, coolDownOffset: 600),
                    new Shoot(12, 1, projectileIndex: 3, predictive: 0.7, coolDown: 3000, coolDownOffset: 1500),
                    new TimedTransition(9000, "risen2")
                    )),
                Elder(new State("risen2",
                    new SetAltTexture(2),
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                    new Taunt("Icicles, rend their flesh!"),
                    new Orbit(0.3, 2, 20, N("ic boss manager")),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 0, coolDown: 1400),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 90, coolDown: 1400),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 180, coolDown: 1400),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 270, coolDown: 1400),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 45, coolDown: 1400, coolDownOffset: 700),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 135, coolDown: 1400, coolDownOffset: 700),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 225, coolDown: 1400, coolDownOffset: 700),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 315, coolDown: 1400, coolDownOffset: 700),
                    Frostfall(1400),
                    new TimedTransition(7000, "risen3")
                    )),
                Elder(new State("risen3",
                    new SetAltTexture(0),
                    new Flash(Frost, 0.3, 3),
                    new Shoot(20, 10, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDown: 1500),
                    new Shoot(20, 14, projectileIndex: 2, fixedAngle: 0, coolDown: 1500, coolDownOffset: 750),
                    new Shoot(12, 3, shootAngle: 15, projectileIndex: 0, predictive: 0.5, coolDown: 2000),
                    new TimedTransition(6000, "risen1")
                    )),
                new State("My cross never dead <3",
                    new Taunt("Ah fresh meat.I must thank you for the souls..."),
                    new Taunt("Icicles, rend their flesh!"),
                    new SetAltTexture(0),
                    new Orbit(0.1, 1, 10, N("ic boss manager")),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    Frostfall(2600),
                    new EntitiesNotExistsTransition(20, "nooo!", N("ic boss purifier")),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 0, coolDownOffset: 3000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 90, coolDownOffset: 3000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 180, coolDownOffset: 3000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 270, coolDownOffset: 3000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 45, coolDownOffset: 4000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 135, coolDownOffset: 4000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 225, coolDownOffset: 4000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 315, coolDownOffset: 4000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 0, coolDownOffset: 5000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 90, coolDownOffset: 5000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 180, coolDownOffset: 5000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 270, coolDownOffset: 5000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 45, coolDownOffset: 6000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 135, coolDownOffset: 6000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 225, coolDownOffset: 6000, coolDown: 15000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 315, coolDownOffset: 6000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 8000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 9000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 11000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 12000, coolDown: 15000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 12000, coolDown: 15000)
                    ),
                new State("nooo!",
                    new Taunt("LOOSING....CONTROL....NOOOO"),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new SetAltTexture(0),
                    new TimedTransition(250, "A")
                    ),
                new State("A",
                    new SetAltTexture(2),
                    new TimedTransition(250, "B")
                    ),
               new State("B",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                   new SetAltTexture(0),
                   new TimedTransition(250, "C")
                   ),
               new State("C",
                   new SetAltTexture(2),
                   new TimedTransition(250, "D")
                   ),
               new State("D",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                   new SetAltTexture(0),
                   new TimedTransition(250, "E")
                   ),
               new State("E",
                   new SetAltTexture(2),
                   new TimedTransition(250, "F")
                   ),
               new State("F",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                   new SetAltTexture(0),
                   new TimedTransition(250, "G")
                   ),
               new State("G",
                   new SetAltTexture(2),
                   new TimedTransition(250, "H")
                   ),
               new State("H",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                   new SetAltTexture(0),
                   new TimedTransition(250, "I")
                   ),
               new State("I",
                   new SetAltTexture(2),
                   new TimedTransition(250, "J")
                   ),
               new State("J",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                   new SetAltTexture(0),
                   new TimedTransition(250, "K")
                   ),
               new State("K",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                   new SetAltTexture(1),
                   new Shoot(20, 8, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 1000, coolDown: 6000),
                   new Shoot(20, 10, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 2000, coolDown: 6000),
                   new Shoot(20, 12, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 3000, coolDown: 6000),
                   new Shoot(20, 14, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 4000, coolDown: 6000),
                   new Shoot(20, 16, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 5000, coolDown: 6000),
                   new Shoot(20, 18, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 6000, coolDown: 6000),
                   new Shoot(20, 20, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 7000, coolDown: 6000),
                   new TimedTransition(7000, "spawn minions")
                   ),
               new State("spawn minions",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                   new Order(30, N("ic boss purifier generator"), "spawn minion plox <3"),
                   new SetAltTexture(1),
                   new TimedTransition(2000, "spawn minions2")
                   ),
               new State("spawn minions2",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                   new SetAltTexture(1),
                   new EntityNotExistsTransition(N("ic shielded king"), 30, "kill me time...")
                   ),
               new State("kill me time...",
                   new Orbit(2.0, 1, 10, N("ic boss manager")),
                   new SetAltTexture(2),
                   // Unbound Rend (Elder): the demon lets go of him for a while and he fights
                   // back as himself: fans of icicles at the nearest player, a turning ring of
                   // ice bursts, and a quieting boomerang now and then
                   Elder(new Taunt(true, "It lets go of me... only for a moment. Forgive me, I cannot stop my hands!")),
                   Elder(new Shoot(14, 5, shootAngle: 12, projectileIndex: 4, predictive: 0.5, coolDown: 1100)),
                   Elder(new Shoot(20, 12, projectileIndex: 2, fixedAngle: 0, rotateAngle: 11, coolDown: 2400, coolDownOffset: 800)),
                   Elder(new Shoot(12, 1, projectileIndex: 3, predictive: 0.7, coolDown: 3600, coolDownOffset: 1800)),
                   new TimedTransition(12000, "WTF HE DO...")
                   ),
               new State("WTF HE DO...",
                   new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                   new SetAltTexture(0),
                   new TimedTransition(250, "AA")
                   ),
               new State("AA",
                   new SetAltTexture(2),
                   new TimedTransition(250, "BB")
                   ),
              new State("BB",
                  new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                  new SetAltTexture(0),
                  new TimedTransition(250, "CC")
                  ),
              new State("CC",
                  new SetAltTexture(2),
                  new TimedTransition(250, "DD")
                  ),
              new State("DD",
                  new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                  new SetAltTexture(0),
                  new TimedTransition(250, "EE")
                   ),
              new State("EE",
                  new SetAltTexture(2),
                  new Order(30, N("ic boss purifier generator"), "spawn crosses"),
                  new TimedTransition(2000, "FF")
                  ),
              new State("FF",
                    new SetAltTexture(0),
                    new Orbit(0.1, 1, 10, N("ic boss manager")),
                    Frostfall(2200),
                    new Taunt("I can barely hold on. Please. Do it."),
                    new Taunt("HAHAHAHA"),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new EntitiesNotExistsTransition(20, "nooo!", N("ic boss purifier")),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 0, coolDownOffset: 0, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 10, coolDownOffset: 200, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 20, coolDownOffset: 400, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 30, coolDownOffset: 600, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 40, coolDownOffset: 800, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 50, coolDownOffset: 1000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 60, coolDownOffset: 1200, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 70, coolDownOffset: 1400, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 80, coolDownOffset: 1600, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 90, coolDownOffset: 1800, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 100, coolDownOffset: 2000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 110, coolDownOffset: 2200, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 120, coolDownOffset: 2400, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 130, coolDownOffset: 2600, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 140, coolDownOffset: 2800, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 150, coolDownOffset: 3000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 160, coolDownOffset: 3200, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 170, coolDownOffset: 3400, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 180, coolDownOffset: 3600, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 190, coolDownOffset: 3800, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 200, coolDownOffset: 4000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 210, coolDownOffset: 4200, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 220, coolDownOffset: 4400, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 230, coolDownOffset: 4600, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 240, coolDownOffset: 4800, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 250, coolDownOffset: 5000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 260, coolDownOffset: 5200, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 270, coolDownOffset: 5400, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 280, coolDownOffset: 5600, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 290, coolDownOffset: 5800, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 300, coolDownOffset: 6000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 310, coolDownOffset: 6200, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 320, coolDownOffset: 6400, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 330, coolDownOffset: 6600, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 340, coolDownOffset: 6800, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 1, fixedAngle: 350, coolDownOffset: 7000, coolDown: 14000),
                    new Shoot(20, 100, projectileIndex: 0, fixedAngle: fixedAngle_RingAttack2, coolDownOffset: 3500, coolDown: 14000),
                    new Taunt("Icicles, rend their flesh!"),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 0, coolDownOffset: 8000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 90, coolDownOffset: 8000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 180, coolDownOffset: 8000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 270, coolDownOffset: 8000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 45, coolDownOffset: 9000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 135, coolDownOffset: 9000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 225, coolDownOffset: 9000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 315, coolDownOffset: 9000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 0, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 90, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 180, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 270, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 45, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 135, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 225, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 7, shootAngle: 10, projectileIndex: 3, fixedAngle: 315, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 10000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 11000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 12000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 13000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 0, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 90, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 180, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 1, projectileIndex: 2, fixedAngle: 270, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 330, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 30, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 60, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 120, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 150, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 210, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 240, coolDownOffset: 14000, coolDown: 14000),
                    new Shoot(20, 2, shootAngle: 10, projectileIndex: 2, fixedAngle: 300, coolDownOffset: 14000, coolDown: 14000)
                    )
                ),
              // The Cornus Unbound (Elder, at a tenth of his health): the demon he held tears
              // free. Esben fades into mist and comes back at the heart of the sanctum, then
              // the cornus fights through him: two arms of ice bursts turning against each
              // other, rings of piercing bolts, icicles raining on everyone and two shielded
              // kings at his side. He can be killed in it; if he outlasts it, the regular end
              Elder(new State("cornus",
                  new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                  new SetAltTexture(1),
                  new Taunt(true, "No... NO! He is free! The cornus is FREE!"),
                  new Flash(Frost, 0.2, 10),
                  new ReturnToSpawn(1.5),
                  new TimedTransition(2500, "cornus2")
                  )),
              Elder(new State("cornus2",
                  new SetAltTexture(2),
                  new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                  new Taunt(true, "THIS SHELL IS MINE. AND SO ARE YOUR SOULS."),
                  new Flash(Frost, 0.5, 4),
                  new Spawn(N("ic shielded king"), maxChildren: 2, initialSpawn: 0.5, coolDown: 9000),
                  new Shoot(30, 2, shootAngle: 180, projectileIndex: 2, fixedAngle: 0, rotateAngle: 13, coolDown: 300),
                  new Shoot(30, 2, shootAngle: 180, projectileIndex: 4, fixedAngle: 90, rotateAngle: -11, coolDown: 300),
                  new Shoot(30, 16, projectileIndex: 1, fixedAngle: 11.25, coolDown: 2500, coolDownOffset: 1200),
                  new Shoot(12, 3, shootAngle: 15, projectileIndex: 3, predictive: 0.6, coolDown: 3000),
                  Frostfall(1600, 1),
                  new TimedTransition(16000, "die")
                  )),
              new State("die",
                  new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, duration: 2000),
                  new Taunt("You must destroy me. It is the only way to banish him."),
                  new Taunt("Noooo! Shades, come to me. Protect the cornus."),
                  new OrderOnce(100, N("ic boss purifier generator"), "die"),
                  new OrderOnce(100, N("ic boss manager"), "nothing"),
                  new SetAltTexture(3),
                  new Wander(1.5)
                    )
                ),
                new Threshold(0.005,
                    LootTemplates.StrongerDrop()
                    ),
                new Threshold(0.005,
                    new ItemLoot("Zombie Penguin Generator", 0.03),
                    new ItemLoot("Staff of Esben", 0.02),
                    new ItemLoot("Frosty's Walking Stick", 0.005),
                    new ItemLoot("Winter's Breath Wand", 0.005),
                    new ItemLoot("Skullish Remains of Esben", 0.016),
                    new ItemLoot("Esben's Wedding Ring", 0.016),
                    new ItemLoot("Esben's Shaman Attire", 0.016),
                    new ItemLoot("An Icicle", 0.004),
                    new ItemLoot("Potion of Mana", 1)
                ),
                // the Elder Esben: Elder gear, a Potion of Max Mana, his key and the white bag
                // the Empowered one drops more often (resurrection.json)
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Mana", 0.025),
                    new ItemLoot("Elder Ice Cave Key", 0.01),
                    new ItemLoot("Scroll of the Unwilling", 0.005)
                    ),
                ElderLoot(0.0001,
                    new TierLoot(13, ItemType.Weapon, .1),
                    new TierLoot(14, ItemType.Armor, .1),
                    new TierLoot(6, ItemType.Ability, .1),
                    new TierLoot(6, ItemType.Ring, .1)
                    ),
            new ItemLoot("Lil' Esben Generator", 0.02))
        .Init(E("ic boss purifier"),
            new State(
                new State(":P",
                    new PlayerWithinTransition(5, ":D")
                    ),
                new State(":D",
                    new Spawn(N("ic Whirlwind"), 1, 1, coolDown: 10000),
                    new Shoot(5, 1, 0, defaultAngle: 0, angleOffset: 0, projectileIndex: 0, predictive: 1,
                    coolDown: 5000)
                    )
                )
            )
        .Init(E("ic Whirlwind"),
            new State(
                new Follow(0.5, 6, 1),
                new State("shoot1",
                    new Shoot(5, 7, projectileIndex: 0, fixedAngle: fixedAngle_RingAttack2, coolDown: 2000),
                    new TimedTransition(4000, "shoot2")
                    ),
                new State("shoot2",
                    new Shoot(5, 7, projectileIndex: 1, fixedAngle: fixedAngle_RingAttack2, coolDown: 2000),
                    new TimedTransition(4000, "shoot1")
                    )
                )
            )
        .Init(E("ic shielded king"),
            new State(
                new State("idle",
                    new Taunt(probability: 0.3, text: "SKREEEEE"),
                    new Taunt(probability: 0.3, text: "SSSSSsssSsssssss"),
                    new Follow(1.0, 10, 4),
                    new Shoot(20, 1, projectileIndex: 0, coolDown: 500)
                    )
                )
            )
        .Init(E("ic CreepyTime"),
            new State(
                new Protect(1, N("ic Esben the Unwilling"), 15, 5),
                new Wander(1)
            )
        )
        .Init(E("ic boss purifier generator"),
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                new State("spawn crosses",
                    new Spawn(N("ic boss purifier"), 1, 1, coolDown: 10000)
                    ),
                new State("spawn minion plox <3",
                    new Spawn(N("ic shielded king"), maxChildren: 1, initialSpawn: 0)
                    ),
                new State("die",
                    new Spawn(N("ic CreepyTime"), maxChildren: 1, initialSpawn: 0),
                    new Decay(1000)
                    )
                )
            );
        }
    }
}
