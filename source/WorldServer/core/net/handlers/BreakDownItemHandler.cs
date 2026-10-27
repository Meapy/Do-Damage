using System;
using System.Drawing;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NLog.LayoutRenderers;
using Shared;
using Shared.database.character.inventory;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.objects.containers;
using WorldServer.core.worlds;
using WorldServer.logic.loot;
using WorldServer.networking;
using WorldServer.utils;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldServer.core.net.handlers
{
    public sealed class BreakdownSlotMessage : IMessageHandler
    {
        public override MessageId MessageId => MessageId.BREAKDOWN_SLOT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var time = rdr.ReadInt32();
            var slot = rdr.ReadByte();
            var objectId = rdr.ReadInt32();

            var player = client.Player;

            var entity = player?.World?.GetEntity(objectId);
            if (entity == null || player.TradeTarget != null)
                return;

            // Only the player's own items or a bag beside them. A gift chest's items are gifts in
            // the database too, which breaking one down here wouldn't remove.
            if (!CanTakeFrom(player, entity) || entity is GiftChest)
            {
                player.SendError("You can only break down items in your inventory or a bag beside you.");
                return;
            }

            var container = (IContainer)entity;

            if (entity.Id == player.Id && slot < 4 || slot >= 20)
            {
                client.Disconnect("Invalid Player Breakdown Slot");
                return;
            }

            if (entity.Id != player.Id && slot >= 8)
            {
                client.Disconnect("Invalid Container Breakdown Slot");
                return;
            }

            // empty: already broken down (a quick second click) or moved
            var item = container.Inventory[slot];
            if (item == null)
                return;

            if (TryFameBreakdown(player, container, slot, item))
                return;

            if (TryItemBreakdown(player, container, slot, item))
                return;

            TryDefaultBreakdown(player, container, slot, item);
        }

        private bool TryFameBreakdown(Player player, IContainer e, int slot, Item item)
        {
            if (item.BreakDownData == null || item.BreakDownData.Fame == 0)
                return false;

            var fameToGive = item.BreakDownData.Fame;
            var data = TakeOut(e, slot);

            var db = player.GameServer.Database;
            var acc = player.Client.Account;
            var trans = db.Conn.CreateTransaction();
            var t1 = db.UpdateCurrency(acc, fameToGive, CurrencyType.Fame, trans);
            var t2 = trans.ExecuteAsync();

            _ = Task.WhenAll(t1, t2).ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    PutBack(e, slot, item, data);
                    player.SendError("Unable to breakdown item into fame");
                    return;
                }

                player.CurrentFame = acc.Fame;
                acc.Reload();

                player.SendInfo($"{item.DisplayName} was broken down into {fameToGive} fame");
            }).ContinueWith(e => StaticLogger.Instance.Error(e.Exception.InnerException.ToString()), TaskContinuationOptions.OnlyOnFaulted);
            return true;
        }

        private bool TryItemBreakdown(Player player, IContainer e, int slot, Item item)
        {
            if (item.BreakDownData == null || item.BreakDownData.ItemName == null)
                return false;

            if (!player.GameServer.Resources.GameData.IdToObjectType.TryGetValue(item.BreakDownData.ItemName, out var itemType))
            {
                player.SendError("Unable to breakdown item");
                return false;
            }

            if (!player.GameServer.Resources.GameData.Items.TryGetValue(itemType, out var newItem))
            {
                player.SendError("Unable to breakdown item");
                return false;
            }

            TakeOut(e, slot);
            e.Inventory[slot] = newItem;

            player.SendInfo($"{item.DisplayName} was broken down into {newItem.DisplayName}");
            return true;
        }

        // The item leaves its slot before the fame is paid, so a second breakdown of the same slot
        // (in the same tick, before the database answers) finds it empty and pays nothing.
        private static ItemData TakeOut(IContainer e, int slot)
        {
            var data = e.Inventory.Data[slot];
            e.Inventory[slot] = null;
            e.Inventory.Data[slot] = null;
            return data;
        }

        private static void PutBack(IContainer e, int slot, Item item, ItemData data)
        {
            if (e.Inventory[slot] != null)
                return;
            e.Inventory[slot] = item;
            e.Inventory.Data[slot] = data;
        }

        private void TryDefaultBreakdown(Player player, IContainer e, int slot, Item item)
        {
            //like t10-11 gear would be 3, t12 - 13 would be 5, t14(armor specific since theres no t14 - 15 weapon drops) would be 10

            var fameToGive = 0;

            var type = TierLoot.SlotTypesToItemType(item.SlotType);

            switch (type)
            {
                case ItemType.Weapon:
                    {
                        if (item.Tier == 10 || item.Tier == 11)
                            fameToGive = 3;
                        else if (item.Tier == 12)
                            fameToGive = 5;
                        else if (item.Tier == 13 || item.Tier == 14)
                            fameToGive = 10;
                    }
                    break;
                case ItemType.Ability:
                    {
                        if (item.Tier == 4)
                            fameToGive = 3;
                        else if (item.Tier == 5)
                            fameToGive = 5;
                        else if (item.Tier == 6)
                            fameToGive = 10;
                    }
                    break;
                case ItemType.Armor:
                    {
                        if (item.Tier == 11 || item.Tier == 12)
                            fameToGive = 3;
                        else if (item.Tier == 13)
                            fameToGive = 5;
                        else if (item.Tier == 14 || item.Tier == 15)
                            fameToGive = 10;
                    }
                    break;
                case ItemType.Ring:
                    {
                        if (item.Tier == 4)
                            fameToGive = 3;
                        else if (item.Tier == 5)
                            fameToGive = 5;
                        else if (item.Tier == 6)
                            fameToGive = 10;
                    }
                    break;
                case ItemType.Potion:
                    {
                        if (item.Tier == 2 || item.Tier == 4 || item.Tier == 5)
                        {
                            var name = item.DisplayName;
                            if (name.Contains("Life") || name.Contains("Mana"))
                                fameToGive = 5;
                            else
                                fameToGive = 2;
                            if (item.DisplayName.Contains("Greater"))
                                fameToGive *= 2;
                        }
                    }
                    break;
            }

            if (fameToGive == 0)
            {
                player.SendError("This item can't be broken down");
                return;
            }

            var data = TakeOut(e, slot);

            var db = player.GameServer.Database;
            var acc = player.Client.Account;
            var trans = db.Conn.CreateTransaction();
            var t1 = db.UpdateCurrency(acc, fameToGive, CurrencyType.Fame, trans);
            var t2 = trans.ExecuteAsync();

            _ = Task.WhenAll(t1, t2).ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    PutBack(e, slot, item, data);
                    player.SendError("Unable to breakdown item into fame");
                    return;
                }

                player.CurrentFame = acc.Fame;

                acc.Reload();

                player.SendInfo($"Tier {item.Tier} {type} was broken down into {fameToGive} fame");
            }).ContinueWith(e => StaticLogger.Instance.Error(e.Exception.InnerException.ToString()), TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
