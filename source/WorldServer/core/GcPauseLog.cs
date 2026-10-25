using System;
using System.Diagnostics.Tracing;
using WorldServer.utils;

namespace WorldServer.core
{
    /// <summary>
    /// Logs each garbage collection pause over 50 ms ("GC pause") with what the collection was:
    /// its generation, kind and reason. A pause stops every world, so these are lag spikes that
    /// Tick stats only sum up.
    /// </summary>
    public sealed class GcPauseLog : EventListener
    {
        private const double LogOverMs = 50;
        private static readonly string[] Kinds = { "blocking", "background", "foreground" };
        private static readonly string[] Reasons =
        {
            "small allocations", "induced", "low memory", "empty", "large allocations", "out of space (small)",
            "out of space (large)", "induced, not forced", "stress", "low memory (induced)", "induced, compacting",
            "low memory (host)", "PM full", "PM low memory", "PM full (induced)", "PM full (low memory)",
            "under the heap's hard limit", "BGC tuning (small)", "BGC tuning (large)", "service flush"
        };

        private DateTime _suspended;
        private string _what = "";

        private static GcPauseLog _instance;

        public static void Start() => _instance ??= new GcPauseLog();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Informational, (EventKeywords)0x1); // GC
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            switch (e.EventId)
            {
                case 9: // GCSuspendEEBegin: the pause starts
                    _suspended = e.TimeStamp;
                    _what = "";
                    break;
                case 1: // GCStart: what this collection is (one pause can hold more than one)
                    var gen = Convert.ToInt32(e.Payload[1]);
                    var reason = Convert.ToInt32(e.Payload[2]);
                    var kind = Convert.ToInt32(e.Payload[3]);
                    _what += (_what.Length > 0 ? ", " : "") + $"gen {gen} {Name(Kinds, kind)} ({Name(Reasons, reason)})";
                    break;
                case 3: // GCRestartEEEnd: the pause ends
                    if (_suspended == default)
                        break;
                    var ms = (e.TimeStamp - _suspended).TotalMilliseconds;
                    _suspended = default;
                    if (ms >= LogOverMs)
                        StaticLogger.Instance.Warn($"GC pause {ms:0} ms: {(_what.Length > 0 ? _what : "the end of a background collection")}, heap {GC.GetTotalMemory(false) >> 20} MB");
                    break;
            }
        }

        private static string Name(string[] names, int i) => i >= 0 && i < names.Length ? names[i] : i.ToString();
    }
}
