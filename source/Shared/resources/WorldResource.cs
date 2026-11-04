using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Shared.resources
{
    public enum WorldResourceInstanceType
    {
        Nexus,
        Vault,
        Guild,
        Dungeon,
        Test,
        Realm2
    }

    public sealed class WorldResource
    {
        public readonly string IdName;
        public readonly string DisplayName;
        public readonly int Width;
        public readonly int Height;
        public readonly int Capacity;
        public readonly WorldResourceInstanceType Instance;
        public readonly bool Persists;
        public readonly byte Difficulty;
        public readonly byte Background;
        public readonly byte VisibilityType;
        public readonly List<string> MapJM;
        public readonly RandomLayoutDesc RandomLayout;
        public readonly List<string> Music;
        public readonly bool AllowTeleport;
        public readonly bool ShowDisplays;
        public readonly bool DisableShooting;
        public readonly bool DisableAbilities;
        public readonly bool CreateInstance;
        public readonly bool DefeatToNexus;
        public readonly double LootRate;
        public readonly double FameRate;
        public readonly double KillFameRate;
        // floors stacked over the map (`<Levels height="3"/>`): "<map> 1.jm", "<map> 2.jm"... are
        // the floors above it, this many tiles apart (World.Levels); 0 for a flat world
        public readonly float LevelHeight;
        // a Nexus's realm portal north of its Realm 2 portal (`<RealmPortalNorth>`, an object id;
        // RealmPortalMonitor): the islands' turned, its steps to the north, so every cavern faces
        // away from the shrine's middle; elsewhere, and by default, the garden's
        public readonly string RealmPortalNorth;

        public WorldResource(XElement elem)
        {
            IdName = elem.GetAttribute<string>("id");
            DisplayName = elem.GetValue("DisplayName", IdName);
            Width = elem.GetValue<int>("Width");
            Height = elem.GetValue<int>("Height");
            Capacity = elem.GetValue("Capacity", 65);
            Instance = elem.Element("Instance") == null ? WorldResourceInstanceType.Dungeon : (WorldResourceInstanceType)Enum.Parse(typeof(WorldResourceInstanceType), elem.Element("Instance").GetAttribute("enum", "dungeon"), true);
            Persists = elem.HasElement("Persists");
            Difficulty = elem.GetValue<byte>("Difficulty");
            // as an int: GetValue doesn't read bytes (it gave 0 for every world's Background)
            Background = (byte)elem.GetValue<int>("Background");
            VisibilityType = (byte)elem.GetValue<int>("VisibilityType");
            AllowTeleport = !elem.HasElement("DisableTeleport");
            ShowDisplays = elem.HasElement("ShowDisplays");
            DisableShooting = elem.HasElement("DisableShooting");
            DisableAbilities = elem.HasElement("DisableAbilities");
            CreateInstance = elem.HasElement("CreateInstance");
            DefeatToNexus = elem.HasElement("DefeatToNexus");
            LootRate = elem.GetValue("LootRate", 1.0);
            FameRate = elem.GetValue("FameRate", 1.0);
            KillFameRate = elem.GetValue("KillFameRate", 1.0);
            LevelHeight = elem.Element("Levels") is XElement levels ? levels.GetAttribute("height", 3f) : 0;
            RealmPortalNorth = elem.Element("RealmPortalNorth")?.Value.Trim();

            MapJM = new List<string>();
            foreach (var map in elem.Elements("MapJM"))
                MapJM.Add(map.Value);

            if (elem.HasElement("RandomLayout"))
                RandomLayout = new RandomLayoutDesc(elem.Element("RandomLayout"));

            Music = new List<string>();
            var musicElem = elem.Element("Music");
            if (musicElem != null)
                foreach (var music in musicElem.Elements("Track"))
                    Music.Add(music.Value);
        }

        public override string ToString()
        {
            var ret = $"IdName: {IdName}\n";
            ret += $"Width: {Width}\n";
            ret += $"Height: {Height}\n";
            ret += $"Capacity: {Capacity}\n";
            ret += $"Instance: {Instance}\n";
            ret += $"Persists: {Persists}\n";
            ret += $"MapJM: {MapJM}";
            return ret;
        }
    }
}
