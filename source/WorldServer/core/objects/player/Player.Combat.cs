using System;

namespace WorldServer.core.objects
{
    // Out of combat speed: a player who hasn't been hurt by an enemy, nor hurt one, for a few
    // seconds walks faster (+20 Speed, BoostStatManager). The boost goes the moment either happens
    // and comes back once things have been quiet again for CombatMs. It's an ordinary stat boost,
    // so the client moves faster by itself and shows it as the Speed stat's bonus.
    partial class Player
    {
        public const int OutOfCombatSpeed = 20;
        private const long CombatMs = 10000;

        private long _lastCombatMs = long.MinValue / 2;

        // the state the stats were last worked out for: read by BoostStatManager, changed only
        // here so the boost and the stat it shows agree
        public bool OutOfCombat { get; private set; } = true;

        // hit by an enemy, or hitting one
        public void EnterCombat()
        {
            _lastCombatMs = Environment.TickCount64;
            if (OutOfCombat)
                SetOutOfCombat(false);
        }

        private void TickCombat()
        {
            if (!OutOfCombat && Environment.TickCount64 - _lastCombatMs >= CombatMs)
                SetOutOfCombat(true);
        }

        private void SetOutOfCombat(bool value)
        {
            OutOfCombat = value;
            Stats?.ReCalculateValues();
        }
    }
}
