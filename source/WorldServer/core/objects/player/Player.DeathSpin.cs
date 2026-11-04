using Shared.database.character;
using Shared.resources;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // The death spin: a character that dies with enough base fame spins a loot reel. Its base fame
    // sets the tier, the tier the odds of each rarity, and the prize goes to the account's Gift
    // Chest, with some of the items it died with (more the more base fame). The result is kept
    // with the death (DbDeath), where the account server's fame summary hands it to the client,
    // which plays the reel on the death screen.
    partial class Player
    {
        public const int DeathSpinMinFame = 20;

        // (name, base fame from, chances of Common, Uncommon, Rare, Epic, Legendary, Divine out of
        // 1000). Divine (white bags, the very best gear, Lil' Oryxes, Potion of Maxy) is rare at
        // every tier: 0.2% at Bronze up to 4% at Mythic.
        public static readonly (string Name, int Fame, int[] Odds)[] DeathSpinTiers =
        {
            ("Bronze", DeathSpinMinFame, new[] { 550, 330, 98, 20, 0, 2 }),
            ("Silver", 150, new[] { 300, 400, 226, 60, 10, 4 }),
            ("Gold", 500, new[] { 100, 320, 395, 150, 27, 8 }),
            ("Platinum", 1500, new[] { 0, 150, 445, 330, 60, 15 }),
            ("Diamond", 4000, new[] { 0, 0, 345, 500, 130, 25 }),
            ("Mythic", 10000, new[] { 0, 0, 145, 550, 265, 40 }),
        };

        public static readonly string[] DeathSpinRarities = { "Common", "Uncommon", "Rare", "Epic", "Legendary", "Divine" };

        private static readonly string[] Stats8 = { "Attack", "Defense", "Speed", "Vitality", "Wisdom", "Dexterity", "Life", "Mana" };

        private static string[] Ids(string format) => Stats8.Select(s => string.Format(format, s)).ToArray();

        private static readonly string[] SmallPets =
        {
            "Black Cat Generator", "Grey Cat Generator", "Orange Cat Generator", "Tan Cat Generator", "Yellow Cat Generator",
            "White Cat Generator", "Brown Pup Generator", "Grey Pup Generator", "Golden Pup Generator", "Blue Snail Generator",
            "Purple Snail Generator", "Grey Wolf Generator", "Black Wolf Generator", "Brown Fox Generator", "Grey Fox Generator",
            "Green Frog Generator", "Purple Frog Generator", "Lion Generator", "White Lion Generator", "Penguin Generator",
            "Green Penguin Generator", "Purple Penguin Generator", "Grass Penguin Generator", "Sheepdog Generator",
            "Golden Sheepdog Generator", "Panda Generator", "Bluebird Generator", "Chick Generator", "Pig Generator",
            "Sea Slurp Generator", "Squirrel Generator", "Bat Generator", "Lil' Ghost Generator", "Leprechaun Generator",
            "Toucan Generator", "Skunk Generator", "Mallard Generator", "Duck Generator", "Eagle Generator",
            "Turtle Generator", "Jelly Generator", "Bee Generator", "Raven Generator", "Elf Generator", "Crab Generator",
            "Hamster Generator", "Dino Generator", "Elephant Generator", "Demon Frog Generator", "Gargoyle Generator",
            "Gummy Bear Generator", "Dragonfly Generator", "Lil' Cyclops Generator", "Snowy Owl Generator", "Turkey Generator",
            "Baby Dragon Generator", "Karate Penguin Red Generator", "Karate Penguin Blue Generator",
            "Karate Penguin Yellow Generator", "Karate Penguin Purple Generator", "Robobuddy Generator",
            "Sea Horse Generator", "Pentaract Eye Generator", "Bunny Generator", "Kirby Generator", "Monkey Generator"
        };

        private static readonly string[] MediumPets =
        {
            "Knight Generator", "Kythal the Necromancer Generator", "Kythal the Skull Generator", "Shroom Generator",
            "Zenco Generator", "Cerberus Skull Generator", "Sea Mare Generator", "Pyramid Artifact Generator",
            "Beholder Generator", "Angel Generator", "Ninja Generator", "Reaper Generator", "Djinn Generator",
            "Companion Knight Generator", "Paladin Generator", "Priest Generator", "Gyarados Generator",
            "Tornado Generator", "Shark Generator", "Pumpkman Generator", "Phoenix Generator", "Red Demon Generator",
            "Drake Generator", "Zombie Penguin Generator", "Pumpkin Head Generator", "Skeletal Guardian Generator"
        };

        // the boss pets, weakest to strongest (by damage a second)
        private static readonly string[] LargePets =
        {
            "Great Coil Snake Generator", "Doomed Nature Generator", "Nut Generator", "Nightmare Generator",
            "Stheno Generator", "Scootaloo Generator", "Limon Generator", "Lil' Thessal Generator"
        };

        private static readonly string[] BigBossPets =
        {
            "Malphas Generator", "Geb Generator", "Bes Generator", "Septavius Generator", "Skull Shrine Generator",
            "Horrific Creation Generator"
        };

        private static readonly string[] MythicPets = { "Sphinx Generator", "Cube Generator", "Cookie Generator" };

        // Each rarity's prizes: groups of items, a group picked by weight, then an item in it evenly.
        // Ids are names; int[] any weapon or armour of those tiers; a Func any gear it picks.
        private static readonly (int Weight, object Pool)[][] DeathSpinPools =
        {
            // Common
            new (int, object)[]
            {
                (45, Ids("Potion of {0}")),
                (20, new[] { "Lucky Clover", "Loot Drop Potion", "Loot Tier Potion" }),
                (15, SmallPets),
                (20, new[] { 10, 11 }),
            },
            // Uncommon
            new (int, object)[]
            {
                (30, Ids("Greater Potion of {0}")),
                (10, new[] { "Potion of Oryx" }),
                (15, new[] { "Superburger", "Golden Lucky Clover", "Mystery ST Shard x3" }),
                (25, MediumPets),
                (20, new[] { 12 }),
            },
            // Rare
            new (int, object)[]
            {
                (40, Ids("Potion of Max {0}")),
                (20, new[] { 13 }),
                (20, LargePets),
                (20, new[] { "Mystery ST Chest", "Ambrosia" }),
            },
            // Epic
            new (int, object)[]
            {
                (35, Ids("Potion of Max {0}")),
                (25, new[] { 14 }),
                (25, BigBossPets),
                (15, new[] { "Char Slot Unlocker", "Vault Chest Unlocker" }),
            },
            // Legendary
            new (int, object)[]
            {
                (35, Ids("Infernal Potion of {0}")),
                (30, MythicPets),
                (15, new[] { "Potion of Maxy" }),
                (20, new[] { "Char Slot Unlocker", "Vault Chest Unlocker" }),
            },
            // Divine
            new (int, object)[]
            {
                (45, (Func<Item, bool>)(i => i.BagType == 6)),
                (20, (Func<Item, bool>)(i => i.Tier >= 15)),
                (15, new[] { "Lil' Wine Cellar Oryx Generator", "Lil' Celestial Oryx Generator", "Lil' Infernal Oryx Generator",
                    "Lil' Ascended Oryx Generator", "Lil' Oryx Generator" }),
                (20, new[] { "Potion of Maxy" }),
            },
        };

        // the pools as items, built once from the game data (names it doesn't know are left out)
        private static (int Weight, Item[] Items)[][] _deathSpinItems;

        private (int Weight, Item[] Items)[][] DeathSpinItems
        {
            get
            {
                if (_deathSpinItems != null)
                    return _deathSpinItems;
                var gameData = GameServer.Resources.GameData;
                Item[] Resolve(object pool)
                {
                    bool Gear(Item i) => i.Class == "Equipment" && i.SlotType != 0 && i.SlotType != 10
                        && !i.Untradeable && !i.ObjectId.StartsWith("Enthroned");
                    if (pool is int[] tiers)
                        return gameData.Items.Values.Where(i => Gear(i) && tiers.Contains(i.Tier)).OrderBy(i => i.ObjectType).ToArray();
                    if (pool is Func<Item, bool> pick)
                        return gameData.Items.Values.Where(i => Gear(i) && pick(i)).OrderBy(i => i.ObjectType).ToArray();
                    var items = new List<Item>();
                    foreach (var id in (string[])pool)
                    {
                        if (gameData.IdToObjectType.TryGetValue(id, out var type) && gameData.Items.TryGetValue(type, out var item))
                            items.Add(item);
                        else
                            StaticLogger.Instance.Warn($"Death spin: no item {id}");
                    }
                    return items.ToArray();
                }
                return _deathSpinItems = DeathSpinPools
                    .Select(groups => groups.Select(g => (g.Weight, Resolve(g.Pool))).Where(g => g.Item2.Length > 0).ToArray())
                    .ToArray();
            }
        }

        public static int DeathSpinTier(int baseFame)
        {
            var tier = -1;
            for (var i = 0; i < DeathSpinTiers.Length; i++)
                if (baseFame >= DeathSpinTiers[i].Fame)
                    tier = i;
            return tier;
        }

        private static int Weighted(Random rand, IReadOnlyList<int> weights)
        {
            var roll = rand.Next(weights.Sum());
            for (var i = 0; i < weights.Count; i++)
            {
                if (roll < weights[i])
                    return i;
                roll -= weights[i];
            }
            return weights.Count - 1;
        }

        private Item DeathSpinPick(Random rand, int rarity)
        {
            // a rarity with nothing in it falls to the one below
            while (rarity > 0 && DeathSpinItems[rarity].Length == 0)
                rarity--;
            var groups = DeathSpinItems[rarity];
            if (groups.Length == 0)
                return null;
            var items = groups[Weighted(rand, groups.Select(g => g.Weight).ToArray())].Items;
            return items[rand.Next(items.Length)];
        }

        // the reel the client scrolls past: it stops at DeathSpinStop, the prize (marked with a *)
        public const int DeathSpinReelLength = 48;
        public const int DeathSpinStop = 42;

        // Items it gets back, a ladder of chances: the first item at 25%, each further one at half
        // the chance of the last. Each step of base fame doubles every chance (capped at sure):
        // step 1 is 50% of one, step 2 one sure and 50% of a second, and so on.
        public static readonly (int Fame, int Step)[] DeathKeepTiers =
        {
            (0, 0), (400, 1), (800, 2), (1600, 3), (3000, 4), (6000, 5), (12000, 6), (20000, 8)
        };

        public static int DeathKeepStep(int baseFame) => DeathKeepTiers.Last(t => baseFame >= t.Fame).Step;

        // Picks the items it died with that come back: random slots of its equipment, inventory
        // and backpack.
        private List<Item> RollDeathKeep(Random rand, int baseFame)
        {
            var carried = new List<Item>();
            for (var i = 0; i < Inventory.Length; i++)
                if (Inventory[i] != null)
                    carried.Add(Inventory[i]);
            var step = DeathKeepStep(baseFame);
            var count = Math.Max(0, step - 1);
            if (rand.Next(step == 0 ? 4 : 2) == 0)
            {
                count++;
                while (rand.Next(2) == 0)
                    count++;
            }
            count = Math.Min(count, carried.Count);
            return carried.OrderBy(_ => rand.Next()).Take(count).ToList();
        }

        // The spin: (tier, rarity, prize, reel), or null when its base fame is too low.
        private (int Tier, int Rarity, Item Prize, string Reel)? RollDeathSpin(Random rand, int baseFame)
        {
            var tier = DeathSpinTier(baseFame);
            if (tier < 0)
                return null;

            var odds = DeathSpinTiers[tier].Odds;
            var rarity = Weighted(rand, odds);
            var prize = DeathSpinPick(rand, rarity);
            if (prize == null)
                return null;
            while (rarity > 0 && DeathSpinItems[rarity].Length == 0)
                rarity--;

            // the reel: the tier's odds, with the better rarities shown a little more often to tease
            var tease = odds.Select((w, i) => w == 0 ? 0 : w + i * 15).ToArray();
            var reel = new List<string>();
            for (var i = 0; i < DeathSpinReelLength; i++)
            {
                if (i == DeathSpinStop)
                {
                    reel.Add($"{prize.ObjectType}:{rarity}*");
                    continue;
                }
                var r = Weighted(rand, tease);
                var item = DeathSpinPick(rand, r) ?? prize;
                reel.Add($"{item.ObjectType}:{(item == prize ? rarity : r)}");
            }
            return (tier, rarity, prize, string.Join(",", reel));
        }

        // A death's rewards: the spin's prize and the items it gets back go to the Gift Chest, and
        // what they were into the death record.
        private Action<DbDeath> RollDeathRewards()
        {
            var baseFame = Client.Character.Fame;
            var rand = Random.Shared;
            var kept = RollDeathKeep(rand, baseFame);
            var spin = RollDeathSpin(rand, baseFame);

            var gifts = kept.Select(i => i.ObjectType).ToList();
            if (spin != null)
                gifts.Add(spin.Value.Prize.ObjectType);
            if (gifts.Count == 0)
                return null;
            try
            {
                GameServer.Database.AddGifts(Client.Account, gifts);
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Error($"Death rewards: {Name}'s weren't given: {e}");
                return null;
            }

            if (kept.Count > 0)
                StaticLogger.Instance.Info($"Death keep: {Name} ({baseFame} base fame) got back {string.Join(", ", kept.Select(i => i.ObjectId))}");
            if (spin is { } won)
            {
                StaticLogger.Instance.Info($"Death spin: {Name} ({baseFame} base fame, {DeathSpinTiers[won.Tier].Name}) won {DeathSpinRarities[won.Rarity]} {won.Prize.ObjectId}");
                if (won.Rarity >= 4)
                {
                    var text = $"{Name}'s death spin landed on a {DeathSpinRarities[won.Rarity]} prize: {won.Prize.DisplayName}!";
                    foreach (var world in GameServer.WorldManager.GetWorlds())
                        world.ForeachPlayer(p => p.DeathNotif(text));
                }
            }

            var keptText = string.Join(",", kept.Select(i => (int)i.ObjectType));
            return death =>
            {
                death.Kept = keptText;
                if (spin is not { } won)
                    return;
                death.SpinTier = won.Tier;
                death.SpinRarity = won.Rarity;
                death.SpinPrize = won.Prize.ObjectType;
                death.SpinReel = won.Reel;
            };
        }
    }
}
