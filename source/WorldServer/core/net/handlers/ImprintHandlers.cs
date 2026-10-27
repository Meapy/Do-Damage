using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.database.character.inventory;
using Shared.resources;
using WorldServer.core.commands.doomed;
using WorldServer.core.objects;
using WorldServer.core.objects.inventory;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    // The Enchanter's window (the Rust client), beside the Divine Forge in the Nexus: it imprints
    // Enthroned gear (Shared/resources/Imprints.cs), in the inventory or in the account's vault
    // chests (ForgeVault, while its owner isn't in the vault), for an Ascended shard from the forge
    // storage, or from the inventory when the storage has none. The window asks for the storage's
    // shards and the vault's Enthroned gear when it opens. While imprints are tested, only admins
    // (rank 6, MinRank) can imprint; anyone can look.
    public class ImprintRequestHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.IMPRINT_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            if (client.Player == null)
                return;
            ForgeStorage.Send(client.Player);
            ImprintHandler.SendVault(client.Player);
        }
    }

    public class ImprintHandler : IMessageHandler
    {
        public const string EnchanterClass = "Enchanter";
        public const int MinRank = (int)RankingType.Admin;
        // ImprintResult's slot for an item in the vault
        public const byte VaultSlot = 255;

        public override MessageId MessageId => MessageId.IMPRINT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var slot = rdr.ReadByte();
            var id = rdr.ReadByte();
            var player = client.Player;
            if (player == null)
                return;
            var error = Allowed(player) ?? ApplyInventory(player, slot, id);
            client.SendPacket(new ImprintResult() { Success = error == null, Slot = slot, Message = error });
        }

        // Why this player can't imprint now, or null.
        public static string Allowed(Player player)
        {
            if (player.Client.Account.Rank < MinRank)
                return "Imprints are being tested: only admins can imprint gear for now.";
            if (!DoomedEconomy.NearObject(player, EnchanterClass))
                return "Stand by the Enchanter to imprint gear.";
            return null;
        }

        // The gear a class wears (its first four slot types: weapon, ability, armour, ring).
        public static HashSet<int> GearSlots(GameServer server) =>
            new HashSet<int>(server.Resources.GameData.Classes.Values.Where(c => c.SlotTypes != null)
                .SelectMany(c => c.SlotTypes.Take(4)).Where(t => t != 0));

        // Whether an item takes imprints: Oryx 5's own gear, the Enthroned items, worn as gear.
        public static bool Imprintable(GameServer server, Item item) =>
            item != null && item.Ascended && !item.Consumable && GearSlots(server).Contains(item.SlotType);

        // Imprints the item in this inventory slot. Returns what went wrong, or null.
        private static string ApplyInventory(Player player, byte slot, byte id)
        {
            var end = player.HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
            if (slot >= end || player.Inventory[slot] == null)
                return "That slot holds nothing to imprint.";
            var item = player.Inventory[slot];
            var error = Check(player, item, player.Inventory.Data[slot], id, out var imprint);
            if (error != null)
                return error;
            if ((error = Pay(player, imprint.Kind, slot)) != null)
                return error;
            player.Inventory.Data[slot] = WithImprint(item, player.Inventory.Data[slot], imprint);
            player.Stats.ReCalculateValues();
            player.SendInfo($"{item.DisplayName} now carries {imprint.Name}.");
            return null;
        }

        // Imprints the item in a vault chest's slot. Returns what went wrong, or null.
        public static string ApplyVault(Player player, int chest, int slot, byte id)
        {
            var vault = ForgeVault.Read(player);
            if (vault == null)
                return "Your vault can't be reached from here while you're in it.";
            var (type, data) = vault.At(chest, slot);
            if (!player.GameServer.Resources.GameData.Items.TryGetValue(type, out var item))
                return "That vault slot holds nothing to imprint.";
            var error = Check(player, item, data, id, out var imprint);
            if (error != null)
                return error;
            // the vault first (nothing taken if it changed meanwhile), then the shard
            if (!Afford(player, imprint.Kind))
                return CostText(imprint.Kind);
            vault.SetData(chest, slot, WithImprint(item, data, imprint));
            if (!vault.Save())
                return "Your vault changed meanwhile: try again.";
            Pay(player, imprint.Kind, -1);
            player.SendInfo($"{item.DisplayName} in your vault now carries {imprint.Name}.");
            return null;
        }

        private static string Check(Player player, Item item, ItemData data, byte id, out Imprint imprint)
        {
            imprint = Imprint.Get(id);
            if (!Imprintable(player.GameServer, item))
                return $"Only Enthroned gear takes imprints: {item.DisplayName} can't.";
            if (imprint == null)
                return "That imprint isn't known here.";
            if (!imprint.Fits(item))
                return $"{imprint.Name} doesn't fit {item.DisplayName}.";
            if (data?.Imprint(imprint.Kind) == imprint.Id)
                return $"{item.DisplayName} already carries {imprint.Name}.";
            return null;
        }

        private static string CostText(ImprintKind kind) =>
            $"It costs an {string.Join(" or an ", Imprint.Cost(kind))}, in your forge storage or inventory.";

        // The shard in the forge storage, or the inventory slot holding one (not `skip`): (stored id, held slot).
        private static (string Stored, int Held) FindShard(Player player, ImprintKind kind, int skip)
        {
            var cost = Imprint.Cost(kind);
            var counts = ForgeStorage.Counts(player);
            var stored = cost.FirstOrDefault(s => ForgeStorage.Index(s) >= 0 && counts[ForgeStorage.Index(s)] > 0);
            if (stored != null)
                return (stored, -1);
            var end = player.HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
            for (var i = 4; i < end; i++)
                if (i != skip && player.Inventory[i] != null && cost.Contains(player.Inventory[i].ObjectId))
                    return (null, i);
            return (null, -1);
        }

        private static bool Afford(Player player, ImprintKind kind)
        {
            var (stored, held) = FindShard(player, kind, -1);
            return stored != null || held >= 0;
        }

        // Takes the shard: from the forge storage, else the inventory. Returns what went wrong, or null.
        private static string Pay(Player player, ImprintKind kind, int skip)
        {
            var (stored, held) = FindShard(player, kind, skip);
            if (stored != null)
                ForgeStorage.Take(player, new Dictionary<string, int> { [stored] = 1 });
            else if (held >= 0)
            {
                player.Inventory[held] = null;
                player.Inventory.Data[held] = null;
            }
            else
                return CostText(kind);
            return null;
        }

        private static ItemData WithImprint(Item item, ItemData data, Imprint imprint)
        {
            var imprints = new int[3];
            if (data?.Imprints != null)
                for (var i = 0; i < imprints.Length && i < data.Imprints.Length; i++)
                    imprints[i] = data.Imprints[i];
            imprints[(int)imprint.Kind] = imprint.Id;
            return new ItemData()
            {
                ObjectId = data?.ObjectId ?? item.ObjectId,
                Stack = data?.Stack ?? 0,
                MaxStack = data?.MaxStack ?? 0,
                Imprints = imprints
            };
        }

        // The vault's Enthroned gear, to the window (IMPRINT_VAULT): nothing while its owner is in it.
        public static void SendVault(Player player)
        {
            var vault = ForgeVault.Read(player);
            var items = player.GameServer.Resources.GameData.Items;
            var gear = vault?.All()
                .Where(g => items.TryGetValue(g.Type, out var item) && Imprintable(player.GameServer, item))
                .Select(g => ((ushort)g.Chest, (byte)g.Slot, g.Type, g.Data?.Imprints ?? new int[3]))
                .ToArray() ?? System.Array.Empty<(ushort, byte, ushort, int[])>();
            player.Client.SendPacket(new ImprintVault() { Items = gear });
        }
    }

    public class ImprintVaultHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.IMPRINT_VAULT_APPLY;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var chest = rdr.ReadUInt16();
            var slot = rdr.ReadByte();
            var id = rdr.ReadByte();
            var player = client.Player;
            if (player == null)
                return;
            var error = ImprintHandler.Allowed(player) ?? ImprintHandler.ApplyVault(player, chest, slot, id);
            client.SendPacket(new ImprintResult() { Success = error == null, Slot = ImprintHandler.VaultSlot, Message = error });
            ImprintHandler.SendVault(player);
        }
    }
}
