using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;

namespace WorldServer.core.worlds.impl
{
    // The places of the Nexus the minimap marks (the Rust client's events.rs, NEXUS_PLACES): those
    // you talk to (the Warcaller, the Herald, the Divine Forge, the Market Broker, the Pen Keeper...)
    // and the portals (the realms', the vault's, the Arena's), wherever the map puts them, so
    // players can find the way to them before they've been there. Worked out once the map's objects
    // are in. Each comes with the object the minimap draws for it.
    public static class NexusPlaces
    {
        // the classes of those you talk to, with what the map calls them when their name won't do
        private static readonly Dictionary<string, string> Talkers = new Dictionary<string, string>
        {
            ["CommunityGoals"] = null, ["Herald"] = null, ["QuestMaster"] = null, ["Chronicler"] = null,
            ["Forge"] = "Divine Forge", ["MarketNPC"] = null, ["PetPen"] = null, ["ChampionsBoard"] = "Hall of Champions",
            ["ReskinVendor"] = null, ["GuildRegister"] = null
        };

        // the object drawn for those whose own sprite won't do (the islands' forge and Guild Hall door
        // are models, their objects invisible markers)
        private static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            ["Forge"] = "Divine Forge", ["GuildHallPortal"] = "Guild Hall Portal"
        };

        // places this close together share one mark, named for them all
        private const float Together = 3f;

        public static (string Name, byte Kind, float X, float Y, ushort Icon)[] Of(World world)
        {
            var data = world.GameServer.Resources.GameData;
            ushort Type(string id) => id != null && data.IdToObjectType.TryGetValue(id, out var t) ? t : (ushort)0;
            var places = new List<(string, byte, float, float, ushort)>();
            var entities = world.StaticObjects.Values.Cast<Entity>().Concat(world.Enemies.Values).Concat(world.SellableObjects.Values)
                .Concat(world.Portals.Values);
            var seen = new HashSet<string>();
            void Consider(ObjectDesc desc, ushort type, float x, float y)
            {
                if (desc?.Class == null)
                    return;
                var icon = Icons.TryGetValue(desc.Class, out var shown) ? Type(shown) : type;
                if (Talkers.TryGetValue(desc.Class, out var name))
                {
                    name ??= desc.DisplayName;
                    if (seen.Add(name))
                        places.Add((name, 0, x, y, icon));
                }
                else if (desc.IdName == "Vault Portal" || desc.Class == "GuildHallPortal")
                {
                    if (seen.Add(desc.DisplayName))
                        places.Add((desc.DisplayName, 1, x, y, icon));
                }
            }
            foreach (var e in entities)
                Consider(e.ObjectDesc, e.ObjectType, e.X, e.Y);
            // and the map's own objects that are no entity (static ones: the islands' Divine Forge, the
            // Champions Monument), on every floor
            foreach (var map in world.Levels ?? new List<terrain.Wmap> { world.Map })
                for (var y = 0; y < map.Height; y++)
                    for (var x = 0; x < map.Width; x++)
                    {
                        var tile = map[x, y];
                        if (tile.ObjType != 0 && tile.ObjDesc?.Class != null && (Talkers.ContainsKey(tile.ObjDesc.Class) || tile.ObjDesc.Class == "GuildHallPortal"))
                            Consider(tile.ObjDesc, tile.ObjType, x + 0.5f, y + 0.5f);
                    }
            void Region(TileRegion region, string name, string icon)
            {
                var tiles = world.RegionTiles(region);
                if (tiles.Count > 0 && seen.Add(name))
                    places.Add((name, 1, (float)tiles.Average(t => t.Point.X) + 0.5f, (float)tiles.Average(t => t.Point.Y) + 0.5f, Type(icon)));
            }
            Region(TileRegion.Realm_Portals, "Realm Portals", "Nexus Portal");
            Region(TileRegion.Realm2_Portal, "Realm 2", "Nexus Portal");
            Region(TileRegion.Arena_Portal, "The Arena", "Arena Portal");
            Region(TileRegion.Nexus_Bar, "Sky Bar", "NI Barman");

            // places close together share the first one's mark
            var marks = new List<(string Name, byte Kind, float X, float Y, ushort Icon)>();
            foreach (var p in places)
            {
                var i = marks.FindIndex(m => MathF.Abs(m.X - p.Item3) <= Together && MathF.Abs(m.Y - p.Item4) <= Together);
                if (i < 0)
                    marks.Add(p);
                else
                    marks[i] = (marks[i].Name + ", " + p.Item1, marks[i].Kind, marks[i].X, marks[i].Y, marks[i].Icon);
            }
            return marks.ToArray();
        }
    }
}
