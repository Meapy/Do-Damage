using System;
using System.IO;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using Xunit;

namespace Tests
{
    // The Sharklord: Blood in the Water, the Frenzy and the Maw of the Deep (Player.Maw.cs).
    public class SharklordTests
    {
        private static readonly Lazy<XmlData> Data = new Lazy<XmlData>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false).GameData);

        // the Sharklord's class type is the one Player.Maw.cs checks, and they start with an
        // oceanic spell that is a Maw
        [Fact]
        public void TheSharklordHoldsAMaw()
        {
            var data = Data.Value;
            var shark = data.ObjectDescs[data.IdToObjectType["Sharklord"]] as PlayerDesc;
            Assert.NotNull(shark);
            Assert.Equal(Player.SharklordType, shark.ObjectType);
            Assert.Equal(5000, shark.SlotTypes[1]);
            var start = data.Items[(ushort)shark.Equipment[1]];
            Assert.Equal(ActivateEffects.DeepMaw, Assert.Single(start.ActivateEffects).Effect);
        }

        // the tiered oceanic spells, T0 to T8, each a Maw stronger than the one below
        [Fact]
        public void EveryTieredSpellIsAMaw()
        {
            var data = Data.Value;
            var spells = data.Items.Values.Where(i => i.SlotType == 5000 && i.Tier >= 0 && i.Tier <= 8)
                .OrderBy(i => i.Tier).ToList();
            Assert.Equal(Enumerable.Range(0, 9), spells.Select(s => s.Tier));
            ActivateEffect before = null;
            foreach (var s in spells)
            {
                var eff = Assert.Single(s.ActivateEffects);
                Assert.Equal(ActivateEffects.DeepMaw, eff.Effect);
                Assert.True(eff.Radius > 0 && eff.MaximumDistance > 0 && eff.Amount > 0, s.ObjectId);
                if (before != null)
                    Assert.True(eff.ImpactDamage > before.ImpactDamage && eff.TotalDamage > before.TotalDamage
                        && eff.Radius >= before.Radius && eff.Amount >= before.Amount, s.ObjectId);
                before = eff;
            }
            Assert.Equal(9, Player.MawColors.Length);
        }

        // under half its health an enemy is Bloodied: a fifth more from the Sharklord's shots, and
        // twice the Maw's bite; the Rust client works out the same (combat.rs)
        [Fact]
        public void TheBloodiedTakeMore()
        {
            Assert.False(Player.Bloodied(500, 1000));
            Assert.True(Player.Bloodied(499, 1000));
            Assert.False(Player.Bloodied(0, 1000));
            Assert.False(Player.Bloodied(10, 0));
            Assert.Equal(100, Player.BloodiedDamage(100, 800, 1000));
            Assert.Equal(120, Player.BloodiedDamage(100, 300, 1000));
            Assert.Equal(36, Player.BloodiedDamage(30, 400, 1000));
            Assert.Equal(0, Player.BloodiedDamage(0, 300, 1000));
            Assert.Equal(200, Player.BiteDamage(200, 900, 1000));
            Assert.Equal(400, Player.BiteDamage(200, 100, 1000));
        }

        // the bite feeds the Sharklord for at most FeedMax enemies, twice over for a Megalodon
        [Fact]
        public void FeedingIsCapped()
        {
            Assert.Equal(0, Player.FeedHeal(40, 0, false));
            Assert.Equal(80, Player.FeedHeal(40, 2, false));
            Assert.Equal(40 * Player.FeedMax, Player.FeedHeal(40, 12, false));
            Assert.Equal(80 * Player.FeedMax, Player.FeedHeal(40, 12, true));
            Assert.Equal(0, Player.FeedHeal(-5, 3, false));
        }

        // the fin is quick over a short way and never slower than FinMaxMs
        [Fact]
        public void TheFinTakesLongerFurtherOut()
        {
            Assert.Equal(Player.FinMinMs, Player.FinMs(0));
            Assert.True(Player.FinMs(6) > Player.FinMs(3));
            Assert.Equal(Player.FinMaxMs, Player.FinMs(40));
        }

        // the look's ObjectType: the tier, 16 more for a Megalodon (particles/maw.rs reads it so)
        [Fact]
        public void TheLookCarriesTheMegalodon()
        {
            Assert.Equal(7, Player.MawLookCode(7, false));
            Assert.Equal(24, Player.MawLookCode(8, true));
            Assert.Equal(5, Player.FrenzyMax);
        }
    }
}
