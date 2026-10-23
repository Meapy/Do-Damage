using System;
using System.Collections.Generic;
using Shared.resources;
using BL = WorldServer.logic.loot;

// Loot added to monsters that run a Doomed Realms behaviour but whose Doomed Realms loot is missing or
// thin: the classic realm heroes and their minions, which upstream betterSkillys never gave a
// behaviour (tools/port_doomed.py gives them the Doomed Realms one). DoomedSetup.Drops adds these to
// the generated drops; written by hand, not generated.
//
// Heroes drop like the Actual Lich and Actual Ent Ancient (levels 9 and 11, tier 6 and 7 gear),
// stepping up with the hero's level; minions drop a low-tier item now and then and a potion, as the
// betterSkillys realm minions do.
namespace WorldServer.logic.doomed
{
    internal static class DoomedExtraLoot
    {
        public static readonly Dictionary<string, Func<BL.MobDrops[]>> Extra = new Dictionary<string, Func<BL.MobDrops[]>>
        {
            // heroes (Phoenix Lord and its egg drop nothing: the Phoenix Reborn they turn into does)
            ["Deathmage"] = () => Hero(5, 2, 2, StatPotion("Potion of Wisdom", 0.02)),
            ["Oasis Giant"] = () => Hero(6, 2, 2, StatPotion("Potion of Vitality", 0.03)),
            ["Actual Ghost King"] = () => Hero(6, 3, 3, StatPotion("Potion of Wisdom", 0.04)),
            ["Phoenix Reborn"] = () => Hero(7, 3, 3, StatPotion("Potion of Dexterity", 0.05)),
            ["Red Demon"] = () => Hero(7, 3, 3, StatPotion("Potion of Attack", 0.05), pots: false), // already drops potions
            ["Jackal Priest"] = () => new BL.MobDrops[]
            {
                new BL.Threshold(.01, new BL.TierLoot(4, ItemType.Weapon, 0.06), new BL.TierLoot(4, ItemType.Armor, 0.06), new BL.TierLoot(1, ItemType.Ring, 0.05)),
                new BL.ItemLoot("Health Potion", 0.3)
            },

            // minions, with the tier of the hero they serve
            ["Skeleton"] = () => Minion(2), ["Skeleton Swordsman"] = () => Minion(2), ["Skeleton Veteran"] = () => Minion(3),
            ["Skeleton Mage"] = () => Minion(3), ["Skeleton King"] = () => Minion(3),
            ["Mummy"] = () => Minion(3), ["Mummy King"] = () => Minion(3), ["Mummy Pharaoh"] = () => Minion(4),
            ["Mask Warrior"] = () => Minion(3), ["Mask Hunter"] = () => Minion(3), ["Mask Shaman"] = () => Minion(3),
            ["Totem Spirit"] = () => Minion(3),
            ["Oasis Soldier"] = () => Minion(4), ["Oasis Creature"] = () => Minion(4), ["Oasis Monster"] = () => Minion(4),
            ["Oasis Ruler"] = () => Minion(5),
            ["Birdman"] = () => Minion(5), ["Birdman Chief"] = () => Minion(6),
            ["Small Ghost"] = () => Minion(4), ["Medium Ghost"] = () => Minion(5), ["Large Ghost"] = () => Minion(6, 0.06),
            ["Ent Sapling"] = () => Minion(4),
            ["Vampire Bat"] = () => Pots(), ["Vampire Bat Swarmer 1"] = () => Pots(),

            // untiered rings given a source (SETUP-LOCAL.md, "Untiered rings"), as rare as each boss's own
            ["Christmas Oryx"] = () => new BL.MobDrops[]
            {
                new BL.ItemLoot("Oryxmas Ornament: Energized", 0.01, threshold: .01),
                new BL.ItemLoot("Oryxmas Ornament: Exposed", 0.01, threshold: .01),
                new BL.ItemLoot("Oryxmas Ornament: Weak", 0.01, threshold: .01)
            },
            ["Elder Thessal the Mermaid Goddess"] = () => new BL.MobDrops[] { new BL.ItemLoot("Coral Medallion", 0.01, threshold: .01) },
            ["Elder Limon the Sprite God"] = () => new BL.MobDrops[] { new BL.ItemLoot("Necklace of Light", 0.005, threshold: .01) },
            ["Elder Archdemon Malphas"] = () => new BL.MobDrops[]
            {
                new BL.ItemLoot("Ring of the Blood Harvest", 0.01, threshold: .01),
                new BL.ItemLoot("Skull of Endless Torment", 0.01, threshold: .01)
            },
        };

        private static BL.MobDrops StatPotion(string id, double probability) => new BL.ItemLoot(id, probability, threshold: .01);

        private static BL.MobDrops[] Hero(int tier, int ringTier, int abilityTier, BL.MobDrops statPotion, bool pots = true) => new BL.MobDrops[]
        {
            new BL.Threshold(.01,
                new BL.TierLoot(tier, ItemType.Weapon, 0.20),
                new BL.TierLoot(tier + 1, ItemType.Weapon, 0.10),
                new BL.TierLoot(tier, ItemType.Armor, 0.20),
                new BL.TierLoot(tier + 1, ItemType.Armor, 0.10),
                new BL.TierLoot(ringTier, ItemType.Ring, 0.15),
                new BL.TierLoot(abilityTier, ItemType.Ability, 0.10)),
            statPotion,
            new BL.ItemLoot("Health Potion", pots ? 0.5 : 0),
            new BL.ItemLoot("Magic Potion", pots ? 0.5 : 0)
        };

        private static BL.MobDrops[] Minion(int tier, double chance = 0.03) => new BL.MobDrops[]
        {
            new BL.TierLoot(tier, ItemType.Weapon, chance),
            new BL.TierLoot(tier, ItemType.Armor, chance),
            new BL.ItemLoot("Health Potion", 0.05),
            new BL.ItemLoot("Magic Potion", 0.05)
        };

        private static BL.MobDrops[] Pots() => new BL.MobDrops[] { new BL.ItemLoot("Health Potion", 0.05), new BL.ItemLoot("Magic Potion", 0.05) };
    }
}
