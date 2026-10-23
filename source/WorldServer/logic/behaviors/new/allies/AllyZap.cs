using Shared.resources;
using System.Collections.Generic;
using WorldServer.core;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // An ally's zap: every cooldown, a bolt of lightning from the host to the nearest enemy within
    // radius for minDamage-maxDamage, ignoring defense and credited to the player who summoned it;
    // optionally with a condition (and a chance of it). The Genesis Spell's portals, the Hivemaster
    // Helm's bees.
    internal class AllyZap : Behavior
    {
        private readonly float _radius;
        private readonly int _minDamage;
        private readonly int _maxDamage;
        private readonly Cooldown _coolDown;
        private readonly uint _color;
        private readonly ConditionEffectIndex? _effect;
        private readonly int _effectMs;
        private readonly double _effectChance;

        public AllyZap(float radius, int minDamage, int maxDamage, Cooldown coolDown, uint color = 0xffffffff,
            ConditionEffectIndex? effect = null, int effectMs = 0, double effectChance = 1)
        {
            _radius = radius;
            _minDamage = minDamage;
            _maxDamage = maxDamage;
            _coolDown = coolDown.Normalize(500);
            _color = color;
            _effect = effect;
            _effectMs = effectMs;
            _effectChance = effectChance;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = 0;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state - time.ElapsedMsDelta;
            if (cool > 0)
            {
                state = cool;
                return;
            }
            state = 0;

            var player = host.World.Players.GetValueOrDefault(host.AllyOwnerId);
            if (player == null)
                return;
            Enemy target = null;
            var best = double.MaxValue;
            host.World.AOE(host.Position, _radius, false, e =>
            {
                if (e is not Enemy en || en.HasConditionEffect(ConditionEffectIndex.Invincible) || en.AllyOwnerId != -1)
                    return;
                var d = (e.X - host.X) * (e.X - host.X) + (e.Y - host.Y) * (e.Y - host.Y);
                if (d < best)
                {
                    best = d;
                    target = en;
                }
            });
            if (target == null)
                return;

            host.World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Lightning,
                TargetObjectId = host.Id,
                Color = new ARGB(_color),
                Pos1 = new Position() { X = target.X, Y = target.Y },
                Pos2 = new Position() { X = 350 }
            }, host);
            var dmg = Random.Next(_minDamage, _maxDamage + 1);
            if (_effect is ConditionEffectIndex effect && Random.NextDouble() < _effectChance)
                target.Damage(player, ref time, dmg, true, false, new ConditionEffect(effect, _effectMs));
            else
                target.Damage(player, ref time, dmg, true);
            state = _coolDown.Next(Random);
        }
    }
}
