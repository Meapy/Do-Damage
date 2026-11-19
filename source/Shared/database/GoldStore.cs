using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Shared.database
{
    /// <summary>A gold pack the store sells: how much gold, at what price (in the store's currency).</summary>
    public sealed class GoldPack
    {
        public string id { get; set; } = "";
        public int gold { get; set; }
        // shown as "+N bonus" beside the gold; included in `gold`
        public int bonus { get; set; }
        public decimal price { get; set; }
        // the pack the page marks as the best value
        public bool best { get; set; }

        /// <summary>The price as PayPal takes it: two decimals, a dot, no thousands separator.</summary>
        public string PriceText => price.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>An order: one pack bought by one account, through one PayPal order.</summary>
    public sealed class GoldOrder
    {
        public string OrderId;
        public int AccountId;
        public string Pack;
        public int Gold;
        public string Price;
        public string Currency;
        public string Status;      // GoldStore.Created, Pending, Credited, Refunded
        public DateTime Created;
        public DateTime? Credited;
        public string CaptureId;
        public string Payer;       // the buyer's PayPal email, for the owner's records
        public int TakenBack;      // gold taken back by a refund
    }

    /// <summary>
    /// The gold store's Redis side (App/Controllers/GoldController.cs sells, App/PayPal.cs talks to
    /// PayPal). Every change that pays or takes gold is one Lua script, so an order is credited
    /// once only, however many of the page's capture, PayPal's webhook and a retry arrive together.
    ///
    ///   gold.order.&lt;paypal order id&gt;  hash: the order (GoldOrder). Created ones expire after a day;
    ///                                one that's paid is kept.
    ///   gold.orders.&lt;acc&gt;            list of the account's order ids, newest first, the last 50
    ///   gold.capture.&lt;capture id&gt;     the order id of a PayPal capture (refunds name the capture)
    ///   gold.log                     list of JSON lines, newest first, the last 1000: every
    ///                                payment and refund, for the owner
    ///   gold.sales                   hash: gold, orders and per-currency totals sold
    ///   gold.session.&lt;token&gt;         the account a store page signed in as (an hour)
    ///   gold.made.&lt;acc&gt;              orders the account made this hour (the limit)
    /// </summary>
    public static class GoldStore
    {
        public const string Created = "created";
        public const string Pending = "pending";
        public const string Credited = "credited";
        public const string Refunded = "refunded";

        public const string LogKey = "gold.log";
        public const string SalesKey = "gold.sales";
        public const int HistoryLength = 50;
        public const int LogLength = 1000;
        public const int OrdersPerHour = 12;
        public static readonly TimeSpan SessionFor = TimeSpan.FromHours(1);
        public static readonly TimeSpan UnpaidFor = TimeSpan.FromDays(1);

        public static string OrderKey(string orderId) => "gold.order." + orderId;
        public static string OrdersKey(int accountId) => "gold.orders." + accountId;
        public static string CaptureKey(string captureId) => "gold.capture." + captureId;
        public static string SessionKey(string token) => "gold.session." + token;
        public static string MadeKey(int accountId) => "gold.made." + accountId;
        private static string AccountKey(int accountId) => "account." + accountId;

        /// <summary>Gold per dollar (or whatever the store's currency is) before any bonus.</summary>
        public const int GoldPerUnit = 100;

        /// <summary>
        /// The packs sold when the settings name none: $1 buys 100 gold, and the bigger packs add
        /// bonus gold on top, 5% at $5 up to 25% at $100.
        /// </summary>
        public static readonly GoldPack[] DefaultPacks =
        {
            Pack("pouch", 1, 0),
            Pack("sack", 5, 5),
            Pack("chest", 10, 10, best: true),
            Pack("hoard", 20, 15),
            Pack("vault", 50, 20),
            Pack("treasury", 100, 25),
        };

        /// <summary>A pack at the base rate for a whole price, with a bonus of that many percent.</summary>
        public static GoldPack Pack(string id, int price, int bonusPercent, bool best = false)
        {
            var bonus = price * GoldPerUnit * bonusPercent / 100;
            return new GoldPack { id = id, gold = price * GoldPerUnit + bonus, bonus = bonus, price = price, best = best };
        }

        /// <summary>
        /// The packs to sell: the configured ones that make sense (an id of letters, digits and
        /// dashes, 1 to 1,000,000 gold, a price from 0.50 to 1,000 with at most two decimals, no id
        /// twice), else the defaults.
        /// </summary>
        public static GoldPack[] Packs(GoldPack[] configured)
        {
            var ok = (configured ?? new GoldPack[0])
                .Where(p => p != null && ValidId(p.id) && p.gold > 0 && p.gold <= 1_000_000
                            && p.bonus >= 0 && p.bonus < p.gold
                            && p.price >= 0.50m && p.price <= 1000m && decimal.Round(p.price, 2) == p.price)
                .GroupBy(p => p.id).Select(g => g.First())
                .ToArray();
            return ok.Length > 0 ? ok : DefaultPacks;
        }

        private static bool ValidId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 24 && id.All(c => c == '-' || (c < 128 && char.IsLetterOrDigit(c)));

        /// <summary>A PayPal order id as PayPal makes them: letters and digits, 10 to 40 of them.</summary>
        public static bool ValidOrderId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length >= 10 && id.Length <= 40 && id.All(c => c < 128 && char.IsLetterOrDigit(c));

        // --- store sessions -----------------------------------------------------------------

        /// <summary>A new store session for the account: a token the page sends instead of the password.</summary>
        public static string NewSession(IDatabase db, int accountId)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            db.StringSet(SessionKey(token), accountId, SessionFor);
            return token;
        }

        /// <summary>The account of a store session, its hour starting again; null: none (or expired).</summary>
        public static int? SessionAccount(IDatabase db, string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length != 48 || !token.All(Uri.IsHexDigit))
                return null;
            var id = (int?)db.StringGet(SessionKey(token));
            if (id != null)
                db.KeyExpire(SessionKey(token), SessionFor, CommandFlags.FireAndForget);
            return id;
        }

        public static void EndSession(IDatabase db, string token)
        {
            if (!string.IsNullOrEmpty(token) && token.Length == 48)
                db.KeyDelete(SessionKey(token));
        }

        // --- orders -------------------------------------------------------------------------

        /// <summary>
        /// Counts an order the account is about to make; false: it made OrdersPerHour this hour
        /// already (each unpaid one is an order at PayPal, so a script can't make thousands).
        /// </summary>
        public static bool MayMakeOrder(IDatabase db, int accountId)
        {
            var n = db.StringIncrement(MadeKey(accountId));
            if (n == 1)
                db.KeyExpire(MadeKey(accountId), TimeSpan.FromHours(1));
            return n <= OrdersPerHour;
        }

        /// <summary>Records an order PayPal just made for the account, unpaid.</summary>
        public static void AddOrder(IDatabase db, string orderId, int accountId, GoldPack pack, string currency, DateTime now)
        {
            var key = OrderKey(orderId);
            var tran = db.CreateTransaction();
            _ = tran.HashSetAsync(key, new[]
            {
                new HashEntry("account", accountId),
                new HashEntry("pack", pack.id),
                new HashEntry("gold", pack.gold),
                new HashEntry("price", pack.PriceText),
                new HashEntry("currency", currency),
                new HashEntry("status", Created),
                new HashEntry("created", now.ToString("o")),
            });
            _ = tran.KeyExpireAsync(key, UnpaidFor);
            _ = tran.ListLeftPushAsync(OrdersKey(accountId), orderId);
            _ = tran.ListTrimAsync(OrdersKey(accountId), 0, HistoryLength - 1);
            tran.Execute();
        }

        public static GoldOrder GetOrder(IDatabase db, string orderId)
        {
            if (!ValidOrderId(orderId))
                return null;
            var h = db.HashGetAll(OrderKey(orderId)).ToDictionary(e => (string)e.Name, e => (string)e.Value);
            if (h.Count == 0 || !h.ContainsKey("account"))
                return null;
            DateTime? Date(string k) => h.TryGetValue(k, out var v) && DateTime.TryParse(v, null, DateTimeStyles.RoundtripKind, out var d) ? d : null;
            return new GoldOrder
            {
                OrderId = orderId,
                AccountId = int.Parse(h["account"], CultureInfo.InvariantCulture),
                Pack = h.GetValueOrDefault("pack", ""),
                Gold = int.TryParse(h.GetValueOrDefault("gold"), out var g) ? g : 0,
                Price = h.GetValueOrDefault("price", ""),
                Currency = h.GetValueOrDefault("currency", ""),
                Status = h.GetValueOrDefault("status", Created),
                Created = Date("created") ?? DateTime.MinValue,
                Credited = Date("credited"),
                CaptureId = h.GetValueOrDefault("capture"),
                Payer = h.GetValueOrDefault("payer"),
                TakenBack = int.TryParse(h.GetValueOrDefault("takenBack"), out var t) ? t : 0,
            };
        }

        /// <summary>The account's recent orders that went through (paid, waiting or refunded), newest first.</summary>
        public static List<GoldOrder> History(IDatabase db, int accountId, int count = 10)
        {
            var ids = db.ListRange(OrdersKey(accountId), 0, HistoryLength - 1);
            var list = new List<GoldOrder>();
            foreach (var id in ids)
            {
                var o = GetOrder(db, id);
                if (o != null && o.AccountId == accountId && o.Status != Created)
                    list.Add(o);
                if (list.Count >= count)
                    break;
            }
            return list;
        }

        /// <summary>The capture is waiting at PayPal (an eCheck, a review): credited when it completes.</summary>
        public static bool MarkPending(IDatabase db, string orderId, string captureId)
        {
            const string script = @"
                if redis.call('HGET', KEYS[1], 'status') ~= 'created' then return 0 end
                redis.call('HSET', KEYS[1], 'status', 'pending')
                if ARGV[1] ~= '' then
                    redis.call('HSET', KEYS[1], 'capture', ARGV[1])
                    redis.call('SET', KEYS[2], ARGV[2])
                end
                redis.call('PERSIST', KEYS[1])
                return 1";
            return (int)db.ScriptEvaluate(script,
                new RedisKey[] { OrderKey(orderId), CaptureKey(captureId ?? "") },
                new RedisValue[] { captureId ?? "", orderId }) == 1;
        }

        public enum CreditResult { Credited, AlreadyCredited, NoOrder, NoAccount, Refunded }

        /// <summary>
        /// Pays the order's gold into its account, once: the order goes from created or pending to
        /// credited and the account's gold and total gold rise, together. The order is kept for
        /// good, its capture indexed for refunds, and the payment logged. balance: the account's
        /// gold after (or now, when it was credited before).
        /// </summary>
        public static CreditResult Credit(IDatabase db, string orderId, string captureId, string payer, DateTime now, out int balance)
        {
            const string script = @"
                local status = redis.call('HGET', KEYS[1], 'status')
                if not status then return {-1, 0} end
                local account = 'account.' .. redis.call('HGET', KEYS[1], 'account')
                if status == 'credited' then return {1, tonumber(redis.call('HGET', account, 'credits') or '0')} end
                if status == 'refunded' then return {3, tonumber(redis.call('HGET', account, 'credits') or '0')} end
                if redis.call('EXISTS', account) == 0 then return {-2, 0} end
                local gold = tonumber(redis.call('HGET', KEYS[1], 'gold'))
                redis.call('HSET', KEYS[1], 'status', 'credited')
                redis.call('HSET', KEYS[1], 'credited', ARGV[3])
                if ARGV[1] ~= '' then
                    redis.call('HSET', KEYS[1], 'capture', ARGV[1])
                    redis.call('SET', 'gold.capture.' .. ARGV[1], ARGV[4])
                end
                if ARGV[2] ~= '' then redis.call('HSET', KEYS[1], 'payer', ARGV[2]) end
                redis.call('PERSIST', KEYS[1])
                redis.call('HINCRBY', account, 'totalCredits', gold)
                local balance = redis.call('HINCRBY', account, 'credits', gold)
                local currency = redis.call('HGET', KEYS[1], 'currency')
                local price = redis.call('HGET', KEYS[1], 'price')
                redis.call('HINCRBY', KEYS[2], 'gold', gold)
                redis.call('HINCRBY', KEYS[2], 'orders', 1)
                redis.call('HINCRBY', KEYS[2], 'cents.' .. currency, math.floor(tonumber(price) * 100 + 0.5))
                redis.call('LPUSH', KEYS[3], cjson.encode({kind = 'paid', order = ARGV[4], account = tonumber(redis.call('HGET', KEYS[1], 'account')),
                    gold = gold, price = price, currency = currency, capture = ARGV[1], payer = ARGV[2], at = ARGV[3]}))
                redis.call('LTRIM', KEYS[3], 0, tonumber(ARGV[5]) - 1)
                return {0, balance}";
            var r = (RedisResult[])db.ScriptEvaluate(script,
                new RedisKey[] { OrderKey(orderId), SalesKey, LogKey },
                new RedisValue[] { captureId ?? "", payer ?? "", now.ToString("o"), orderId, LogLength });
            balance = (int)r[1];
            return (int)r[0] switch
            {
                0 => CreditResult.Credited,
                1 => CreditResult.AlreadyCredited,
                3 => CreditResult.Refunded,
                -2 => CreditResult.NoAccount,
                _ => CreditResult.NoOrder,
            };
        }

        /// <summary>
        /// A credited order was refunded or charged back: its gold is taken back, as much as the
        /// account still has (what it spent stays spent, and the log says how much was missing),
        /// once. The order's id, else null when the capture isn't one of ours or was taken back
        /// before. taken: the gold taken back.
        /// </summary>
        public static string TakeBack(IDatabase db, string captureId, string why, DateTime now, out int taken, out int accountId)
        {
            taken = 0;
            accountId = 0;
            var orderId = (string)db.StringGet(CaptureKey(captureId ?? ""));
            if (orderId == null)
                return null;
            const string script = @"
                if redis.call('HGET', KEYS[1], 'status') ~= 'credited' then return {-1, 0, 0} end
                local id = redis.call('HGET', KEYS[1], 'account')
                local account = 'account.' .. id
                local gold = tonumber(redis.call('HGET', KEYS[1], 'gold'))
                local have = tonumber(redis.call('HGET', account, 'credits') or '0')
                local take = math.min(gold, math.max(have, 0))
                if take > 0 then redis.call('HINCRBY', account, 'credits', -take) end
                redis.call('HSET', KEYS[1], 'status', 'refunded')
                redis.call('HSET', KEYS[1], 'takenBack', take)
                redis.call('HINCRBY', KEYS[2], 'refunds', 1)
                redis.call('LPUSH', KEYS[3], cjson.encode({kind = ARGV[1], order = ARGV[3], account = tonumber(id), gold = gold, takenBack = take,
                    missing = gold - take, capture = ARGV[4], at = ARGV[2]}))
                redis.call('LTRIM', KEYS[3], 0, tonumber(ARGV[5]) - 1)
                return {0, take, tonumber(id)}";
            var r = (RedisResult[])db.ScriptEvaluate(script,
                new RedisKey[] { OrderKey(orderId), SalesKey, LogKey },
                new RedisValue[] { why ?? "refunded", now.ToString("o"), orderId, captureId, LogLength });
            if ((int)r[0] != 0)
                return null;
            taken = (int)r[1];
            accountId = (int)r[2];
            return orderId;
        }

        /// <summary>The account's gold now.</summary>
        public static int Balance(IDatabase db, int accountId) => (int?)db.HashGet(AccountKey(accountId), "credits") ?? 0;

        /// <summary>The newest entries of the payments log (JSON), for /goldlog and the owner.</summary>
        public static List<Dictionary<string, object>> Log(IDatabase db, int count) =>
            db.ListRange(LogKey, 0, count - 1)
              .Select(v => JsonConvert.DeserializeObject<Dictionary<string, object>>(v))
              .Where(d => d != null)
              .ToList();
    }
}
