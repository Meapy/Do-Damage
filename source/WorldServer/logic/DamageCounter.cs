using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.resources;
using Shared.quests;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;

namespace WorldServer.logic
{
    public class DamageCounter
    {
        private WeakDictionary<Player, int> Hitters;

        // assist credit (AssistCredit): what healers, buffers and debuffers earned on this enemy,
        // and who has debuffed it
        private WeakDictionary<Player, double> Assists;
        private AssistCover Debuffers;

        public Enemy Host;

        public DamageCounter(Enemy enemy) => Host = enemy;

        public DamageCounter Corpse { get; set; }
        public Player LastHitter { get; private set; }
        public DamageCounter Parent { get; set; }
        public int TotalDamage { get; private set; }

        public WeakDictionary<Player, int> GetHitters()
        {
            if (Hitters == null)
                Hitters = new WeakDictionary<Player, int>();
            return Hitters;
        }

        private bool Dead;
        public void Death()
        {
            if (Dead)
                return;
            Dead = true;

            if (Corpse != null)
            {
                Corpse.Parent = this;
                return;
            }

            var enemy = (Parent ?? this).Host;

            if (enemy.World is RealmWorld)
                (enemy.World as RealmWorld).EnemyKilled(enemy, (Parent ?? this).LastHitter);

            var levelUpAssists = 0;
            // quests and achievements: everyone near counts the kill, as they share its experience
            var questKill = QuestTracker.KillCounts(enemy) ? QuestTracker.KillEvent(enemy) : (QuestEvent?)null;
            foreach (var player in enemy.World.Players.Values)
            {
                // left the game: the connection let go of the account, and the world drops the
                // player on its next tick (crediting them threw, and cut off whoever made the kill)
                if (player.Client?.Account == null)
                    continue;
                if (player.SqDistTo(enemy) >= World.CULL_RANGE * World.CULL_RANGE)
                    continue;

                var level = player.Level;
                if (player.HasConditionEffect(ConditionEffectIndex.Paused))
                    continue;

                var xp = enemy.ObjectDesc.MaxHP / 10.0f * enemy.ObjectDesc.ExpMultiplier * enemy.XpScale;
                var upperLimit = player.ExperienceGoal * 0.1f;
                if (player.Quest == enemy)
                    upperLimit = player.ExperienceGoal * 0.5f;
                // monsters worth more (Elder ones) raise the cap with the experience
                xp *= enemy.ObjectDesc.XpBonus;
                upperLimit *= enemy.ObjectDesc.XpBonus;

                float playerXp;
                if (upperLimit < xp)
                    playerXp = upperLimit;
                else
                    playerXp = xp;

                if (player.Level < 20 && player.XPBoostTime != 0)
                    playerXp *= 1.5f;

                var xpEvent = player.Client.GameServer.Configuration.serverSettings.expEvent;
                if (xpEvent > 0)
                    playerXp *= (float)(1 + xpEvent);

                // the Hall of Champions' Weekly Fame board: more experience, so more fame at 20
                var champXp = player.Champion.Experience;
                if (champXp > 0)
                    playerXp *= (float)(1 + champXp);

                // the guild hall's
                var guildXp = player.GuildBuffs.Experience;
                if (guildXp > 0)
                    playerXp *= (float)(1 + guildXp);

                // at level 20 experience only makes fame, so a world's KillFameRate (the arenas) cuts it
                playerXp = FameRates.Experience(playerXp, player.Level, enemy.World?.KillFameRate ?? 1);

                if (enemy.GivesNoXp)
                    playerXp = 0;

                var killer = (Parent ?? this).LastHitter == player;
                if (player.EnemyKilled(enemy, (int)playerXp, killer) && !killer)
                    levelUpAssists++;

                if (enemy.ObjectDesc.Quest)
                {
                    enemy.World.BossCredit.Add(player.AccountId);
                    enemy.World.StrongestBossHp = Math.Max(enemy.World.StrongestBossHp, enemy.ObjectDesc.MaxHP);
                }
                if (questKill.HasValue)
                    player.Quests?.Event(questKill.Value);
            }

            if ((Parent ?? this).LastHitter != null)
                (Parent ?? this).LastHitter.FameCounter.LevelUpAssist(levelUpAssists);
        }

        public Tuple<Player, int>[] GetPlayerData()
        {
            if (Parent != null)
                return Parent.GetPlayerData();
            List<Tuple<Player, int>> dat = new List<Tuple<Player, int>>();

            var hitters = GetHitters();
            foreach (var i in hitters)
            {
                if (i.Key == null || i.Key.World == null || i.Key.World.GetEntity(i.Key.Id) == null) continue;

                dat.Add(new Tuple<Player, int>(i.Key, i.Value));
            }
            return dat.ToArray();
        }

        public WeakDictionary<Player, double> GetAssists()
        {
            if (Assists == null)
                Assists = new WeakDictionary<Player, double>();
            return Assists;
        }

        // Who the loot goes to, and for how much (Loots.Handle, as a share of TotalDamage): the
        // hitters for their damage and assist credit (AssistCredit.Credit), and the helpers who
        // never hit it for their assist credit alone.
        public Tuple<Player, int>[] GetLootCredit()
        {
            if (Parent != null)
                return Parent.GetLootCredit();
            var hitters = GetPlayerData();
            if (Assists == null || Assists.Count == 0)
                return hitters;

            var top = hitters.Length == 0 ? 0 : hitters.Max(h => h.Item2);
            var credit = new Dictionary<Player, int>();
            foreach (var h in hitters)
                credit[h.Item1] = h.Item2;
            foreach (var a in Assists)
            {
                var player = a.Key;
                if (player == null || player.World == null || player.World.GetEntity(player.Id) == null)
                    continue;
                credit.TryGetValue(player, out var dmg);
                credit[player] = AssistCredit.Credit(dmg, a.Value, TotalDamage, top);
            }
            return credit.Select(c => Tuple.Create(c.Key, c.Value)).ToArray();
        }

        public bool AssistedBy(Player player) =>
            (Parent ?? this).Assists?.TryGetValue(player, out var credit) == true && credit > 0;

        // a player's debuff on this enemy, for durationMs: they earn assist credit on others' hits
        public void DebuffedBy(Player player, int durationMs)
        {
            if (player == null)
                return;
            (Debuffers ??= new AssistCover()).Cover(player, AssistCredit.EffectWindow(durationMs));
        }

        private void Assist(Player helper, double credit)
        {
            if (helper.World != Host.World)
                return;
            var assists = GetAssists();
            assists.TryGetValue(helper, out var had);
            assists[helper] = had + credit;
        }

        public void HitBy(Player player, int dmg)
        {
            // every hit on an enemy comes here, a shot's and an ability's
            Host.DpsMeter?.Hit(player, dmg);

            var hitters = GetHitters();
            if (!hitters.TryGetValue(player, out int totalDmg))
                totalDmg = 0;

            int trueDmg = Math.Min(dmg, Host.MaxHealth);
            TotalDamage += trueDmg;
            hitters[player] = totalDmg + trueDmg;

            LastHitter = player;

            // the hitter's healers and buffers, and this enemy's debuffers, share in the hit
            if (trueDmg > 0)
            {
                player.Helpers.ForEach(helper => Assist(helper, trueDmg * AssistCredit.HelpRate));
                Debuffers?.ForEach(debuffer =>
                {
                    if (debuffer != player)
                        Assist(debuffer, trueDmg * AssistCredit.DebuffRate);
                });
            }

            player.FameCounter.Hit();
        }

        public void TransferData(DamageCounter dc)
        {
            dc.LastHitter = LastHitter;
            dc.TotalDamage = TotalDamage;

            var hitters = GetHitters();
            foreach (var plr in hitters.Keys)
            {
                if (!hitters.TryGetValue(plr, out var totalDmg))
                    totalDmg = 0;

                var dch = dc.GetHitters();
                if (!dch.TryGetValue(plr, out int totalExistingDmg))
                    totalExistingDmg = 0;
                dch[plr] = totalDmg + totalExistingDmg;
            }

            if (Assists == null)
                return;
            var dca = dc.GetAssists();
            foreach (var a in Assists)
            {
                if (a.Key == null)
                    continue;
                dca.TryGetValue(a.Key, out var had);
                dca[a.Key] = had + a.Value;
            }
        }
    }
}
