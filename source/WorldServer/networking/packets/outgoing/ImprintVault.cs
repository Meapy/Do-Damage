using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // The Enthroned gear in the account's vault chests, for the Enchanter's window (ImprintHandlers.cs):
    // each item's chest, slot, type and imprints (Offensive, Defensive, Misc). Empty while the
    // player is in their vault. An older client ignores it.
    public class ImprintVault : OutgoingMessage
    {
        public (ushort Chest, byte Slot, ushort Item, int[] Imprints)[] Items { get; set; }

        public override MessageId MessageId => MessageId.IMPRINT_VAULT;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((short)Items.Length);
            foreach (var (chest, slot, item, imprints) in Items)
            {
                wtr.Write(chest);
                wtr.Write(slot);
                wtr.Write(item);
                for (var i = 0; i < 3; i++)
                    wtr.Write((byte)(imprints != null && i < imprints.Length ? imprints[i] : 0));
            }
        }
    }
}
