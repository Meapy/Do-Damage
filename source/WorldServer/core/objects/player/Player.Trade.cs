using Shared.database;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.worlds.impl;
using WorldServer.networking.packets.outgoing;
using WorldServer.core.worlds;
using WorldServer.core.net.datas;
using WorldServer.core.objects.inventory;

namespace WorldServer.core.objects
{
    partial class Player
    {
        private readonly Dictionary<Player, int> _tradeOffers = new Dictionary<Player, int>();
        public bool[] TradeOffers { get; set; }
        public bool TradeAccepted { get; set; }
        public Player TradeTarget { get; set; }

        public void CancelTrade(bool leftmarket = false)
        {
            if (!leftmarket)
            {
                Client.SendPacket(new TradeDone()
                {
                    Code = 1,
                    Description = "Trade canceled!"
                });

                if (TradeTarget != null && TradeTarget.Client != null)
                    TradeTarget.Client.SendPacket(new TradeDone()
                    {
                        Code = 1,
                        Description = "Trade canceled!"
                    });
            }
            else
            {
                Client.SendPacket(new TradeDone()
                {
                    Code = 1,
                    Description = "You left the market, Trade canceled!"
                });

                if (TradeTarget != null && TradeTarget.Client != null)
                    TradeTarget.Client.SendPacket(new TradeDone()
                    {
                        Code = 1,
                        Description = "Client left the market, Trade canceled!"
                    });
            }

            ResetTrade();
        }

        public void RequestTrade(string name)
        {
            if (World is TestWorld) return;

            if (World is DailyQuestWorld)
            {
                SendError("You cannot trade while in the Marketplace!");
                return;
            }

            GameServer.Database.ReloadAccount(Client.Account);

            var acc = Client.Account;

            if (!acc.NameChosen)
            {
                SendError("A unique name is required before trading with others!");
                return;
            }

            if (TradeTarget != null)
            {
                SendError("Already trading!");
                return;
            }

            if (Database.GuestNames.Contains(name))
            {
                SendError(name + " needs to choose a unique name first!");
                return;
            }

            var target = World.GetUniqueNamedPlayer(name);

            if (target == null || !target.CanBeSeenBy(this))
            {
                SendError(name + " not found!");
                return;
            }

            if (target == this)
            {
                SendError("You can't trade with yourself!");
                return;
            }

            if (target.World is DailyQuestWorld)
            {
                SendError("The targeted player is in the Marketplace!");
                return;
            }

            if (target.Client.Account.IgnoreList.Contains(AccountId))
                return; // account is ignored

            if (target.TradeTarget != null)
            {
                SendError(target.Name + " is already trading!");
                return;
            }

            if (_tradeOffers.ContainsKey(target))
            {
                TradeTarget = target;
                TradeOffers = new bool[TradeSlots];
                TradeAccepted = false;
                target.TradeTarget = this;
                target.TradeOffers = new bool[target.TradeSlots];
                target.TradeAccepted = false;
                _tradeOffers.Clear();
                target._tradeOffers.Clear();

                // shouldn't be needed since there is checks on
                // invswap, invdrop, and useitem packets for trading
                //MonitorTrade();
                //target.MonitorTrade();

                var my = TradeItems();
                var your = target.TradeItems();

                Client.SendPacket(new TradeStart()
                {
                    MyItems = my,
                    YourName = target.Name,
                    YourItems = your
                });
                target.Client.SendPacket(new TradeStart()
                {
                    MyItems = your,
                    YourName = Name,
                    YourItems = my
                });
            }
            else
            {
                target._tradeOffers[this] = 1000 * 20;
                target.Client.SendPacket(new TradeRequested()
                {
                    Name = Name
                });
                SendInfo("You have sent a trade request to " + target.Name + "!");
                return;
            }
        }

        // the slots a trade shows and takes from: equipment and inventory, and the backpack if there is one
        public int TradeSlots => HasBackpack ? InventoryConstants.SWAP_FIRST : InventoryConstants.NUM_EQUIPMENT_SLOTS + InventoryConstants.NUM_INVENTORY_SLOTS;

        // whether the item in slot i can be offered (equipment and soulbound items can't)
        public bool CanTradeSlot(int i) => i >= InventoryConstants.NUM_EQUIPMENT_SLOTS && i < TradeSlots && Inventory[i] != null && !Inventory[i].Soulbound;

        // an offer from the client, cut to the trade's slots, with what can't be offered left out
        public bool[] CleanTradeOffer(bool[] offer, out bool soulbound)
        {
            soulbound = false;
            var clean = new bool[TradeSlots];
            for (var i = 0; i < clean.Length && i < offer.Length; i++)
                if (offer[i])
                {
                    if (Inventory[i] != null && Inventory[i].Soulbound)
                        soulbound = true;
                    clean[i] = CanTradeSlot(i);
                }
            return clean;
        }

        private TradeItem[] TradeItems()
        {
            var items = new TradeItem[TradeSlots];
            for (int i = 0; i < items.Length; i++)
                items[i] = new TradeItem()
                {
                    Item = Inventory[i] == null ? -1 : Inventory[i].ObjectType,
                    SlotType = SlotTypes[i],
                    Included = false,
                    Tradeable = CanTradeSlot(i),
                    ItemData = Inventory.Data[i]?.GetData() ?? "{}"
                };
            return items;
        }

        public void ResetTrade()
        {
            if (TradeTarget != null)
            {
                TradeTarget.TradeTarget = null;
                TradeTarget.TradeOffers = null;
                TradeTarget.TradeAccepted = false;
            }
            TradeTarget = null;
            TradeOffers = null;
            TradeAccepted = false;
        }

        private void CheckTradeTimeout(TickTime time)
        {
            var newState = new List<Tuple<Player, int>>();

            foreach (var i in _tradeOffers)
                newState.Add(new Tuple<Player, int>(i.Key, i.Value - time.ElapsedMsDelta));

            foreach (var i in newState)
            {
                if (i.Item2 < 0)
                {
                    i.Item1.SendInfo("Trade to " + Name + " has timed out!");
                    _tradeOffers.Remove(i.Item1);
                }
                else
                    _tradeOffers[i.Item1] = i.Item2;
            }
        }
    }
}
