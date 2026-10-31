using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.market
{
    // The Market Broker greets each player who comes near with what the Marketplace is for, at most
    // every GREET_AGAIN seconds, as the Tinkerer and the Chronicler do (QuestGivers). In the Nexus
    // garden it walks about the market's courtyard (`NG Market Broker`, NexusVillagers): it's made
    // here, at START, and greets from wherever it is. A map that has its own Broker (a MarketNPC on
    // a tile) keeps it there. The lines read the cached listings only, never Redis.
    public sealed class MarketBroker
    {
        private const float GREET_RANGE = 4.5f;
        private const float GREET_AGAIN = 180f;
        private const string NAME = "#Market Broker";
        private const string WALKER = "NG Market Broker";
        // where the walking Broker starts (tools/build_nexus.py BROKER)
        private static readonly (float X, float Y) START = (110.5f, 61.5f);
        private Entity _walker;

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
            if (_walker?.World != null)
                _broker = (_walker.X, _walker.Y, _walker.Id);
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
            if (_world.GameServer.Resources.GameData.IdToObjectType.ContainsKey(WALKER))
                _walker = _world.CreateNewEntity(WALKER, START.X, START.Y);
            return _walker == null ? null : (_walker.X, _walker.Y, _walker.Id);
        }

        /// <summary>Where the Broker is now (on its tile, or walking), for /market.</summary>
        public static (float X, float Y)? Where(World world)
        {
            var walker = world.SellableObjects.Values.FirstOrDefault(o => o.ObjectDesc.IdName == WALKER);
            return walker == null ? null : (walker.X, walker.Y);
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
