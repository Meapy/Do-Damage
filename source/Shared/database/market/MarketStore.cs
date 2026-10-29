using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Shared.database.account;
using Shared.database.character;
using Shared.database.character.inventory;
using StackExchange.Redis;

namespace Shared.database.market
{
    // The Marketplace's Redis side: every change is one Lua script, so it happens whole or not at
    // all, and two world servers (a rolling deploy) racing for one listing can't both win it.
    //
    //   market                    hash: listing id -> DbMarketData JSON (as before 2.3.3)
    //   nextMarketId              listing and box entry ids
    //   market.version            counter, raised by every change to `market`; world servers
    //                             reread the listings only when it moved (MarketIndex)
    //   market.seller.<acc>       set of the account's listing ids (the listing limit)
    //   market.box.<acc>          list of MarketBoxItem JSON: items waiting for the account (bought,
    //                             taken back or expired while its inventory was full or it was away)
    //   market.history.<acc>      list of MarketHistoryEntry JSON, newest first, the last 50
    //   market.news.<acc>         list of MarketHistoryEntry JSON the account hasn't been told
    //                             about yet (sales and expiries while it was away)
    //
    // A listing from the inventory writes the character's items in the same script, on the
    // condition that this world server still holds the account's lock: the item is never both
    // listed and saved in the character. An item delivered into the inventory is written the same
    // way; with no room, it goes to the box instead, keeping its data (imprints, stack counts).
    public sealed class MarketStore
    {
        public const string MarketKey = "market";
        public const string VersionKey = "market.version";
        public const int HistoryLength = 50;

        public static string SellerKey(int accountId) => "market.seller." + accountId;
        public static string BoxKey(int accountId) => "market.box." + accountId;
        public static string HistoryKey(int accountId) => "market.history." + accountId;
        public static string NewsKey(int accountId) => "market.news." + accountId;

        private readonly IDatabase _db;

        public MarketStore(IDatabase db) => _db = db;

        public IDatabase Db => _db;

        // What a script answered.
        public enum Result
        {
            Ok = 1,
            Gone = -1,          // no such listing (sold, taken back or expired)
            OwnListing = -2,
            PriceChanged = -3,
            Expired = -4,
            NotEnoughFame = -5,
            NotYours = -6,
            NotOwner = -7,      // this server no longer holds the account (signed in elsewhere)
            TooMany = -8,       // the listing limit
            VaultChanged = -9,  // the chest changed since it was read
            ItemChanged = -10,  // the listing holds another item than the one expected
        }

        // Shared by the scripts: a history line for an account, kept to the last 50.
        private const string Lib = @"
local function hist(acc, entry)
  local k = 'market.history.' .. acc
  redis.call('LPUSH', k, entry)
  redis.call('LTRIM', k, 0, 49)
end
local function entry(kind, o, price, net, other, now)
  return cjson.encode({kind = kind, itemType = o.itemType, itemData = o.itemData, price = price, net = net, other = other, time = now})
end
local function box(acc, o, why, boxId, now)
  redis.call('RPUSH', 'market.box.' .. acc, cjson.encode({boxId = boxId, itemType = o.itemType, itemData = o.itemData, why = why, time = now}))
end
local function deliver(lockKey, token, charKey, items, datas)
  if items == '' then return false end
  if redis.call('GET', lockKey) ~= token then return false end
  redis.call('HSET', charKey, 'items', items)
  redis.call('HSET', charKey, 'datas', datas)
  return true
end
-- the seller's set loses ids whose listing is gone (from before the sets existed)
local function tidy(setKey, marketKey)
  for _, id in ipairs(redis.call('SMEMBERS', setKey)) do
    if redis.call('HEXISTS', marketKey, id) == 0 then redis.call('SREM', setKey, id) end
  end
end
";

        // KEYS: lock, char, market, seller set, version
        // ARGV: token, items, datas, id, listing json, limit, sellerId, now
        private static readonly LuaScript ListFromInventoryScript = LuaScript.Prepare(Lib + @"
if redis.call('GET', @lockKey) ~= @token then return -7 end
tidy(@sellerKey, @marketKey)
if redis.call('SCARD', @sellerKey) >= tonumber(@limit) then return -8 end
redis.call('HSET', @charKey, 'items', @items)
redis.call('HSET', @charKey, 'datas', @datas)
redis.call('HSET', @marketKey, @id, @json)
redis.call('SADD', @sellerKey, @id)
redis.call('INCR', @versionKey)
local o = cjson.decode(@json)
hist(@sellerId, entry('listed', o, o.price, 0, '', tonumber(@now)))
return 1
");

        private static readonly LuaScript ListFromVaultScript = LuaScript.Prepare(Lib + @"
local cur = redis.call('HGET', @vaultKey, @itemsField)
if cur == false then cur = '' end
if cur ~= @expectItems then return -9 end
cur = redis.call('HGET', @vaultKey, @datasField)
if cur == false then cur = '' end
if cur ~= @expectDatas then return -9 end
tidy(@sellerKey, @marketKey)
if redis.call('SCARD', @sellerKey) >= tonumber(@limit) then return -8 end
redis.call('HSET', @vaultKey, @itemsField, @items)
if @datas ~= '' then redis.call('HSET', @vaultKey, @datasField, @datas) end
redis.call('HSET', @marketKey, @id, @json)
redis.call('SADD', @sellerKey, @id)
redis.call('INCR', @versionKey)
local o = cjson.decode(@json)
hist(@sellerId, entry('listed', o, o.price, 0, '', tonumber(@now)))
return 1
");

        private static readonly LuaScript BuyScript = LuaScript.Prepare(Lib + @"
local json = redis.call('HGET', @marketKey, @id)
if not json then return {-1} end
local o = cjson.decode(json)
if tostring(o.sellerId) == @buyerId then return {-2} end
if o.itemType ~= tonumber(@itemType) then return {-10} end
if tonumber(@price) > 0 and o.price ~= tonumber(@price) then return {-3, o.price} end
local now = tonumber(@now)
if o.timeLeft <= now then return {-4} end
local fame = tonumber(redis.call('HGET', @buyerAcc, 'fame') or '0') or 0
if fame < o.price then return {-5, fame} end
redis.call('HDEL', @marketKey, @id)
redis.call('SREM', 'market.seller.' .. o.sellerId, @id)
redis.call('INCR', @versionKey)
local left = redis.call('HINCRBY', @buyerAcc, 'fame', -o.price)
local tax = math.floor(o.price * tonumber(@tax) / 100)
local net = o.price - tax
if net > 0 then
  redis.call('HINCRBY', 'account.' .. o.sellerId, 'fame', net)
  redis.call('HINCRBY', 'account.' .. o.sellerId, 'totalFame', net)
end
local inInventory = 0
if deliver(@lockKey, @token, @charKey, @items, @datas) then inInventory = 1 else box(@buyerId, o, 'bought', tonumber(@boxId), now) end
hist(@buyerId, entry('bought', o, o.price, 0, o.sellerName, now))
local sold = entry('sold', o, o.price, net, @buyerName, now)
hist(o.sellerId, sold)
redis.call('RPUSH', 'market.news.' .. o.sellerId, sold)
return {1, left, inInventory, net, o.sellerId}
");

        private static readonly LuaScript CancelScript = LuaScript.Prepare(Lib + @"
local json = redis.call('HGET', @marketKey, @id)
if not json then return {-1} end
local o = cjson.decode(json)
if tostring(o.sellerId) ~= @sellerId then return {-6} end
local now = tonumber(@now)
redis.call('HDEL', @marketKey, @id)
redis.call('SREM', 'market.seller.' .. o.sellerId, @id)
redis.call('INCR', @versionKey)
local inInventory = 0
if deliver(@lockKey, @token, @charKey, @items, @datas) then inInventory = 1 else box(@sellerId, o, 'cancelled', tonumber(@boxId), now) end
hist(@sellerId, entry('cancelled', o, o.price, 0, '', now))
return {1, inInventory}
");

        // Only once the listing's time is up: a sweeper on any world server may call it.
        private static readonly LuaScript ExpireScript = LuaScript.Prepare(Lib + @"
local json = redis.call('HGET', @marketKey, @id)
if not json then return -1 end
local o = cjson.decode(json)
local now = tonumber(@now)
if tonumber(@force) == 0 and o.timeLeft > now then return -4 end
redis.call('HDEL', @marketKey, @id)
redis.call('SREM', 'market.seller.' .. o.sellerId, @id)
redis.call('INCR', @versionKey)
box(o.sellerId, o, 'expired', tonumber(@boxId), now)
local e = entry('expired', o, o.price, 0, '', now)
hist(o.sellerId, e)
redis.call('RPUSH', 'market.news.' .. o.sellerId, e)
return 1
");

        // An item out of the box into the inventory: exactly one entry, once.
        private static readonly LuaScript CollectScript = LuaScript.Prepare(@"
if redis.call('GET', @lockKey) ~= @token then return -7 end
if redis.call('LREM', @boxKey, 1, @entry) == 0 then return -1 end
redis.call('HSET', @charKey, 'items', @items)
redis.call('HSET', @charKey, 'datas', @datas)
return 1
");

        private static readonly LuaScript DrainScript = LuaScript.Prepare(@"
local all = redis.call('LRANGE', @newsKey, 0, -1)
redis.call('DEL', @newsKey)
return all
");

        // Whose inventory a change writes: the account, the lock this server holds on it (the
        // world server's, `lock:<id>`) and the character's record.
        public readonly struct Owner
        {
            public readonly int AccountId;
            public readonly string LockToken;
            public readonly string CharKey;

            public Owner(int accountId, string lockToken, string charKey)
            {
                AccountId = accountId;
                LockToken = lockToken ?? "";
                CharKey = charKey ?? "none";
            }

            public string AccountKey => "account." + AccountId;
        }

        public static Owner Of(DbAccount acc, DbChar chr) => new Owner(acc.AccountId, acc.LockToken, chr?.Key);

        public int NextId() => (int)_db.StringIncrement("nextMarketId");

        public long Version() => (long)_db.StringGet(VersionKey);

        // Every listing; only when the version moved (MarketIndex).
        public List<DbMarketData> All()
        {
            var list = new List<DbMarketData>();
            foreach (var e in _db.HashGetAll(MarketKey))
            {
                try
                {
                    var l = JsonConvert.DeserializeObject<DbMarketData>(e.Value);
                    if (l != null && l.Id == (int)e.Name)
                        list.Add(l);
                }
                catch (JsonException) { }
            }
            return list;
        }

        // The listing as it would be stored.
        public static DbMarketData NewListing(int id, int sellerId, string sellerName, ushort itemType, ItemData data, int price, int start, int end) => new DbMarketData
        {
            Id = id,
            ItemType = itemType,
            ItemData = data?.GetData(),
            SellerId = sellerId,
            SellerName = sellerName,
            Price = price,
            StartTime = start,
            TimeLeft = end,
            Currency = resources.CurrencyType.Fame
        };

        private static string Json(DbMarketData l) => JsonConvert.SerializeObject(l);

        // A character's items and data as the character record stores them (DbChar).
        public static (byte[] Items, string Datas) CharacterFields(ushort[] items, ItemData[] datas)
        {
            var bytes = new byte[items.Length * 2];
            Buffer.BlockCopy(items, 0, bytes, 0, bytes.Length);
            return (bytes, JsonConvert.SerializeObject(datas));
        }

        // Lists an item taken out of the inventory: the character's items (without it) are written
        // with the listing, if this server still holds the account's lock.
        public Result ListFromInventory(Owner acc, ushort[] items, ItemData[] datas, DbMarketData listing, int limit, int now)
        {
            var (bytes, json) = CharacterFields(items, datas);
            return (Result)(int)_db.ScriptEvaluate(ListFromInventoryScript, new
            {
                lockKey = (RedisKey)$"lock:{acc.AccountId}",
                charKey = (RedisKey)acc.CharKey,
                marketKey = (RedisKey)MarketKey,
                sellerKey = (RedisKey)SellerKey(acc.AccountId),
                versionKey = (RedisKey)VersionKey,
                token = acc.LockToken,
                items = bytes,
                datas = json,
                id = listing.Id,
                json = Json(listing),
                limit,
                sellerId = acc.AccountId,
                now
            });
        }

        // Lists an item out of a vault chest: the chest is written with the listing, if it's still
        // as it was read (expect*: the raw fields, null for none).
        public Result ListFromVault(int accountId, string itemsField, string datasField, byte[] expectItems, byte[] expectDatas,
            byte[] items, byte[] datas, DbMarketData listing, int limit, int now)
        {
            return (Result)(int)_db.ScriptEvaluate(ListFromVaultScript, new
            {
                vaultKey = (RedisKey)("vault." + accountId),
                marketKey = (RedisKey)MarketKey,
                sellerKey = (RedisKey)SellerKey(accountId),
                versionKey = (RedisKey)VersionKey,
                itemsField,
                datasField,
                expectItems = expectItems ?? Array.Empty<byte>(),
                expectDatas = expectDatas ?? Array.Empty<byte>(),
                items,
                datas = datas ?? Array.Empty<byte>(),
                id = listing.Id,
                json = Json(listing),
                limit,
                sellerId = accountId,
                now
            });
        }

        public sealed class BuyResult
        {
            public Result Result;
            public int FameLeft;        // the buyer's fame after paying
            public bool InInventory;    // delivered into the inventory, else into the box
            public int Net;             // what the seller got
            public int SellerId;
            public int Price;           // PriceChanged: the listing's price
        }

        // Buys a listing: the fame moves, the listing goes, and the item lands in the buyer's
        // inventory (items/datas: the inventory with it, or null when there's no room) or box.
        public BuyResult Buy(Owner buyer, string buyerName, int id, ushort itemType, int expectedPrice, int taxPercent, ushort[] items, ItemData[] datas, int now)
        {
            var (bytes, json) = items != null ? CharacterFields(items, datas) : (Array.Empty<byte>(), "");
            var r = (RedisResult[])_db.ScriptEvaluate(BuyScript, new
            {
                marketKey = (RedisKey)MarketKey,
                buyerAcc = (RedisKey)buyer.AccountKey,
                versionKey = (RedisKey)VersionKey,
                lockKey = (RedisKey)$"lock:{buyer.AccountId}",
                charKey = (RedisKey)buyer.CharKey,
                id,
                buyerId = buyer.AccountId.ToString(),
                buyerName,
                itemType = (int)itemType,
                price = expectedPrice,
                now,
                tax = taxPercent,
                boxId = NextId(),
                token = buyer.LockToken,
                items = bytes,
                datas = json
            });
            var result = new BuyResult { Result = (Result)(int)r[0] };
            if (result.Result == Result.Ok)
            {
                result.FameLeft = (int)r[1];
                result.InInventory = (int)r[2] == 1;
                result.Net = (int)r[3];
                result.SellerId = (int)r[4];
            }
            else if (result.Result == Result.PriceChanged)
                result.Price = (int)r[1];
            return result;
        }

        // Takes a listing back: into the inventory (items/datas with it) or, with no room, the box.
        public (Result Result, bool InInventory) Cancel(Owner seller, int id, ushort[] items, ItemData[] datas, int now)
        {
            var (bytes, json) = items != null ? CharacterFields(items, datas) : (Array.Empty<byte>(), "");
            var r = (RedisResult[])_db.ScriptEvaluate(CancelScript, new
            {
                marketKey = (RedisKey)MarketKey,
                versionKey = (RedisKey)VersionKey,
                lockKey = (RedisKey)$"lock:{seller.AccountId}",
                charKey = (RedisKey)seller.CharKey,
                id,
                sellerId = seller.AccountId.ToString(),
                now,
                boxId = NextId(),
                token = seller.LockToken,
                items = bytes,
                datas = json
            });
            var result = (Result)(int)r[0];
            return (result, result == Result.Ok && (int)r[1] == 1);
        }

        // Ends a listing whose time is up (or any, with force: the admin's /forcecleanmarket), its
        // item to the seller's box.
        public Result Expire(int id, int now, bool force = false) => (Result)(int)_db.ScriptEvaluate(ExpireScript, new
        {
            marketKey = (RedisKey)MarketKey,
            versionKey = (RedisKey)VersionKey,
            id,
            now,
            force = force ? 1 : 0,
            boxId = NextId()
        });

        // The items waiting in the account's box, oldest first, with their raw entries (to collect).
        public List<(MarketBoxItem Item, string Raw)> Box(int accountId) =>
            _db.ListRange(BoxKey(accountId)).Select(v => (string)v).Select(s => (Parse<MarketBoxItem>(s), s)).Where(e => e.Item1 != null).ToList();

        public long BoxCount(int accountId) => _db.ListLength(BoxKey(accountId));

        // Takes this box entry into the inventory (items/datas: the inventory with it).
        public Result Collect(Owner acc, string rawEntry, ushort[] items, ItemData[] datas)
        {
            var (bytes, json) = CharacterFields(items, datas);
            return (Result)(int)_db.ScriptEvaluate(CollectScript, new
            {
                lockKey = (RedisKey)$"lock:{acc.AccountId}",
                charKey = (RedisKey)acc.CharKey,
                boxKey = (RedisKey)BoxKey(acc.AccountId),
                token = acc.LockToken,
                entry = rawEntry,
                items = bytes,
                datas = json
            });
        }

        public List<MarketHistoryEntry> History(int accountId) =>
            _db.ListRange(HistoryKey(accountId)).Select(v => Parse<MarketHistoryEntry>(v)).Where(e => e != null).ToList();

        // What the account hasn't been told yet, once: the list is emptied as it's read.
        public List<MarketHistoryEntry> TakeNews(int accountId)
        {
            var r = (RedisResult[])_db.ScriptEvaluate(DrainScript, new { newsKey = (RedisKey)NewsKey(accountId) });
            return r.Select(v => Parse<MarketHistoryEntry>((string)v)).Where(e => e != null).ToList();
        }

        public long ListingCount(int accountId) => _db.SetLength(SellerKey(accountId));

        // The sellers' sets for listings made before they existed (once, as a server starts).
        public void IndexSellers(IEnumerable<DbMarketData> listings)
        {
            var batch = _db.CreateBatch();
            foreach (var l in listings)
                _ = batch.SetAddAsync(SellerKey(l.SellerId), l.Id);
            batch.Execute();
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch (JsonException) { return null; }
        }
    }

    public sealed class MarketBoxItem
    {
        [JsonProperty("boxId")] public int BoxId;
        [JsonProperty("itemType")] public ushort ItemType;
        [JsonProperty("itemData")] public string ItemData;
        // bought, cancelled or expired
        [JsonProperty("why")] public string Why;
        [JsonProperty("time")] public int Time;
    }

    public sealed class MarketHistoryEntry
    {
        // listed, sold, bought, cancelled or expired
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("itemType")] public ushort ItemType;
        [JsonProperty("itemData")] public string ItemData;
        [JsonProperty("price")] public int Price;
        // sold: what the seller got after the tax
        [JsonProperty("net")] public int Net;
        // sold: the buyer; bought: the seller
        [JsonProperty("other")] public string Other;
        [JsonProperty("time")] public int Time;
    }
}
