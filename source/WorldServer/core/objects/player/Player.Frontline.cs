using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // Frontline: the sword classes' passive (every class whose weapon slot takes swords: the Warrior,
    // Knight, Paladin and Pirate). They fight a few tiles from a boss, in twice the bullets the
    // ranged classes see, so they take a share off every hit and heal from what their sword deals.
    // The Paladin's healing reaches the players round him; the Pirate shrugs off the odd shot.
    // The Rust client takes the same share off the hits it shows (combat.rs, `frontline`).
    public partial class Player
    {
        public const int SwordSlotType = 1;
        public const int FrontlineCutPercent = 15;
        // the Paladin's leech heals the other players within this many tiles as much as him
        public const float PaladinLeechRange = 4f;
        // the Pirate's chance in 100 of a shot missing him altogether
        public const int PirateLuckPercent = 10;

        private const string PaladinId = "Paladin";
        private const string PirateId = "Pirate (Doomed)";

        private readonly FrontlineLeech _leech = new FrontlineLeech();

        public bool Frontline => ObjectDesc.SlotTypes is { Length: > 0 } s && s[0] == SwordSlotType;

        // what a hit of `dmg` (after defense and a forcefield) leaves a sword class with
        public int FrontlineDamage(int dmg) => Frontline ? FrontlineCut(dmg) : dmg;

        // the share taken off rounded to the nearest point, as the client works it out
        public static int FrontlineCut(int dmg) => dmg <= 0 ? dmg : dmg - (dmg * FrontlineCutPercent + 50) / 100;

        // whether a shot that hit the Pirate missed him by luck: he takes nothing and none of its
        // effects, and "Lucky!" shows over him
        public bool PirateDodged()
        {
            if (ObjectDesc.IdName != PirateId || Random.Shared.Next(100) >= PirateLuckPercent)
                return false;
            World?.BroadcastIfVisible(new Notification { ObjectId = Id, Message = "Lucky!", Color = new ARGB(0xFFFFD34D) }, this);
            return true;
        }

        // the sword's shot dealt `dmg` to an enemy: heal a share of it, at most once a second
        private void FrontlineHit(int dmg)
        {
            if (!Frontline || Dead || Ghost || Health <= 0)
                return;
            var heal = _leech.Add(dmg, Environment.TickCount64);
            if (heal <= 0)
                return;
            LeechHeal(this, heal);
            if (ObjectDesc.IdName != PaladinId || World == null)
                return;
            foreach (var p in World.Players.Values)
                if (p != this && !p.Dead && !p.Ghost && p.Health > 0 && p.DistSqr(this) <= PaladinLeechRange * PaladinLeechRange)
                    LeechHeal(p, heal);
        }

        private void LeechHeal(Player p, int amount)
        {
            if (p.HasConditionEffect(ConditionEffectIndex.Sick))
                return;
            var healed = Math.Min(amount, p.Stats[0] - p.Health);
            if (healed <= 0)
                return;
            p.HealedBy(this, healed, p.Stats[0]);
            p.Health += healed;
            p.World?.BroadcastIfVisible(new Notification { ObjectId = p.Id, Message = "+" + healed, Color = new ARGB(0xFF00FF00) }, p);
        }

        private float DistSqr(Entity e)
        {
            var dx = X - e.X;
            var dy = Y - e.Y;
            return dx * dx + dy * dy;
        }
    }

    // A sword class's leech: 1.5% of what its sword deals, at most 40 health a second, paid out at
    // most once a second so the numbers over the player stay readable.
    public sealed class FrontlineLeech
    {
        public const double Share = 0.015;
        public const double MaxPerSecond = 40;
        public const long PayEveryMs = 1000;

        private double _owed;
        private double _allowance = MaxPerSecond;
        private long _filledAt = -1;
        private long _paidAt = long.MinValue / 2;

        // what to heal now after a hit that dealt `dmg` at `now` (ms); 0 to wait
        public int Add(int dmg, long now)
        {
            if (_filledAt < 0)
                _filledAt = now;
            _allowance = Math.Min(MaxPerSecond, _allowance + (now - _filledAt) * MaxPerSecond / 1000.0);
            _filledAt = now;
            if (dmg > 0)
                _owed = Math.Min(_owed + dmg * Share, MaxPerSecond);
            if (now - _paidAt < PayEveryMs)
                return 0;
            var heal = (int)Math.Min(_owed, _allowance);
            if (heal <= 0)
                return 0;
            _owed -= heal;
            _allowance -= heal;
            _paidAt = now;
            return heal;
        }
    }
}
