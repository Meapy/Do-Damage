using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Shared.resources;
using WorldServer.core.commands.doomed;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // The Collection (the Collector in the vault, CollectionHandler.cs): every piece of gear the
    // account has had. resources/data/collection.json (tools/build_collection.py) lists the gear
    // there is to collect and where the untiered gear comes from. A piece counts once it's in any of
    // the account's characters' slots (loot picked up, forged, traded, taken from the vault or the
    // gift chest); the first time, everything the account holds then counts (its characters, its
    // vault and its gift chest). Kept on the account (DbAccount.Collection), saved with it.
    partial class Player
    {
        public sealed class CollectionCatalogue
        {
            public ushort[] All = Array.Empty<ushort>();
            public HashSet<ushort> Collectible = new HashSet<ushort>();
            public (string Name, ushort Icon, byte Level, ushort[] Items)[] Groups = Array.Empty<(string, ushort, byte, ushort[])>();
        }

        private static CollectionCatalogue _catalogue;

        public static CollectionCatalogue Catalogue(GameServer server)
        {
            if (_catalogue != null)
                return _catalogue;
            var c = new CollectionCatalogue();
            try
            {
                var gameData = server.Resources.GameData;
                ushort Type(string id) => gameData.IdToObjectType.TryGetValue(id, out var t) && gameData.Items.ContainsKey(t) ? t : (ushort)0;
                var json = JObject.Parse(File.ReadAllText($"{server.Resources.ResourcePath}/data/collection.json"));
                c.All = json["all"].Select(t => Type((string)t)).Where(t => t != 0).Distinct().ToArray();
                c.Collectible = new HashSet<ushort>(c.All);
                c.Groups = json["groups"].Select(g => (
                    (string)g["name"],
                    g["icon"]?.Type == JTokenType.String && gameData.IdToObjectType.TryGetValue((string)g["icon"], out var icon) ? icon : (ushort)0,
                    (byte)Math.Clamp((int?)g["level"] ?? 0, 0, 255),
                    g["items"].Select(t => Type((string)t)).Where(t => t != 0 && c.Collectible.Contains(t)).ToArray()))
                    .Where(g => g.Item4.Length > 0).ToArray();
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Error($"The Collection's list (data/collection.json) didn't load: {e.Message}");
            }
            return _catalogue = c;
        }

        private HashSet<ushort> _collection;

        /// <summary>The account's collection, worked out the first time from what it holds.</summary>
        public HashSet<ushort> Collection
        {
            get
            {
                if (_collection != null)
                    return _collection;
                var acc = Client.Account;
                var stored = acc.Collection;
                _collection = new HashSet<ushort>(stored ?? Array.Empty<ushort>());
                if (stored == null)
                    SeedCollection();
                return _collection;
            }
        }

        // everything the account holds now: its living characters (this one included), its vault
        // and its gift chest
        private void SeedCollection()
        {
            var catalogue = Catalogue(GameServer);
            var acc = Client.Account;
            void Add(ushort t)
            {
                if (catalogue.Collectible.Contains(t))
                    _collection.Add(t);
            }
            try
            {
                var db = GameServer.Database;
                foreach (var id in db.GetAliveCharacters(acc).ToList())
                {
                    var chr = db.LoadCharacter(acc, id);
                    foreach (var t in chr?.Items ?? Array.Empty<ushort>())
                        Add(t);
                }
                foreach (var (_, _, t, _) in ForgeVault.Peek(this)?.All() ?? Enumerable.Empty<(int, int, ushort, Shared.database.character.inventory.ItemData)>())
                    Add(t);
                foreach (var t in acc.Gifts)
                    Add(t);
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Warn($"Collection of {Name}: {e.Message}");
            }
            foreach (var item in Inventory?.ToArray() ?? Array.Empty<Item>())
                if (item != null)
                    Add(item.ObjectType);
            acc.Collection = _collection.ToArray();
            acc.FlushAsync();
        }

        /// <summary>Items now in a slot of ours: the gear among them is collected.</summary>
        public void Collect(IEnumerable<Item> items)
        {
            if (Client?.Account == null)
                return;
            var catalogue = Catalogue(GameServer);
            var added = false;
            foreach (var item in items)
                if (item != null && catalogue.Collectible.Contains(item.ObjectType) && Collection.Add(item.ObjectType))
                    added = true;
            if (added)
                Client.Account.Collection = _collection.ToArray();
        }
    }
}
