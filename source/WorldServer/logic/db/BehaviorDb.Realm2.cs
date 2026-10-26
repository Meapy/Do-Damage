using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // Realm 2's monsters (tools/build_realm2.py draws them and writes their XML; the projectile
    // indices here are its MOBS lists). For each event land, two to four monsters as strong as the
    // gods (who roam the lands with them), a few small ones that only its quests bring (Eye
    // Swarmers, Scarabs, Worker Bees...), and two quests: the monsters drop as the gods do, the
    // quests better, with a dungeon of their land's kind now and then.
    partial class BehaviorDb
    {
        // keeps to its patch, comes for players near it, otherwise wanders
        private static Prioritize R2Walk(double speed = 0.8, double acquire = 9, double range = 3) =>
            new Prioritize(new StayCloseToSpawn(speed, 16), new Follow(speed, acquire, range), new Wander(speed * 0.5));

        // keeps its distance from players, shooting from there
        private static Prioritize R2Keep(double speed, double distance) =>
            new Prioritize(new StayCloseToSpawn(speed, 16), new StayBack(speed, distance), new Follow(speed, 11, distance + 1), new Wander(speed * 0.4));

        private static MobDrops[] R2Mob(string potion) => new MobDrops[]
        {
            new Threshold(0.01, LootTemplates.MountainDrop()),
            new Threshold(0.01, new ItemLoot(potion, 0.05)),
        };

        private static MobDrops[] R2Quest(string potionA, string potionB) => new MobDrops[]
        {
            new Threshold(0.025,
                new TierLoot(10, ItemType.Weapon, 0.08), new TierLoot(11, ItemType.Weapon, 0.04),
                new TierLoot(11, ItemType.Armor, 0.08), new TierLoot(12, ItemType.Armor, 0.04),
                new TierLoot(4, ItemType.Ability, 0.08), new TierLoot(5, ItemType.Ability, 0.04),
                new TierLoot(4, ItemType.Ring, 0.08), new TierLoot(5, ItemType.Ring, 0.04)),
            new Threshold(0.025,
                new ItemLoot(potionA, 0.3), new ItemLoot(potionB, 0.3),
                new ItemLoot("Potion of Life", 0.02), new ItemLoot("Potion of Mana", 0.02)),
        };

        private _ Realm2 = () => Behav()
        // ---------------------------------------------------------------- the Cubic Expanse
        .Init("R2 Cubelet",
            new State(
                new Prioritize(new StayCloseToSpawn(1.2, 16), new Swirl(1.2, 3, 9), new Wander(0.6)),
                new Shoot(8, 3, shootAngle: 15, coolDown: 900)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Prism Sentinel",
            new State(
                new Prioritize(new StayCloseToSpawn(0.4, 12), new Wander(0.3)),
                new Shoot(10, 8, projectileIndex: 0, fixedAngle: 0, rotateAngle: 11, coolDown: 1200),
                new Shoot(9, projectileIndex: 1, predictive: 0.6, coolDown: 1800)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Shard Walker",
            new State(
                new State("Close",
                    R2Walk(1.0),
                    new Shoot(8, 3, shootAngle: 12, coolDown: 1000),
                    new TimedTransition(3500, "Rush")
                    ),
                new State("Rush",
                    new Charge(2.2, 9, coolDown: 400),
                    new Shoot(6, 5, shootAngle: 20, coolDown: 600),
                    new TimedTransition(1500, "Close")
                    )
                ),
            R2Mob("Potion of Dexterity"))
        .Init("R2 Cube Overseer",
            new State(
                R2Keep(0.6, 6),
                new Shoot(11, 4, shootAngle: 90, fixedAngle: 45, rotateAngle: 15, coolDown: 1100),
                new Shoot(11, predictive: 0.5, coolDown: 1500)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Tesseract",
            new State(
                new State("Spin",
                    new Prioritize(new StayCloseToSpawn(0.4, 10), new Wander(0.3)),
                    new Shoot(14, 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 0, rotateAngle: 9, coolDown: 250),
                    new Shoot(12, 3, shootAngle: 14, projectileIndex: 2, predictive: 0.4, coolDown: 1400),
                    new TimedTransition(7000, "Fold"),
                    new HpLessTransition(0.35, "Unfold")
                    ),
                new State("Fold",
                    new Flash(0x8D4FE0, 0.3, 4),
                    new Prioritize(new Follow(1.0, 12, 2), new Wander(0.4)),
                    new Shoot(12, 12, projectileIndex: 1, coolDown: 1000),
                    new Spawn("R2 Prism Sentinel", maxChildren: 2, initialSpawn: 0, coolDown: 10000),
                    new TimedTransition(4000, "Spin"),
                    new HpLessTransition(0.35, "Unfold")
                    ),
                new State("Unfold",
                    new Taunt("THE FOURTH SIDE OPENS!"),
                    new Prioritize(new Follow(0.9, 12, 4), new Wander(0.4)),
                    new Shoot(14, 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 0, rotateAngle: 13, coolDown: 200),
                    new Shoot(14, 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 45, rotateAngle: -13, coolDown: 200),
                    new Shoot(12, 5, shootAngle: 12, projectileIndex: 2, predictive: 0.5, coolDown: 1100),
                    new Shoot(12, 10, projectileIndex: 1, coolDown: 2600)
                    ),
                new DropPortalOnDeath("Mad Lab Portal", 0.25)
                ),
            R2Quest("Potion of Wisdom", "Potion of Dexterity"))
        .Init("R2 Hypercube Warden",
            new State(
                new State("Guard",
                    R2Walk(0.7, 11, 4),
                    new Shoot(10, 3, shootAngle: 20, projectileIndex: 0, coolDown: 1300),
                    new Shoot(12, 2, shootAngle: 8, projectileIndex: 1, predictive: 0.7, coolDown: 700),
                    new TimedTransition(5000, "Ram")
                    ),
                new State("Ram",
                    new Flash(0x5FC6E0, 0.25, 3),
                    new Charge(2.4, 11, coolDown: 1200),
                    new Shoot(10, 8, projectileIndex: 0, coolDown: 900),
                    new TimedTransition(3000, "Guard")
                    ),
                new DropPortalOnDeath("Mad Lab Portal", 0.15)
                ),
            R2Quest("Potion of Dexterity", "Potion of Wisdom"))
        // ---------------------------------------------------------------- the Watching Dark
        .Init("R2 Floating Eye",
            new State(
                new Prioritize(new StayCloseToSpawn(0.6, 16), new Orbit(0.6, 5, 10), new Wander(0.4)),
                new Shoot(10, 2, shootAngle: 10, predictive: 0.4, coolDown: 900)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Eye Swarmer",
            new State(
                new Prioritize(new StayCloseToSpawn(1.4, 16), new Swirl(1.4, 2.5, 9), new Wander(0.8)),
                new Shoot(7, coolDown: 500)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Gazer",
            new State(
                R2Walk(0.6, 10, 6),
                new Shoot(12, 5, shootAngle: 72, projectileIndex: 0, predictive: 0.5, coolDown: 900),
                new Shoot(10, projectileIndex: 1, predictive: 1, coolDown: 3000)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Watcher Stalk",
            new State(
                new Shoot(11, projectileIndex: 0, predictive: 0.8, coolDown: 650),
                new Shoot(9, 3, shootAngle: 25, projectileIndex: 1, coolDown: 2600)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Eye of the Pentagram",
            new State(
                new State("Watch",
                    new Prioritize(new StayCloseToSpawn(0.4, 10), new Wander(0.25)),
                    new Shoot(14, 5, shootAngle: 72, projectileIndex: 0, fixedAngle: 0, rotateAngle: 7, coolDown: 300),
                    new Shoot(10, projectileIndex: 2, predictive: 0.7, coolDown: 800),
                    new TimedTransition(6000, "Stare"),
                    new HpLessTransition(0.4, "Bleed")
                    ),
                new State("Stare",
                    new Flash(0xFF4030, 0.4, 3),
                    new Shoot(14, 3, shootAngle: 10, projectileIndex: 1, predictive: 0.5, coolDown: 1200),
                    new Shoot(12, 10, projectileIndex: 0, coolDown: 1500),
                    new Spawn("R2 Eye Swarmer", maxChildren: 2, initialSpawn: 0, coolDown: 6000),
                    new TimedTransition(4500, "Watch"),
                    new HpLessTransition(0.4, "Bleed")
                    ),
                new State("Bleed",
                    new Taunt("I SEE ALL OF YOU."),
                    new Prioritize(new Follow(0.6, 12, 5), new Wander(0.3)),
                    new Shoot(14, 5, shootAngle: 72, projectileIndex: 0, fixedAngle: 0, rotateAngle: 11, coolDown: 220),
                    new Shoot(14, 5, shootAngle: 72, projectileIndex: 0, fixedAngle: 36, rotateAngle: -11, coolDown: 220),
                    new Shoot(10, 2, shootAngle: 8, projectileIndex: 2, predictive: 0.8, coolDown: 600),
                    new Shoot(12, projectileIndex: 1, predictive: 0.5, coolDown: 2500)
                    ),
                new DropPortalOnDeath("Cursed Library Portal", 0.2)
                ),
            R2Quest("Potion of Attack", "Potion of Wisdom"))
        .Init("R2 Elder Watcher",
            new State(
                R2Walk(0.5, 11, 6),
                new Shoot(13, 6, shootAngle: 60, projectileIndex: 0, rotateAngle: 10, predictive: 0.3, coolDown: 800),
                new Shoot(11, projectileIndex: 1, predictive: 1, coolDown: 3500),
                new Spawn("R2 Floating Eye", maxChildren: 3, initialSpawn: 0, coolDown: 8000),
                new DropPortalOnDeath("Cursed Library Portal", 0.15)
                ),
            R2Quest("Potion of Wisdom", "Potion of Attack"))
        // ---------------------------------------------------------------- the Lost Lands
        .Init("R2 Lost Sentry",
            new State(
                new State("March",
                    R2Walk(0.9, 10, 1.5),
                    new Shoot(3, 2, shootAngle: 20, projectileIndex: 0, coolDown: 500),
                    new Shoot(9, 3, shootAngle: 15, projectileIndex: 1, coolDown: 1400),
                    new TimedTransition(4000, "Lunge")
                    ),
                new State("Lunge",
                    new Charge(2.5, 9, coolDown: 600),
                    new Shoot(4, 3, shootAngle: 25, projectileIndex: 0, coolDown: 400),
                    new TimedTransition(1200, "March")
                    )
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Forsaken Archer",
            new State(
                R2Keep(0.8, 7),
                new Shoot(13, projectileIndex: 0, predictive: 0.9, coolDown: 800),
                new Shoot(13, 3, shootAngle: 12, projectileIndex: 0, coolDown: 2400, coolDownOffset: 1200)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Wailing Wraith",
            new State(
                new Prioritize(new StayCloseToSpawn(0.8, 16), new Orbit(0.9, 4, 10), new Wander(0.5)),
                new Shoot(9, 4, shootAngle: 90, rotateAngle: 20, coolDown: 700)
                ),
            R2Mob("Potion of Vitality"))
        .Init("R2 Ruin Golem",
            new State(
                R2Walk(0.4, 9, 2),
                new Shoot(10, 6, shootAngle: 60, coolDown: 1800),
                new Shoot(10, 2, shootAngle: 10, predictive: 0.4, coolDown: 1200)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Sentry Captain",
            new State(
                new State("Drill",
                    R2Walk(0.8, 12, 3),
                    new Shoot(4, 3, shootAngle: 30, projectileIndex: 0, coolDown: 600),
                    new Shoot(12, 5, shootAngle: 15, projectileIndex: 2, predictive: 0.6, coolDown: 1300),
                    new TimedTransition(5000, "Formation")
                    ),
                new State("Formation",
                    new Taunt("Sentries, to me!"),
                    new Flash(0x5AA6B8, 0.3, 4),
                    new Shoot(12, 12, projectileIndex: 1, coolDown: 900),
                    new Spawn("R2 Lost Sentry", maxChildren: 3, initialSpawn: 0, coolDown: 9000),
                    new TimedTransition(3500, "Drill")
                    ),
                new DropPortalOnDeath("Haunted Cemetery Portal", 0.15)
                ),
            R2Quest("Potion of Defense", "Potion of Vitality"))
        .Init("R2 Forgotten Warlord",
            new State(
                new State("Advance",
                    R2Walk(0.6, 12, 4),
                    new Shoot(13, 3, shootAngle: 20, projectileIndex: 0, predictive: 0.4, coolDown: 900),
                    new Shoot(8, 5, shootAngle: 24, projectileIndex: 1, coolDown: 1600),
                    new TimedTransition(6000, "Whirl"),
                    new HpLessTransition(0.35, "Last Stand")
                    ),
                new State("Whirl",
                    new Prioritize(new Follow(1.2, 12, 1), new Wander(0.5)),
                    new Shoot(10, 8, projectileIndex: 1, rotateAngle: 22, coolDown: 450),
                    new TimedTransition(3000, "Advance"),
                    new HpLessTransition(0.35, "Last Stand")
                    ),
                new State("Last Stand",
                    new Taunt("The lands forget. I DO NOT."),
                    new Prioritize(new Follow(0.9, 12, 3), new Wander(0.5)),
                    new Shoot(13, 10, projectileIndex: 0, rotateAngle: 9, coolDown: 600),
                    new Shoot(8, 5, shootAngle: 24, projectileIndex: 1, coolDown: 1100),
                    new Spawn("R2 Lost Sentry", maxChildren: 2, initialSpawn: 0, coolDown: 12000)
                    ),
                new DropPortalOnDeath("Haunted Cemetery Portal", 0.25)
                ),
            R2Quest("Potion of Vitality", "Potion of Defense"))
        // ---------------------------------------------------------------- the Tidepools
        .Init("R2 Hermit Brute",
            new State(
                R2Walk(0.5, 9, 2),
                new Shoot(10, 3, shootAngle: 18, predictive: 0.3, coolDown: 1100),
                new Shoot(10, 6, shootAngle: 60, coolDown: 2400)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Tide Snapper",
            new State(
                new State("Scuttle",
                    new Prioritize(new StayCloseToSpawn(1.0, 16), new Follow(1.1, 9, 4), new Wander(0.7)),
                    new Shoot(9, 2, shootAngle: 30, coolDown: 1200),
                    new TimedTransition(3000, "Snap")
                    ),
                new State("Snap",
                    new Charge(2.6, 8, coolDown: 500),
                    new Shoot(6, 4, shootAngle: 90, coolDown: 500),
                    new TimedTransition(1000, "Scuttle")
                    )
                ),
            R2Mob("Potion of Vitality"))
        .Init("R2 Barnacle Turret",
            new State(
                new Shoot(10, 6, shootAngle: 60, rotateAngle: 15, coolDown: 900),
                new Shoot(10, predictive: 0.6, coolDown: 1300)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Kelp Lurker",
            new State(
                R2Keep(0.6, 5),
                new Shoot(11, 3, shootAngle: 35, predictive: 0.3, coolDown: 1500)
                ),
            R2Mob("Potion of Vitality"))
        .Init("R2 Shellback Champion",
            new State(
                new State("Shell",
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                    R2Walk(0.4, 12, 3),
                    new Shoot(12, 8, projectileIndex: 0, rotateAngle: 12, coolDown: 700),
                    new TimedTransition(5000, "Out")
                    ),
                new State("Out",
                    new Flash(0xE07858, 0.3, 3),
                    new Prioritize(new Follow(1.0, 12, 2), new Wander(0.4)),
                    new Shoot(12, 3, shootAngle: 14, projectileIndex: 1, predictive: 0.6, coolDown: 600),
                    new Shoot(12, 12, projectileIndex: 0, coolDown: 1600),
                    new TimedTransition(4500, "Shell")
                    ),
                new DropPortalOnDeath("Ocean Trench Portal", 0.2)
                ),
            R2Quest("Potion of Defense", "Potion of Vitality"))
        .Init("R2 Ancient Snapper",
            new State(
                new State("Circle",
                    new Prioritize(new StayCloseToSpawn(0.9, 12), new Orbit(0.9, 6, 12), new Wander(0.5)),
                    new Shoot(12, 2, shootAngle: 40, projectileIndex: 0, predictive: 0.5, coolDown: 900),
                    new Shoot(12, 5, shootAngle: 72, projectileIndex: 1, rotateAngle: 18, coolDown: 1300),
                    new TimedTransition(5000, "Snap")
                    ),
                new State("Snap",
                    new Charge(2.8, 12, coolDown: 900),
                    new Shoot(8, 6, shootAngle: 60, projectileIndex: 0, coolDown: 700),
                    new TimedTransition(2400, "Circle")
                    ),
                new DropPortalOnDeath("Ocean Trench Portal", 0.25)
                ),
            R2Quest("Potion of Vitality", "Potion of Defense"))
        // ---------------------------------------------------------------- the Wreck Coast
        .Init("R2 Drowned Buccaneer",
            new State(
                R2Walk(0.9, 10, 1.5),
                new Shoot(3, 2, shootAngle: 15, projectileIndex: 0, coolDown: 450),
                new Shoot(9, projectileIndex: 1, predictive: 0.5, coolDown: 1500)
                ),
            R2Mob("Potion of Dexterity"))
        .Init("R2 Spectral Gull",
            new State(
                new Prioritize(new StayCloseToSpawn(1.4, 16), new Buzz(1.6, 1.5, 300), new Follow(1.4, 10, 2), new Wander(1)),
                new Shoot(7, 3, shootAngle: 20, coolDown: 800)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Bone Gunner",
            new State(
                R2Keep(0.6, 7),
                new Shoot(12, predictive: 0.8, coolDown: 1400),
                new Grenade(2.5, 110, 8, coolDown: 3000, color: 0x3a3a3a)
                ),
            R2Mob("Potion of Dexterity"))
        .Init("R2 Wreck Wraith",
            new State(
                new Prioritize(new StayCloseToSpawn(0.8, 16), new Orbit(0.8, 5, 10), new Wander(0.5)),
                new Shoot(10, 3, shootAngle: 120, rotateAngle: 17, coolDown: 600)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Phantom Quartermaster",
            new State(
                new State("Orders",
                    R2Keep(0.7, 6),
                    new Shoot(13, 4, shootAngle: 12, projectileIndex: 0, predictive: 0.5, coolDown: 1000),
                    new Spawn("R2 Drowned Buccaneer", maxChildren: 3, initialSpawn: 0, coolDown: 8000),
                    new TimedTransition(5000, "Broadside")
                    ),
                new State("Broadside",
                    new Taunt("Fire the cannons!"),
                    new Shoot(13, 3, shootAngle: 30, projectileIndex: 1, predictive: 0.4, coolDown: 700),
                    new Grenade(3, 140, 9, coolDown: 1200, color: 0x3a3a3a),
                    new Spawn("R2 Spectral Gull", maxChildren: 2, initialSpawn: 0, coolDown: 5000),
                    new TimedTransition(3500, "Orders")
                    ),
                new DropPortalOnDeath("Davy Jones' Locker Portal", 0.2)
                ),
            R2Quest("Potion of Dexterity", "Potion of Speed"))
        .Init("R2 Abyssal Kraken",
            new State(
                new State("Lurk",
                    new Prioritize(new StayCloseToSpawn(0.4, 8), new Wander(0.2)),
                    new Shoot(13, 8, projectileIndex: 1, fixedAngle: 0, rotateAngle: 8, coolDown: 450),
                    new Shoot(13, 2, shootAngle: 20, projectileIndex: 0, predictive: 0.4, coolDown: 1200),
                    new TimedTransition(6000, "Rise"),
                    new HpLessTransition(0.4, "Frenzy")
                    ),
                new State("Rise",
                    new Flash(0x3AC080, 0.3, 4),
                    new Prioritize(new Follow(0.8, 12, 3), new Wander(0.3)),
                    new Shoot(13, 16, projectileIndex: 1, coolDown: 1000),
                    new TimedTransition(3500, "Lurk"),
                    new HpLessTransition(0.4, "Frenzy")
                    ),
                new State("Frenzy",
                    new Taunt("The deep takes everything back."),
                    new Prioritize(new Follow(0.7, 12, 3), new Wander(0.3)),
                    new Shoot(13, 8, projectileIndex: 1, fixedAngle: 0, rotateAngle: 12, coolDown: 350),
                    new Shoot(13, 8, projectileIndex: 1, fixedAngle: 22, rotateAngle: -12, coolDown: 350),
                    new Shoot(13, 3, shootAngle: 15, projectileIndex: 0, predictive: 0.5, coolDown: 1000)
                    ),
                new DropPortalOnDeath("Davy Jones' Locker Portal", 0.25)
                ),
            R2Quest("Potion of Speed", "Potion of Dexterity"))
        // ---------------------------------------------------------------- the Sunken Tombs
        .Init("R2 Scarab",
            new State(
                new Prioritize(new StayCloseToSpawn(1.3, 16), new Swirl(1.3, 2, 9), new Wander(0.8)),
                new Shoot(7, 2, shootAngle: 30, coolDown: 600)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Tomb Mummy",
            new State(
                R2Walk(0.5, 9, 2),
                new Shoot(10, 3, shootAngle: 20, coolDown: 1200),
                new Shoot(10, predictive: 0.5, coolDown: 2000)
                ),
            R2Mob("Potion of Vitality"))
        .Init("R2 Jackal Guard",
            new State(
                new State("Watch",
                    R2Walk(0.9, 10, 4),
                    new Shoot(12, 2, shootAngle: 8, predictive: 0.8, coolDown: 900),
                    new TimedTransition(3500, "Pounce")
                    ),
                new State("Pounce",
                    new Charge(2.4, 10, coolDown: 500),
                    new Shoot(6, 3, shootAngle: 20, coolDown: 400),
                    new TimedTransition(1200, "Watch")
                    )
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Sun Obelisk",
            new State(
                new Shoot(11, 4, shootAngle: 90, fixedAngle: 0, rotateAngle: 10, coolDown: 400)
                ),
            R2Mob("Potion of Vitality"))
        .Init("R2 Tomb Sentinel",
            new State(
                new State("Rays",
                    new Prioritize(new StayCloseToSpawn(0.5, 10), new Follow(0.5, 12, 5), new Wander(0.3)),
                    new Shoot(14, 4, shootAngle: 90, projectileIndex: 0, fixedAngle: 0, rotateAngle: 8, coolDown: 300),
                    new Shoot(12, 3, shootAngle: 10, projectileIndex: 1, predictive: 0.6, coolDown: 1000),
                    new TimedTransition(7000, "Judgement")
                    ),
                new State("Judgement",
                    new Taunt("Who disturbs the tomb?"),
                    new Flash(0xD8A020, 0.3, 5),
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                    new Shoot(14, 16, projectileIndex: 0, coolDown: 800),
                    new Spawn("R2 Scarab", maxChildren: 3, initialSpawn: 0, coolDown: 5000),
                    new TimedTransition(3200, "Rays")
                    ),
                new DropPortalOnDeath("Tomb of the Ancients Portal", 0.15)
                ),
            R2Quest("Potion of Vitality", "Potion of Speed"))
        .Init("R2 Sand Scourge",
            new State(
                new State("Coil",
                    new Prioritize(new StayCloseToSpawn(1.0, 14), new Orbit(1.0, 7, 12), new Wander(0.6)),
                    new Shoot(12, 6, shootAngle: 10, projectileIndex: 0, predictive: 0.3, coolDown: 900),
                    new TimedTransition(5000, "Strike")
                    ),
                new State("Strike",
                    new Charge(2.6, 12, coolDown: 800),
                    new Shoot(9, 3, shootAngle: 30, projectileIndex: 1, coolDown: 700),
                    new TimedTransition(2500, "Coil")
                    ),
                new DropPortalOnDeath("Tomb of the Ancients Portal", 0.25)
                ),
            R2Quest("Potion of Speed", "Potion of Vitality"))
        // ---------------------------------------------------------------- the Bonefields
        .Init("R2 Bone Flayer",
            new State(
                R2Walk(0.9, 10, 2),
                new Shoot(8, 3, shootAngle: 15, coolDown: 800)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Skull Wisp",
            new State(
                new Prioritize(new StayCloseToSpawn(1.1, 16), new Orbit(1.1, 3.5, 10), new Wander(0.6)),
                new Shoot(10, predictive: 0.5, coolDown: 700)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Blood Imp",
            new State(
                new Prioritize(new StayCloseToSpawn(1.2, 16), new Buzz(1.4, 1.2, 500), new Follow(1.2, 9, 2), new Wander(0.8)),
                new Shoot(8, 2, shootAngle: 20, coolDown: 600)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Shrine Cultist",
            new State(
                R2Keep(0.6, 6),
                new Shoot(11, 3, shootAngle: 25, predictive: 0.3, coolDown: 1300),
                new Grenade(2, 90, 7, coolDown: 3200, color: 0xE06010),
                new HealGroup(8, "Realm 2 skull", coolDown: 4000, healAmount: 300)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Shrine Keeper",
            new State(
                new State("Rite",
                    R2Keep(0.5, 6),
                    new Shoot(13, 5, shootAngle: 15, projectileIndex: 0, predictive: 0.4, coolDown: 1100),
                    new Shoot(12, 6, shootAngle: 60, projectileIndex: 1, rotateAngle: 12, coolDown: 700),
                    new Spawn("R2 Skull Wisp", maxChildren: 2, initialSpawn: 0, coolDown: 8000),
                    new TimedTransition(6000, "Pyre"),
                    new HpLessTransition(0.3, "Ascend")
                    ),
                new State("Pyre",
                    new Flash(0xFFC040, 0.25, 6),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Grenade(3, 150, 10, coolDown: 600, color: 0xE06010, scatter: 3),
                    new TimedTransition(2000, "Rite")
                    ),
                new State("Ascend",
                    new Taunt("The shrine wants more bones!"),
                    new Prioritize(new Follow(0.7, 12, 4), new Wander(0.4)),
                    new Shoot(13, 8, projectileIndex: 1, rotateAngle: 15, coolDown: 400),
                    new Shoot(13, 5, shootAngle: 12, projectileIndex: 0, predictive: 0.5, coolDown: 900),
                    new Grenade(3, 150, 10, coolDown: 2500, color: 0xE06010)
                    ),
                new DropPortalOnDeath("Undead Lair Portal", 0.2)
                ),
            R2Quest("Potion of Attack", "Potion of Defense"))
        .Init("R2 Marrow Fiend",
            new State(
                new State("Stalk",
                    R2Walk(0.8, 12, 3),
                    new Shoot(11, 4, shootAngle: 12, projectileIndex: 0, coolDown: 900),
                    new TimedTransition(4000, "Rend")
                    ),
                new State("Rend",
                    new Charge(2.6, 11, coolDown: 700),
                    new Shoot(7, 10, projectileIndex: 1, coolDown: 800),
                    new TimedTransition(2500, "Stalk")
                    ),
                new DropPortalOnDeath("Undead Lair Portal", 0.15)
                ),
            R2Quest("Potion of Defense", "Potion of Attack"))
        // ---------------------------------------------------------------- the Jade Garden
        .Init("R2 Komainu",
            new State(
                new State("Guard",
                    new Prioritize(new StayCloseToSpawn(0.6, 10), new Follow(0.7, 8, 3), new Wander(0.3)),
                    new Shoot(9, 3, shootAngle: 20, coolDown: 1100),
                    new TimedTransition(4000, "Leap")
                    ),
                new State("Leap",
                    new Charge(2.4, 9, coolDown: 600),
                    new Shoot(6, 6, shootAngle: 60, coolDown: 600),
                    new TimedTransition(1200, "Guard")
                    )
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Oni Mask",
            new State(
                new Prioritize(new StayCloseToSpawn(0.8, 16), new Orbit(0.8, 4.5, 10), new Wander(0.5)),
                new Shoot(10, 5, shootAngle: 14, predictive: 0.2, coolDown: 1400)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Kitsune Spirit",
            new State(
                R2Keep(1.0, 6),
                new Shoot(11, 3, shootAngle: 40, projectileIndex: 0, rotateAngle: 20, coolDown: 700),
                new Shoot(11, projectileIndex: 1, predictive: 0.7, coolDown: 1200)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Lantern Wisp",
            new State(
                new Prioritize(new StayCloseToSpawn(0.7, 16), new Follow(0.7, 9, 4), new Wander(0.5)),
                new Shoot(9, 6, shootAngle: 60, rotateAngle: 10, coolDown: 1000)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Oni Warlord",
            new State(
                new State("Rage",
                    R2Walk(0.9, 12, 2),
                    new Shoot(12, 5, shootAngle: 12, projectileIndex: 0, predictive: 0.3, coolDown: 800),
                    new Shoot(6, 4, shootAngle: 30, projectileIndex: 1, coolDown: 1200),
                    new TimedTransition(5000, "Storm")
                    ),
                new State("Storm",
                    new Taunt("RAAAARGH!"),
                    new Flash(0xFF5A6A, 0.2, 6),
                    new Prioritize(new Follow(1.4, 12, 1), new Wander(0.6)),
                    new Shoot(10, 10, projectileIndex: 0, rotateAngle: 18, coolDown: 500),
                    new TimedTransition(3000, "Rage")
                    ),
                new DropPortalOnDeath("Magic Woods Portal", 0.2)
                ),
            R2Quest("Potion of Speed", "Potion of Wisdom"))
        .Init("R2 Jade Golem",
            new State(
                new State("Stand",
                    R2Walk(0.35, 11, 3),
                    new Shoot(13, 10, projectileIndex: 0, rotateAngle: 6, coolDown: 1000),
                    new Shoot(11, 3, shootAngle: 15, projectileIndex: 1, predictive: 0.5, coolDown: 900),
                    new TimedTransition(6000, "Harden")
                    ),
                new State("Harden",
                    new Flash(0x90F0C0, 0.3, 4),
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                    new Shoot(13, 20, projectileIndex: 0, coolDown: 1200),
                    new Spawn("R2 Lantern Wisp", maxChildren: 2, initialSpawn: 0, coolDown: 5000),
                    new TimedTransition(3000, "Stand")
                    ),
                new DropPortalOnDeath("Magic Woods Portal", 0.15)
                ),
            R2Quest("Potion of Wisdom", "Potion of Speed"))
        // ---------------------------------------------------------------- the Ashen Caldera
        .Init("R2 Drakeling",
            new State(
                new Prioritize(new StayCloseToSpawn(1.0, 16), new Orbit(1.0, 4, 10), new Wander(0.6)),
                new Shoot(10, 2, shootAngle: 12, predictive: 0.5, coolDown: 800)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Magma Salamander",
            new State(
                R2Walk(0.8, 9, 3),
                new Shoot(10, 3, shootAngle: 15, coolDown: 1000),
                new Grenade(2, 100, 7, coolDown: 3500, color: 0xFF7010)
                ),
            R2Mob("Potion of Dexterity"))
        .Init("R2 Ember Wyrm",
            new State(
                new Prioritize(new StayCloseToSpawn(0.9, 16), new Orbit(0.9, 6, 11), new Wander(0.6)),
                new Shoot(12, 3, shootAngle: 25, predictive: 0.3, coolDown: 1200)
                ),
            R2Mob("Potion of Attack"))
        .Init("R2 Obsidian Golem",
            new State(
                R2Walk(0.35, 9, 2),
                new Shoot(10, 8, shootAngle: 45, coolDown: 2000),
                new Shoot(10, predictive: 0.6, coolDown: 1400)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Wyrm Matriarch",
            new State(
                new State("Soar",
                    new Prioritize(new StayCloseToSpawn(0.9, 12), new Orbit(0.9, 7, 13), new Wander(0.5)),
                    new Shoot(13, 3, shootAngle: 20, projectileIndex: 0, predictive: 0.4, coolDown: 1000),
                    new Shoot(13, 6, shootAngle: 60, projectileIndex: 1, rotateAngle: 14, coolDown: 600),
                    new TimedTransition(6000, "Breath"),
                    new HpLessTransition(0.35, "Inferno")
                    ),
                new State("Breath",
                    new Flash(0xFF7010, 0.25, 5),
                    new Prioritize(new Follow(0.6, 12, 5), new Wander(0.3)),
                    new Shoot(12, 7, shootAngle: 9, projectileIndex: 0, predictive: 0.3, coolDown: 400),
                    new Spawn("R2 Drakeling", maxChildren: 3, initialSpawn: 0, coolDown: 6000),
                    new TimedTransition(3000, "Soar"),
                    new HpLessTransition(0.35, "Inferno")
                    ),
                new State("Inferno",
                    new Taunt("My brood will feast on ashes!"),
                    new Prioritize(new Orbit(1.1, 5, 13), new Wander(0.5)),
                    new Shoot(13, 8, projectileIndex: 1, rotateAngle: 16, coolDown: 400),
                    new Shoot(13, 5, shootAngle: 12, projectileIndex: 0, predictive: 0.4, coolDown: 900),
                    new Grenade(3, 140, 9, coolDown: 2000, color: 0xFF7010)
                    ),
                new DropPortalOnDeath("Lair of Draconis Portal", 0.2)
                ),
            R2Quest("Potion of Attack", "Potion of Dexterity"))
        .Init("R2 Elder Drake",
            new State(
                new State("Prowl",
                    R2Walk(0.6, 12, 4),
                    new Shoot(12, 3, shootAngle: 25, projectileIndex: 0, coolDown: 1000),
                    new Shoot(12, 2, shootAngle: 10, projectileIndex: 1, predictive: 0.6, coolDown: 800),
                    new TimedTransition(5000, "Roar")
                    ),
                new State("Roar",
                    new Taunt("Grrraaah!"),
                    new Shoot(12, 12, projectileIndex: 0, coolDown: 900),
                    new Spawn("R2 Drakeling", maxChildren: 2, initialSpawn: 0, coolDown: 9000),
                    new TimedTransition(2500, "Prowl")
                    ),
                new DropPortalOnDeath("Lair of Draconis Portal", 0.15)
                ),
            R2Quest("Potion of Dexterity", "Potion of Attack"))
        // ---------------------------------------------------------------- the Forgotten Kingdom
        .Init("R2 Shattered Knight",
            new State(
                R2Walk(0.8, 10, 1.5),
                new Shoot(5, 3, shootAngle: 25, projectileIndex: 0, coolDown: 900),
                new Shoot(3, projectileIndex: 1, coolDown: 400)
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Forgotten Mage",
            new State(
                R2Keep(0.7, 7),
                new Shoot(12, 2, shootAngle: 10, projectileIndex: 0, predictive: 0.6, coolDown: 900),
                new Shoot(10, 6, shootAngle: 60, projectileIndex: 1, coolDown: 2200)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Stone Gargoyle",
            new State(
                new State("Perch",
                    new Prioritize(new StayCloseToSpawn(0.5, 10), new Wander(0.2)),
                    new Shoot(10, 2, shootAngle: 15, coolDown: 1200),
                    new PlayerWithinTransition(6, "Dive")
                    ),
                new State("Dive",
                    new Charge(2.6, 10, coolDown: 700),
                    new Shoot(8, 4, shootAngle: 90, rotateAngle: 45, coolDown: 500),
                    new TimedTransition(3000, "Perch")
                    )
                ),
            R2Mob("Potion of Defense"))
        .Init("R2 Soul Lantern",
            new State(
                new Prioritize(new StayCloseToSpawn(0.6, 16), new Orbit(0.6, 5, 10), new Wander(0.4)),
                new Shoot(11, 8, projectileIndex: 0, rotateAngle: 12, coolDown: 1400)
                ),
            R2Mob("Potion of Wisdom"))
        .Init("R2 Forgotten Archmage",
            new State(
                new State("Weave",
                    R2Keep(0.6, 7),
                    new Shoot(14, 3, shootAngle: 120, projectileIndex: 0, rotateAngle: 11, coolDown: 200),
                    new Shoot(12, 2, shootAngle: 10, projectileIndex: 1, predictive: 0.6, coolDown: 1400),
                    new Spawn("R2 Soul Lantern", maxChildren: 2, initialSpawn: 0, coolDown: 8000),
                    new TimedTransition(6000, "Blink"),
                    new HpLessTransition(0.35, "Unravel")
                    ),
                new State("Blink",
                    new Flash(0xB898F0, 0.2, 4),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Prioritize(new Follow(1.6, 12, 6), new Wander(0.8)),
                    new Shoot(12, 10, projectileIndex: 0, coolDown: 700),
                    new TimedTransition(1800, "Weave")
                    ),
                new State("Unravel",
                    new Taunt("Remember the kingdom... in pieces!"),
                    new Prioritize(new Follow(0.6, 12, 5), new Wander(0.4)),
                    new Shoot(14, 3, shootAngle: 120, projectileIndex: 0, rotateAngle: 14, coolDown: 160),
                    new Shoot(14, 3, shootAngle: 120, projectileIndex: 0, fixedAngle: 60, rotateAngle: -14, coolDown: 160),
                    new Shoot(12, 3, shootAngle: 12, projectileIndex: 1, predictive: 0.5, coolDown: 1600)
                    ),
                new DropPortalOnDeath("The Shatters", 0.1)
                ),
            R2Quest("Potion of Wisdom", "Potion of Defense"))
        .Init("R2 King's Champion",
            new State(
                new State("Duel",
                    R2Walk(0.8, 12, 2),
                    new Shoot(8, 5, shootAngle: 20, projectileIndex: 0, coolDown: 1000),
                    new Shoot(4, 3, shootAngle: 15, projectileIndex: 1, coolDown: 500),
                    new TimedTransition(5000, "Rally")
                    ),
                new State("Rally",
                    new Taunt("For the Forgotten King!"),
                    new Flash(0xFFD24A, 0.3, 4),
                    new ConditionEffectBehavior(ConditionEffectIndex.Armored),
                    new Shoot(12, 16, projectileIndex: 0, coolDown: 1100),
                    new Spawn("R2 Shattered Knight", maxChildren: 2, initialSpawn: 0, coolDown: 10000),
                    new TimedTransition(3000, "Duel")
                    ),
                new DropPortalOnDeath("The Shatters", 0.1)
                ),
            R2Quest("Potion of Defense", "Potion of Wisdom"))
        // ---------------------------------------------------------------- the Honey Meadow
        .Init("R2 Worker Bee",
            new State(
                new Prioritize(new StayCloseToSpawn(1.4, 16), new Buzz(1.6, 1, 400), new Swirl(1.4, 2.5, 9), new Wander(0.9)),
                new Shoot(7, coolDown: 450)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Hornet Drone",
            new State(
                new State("Hover",
                    new Prioritize(new StayCloseToSpawn(1.2, 16), new Orbit(1.2, 4, 10), new Wander(0.8)),
                    new Shoot(9, 2, shootAngle: 10, predictive: 0.5, coolDown: 700),
                    new TimedTransition(3000, "Sting")
                    ),
                new State("Sting",
                    new Charge(3, 9, coolDown: 400),
                    new Shoot(5, 3, shootAngle: 15, coolDown: 300),
                    new TimedTransition(900, "Hover")
                    )
                ),
            R2Mob("Potion of Dexterity"))
        .Init("R2 Honey Slime",
            new State(
                R2Walk(0.4, 8, 2),
                new Shoot(9, 5, shootAngle: 72, coolDown: 1600)
                ),
            R2Mob("Potion of Speed"))
        .Init("R2 Pollen Sprite",
            new State(
                new Prioritize(new StayCloseToSpawn(0.8, 16), new Orbit(0.8, 3, 9), new Wander(0.6)),
                new Shoot(9, 4, shootAngle: 90, rotateAngle: 25, coolDown: 900)
                ),
            R2Mob("Potion of Dexterity"))
        .Init("R2 Royal Guard Bee",
            new State(
                new State("Patrol",
                    new Prioritize(new StayCloseToSpawn(1.0, 12), new Orbit(1.0, 6, 12), new Wander(0.6)),
                    new Shoot(12, 2, shootAngle: 30, projectileIndex: 0, predictive: 0.4, coolDown: 1100),
                    new Shoot(10, 5, shootAngle: 10, projectileIndex: 1, coolDown: 800),
                    new TimedTransition(5000, "Defend")
                    ),
                new State("Defend",
                    new Flash(0xF8C030, 0.25, 5),
                    new Charge(2.5, 12, coolDown: 900),
                    new Shoot(10, 8, projectileIndex: 0, coolDown: 1000),
                    new Spawn("R2 Worker Bee", maxChildren: 2, initialSpawn: 0, coolDown: 7000),
                    new TimedTransition(3500, "Patrol")
                    ),
                new DropPortalOnDeath("The Hive Portal", 0.15)
                ),
            R2Quest("Potion of Speed", "Potion of Dexterity"))
        .Init("R2 Hornet Queen",
            new State(
                new State("Court",
                    new Prioritize(new StayCloseToSpawn(0.7, 10), new Orbit(0.7, 5, 12), new Wander(0.4)),
                    new Shoot(13, 3, shootAngle: 120, projectileIndex: 0, rotateAngle: 13, coolDown: 500),
                    new Shoot(11, 4, shootAngle: 10, projectileIndex: 1, predictive: 0.5, coolDown: 900),
                    new Spawn("R2 Hornet Drone", maxChildren: 2, initialSpawn: 0, coolDown: 9000),
                    new TimedTransition(6000, "Swarm"),
                    new HpLessTransition(0.35, "Fury")
                    ),
                new State("Swarm",
                    new Taunt("Bzzzz... protect your queen!"),
                    new Shoot(13, 12, projectileIndex: 1, coolDown: 600),
                    new Spawn("R2 Worker Bee", maxChildren: 3, initialSpawn: 0, coolDown: 3000),
                    new TimedTransition(3000, "Court"),
                    new HpLessTransition(0.35, "Fury")
                    ),
                new State("Fury",
                    new Flash(0xE04020, 0.3, 4),
                    new Prioritize(new Follow(1.0, 12, 3), new Wander(0.5)),
                    new Shoot(13, 3, shootAngle: 120, projectileIndex: 0, rotateAngle: 17, coolDown: 350),
                    new Shoot(11, 6, shootAngle: 10, projectileIndex: 1, predictive: 0.5, coolDown: 700)
                    ),
                new DropPortalOnDeath("The Hive Portal", 0.25)
                ),
            R2Quest("Potion of Dexterity", "Potion of Speed"))
        ;
    }
}
