using System.Globalization;

namespace Shared
{
    // How long a player has stood still in the Nexus (serverSettings.nexusAfkMinutes): only
    // moving counts as being there, so chatting, trading or browsing the Marketplace while
    // standing still don't. One per player in the Nexus, so leaving it starts afresh.
    public sealed class NexusAfk
    {
        private long _stillMs;
        private bool _sent;

        public long StillMs => _stillMs;

        // The limit in ms, or 0 when it's off (0 or less in the config).
        public static long LimitMs(ServerSettings settings) =>
            settings == null || settings.nexusAfkMinutes <= 0 ? 0 : (long)(settings.nexusAfkMinutes * 60000);

        public void Moved() => _stillMs = 0;

        // Counts a tick of standing still; true once, the tick the limit is passed.
        public bool Tick(int elapsedMs, long limitMs)
        {
            if (limitMs <= 0 || _sent)
                return false;
            _stillMs += elapsedMs;
            if (_stillMs < limitMs)
                return false;
            _sent = true;
            return true;
        }

        public static string Message(ServerSettings settings)
        {
            var minutes = settings.nexusAfkMinutes;
            var length = minutes == 1 ? "a minute" : minutes == (int)minutes ? $"{(int)minutes} minutes" : minutes.ToString("0.#", CultureInfo.InvariantCulture) + " minutes";
            return $"You were away for {length} and have been returned to the character screen.";
        }
    }
}
