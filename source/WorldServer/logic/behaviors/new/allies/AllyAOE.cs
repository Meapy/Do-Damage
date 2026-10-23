using Shared.resources;
using System;
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
    // An ally's blast: minDamage-maxDamage to every enemy within radius, ignoring defense, credited to
    // the player who summoned it, every cooldown. Optionally each enemy hit gets a condition (with a
    // chance), and beam draws a bolt falling from the sky onto the host instead of the area ring.
    internal class AllyAOE : CycleBehavior
    {
        private readonly uint _color;
        private readonly int _minDamage;
        private readonly int _maxDamage;
        private readonly float _radius;
        private readonly Cooldown _coolDown;
        private readonly int _coolDownOffset;
        private readonly ConditionEffectIndex? _effect;
        private readonly int _effectMs;
        private readonly double _effectChance;
        private readonly bool _beam;

        public AllyAOE(int minDamage, int maxDamage, float radius, uint color = 0, Cooldown cooldown = new Cooldown(), int coolDownOffset = 0,
            ConditionEffectIndex? effect = null, int effectMs = 0, double effectChance = 1, bool beam = false)
        {
            _minDamage = minDamage;
            _maxDamage = maxDamage;
            _radius = radius;
            _color = color;
            _coolDownOffset = coolDownOffset;
            _coolDown = cooldown.Normalize();
            _effect = effect;
            _effectMs = effectMs;
            _effectChance = effectChance;
            _beam = beam;
        }
        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = _coolDownOffset;
        }
        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int?)state ?? -1;

            Status = CycleStatus.NotStarted;

            if (cool > 0)
            {
                cool -= time.ElapsedMsDelta;
                Status = CycleStatus.InProgress;
                state = cool;
                return;
            }
            if (host == null)
                return;

            var world = host.World;
            var color = new ARGB(_color != 0 ? _color : 0xffffffff);
            if (_beam)
                world.BroadcastIfVisible(new ShowEffect()
                {
                    EffectType = EffectType.Lightning,
                    TargetObjectId = host.Id,
                    Color = color,
                    Pos1 = new Position() { X = host.X, Y = host.Y - 6 },
                    Pos2 = new Position() { X = 450 }
                }, host);
            world.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.AreaBlast,
                Color = color,
                TargetObjectId = host.Id,
                Pos1 = new Position() { X = _radius },
                Pos2 = new Position() { X = host.Id, Y = 255 }
            }, host);

            var player = host.World.Players.GetValueOrDefault(host.AllyOwnerId);
            if (player == null)
                return;
            world.AOE(host.Position, _radius, false, entity =>
            {
                if (entity is not Enemy en)
                    return;
                if (_effect is ConditionEffectIndex effect && Random.NextDouble() < _effectChance)
                    en.Damage(player, ref time, Random.Next(_minDamage, _maxDamage + 1), true, false, new ConditionEffect(effect, _effectMs));
                else
                    en.Damage(player, ref time, Random.Next(_minDamage, _maxDamage + 1), true);
            });

            cool = _coolDown.Next(Random);
            Status = CycleStatus.Completed;
            state = cool;
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
        }
    }
}
