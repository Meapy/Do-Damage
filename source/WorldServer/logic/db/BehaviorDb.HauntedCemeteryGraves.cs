using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;
using WorldServer.logic.behaviors.@new.movements;
namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ HauntedCemeteryGraves = () => HauntedCemeteryGravesBehaviours(Behav(), false);
        private _ ElderCemeteryGraves = () => HauntedCemeteryGravesBehaviours(Behav(), true);

        private static Ctor HauntedCemeteryGravesBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);
            return db
            .Init(E("Area 3 Controller"),
                new State(
                    new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                    new TransformOnDeath(N("Haunted Cemetery Final Rest Portal")),
                    new State("Start",
                        new PlayerWithinTransition(4, "1")
                        ),
                    new State("1",
                        new TimedTransition(0, "2")
                        ),
                    new State("2",
                        new SetAltTexture(2),
                        new TimedTransition(100, "3")
                        ),
                    new State("3",
                        new SetAltTexture(1),
                        new Taunt("Your prowess in battle is impressive... and most annoying. This round shall crush you."),
                        new TimedTransition(500, "4")
                        ),
                    new State("4",
                        new Taunt(true, "Say,'READY' when you are ready to face your opponeants.", "Prepare yourself...Say, 'READY' when you wish the battle to begin!"),
                        new TimedTransition(0, "5")
                        ),
                    //repeat
                    new State("5",
                        new PlayerTextTransition("7", "ready"),
                        new SetAltTexture(3),
                        new TimedTransition(150, "6")
                        ),
                    new State("6",
                        new PlayerTextTransition("7", "ready"),
                        new SetAltTexture(4),
                        new TimedTransition(150, "6_1")
                        ),
                    new State("6_1",
                        new PlayerTextTransition("7", "ready"),
                        new SetAltTexture(3),
                        new TimedTransition(150, "6_2")
                        ),
                    new State("6_2",
                        new PlayerTextTransition("7", "ready"),
                        new SetAltTexture(4),
                        new TimedTransition(150, "6_3")
                        ),
                    new State("6_3",
                        new PlayerTextTransition("7", "ready"),
                        new SetAltTexture(3),
                        new TimedTransition(150, "6_4")
                        ),
                    new State("6_4",
                        new SetAltTexture(1),
                        new PlayerTextTransition("7", "ready")
                        ),
                    new State("7",
                        new SetAltTexture(0),
                        new OrderOnce(100, N("Arena South Gate Spawner"), "Stage 11"),
                        new OrderOnce(100, N("Arena West Gate Spawner"), "Stage 11"),
                        new OrderOnce(100, N("Arena East Gate Spawner"), "Stage 11"),
                        new OrderOnce(100, N("Arena North Gate Spawner"), "Stage 11"),
                        new EntityExistsTransition(N("Arena Risen Warrior"), 999, "Check 1")
                        ),
                    new State("Check 1",
                        new EntitiesNotExistsTransition(100, "8", N("Arena Risen Archer"), N("Arena Risen Mage"), N("Arena Risen Brawler"), N("Arena Risen Warrior"))
                        ),
                    new State("8",
                        new SetAltTexture(2),
                        new TimedTransition(0, "9")
                        ),
                    new State("9",
                        new SetAltTexture(1),
                        new TimedTransition(500, "10")
                        ),
                    new State("10",
                        new Taunt(true, "The next wave will appear in 3 seconds. Prepare yourself!", "l hope you're prepared because the next wave is in 3 seconds.", "The next onslaught will begin in 3 seconds!", "You have 3 seconds until your next challenge!", "3 seconds until the next attack!"),
                        new TimedTransition(0, "11")
                        ),
                    new State("11",
                        new SetAltTexture(3),
                        new TimedTransition(150, "12")
                        ),
                    new State("12",
                        new SetAltTexture(4),
                        new TimedTransition(150, "13")
                        ),
                    new State("13",
                        new SetAltTexture(3),
                        new TimedTransition(150, "14")
                        ),
                    new State("14",
                        new SetAltTexture(4),
                        new TimedTransition(150, "15")
                        ),
                    new State("15",
                        new SetAltTexture(3),
                        new TimedTransition(150, "16")
                        ),
                    new State("16",
                        new SetAltTexture(4),
                        new TimedTransition(150, "17")
                        ),
                    new State("17",
                        new SetAltTexture(3),
                        new TimedTransition(150, "18")
                        ),
                    new State("18",
                        new SetAltTexture(4),
                        new TimedTransition(150, "19")
                        ),
                    new State("19",
                        new SetAltTexture(3),
                        new TimedTransition(150, "20")
                        ),
                    new State("20",
                        new SetAltTexture(4),
                        new TimedTransition(150, "21")
                        ),
                    new State("21",
                        new SetAltTexture(3),
                        new TimedTransition(150, "22")
                        ),
                    new State("22",
                        new SetAltTexture(4),
                        new TimedTransition(150, "23")
                        ),
                    new State("23",
                        new SetAltTexture(3),
                        new TimedTransition(150, "24")
                        ),
                    new State("24",
                        new SetAltTexture(4),
                        new TimedTransition(100, "25")
                        ),
                    new State("25",
                        new SetAltTexture(1),
                        new TimedTransition(100, "26")
                        ),
                    new State("26",
                        new SetAltTexture(2),
                        new TimedTransition(100, "27")
                        ),
                    new State("27",
                        new SetAltTexture(0),
                        new OrderOnce(100, N("Arena South Gate Spawner"), "Stage 12"),
                        new OrderOnce(100, N("Arena West Gate Spawner"), "Stage 12"),
                        new OrderOnce(100, N("Arena East Gate Spawner"), "Stage 12"),
                        new OrderOnce(100, N("Arena North Gate Spawner"), "Stage 12"),
                        new EntityExistsTransition(N("Arena Risen Warrior"), 999, "Check 2")
                        ),
                    new State("Check 2",
                        new EntitiesNotExistsTransition(100, "28", N("Arena Risen Archer"), N("Arena Risen Mage"), N("Arena Risen Brawler"), N("Arena Risen Warrior"))
                        ),
                    new State("28",
                        new SetAltTexture(2),
                        new TimedTransition(0, "29")
                        ),
                    new State("29",
                        new SetAltTexture(1),
                        new TimedTransition(500, "30")
                        ),
                    new State("30",
                        new Taunt(true, "The next wave will appear in 3 seconds. Prepare yourself!", "l hope you're prepared because the next wave is in 3 seconds.", "The next onslaught will begin in 3 seconds!", "You have 3 seconds until your next challenge!", "3 seconds until the next attack!"),
                        new TimedTransition(0, "31")
                        ),
                    new State("31",
                        new SetAltTexture(3),
                        new TimedTransition(150, "32")
                        ),
                    new State("32",
                        new SetAltTexture(4),
                        new TimedTransition(150, "33")
                        ),
                    new State("33",
                        new SetAltTexture(3),
                        new TimedTransition(150, "34")
                        ),
                    new State("34",
                        new SetAltTexture(4),
                        new TimedTransition(150, "35")
                        ),
                    new State("35",
                        new SetAltTexture(3),
                        new TimedTransition(150, "36")
                        ),
                    new State("36",
                        new SetAltTexture(4),
                        new TimedTransition(150, "37")
                        ),
                    new State("37",
                        new SetAltTexture(3),
                        new TimedTransition(150, "38")
                        ),
                    new State("38",
                        new SetAltTexture(4),
                        new TimedTransition(150, "39")
                        ),
                    new State("39",
                        new SetAltTexture(3),
                        new TimedTransition(150, "40")
                        ),
                    new State("40",
                        new SetAltTexture(4),
                        new TimedTransition(150, "41")
                        ),
                    new State("41",
                        new SetAltTexture(3),
                        new TimedTransition(150, "42")
                        ),
                    new State("42",
                        new SetAltTexture(4),
                        new TimedTransition(150, "43")
                        ),
                    new State("43",
                        new SetAltTexture(3),
                        new TimedTransition(150, "44")
                        ),
                    new State("44",
                        new SetAltTexture(4),
                        new TimedTransition(100, "45")
                        ),
                    new State("45",
                        new SetAltTexture(1),
                        new TimedTransition(100, "46")
                        ),
                    new State("46",
                        new SetAltTexture(2),
                        new TimedTransition(100, "47")
                        ),
                    new State("47",
                        new SetAltTexture(0),
                        new OrderOnce(100, N("Arena South Gate Spawner"), "Stage 13"),
                        new OrderOnce(100, N("Arena West Gate Spawner"), "Stage 13"),
                        new OrderOnce(100, N("Arena East Gate Spawner"), "Stage 13"),
                        new OrderOnce(100, N("Arena North Gate Spawner"), "Stage 13"),
                        new EntityExistsTransition(N("Arena Risen Warrior"), 999, "Check 3")
                        ),
                    new State("Check 3",
                        new EntitiesNotExistsTransition(100, "48", N("Arena Risen Archer"), N("Arena Risen Mage"), N("Arena Risen Brawler"), N("Arena Risen Warrior"))
                        ),
                    new State("48",
                        new SetAltTexture(2),
                        new TimedTransition(0, "49")
                        ),
                    new State("49",
                        new SetAltTexture(1),
                        new TimedTransition(500, "50")
                        ),
                    new State("50",
                        new Taunt(true, "The next wave will appear in 3 seconds. Prepare yourself!", "l hope you're prepared because the next wave is in 3 seconds.", "The next onslaught will begin in 3 seconds!", "You have 3 seconds until your next challenge!", "3 seconds until the next attack!"),
                        new TimedTransition(0, "51")
                        ),
                    new State("51",
                        new SetAltTexture(3),
                        new TimedTransition(150, "52")
                        ),
                    new State("52",
                        new SetAltTexture(4),
                        new TimedTransition(150, "53")
                        ),
                    new State("53",
                        new SetAltTexture(3),
                        new TimedTransition(150, "54")
                        ),
                    new State("54",
                        new SetAltTexture(4),
                        new TimedTransition(150, "55")
                        ),
                    new State("55",
                        new SetAltTexture(3),
                        new TimedTransition(150, "56")
                        ),
                    new State("56",
                        new SetAltTexture(4),
                        new TimedTransition(150, "57")
                        ),
                    new State("57",
                        new SetAltTexture(3),
                        new TimedTransition(150, "58")
                        ),
                    new State("58",
                        new SetAltTexture(4),
                        new TimedTransition(150, "59")
                        ),
                    new State("59",
                        new SetAltTexture(3),
                        new TimedTransition(150, "60")
                        ),
                    new State("60",
                        new SetAltTexture(4),
                        new TimedTransition(150, "61")
                        ),
                    new State("61",
                        new SetAltTexture(3),
                        new TimedTransition(150, "62")
                        ),
                    new State("62",
                        new SetAltTexture(4),
                        new TimedTransition(150, "63")
                        ),
                    new State("63",
                        new SetAltTexture(3),
                        new TimedTransition(150, "64")
                        ),
                    new State("64",
                        new SetAltTexture(4),
                        new TimedTransition(150, "65")
                        ),
                    new State("65",
                        new SetAltTexture(1),
                        new TimedTransition(100, "66")
                        ),
                    new State("66",
                        new SetAltTexture(2),
                        new TimedTransition(100, "67")
                        ),
                    new State("67",
                        new SetAltTexture(0),
                        new OrderOnce(100, N("Arena South Gate Spawner"), "Stage 14"),
                        new OrderOnce(100, N("Arena West Gate Spawner"), "Stage 14"),
                        new OrderOnce(100, N("Arena East Gate Spawner"), "Stage 14"),
                        new OrderOnce(100, N("Arena North Gate Spawner"), "Stage 14"),
                        new EntityExistsTransition(N("Arena Risen Warrior"), 999, "Check 4")
                        ),
                    new State("Check 4",
                        new EntitiesNotExistsTransition(100, "68", N("Arena Risen Archer"), N("Arena Risen Mage"), N("Arena Risen Brawler"), N("Arena Risen Warrior"))
                        ),
                    new State("68",
                        new SetAltTexture(2),
                        new TimedTransition(0, "69")
                        ),
                    new State("69",
                        new SetAltTexture(1),
                        new TimedTransition(500, "70")
                        ),
                    new State("70",
                        new Taunt(true, "The next wave will appear in 3 seconds. Prepare yourself!", "l hope you're prepared because the next wave is in 3 seconds.", "The next onslaught will begin in 3 seconds!", "You have 3 seconds until your next challenge!", "3 seconds until the next attack!"),
                        new TimedTransition(0, "71")
                        ),
                    new State("71",
                        new SetAltTexture(3),
                        new TimedTransition(150, "72")
                        ),
                    new State("72",
                        new SetAltTexture(4),
                        new TimedTransition(150, "73")
                        ),
                    new State("73",
                        new SetAltTexture(3),
                        new TimedTransition(150, "74")
                        ),
                    new State("74",
                        new SetAltTexture(4),
                        new TimedTransition(150, "75")
                        ),
                    new State("75",
                        new SetAltTexture(3),
                        new TimedTransition(150, "76")
                        ),
                    new State("76",
                        new SetAltTexture(4),
                        new TimedTransition(150, "77")
                        ),
                    new State("77",
                        new SetAltTexture(3),
                        new TimedTransition(150, "78")
                        ),
                    new State("78",
                        new SetAltTexture(4),
                        new TimedTransition(150, "79")
                        ),
                    new State("79",
                        new SetAltTexture(3),
                        new TimedTransition(150, "80")
                        ),
                    new State("80",
                        new SetAltTexture(4),
                        new TimedTransition(150, "81")
                        ),
                    new State("81",
                        new SetAltTexture(3),
                        new TimedTransition(150, "82")
                        ),
                    new State("82",
                        new SetAltTexture(4),
                        new TimedTransition(150, "83")
                        ),
                    new State("83",
                        new SetAltTexture(3),
                        new TimedTransition(150, "84")
                        ),
                    new State("84",
                        new SetAltTexture(4),
                        new TimedTransition(150, "85")
                        ),
                    new State("85",
                        new SetAltTexture(1),
                        new TimedTransition(100, "86")
                        ),
                    new State("86",
                        new SetAltTexture(2),
                        new TimedTransition(100, "87")
                        ),
                    new State("87",
                        new SetAltTexture(0),
                        new OrderOnce(100, N("Arena South Gate Spawner"), "Stage 15"),
                        new OrderOnce(100, N("Arena West Gate Spawner"), "Stage 15"),
                        new OrderOnce(100, N("Arena East Gate Spawner"), "Stage 15"),
                        new OrderOnce(100, N("Arena North Gate Spawner"), "Stage 15"),
                        new EntityExistsTransition(N("Arena Grave Caretaker"), 999, "Check 5")
                        ),
                    new State("Check 5",
                        new EntitiesNotExistsTransition(100, "88", N("Arena Blue Flame"), N("Arena Grave Caretaker"))
                        ),
                    new State("88",
                        new Suicide()
                    )
                )
            )
            .Init(E("Arena Risen Brawler"),
                new State(
                    new Shoot(radius: 10, count: 3, shootAngle: 10, projectileIndex: 0, coolDown: 2000),
                    new Prioritize(
                        new Orbit(speed: 0.7, radius: 1, acquireRange: 14, target: null),
                        new Follow(speed: 0.6, acquireRange: 10, range: 7)
                    )
                )
            )
            .Init(E("Arena Risen Warrior"),
                new State(
                    new Shoot(radius: 4, projectileIndex: 0, coolDown: 1200),
                    new Prioritize(
                        new Follow(speed: 0.9, acquireRange: 10, range: 1.2),
                        new Orbit(speed: 0.4, radius: 3, acquireRange: 10, target: null)
                    )
                )
            )
            .Init(E("Arena Risen Mage"),
                new State(
                    new Shoot(radius: 10, projectileIndex: 0, coolDown: 1000),
                    new Prioritize(
                        new Follow(speed: 0.9, acquireRange: 10, range: 7),
                        new Orbit(speed: 0.4, radius: 3, acquireRange: 10,target: null)
                    ),
                    new HealGroup(range: 7, group: "Hallowrena", coolDown: 2500, healAmount: 175)
                )
            )
            .Init(E("Arena Risen Archer"),
                new State(
                    new Shoot(radius: 10, count: 3, shootAngle: 33, projectileIndex: 0, coolDown: 3000),
                    new Shoot(radius: 10, count: 1, projectileIndex: 1, coolDown: 1300),
                    new Prioritize(
                        new Orbit(speed: 0.4, radius: 6, acquireRange: 10, target: null),
                        new Follow(speed: 0.8, acquireRange: 10, range: 7)
                    )
                )
            )
            .Init(E("Arena Blue Flame"),
                new State(
                    new State("Ini",
                        new Orbit(speed: 1.6, radius: 1.5, acquireRange: 15, target: N("Arena Grave Caretaker")),
                        // (round the Elder Caretaker raised again too, core/resurrection)
                        elder ? new Orbit(speed: 1.6, radius: 1.5, acquireRange: 15, target: "Empowered Elder Arena Grave Caretaker") : null,
                        new PlayerWithinTransition(dist: 3, targetState: "AttackPlayer", seeInvis: true),
                        // its Caretaker gone, it seeks the players out rather than waiting
                        // where he left it (in the Arena, it held the next wave back)
                        elder
                            ? new EntitiesNotExistsTransition(20, "Hunt", N("Arena Grave Caretaker"), "Empowered Elder Arena Grave Caretaker")
                            : new EntitiesNotExistsTransition(20, "Hunt", N("Arena Grave Caretaker"))
                    ),
                    new State("Hunt",
                        new Prioritize(
                            new Follow(speed: 1, acquireRange: 30, range: 0.5),
                            new Wander(speed: 0.5)
                        ),
                        new PlayerWithinTransition(dist: 1, targetState: "Explode", seeInvis: true)
                    ),
                    new State("AttackPlayer",
                        new Charge(speed: 4.1, range: 9),
                        new PlayerWithinTransition(dist: 1, targetState: "Explode", seeInvis: true),
                        new PlayerWithinTransition(dist: 4, targetState: "Follow", seeInvis: true)
                    ),
                    new State("Follow",
                        new Follow(speed: 1, acquireRange: 6, range: 0.5),
                        new PlayerWithinTransition(dist: 1, targetState: "Explode", seeInvis: true)
                    ),
                    new State("Explode",
                        new Shoot(radius: 6, count: 10, shootAngle: 36, projectileIndex: 0, fixedAngle: 18),
                        new Suicide()
                    )
                )
            )
            .Init(E("Arena Grave Caretaker"),
                new State(
                    new ScaleHP2(elder ? 15 : 35),
                    new State("Ini",
                        new Prioritize(
                            new StayBack(speed: 0.6, distance: 4, entity: null),
                            new Wander(speed: 0.3)
                        ),
                        new Shoot(radius: 8, count: 1, projectileIndex: 0, coolDown: 1000),
                        new Shoot(radius: 8, count: 2, shootAngle: 45, projectileIndex: 1, coolDown: 1000),
                        new Spawn(children: N("Arena Blue Flame"), maxChildren: 5, initialSpawn: 0, coolDown: 1000, givesNoXp: true),
                        new TimedTransition(time: 10000, targetState: "Circle")
                    ),
                    new State("Circle",
                        new Orbit(speed: 0.8, radius: 3, acquireRange: 10, target: null),
                        new Shoot(radius: 8, count: 2, shootAngle: 45, projectileIndex: 1, coolDown: 1000),
                        new Spawn(children: N("Arena Blue Flame"), maxChildren: 5, initialSpawn: 0, coolDown: 2000, givesNoXp: true),
                        elder ? new HpLessTransition(threshold: 0.4, targetState: "Burial") : null,
                        new TimedTransition(time: 8000, targetState: "Ini")
                    ),
                    // the Elder Caretaker's Last Burial, below 40% (each time he would circle): he
                    // stands his ground armoured, a ring of graves opening round him, each giving
                    // up a blue flame that rushes in, while two crosses of angry ghosts and
                    // blinding spinners turn round him
                    new State("Burial",
                        new Taunt(true, "Every grave in this yard has room for one more. I dug yours myself."),
                        new Flash(0xFF60A0FF, 0.2, 8),
                        new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                        new TossObject(N("Arena Blue Flame"), range: 6, angle: 0, coolDown: 3000),
                        new TossObject(N("Arena Blue Flame"), range: 6, angle: 60, coolDown: 3000, coolDownOffset: 250),
                        new TossObject(N("Arena Blue Flame"), range: 6, angle: 120, coolDown: 3000, coolDownOffset: 500),
                        new TossObject(N("Arena Blue Flame"), range: 6, angle: 180, coolDown: 3000, coolDownOffset: 750),
                        new TossObject(N("Arena Blue Flame"), range: 6, angle: 240, coolDown: 3000, coolDownOffset: 1000),
                        new TossObject(N("Arena Blue Flame"), range: 6, angle: 300, coolDown: 3000, coolDownOffset: 1250),
                        new Shoot(radius: 20, count: 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 0, rotateAngle: 10, coolDown: 250),
                        new Shoot(radius: 20, count: 4, shootAngle: 90, projectileIndex: 1, fixedAngle: 45, rotateAngle: -8, coolDown: 500),
                        new TimedTransition(time: 9000, targetState: "Ini")
                    )
                ),
                new Threshold(.005,
                    LootTemplates.BasicDrop()
                    ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Speed", 0.3, 3),
                    new ItemLoot("Potion of Wisdom", 0.3)
                ),
                ElderLoot(0.03,
                    new ItemLoot("Potion of Max Wisdom", 0.025),
                    new ItemLoot("Elder Cemetery Key", 0.01)
                    ),
                ElderLoot(0.0001,
                    new TierLoot(13, ItemType.Weapon, .1),
                    new TierLoot(14, ItemType.Armor, .1),
                    new TierLoot(6, ItemType.Ability, .1),
                    new TierLoot(6, ItemType.Ring, .1)
                    )
            );
        }
    }
}
