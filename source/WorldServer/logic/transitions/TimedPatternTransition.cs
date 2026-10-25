using System.Collections.Generic;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    // TimedRandomTransition for a boss's patterns: after `time` it picks one of `states` at random,
    // but never the same one more than `maxRun` times in a row. Every transition with the same list
    // of states shares the host's record of what it picked (Oryx 5's patterns each end in one, and
    // the state before them starts them).
    internal class TimedPatternTransition : Transition
    {
        //State storage: cooldown timer; the shared record is under _key

        private readonly int _time;
        private readonly int _maxRun;
        private readonly string _key;

        private sealed class Run
        {
            public int Last = -1;
            public int Count;
        }

        public TimedPatternTransition(int time, params string[] states) : this(time, 2, states) { }

        public TimedPatternTransition(int time, int maxRun, params string[] states)
            : base(states)
        {
            _time = time;
            _maxRun = maxRun;
            _key = "patterns:" + string.Join("|", states);
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = state == null ? _time : (int)state;
            if (cool > 0)
            {
                state = cool - time.ElapsedMsDelta;
                return false;
            }
            state = _time;

            if (!host.StateStorage.TryGetValue(_key, out var stored) || !(stored is Run run))
                host.StateStorage[_key] = run = new Run();
            var choices = new List<int>();
            for (var i = 0; i < TargetStates.Length; i++)
                if (i != run.Last || run.Count < _maxRun)
                    choices.Add(i);
            SelectedState = choices[Random.Next(choices.Count)];
            run.Count = SelectedState == run.Last ? run.Count + 1 : 1;
            run.Last = SelectedState;
            return true;
        }
    }
}
