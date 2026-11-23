using System;
using Shared.crucible;
using StackExchange.Redis;
using Xunit;

namespace Tests
{
    // /resetcrucible's Redis side (Shared/crucible/CrucibleReset.cs), on a Redis of its own.
    public class CrucibleResetTests : IClassFixture<MarketRedis>
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc);
        private static readonly int Today = CrucibleRules.Day(Now);

        private readonly IDatabase _db;

        public CrucibleResetTests(MarketRedis redis)
        {
            redis.Conn.GetServer(redis.Conn.GetEndPoints()[0]).FlushDatabase();
            _db = redis.Db;
        }

        private void Account(int id, int? day, int? climbs)
        {
            _db.HashSet($"account.{id}", "name", "Player" + id);
            if (day != null)
                _db.HashSet($"account.{id}", "crucibleDay", day.Value);
            if (climbs != null)
                _db.HashSet($"account.{id}", "crucibleClimbs", climbs.Value);
        }

        private int Climbs(int id) => (int)_db.HashGet($"account.{id}", "crucibleClimbs");

        [Theory]
        [InlineData(100, 100, 1, true)]
        [InlineData(100, 100, 4, true)]
        [InlineData(100, 100, 0, false)]
        [InlineData(100, 99, 2, false)]
        [InlineData(100, 0, 0, false)]
        public void OnlyTodaysClimbsAreGivenBack(int today, int lastDay, int climbs, bool back) =>
            Assert.Equal(back, CrucibleReset.ClimbedToday(today, lastDay, climbs));

        [Fact]
        public void EveryoneWhoClimbedTodayGetsTheirClimbsBack()
        {
            Account(1, Today, 1);        // climbed today
            Account(2, Today, 2);        // Gilded, both climbs spent
            Account(3, Today - 1, 1);    // climbed yesterday: has today's already
            Account(4, null, null);      // never climbed
            // account 5 was deleted
            Account(6, Today, 1);
            _db.StringSet("nextAccId", 7);

            var reset = CrucibleReset.ResetAll(_db, Now);

            Assert.Equal(new[] { 1, 2, 6 }, reset);
            Assert.Equal(0, Climbs(1));
            Assert.Equal(0, Climbs(2));
            Assert.Equal(1, Climbs(3));
            Assert.False(_db.HashExists("account.4", "crucibleClimbs"));
            Assert.False(_db.KeyExists("account.5"));
            // the Gilded account may climb twice again
            Assert.Equal(2, CrucibleRules.ClimbsLeft(Today, (int)_db.HashGet("account.2", "crucibleDay"), Climbs(2), true, false));
        }

        [Fact]
        public void ResettingTwiceFindsNobodyTheSecondTime()
        {
            Account(1, Today, 1);
            _db.StringSet("nextAccId", 2);
            Assert.Single(CrucibleReset.ResetAll(_db, Now));
            Assert.Empty(CrucibleReset.ResetAll(_db, Now));
        }

        [Fact]
        public void ManyAccountsAreReadInBatches()
        {
            for (var id = 1; id <= 1203; id++)
                Account(id, id % 3 == 0 ? Today : Today - 1, 1);
            _db.StringSet("nextAccId", 1204);
            Assert.Equal(401, CrucibleReset.ResetAll(_db, Now).Count);
            Assert.Equal(0, Climbs(1203));
            Assert.Equal(1, Climbs(1202));
        }
    }
}
