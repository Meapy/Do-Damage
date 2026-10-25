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
    // Realm 2 (Realm2World): its map comes with a layout, and the realm runs by it. Every area's
    // event is up from the start, at its arena (the Lucky gods in their midland clearings), so the
    // realm closes once they're all defeated; a crowd calls for more (EventsNeeded), which come back
    // one at a time to areas whose event is down. Each event area keeps its own monsters and two
    // of each of its quests; the starter lands and midlands have the realm's own, by terrain, as
    // in the realm.
    public sealed partial class RealmManager
    {
        public Realm2Layout Layout;

        private const double PopulationLow = 0.75;      // an area below this share of its monsters is topped up
        private const int SpawnClear = 12;              // tiles from any player a monster may appear

        private Dictionary<ushort, (Realm2Theme Theme, bool Quest)> _areaMobs;

        private void InitLayout()
        {
            var data = World.GameServer.Resources.GameData;
            _areaMobs = new Dictionary<ushort, (Realm2Theme, bool)>();
            foreach (var theme in Realm2Themes.Events)
            {
                foreach (var (id, _) in theme.Mobs)
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

            // every event at once, none of them counting as the realm's own set pieces
            foreach (var area in Layout.Areas)
                foreach (var (evt, at) in area.Events)
                    SpawnLayoutEvent(evt, at, area, false);
            _lastEventChange = 0;
            LayoutPopulation();
        }

        private void LayoutWelcome(Player player)
        {
            player.SendInfo("Welcome to Realm 2!");
            player.SendInfo($"Every realm event has a land of its own here: defeat {EventsNeeded} events and Oryx must face you.");
            player.SendInfo("Head out from the shore: each land beyond holds a realm event in its heart.");
            player.SendEnemy("Oryx the Mad God", "You wander into MY menagerie? Every beast here answers to me!");
        }

        // ---- events

        private void EnsureLayoutEvents()
        {
            _parts.RemoveAll(p => p.Enemy.World != World || p.Enemy.Dead);
            var alive = new HashSet<string>(_parts.Where(p => p.Event != null).Select(p => p.Event));
            if (_EventCount + alive.Count >= EventsNeeded)
                return;

            // another event for the crowd: one whose home has none running, picked at random
            var homes = Layout.Areas.SelectMany(a => a.Events.Select(e => (Area: a, e.Event, e.At)))
                .Where(h => !alive.Contains(h.Event)).ToList();
            if (homes.Count == 0)
                return;
            var home = homes[Random.Shared.Next(homes.Count)];
            SpawnLayoutEvent(home.Event, home.At, home.Area, true);
            _lastEventChange = _now;
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
                RealmBroadcast($"{EventTitle(name)} has been spawned in {area.Name}!");
        }

        private void Place(string id, float x, float y)
        {
            var entity = Entity.Resolve(World.GameServer, id);
            if (entity == null)
            {
                Log.Warn("Realm 2: no entity '{0}'", id);
                return;
            }
            entity.Move(x, y);
            World.EnterWorld(entity);
        }

        private static string EventTitle(string name)
        {
            foreach (var dat in CriticalEnemies)
                if (dat.Item1 == name)
                    return dat.Item2.NameOfDeath;
            return name;
        }

        // ---- monsters

        // Every minute (and at the start): each event area's monsters topped up to their share of
        // its size, and any of its quests that's down brought back, out of sight of players.
        private void LayoutPopulation()
        {
            if (DisableSpawning || _areaMobs == null)
                return;
            var counts = new int[Layout.Areas.Count];
            var quests = new HashSet<(int, ushort)>();
            foreach (var enemy in World.Enemies.Values)
            {
                if (!_areaMobs.TryGetValue(enemy.ObjectType, out var kind))
                    continue;
                var area = Layout.AreaAt((int)enemy.X, (int)enemy.Y);
                if (area?.Theme == null)
                    continue;
                if (kind.Quest)
                    quests.Add((area.Index, enemy.ObjectType));
                else
                    counts[area.Index]++;
            }

            var data = World.GameServer.Resources.GameData;
            foreach (var area in Layout.Areas)
            {
                if (area.Theme == null)
                    continue;
                var target = area.Tiles / area.Theme.TilesPerMob;
                if (counts[area.Index] < target * PopulationLow)
                    for (var i = counts[area.Index]; i < target; i++)
                        SpawnInArea(area, PickMob(area.Theme));
                foreach (var quest in area.Theme.Quests)
                    if (data.IdToObjectType.TryGetValue(quest, out var type) && !quests.Contains((area.Index, type)))
                        SpawnInArea(area, quest);
            }
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

        private void SpawnInArea(Realm2Area area, string id)
        {
            for (var tries = 0; tries < 60; tries++)
            {
                var angle = Random.Shared.NextDouble() * Math.PI * 2;
                var dist = Realm2Generator.ArenaRadius + 6 + Random.Shared.NextDouble() * area.Radius;
                var x = (int)(area.Center.X + Math.Cos(angle) * dist);
                var y = (int)(area.Center.Y + Math.Sin(angle) * dist);
                if (Layout.AreaAt(x, y) != area || !World.IsPassable(x, y) || World.AnyPlayerNearby(x, y, SpawnClear))
                    continue;
                Place(id, x + 0.5f, y + 0.5f);
                return;
            }
        }

        private string LayoutStatus()
        {
            var areas = Layout.Areas.Select((a, i) => $"{a.Kind}{(i < Layout.Links.Count ? " -" + Layout.Links[i] + "-" : "")}");
            return $"Realm 2: {string.Join(" ", areas)}";
        }
    }
}
