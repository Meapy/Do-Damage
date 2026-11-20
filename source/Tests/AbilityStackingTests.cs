using WorldServer.core.objects;
using Xunit;

namespace Tests
{
    // Ascendant forcefields and the Father's wards stacking: the strongest in full, each other at
    // half the one before. The forcefield's numbers match the Rust client's (combat.rs,
    // `forcefields_stack_at_half_each`).
    public class AbilityStackingTests
    {
        [Fact]
        public void ForcefieldsStackAtHalfEach()
        {
            Assert.Equal(0, Player.StackedShield(new int[0]));
            Assert.Equal(30, Player.StackedShield(new[] { 30 }));
            Assert.Equal(50, Player.StackedShield(new[] { 20, 40 }));
            Assert.Equal(52, Player.StackedShield(new[] { 30, 30, 30 }));
            Assert.Equal(37, Player.StackedShield(new[] { 25, 25, 0 }));
            Assert.Equal(90, Player.StackedShield(new[] { 80, 80 }));
        }

        [Fact]
        public void WardsStackAtHalfEach()
        {
            Assert.Equal(170, Player.Stacked(new[] { 170 }));
            Assert.Equal(255, Player.Stacked(new[] { 170, 170 }));
            Assert.Equal(297, Player.Stacked(new[] { 170, 170, 170 }));
            Assert.Equal(195,Player.Stacked(new[] { 50, 170 }));
        }
    }
}
