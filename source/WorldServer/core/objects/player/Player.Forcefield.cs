using System;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // Standing in an Ascendant's forcefield (Forcefield.cs): a percentage off every hit taken.
    public partial class Player
    {
        private int _shieldPercent;
        private long _shieldUntil;
        private long _shieldFxAt;
        private uint _shieldColor = Forcefield.COLOR;

        // in a forcefield for the next `holdMs`; of several overlapping, the strongest counts
        public void Shield(int percent, int holdMs, uint color = Forcefield.COLOR)
        {
            var now = Environment.TickCount64;
            if (now >= _shieldUntil || percent >= _shieldPercent)
            {
                _shieldPercent = percent;
                _shieldColor = color;
            }
            _shieldUntil = Math.Max(_shieldUntil, now + holdMs);
        }

        // what a hit of `dmg` (after defense) does in the forcefield the player stands in: the
        // part taken off rounded to the nearest point, as the Rust client works out the number it
        // shows (combat.rs, `shielded`)
        public int ShieldedDamage(int dmg)
        {
            if (dmg <= 0 || _shieldPercent <= 0 || Environment.TickCount64 >= _shieldUntil)
                return dmg;
            dmg -= (dmg * _shieldPercent + 50) / 100;

            // the field flares round the player it turned a blow from, at most every 250 ms
            var now = Environment.TickCount64;
            if (World != null && now >= _shieldFxAt)
            {
                _shieldFxAt = now + 250;
                var at = new Position(X, Y);
                World.BroadcastIfVisible(new ShowEffect
                {
                    EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at,
                    Pos2 = new Position(0.9f, Forcefield.LOOK_BLOCK), Color = new ARGB(_shieldColor),
                    SourceId = Id,
                }, ref at);
            }
            return dmg;
        }
    }
}
