using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.database.character.inventory;
using Shared.database.market;
using Shared.isc;
using Shared.isc.data;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.objects;
using WorldServer.core.objects.inventory;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.utils;

namespace WorldServer.core.market
{
    // The Marketplace in the Nexus: players list items for fame and others buy them, from the
    // Market Broker's window (the Rust client's market.rs) or the market stalls (MarketStall).
    // Every change is one script in MarketStore, so nothing is duplicated or lost when two buyers
    // race, a buyer races the seller taking the item back, or two world servers sweep at once.
    //
    // - Listing takes the item out of the inventory (or a vault chest) in the same script that
    //   writes the listing; each account has at most MaxListings at once.
    // - Buying moves the fame (the seller gets the price less TaxPercent, as before 2.3.3, even
    //   while away) and puts the item in the buyer's inventory, or their Marketplace box when it's
    //   full. Taking a listing back works the same way for the seller.
    // - Listings end after their time (1, 3 or 7 days): the sweeper puts the item in the seller's
    //   box. Items wait in the box, imprints and all, until collected from the window.
    // - Sellers hear of sales and expiries at once if they're playing, else when they next come
    //   to the Nexus (OnEnterNexus).
    public sealed class Marketplace
    {
        // the Market Broker's class (Shared/resources/xml/custom/CustomObjects.xml): walking up
        // to it opens the window in the Rust client
        public const string BrokerClass = "MarketNPC";
        public const int TaxPercent = 5;
        public const int MaxListings = 10;
        public const int MinPrice = 1;
        public const int MaxPrice = 10_000_000;
        public const int DefaultHours = 72;
        public const int MaxHours = 168;
        // the most listings sent to a client at once
        public const int MaxSent = 2000;

        // Seconds between checks of the listings' version, and between expiry sweeps.
        private const double VersionCheck = 1.0;

        private readonly GameServer _server;
        public MarketStore Store { get; }

        private readonly object _cacheLock = new object();
        private long _version = -1;
        private DbMarketData[] _listings = Array.Empty<DbMarketData>();
        private DateTime _checked = DateTime.MinValue;

        // Per account: actions (selling, buying, taking back, collecting) and window refreshes.
        private readonly ConcurrentDictionary<int, Bucket> _actions = new ConcurrentDictionary<int, Bucket>();
        private readonly ConcurrentDictionary<int, Bucket> _views = new ConcurrentDictionary<int, Bucket>();
        // When each account was last reminded of its box, so it's once a sign-in rather than at
        // every Nexus visit.
        private readonly ConcurrentDictionary<int, DateTime> _reminded = new ConcurrentDictionary<int, DateTime>();

        public Marketplace(GameServer server)
        {
            _server = server;
            Store = new MarketStore(server.Database.Conn);
        }

        public void Init()
        {
            _server.InterServerManager.AddHandler<MarketMsg>(Channel.Market, OnMarketMsg);
            // listings from before the sellers' sets: count them in the listing limit
            Store.IndexSellers(Listings().Listings);
        }

        private static int Now => DateTime.UtcNow.ToUnixTimestamp();

        // --- the listings, cached --------------------------------------------------------------

        // Every listing, reread from Redis only when the version moved (checked at most once a
        // second), so a busy window costs one GET rather than reading the whole market.
        public (long Version, DbMarketData[] Listings) Listings()
        {
            lock (_cacheLock)
            {
                if ((DateTime.UtcNow - _checked).TotalSeconds >= VersionCheck)
                {
                    _checked = DateTime.UtcNow;
                    var v = Store.Version();
                    if (v != _version)
                    {
                        _listings = Store.All().OrderByDescending(l => l.StartTime).ThenByDescending(l => l.Id).ToArray();
                        _version = v;
                    }
                }
                return (_version, _listings);
            }
        }

        // After a change of ours: the next read rereads.
        private void Changed()
        {
            lock (_cacheLock)
                _checked = DateTime.MinValue;
        }

        public DbMarketData Listing(int id) => Listings().Listings.FirstOrDefault(l => l.Id == id);

        public IEnumerable<DbMarketData> ListingsOf(int accountId) => Listings().Listings.Where(l => l.SellerId == accountId);

        // --- checks --------------------------------------------------------------------------

        private sealed class Bucket
        {
            public double Tokens;
            public DateTime At = DateTime.UtcNow;
        }

        // A token bucket per account: `burst` at once, `perSecond` after that.
        private static bool Take(ConcurrentDictionary<int, Bucket> buckets, int accountId, double burst, double perSecond)
        {
            var b = buckets.GetOrAdd(accountId, _ => new Bucket { Tokens = burst });
            lock (b)
            {
                var now = DateTime.UtcNow;
                b.Tokens = Math.Min(burst, b.Tokens + (now - b.At).TotalSeconds * perSecond);
                b.At = now;
                if (b.Tokens < 1)
                    return false;
                b.Tokens -= 1;
                return true;
            }
        }

        // Whether the window may refresh now: a few a second at most.
        public bool MayView(Player player) => Take(_views, player.AccountId, 4, 2);

        // Why this player can't use the Marketplace now, or null. rate: count it as an action.
        public string Refusal(Player player, bool rate = true)
        {
            var acc = player.Client?.Account;
            if (acc == null)
                return "Not signed in.";
            if (!_server.WorldManager.Nexus.MarketEnabled)
                return "The Marketplace is closed for now.";
            if (!(player.World is NexusWorld))
                return "The Marketplace is in the Nexus: /market takes you to it.";
            if (acc.Guest)
                return "Register an account to trade on the Marketplace.";
            if (!acc.NameChosen)
                return "Choose a name before trading on the Marketplace.";
            if (player.TradeTarget != null)
                return "Finish your trade first.";
            if (rate && !Take(_actions, player.AccountId, 5, 1.5))
                return "Slow down a little.";
            return null;
        }

        // Why this item can't be listed, or null.
        public static string CantList(Item item)
        {
            if (item == null)
                return "There's no item there.";
            if (item.Untradeable)
                return $"{item.DisplayName} can't be traded.";
            return null;
        }

        public static string CheckPrice(int price, int hours)
        {
            if (price < MinPrice || price > MaxPrice)
                return $"Set a price from {MinPrice} to {MaxPrice:N0} fame.";
            if (hours < 1 || hours > MaxHours)
                return "Listings last from an hour to 7 days.";
            return null;
        }

        // The seller's share of a price.
        public static int Net(int price) => price - price * TaxPercent / 100;

        // The inventory slots items come from and go to: the 8 below the equipment, and the
        // backpack's 8 with one.
        private static IEnumerable<int> Slots(Player player) =>
            Enumerable.Range(4, 8).Concat(player.HasBackpack ? Enumerable.Range(12, 8) : Enumerable.Empty<int>());

        private static int FreeSlot(Player player) => Slots(player).Where(s => player.Inventory[s] == null).DefaultIfEmpty(-1).First();

        private Item ItemOf(ushort type) => _server.Resources.GameData.Items.TryGetValue(type, out var item) ? item : null;

        public string Name(ushort type) => ItemOf(type)?.DisplayName ?? "an item";

        private static ItemData DataOf(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try { return ItemData.CreateData(json); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        // The inventory as the character record holds it, with `slot` changed to this item.
        private static (ushort[] Items, ItemData[] Datas) WithSlot(Player player, int slot, Item item, ItemData data)
        {
            player.SaveToCharacter();
            var items = player.Inventory.GetItemTypes();
            var datas = player.Inventory.Data.GetDatas();
            items[slot] = item?.ObjectType ?? 0xffff;
            datas[slot] = data;
            return (items, datas);
        }

        private static void SetSlot(Player player, int slot, Item item, ItemData data)
        {
            player.Inventory[slot] = item;
            player.Inventory.Data[slot] = data;
            player.SaveToCharacter();
        }

        private static string Describe(MarketStore.Result r) => r switch
        {
            MarketStore.Result.Gone => "That listing is gone: someone bought it, or it ended.",
            MarketStore.Result.OwnListing => "That's your own listing: take it back from My listings.",
            MarketStore.Result.PriceChanged => "That listing's price isn't what you saw: look again.",
            MarketStore.Result.Expired => "That listing has ended.",
            MarketStore.Result.NotEnoughFame => "You haven't enough fame.",
            MarketStore.Result.NotYours => "That isn't your listing.",
            MarketStore.Result.NotOwner => "Your character is busy elsewhere: try again in a moment.",
            MarketStore.Result.TooMany => $"You have {MaxListings} listings already: take one back or wait for a sale.",
            MarketStore.Result.VaultChanged => "Your vault changed meanwhile: try again.",
            MarketStore.Result.ItemChanged => "That listing isn't what you saw: look again.",
            _ => "Something went wrong: try again."
        };

        // --- listing -------------------------------------------------------------------------

        // Lists the item in this inventory slot. Returns (done, what to tell the player).
        public (bool Ok, string Message) Sell(Player player, int slot, int price, int hours, bool rate = true)
        {
            var refusal = Refusal(player, rate) ?? CheckPrice(price, hours);
            if (refusal != null)
                return (false, refusal);
            if (!Slots(player).Contains(slot))
                return (false, "Pick an item from your inventory.");
            var item = player.Inventory[slot];
            var data = player.Inventory.Data[slot];
            var why = CantList(item);
            if (why != null)
                return (false, why);

            var acc = player.Client.Account;
            var now = Now;
            var listing = MarketStore.NewListing(Store.NextId(), acc.AccountId, player.Name, item.ObjectType, data, price, now, now + hours * 3600);
            var (items, datas) = WithSlot(player, slot, null, null);
            MarketStore.Result r;
            try
            {
                r = Store.ListFromInventory(MarketStore.Of(acc, player.Client.Character), items, datas, listing, MaxListings, now);
            }
            catch (RedisException e)
            {
                StaticLogger.Instance.Error($"Marketplace: listing failed: {e.Message}");
                return (false, "The Marketplace didn't answer: nothing was listed. Try again.");
            }
            if (r != MarketStore.Result.Ok)
                return (false, Describe(r));
            SetSlot(player, slot, null, null);
            Changed();
            StaticLogger.Instance.Info($"Marketplace: <{player.Name} {acc.AccountId}> listed {item.ObjectId} for {price} fame ({listing.Id}, {hours} h)");
            return (true, $"Listed {item.DisplayName} for {price:N0} fame. You'll get {Net(price):N0} when it sells.");
        }

        // Lists the item in a vault chest's slot, from Redis (as the Divine Forge takes from the
        // vault): not while its owner is in the vault, whose world holds the chests.
        public (bool Ok, string Message) SellFromVault(Player player, int chest, int slot, int price, int hours)
        {
            var refusal = Refusal(player) ?? CheckPrice(price, hours);
            if (refusal != null)
                return (false, refusal);
            var acc = player.Client.Account;
            if (chest < 0 || chest >= acc.VaultCount || slot < 0 || slot >= 8)
                return (false, "Pick an item from your vault.");
            if (_server.WorldManager.GetWorlds().Any(w => w is VaultWorld v && v.AccountId == player.AccountId && v.Players.Count > 0))
                return (false, "Your vault is open: list from it once you've left it.");

            var db = _server.Database.Conn;
            var key = "vault." + acc.AccountId;
            string itemsField = "vault." + chest, datasField = "vaultData." + chest;
            var raw = db.HashGet(key, new RedisValue[] { itemsField, datasField });
            if (raw[0].IsNull)
                return (false, "There's no item there.");
            var bytes = (byte[])raw[0];
            var types = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, types, 0, types.Length * 2);
            if (slot >= types.Length || types[slot] == 0xffff)
                return (false, "There's no item there.");
            var datas = raw[1].IsNull ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<ItemData[]>((string)raw[1]);
            var item = ItemOf(types[slot]);
            var data = datas != null && slot < datas.Length ? datas[slot] : null;
            var why = CantList(item);
            if (why != null)
                return (false, why);

            types[slot] = 0xffff;
            var newBytes = new byte[bytes.Length];
            Buffer.BlockCopy(types, 0, newBytes, 0, newBytes.Length);
            byte[] newDatas = null;
            if (datas != null && slot < datas.Length)
            {
                datas[slot] = null;
                newDatas = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(datas));
            }
            var now = Now;
            var listing = MarketStore.NewListing(Store.NextId(), acc.AccountId, player.Name, item.ObjectType, data, price, now, now + hours * 3600);
            MarketStore.Result r;
            try
            {
                r = Store.ListFromVault(acc.AccountId, itemsField, datasField, bytes, raw[1].IsNull ? null : (byte[])raw[1], newBytes, newDatas, listing, MaxListings, now);
            }
            catch (RedisException e)
            {
                StaticLogger.Instance.Error($"Marketplace: vault listing failed: {e.Message}");
                return (false, "The Marketplace didn't answer: nothing was listed. Try again.");
            }
            if (r != MarketStore.Result.Ok)
                return (false, Describe(r));
            Changed();
            StaticLogger.Instance.Info($"Marketplace: <{player.Name} {acc.AccountId}> listed {item.ObjectId} from vault chest {chest} for {price} fame ({listing.Id}, {hours} h)");
            return (true, $"Listed {item.DisplayName} from your vault for {price:N0} fame. You'll get {Net(price):N0} when it sells.");
        }

        // --- buying, taking back, collecting ---------------------------------------------------

        // Buys a listing (expectedPrice: what the player saw, 0 for any). The item goes into the
        // inventory, or the box when it's full.
        public (bool Ok, string Message) Buy(Player player, int id, int expectedPrice)
        {
            var refusal = Refusal(player);
            if (refusal != null)
                return (false, refusal);
            var listing = Listing(id);
            if (listing == null)
            {
                // maybe newer than our copy
                Changed();
                listing = Listing(id);
                if (listing == null)
                    return (false, Describe(MarketStore.Result.Gone));
            }
            var acc = player.Client.Account;
            if (listing.SellerId == acc.AccountId)
                return (false, Describe(MarketStore.Result.OwnListing));
            var item = ItemOf(listing.ItemType);
            if (item == null)
                return (false, "That item no longer exists.");
            if (player.CurrentFame < listing.Price)
                return (false, $"You need {listing.Price:N0} fame for that, and have {player.CurrentFame:N0}.");

            var data = DataOf(listing.ItemData);
            var slot = FreeSlot(player);
            var (items, datas) = slot >= 0 ? WithSlot(player, slot, item, data) : (null, null);
            MarketStore.BuyResult r;
            try
            {
                r = Store.Buy(MarketStore.Of(acc, player.Client.Character), player.Name, id, listing.ItemType, expectedPrice, TaxPercent, items, datas, Now);
            }
            catch (RedisException e)
            {
                StaticLogger.Instance.Error($"Marketplace: buying failed: {e.Message}");
                return (false, "The Marketplace didn't answer: nothing was bought. Try again.");
            }
            Changed();
            if (r.Result != MarketStore.Result.Ok)
            {
                if (r.Result == MarketStore.Result.NotEnoughFame)
                    RefreshFame(player);
                return (false, Describe(r.Result));
            }
            acc.StoredFame(r.FameLeft);
            player.CurrentFame = r.FameLeft;
            if (r.InInventory)
                SetSlot(player, slot, item, data);
            Notify(r.SellerId);
            StaticLogger.Instance.Info($"Marketplace: <{player.Name} {acc.AccountId}> bought {item.ObjectId} ({id}) for {listing.Price} fame from <{listing.SellerName} {listing.SellerId}>");
            return (true, r.InInventory
                ? $"Bought {item.DisplayName} for {listing.Price:N0} fame."
                : $"Bought {item.DisplayName} for {listing.Price:N0} fame. Your inventory is full: it's waiting in your Marketplace box.");
        }

        // Takes one of the player's listings back, into the inventory or the box.
        public (bool Ok, string Message) Cancel(Player player, int id)
        {
            var refusal = Refusal(player);
            if (refusal != null)
                return (false, refusal);
            var listing = Listing(id) ?? ReadListing(id);
            if (listing == null)
                return (false, Describe(MarketStore.Result.Gone));
            var acc = player.Client.Account;
            if (listing.SellerId != acc.AccountId)
                return (false, Describe(MarketStore.Result.NotYours));
            var item = ItemOf(listing.ItemType);
            var data = DataOf(listing.ItemData);
            var slot = item != null ? FreeSlot(player) : -1;
            var (items, datas) = slot >= 0 ? WithSlot(player, slot, item, data) : (null, null);
            (MarketStore.Result Result, bool InInventory) r;
            try
            {
                r = Store.Cancel(MarketStore.Of(acc, player.Client.Character), id, items, datas, Now);
            }
            catch (RedisException e)
            {
                StaticLogger.Instance.Error($"Marketplace: taking back failed: {e.Message}");
                return (false, "The Marketplace didn't answer: try again.");
            }
            Changed();
            if (r.Result != MarketStore.Result.Ok)
                return (false, Describe(r.Result));
            if (r.InInventory)
                SetSlot(player, slot, item, data);
            StaticLogger.Instance.Info($"Marketplace: <{player.Name} {acc.AccountId}> took back {listing.ItemType:x4} ({id})");
            return (true, r.InInventory ? $"Took back {Name(listing.ItemType)}." : $"Took back {Name(listing.ItemType)}: your inventory is full, so it's in your Marketplace box.");
        }

        private DbMarketData ReadListing(int id)
        {
            var json = _server.Database.Conn.HashGet(MarketStore.MarketKey, id);
            if (json.IsNullOrEmpty)
                return null;
            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<DbMarketData>(json); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        // Takes items out of the box into the inventory: this entry, or as many as fit (boxId 0).
        public (bool Ok, string Message) Collect(Player player, int boxId)
        {
            var refusal = Refusal(player);
            if (refusal != null)
                return (false, refusal);
            var acc = player.Client.Account;
            var box = Store.Box(acc.AccountId).Where(e => boxId == 0 || e.Item.BoxId == boxId).ToList();
            if (box.Count == 0)
                return (false, boxId == 0 ? "Your Marketplace box is empty." : "That item has already been collected.");
            var took = new List<string>();
            foreach (var (entry, raw) in box)
            {
                var item = ItemOf(entry.ItemType);
                if (item == null)
                    continue;
                var slot = FreeSlot(player);
                if (slot < 0)
                    break;
                var data = DataOf(entry.ItemData);
                var (items, datas) = WithSlot(player, slot, item, data);
                MarketStore.Result r;
                try
                {
                    r = Store.Collect(MarketStore.Of(acc, player.Client.Character), raw, items, datas);
                }
                catch (RedisException e)
                {
                    StaticLogger.Instance.Error($"Marketplace: collecting failed: {e.Message}");
                    break;
                }
                if (r == MarketStore.Result.Ok)
                {
                    SetSlot(player, slot, item, data);
                    took.Add(item.DisplayName);
                }
                else if (r == MarketStore.Result.NotOwner)
                    return (false, Describe(r));
            }
            if (took.Count == 0)
                return (false, "Your inventory is full: make room first.");
            var left = box.Count - took.Count;
            return (true, $"Collected {string.Join(", ", took)}." + (left > 0 ? $" {left} more wait in your box: make room for them." : ""));
        }

        // --- news ----------------------------------------------------------------------------

        // Tells the server the account plays on that its fame and news changed.
        private void Notify(int accountId) =>
            _server.InterServerManager.Publish(Channel.Market, new MarketMsg { AccountId = accountId });

        private void OnMarketMsg(object sender, InterServerEventArgs<MarketMsg> e)
        {
            var id = e.Content.AccountId;
            foreach (var client in _server.ConnectionManager.Clients.Keys.Where(c => c.Player != null && c.Account?.AccountId == id).ToList())
            {
                var player = client.Player;
                player.World?.Post(() =>
                {
                    if (player.Client?.Account == null || player.World == null)
                        return;
                    RefreshFame(player);
                    TellNews(player);
                });
            }
        }

        // The fame Redis holds, after a sale paid it from elsewhere.
        private void RefreshFame(Player player)
        {
            var acc = player.Client.Account;
            var fame = (int)_server.Database.Conn.HashGet(acc.Key, "fame");
            acc.StoredFame(fame);
            player.CurrentFame = fame;
        }

        private void TellNews(Player player)
        {
            List<MarketHistoryEntry> news;
            try { news = Store.TakeNews(player.AccountId); }
            catch (RedisException) { return; }
            foreach (var n in news)
                player.SendInfo(n.Kind == "sold"
                    ? $"<Marketplace> {n.Other} bought your {Name(n.ItemType)} for {n.Price:N0} fame: you got {n.Net:N0} after the {TaxPercent}% tax."
                    : $"<Marketplace> Your {Name(n.ItemType)} didn't sell in time: it's waiting in your Marketplace box.");
        }

        // A player came into the Nexus: what sold or ended while they were away, and, once a
        // sign-in, what waits in their box.
        public void OnEnterNexus(Player player)
        {
            if (player.Client?.Account == null)
                return;
            try
            {
                TellNews(player);
                var count = Store.BoxCount(player.AccountId);
                var now = DateTime.UtcNow;
                if (count > 0 && (!_reminded.TryGetValue(player.AccountId, out var at) || (now - at).TotalHours > 1))
                {
                    _reminded[player.AccountId] = now;
                    player.SendInfo($"<Marketplace> {count} item{(count == 1 ? "" : "s")} wait{(count == 1 ? "s" : "")} for you in your Marketplace box: collect {(count == 1 ? "it" : "them")} at the Market Broker.");
                }
            }
            catch (RedisException) { }
        }

        // --- expiry --------------------------------------------------------------------------

        // Ends the listings whose time is up (every minute, MarketSweeper; any world server may,
        // and only one ends each). force: all of them (/forcecleanmarket).
        public int Sweep(bool force = false)
        {
            var now = Now;
            var ended = 0;
            foreach (var l in Listings().Listings.Where(l => force || l.TimeLeft <= now))
            {
                try
                {
                    if (Store.Expire(l.Id, now, force) == MarketStore.Result.Ok)
                    {
                        ended++;
                        Notify(l.SellerId);
                    }
                }
                catch (RedisException e)
                {
                    StaticLogger.Instance.Error($"Marketplace: expiring {l.Id} failed: {e.Message}");
                }
            }
            if (ended > 0)
            {
                Changed();
                StaticLogger.Instance.Info($"Marketplace: {ended} listing{(ended == 1 ? "" : "s")} ended");
            }
            return ended;
        }
    }
}
