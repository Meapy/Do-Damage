using Shared;
using Shared.database.vault;
using Shared.resources;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.vendors;
using WorldServer.core.structures;
using WorldServer.utils;

namespace WorldServer.core.worlds.impl
{
    public sealed class MerchantData
    {
        public TileRegion TileRegion;
        public float TimeToSpawn;
        public IntPoint Position;
        // the ground's height at Position, on stacked floors (the Nexus islands)
        public float Z;
        public NexusMerchant NewMerchant;
        public ISellableItem SellableItem;
        public CurrencyType CurrencyType;
        public int RankRequired;
        // the shop has more items than stalls, so its stalls take turns quickly (NexusMerchant)
        public bool Rotates;
    }

    // a world whose merchants go back to it when they sell out or time out
    public interface IMerchantWorld
    {
        void ReturnMerchant(MerchantData merchantData);
    }

    /// <summary>
    /// The Nexus. Everything in it is placed where its map puts it: the shops on their `Store N`
    /// tiles, the market's stalls, the realm portals, and the people (the Support Bot, the quest
    /// givers, the Broker, the villagers, the Arena's portal) on their marker regions (Nexus_Bot
    /// and so on), on whichever floor they are: the islands (tools/build_nexus_islands.py) are
    /// floors at several heights (World.Levels), the garden (tools/build_nexus.py) one.
    ///
    /// The game's Nexus is WorldManager.Nexus (<see cref="IsMain"/>). An admin can open another with
    /// /dungeon (the world `oldnexus`, the garden the islands replaced): a copy shows the
    /// real Nexus's realm portals (RealmPortalMonitor's mirror), sells from its own stock
    /// (MerchantLists.Stock) and keeps its vault chests' items apart (DbNexusChest's space), its
    /// Arena portal leads to the real Nexus's Arena; everything that sends players "to the Nexus"
    /// still means the real one.
    /// </summary>
    public sealed class NexusWorld : World, IMerchantWorld
    {
        // i dont really want to use static but it works so?
        private readonly List<MerchantData> _inactiveStorePoints = new List<MerchantData>();
        private readonly List<MerchantData> _activeStorePoints = new List<MerchantData>();

        public RealmPortalMonitor PortalMonitor { get; private set; }
        public bool MarketEnabled { get; set; } = true;

        /// <summary>The game's Nexus, not an admin's copy.</summary>
        public bool IsMain => Id == NEXUS_ID;
        // a copy's own shop items (the real Nexus takes from MerchantLists.Shops)
        private Dictionary<TileRegion, List<ISellableItem>> _stock;

        // The fountains heal whoever stands by them: Healing, renewed while they stay. The garden's
        // are stone water basins (tools/build_nexus_garden.py); an event's map may keep fountains.
        private static readonly HashSet<string> Fountains = new HashSet<string> { "White Fountain", "NG Garden Basin" };
        private const float FOUNTAIN_RANGE = 3.5f;
        private List<objects.Entity> _fountains;
        private float _fountainCheck;

        private const ushort VAULT_CHEST = 0x0504;

        // the Support Bot by the spawn, with its /gear tips and XP gifts
        private NexusGuide _guide;
        // the Tinkerer and the Chronicler, the quest NPCs beside it
        private quests.QuestGivers _questGivers;
        private community.Warcaller _warcaller;
        // the market square's stalls, showing Marketplace listings
        private market.MarketStalls _marketStalls;
        private market.MarketBroker _marketBroker;
        // the villagers walking about and the bees over the flowers
        private NexusVillagers _villagers;
        // the islands' bar on the Arena tower, and its barman
        private NexusBar _bar;

        public NexusWorld(GameServer gameServer, int id, WorldResource resource, World parent = null)
            : base(gameServer, id, resource, parent)
        {
            PortalMonitor = new RealmPortalMonitor(gameServer, this, resource.RealmPortalNorth);
        }

        public override void Init()
        {
            if (!IsMain)
                _stock = MerchantLists.Stock.ToDictionary(s => s.Key, s => s.Value.ToList());
            foreach (var shop in MerchantLists.Shops)
            {
                var stalls = RegionTiles(shop.Key);
                foreach (var (at, z) in stalls)
                    _inactiveStorePoints.Add(new MerchantData()
                    {
                        TileRegion = shop.Key,
                        Position = at,
                        Z = z,
                        Rotates = Stock(shop.Key).Count > stalls.Count
                    });
            }

            base.Init();
            LinkVaultChests();
            doomed.DoomedWorlds.AddNexusPortals(this);
            _guide = new NexusGuide(this);
            _guide.Init();
            _questGivers = new quests.QuestGivers(this);
            _questGivers.Init();
            _warcaller = new community.Warcaller(this);
            _warcaller.Init();
            _marketStalls = new market.MarketStalls(this);
            _marketBroker = new market.MarketBroker(this);
            _villagers = new NexusVillagers(this);
            _bar = NexusBar.Find(this);
        }

        // the items a shop has left to put out: the real Nexus's are the game's lists, a copy's its own
        private List<ISellableItem> Stock(TileRegion shop) => _stock != null ? _stock[shop] : MerchantLists.Shops[shop].Item1;

        // The map's shared vault chests keep their items in Redis, one record per tile: the tile a
        // chest stood on in the castle Nexus, which the garden's chests carry as their names
        // ("61_70", tools/build_nexus.py CHESTS), so they kept their items when it moved. A copy's
        // chests are its own (its world's name before the tile).
        private void LinkVaultChests()
        {
            var db = GameServer.Database.Conn;
            var chests = PendingEntities.OfType<Container>().Where(c => c.ObjectType == VAULT_CHEST && c.DbLink == null).ToList();
            foreach (var chest in chests)
            {
                var (x, y) = ((int)chest.X, (int)chest.Y);
                var key = chest.Name?.Split('_');
                if (key?.Length == 2 && int.TryParse(key[0], out var kx) && int.TryParse(key[1], out var ky))
                    (x, y) = (kx, ky);
                chest.LinkToDb(new DbNexusChest(db, x, y, IsMain ? null : IdName));
            }
            StaticLogger.Instance.Info($"{IdName}: {chests.Count} vault chests, {chests.Sum(c => c.Inventory.Count(i => i != null))} stored items");
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            HealByFountains(ref time);
            HandleMerchants(ref time);
            PortalMonitor.Update(ref time);
            _guide?.Update(ref time);
            _questGivers?.Update(ref time);
            _warcaller?.Update(ref time);
            _marketStalls?.Update(ref time);
            _marketBroker?.Update(ref time);
            _villagers?.Update(ref time);
            _bar?.Update(ref time);
            base.UpdateLogic(ref time);
        }

        private void HealByFountains(ref TickTime time)
        {
            _fountainCheck -= time.DeltaTime;
            if (_fountainCheck > 0)
                return;
            _fountainCheck = 0.5f;

            // Found once the map's objects are in (the first ticks can come before).
            if (_fountains == null || _fountains.Count == 0)
                _fountains = StaticObjects.Values.Where(o => Fountains.Contains(o.ObjectDesc.IdName)).Cast<objects.Entity>().ToList();
            if (_fountains.Count == 0)
                return;

            // DistTo: on stacked floors only a basin on the player's own floor
            foreach (var player in Players.Values)
                if (_fountains.Any(f => player.DistTo(f) <= FOUNTAIN_RANGE))
                    player.ApplyConditionEffect(ConditionEffectIndex.Healing, 1000);
        }

        public void SendOutMerchant(MerchantData merchantData)
        {
            if (MerchantLists.Shops.TryGetValue(merchantData.TileRegion, out var data))
            {
                // every item of this shop is out on another stall: try again when one comes back
                var items = Stock(merchantData.TileRegion);
                if (items.Count == 0)
                {
                    merchantData.TimeToSpawn = 10.0f;
                    return;
                }

                _inactiveStorePoints.Remove(merchantData);

                var x = merchantData.Position.X;
                var y = merchantData.Position.Y;

                merchantData.NewMerchant = new NexusMerchant(GameServer, 0x01ca);
                merchantData.NewMerchant.Move(x + 0.5f, y + 0.5f);
                merchantData.NewMerchant.Z = merchantData.Z;
                merchantData.CurrencyType = data.Item2;
                merchantData.RankRequired = data.Item3;

                merchantData.SellableItem = Random.Shared.NextLength(items);
                merchantData.NewMerchant.SetData(merchantData);
                _ = items.Remove(merchantData.SellableItem);

                EnterWorld(merchantData.NewMerchant);
                _activeStorePoints.Add(merchantData);
            }
        }

        private void HandleMerchants(ref TickTime time)
        {
            var merchantsToAdd = new List<MerchantData>();
            foreach (var merchantData in _inactiveStorePoints)
            {
                merchantData.TimeToSpawn -= time.DeltaTime;
                if (merchantData.TimeToSpawn <= 0.0f)
                    merchantsToAdd.Add(merchantData);
            }

            foreach (var merchant in merchantsToAdd)
                SendOutMerchant(merchant);
        }

        public void ReturnMerchant(MerchantData merchantData)
        {
            merchantData.TimeToSpawn = 10.0f;
            Stock(merchantData.TileRegion).Add(merchantData.SellableItem);
            LeaveWorld(merchantData.NewMerchant);
            merchantData.NewMerchant = null;
            _inactiveStorePoints.Add(merchantData);
        }

    }
}
