using StackExchange.Redis;

namespace Shared.database.vault
{
    // A shared vault chest placed by the nexus map. All of them live in the hash "nexusChests",
    // two fields each, named after the chest's tile so they find their items again after a restart.
    public class DbNexusChest : RInventory
    {
        public DbNexusChest(IDatabase db, int x, int y)
        {
            Field = $"items.{x}_{y}";
            DataField = $"datas.{x}_{y}";

            InitTwo(db, "nexusChests", new string[] { Field, DataField });
        }
    }
}
