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
        // are in fame and match the Doomed Realms shop (/shop) where both sell an item, but for
        // the Elder keys and the permanent pets, which are gold.

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
            new ShopItem("Mystery Dye (Clothing)", 15),
            new ShopItem("Mystery Dye (Accessory)", 10)
        };

        // one regular key per dungeon that has a world; guild, test and reusable keys are left out, and
        // so are the Wine Cellar's and the Sanctuary's incantations: Oryx 1 and 2 leave those portals open
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
            ("Cult Key", 150), ("Ice Tomb Key", 150), ("Shaitan's Key", 300), ("Draconis Key", 300),
            ("Puppet Master's Encore Key", 300), ("Oryx's Castle Key", 300), ("Shatters Key", 400), ("Oryx Chamber Key", 400),
            ("Battle Nexus Key", 400), ("Tomb of the Ancients Key", 500), ("Lost Halls Key", 500), ("The Nest Key", 400),
            ("Oryx Chicken Chamber Key", 400)
        };

        // the Christmas Key isn't sold (it still opens its dungeon); sold for fame beside the
        // regular keys (Store 11) since the Elder keys took Store 12 for gold
        private static readonly (string Key, int Price)[] DoomedKeyPrices =
        {
            ("Doom Crystal Key", 30), ("Zombies Key", 50), ("Birthday Key", 50), ("Island Key", 75),
            ("Hash Cellar Incantation", 500), ("Doomed Cellar Incantation", 1000)
        };

        // Every Elder dungeon's key, sold only for gold (bought in the Gold Store, $1 = 100 gold):
        // the Bazaar's Elder pavilion (Store 12) and /shop elderkeys. 10 to 100 gold, in step with
        // how hard the dungeon is. The keys still drop in their dungeons as before.
        public static readonly (string Key, int Price)[] ElderKeyPrices =
        {
            ("Elder Pirate Cave Key", 10), ("Elder Spider Den Key", 10), ("Elder Snake Pit Key", 15),
            ("Elder Sprite World Key", 15), ("Elder Manor Key", 15), ("Elder Beach Key", 20),
            ("Elder Jungle Key", 20), ("Elder Undead Lair Key", 25), ("Elder Davy Key", 25),
            ("Elder Ocean Trench Key", 25), ("Elder Abyss Key", 30), ("Elder Cemetery Key", 35),
            ("Elder Draconis Key", 40), ("Elder Ivory Wyvern Key", 40), ("Elder Tomb Key", 50),
            ("Elder Shatters Key", 75), ("Elder Lost Halls Key", 100)
        };

        // /shop keys (DoomedShop): every key the Bazaar sells for fame, regular and Doomed, but the
        // Oryx chain's keys (Oryx 1 and 2 leave those portals open). No Elder keys: those are gold
        // (/shop elderkeys). More than the shop's 48 stalls, so they rotate.
        private static readonly string[] NotInKeyShop = { "Oryx's Castle Key", "Oryx Chamber Key", "Oryx Chicken Chamber Key" };

        public static IEnumerable<(string Key, int Price)> KeyShopPrices =>
            DungeonKeyPrices.Concat(DoomedKeyPrices)
                .Where(k => !NotInKeyShop.Contains(k.Key) && !SealedDungeons.IsSealedItem(k.Key));

        /// <summary>/shop elderkeys and the Bazaar's Elder pavilion: the Elder keys, in gold, without a closed dungeon's.</summary>
        public static IEnumerable<(string Key, int Price)> ElderKeyShopPrices =>
            ElderKeyPrices.Where(k => !SealedDungeons.IsSealedItem(k.Key));

        private static readonly List<ISellableItem> DungeonKeys = DungeonKeyPrices.Concat(DoomedKeyPrices).Where(k => !SealedDungeons.IsSealedItem(k.Key)).Select(k => (ISellableItem)new ShopItem(k.Key, (ushort)k.Price)).ToList();
        private static readonly List<ISellableItem> ElderKeys = ElderKeyShopPrices.Select(k => (ISellableItem)new ShopItem(k.Key, (ushort)k.Price)).ToList();

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
            Stock = Shops.ToDictionary(s => s.Key, s => (IReadOnlyList<ISellableItem>)s.Value.Item1.ToList());
        }

        // every shop's items as they were at start, before the Nexus put any out on its stalls: a
        // second Nexus (an admin's copy, NexusWorld.IsMain) sells from its own copy of these
        public static Dictionary<TileRegion, IReadOnlyList<ISellableItem>> Stock { get; private set; } = new Dictionary<TileRegion, IReadOnlyList<ISellableItem>>();

        public static readonly Dictionary<TileRegion, Tuple<List<ISellableItem>, CurrencyType, int>> Shops = new Dictionary<TileRegion, Tuple<List<ISellableItem>, CurrencyType, int>>()
        {
            { TileRegion.Store_1, new Tuple<List<ISellableItem>, CurrencyType, int>(StatPotions, CurrencyType.Fame, 0) },
            { TileRegion.Store_2, new Tuple<List<ISellableItem>, CurrencyType, int>(Consumables, CurrencyType.Fame, 0) },
            { TileRegion.Store_3, new Tuple<List<ISellableItem>, CurrencyType, int>(Specials, CurrencyType.Fame, 0) },
            // Store_4 sold the Enchanted Keys, which also gave a Mystery ST Crate: set (ST) gear has no
            // source for now (SETUP-LOCAL.md, "Untiered rings"), and its empty pavilion made way for
            // the Hall of Champions
            { TileRegion.Store_11, new Tuple<List<ISellableItem>, CurrencyType, int>(DungeonKeys, CurrencyType.Fame, 0) },
            { TileRegion.Store_12, new Tuple<List<ISellableItem>, CurrencyType, int>(ElderKeys, CurrencyType.Gold, 0) }
        };

        private static void InitDyes(GameServer gameServer)
        {
            // the Clothing pavilion sells the large cloths and the clothing dyes, the Accessories
            // pavilion the small cloths and the accessory dyes (four pavilions before the Hall of
            // Champions took the east ward's south half); their stalls rotate through them
            var clothing = new List<ISellableItem>();
            var accessories = new List<ISellableItem>();
            var petGenerators = new List<ISellableItem>();
            var permaPetGenerators = new List<ISellableItem>();

            foreach (var i in gameServer.Resources.GameData.Items.Values)
            {
                if (i.InvUse && i.ObjectId.Contains("Generator"))
                {
                    // only the Tiny pets' generators are sold: every pet drops, each where its
                    // pet belongs (SETUP-LOCAL.md, "Pets: single use, drops and attacks")
                    if (Array.Exists(i.ActivateEffects, e => e.Effect == ActivateEffects.PermaPet && e.PetSize == "Tiny"))
                    {
                        permaPetGenerators.Add(new ShopItem(i.ObjectId, 1000));
                        petGenerators.Add(new ShopItem(i.ObjectId, 1250));
                    }
                    continue;
                }

                if (!i.Class.Equals("Dye"))
                    continue;

                if (i.Texture1 != 0)
                {
                    if ((i.ObjectId.Contains("Cloth") && i.ObjectId.Contains("Large")) || (i.ObjectId.Contains("Dye") && i.ObjectId.Contains("Clothing")))
                        clothing.Add(new ShopItem(i.ObjectId, 25));

                    continue;
                }

                if (i.Texture2 != 0)
                {
                    if ((i.ObjectId.Contains("Cloth") && i.ObjectId.Contains("Small")) || (i.ObjectId.Contains("Dye") && i.ObjectId.Contains("Accessory")))
                        accessories.Add(new ShopItem(i.ObjectId, 10));

                    continue;
                }
            }

            Shops[TileRegion.Store_5] = new Tuple<List<ISellableItem>, CurrencyType, int>(petGenerators, CurrencyType.Fame, 0);
            Shops[TileRegion.Store_10] = new Tuple<List<ISellableItem>, CurrencyType, int>(permaPetGenerators, CurrencyType.Gold, 0);

            Shops[TileRegion.Store_8] = new Tuple<List<ISellableItem>, CurrencyType, int>(clothing, CurrencyType.Fame, 0);
            Shops[TileRegion.Store_9] = new Tuple<List<ISellableItem>, CurrencyType, int>(accessories, CurrencyType.Fame, 0);

        }
    }
}
