using Shared.resources;
using WorldServer.core.worlds.impl;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;

namespace WorldServer.logic
{
    // The Oryxes in the arenas (ArenaPools.OryxBosses). Each one's first state asks ArenaTransition
    // where he is: in his own dungeon he fights as ever; in an arena he has less health (a BossStats
    // with arena set), wakes invulnerable and holding his fire (ArenaWake), and then repeats only
    // his first phase, which ends nowhere: no invulnerable calls for help, no shields, no rage. He
    // spawns in the arena's middle, since his patterns go back to where he spawned.
    partial class BehaviorDb
    {
        private static State ArenaWake(string taunt, string then) =>
            new State("Arena Wake",
                new ConditionEffectBehavior(ConditionEffectIndex.Invulnerable),
                new Flash(0xFFFFFFFF, 0.4, ArenaPools.OryxWakeMs / 400),
                new Taunt(taunt) { Bubble = false },
                new TimedTransition(ArenaPools.OryxWakeMs, then)
            );
    }
}
