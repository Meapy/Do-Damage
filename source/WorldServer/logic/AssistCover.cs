using System;
using System.Collections.Generic;
using WorldServer.core.objects;

namespace WorldServer.logic
{
    // Who is helping, until when (AssistCredit): a player's healers and buffers (Player.Helpers),
    // or an enemy's debuffers (DamageCounter). Kept on the world's thread.
    public sealed class AssistCover
    {
        private readonly Dictionary<Player, long> _until = new Dictionary<Player, long>();
        private readonly List<Player> _expired = new List<Player>();

        public void Cover(Player helper, int windowMs)
        {
            if (helper == null || windowMs <= 0)
                return;
            var until = Environment.TickCount64 + windowMs;
            if (!_until.TryGetValue(helper, out var was) || was < until)
                _until[helper] = until;
        }

        // each helper still covering, dropping the rest
        public void ForEach(Action<Player> action)
        {
            if (_until.Count == 0)
                return;
            var now = Environment.TickCount64;
            foreach (var (helper, until) in _until)
            {
                if (until < now || helper.World == null)
                    _expired.Add(helper);
                else
                    action(helper);
            }
            foreach (var helper in _expired)
                _until.Remove(helper);
            _expired.Clear();
        }
    }
}
