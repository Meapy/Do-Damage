using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shared.database.character.inventory;
using Shared.database.market;
using StackExchange.Redis;
using Xunit;

namespace Tests
{
    // A Redis of its own for the Marketplace tests, on a free port, with no data on disk: the
    // bundled Windows build (source/Redis), or `redis-server` on the PATH (the CI installs it).
    public sealed class MarketRedis : IDisposable
    {
        private readonly Process _process;
        public ConnectionMultiplexer Conn { get; }
        public IDatabase Db => Conn.GetDatabase();

        public MarketRedis()
        {
            var port = FreePort();
            var exe = "redis-server";
            for (var dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                var bundled = Path.Combine(dir, "source", "Redis", "redis-server.exe");
                if (OperatingSystem.IsWindows() && File.Exists(bundled))
                {
                    exe = bundled;
                    break;
                }
            }
            var info = new ProcessStartInfo(exe, $"--port {port} --save \"\" --appendonly no")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            try { _process = Process.Start(info); }
            catch (Exception e) { throw new InvalidOperationException($"The Marketplace tests need a Redis server ({exe}): {e.Message}"); }
            _process.OutputDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            for (var i = 0; ; i++)
            {
                try
                {
                    Conn = ConnectionMultiplexer.Connect($"127.0.0.1:{port},allowAdmin=true,connectTimeout=500");
                    break;
                }
                catch (RedisConnectionException) when (i < 40) { Thread.Sleep(100); }
            }
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            Conn?.Dispose();
            try { _process.Kill(); } catch (InvalidOperationException) { }
            _process.Dispose();
        }
    }

    public class MarketStoreTests : IClassFixture<MarketRedis>
    {
        private const ushort Sword = 0x0a00, Bow = 0x0a01, Empty = 0xffff;
        private const int Now = 1_800_000_000;

        private readonly IDatabase _db;
        private readonly MarketStore _store;

        public MarketStoreTests(MarketRedis redis)
        {
            redis.Conn.GetServer(redis.Conn.GetEndPoints()[0]).FlushDatabase();
            _db = redis.Db;
            _store = new MarketStore(_db);
        }

        // An account playing on "this server": its lock, a character with these items, fame.
        private MarketStore.Owner Player(int id, int fame, params ushort[] items)
        {
            _db.StringSet($"lock:{id}", "token" + id);
            _db.HashSet($"account.{id}", "fame", fame);
            var inv = Enumerable.Repeat(Empty, 28).ToArray();
            items.CopyTo(inv, 4);
            var (bytes, datas) = MarketStore.CharacterFields(inv, new ItemData[28]);
            _db.HashSet($"char.{id}.1", new[] { new HashEntry("items", bytes), new HashEntry("datas", datas) });
            return new MarketStore.Owner(id, "token" + id, $"char.{id}.1");
        }

        private ushort[] Items(MarketStore.Owner o)
        {
            var bytes = (byte[])_db.HashGet(o.CharKey, "items");
            var items = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, items, 0, bytes.Length);
            return items;
        }

        private int Fame(int id) => (int)_db.HashGet($"account.{id}", "fame");

        private static ushort[] Inv(params ushort[] items)
        {
            var inv = Enumerable.Repeat(Empty, 28).ToArray();
            items.CopyTo(inv, 4);
            return inv;
        }

        private DbMarketData List(MarketStore.Owner seller, ushort item, int price, ItemData data = null, int hours = 24)
        {
            var l = MarketStore.NewListing(_store.NextId(), seller.AccountId, "Seller" + seller.AccountId, item, data, price, Now, Now + hours * 3600);
            Assert.Equal(MarketStore.Result.Ok, _store.ListFromInventory(seller, Inv(), new ItemData[28], l, 10, Now));
            return l;
        }

        [Fact]
        public void ListingTakesTheItemOutOfTheCharacterInTheSameStep()
        {
            var seller = Player(1, 0, Sword);
            var l = List(seller, Sword, 100);
            Assert.Equal(Empty, Items(seller)[4]);
            Assert.Single(_store.All());
            Assert.Equal(1, _store.ListingCount(1));
            Assert.Equal("listed", _store.History(1).Single().Kind);
            Assert.True(_store.Version() > 0);
            Assert.Equal(l.Id, _store.All()[0].Id);
        }

        [Fact]
        public void ListingRefusesWithoutTheAccountLockAndWritesNothing()
        {
            var seller = Player(1, 0, Sword);
            var stale = new MarketStore.Owner(1, "someone else's token", seller.CharKey);
            var l = MarketStore.NewListing(_store.NextId(), 1, "S", Sword, null, 100, Now, Now + 3600);
            Assert.Equal(MarketStore.Result.NotOwner, _store.ListFromInventory(stale, Inv(), new ItemData[28], l, 10, Now));
            Assert.Equal(Sword, Items(seller)[4]);
            Assert.Empty(_store.All());
        }

        [Fact]
        public void TheListingLimitHolds()
        {
            var seller = Player(1, 0);
            for (var i = 0; i < 3; i++)
                List(seller, Sword, 10);
            var l = MarketStore.NewListing(_store.NextId(), 1, "S", Sword, null, 10, Now, Now + 3600);
            Assert.Equal(MarketStore.Result.TooMany, _store.ListFromInventory(seller, Inv(), new ItemData[28], l, 3, Now));
            Assert.Equal(3, _store.All().Count);
        }

        [Fact]
        public void BuyingMovesFameLessTheTaxAndDeliversIntoTheInventory()
        {
            var seller = Player(1, 50);
            var buyer = Player(2, 1000);
            var l = List(seller, Sword, 200);
            var r = _store.Buy(buyer, "Buyer", l.Id, Sword, 200, 5, Inv(Sword), new ItemData[28], Now);
            Assert.Equal(MarketStore.Result.Ok, r.Result);
            Assert.True(r.InInventory);
            Assert.Equal(800, r.FameLeft);
            Assert.Equal(800, Fame(2));
            Assert.Equal(50 + 190, Fame(1));        // 5% of 200 kept as tax
            Assert.Equal(190, r.Net);
            Assert.Equal(Sword, Items(buyer)[4]);
            Assert.Empty(_store.All());
            Assert.Equal(0, _store.ListingCount(1));
            Assert.Empty(_store.Box(2));
            // the seller, away, is told when they come back; once
            var news = _store.TakeNews(1);
            Assert.Equal("sold", news.Single().Kind);
            Assert.Equal("Buyer", news.Single().Other);
            Assert.Empty(_store.TakeNews(1));
            Assert.Equal("bought", _store.History(2).Single().Kind);
        }

        [Fact]
        public void BuyingWithAFullInventoryPutsTheItemInTheBoxWithItsData()
        {
            var seller = Player(1, 0);
            var buyer = Player(2, 1000);
            var data = new ItemData { ObjectId = "Sword", Imprints = new[] { 3, 0, 1 } };
            var l = List(seller, Sword, 100, data);
            var r = _store.Buy(buyer, "Buyer", l.Id, Sword, 100, 5, null, null, Now);
            Assert.Equal(MarketStore.Result.Ok, r.Result);
            Assert.False(r.InInventory);
            var box = _store.Box(2).Single();
            Assert.Equal(Sword, box.Item.ItemType);
            Assert.Equal("bought", box.Item.Why);
            Assert.Equal(new[] { 3, 0, 1 }, ItemData.CreateData(box.Item.ItemData).Imprints);
            // collecting takes it out once
            Assert.Equal(MarketStore.Result.Ok, _store.Collect(buyer, box.Raw, Inv(Sword), new ItemData[28]));
            Assert.Equal(MarketStore.Result.Gone, _store.Collect(buyer, box.Raw, Inv(Sword, Sword), new ItemData[28]));
            Assert.Equal(Sword, Items(buyer)[4]);
            Assert.Equal(Empty, Items(buyer)[5]);
        }

        [Fact]
        public void ABuyerWhoseLockMovedStillGetsTheItemInTheBox()
        {
            var seller = Player(1, 0);
            var buyer = Player(2, 1000);
            var l = List(seller, Sword, 100);
            var moved = new MarketStore.Owner(2, "old token", buyer.CharKey);
            var r = _store.Buy(moved, "Buyer", l.Id, Sword, 100, 5, Inv(Sword), new ItemData[28], Now);
            Assert.Equal(MarketStore.Result.Ok, r.Result);
            Assert.False(r.InInventory);
            Assert.Equal(Empty, Items(buyer)[4]);
            Assert.Single(_store.Box(2));
        }

        [Fact]
        public void BuyingRefusesWhatItShouldAndChangesNothing()
        {
            var seller = Player(1, 0);
            var poor = Player(2, 99);
            var l = List(seller, Sword, 100);
            Assert.Equal(MarketStore.Result.NotEnoughFame, _store.Buy(poor, "P", l.Id, Sword, 100, 5, Inv(Sword), new ItemData[28], Now).Result);
            Assert.Equal(MarketStore.Result.OwnListing, _store.Buy(seller, "S", l.Id, Sword, 100, 5, Inv(Sword), new ItemData[28], Now).Result);
            var rich = Player(3, 1000);
            var changed = _store.Buy(rich, "R", l.Id, Sword, 50, 5, Inv(Sword), new ItemData[28], Now);
            Assert.Equal(MarketStore.Result.PriceChanged, changed.Result);
            Assert.Equal(100, changed.Price);
            Assert.Equal(MarketStore.Result.ItemChanged, _store.Buy(rich, "R", l.Id, Bow, 100, 5, Inv(Bow), new ItemData[28], Now).Result);
            Assert.Equal(MarketStore.Result.Expired, _store.Buy(rich, "R", l.Id, Sword, 100, 5, Inv(Sword), new ItemData[28], Now + 25 * 3600).Result);
            Assert.Equal(MarketStore.Result.Gone, _store.Buy(rich, "R", 999999, Sword, 100, 5, Inv(Sword), new ItemData[28], Now).Result);
            Assert.Equal(99, Fame(2));
            Assert.Equal(1000, Fame(3));
            Assert.Equal(0, Fame(1));
            Assert.Single(_store.All());
            Assert.Equal(Empty, Items(rich)[4]);
        }

        // Many buyers (as on several world servers) race for one listing: exactly one gets it and
        // pays; nobody else loses fame or gets an item.
        [Fact]
        public void RacingBuyersExactlyOneWins()
        {
            var seller = Player(1, 0);
            var l = List(seller, Sword, 100);
            var buyers = Enumerable.Range(10, 40).Select(id => Player(id, 500)).ToList();
            var start = new ManualResetEventSlim();
            var results = new MarketStore.BuyResult[buyers.Count];
            var threads = buyers.Select((b, i) => new Thread(() =>
            {
                start.Wait();
                results[i] = new MarketStore(_db).Buy(b, "B" + b.AccountId, l.Id, Sword, 100, 5, Inv(Sword), new ItemData[28], Now);
            })).ToList();
            threads.ForEach(t => t.Start());
            start.Set();
            threads.ForEach(t => t.Join());

            Assert.Equal(1, results.Count(r => r.Result == MarketStore.Result.Ok));
            Assert.All(results.Where(r => r.Result != MarketStore.Result.Ok), r => Assert.Equal(MarketStore.Result.Gone, r.Result));
            var winner = buyers[Array.FindIndex(results, r => r.Result == MarketStore.Result.Ok)];
            Assert.Equal(400, Fame(winner.AccountId));
            Assert.Equal(95, Fame(1));
            var withSword = buyers.Count(b => Items(b)[4] == Sword) + buyers.Sum(b => _store.Box(b.AccountId).Count);
            Assert.Equal(1, withSword);
            Assert.Equal((buyers.Count - 1) * 500, buyers.Where(b => b.AccountId != winner.AccountId).Sum(b => Fame(b.AccountId)));
        }

        // A buyer, the seller taking it back and two expiry sweeps race: the item goes exactly one
        // way, and fame moves only if a buyer won.
        [Fact]
        public void BuyCancelAndExpiryRacingHandTheItemOutOnce()
        {
            for (var round = 0; round < 25; round++)
            {
                _db.Multiplexer.GetServer(_db.Multiplexer.GetEndPoints()[0]).FlushDatabase();
                var seller = Player(1, 0);
                var buyer = Player(2, 500);
                var l = List(seller, Sword, 100, hours: 1);
                var end = Now + 3600;
                var start = new ManualResetEventSlim();
                var outcomes = new List<string>();
                var tasks = new List<Thread>
                {
                    new Thread(() => { start.Wait(); if (_store.Buy(buyer, "B", l.Id, Sword, 100, 5, Inv(Sword), new ItemData[28], end - 1).Result == MarketStore.Result.Ok) lock (outcomes) outcomes.Add("bought"); }),
                    new Thread(() => { start.Wait(); if (_store.Cancel(seller, l.Id, Inv(Sword), new ItemData[28], end).Result == MarketStore.Result.Ok) lock (outcomes) outcomes.Add("cancelled"); }),
                    new Thread(() => { start.Wait(); if (_store.Expire(l.Id, end) == MarketStore.Result.Ok) lock (outcomes) outcomes.Add("expired"); }),
                    new Thread(() => { start.Wait(); if (_store.Expire(l.Id, end) == MarketStore.Result.Ok) lock (outcomes) outcomes.Add("expired"); }),
                };
                tasks.ForEach(t => t.Start());
                start.Set();
                tasks.ForEach(t => t.Join());

                Assert.Single(outcomes);
                var swords = (Items(seller)[4] == Sword ? 1 : 0) + (Items(buyer)[4] == Sword ? 1 : 0) + _store.Box(1).Count + _store.Box(2).Count;
                Assert.Equal(1, swords);
                Assert.Equal(outcomes[0] == "bought" ? 400 : 500, Fame(2));
                Assert.Equal(outcomes[0] == "bought" ? 95 : 0, Fame(1));
                Assert.Empty(_store.All());
                Assert.Equal(0, _store.ListingCount(1));
            }
        }

        [Fact]
        public void TakingBackOnlyYourOwnAndExpiryWaitsForTheEnd()
        {
            var seller = Player(1, 0);
            var other = Player(2, 0);
            var l = List(seller, Sword, 100);
            Assert.Equal(MarketStore.Result.NotYours, _store.Cancel(other, l.Id, Inv(Sword), new ItemData[28], Now).Result);
            Assert.Equal(MarketStore.Result.Expired, _store.Expire(l.Id, Now + 10));
            Assert.Equal(MarketStore.Result.Ok, _store.Expire(l.Id, Now + 24 * 3600));
            Assert.Equal("expired", _store.Box(1).Single().Item.Why);
            Assert.Equal("expired", _store.TakeNews(1).Single().Kind);
            // a taken-back item with no room waits in the box too
            var l2 = List(seller, Bow, 5);
            var (r, inInventory) = _store.Cancel(seller, l2.Id, null, null, Now);
            Assert.Equal(MarketStore.Result.Ok, r);
            Assert.False(inInventory);
            Assert.Equal(2, _store.Box(1).Count);
        }

        [Fact]
        public void ListingFromTheVaultChecksTheChestIsAsRead()
        {
            var vault = "vault.1";
            var chest = new ushort[] { Sword, Bow, Empty, Empty, Empty, Empty, Empty, Empty };
            var bytes = new byte[16];
            Buffer.BlockCopy(chest, 0, bytes, 0, 16);
            _db.HashSet(vault, "vault.0", bytes);
            var after = (ushort[])chest.Clone();
            after[0] = Empty;
            var afterBytes = new byte[16];
            Buffer.BlockCopy(after, 0, afterBytes, 0, 16);

            var stale = (byte[])bytes.Clone();
            stale[2] = 0x42;
            var l = MarketStore.NewListing(_store.NextId(), 1, "S", Sword, null, 10, Now, Now + 3600);
            Assert.Equal(MarketStore.Result.VaultChanged, _store.ListFromVault(1, "vault.0", "vaultData.0", stale, null, afterBytes, null, l, 10, Now));
            Assert.Empty(_store.All());
            Assert.Equal(MarketStore.Result.Ok, _store.ListFromVault(1, "vault.0", "vaultData.0", bytes, null, afterBytes, null, l, 10, Now));
            Assert.Equal(afterBytes, (byte[])_db.HashGet(vault, "vault.0"));
            // the same read can't list the same sword twice
            var l2 = MarketStore.NewListing(_store.NextId(), 1, "S", Sword, null, 10, Now, Now + 3600);
            Assert.Equal(MarketStore.Result.VaultChanged, _store.ListFromVault(1, "vault.0", "vaultData.0", bytes, null, afterBytes, null, l2, 10, Now));
            Assert.Single(_store.All());
        }

        [Fact]
        public void OldListingsJoinTheLimitAndGoneOnesLeaveIt()
        {
            // a listing from before 2.3.3: in the hash, in no seller set
            var old = MarketStore.NewListing(_store.NextId(), 1, "S", Sword, null, 10, Now, Now + 3600);
            _db.HashSet(MarketStore.MarketKey, old.Id, Newtonsoft.Json.JsonConvert.SerializeObject(old));
            _store.IndexSellers(_store.All());
            Assert.Equal(1, _store.ListingCount(1));
            // a stray id whose listing is gone doesn't count against the limit
            _db.SetAdd(MarketStore.SellerKey(1), 424242);
            var seller = Player(1, 0);
            var l = MarketStore.NewListing(_store.NextId(), 1, "S", Sword, null, 10, Now, Now + 3600);
            Assert.Equal(MarketStore.Result.Ok, _store.ListFromInventory(seller, Inv(), new ItemData[28], l, 2, Now));
            Assert.Equal(2, _store.ListingCount(1));
        }
    }
}
