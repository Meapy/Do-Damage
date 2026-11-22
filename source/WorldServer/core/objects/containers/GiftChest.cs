using System.Linq;
using Shared;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;

namespace WorldServer.core.objects.containers
{
    internal class GiftChest : Container
    {
        public GiftChest(GameServer manager, ushort id) : base(manager, id)
        { }

        public override void Tick(ref TickTime time)
        {
            // emptied in the vault, it takes the next gifts waiting for room rather than closing
            if (World is VaultWorld vault && Inventory != null && Inventory.All(i => i == null))
                vault.Refill(this);
            base.Tick(ref time);
        }
    }
}
