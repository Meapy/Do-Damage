using Shared;
using Shared.resources;
using System;
using System.Collections.Generic;
using WorldServer.core.worlds.impl;
using WorldServer.core.objects;
using WorldServer.core.net.stats;

namespace WorldServer.core.objects.vendors
{
    public abstract class SellableObject : StaticObject
    {
        // an int, not a CurrencyType: a change goes out as it's stored, and stats are written as
        // numbers (a CurrencyType, sent when a Marketplace stall turned to a gold listing, ended
        // the Nexus's thread on 2026-11-20)
        private StatTypeValue<int> _currency;
        private StatTypeValue<int> _price;
        private StatTypeValue<int> _rankReq;

        protected SellableObject(GameServer manager, ushort objType) : base(manager, objType, null, true, false, false)
        {
            _price = new StatTypeValue<int>(this, StatDataType.MerchandisePrice, 0);
            _currency = new StatTypeValue<int>(this, StatDataType.MerchandiseCurrency, 0);
            _rankReq = new StatTypeValue<int>(this, StatDataType.MerchandiseRankReq, 0);
        }

        public CurrencyType Currency { get => (CurrencyType)_currency.GetValue(); set => _currency.SetValue((int)value); }
        public int Price { get => _price.GetValue(); set => _price.SetValue(value); }
        public int RankRequired { get => _rankReq.GetValue(); set => _rankReq.SetValue(value); }
        public int Tax { get; set; }

        public virtual void Buy(Player player) => SendFailed(player, BuyResult.Uninitialized);

        // a Buy naming the currency to pay in (ClosedVaultChest sells for fame or gold); the
        // others sell for their one currency
        public virtual void Buy(Player player, CurrencyType currency) => Buy(player);

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            stats[StatDataType.MerchandisePrice] = Price;
            stats[StatDataType.MerchandiseCurrency] = (int)Currency;
            stats[StatDataType.MerchandiseRankReq] = RankRequired;
            base.ExportStats(stats, isOtherPlayer);
        }

        protected void SendFailed(Player player, BuyResult result) => player.Client.SendPacket(new networking.packets.outgoing.BuyResultMessage
        {
            Result = 1,
            ResultString = $"Purchase Error: {result.GetDescription()}"
        });

        protected BuyResult ValidateCustomer(Player player, Item item, int? price = null, CurrencyType? currency = null)
        {
            if (World is TestWorld)
                return BuyResult.IsTestMap;

            if (player.Stars < RankRequired)
                return BuyResult.InsufficientRank;

            var acc = player.Client.Account;

            if (acc.Guest)
            {
                // reload guest prop just in case user registered in game
                acc.Reload("guest");

                if (acc.Guest)
                    return BuyResult.IsGuest;
            }

            if (player.GetCurrency(currency ?? Currency) < (price ?? Price))
                return BuyResult.InsufficientFunds;

            if (item != null) // not perfect, but does the job for now
            {
                var availableSlot = player.Inventory.CreateTransaction().GetAvailableInventorySlot(item);
                if (availableSlot == -1)
                    return BuyResult.NotEnoughSlots;
            }

            return BuyResult.Ok;
        }
    }
}
