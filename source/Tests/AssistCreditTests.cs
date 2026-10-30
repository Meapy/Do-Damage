using Shared.resources;
using Xunit;

namespace Tests
{
    public class AssistCreditTests
    {
        [Fact]
        public void OverhealCoversNothing()
        {
            Assert.Equal(0, AssistCredit.HealWindow(0, 1000));
            Assert.Equal(0, AssistCredit.HealWindow(-5, 1000));
        }

        [Fact]
        public void SmallHealsCoverLess()
        {
            // 10% of max or more covers the whole window, 5% half of it
            Assert.Equal(AssistCredit.HealWindowMs, AssistCredit.HealWindow(100, 1000));
            Assert.Equal(AssistCredit.HealWindowMs, AssistCredit.HealWindow(800, 1000));
            Assert.Equal(AssistCredit.HealWindowMs / 2, AssistCredit.HealWindow(50, 1000));
        }

        [Fact]
        public void EffectWindowIsCapped()
        {
            Assert.Equal(4000, AssistCredit.EffectWindow(4000));
            Assert.Equal(AssistCredit.MaxEffectWindowMs, AssistCredit.EffectWindow(60000));
            Assert.Equal(0, AssistCredit.EffectWindow(-1));
        }

        [Fact]
        public void DamageOnlyIsUnchanged()
        {
            Assert.Equal(300, AssistCredit.Credit(300, 0, 1000, 500));
            Assert.Equal(0, AssistCredit.Credit(0, 0, 1000, 500));
        }

        [Fact]
        public void HealerWhoNeverShootsGetsTheirAssist()
        {
            // a healer whose group did 10000 damage, of which the healed tank did 4000: half of it
            // is 2000, which is under the 20% cap
            Assert.Equal(2000, AssistCredit.Credit(0, 4000 * AssistCredit.HelpRate, 10000, 4000));
        }

        [Fact]
        public void AssistIsCappedAtMaxShare()
        {
            // healing everyone who did all 10000 damage: 5000 credit, capped at 20%
            Assert.Equal(2000, AssistCredit.Credit(0, 5000, 10000, 3000));
            Assert.Equal(1000 + 2000, AssistCredit.Credit(1000, 5000, 10000, 3000));
        }

        [Fact]
        public void NeverAboveTheTopDamageDealer()
        {
            // the top dealer did 1500: a healer can't pass them
            Assert.Equal(1500, AssistCredit.Credit(0, 5000, 10000, 1500));
            Assert.Equal(1500, AssistCredit.Credit(1000, 5000, 10000, 1500));
            // but the top dealer keeps their own damage, and gains nothing on top of it
            Assert.Equal(1500, AssistCredit.Credit(1500, 5000, 10000, 1500));
        }
    }
}
