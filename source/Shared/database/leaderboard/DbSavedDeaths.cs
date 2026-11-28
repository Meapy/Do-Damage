using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Linq;

namespace Shared.database.leaderboard
{
    /// A death that didn't happen: a character that took a killing blow while wearing what saves
    /// it (an amulet that resurrects, a ring with Guardian Angel) and was carried to the Nexus,
    /// alive, as the Legends screen's Deaths Saved lists it. Its replay is kept as a death's is
    /// (DeathReplays.SaveSaved), by <see cref="Id"/>, as the character may be saved again.
    public class DbSavedDeath
    {
        /// The save's own number (`deaths:saved:next`), naming its replay.
        public long Id { get; set; }
        public int AccountId { get; set; }
        public int CharId { get; set; }
        public string Name { get; set; }
        public ushort ObjectType { get; set; }
        public int Skin { get; set; }
        public int Tex1 { get; set; }
        public int Tex2 { get; set; }
        public int Level { get; set; }
        /// The character's fame then, and its stats maxed (of 8).
        public int Fame { get; set; }
        public int Maxed { get; set; }
        /// What would have killed it, and where.
        public string Killer { get; set; }
        public string World { get; set; }
        /// What saved it: the item's type and name, and whether it was the ring's Guardian Angel.
        public ushort SaverType { get; set; }
        public string Saver { get; set; }
        public bool Angel { get; set; }
        /// The blow that would have killed it: its damage after defense, the health it met, and
        /// the character's most health (0 when no hit was seen, as a curse's).
        public int Blow { get; set; }
        public int HpBefore { get; set; }
        public int MaxHp { get; set; }
        /// Whether a replay was kept.
        public bool HasReplay { get; set; }
        /// Unix time.
        public int Time { get; set; }
    }

    /// The server's latest saves, newest first, in the capped list `deaths:saved` (JSON entries),
    /// with how many there have been ever (`deaths:saved:next`).
    public static class DbSavedDeaths
    {
        private const string Key = "deaths:saved";
        private const string NextKey = "deaths:saved:next";

        /// How many the list keeps; the client shows them all.
        public const int Max = 30;

        /// A new save's number.
        public static long NextId(IDatabase db) => db.StringIncrement(NextKey);

        /// How many saves there have been.
        public static long Count(IDatabase db)
        {
            var v = db.StringGet(NextKey);
            return v.IsNullOrEmpty ? 0 : (long)v;
        }

        public static void Insert(IDatabase db, DbSavedDeath save)
        {
            var trans = db.CreateTransaction();
            trans.ListLeftPushAsync(Key, JsonConvert.SerializeObject(save), When.Always, CommandFlags.FireAndForget);
            trans.ListTrimAsync(Key, 0, Max - 1, CommandFlags.FireAndForget);
            trans.ExecuteAsync(CommandFlags.FireAndForget);
        }

        public static DbSavedDeath[] Get(IDatabase db, int count)
        {
            return db.ListRange(Key, 0, Math.Clamp(count, 1, Max) - 1)
                .Select(e =>
                {
                    try { return JsonConvert.DeserializeObject<DbSavedDeath>(e); }
                    catch (JsonException) { return null; }
                })
                .Where(e => e != null)
                .ToArray();
        }
    }
}
