using Shared.resources;
using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.utils;
using WorldServer.core.net.stats;

namespace WorldServer.core
{
    internal class BoostStatManager
    {
        public ActivateBoost[] ActivateBoost;
        private int[] _boost;
        private StatTypeValue<int>[] _boostSV;
        private StatsManager _parent;
        private Player _player;

        public BoostStatManager(StatsManager parent)
        {
            _parent = parent;
            _player = parent.Owner;
            _boost = new int[StatsManager.NumStatTypes];
            _boostSV = new StatTypeValue<int>[_boost.Length];

            for (var i = 0; i < _boostSV.Length; i++)
                _boostSV[i] = new StatTypeValue<int>(_player, StatsManager.GetBoostStatType(i), _boost[i], i != 0 && i != 1);

            ActivateBoost = new ActivateBoost[_boost.Length];
            for (var i = 0; i < ActivateBoost.Length; i++)
                ActivateBoost[i] = new ActivateBoost();

            ReCalculateValues();
        }

        public int this[int index] => _boost[index];

        protected internal void ReCalculateValues()
        {
            for (var i = 0; i < _boost.Length; i++)
                _boost[i] = 0;

            ApplyEquipBonus();
            ApplyActivateBonus();
            IncrementStatBoost();
            ApplyImprintScaling();

            for (var i = 0; i < _boost.Length; i++)
                _boostSV[i].SetValue(_boost[i]);
        }

        // the Boost condition of each stat, in StatsManager's order (HP, MP, Att, Def, Spd, Dex, Vit,
        // Wis), which isn't the conditions' order (... Spd, Vit, Wis, Dex)
        private static readonly ConditionEffectIndex[] BoostConditions =
        {
            ConditionEffectIndex.HpBoost, ConditionEffectIndex.MpBoost, ConditionEffectIndex.AttBoost,
            ConditionEffectIndex.DefBoost, ConditionEffectIndex.SpdBoost, ConditionEffectIndex.DexBoost,
            ConditionEffectIndex.VitBoost, ConditionEffectIndex.WisBoost
        };

        public void ExtendBoostCondition(int index, int durationMs) =>
            _player.ExtendConditionEffect(BoostConditions[index], durationMs);

        private void ApplyActivateBonus()
        {
            for (var i = 0; i < ActivateBoost.Length; i++)
            {
                // set boost
                var b = ActivateBoost[i].GetBoost();
                _boost[i] += b;

                // the stat's Boost condition shows while a timed boost (StatBoostSelf, StatBoostAura)
                // lasts: its icon over the player and its name
                var effect = BoostConditions[i];
                var haveCondition = _player.HasConditionEffect(effect);
                if (b > 0)
                {
                    if (!haveCondition)
                        _player.ApplyPermanentConditionEffect(effect);
                }
                else
                {
                    if (haveCondition)
                        _player.RemoveCondition(effect);
                }
            }
        }

        private void ApplyEquipBonus()
        {
            for (var i = 0; i < 4; i++)
            {
                if (_player.Inventory[i] == null)
                    continue;
                // a ring with Guardian Angel gives its imprint's stats in place of its own
                if (i == 3 && _player.RingGuarded)
                    continue;

                foreach (var b in _player.Inventory[i].ActivateOnEquips)
                    IncrementBoost((StatDataType)b.Key, b.Value);
            }

            for (var i = 20; i < 28; i++)
            {
                if (_player.Inventory[i] == null || _player.Inventory[i].SlotType != 26)
                    continue;
                foreach (var b in _player.Inventory[i].ActivateOnEquips)
                    IncrementBoost((StatDataType)b.Key, b.Value);
            }

            // the equipped gear's imprints (Player.Imprints.cs)
            foreach (var (stat, amount) in _player.ImprintStats())
                IncrementBoost(stat, amount);

            // the guild hall's (GuildHalls.BuffsOf)
            var guild = _player.GuildBuffs;
            if (guild.HasStats)
            {
                IncrementBoost(StatDataType.MaximumHealth, guild.Health);
                IncrementBoost(StatDataType.MaximumMana, guild.Mana);
                IncrementBoost(StatDataType.Vitality, guild.Vitality);
                IncrementBoost(StatDataType.Wisdom, guild.Wisdom);
            }
        }

        // The Apotheosis imprints, after everything else: a share more of what gear and boosts add to
        // a stat, and the Apotheosis of Balance's fifth off all of two (never below 1, as the rest)
        private void ApplyImprintScaling()
        {
            var (added, lowered) = _player.ImprintScaling();
            for (var i = 0; i < 8 && i < _boost.Length; i++)
            {
                if (added[i] > 0 && _boost[i] > 0)
                    _boost[i] += _boost[i] * added[i] / 100;
                if (lowered[i])
                {
                    var total = _parent.Base[i] + _boost[i];
                    var cut = total * 20 / 100;
                    if (total - cut < 1)
                        cut = total - 1;
                    if (cut > 0)
                        _boost[i] -= cut;
                }
            }
        }

        private void FixedStat(StatDataType stat, int value)
        {
            var i = StatsManager.GetStatIndex(stat);
            _boost[i] = value - _parent.Base[i];
        }

        private int IncreasePercentage(int percentageToIncrease, int stat)
        {
            int percentage = percentageToIncrease;
            var result = percentage * _parent.Base[stat] / 100;
            return result;
        }

        public void IncrementBoost(StatDataType stat, int amount)
        {
            var i = StatsManager.GetStatIndex(stat);

            if (_parent.Base[i] + amount < 1)
                amount = i == 0 ? -_parent.Base[i] + 1 : -_parent.Base[i];

            _boost[i] += amount;
        }

        private void IncrementStatBoost()
        {
            if (_player == null || _player.Client == null || _player.Client.Account == null)
                return;

            for (var a = 0; a < 8; a++)
            {
                if (a >= 7)
                    a = 7;

                if (a >= 8)
                    return;
                if (_player.Client.Account.SetBaseStat > 0)
                    _boost[a] += a < 2 ? _player.Client.Account.SetBaseStat * 5 : _player.Client.Account.SetBaseStat;
            }
        }
    }
}
