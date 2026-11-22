using WorldServer.logic.behaviors;
using Xunit;

namespace Tests
{
    // Which dungeons' bosses gain half as much health per extra player (GroupScaling).
    public class GroupScalingTests
    {
        [Theory]
        [InlineData("The Black Between")]
        [InlineData("The Crucible")]
        [InlineData("The Elder Shatters")]
        [InlineData("Elder Lost Halls")]
        [InlineData("Elder Cultist Hideout")]
        [InlineData("The Elder Void")]
        [InlineData("Lair of Elder Draconis")]
        [InlineData("The Elder Ivory Wyvern")]
        [InlineData("Elder Cemetery Final Battle")]
        [InlineData("Elder Tomb of the Ancients")]
        public void HardDungeonsScaleLess(string world) => Assert.True(GroupScaling.IsHard(world));

        [Theory]
        [InlineData("The Shatters")]
        [InlineData("Lost Halls")]
        [InlineData("Void")]
        [InlineData("Lair of Draconis")]
        [InlineData("Arena")]
        [InlineData("Nexus")]
        [InlineData("Elysium")]
        [InlineData(null)]
        public void OtherWorldsScaleAsBefore(string world) => Assert.False(GroupScaling.IsHard(world));
    }
}
