using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Shared.database
{
    /// One comment on a replay.
    public class ReplayComment
    {
        public int AccountId { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        /// Unix time.
        public int Time { get; set; }
    }

    /// <summary>
    /// What players say under a replay (a death's, or a death saved's: DeathReplays.cs), from the
    /// replay viewer: the Legends, the graveyard, a death's summary and the Scrying Glass's
    /// Replays tab.
    ///
    /// A replay is named as the client names it: <c>d.&lt;account&gt;.&lt;char&gt;</c> for a death,
    /// <c>s.&lt;save&gt;</c> for a death saved. Its comments are the list
    /// <c>replay.comments.&lt;name&gt;</c> (JSON entries, oldest first, the newest
    /// <see cref="Max"/> kept), lasting as long as the replay does.
    /// </summary>
    public static class ReplayComments
    {
        public const int Max = 50;
        public const int MaxLength = 140;

        private static readonly Regex Name = new Regex(@"^(d\.\d{1,10}\.\d{1,10}|s\.\d{1,18})$", RegexOptions.Compiled);

        public static bool Valid(string replay) => replay != null && Name.IsMatch(replay);

        /// The replay's own key (DeathReplays), or null for a name that isn't one.
        public static string ReplayKey(string replay)
        {
            if (!Valid(replay))
                return null;
            var parts = replay.Split('.');
            if (parts[0] == "s")
                return long.TryParse(parts[1], out var id) ? DeathReplays.SavedKey(id) : null;
            return int.TryParse(parts[1], out var acc) && int.TryParse(parts[2], out var chr) ? DeathReplays.Key(acc, chr) : null;
        }

        public static string Key(string replay) => $"replay.comments.{replay}";

        public static ReplayComment[] Get(IDatabase db, string replay)
        {
            if (!Valid(replay))
                return Array.Empty<ReplayComment>();
            return db.ListRange(Key(replay))
                .Select(e =>
                {
                    try { return JsonConvert.DeserializeObject<ReplayComment>(e); }
                    catch (JsonException) { return null; }
                })
                .Where(e => e != null)
                .ToArray();
        }

        public static long Count(IDatabase db, string replay) => Valid(replay) ? db.ListLength(Key(replay)) : 0;

        /// <summary>
        /// The text as it's kept: one line, no control characters, spaces run together, at most
        /// <see cref="MaxLength"/> characters; null when nothing's left.
        /// </summary>
        public static string Clean(string text)
        {
            if (text == null)
                return null;
            var sb = new StringBuilder();
            var space = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space)
                    sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            var s = sb.ToString();
            if (s.Length > MaxLength)
                s = s.Substring(0, MaxLength).TrimEnd();
            return s.Length == 0 ? null : s;
        }

        /// <summary>
        /// Adds a comment under a replay that's kept, the oldest let go past <see cref="Max"/>,
        /// lasting as the replay does. False when there's no such replay.
        /// </summary>
        public static bool Add(IDatabase db, string replay, ReplayComment comment)
        {
            var replayKey = ReplayKey(replay);
            if (replayKey == null || !db.KeyExists(replayKey))
                return false;
            var key = Key(replay);
            db.ListRightPush(key, JsonConvert.SerializeObject(comment));
            db.ListTrim(key, -Max, -1);
            var ttl = db.KeyTimeToLive(replayKey);
            if (ttl == null)
                db.KeyPersist(key);
            else
                db.KeyExpire(key, ttl);
            return true;
        }

        /// A replay kept for good (a legend's): its comments too.
        public static void Keep(IDatabase db, string replay) =>
            db.KeyPersistAsync(Key(replay), CommandFlags.FireAndForget);
    }
}
