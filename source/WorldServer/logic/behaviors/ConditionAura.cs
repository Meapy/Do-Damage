using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Every coolDown, gives each player within range a condition for durationMs: a touch that slows
    // (Oryx 5's stars, range about a tile), or a whole arena slowed (range the arena's).
    internal class ConditionAura : Behavior
    {
        private readonly double _range;
        private readonly ConditionEffectIndex _effect;
        private readonly int _duration;
        private readonly Cooldown _coolDown;

        public ConditionAura(double range, ConditionEffectIndex effect, int durationMs, Cooldown coolDown = new Cooldown())
        {
            _range = range;
            _effect = effect;
            _duration = durationMs;
            _coolDown = coolDown.Normalize(500);
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = 0;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state - time.ElapsedMsDelta;
            if (cool <= 0)
            {
                foreach (var player in host.GetNearestEntities(_range, null, true).OfType<Player>())
                    if (!player.HasConditionEffect(ConditionEffectIndex.Invincible) && !player.HasConditionEffect(ConditionEffectIndex.Stasis))
                        player.ApplyConditionEffect(new ConditionEffect(_effect, _duration));
                cool = _coolDown.Next(Random);
            }
            state = cool;
        }
    }
}
