using Shared;
using Shared.database.character;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic
{
    public class FameCounter
    {
        public Player Host;
        private int elapsed = 0;

        public FameCounter(Player player)
        {
            Host = player;
            Stats = FameStats.Read(player.Client.Character.FameStats);
            ClassStats = new DbClassStats(player.Client.Account);
        }

        public DbClassStats ClassStats { get; private set; }
        public FameStats Stats { get; private set; }

        // A dungeon cleared (QuestTracker.DungeonCleared): every one counts for the records'
        // Dungeons, and these also for the fame bonuses that ask for them.
        public void CompleteDungeon(string name)
        {
            Stats.DungeonsCompleted++;
            switch (name)
            {
                case "Pirate Cave":
                case "PirateCave":
                    Stats.PirateCavesCompleted++;
                    break;

                case "Undead Lair":
                    Stats.UndeadLairsCompleted++;
                    break;

                case "Abyss":
                case "Abyss of Demons":
                    Stats.AbyssOfDemonsCompleted++;
                    break;

                case "Snake Pit":
                    Stats.SnakePitsCompleted++;
                    break;

                case "Spider Den":
                    Stats.SpiderDensCompleted++;
                    break;

                case "Sprite World":
                    Stats.SpriteWorldsCompleted++;
                    break;

                case "Tomb":
                case "Tomb of the Ancients":
                    Stats.TombsCompleted++;
                    break;

                case "OceanTrench":
                case "Ocean Trench":
                    Stats.TrenchesCompleted++;
                    break;

                case "Forbidden Jungle":
                    Stats.JunglesCompleted++;
                    break;

                case "Manor of the Immortals":
                    Stats.ManorsCompleted++;
                    break;
            }
        }

        public void DrinkPot(int count = 1)
        {
            Stats.PotionsDrunk += count;
            WorldServer.core.WeeklyStats.Count(Shared.database.leaderboard.DbWeeklyStats.Potions, count);
        }

        public void Hit() => Stats.ShotsThatDamage++;

        public void Killed(Enemy enemy, bool killer)
        {
            if (enemy.ObjectDesc.God)
                Stats.GodAssists++;
            else
                Stats.MonsterAssists++;
            if (Host.Quest == enemy)
                Stats.QuestsCompleted++;
            if (killer)
            {
                if (enemy.ObjectDesc.God)
                    Stats.GodKills++;
                else
                    Stats.MonsterKills++;

                if (enemy.ObjectDesc.Cube)
                    Stats.CubeKills++;
                if (enemy.ObjectDesc.Oryx)
                    Stats.OryxKills++;
            }
        }

        public void LevelUpAssist(int count) => Stats.LevelUpAssists += count;

        public void Shoot() => Stats.Shots++;

        public void Teleport() => Stats.Teleports++;

        public void Tick(TickTime time)
        {
            elapsed += time.ElapsedMsDelta;

            if (elapsed > 60000)
            {
                elapsed -= 60000;
                Stats.MinutesActive++;
            }
        }

        public void TileSent(int num) => Stats.TilesUncovered += num;

        public void UseAbility() => Stats.SpecialAbilityUses++;
    }
}
