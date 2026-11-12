using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shared.database.account;

namespace Shared.database
{
    /// <summary>
    /// Sprites players draw in the client's Sprite Editor and send in: a new look for an item
    /// ("item", with the item's id), or the art for an item still to be made ("new", with its
    /// name and kind). Admins approve or reject them in the editor's Review tab; approved ones are
    /// taken into the repository by tools/community_sprites.py, on the release branch, and reach
    /// players with the next release. The catalog that tool writes (resources/data/
    /// community_sprites.json) ships with the servers, so a submission it lists shows as in the
    /// game ("released") once that release is deployed.
    ///
    /// In Redis: the hash sprites.&lt;id&gt; for each, the counter sprites.nextId, the sorted
    /// sets sprites.pending and sprites.approved (ids by the time they were sent or approved),
    /// the list sprites.by.&lt;account&gt; (an account's own, newest first) and, for a few
    /// seconds after each, sprites.wait.&lt;account&gt;.
    /// </summary>
    public static class SpriteSubmissions
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        public const string Released = "released";
        public const string Withdrawn = "withdrawn";

        public const string KindItem = "item";
        public const string KindNew = "new";

        /// <summary>What a new item can be: its kind, as the editor offers them.</summary>
        public static readonly string[] Slots = { "weapon", "ability", "armor", "ring", "consumable", "other" };

        /// <summary>The sizes a sprite can be drawn at, in pixels square.</summary>
        public static readonly int[] Sizes = { 8, 16 };

        /// <summary>Waiting for review at once, per account.</summary>
        public const int MaxPendingPerAccount = 5;
        /// <summary>Waiting for review at once, from everyone: a full queue takes no more.</summary>
        public const int MaxPendingTotal = 300;
        /// <summary>Between two submissions from one account.</summary>
        public static readonly TimeSpan SubmitEvery = TimeSpan.FromSeconds(20);
        /// <summary>An account's own, kept in its list.</summary>
        public const int KeepPerAccount = 30;
        /// <summary>Sent in a day by one account, an hour from one address, and a day by everyone.</summary>
        public const int MaxPerAccountDay = 15;
        public const int MaxPerIpHour = 10;
        public const int MaxPerDay = 400;
        /// <summary>
        /// A rejected, withdrawn or released one is forgotten this long after (its art is in the
        /// repository by then, or wasn't wanted), so Redis doesn't keep every sprite ever sent.
        /// </summary>
        public static readonly TimeSpan KeepDone = TimeSpan.FromDays(30);
        /// <summary>Approved ones listed at once (the admins' queue and the public list).</summary>
        public const int MaxApprovedListed = 200;

        public const int MaxName = 32;
        public const int MaxNote = 200;
        /// <summary>Longest a sprite's text can be: 16 × 16 pixels of 8 hex digits.</summary>
        public const int MaxPixelsLength = 16 * 16 * 8;

        private const string NextId = "sprites.nextId";
        private const string PendingSet = "sprites.pending";
        private const string ApprovedSet = "sprites.approved";

        private static string Key(int id) => "sprites." + id;
        private static string ByAccount(int accountId) => "sprites.by." + accountId;

        public static bool IsAdmin(DbAccount acc) => acc != null && (acc.Admin || acc.Rank >= (int)RankingType.Admin);

        /// <summary>
        /// A sprite as sent: size × size pixels, each 8 hex digits (RRGGBBAA), row by row. Kept
        /// as the game draws its sprites, each pixel either solid or empty (alpha under half: an
        /// empty 00000000), in lower case. Null when it isn't one, or nothing is drawn.
        /// </summary>
        public static string CleanPixels(string pixels, int size)
        {
            if (pixels == null || !Sizes.Contains(size) || pixels.Length != size * size * 8)
                return null;
            var sb = new StringBuilder(pixels.Length);
            var any = false;
            for (var i = 0; i < pixels.Length; i += 8)
            {
                var px = pixels.Substring(i, 8).ToLowerInvariant();
                if (!px.All(Uri.IsHexDigit))
                    return null;
                if (Convert.ToInt32(px.Substring(6, 2), 16) < 0x80)
                    sb.Append("00000000");
                else
                {
                    sb.Append(px, 0, 6).Append("ff");
                    any = true;
                }
            }
            return any ? sb.ToString() : null;
        }

        /// <summary>A name or note as kept and shown: one line, no markup characters, at most max long.</summary>
        public static string CleanText(string text, int max)
        {
            // line breaks and tabs become spaces; markup characters go
            text = new string((text ?? "").Select(c => char.IsControl(c) ? ' ' : c).Where(c => c != '<' && c != '>' && c != '&').ToArray()).Trim();
            while (text.Contains("  "))
                text = text.Replace("  ", " ");
            return text.Length > max ? text.Substring(0, max).TrimEnd() : text;
        }

        public static string CleanSlot(string slot)
        {
            slot = (slot ?? "").Trim().ToLowerInvariant();
            return Slots.Contains(slot) ? slot : null;
        }

        public sealed class Submission
        {
            public int Id;
            public int AccountId;
            public string Author = "";
            public string Kind = KindItem;
            /// <summary>The item's id ("item"), or "" ("new").</summary>
            public string Item = "";
            /// <summary>A new item's name and kind ("new").</summary>
            public string Name = "";
            public string Slot = "";
            /// <summary>What the player wrote about it.</summary>
            public string Note = "";
            public int Size;
            public string Pixels = "";
            public string Status = Pending;
            /// <summary>The admin's answer (why it was rejected), who reviewed it and when.</summary>
            public string Review = "";
            public string ReviewedBy = "";
            public long Created;
            public long Reviewed;

            internal HashEntry[] Entries() => new[]
            {
                new HashEntry("id", Id), new HashEntry("accountId", AccountId), new HashEntry("author", Author),
                new HashEntry("kind", Kind), new HashEntry("item", Item), new HashEntry("name", Name),
                new HashEntry("slot", Slot), new HashEntry("note", Note), new HashEntry("size", Size),
                new HashEntry("pixels", Pixels), new HashEntry("status", Status), new HashEntry("review", Review),
                new HashEntry("reviewedBy", ReviewedBy), new HashEntry("created", Created), new HashEntry("reviewed", Reviewed),
            };

            internal static Submission From(HashEntry[] entries)
            {
                if (entries == null || entries.Length == 0)
                    return null;
                var d = entries.ToDictionary(e => (string)e.Name, e => e.Value);
                string S(string k) => d.TryGetValue(k, out var v) && v.HasValue ? (string)v : "";
                long L(string k) => long.TryParse(S(k), out var v) ? v : 0;
                return new Submission
                {
                    Id = (int)L("id"), AccountId = (int)L("accountId"), Author = S("author"), Kind = S("kind"),
                    Item = S("item"), Name = S("name"), Slot = S("slot"), Note = S("note"), Size = (int)L("size"),
                    Pixels = S("pixels"), Status = S("status"), Review = S("review"), ReviewedBy = S("reviewedBy"),
                    Created = L("created"), Reviewed = L("reviewed"),
                };
            }
        }

        public enum SubmitStatus
        {
            OK,
            Guest,
            BadSprite,
            BadItem,
            NoName,
            BadSlot,
            TooSoon,
            TooManyPending,
            QueueFull,
            /// <summary>The account is banned.</summary>
            Banned,
            /// <summary>This account's day, this address's hour or everyone's day is used up.</summary>
            TooMany,
        }

        public static Submission Get(IDatabase db, int id) => Submission.From(db.HashGetAll(Key(id)));

        private static List<Submission> GetMany(IDatabase db, IEnumerable<int> ids)
        {
            var conn = db;
            var batch = ids.Select(id => conn.HashGetAllAsync(Key(id))).ToList();
            var all = new List<Submission>();
            foreach (var t in batch)
            {
                var s = Submission.From(conn.Wait(t));
                if (s != null)
                    all.Add(s);
            }
            return all;
        }

        /// <summary>
        /// Sends a sprite in for review. item: the item it's for (its id, checked by the caller
        /// against the game's items), or null for a new item named name, of kind slot.
        /// </summary>
        public static SubmitStatus Submit(IDatabase db, DbAccount acc, string ip, string item, string name, string slot, string note, int size, string pixels, out Submission made)
        {
            made = null;
            if (acc.Banned)
                return SubmitStatus.Banned;
            if (acc.Guest)
                return SubmitStatus.Guest;
            // everything that costs nothing is checked before Redis is asked anything
            if (pixels == null || pixels.Length > MaxPixelsLength)
                return SubmitStatus.BadSprite;
            pixels = CleanPixels(pixels, size);
            if (pixels == null)
                return SubmitStatus.BadSprite;
            var sub = new Submission
            {
                AccountId = acc.AccountId,
                Author = acc.Name ?? "",
                Size = size,
                Pixels = pixels,
                Note = CleanText(note, MaxNote),
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            if (item != null)
            {
                sub.Kind = KindItem;
                sub.Item = item;
            }
            else
            {
                sub.Kind = KindNew;
                sub.Name = CleanText(name, MaxName);
                if (sub.Name.Length == 0)
                    return SubmitStatus.NoName;
                sub.Slot = CleanSlot(slot);
                if (sub.Slot == null)
                    return SubmitStatus.BadSlot;
            }

            var conn = db;
            if (conn.SortedSetLength(PendingSet) >= MaxPendingTotal)
                return SubmitStatus.QueueFull;
            var mine = Mine(db, acc.AccountId);
            if (mine.Count(s => s.Status == Pending) >= MaxPendingPerAccount)
                return SubmitStatus.TooManyPending;
            if (!conn.StringSet("sprites.wait." + acc.AccountId, 1, SubmitEvery, When.NotExists))
                return SubmitStatus.TooSoon;
            // counted once it's otherwise good, so a mistake doesn't use one up
            var day = DateTime.UtcNow.ToString("yyyyMMdd");
            if (!Within(conn, "sprites.day." + acc.AccountId, MaxPerAccountDay, TimeSpan.FromDays(1))
                || (!string.IsNullOrEmpty(ip) && !Within(conn, "sprites.ip." + CleanText(ip, 64), MaxPerIpHour, TimeSpan.FromHours(1)))
                || !Within(conn, "sprites.all." + day, MaxPerDay, TimeSpan.FromDays(2)))
                return SubmitStatus.TooMany;

            sub.Id = (int)conn.StringIncrement(NextId);
            var tran = conn.CreateTransaction();
            _ = tran.HashSetAsync(Key(sub.Id), sub.Entries());
            _ = tran.SortedSetAddAsync(PendingSet, sub.Id, sub.Created);
            _ = tran.ListLeftPushAsync(ByAccount(acc.AccountId), sub.Id);
            _ = tran.ListTrimAsync(ByAccount(acc.AccountId), 0, KeepPerAccount - 1);
            tran.Execute();
            made = sub;
            return SubmitStatus.OK;
        }

        /// <summary>One more under key, which counts for window from its first: false once over max.</summary>
        private static bool Within(IDatabase conn, string key, int max, TimeSpan window)
        {
            var n = conn.StringIncrement(key);
            if (n == 1)
                conn.KeyExpire(key, window);
            return n <= max;
        }

        /// <summary>An account's own submissions, newest first.</summary>
        public static List<Submission> Mine(IDatabase db, int accountId)
        {
            var ids = db.ListRange(ByAccount(accountId), 0, KeepPerAccount - 1).Select(v => (int)v);
            return MarkReleased(db, GetMany(db, ids));
        }

        /// <summary>The author takes back one still waiting for review.</summary>
        public static bool Withdraw(IDatabase db, DbAccount acc, int id)
        {
            var sub = Get(db, id);
            if (sub == null || sub.AccountId != acc.AccountId || sub.Status != Pending)
                return false;
            var tran = db.CreateTransaction();
            tran.AddCondition(Condition.HashEqual(Key(id), "status", Pending));
            _ = tran.HashSetAsync(Key(id), "status", Withdrawn);
            _ = tran.SortedSetRemoveAsync(PendingSet, id);
            _ = tran.KeyExpireAsync(Key(id), KeepDone);
            return tran.Execute();
        }

        /// <summary>Waiting for review, oldest first.</summary>
        public static List<Submission> PendingList(IDatabase db, int count = 60) =>
            GetMany(db, db.SortedSetRangeByRank(PendingSet, 0, count - 1).Select(v => (int)v));

        /// <summary>Approved and not yet in a release, in the order they were approved.</summary>
        public static List<Submission> ApprovedList(IDatabase db, int count = MaxApprovedListed) =>
            MarkReleased(db, GetMany(db, db.SortedSetRangeByRank(ApprovedSet, 0, count - 1).Select(v => (int)v)))
                .Where(s => s.Status == Approved).ToList();

        /// <summary>
        /// An admin's verdict: Approved, Rejected (with why, in note) or Pending again. Approving
        /// a new look for an item turns down any other approved, unreleased one for that item,
        /// so only one goes in. False: no such submission, or it's withdrawn or released.
        /// </summary>
        public static bool Review(IDatabase db, DbAccount admin, int id, string verdict, string note)
        {
            var sub = Get(db, id);
            if (sub == null || sub.Status == Withdrawn || sub.Status == Released || !(verdict is Pending or Approved or Rejected))
                return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var conn = db;
            var tran = conn.CreateTransaction();
            tran.AddCondition(Condition.HashEqual(Key(id), "status", sub.Status));
            _ = tran.HashSetAsync(Key(id), new[]
            {
                new HashEntry("status", verdict),
                new HashEntry("review", verdict == Rejected ? CleanText(note, MaxNote) : ""),
                new HashEntry("reviewedBy", verdict == Pending ? "" : admin.Name ?? ""),
                new HashEntry("reviewed", verdict == Pending ? 0 : now),
            });
            if (verdict == Pending)
                _ = tran.SortedSetAddAsync(PendingSet, id, sub.Created);
            else
                _ = tran.SortedSetRemoveAsync(PendingSet, id);
            // a rejected one is forgotten in a while; one waiting or approved is kept
            if (verdict == Rejected)
                _ = tran.KeyExpireAsync(Key(id), KeepDone);
            else
                _ = tran.KeyPersistAsync(Key(id));
            if (verdict == Approved)
                _ = tran.SortedSetAddAsync(ApprovedSet, id, now);
            else
                _ = tran.SortedSetRemoveAsync(ApprovedSet, id);
            if (!tran.Execute())
                return false;

            if (verdict == Approved && sub.Kind == KindItem)
                foreach (var other in ApprovedList(db).Where(o => o.Id != id && o.Kind == KindItem && string.Equals(o.Item, sub.Item, StringComparison.OrdinalIgnoreCase)))
                    Review(db, admin, other.Id, Rejected, "Another sprite was chosen for this item.");
            return true;
        }

        // ---- what the release shipped -------------------------------------------------------

        private static readonly object CatalogLock = new object();
        private static string _catalogPath;
        private static DateTime _catalogTime;
        private static HashSet<int> _released = new HashSet<int>();

        /// <summary>Where the shipped catalog is: resources/data/community_sprites.json. Set at startup.</summary>
        public static void UseCatalog(string resourcePath)
        {
            lock (CatalogLock)
            {
                _catalogPath = Path.Combine(resourcePath, "data", "community_sprites.json");
                _catalogTime = default;
            }
        }

        /// <summary>The submission ids in a catalog's text (its "sprites", each with an "id").</summary>
        public static HashSet<int> ReleasedIds(string catalogJson)
        {
            var ids = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(catalogJson))
                return ids;
            var sprites = JObject.Parse(catalogJson)["sprites"] as JArray;
            if (sprites != null)
                foreach (var s in sprites)
                    if (s["id"] != null && int.TryParse((string)s["id"], out var id))
                        ids.Add(id);
            return ids;
        }

        /// <summary>The ids the deployed release has in it, read again when the file changes.</summary>
        public static HashSet<int> ReleasedNow()
        {
            lock (CatalogLock)
            {
                if (_catalogPath == null || !File.Exists(_catalogPath))
                    return _released;
                var time = File.GetLastWriteTimeUtc(_catalogPath);
                if (time != _catalogTime)
                {
                    try
                    {
                        _released = ReleasedIds(File.ReadAllText(_catalogPath));
                    }
                    catch (Exception)
                    {
                        // a broken catalog releases nothing; the old set stays
                    }
                    _catalogTime = time;
                }
                return _released;
            }
        }

        /// <summary>Approved ones the release has in it become released (in Redis too).</summary>
        private static List<Submission> MarkReleased(IDatabase db, List<Submission> subs)
        {
            var released = ReleasedNow();
            foreach (var s in subs.Where(s => s.Status == Approved && released.Contains(s.Id)))
            {
                s.Status = Released;
                db.HashSet(Key(s.Id), "status", Released);
                db.SortedSetRemove(ApprovedSet, s.Id);
                db.KeyExpire(Key(s.Id), KeepDone);
            }
            return subs;
        }
    }
}
