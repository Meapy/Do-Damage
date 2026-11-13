using System.Linq;
using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;
using WorldServer.logic.behaviors.@new.movements;
namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ MagicWoods = () => Behav() //Moonman the g

        #region Lesser Fairies
        .Init("MW Drac",
            new State(
            new State("Shoot",
                new Wander(0.5),
                new Shoot(radius: 8, count: 5, shootAngle: 72, projectileIndex: 0, fixedAngle: 0, angleOffset: 72, coolDown: 1500)
                )
              )
            )
          .Init("MW Gharr",
            new State(
            new State("Shoot",
                new Wander(0.5),
                new Follow(1, 10, 2),
                new Shoot(radius: 8, count: 1, projectileIndex: 0, predictive: 1, coolDown: 600)
                )
               )
            )
           .Init("MW Iatho",
            new State(
            new State("Shoot",
                new Wander(0.5),
                new Follow(1, 10, 2),
                new Grenade(2, 35, 1, coolDown: 1000, color: 255)
                )
              )
            )
        .Init("MW Issz",
            new State(
                new EntityWithinTransition(10, "MW Utanu", "Utanu"),
                new EntityWithinTransition(10, "MW Deyst", "Deyst"),
                new EntityNotExistsTransition("MW Utanu", 10, "Attack"),
                new EntityNotExistsTransition("MW Deyst", 10, "Attack"),
            new State("Attack",
                new Follow(1, 10, 2),
                new Shoot(radius: 0.5, count: 1, projectileIndex: 0, coolDown: 500)
                ),
            new State("Deyst",
                new Orbit(3, 3, 10, target: "MW Deyst"),
                new Shoot(radius: 0.5, count: 1, projectileIndex: 0, coolDown: 500)
                ),
            new State("Utanu",
                new ConditionEffectBehavior(ConditionEffectIndex.Armored, false),
                new Prioritize(
                new Orbit(3, 3, 10, target: "MW Utanu")
                ),
                new Shoot(radius: 0.5, count: 1, projectileIndex: 0, coolDown: 100)
                )
              )
            )
          .Init("MW Lauk",
            new State(
            new State("Shoot",
                new Wander(0.5),
                new Follow(1, 10, 10),
                new Shoot(radius: 8, count: 3, shootAngle: 7, projectileIndex: 0, predictive: 2, coolDown: 1000)
                )
               )
            )
          .Init("MW Lorz",
            new State(
            new State("Shoot",
                new Wander(0.5),
                new Follow(1, 10, 2),
                new Shoot(radius: 5, count: 1, projectileIndex: 0, predictive: 1, coolDown: 2000, coolDownOffset: 1000),
                new Shoot(radius: 5, count: 2, shootAngle: 30, projectileIndex: 0, predictive: 1, coolDown: 2000, coolDownOffset: 2000)
              )
                )
            )
        .Init("MW Orothi",
            new State(
            new State("Shoot",
                new Wander(0.5),
                new Follow(1, 10, 0.5),
                new PlayerWithinTransition(1.5, "BOMB", true)
                ),
            new State("BOMB",
                new Shoot(radius: 8, count: 6, shootAngle: 60, projectileIndex: 0, fixedAngle: 0, coolDown: 100),
                new Suicide()
                )
              )
            )
          .Init("MW Radph",
            new State(
                new Orbit(3, 2, 10, "MW Vorv"),
                new HealEntity(8, "MW Vorv", 40, coolDown: 2000),
            new State("Shoot",
                new Wander(2),
                new Shoot(radius: 0.5, count: 1, projectileIndex: 0, predictive: 1, coolDown: 400)
                )
               )
            )
           .Init("MW Ril",
            new State(
            new State("Shoot",
                new Follow(1, 10, 4, 4000),
                new Orbit(3, 2, 4),
                new Shoot(radius: 10, count: 1, projectileIndex: 0, coolDown: 1000),
                new TimedTransition(4000, "Stationary")
                ),
            new State("Stationary",
                new Shoot(10, 6, 60, 1, fixedAngle: 0, rotateAngle: 15, angleOffset: 60, coolDown: 500),
                new TimedTransition(4000, "Shoot")
                    )
              )
            )
        .Init("MW Sek",
            new State(
            new State("STAY",
                new PlayerWithinTransition(10, "ATTACK")
                ),
            new State("ATTACK",
                new Follow(1, 10, 0.4),
                new Shoot(0.5, 1, projectileIndex: 0, coolDown: 100)
                )
              )
            )
          .Init("MW Serl",
            new State(
            new State("Shoot",
                new Wander(1),
                new Shoot(radius: 8, count: 1, projectileIndex: 0, coolDown: 100)
                )
               )
            )
           .Init("MW Seus",
            new State(
                new Wander(1),
            new State("Chase",
                new Prioritize(
                new Follow(1, 10, 2, 3000)
                ),
                new Shoot(radius: 7, count: 1, projectileIndex: 0, coolDown: 1000, coolDownOffset: 500),
                new Shoot(radius: 7, count: 2, shootAngle: 8, projectileIndex: 1, coolDown: 1000, coolDownOffset: 1000)
                )
                )
            )
        .Init("MW Tal",
            new State(
            new State("Shoot",
                new Circle(10, 3, 2),
                new Grenade(3, 30, 9, coolDown: 1000, color: 16776960)
                )
               )
            )
           .Init("MW Uoro",
            new State(
            new State("Shoot",
                new Wander(1),
                new Shoot(radius: 10, count: 3, shootAngle: 7, projectileIndex: 0, coolDown: 1000)
                )
              )
            )
        .Init("MW Zhiar",
            new State(
            new State("Shoot",
                new StayBack(1, 5),
                new Shoot(radius: 7, count: 2, shootAngle: 15, projectileIndex: 0, coolDown: 1000)
                )
                )
            )
        #endregion

        #region Standard Fairies
         .Init("MW Deyst",
            new State(
                new EntityWithinTransition(10, "MW Utanu", "Utanu"),
            new State("Attack",
                new Follow(1, 10, 0.5),
                new Shoot(radius: 0.5, count: 1, projectileIndex: 0, coolDown: 100)
                    ),
                new State("Utanu",
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored, false),
                    new EntityNotExistsTransition("MW Utanu", 10, "Attack"),
                    new Orbit(3, 3, 10, target: "MW Utanu")
                )
              )
            )
          .Init("MW Eango",
            new State(
                new EntityWithinTransition(10, "MW Vorv", "Orbit"),
                new EntitiesNotExistsTransition(10, "Wander", "MW Vorv"),
            new State("Orbit",
                new Orbit(3, 1.5, 10, "MW Vorv"),
                new HealEntity(8, "MW Vorv", 50, 2500),
                new Shoot(radius: 0.5, count: 1, projectileIndex: 0, coolDown: 100)
                ),
            new State("Wander",
                new Wander(1),
                new Shoot(radius: 1, count: 1, projectileIndex: 0, coolDown: 100)
                )
               )
            )
           .Init("MW Eati",
            new State(
                new Wander(1),
            new State("Shoot",
                new Follow(1.2, 10, 2),
                new Shoot(10, 2, shootAngle: 40, 0, coolDown: 1000),
                new Shoot(10, 2, shootAngle: 20, 1, coolDown: 1000)
                )
              )
            )
        .Init("MW Ehoni",
            new State(
                new Wander(1),
            new State("Attack",
                 new Shoot(10, 5, 8, 0, coolDown: 1000)
                )
              )
            )
          .Init("MW Iawa",
            new State(
                new Wander(1),
            new State("Shoot",
                 new Follow(1, 10, 1),
                 new Shoot(1, 6, 60, 0, fixedAngle: 0, coolDown: 1000)
                )
               )
            )
          .Init("MW Laen",
            new State(
            new State("Shoot",
                new Follow(1, 10, 4, 4000),
                new Orbit(3, 2, 4),
                new Shoot(radius: 10, count: 1, projectileIndex: 0, coolDown: 1000),
                new TimedTransition(4000, "Stationary")
                ),
            new State("Stationary",
                new Shoot(10, 6, 60, 1, fixedAngle: 0, rotateAngle: 15, angleOffset: 60, coolDown: 500),
                new TimedTransition(4000, "Shoot")
              )
                )
            )
        .Init("MW Odaru",
            new State(
            new State("Shoot",
                new Wander(0.7),
                new Shoot(5, 7, shootAngle: 51.5, 0, 0, coolDown: 1000)
                )
              )
            )
          .Init("MW Rilr",
            new State(
            new State("Shoot",
               new StayBack(1, 5),
               new Shoot(7, 3, 9, 0, coolDown: 1000)
                )
               )
            )
           .Init("MW Risrr",
            new State(
            new State("Shoot",
                new Wander(0.5),
                new Follow(2, 10, 0.5),
                new PlayerWithinTransition(1.5, "BOMB", true)
                ),
            new State("BOMB",
                new Shoot(radius: 8, count: 8, shootAngle: 45, projectileIndex: 0, fixedAngle: 0, coolDown: 100),
                new Suicide()
                    )
              )
            )
        .Init("MW SayIt",
            new State(
            new State("STAY",
                new Wander(1),
                new TimedTransition(3000, "ATTACK")
                ),
            new State("ATTACK",
                new Charge(1.5, 8),
                new Grenade(3, 45, 8, coolDown: 3000, coolDownOffset: 1000, color: 255),
                new Grenade(3, 45, 8, coolDown: 3000, coolDownOffset: 2000, color: 255),
                new Grenade(3, 45, 8, coolDown: 3000, coolDownOffset: 3000, color: 255),
                new TimedTransition(3000, "STAY")
                )
              )
            )
          .Init("MW Scheev",
            new State(
            new State("Chase",
                new Follow(1, 10, 3),
                new Shoot(radius: 7.5, count: 1, projectileIndex: 0, coolDown: 1000),
                new TimedTransition(3000, "Circle")
                ),
            new State("Circle",
                new ConditionEffectBehavior(ConditionEffectIndex.Armored, false),
                new Circle(10, 3, 2),
                new TimedTransition(3000, "Chase")
                )
               )
            )
           .Init("MW Tiar",
            new State(
            new State("Chase",
                new Follow(1, 10, 3),
                new Shoot(6, 1, projectileIndex: 0, coolDown: 800)
                )
                )
            )
        .Init("MW Vorck",
            new State(
            new State("Shoot",
                new Circle(3, 3, 10),
                new Grenade(3, 30, 9, coolDown: 1000, color: 16776960)
                )
               )
            )
       .Init("MW Yangu",
          new State(
              new Wander(0.2),
            new State("Shoot",
                new Shoot(radius: 10, count: 2, shootAngle: 10, projectileIndex: 0, predictive: 2, coolDown: 1000),
                new TransformOnDeath("MW Lauk", 2, 3, 1)
                )
              )
            )
        .Init("MW Yimi",
            new State(
                new Wander(0.2),
            new State("Shoot",
                new Prioritize(
                new Follow(1, 10, 3, 3000, 3000),
                new Shoot(radius: 7, count: 2, shootAngle: 7, projectileIndex: 0, coolDown: 1000)
                )
                )
                )
            )
        #endregion

        #region Greater Fairies
        .Init("MW Darq",
            new State(
            new State("Shoot",
                new Wander(0.7),
                new Shoot(radius: 8, count: 7, shootAngle: 7, projectileIndex: 0, coolDown: 1000)
                )
              )
            )
          .Init("MW Eashy",
            new State(
            new State("Shoot",
                new BackAndForth(0.5, 6),
                new Shoot(radius: 10, count: 1, projectileIndex: 0, coolDown: 1000),
                new Spawn("MW Sek", 3, 3, 4000)
                )
               )
            )
           .Init("MW Eendi",
            new State(
                new Wander(0.7),
            new State("Shoot",
                new Prioritize(
                new Charge(1.5, 8)
                ),
                new Grenade(4, 55, 8, coolDown: 3000, coolDownOffset: 1000, color: 255),
                new Grenade(4, 55, 8, coolDown: 3000, coolDownOffset: 2000, color: 255),
                new Grenade(4, 55, 8, coolDown: 3000, coolDownOffset: 3000, color: 255)
                )
              )
            )
        .Init("MW Idrae",
           new State(
            new State("Attack",
                new Follow(0.4, 10, 4),
                new Shoot(6.5, 9, 40, 0, 0, coolDown: 1000)
                )
              )
            )
          .Init("MW Iri",
            new State(
            new State("Shoot",
                new Circle(3, 3, 10),
                new Grenade(6, 70, 6, coolDown: 1000, color: 16776960)
                )
               )
            )
          .Init("MW Itani",
            new State(
            new State("Shoot",
                new Follow(0.3, 10, 1),
                new Shoot(6.5, 1, projectileIndex: 0, coolDown: 500)
              )
                )
            )
        .Init("MW Oalei",
            new State(
                new Follow(1, 10, 2),
            new State("Shoot",

                new Shoot(14, 1, projectileIndex: 0, coolDown: 700),
                new HpLessTransition(0.5, "Spawn")
                ),
             new State("Spawn",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, false, 4000),
                new Spawn("MW Yimi", 2, 2),
                new Spawn("MW Seus", 2, 2),
                new Shoot(14, 1, projectileIndex: 0, coolDown: 700)
                  )
                )
              )
          .Init("MW Oeti",
            new State(
              new State("Shoot",
                new Follow(1, 10, 4, 4000),
                new Orbit(3, 2, 4),
                new Shoot(radius: 10, count: 1, projectileIndex: 0, coolDown: 1000),
                new TimedTransition(4000, "Stationary")
                ),
            new State("Stationary",
                new Shoot(10, 6, 60, 1, fixedAngle: 0, rotateAngle: 15, coolDown: 500),
                new TimedTransition(4000, "Shoot")
                )
               )
            )
           .Init("MW Oshyu",
            new State(
            new State("Shoot",
                new Follow(1, 10, 4),
                new Prioritize(
                new Orbit(3, 3, 5)
                ),
                new Shoot(10, 5, 72, 0, 0, coolDown: 1000)
                    )
              )
            )
        .Init("MW Queq",
            new State(
            new State("DAMN",
                new StayBack(2, 5),
                new Shoot(radius: 7, count: 4, shootAngle: 8, projectileIndex: 0, coolDown: 1000)
                )
              )
            )
          .Init("MW Rayr",
            new State(
            new State("Shoot",
                new Follow(1, 10, 1),
                new PlayerWithinTransition(1.5, "EXPLODE")
                ),
            new State("EXPLODE",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, true),
                new Flash(16777215, 1, 1),
                new Shoot(8, 8, 45, 0, 0, coolDown: 1000),
                new Suicide()
                )
               )
            )
           .Init("MW Urake",
            new State(
                new Wander(0.2),
            new State("STAY",
                new Shoot(8, 1, projectileIndex: 0, predictive: 2, coolDown: 800),
                new TransformOnDeath("MW Yangu", 3, 4, 1)
                )
                )
            )
        .Init("MW Utanu",
            new State(
                new Wander(0.1),
            new State("Shoot",
                new Prioritize(
                    new StayBack(2, 3),
                    new Shoot(4, 5, 8, 0, coolDown: 800)
                    )
                )
                )
                )
         .Init("MW Vorv",
            new State(
            new State("Shoot",
                new Follow(1, 10, 1),
                new Shoot(8, 2, 45, 0, coolDown: 1000),
                new Shoot(8, 2, 20, 0, coolDown: 1000)
                )
                )
            )
        #endregion

        #region Fountain Spirit
        // The fountain in the boss room (its spout at 132, 116 on the map; the spawner sits in its pool
        // four tiles above, at 132, 112). A player coming near stirs it; the four small fountains
        // around it (120/144, 104/128) send out a
        // wave of lesser, then standard, then greater fairies, and once each wave is beaten (none left
        // within 22 tiles, or after 40 s) the next comes. Then the fountain opens and the Fountain Spirit rises from it, the
        // small fountains sending a fairy now and then while she lives (RealmEye, Magic Woods).
        .Init("MW Boss Spawner",
            new State(
                new ConditionEffectBehavior(ConditionEffectIndex.Invincible, true),
                new State("Wait",
                    new PlayerWithinTransition(12, "Taunt")
                    ),
                new State("Taunt",
                    new Taunt("Ah, so you must be the guest I have felt the presence of! Best my fairies and I shall show myself!"),
                    new TimedTransition(4000, "Wave1")
                    ),
                FairyWave("Wave1", "Lesser Fairies", 2, "Wave2"),
                FairyWave("Wave2", "Standard Fairies", 2, "Wave3"),
                FairyWave("Wave3", "Greater Fairies", 1, "Emerge"),
                new State("Emerge",
                    new Taunt("You have stirred my waters. Now you shall live an eternity with US!"),
                    new OpenGate("MW Fountain Inner Left", 6),
                    new OpenGate("MW Fountain Inner Right", 6),
                    new OpenGate("MW Fountain Inner Top", 6),
                    new OpenGate("MW Fountain Inner Bottom", 6),
                    new OpenGate("MW Fountain Inner Bottom Left", 6),
                    new OpenGate("MW Fountain Inner Bottom Right", 6),
                    new OpenGate("MW Fountain Inner Top Left", 6),
                    new OpenGate("MW Fountain Inner Top Right", 6),
                    new OpenGate("MW Big Fountain Top", 6),
                    // she rises from the spout
                    new TossObject("MW Fountain Spirit", 4, 90, coolDown: 999999),
                    new TimedTransition(1500, "Fight")
                    ),
                new State("Fight",
                    new TossObject(null, 20, 53.13, coolDown: 12000, group: "Lesser Fairies", densityRange: 30, maxDensity: 6),
                    new TossObject(null, 20, 126.87, coolDown: 12000, coolDownOffset: 3000, group: "Lesser Fairies", densityRange: 30, maxDensity: 6),
                    new TossObject(null, 14.42, 213.69, coolDown: 12000, coolDownOffset: 6000, group: "Lesser Fairies", densityRange: 30, maxDensity: 6),
                    new TossObject(null, 14.42, 326.31, coolDown: 12000, coolDownOffset: 9000, group: "Lesser Fairies", densityRange: 30, maxDensity: 6),
                    new EntityNotExistsTransition("MW Fountain Spirit", 60, "Done")
                    ),
                new State("Done",
                    new Decay(0)
                    )
                )
            )
        .Init("MW Fountain Spirit",
            new State(
                new ScaleHP2(20),
            //new State("Waiting",
            //    new Grenade(14, 50, 7, 90, 2000, color: 255)
            //    ),
            new StayCloseToSpawn(0.6, 20),
            new State("Missing Healing Weirdo",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, false, 5000),
                new HealSelf(1500, 1000),
                new ReturnToSpawn(2),
                new Flash(0x00D0E2, 1, 1000),
                new ChangeSize(20, 100),
                new TimedRandomTransition(5000, false, "P1", "P2", "P3", "P4", "P5")
                ),
            new State("Random",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable, false, 5000),
                new TimedRandomTransition(5000, false, "P1", "P2", "P3", "P4", "P5")
                ),
            new State("P1",
                new Wander(0.7),
                new Follow(0.5, 12, 3),
                new Shoot(17, 3, 10, 0, predictive: 2, coolDown: 1000),
                new TimedTransition(12000, "Random")
                ),
             new State("P2",
                new Orbit(3, 3.4, 16),
                new Shoot(16, 7, 12, 10, coolDown: 1000),
                new Shoot(16, 2, 24, 1, coolDown: 1000),
                new TimedTransition(12000, "Random")
                ),
            new State("P3",
                new Shoot(16, 8, 45, 2, 0, 8, coolDown: 500),
                new Shoot(16, 1, projectileIndex: 3, predictive: 0.5, coolDown: 1000),
                new TimedTransition(12000, "Random")
                ),
            new State("P4",
                new Shoot(16, 9, 40, 8, 0, coolDown: 1000),
                new Shoot(16, 4, 18, 7, predictive: 2, coolDown: 1000),
                new TimedTransition(12000, "Random")
                ),
            new State("P5",
                new Shoot(5.5, 5, 4, 9, coolDown: 1000),
                new Shoot(16, 1, projectileIndex: 4, coolDown: 500),
                new Shoot(16, 1, projectileIndex: 5, coolDown: 500),
                new Shoot(16, 1, projectileIndex: 6, coolDown: 500),
                new TimedTransition(12000, "Random")
                   )
              ),
            new Threshold(0.01,
                new ItemLoot("Potion of Speed", 1),
                new ItemLoot("Potion of Dexterity", 1),
                new ItemLoot("Potion of Speed", 0.5),
                new ItemLoot("Magic Woods Key", 0.1, 0, 0.03),
                new ItemLoot("Shield of Flowing Clarity", 0.01),
                new ItemLoot("Spirit Staff", 0.02),
                new ItemLoot("Fountain Child Pet Stone", 0.005),
                new TierLoot(11, ItemType.Weapon, 0.1),
                new TierLoot(5, ItemType.Ability, 0.1),
                new TierLoot(11, ItemType.Armor, 0.1),
                new TierLoot(5, ItemType.Ring, 0.08)
                )
            )
        #endregion

       ;

        // every fairy in the woods, for knowing when a wave from the fountains is beaten
        private static readonly string[] Fairies =
        {
            "MW Drac", "MW Gharr", "MW Iatho", "MW Issz", "MW Lauk", "MW Lorz", "MW Orothi", "MW Radph", "MW Ril",
            "MW Sek", "MW Serl", "MW Seus", "MW Tal", "MW Uoro", "MW Zhiar", "MW Deyst", "MW Eango", "MW Eati",
            "MW Ehoni", "MW Iawa", "MW Laen", "MW Odaru", "MW Rilr", "MW Risrr", "MW SayIt", "MW Scheev", "MW Tiar",
            "MW Vorck", "MW Yangu", "MW Yimi", "MW Darq", "MW Eashy", "MW Eendi", "MW Idrae", "MW Iri", "MW Itani",
            "MW Oalei", "MW Oeti", "MW Oshyu", "MW Queq", "MW Rayr", "MW Urake", "MW Utanu", "MW Vorv"
        };

        // one wave from the four small fountains: `each` fairies of the group from every fountain,
        // then on once none of the woods' fairies is near (or after 40 s, so a straggler lost in the
        // woods can't hold the fight up)
        private static State FairyWave(string name, string group, int each, string next)
        {
            var tosses = new System.Collections.Generic.List<IStateChildren>();
            // the small fountains, as (range, angle) from the spawner
            foreach (var (range, angle) in new[] { (20, 53.13), (20, 126.87), (14.42, 213.69), (14.42, 326.31) })
                for (var i = 0; i < each; i++)
                    tosses.Add(new TossObject(null, range, angle, coolDown: 999999, coolDownOffset: i * 600, group: group));
            var check = name + " Check";
            return new State(name,
                new State(name + " Toss",
                    tosses.Concat(new IStateChildren[] { new TimedTransition(each * 600 + 1000, check) }).ToArray()
                    ),
                new State(check,
                    // within the fountains' reach (14-20 tiles) only: the woods' own fairies stand 25 out
                    new EntitiesNotExistsTransition(22, next, Fairies),
                    new TimedTransition(40000, next)
                    )
                );
        }
    }
}