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
    // The vault window (the Rust client): every item in the account's vault chests, from anywhere,
    // so players needn't walk from chest to chest to find one. Read-only: nothing here moves an
    // item. In their own vault the chests' live contents are sent, with where each chest stands
    // so the window can point at it; elsewhere what Redis holds (ForgeVault.Peek), which the vault
    // keeps current by saving a chest on every change.
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
            var chests = Enumerable.Repeat((-1f, -1f), count).ToArray();
            var items = new List<(ushort, byte, ushort, int[])>();
            var live = player.World is VaultWorld v && v.AccountId == player.AccountId ? v.VaultChests : new List<Container>();
            for (var c = 0; c < live.Count && c < count; c++)
            {
                chests[c] = (live[c].X, live[c].Y);
                var inv = live[c].Inventory;
                for (var s = 0; s < inv.Length && s < 8; s++)
                    if (inv[s] != null)
                        items.Add(((ushort)c, (byte)s, inv[s].ObjectType, s < inv.Data.Length ? inv.Data[s]?.Imprints : null));
            }
            if (vault != null)
                items.AddRange(vault.All().Where(i => i.Chest >= live.Count).Select(i => ((ushort)i.Chest, (byte)i.Slot, i.Type, i.Data?.Imprints)));
            client.SendPacket(new VaultContents() { Chests = chests, Items = items.ToArray() });
        }
    }
}
