using System;
using System.Globalization;

namespace Shared.database.guild
{
    // What making a guild costs at the Guild Register: 1,000 fame, or nothing during the free
    // guilds event (serverSettings.freeGuildsUntil, the moment it ends in UTC, such as
    // "2026-12-01T00:00:00Z"). The normal price comes back by itself at that moment.
    public static class GuildPrice
    {
        public const int Normal = 1000;

        // When the event ends, or null when there's none (empty, or not a date).
        public static DateTime? FreeUntil(ServerSettings settings)
        {
            var text = settings?.freeGuildsUntil;
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var end)
                ? end
                : null;
        }

        public static bool Free(DateTime? until, DateTime utcNow) => until.HasValue && utcNow < until.Value;

        public static int At(DateTime? until, DateTime utcNow) => Free(until, utcNow) ? 0 : Normal;

        // The Nexus's line while it runs: "until the end of 30 November" for an end at midnight.
        public static string Message(DateTime until)
        {
            var when = until.TimeOfDay == TimeSpan.Zero
                ? $"until the end of {until.AddDays(-1).ToString("d MMMM", CultureInfo.InvariantCulture)} (UTC)"
                : $"until {until.ToString("d MMMM, HH:mm", CultureInfo.InvariantCulture)} UTC";
            return $"Guild event: making a guild is free {when}. Visit the Guild Register in the Nexus!";
        }
    }
}
