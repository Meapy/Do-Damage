using System.Linq;
using Shared;
using WorldServer.core.commands.doomed;
using WorldServer.core.worlds;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.net.handlers
{
    // The Divine Forge's window (the Rust client): its recipes and prices, forging and selling.
    // The work is DoomedEconomy's; forging and selling need the player beside a forge. Forging also
    // takes ingredients from the account's vault (ForgeVault), whose contents the window gets too.
    public class ForgeDataRequestHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.FORGE_DATA_REQUEST;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            if (client.Player == null)
                return;
            var data = client.GameServer.Resources.GameData;
            var types = data.IdToObjectType;
            // recipes whose result or ingredients aren't items here are left out
            var recipes = ForgeRecipes.All(client.GameServer)
                .Where(r => types.ContainsKey(r.Result) && r.Items.All(types.ContainsKey))
                .Select(r => (types[r.Result], r.Fame, r.Advanced, r.Items.Select(i => types[i]).ToArray()))
                .ToArray();
            var prices = DoomedEconomyData.SellPrices
                .Where(p => types.ContainsKey(p.Item))
                .Select(p => (types[p.Item], p.Fame))
                .ToArray();
            client.SendPacket(new ForgeData() { Recipes = recipes, Prices = prices });
            ForgeStorage.Send(client.Player);
            ForgeVault.Send(client.Player);
        }
    }

    public class ForgeWithdrawHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.FORGE_WITHDRAW;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var item = rdr.ReadUInt16();
            var player = client.Player;
            if (player == null)
                return;
            var error = DoomedEconomy.NearForge(player) ? ForgeStorage.Withdraw(player, item) : "Stand by the Divine Forge to take shards out.";
            if (error != null)
                player.SendError(error);
        }
    }

    public class ForgeHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.FORGE;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var result = rdr.ReadUInt16();
            var slots = rdr.ReadBytes(rdr.ReadByte());
            var player = client.Player;
            if (player == null)
                return;
            var data = client.GameServer.Resources.GameData;
            string error;
            var fame = 0;
            if (!DoomedEconomy.NearForge(player))
                error = "Stand by the Divine Forge to forge.";
            else if (!data.ObjectTypeToId.TryGetValue(result, out var resultId))
                error = "That item doesn't exist here.";
            else
                error = DoomedEconomy.Forge(player, slots, resultId, out fame);
            if (error == null)
                player.Quests?.Event(new Shared.quests.QuestEvent(Shared.quests.QuestEvents.Forge));
            client.SendPacket(new ForgeResult() { Success = error == null, Item = result, Fame = fame, Message = error });
        }
    }

    public class ForgeSellHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.FORGE_SELL;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime time)
        {
            var slots = rdr.ReadBytes(rdr.ReadByte());
            var player = client.Player;
            if (player == null)
                return;
            var fame = 0;
            var error = DoomedEconomy.NearForge(player) ? DoomedEconomy.Sell(player, slots, out fame) : "Stand by the Divine Forge to sell.";
            client.SendPacket(new ForgeResult() { Sold = true, Success = error == null, Fame = fame, Message = error });
        }
    }
}
