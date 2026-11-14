using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.transitions
{
    // Goes at once to one of its states at random, never to one of the last few picked: Oryx 1 draws
    // his next attack this way, as the original game's server did (its "noRepeats" tags). The history
    // is kept on the monster under a key that several of these share, so pools for different health
    // can draw from one history; a state's name counts, whichever pool picked it. `Remember` records a
    // state as picked without going there (an attack entered another way, his opening one). With a
    // prefix, "<prefix>X" and "X" count as the same state (his pause before an attack, and the attack).
    internal class FreshRandomTransition : Transition
    {
        private readonly object _key;
        private readonly int _remember;
        private readonly string _prefix;

        public FreshRandomTransition(object historyKey, int remember, string prefix, params string[] states)
            : base(states)
        {
            _key = historyKey;
            _remember = remember;
            _prefix = prefix;
        }

        private string Name(string state) =>
            _prefix != null && state.StartsWith(_prefix) ? state.Substring(_prefix.Length) : state;

        public static List<string> History(Entity host, object key)
        {
            if (!host.StateStorage.TryGetValue(key, out var o) || !(o is List<string> list))
                host.StateStorage[key] = list = new List<string>();
            return list;
        }

        public static void Remember(Entity host, object key, int remember, string state)
        {
            var list = History(host, key);
            list.Remove(state);
            list.Insert(0, state);
            while (list.Count > remember)
                list.RemoveAt(list.Count - 1);
        }

        protected override bool TickCore(Entity host, TickTime time, ref object state)
        {
            var used = History(host, _key);
            var free = Enumerable.Range(0, TargetStates.Length).Where(i => !used.Contains(Name(TargetStates[i]))).ToArray();
            SelectedState = free.Length > 0 ? free[Random.Next(free.Length)] : Random.Next(TargetStates.Length);
            Remember(host, _key, _remember, Name(TargetStates[SelectedState]));
            return true;
        }
    }
}
