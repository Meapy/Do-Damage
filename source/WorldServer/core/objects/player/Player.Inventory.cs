using Shared;
using Shared.database.character;
using Shared.database.character.inventory;
using Shared.resources;
using System.Linq;
using WorldServer.core.objects.inventory;

namespace WorldServer.core.objects
{
    partial class Player
    {
        public RInventory DbLink { get; private set; }
        public Inventory Inventory { get; private set; }

        public void InitializeInventory(XmlData gameData, DbChar character)
        {
            character.Datas ??= new ItemData[28];
            var items = Utils.ResizeArray(character.Items.Select(_ => (_ == 0xffff || !gameData.Items.ContainsKey(_)) ? null : gameData.Items[_]).ToArray(), 28);
            var datas = Utils.ResizeArray(Client.Character.Datas, 28);
            MoveMisfits(gameData.Classes[ObjectType].SlotTypes, items, datas, character.HasBackpack);
            Inventory = new Inventory(this, items, datas);
            Inventory.InventoryChanged += (sender, e) =>
            {
                Stats.ReCalculateValues();
                Collect(e.NewItems);
            };
            SlotTypes = Utils.ResizeArray(gameData.Classes[ObjectType].SlotTypes, 28);
            // the swap-out row takes what its equipment slot takes
            for (var i = 0; i < InventoryConstants.NUM_SWAP_SLOTS; i++)
                SlotTypes[InventoryConstants.SWAP_FIRST + i] = SlotTypes[i];
        }

        // An item in an equipment slot (or the swap-out row) its class no longer takes, as a Father's
        // tome since his censers took the ability slot: moved to the first empty slot of the
        // inventory or backpack, else left where it is.
        private static void MoveMisfits(int[] classSlots, Item[] items, ItemData[] datas, bool backpack)
        {
            if (classSlots == null)
                return;
            var last = backpack ? 19 : 11;
            for (var e = 0; e < 4 + InventoryConstants.NUM_SWAP_SLOTS; e++)
            {
                var slot = e < 4 ? e : InventoryConstants.SWAP_FIRST + e - 4;
                var want = classSlots[slot < 4 ? slot : slot - InventoryConstants.SWAP_FIRST];
                var item = items[slot];
                if (item == null || want == 0 || item.SlotType == want)
                    continue;
                for (var to = 4; to <= last; to++)
                {
                    if (items[to] != null)
                        continue;
                    items[to] = item;
                    datas[to] = datas[slot];
                    items[slot] = null;
                    datas[slot] = null;
                    break;
                }
            }
        }
    }
}
