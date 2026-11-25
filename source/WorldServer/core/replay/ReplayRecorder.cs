using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Shared;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.objects;
using WorldServer.core.terrain;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.replay
{
    /// <summary>
    /// A player's death replay: what they saw over their last seconds, kept all the time outside
    /// the Nexus and saved when they die (Shared/database/DeathReplays.cs), for the client to play
    /// back from the graveyard, the legends and the death's summary.
    ///
    /// It keeps the player's view as the Scrying Glass sends it to those watching
    /// (core/spectate/SpectatorHub.cs): the tiles and objects as they come into sight, every
    /// tick's moves and stats with the player's own place, and the shots and effects they're
    /// sent (<see cref="Forward"/>), each as the message's id and body. With it, every hit the
    /// player took as the server counted it (<see cref="Hurt"/>): what dealt it, its damage before
    /// and after defense, and the health left, so a death can be checked against what was seen.
    ///
    /// It's kept in segments of <see cref="SegmentMs"/>, each opening with a snapshot of
    /// everything in sight then, so the oldest can be let go: a replay is the oldest segment's
    /// snapshot and every message since, <see cref="KeepMs"/> to <see cref="KeepMs"/> +
    /// <see cref="SegmentMs"/> of play. Only the player's public stats are kept (with their
    /// defense), as anyone may watch a death from the legends.
    ///
    /// The world's thread steps it (<see cref="Step"/>, after the player's own update); sends
    /// from other threads come through <see cref="Forward"/>, so it locks itself.
    /// </summary>
    public sealed class ReplayRecorder
    {
        public const int SegmentMs = 5000;
        public const int KeepMs = 20000;
        // a segment this big starts a fresh recording, the older ones let go (a flood of shots)
        private const int MaxSegmentBytes = 3 << 20;
        // tiles remembered (a long walk through a realm): past this they're sent again
        private const int MaxTiles = 200_000;

        public const byte Version = 1;
        // record kinds
        public const byte KindMessage = 0;
        public const byte KindHurt = 1;
        public const byte KindDeath = 2;

        // what dealt a hit (KindHurt)
        public const byte FromShot = 0;
        public const byte FromHit = 1;
        public const byte FromGround = 2;
        public const byte FromAir = 3;

        // a hit's flags
        public const byte FlagPierce = 1;
        public const byte FlagDodged = 2;
        public const byte FlagStacked = 4;
        public const byte FlagNoHarm = 8;

        private sealed class Segment
        {
            public long Start;
            public byte[] Snapshot;
            public readonly MemoryStream Events = new MemoryStream();
            public NetworkWriter Writer;
        }

        private readonly Player _player;
        private readonly List<Segment> _segments = new List<Segment>();
        // the stream every message is first written into, to know its length
        private readonly MemoryStream _scratch = new MemoryStream(4096);
        private readonly NetworkWriter _scratchWriter;

        // what the replay knows of the view so far (as SpectatorHub's Spectator)
        private World _world;
        private readonly Dictionary<int, byte> _tiles = new Dictionary<int, byte>();
        private readonly HashSet<Entity> _objects = new HashSet<Entity>();
        // the map's own objects in sight, and where
        private readonly Dictionary<WmapTile, (int X, int Y)> _statics = new Dictionary<WmapTile, (int, int)>();
        private readonly List<Entity> _gone = new List<Entity>();
        private readonly List<WmapTile> _goneStatics = new List<WmapTile>();
        private int _sightStamp = -1;
        private int _ticks;
        private HashSet<StatDataType> _shown;

        // the stats of the player kept beyond the public ones: what their defense took off
        private static readonly StatDataType[] Own = { StatDataType.Defense, StatDataType.DefenseBonus, StatDataType.Vitality, StatDataType.VitalityBonus };

        public ReplayRecorder(Player player)
        {
            _player = player;
            _scratchWriter = new NetworkWriter(_scratch);
        }

        private static long Now => Environment.TickCount64;

        public static bool Records(World world) => world != null && world is not NexusWorld;

        private void Reset()
        {
            _segments.Clear();
            _tiles.Clear();
            _objects.Clear();
            _statics.Clear();
            _sightStamp = -1;
            _ticks = 0;
            _world = null;
        }

        /// <summary>After the player's own update and tick (their world's thread).</summary>
        public void Step(List<ObjectStats> statuses)
        {
            var p = _player;
            var world = p.World;
            lock (this)
            {
                if (!Records(world) || p.Dead || p.Ghost)
                {
                    if (_world != null)
                        Reset();
                    return;
                }
                try
                {
                    StepIn(world, statuses);
                }
                catch (Exception e)
                {
                    // a replay is never worth a player's tick
                    StaticLogger.Instance.Error($"Replay of {p.Name} stopped: {e}");
                    Reset();
                }
            }
        }

        private void StepIn(World world, List<ObjectStats> statuses)
        {
            var p = _player;
            var now = Now;
            if (_world != world)
            {
                Reset();
                _world = world;
            }
            _ticks++;

            var update = new Update();
            if (_tiles.Count > MaxTiles)
                _tiles.Clear();
            var map = world.Map;
            if (_sightStamp != p.SightStamp)
            {
                _sightStamp = p.SightStamp;
                foreach (var point in p.SightTiles)
                {
                    var tile = map[point.X, point.Y];
                    if (!tile.Exists)
                        continue;
                    var key = point.X << 16 | point.Y;
                    var known = _tiles.TryGetValue(key, out var count);
                    if (known ? count >= tile.UpdateCount : tile.TileId == 0xFF)
                        continue;
                    _tiles[key] = tile.UpdateCount;
                    update.Tiles.Add(new TileData(point.X, point.Y, tile.TileId));
                }
                foreach (var point in p.SightTiles)
                    AddStatics(update, world, point.X, point.Y);
            }
            if (_ticks % 20 == 0)
            {
                _goneStatics.Clear();
                foreach (var t in _statics.Keys)
                    if (t.ObjType == 0)
                        _goneStatics.Add(t);
                foreach (var t in _goneStatics)
                {
                    _statics.Remove(t);
                    if (t.ObjId != 0)
                        update.Drops.Add(t.ObjId);
                }
            }

            var objects = p.KnownObjects;
            foreach (var e in objects)
                if (!e.Dead && _objects.Add(e))
                    update.NewObjs.Add(e.ToDefinition(e is Player));
            _gone.Clear();
            foreach (var e in _objects)
                if (e.Dead || !objects.Contains(e))
                    _gone.Add(e);
            foreach (var e in _gone)
            {
                _objects.Remove(e);
                update.Drops.Add(e.Id);
            }

            // a new segment every SegmentMs, opening with a snapshot of all that's in sight now
            var current = _segments.Count > 0 ? _segments[^1] : null;
            var fresh = current == null || now - current.Start >= SegmentMs || current.Events.Length > MaxSegmentBytes;
            if (current != null && current.Events.Length > MaxSegmentBytes)
                _segments.Clear();
            if (update.Tiles.Count > 0 || update.NewObjs.Count > 0 || update.Drops.Count > 0)
                Write(KindMessage, update, now);
            Write(KindMessage, Tick(statuses), now);
            if (fresh)
                StartSegment(world, now);
            // the oldest let go once the next one alone covers KeepMs
            while (_segments.Count > 1 && now - _segments[1].Start >= KeepMs)
                _segments.RemoveAt(0);
        }

        private void AddStatics(Update update, World world, int x, int y)
        {
            AddStatic(update, world.Map[x, y], x, y);
            for (var i = 1; i < (world.Levels?.Count ?? 0); i++)
                AddStatic(update, world.Levels[i][x, y], x, y);
        }

        private void AddStatic(Update update, WmapTile tile, int x, int y)
        {
            if (tile.Exists && tile.ObjId != 0 && tile.ObjType != 0 && _statics.TryAdd(tile, (x, y)))
                update.NewObjs.Add(tile.ToObjectDef(x, y));
        }

        // the tick as the player got it, their own stats only those anyone may see (and their
        // defense), with their own place every tick (their own tick leaves it out: their client
        // moves them itself)
        private NewTickMessage Tick(List<ObjectStats> statuses)
        {
            var p = _player;
            if (_shown == null)
            {
                _shown = new HashSet<StatDataType>(p.PublicStats ??= new HashSet<StatDataType>(p.ExportStats(true).Stats.Select(kv => kv.Key)));
                _shown.UnionWith(Own);
            }
            var tick = new NewTickMessage(p.TickId, p.TickTime);
            tick.Statuses.Capacity = statuses.Count + 1;
            var self = false;
            foreach (var st in statuses)
            {
                if (st.Id != p.Id)
                {
                    tick.Statuses.Add(st);
                    continue;
                }
                self = true;
                tick.Statuses.Add(new ObjectStats(st.Id, p.X, p.Y, st.Stats.Where(kv => _shown.Contains(kv.Key)).ToArray()));
            }
            if (!self)
                tick.Statuses.Add(new ObjectStats(p.Id, p.X, p.Y, Array.Empty<KeyValuePair<StatDataType, object>>()));
            return tick;
        }

        // everything in sight now, as one Update (and the floors, in a world that has them)
        private void StartSegment(World world, long now)
        {
            var p = _player;
            var map = world.Map;
            var snap = new Update();
            // the tiles kept are those in sight: any seen before that come into sight again are
            // sent again, so a replay from this snapshot misses none
            _tiles.Clear();
            foreach (var point in p.SightTiles)
            {
                var tile = map[point.X, point.Y];
                if (!tile.Exists || tile.TileId == 0xFF)
                    continue;
                _tiles[point.X << 16 | point.Y] = tile.UpdateCount;
                snap.Tiles.Add(new TileData(point.X, point.Y, tile.TileId));
            }
            foreach (var (t, at) in _statics)
                if (t.ObjType != 0 && t.ObjId != 0)
                    snap.NewObjs.Add(t.ToObjectDef(at.X, at.Y));
            foreach (var e in _objects)
                snap.NewObjs.Add(e.ToDefinition(e is Player));
            // the player's own stats as of now: the snapshot's player stands in for every change
            // before it
            // (writers over memory streams aren't disposed: that would close the stream)
            var ms = new MemoryStream();
            var w = new NetworkWriter(ms);
            if (world.Levels != null)
                WriteMessage(w, world.Layers());
            WriteMessage(w, snap);
            w.Flush();
            var seg = new Segment { Start = now, Snapshot = ms.ToArray() };
            seg.Writer = new NetworkWriter(seg.Events);
            _segments.Add(seg);
        }

        private static void WriteMessage(NetworkWriter w, OutgoingMessage m)
        {
            var body = new MemoryStream();
            var bw = new NetworkWriter(body);
            m.Write(bw);
            bw.Flush();
            w.Write((byte)m.MessageId);
            w.Write((int)body.Length);
            w.Write(body.GetBuffer(), 0, (int)body.Length);
        }

        // a record: when, its kind, its length, then what it is
        private void Write(byte kind, OutgoingMessage m, long now)
        {
            if (_segments.Count == 0)
                return;
            _scratch.Position = 0;
            _scratch.SetLength(0);
            _scratchWriter.Write((byte)m.MessageId);
            m.Write(_scratchWriter);
            _scratchWriter.Flush();
            Append(kind, now);
        }

        private void Append(byte kind, long now)
        {
            var seg = _segments[^1];
            var w = seg.Writer;
            w.Write(now);
            w.Write(kind);
            w.Write((int)_scratch.Length);
            w.Write(_scratch.GetBuffer(), 0, (int)_scratch.Length);
        }

        /// <summary>
        /// A message sent to the player (any thread): the shots, effects and numbers among them
        /// go into the replay.
        /// </summary>
        public void Forward(OutgoingMessage message)
        {
            if (!(message is EnemyShootMessage || message is ServerPlayerShoot || message is AllyShootMessage || message is GotoMessage
                || message is ShowEffect || message is AoeMessage || message is DamageMessage || message is Notification))
                return;
            lock (this)
            {
                if (_world == null || _world != _player.World)
                    return;
                try
                {
                    Write(KindMessage, message, Now);
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"Replay of {_player.Name} stopped: {e}");
                    Reset();
                }
            }
        }

        /// <summary>
        /// A hit the player took, as the server counted it: what dealt it (<see cref="FromShot"/>...),
        /// the object's type and id, its damage before defense and what it took off, and the
        /// health left.
        /// </summary>
        public void Hurt(byte from, ushort sourceType, int sourceId, int raw, int dealt, byte flags = 0)
        {
            lock (this)
            {
                if (_world == null || _segments.Count == 0)
                    return;
                _scratch.Position = 0;
                _scratch.SetLength(0);
                var w = _scratchWriter;
                w.Write(from);
                w.Write(sourceType);
                w.Write(sourceId);
                w.Write(raw);
                w.Write(dealt);
                w.Write(_player.Health);
                w.Write(_player.Stats[0]);
                w.Write(flags);
                w.Flush();
                Append(KindHurt, Now);
            }
        }

        /// <summary>
        /// The player died (their world's thread): the replay as the client reads it, gzipped,
        /// or null when there's none (they died the moment they came in).
        /// </summary>
        public byte[] Finish(string killer)
        {
            var p = _player;
            lock (this)
            {
                try
                {
                    if (_world == null || _segments.Count == 0)
                        return null;
                    var now = Now;
                    var start = _segments[0].Start;
                    using var raw = new MemoryStream();
                    using (var gz = new GZipStream(raw, CompressionLevel.Optimal, leaveOpen: true))
                    using (var w = new NetworkWriter(gz))
                    {
                        w.Write((byte)'B');
                        w.Write((byte)'S');
                        w.Write((byte)'K');
                        w.Write((byte)'R');
                        w.Write(Version);
                        w.Write(p.AccountId);
                        w.Write(p.Client?.Character?.CharId ?? 0);
                        w.WriteUTF16(p.Name);
                        w.Write(p.ObjectType);
                        w.WriteUTF16(_world.GetDisplayName());
                        w.WriteUTF16(_world.IdName);
                        w.Write((short)Math.Max(_world.Map.Width, _world.Map.Height));
                        w.Write(_world.Background);
                        w.Write(p.Id);
                        w.WriteUTF16(killer ?? "");
                        w.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        w.Write((int)(now - start));
                        // the snapshot, at 0
                        var snap = _segments[0].Snapshot;
                        w.Write(snap.Length);
                        w.Write(snap);
                        // then every record, its time from the start
                        foreach (var seg in _segments)
                        {
                            seg.Writer.Flush();
                            var buf = seg.Events.GetBuffer();
                            var len = (int)seg.Events.Length;
                            var i = 0;
                            while (i + 13 <= len)
                            {
                                var at = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(buf.AsSpan(i));
                                var kind = buf[i + 8];
                                var n = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(i + 9));
                                w.Write((int)(at - start));
                                w.Write(kind);
                                w.Write(n);
                                w.Write(buf, i + 13, n);
                                i += 13 + n;
                            }
                        }
                        // the death itself
                        w.Write((int)(now - start));
                        w.Write(KindDeath);
                        _scratch.Position = 0;
                        _scratch.SetLength(0);
                        _scratchWriter.WriteUTF16(killer ?? "");
                        _scratchWriter.Flush();
                        w.Write((int)_scratch.Length);
                        w.Write(_scratch.GetBuffer(), 0, (int)_scratch.Length);
                    }
                    return raw.ToArray();
                }
                catch (Exception e)
                {
                    StaticLogger.Instance.Error($"Replay of {p.Name}'s death not saved: {e}");
                    return null;
                }
                finally
                {
                    Reset();
                }
            }
        }
    }
}
