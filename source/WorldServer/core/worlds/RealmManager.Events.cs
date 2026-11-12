using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds
{
    // What a realm tells the clients that ask (REALM_EVENTS): its running events, its side quests and
    // Realm 2's beacons, with the count to close it, so a player sees how far the realm is and can
    // pick which event their quest arrow follows (Player.PickQuest).
    public sealed partial class RealmManager
    {
        // Kind of each entry: an event that counts toward closing, a side quest (the Lost Sentry),
        // a beacon to teleport to.
        public const byte EntryEvent = 0, EntrySide = 1, EntryBeacon = 2;

        private readonly List<Enemy> _sideQuests = new List<Enemy>();

        private List<(byte Kind, int ObjectId, ushort ObjectType, string Name, float X, float Y)> EventEntries()
        {
            var list = new List<(byte, int, ushort, string, float, float)>();
            var alive = _parts.Where(p => p.Event != null && p.Enemy.World == World && !p.Enemy.Dead).ToList();
            // each running event by its counted part; one whose counted part has gone while the rest
            // fight on (the hive's bees) by the first of those
            foreach (var part in alive.Where(p => p.Counted))
                list.Add((EntryEvent, part.Enemy.Id, part.Enemy.ObjectType, EventTitle(part.Event), part.Enemy.X, part.Enemy.Y));
            foreach (var evt in alive.Where(p => !p.Counted).GroupBy(p => p.Event))
                if (!alive.Any(p => p.Counted && p.Event == evt.Key))
                {
                    var part = evt.First();
                    list.Add((EntryEvent, part.Enemy.Id, part.Enemy.ObjectType, EventTitle(evt.Key), part.Enemy.X, part.Enemy.Y));
                }

            _sideQuests.RemoveAll(e => e.Dead || e.World != World);
            foreach (var side in _sideQuests)
                list.Add((EntrySide, side.Id, side.ObjectType, side.ObjectDesc.DisplayId ?? side.ObjectDesc.IdName, side.X, side.Y));

            foreach (var beacon in Beacons())
                list.Add((EntryBeacon, beacon.Id, beacon.ObjectType, beacon.Name, beacon.X, beacon.Y));
            return list;
        }

        /// <summary>
        /// For /realmevent: start the event a word of whose id or name is `what`, out of sight not far
        /// from the players (as EnsureQuest does), or with `here` 8 tiles east of them; with
        /// nothing, the list of them.
        /// </summary>
        public string ForceEvent(string what, Player here = null)
        {
            var names = EventParts.Keys.ToList();
            if (string.IsNullOrEmpty(what))
                return "Events: " + string.Join(", ", names.Select(n => EventTitle(n)));
            var w = what.ToLowerInvariant();
            var name = names.FirstOrDefault(n => n.ToLowerInvariant().Contains(w) || EventTitle(n).ToLowerInvariant().Contains(w));
            if (name == null)
                return $"No event '{what}': " + string.Join(", ", names.Select(n => EventTitle(n)));
            if (DisableSpawning)
                return "No events come here now.";
            if (here != null)
            {
                var sp = SetPieceFor(name) ?? new setpieces.NamedEntitySetPiece(name);
                sp.RenderSetPiece(World, new core.structures.IntPoint((int)here.X + 8 - (sp.Size - 1) / 2, (int)here.Y - (sp.Size - 1) / 2));
                _snapshotDirty = true;
            }
            else
                SpawnEvent(name, SetPieceFor(name));
            _spawned++;
            _lastEventChange = _now;
            return $"{EventTitle(name)} is coming.";
        }

        // Every 2 s, and when an event falls: each player who asked gets the list when it has changed
        // (a boss moves a few tiles, an event comes or goes, they pick another).
        private void SendEventLists()
        {
            List<(byte Kind, int ObjectId, ushort ObjectType, string Name, float X, float Y)> entries = null;
            string key = null;
            foreach (var player in World.Players.Values)
            {
                if (!player.WantsRealmEvents)
                    continue;
                if (entries == null)
                {
                    entries = EventEntries();
                    key = $"{_EventCount}/{EventsNeeded}:" + string.Join(",", entries.Select(e => $"{e.Kind}.{e.ObjectId}.{(int)e.X / 3}.{(int)e.Y / 3}"));
                }
                var mine = key + "|" + player.PickedQuestId;
                if (mine == player.RealmEventsSent)
                    continue;
                player.RealmEventsSent = mine;
                player.Client.SendPacket(new RealmEvents
                {
                    Defeated = _EventCount,
                    Needed = EventsNeeded,
                    Picked = player.PickedQuestId,
                    Entries = entries.ToArray(),
                });
            }
        }
    }
}
