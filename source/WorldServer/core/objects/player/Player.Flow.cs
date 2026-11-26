using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The Monk: Chi and the Flowing Strike.
    //
    // Chi: every ChiPunches-th fist shot (a weapon of FistSlotType) that strikes an enemy builds a
    // Chi, up to ChiMax (a shot counts once, however many it passes through). After ChiFadeMs
    // without a punch landing they fade, one every ChiFadeStepMs. The count is the Chi stat, sent
    // to everyone: the Rust client draws the orbs circling the Monk (chi.rs).
    //
    // The Flowing Strike (<Activate maxDistance totalDamage impactDamage radius amount range>FlowStrike</Activate>,
    // the Monk's fountains): the Monk flows to where they aim, at most `maxDistance` tiles, stopping
    // short of a wall, and strikes every enemy within FlowPathWidth of the way for `totalDamage`.
    // Where they land a fountain bursts: `impactDamage` to the enemies within `radius`, and
    // `amount` healed to them and the allies within `range`; both a fifth more (ChiPercent) for
    // each Chi spent, and every Chi is spent. With all five, it's a Flow: the enemies in the burst
    // are Armor Broken and the Monk goes Berserk.
    public partial class Player
    {
        public const int FistSlotType = 200;
        public const int ChiMax = 5;
        public const int ChiPunches = 3;
        public const int ChiFadeMs = 4000, ChiFadeStepMs = 1000;
        public const int ChiPercent = 20;
        public const float FlowPathWidth = 1.2f;
        public const int FlowDashMs = 160;
        public const int FlowArmorBreakMs = 3000, FlowBerserkMs = 4000;
        // the Relic look the Rust client draws the strike by (particles/flow.rs)
        public const int LOOK_FLOW = 66;

        // the fountains' colours by tier (Droplet to Heart of the Fountains); flow.rs knows them too
        public static readonly uint[] FlowColors =
        {
            0xFF9AD8FF, 0xFF7FE6E0, 0xFF6EF0A8, 0xFF4FC3FF, 0xFF4A8CFF,
            0xFFB48CFF, 0xFFFF7A9A, 0xFFFFC857, 0xFFFFF4D6,
        };

        private StatTypeValue<int> _chi;
        public int Chi
        {
            get => _chi.GetValue();
            private set => _chi.SetValue(Math.Clamp(value, 0, ChiMax));
        }

        private int _chiShot = -1;
        private int _chiPunches;
        private long _chiLastHit, _chiFadedAt;

        // a weapon shot (`bulletId`) struck an enemy
        private void ChiHit(int bulletId)
        {
            if (Inventory[0]?.SlotType != FistSlotType || bulletId == _chiShot)
                return;
            _chiShot = bulletId;
            _chiLastHit = Now;
            if (++_chiPunches < ChiPunches)
                return;
            _chiPunches = 0;
            if (Chi < ChiMax)
                Chi++;
        }

        private void ChiTick()
        {
            if (Chi <= 0)
                return;
            var now = Now;
            if (now - _chiLastHit < ChiFadeMs || now - _chiFadedAt < ChiFadeStepMs)
                return;
            _chiFadedAt = now;
            Chi--;
        }

        // How much more a strike spending `chi` does, in percent of its own (100 with none).
        public static int ChiBoost(int chi) => 100 + ChiPercent * Math.Clamp(chi, 0, ChiMax);

        // Where a strike aimed at `target` ends: along the way, at most `max` tiles off, stopped
        // before the first square `passable` refuses (checked every FlowStep tiles).
        private const float FlowStep = 0.2f;
        public static Position FlowEnd(float x, float y, Position target, float max, Func<double, double, bool> passable)
        {
            var (dx, dy) = (target.X - x, target.Y - y);
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f)
                return new Position(x, y);
            var reach = Math.Min(len, max);
            var (ux, uy) = (dx / len, dy / len);
            var end = new Position(x, y);
            for (var d = FlowStep; d <= reach + 0.001f; d += FlowStep)
            {
                var (px, py) = (x + ux * Math.Min(d, reach), y + uy * Math.Min(d, reach));
                if (!passable(px, py))
                    break;
                end = new Position(px, py);
            }
            return end;
        }

        private void AEFlowStrike(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            var world = World;
            if (world == null)
                return;
            var max = eff.MaximumDistance > 0 ? eff.MaximumDistance : 6;
            var from = new Position(X, Y);
            var end = FlowEnd(X, Y, target, max, (px, py) => world.IsPassable(px, py));
            // the Oryx chain's gates: no flowing past one still shut, the burst where they stand
            if (!CanAbilityMoveTo(end))
                end = from;

            var chi = Chi;
            Chi = 0;
            _chiPunches = 0;
            var tier = Math.Clamp(item.Tier, 0, FlowColors.Length - 1);
            var color = FlowColors[tier];
            var radius = eff.Radius > 0 ? eff.Radius : 3;

            // drawn first, so the clients still have the Monk where they started (flow.rs)
            world.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = Id,
                Pos1 = end,
                Pos2 = new Position(radius, LOOK_FLOW),
                Color = new ARGB(color),
                Duration = FlowDashMs,
                ObjectType = (ushort)(tier + 16 * chi),
                SourceId = Id,
            }, ref from);

            // the way: every enemy beside it, struck as the Monk passes
            var (dx, dy) = (end.X - from.X, end.Y - from.Y);
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.3f)
                foreach (var e in EnemiesNear(from.X + dx / 2, from.Y + dy / 2, len / 2 + FlowPathWidth))
                {
                    if (!TidePearl.OnWay(from.X, from.Y, end.X, end.Y, e.X, e.Y, FlowPathWidth, out var u))
                        continue;
                    var struck = e;
                    var damage = eff.TotalDamage;
                    After(Math.Max(1, (int)(u * FlowDashMs)), (w, t) => RelicDamage(struck, damage, ref t, false));
                }

            if (World.Levels != null)
                TeleportPosition(time, end, true);
            else if (len > 0.05f)
                Shift(time, end);

            // the fountain bursting where they land
            var boost = ChiBoost(chi);
            var burst = eff.ImpactDamage * boost / 100;
            var heal = eff.Amount * boost / 100;
            var range = eff.Range > 0 ? eff.Range : radius;
            After(FlowDashMs, (w, t) =>
            {
                if (World != w || Dead)
                    return;
                foreach (var e in EnemiesNear(end.X, end.Y, radius))
                {
                    RelicDamage(e, burst, ref t, false);
                    if (chi >= ChiMax && !e.Dead)
                        e.ApplyConditionEffect(ConditionEffectIndex.ArmorBroken, FlowArmorBreakMs);
                }
                foreach (var p in w.Players.Values)
                    if (!p.Dead && p.DistTo(end.X, end.Y) <= range)
                        ActivateHealHp(p, heal, false, this);
                HealTurrets(end.X, end.Y, range, heal);
                if (chi >= ChiMax)
                    ApplyConditionEffect(ConditionEffectIndex.Berserk, FlowBerserkMs);
            });
        }
    }
}
