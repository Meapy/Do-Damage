using System.Linq;
using Shared.isc;
using Shared.isc.data;
using WorldServer.core.net.datas;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core
{
    /// <summary>
    /// The gold store (the account server's GoldController) paid gold into an account, or a refund
    /// took it back: the player, when they're on this server, sees their gold change at once and
    /// is told. The gold was already added in Redis; this only shows it.
    /// </summary>
    public sealed class GoldNews
    {
        private readonly GameServer _server;

        public GoldNews(GameServer server)
        {
            _server = server;
        }

        public void Init() => _server.InterServerManager.AddHandler<GoldMsg>(Channel.Gold, OnGoldMsg);

        private void OnGoldMsg(object sender, InterServerEventArgs<GoldMsg> e)
        {
            var id = e.Content.AccountId;
            var change = e.Content.Gold;
            foreach (var client in _server.ConnectionManager.Clients.Keys.Where(c => c.Player != null && c.Account?.AccountId == id).ToList())
            {
                var player = client.Player;
                player.World?.Post(() =>
                {
                    var acc = player.Client?.Account;
                    if (acc == null || player.World == null)
                        return;
                    var gold = (int)_server.Database.Conn.HashGet(acc.Key, "credits");
                    var total = (int)_server.Database.Conn.HashGet(acc.Key, "totalCredits");
                    acc.StoredCredits(gold);
                    acc.StoredTotalCredits(total);
                    player.Credits = gold;
                    if (change > 0)
                    {
                        player.World.BroadcastIfVisible(new Notification
                        {
                            ObjectId = player.Id,
                            Color = new ARGB(0xFFFFC65C),
                            Message = $"+{change:N0} Gold"
                        }, player);
                        player.SendInfo($"<Gold Store> Thank you! {change:N0} gold was added to your account: you have {gold:N0} now.");
                    }
                    else if (change < 0)
                        player.SendInfo($"<Gold Store> A payment was refunded: {-change:N0} gold was taken back.");
                });
            }
        }
    }
}
