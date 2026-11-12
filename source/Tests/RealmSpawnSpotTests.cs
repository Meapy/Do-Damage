using System;
using WorldServer.core.worlds;
using Xunit;

namespace Tests
{
    // A realm's monsters come back on a free spot of their terrain, out of every player's sight. In
    // Realm 2 a small patch of the starter land's terrain (994 LowForest tiles, say) can be all in
    // sight of the players by the spawn, and the search for a spot, which had no end, held the
    // realm's thread for good: everyone in the realm froze.
    public class RealmSpawnSpotTests
    {
        [Fact]
        public void NoFittingTileEndsTheSearch()
        {
            var looks = 0;
            var spot = RealmManager.FreeSpot(1024, 768, (x, y) => { looks++; return false; }, new Random(1));
            Assert.Null(spot);
            Assert.Equal(RealmManager.SpawnTries, looks);
        }

        [Fact]
        public void FindsAFittingTile()
        {
            // a tenth of the map fits: a spot turns up, and it's one that fits
            static bool Fits(int x, int y) => x % 10 == 0;
            var spot = RealmManager.FreeSpot(1024, 768, Fits, new Random(2));
            Assert.NotNull(spot);
            Assert.True(Fits(spot.Value.X, spot.Value.Y));
            Assert.InRange(spot.Value.X, 0, 1023);
            Assert.InRange(spot.Value.Y, 0, 767);
        }
    }
}
