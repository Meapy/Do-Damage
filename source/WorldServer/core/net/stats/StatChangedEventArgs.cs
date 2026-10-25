using System;

namespace WorldServer.core.net.stats
{
    public sealed class StatChangedEventArgs : EventArgs
    {
        public StatDataType Stat { get; private set; }
        public bool UpdateSelfOnly { get; private set; }
        public object Value { get; private set; }

        /// <summary>An entity moved (X and Y are StatDataType.None): one shared, value-less event.</summary>
        public static readonly StatChangedEventArgs Moved = new StatChangedEventArgs(StatDataType.None, null);

        public StatChangedEventArgs(StatDataType stat, object value, bool updateSelfOnly = false)
        {
            Stat = stat;
            Value = value;
            UpdateSelfOnly = updateSelfOnly;
        }
    }
}
