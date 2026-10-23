using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.logic.transitions;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Sets a boss's health, defense and shot damage from the behaviour instead of its XML, so a chain
    // of bosses (the Oryx endgame) can be tuned on one difficulty curve without changing the client's
    // copy of the XML. Health grows by perPlayer (a fraction of the base) for every player after the
    // first who comes within range, and never shrinks when they leave. Shot damage is the XML damage
    // times damageScale (Shoot applies Enemy.DamageScale). It also clears the random rare, epic and
    // legendary rolls that quest monsters get, which would multiply the tuned health.
    // With world set, it only applies in that world and wins over an untargeted BossStats; with arena
    // set, the same in any arena (ArenaTransition). List it before the untargeted one.
    internal class BossStats : Behavior
    {
        private readonly int _health;
        private readonly int _defense;
        private readonly double _damageScale;
        private readonly double _perPlayer;
        private readonly double _range;
        private readonly string _world;
        private readonly bool _arena;

        public BossStats(int health, int defense, double damageScale = 1.0, double perPlayer = 0.3, double range = 30, string world = null,
            bool arena = false)
        {
            _health = health;
            _defense = defense;
            _damageScale = damageScale;
            _perPlayer = perPlayer;
            _range = range;
            _world = world;
            _arena = arena;
        }

        private sealed class ScaleState
        {
            public readonly HashSet<int> Counted = new HashSet<int>();
            public int Cooldown;
        }

        private bool Targeted => _world != null || _arena;

        private bool Applies(Entity host) =>
            _arena ? ArenaTransition.InArena(host) : _world == null ? !host.StatsTunedForWorld : host.World?.IdName == _world;

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            if (state != null || host is not Enemy enemy || !Applies(host))
                return;

            if (Targeted)
                host.StatsTunedForWorld = true;

            enemy.IsLegendary = enemy.IsEpic = enemy.IsRare = false;
            enemy.GlowEnemy = 0;
            enemy.MaxHealth = _health;
            enemy.Health = _health;
            enemy.Defense = _defense;
            enemy.DamageScale = (float)_damageScale;
            state = new ScaleState();
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not ScaleState s || host is not Enemy enemy)
                return;

            if ((s.Cooldown -= time.ElapsedMsDelta) > 0)
                return;
            s.Cooldown = 1000;

            var before = s.Counted.Count;
            foreach (var player in host.GetNearestEntities(_range, null, true).OfType<Player>())
                s.Counted.Add(player.Id);

            // the first player is the base health; each one after adds perPlayer of it
            var extra = System.Math.Max(0, s.Counted.Count - 1) - System.Math.Max(0, before - 1);
            if (extra <= 0)
                return;

            var add = (int)(_health * _perPlayer * extra);
            enemy.MaxHealth += add;
            enemy.Health += add;
        }
    }
}
