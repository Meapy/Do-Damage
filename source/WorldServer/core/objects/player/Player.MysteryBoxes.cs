using Shared.resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WorldServer.core.worlds;

namespace WorldServer.core.objects
{
    partial class Player
    {
        // Effects the server doesn't carry out yet: an item with one of them is refused, not spent.
        private static readonly HashSet<ActivateEffects> UnhandledEffects = new HashSet<ActivateEffects>
        {
            ActivateEffects.UnlockPetSkin, ActivateEffects.CreatePet, ActivateEffects.TeleportToObject,
            ActivateEffects.MysteryPortal, ActivateEffects.KillRealmHeroes
        };

        // What the Mystery ST Crate (from an Enchanted Key) gives, by weight out of 1000: its description's list,
        // with the ST gear rare (1.5% a piece) and the shards, clovers and loot potions common.
        private static readonly (string Id, int Weight)[] MysteryStCrate =
        {
            // Daring Discoverer Samurai, Slurp Scion Wizard and Court Magician Sorcerer sets
            ("2SamuraiST0", 15), ("2SamuraiST1", 15), ("2SamuraiST2", 15), ("2SamuraiST3", 15),
            ("2WizardST0", 15), ("2WizardST1", 15), ("2WizardST2", 15), ("2WizardST3", 15),
            ("2SorcererST0", 15), ("2SorcererST1", 15), ("2SorcererST2", 15), ("2SorcererST3", 15),
            ("Char Slot Unlocker", 30), ("Vault Chest Unlocker", 30), ("Ambrosia", 20),
            ("Mystery ST Shard x5", 50), ("Mystery ST Shard x3", 80), ("Mystery ST Shard x2", 110), ("Mystery ST Shard x1", 140),
            ("Superburger", 60), ("Golden Lucky Clover", 40), ("Lucky Clover", 100),
            ("Loot Drop Potion", 80), ("Loot Tier Potion", 80)
        };

        private static readonly Regex StGearId = new Regex(@"ST\d$", RegexOptions.Compiled);

        // What the Mystery Keys open, at random, where they're used (as the dungeon's own key would:
        // OpenDungeonPortal): the dungeons whose portals the realms drop, split by the wiki's
        // difficulty (from the boss's health). Bronze the beginner and easy ones, Silver the medium
        // and hard ones, Gold the hardest, and now and then Oryx's Castle.
        private static readonly Dictionary<string, string[]> MysteryKeys = new Dictionary<string, string[]>
        {
            ["Mystery Key (Bronze)"] = new[]
            {
                "Pirate Cave Portal", "Forest Maze Portal", "Spider Den Portal", "Deadwater Docks", "Forbidden Jungle Portal",
                "Snake Pit Portal", "GC Gigantic Cave Portal", "Undead Lair Portal", "Glowing Portal", "Abyss of Demons Portal",
                "Ancient Ruins Portal", "Magic Woods Portal", "Candyland Portal", "Manor of the Immortals Portal"
            },
            ["Mystery Key (Silver)"] = new[]
            {
                "Toxic Sewers Portal", "Mad Lab Portal", "Beachzone Portal", "Cursed Library Portal", "Puppet Theatre Portal",
                "Woodland Labyrinth", "Davy Jones' Locker Portal", "Lair of Draconis Portal", "Treasure Cave Portal",
                "Haunted Cemetery Portal", "The Crawling Depths", "Ice Cave Portal", "Lair of Shaitan Portal", "Ice Tomb Portal",
                "Mountain Temple Portal"
            },
            ["Mystery Key (Gold)"] = new[]
            {
                "Ocean Trench Portal", "Parasite Chambers Portal", "Secluded Thicket Portal", "The Shatters",
                "Tomb of the Ancients Portal", "Lost Halls Portal", "Cnidarian Reef Portal"
            }
        };

        // the Gold key's chance of Oryx's Castle instead
        private const string MysteryKeyOryxKey = "Mystery Key (Gold)";
        private const string MysteryKeyOryxPortal = "Oryx's Castle Portal";
        private const double MysteryKeyOryxChance = 0.1;

        // What the Mystery Pet Stones give: a pet of these sizes at random (its generator, which
        // gives the pet when used)
        private static readonly Dictionary<string, string[]> MysteryPetSizes = new Dictionary<string, string[]>
        {
            ["Mystery Pet Stone (Bronze)"] = new[] { "Tiny", "Small" },
            ["Mystery Pet Stone (Silver)"] = new[] { "Medium" },
            ["Mystery Pet Stone (Gold)"] = new[] { "Large" }
        };

        // The reward pools of the mystery items that work, built once from the game data.
        private static Dictionary<string, (Item Item, int Weight)[]> _mysteryBoxes;

        private Dictionary<string, (Item Item, int Weight)[]> MysteryBoxes
        {
            get
            {
                if (_mysteryBoxes != null)
                    return _mysteryBoxes;

                var gameData = GameServer.Resources.GameData;
                var items = gameData.Items.Values;
                (Item, int)[] Even(IEnumerable<Item> pool) => pool.Select(i => (i, 1)).ToArray();

                var boxes = new Dictionary<string, (Item Item, int Weight)[]>
                {
                    ["Mystery ST Crate"] = MysteryStCrate
                        .Where(r => gameData.IdToObjectType.ContainsKey(r.Id))
                        .Select(r => (gameData.Items[gameData.IdToObjectType[r.Id]], r.Weight)).ToArray(),
                    // "a random ST item": any piece of an ST set
                    ["Mystery ST Chest"] = Even(items.Where(i => StGearId.IsMatch(i.ObjectId) && i.SlotType != 10)),
                    ["Mystery Dye (Clothing)"] = Even(items.Where(i => i.ObjectId.EndsWith(" Clothing Dye"))),
                    ["Mystery Dye (Accessory)"] = Even(items.Where(i => i.ObjectId.EndsWith(" Accessory Dye")))
                };
                foreach (var (stone, sizes) in MysteryPetSizes)
                    boxes[stone] = Even(items.Where(i => i.Consumable && i.ActivateEffects.Any(e =>
                        e.Effect == ActivateEffects.PermaPet && e.PetSize != null && sizes.Contains(e.PetSize))));
                foreach (var name in boxes.Where(b => b.Value.Length == 0).Select(b => b.Key).ToList())
                    boxes.Remove(name);
                return _mysteryBoxes = boxes;
            }
        }

        private bool CanCarryOut(Item item)
        {
            var gameData = GameServer.Resources.GameData;
            foreach (var eff in item.ActivateEffects)
            {
                if (UnhandledEffects.Contains(eff.Effect))
                    return false;
                if (eff.Effect == ActivateEffects.Exchange && (eff.Id == null || !gameData.IdToObjectType.ContainsKey(eff.Id)))
                    return false;
                if (eff.Effect == ActivateEffects.MysteryDyes && !MysteryBoxes.ContainsKey(item.ObjectId) && !MysteryKeys.ContainsKey(item.ObjectId))
                    return false;
            }
            return true;
        }

        // Gives an item: into the first free slot of the inventory, or to the gift chest when it's full.
        private void GiveItem(Item item)
        {
            var slot = Inventory.GetAvailableInventorySlot(item);
            if (slot != -1)
            {
                Inventory[slot] = item;
                SendInfo($"You received {item.DisplayName}!");
                return;
            }

            GameServer.Database.AddGift(Client.Account, item.ObjectType);
            SendInfo($"You received {item.DisplayName}! Your inventory is full, so it's in your Gift Chest.");
        }

        // Exchange: the item used turns into another (an Enchanted Key into a Mystery ST Crate)
        private void AEExchange(ActivateEffect eff)
        {
            var gameData = GameServer.Resources.GameData;
            GiveItem(gameData.Items[gameData.IdToObjectType[eff.Id]]);
        }

        // MysteryDyes: a mystery box gives one item from its pool; a Mystery Key opens a dungeon
        private void AEMysteryBox(Item box)
        {
            if (MysteryKeys.TryGetValue(box.ObjectId, out var portals))
            {
                var open = portals.Where(p => SealedDungeons.WhyPortal(p) == null).ToArray();
                var portal = box.ObjectId == MysteryKeyOryxKey && Random.Shared.NextDouble() < MysteryKeyOryxChance
                    ? MysteryKeyOryxPortal
                    : open[Random.Shared.Next(open.Length)];
                OpenDungeonPortal(portal);
                return;
            }
            var pool = MysteryBoxes[box.ObjectId];
            var roll = Random.Shared.Next(pool.Sum(r => r.Weight));
            foreach (var (item, weight) in pool)
            {
                if (roll < weight)
                {
                    GiveItem(item);
                    return;
                }
                roll -= weight;
            }
        }
    }
}
