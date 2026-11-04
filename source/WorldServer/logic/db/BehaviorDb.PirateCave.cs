#region

using Shared.resources;
using WorldServer.logic.loot;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

#endregion

namespace WorldServer.logic
{
    partial class BehaviorDb
    {
        private _ PirateCave = () => PirateCaveBehaviours(Behav(), false);
        private _ ElderPirateCave = () => PirateCaveBehaviours(Behav(), true);

        // the regular and the Elder version (BehaviorDb.Elder.cs)
        private static Ctor PirateCaveBehaviours(Ctor db, bool elder)
        {
            string N(string id) => ElderOr(id, elder);
            string E(string id) => ElderDefines(id, elder);
            int Cd(int ms) => ElderCooldown(ms, elder);
            MobDrops Elder(string id) => ElderDrops(id, elder);

            return db
            .Init(E("Dreadstump the Pirate King"),
                new State(
                    new DropPortalOnDeath("Realm Portal", probability: 1.0, timeout: null),
                    // the regular dungeon leads to its Elder version half the time
                    new DropPortalOnDeath("Elder Pirate Cave Portal", elder ? 0 : 0.5, xAdjustment: 3),
                    new State("Idle",
                        new PlayerWithinTransition(15, "swiggity")
                        ),
                    new State("swiggity",
                        new StayCloseToSpawn(1, 7),
                        new Wander(0.3),
                        new Shoot(radius: 8, count: 1, projectileIndex: 1, coolDown: Cd(2000), predictive: 0.9),
                        new Shoot(radius: 8, count: 1, projectileIndex: 0, coolDown: Cd(1000), predictive: 0.9),
                        new Taunt(0.3, 14000,
                            "Hah! I'll drink my rum out of your skull!",
                            "Eat cannonballs!",
                            "Arrrr..."
                            )
                        )
                    ),
                new Threshold(0.03,
                    new ItemLoot("Monkey Generator", elder ? 0.06 : 0.03),
                    new ItemLoot("Pirate Cave Key", 0.01)
                    ),
                new TierLoot(4, ItemType.Weapon, 0.01),
                new TierLoot(3, ItemType.Weapon, 0.05),
                new TierLoot(2, ItemType.Weapon, 0.15),
                new TierLoot(1, ItemType.Armor, 0.2),
                new TierLoot(2, ItemType.Armor, 0.1),
                new TierLoot(3, ItemType.Armor, 0.05),
                new TierLoot(4, ItemType.Armor, 0.01),
                new TierLoot(1, ItemType.Ring, 0.1),
                new ItemLoot("Pirate Rum", 0.01), Elder("Dreadstump the Pirate King"))
            .Init(E("Pirate Lieutenant"),
                new State(
                    new Prioritize(
                        new Protect(0.4, N("Dreadstump the Pirate King"), protectionRange: 6),
                        new Wander(0.5),
                        new Follow(1, 6, 1, -1, 0)
                        ),
                    new Shoot(radius: 7, projectileIndex: 0, predictive: 1, coolDown: Cd(1500))
                    ),
                new TierLoot(3, ItemType.Weapon, 0.05),
                new TierLoot(2, ItemType.Weapon, 0.15),
                new TierLoot(1, ItemType.Armor, 0.2),
                new TierLoot(1, ItemType.Ring, 0.1),
                new ItemLoot("Pirate Rum", 0.01), Elder("Pirate Lieutenant"))
            .Init(E("Pirate Captain"),
                new State(
                    new Prioritize(
                        new Protect(0.4, N("Dreadstump the Pirate King"), protectionRange: 6),
                        new Wander(0.5),
                        new Follow(1, 6, 1, -1, 0)
                        ),
                    new Shoot(radius: 7, projectileIndex: 0, predictive: 1, coolDown: Cd(1500))
                    ),
                new TierLoot(3, ItemType.Weapon, 0.05),
                new TierLoot(2, ItemType.Weapon, 0.15),
                new TierLoot(1, ItemType.Armor, 0.2),
                new TierLoot(1, ItemType.Ring, 0.1),
                new ItemLoot("Pirate Rum", 0.01) //swiggity
                , Elder("Pirate Captain"))
            .Init(E("Pirate Commander"),
                new State(
                    new Prioritize(
                        new Protect(0.4, N("Dreadstump the Pirate King"), protectionRange: 6),
                        new Wander(0.5)
                        ),
                    new Shoot(radius: 7, projectileIndex: 0, predictive: 1, coolDown: Cd(1500))
                    ),
                new TierLoot(3, ItemType.Weapon, 0.05),
                new TierLoot(2, ItemType.Weapon, 0.15),
                new TierLoot(1, ItemType.Armor, 0.2),
                new TierLoot(1, ItemType.Ring, 0.1),
                new ItemLoot("Pirate Rum", 0.01) //swooty
                , Elder("Pirate Commander"))
            .Init(E("Cave Pirate Brawler"),
                new State(
                    new State("that",
                        new Follow(1, 6, 1, -1, 0),
                        new Wander(0.3),
                        new Shoot(radius: 5, count: 1, projectileIndex: 0, coolDown: Cd(1000))
                        )
                    ),
                new ItemLoot("Magic Potion", 0.2), Elder("Cave Pirate Brawler"))
            .Init(E("Cave Pirate Sailor"),
                new State(
                    new State("booty",
                        new Wander(0.4),
                        new Follow(0.8, 6, 1, -1, 0),
                        new Shoot(radius: 5, count: 1, projectileIndex: 0, coolDown: Cd(1000))
                        )
                    ),
                new ItemLoot("Magic Potion", 0.2), Elder("Cave Pirate Sailor"))
            .Init(E("Cave Pirate Cabin Boy"),
                new State(
                    new Prioritize(
                        new Wander(0.5)
                        )
                    ),
                new TierLoot(1, ItemType.Weapon, 0.4), Elder("Cave Pirate Cabin Boy"))
            .Init(E("Cave Pirate Macaw"),
                new State(
                    new Prioritize(
                        new Wander(0.5)
                        )
                    ),
                new TierLoot(1, ItemType.Ability, 0.2), Elder("Cave Pirate Macaw"))
            .Init(E("Cave Pirate Moll"),
                new State(
                    new Prioritize(
                        new Wander(0.1)
                        )
                    ),
                new TierLoot(1, ItemType.Ability, 0.2), Elder("Cave Pirate Moll"))
            .Init(E("Cave Pirate Monkey"),
                new State(
                    new Prioritize(
                        new Wander(0.1)
                        )
                    ),
                new TierLoot(1, ItemType.Ability, 0.2), Elder("Cave Pirate Monkey"))
            .Init(E("Cave Pirate Parrot"),
                new State(
                    new Prioritize(
                        new Wander(0.1)
                        )
                    ),
                new TierLoot(1, ItemType.Ability, 0.2), Elder("Cave Pirate Parrot"))
            .Init(E("Cave Pirate Hunchback"),
                new State(
                    new Prioritize(
                        new Wander(0.5)
                        )
                    ),
                new TierLoot(1, ItemType.Ability, 0.2), Elder("Cave Pirate Hunchback"))
            .Init(E("Cave Pirate Veteran"),
                new State(
                    new State("woot",
                        new Follow(1, 6, 1, -1, 0),
                        new Wander(0.4),
                        new Shoot(radius: 5, count: 1, projectileIndex: 0, coolDown: Cd(1000))
                        )
                    ),
                new ItemLoot("Magic Potion", 0.2), Elder("Cave Pirate Veteran"))
            .Init(E("Pirate Admiral"),
                new State(
                    new Prioritize(
                        new Protect(0.4, N("Dreadstump the Pirate King"), protectionRange: 6),
                        new Wander(0.5),
                        new Follow(1, 6, 1, -1, 0)
                        ),
                    new Shoot(radius: 7, projectileIndex: 0, predictive: 1, coolDown: Cd(1500))
                    ),
                new TierLoot(3, ItemType.Weapon, 0.05),
                new TierLoot(2, ItemType.Weapon, 0.15),
                new TierLoot(1, ItemType.Armor, 0.2),
                new TierLoot(2, ItemType.Armor, 0.1),
                new TierLoot(1, ItemType.Ring, 0.1),
                new ItemLoot("Pirate Rum", 0.01) //coming for that booty
                , Elder("Pirate Admiral"));
        }
    }
}
