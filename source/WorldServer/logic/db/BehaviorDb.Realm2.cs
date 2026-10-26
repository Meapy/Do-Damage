using Shared.resources;
using WorldServer.logic.behaviors;
using WorldServer.logic.loot;
using WorldServer.logic.transitions;
using WorldServer.logic.behaviors.@new;
using WorldServer.logic.behaviors.@new.movements;

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

        // Each land's regular monsters stand in for the Godlands' gods one for one
        // (tools/build_realm2.py STAND_INS gives them the god's numbers): the god's movement and
        // shot pattern, its Reproduce, its potion and its dungeon portal.
        private static readonly System.Collections.Generic.Dictionary<string, string> R2StandIns = new System.Collections.Generic.Dictionary<string, string>
        {
            { "R2 Prism Sentinel", "Flying Brain" },
            { "R2 Shard Walker", "White Demon" },
            { "R2 Cube Overseer", "Sprite God" },
            { "R2 Floating Eye", "Ghost God" },
            { "R2 Gazer", "Beholder" },
            { "R2 Watcher Stalk", "Medusa" },
            { "R2 Lost Sentry", "Ent God" },
            { "R2 Forsaken Archer", "White Demon" },
            { "R2 Wailing Wraith", "Ghost God" },
            { "R2 Ruin Golem", "Leviathan" },
            { "R2 Hermit Brute", "Leviathan" },
            { "R2 Tide Snapper", "Slime God" },
            { "R2 Barnacle Turret", "Flying Brain" },
            { "R2 Kelp Lurker", "Medusa" },
            { "R2 Drowned Buccaneer", "Ent God" },
            { "R2 Bone Gunner", "Medusa" },
            { "R2 Wreck Wraith", "Ghost God" },
            { "R2 Tomb Mummy", "Slime God" },
            { "R2 Jackal Guard", "White Demon" },
            { "R2 Sun Obelisk", "Djinn" },
            { "R2 Bone Flayer", "Ent God" },
            { "R2 Blood Imp", "Flying Brain" },
            { "R2 Shrine Cultist", "Sprite God" },
            { "R2 Komainu", "Medusa" },
            { "R2 Oni Mask", "Beholder" },
            { "R2 Kitsune Spirit", "Djinn" },
            { "R2 Drakeling", "White Demon" },
            { "R2 Magma Salamander", "Slime God" },
            { "R2 Ember Wyrm", "Leviathan" },
            { "R2 Obsidian Golem", "Ent God" },
            { "R2 Shattered Knight", "Sprite God" },
            { "R2 Forgotten Mage", "Ghost God" },
            { "R2 Stone Gargoyle", "Beholder" },
            { "R2 Hornet Drone", "Djinn" },
            { "R2 Honey Slime", "Slime God" }
        };

        // god -> (its behaviour, its potion, its dungeon portal and the chance of it)
        private static (System.Func<State> State, string Potion, string Portal, double Chance) R2God(string god) => god switch
        {
            "White Demon" => (() => new State(
                new OrderedBehavior(new NewFollow(range: 7, speed: 1.0f), new NewWander(0.4f)),
                new Shoot(10, count: 3, shootAngle: 20, predictive: 1, coolDown: 500),
                new Reproduce(densityMax: 3)), "Potion of Attack", "Abyss of Demons Portal", 0.25),
            "Sprite God" => (() => new State(
                new OrderedBehavior(new NewWander(0.4f)),
                new Shoot(12, projectileIndex: 0, count: 4, shootAngle: 10),
                new Shoot(10, projectileIndex: 1, predictive: 1),
                new Reproduce(densityMax: 3)), "Potion of Attack", "Glowing Portal", 0.25),
            "Medusa" => (() => new State(
                new OrderedBehavior(new NewFollow(range: 7, speed: 1.0f), new NewWander(0.4f)),
                new Shoot(12, count: 5, shootAngle: 10, coolDown: 1000),
                new Grenade(4, 150, range: 8, coolDown: 3000),
                new Reproduce(densityMax: 3)), "Potion of Speed", "Snake Pit Portal", 0.25),
            "Ent God" => (() => new State(
                new OrderedBehavior(new NewFollow(range: 7, speed: 1.0f), new NewWander(0.4f)),
                new Shoot(12, count: 5, shootAngle: 10, predictive: 1, coolDown: 1250),
                new Reproduce(densityMax: 3)), "Potion of Defense", null, 0),
            "Beholder" => (() => new State(
                new OrderedBehavior(new NewFollow(range: 7, speed: 1.0f), new NewWander(0.4f)),
                new Shoot(12, projectileIndex: 0, count: 5, shootAngle: 72, predictive: 0.5, coolDown: 750),
                new Shoot(10, projectileIndex: 1, predictive: 1),
                new Reproduce(densityMax: 3)), "Potion of Defense", null, 0),
            "Flying Brain" => (() => new State(
                new OrderedBehavior(new NewFollow(range: 7, speed: 1.0f), new NewWander(0.4f)),
                new Shoot(12, count: 5, shootAngle: 72, coolDown: 500),
                new Reproduce(densityMax: 3)), "Potion of Attack", "Mad Lab Portal", 0.17),
            "Slime God" => (() => new State(
                new OrderedBehavior(new NewFollow(range: 7, speed: 1.0f), new NewWander(0.4f)),
                new Shoot(12, projectileIndex: 0, count: 5, shootAngle: 10, predictive: 1, coolDown: 1000),
                new Shoot(10, projectileIndex: 1, predictive: 1, coolDown: 650),
                new Reproduce(densityMax: 2)), "Potion of Defense", "Toxic Sewers Portal", 0.3),
            "Ghost God" => (() => new State(
                new OrderedBehavior(new NewFollow(range: 7, speed: 1.0f), new NewWander(0.4f)),
                new Shoot(12, count: 7, shootAngle: 25, predictive: 0.5, coolDown: 900),
                new Reproduce(densityMax: 3)), "Potion of Speed", "Undead Lair Portal", 0.25),
            "Djinn" => (R2Djinn, "Potion of Speed", "Treasure Cave Portal", 0.25),
            "Leviathan" => (R2Leviathan, "Potion of Defense", "Puppet Theatre Portal", 0.25),
            _ => throw new System.ArgumentException(god),
        };

        // the Djinn's: idle and untouchable until a player comes near, then its turning bullet
        // waves, and at half health a last burst as it bursts
        private static State R2Djinn()
        {
            var bullet = new System.Collections.Generic.List<IStateChildren>();
            for (var i = 0; i < 10; i++)
                bullet.Add(new Shoot(1, count: 4, coolDown: 10000, fixedAngle: 90 + i * 10, coolDownOffset: i * 200, shootAngle: 90));
            bullet.Add(new Shoot(1, count: 8, coolDown: 10000, fixedAngle: 180, coolDownOffset: 2000, shootAngle: 45));
            for (var i = 0; i < 10; i++)
                bullet.Add(new Shoot(1, count: 4, coolDown: 10000, fixedAngle: 180 - i * 10, coolDownOffset: i * 200, shootAngle: 90));
            bullet.Add(new Shoot(1, count: 4, coolDown: 10000, fixedAngle: 0, coolDownOffset: 1800, shootAngle: 90));
            bullet.Add(new Shoot(1, count: 16, coolDown: 10000, fixedAngle: 0, coolDownOffset: 2000, shootAngle: 22.5));
            bullet.Add(new TimedTransition(2000, "Wait"));
            return new State(
                new State("Idle",
                    new OrderedBehavior(new NewWander(0.4f)),
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Reproduce(densityMax: 3, densityRadius: 20),
                    new PlayerWithinTransition(8, "Attacking")
                    ),
                new State("Attacking",
                    new State("Bullet", bullet.ToArray()),
                    new State("Wait",
                        new Follow(0.7, range: 0.5),
                        new Flash(0xff00ff00, 0.1, 20),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(2000, "Bullet")
                        ),
                    new NoPlayerWithinTransition(13, "Idle"),
                    new HpLessTransition(0.5, "FlashBeforeExplode")
                    ),
                new State("FlashBeforeExplode",
                    new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                    new Flash(0xff0000, 0.3, 3),
                    new TimedTransition(1000, "Explode")
                    ),
                new State("Explode",
                    new Shoot(0, count: 10, shootAngle: 36, fixedAngle: 0),
                    new Suicide()
                    )
                );
        }

        // the Leviathan's: swirling with twin shots, then a triangle run firing its three-way bursts
        private static State R2Leviathan()
        {
            State Leg(string name, double angle, int fire, string next) => new State(name,
                new MoveLine(.7, angle),
                new Shoot(1, 3, 120, fixedAngle: fire, coolDown: 300),
                new Shoot(1, 3, 120, fixedAngle: fire + 4, coolDown: 300),
                new Shoot(1, 3, 120, fixedAngle: fire + 8, coolDown: 300),
                new Shoot(1, 3, 120, fixedAngle: fire + 12, coolDown: 300),
                new TimedTransition(1500, next));
            return new State(
                new State("Wander",
                    new Swirl(),
                    new Shoot(10, 2, 10, 1, coolDown: 500),
                    new TimedTransition(5000, "Triangle")
                    ),
                new State("Triangle",
                    Leg("1", 40, 34, "2"),
                    Leg("2", 160, 94, "3"),
                    Leg("3", 280, 274, "Wander")
                    )
                );
        }

        private _ Realm2Gods = () =>
        {
            var ctor = Behav();
            foreach (var (mob, god) in R2StandIns)
            {
                var (state, potion, portal, chance) = R2God(god);
                var root = state();
                if (portal != null)
                    root = new State(new DropPortalOnDeath(portal, chance), root);
                ctor = ctor.Init(mob, root, R2Mob(potion));
            }
            return ctor;
        };

        private _ Realm2 = () => Behav()
        // ---------------------------------------------------------------- the Cubic Expanse
        .Init("R2 Cubelet",
            new State(
                new Prioritize(new StayCloseToSpawn(1.2, 16), new Swirl(1.2, 3, 9), new Wander(0.6)),
                new Shoot(8, 3, shootAngle: 15, coolDown: 900)
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
        .Init("R2 Eye Swarmer",
            new State(
                new Prioritize(new StayCloseToSpawn(1.4, 16), new Swirl(1.4, 2.5, 9), new Wander(0.8)),
                new Shoot(7, coolDown: 500)
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
        .Init("R2 Spectral Gull",
            new State(
                new Prioritize(new StayCloseToSpawn(1.4, 16), new Buzz(1.6, 1.5, 300), new Follow(1.4, 10, 2), new Wander(1)),
                new Shoot(7, 3, shootAngle: 20, coolDown: 800)
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
        .Init("R2 Skull Wisp",
            new State(
                new Prioritize(new StayCloseToSpawn(1.1, 16), new Orbit(1.1, 3.5, 10), new Wander(0.6)),
                new Shoot(10, predictive: 0.5, coolDown: 700)
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
