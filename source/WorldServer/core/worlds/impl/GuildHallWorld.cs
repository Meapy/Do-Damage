using System.Collections.Generic;
using System.Linq;
using Shared.database.guild;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.objects;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.inventory;
using WorldServer.networking;

namespace WorldServer.core.worlds.impl
{
    // A guild's hall (Guild Hall 1-4 in Worlds.xml, one instance per guild, under the Nexus). Its
    // map's Vault Chests are the guild vault (DbGuildChest, numbered top to bottom, left to right),
    // shared by every member: Initiates can put items in, members and up take them out too
    // (GuildHalls.MayTake), and every move is logged (/gvault). During a rolling deploy another world
    // server may run its own copy of the hall, so the chests are checked against Redis before every
    // move and every few seconds while anyone is in, and saved before the move is confirmed.
    public sealed class GuildHallWorld : World
    {
        private const ushort VaultChest = 0x0504;
        private const float RefreshEvery = 2f;

        public int GuildId { get; private set; }
        public int Level { get; private set; }

        private readonly List<Container> _chests = new List<Container>();
        private float _refreshIn = RefreshEvery;

        public GuildHallWorld(GameServer gameServer, int id, WorldResource resource, World parent) : base(gameServer, id, resource, parent)
        {
        }

        public IReadOnlyList<Container> Chests => _chests;

        // Before the hall runs (WorldManager.CreateGuildHall): whose it is, and its chests' items.
        public void Link(int guildId, int level)
        {
            GuildId = guildId;
            Level = level;
            var db = GameServer.Database.Conn;
            var chests = PendingEntities.OfType<Container>().Where(c => c.ObjectType == VaultChest && c.DbLink == null)
                .OrderBy(c => (int)c.Y).ThenBy(c => (int)c.X).ToList();
            for (var i = 0; i < chests.Count; i++)
            {
                chests[i].LinkToDb(new DbGuildChest(db, guildId, i), wait: true);
                _chests.Add(chests[i]);
            }

            // The Guild Hall Upgrade is <Static/> in the XML, so the map holds it as a tile's object,
            // which can't be bought from: it becomes a merchant standing on its tile.
            for (var y = 0; y < Map.Height; y++)
                for (var x = 0; x < Map.Width; x++)
                {
                    var tile = Map[x, y];
                    if (tile.ObjType == 0 || tile.ObjDesc?.Class != "GuildMerchant")
                        continue;
                    var merchant = Entity.Resolve(GameServer, tile.ObjType);
                    tile.ObjType = 0;
                    tile.ObjId = 0;
                    merchant.Move(x + 0.5f, y + 0.5f);
                    EnterWorld(merchant);
                }
        }

        public override bool AllowedAccess(Client client) => client.Account.GuildId == GuildId || client.Account.Admin;

        // Sends the player to their guild's hall (the Guild Hall Portal, /ghall), opening it on the
        // Nexus's thread if it isn't open.
        public static void Open(Player player)
        {
            var account = player.Client?.Account;
            if (account == null || account.GuildId <= 0 || !player.InGuild)
            {
                player.SendError("You are not in a guild. Make one at the Guild Register beside the Guild Hall's door in the Nexus's village, or ask a guild to invite you.");
                return;
            }
            var manager = player.GameServer.WorldManager;
            var open = manager.GetGuild(account.GuildId);
            if (open != null && !open.Deleted)
            {
                player.Reconnect(open);
                return;
            }
            var guildId = account.GuildId;
            manager.Nexus.Post(() =>
            {
                var hall = manager.GetGuild(guildId);
                if (hall == null || hall.Deleted)
                    hall = manager.CreateGuildHall(guildId, GuildHalls.HallOf(player.GameServer.Database.GetGuild(guildId).Level));
                if (hall != null)
                    player.Reconnect(hall);
                else
                    player.SendError("Your guild hall couldn't be opened. Try again in a moment.");
            });
        }

        // After an upgrade (GuildMerchant): the bigger hall opens, everyone here moves into it, and
        // this one closes once it's empty. Its vault comes along (the same chests, and more).
        public void MoveTo(int level)
        {
            var manager = GameServer.WorldManager;
            var players = Players.Values.ToList();
            manager.Nexus.Post(() =>
            {
                var hall = manager.CreateGuildHall(GuildId, level);
                if (hall == null)
                    return;
                foreach (var p in players)
                    if (p.World == this)
                        p.Reconnect(hall);
                FlagForClose();
            });
        }

        // a line to every member online on this world server
        // A line to the guild's online members; with a hall, the guild has bought it, so they take its
        // buffs at once (the others do on signing in).
        public static void Announce(GameServer server, int guildId, string text, int hall = 0)
        {
            foreach (var world in server.WorldManager.GetWorlds())
                world.ForeachPlayer(p =>
                {
                    if (p.Client?.Account?.GuildId != guildId)
                        return;
                    if (hall > 0)
                        p.SetGuildHall(hall);
                    p.SendHelp(text);
                });
        }

        public static bool IsVaultChest(Entity e) => e is Container { DbLink: DbGuildChest } && e.World is GuildHallWorld;

        // May this player take items out of this guild's vault?
        public bool MayTake(Player player) =>
            player.Client?.Account != null && player.Client.Account.GuildId == GuildId && GuildHalls.MayTake(player.Client.Account.GuildRank);

        public bool MayPut(Player player) => player.Client?.Account != null && player.Client.Account.GuildId == GuildId;

        // Whether the chest still holds what Redis has; if not, it takes Redis's and says no.
        public bool Fresh(Container chest)
        {
            var link = (DbGuildChest)chest.DbLink;
            var v = GameServer.Database.Conn.HashGet(link.Key, new RedisValue[] { link.Field, link.DataField });
            if (link.Matches(v[0], v[1]))
                return true;
            link.ReloadBoth();
            chest.ReloadFromDb();
            return false;
        }

        public void Log(Player player, bool put, Item item)
        {
            if (item != null)
                DbGuildVault.Log(GameServer.Database.Conn, GuildId, player.Name, put, item.DisplayName ?? item.ObjectId);
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            base.UpdateLogic(ref time);
            _refreshIn -= time.DeltaTime;
            if (_refreshIn > 0 || Players.Count == 0 || _chests.Count == 0)
                return;
            _refreshIn = RefreshEvery;

            var all = GameServer.Database.Conn.HashGetAll(DbGuildVault.Key(GuildId)).ToDictionary(e => e.Name.ToString(), e => e.Value);
            foreach (var chest in _chests)
            {
                var link = (DbGuildChest)chest.DbLink;
                all.TryGetValue(link.Field, out var items);
                all.TryGetValue(link.DataField, out var datas);
                if (link.Matches(items, datas))
                    continue;
                link.ReloadBoth();
                chest.ReloadFromDb();
            }
        }
    }
}
