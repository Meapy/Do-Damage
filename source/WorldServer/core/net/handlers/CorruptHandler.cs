using System;
using System.Linq;
using Shared;
using Shared.database.character.inventory;
using Shared.resources;
using WorldServer.core.commands.doomed;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    // The Cosmic Anvil (the Rust client's Divine Workbench, opened at the anvil the Mysterious One
    // leaves in the Black Between): an Unstable Essence from the inventory corrupts a piece of
    // Enthroned gear in the inventory (ItemData.Corruption, Shared/resources/Corruption.cs). The
    // answer carries how much each value moved and whether a staff's shots are reversed.
    public class CorruptHandler : IMessageHandler
    {
        public const string AnvilClass = "CosmicAnvil";
        public const string Essence = "Unstable Essence";

        public override MessageId MessageId => MessageId.CORRUPT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var slot = rdr.ReadByte();
            var player = client.Player;
            if (player == null)
                return;
            var result = new CorruptResult { Slot = slot };
            result.Message = Corrupt(player, slot, result);
            result.Success = result.Message == null;
            client.SendPacket(result);
        }

        private static string Corrupt(Player player, int slot, CorruptResult result)
        {
            if (!DoomedEconomy.NearObject(player, AnvilClass))
                return "Stand by the Cosmic Anvil to corrupt gear.";
            var inv = player.Inventory;
            var max = player.HasBackpack ? 20 : 12;
            if (slot < 0 || slot >= max)
                return "That slot holds nothing.";
            var item = inv[slot];
            if (!ImprintHandler.Imprintable(player.GameServer, item))
                return "Only Enthroned gear can be corrupted.";
            var essence = Enumerable.Range(4, max - 4).FirstOrDefault(i => inv[i]?.ObjectId == Essence, -1);
            if (essence < 0)
                return "You need an Unstable Essence.";

            var data = inv.Data[slot] ?? new ItemData();
            var before = Corruption.Of(data.Corruption);
            var after = Corruption.Roll(before, Random.Shared);
            data.Corruption = after.Any(v => v != 0) ? after : null;
            // a staff's shots may turn round: a tenth of the time, each corruption
            if (item.SlotType == Imprint.StaffSlot)
                data.Unique = Random.Shared.NextDouble() < Corruption.ReversedChance ? Corruption.Reversed : null;
            inv[essence] = null;
            inv.Data[essence] = null;
            // (setting the data sends it; the stats work it out again)
            inv.Data[slot] = data;
            player.Stats.ReCalculateValues();
            for (var k = 0; k < 4; k++)
                result.Changes[k] = (sbyte)(after[k] - before[k]);
            result.Reversed = data.Unique == Corruption.Reversed;
            return null;
        }
    }
}
