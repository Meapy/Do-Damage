using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Shared.database.character.inventory;
using StackExchange.Redis;
using WorldServer.core.objects;
using WorldServer.core.worlds.impl;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.commands.doomed
{
    // The account's vault chests, for the Divine Forge: it counts their items as ingredients, takes
    // what the inventory lacks from them, and puts the forged item in one when the inventory is full.
    // Only the account's own chests (DbVaultSingle: the hash vault.<accountId>, fields vault.<i>
    // and vaultData.<i>), never the Nexus's shared ones.
    //
    // Read straight from Redis while the player is away from their vault: the vault world only
    // holds the chests while its owner is in it (it saves a chest whole on every change, which would
    // put back what the forge took), so a vault with its owner inside isn't touched. A change is
    // written in one transaction on the condition that every chest it touches is as read, so a
    // change in between refuses the forging rather than losing or doubling an item.
    internal sealed class ForgeVault
    {
        private const ushort Empty = 0xffff;

        private sealed class Chest
        {
            public string Field, DataField;
            public RedisValue RawItems, RawDatas;
            public ushort[] Items;
            public ItemData[] Datas;
            public bool Changed;
        }

        private readonly Player _player;
        private readonly string _key;
        private readonly List<Chest> _chests = new List<Chest>();

        private ForgeVault(Player player)
        {
            _player = player;
            _key = "vault." + player.AccountId;
        }

        // The vault as it is now, or null when its owner is in it (or it has no chests).
        public static ForgeVault Read(Player player)
        {
            var acc = player.Client.Account;
            if (acc.VaultCount <= 0 || player.GameServer.WorldManager.GetWorlds()
                    .Any(w => w is VaultWorld v && v.AccountId == player.AccountId && v.Players.Count > 0))
                return null;
            var vault = new ForgeVault(player);
            var all = acc.Database.HashGetAll(vault._key).ToDictionary(e => (string)e.Name, e => e.Value);
            for (var i = 0; i < acc.VaultCount; i++)
            {
                var chest = new Chest { Field = "vault." + i, DataField = "vaultData." + i };
                all.TryGetValue(chest.Field, out chest.RawItems);
                all.TryGetValue(chest.DataField, out chest.RawDatas);
                if (chest.RawItems.IsNull)
                    chest.Items = Enumerable.Repeat(Empty, 8).ToArray();
                else
                {
                    var bytes = (byte[])chest.RawItems;
                    chest.Items = new ushort[bytes.Length / 2];
                    System.Buffer.BlockCopy(bytes, 0, chest.Items, 0, chest.Items.Length * 2);
                }
                chest.Datas = chest.RawDatas.IsNull ? null : JsonConvert.DeserializeObject<ItemData[]>(Encoding.UTF8.GetString(chest.RawDatas));
                vault._chests.Add(chest);
            }
            return vault;
        }

        private ItemData Data(Chest c, int slot) => c.Datas != null && slot < c.Datas.Length ? c.Datas[slot] : null;

        // An item the forge can take: not a stack of several.
        private bool Usable(Chest c, int slot) => c.Items[slot] != Empty && (Data(c, slot)?.Stack ?? 0) <= 1;

        // How many of each item type the forge can take, by type.
        public Dictionary<ushort, int> Counts()
        {
            var counts = new Dictionary<ushort, int>();
            foreach (var c in _chests)
                for (var s = 0; s < c.Items.Length; s++)
                    if (Usable(c, s))
                        counts[c.Items[s]] = counts.GetValueOrDefault(c.Items[s]) + 1;
            return counts;
        }

        public int Count(string itemId) =>
            _player.GameServer.Resources.GameData.IdToObjectType.TryGetValue(itemId, out var t) ? Counts().GetValueOrDefault(t) : 0;

        // Takes these items (by item id, a count each; Count first), those without data first. In
        // memory only until Save.
        public void Take(Dictionary<string, int> items)
        {
            var types = _player.GameServer.Resources.GameData.IdToObjectType;
            foreach (var (id, n) in items)
            {
                var type = types[id];
                var left = n;
                foreach (var plain in new[] { true, false })
                    foreach (var c in _chests)
                        for (var s = 0; s < c.Items.Length && left > 0; s++)
                            if (c.Items[s] == type && Usable(c, s) && (Data(c, s) == null) == plain)
                            {
                                c.Items[s] = Empty;
                                if (Data(c, s) != null)
                                    c.Datas[s] = null;
                                c.Changed = true;
                                left--;
                            }
            }
        }

        // Puts this item in the first empty slot. False when every chest is full.
        public bool Put(ushort type)
        {
            foreach (var c in _chests)
                for (var s = 0; s < c.Items.Length; s++)
                    if (c.Items[s] == Empty)
                    {
                        c.Items[s] = type;
                        if (Data(c, s) != null)
                            c.Datas[s] = null;
                        c.Changed = true;
                        return true;
                    }
            return false;
        }

        // Every item in the chests: (chest, slot, type, its data), for the Enchanter.
        public IEnumerable<(int Chest, int Slot, ushort Type, ItemData Data)> All()
        {
            for (var c = 0; c < _chests.Count; c++)
                for (var s = 0; s < _chests[c].Items.Length; s++)
                    if (_chests[c].Items[s] != Empty)
                        yield return (c, s, _chests[c].Items[s], Data(_chests[c], s));
        }

        // The item in a chest's slot and its data ((0xffff, null) for none).
        public (ushort Type, ItemData Data) At(int chest, int slot) =>
            chest >= 0 && chest < _chests.Count && slot >= 0 && slot < _chests[chest].Items.Length
                ? (_chests[chest].Items[slot], Data(_chests[chest], slot)) : (Empty, null);

        // Sets an item's data (its imprints). In memory only until Save.
        public void SetData(int chest, int slot, ItemData data)
        {
            var c = _chests[chest];
            if (c.Datas == null || c.Datas.Length < c.Items.Length)
            {
                var datas = new ItemData[c.Items.Length];
                if (c.Datas != null)
                    System.Array.Copy(c.Datas, datas, c.Datas.Length);
                c.Datas = datas;
            }
            c.Datas[slot] = data;
            c.Changed = true;
        }

        // Writes the changed chests, if nothing changed them since Read. False, and nothing
        // written, if something did.
        public bool Save()
        {
            var changed = _chests.Where(c => c.Changed).ToList();
            if (changed.Count == 0)
                return true;
            var db = _player.Client.Account.Database;
            var trans = db.CreateTransaction();
            var update = new List<HashEntry>();
            foreach (var c in changed)
            {
                trans.AddCondition(c.RawItems.IsNull ? Condition.HashNotExists(_key, c.Field) : Condition.HashEqual(_key, c.Field, c.RawItems));
                trans.AddCondition(c.RawDatas.IsNull ? Condition.HashNotExists(_key, c.DataField) : Condition.HashEqual(_key, c.DataField, c.RawDatas));
                var bytes = new byte[c.Items.Length * 2];
                System.Buffer.BlockCopy(c.Items, 0, bytes, 0, bytes.Length);
                update.Add(new HashEntry(c.Field, bytes));
                if (c.Datas != null)
                    update.Add(new HashEntry(c.DataField, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(c.Datas))));
            }
            _ = trans.HashSetAsync(_key, update.ToArray());
            return trans.Execute();
        }

        // What the vault holds, to the forge window (FORGE_VAULT): nothing while its owner is in it.
        public static void Send(Player player, ForgeVault vault = null)
        {
            vault ??= Read(player);
            var counts = vault?.Counts() ?? new Dictionary<ushort, int>();
            player.Client.SendPacket(new ForgeVaultItems() { Items = counts.Select(c => (c.Key, c.Value)).ToArray() });
        }
    }
}
