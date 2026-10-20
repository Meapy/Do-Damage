using System;
using System.Collections.Generic;

namespace WorldServer.core.objects
{
    // The vault's Training Dummy: a player's first hit starts 5 seconds, then it tells that player,
    // in their chat only, the damage they did in them and the damage a second. It never dies: every
    // hit is healed at once.
    public sealed class DpsMeter
    {
        public const string ObjectId = "Training Dummy";
        private const int WindowMs = 5000;

        private readonly Enemy _host;
        // by player: when their first hit landed (Environment.TickCount64) and the damage since
        private readonly Dictionary<Player, (long Start, long Damage)> _runs = new Dictionary<Player, (long, long)>();

        public DpsMeter(Enemy host) => _host = host;

        public void Hit(Player player, int dmg)
        {
            _host.Health = _host.MaxHealth;
            var now = Environment.TickCount64;
            _runs[player] = _runs.TryGetValue(player, out var run) ? (run.Start, run.Damage + dmg) : (now, dmg);
        }

        public void Tick()
        {
            if (_runs.Count == 0)
                return;
            var now = Environment.TickCount64;
            List<Player> done = null;
            foreach (var (player, run) in _runs)
                if (now - run.Start >= WindowMs || player.World != _host.World)
                    (done ??= new List<Player>()).Add(player);
            if (done == null)
                return;
            foreach (var player in done)
            {
                var run = _runs[player];
                _runs.Remove(player);
                if (player.World == _host.World && player.Client != null)
                    player.SendInfo($"Training Dummy: {run.Damage:N0} damage in {WindowMs / 1000} seconds, {run.Damage * 1000 / WindowMs:N0} DPS.");
            }
        }
    }
}
