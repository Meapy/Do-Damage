using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Shared.resources
{
    // A timed server-wide event (serverSettings.events): a name, when it starts and ends (UTC, such
    // as "2026-10-30T00:00:00Z"), and what it does. It runs from its start until just before its
    // end and switches itself on and off at those moments, with no restart or deploy.
    public class ServerEvent
    {
        public string name { get; set; } = "";
        public DateTime start { get; set; }
        public DateTime end { get; set; }

        // Stat potion drops (ServerEvents.IsStatPotion): each one's chance times potionChance (at
        // most a sure drop), and each one that drops comes potionAmount times. 1: unchanged.
        public double potionChance { get; set; } = 1;
        public int potionAmount { get; set; } = 1;

        // The Mysterious One answers every fall of Oryx the Ascended God (BlackBetween.cs
        // OpenTheBetween; a quarter of the time otherwise)
        public bool mysteriousOne { get; set; }

        // The Necromantic Relic (BossResurrection.AddRelicDrop): its chance from every Elder boss
        // times relicChance (at most a sure drop). 1: unchanged.
        public double relicChance { get; set; } = 1;

        public bool ActiveAt(DateTime utcNow) => utcNow >= start && utcNow < end;

        // "until the end of 6 November (UTC)" for an end at midnight, as GuildPrice.Message says it
        public string Until() => end.TimeOfDay == TimeSpan.Zero
            ? $"until the end of {end.AddDays(-1).ToString("d MMMM", CultureInfo.InvariantCulture)} (UTC)"
            : $"until {end.ToString("d MMMM, HH:mm", CultureInfo.InvariantCulture)} UTC";

        // What it does, for players: "potions drop twice as often, and twice as many"
        public string Effects()
        {
            var parts = new List<string>();
            if (potionChance > 1)
                parts.Add($"potions drop {Times(potionChance)} as often");
            if (potionAmount > 1)
                parts.Add($"{Times(potionAmount)} as many");
            if (relicChance > 1)
                parts.Add($"Necromantic Relics drop {Times(relicChance)} as often from the Elder bosses");
            if (mysteriousOne)
                parts.Add("the Mysterious One opens his Rift every time Oryx the Ascended God falls");
            return string.Join(", and ", parts);
        }

        // The line players get when it starts and while it runs (the Nexus, /boosts)
        public string Message()
        {
            var effects = Effects();
            return effects.Length > 0
                ? $"{name}: {effects}, {Until()}!"
                : $"{name} is on {Until()}!";
        }

        private static string Times(double x) => x == 2 ? "twice" : x == 3 ? "three times" : $"{x.ToString("0.##", CultureInfo.InvariantCulture)} times";
    }

    public static class ServerEvents
    {
        // The stat potions an event's potion effects cover: the eight stats' potions, their
        // soulbound (SB) and Greater ones. Not the Max potions, the candies or anything else that
        // raises a stat.
        public static readonly string[] Stats = { "Life", "Mana", "Attack", "Defense", "Speed", "Dexterity", "Vitality", "Wisdom" };

        private static readonly HashSet<string> StatPotions = new HashSet<string>(
            Stats.SelectMany(s => new[] { $"Potion of {s}", $"Potion of {s} (SB)", $"Greater Potion of {s}" }));

        public static bool IsStatPotion(string itemId) => itemId != null && StatPotions.Contains(itemId);

        public static IEnumerable<ServerEvent> Active(ServerEvent[] events, DateTime utcNow) =>
            events?.Where(e => e != null && e.ActiveAt(utcNow)) ?? Enumerable.Empty<ServerEvent>();

        // What the events running now multiply a stat potion's chance and amount by (both multiply
        // when two overlap). The chance is the drop's own, before the loot boosts, which then
        // multiply it as any drop's (Loot.Chance caps it at a sure drop).
        public static double PotionChance(ServerEvent[] events, DateTime utcNow) =>
            Active(events, utcNow).Aggregate(1.0, (m, e) => m * Math.Max(1, e.potionChance));

        // What the events running now multiply the Necromantic Relic's chance by (both multiply
        // when two overlap), before the loot boosts, as a stat potion's chance
        public static double RelicChance(ServerEvent[] events, DateTime utcNow) =>
            Active(events, utcNow).Aggregate(1.0, (m, e) => m * Math.Max(1, e.relicChance));

        public static int PotionAmount(ServerEvent[] events, DateTime utcNow) =>
            Active(events, utcNow).Aggregate(1, (m, e) => m * Math.Max(1, e.potionAmount));
    }
}
