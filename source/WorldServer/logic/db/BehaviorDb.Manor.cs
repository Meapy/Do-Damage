#region

using System.Collections.Generic;
using Shared.resources;
using WorldServer.logic.loot;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

#endregion

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ Manor = () => ManorBehaviours(Behav(), false);
        private _ ElderManor = () => ManorBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs). The map is tools/build_manor.py's.
        private static Ctor ManorBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);
            MobDrops ElderLoot(double threshold, params MobDrops[] drops) => new Threshold(threshold, elder ? drops : new MobDrops[0]);

            // one of Lord Ruthven's phases: daggers while he stalks the nearest player, then rings of
            // shots from the middle of his hall while he can't be hurt, then a few seconds as a swarm
            // of bats that sends out swarmers; each phase throws more daggers, waits less between
            // rings and sends more swarmers. Below nextHp he moves on to the next phase (the last
            // has none).
            State Phase(string p, int daggers, int ringCd, string next = null, double nextHp = 0)
            {
                var phase = new List<IStateChildren>
                {
                    new State(p + " Daggers",
                        new SetAltTexture(0),
                        new Prioritize(
                            new Follow(0.6, 12, 4),
                            new Wander(0.3)
                            ),
                        new Shoot(12, count: daggers, shootAngle: 10, projectileIndex: 0, predictive: 0.4, coolDown: Cd(900)),
                        new Shoot(12, count: 1, projectileIndex: 2, coolDown: Cd(2600), coolDownOffset: 1300),
                        new TimedTransition(7000, p + " Rings")
                        ),
                    new State(p + " Rings",
                        new SetAltTexture(2),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new ReturnToSpawn(1.2),
                        new Shoot(15, count: 20, projectileIndex: 1, coolDown: Cd(ringCd)),
                        new Shoot(15, count: 20, projectileIndex: 1, angleOffset: 9, coolDown: Cd(ringCd), coolDownOffset: Cd(ringCd) / 2),
                        new TimedTransition(4000, p + " Bats")
                        ),
                    new State(p + " Bats",
                        new SetAltTexture(1),
                        new ConditionEffectBehavior(ConditionEffectIndex.Invincible),
                        new Spawn(N("Vampire Bat"), maxChildren: 5, initialSpawn: 1),
                        new Spawn(N("Vampire Bat Swarmer 1"), maxChildren: daggers / 2 + 1, initialSpawn: 0, coolDown: Cd(1000)),
                        new Wander(0.8),
                        new TimedTransition(5000, p + " Daggers")
                        )
                };
                if (next != null)
                    phase.Insert(0, new HpLessTransition(nextHp, next));
                return new State(p, phase.ToArray());
            }

            return db
            .Init(E("Lord Ruthven"),
                new State(
                    new ScaleHP2(15),
                    // his Coffin holds the loot (and the Elder portal); the way out opens as he dies,
                    // so the world doesn't open another one (World.BossDied: the Coffin is no quest)
                    new TransformOnDeath(N("Coffin")),
                    new DropPortalOnDeath("Realm Portal", probability: 1.0, timeout: null, yAdjustment: 3),
                    new State("Asleep",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new PlayerWithinTransition(8, "Wake")
                        ),
                    new State("Wake",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new SetAltTexture(2),
                        new Taunt("Who dares wake me? Your blood will do."),
                        new TimedTransition(2000, "Thirst")
                        ),
                    Phase("Thirst", 3, 1600, "Hunger", 0.6),
                    // the later phases begin with a pair of Lil Feratus
                    new State("Hunger",
                        new Taunt("You only feed my hunger!"),
                        new Spawn(N("Lil Feratu"), maxChildren: 2, initialSpawn: 1),
                        Phase("Hunger Phase", 5, 1200, "Frenzy", 0.25)
                        ),
                    new State("Frenzy",
                        new Taunt("Enough! I will drain you all!"),
                        new Spawn(N("Lil Feratu"), maxChildren: 2, initialSpawn: 1),
                        Phase("Frenzy Phase", 7, 900)
                        )
                    ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Dexterity", 0.5),
                    new ItemLoot("Holy Water", 0.5),
                    new ItemLoot("Manor Key", 0.01, 0, 0.03)
                    ), Elder("Lord Ruthven"))
            // the bats round Lord Ruthven while he is a swarm
            .Init(E("Vampire Bat"),
                new State(
                    new Orbit(1.6, 2.5, 12, N("Lord Ruthven"), speedVariance: 0.3, radiusVariance: 0.5),
                    new Shoot(1.5, count: 1, coolDown: Cd(500)),
                    new Decay(5000)
                    ))
            .Init(E("Hellhound"),
                new State(
                    new Follow(1.25, 8, 1, coolDown: Cd(275)),
                    new Shoot(10, count: 5, shootAngle: 7, coolDown: Cd(2000))
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new Threshold(0.5,
                    new ItemLoot("Timelock Orb", 0.01)
                    ), Elder("Hellhound"))
            .Init(E("Vampire Bat Swarmer"),
                new State(
                    new Follow(1.5, 8, 0),
                    // the bite: a shot that stays where the bat is
                    new Shoot(1.5, count: 1, coolDown: Cd(500))
                    ), Elder("Vampire Bat Swarmer"))
            // the swarmers Lord Ruthven sends out as a swarm of bats, gone after a while so they
            // don't pile up over a long fight
            .Init(E("Vampire Bat Swarmer 1"),
                new State(
                    new Follow(1.5, 12, 0),
                    new Shoot(1.5, count: 1, coolDown: Cd(500)),
                    new Decay(15000)
                    ))
            .Init(E("Lil Feratu"),
                new State(
                    new Follow(0.35, 8, 1),
                    new Shoot(10, count: 6, shootAngle: 2, coolDown: Cd(900))
                    ),
                new ItemLoot("Health Potion", 0.05),
                new Threshold(0.5,
                    new ItemLoot("Steel Helm", 0.01)
                    ), Elder("Lil Feratu"))
            .Init(E("Lesser Bald Vampire"),
                new State(
                    new Follow(0.35, 8, 1),
                    new Shoot(10, count: 5, shootAngle: 6, coolDown: Cd(1000))
                    ),
                new ItemLoot("Health Potion", 0.05),
                new Threshold(0.5,
                    new ItemLoot("Steel Helm", 0.01)
                    ), Elder("Lesser Bald Vampire"))
            .Init(E("Nosferatu"),
                new State(
                    new Prioritize(
                        new Follow(0.4, 9, 4),
                        new Wander(0.25)
                        ),
                    new Shoot(10, count: 5, shootAngle: 8, projectileIndex: 1, coolDown: Cd(1000)),
                    new Shoot(4, count: 4, shootAngle: 90, projectileIndex: 0, coolDown: Cd(1500))
                    ),
                new ItemLoot("Health Potion", 0.05),
                new Threshold(0.5,
                    new ItemLoot("Bone Dagger", 0.01),
                    new ItemLoot("Wand of Death", 0.05),
                    new ItemLoot("Golden Bow", 0.04),
                    new ItemLoot("Steel Helm", 0.05),
                    new ItemLoot("Ring of Paramount Defense", 0.03)
                    ), Elder("Nosferatu"))
            .Init(E("Armor Guard"),
                new State(
                    new Prioritize(
                        new StayCloseToSpawn(0.3, 4),
                        new Wander(0.2)
                        ),
                    new TossObject(N("RockBomb"), 7, coolDown: Cd(3000)),
                    new Shoot(10, count: 1, projectileIndex: 0, predictive: 0.7, coolDown: Cd(1000)),
                    new Shoot(10, count: 1, projectileIndex: 1, coolDown: Cd(750))
                    ),
                new ItemLoot("Magic Potion", 0.05),
                new Threshold(0.5,
                    new ItemLoot("Glass Sword", 0.01),
                    new ItemLoot("Staff of Destruction", 0.01),
                    new ItemLoot("Golden Shield", 0.01),
                    new ItemLoot("Ring of Paramount Speed", 0.01)
                    ), Elder("Armor Guard"))
            .Init(E("Coffin Creature"),
                new State(
                    // its Lil Feratus stay inside until a player comes near
                    new State("Shut",
                        new PlayerWithinTransition(7, "Open")
                        ),
                    new State("Open",
                        new Spawn(N("Lil Feratu"), initialSpawn: 1, maxChildren: 2, coolDown: Cd(2250)),
                        new Shoot(10, count: 1, projectileIndex: 0, coolDown: Cd(700))
                        )
                    ),
                new ItemLoot("Magic Potion", 0.05), Elder("Coffin Creature"))
            .Init(E("RockBomb"),
                new State(
                    new State("BOUTTOEXPLODE",
                        new TimedTransition(1111, "boom")
                        ),
                    new State("boom",
                        new Shoot(8.4, count: 8, projectileIndex: 0, fixedAngle: 0, coolDown: Cd(1000)),
                        new Suicide()
                        )
                    ), Elder("RockBomb"))
            // what Lord Ruthven leaves: his Coffin, which wakes his bats and his Nosferatu as it breaks
            .Init(E("Coffin"),
                new State(
                    new ScaleHP2(20),
                    // the regular dungeon leads to its Elder version half the time
                    new DropPortalOnDeath("Elder Manor Portal", elder ? 0 : 0.5, xAdjustment: 3),
                    new State("Settling",
                        new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                        new TimedTransition(3000, "Coffin1")
                        ),
                    new State("Coffin1",
                        new HpLessTransition(0.75, "Coffin2")
                        ),
                    new State("Coffin2",
                        new Spawn(N("Vampire Bat Swarmer"), initialSpawn: 1, maxChildren: 8),
                        new HpLessTransition(0.40, "Coffin3")
                        ),
                    new State("Coffin3",
                        new Spawn(N("Vampire Bat Swarmer"), initialSpawn: 1, maxChildren: 6),
                        new Spawn(N("Nosferatu"), initialSpawn: 1, maxChildren: 2)
                        )
                    ),
                new Threshold(0.01,
                    new ItemLoot("Potion of Attack", 1),
                    new ItemLoot("Potion of Dexterity", 0.5),
                    new ItemLoot("Holy Water", 0.5),
                    new ItemLoot("St. Abraham's Wand", 0.02),
                    new ItemLoot("Tome of Purification", 0.02),
                    new ItemLoot("Bone Dagger", 0.02),
                    new ItemLoot("Chasuble of Holy Light", 0.008),
                    new ItemLoot("Ring of Divine Faith", 0.008)
                    ),
                new Threshold(0.005,
                    LootTemplates.BasicDrop()
                    ),
                // the Elder Manor's chance at a Potion of Max, as the Elder Tomb's bosses
                ElderLoot(0.01,
                    new ItemLoot("Potion of Max Attack", 0.01)
                    ), Elder("Coffin"));
        }
    }
}
