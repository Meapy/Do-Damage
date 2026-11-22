using System;
using System.Linq;
using Shared.resources;

namespace Shared.pets
{
    // What feeding an item to your pet at the Pen Keeper is worth (PetLevels: the food a level
    // takes), 0 for what a pet won't eat. The Rust client shows the same numbers
    // (client-rs/app/src/petlevels.rs): change both together.
    // - the treats: a Superburger 1,500, an Ambrosia 5,000, each Shard of the Mad God 200;
    // - another pet's stone, by its size: Tiny 150, Small 400, Medium 1,200, Large 3,000, Oryx 8,000;
    // - gear: tiered 20 + 15 a tier (abilities and rings, whose tiers stop at 7, 20 + 30 a tier);
    //   untiered (UTs and such) by how rare its bag is (Loots.LOOT_BAG_WEIGHTS' order): brown 150,
    //   pink 200, purple 300, 400, cyan 600, blue 800, 1,000, 1,200, 1,500, white 2,000, and
    //   Ascended gear 3,000;
    // - potions: a stat potion 60, a Greater one 150, Life and Mana 200.
    public static class PetFood
    {
        public const string Burger = "Superburger";
        public const string Ambrosia = "Ambrosia";
        public const string ShardPrefix = "Shard of the Mad God x";
        public const int ShardXp = 200;
        private static readonly int[] StoneXp = { 150, 400, 1200, 3000, 8000 };
        // the weapons' and armours' slot types, whose tiers go to 14 and on; the rest of the gear
        // (abilities, rings, the newer classes' pieces) has tiers that stop at 7
        private static readonly int[] LongTiers = { 1, 2, 3, 6, 7, 8, 14, 17, 24 };
        // untiered gear's food by its bag (BagType 0-9; the bags' rank is Loots.LOOT_BAG_WEIGHTS)
        private static readonly int[] UntieredXp = { 150, 200, 300, 400, 600, 800, 2000, 1000, 1200, 1500 };
        public const int AscendedXp = 3000;

        /// <summary>One of `item` fed to a pet (a stack counts each), 0 if pets don't eat it.</summary>
        public static int Value(Item item)
        {
            if (item == null)
                return 0;
            var id = item.ObjectId ?? "";
            if (id == Burger)
                return 1500;
            if (id == Ambrosia)
                return 5000;
            // "Shard of the Mad God x5" is five shards
            if (id.StartsWith(ShardPrefix))
                return ShardXp * (item.Quantity > 0 ? item.Quantity : Math.Max(1, ParseCount(id.Substring(ShardPrefix.Length))));

            var pet = item.ActivateEffects?.FirstOrDefault(e => e.Effect == ActivateEffects.PermaPet);
            if (pet != null)
            {
                var size = PetLevels.SizeIndex(pet.PetSize);
                return size < 0 ? 0 : StoneXp[size];
            }

            if (id.StartsWith("Greater Potion of "))
                return 150;
            if (id == "Potion of Life" || id == "Potion of Mana")
                return 200;
            if (id.StartsWith("Potion of ") && item.Potion)
                return 60;

            // gear: anything worn (not the consumables' and keys' slot 10, not skins or dyes)
            if (item.SlotType <= 0 || item.SlotType == 10 || item.Consumable)
                return 0;
            if (item.Tier >= 0)
                return Array.IndexOf(LongTiers, item.SlotType) >= 0 ? 20 + 15 * item.Tier : 20 + 30 * item.Tier;
            if (item.Ascended)
                return AscendedXp;
            return UntieredXp[Math.Clamp(item.BagType, 0, UntieredXp.Length - 1)];
        }

        /// <summary>The food in an inventory slot: its item's, times its stack.</summary>
        public static int Value(Item item, int stack) => Value(item) * Math.Max(1, stack);

        private static int ParseCount(string s) => int.TryParse(s.Trim(), out var n) ? n : 1;
    }
}
