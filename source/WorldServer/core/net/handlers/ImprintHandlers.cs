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
    // chests (ForgeVault, while its owner isn't in the vault), for Ascended shards from the forge
    // storage, then from the inventory (Imprint.Cost of them: 1 for Offensive and Defensive, 3 for
    // Misc, 5 for Apotheosis, 20 for Guardian Angel). The window asks for the storage's shards and
    // the vault's Enthroned gear when it opens. Anyone can imprint (only admins could while imprints
    // were tested). The Apotheosis imprints go on gear in the inventory only (the vault's list
    // carries three colours), the Apotheosis of Balance with the stats picked for it, three bytes
    // after the imprint (an older client sends none, and can't pick it).
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
        // ImprintResult's slot for an item in the vault
        public const byte VaultSlot = 255;

        public override MessageId MessageId => MessageId.IMPRINT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var slot = rdr.ReadByte();
            var id = rdr.ReadByte();
            // the stats picked for the Apotheosis of Balance, when the client sends them
            int raised = -1, low1 = -1, low2 = -1;
            if (rdr.BaseStream.Length - rdr.BaseStream.Position >= 3)
            {
                raised = rdr.ReadByte();
                low1 = rdr.ReadByte();
                low2 = rdr.ReadByte();
            }
            var player = client.Player;
            if (player == null)
                return;
            var error = Allowed(player) ?? ApplyInventory(player, slot, id, raised, low1, low2);
            client.SendPacket(new ImprintResult() { Success = error == null, Slot = slot, Message = error });
        }

        // Why this player can't imprint now, or null.
        public static string Allowed(Player player)
        {
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
        private static string ApplyInventory(Player player, byte slot, byte id, int raised, int low1, int low2)
        {
            var end = player.HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
            if (slot >= end || player.Inventory[slot] == null)
                return "That slot holds nothing to imprint.";
            var item = player.Inventory[slot];
            var error = Check(player, item, player.Inventory.Data[slot], id, out var imprint);
            if (error != null)
                return error;
            var value = (int)imprint.Id;
            if (imprint.Picks)
            {
                if (!Imprint.ValidChoice(raised, low1, low2))
                    return $"Pick a stat to raise and two others to lower for {imprint.Name}.";
                value = Imprint.WithChoice(imprint.Id, raised, low1, low2);
                if (player.Inventory.Data[slot]?.Imprint(imprint.Kind) == value)
                    return $"{item.DisplayName} already carries {imprint.Name} with those stats.";
            }
            if ((error = Pay(player, imprint, slot)) != null)
                return error;
            player.Inventory.Data[slot] = WithImprint(item, player.Inventory.Data[slot], imprint, value);
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
            if (imprint.Kind == ImprintKind.Apotheosis)
                return $"Take {item.DisplayName} out of your vault first: Apotheosis imprints go on gear in your inventory.";
            // the vault first (nothing taken if it changed meanwhile), then the shards
            if (!Afford(player, imprint, -1))
                return CostText(imprint);
            vault.SetData(chest, slot, WithImprint(item, data, imprint, imprint.Id));
            if (!vault.Save())
                return "Your vault changed meanwhile: try again.";
            Pay(player, imprint, -1);
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
            if (((data?.Imprint(imprint.Kind) ?? 0) & 0xFF) == imprint.Id && !imprint.Picks)
                return $"{item.DisplayName} already carries {imprint.Name}.";
            // Guardian Angel takes the ring's other imprints, and it takes no more while it's on
            if (imprint.Kind != ImprintKind.Misc && ((data?.Imprint(ImprintKind.Misc) ?? 0) & 0xFF) == Imprint.GuardianAngel)
                return $"{item.DisplayName} carries Guardian Angel, which takes no other imprints.";
            return null;
        }

        private static string CostText(Imprint imprint) =>
            imprint.Cost == 1
                ? $"It costs an {string.Join(" or an ", Imprint.Shards(imprint.Kind))}, in your forge storage or inventory."
                : $"It costs {imprint.Cost} shards ({string.Join(", ", Imprint.Shards(imprint.Kind))}), in your forge storage or inventory.";

        // The shards that would pay for an imprint: from the forge storage first, then inventory
        // slots (not `skip`); null if there aren't enough.
        private static (Dictionary<string, int> Stored, List<int> Held)? FindShards(Player player, Imprint imprint, int skip)
        {
            var cost = Imprint.Shards(imprint.Kind);
            var left = imprint.Cost;
            var stored = new Dictionary<string, int>();
            var counts = ForgeStorage.Counts(player);
            foreach (var s in cost)
            {
                var i = ForgeStorage.Index(s);
                if (i < 0 || left == 0)
                    continue;
                var n = System.Math.Min(counts[i], left);
                if (n > 0)
                {
                    stored[s] = n;
                    left -= n;
                }
            }
            var held = new List<int>();
            var end = player.HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
            for (var i = 4; i < end && left > 0; i++)
                if (i != skip && player.Inventory[i] != null && cost.Contains(player.Inventory[i].ObjectId))
                {
                    held.Add(i);
                    left--;
                }
            return left > 0 ? null : (stored, held);
        }

        private static bool Afford(Player player, Imprint imprint, int skip) => FindShards(player, imprint, skip) != null;

        // Takes the shards: from the forge storage, then the inventory. Returns what went wrong, or null.
        private static string Pay(Player player, Imprint imprint, int skip)
        {
            var found = FindShards(player, imprint, skip);
            if (found == null)
                return CostText(imprint);
            var (stored, held) = found.Value;
            ForgeStorage.Take(player, stored);
            foreach (var i in held)
            {
                player.Inventory[i] = null;
                player.Inventory.Data[i] = null;
            }
            return null;
        }

        // The item's data with the imprint on (its value: the id, with any stats picked). Guardian
        // Angel takes the other imprints off.
        private static ItemData WithImprint(Item item, ItemData data, Imprint imprint, int value)
        {
            var imprints = new int[4];
            if (data?.Imprints != null && imprint.Id != Imprint.GuardianAngel)
                for (var i = 0; i < imprints.Length && i < data.Imprints.Length; i++)
                    imprints[i] = data.Imprints[i];
            imprints[(int)imprint.Kind] = value;
            return new ItemData()
            {
                ObjectId = data?.ObjectId ?? item.ObjectId,
                Stack = data?.Stack ?? 0,
                MaxStack = data?.MaxStack ?? 0,
                Imprints = imprints,
                // what else the item carries stays on it (a pet's upgrade, its corruption)
                PetUpgrade = data?.PetUpgrade,
                Corruption = data?.Corruption,
                Unique = data?.Unique
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
