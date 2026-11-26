using System;
using System.Linq;
using System.Threading.Tasks;
using Shared.database;
using StackExchange.Redis;
using Xunit;

namespace Tests
{
    // The gold store's Redis side (Shared/database/GoldStore.cs), on a Redis of its own.
    public class GoldStoreTests : IClassFixture<MarketRedis>
    {
        private const string Order = "5O190127TN364715T";
        private static readonly DateTime Now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

        private readonly IDatabase _db;
        private static readonly GoldPack Chest = GoldStore.DefaultPacks.First(p => p.id == "chest");

        public GoldStoreTests(MarketRedis redis)
        {
            redis.Conn.GetServer(redis.Conn.GetEndPoints()[0]).FlushDatabase();
            _db = redis.Db;
            _db.HashSet("account.7", new[] { new HashEntry("name", "Buyer"), new HashEntry("credits", 40), new HashEntry("totalCredits", 40) });
        }

        private int Gold(int acc = 7) => (int)_db.HashGet($"account.{acc}", "credits");

        [Fact]
        public void APaidOrderIsCreditedOnceOnly()
        {
            GoldStore.AddOrder(_db, Order, 7, Chest, "USD", Now);
            Assert.True(_db.KeyTimeToLive(GoldStore.OrderKey(Order)) > TimeSpan.FromHours(23));

            Assert.Equal(GoldStore.CreditResult.Credited, GoldStore.Credit(_db, Order, "CAP1", "buyer@example.com", Now, out var balance));
            Assert.Equal(1140, balance);
            Assert.Equal(1140, Gold());
            Assert.Equal(1140, (int)_db.HashGet("account.7", "totalCredits"));
            // paid: kept for good, the capture findable for refunds, the sale counted and logged
            Assert.Null(_db.KeyTimeToLive(GoldStore.OrderKey(Order)));
            Assert.Equal(Order, (string)_db.StringGet(GoldStore.CaptureKey("CAP1")));
            Assert.Equal(1000, (int)_db.HashGet(GoldStore.SalesKey, "cents.USD"));
            Assert.Equal("paid", GoldStore.Log(_db, 5).Single()["kind"]);

            // the page's capture, PayPal's webhook and a retry: one payment, one credit
            Assert.Equal(GoldStore.CreditResult.AlreadyCredited, GoldStore.Credit(_db, Order, "CAP1", "", Now, out balance));
            Assert.Equal(1140, balance);
            Assert.Equal(1140, Gold());
            Assert.Single(GoldStore.Log(_db, 5));
        }

        [Fact]
        public void CreditsRacingEachOtherPayOnce()
        {
            GoldStore.AddOrder(_db, Order, 7, Chest, "USD", Now);
            var results = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() => GoldStore.Credit(_db, Order, "CAP1", "", Now, out var _)))
                .Select(t => t.Result).ToList();
            Assert.Single(results, r => r == GoldStore.CreditResult.Credited);
            Assert.Equal(1140, Gold());
        }

        [Fact]
        public void UnknownOrdersAndGoneAccountsPayNothing()
        {
            Assert.Equal(GoldStore.CreditResult.NoOrder, GoldStore.Credit(_db, "NOSUCHORDER123", "CAP", "", Now, out _));
            GoldStore.AddOrder(_db, Order, 99, Chest, "USD", Now);
            Assert.Equal(GoldStore.CreditResult.NoAccount, GoldStore.Credit(_db, Order, "CAP", "", Now, out _));
            Assert.False(_db.KeyExists("account.99"));
            Assert.Equal(GoldStore.Created, GoldStore.GetOrder(_db, Order).Status);
        }

        [Fact]
        public void APendingPaymentIsCreditedWhenItClears()
        {
            GoldStore.AddOrder(_db, Order, 7, Chest, "USD", Now);
            Assert.True(GoldStore.MarkPending(_db, Order, "CAP9"));
            Assert.Equal(GoldStore.Pending, GoldStore.GetOrder(_db, Order).Status);
            Assert.Equal(40, Gold());
            Assert.Equal(GoldStore.CreditResult.Credited, GoldStore.Credit(_db, Order, "CAP9", "", Now, out _));
            Assert.Equal(1140, Gold());
            // credited: never pending again
            Assert.False(GoldStore.MarkPending(_db, Order, "CAP9"));
        }

        [Fact]
        public void ARefundTakesBackWhatIsLeftOnce()
        {
            GoldStore.AddOrder(_db, Order, 7, Chest, "USD", Now);
            GoldStore.Credit(_db, Order, "CAP1", "", Now, out _);
            _db.HashSet("account.7", "credits", 150); // spent 990 of it

            Assert.Equal(Order, GoldStore.TakeBack(_db, "CAP1", "refunded", Now, out var taken, out var acc));
            Assert.Equal(150, taken);
            Assert.Equal(7, acc);
            Assert.Equal(0, Gold());
            var o = GoldStore.GetOrder(_db, Order);
            Assert.Equal(GoldStore.Refunded, o.Status);
            Assert.Equal(150, o.TakenBack);
            Assert.Equal(950L, GoldStore.Log(_db, 1).Single()["missing"]);

            Assert.Null(GoldStore.TakeBack(_db, "CAP1", "refunded", Now, out _, out _));
            Assert.Null(GoldStore.TakeBack(_db, "NOTOURS", "refunded", Now, out _, out _));
            // refunded: paying it again gives nothing
            Assert.Equal(GoldStore.CreditResult.Refunded, GoldStore.Credit(_db, Order, "CAP1", "", Now, out _));
            Assert.Equal(0, Gold());
        }

        [Fact]
        public void HistoryShowsOnlyOrdersThatWentThrough()
        {
            GoldStore.AddOrder(_db, "UNPAIDORDER0001", 7, Chest, "USD", Now);
            GoldStore.AddOrder(_db, Order, 7, Chest, "USD", Now);
            GoldStore.Credit(_db, Order, "CAP1", "", Now, out _);
            var h = GoldStore.History(_db, 7);
            Assert.Equal(Order, h.Single().OrderId);
            Assert.Equal(1100, h[0].Gold);
            Assert.Equal("10.00", h[0].Price);
            Assert.Empty(GoldStore.History(_db, 8));
        }

        [Fact]
        public void SessionsAndTheOrderLimit()
        {
            var token = GoldStore.NewSession(_db, 7);
            Assert.Equal(48, token.Length);
            Assert.Equal(7, GoldStore.SessionAccount(_db, token));
            Assert.Null(GoldStore.SessionAccount(_db, token.Substring(1) + "0"));
            Assert.Null(GoldStore.SessionAccount(_db, "../../account.7"));
            GoldStore.EndSession(_db, token);
            Assert.Null(GoldStore.SessionAccount(_db, token));

            for (var i = 0; i < GoldStore.OrdersPerHour; i++)
                Assert.True(GoldStore.MayMakeOrder(_db, 7));
            Assert.False(GoldStore.MayMakeOrder(_db, 7));
            Assert.True(GoldStore.MayMakeOrder(_db, 8));
        }

        [Fact]
        public void ADollarIsAHundredGoldWithMoreForBiggerPacks()
        {
            var packs = GoldStore.DefaultPacks;
            Assert.Equal(new[] { 100, 525, 1100, 2300, 6000, 12500 }, packs.Select(p => p.gold));
            Assert.Equal(new[] { "1.00", "5.00", "10.00", "20.00", "50.00", "100.00" }, packs.Select(p => p.PriceText));
            foreach (var p in packs)
                Assert.Equal(p.price * 100, p.gold - p.bonus); // the base rate, bonus on top
            Assert.Equal(0, packs[0].bonus);
            // each bigger pack gives more gold per euro than the one before
            for (var i = 1; i < packs.Length; i++)
                Assert.True(packs[i].gold / packs[i].price > packs[i - 1].gold / packs[i - 1].price);
            Assert.Single(packs, p => p.best);
            Assert.Equal(packs.Length, GoldStore.Packs(packs).Length); // all pass the settings' checks
        }

        [Fact]
        public void PacksFromTheSettingsAreChecked()
        {
            Assert.Same(GoldStore.DefaultPacks, GoldStore.Packs(null));
            var packs = GoldStore.Packs(new[]
            {
                new GoldPack { id = "small", gold = 50, price = 0.99m },
                new GoldPack { id = "small", gold = 70, price = 1.99m },      // the same id again
                new GoldPack { id = "bad id", gold = 50, price = 0.99m },
                new GoldPack { id = "free", gold = 50, price = 0m },
                new GoldPack { id = "cents", gold = 50, price = 0.999m },
                new GoldPack { id = "none", gold = 0, price = 5m },
            });
            Assert.Equal("small", packs.Single().id);
            Assert.Equal("0.99", packs[0].PriceText);
            Assert.Equal("1234.50", new GoldPack { price = 1234.5m }.PriceText);
            Assert.True(GoldStore.ValidOrderId(Order));
            Assert.False(GoldStore.ValidOrderId("../" + Order));
            Assert.False(GoldStore.ValidOrderId("short"));
        }

        [Fact]
        public void TheStoreSellsInEurosWhateverAnOldConfigSays()
        {
            Assert.Equal("EUR", new Shared.PayPalSettings().Currency);
            var old = Newtonsoft.Json.JsonConvert.DeserializeObject<Shared.PayPalSettings>("{\"clientId\":\"x\",\"currency\":\"USD\"}");
            Assert.Equal("x", old.ClientId);
            Assert.Equal("EUR", old.Currency);
        }

        [Fact]
        public void BlackFridayHalvesEveryPackForADayFromTheFirstStart()
        {
            var d = GoldStore.BlackFriday;
            var ends = GoldStore.StartDiscount(_db, d, Now);
            Assert.Equal(Now.AddHours(24), ends);
            // a later start (the next deploy, another restart) keeps the first day
            Assert.Equal(ends, GoldStore.StartDiscount(_db, d, Now.AddHours(3)));
            Assert.Equal(ends, GoldStore.StartDiscount(_db, d, Now.AddDays(30)));

            var packs = GoldStore.Discounted(GoldStore.DefaultPacks, d, ends, Now.AddHours(23));
            Assert.Equal(new[] { "0.50", "2.50", "5.00", "10.00", "25.00", "50.00" }, packs.Select(p => p.PriceText));
            Assert.Equal(GoldStore.DefaultPacks.Select(p => (decimal?)p.price), packs.Select(p => p.was));
            Assert.Equal(GoldStore.DefaultPacks.Select(p => p.gold), packs.Select(p => p.gold)); // the same gold
            // the configured packs aren't changed, and once it ends they're sold as they were
            Assert.Null(GoldStore.DefaultPacks[0].was);
            Assert.Same(GoldStore.DefaultPacks, GoldStore.Discounted(GoldStore.DefaultPacks, d, ends, Now.AddHours(24)));
        }

        [Fact]
        public void ADiscountStartsOnlyBeforeItsLastStartAndNeverUnderFiftyCents()
        {
            var d = GoldStore.BlackFriday;
            Assert.Null(GoldStore.StartDiscount(_db, d, d.LastStart));
            Assert.Same(GoldStore.DefaultPacks, GoldStore.Discounted(GoldStore.DefaultPacks, d, null, Now));

            var cheap = new[] { new GoldPack { id = "tiny", gold = 60, price = 0.60m }, new GoldPack { id = "odd", gold = 333, price = 3.33m } };
            var packs = GoldStore.Discounted(cheap, d, Now.AddHours(1), Now);
            Assert.Equal("0.50", packs[0].PriceText);
            Assert.Equal("1.67", packs[1].PriceText); // to the cent
            // a pack already at the least isn't marked as discounted
            var least = new GoldPack { id = "least", gold = 50, price = 0.50m };
            Assert.Same(least, GoldStore.Discounted(new[] { least }, d, Now.AddHours(1), Now)[0]);
        }
    }
}
