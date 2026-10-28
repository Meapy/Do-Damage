using System.Linq;
using WorldServer.core.worlds.impl;
using Xunit;

namespace Tests
{
    // The Arena's votes between waves (ArenaPools, DoomedBattleArena.Vote)
    public class ArenaVotesTests
    {
        [Fact]
        public void NextNeedsHalfThePlayers()
        {
            Assert.Equal(1, ArenaPools.NextVotesNeeded(1));
            Assert.Equal(1, ArenaPools.NextVotesNeeded(2));
            Assert.Equal(2, ArenaPools.NextVotesNeeded(3));
            Assert.Equal(2, ArenaPools.NextVotesNeeded(4));
        }

        [Fact]
        public void CombiningNeedsMoreThanHalf()
        {
            Assert.Equal(1, ArenaPools.CombineVotesNeeded(1));
            Assert.Equal(2, ArenaPools.CombineVotesNeeded(2));
            Assert.Equal(2, ArenaPools.CombineVotesNeeded(3));
            Assert.Equal(3, ArenaPools.CombineVotesNeeded(4));
        }

        [Fact]
        public void TheMostWavesAMajorityBacks()
        {
            Assert.Equal(5, ArenaPools.CombineDecided(new[] { 5 }, 5));       // alone
            Assert.Equal(1, ArenaPools.CombineDecided(new[] { 5, 1 }, 5));    // one of two
            Assert.Equal(2, ArenaPools.CombineDecided(new[] { 5, 2, 1 }, 5)); // +5 backs +2
            Assert.Equal(5, ArenaPools.CombineDecided(new[] { 5, 5, 2 }, 5));
            Assert.Equal(3, ArenaPools.CombineDecided(new[] { 5, 5 }, 3));    // capped
        }

        [Fact]
        public void NoCombiningIntoTheOryxStage()
        {
            Assert.Equal(5, ArenaPools.MaxCombineAt(1));
            Assert.Equal(5, ArenaPools.MaxCombineAt(25));
            Assert.Equal(2, ArenaPools.MaxCombineAt(28));
            Assert.Equal(1, ArenaPools.MaxCombineAt(29));
            Assert.Equal(1, ArenaPools.MaxCombineAt(40));
        }

        [Fact]
        public void CombinedWavesPayWhatTheyWouldOneByOne()
        {
            // waves 2 to 6 paid together after a combined fight of waves 1-5, alone, at half fame
            var together = Enumerable.Range(2, 5).Sum(w => ArenaPools.WavePot(w, 1, ArenaPools.FameRate));
            Assert.Equal(100, together);
        }
    }
}
