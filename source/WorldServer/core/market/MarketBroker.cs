using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.market
{
    // The Market Broker (the map's MarketNPC, in the market square) greets each player who comes
    // near with what the Marketplace is for, at most every GREET_AGAIN seconds, as the Tinkerer and
    // the Chronicler do (QuestGivers). It's a static object of the map, not an entity, so it's found
    // on its tile, and speaks with the tile's object id. The lines read the cached listings only,
    // never Redis.
    public sealed class MarketBroker
    {
        private const float GREET_RANGE = 4.5f;
        private const float GREET_AGAIN = 180f;
        private const string NAME = "#Market Broker";

        private readonly World _world;
        private readonly Dictionary<int, float> _greeted = new Dictionary<int, float>();
        private (float X, float Y, int Id)? _broker;
        private bool _looked;
        private float _clock, _check;

        public MarketBroker(World world) => _world = world;

        public void Update(ref TickTime time)
        {
            _clock += time.DeltaTime;
            _check -= time.DeltaTime;
            if (_check > 0)
                return;
            _check = 0.5f;
            if (!_looked)
            {
                _looked = true;
                _broker = Find();
            }
            if (_broker is not var (bx, by, id))
                return;
            foreach (var k in _greeted.Where(g => g.Value <= _clock).Select(g => g.Key).ToList())
                _greeted.Remove(k);
            foreach (var player in _world.Players.Values)
            {
                var (dx, dy) = (player.X - bx, player.Y - by);
                if (_greeted.ContainsKey(player.Id) || dx * dx + dy * dy > GREET_RANGE * GREET_RANGE)
                    continue;
                _greeted[player.Id] = _clock + GREET_AGAIN;
                player.Client.SendPacket(new Text() { Name = NAME, ObjectId = id, NumStars = -1, BubbleTime = 5, Recipient = "", Txt = Line(player) });
            }
        }

        private (float X, float Y, int Id)? Find()
        {
            var map = _world.Map;
            for (var y = 0; y < map.Height; y++)
                for (var x = 0; x < map.Width; x++)
                    if (map[x, y].ObjDesc?.Class == Marketplace.BrokerClass)
                        return (x + 0.5f, y + 0.5f, map[x, y].ObjId);
            return null;
        }

        private string Line(Player player)
        {
            if (player.Client.Account.Guest)
                return "Register your account, and you can buy and sell here.";
            var market = _world.GameServer.Marketplace;
            var now = DateTime.UtcNow.ToUnixTimestamp();
            var listings = market.Listings().Listings.Where(l => l.TimeLeft > now).ToList();
            var mine = listings.Count(l => l.SellerId == player.AccountId);
            var others = listings.Count - mine;
            if (mine > 0)
                return $"{player.Name}! {mine} of your items {(mine == 1 ? "is" : "are")} on sale. List more, or buy what others sell: talk to me!";
            return others switch
            {
                0 => $"Welcome to the Marketplace, {player.Name}! List your items for fame, and I'll find the buyers, even while you're away.",
                1 => $"Buy, sell and list your items here, {player.Name}! One item is for sale right now.",
                _ => $"Buy, sell and list your items here, {player.Name}! {others} items are for sale right now.",
            };
        }
    }
}
