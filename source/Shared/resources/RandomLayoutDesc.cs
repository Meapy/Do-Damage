using System.Linq;
using System.Xml.Linq;

namespace Shared.resources
{
    // <RandomLayout> on a world: build a new layout from the rooms of its map for every instance
    public sealed class RandomLayoutDesc
    {
        // the widest passage in the map that still counts as a corridor rather than part of a room
        public readonly int Passage;
        // width of the corridors that join the rooms
        public readonly int Corridor;
        // empty tiles between two joined rooms
        public readonly int MinGap;
        public readonly int MaxGap;
        // share of the rooms on the way to the goal rooms; the rest become side rooms
        public readonly double MainPath;
        // a room holding one of these objects ends a branch (the boss room, a key room)
        public readonly string[] Goals;
        // share of the map's other rooms (not the spawn or goal rooms) used by a small, medium and
        // large layout; each instance picks a size, small 30%, medium 60% and large 10% of the time
        public readonly double Small;
        public readonly double Medium;
        public readonly double Large;
        // objects that block the way only for a while (a wall a drill breaks, breakable blocks): the
        // rooms on either side of them are cut as one room, with the objects inside it
        public readonly string[] Keep;
        // a room holding one of these objects is only joined to the rest through the side nearest it
        // (the drill room's way in, so nobody walks round the wall into the boss's chamber)
        public readonly string[] Entrance;

        public RandomLayoutDesc(XElement e)
        {
            Keep = Split(e.GetAttribute("keep", ""));
            Entrance = Split(e.GetAttribute("entrance", ""));
            Small = e.GetAttribute("small", 0.4);
            Medium = e.GetAttribute("medium", 0.65);
            Large = e.GetAttribute("large", 1.0);
            Passage = e.GetAttribute("passage", 2);
            Corridor = e.GetAttribute("corridor", 2);
            MinGap = e.GetAttribute("minGap", 3);
            MaxGap = e.GetAttribute("maxGap", 9);
            MainPath = e.GetAttribute("mainPath", 0.6);
            Goals = e.Elements("Goal").Select(g => g.Value.Trim()).ToArray();
        }

        private static string[] Split(string list) =>
            list.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
    }
}
