using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.commands.doomed
{
    // The forge storage: the account's forge shards (tools/build_ascended_items.py), which Oryx 4 and
    // Oryx 5 drop. A shard used anywhere goes into it, as a stat potion goes to potion storage; the
    // Divine Forge takes a recipe's shards from it, and its window takes them back out
    // (FORGE_WITHDRAW), to trade. Kept on the account (DbAccount.ForgeShards, by index here).
    internal static class ForgeStorage
    {
        // in storage order: only ever add to the end
        public static readonly string[] Shards =
        {
            "Infernal Weapon Shard", "Infernal Ability Shard", "Infernal Armor Shard", "Infernal Accessory Shard",
            "Ascended Weapon Shard", "Ascended Ability Shard", "Ascended Armor Shard", "Ascended Accessory Shard",
        };

        public const int Capacity = 9999;

        public static int Index(string itemId) => Array.IndexOf(Shards, itemId);

        public static int[] Counts(Player player)
        {
            var stored = player.Client.Account.ForgeShards;
            var counts = new int[Shards.Length];
            Array.Copy(stored, counts, Math.Min(stored.Length, counts.Length));
            return counts;
        }

        private static void Save(Player player, int[] counts)
        {
            var acc = player.Client.Account;
            acc.ForgeShards = counts;
            acc.FlushAsync();
            Send(player);
        }

        // Whether this is a shard whose storage has no room.
        public static bool Full(Player player, string itemId) => Index(itemId) is var i && i >= 0 && Counts(player)[i] >= Capacity;

        // A shard used: into storage. False, and nothing stored, when its storage is full.
        public static bool Store(Player player, string itemId)
        {
            var i = Index(itemId);
            var counts = Counts(player);
            if (i < 0 || counts[i] >= Capacity)
                return false;
            counts[i]++;
            Save(player, counts);
            player.SendInfo($"Added {Shards[i]} to your forge storage! [{counts[i]}]");
            return true;
        }

        // Whether storage holds these shards (by item id, a count each).
        public static bool Has(Player player, Dictionary<string, int> shards)
        {
            var counts = Counts(player);
            return shards.All(s => Index(s.Key) >= 0 && counts[Index(s.Key)] >= s.Value);
        }

        // Takes these shards out of storage (Has first).
        public static void Take(Player player, Dictionary<string, int> shards)
        {
            if (shards.Count == 0)
                return;
            var counts = Counts(player);
            foreach (var (id, n) in shards)
                counts[Index(id)] -= n;
            Save(player, counts);
        }

        // One shard from storage into the inventory. Returns what went wrong, or null.
        public static string Withdraw(Player player, ushort type)
        {
            var data = player.GameServer.Resources.GameData;
            if (!data.Items.TryGetValue(type, out var item) || Index(item.ObjectId) is var i && i < 0)
                return "That isn't a forge shard.";
            var counts = Counts(player);
            if (counts[i] <= 0)
                return $"You have no {item.ObjectId} in your forge storage.";
            var slot = player.Inventory.GetAvailableInventorySlot(item);
            if (slot == -1)
                return "Your inventory is full.";
            player.Inventory[slot] = item;
            counts[i]--;
            Save(player, counts);
            return null;
        }

        // The stored shards, to the forge window.
        public static void Send(Player player)
        {
            var types = player.GameServer.Resources.GameData.IdToObjectType;
            var counts = Counts(player);
            player.Client.SendPacket(new ForgeShards()
            {
                Shards = Shards.Select((id, i) => (id, i)).Where(s => types.ContainsKey(s.id))
                    .Select(s => (types[s.id], counts[s.i])).ToArray()
            });
        }
    }
}
