using System.Collections.Generic;
using System.Linq;
using Shared;
using WorldServer.core.commands.doomed;
using WorldServer.core.objects.containers;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    // The vault window (the Rust client): every item in the account's vault chests and gift
    // chests, so players needn't walk from chest to chest to find one. Nothing here moves an item. In their own vault
    // the chests' live contents are sent, with each chest's object id and where it stands: the
    // window moves items with ordinary InvSwaps, which InvSwapHandler allows from anywhere in the
    // vault for its owner's own chests (OwnVaultSwap). Elsewhere what Redis holds
    // (ForgeVault.Peek), which the vault keeps current by saving a chest on every change.
    public class VaultContentsHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.VAULT_CONTENTS_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var player = client.Player;
            if (player == null)
                return;
            var vault = ForgeVault.Peek(player);
            var count = vault?.ChestCount ?? 0;
            var chests = Enumerable.Repeat((-1, -1f, -1f, false), count).ToList();
            var items = new List<(ushort, byte, ushort, int[])>();
            var live = player.World is VaultWorld v && v.AccountId == player.AccountId ? v.VaultChests : new List<Container>();
            for (var c = 0; c < live.Count && c < count; c++)
            {
                chests[c] = (live[c].Id, live[c].X, live[c].Y, false);
                var inv = live[c].Inventory;
                for (var s = 0; s < inv.Length && s < 8; s++)
                    if (inv[s] != null)
                        items.Add(((ushort)c, (byte)s, inv[s].ObjectType, s < inv.Data.Length ? inv.Data[s]?.Imprints : null));
            }
            if (vault != null)
                items.AddRange(vault.All().Where(i => i.Chest >= live.Count).Select(i => ((ushort)i.Chest, (byte)i.Slot, i.Type, i.Data?.Imprints)));

            // The gift chests after them: in the vault the ones still holding something (you can
            // only take from them, InvSwapHandler.ValidateEntities), elsewhere the account's gifts
            // eight to a chest, as the vault lays them out.
            if (player.World is VaultWorld own && own.AccountId == player.AccountId)
                foreach (var g in own.GiftChests.Where(g => g.World == own && g.Inventory.Any(i => i != null)))
                {
                    var c = chests.Count;
                    chests.Add((g.Id, g.X, g.Y, true));
                    for (var s = 0; s < g.Inventory.Length && s < 8; s++)
                        if (g.Inventory[s] != null)
                            items.Add(((ushort)c, (byte)s, g.Inventory[s].ObjectType, null));
                }
            else
            {
                player.Client.Account.Reload("gifts");
                var gifts = player.Client.Account.Gifts;
                for (var i = 0; i < gifts.Length; i++)
                {
                    if (i % 8 == 0)
                        chests.Add((-1, -1f, -1f, true));
                    items.Add(((ushort)(chests.Count - 1), (byte)(i % 8), gifts[i], null));
                }
            }
            client.SendPacket(new VaultContents() { Chests = chests.ToArray(), Items = items.ToArray() });
        }
    }
}
