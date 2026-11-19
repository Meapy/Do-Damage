using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // Standing in an Ascendant's forcefield (Forcefield.cs): a percentage off every hit taken.
    public partial class Player
    {
        // the fields the player stands in, by the field's id: its percent, until when, its colour
        private readonly Dictionary<int, (int Percent, long Until, uint Color)> _shields = new();
        private long _shieldFxAt;
        private uint _shieldColor = Forcefield.COLOR;
        private long _vowUntil;     // in the Ghost Bride's halo's forcefield until (VowedDamage)
        private long _vowReadyAt;   // when its vow may save this player again

        // the most a stack of forcefields takes off, however many
        public const int SHIELD_CAP = 90;

        // what overlapping forcefields take off together: the strongest in full, the next at half,
        // the next at a quarter, and so on, at most SHIELD_CAP. The Rust client works it out the
        // same way (combat.rs, `stacked_shield`).
        public static int StackedShield(IEnumerable<int> percents) => Math.Min(Stacked(percents), SHIELD_CAP);

        // layers of one thing stacking (forcefields, the Father's wards): the strongest in full, each
        // after it at half the one before (rounded down)
        public static int Stacked(IEnumerable<int> values)
        {
            var total = 0;
            var i = 0;
            foreach (var v in values.Where(v => v > 0).OrderByDescending(v => v))
            {
                if (i >= 30)
                    break;
                total += v >> i++;
            }
            return total;
        }

        // in the forcefield `fieldId` for the next `holdMs`; overlapping ones stack (StackedShield).
        // A vow's (the Ghost Bride's halo) keeps the player from dying too.
        public void Shield(int fieldId, int percent, int holdMs, uint color = Forcefield.COLOR, bool vow = false)
        {
            // nothing to shield in a ghost (Player.Grave.cs)
            if (Ghost)
                return;
            var now = Environment.TickCount64;
            if (vow)
                _vowUntil = Math.Max(_vowUntil, now + holdMs);
            _shields[fieldId] = (percent, now + holdMs, color);
        }

        // the percent the fields the player stands in take off now, and the strongest one's colour
        private int ShieldPercent(long now)
        {
            if (_shields.Count == 0)
                return 0;
            List<int> gone = null;
            var best = -1;
            foreach (var (id, s) in _shields)
            {
                if (now >= s.Until)
                    (gone ??= new List<int>()).Add(id);
                else if (s.Percent > best)
                {
                    best = s.Percent;
                    _shieldColor = s.Color;
                }
            }
            if (gone != null)
                foreach (var id in gone)
                    _shields.Remove(id);
            return StackedShield(_shields.Values.Select(s => s.Percent));
        }

        // what a hit of `dmg` (after defense) does in the forcefields the player stands in: the
        // part taken off rounded to the nearest point, as the Rust client works out the number it
        // shows (combat.rs, `shielded`)
        public int ShieldedDamage(int dmg)
        {
            if (dmg <= 0)
                return dmg;
            var percent = ShieldPercent(Environment.TickCount64);
            if (percent <= 0)
                return dmg;
            dmg -= (dmg * percent + 50) / 100;

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
