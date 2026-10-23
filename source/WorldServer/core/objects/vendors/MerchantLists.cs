using Shared.resources;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core;
using WorldServer.core.worlds;

namespace WorldServer.core.objects.vendors
{
    public interface ISellableItem
    {
        int Count { get; }
        ushort ItemId { get; }
        int Price { get; }
    }

    internal static class MerchantLists
    {

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // The nexus bazaar's shops (tools/build_nexus_bazaar.py puts their stalls on the map). Prices
        // are in fame and match the Doomed Realms shop (/shop) where both sell an item.

        private static readonly List<ISellableItem> StatPotions = new List<ISellableItem>
        {
            new ShopItem("Potion of Attack", 150),
            new ShopItem("Potion of Defense", 150),
            new ShopItem("Potion of Speed", 150),
            new ShopItem("Potion of Dexterity", 150),
            new ShopItem("Potion of Vitality", 150),
            new ShopItem("Potion of Wisdom", 150),
            new ShopItem("Potion of Life", 300),
            new ShopItem("Potion of Mana", 300)
        };

        private static readonly List<ISellableItem> Consumables = new List<ISellableItem>
        {
            new ShopItem("Health Potion", 5),
            new ShopItem("Magic Potion", 5),
            new ShopItem("Greater Health Potion", 20),
            new ShopItem("Greater Magic Potion", 20),
            new ShopItem("Tincture of Courage", 25),
            new ShopItem("Tincture of Dexterity", 25),
            new ShopItem("Tincture of Defense", 25),
            new ShopItem("Tincture of Life", 25),
            new ShopItem("Tincture of Mana", 25),
            new ShopItem("Effusion of Life", 40),
            new ShopItem("Effusion of Mana", 40),
            new ShopItem("Effusion of Dexterity", 40),
            new ShopItem("Effusion of Defense", 40),
            new ShopItem("White Drake Egg", 500),
            new ShopItem("Blue Drake Egg", 400),
            new ShopItem("Orange Drake Egg", 400),
            new ShopItem("Green Drake Egg", 400),
            new ShopItem("Yellow Drake Egg", 400)
        };

        private static readonly List<ISellableItem> Specials = new List<ISellableItem>
        {
            new ShopItem("XP Booster", 100),
            new ShopItem("XP Booster 1 hr", 60),
            new ShopItem("Loot Drop Potion", 150),
            new ShopItem("Backpack", 500),
            new ShopItem("Vault Chest Unlocker", 250),
            new ShopItem("Char Slot Unlocker", 1000),
            new ShopItem("Mystery Dye (Clothing)", 15),
            new ShopItem("Mystery Dye (Accessory)", 10)
        };

        // one regular key per dungeon that has a world; guild, test and reusable keys are left out
        private static readonly (string Key, int Price)[] DungeonKeyPrices =
        {
            ("Pirate Cave Key", 10), ("Spider Den Key", 10), ("Totem Key", 10), ("Forest Maze Key", 15),
            ("Snake Pit Key", 20), ("Sprite World Key", 20), ("Abyss of Demons Key", 20), ("Manor Key", 20),
            ("Candy Key", 30), ("Magic Woods Key", 30), ("Treasure Map", 30), ("Toxic Sewers Key", 40),
            ("Cemetery Key", 40), ("Ocean Trench Key", 50), ("Beachzone Key", 75), ("Cursed Library Key", 75),
            ("Theatre Key", 75), ("Woodland Labyrinth Key", 75), ("Deadwater Docks Key", 75), ("The Hive Key", 75),
            ("Bella's Key", 75), ("Undead Lair Key", 100), ("Lab Key", 100), ("Davy's Key", 100),
            ("Reef Key", 100), ("Ice Cave Key", 100), ("The Crawling Depths Key", 100), ("Secluded Thicket Key", 100),
            ("Fungal Cavern Key", 100), ("Parasite Chambers Key", 100), ("Mountain Temple Key", 100), ("Ancient Ruins Key", 100),
            ("Wine Cellar Incantation", 125), ("Cult Key", 150), ("Ice Tomb Key", 150), ("Shaitan's Key", 300),
            ("Puppet Master's Encore Key", 300), ("Oryx's Castle Key", 300), ("Shatters Key", 400), ("Oryx Chamber Key", 400),
            ("Battle Nexus Key", 400), ("Tomb of the Ancients Key", 500), ("Lost Halls Key", 500),
            ("Oryx Sanctuary Key", 300),  // the Oryx chain's incantation for the Sanctuary
            ("Oryx Chicken Chamber Key", 400)
        };

        private static readonly (string Key, int Price)[] DoomedKeyPrices =
        {
            ("Doom Crystal Key", 30), ("Zombies Key", 50), ("Birthday Key", 50), ("Island Key", 75),
            ("Christmas Key", 100), ("Hash Cellar Incantation", 500), ("Doomed Cellar Incantation", 1000),
            ("Elder Pirate Cave Key", 50), ("Elder Spider Den Key", 70), ("Elder Snake Pit Key", 100), ("Elder Sprite World Key", 100),
            ("Elder Manor Key", 100), ("Elder Beach Key", 150), ("Elder Jungle Key", 150), ("Elder Undead Lair Key", 200),
            ("Elder Davy Key", 200), ("Elder Ocean Trench Key", 200), ("Elder Abyss Key", 300), ("Elder Draconis Key", 400),
            ("Elder Tomb Key", 500)
        };

        private static readonly List<ISellableItem> DungeonKeys = DungeonKeyPrices.Select(k => (ISellableItem)new ShopItem(k.Key, (ushort)k.Price)).ToList();
        private static readonly List<ISellableItem> DoomedKeys = DoomedKeyPrices.Where(k => !SealedDungeons.IsSealedItem(k.Key)).Select(k => (ISellableItem)new ShopItem(k.Key, (ushort)k.Price)).ToList();

        public static void Initialize(GameServer gameServer)
        {
            InitDyes(gameServer);

            foreach (var shop in Shops)
            {
                var shopItems = shop.Value.Item1;

                if (shopItems == null)
                    continue;

                // an item that isn't in the game data is dropped, so no merchant offers it
                foreach (var shopItem in shopItems.OfType<ShopItem>().ToList())
                    if (!gameServer.Resources.GameData.IdToObjectType.TryGetValue(shopItem.Name, out ushort id))
                    {
                        Log.Warn("Item name: {0}, not found.", shopItem.Name);
                        shopItems.Remove(shopItem);
                    }
                    else
                        shopItem.SetItem(id);
            }
        }

        public static readonly Dictionary<TileRegion, Tuple<List<ISellableItem>, CurrencyType, int>> Shops = new Dictionary<TileRegion, Tuple<List<ISellableItem>, CurrencyType, int>>()
        {
            { TileRegion.Store_1, new Tuple<List<ISellableItem>, CurrencyType, int>(StatPotions, CurrencyType.Fame, 0) },
            { TileRegion.Store_2, new Tuple<List<ISellableItem>, CurrencyType, int>(Consumables, CurrencyType.Fame, 0) },
            { TileRegion.Store_3, new Tuple<List<ISellableItem>, CurrencyType, int>(Specials, CurrencyType.Fame, 0) },
            // Store_4 sold the Enchanted Keys, which also gave a Mystery ST Crate: set (ST) gear has no
            // source for now (SETUP-LOCAL.md, "Untiered rings")
            { TileRegion.Store_11, new Tuple<List<ISellableItem>, CurrencyType, int>(DungeonKeys, CurrencyType.Fame, 0) },
            { TileRegion.Store_12, new Tuple<List<ISellableItem>, CurrencyType, int>(DoomedKeys, CurrencyType.Fame, 0) }
        };

        private static void InitDyes(GameServer gameServer)
        {
            var d1 = new List<ISellableItem>();
            var d2 = new List<ISellableItem>();
            var c1 = new List<ISellableItem>();
            var c2 = new List<ISellableItem>();
            var petGenerators = new List<ISellableItem>();
            var permaPetGenerators = new List<ISellableItem>();

            foreach (var i in gameServer.Resources.GameData.Items.Values)
            {
                if (i.InvUse && i.ObjectId.Contains("Generator"))
                {
                    permaPetGenerators.Add(new ShopItem(i.ObjectId, 1000));
                    petGenerators.Add(new ShopItem(i.ObjectId, 1250));
                    continue;
                }

                if (!i.Class.Equals("Dye"))
                    continue;

                if (i.Texture1 != 0)
                {
                    if (i.ObjectId.Contains("Cloth") && i.ObjectId.Contains("Large"))
                        d1.Add(new ShopItem(i.ObjectId, 25));

                    if (i.ObjectId.Contains("Dye") && i.ObjectId.Contains("Clothing"))
                        c1.Add(new ShopItem(i.ObjectId, 25));

                    continue;
                }

                if (i.Texture2 != 0)
                {
                    if (i.ObjectId.Contains("Cloth") && i.ObjectId.Contains("Small"))
                        d2.Add(new ShopItem(i.ObjectId, 10));
                    if (i.ObjectId.Contains("Dye") && i.ObjectId.Contains("Accessory"))
                        c2.Add(new ShopItem(i.ObjectId, 10));

                    continue;
                }
            }

            Shops[TileRegion.Store_5] = new Tuple<List<ISellableItem>, CurrencyType, int>(petGenerators, CurrencyType.Fame, 0);
            Shops[TileRegion.Store_10] = new Tuple<List<ISellableItem>, CurrencyType, int>(permaPetGenerators, CurrencyType.Gold, 0);

            Shops[TileRegion.Store_6] = new Tuple<List<ISellableItem>, CurrencyType, int>(d1, CurrencyType.Fame, 0);
            Shops[TileRegion.Store_7] = new Tuple<List<ISellableItem>, CurrencyType, int>(d2, CurrencyType.Fame, 0);
            Shops[TileRegion.Store_8] = new Tuple<List<ISellableItem>, CurrencyType, int>(c1, CurrencyType.Fame, 0);
            Shops[TileRegion.Store_9] = new Tuple<List<ISellableItem>, CurrencyType, int>(c2, CurrencyType.Fame, 0);

        }
    }
}
