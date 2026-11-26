using System;
using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The King's Royal Decree (<Activate maxDistance radius impactDamage duration tributeMp>RoyalDecree</Activate>,
    // the King's scepters).
    //
    // The King names the enemies that must fall: the royal seal comes down where he aims (at most
    // `maxDistance` tiles off) DecreeFallMs later, and every enemy within `radius` of it is
    // Condemned for `duration`: Cursed (a quarter more damage from every hit, as the Curse ever
    // was; not those immune to it) and marked with his crown. Then the seal strikes them for
    // `impactDamage`, so one it kills is already Condemned.
    //
    // The Crown's Due: a Condemned enemy that dies, by anyone's hand, pays tribute to everyone who
    // helped bring it down (whoever hurt it, and the King): a stack of Tribute each, up to
    // TributeMax, every stack TributeDamage more on each of their shots that strikes an enemy,
    // armour or no (added after defence). TributeHoldMs after their last new stack they fade, one
    // every TributeFadeStepMs. The King also gets `tributeMp` mana for the first TributeMpKills of
    // each decree. So the King picks the targets, the party brings them down, and each one that
    // falls makes the party hit harder.
    //
    // Tribute is a stat (StatDataType.Tribute), sent to everyone: the Rust client adds the same to
    // the hits it works out for its own shots (combat.rs) and draws the coins (decree.rs).
    //
    // A newer decree over an enemy replaces the one on it. Its looks are the Rust client's
    // (particles/decree.rs): a ShowEffect Relic with the look in pos2.y, the decree's tier in
    // ObjectType, its colour (DecreeColors) and its King in SourceId.
    public partial class Player
    {
        public const int LOOK_SEAL = 67;      // the seal falling on pos1 over Duration, pos2.x across
        public const int LOOK_CONDEMN = 68;   // the target Condemned for Duration: the crown over it
        public const int LOOK_TRIBUTE = 69;   // tribute from pos1 to the target, its stacks in pos2.x; ObjectType + 16 if it paid mana
        public const int DecreeFallMs = 260;
        public const float DecreeReach = 9;
        public const int TributeMpKills = 3;
        public const int TributeDamage = 10, TributeMax = 10;
        public const int TributeHoldMs = 10_000, TributeFadeStepMs = 1000;

        // the scepters' colours by tier: Tin, Copper, Bronze, Silver, Gilded, the Lion Court's amber,
        // the Sunlit Throne's gold, the High King's royal violet, the Last Dynasty's crimson;
        // decree.rs knows them too
        public static readonly uint[] DecreeColors =
        {
            0xFFB8C0C8, 0xFFE0885A, 0xFFD8A050, 0xFFE8F0FF, 0xFFFFD050,
            0xFFFFA830, 0xFFFFE890, 0xFFB070FF, 0xFFFF4A5A,
        };

        // A decree on an enemy (Enemy.Condemned): whose, until when, and what it pays when the
        // enemy falls.
        public sealed class Decree
        {
            public Player King;
            public long Until;
            public int Mp, Tier;
            public uint Color;
            // how many of its enemies have paid the King mana so far
            public int Paid;

            public bool Holds(Enemy e) => King?.World != null && King.World == e.World && !King.Dead && Now <= Until;
        }

        private void AERoyalDecree(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            var world = World;
            if (world == null)
                return;
            var spot = ClampAbilityTarget(target, eff.MaximumDistance > 0 ? eff.MaximumDistance : DecreeReach);
            var tier = Math.Clamp(item.Tier, 0, DecreeColors.Length - 1);
            var color = DecreeColors[tier];
            var radius = eff.Radius > 0 ? eff.Radius : 3;
            var decree = new Decree
            {
                King = this,
                Mp = eff.TributeMp,
                Tier = tier,
                Color = color,
            };
            var ms = eff.DurationMS > 0 ? eff.DurationMS : 5000;

            world.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = Id,
                Pos1 = spot,
                Pos2 = new Position(radius, LOOK_SEAL),
                Color = new ARGB(color),
                Duration = DecreeFallMs,
                ObjectType = (ushort)tier,
                SourceId = Id,
            }, ref spot);

            After(DecreeFallMs, (w, t) =>
            {
                if (World != w || Dead)
                    return;
                decree.Until = Now + ms;
                foreach (var e in EnemiesNear(spot.X, spot.Y, radius))
                {
                    Condemn(e, decree, ms);
                    RelicDamage(e, eff.ImpactDamage, ref t, false);
                }
            });
        }

        private void Condemn(Enemy e, Decree decree, int ms)
        {
            e.Condemned = decree;
            e.ApplyConditionEffect(ConditionEffectIndex.Curse, ms);
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = e.Id,
                Pos1 = new Position(e.X, e.Y),
                Pos2 = new Position(e.Size / 100f, LOOK_CONDEMN),
                Color = new ARGB(decree.Color),
                Duration = ms,
                ObjectType = (ushort)decree.Tier,
                SourceId = Id,
            }, e);
        }

        // How much mana a decree's enemy pays its King as it falls: `mp` for each of the first
        // TributeMpKills, nothing after.
        public static int TributeMana(int paidBefore, int mp) => paidBefore < TributeMpKills ? Math.Max(0, mp) : 0;

        private StatTypeValue<int> _tribute;
        // the stacks of Tribute this player carries (0 to TributeMax)
        public int Tribute
        {
            get => _tribute.GetValue();
            private set => _tribute.SetValue(Math.Clamp(value, 0, TributeMax));
        }

        private long _tributeGained, _tributeFadedAt;

        // How much more each of a player's shots does on an enemy with `stacks` of Tribute.
        public static int TributeBonus(int stacks) => TributeDamage * Math.Clamp(stacks, 0, TributeMax);

        private void GainTribute()
        {
            Tribute++;
            _tributeGained = Now;
        }

        private void TributeTick()
        {
            if (Tribute <= 0)
                return;
            var now = Now;
            if (now - _tributeGained < TributeHoldMs || now - _tributeFadedAt < TributeFadeStepMs)
                return;
            _tributeFadedAt = now;
            Tribute--;
        }

        // A Condemned enemy fell (Enemy.Death): the Crown's Due, to everyone who helped bring it
        // down and the King. Each gets a look of their own: the coin from where it fell to them
        // (TargetObjectId), their stacks now in pos2.x, ObjectType + 16 for the King when it paid
        // him mana.
        internal void PayTribute(Enemy e, Decree decree)
        {
            var world = World;
            if (world == null)
                return;
            var at = new Position(e.X, e.Y);
            var mp = TributeMana(decree.Paid++, decree.Mp);
            var paid = new HashSet<Player> { this };
            foreach (var p in e.DamageCounter.GetHitters().Keys)
                if (p != null && p.World == world && !p.Dead)
                    paid.Add(p);
            foreach (var p in paid)
            {
                p.GainTribute();
                world.BroadcastIfVisible(new ShowEffect
                {
                    EffectType = EffectType.Relic,
                    TargetObjectId = p.Id,
                    Pos1 = at,
                    Pos2 = new Position(p.Tribute, LOOK_TRIBUTE),
                    Color = new ARGB(decree.Color),
                    ObjectType = (ushort)(decree.Tier + (p == this && mp > 0 ? 16 : 0)),
                    SourceId = Id,
                }, ref at);
            }
            if (mp > 0)
                ActivateHealMp(this, mp);
        }
    }
}
