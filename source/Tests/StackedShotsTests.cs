using System;
using System.Xml.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using Xunit;
using static WorldServer.core.objects.Player;

namespace Tests
{
    public class StackedShotsTests
    {
        private static ProjectileDesc Desc(string look = "Fire Bust Shot", int speed = 70) =>
            new ProjectileDesc(XElement.Parse(
                $"<Projectile id=\"0\"><ObjectId>{look}</ObjectId><Damage>120</Damage><Speed>{speed}</Speed><LifetimeMS>2000</LifetimeMS></Projectile>"));

        private static ValidatedProjectile Shot(int bulletId, double x, double y, double angleDeg, int time, ProjectileDesc desc = null)
        {
            var p = new ValidatedProjectile(7, bulletId, new Position(x, y), (float)(angleDeg * Math.PI / 180), 0x5e78, 120, DamageType.Player, desc ?? Desc(), false);
            p.Time = time;
            return p;
        }

        [Fact]
        public void TwoShootsOfOneMonsterOnOneTickAreOneShot()
        {
            // the Mini Yeti's two Shoots of the same bullet at a player standing still
            Assert.True(StackedShots.Stacked(Shot(1, 10, 10, 30, 1000), Shot(2, 10, 10, 30, 1000), 1500));
        }

        [Fact]
        public void MonstersOnOneSpotStack()
        {
            // two bats a fifth of a tile apart, aiming at the same player
            Assert.True(StackedShots.Stacked(Shot(1, 10, 10, 0, 1000), Shot(1, 10.1, 10.15, 1, 1002), 1400));
        }

        [Fact]
        public void ASpreadIsSeparateShots()
        {
            // a spread 8 degrees apart, hitting 3 tiles out
            Assert.False(StackedShots.Stacked(Shot(1, 10, 10, 0, 1000), Shot(2, 10, 10, 8, 1000), 1430));
        }

        [Fact]
        public void ShotsFromAnotherTickAreSeparate()
        {
            // the same monster firing again a tick later: one behind the other
            Assert.False(StackedShots.Stacked(Shot(1, 10, 10, 0, 1000), Shot(2, 10, 10, 0, 1100), 1500));
        }

        [Fact]
        public void ShotsThatLookDifferentAreSeparate()
        {
            Assert.False(StackedShots.Stacked(Shot(1, 10, 10, 0, 1000), Shot(2, 10, 10, 0, 1000, Desc("Dragon Eye Blast")), 1300));
        }

        [Fact]
        public void ShotsFromApartThatMeetAreSeparate()
        {
            // two bombs either side of the player, both aimed at them
            Assert.False(StackedShots.Stacked(Shot(1, 8, 10, 0, 1000), Shot(1, 12, 10, 180, 1000), 1285));
        }
    }
}
