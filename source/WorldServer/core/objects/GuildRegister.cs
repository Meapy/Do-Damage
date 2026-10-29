using System;
using System.Collections.Generic;
using Shared.database.guild;
using WorldServer.core.net.stats;
using WorldServer.core.worlds;

namespace WorldServer.core.objects
{
    // The Guild Register in the Nexus: it sends what making a guild costs (MerchandisePrice, in
    // fame), so the client shows "free" during the free guilds event (GuildPrice) and the normal
    // price once it ends. Older clients ignore the stat on it.
    public sealed class GuildRegister : StaticObject
    {
        private readonly StatTypeValue<int> _price;
        private readonly DateTime? _freeUntil;

        public GuildRegister(GameServer manager, ushort objType) : base(manager, objType, null, true, false, false)
        {
            _freeUntil = GuildPrice.FreeUntil(manager.Configuration.serverSettings);
            _price = new StatTypeValue<int>(this, StatDataType.MerchandisePrice, GuildPrice.At(_freeUntil, DateTime.UtcNow));
        }

        public override void Tick(ref TickTime time)
        {
            if (_freeUntil.HasValue)
                _price.SetValue(GuildPrice.At(_freeUntil, DateTime.UtcNow));
            base.Tick(ref time);
        }

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            stats[StatDataType.MerchandisePrice] = _price.GetValue();
            base.ExportStats(stats, isOtherPlayer);
        }
    }
}
