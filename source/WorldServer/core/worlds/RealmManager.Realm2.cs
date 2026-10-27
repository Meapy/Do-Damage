using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.setpieces;
using WorldServer.core.structures;
using WorldServer.core.worlds.realm2;
using WorldServer.utils;

namespace WorldServer.core.worlds
{
    // Realm 2 (Realm2World): its map comes with a layout, and the realm runs by it. It opens with
    // the shore and lowlands, the midlands and the Godlands, whose monsters live by terrain as in
    // the realm. Events come as the realm's do (EnsureQuest: one at a time for a few players, more
    // for a crowd): first the two Lucky gods in their midland clearings, then the eleven events in a
    // random order, each bringing its own land, drawn into the Godlands (or out in the void with a
    // bridge or pool) further from the shore than the last, in any direction. Each land keeps its
    // monsters, in small groups as the gods are, and two of each of its quests. Realm 2 needs every
    // event to close (EventsNeeded), more for a crowd; once all have come, more come to new lands.
    public sealed partial class RealmManager
    {
        public Realm2Layout Layout;

        private const double PopulationLow = 0.75;      // a land below this share of its monsters is topped up
        private const int SpawnClear = 12;              // tiles from any player a monster may appear
        private const int TilesPerMob = 125;            // as the realm's Godlands
        private const double LandRadius = 50;
        private const int Realm2StaleMs = 3 * 60000;    // an event nobody has touched lets the next come after this
        private const int Realm2DroughtMs = 30000;      // with no event running this long, one comes wherever it can
        private const double Apart = 90;                // a land's heart at least this far from every other land's
        private const double MaxBack = 15;              // how much nearer the shore than the last a land may come

        private Dictionary<ushort, (Realm2Theme Theme, bool Quest)> _areaMobs;
        private readonly Queue<string> _upcoming = new Queue<string>();
        private double _lastAlong;
        private int _lands;                             // event lands drawn so far

        private void InitLayout()
        {
            var data = World.GameServer.Resources.GameData;
            _areaMobs = new Dictionary<ushort, (Realm2Theme, bool)>();
            foreach (var theme in Realm2Themes.Events)
            {
                foreach (var id in theme.Mobs.Select(m => m.Id))
                    if (data.IdToObjectType.TryGetValue(id, out var type))
                        _areaMobs[type] = (theme, false);
                    else
                        Log.Warn("Realm 2: no monster '{0}' ({1})", id, theme.Key);
                foreach (var id in theme.Quests)
                    if (data.IdToObjectType.TryGetValue(id, out var type))
                        _areaMobs[type] = (theme, true);
                    else
                        Log.Warn("Realm 2: no quest '{0}' ({1})", id, theme.Key);
            }

            // the Lucky gods first, in either order, then the event lands in a random one
            var mid = Layout.Areas.First(a => a.Kind == Realm2Themes.Midlands);
            foreach (var (evt, _) in mid.Events.OrderBy(_ => Random.Shared.Next()))
                _upcoming.Enqueue(evt);
            QueueLands();
            _lastAlong = Layout.GodlandsFrom;
        }

        private void QueueLands()
        {
            foreach (var theme in Realm2Themes.Events.OrderBy(_ => Random.Shared.Next()))
                _upcoming.Enqueue(theme.Event);
        }

        private void LayoutWelcome(Player player)
        {
            player.SendInfo("Welcome to Realm 2!");
            player.SendInfo($"Its events bring lands of their own as they come: defeat {EventsNeeded} and Oryx must face you.");
            player.SendInfo("Head out from the shore, through the midlands to the Godlands.");
            player.SendEnemy("Oryx the Mad God", "You wander into MY menagerie? Every beast here answers to me!");
        }

        /// <summary>For /realm2 next: bring the next event now, whatever is running.</summary>
        public string ForceLayoutEvent()
        {
            if (Layout == null || DisableSpawning)
                return "No events come here now.";
            if (_upcoming.Count == 0)
                QueueLands();
            var next = _upcoming.Peek();
            if (!SpawnNextLayoutEvent(true))
                return $"Nowhere fits {EventTitle(next)}.";
            _lastEventChange = _now;
            return $"{EventTitle(next)} is coming.";
        }

        // ---- events

        // EnsureQuest's turn for an event: false if none could come now (no spot fits)
        private bool SpawnNextLayoutEvent(bool force = false)
        {
            if (_upcoming.Count == 0)
                QueueLands();
            var evt = _upcoming.Peek();
            var mid = Layout.Areas.First(a => a.Kind == Realm2Themes.Midlands);
            foreach (var (name, at) in mid.Events)
                if (name == evt)
                {
                    _upcoming.Dequeue();
                    SpawnLayoutEvent(name, at, mid, true);
                    return true;
                }

            var theme = Realm2Themes.Events.First(t => t.Event == evt);
            var site = PickSite(force) ?? (force ? PickSite(true, true) : null);
            if (site == null)
                return false;
            _upcoming.Dequeue();
            var area = DrawLand(theme, site.Value.At, site.Value.LinkTo, site.Value.Pool);
            SpawnLayoutEvent(evt, area.Center, area, true);
            return true;
        }

        // Where the next land goes: the eleven spread evenly along the Godlands from the midlands to
        // its far end (the nth at about n/11 of the way), never nearer the shore than the last, any
        // way across; best on the Godlands' edge, sticking out into the void, so its middle stays
        // free (or, a quarter of the time, out in the void beside it, joined by a bridge or pool);
        // out of sight of everyone but not far from someone, and clear of the events still running.
        // `force` (a drought): players only need to be off the land's heart, not out of it, and
        // needn't be near; `anywhere`: nor does the way out, and lands need only be 60 apart.
        private (IntPoint At, IntPoint? LinkTo, bool Pool)? PickSite(bool force = false, bool anywhere = false)
        {
            var players = World.Players.Values.ToArray();
            var running = _parts.Where(p => p.Counted && !p.Enemy.Dead && p.Enemy.World == World)
                .Select(p => new IntPoint((int)p.Enemy.X, (int)p.Enemy.Y)).ToList();
            var edge = (int)(LandRadius * 1.25) + 6;
            var island = Random.Shared.NextDouble() < 0.25;
            var count = Realm2Themes.Events.Length;
            var span = Layout.GodlandsTo - Layout.GodlandsFrom;
            var target = Layout.GodlandsFrom + span * ((_lands % count) + 0.6) / count;

            for (var pass = 0; pass < 5; pass++)
            {
                var lo = Math.Max(_lastAlong - (pass < 4 ? MaxBack : 80), target - 40 - pass * 15);
                var hi = target + 45 + pass * 25;
                for (var tries = 0; tries < 500; tries++)
                {
                    var x = Random.Shared.Next(edge, Layout.Width - edge);
                    var y = Random.Shared.Next(edge, Layout.Height - edge);
                    var along = Layout.Along(x, y);
                    if (!anywhere && (along < lo || along > hi))
                        continue;
                    var p = new IntPoint(x, y);
                    // well apart from every land before it: they may touch, not pile up
                    var apart = anywhere || pass >= 4 ? 60 : pass < 3 ? Apart : 75;
                    if (Layout.Areas.Any(a => a.Theme != null && Realm2Canvas.Dist(a.Center, x, y) < apart))
                        continue;
                    if (!anywhere && running.Any(r => Realm2Canvas.Dist(r, x, y) < 85))
                        continue;
                    // no one inside the land when it comes; someone within reach of it, if anyone's here
                    if (players.Any(pl => pl.DistTo(x, y) < (force ? 20 : LandRadius + 8)))
                        continue;
                    if (!force && pass < 2 && players.Length > 0 && !players.Any(pl => pl.DistTo(x, y) < 150))
                        continue;
                    // nothing of the starter lands or midlands under it, nor the Godlands kept as they are
                    if (Covers(p, LandRadius * 1.2, a => a.Kind is Realm2Themes.Starter or Realm2Themes.Midlands))
                        continue;
                    if (Layout.Untouched.Any(u => Realm2Canvas.Dist(u.At, x, y) < LandRadius * 1.25 + u.R))
                        continue;

                    if (island && pass < 2)
                    {
                        if (Layout.AreaOf[x, y] >= 0)
                            continue;
                        var to = NearestLand(p, LandRadius * 0.9, LandRadius + 28);
                        if (to == null)
                            continue;
                        return (p, to, Random.Shared.NextDouble() < 0.4);
                    }
                    if (Layout.AreaOf[x, y] < 0)
                        continue;
                    if (pass < 2 && !Covers(p, LandRadius * 0.9, a => false, true))
                        continue;
                    return (p, null, false);
                }
            }
            return null;
        }

        // whether any of a ring of points round c lies in an area `what` takes (or, with voidToo, in the void)
        private bool Covers(IntPoint c, double r, Func<Realm2Area, bool> what, bool voidToo = false)
        {
            for (var a = 0.0; a < Math.PI * 2; a += Math.PI / 8)
                for (var d = r / 3; d <= r; d += r / 3)
                {
                    var area = Layout.AreaAt((int)(c.X + Math.Cos(a) * d), (int)(c.Y + Math.Sin(a) * d));
                    if (area == null ? voidToo : what(area))
                        return true;
                }
            return false;
        }

        private IntPoint? NearestLand(IntPoint c, double from, double to)
        {
            for (var d = from; d <= to; d += 2)
                for (var a = 0.0; a < Math.PI * 2; a += Math.PI / 24)
                {
                    var x = (int)(c.X + Math.Cos(a) * d);
                    var y = (int)(c.Y + Math.Sin(a) * d);
                    if (Layout.AreaAt(x, y) is Realm2Area area && (area.Kind == Realm2Themes.Godlands || area.Theme != null))
                        return new IntPoint(x, y);
                }
            return null;
        }

        // Draws the land into the world, clearing the monsters that stood there, and peoples it.
        private Realm2Area DrawLand(Realm2Theme theme, IntPoint at, IntPoint? linkTo, bool pool)
        {
            var radius = LandRadius + Random.Shared.Next(-3, 7);
            var painter = new Realm2EventPainter(World.GameServer.Resources.GameData, Random.Shared.Next(), theme, at, radius);
            painter.Paint((x, y) => Layout.AreaAt(x, y) != null, linkTo, pool);

            var area = new Realm2Area
            {
                Index = Layout.Areas.Count, Theme = theme, Kind = theme.Key, Center = at, Radius = radius,
                Link = linkTo == null ? null : pool ? "pool" : "bridge",
            };
            area.Events.Add((theme.Event, at));
            Layout.Areas.Add(area);
            var mine = new HashSet<IntPoint>();
            foreach (var p in painter.Tiles)
            {
                if (p.X < 0 || p.Y < 0 || p.X >= Layout.Width || p.Y >= Layout.Height || !mine.Add(p))
                    continue;
                if (Layout.AreaAt(p.X, p.Y) is Realm2Area was)
                    was.Tiles--;
                Layout.AreaOf[p.X, p.Y] = area.Index;
            }
            area.Tiles = mine.Count;

            foreach (var enemy in World.Enemies.Values.ToArray())
                if (!_partTypes.ContainsKey(enemy.ObjectType) && mine.Contains(new IntPoint((int)enemy.X, (int)enemy.Y)))
                    World.LeaveWorld(enemy);

            // the Godlands it covers have no gods any more: fewer for the rest, at the same density
            var covered = mine.Count(p => World.Map[p.X, p.Y].Terrain == Shared.resources.TerrainType.Mountains);
            var gods = (int)Shared.resources.TerrainType.Mountains - 1;
            EnemyMaxCounts[gods] = Math.Max(0, EnemyMaxCounts[gods] - covered / RegionMobs[Shared.resources.TerrainType.Mountains].Item1);

            var c = painter.Canvas;
            SetPieces.RenderFromData(World, new IntPoint(c.X0 + c.W / 2, c.Y0 + c.H / 2), c.Export(), true);
            _lastAlong = Math.Max(_lastAlong, Layout.Along(at.X, at.Y));
            _lands++;
            Populate(area);
            return area;
        }

        private void SpawnLayoutEvent(string name, IntPoint at, Realm2Area area, bool announce)
        {
            if (DisableSpawning)
                return;
            _spawned++;
            switch (name)
            {
                case "Pentaract":
                    // its five towers on the points of the pentagram the arena is scored with
                    Place("Pentaract", at.X + 0.5f, at.Y + 0.5f);
                    for (var i = 0; i < 5; i++)
                    {
                        var a = Math.PI * 2 * i / 5 - Math.PI / 2;
                        Place("Pentaract Tower", at.X + 0.5f + (float)(Math.Cos(a) * 13), at.Y + 0.5f + (float)(Math.Sin(a) * 13));
                    }
                    break;
                case "Garnet Statue":
                    // the two statues either side of the garden's altar, which wakes them
                    Place("Garnet Statue", at.X - 5.5f, at.Y + 0.5f);
                    Place("Jade Statue", at.X + 6.5f, at.Y + 0.5f);
                    Place("Encounter Altar", at.X + 0.5f, at.Y + 0.5f);
                    break;
                case "shtrs Defense System":
                    // the Avatar comes with its own arena
                    var sp = new ShtrsDefenseSystem();
                    sp.RenderSetPiece(World, new IntPoint(at.X - sp.Size / 2, at.Y - sp.Size / 2));
                    break;
                default:
                    Place(name, at.X + 0.5f, at.Y + 0.5f);
                    break;
            }
            if (announce)
                RealmBroadcast(area.Theme != null
                    ? $"{EventTitle(name)} has risen, and {area.Name} with it!"
                    : $"{EventTitle(name)} has been spawned in {area.Name}!");
        }

        private Entity Place(string id, float x, float y)
        {
            var entity = Entity.Resolve(World.GameServer, id);
            if (entity == null)
            {
                Log.Warn("Realm 2: no entity '{0}'", id);
                return null;
            }
            entity.Move(x, y);
            World.EnterWorld(entity);
            return entity;
        }

        private static string EventTitle(string name)
        {
            foreach (var dat in CriticalEnemies)
                if (dat.Item1 == name)
                    return dat.Item2.NameOfDeath;
            return name;
        }

        // ---- monsters

        // Every minute: each event land's monsters topped up to its share of its size, and any of its
        // quests that's down brought back, out of sight.
        private void LayoutPopulation()
        {
            if (DisableSpawning || _areaMobs == null)
                return;
            foreach (var area in Layout.Areas)
                if (area.Theme != null)
                    Populate(area);
        }

        private void Populate(Realm2Area area)
        {
            var count = 0;
            var quests = new HashSet<ushort>();
            var questsAt = new List<IntPoint>();
            foreach (var enemy in World.Enemies.Values)
            {
                if (!_areaMobs.TryGetValue(enemy.ObjectType, out var kind) || Layout.AreaAt((int)enemy.X, (int)enemy.Y) != area)
                    continue;
                if (kind.Quest)
                {
                    quests.Add(enemy.ObjectType);
                    questsAt.Add(new IntPoint((int)enemy.X, (int)enemy.Y));
                }
                else
                    count++;
            }

            // in groups of two to four, as the gods come
            var target = area.Tiles / TilesPerMob;
            if (count < target * PopulationLow)
                while (count < target)
                {
                    var placed = SpawnGroup(area, PickMob(area.Theme), 2 + Random.Shared.Next(3));
                    if (placed == 0)
                        break;
                    count += placed;
                }
            var data = World.GameServer.Resources.GameData;
            foreach (var quest in area.Theme.Quests)
                if (data.IdToObjectType.TryGetValue(quest, out var type) && !quests.Contains(type))
                    SpawnQuest(area, quest, questsAt);
        }

        private static string PickMob(Realm2Theme theme)
        {
            var roll = Random.Shared.NextDouble() * theme.Mobs.Sum(m => m.Weight);
            foreach (var (id, weight) in theme.Mobs)
            {
                roll -= weight;
                if (roll <= 0)
                    return id;
            }
            return theme.Mobs[^1].Id;
        }

        // A land's quests apart from each other: in its middle ring, the second roughly across the
        // land from the first, never within 22 tiles of another of its quests
        private void SpawnQuest(Realm2Area area, string id, List<IntPoint> others)
        {
            for (var tries = 0; tries < 80; tries++)
            {
                var angle = others.Count == 0
                    ? Random.Shared.NextDouble() * Math.PI * 2
                    : Math.Atan2(others[0].Y - area.Center.Y, others[0].X - area.Center.X) + Math.PI + (Random.Shared.NextDouble() - 0.5) * 1.8;
                var dist = area.Radius * (0.45 + Random.Shared.NextDouble() * 0.35);
                var x = (int)(area.Center.X + Math.Cos(angle) * dist);
                var y = (int)(area.Center.Y + Math.Sin(angle) * dist);
                if (Layout.AreaAt(x, y) != area || !World.IsPassable(x, y) || World.AnyPlayerNearby(x, y, SpawnClear)
                    || others.Any(o => Realm2Canvas.Dist(o, x, y) < 22))
                    continue;
                if (Place(id, x + 0.5f, y + 0.5f) != null)
                    others.Add(new IntPoint(x, y));
                return;
            }
            SpawnGroup(area, id, 1);
        }

        private int SpawnGroup(Realm2Area area, string id, int size)
        {
            for (var tries = 0; tries < 60; tries++)
            {
                var angle = Random.Shared.NextDouble() * Math.PI * 2;
                var dist = Realm2EventPainter.ArenaRadius + 6 + Random.Shared.NextDouble() * area.Radius;
                var x = (int)(area.Center.X + Math.Cos(angle) * dist);
                var y = (int)(area.Center.Y + Math.Sin(angle) * dist);
                if (Layout.AreaAt(x, y) != area || !World.IsPassable(x, y) || World.AnyPlayerNearby(x, y, SpawnClear))
                    continue;
                var placed = 0;
                for (var i = 0; i < size; i++)
                {
                    var px = x + Random.Shared.Next(-3, 4);
                    var py = y + Random.Shared.Next(-3, 4);
                    if (Layout.AreaAt(px, py) == area && World.IsPassable(px, py) && Place(id, px + 0.5f, py + 0.5f) != null)
                        placed++;
                }
                return placed;
            }
            return 0;
        }

        private string LayoutStatus()
        {
            string Quests(Realm2Area a) => string.Join(" ", World.Enemies.Values
                .Where(e => _areaMobs.TryGetValue(e.ObjectType, out var k) && k.Quest && Layout.AreaAt((int)e.X, (int)e.Y) == a)
                .Select(e => $"{e.ObjectDesc.DisplayId ?? e.ObjectDesc.IdName} ({(int)e.X}, {(int)e.Y})"));
            var lands = Layout.Areas.Where(a => a.Theme != null).Select(a => $"{a.Kind}{(a.Link != null ? " by " + a.Link : "")} ({a.Center.X}, {a.Center.Y}) quests: {Quests(a)}");
            return $"Realm 2: next {string.Join(", ", _upcoming.Take(3).Select(EventTitle))}; lands: {string.Join(", ", lands)}; kept: {string.Join(", ", Layout.Untouched.Select(u => $"({u.At.X}, {u.At.Y})"))}";
        }
    }
}
