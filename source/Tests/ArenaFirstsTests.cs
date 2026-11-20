using System.Linq;
using Shared.database.leaderboard;
using StackExchange.Redis;
using Xunit;

namespace Tests
{
    // Who glows hot pink: the #1 solo and #1 group runs of Arena Season 1 (ArenaFirsts), on a
    // Redis of its own.
    public class ArenaFirstsTests : IClassFixture<MarketRedis>
    {
        private readonly IDatabase _db;

        public ArenaFirstsTests(MarketRedis redis)
        {
            redis.Conn.GetServer(redis.Conn.GetEndPoints()[0]).FlushDatabase();
            _db = redis.Db;
        }

        [Fact]
        public void TheBestSoloAndGroupRunsOfSeasonOne()
        {
            var s1 = ArenaSeasons.Get(1);
            DbArenaRuns.Add(_db, s1, 30, 900, "Arena", new[] { "Solo" });
            DbArenaRuns.Add(_db, s1, 30, 950, "Arena", new[] { "Slower" });
            DbArenaRuns.Add(_db, s1, 41, 1500, "Arena", new[] { "Ann", "Bo" });
            DbArenaRuns.Add(_db, s1, 35, 1200, "Arena", new[] { "Cy", "Di" });
            // Season 2's best counts for nothing here
            DbArenaRuns.Add(_db, ArenaSeasons.Get(2), 60, 2000, "Arena", new[] { "Later" });

            Assert.Equal(new[] { "Ann", "Bo", "Solo" }, ArenaFirsts.Names(_db).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void NoRunsNoNames()
        {
            Assert.Empty(ArenaFirsts.Names(_db));
        }
    }
}
