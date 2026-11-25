using StackExchange.Redis;
using System;

namespace Shared.database
{
    /// <summary>
    /// Death replays: the last seconds a character saw before it died, as the world server
    /// recorded them (WorldServer/core/replay/ReplayRecorder.cs), gzipped. The account server
    /// hands them to the client (/fame/replay), which plays them from the graveyard, the legends
    /// and the death's summary.
    ///
    /// In Redis: <c>replay.&lt;account&gt;.&lt;char&gt;</c>, a string kept for <see cref="KeepDays"/>
    /// days; a death that makes the all-time legends keeps its replay for good (DbLegend.Insert).
    /// </summary>
    public static class DeathReplays
    {
        public const int KeepDays = 30;

        public static string Key(int accountId, int charId) => $"replay.{accountId}.{charId}";

        public static void Save(IDatabase db, int accountId, int charId, byte[] gzipped) =>
            db.StringSetAsync(Key(accountId, charId), gzipped, TimeSpan.FromDays(KeepDays), flags: CommandFlags.FireAndForget);

        public static byte[] Load(IDatabase db, int accountId, int charId)
        {
            var v = db.StringGet(Key(accountId, charId));
            return v.IsNullOrEmpty ? null : (byte[])v;
        }

        /// <summary>A legend's death: its replay stays.</summary>
        public static void Keep(IDatabase db, int accountId, int charId) =>
            db.KeyPersistAsync(Key(accountId, charId), CommandFlags.FireAndForget);
    }
}
