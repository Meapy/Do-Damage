using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors.doomed
{
    // Pets that help their owner: every cooldown, gives the owner a condition (Healing, Speedy,
    // Invulnerable and so on) for durationMs while the owner is within range.
    internal class PetOwnerEffect : Behavior
    {
        private readonly ConditionEffectIndex _effect;
        private readonly int _durationMs;
        private readonly double _range;
        private Cooldown _coolDown;

        public PetOwnerEffect(ConditionEffectIndex effect, int durationMs, Cooldown coolDown, double range = 10)
        {
            _effect = effect;
            _durationMs = durationMs;
            _coolDown = coolDown.Normalize();
            _range = range;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = _coolDown.Next(Random);

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state;
            if (cool > 0)
            {
                state = cool - time.ElapsedMsDelta;
                return;
            }
            state = _coolDown.Next(Random);

            var owner = (host as Pet)?.PlayerOwner;
            if (owner?.World == null || owner.World != host.World || owner.Dead || host.DistTo(owner) > _range)
                return;

            owner.ApplyConditionEffect(_effect, _durationMs);
            owner.World.BroadcastIfVisible(new ShowEffect() { EffectType = EffectType.Potion, TargetObjectId = owner.Id, Color = new ARGB(0xffffffff) }, owner);
        }
    }
}
