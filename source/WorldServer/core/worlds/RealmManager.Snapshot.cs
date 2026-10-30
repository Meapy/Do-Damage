using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.setpieces;
using WorldServer.core.structures;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.realm2;

namespace WorldServer.core.worlds
{
    // A realm's state across world servers (RealmSnapshots; SETUP-LOCAL.md, "Realm state across
    // restarts"): its name, the events defeated toward closing, those running, and Realm 2's map,
    // lands, waystones and the events still to come. Written when it changes and every 30 s;
    // the next world server opens the realm again from it (Restore). While a rolling deploy hands
    // over, the old server's realm and the new one's are the same realm: each takes up the other's
    // count when it's higher, and one closing closes the other.
    public sealed partial class RealmManager
    {
        /// <summary>The realm's key in the snapshots, the same on every server; null: not kept (a Realm 2 from a key).</summary>
        public string SnapshotKey;

        private bool _snapshotDirty;
        private long _lastSnapshot = -SnapshotMs;
        private long _lastMerge;
        private const int SnapshotMs = 30000;
        private const int MergeMs = 10000;

        // the realm's own set pieces' monsters, still to come into the world when a snapshot's
        // events are spawned (Restore), so they're still told apart from those
        private HashSet<objects.Entity> _setPieces = new HashSet<objects.Entity>();

        /// <summary>RealmWorld.Init, before Restore: what its set pieces placed.</summary>
        public void SetPiecesPlaced(IEnumerable<objects.Entity> placed) => _setPieces = new HashSet<objects.Entity>(placed);

        private StackExchange.Redis.IDatabase Db => World.GameServer.Database.Conn;

        // with the events list, every 2 s
        private void SnapshotTick()
        {
            if (SnapshotKey == null || DisableSpawning)
                return;
            // a server handing over writes only what its players change: the new one keeps it
            if (_snapshotDirty || !World.GameServer.Draining && _now - _lastSnapshot >= SnapshotMs)
            {
                _snapshotDirty = false;
                _lastSnapshot = _now;
                RealmSnapshots.Save(Db, Snapshot());
            }
            if (_now - _lastMerge >= MergeMs)
            {
                _lastMerge = _now;
                var me = World.GameServer.InstanceId;
                _ = RealmSnapshots.Fetch(Db, SnapshotKey).ContinueWith(t =>
                {
                    if (t.Result is RealmSnapshot s && s.By != me)
                        World.Post(() => Adopt(s));
                });
            }
        }

        // the same realm on the other server got further, or closed
        private void Adopt(RealmSnapshot s)
        {
            if (DisableSpawning || World.Closed)
                return;
            if (s.Closed)
            {
                World.CloseRealm();
                return;
            }
            if (s.Events <= _EventCount)
                return;
            _EventCount = s.Events;
            SendEventLists();
            if (_EventCount >= EventsNeeded)
                World.CloseRealm();
        }

        /// <summary>RealmWorld.CloseRealm: closed for good, on every server.</summary>
        public void SnapshotClosed()
        {
            if (SnapshotKey != null)
                RealmSnapshots.SaveClosed(Db, SnapshotKey);
        }

        private RealmSnapshot Snapshot()
        {
            var s = new RealmSnapshot
            {
                Key = SnapshotKey,
                Name = World.DisplayName,
                Realm2 = Layout != null,
                At = RealmSnapshots.Now(),
                By = World.GameServer.InstanceId,
                Events = _EventCount,
            };
            foreach (var part in _parts)
                if (part.Counted && !part.Static && !part.Enemy.Dead && part.Enemy.World == World)
                    s.Running.Add(new RealmSnapshotEvent { Name = part.Event, X = (int)part.Enemy.X, Y = (int)part.Enemy.Y });

            if (Layout == null)
            {
                s.Left = _events.Select(e => e.Item1).ToList();
                return s;
            }
            s.Seed = (World as Realm2World)?.Seed ?? 0;
            s.Upcoming = _upcoming.ToList();
            s.Lands = Layout.Areas.Where(a => a.Theme != null).Select(a => new RealmSnapshotLand
            {
                Key = a.Kind, X = a.Center.X, Y = a.Center.Y, Radius = a.Radius, Seed = a.PainterSeed,
                LinkX = a.LinkTo?.X, LinkY = a.LinkTo?.Y, Pool = a.Link == "pool",
                Sentry = _sideQuests.Any(e => !e.Dead && e.World == World && Layout.AreaAt((int)e.X, (int)e.Y) == a),
            }).ToList();
            s.Waystones = Beacons().Select(b => new RealmSnapshotPoint { X = (int)b.X, Y = (int)b.Y }).ToList();
            s.SinceBeacon = _sinceBeacon;
            s.Voiced = _voiced;
            return s;
        }

        /// <summary>
        /// RealmWorld.Init, once the realm has its set pieces: back to where the snapshot left it.
        /// What doesn't fit (a land or event this version doesn't have) is left out.
        /// </summary>
        public void Restore(RealmSnapshot s)
        {
            _EventCount = Math.Max(0, s.Events);
            if (Layout != null)
                RestoreLayout(s);
            else if (s.Left != null)
                _events.RemoveAll(e => !s.Left.Contains(e.Item1));

            // the events that were running come back where they were (at full health); counted
            // as spawned, not as the realm's own set pieces
            _spawned++;
            foreach (var evt in s.Running ?? new List<RealmSnapshotEvent>())
            {
                if (Layout != null)
                {
                    var mid = Layout.Areas.First(a => a.Kind == Realm2Themes.Midlands);
                    if (mid.Events.Any(e => e.Event == evt.Name))
                    {
                        SpawnLayoutEvent(evt.Name, mid.Events.First(e => e.Event == evt.Name).At, mid, false);
                        continue;
                    }
                    var land = Layout.Areas.Where(a => a.Theme?.Event == evt.Name)
                        .OrderBy(a => Realm2Canvas.Dist(a.Center, evt.X, evt.Y)).FirstOrDefault();
                    if (land != null)
                        SpawnLayoutEvent(evt.Name, land.Center, land, false);
                }
                else if (EventParts.ContainsKey(evt.Name) && evt.X >= 0 && evt.Y >= 0 && evt.X < World.Map.Width && evt.Y < World.Map.Height)
                    SpawnEvent(evt.Name, SetPieceFor(evt.Name), evt.X, evt.Y, false);
            }
            _lastEventChange = _now;
            Log.Info($"{World.DisplayName}: opened again from its snapshot, {_EventCount} events defeated, {s.Running?.Count ?? 0} running" +
                (Layout != null ? $", {s.Lands?.Count ?? 0} lands, {s.Waystones?.Count ?? 0} waystones" : ""));
        }

        private static ISetPiece SetPieceFor(string name) => name switch
        {
            "Pentaract" => new Pentaract(),
            "Garnet Statue" => new GarnetJade(),
            "shtrs Defense System" => new ShtrsDefenseSystem(),
            _ => null,
        };

        private void RestoreLayout(RealmSnapshot s)
        {
            var events = Realm2Themes.Events.Select(t => t.Event)
                .Concat(Layout.Areas.First(a => a.Kind == Realm2Themes.Midlands).Events.Select(e => e.Event)).ToHashSet();
            if (s.Upcoming != null)
            {
                _upcoming.Clear();
                foreach (var evt in s.Upcoming.Where(events.Contains))
                    _upcoming.Enqueue(evt);
            }
            foreach (var land in s.Lands ?? new List<RealmSnapshotLand>())
            {
                var theme = Realm2Themes.Events.FirstOrDefault(t => t.Key == land.Key);
                if (theme == null || land.X < 0 || land.Y < 0 || land.X >= Layout.Width || land.Y >= Layout.Height)
                    continue;
                var link = land.LinkX is int lx && land.LinkY is int ly ? new IntPoint(lx, ly) : (IntPoint?)null;
                DrawLand(theme, new IntPoint(land.X, land.Y), link, land.Pool, land.Radius, land.Seed, land.Sentry);
            }
            foreach (var w in s.Waystones ?? new List<RealmSnapshotPoint>())
                if (w.X >= 0 && w.Y >= 0 && w.X < Layout.Width && w.Y < Layout.Height)
                    PlaceBeacon(new IntPoint(w.X, w.Y), Layout.AreaAt(w.X, w.Y), false);
            _sinceBeacon = s.SinceBeacon;
            _voiced = s.Voiced;
        }
    }
}
