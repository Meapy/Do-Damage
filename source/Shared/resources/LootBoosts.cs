namespace Shared.resources
{
    // A player's loot boosts, each a share added to the drop chance multiplier (Loots.Handle: a
    // drop's chance times 1 + damage share + these), and what /boosts lists.
    public static class LootBoosts
    {
        // a Loot Drop Potion's, while it lasts
        public const double LootDrop = 0.5;

        // an account with a character on a Legends board (top 20 by fame, this week, this month or
        // all time: Redis's "legend" hash, rebuilt by the account server every minute), checked on
        // entering a world like the legend glow
        public const double Legend = 0.01;

        // being in a guild (Player.Guild set), whatever its hall (GuildHalls.LootBoost)
        public const double Guild = database.guild.GuildHalls.LootBoost;

        // what the boosts add together; the rare, epic and legendary monsters' and the damage share
        // come on top in Loots.Handle. champion is the Hall of Champions' boards' loot boost
        // (ChampionPlaces.Loot, already capped at ChampionBoards.LootCap), on top of the Legends'
        // and the guild's.
        public static double Total(bool lootDrop, bool legend, double weekend, double lootEvent, double champion = 0, bool guild = false) =>
            (lootDrop ? LootDrop : 0) + (legend ? Legend : 0) + weekend + lootEvent + champion + (guild ? Guild : 0);
    }
}
