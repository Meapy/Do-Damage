using Shared.resources;
using Xunit;

namespace Tests
{
    public class FameRatesTests
    {
        [Fact]
        public void LevellingExperienceIsKept()
        {
            Assert.Equal(1200f, FameRates.Experience(1200f, 1, 0.5));
            Assert.Equal(1200f, FameRates.Experience(1200f, 19, 0.5));
        }

        [Fact]
        public void FameExperienceIsCutInTheArenas()
        {
            Assert.Equal(660f, FameRates.Experience(1200f, 20, 0.5), 2);
            Assert.Equal(990f, FameRates.Experience(1200f, 20, 0.75), 2);
            Assert.Equal(1320f, FameRates.Experience(1200f, 20, 1), 2);
        }

        [Fact]
        public void BossesGiveTheirWholeExperienceTwiceOverUpToTheMost()
        {
            Assert.Equal(15000f, FameRates.Boss(7500f));     // Oryx 2: 15 fame
            Assert.Equal(3200f, FameRates.Boss(1600f));      // the Cube God
            Assert.Equal(50000f, FameRates.Boss(75000f));    // Oryx 3's 750,000 health: the most
        }

        [Fact]
        public void WaveFameIsHalvedRoundingUp()
        {
            Assert.Equal(5, FameRates.Fame(10, 0.5));    // wave 1, alone
            Assert.Equal(3, FameRates.Fame(5, 0.5));     // wave 1, in a group
            Assert.Equal(125, FameRates.Fame(250, 0.5)); // wave 25, alone
            Assert.Equal(250, FameRates.Fame(250, 1));
        }

        [Fact]
        public void FameRateIsReadFromTheWorldXml()
        {
            var arena = new WorldResource(System.Xml.Linq.XElement.Parse(
                "<World id=\"Admin Arena\"><Width>8</Width><Height>8</Height><FameRate>0.5</FameRate><KillFameRate>0.75</KillFameRate></World>"));
            var other = new WorldResource(System.Xml.Linq.XElement.Parse(
                "<World id=\"Tomb\"><Width>8</Width><Height>8</Height></World>"));
            Assert.Equal(0.5, arena.FameRate);
            Assert.Equal(0.75, arena.KillFameRate);
            Assert.Equal(1, other.FameRate);
            Assert.Equal(1, other.KillFameRate);
        }
    }
}
