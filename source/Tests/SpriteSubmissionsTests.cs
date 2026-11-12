using System.Linq;
using Shared.database;
using Shared.database.account;
using StackExchange.Redis;
using Xunit;

namespace Tests
{
    public class SpriteSubmissionsTests : IClassFixture<MarketRedis>
    {
        private readonly MarketRedis _redis;

        public SpriteSubmissionsTests(MarketRedis redis)
        {
            _redis = redis;
        }

        private static string Sprite(int size, string pixel = "ff8800ff") =>
            string.Concat(Enumerable.Repeat("00000000", size * size - 1)) + pixel;

        private DbAccount Account(int id, string name, bool guest = false, int rank = 0, bool banned = false)
        {
            var key = "account." + id;
            _redis.Db.KeyDelete(key);
            // flags are stored as a byte, and left out when off
            var on = new byte[] { 1 };
            _redis.Db.HashSet(key, new[] { new HashEntry("name", name), new HashEntry("rank", rank) });
            if (guest)
                _redis.Db.HashSet(key, "guest", on);
            if (banned)
            {
                // for good (a ban with an end in the past is lifted on loading)
                _redis.Db.HashSet(key, "banned", on);
                _redis.Db.HashSet(key, "banLiftTime", -1);
            }
            return new DbAccount(_redis.Db, id);
        }

        private void Fresh() => _redis.Conn.GetServer(_redis.Conn.GetEndPoints()[0]).FlushDatabase();

        [Fact]
        public void PixelsAreSolidOrEmptyAndTheRightSize()
        {
            Assert.Equal(Sprite(8), SpriteSubmissions.CleanPixels(Sprite(8), 8));
            Assert.Equal(Sprite(16), SpriteSubmissions.CleanPixels(Sprite(16).ToUpperInvariant(), 16));
            // half see-through or more is empty, the rest solid
            Assert.Equal(Sprite(8, "ff8800ff"), SpriteSubmissions.CleanPixels(Sprite(8, "ff8800c0"), 8));
            Assert.Null(SpriteSubmissions.CleanPixels(Sprite(8, "ff88007f"), 8));
            Assert.Null(SpriteSubmissions.CleanPixels(Sprite(8), 16));
            Assert.Null(SpriteSubmissions.CleanPixels(Sprite(8), 12));
            Assert.Null(SpriteSubmissions.CleanPixels(Sprite(8, "ff8800zz"), 8));
            Assert.Null(SpriteSubmissions.CleanPixels(null, 8));
            Assert.Null(SpriteSubmissions.CleanPixels(Sprite(32), 32));
        }

        [Fact]
        public void TextIsOneShortLineWithoutMarkup()
        {
            Assert.Equal("Sword of Ages", SpriteSubmissions.CleanText("  Sword\nof   Ages ", 32));
            Assert.Equal("ab", SpriteSubmissions.CleanText("<a>&b", 32));
            Assert.Equal(32, SpriteSubmissions.CleanText(new string('x', 100), 32).Length);
            Assert.Equal("ring", SpriteSubmissions.CleanSlot(" Ring "));
            Assert.Null(SpriteSubmissions.CleanSlot("hat"));
        }

        [Fact]
        public void TheCatalogNamesWhatWasReleased()
        {
            Assert.Equal(new[] { 3, 7 }, SpriteSubmissions.ReleasedIds("{\"sprites\":[{\"id\":3},{\"id\":7,\"item\":\"x\"}]}").OrderBy(i => i));
            Assert.Empty(SpriteSubmissions.ReleasedIds("{\"sprites\":[]}"));
            Assert.Empty(SpriteSubmissions.ReleasedIds(""));
        }

        [Fact]
        public void SentReviewedAndListed()
        {
            Fresh();
            var player = Account(10, "Painter");
            var admin = Account(11, "Boss", rank: 6);
            Assert.True(SpriteSubmissions.IsAdmin(admin));
            Assert.False(SpriteSubmissions.IsAdmin(player));

            Assert.Equal(SpriteSubmissions.SubmitStatus.OK,
                SpriteSubmissions.Submit(_redis.Db, player, "1.2.3.4", "Potion of Maxy", null, null, "shinier", 8, Sprite(8), out var a));
            Assert.Equal(SpriteSubmissions.SubmitStatus.TooSoon,
                SpriteSubmissions.Submit(_redis.Db, player, "1.2.3.4", "Potion of Maxy", null, null, "", 8, Sprite(8), out _));
            _redis.Db.KeyDelete("sprites.wait.10");
            Assert.Equal(SpriteSubmissions.SubmitStatus.NoName,
                SpriteSubmissions.Submit(_redis.Db, player, "1.2.3.4", null, "  ", "ring", "", 16, Sprite(16), out _));
            Assert.Equal(SpriteSubmissions.SubmitStatus.BadSlot,
                SpriteSubmissions.Submit(_redis.Db, player, "1.2.3.4", null, "Crown", "hat", "", 16, Sprite(16), out _));
            Assert.Equal(SpriteSubmissions.SubmitStatus.OK,
                SpriteSubmissions.Submit(_redis.Db, player, "1.2.3.4", null, "Crown", "ring", "", 16, Sprite(16), out var b));

            var mine = SpriteSubmissions.Mine(_redis.Db, 10);
            Assert.Equal(new[] { b.Id, a.Id }, mine.Select(s => s.Id));
            Assert.Equal("Painter", mine[1].Author);
            Assert.Equal(new[] { a.Id, b.Id }, SpriteSubmissions.PendingList(_redis.Db).Select(s => s.Id));

            Assert.True(SpriteSubmissions.Review(_redis.Db, admin, a.Id, SpriteSubmissions.Approved, ""));
            Assert.True(SpriteSubmissions.Review(_redis.Db, admin, b.Id, SpriteSubmissions.Rejected, "Too big"));
            Assert.Equal(new[] { a.Id }, SpriteSubmissions.ApprovedList(_redis.Db).Select(s => s.Id));
            Assert.Empty(SpriteSubmissions.PendingList(_redis.Db));
            var rejected = SpriteSubmissions.Get(_redis.Db, b.Id);
            Assert.Equal("Too big", rejected.Review);
            Assert.Equal("Boss", rejected.ReviewedBy);
            Assert.True(_redis.Db.KeyTimeToLive("sprites." + b.Id) > System.TimeSpan.FromDays(29));
            Assert.Null(_redis.Db.KeyTimeToLive("sprites." + a.Id));

            // a second approved look for the same item replaces the first
            _redis.Db.KeyDelete("sprites.wait.10");
            SpriteSubmissions.Submit(_redis.Db, player, "1.2.3.4", "Potion of Maxy", null, null, "", 8, Sprite(8, "112233ff"), out var c);
            Assert.True(SpriteSubmissions.Review(_redis.Db, admin, c.Id, SpriteSubmissions.Approved, ""));
            Assert.Equal(new[] { c.Id }, SpriteSubmissions.ApprovedList(_redis.Db).Select(s => s.Id));
            Assert.Equal(SpriteSubmissions.Rejected, SpriteSubmissions.Get(_redis.Db, a.Id).Status);
        }

        [Fact]
        public void OnlyTheAuthorWithdrawsAndOnlyWhileWaiting()
        {
            Fresh();
            var player = Account(20, "Painter");
            var other = Account(21, "Other");
            SpriteSubmissions.Submit(_redis.Db, player, "", null, "Crown", "ring", "", 8, Sprite(8), out var s);
            Assert.False(SpriteSubmissions.Withdraw(_redis.Db, other, s.Id));
            Assert.True(SpriteSubmissions.Withdraw(_redis.Db, player, s.Id));
            Assert.False(SpriteSubmissions.Withdraw(_redis.Db, player, s.Id));
            Assert.Equal(SpriteSubmissions.Withdrawn, SpriteSubmissions.Get(_redis.Db, s.Id).Status);
            Assert.Empty(SpriteSubmissions.PendingList(_redis.Db));
        }

        [Fact]
        public void GuestsBannedAccountsAndFloodsAreTurnedAway()
        {
            Fresh();
            Assert.Equal(SpriteSubmissions.SubmitStatus.Guest,
                SpriteSubmissions.Submit(_redis.Db, Account(30, "Guest1", guest: true), "", null, "Crown", "ring", "", 8, Sprite(8), out _));
            Assert.Equal(SpriteSubmissions.SubmitStatus.Banned,
                SpriteSubmissions.Submit(_redis.Db, Account(31, "Bad", banned: true), "", null, "Crown", "ring", "", 8, Sprite(8), out _));
            Assert.Equal(SpriteSubmissions.SubmitStatus.BadSprite,
                SpriteSubmissions.Submit(_redis.Db, Account(32, "Big"), "", null, "Crown", "ring", "", 16, Sprite(16) + "00", out _));
            // nothing was stored for any of them
            Assert.Empty(SpriteSubmissions.PendingList(_redis.Db));

            // five waiting is the most one account may have
            var busy = Account(33, "Busy");
            for (var i = 0; i < SpriteSubmissions.MaxPendingPerAccount; i++)
            {
                _redis.Db.KeyDelete("sprites.wait.33");
                Assert.Equal(SpriteSubmissions.SubmitStatus.OK,
                    SpriteSubmissions.Submit(_redis.Db, busy, "", null, "Crown " + i, "ring", "", 8, Sprite(8), out _));
            }
            _redis.Db.KeyDelete("sprites.wait.33");
            Assert.Equal(SpriteSubmissions.SubmitStatus.TooManyPending,
                SpriteSubmissions.Submit(_redis.Db, busy, "", null, "One more", "ring", "", 8, Sprite(8), out _));

            // one address sends at most MaxPerIpHour in an hour, whatever the accounts
            Fresh();
            var sent = 0;
            for (var i = 0; i < SpriteSubmissions.MaxPerIpHour + 3; i++)
            {
                var status = SpriteSubmissions.Submit(_redis.Db, Account(100 + i, "Alt" + i), "5.6.7.8", null, "Crown", "ring", "", 8, Sprite(8), out _);
                if (status == SpriteSubmissions.SubmitStatus.OK)
                    sent++;
                else
                    Assert.Equal(SpriteSubmissions.SubmitStatus.TooMany, status);
            }
            Assert.Equal(SpriteSubmissions.MaxPerIpHour, sent);
        }
    }
}
