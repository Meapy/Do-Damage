using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // Equipped items' stat boosts that come by themselves (Item.LowHealthBoost and AbilityBoost):
    // the Ascended Gem of Adoration's Vitality when health first drops below a fifth, and the
    // Ascended Gem of Tenderness's chance of Wisdom with each use of the ability.
    partial class Player
    {
        // whether each equipment slot's low-health boost can go off again: it has once health has
        // been back above the line since it last did
        private readonly bool[] _lowHealthArmed = { true, true, true, true };

        private void LowHealthBoosts()
        {
            for (var slot = 0; slot < 4; slot++)
            {
                if (Inventory[slot]?.LowHealthBoost is not { } b)
                {
                    _lowHealthArmed[slot] = true;
                    continue;
                }
                var below = Health < Stats[0] * b.Value;
                if (below && _lowHealthArmed[slot])
                {
                    _lowHealthArmed[slot] = false;
                    TimedStatBoost(b.Stat, b.Amount, b.DurationMs);
                }
                else if (!below)
                    _lowHealthArmed[slot] = true;
            }
        }

        // after the ability was used: each equipped item's chance of its boost
        private void AbilityBoosts()
        {
            for (var slot = 0; slot < 4; slot++)
                if (Inventory[slot]?.AbilityBoost is { } b && Random.Shared.NextDouble() < b.Value)
                    TimedStatBoost(b.Stat, b.Amount, b.DurationMs);
        }

        // as StatBoostSelf, but not stacking: a second one while the first lasts keeps the amount
        // up until both end (StatBoostSelf's stack, at half for each more, read 87 for three 50s)
        private void TimedStatBoost(int stat, int amount, int durationMs)
        {
            var idx = StatsManager.GetStatIndex((StatDataType)stat);
            if (idx < 0 || World == null)
                return;
            Stats.Boost.ActivateBoost[idx].Push(amount, true);
            Stats.ReCalculateValues();
            World.StartNewTimer(durationMs, (world, t) =>
            {
                Stats.Boost.ActivateBoost[idx].Pop(amount, true);
                Stats.ReCalculateValues();
            });
            World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.Potion,
                TargetObjectId = Id,
                Color = new ARGB(0xffffffff)
            }, this);
        }
    }
}
