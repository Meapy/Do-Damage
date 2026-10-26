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
        public void FameExperienceIsHalvedInTheArenas()
        {
            Assert.Equal(600f, FameRates.Experience(1200f, 20, 0.5));
            Assert.Equal(1200f, FameRates.Experience(1200f, 20, 1));
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
                "<World id=\"Admin Arena\"><Width>8</Width><Height>8</Height><FameRate>0.5</FameRate></World>"));
            var other = new WorldResource(System.Xml.Linq.XElement.Parse(
                "<World id=\"Tomb\"><Width>8</Width><Height>8</Height></World>"));
            Assert.Equal(0.5, arena.FameRate);
            Assert.Equal(1, other.FameRate);
        }
    }
}
