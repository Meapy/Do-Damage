using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // Keeps up to `max` of its own minions alive, making one whenever fewer live and the cooldown has
    // run: the original game's MakeMinions, which Spawn (a fixed number per state entry) and Reproduce
    // (only while no player is near) don't do. The first comes at once on entering the state.
    internal class KeepMinions : Behavior
    {
        private readonly ushort _child;
        private readonly int _max;
        private Cooldown _coolDown;

        public KeepMinions(string child, int max, Cooldown coolDown = new Cooldown())
        {
            _child = GetObjType(child);
            _max = max;
            _coolDown = coolDown.Normalize(1000);
        }

        private sealed class Minions
        {
            public readonly List<Entity> Alive = new List<Entity>();
            public int Wait;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            // those made on an earlier visit to the state still count
            if (!(state is Minions))
                state = new Minions();
            ((Minions)state).Wait = 0;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (!(state is Minions m))
                state = m = new Minions();

            m.Alive.RemoveAll(e => e.Dead || e.World != host.World);

            if (m.Wait > 0)
            {
                m.Wait -= time.ElapsedMsDelta;
                return;
            }
            if (m.Alive.Count >= _max || host.World == null)
                return;

            var entity = Entity.Resolve(host.GameServer, _child);
            entity.Move(host.X, host.Y);
            entity.SpawnedByBehavior = true;
            entity.GivesNoXp = true;

            if (host is Enemy enemyHost && entity is Enemy enemy)
            {
                enemy.ParentEntity = enemyHost;
                enemy.Terrain = enemyHost.Terrain;
                if (enemyHost.Spawned)
                {
                    enemy.Spawned = true;
                    enemy.ApplyPermanentConditionEffect(ConditionEffectIndex.Invisible);
                }
            }

            entity.SummonedBy(host);
            host.World.EnterWorld(entity);
            m.Alive.Add(entity);
            m.Wait = _coolDown.Next(Random);
        }
    }
}
