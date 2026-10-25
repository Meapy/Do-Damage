using System;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core;
using WorldServer.utils;
using System.Collections.Generic;


namespace WorldServer.logic.behaviors
{
    class OrderOnDeath : Behavior
    {
        private readonly double _range;
        private readonly ushort _target;
        private readonly string _stateName;
        private readonly float _probability;

        private State _targetState;

        public OrderOnDeath(double range, string target, string state, double probability = 1)
        {
            _range = range;
            _target = GetObjType(target);
            _stateName = state;
            _probability = (float)probability;
        }

        private static State FindState(State state, string name)
        {
            if (state.Name == name)
                return state;

            return state.States
                .Select(i => FindState(i, name))
                .FirstOrDefault(s => s != null);
        }

        // Enemy.Death calls each behaviour's OnDeath; the State.Death event this used to wait for is
        // never raised, so the order never went out
        public override void OnDeath(Entity host, ref TickTime time)
        {
            if (Random.NextDouble() >= _probability)
                return;

            if (_targetState == null && host.GameServer.BehaviorDb.Definitions.TryGetValue(_target, out var def))
                _targetState = FindState(def.Item1, _stateName);
            if (_targetState == null)
                return;

            foreach (var i in host.GetNearestEntities(_range, _target))
                if (i.CurrentState != null && !i.CurrentState.Is(_targetState))
                    i.SwitchTo(_targetState);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }
}
