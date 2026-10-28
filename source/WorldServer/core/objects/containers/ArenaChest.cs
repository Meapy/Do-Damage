using Shared.database.character.inventory;
using Shared.resources;

namespace WorldServer.core.objects.containers
{
    // The Arena's shared chest (DoomedBattleArena puts it down inside the ring): anyone in that
    // Arena can put items in and take them out, and it doesn't disappear while the Arena is open,
    // as a loot bag would. Items only move in and out through InvSwapHandler (the bag and vault
    // code). What's still in it when the Arena closes (everyone has left) is lost with it.
    // Soulbound items and Oryx 5's own gear can't go in (CanHold).
    public sealed class ArenaChest : Container
    {
        public const string ObjectId = "Arena Chest";

        public ArenaChest(GameServer manager, ushort type) : base(manager, type) { }

        protected override bool KeepsWhenEmpty => true;

        public static bool CanHold(Item item, ItemData data) => item == null || !item.Untradeable && !item.Ascended;

        // why CanHold refused an item, for the player
        public const string Refusal = "Soulbound items can't go in the Arena Chest.";
    }
}
