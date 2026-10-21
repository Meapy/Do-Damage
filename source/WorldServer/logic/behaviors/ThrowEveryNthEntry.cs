using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // Counts the host's entries into states that hold one of these with the same `series` (each of
    // Oryx 5's patterns holds one, so it counts his pattern changes): every `every`th entry, once
    // the host is below `belowHealth` of its health, it throws `count` of `child` to random spots
    // minRange to maxRange tiles away, landing 1.5 s later as a toss does.
    internal class ThrowEveryNthEntry : Behavior
    {
        private static readonly ConditionalWeakTable<Entity, Dictionary<string, int>> Counts = new ConditionalWeakTable<Entity, Dictionary<string, int>>();

        private readonly ushort _child;
        private readonly int _count;
        private readonly int _every;
        private readonly double _belowHealth;
        private readonly double _minRange;
        private readonly double _maxRange;
        private readonly string _series;

        public ThrowEveryNthEntry(string child, int count, int every, double belowHealth, double minRange, double maxRange, string series = null)
        {
            _child = GetObjType(child);
            _count = count;
            _every = every;
            _belowHealth = belowHealth;
            _minRange = minRange;
            _maxRange = maxRange;
            _series = series ?? child;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var counts = Counts.GetOrCreateValue(host);
            counts.TryGetValue(_series, out var n);
            counts[_series] = ++n;
            if (n % _every != 0 || host is not Enemy enemy || enemy.Health >= enemy.MaxHealth * _belowHealth)
                return;

            for (var i = 0; i < _count; i++)
            {
                var a = Random.NextDouble() * 2 * Math.PI;
                var d = _minRange + Random.NextDouble() * (_maxRange - _minRange);
                var target = new Position(host.X + (float)(Math.Cos(a) * d), host.Y + (float)(Math.Sin(a) * d));
                host.World.Broadcast(new ShowEffect()
                {
                    EffectType = EffectType.Throw,
                    Color = new ARGB(0xffffd060),
                    TargetObjectId = host.Id,
                    Pos1 = target
                });
                host.World.StartNewTimer(1500, (world, t) =>
                {
                    if (!world.IsPassable(target.X, target.Y, true))
                        return;
                    var entity = Entity.Resolve(world.GameServer, _child);
                    entity.Move(target.X, target.Y);
                    world.EnterWorld(entity);
                });
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        { }
    }
}
