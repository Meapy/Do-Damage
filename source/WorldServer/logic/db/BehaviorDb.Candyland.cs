using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;
using WorldServer.logic.behaviors.@new.movements;

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Candyland = () => Behav()
        .Init("Candy Gnome",
            new State(
                new State("Ini",
                    new Wander(speed: 0.4),
                    new PlayerWithinTransition(dist: 14, targetState: "Main", seeInvis: true)
                    ),
                new State("Main",
                    new Follow(speed: 1.4, acquireRange: 17, range: 6),
                    new TimedTransition(time: 1600, targetState: "Flee")
                    ),
                new State("Flee",
                    new PlayerWithinTransition(dist: 11, targetState: "RunAwayMed", seeInvis: true),
                    new PlayerWithinTransition(dist: 8, targetState: "RunAwayMedFast", seeInvis: true),
                    new PlayerWithinTransition(dist: 5, targetState: "RunAwayFast", seeInvis: true),
                    new PlayerWithinTransition(dist: 16, targetState: "RunAwaySlow", seeInvis: true)
                    ),
                new State("RunAwayFast",
                    new StayBack(speed: 1.9, distance: 30, entity: null),
                    new TimedRandomTransition(1000, false, "RunAwayMedFast", "RunAwayMed", "RunAwaySlow")
                    ),
                new State("RunAwayMedFast",
                    new StayBack(speed: 1.45, distance: 30, entity: null),
                    new TimedRandomTransition(1000, false, "RunAwayMed", "RunAwaySlow")
                    ),
                new State("RunAwayMed",
                    new StayBack(speed: 1.1, distance: 30, entity: null),
                    new TimedRandomTransition(1000, false, "RunAwayMedFast", "RunAwaySlow")
                    ),
                new State("RunAwaySlow",
                    new StayBack(speed: 0.8, distance: 30, entity: null),
                    new TimedRandomTransition(1000, false, "RunAwayMedFast", "RunAwayMed")
                    )
                ),
                new Threshold(0.01,
                    new ItemLoot(item: "Red Gumball", probability: 0.15),
                    new ItemLoot(item: "Purple Gumball", probability: 0.15),
                    new ItemLoot(item: "Blue Gumball", probability: 0.15),
                    new ItemLoot(item: "Green Gumball", probability: 0.15),
                    new ItemLoot(item: "Yellow Gumball", probability: 0.15)
                    )
            )
            .Init("MegaRototo",
                new State(
                    new Taunt(true, "The MegaRototo's breath will roast you, well done!", "You really shouldn't have threatened the Rototos!"),
                    new MarkWorldOnDeath("MegaRototo", CandyBosses, "Cupcake", "Your crazy hijinks even awakened the great Cupcake!"),
                    new Reproduce(children: "Tiny Rototo", densityRadius: 12, densityMax: 7, coolDown: 7000),
                    new State("Follow",
                        new Shoot(radius: 0, count: 4, shootAngle: 90, defaultAngle: 45, coolDown: 1400),
                        new Shoot(radius: 0, count: 4, shootAngle: 90, projectileIndex: 1, coolDown: 1400),
                        new Follow(speed: 0.45, acquireRange: 11, range: 5),
                        new TimedRandomTransition(4300, false, "FlameThrower", "StayBack")
                        ),
                    new State("StayBack",
                        new Shoot(radius: 0, count: 3, shootAngle: 16, projectileIndex: 1, predictive: 0.6, coolDown: 1200),
                        new Shoot(radius: 0, count: 3, shootAngle: 16, projectileIndex: 0, predictive: 0.9, coolDown: 600),
                        new StayBack(speed: 0.5, distance: 10, entity: null),
                        new TimedTransition(time: 2400, targetState: "Follow")
                        ),
                    new State("FlameThrower",
                        new State("FB1ORFB2",
                            new TimedRandomTransition(0, false, "FB1", "FB2")
                            ),
                        new State("FB1",
                            new Shoot(radius: 12, count: 2, shootAngle: 16, projectileIndex: 2, coolDown: 400),
                            new Shoot(radius: 12, count: 1, projectileIndex: 3, coolDown: 200)
                            ),
                        new State("FB2",
                            new Shoot(radius: 12, count: 2, shootAngle: 16, projectileIndex: 3, coolDown: 400),
                            new Shoot(radius: 12, count: 1, projectileIndex: 2, coolDown: 200)
                            ),
                        new TimedTransition(time: 4000, targetState: "Follow")
                        )
                ),
                CandyBossLoot("Legacy Fairy Plate"),
                new Threshold(.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(.005,
                    new ItemLoot("Cookie Generator", 0.02)
                    ),
                new Threshold(.005,
                    LootTemplates.BasicPots()
                    ),
                new ItemLoot("Gummy Bear Generator", 0.02)
            )
            .Init("Spoiled Creampuff",
                new State(
                    new Taunt(true, "These puffs are sure to sour your mood!", "This creampuff is here to spoil your fun!"),
                    new MarkWorldOnDeath("Spoiled Creampuff", CandyBosses, "Cupcake", "Your crazy hijinks even awakened the great Cupcake!"),
                    new Spawn(children: "Big Creampuff", maxChildren: 2, initialSpawn: 0, givesNoXp: true),
                    new Reproduce(children: "Big Creampuff", densityRadius: 24, densityMax: 2, coolDown: 25000),
                    new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 1400),
                    new Shoot(radius: 4.4, count: 5, shootAngle: 12, projectileIndex: 1, predictive: 0.6, coolDown: 800),
                    new Prioritize(
                        new Charge(speed: 1.4, range: 11, coolDown: 4200),
                        new StayBack(speed: 1, distance: 4, entity: null),
                        new StayBack(speed: 0.5, distance: 7, entity: null)
                        ),
                    new StayCloseToSpawn(speed: 1.35, range: 13),
                    new Wander(speed: 0.4)
                ),
                CandyBossLoot("Potion of Defense", "Potion of Attack", "Legacy Seal of the Enchanted Forest"),
                new Threshold(.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(.005,
                    new ItemLoot("Cookie Generator", 0.02)
                    ),
                new Threshold(.005,
                    LootTemplates.BasicPots()
                    ),
                new ItemLoot("Gummy Bear Generator", 0.02)
            )
            .Init("Desire Troll",
                new State(
                    new Taunt(true, "It's all you don't desire, and more!", "This troll will keep all your dreams just out of reach!"),
                    new MarkWorldOnDeath("Desire Troll", CandyBosses, "Cupcake", "Your crazy hijinks even awakened the great Cupcake!"),
                    new State("BaseAttack",
                        new Shoot(radius: 10, count: 3, shootAngle: 15, projectileIndex: 0, predictive: 1, coolDown: 1400),
                        new Grenade(radius: 5, damage: 200, range: 8, coolDown: 3000),
                        new Shoot(radius: 10, count: 1, projectileIndex: 1, predictive: 1, coolDown: 2000),
                        new State("Choose",
                            new TimedRandomTransition(3800, false, "Run", "Attack")
                            ),
                        new State("Run",
                            new StayBack(speed: 1.1, distance: 10, entity: null),
                            new TimedTransition(time: 1200, targetState: "Choose")
                            ),
                        new State("Attack",
                            new Charge(speed: 1.2, range: 11, coolDown: 1000),
                            new TimedTransition(time: 1000, targetState: "Choose")
                            ),
                        new HpLessTransition(threshold: 0.6, targetState: "NextAttack")
                        ),
                    new State("NextAttack",
                        new Shoot(radius: 10, count: 5, shootAngle: 10, projectileIndex: 2, predictive: 0.5, angleOffset: 0.4, coolDown: 2000),
                        new Shoot(radius: 10, count: 1, projectileIndex: 1, predictive: 1, coolDown: 2000),
                        new Shoot(radius: 10, count: 3, shootAngle: 15, projectileIndex: 0, predictive: 1, angleOffset: 1, coolDown: 4000),
                        new Grenade(radius: 5, damage: 200, range: 8, coolDown: 3000),
                        new State("Choose2",
                            new TimedRandomTransition(3800, false, "Run2", "Attack2")
                            ),
                        new State("Run2",
                            new StayBack(speed: 1.5, distance: 10, entity: null),
                            new TimedTransition(time: 1500, targetState: "Choose2"),
                            new PlayerWithinTransition(dist: 3.5, targetState: "Boom", seeInvis: false)
                            ),
                        new State("Attack2",
                            new Charge(speed: 1.2, range: 11, coolDown: 1000),
                            new TimedTransition(time: 1000, targetState: "Choose2"),
                            new PlayerWithinTransition(dist: 3.5, targetState: "Boom", seeInvis: false)
                            ),
                        new State("Boom",
                            new Shoot(radius: 0, count: 20, shootAngle: 18, projectileIndex: 3, coolDown: 2000),
                            new TimedTransition(time: 200, targetState: "Choose2")
                            )
                        ),
                    new StayCloseToSpawn(speed: 1.5, range: 15),
                    new Prioritize(
                        new Follow(speed: 1, acquireRange: 11, range: 5)
                        ),
                    new Wander(speed: 0.4)
                ),
                CandyBossLoot("Potion of Attack", "Potion of Wisdom", "Legacy Seal of the Enchanted Forest", "Mini Desire Troll Pet Stone"),
                new Threshold(.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(.005,
                    new ItemLoot("Cookie Generator", 0.02)
                    ),
                new Threshold(.005,
                    LootTemplates.BasicPots()
                    ),
                new ItemLoot("Gummy Bear Generator", 0.02)
            )

        .Init("Wishing Troll",
                new State(
                    new State("BaseAttack",
                        new Shoot(radius: 10, count: 3, shootAngle: 15, projectileIndex: 0, predictive: 1, coolDown: 1400),
                        new Grenade(radius: 5, damage: 200, range: 8, coolDown: 3000),
                        new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 2000),
                        new State("Choose",
                            new TimedRandomTransition(3800, false, "Run", "Attack")
                            ),
                        new State("Run",
                            new StayBack(speed: 1.1, distance: 10, entity: null),
                            new TimedTransition(time: 1200, targetState: "Choose")
                            ),
                        new State("Attack",
                            new Charge(speed: 1.2, range: 11, coolDown: 1000),
                            new TimedTransition(time: 1000, targetState: "Choose")
                            ),
                        new HpLessTransition(threshold: 0.6, targetState: "NextAttack")
                        ),
                    new State("NextAttack",
                        new Shoot(radius: 10, count: 5, shootAngle: 10, projectileIndex: 0, predictive: 0.5, angleOffset: 0.4, coolDown: 2000),
                        new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 2000),
                        new Shoot(radius: 10, count: 3, shootAngle: 15, projectileIndex: 0, predictive: 1, angleOffset: 1, coolDown: 4000),
                        new Grenade(radius: 5, damage: 200, range: 8, coolDown: 3000),
                        new State("Choose2",
                            new TimedRandomTransition(3800, false, "Run2", "Attack2")
                            ),
                        new State("Run2",
                            new StayBack(speed: 1.5, distance: 10, entity: null),
                            new TimedTransition(time: 1500, targetState: "Choose2"),
                            new PlayerWithinTransition(dist: 3.5, targetState: "Boom", seeInvis: false)
                            ),
                        new State("Attack2",
                            new Charge(speed: 1.2, range: 11, coolDown: 1000),
                            new TimedTransition(time: 1000, targetState: "Choose2"),
                            new PlayerWithinTransition(dist: 3.5, targetState: "Boom", seeInvis: false)
                            ),
                        new State("Boom",
                            new Shoot(radius: 0, count: 20, shootAngle: 18, projectileIndex: 0, coolDown: 2000),
                            new TimedTransition(time: 200, targetState: "Choose2")
                            )
                        ),
                    new StayCloseToSpawn(speed: 1.5, range: 15),
                    new Prioritize(
                        new Follow(speed: 1, acquireRange: 11, range: 5)
                        ),
                    new Wander(speed: 0.4)
                )
            )
            .Init("Swoll Fairy",
                new State(
                    new Taunt(true, "All pain, no gain is the credo of these potent pixies!", "The brawny fairies won't even break a sweat against you!"),
                    new MarkWorldOnDeath("Swoll Fairy", CandyBosses, "Cupcake", "Your crazy hijinks even awakened the great Cupcake!"),
                    new Spawn(children: "Fairy", maxChildren: 6, initialSpawn: 0, coolDown: 10000, givesNoXp: false),
                    new StayCloseToSpawn(speed: 0.6, range: 13),
                    new Prioritize(
                        new Follow(speed: 0.3, acquireRange: 10, range: 5)
                        ),
                    new State("Shoot",
                        new Shoot(radius: 11, count: 2, shootAngle: 30, projectileIndex: 0, predictive: 1, coolDown: 600),
                        new TimedTransition(time: 3000, targetState: "Pause")
                        ),
                    new State("ShootB",
                        new Shoot(radius: 11, count: 8, shootAngle: 45, projectileIndex: 1, coolDown: 1000),
                        new TimedTransition(time: 3000, targetState: "Pause")
                        ),
                    new State("Pause",
                        new TimedRandomTransition(3000, false, "Shoot", "ShootB")
                        )
                ),
                CandyBossLoot("Potion of Defense", "Potion of Wisdom", "Legacy Fairy Plate"),
                new Threshold(.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(.005,
                    new ItemLoot("Cookie Generator", 0.02)
                    ),
                new Threshold(.005,
                    LootTemplates.BasicPots()
                    ),
                new ItemLoot("Valentine Generator", 0.005),
                new ItemLoot("Gummy Bear Generator", 0.02)
            )
        .Init("Unicorn",
             new State(
                    new Prioritize(
                        new Charge(speed: 1.4, range: 11, coolDown: 3800),
                        new StayBack(speed: 0.8, distance: 6, entity: null)
                        ),
                    new State("Start",
                        new State("Shoot",
                            new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 200),
                            new TimedTransition(time: 850, targetState: "ShootPause")
                            ),
                        new State("ShootPause",
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 1, predictive: 0.4, coolDown: 3000, coolDownOffset: 500),
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 1, predictive: 0.4, coolDown: 3000, coolDownOffset: 1000),
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 1, predictive: 0.4, coolDown: 3000, coolDownOffset: 1500),
                            new TimedTransition(time: 1200, targetState: "Shoot")
                            )
                        )
                )
            )
         .Init("Spilled Icecream",
             new State(
                    new Prioritize(
                        new Charge(speed: 1.4, range: 11, coolDown: 3800),
                        new StayBack(speed: 0.8, distance: 6, entity: null)
                        ),
                    new State("Start",
                        new State("Shoot",
                            new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 200),
                            new TimedTransition(time: 850, targetState: "ShootPause")
                            ),
                        new State("ShootPause",
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 0, predictive: 0.4, coolDown: 3000, coolDownOffset: 500),
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 0, predictive: 0.4, coolDown: 3000, coolDownOffset: 1000),
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 0, predictive: 0.4, coolDown: 3000, coolDownOffset: 1500),
                            new TimedTransition(time: 1200, targetState: "Shoot")
                            )
                        )
                )
            )
            .Init("Gigacorn",
                new State(
                    new Taunt(true, "You'll taste the rainbow when this Gigacorn tramples you!", "Time for you to see what's at the end of the rainbow!"),
                    new MarkWorldOnDeath("Gigacorn", CandyBosses, "Cupcake", "Your crazy hijinks even awakened the great Cupcake!"),
                    new StayCloseToSpawn(speed: 1, range: 13),
                    new Prioritize(
                        new Charge(speed: 1.4, range: 11, coolDown: 3800),
                        new StayBack(speed: 0.8, distance: 6, entity: null)
                        ),
                    new State("Start",
                        new State("Shoot",
                            new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 200),
                            new TimedTransition(time: 2850, targetState: "ShootPause")
                            ),
                        new State("ShootPause",
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 1, predictive: 0.4, coolDown: 3000, coolDownOffset: 500),
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 1, predictive: 0.4, coolDown: 3000, coolDownOffset: 1000),
                            new Shoot(radius: 4.5, count: 3, shootAngle: 10, projectileIndex: 1, predictive: 0.4, coolDown: 3000, coolDownOffset: 1500),
                            new TimedTransition(time: 5700, targetState: "Shoot")
                            )
                        )
                ),
                CandyBossLoot("Potion of Attack", "Potion of Wisdom", "Legacy Pixie-Enchanted Sword"),
                new Threshold(.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(.005,
                    new ItemLoot("Cookie Generator", 0.02)
                    ),
                new Threshold(.005,
                    LootTemplates.BasicPots()
                    ),
                new ItemLoot("Gummy Bear Generator", 0.02)
            )
            .Init("Candyland Spawner",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new State("Ini",
                        new PlayerWithinTransition(dist: 16, targetState: "Start Wave")
                        ),
                    new State("Start Wave",
                        new Spawn(children: "Candyland Boss Spawner", maxChildren: 1, initialSpawn: 1, givesNoXp: true),
                        new SpawnGroup(group: "Candy Creature", maxChildren: 6, initialSpawn: 1, radius: 8),
                        new TimedTransition(time: 100, targetState: "Wait for Wave")
                        ),
                    new State("Wait for Wave",
                        new EntitiesNotExistsTransition(16, "Wait for Room Exit",
                            "Unicorn", "Hard Candy", "Beefy Fairy", "Fairy", "Wishing Troll",
                            "Spilled Icecream", "Big Creampuff", "Small Creampuff", "Tiny Rototo", "Rototo")
                        ),
                    new State("Wait for Room Exit",
                        new NoPlayerWithinTransition(dist: 16, targetState: "Wait for Boss Spawn")
                        ),
                    new State("Wait for Boss Spawn",
                        new TimedTransition(time: 1000, targetState: "Wait for Boss")
                        ),
                    new State("Wait for Boss",
                        new EntitiesNotExistsTransition(16, "Wait for Player",
                            "Spoiled Creampuff", "Gigacorn", "Desire Troll", "MegaRototo", "Swoll Fairy")
                        ),
                    new State("Wait for Player",
                        new PlayerWithinTransition(dist: 16, targetState: "Spawn Wave")
                        ),
                    new State("Spawn Wave",
                        new SpawnGroup(group: "Candy Creature", maxChildren: 6, initialSpawn: 1, radius: 8),
                        new TimedTransition(time: 100, targetState: "Wait for Wave")
                        )
                )
            )
            .Init("Candyland Boss Spawner",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new State("Ini",
                        new EntitiesNotExistsTransition(16, "Wait for Room Exit",
                            "Unicorn", "Hard Candy", "Beefy Fairy", "Fairy", "Wishing Troll",
                            "Spilled Icecream", "Big Creampuff", "Small Creampuff", "Tiny Rototo", "Rototo")
                        ),
                    new State("Wait for Room Exit",
                        new PlayerWithinTransition(dist: 16, targetState: "Ini"),
                        new NoPlayerWithinTransition(dist: 16, targetState: "Ini2")
                        ),
                    new State("Ini2",
                        new TimedRandomTransition(0, false, "Creampuff", "Unicorn", "Troll", "Rototo", "Fairy")
                        ),
                    new State("Ini3",
                        new EntitiesNotExistsTransition(16, "Ini", "Spoiled Creampuff", "Gigacorn", "Desire Troll", "MegaRototo", "Swoll Fairy")
                        ),
                    new State("Creampuff",
                        new Spawn(children: "Spoiled Creampuff", maxChildren: 1, initialSpawn: 0, givesNoXp: false, spawnedByBehav: false),
                        new TimedTransition(time: 3000, targetState: "Ini3")
                        ),
                    new State("Unicorn",
                        new Spawn(children: "Gigacorn", maxChildren: 1, initialSpawn: 0, givesNoXp: false, spawnedByBehav: false),
                        new TimedTransition(time: 3000, targetState: "Ini3")
                        ),
                    new State("Troll",
                        new Spawn(children: "Desire Troll", maxChildren: 1, initialSpawn: 0, givesNoXp: false, spawnedByBehav: false),
                        new TimedTransition(time: 3000, targetState: "Ini3")
                        ),
                    new State("Rototo",
                        new Spawn(children: "MegaRototo", maxChildren: 1, initialSpawn: 0, givesNoXp: false, spawnedByBehav: false),
                        new TimedTransition(time: 3000, targetState: "Ini3")
                        ),
                    new State("Fairy",
                        new Spawn(children: "Swoll Fairy", maxChildren: 1, initialSpawn: 0, givesNoXp: false, spawnedByBehav: false),
                        new TimedTransition(time: 3000, targetState: "Ini3")
                        )
                )
            )

            .Init("Fairy",
                new State(
                    new StayCloseToSpawn(speed: 1, range: 13),
                    new Prioritize(
                        new Protect(speed: 1.2, protectee: "Beefy Fairy", acquireRange: 15, protectionRange: 8, reprotectRange: 6),
                        new Orbit(speed: 1.2, radius: 4, acquireRange: 7)
                        ),
                    new Wander(speed: 0.6),
                    new Shoot(radius: 10, count: 2, shootAngle: 30, projectileIndex: 0, predictive: 1, coolDown: 2000),
                    new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 2000, coolDownOffset: 1000)
                )
            )
        .Init("Beefy Fairy",
                new State(
                    new StayCloseToSpawn(speed: 1, range: 13),
                    new Prioritize(
                        new Protect(speed: 1.2, protectee: "Beefy Fairy", acquireRange: 15, protectionRange: 8, reprotectRange: 6),
                        new Orbit(speed: 1.2, radius: 4, acquireRange: 7)
                        ),
                    new Wander(speed: 0.6),
                    new Shoot(radius: 10, count: 2, shootAngle: 30, projectileIndex: 0, predictive: 1, coolDown: 2000),
                    new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 2000, coolDownOffset: 1000)
                ),
                new ItemLoot(item: "Fairy Ring", probability: 0.01)
            )
            .Init("Big Creampuff",
                new State(
                    new Spawn(children: "Small Creampuff", maxChildren: 4, initialSpawn: 0, givesNoXp: false),
                    new Shoot(radius: 10, count: 1, projectileIndex: 0, predictive: 1, coolDown: 1400),
                    new Shoot(radius: 4.4, count: 5, shootAngle: 12, projectileIndex: 1, predictive: 0.6, coolDown: 800),
                    new Prioritize(
                        new Charge(speed: 1.4, range: 11, coolDown: 4200),
                        new StayBack(speed: 1, distance: 4, entity: null),
                        new StayBack(speed: 0.5, distance: 7, entity: null)
                        ),
                    new StayCloseToSpawn(speed: 1.35, range: 13),
                    new Wander(speed: 0.4)
                ),
                new ItemLoot("Kirby Generator", 0.01)
            )
            .Init("Small Creampuff",
                new State(
                    new Shoot(radius: 5, count: 3, shootAngle: 12, projectileIndex: 1, predictive: 0.6, coolDown: 1000),
                    new StayCloseToSpawn(speed: 1.3, range: 13),
                    new Prioritize(
                        new Charge(speed: 1.3, range: 13, coolDown: 2500),
                        new Protect(speed: 0.8, protectee: "Big Creampuff", acquireRange: 15, protectionRange: 7, reprotectRange: 6)
                        ),
                    new Wander(speed: 0.6)
                )
            )
        .Init("Hard Candy",
                new State(
                    new Shoot(radius: 5, count: 3, shootAngle: 12, projectileIndex: 0, predictive: 0.6, coolDown: 1000),
                    new StayCloseToSpawn(speed: 1.3, range: 13),
                    new Prioritize(
                        new Charge(speed: 1.3, range: 13, coolDown: 2500),
                        new Protect(speed: 0.8, protectee: "Big Creampuff", acquireRange: 15, protectionRange: 7, reprotectRange: 6)
                        ),
                    new Wander(speed: 0.6)
                )
            )
            .Init("Tiny Rototo",
                new State(
                    new Prioritize(
                        new Orbit(speed: 1.2, radius: 4, acquireRange: 10, target: "MegaRototo"),
                        new Protect(speed: 0.8, protectee: "Rototo", acquireRange: 15, protectionRange: 7, reprotectRange: 6)
                        ),
                    new State("Main",
                        new State("Unaware",
                            new Prioritize(
                                new Orbit(speed: 0.4, radius: 2.6, acquireRange: 8, target: "Rototo", speedVariance: 0.2, radiusVariance: 0.2, orbitClockwise: true),
                                new Wander(speed: 0.35)
                                ),
                            new PlayerWithinTransition(dist: 3.4, targetState: "Attack"),
                            new HpLessTransition(threshold: 0.999, targetState: "Attack")
                            ),
                        new State("Attack",
                            new Shoot(radius: 0, count: 4, shootAngle: 90, projectileIndex: 1, defaultAngle: 45, coolDown: 1400),
                            new Shoot(radius: 0, count: 4, shootAngle: 90, projectileIndex: 0, coolDown: 1400),
                            new Prioritize(
                                new Follow(speed: 0.8, acquireRange: 8, range: 3, duration: 3000, coolDown: 2000),
                                new Charge(speed: 1.35, range: 11, coolDown: 1000),
                                new Wander(speed: 0.35)
                                )
                            )
                        )
                )
            )
            .Init("Butterfly",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new StayCloseToSpawn(speed: 0.3, range: 6),
                    new State("Moving",
                        new Wander(speed: 0.25),
                        new PlayerWithinTransition(dist: 6, targetState: "Follow")
                        ),
                    new State("Follow",
                        new Prioritize(
                            new StayBack(speed: 0.23, distance: 1.2, entity: null),
                            new Orbit(speed: 0.2, radius: 1.6, acquireRange: 3)
                            ),
                        new Follow(speed: 0.2, acquireRange: 7, range: 3),
                        new Wander(speed: 0.2),
                        new NoPlayerWithinTransition(dist: 4, targetState: "Moving")
                        )
                )
            )
            // a miniboss: the MegaRototo's kind, guarded by Tiny Rototos (RealmEye, Rototo)
            .Init("Rototo",
                new State(
                    new Reproduce(children: "Tiny Rototo", densityRadius: 10, densityMax: 4, coolDown: 9000),
                    new StayCloseToSpawn(speed: 0.8, range: 12),
                    new State("Follow",
                        new Follow(speed: 0.5, acquireRange: 11, range: 4),
                        new Shoot(radius: 10, count: 4, shootAngle: 90, projectileIndex: 1, defaultAngle: 45, coolDown: 1600),
                        new Shoot(radius: 10, count: 2, shootAngle: 20, projectileIndex: 0, predictive: 0.6, coolDown: 1200),
                        new TimedTransition(time: 4000, targetState: "StayBack")
                        ),
                    new State("StayBack",
                        new StayBack(speed: 0.6, distance: 7, entity: null),
                        new Shoot(radius: 10, count: 3, shootAngle: 16, projectileIndex: 0, predictive: 0.8, coolDown: 900),
                        new TimedTransition(time: 2500, targetState: "Follow")
                        )
                ),
                new ItemLoot("Red Gumball", 0.04),
                new ItemLoot("Purple Gumball", 0.04)
            )
            // it stands in its room and spills gumballs when broken (RealmEye, Gumball Machine)
            .Init("Gumball Machine",
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.StasisImmune, true)
                ),
                new Threshold(0.01,
                    new ItemLoot(item: "Red Gumball", probability: 0.3),
                    new ItemLoot(item: "Purple Gumball", probability: 0.3),
                    new ItemLoot(item: "Blue Gumball", probability: 0.3),
                    new ItemLoot(item: "Green Gumball", probability: 0.3),
                    new ItemLoot(item: "Yellow Gumball", probability: 0.3),
                    new ItemLoot(item: "Rock Candy", probability: 0.1),
                    new ItemLoot(item: "Candy Ring", probability: 0.005)
                    )
            )
            // the final boss: it comes to the starting room once each of the five bosses has fallen
            // (MarkWorldOnDeath), chases players with rings of happy magic, and its death clears the
            // dungeon and opens the Realm Portal (World.BossDied) (RealmEye, Cupcake)
            .Init("Cupcake",
                new State(
                    new ScaleHP2(20),
                    new State("Wake",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, false, 3000),
                        new Taunt("Even the happy-go-lucky Cupcake despises you!"),
                        new TimedTransition(time: 3000, targetState: "Chase")
                        ),
                    new State("Chase",
                        new Prioritize(
                            new Follow(speed: 0.7, acquireRange: 20, range: 2),
                            new Wander(speed: 0.4)
                            ),
                        new Shoot(radius: 20, count: 12, shootAngle: 30, projectileIndex: 0, fixedAngle: 0, rotateAngle: 12, coolDown: 1200),
                        new Shoot(radius: 20, count: 3, shootAngle: 12, projectileIndex: 1, predictive: 0.5, coolDown: 1800, coolDownOffset: 600),
                        new HpLessTransition(threshold: 0.5, targetState: "Frenzy")
                        ),
                    new State("Frenzy",
                        new Prioritize(
                            new Follow(speed: 0.9, acquireRange: 20, range: 1),
                            new Wander(speed: 0.4)
                            ),
                        new Shoot(radius: 20, count: 16, shootAngle: 22.5, projectileIndex: 0, fixedAngle: 0, rotateAngle: 11, coolDown: 1000),
                        new Shoot(radius: 20, count: 5, shootAngle: 10, projectileIndex: 1, predictive: 0.5, coolDown: 1500, coolDownOffset: 500)
                        ),
                    new MarkWorldOnDeath("Cupcake", deathTaunt: "You're no fun! Just get out and leave us alone!")
                ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Defense", 1),
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Wisdom", 1),
                    new ItemLoot("Candy-Coated Armor", 0.03),
                    new ItemLoot("Candy Ring", 0.03),
                    new ItemLoot("Legacy Pixie-Enchanted Sword", 0.02),
                    new ItemLoot("Legacy Seal of the Enchanted Forest", 0.02),
                    new ItemLoot("Legacy Fairy Plate", 0.02),
                    new ItemLoot("Legacy Ring of Pure Wishes", 0.02),
                    new ItemLoot("Rock Candy", 0.3),
                    new ItemLoot("Red Gumball", 0.3),
                    new ItemLoot("Purple Gumball", 0.3),
                    new ItemLoot("Blue Gumball", 0.3),
                    new ItemLoot("Green Gumball", 0.3),
                    new ItemLoot("Yellow Gumball", 0.3),
                    new ItemLoot("Candy Key", 0.1, 0, 0.03),
                    new TierLoot(11, ItemType.Weapon, 0.12),
                    new TierLoot(12, ItemType.Armor, 0.12),
                    new TierLoot(5, ItemType.Ability, 0.1),
                    new TierLoot(5, ItemType.Ring, 0.1)
                    )
            )
        ;

        // the five bosses; each marks the dungeon when it falls, and the fifth kind brings the Cupcake
        private static readonly string[] CandyBosses = { "Gigacorn", "Desire Troll", "Spoiled Creampuff", "MegaRototo", "Swoll Fairy" };

        // what every Candyland boss drops (RealmEye's drops table), with its own extras: its stat
        // potions at half, its piece of the Swoll Paladin set at 1%, or a pet stone
        private static Threshold CandyBossLoot(params string[] extras)
        {
            var loot = new System.Collections.Generic.List<MobDrops>
            {
                new ItemLoot("Candy-Coated Armor", 0.01),
                new ItemLoot("Candy Ring", 0.01),
                new ItemLoot("Legacy Ring of Pure Wishes", 0.01),
                new ItemLoot("Rock Candy", 0.15),
                new ItemLoot("Red Gumball", 0.15),
                new ItemLoot("Purple Gumball", 0.15),
                new ItemLoot("Blue Gumball", 0.15),
                new ItemLoot("Green Gumball", 0.15),
                new ItemLoot("Yellow Gumball", 0.15),
            };
            foreach (var item in extras)
                loot.Add(new ItemLoot(item, item.StartsWith("Potion of") ? 0.5 : item.EndsWith("Pet Stone") ? 0.005 : 0.01));
            return new Threshold(0.01, loot.ToArray());
        }
    }
}
