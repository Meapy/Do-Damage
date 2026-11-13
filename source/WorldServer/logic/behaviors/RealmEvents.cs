using System;
using System.Linq;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Behaviours of the realm events added with the Thieves' Bazaar, the Starfall Crater and the
    // Blightwood (BehaviorDb.RealmEvents.cs).

    /// <summary>
    /// The Gilded Thief's vanishing trick: with a player chasing it, every so often it puffs into
    /// smoke and comes out of it `min` to `max` tiles away (never more than `home` tiles from where it
    /// came into the world, and never closer to a player than it was), leaving `decoy` where it stood.
    /// </summary>
    internal class ThiefBlink : Behavior
    {
        private readonly Cooldown _coolDown;
        private readonly double _min, _max, _home, _chased;
        private readonly string _decoy;

        public ThiefBlink(Cooldown coolDown, double min = 6, double max = 11, double home = 24, double chased = 12, string decoy = null)
        {
            _coolDown = coolDown.Normalize(6000);
            _min = min;
            _max = max;
            _home = home;
            _chased = chased;
            _decoy = decoy;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state ??= _coolDown.Next(Random);

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state - time.ElapsedMsDelta;
            if (cool > 0)
            {
                state = cool;
                return;
            }
            state = 1000;   // nothing near or nowhere to go: look again in a second
            var world = host.World;
            if (world == null || host.HasConditionEffect(Shared.resources.ConditionEffectIndex.Stunned))
                return;
            var near = host.GetNearestEntity(_chased, true);
            if (near == null)
                return;
            var home = host.SpawnPoint.Value;
            var before = near.DistTo(host);
            for (var tries = 0; tries < 30; tries++)
            {
                var a = Random.NextDouble() * Math.PI * 2;
                var d = _min + Random.NextDouble() * (_max - _min);
                var x = host.X + (float)(Math.Cos(a) * d);
                var y = host.Y + (float)(Math.Sin(a) * d);
                var hx = x - home.X;
                var hy = y - home.Y;
                if (hx * hx + hy * hy > _home * _home || !world.IsPassable(x, y, true))
                    continue;
                if (world.Players.Values.Any(p => p.DistTo(x, y) < Math.Max(4, before)))
                    continue;

                Puff(host, host.X, host.Y);
                if (_decoy != null && Entity.Resolve(world.GameServer, _decoy) is Enemy decoy)
                {
                    decoy.Spawned = true;
                    decoy.Move(host.X, host.Y);
                    world.EnterWorld(decoy);
                }
                host.Move(x, y);
                Puff(host, x, y);
                state = _coolDown.Next(Random);
                return;
            }
        }

        private static void Puff(Entity host, float x, float y)
        {
            var at = new Position(x, y);
            host.World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.AreaBlast,
                TargetObjectId = host.Id,
                Pos1 = new Position(2.5f, 0),
                Color = new ARGB(0xFFFFD24A),
            }, ref at);
        }
    }

    /// <summary>
    /// Leaves the world without dying: no loot, no kill counted (the Gilded Thief getting away, its
    /// decoys going up in smoke, the Blight Mother's pods once they have burst).
    /// </summary>
    internal class Vanish : Behavior
    {
        private readonly bool _puff;

        public Vanish(bool puff = true) => _puff = puff;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            if (world == null || state != null)
                return;
            state = true;
            if (_puff)
            {
                var at = new Position(host.X, host.Y);
                world.BroadcastIfVisible(new ShowEffect
                {
                    EffectType = EffectType.Teleport,
                    TargetObjectId = host.Id,
                    Pos1 = at,
                    Color = new ARGB(0xFFFFFFFF),
                }, ref at);
            }
            world.LeaveWorld(host);
        }
    }
}
