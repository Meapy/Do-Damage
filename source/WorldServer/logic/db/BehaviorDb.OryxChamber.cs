using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Oryx the Mad God 1 in his Chamber, as the original game's server ran him (release 7.0,
    // oryxChamber.xml): nine attacks drawn at random, never one of his last three, each after five
    // invulnerable seconds to get clear (none after the Hadouken), shields that drop for a second
    // or so at a time, and below 6% his last stand with his Assassins. Shots of type "auto" there
    // start their spread at their angle, ours centre on it: Auto() converts.
    partial class BehaviorDb
    {
        private static readonly object O1Picks = new object();
        private const string O1 = "Oryx the Mad God 1";
        private const string O4 = "Oryx the Mad God 4";

        // the fixedAngle that fires an "auto" spread of n shots, inc degrees apart, as the original did
        private static double Auto(int n, double inc, double angle = 90) => angle + inc * ((n - 1) / 2.0 - n / 2);

        // how an Oryx fights Oryx 1's fight (OryxFight): Oryx 1 as the original game ran him by default
        private sealed class OryxSpec
        {
            public int Pause = 5000;            // the invulnerable warning before each attack, ms
            public double AllIn = 0.06;         // his last stand below this share of health
            public bool Bubble = true;          // taunts over his head as well as in chat
            public string Inner;                // the Artifact Dance's close elements (null: Guardian Elements 1-4)
            public string Outer = "Outer Guardian Element";
            public string[] AllInTaunt = { "THIS SHALL BE YOUR GRAVE!!!", "I HAVE HAD ENOUGH OF YOU!!!", "DIE!!!", "ENOUGH!!!" };
            public int Heal = 2500;             // healed every 0.4 s for 3 s as his last stand starts
            public int Size = 160;              // how big he grows for it
            public IStateChildren[] Fight = new IStateChildren[0];   // added throughout the fight
            public IStateChildren[] Minions = new IStateChildren[0]; // added to Make Minions
            public IStateChildren[] Hunt = new IStateChildren[0];    // added to his last stand's hunt
        }

        // invulnerable for `on` ms, open for `off`, again and again
        private static State[] O1Shield(string p, int on, int off) => new[]
        {
            new State(p + " Shut", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(on, p + " Open")),
            new State(p + " Open", new TimedTransition(off, p + " Shut"))
        };

        // the invulnerable seconds before an attack (five for Oryx 1), with the line that warns of it
        private static State O1Pause(OryxSpec o, string attack, params string[] taunt)
        {
            var c = new List<IStateChildren>
            {
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new StayCloseToSpawn(0.4, 5),
                new Wander(0.4),
                new Flash(0xFF00FF00, 0.2, 20),
                new TimedTransition(o.Pause, attack)
            };
            if (taunt.Length > 0)
                c.Add(new Taunt(taunt) { Bubble = o.Bubble });
            return new State("Pre " + attack, c.ToArray());
        }

        // where he goes after an attack: pools as the original's, the low one below 55%
        private static State O1Chooser(string name, string prefix)
        {
            string[] P(params string[] s) => s.Select(x => prefix + x).ToArray();
            return new State(name,
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new State(name + " Pool",
                    new HpLessTransition(0.55, name + " Low"),
                    new TimedRandomTransition(0, false, name + " A", name + " B")
                ),
                new State(name + " Low", new FreshRandomTransition(O1Picks, 3, "Pre ",
                    P("Hadouken", "Minion Toss", "Bullet Dance", "Orbitals", "Chase Course", "White Planet"))),
                new State(name + " A", new FreshRandomTransition(O1Picks, 3, "Pre ",
                    P("Circle Combat", "Artifact Dance", "Hadouken", "Minion Toss", "White Planet"))),
                new State(name + " B", new FreshRandomTransition(O1Picks, 3, "Pre ",
                    P("Circle Combat", "Artifact Dance", "Hadouken", "Minion Toss", "Bullet Dance", "Orbitals", "Chase Course")))
            );
        }

        // a ring of 15 Ring Elements around him, range r
        private static IEnumerable<IStateChildren> O1Ring(double r) =>
            Enumerable.Range(0, 15).Select(i => (IStateChildren)new TossObject("Ring Element", r, i * 24, 90000));

        // Circle Combat: a turn of the grenade dance; the first lays the silencing and weakening rings
        private static State O1Grenades(int i)
        {
            var a = new[] { 0, 210, 70, 0, 110, 325 }[i];
            var b = new[] { 0, 30, 250, 180, 290, 145 }[i];
            var c = new List<IStateChildren> { new TimedTransition(i == 0 ? 1000 : 1200, "CC Grenade " + (i + 1) % 6) };
            if (i == 0 || i >= 3)
                c.Add(new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable));
            if (i == 0)
            {
                c.Add(new Shoot(0, 36, 10, 13, fixedAngle: Auto(36, 10, 0), coolDown: 90000));
                c.Add(new Shoot(0, 36, 10, 14, fixedAngle: Auto(36, 10, 5), coolDownOffset: 600, coolDown: 90000));
                c.Add(new Shoot(0, 15, 24, 0, fixedAngle: Auto(15, 24), coolDownOffset: 800, coolDown: 90000));
            }
            else
            {
                c.Add(new Shoot(0, 15, 24, 0, fixedAngle: Auto(15, 24), coolDownOffset: 400, coolDown: 600));
                c.Add(new Grenade(2.5, 220, 5.5, a, 90000));
                c.Add(new Grenade(2.5, 220, 4.5, b, 90000));
                c.Add(new Grenade(2.5, 220, 8, coolDown: 90000));
            }
            return new State("CC Grenade " + i, c.ToArray());
        }

        // Bullet Dance: a turn of crosses and a grenade; on every other round the second turn also lays
        // the silencing and weakening rings
        private static State O1Bullets(int round, int i)
        {
            var name = "BD " + round + " " + i;
            var next = i == 5 ? "BD " + (1 - round) + " 0" : "BD " + round + " " + (i + 1);
            var grenade = new[] { 60, 240, 0, 180, 300, 120 }[i];
            var crosses = new[]
            {
                new[] { (0.0, 200), (45.0, 600) },
                new[] { (7.5, 600), (52.5, 200), (75.0, 0) },
                new[] { (15.0, 200), (60.0, 600) },
                new[] { (22.5, 600), (67.5, 200), (90.0, 0) },
                new[] { (30.0, 200), (75.0, 600) },
                new[] { (37.5, 600), (82.5, 200), (105.0, 0) }
            }[i];
            var c = new List<IStateChildren>
            {
                new Grenade(2.5, 220, 4.5, grenade, 90000),
                new TimedTransition(700, next)
            };
            foreach (var (angle, offset) in crosses)
                c.Add(new Shoot(0, 4, 90, 12, fixedAngle: Auto(4, 90, angle), coolDownOffset: offset, coolDown: 350));
            if (i >= 2)
                c.Add(new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable));
            if (i == 1 && round == 0)
            {
                c.Add(new Shoot(0, 36, 10, 13, fixedAngle: Auto(36, 10, 0), coolDown: 90000));
                c.Add(new Shoot(0, 36, 10, 14, fixedAngle: Auto(36, 10, 5), coolDownOffset: 500, coolDown: 90000));
            }
            return new State(name, c.ToArray());
        }

        // four Anti-Spectators, one every `gap` ms: they circle 12 tiles out, punishing anyone who
        // stands back to watch
        private static State[] O1AntiSpectators(string p, int gap) => Enumerable.Range(0, 4).Select(i =>
            i < 3
                ? new State(p + " Anti " + i, new Spawn("Anti-Spectator", 1, 1, 90000), new TimedTransition(gap, p + " Anti " + (i + 1)))
                : new State(p + " Anti " + i, new Spawn("Anti-Spectator", 1, 1, 90000))).ToArray();

        // White Planet's opening and returning volleys
        private static IStateChildren[] O1PlanetVolleys() => new IStateChildren[]
        {
            new Shoot(0, 24, 15, 6, fixedAngle: Auto(24, 15, 0), coolDown: 2000),
            new Shoot(0, 36, 10, 13, fixedAngle: Auto(36, 10, 0), coolDownOffset: 600, coolDown: 90000),
            new Shoot(0, 10, 36, 16, fixedAngle: Auto(10, 36, 0), coolDownOffset: 1000, coolDown: 2000),
            new Shoot(0, 10, 36, 16, fixedAngle: Auto(10, 36, 18), coolDownOffset: 2000, coolDown: 2000)
        };

        // a helper that fires one ring when Oryx goes all in, then is gone. Oryx orders "Final" every
        // tick, so the end lies inside it: a state of its own would be ordered back to the start.
        private static State O1Final(int count, int projectile) =>
            new State("Final",
                new Shoot(0, count, projectileIndex: projectile, fixedAngle: 0, coolDownOffset: 200, coolDown: 90000),
                new State("Final Burst", new TimedTransition(400, "Final Gone")),
                new State("Final Gone", new Suicide())
            );

        private _ OryxChamber = () => Behav()
            // Stage 3 of the Oryx chain
            .Init(O1,
                new State(new IStateChildren[]
                {
                    new BossStats(100000, 35, 1.0, 0.3),
                    // open: Oryx 2 needs no incantation
                    new DropPortalOnDeath("Wine Cellar Portal", 1.0, timeout: 600, xAdjustment: 2),
                    new AnnounceOnDeath("{PL_LIST} defeated Oryx the Mad God in his Chamber! He retreats to his Wine Cellar, and the way lies open..."),
                    new State("Wait",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(12, "Intro")
                    ),
                    new State("Intro",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Flash(0xFF389E13, 0.5, 5),
                        new Taunt("You dare enter my Chamber? Fools! I still have {HP} hitpoints!"),
                        new TimedTransition(3000, "Old Times")
                    )
                }.Concat(OryxFight(new OryxSpec())).ToArray()
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Life", 0.15)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Dexterity", 0.45),
                    new ItemLoot("Potion of Speed", 0.45),
                    new ItemLoot("Potion of Vitality", 0.25),
                    new ItemLoot("Potion of Wisdom", 0.25),
                    // his Lil' Oryx, in a white bag (each Oryx drops his own)
                    new ItemLoot("Lil' Oryx Generator", 0.01)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(11, ItemType.Weapon, 0.12),
                    new TierLoot(12, ItemType.Weapon, 0.06),
                    new TierLoot(5, ItemType.Ability, 0.1),
                    new TierLoot(6, ItemType.Ability, 0.03),
                    new TierLoot(12, ItemType.Armor, 0.1),
                    new TierLoot(13, ItemType.Armor, 0.04),
                    new TierLoot(5, ItemType.Ring, 0.08),
                    new TierLoot(6, ItemType.Ring, 0.03)
                )
            )

            // ---- his helpers ----

            // a warning ring of heavy shots, then lighter ones; gone after 19 seconds. Oryx 4 (the
            // Inferno) throws them too.
            .Init("Ring Element",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new State("Live",
                        new TimedTransition(19000, "Final Gone"),
                        new State("Setup", new TimedTransition(1000, "Warning")),
                        new State("Warning",
                            new Shoot(0, 12, 30, 9, fixedAngle: Auto(12, 30), coolDown: 800),
                            new TimedTransition(1000, "Attacking")
                        ),
                        new State("Attacking",
                            new Shoot(0, 12, 30, 0, fixedAngle: Auto(12, 30), coolDown: 800)
                        )
                    ),
                    new State("Final", new State("Final Gone", new Suicide()))
                )
            )
            // circle and shoot; with Oryx gone they fall apart. They live 15 seconds.
            .Init("Minion of Oryx",
                new State(
                    new Decay(15000),
                    new State("With Oryx",
                        new EntitiesNotExistsTransition(40, "No Oryx", O1, O4),
                        new Shoot(8, 3, 12, 0, coolDown: 800),
                        new Shoot(8, 2, 180, 1, predictive: 0.35, coolDownOffset: 400, coolDown: 1000),
                        new State("Roam", new Wander(0.6), new TimedTransition(3000, "Circle")),
                        new State("Circle",
                            new Prioritize(new Circle(0.8, 3.5, 8), new Wander(0.8)),
                            new TimedTransition(4000, "Roam")
                        )
                    ),
                    new State("No Oryx",
                        new SetAltTexture(1),
                        new Flash(0xFFFFFFFF, 0.2, 10),
                        new Shoot(0, 5, projectileIndex: 0, fixedAngle: 0, coolDownOffset: 2000, coolDown: 90000),
                        new TimedTransition(2200, "Gone")
                    ),
                    new State("Gone", new Suicide())
                ),
                new TierLoot(7, ItemType.Weapon, 0.2),
                new ItemLoot("Magic Potion", 0.03)
            )
            // they circle Oryx 12 tiles out and dart in at whoever is near; they live 20 seconds
            .Init("Assassin of Oryx",
                new State(
                    new Decay(20000),
                    new State("Hunting",
                        new EntitiesNotExistsTransition(40, "No Oryx", O1, O4),
                        new State("Arrive",
                            new Charge(1.6, 20, 2400),
                            new TimedRandomTransition(800, false, "Orbit Oryx", "Chase Attack")
                        ),
                        new State("Orbit Oryx",
                            OryxOrbit(1.4, 12, 30, 0.5, 1.5),
                            new Shoot(9, 4, 90, 1, coolDown: 1000),
                            new TimedTransition(2200, "Chase Attack")
                        ),
                        new State("Chase Attack",
                            new Prioritize(new Charge(1.6, 12, 2400), new Circle(1.4, 3, 15)),
                            new Shoot(6, 3, 20, 0, predictive: 0.75, coolDown: 1000),
                            new Shoot(9, 4, 90, 1, coolDownOffset: 400, coolDown: 3200),
                            new TimedTransition(7000, "Orbit Oryx")
                        )
                    ),
                    // Oryx's last stand: faster, wider, and two kinds of shot at once
                    new State("Final",
                        new EntitiesNotExistsTransition(40, "No Oryx", O1, O4),
                        new State("Final Arrive",
                            new Charge(1.6, 20, 2400),
                            new TimedRandomTransition(800, false, "Final Orbit", "Final Chase")
                        ),
                        new State("Final Orbit",
                            OryxOrbit(1.8, 13, 30, 0.4, 1.5),
                            new Shoot(9, 4, 90, 1, coolDown: 1000),
                            new Shoot(7, 3, 20, 0, predictive: 0.75, coolDown: 3200),
                            new TimedTransition(5000, "Final Chase")
                        ),
                        new State("Final Chase",
                            new Prioritize(new Charge(1.4, 12, 2400), new Circle(1, 3, 15)),
                            new Shoot(7, 3, 20, 0, predictive: 0.75, coolDown: 1000),
                            new Shoot(9, 4, 90, 1, coolDownOffset: 400, coolDown: 3200),
                            new TimedTransition(5000, "Final Orbit")
                        )
                    ),
                    new State("No Oryx",
                        new SetAltTexture(1),
                        new Flash(0xFFFFFFFF, 0.2, 10),
                        new Shoot(0, 4, 90, 1, fixedAngle: 0, coolDownOffset: 2000, coolDown: 90000),
                        new TimedTransition(2200, "Gone")
                    ),
                    new State("Gone", new Suicide())
                )
            )
            // circle Oryx 12 tiles out fast for 18 seconds
            .Init("Anti-Spectator",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Decay(18000),
                    OryxOrbit(4, 12, 20),
                    new Shoot(0, 10, 36, 0, fixedAngle: Auto(10, 36, 0), coolDownOffset: 1500, coolDown: 700),
                    new Shoot(0, 10, 36, 0, fixedAngle: Auto(10, 36, 18), coolDownOffset: 1850, coolDown: 700)
                )
            )
            .Init("Guardian Element 1", new State(O1Guardian(0, O1)))
            .Init("Guardian Element 2", new State(O1Guardian(90, O1)))
            .Init("Guardian Element 3", new State(O1Guardian(180, O1)))
            .Init("Guardian Element 4", new State(O1Guardian(270, O1)))
            .Init("Outer Guardian Element",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Decay(32000),
                    OryxOrbit(3.2, 8.5, 20),
                    new Shoot(3, 3, 9, 0, predictive: 0.45, coolDown: 1200)
                )
            )
            // swing between 9 and 3 tiles from Oryx; burst after 19 seconds
            .Init("White Planet",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Shoot(0, 10, 36, 0, fixedAngle: Auto(10, 36, 0), coolDownOffset: 1400, coolDown: 1000),
                    new State("Live",
                        new TimedTransition(19000, "Burst"),
                        new State("Wide", OryxOrbit(0.75, 9, 15), new TimedTransition(6000, "Small")),
                        new State("Small", OryxOrbit(0.75, 3, 15), new TimedTransition(6000, "Wide"))
                    ),
                    new State("Burst",
                        new Shoot(0, 8, projectileIndex: 1, fixedAngle: 0, coolDown: 90000),
                        new TimedTransition(200, "Final Gone")
                    ),
                    O1Final(6, 1)
                )
            )
            // still until Oryx wakes them, then rings and crosses until they fade
            .Init("Chase Element",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Decay(19600),
                    new State("Setup"),
                    new State("Attacking",
                        new Shoot(0, 10, 36, 0, fixedAngle: Auto(10, 36), coolDown: 800),
                        new Shoot(0, 4, 90, 1, fixedAngle: Auto(4, 90, 0), coolDown: 2400)
                    ),
                    O1Final(4, 1)
                )
            )
            .Init("Black Planet",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Spawn("Black Moon", 1, 1, 90000),
                    new State("Live",
                        OryxOrbit(0.2, 9.5, 20),
                        new Shoot(0, 10, 36, 0, fixedAngle: Auto(10, 36, 0), coolDownOffset: 1850, coolDown: 800),
                        new TimedTransition(21000, "Burst")
                    ),
                    new State("Burst",
                        new Shoot(0, 8, projectileIndex: 1, fixedAngle: 0, coolDown: 90000),
                        new TimedTransition(200, "Final Gone")
                    ),
                    O1Final(8, 1)
                )
            )
            .Init("Black Moon",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new State("Live",
                        new Orbit(1.5, 3.5, 4, "Black Planet", 0, 0),
                        new Shoot(5, 3, 15, 0, coolDownOffset: 200, coolDown: 1000),
                        new TimedTransition(20000, "Final Gone")
                    ),
                    new State("Final",
                        new State("Final Wait", new TimedTransition(200, "Final Gone")),
                        new State("Final Gone", new Suicide())
                    )
                )
            );

        // Oryx 1's fight from his first draw on, and his last stand; Oryx 4 (the Inferno) fights it too
        private static IStateChildren[] OryxFight(OryxSpec o) => new IStateChildren[]
        {
            new State("Fight", new IStateChildren[]
            {
                new HpLessTransition(o.AllIn, "All In"),
                // his opening: the original counted the Hadouken and the Minion Toss as already
                // used, so his first draw is neither
                new State("Old Times",
                    new RememberPicks(O1Picks, 3, "Hadouken", "Minion Toss", "Make Minions"),
                    new StayCloseToSpawn(0.4, 5),
                    new Wander(0.4),
                    new Shoot(12, 9, 9, 3, predictive: 0.5, coolDownOffset: 1000, coolDown: 800),
                    new Shoot(12, 1, projectileIndex: 4, coolDownOffset: 1000, coolDown: 800),
                    new HpLessTransition(0.97, "Make Minions")
                ),
                O1Chooser("Choose", "Pre "),
                // after the Hadouken: straight into the next attack
                O1Chooser("Choose Now", ""),
                O1Pause(o, "Circle Combat"),
                O1Pause(o, "Artifact Dance", "My artifacts will protect me!"),
                O1Pause(o, "Hadouken", "Insufferable fools! If I see you, I will kill you!"),
                O1Pause(o, "Minion Toss", "Bah! I need no minions to destroy you!!", "I will finish you off myself!",
                    "These minions will only get in my way!!!", "Fools! I will deal with you PERSONALLY!"),
                O1Pause(o, "Bullet Dance"),
                O1Pause(o, "Orbitals", "Tremble before my cosmic might!!!"),
                O1Pause(o, "Chase Course"),
                O1Pause(o, "White Planet"),

                new State("Make Minions", new IStateChildren[]
                {
                    new Taunt(0.75, "Go forth minions! Crush these weaklings!!!") { Bubble = o.Bubble },
                    new Taunt(1, 9000, "Fools! I still have {HP} hitpoints!", "Puny mortals! My {HP} hitpoints are more than enough!",
                        "I have {HP} hitpoints and I shall destroy you!", "Insignificant peons! I have {HP} hitpoints!") { Bubble = o.Bubble },
                    new StayCloseToSpawn(0.4, 5),
                    new Wander(0.4),
                    new KeepMinions("Minion of Oryx", 5, 300),
                    new Shoot(22, 4, 9, 4, coolDown: 800),
                    new TimedTransition(12000, "Choose"),
                    new State("MM Shut", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(6000, "MM Open")),
                    new State("MM Open", new TimedTransition(1500, "MM Shut Again"), new HpLessTransition(0.45, "MM Shut Again")),
                    new State("MM Shut Again", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable))
                }.Concat(o.Minions).ToArray()),

                // inside a ring of elements, under grenades; minions and Anti-Spectators outside it
                new State("Circle Combat",
                    new Taunt("BE SILENT!!!") { Bubble = o.Bubble },
                    new TimedTransition(21000, "Choose"),
                    new State("CC Setup",
                        new[]
                        {
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            (IStateChildren)new TimedTransition(4000, "CC Grenade 0")
                        }
                        .Concat(Enumerable.Range(0, 5).Select(i => (IStateChildren)new TossObject("Minion of Oryx", 20, i * 72, 90000)))
                        .Concat(O1Ring(8.5))
                        .Concat(O1AntiSpectators("CC", 600)).ToArray()
                    ),
                    O1Grenades(0), O1Grenades(1), O1Grenades(2), O1Grenades(3), O1Grenades(4), O1Grenades(5)
                ),

                // four Guardian Elements circle him close and three Outer ones further out
                new State("Artifact Dance",
                    new TossObject(o.Inner ?? "Guardian Element 1", 2, 0, 90000),
                    new TossObject(o.Inner ?? "Guardian Element 2", 2, 90, 90000),
                    new TossObject(o.Inner ?? "Guardian Element 3", 2, 180, 90000),
                    new TossObject(o.Inner ?? "Guardian Element 4", 2, 270, 90000),
                    new TossObject(o.Outer, 9, 120, 90000),
                    new TossObject(o.Outer, 9, 240, 90000),
                    new TossObject(o.Outer, 9, 0, 90000),
                    new Shoot(0, 10, 30, 7, fixedAngle: Auto(10, 30), coolDownOffset: 1500, coolDown: 500),
                    new TimedTransition(32000, "Choose"),
                    new State("AD Attack 1", O1Shield("AD A1", 2500, 1000)
                        .Concat(new IStateChildren[] { new Shoot(8, 3, 120, 9, coolDownOffset: 1500, coolDown: 900), new TimedTransition(6000, "AD Defended 1") }).ToArray()),
                    new State("AD Defended 1", O1Shield("AD D1", 2500, 1000)
                        .Concat(new IStateChildren[] { new Shoot(8, 6, 45, 8, coolDown: 1500), new TimedTransition(5000, "AD Attack 2") }).ToArray()),
                    new State("AD Attack 2", O1Shield("AD A2", 2500, 1000)
                        .Concat(new IStateChildren[] { new Shoot(8, 3, 120, 9, coolDownOffset: 1500, coolDown: 900), new TimedTransition(6000, "AD Defended 2") }).ToArray()),
                    new State("AD Defended 2", O1Shield("AD D2", 2500, 1000)
                        .Concat(new IStateChildren[] { new Shoot(8, 6, 45, 8, coolDown: 1500), new TimedTransition(5000, "AD Attack 3") }).ToArray()),
                    new State("AD Attack 3",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Shoot(8, 3, 120, 9, coolDownOffset: 1500, coolDown: 900)
                    )
                ),

                // his flames for anyone within 10 tiles; below 55% bubbles in every direction too
                new State("Hadouken",
                    new Taunt("All who look upon my face shall die.") { Bubble = o.Bubble },
                    new Shoot(10, 2, 10, 1, predictive: 0.6, coolDown: 500),
                    new State("HK Which",
                        new HpLessTransition(0.55, "HK Big"),
                        new TimedTransition(0, "HK Regular")
                    ),
                    new State("HK Big",
                        new[]
                        {
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 0, coolDownOffset: 200, coolDown: 1600),
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 180, coolDownOffset: 400, coolDown: 1600),
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 270, coolDownOffset: 600, coolDown: 1600),
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 90, coolDownOffset: 800, coolDown: 1600),
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 225, coolDownOffset: 1000, coolDown: 1600),
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 45, coolDownOffset: 1200, coolDown: 1600),
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 135, coolDownOffset: 1400, coolDown: 1600),
                            new Shoot(0, 1, projectileIndex: 5, fixedAngle: 315, coolDownOffset: 1600, coolDown: 1600),
                            (IStateChildren)new TimedTransition(13500, "Choose Now")
                        }.Concat(O1Shield("HK B", 3500, 1000)).ToArray()
                    ),
                    new State("HK Regular",
                        new TimedTransition(7500, "Choose Now"),
                        new State("HK R Shut", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(3500, "HK R Open")),
                        new State("HK R Open", new TimedTransition(1000, "HK R Shut Again")),
                        new State("HK R Shut Again", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable))
                    )
                ),

                // he throws minions and Assassins around him, wider each time
                new State("Minion Toss",
                    new Taunt(1, 9000, "Fools! I still have {HP} hitpoints!", "Puny mortals! My {HP} hitpoints are more than enough!",
                        "I have {HP} hitpoints and I shall destroy you!", "Insignificant peons! I have {HP} hitpoints!") { Bubble = o.Bubble },
                    new StayCloseToSpawn(0.6, 5),
                    new Wander(0.6),
                    new Shoot(8.5, 2, 180, 2, predictive: 0.5, coolDown: 1000),
                    new Shoot(8.5, 1, projectileIndex: 2, predictive: 0.5, coolDown: 1000),
                    new Shoot(22, 4, 9, 4, coolDown: 2400),
                    new TimedTransition(15000, "Choose"),
                    new State("MT 0", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(2000, "MT 1")),
                    new State("MT 1", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TossObject("Minion of Oryx", 3, 30, 90000), new TossObject("Minion of Oryx", 3, 180, 90000),
                        new TossObject("Assassin of Oryx", 3, 330, 90000), new TimedTransition(400, "MT 2")),
                    new State("MT 2", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TossObject("Minion of Oryx", 4, 120, 90000), new TossObject("Minion of Oryx", 4, 260, 90000),
                        new TossObject("Assassin of Oryx", 4, 40, 90000), new TimedTransition(400, "MT 3")),
                    new State("MT 3", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TossObject("Minion of Oryx", 6, 210, 90000), new TossObject("Minion of Oryx", 6, 300, 90000),
                        new TossObject("Assassin of Oryx", 6, 120, 90000), new TimedTransition(400, "MT 4")),
                    new State("MT 4",
                        new TossObject("Minion of Oryx", 11, 0, 90000), new TossObject("Minion of Oryx", 11, 120, 90000),
                        new TossObject("Assassin of Oryx", 11, 240, 90000), new TimedTransition(4000, "MT Watch")),
                    new State("MT Watch", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable))
                ),

                // crosses spinning out from him inside a ring of elements, with grenades
                new State("Bullet Dance",
                    new Taunt("Time for more dancing! Hahahaha!!") { Bubble = o.Bubble },
                    new TimedTransition(21000, "Choose"),
                    new State("BD Setup",
                        new[]
                        {
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            (IStateChildren)new TimedTransition(2000, "BD 0 0")
                        }
                        .Concat(O1Ring(8))
                        .Concat(O1AntiSpectators("BD", 500)).ToArray()
                    ),
                    O1Bullets(0, 0), O1Bullets(0, 1), O1Bullets(0, 2), O1Bullets(0, 3), O1Bullets(0, 4), O1Bullets(0, 5),
                    O1Bullets(1, 0), O1Bullets(1, 1), O1Bullets(1, 2), O1Bullets(1, 3), O1Bullets(1, 4), O1Bullets(1, 5)
                ),

                // eight Black Planets, each with its moon, drift slowly round him
                new State("Orbitals",
                    new[]
                    {
                        new Shoot(0, 3, 120, 15, fixedAngle: Auto(3, 120, 0), coolDownOffset: 400, coolDown: 1000),
                        new Shoot(8, 3, 120, 16, coolDownOffset: 1200, coolDown: 1000),
                        new Shoot(0, 10, 36, 19, fixedAngle: Auto(10, 36, 0), coolDownOffset: 4000, coolDown: 5200),
                        new Shoot(0, 10, 36, 19, fixedAngle: Auto(10, 36, 18), coolDownOffset: 4600, coolDown: 5200),
                        (IStateChildren)new TimedTransition(23000, "Choose")
                    }
                    .Concat(Enumerable.Range(0, 8).Select(i => (IStateChildren)new TossObject("Black Planet", 9.5, i * 45, 90000)))
                    .Concat(O1Shield("OB", 3600, 1000)).ToArray()
                ),

                // a field of Chase Elements; he charges through it
                new State("Chase Course",
                    new TimedTransition(25000, "Choose"),
                    new State("CH Start",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1),
                        new Taunt("FOOL! Flee while you can!!!", "You shall never see the light of day!",
                            "Yes, run for your worthless lives!!!", "You WILL fear me!!!") { Bubble = o.Bubble },
                        new TimedTransition(1600, "CH Throw")
                    ),
                    new State("CH Throw",
                        new[]
                        {
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            (IStateChildren)new TimedTransition(2000, "CH Wake")
                        }
                        .Concat(new[] { (8.0, 0), (11.0, 45), (16.0, 0), (22.0, 45) }
                            .SelectMany(r => Enumerable.Range(0, 4).Select(i => (IStateChildren)new TossObject("Chase Element", r.Item1, r.Item2 + i * 90, 90000))))
                        .ToArray()
                    ),
                    new State("CH Wake",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new Order(30, "Chase Element", "Attacking"),
                        new TimedTransition(200, "CH Chase")
                    ),
                    new State("CH Chase",
                        new[]
                        {
                            new Prioritize(
                                new StayCloseToSpawn(1.6, 23),
                                new Charge(1.2, 10, 100)
                            ),
                            new Shoot(8, 5, 25, 18, predictive: 1, coolDownOffset: 200, coolDown: 2000),
                            new Shoot(8, 2, 30, 17, coolDownOffset: 600, coolDown: 1200),
                            (IStateChildren)new Shoot(8, 3, 20, 17, coolDownOffset: 1200, coolDown: 1200)
                        }.Concat(O1Shield("CH", 3600, 1000)).ToArray()
                    )
                ),

                // White Moons race round him far out, White Planets swing in and out
                new State("White Planet",
                    new TimedTransition(20000, "Choose"),
                    new State("WP Start",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1),
                        new Taunt("I am the master of this existence!!!", "I control your fate!", "The universe bends to my will alone!!!") { Bubble = o.Bubble },
                        new TimedTransition(1600, "WP Throw")
                    ),
                    new State("WP Throw",
                        new[]
                        {
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            (IStateChildren)new TimedTransition(200, "WP Attack")
                        }
                        .Concat(Enumerable.Range(0, 4).Select(i => (IStateChildren)new TossObject("White Moon", 13.4, i * 90, 90000)))
                        .Concat(Enumerable.Range(0, 4).Select(i => (IStateChildren)new TossObject("White Planet", 9, 45 + i * 90, 90000)))
                        .ToArray()
                    ),
                    new State("WP Attack",
                        O1PlanetVolleys().Concat(new IStateChildren[] { new TimedTransition(4000, "WP Cross") }).Concat(O1Shield("WP A", 3600, 1000)).ToArray()),
                    new State("WP Cross",
                        Enumerable.Range(0, 15).Select(i => (IStateChildren)new Shoot(0, 4, 90, 5, fixedAngle: Auto(4, 90, i * 12), coolDownOffset: i * 200, coolDown: 3000))
                        .Concat(new IStateChildren[]
                        {
                            new TimedTransition(8000, "WP Return"),
                            new State("WP C Shut", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(1400, "WP C Open")),
                            new State("WP C Open", new TimedTransition(1400, "WP C Shut Again")),
                            new State("WP C Shut Again", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(2200, "WP C Shut"))
                        }).ToArray()
                    ),
                    new State("WP Return",
                        O1PlanetVolleys().Concat(new IStateChildren[]
                        {
                            new Taunt(0.8, "You have uttered your last pathetic whimper!", "SILENCE!", "Speak no more, fools!", "You have spoken your last!") { Bubble = o.Bubble },
                            new TimedTransition(7000, "WP Cross"),
                            new State("WP R Shut", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(1600, "WP R Open")),
                            new State("WP R Open", new TimedTransition(800, "WP R Shut Again")),
                            new State("WP R Shut Again", new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable), new TimedTransition(1600, "WP R Shut"))
                        }).ToArray()
                    )
                )
            }.Concat(o.Fight).ToArray()),

            // at his last stand (6% for Oryx 1): his helpers burst, he heals a little, grows, and hunts with his Assassins
            new State("All In",
                new Order(40, "White Moon", "Final"),
                new Order(40, "White Planet", "Final"),
                new Order(40, "Chase Element", "Final"),
                new Order(40, "Black Planet", "Final"),
                new Order(40, "Black Moon", "Final"),
                new Order(40, "Ring Element", "Final"),
                new Order(40, "Assassin of Oryx", "Final"),
                new State("AI Start",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new ReturnToSpawn(1),
                    new Taunt(o.AllInTaunt) { Bubble = o.Bubble },
                    new HealSelf(400, o.Heal),
                    new TimedTransition(3000, "AI Summon")
                ),
                new State("AI Summon",
                    new Flash(0xFFFF0000, 0.2, 10),
                    new ChangeSize(10, o.Size),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Shoot(10, 12, 30, 18, predictive: 0.5, coolDownOffset: 200, coolDown: 1000),
                    new TossObject("Assassin of Oryx", 4.3, 270, 90000),
                    new TossObject("Assassin of Oryx", 4.3, 150, 90000),
                    new TossObject("Assassin of Oryx", 4.3, 30, 90000),
                    new TimedTransition(1000, "AI Hunt")
                ),
                new State("AI Hunt", new IStateChildren[]
                {
                    new ChangeSize(10, o.Size),
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                    new Flash(0xFFFF0000, 0.2, 12),
                    new KeepMinions("Assassin of Oryx", 5, 5000),
                    new Prioritize(
                        new Charge(1.2, 10, 800),
                        new Wander(1)
                    ),
                    new Shoot(14, 2, 16, 1, predictive: 0.75, coolDown: 1200),
                    new Shoot(8, 6, 25, 18, predictive: 1, coolDownOffset: 3000, coolDown: 3400),
                    new Shoot(8, 4, 90, 17, predictive: 0.6, coolDownOffset: 1000, coolDown: 2000),
                    new State("AI Toss 1",
                        new TossObject("Minion of Oryx", 3, 45, 90000),
                        new TossObject("Minion of Oryx", 3, 225, 90000),
                        new TimedTransition(2200, "AI Toss 2")
                    ),
                    new State("AI Toss 2",
                        new TossObject("Minion of Oryx", 3, 135, 90000),
                        new TossObject("Minion of Oryx", 3, 315, 90000),
                        new TimedTransition(2200, "AI Wait")
                    ),
                    new State("AI Wait", new TimedTransition(12000, "AI Toss 1"))
                }.Concat(o.Hunt).ToArray())
            )
        };

        // Guardian Elements 1-4: circle Oryx 2 tiles out, shooting one way, then grow and spin fast
        // with rings, twice, and burst after about 32 seconds. Oryx 4's Infernal Elements do the same
        // round him, aiming their shots (angle null).
        private static IStateChildren[] O1Guardian(double? angle, string oryx)
        {
            Shoot Single(double turn) => angle == null
                ? new Shoot(10, 1, projectileIndex: 0, predictive: 0.3, coolDown: 600)
                : new Shoot(0, 1, projectileIndex: 0, fixedAngle: angle + turn, coolDown: 600);
            return new IStateChildren[]
            {
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new State("Setup", new Orbit(1, 2, 10, oryx, 0, 0), new TimedTransition(2000, "Attacking 1")),
                new State("Attacking 1",
                    new Orbit(1.5, 2, 10, oryx, 0, 0),
                    Single(0),
                    new TimedTransition(6000, "Shielding 1")
                ),
                new State("Shielding 1",
                    new ChangeSize(11, 230),
                    new Orbit(5, 2, 10, oryx, 0, 0),
                    new Shoot(0, 5, 72, 0, fixedAngle: Auto(5, 72), coolDown: 1200),
                    new TimedTransition(5000, "Attacking 2")
                ),
                new State("Attacking 2",
                    new ChangeSize(-11, 130),
                    new Orbit(1.5, 2, 10, oryx, 0, 0),
                    Single(45),
                    new TimedTransition(6000, "Shielding 2")
                ),
                new State("Shielding 2",
                    new ChangeSize(11, 230),
                    new Orbit(5, 2, 10, oryx, 0, 0),
                    new Shoot(0, 5, 72, 0, fixedAngle: Auto(5, 72), coolDown: 1200),
                    new TimedTransition(5000, "Attacking 3")
                ),
                new State("Attacking 3",
                    new ChangeSize(-11, 130),
                    new Orbit(1.5, 2, 10, oryx, 0, 0),
                    Single(0),
                    new TimedTransition(6000, "Kamikaze")
                ),
                new State("Kamikaze",
                    new Flash(0xFF00FF00, 0.2, 12),
                    new Orbit(0.8, 4, 10, oryx, 0, 0),
                    new Shoot(0, 8, projectileIndex: 1, fixedAngle: 0, coolDownOffset: 2300, coolDown: 90000),
                    new TimedTransition(2500, "Gone")
                ),
                new State("Gone", new Suicide())
            };
        }

        // circling whichever Oryx fights Oryx 1's fight nearby: Oryx 1, or Oryx 4 in his Inferno
        private static IStateChildren OryxOrbit(double speed, double radius, double acquire, double speedVariance = 0, double radiusVariance = 0) =>
            new Prioritize(
                new Orbit(speed, radius, acquire, O1, speedVariance, radiusVariance),
                new Orbit(speed, radius, acquire, O4, speedVariance, radiusVariance)
            );
    }
}
