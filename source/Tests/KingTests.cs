using System;
using System.IO;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using Xunit;

namespace Tests
{
    // The King's Royal Decree (Player.Decree.cs).
    public class KingTests
    {
        private static readonly Lazy<XmlData> Data = new Lazy<XmlData>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false).GameData);

        // the King's ability slot is the scepters' own (no longer the Priest's tome), and he starts
        // with the lowest
        [Fact]
        public void TheKingHoldsAScepter()
        {
            var data = Data.Value;
            var king = data.ObjectDescs[data.IdToObjectType["King"]] as PlayerDesc;
            Assert.NotNull(king);
            Assert.Equal(134, king.SlotTypes[1]);
            Assert.Equal(17, king.SlotTypes[0]);
            var start = data.Items[(ushort)king.Equipment[1]];
            Assert.Equal(134, start.SlotType);
            Assert.Equal(0, start.Tier);
        }

        // nine scepters, T0 to T8, each a Royal Decree stronger than the one below
        [Fact]
        public void EveryScepterIsARoyalDecree()
        {
            var data = Data.Value;
            var scepters = data.Items.Values.Where(i => i.SlotType == 134 && i.Tier >= 0).OrderBy(i => i.Tier).ToList();
            Assert.Equal(Enumerable.Range(0, 9), scepters.Select(s => s.Tier));
            ActivateEffect before = null;
            foreach (var s in scepters)
            {
                var eff = Assert.Single(s.ActivateEffects);
                Assert.Equal(ActivateEffects.RoyalDecree, eff.Effect);
                Assert.True(eff.Radius > 0 && eff.MaximumDistance > 0, s.ObjectId);
                Assert.True(eff.ImpactDamage > 0 && eff.DurationMS > 0 && eff.TributeMp > 0, s.ObjectId);
                Assert.True(s.MpCost > eff.TributeMp * Player.TributeMpKills / 2, s.ObjectId + ": the tribute mustn't pay for the decree twice over");
                if (before != null)
                    Assert.True(eff.ImpactDamage > before.ImpactDamage && eff.DurationMS > before.DurationMS
                        && eff.Radius >= before.Radius && eff.TributeMp >= before.TributeMp, s.ObjectId);
                before = eff;
            }
            Assert.Equal(9, Player.DecreeColors.Length);
        }

        // each stack of Tribute is 10 more on a shot, up to ten stacks; the Rust client adds the same
        [Fact]
        public void TributeStacksToTen()
        {
            Assert.Equal(0, Player.TributeBonus(0));
            Assert.Equal(30, Player.TributeBonus(3));
            Assert.Equal(100, Player.TributeBonus(Player.TributeMax));
            Assert.Equal(100, Player.TributeBonus(25));
            Assert.Equal(10, Player.TributeMax);
            Assert.Equal(10_000, Player.TributeHoldMs);
        }

        // a Condemned enemy takes a little more from every hit, not the Curse's quarter
        [Fact]
        public void CondemnedIsFivePercent()
        {
            Assert.Equal(5, Player.CondemnedPercent);
        }

        // the Frozen Court's white bag: a Royal Decree that slows and spreads, a T8's power or near it,
        // in the tomb's frost (its own colour) rather than a tier's
        [Fact]
        public void TheEdictSpreadsTheDecree()
        {
            var data = Data.Value;
            var edict = data.Items[data.IdToObjectType["Edict of the Frozen Court"]];
            Assert.Equal(134, edict.SlotType);
            Assert.Equal(-1, edict.Tier);
            Assert.Equal(6, edict.BagType);
            var eff = Assert.Single(edict.ActivateEffects);
            Assert.Equal(ActivateEffects.RoyalDecree, eff.Effect);
            Assert.Equal(3.5f, eff.Spread);
            Assert.Equal(2f, eff.EffectDuration);
            Assert.Equal(0x9FE8FFu, eff.Color);
            var t8 = data.Items.Values.Single(i => i.SlotType == 134 && i.Tier == 8).ActivateEffects[0];
            Assert.True(eff.ImpactDamage < t8.ImpactDamage && eff.Radius < t8.Radius,
                "the Edict pays for its spreading with a smaller seal than the T8's");
            // the ordinary scepters don't spread
            Assert.All(data.Items.Values.Where(i => i.SlotType == 134 && i.Tier >= 0), s => Assert.Equal(0f, s.ActivateEffects[0].Spread));
        }

        // a decree's first TributeMpKills enemies to fall pay the King mana, the rest only heal
        [Fact]
        public void OnlyTheFirstFewPayMana()
        {
            Assert.Equal(12, Player.TributeMana(0, 12));
            Assert.Equal(12, Player.TributeMana(Player.TributeMpKills - 1, 12));
            Assert.Equal(0, Player.TributeMana(Player.TributeMpKills, 12));
            Assert.Equal(0, Player.TributeMana(0, -5));
        }
    }
}
