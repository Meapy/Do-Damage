using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.logic.transitions;

namespace WorldServer.logic.behaviors
{
    // On entering its state, records these states as the last ones a FreshRandomTransition with the
    // same key picked, oldest first, so its next draws skip them (Oryx 1's opening attack, and the two
    // the original game counted as used before it).
    internal class RememberPicks : Behavior
    {
        private readonly object _key;
        private readonly int _remember;
        private readonly string[] _states;

        public RememberPicks(object historyKey, int remember, params string[] states)
        {
            _key = historyKey;
            _remember = remember;
            _states = states;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            foreach (var s in _states)
                FreshRandomTransition.Remember(host, _key, _remember, s);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }
}
