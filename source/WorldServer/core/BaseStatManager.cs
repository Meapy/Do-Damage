using Shared;
using WorldServer.core.net.stats;

namespace WorldServer.core
{
    internal class BaseStatManager
    {
        private int[] _base;
        private StatsManager _parent;

        public BaseStatManager(StatsManager parent)
        {
            _parent = parent;
            _base = Utils.ResizeArray(parent.Owner.Client.Character.Stats, StatsManager.NumStatTypes);

            // a stat above its class's highest (the class's maximum was lowered since: the Knight's
            // health and defense) comes down to it, as levelling up would bring it
            if (parent.Owner.GameServer.Resources.GameData.Classes.TryGetValue(parent.Owner.ObjectType, out var desc))
                for (var i = 0; i < desc.Stats.Length && i < _base.Length; i++)
                    if (_base[i] > desc.Stats[i].MaxValue)
                        _base[i] = desc.Stats[i].MaxValue;

            ReCalculateValues();
        }

        public int this[int index]
        {
            get => _base[index];
            set
            {
                _base[index] = value;
                _parent.StatChanged(index);
            }
        }

        public int[] GetStats() => (int[])_base.Clone();

        protected internal void ReCalculateValues()
        { }
    }
}
