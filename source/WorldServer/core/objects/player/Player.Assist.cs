using Shared.resources;
using WorldServer.logic;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // the players healing or buffing this one: they earn assist credit on what it hits
        // (DamageCounter.HitBy, AssistCredit)
        public readonly AssistCover Helpers = new AssistCover();

        // healed by another player's ability: healed is what it actually restored
        public void HealedBy(Player helper, int healed, int max)
        {
            if (helper != null && helper != this)
                Helpers.Cover(helper, AssistCredit.HealWindow(healed, max));
        }

        // buffed by another player's ability with effect, for durationMs; Healing (a heal over
        // time) counts as a heal, so not on a player at full health
        public void BuffedBy(Player helper, int durationMs, ConditionEffectIndex effect)
        {
            if (effect == ConditionEffectIndex.Healing && Health >= Stats[0])
                return;
            if (helper != null && helper != this)
                Helpers.Cover(helper, AssistCredit.EffectWindow(durationMs));
        }

        // buffed by another player's stat boost, for durationMs
        public void BuffedBy(Player helper, int durationMs)
        {
            if (helper != null && helper != this)
                Helpers.Cover(helper, AssistCredit.EffectWindow(durationMs));
        }
    }
}
