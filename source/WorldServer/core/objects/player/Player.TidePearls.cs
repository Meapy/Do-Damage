using System;
using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The Druid's pearls (TidePearl.cs). A pearl
    // (<Activate range radius duration impactDamage pulseDamage pulseHeal totalDamage amount pearls>TidePearl</Activate>)
    // is thrown to where it was aimed, at most `range` tiles off (UseItem refuses a spot that isn't
    // open floor, before the mana is spent), and lands PearlThrowMs later with a splash
    // (`impactDamage` to the enemies within PearlSplashRadius). There it lies in a tide pool of
    // `radius` for `duration`: every TidePearl.PULSE_MS the enemies in it take `pulseDamage`, past
    // their defense, and are slowed, and the players and turrets in it are mended `pulseHeal`.
    //
    // Then it's recalled: its time up, or a pearl thrown while `pearls` are already out (the
    // oldest comes back). It flies home to its Druid, striking each enemy within PearlRecallWidth
    // of its way once for `totalDamage`, and in their hand heals them `amount`, a fifth more for
    // each enemy it struck (at most twice as much, for PearlStruckMax). So the Druid plays the
    // tide: throw, stand where the enemies are between them and the pool, throw again.
    public partial class Player
    {
        public const int PearlThrowMs = 380;
        private const float PearlRange = 8;
        private const float PearlSplashRadius = 2;
        private const float PearlRecallWidth = 1.1f;
        private const float PearlRecallSpeed = 26; // tiles a second
        private const int PearlSlowMs = 700;
        private const int PearlStruckPercent = 20, PearlStruckMax = 5;

        private readonly List<TidePearl> _pearls = new List<TidePearl>();

        // where a pearl aimed at `target` lands
        private Position PearlSpot(Position target, ActivateEffect eff) =>
            ClampAbilityTarget(target, eff.Range > 0 ? eff.Range : PearlRange);

        private void AETidePearl(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            var spot = PearlSpot(target, eff);

            // one past the most that may be out: the oldest comes home
            _pearls.RemoveAll(p => p.Gone);
            var max = Math.Max(1, eff.MaxPearls);
            while (_pearls.Count >= max)
            {
                var oldest = _pearls[0];
                _pearls.RemoveAt(0);
                if (oldest.World == null)
                    oldest.Cancel();
                else
                    oldest.Recall(ref time);
            }

            var pearl = new TidePearl(this, eff, item);
            _pearls.Add(pearl);

            // the pearl flying from the Druid to the spot (tide.rs, look 61)
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = Id,
                Pos1 = spot,
                Pos2 = new Position(pearl.Radius, TidePearl.LOOK_THROW),
                Color = new ARGB(pearl.Color),
                Duration = PearlThrowMs,
                ObjectType = (ushort)pearl.Tier,
                SourceId = Id,
            }, this);

            var world = World;
            var z = Z;
            world.StartNewTimer(PearlThrowMs, (w, t) =>
            {
                if (pearl.Gone || World != w || Dead)
                {
                    pearl.Cancel();
                    return;
                }
                pearl.Move(spot.X, spot.Y);
                pearl.Z = z;
                w.EnterWorld(pearl);
            });
        }

        // landed: the splash
        internal void PearlLands(TidePearl pearl, ref TickTime time)
        {
            if (World == null)
                return;
            foreach (var e in EnemiesNear(pearl.X, pearl.Y, PearlSplashRadius))
                RelicDamage(e, pearl.Eff.ImpactDamage, ref time, false);
            // Nerissa's: the sea answers (Player.CruciblePowers.cs)
            if (pearl.Tier == TidePearl.NERISSA)
                NerissaTide(new Position(pearl.X, pearl.Y), ref time);
        }

        // a pulse of its pool
        internal void PearlPulse(TidePearl pearl, ref TickTime time)
        {
            if (World == null)
                return;
            foreach (var e in EnemiesNear(pearl.X, pearl.Y, pearl.Radius))
            {
                RelicDamage(e, pearl.Eff.PulseDamage, ref time, true);
                if (!e.Dead)
                    e.ApplyConditionEffect(ConditionEffectIndex.Slowed, PearlSlowMs);
            }
            var heal = pearl.Eff.PulseHeal;
            if (heal <= 0)
                return;
            foreach (var p in World.Players.Values)
                if (!p.Dead && p.DistTo(pearl.X, pearl.Y) <= pearl.Radius)
                    Mend(p, heal);
            HealTurrets(pearl.X, pearl.Y, pearl.Radius, heal);
        }

        // a pool's heal: quietly, without the potion's sparkle and number every half second
        private void Mend(Player p, int amount)
        {
            if (p.HasConditionEffect(ConditionEffectIndex.Sick))
                return;
            var max = p.Stats[0];
            var hp = Math.Min(max, p.Health + amount);
            if (hp <= p.Health)
                return;
            p.HealedBy(this, hp - p.Health, max);
            p.Health = hp;
        }

        // the pearl flying home from its pool: what it strikes on the way, as it gets there, and
        // the heal when it's back in hand
        internal void RecallPearl(TidePearl pearl, ref TickTime time)
        {
            if (World == null || Dead)
                return;
            var from = new Position(pearl.X, pearl.Y);
            var (dx, dy) = (X - from.X, Y - from.Y);
            var len = MathF.Sqrt(dx * dx + dy * dy);
            var flightMs = (int)Math.Clamp(len / PearlRecallSpeed * 1000, 120, 450);

            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = pearl.Id,
                Pos1 = from,
                Pos2 = new Position(PearlRecallWidth, TidePearl.LOOK_RECALL),
                Color = new ARGB(pearl.Color),
                Duration = flightMs,
                ObjectType = (ushort)pearl.Tier,
                SourceId = Id,
            }, ref from);

            var struck = 0;
            var damage = pearl.Eff.TotalDamage;
            foreach (var e in EnemiesNear(from.X + dx / 2, from.Y + dy / 2, len / 2 + PearlRecallWidth))
            {
                // on its way (u: how far along, 0 the pool, 1 the Druid)?
                if (!TidePearl.OnWay(from.X, from.Y, X, Y, e.X, e.Y, PearlRecallWidth, out var u))
                    continue;
                struck++;
                var target = e;
                After(Math.Max(1, (int)(u * flightMs)), (w, t) => RelicDamage(target, damage, ref t, false));
            }

            var heal = pearl.Eff.Amount;
            var color = pearl.Color;
            After(flightMs, (w, t) => PearlHome(struck, heal, color));
        }

        private void PearlHome(int struck, int heal, uint color)
        {
            if (World == null || Dead)
                return;
            var at = new Position(X, Y);
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = Id,
                Pos1 = at,
                Pos2 = new Position(1, TidePearl.LOOK_HOME),
                Color = new ARGB(color),
                ObjectType = (ushort)Math.Min(struck, 99),
                SourceId = Id,
            }, ref at);
            if (heal > 0)
                ActivateHealHp(this, heal * (100 + PearlStruckPercent * Math.Min(struck, PearlStruckMax)) / 100);
        }
    }
}
