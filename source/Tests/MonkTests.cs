using System;
using System.IO;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using Xunit;

namespace Tests
{
    // The Monk's Chi and Flowing Strike (Player.Flow.cs).
    public class MonkTests
    {
        private static readonly Lazy<XmlData> Data = new Lazy<XmlData>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false).GameData);

        // a strike flows as far as it was aimed, at most its reach, and stops before a wall
        [Fact]
        public void TheStrikeStopsShortOfAWall()
        {
            static bool open(double x, double y) => true;
            var end = Player.FlowEnd(10, 10, new Position(14, 10), 6, open);
            Assert.Equal(14, end.X, 1);
            end = Player.FlowEnd(10, 10, new Position(30, 10), 6, open);
            Assert.Equal(16, end.X, 1);
            Assert.Equal(10, end.Y, 3);
            // a wall from x = 13 on: stopped on the open side of it
            end = Player.FlowEnd(10, 10, new Position(30, 10), 6, (x, y) => x < 13);
            Assert.InRange(end.X, 12.5f, 12.999f);
            // a wall right in front: nowhere to go
            end = Player.FlowEnd(10, 10, new Position(11, 10), 6, (x, y) => false);
            Assert.Equal(10, end.X);
            // aimed at their own feet
            end = Player.FlowEnd(10, 10, new Position(10, 10), 6, open);
            Assert.Equal(10, end.X);
        }

        [Fact]
        public void EachChiMakesTheBurstAFifthStronger()
        {
            Assert.Equal(100, Player.ChiBoost(0));
            Assert.Equal(160, Player.ChiBoost(3));
            Assert.Equal(200, Player.ChiBoost(Player.ChiMax));
            Assert.Equal(200, Player.ChiBoost(9));
        }

        // a strike gives back a Chi for each enemy struck on its way, at most two, and its look
        // carries the tier, the Chi spent and the Chi given back (flow.rs reads them back)
        [Fact]
        public void TheStrikeKeepsItsMomentum()
        {
            Assert.Equal(0, Player.ChiEcho(0));
            Assert.Equal(1, Player.ChiEcho(1));
            Assert.Equal(Player.FlowEchoMax, Player.ChiEcho(7));
            var code = Player.FlowLookCode(8, Player.ChiMax, 3);
            Assert.Equal(8, code % 16);
            Assert.Equal(Player.ChiMax, code / 16 % 8);
            Assert.Equal(Player.FlowEchoMax, code / 128);
            Assert.True(Player.FlowGuardMs > Player.FlowDashMs);
        }

        // every tiered fist fires its own chi strike, a projectile the game knows
        [Fact]
        public void EveryTieredFistFiresAChiStrike()
        {
            var data = Data.Value;
            var fists = data.Items.Values.Where(i => i.SlotType == Player.FistSlotType && i.Tier >= 0).ToList();
            Assert.Equal(16, fists.Count);
            foreach (var f in fists)
            {
                var shot = Assert.Single(f.Projectiles);
                Assert.Equal($"Monk Chi Strike {f.Tier}", shot.ObjectId);
                Assert.True(data.IdToObjectType.ContainsKey(shot.ObjectId), shot.ObjectId);
            }
        }

        // every Monk's ability (slot 130) is a Flowing Strike, stronger by tier, and the Monk's
        // weapons are fists (the Chi's slot type)
        [Fact]
        public void EveryFountainIsAFlowingStrike()
        {
            var data = Data.Value;
            var fountains = data.Items.Values.Where(i => i.SlotType == 130).OrderBy(i => i.Tier).ToList();
            Assert.Equal(9, fountains.Count);
            ActivateEffect before = null;
            foreach (var f in fountains)
            {
                var eff = Assert.Single(f.ActivateEffects);
                Assert.Equal(ActivateEffects.FlowStrike, eff.Effect);
                Assert.True(eff.MaximumDistance > 0 && eff.Radius > 0 && eff.Range > 0, f.ObjectId);
                Assert.True(eff.TotalDamage > 0 && eff.ImpactDamage > 0 && eff.Amount > 0, f.ObjectId);
                Assert.InRange(f.Tier, 0, Player.FlowColors.Length - 1);
                if (before != null)
                    Assert.True(eff.TotalDamage > before.TotalDamage && eff.ImpactDamage > before.ImpactDamage
                        && eff.Amount >= before.Amount && eff.MaximumDistance >= before.MaximumDistance, f.ObjectId);
                before = eff;
            }
            var monk = data.ObjectDescs[data.IdToObjectType["Monk"]] as PlayerDesc;
            Assert.NotNull(monk);
            Assert.Equal(Player.FistSlotType, monk.SlotTypes[0]);
        }
    }
}
