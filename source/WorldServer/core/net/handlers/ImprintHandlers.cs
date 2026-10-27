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
    // The Enchanter's window (the Rust client), beside the Divine Forge in the Nexus: it imprints gear
    // (Shared/resources/Imprints.cs) for an Ascended shard from the forge storage, or from the
    // inventory when the storage has none. The window asks for the storage's shards when it opens.
    // While imprints are tested, only admins (rank 6, MinRank) can imprint; anyone can look.
    public class ImprintRequestHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.IMPRINT_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            if (client.Player != null)
                ForgeStorage.Send(client.Player);
        }
    }

    public class ImprintHandler : IMessageHandler
    {
        public const string EnchanterClass = "Enchanter";
        public const int MinRank = (int)RankingType.Admin;

        public override MessageId MessageId => MessageId.IMPRINT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var slot = rdr.ReadByte();
            var id = rdr.ReadByte();
            var player = client.Player;
            if (player == null)
                return;
            string error;
            if (client.Account.Rank < MinRank)
                error = "Imprints are being tested: only admins can imprint gear for now.";
            else if (!DoomedEconomy.NearObject(player, EnchanterClass))
                error = "Stand by the Enchanter to imprint gear.";
            else
                error = Apply(player, slot, id);
            client.SendPacket(new ImprintResult() { Success = error == null, Slot = slot, Message = error });
        }

        // The gear a class wears (its first four slot types: weapon, ability, armour, ring).
        public static HashSet<int> GearSlots(GameServer server) =>
            new HashSet<int>(server.Resources.GameData.Classes.Values.Where(c => c.SlotTypes != null)
                .SelectMany(c => c.SlotTypes.Take(4)).Where(t => t != 0));

        // Imprints the item in this inventory slot. Returns what went wrong, or null.
        private static string Apply(Player player, byte slot, byte id)
        {
            var end = player.HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
            if (slot >= end)
                return "That slot holds nothing to imprint.";
            var item = player.Inventory[slot];
            if (item == null)
                return "That slot holds nothing to imprint.";
            if (!GearSlots(player.GameServer).Contains(item.SlotType) || item.Consumable)
                return $"{item.DisplayName} can't take imprints.";
            var imprint = Imprint.Get(id);
            if (imprint == null)
                return "That imprint isn't known here.";
            if (!imprint.Fits(item))
                return $"{imprint.Name} doesn't fit {item.DisplayName}.";
            var data = player.Inventory.Data[slot];
            if (data?.Imprint(imprint.Kind) == imprint.Id)
                return $"{item.DisplayName} already carries {imprint.Name}.";

            // the shard: from the forge storage, else the inventory
            var cost = Imprint.Cost(imprint.Kind);
            var counts = ForgeStorage.Counts(player);
            var stored = cost.FirstOrDefault(s => ForgeStorage.Index(s) >= 0 && counts[ForgeStorage.Index(s)] > 0);
            var held = -1;
            if (stored == null)
            {
                for (var i = 4; i < end && held < 0; i++)
                    if (i != slot && player.Inventory[i] != null && cost.Contains(player.Inventory[i].ObjectId))
                        held = i;
                if (held < 0)
                    return $"It costs an {string.Join(" or an ", cost)}, in your forge storage or inventory.";
            }
            if (stored != null)
                ForgeStorage.Take(player, new Dictionary<string, int> { [stored] = 1 });
            else
            {
                player.Inventory[held] = null;
                player.Inventory.Data[held] = null;
            }

            var imprints = new int[3];
            if (data?.Imprints != null)
                for (var i = 0; i < imprints.Length && i < data.Imprints.Length; i++)
                    imprints[i] = data.Imprints[i];
            imprints[(int)imprint.Kind] = imprint.Id;
            player.Inventory.Data[slot] = new ItemData()
            {
                ObjectId = data?.ObjectId ?? item.ObjectId,
                Stack = data?.Stack ?? 0,
                MaxStack = data?.MaxStack ?? 0,
                Imprints = imprints
            };
            player.Stats.ReCalculateValues();
            player.SendInfo($"{item.DisplayName} now carries {imprint.Name}.");
            return null;
        }
    }
}
