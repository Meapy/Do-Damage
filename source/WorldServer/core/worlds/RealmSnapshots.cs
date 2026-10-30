using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;
using WorldServer.utils;

namespace WorldServer.core.worlds
{
    /// <summary>
    /// A realm's state, kept in Redis so it outlives the world server running it (SETUP-LOCAL.md,
    /// "Realm state across restarts"): the next world server, a rolling deploy's or a restart's,
    /// opens the same realms again. One field of the hash "realms" per realm, by its Key, which
    /// stays the same from server to server.
    /// </summary>
    public sealed class RealmSnapshot
    {
        public string Key;
        public string Name;
        public bool Realm2;
        public bool Closed;         // closed: never opened again
        public long At;             // unix seconds it was written
        public string By;           // the world server that wrote it (GameServer.InstanceId)
        public int Events;          // defeated toward closing
        public List<RealmSnapshotEvent> Running = new List<RealmSnapshotEvent>();
        public List<string> Left;   // a realm's events it can still have (one-per-realm ones go)

        // Realm 2
        public int Seed;
        public List<string> Upcoming;
        public List<RealmSnapshotLand> Lands;
        public List<RealmSnapshotPoint> Waystones;
        public int SinceBeacon;
        public bool Voiced;
    }

    public sealed class RealmSnapshotEvent
    {
        public string Name;
        public int X, Y;
    }

    public sealed class RealmSnapshotLand
    {
        public string Key;          // its theme
        public int X, Y;
        public double Radius;
        public int Seed;            // its painter's
        public int? LinkX, LinkY;   // out in the void: where its bridge or pool joins the rest
        public bool Pool;
        public bool Sentry;         // its Lost Sentry still stands
    }

    public sealed class RealmSnapshotPoint
    {
        public int X, Y;
    }

    public static class RealmSnapshots
    {
        public const string Hash = "realms";

        /// <summary>Older than this, a snapshot is from a game that stopped: its realms start afresh.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

        // never over a closed realm's: a server that hasn't heard yet mustn't open it again
        private const string SaveScript =
            "local v = redis.call('HGET', KEYS[1], ARGV[1]) " +
            "if v and string.find(v, '\"Closed\":true', 1, true) then return 0 end " +
            "redis.call('HSET', KEYS[1], ARGV[1], ARGV[2]) return 1";

        public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static string Write(RealmSnapshot snapshot) => JsonConvert.SerializeObject(snapshot);

        /// <summary>null if it can't be read.</summary>
        public static RealmSnapshot Read(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                var s = JsonConvert.DeserializeObject<RealmSnapshot>(json);
                return s == null || string.IsNullOrEmpty(s.Key) ? null : s;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static bool Fresh(RealmSnapshot s, long now) => s != null && now - s.At <= MaxAge.TotalSeconds && s.At <= now + 60;

        /// <summary>
        /// At start-up: the realms to open again (fresh and not closed), realms before Realm 2; stale
        /// and unreadable ones are removed. Nothing (start afresh) if Redis can't be read.
        /// </summary>
        public static List<RealmSnapshot> Load(IDatabase db)
        {
            try
            {
                var now = Now();
                var keep = new List<RealmSnapshot>();
                var gone = new List<RedisValue>();
                foreach (var entry in db.HashGetAll(Hash))
                {
                    var s = Read(entry.Value);
                    if (!Fresh(s, now))
                        gone.Add(entry.Name);
                    else if (!s.Closed)
                        keep.Add(s);
                }
                if (gone.Count > 0)
                    db.HashDelete(Hash, gone.ToArray(), CommandFlags.FireAndForget);
                return keep.OrderBy(s => s.Realm2).ThenBy(s => s.At).ToList();
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Warn($"Realm snapshots: couldn't read them, the realms start afresh: {e.Message}");
                return new List<RealmSnapshot>();
            }
        }

        // Neither waits: the world thread goes on, and a lost write only means an older snapshot.
        public static void Save(IDatabase db, RealmSnapshot snapshot)
        {
            try
            {
                db.ScriptEvaluate(SaveScript, new RedisKey[] { Hash }, new RedisValue[] { snapshot.Key, Write(snapshot) }, CommandFlags.FireAndForget);
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Warn($"Realm snapshot {snapshot.Name}: {e.Message}");
            }
        }

        public static void SaveClosed(IDatabase db, string key)
        {
            try
            {
                db.HashSet(Hash, key, Write(new RealmSnapshot { Key = key, Closed = true, At = Now(), Running = null }), flags: CommandFlags.FireAndForget);
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Warn($"Realm snapshot {key}: {e.Message}");
            }
        }

        public static async Task<RealmSnapshot> Fetch(IDatabase db, string key)
        {
            try
            {
                return Read(await db.HashGetAsync(Hash, key));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
