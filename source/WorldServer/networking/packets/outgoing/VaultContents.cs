using Shared;

namespace WorldServer.networking.packets.outgoing
{
    // Everything in the account's vault chests, for the vault window (VaultContentsHandler.cs):
    // where each chest stands (x, y; -1, -1 unless the player is in their vault), then each item's
    // chest, slot, type and imprints (Offensive, Defensive, Misc). An older client never asks.
    public class VaultContents : OutgoingMessage
    {
        public (float X, float Y)[] Chests { get; set; }
        public (ushort Chest, byte Slot, ushort Item, int[] Imprints)[] Items { get; set; }

        public override MessageId MessageId => MessageId.VAULT_CONTENTS;

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write((short)Chests.Length);
            foreach (var (x, y) in Chests)
            {
                wtr.Write(x);
                wtr.Write(y);
            }
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
