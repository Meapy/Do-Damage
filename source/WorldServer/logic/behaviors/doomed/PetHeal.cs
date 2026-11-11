using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors.doomed
{
    // Doomed Realms pet healing (PetsHealingHP / PetsHealingMP): every cooldown, restores a random
    // amount between min and max of the owner's HP or MP, wherever the owner is in the pet's world.
    internal class PetHeal : Behavior
    {
        private readonly bool _mana;
        private readonly int _min;
        private readonly int _max;
        private readonly double _range;
        private Cooldown _coolDown;

        public PetHeal(bool mana, int min, int max, Cooldown coolDown, double range = 10)
        {
            _mana = mana;
            _min = min;
            _max = Math.Max(min, max);
            _coolDown = coolDown.Normalize();
            _range = range;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = _coolDown.Next(Random);

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            // a pet in stasis (the Black Between's survival phases) does nothing
            if (host.HasConditionEffect(ConditionEffectIndex.Stasis))
                return;
            var cool = (int)state;
            if (cool > 0)
            {
                state = cool - time.ElapsedMsDelta;
                return;
            }
            var raised = (host as Pet)?.Heal;
            state = raised?.Ms ?? _coolDown.Next(Random);

            // from any distance: a pet left behind by a fast character, or stuck on a wall, still heals
            var owner = (host as Pet)?.PlayerOwner;
            if (owner?.World == null || owner.World != host.World || owner.Dead)
                return;
            if (owner.HasConditionEffect(ConditionEffectIndex.Sick))
                return;

            var amount = raised?.Amount ?? Random.Next(_min, _max + 1);
            int gained;
            if (_mana)
            {
                var mp = Math.Min(owner.Mana + amount, owner.Stats[1]);
                gained = mp - owner.Mana;
                owner.Mana = mp;
            }
            else
            {
                var hp = Math.Min(owner.Health + amount, owner.Stats[0]);
                gained = hp - owner.Health;
                owner.Health = hp;
            }
            if (gained <= 0)
                return;

            var color = _mana ? new ARGB(0xff6084e0) : new ARGB(0xff00ff00);
            owner.World.BroadcastIfVisible(new ShowEffect() { EffectType = EffectType.Potion, TargetObjectId = owner.Id, Color = color }, owner);
            owner.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Trail,
                TargetObjectId = host.Id,
                Pos1 = new Position { X = owner.X, Y = owner.Y },
                Color = color
            }, host);
            owner.World.BroadcastIfVisible(new Notification() { ObjectId = owner.Id, Message = "+" + gained, Color = color }, owner);
        }
    }
}
