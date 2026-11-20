using System.Threading.Tasks;
using Shared.database;
using Shared.database.account;
using Shared.resources;
using WorldServer.core.net.stats;
using WorldServer.core.worlds.impl;
using WorldServer.utils;
using System.Collections.Generic;

namespace WorldServer.core.objects.vendors
{
    // A vault chest still to buy: for fame (Price, doubling every UnlockPrices.DoublesEvery the
    // account bought with fame) or for gold (GoldPrice, always the same, not counted towards the
    // fame price). A client that doesn't send a currency buys with fame.
    internal class ClosedVaultChest : SellableObject
    {
        private readonly StatTypeValue<int> _goldPrice;

        public ClosedVaultChest(GameServer manager, ushort objType) : base(manager, objType)
        {
            Price = manager.Resources.Settings.VaultChestPrice;
            Currency = CurrencyType.Fame;
            _goldPrice = new StatTypeValue<int>(this, StatDataType.MerchandiseGoldPrice, UnlockPrices.VaultGold);
        }

        public int GoldPrice { get => _goldPrice.GetValue(); set => _goldPrice.SetValue(value); }

        public static int FamePrice(GameServer manager, DbAccount acc) =>
            UnlockPrices.FamePrice(manager.Resources.Settings.VaultChestPrice, acc.VaultChestsFame);

        // the price for the account whose vault it stands in
        public void PriceFor(DbAccount acc) => Price = FamePrice(GameServer, acc);

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            stats[StatDataType.MerchandiseGoldPrice] = GoldPrice;
            base.ExportStats(stats, isOtherPlayer);
        }

        public override void Buy(Player player) => Buy(player, CurrencyType.Fame);

        public override void Buy(Player player, CurrencyType currency)
        {
            var db = GameServer.Database;
            var acc = player.Client.Account;
            acc.Reload(UnlockPrices.VaultFameField);
            var gold = currency == CurrencyType.Gold;
            var paid = gold ? CurrencyType.Gold : CurrencyType.Fame;
            var price = gold ? UnlockPrices.VaultGold : FamePrice(GameServer, acc);

            var result = ValidateCustomer(player, null, price, paid);
            if (result != BuyResult.Ok)
            {
                SendFailed(player, result);
                return;
            }

            var trans = db.Conn.CreateTransaction();

            _ = GameServer.Database.CreateChest(acc, trans);
            if (!gold)
                _ = trans.HashIncrementAsync(acc.Key, UnlockPrices.VaultFameField);

            var t1 = db.UpdateCurrency(acc, -price, paid, trans);
            var t2 = trans.ExecuteAsync();

            _ = Task.WhenAll(t1, t2).ContinueWith(t =>
            {
                if (t.IsCanceled || !t2.Result)
                {
                    SendFailed(player, BuyResult.TransactionFailed);
                    return;
                }

                acc.Reload("vaultCount");
                acc.Reload(UnlockPrices.VaultFameField);
                player.CurrentFame = acc.Fame;
                player.Credits = acc.Credits;
                var vault = World as VaultWorld;
                vault?.AddChest(this);
                vault?.PriceClosedChests();
                acc.Reload();
                player.Client.SendPacket(new networking.packets.outgoing.BuyResultMessage()
                {
                    Result = 0,
                    ResultString = "Vault chest purchased!"
                });
            }).ContinueWith(e => StaticLogger.Instance.Error(e.Exception.InnerException.ToString()), TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
