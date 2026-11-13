using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Oryx the Mad God 2 in his Wine Cellar and his Henchmen's crew, as the original game's server ran
    // them (release 7.0, testingObjects.xml and oryxWineCellar.xml), with our health, loot and arena
    // version. Oryx 1 is in BehaviorDb.OryxChamber.cs, where Auto() is.
    partial class BehaviorDb
    {
        private const string O2 = "Oryx the Mad God 2";

        // his six stars, each at its own reach (the Blade only up close); he throws them in every phase
        private static IStateChildren[] O2Stars() => new IStateChildren[]
        {
            new Shoot(12, 3, 9, 1, predictive: 0.25, coolDown: 800),
            new Shoot(6, 3, 9, 2, coolDown: 800),
            new Shoot(13, 2, 9, 3, coolDown: 1000),
            new Shoot(14, 3, 9, 4, coolDown: 1200),
            new Shoot(11, 2, 9, 5, coolDown: 800),
            new Shoot(10, 3, 9, 6, coolDown: 1500),
            new Taunt(1, 8000, "Puny mortals! My {HP} HP will annihilate you!", "Fools! I still have {HP} hitpoints!",
                "I have {HP} hitpoints and I shall destroy you!")
        };

        // above 30%: up to `henchmen` Henchmen, one every 12 s, and two rings of Fire Bolts once a
        // player comes near or hits him; p goes before the states' names
        private static State O2Henchmen(string name, string p, int henchmen, params IStateChildren[] more) =>
            new State(name, O2Stars().Concat(new IStateChildren[]
            {
                new Wander(0.4),
                new KeepMinions("Henchman of Oryx", henchmen, 12000),
                new State(p + "Start",
                    new PlayerWithinTransition(12, p + "Rings"),
                    new HpLessTransition(0.9999, p + "Rings")
                ),
                new State(p + "Rings",
                    new Shoot(0, 8, 45, 0, fixedAngle: Auto(8, 45, 0), coolDown: 3000),
                    new Shoot(0, 8, 45, 0, fixedAngle: Auto(8, 45, 20), coolDownOffset: 1500, coolDown: 3000)
                )
            }).Concat(more).ToArray());

        private _ Oryx = () => Behav()
            // Stage 4 of the Oryx chain (castle guardians, Janus, Oryx 1, 2, 3, 4 and 5). BossStats puts
            // every stage on one difficulty curve; SETUP-LOCAL.md has the table.
            .Init(O2,
                new State(
                    new BossStats(60000, 40, 1.0, 0.3, arena: true),
                    new BossStats(130000, 40, 1.0, 0.3),
                    // open: Oryx 3 needs no incantation
                    new DropPortalOnDeath("Oryx's Sanctuary Portal", 1.0, timeout: 600, xAdjustment: 2),
                    new DropPortalOnDeath("Lost Halls Portal", 0.1, timeout: 120, yAdjustment: 2),
                    new AnnounceOnDeath("{PL_LIST} drove Oryx the Mad God out of his Wine Cellar! He flees to his Sanctuary, and the way lies open..."),
                    new State("Where",
                        new ArenaTransition("Arena Wake"),
                        new ArenaTransition("Henchmen", false)
                    ),
                    // in an arena: only his first phase, with fewer Henchmen
                    ArenaWake("Puny mortals! You will not leave this arena alive!", "Arena Henchmen"),
                    O2Henchmen("Arena Henchmen", "Arena ", 2),
                    O2Henchmen("Henchmen", "", 3, new HpLessTransition(0.3, "Bye Henchmen")),
                    // at 30% his Henchmen fade, and he floods the cellar with silencing and weakening spinners
                    new State("Bye Henchmen",
                        O2Stars().Concat(new IStateChildren[]
                        {
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Wander(0.4),
                            new Taunt("Can't... keep... henchmen... alive... anymore! ARGHHH!!!"),
                            new Order(40, "Henchman of Oryx", "Leave"),
                            new Shoot(0, 8, 45, 0, fixedAngle: Auto(8, 45, 0), coolDown: 3600),
                            new Shoot(0, 8, 45, 0, fixedAngle: Auto(8, 45, 20), coolDownOffset: 1800, coolDown: 3600),
                            new TimedTransition(3000, "Time To Die")
                        }).ToArray()
                    ),
                    new State("Time To Die",
                        O2Stars().Concat(new IStateChildren[]
                        {
                            new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                            new Wander(0.4),
                            new Shoot(0, 36, 10, 13, fixedAngle: Auto(36, 10, 0), coolDown: 90000),
                            new Shoot(0, 36, 10, 14, fixedAngle: Auto(36, 10, 5), coolDownOffset: 600, coolDown: 90000),
                            new Shoot(0, 9, 40, 0, fixedAngle: Auto(9, 40, 0), coolDownOffset: 800, coolDown: 1600),
                            new Shoot(0, 9, 40, 0, fixedAngle: Auto(9, 40, 20), coolDownOffset: 1600, coolDown: 1600),
                            new TimedTransition(7000, "Last Stand")
                        }).ToArray()
                    ),
                    // circling close with a weakening ring each time, then backing off; Scarabs all round
                    new State("Last Stand",
                        O2Stars().Concat(new IStateChildren[]
                        {
                            new Shoot(0, 9, 40, 0, fixedAngle: Auto(9, 40, 0), coolDown: 1600),
                            new Shoot(0, 9, 40, 0, fixedAngle: Auto(9, 40, 20), coolDownOffset: 800, coolDown: 1600),
                            new TossObject("Monstrosity Scarab", 10.8, 0, 2400),
                            new TossObject("Monstrosity Scarab", 10.8, 120, 2400),
                            new TossObject("Monstrosity Scarab", 10.8, 240, 2400),
                            new TossObject("Monstrosity Scarab", 7.4, 60, 2400, 1200),
                            new TossObject("Monstrosity Scarab", 7.4, 180, 2400, 1200),
                            new TossObject("Monstrosity Scarab", 7.4, 300, 2400, 1200),
                            new State("Strafe",
                                new Shoot(0, 36, 10, 14, fixedAngle: Auto(36, 10, 5), coolDownOffset: 600, coolDown: 90000),
                                new Circle(0.8, 4, 13),
                                new TimedTransition(4500, "Run Away")
                            ),
                            new State("Run Away",
                                new StayBack(0.6, 7),
                                new TimedTransition(3000, "Strafe")
                            )
                        }).ToArray()
                    )
                ),
                // everyone who did 1% of the damage: the sure drops
                new Threshold(0.01,
                    new ItemLoot("Potion of Life", 1),
                    new ItemLoot("Potion of Mana", 0.6),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Defense", 1)
                ),
                // 3% of the damage: more stat potions and the rare drops
                new Threshold(0.03,
                    new ItemLoot("Potion of Vitality", 0.6),
                    new ItemLoot("Potion of Wisdom", 0.6),
                    new ItemLoot("Potion of Speed", 0.35),
                    new ItemLoot("Potion of Dexterity", 0.35),
                    new ItemLoot("Seal of Blasphemous Prayer", 0.01),
                    new ItemLoot("Doom Bow", 0.008),
                    new ItemLoot("Lil' Wine Cellar Oryx Generator", 0.01)
                ),
                // 5% of the damage: the gear
                new Threshold(0.05,
                    new TierLoot(12, ItemType.Weapon, 0.12),
                    new TierLoot(13, ItemType.Weapon, 0.06),
                    new TierLoot(5, ItemType.Ability, 0.1),
                    new TierLoot(6, ItemType.Ability, 0.06),
                    new TierLoot(12, ItemType.Armor, 0.1),
                    new TierLoot(13, ItemType.Armor, 0.07),
                    new TierLoot(5, ItemType.Ring, 0.08),
                    new TierLoot(6, ItemType.Ring, 0.06)
                )
            )
            // each Henchman brings three of the four kinds of minion (one of each, again 7 s after it
            // dies), and alternates guarding Oryx with going after players. In the arenas and the
            // Battle for the Nexus there's no Oryx to guard. Oryx sends them away at 30%.
            .Init("Henchman of Oryx",
                new State(
                    new State("Serving",
                        new Shoot(12, projectileIndex: 0, predictive: 1, coolDown: 1500),
                        new Shoot(12, 3, 9, 1, coolDown: 1000),
                        new State("Pick", new TimedRandomTransition(0, false, "Group 1", "Group 2", "Group 3", "Group 4")),
                        HenchmanGroup(1, "Abomination of Oryx", "Aberrant of Oryx", "Monstrosity of Oryx"),
                        HenchmanGroup(2, "Abomination of Oryx", "Bile of Oryx", "Monstrosity of Oryx"),
                        HenchmanGroup(3, "Abomination of Oryx", "Aberrant of Oryx", "Vintner of Oryx"),
                        HenchmanGroup(4, "Vintner of Oryx", "Bile of Oryx", "Monstrosity of Oryx")
                    ),
                    new State("Leave", new Decay(400))
                )
            )
            // a short-range fan of flames, five layers deep, every 3 s
            .Init("Abomination of Oryx",
                new State(
                    new Prioritize(
                        new Protect(1, "Henchman of Oryx", 15, 12, 2),
                        new Follow(1.4, 10, 3),
                        new Wander(0.4)
                    ),
                    new Shoot(6, 3, 9, 0, coolDown: 3000),
                    new Shoot(6, 5, 9, 1, coolDown: 3000),
                    new Shoot(6, 7, 9, 2, coolDown: 3000),
                    new Shoot(6, 5, 9, 3, coolDown: 3000),
                    new Shoot(6, 3, 9, 4, coolDown: 3000)
                )
            )
            // breeds Scarabs while a player is within 10 tiles
            .Init("Monstrosity of Oryx",
                new State(
                    new Prioritize(
                        new Protect(1, "Henchman of Oryx", 15, 8, 2),
                        new Follow(1, 10, 6),
                        new Wander(0.4)
                    ),
                    new State("Wait", new PlayerWithinTransition(10, "Breed")),
                    new State("Breed",
                        new KeepMinions("Monstrosity Scarab", 5, 1000),
                        new NoPlayerWithinTransition(10, "Wait")
                    )
                )
            )
            // runs at you and bursts; gone after 5 s if it doesn't reach anyone
            .Init("Monstrosity Scarab",
                new State(
                    new State("Chase",
                        new Prioritize(
                            new Follow(1.8, 15, 0.25),
                            new Wander(0.4)
                        ),
                        new PlayerWithinTransition(0.5, "Boom"),
                        new TimedTransition(5000, "Fizzle")
                    ),
                    new State("Boom",
                        new Shoot(1, 12, 30, 0),
                        new Decay(0)
                    ),
                    new State("Fizzle", new Suicide())
                )
            )
            // throws Blasters at you every 1.5 s
            .Init("Aberrant of Oryx",
                new State(
                    new Prioritize(
                        new Protect(1, "Henchman of Oryx", 15, 8, 2),
                        new Follow(1.1, 12, 9),
                        new Wander(0.4)
                    ),
                    new TossObject("Aberrant Blaster", 14, coolDown: 1500)
                )
            )
            // bursts in a 60-degree fan at whoever comes within 5 tiles; gone after 10 s
            .Init("Aberrant Blaster",
                new State(
                    new State("Arm", new TimedTransition(500, "Wait")),
                    new State("Wait",
                        new PlayerWithinTransition(5, "Boom"),
                        new TimedTransition(9500, "Fizzle")
                    ),
                    new State("Boom",
                        new Shoot(5, 6, 10, 0),
                        new Decay(0)
                    ),
                    new State("Fizzle", new Suicide())
                )
            )
            // circles its Henchman leaving puddles that sicken and slow
            .Init("Bile of Oryx",
                new State(
                    new Prioritize(
                        new Orbit(0.5, 3.5, 15, "Henchman of Oryx", 0.3, 0.3),
                        new Follow(0.5, 6, 8, 2000, 2000),
                        new Wander(0.5)
                    ),
                    new KeepMinions("Purple Goo", 20, 200)
                )
            )
            // three Wine Bombs in quick succession, then a pause; each makes you drunk
            .Init("Vintner of Oryx",
                new State(
                    new Prioritize(
                        new Protect(1, "Henchman of Oryx", 15, 8, 2),
                        new Follow(1, 10, 6, 3000, 400),
                        new Wander(0.4)
                    ),
                    new State("One", new Shoot(10, predictive: 1, coolDown: 1400), new TimedTransition(0, "Two")),
                    new State("Two", new Shoot(10, predictive: 0.6, coolDown: 1400), new TimedTransition(0, "Three")),
                    new State("Three", new Shoot(10, predictive: 1, coolDown: 1400), new TimedTransition(0, "Rest")),
                    new State("Rest", new TimedTransition(800, "One"))
                )
            );

        private static State HenchmanGroup(int n, params string[] minions)
        {
            var p = "Group " + n;
            return new State(p,
                minions.Select(m => (IStateChildren)new KeepMinions(m, 1, 7000)).Concat(new IStateChildren[]
                {
                    new State(p + " Guard",
                        new Prioritize(
                            new Orbit(1, 4.5, 15, O2, 0, 0),
                            new Follow(0.6, 11, 4),
                            new Wander(1.1)
                        ),
                        new TimedTransition(2500, p + " Roam")
                    ),
                    new State(p + " Roam",
                        new Prioritize(
                            new Follow(0.6, 11, 4),
                            new Wander(1.1)
                        ),
                        new TimedTransition(2000, p + " Guard")
                    )
                }).ToArray()
            );
        }

        // a puddle of wine that sickens and slows whoever stands in it, for 6 seconds
        private _ WineCellarGoo = () => Behav()
            .Init("Purple Goo",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new Shoot(0, 1, projectileIndex: 0, fixedAngle: 0, coolDown: 90000),
                    new State("Wet", new TimedTransition(6000, "Dry")),
                    new State("Dry", new Suicide())
                )
            );
    }
}
