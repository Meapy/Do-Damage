using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.crucible
{
    /// The Crucible: the boss-only tower over the Arena (WorldServer CrucibleWorld, BehaviorDb.Crucible.cs,
    /// tools/build_crucible.py). Who may climb it, its floors and the phases its champions announce.
    /// No game code here, so the tests cover it (CrucibleTests).
    public static class CrucibleRules
    {
        /// The Arena round to have reached (in any season) to climb it.
        public const int UnlockRound = 50;

        /// The world's name (Dungeons.xml) and its portal's (CrucibleObjects.xml).
        public const string WorldName = "The Crucible";
        public const string PortalName = "Crucible Portal";

        /// How many may climb together.
        public const int MaxClimbers = 4;

        /// Whether an account may climb: it reached round 50 of the Arena, holds this month's
        /// Gilded Realm Pass, or is an admin's.
        public static bool Unlocked(int bestArenaRound, bool gilded, bool admin) =>
            admin || gilded || bestArenaRound >= UnlockRound;

        /// Climbs a day (UTC): one, or two with a Gilded Realm Pass. A climb counts as it begins (the
        /// lobby's countdown ends), so leaving a lobby costs nothing. An event may multiply them
        /// (ServerEvents.CrucibleClimbs).
        public static int DailyClimbs(bool gilded, int eventTimes = 1) => (gilded ? 2 : 1) * Math.Max(1, eventTimes);

        /// The UTC day a time falls on, as a number (days since 1970).
        public static int Day(DateTime utc) => (int)(utc - DateTime.UnixEpoch).TotalDays;

        /// Climbs left today, from the day the account last climbed and how many it began that day.
        /// Admins aren't limited.
        public static int ClimbsLeft(int today, int lastDay, int climbsThatDay, bool gilded, bool admin, int eventTimes = 1) =>
            admin ? int.MaxValue : Math.Max(0, DailyClimbs(gilded, eventTimes) - (lastDay == today ? climbsThatDay : 0));

        /// What a player out of climbs is told, with the time until the next UTC day.
        public static string SpentHint(bool gilded, TimeSpan untilReset, int eventTimes = 1) =>
            $"You've climbed the Crucible {Times(DailyClimbs(gilded, eventTimes))} today, all a day allows" +
            (gilded ? "" : " (a Gilded Realm Pass doubles that)") +
            $". It opens to you again in {Math.Max(0, (int)untilReset.TotalHours)}h {Math.Max(0, untilReset.Minutes)}m (00:00 UTC).";

        private static string Times(int n) => n == 1 ? "once" : n == 2 ? "twice" : $"{n} times";

        /// What a locked player is told at the portal.
        public static string LockedHint(int bestArenaRound) =>
            $"The Crucible opens to those who reach round {UnlockRound} in the Arena (your best: {Math.Max(0, bestArenaRound)}), " +
            "or who hold a Gilded Realm Pass.";

        /// The tower's floors, bottom to top: (number, name, its Heart, its champion, its white bag).
        public static readonly IReadOnlyList<CrucibleFloor> Floors = new[]
        {
            new CrucibleFloor(1, "The Iron Bastion", "Bastion Heart", "Ferrogrim, the Iron Warden", "Ferrogrim's Bastion Turret"),
            new CrucibleFloor(2, "The Drowned Sanctum", "Sanctum Heart", "Nerissa, the Tidecaller", "Nerissa's Tidepearl"),
            new CrucibleFloor(3, "The Ember Foundry", "Foundry Heart", "Pyrrhax, the Forge Tyrant", "Pyrrhax's Meteor Mortar"),
            new CrucibleFloor(4, "The Clockwork Orrery", "Orrery Heart", "Chronarch, the Hour Thief", "Chronarch's Coat of Stolen Hours"),
            new CrucibleFloor(5, "The Apex", "Apex Heart", "Vaelith, the Crucible Sovereign", "Vaelith's Final Aria"),
        };

        private static readonly HashSet<string> WhiteBagIds = new HashSet<string>(Floors.Select(f => f.WhiteBag));

        /// Whether an item is one of the champions' white bags (an event may make them drop more often,
        /// ServerEvents.CrucibleWhiteChance).
        public static bool IsWhiteBag(string itemId) => itemId != null && WhiteBagIds.Contains(itemId);

        public static CrucibleFloor Floor(int number) => Floors.FirstOrDefault(f => f.Number == number);

        /// Every champion's name (the tests check each has a behaviour).
        public static IEnumerable<string> Champions => Floors.Select(f => f.Champion);
    }

    /// A floor of the tower: its number, its name as players read it, the invisible Heart at its
    /// centre (where its champion arrives) and the champion. It pays no fame: the champions' loot is
    /// the reward.
    public sealed class CrucibleFloor
    {
        public readonly int Number;
        public readonly string Name;
        public readonly string Heart;
        public readonly string Champion;
        public readonly string WhiteBag;

        public CrucibleFloor(int number, string name, string heart, string champion, string whiteBag)
        {
            Number = number;
            Name = name;
            Heart = heart;
            Champion = champion;
            WhiteBag = whiteBag;
        }
    }

    /// The phases the champions announce (CrucibleMessage.Cue; the Rust client names them in the
    /// player's language, crucible.rs `cue_name`). Never renumber one: clients know them by number.
    public static class CrucibleCues
    {
        public static readonly IReadOnlyDictionary<byte, string> Names = new Dictionary<byte, string>
        {
            // I. Ferrogrim
            [1] = "Hammerfall", [2] = "Lockdown", [3] = "Grinding Blades", [4] = "Iron Tempest",
            // II. Nerissa
            [5] = "Tidal Lanes", [6] = "Rising Tide", [7] = "Whirlpool", [8] = "Maelstrom",
            // III. Pyrrhax
            [9] = "Lava Veins", [10] = "Meteor Storm", [11] = "The Crucible Pours", [12] = "Molten Core",
            // IV. Chronarch
            [13] = "Hour Hands", [14] = "Midnight Chime", [15] = "Time Fracture", [16] = "Reversal",
            // V. Vaelith
            [17] = "Sovereign's Decree", [18] = "Echo of Iron", [19] = "Echo of the Tide", [20] = "Echo of the Forge",
            [21] = "Echo of the Hours", [22] = "The Crucible Breaks", [23] = "Unbound",
            // the champions' last stands (2.6.5), one each, in the order of the floors
            [24] = "Last Bastion", [25] = "Tsunami", [26] = "Molten Sea", [27] = "Zero Hour", [28] = "Final Aria",
        };

        public static string Name(byte cue) => Names.TryGetValue(cue, out var n) ? n : "";
    }
}
