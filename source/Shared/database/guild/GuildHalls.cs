using System;

namespace Shared.database.guild
{
    // The guild's hall and what each of its four levels gives, the rules only (dotnet test
    // source\Tests covers them). A guild's level in Redis (DbGuild.Level) is 0 for the first hall,
    // which the Guild Hall Portal opens as "Guild Hall 1" (Worlds.xml) and so on. Each hall is drawn
    // by tools/build_guild_halls.py with VaultChests(level) chests; the guild vault keeps chest n's
    // items whatever the hall, so an upgrade keeps everything and adds empty chests.
    public static class GuildHalls
    {
        public const int Levels = 4;

        public const int ChestSlots = 8;

        // the vault's chests in each hall, by level 1-4
        private static readonly int[] Chests = { 2, 4, 6, 8 };

        // guild fame for the upgrade to each level 2-4
        private static readonly int[] Prices = { 10_000, 30_000, 75_000 };

        // what each hall adds, for the upgrade's message and the guild window
        private static readonly string[] Adds =
        {
            "an octagonal lodge with the guild vault (2 chests), the board and the chronicle",
            "a cross-shaped hall: 4 vault chests and a training yard",
            "a star-fort keep: 6 vault chests, the Armoire, potion storage and a fountain garden",
            "a citadel: 8 vault chests and a throne room with the Divine Workbench",
        };

        // being in a guild gives this loot boost (LootBoosts.Guild): the same whatever the hall
        public const double LootBoost = 0.01;

        public static int Clamp(int level) => Math.Clamp(level, 1, Levels);

        // a guild's hall level (1-4) from DbGuild.Level (0-3)
        public static int HallOf(int dbLevel) => Clamp(dbLevel + 1);

        public static string WorldName(int level) => $"Guild Hall {Clamp(level)}";

        public static int VaultChests(int level) => Chests[Clamp(level) - 1];

        public static int VaultSlots(int level) => VaultChests(level) * ChestSlots;

        public static string Describe(int level) => Adds[Clamp(level) - 1];

        // the price of the upgrade from this level, or 0 at the top
        public static int UpgradePrice(int level) => level >= 1 && level < Levels ? Prices[level - 1] : 0;

        // Initiates can put items into the vault; members and above take them out too.
        public static bool MayTake(int guildRank) => guildRank >= (int)GuildRank.Member;

        // Leaders and the founder buy the upgrades.
        public static bool MayUpgrade(int guildRank) => guildRank >= (int)GuildRank.Leader;

        public static string RankName(int guildRank) => guildRank switch
        {
            >= (int)GuildRank.Founder => "Founder",
            >= (int)GuildRank.Leader => "Leader",
            >= (int)GuildRank.Officer => "Officer",
            >= (int)GuildRank.Member => "Member",
            _ => "Initiate",
        };
    }
}
