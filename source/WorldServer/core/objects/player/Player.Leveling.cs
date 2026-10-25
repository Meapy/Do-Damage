using System;
using System.Linq;
using Shared.quests;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        public Entity Quest { get; private set; }

        public static int GetExpGoal(int level) => 50 + (level - 1) * 100;
        public static int GetFameGoal(int fame)
        {
            if (fame >= 2000) return 0;
            if (fame >= 800) return 2000;
            if (fame >= 400) return 800;
            if (fame >= 150) return 400;
            if (fame >= 20) return 150;
            return 20;
        }

        public static int GetLevelExp(int level)
        {
            if (level == 1)
                return 0;
            return 50 * (level - 1) + (level - 2) * (level - 1) * 50;
        }

        public void CalculateFame()
        {
            var newFame = (Experience < 200 * 1000) ? Experience / 1000 : 200 + (Experience - 200 * 1000) / 1000;

            if (newFame == Fame)
                return;

            var Stats = FameCounter.ClassStats[ObjectType];
            var newGoal = GetFameGoal(Stats.BestFame > newFame ? Stats.BestFame : newFame);

            if (newGoal > FameGoal)
            {
                World.BroadcastIfVisible(new Notification()
                {
                    ObjectId = Id,
                    Color = new ARGB(0xFF00FF00),
                    Message = "Quest Complete!"
                }, this);
                var prevStars = Stars;
                Stars = GetStars();
            }
            else if (newFame != Fame)
            {
                var delta = (newFame - Fame);
                if (delta > 0)
                {
                    World.BroadcastIfVisible(new Notification()
                    {
                        ObjectId = Id,
                        Color = new ARGB(0xFFE25F00),
                        Message = $"+{delta} Fame"
                    }, this);
                }
            }

            if (newFame > Fame)
                Quests?.Event(new QuestEvent(QuestEvents.Fame, newFame - Fame));
            Fame = newFame;
            FameGoal = newGoal;
        }

        public bool EnemyKilled(Enemy enemy, int exp, bool killer)
        {
            if (enemy == Quest)
            {
                World.BroadcastIfVisible(new Notification()
                {
                    ObjectId = Id,
                    Color = new ARGB(0xFF00FF00),
                    Message = "Quest Complete!"
                }, this);
                Quest = null;
            }

            if (exp != 0)
            {
                Experience += exp;
            }

            FameCounter.Killed(enemy, killer);
            return CheckLevelUp();
        }

        public int GetStars()
        {
            int ret = 0;
            foreach (var i in FameCounter.ClassStats.AllKeys)
            {
                var entry = FameCounter.ClassStats[ushort.Parse(i)];
                if (entry.BestFame >= 2000) ret += 5;
                else if (entry.BestFame >= 800) ret += 4;
                else if (entry.BestFame >= 400) ret += 3;
                else if (entry.BestFame >= 150) ret += 2;
                else if (entry.BestFame >= 20) ret += 1;
            }
            return ret;
        }

        // The client asked for its quest's top damage dealers (QUEST_DAMAGE_SUBSCRIBE), and what
        // it was last sent.
        public bool WantsQuestDamage { get; set; }
        // ... everyone who hit it, with classes (QUEST_DAMAGE_FULL_SUBSCRIBE).
        public bool WantsFullQuestDamage { get; set; }
        private string _questDamageSent;
        // ... and its fame records (FAME_STATS_SUBSCRIBE), and what it was last sent.
        public bool WantsFameStats { get; set; }
        private string _fameStatsSent;

        public void HandleQuest(TickTime time)
        {
            if (Quest != null && Quest.Dead)
            {
                // The last word on it, so the client can keep the fight.
                if (WantsFullQuestDamage && Quest is Enemy dead)
                    SendQuestDamage(dead, true);
                Quest = null;
            }

            if (WantsQuestDamage && time.TickCount % 5 == 0 && Quest is Enemy quest)
                SendQuestDamage(quest, false);

            if (WantsFameStats && time.TickCount % 25 == 0)
                SendFameStats();

            if (Quest == null || time.TickCount % 10 == 0)
            {
                var newQuest = FindQuest();
                if (newQuest != null && newQuest != Quest)
                {
                    Client.SendPacket(new QuestObjId()
                    {
                        ObjectId = newQuest.Id
                    });
                    Quest = newQuest;
                }
            }
        }

        // Who hit the quest (the top three, or everyone with their classes for clients that
        // asked) and our own damage, when it changes, and once more when it dies.
        private void SendQuestDamage(Enemy enemy, bool dead)
        {
            if (enemy.DamageCounter == null)
                return;
            var hitters = enemy.DamageCounter.GetPlayerData().OrderByDescending(h => h.Item2).ToArray();
            var mine = hitters.Where(h => h.Item1 == this).Select(h => h.Item2).FirstOrDefault();
            var shown = WantsFullQuestDamage ? hitters.Take(255) : hitters.Take(3);
            var key = enemy.Id + ":" + string.Join(",", shown.Select(h => h.Item1.Name + "=" + h.Item2)) + ":" + mine + ":" + dead;
            if (key == _questDamageSent)
                return;
            _questDamageSent = key;
            if (WantsFullQuestDamage)
                Client.SendPacket(new QuestDamageFull()
                {
                    ObjectId = enemy.Id,
                    ObjectType = enemy.ObjectType,
                    MaxHp = enemy.MaxHealth,
                    Dead = dead,
                    Hitters = shown.Select(h => (h.Item1.Name, h.Item1.ObjectType, h.Item2)).ToArray(),
                    Mine = mine
                });
            else
                Client.SendPacket(new QuestDamage() { ObjectId = enemy.Id, Top = shown.Select(h => (h.Item1.Name, h.Item2)).ToArray(), Mine = mine });
        }

        // The fame records (FameStats.Write: an id byte and a count each), when they change.
        private void SendFameStats()
        {
            var stats = FameCounter?.Stats;
            if (stats == null)
                return;
            var bytes = stats.Write();
            var key = System.Convert.ToBase64String(bytes);
            if (key == _fameStatsSent)
                return;
            _fameStatsSent = key;
            var pairs = new (byte, int)[bytes.Length / 5];
            for (var i = 0; i < pairs.Length; i++)
            {
                var o = i * 5;
                pairs[i] = (bytes[o], (bytes[o + 1] << 24) | (bytes[o + 2] << 16) | (bytes[o + 3] << 8) | bytes[o + 4]);
            }
            Client.SendPacket(new FameStatsUpdate() { Stats = pairs });
        }

        private bool CheckLevelUp()
        {
            if (Experience - GetLevelExp(Level) >= ExperienceGoal && Level < 20)
            {
                Level++;
                ExperienceGoal = GetExpGoal(Level);
                Quests?.Event(new QuestEvent(QuestEvents.Level));
                if (Level == 20)
                    Quests?.CheckClasses();

                if (Level < 20)
                {
                    var statInfo = GameServer.Resources.GameData.Classes[ObjectType].Stats;
                    for (var i = 0; i < statInfo.Length; i++)
                    {
                        var min = statInfo[i].MinIncrease;
                        var max = statInfo[i].MaxIncrease + 1;

                        Stats.Base[i] += Random.Shared.Next(min, max);
                        if (Stats.Base[i] > statInfo[i].MaxValue)
                            Stats.Base[i] = statInfo[i].MaxValue;
                    }
                }

                Health = Stats[0];
                Mana = Stats[1];

                if (Level == 20)
                    World.ForeachPlayer(_ => _.SendInfo($"{Name} achieved level 20"));
                else
                    InvokeStatChange(StatDataType.Experience, Experience - GetLevelExp(Level), true);

                Quest = null;
                return true;
            }

            CalculateFame();
            return false;
        }

        private Entity FindQuest()
        {
            Entity ret = null;
            double bestScore = 0.0;
            foreach (var entry in World.Quests)
            {
                var quest = entry.Value;
                if (quest.ObjectDesc == null || !quest.ObjectDesc.Quest)
                    continue;

                var maxVisibleLevel = Math.Min(quest.ObjectDesc.Level + 5, 20);
                var minVisibleLevel = Math.Max(quest.ObjectDesc.Level - 5, 1);
                var force = false;
                if (!(quest.World is RealmWorld) && quest.ObjectDesc.Quest && !quest.ObjectDesc.Hero)
                    force = true;

                if (Level >= minVisibleLevel && Level <= maxVisibleLevel || force)
                {
                    // Oryx himself (<Oryx/>) always comes first: the Doomed Cellar's Skull Shrine, an
                    // Encounter, outscored Oryx 5, and Oryx 3's XML level is 1
                    var priority = quest.ObjectDesc.Oryx ? 4 : quest.ObjectDesc.Encounter ? 3 : quest.ObjectDesc.Hero ? 2 : 1;
                    var levelFit = quest.ObjectDesc.Oryx ? 20 : 20 - Math.Abs(quest.ObjectDesc.Level - Level);
                    var score =
                        //priority * level diff
                        levelFit * priority -
                        //minus 1 for every 100 tile distance
                        this.DistTo(quest) / 100;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        ret = quest;
                    }
                }
            }
            return ret;
        }
    }
}
