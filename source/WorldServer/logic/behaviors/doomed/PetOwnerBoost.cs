using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors.doomed
{
    // Pets that boost their owner: every cooldown, raises one of the owner's stats by amount for
    // durationMs while the owner is within range, as a StatBoostSelf item does (the stat's Boost
    // condition shows meanwhile).
    internal class PetOwnerBoost : Behavior
    {
        private readonly StatDataType _stat;
        private readonly int _amount;
        private readonly int _durationMs;
        private readonly double _range;
        private Cooldown _coolDown;

        public PetOwnerBoost(StatDataType stat, int amount, int durationMs, Cooldown coolDown, double range = 10)
        {
            _stat = stat;
            _amount = amount;
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

            var stats = owner.Stats;
            var idx = StatsManager.GetStatIndex(_stat);
            stats.Boost.ActivateBoost[idx].Push(_amount);
            stats.ReCalculateValues();
            owner.World.StartNewTimer(_durationMs, (world, t) =>
            {
                stats.Boost.ActivateBoost[idx].Pop(_amount);
                stats.ReCalculateValues();
            });
            owner.World.BroadcastIfVisible(new ShowEffect() { EffectType = EffectType.Potion, TargetObjectId = owner.Id, Color = new ARGB(0xffffffff) }, owner);
        }
    }
}
