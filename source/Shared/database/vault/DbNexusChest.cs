using StackExchange.Redis;

namespace Shared.database.vault
{
    // A shared vault chest placed by the nexus map. All of them live in the hash "nexusChests",
    // two fields each, named after the chest's tile so they find their items again after a restart.
    public class DbNexusChest : RInventory
    {
        // `space`: a second Nexus's own chests (an admin's copy of the Nexus, its world's name),
        // kept apart from the real one's so nothing is in both
        public DbNexusChest(IDatabase db, int x, int y, string space = null)
        {
            var key = space == null ? $"{x}_{y}" : $"{space}.{x}_{y}";
            Field = $"items.{key}";
            DataField = $"datas.{key}";

            InitTwo(db, "nexusChests", new string[] { Field, DataField });
        }
    }
}
