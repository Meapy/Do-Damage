using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace Shared.database.guild
{
    // One chest of a guild's shared vault, in its guild hall. A guild's chests all live in the hash
    // "guildVault.<guildId>", two fields each (items.<n> and datas.<n>, n from 0), so a bigger hall
    // finds the same items in its first chests.
    public class DbGuildChest : RInventory
    {
        public int Index { get; }

        public DbGuildChest(IDatabase db, int guildId, int index)
        {
            Index = index;
            Field = $"items.{index}";
            DataField = $"datas.{index}";

            InitTwo(db, DbGuildVault.Key(guildId), new[] { Field, DataField });
        }

        // whether Redis holds what this chest last read or wrote (another world server, during a
        // rolling deploy, may have its own copy of the hall and changed it)
        public bool Matches(RedisValue items, RedisValue datas) =>
            Same(GetValueRaw(Field), items) && Same(GetValueRaw(DataField), datas);

        private static bool Same(byte[] mine, RedisValue theirs) =>
            mine == null || mine.Length == 0 ? theirs.IsNullOrEmpty : theirs.HasValue && ((byte[])theirs).AsSpan().SequenceEqual(mine);

        public void ReloadBoth()
        {
            Reload(Field);
            Reload(DataField);
        }
    }

    // The guild vault's record of who put in and took out what: the newest first, the last 100.
    public static class DbGuildVault
    {
        public const int LogKept = 100;

        public static string Key(int guildId) => $"guildVault.{guildId}";

        public static string LogKey(int guildId) => $"guildVault.{guildId}.log";

        // "<unix seconds>|<name>|<+ or ->|<item>": + put in, - taken out
        public static void Log(IDatabase db, int guildId, string name, bool put, string item)
        {
            var key = LogKey(guildId);
            var line = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}|{name}|{(put ? '+' : '-')}|{item}";
            db.ListLeftPush(key, line, flags: CommandFlags.FireAndForget);
            db.ListTrim(key, 0, LogKept - 1, CommandFlags.FireAndForget);
        }

        public static List<(long Time, string Name, bool Put, string Item)> ReadLog(IDatabase db, int guildId, int count)
        {
            var list = new List<(long, string, bool, string)>();
            foreach (var v in db.ListRange(LogKey(guildId), 0, count - 1))
            {
                var p = v.ToString().Split('|', 4);
                if (p.Length == 4 && long.TryParse(p[0], out var t))
                    list.Add((t, p[1], p[2] == "+", p[3]));
            }
            return list;
        }

        // the items stored in a guild's vault (any chest, the hall or not)
        public static int Stored(IDatabase db, int guildId) =>
            db.HashGetAll(Key(guildId)).Where(e => e.Name.ToString().StartsWith("items.")).Sum(e => Count(e.Value));

        private static int Count(RedisValue items)
        {
            // RedisObject stores a ushort[] as its bytes, two to an item, 0xffff empty
            var b = (byte[])items;
            var n = 0;
            for (var i = 0; i + 1 < b.Length; i += 2)
                if (BitConverter.ToUInt16(b, i) != 0xffff)
                    n++;
            return n;
        }
    }
}
