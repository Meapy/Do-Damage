using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // What the account's vault chests hold that the forge can take, by item type and how many
    // (ForgeVault.cs): after FORGE_DATA_REQUEST and after each forging. Empty while the player is in
    // their vault. An older client ignores it.
    public class ForgeVaultItems : OutgoingMessage
    {
        public (ushort Item, int Count)[] Items { get; set; }

        public override MessageId MessageId => MessageId.FORGE_VAULT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((short)Items.Length);
            foreach (var (item, count) in Items)
            {
                wtr.Write(item);
                wtr.Write(count);
            }
        }
    }
}
