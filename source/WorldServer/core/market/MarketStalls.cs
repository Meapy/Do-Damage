using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.resources;
using WorldServer.core.objects.vendors;
using WorldServer.core.worlds;

namespace WorldServer.core.market
{
    // Fills the market square's stalls (the map's `Market Stall` region tiles) with Marketplace
    // listings, the newest first. With more listings than stalls they take turns: every
    // TURN seconds the stalls move on to the next ones. A stall with nothing to show stays empty.
    public sealed class MarketStalls
    {
        private const float CHECK = 2f;     // seconds between looks at the listings
        private const float TURN = 30f;

        private readonly World _world;
        private readonly List<(float X, float Y)> _spots;
        private readonly MarketStall[] _stalls;
        private float _check, _turn;
        private int _page;
        private long _version = -1;

        public MarketStalls(World world)
        {
            _world = world;
            _spots = world.Map.Regions.Where(r => r.Value == TileRegion.Market_Stall)
                .Select(r => (r.Key.X + 0.5f, r.Key.Y + 0.5f))
                .OrderBy(p => p.Item2).ThenBy(p => p.Item1).ToList();
            _stalls = new MarketStall[_spots.Count];
        }

        public void Update(ref TickTime time)
        {
            if (_spots.Count == 0)
                return;
            _check -= time.DeltaTime;
            _turn -= time.DeltaTime;
            if (_check > 0)
                return;
            _check = CHECK;

            var market = _world.GameServer.Marketplace;
            var (version, all) = market.Listings();
            var now = DateTime.UtcNow.ToUnixTimestamp();
            var listings = all.Where(l => l.TimeLeft > now && market.Name(l.ItemType) != "an item").ToList();
            var pages = Math.Max(1, (listings.Count + _spots.Count - 1) / _spots.Count);
            var turned = _turn <= 0 && pages > 1;
            if (version == _version && !turned)
                return;
            if (_turn <= 0)
            {
                _turn = TURN;
                _page = pages > 1 ? (_page + 1) % pages : 0;
            }
            _page = Math.Min(_page, pages - 1);
            _version = version;

            var shown = listings.Skip(_page * _spots.Count).Take(_spots.Count).ToList();
            for (var i = 0; i < _spots.Count; i++)
            {
                if (i < shown.Count)
                {
                    if (_stalls[i] == null)
                    {
                        var stall = new MarketStall(_world.GameServer);
                        stall.Move(_spots[i].X, _spots[i].Y);
                        stall.Show(shown[i], now);
                        _world.EnterWorld(stall);
                        _stalls[i] = stall;
                    }
                    else
                        _stalls[i].Show(shown[i], now);
                }
                else if (_stalls[i] != null)
                {
                    _world.LeaveWorld(_stalls[i]);
                    _stalls[i] = null;
                }
            }
        }
    }
}
