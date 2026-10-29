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

    public sealed class NexusWorld : World, IMerchantWorld
    {
        // i dont really want to use static but it works so?
        private readonly List<MerchantData> _inactiveStorePoints = new List<MerchantData>();
        private readonly List<MerchantData> _activeStorePoints = new List<MerchantData>();

        public RealmPortalMonitor PortalMonitor { get; private set; }
        public bool MarketEnabled { get; set; } = true;

        // The fountains heal whoever stands by them: Healing, renewed while they stay.
        private const string FOUNTAIN = "White Fountain";
        private const float FOUNTAIN_RANGE = 3.5f;
        private List<(float X, float Y)> _fountains;
        private float _fountainCheck;

        private const ushort VAULT_CHEST = 0x0504;

        // the Support Bot by the spawn, with its /gear tips and XP gifts
        private NexusGuide _guide;
        // the Tinkerer and the Chronicler, the quest NPCs beside it
        private quests.QuestGivers _questGivers;
        // the market square's stalls, showing Marketplace listings
        private market.MarketStalls _marketStalls;
        private market.MarketBroker _marketBroker;

        public NexusWorld(GameServer gameServer, int id, WorldResource resource) 
            : base(gameServer, id, resource)
        {
            PortalMonitor = new RealmPortalMonitor(gameServer, this);
        }

        public override void Init()
        {
            foreach (var shop in MerchantLists.Shops)
            {
                var stalls = Map.Regions.Where(r => shop.Key == r.Value).ToList();
                foreach (var data in stalls)
                    _inactiveStorePoints.Add(new MerchantData()
                    {
                        TileRegion = data.Value,
                        Position = data.Key,
                        Rotates = shop.Value.Item1.Count > stalls.Count
                    });
            }

            base.Init();
            LinkVaultChests();
            doomed.DoomedWorlds.AddNexusPortals(this);
            _guide = new NexusGuide(this);
            _guide.Init();
            _questGivers = new quests.QuestGivers(this);
            _questGivers.Init();
            _marketStalls = new market.MarketStalls(this);
            _marketBroker = new market.MarketBroker(this);
        }

        // The map's shared vault chests keep their items in Redis, one record per tile.
        private void LinkVaultChests()
        {
            var db = GameServer.Database.Conn;
            var chests = PendingEntities.OfType<Container>().Where(c => c.ObjectType == VAULT_CHEST && c.DbLink == null).ToList();
            foreach (var chest in chests)
                chest.LinkToDb(new DbNexusChest(db, (int)chest.X, (int)chest.Y));
            StaticLogger.Instance.Info($"Nexus: {chests.Count} vault chests, {chests.Sum(c => c.Inventory.Count(i => i != null))} stored items");
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            HealByFountains(ref time);
            HandleMerchants(ref time);
            PortalMonitor.Update(ref time);
            _guide?.Update(ref time);
            _questGivers?.Update(ref time);
            _marketStalls?.Update(ref time);
            _marketBroker?.Update(ref time);
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
                _fountains = StaticObjects.Values.Where(o => o.ObjectDesc.IdName == FOUNTAIN).Select(o => (o.X, o.Y)).ToList();
            if (_fountains.Count == 0)
                return;

            foreach (var player in Players.Values)
            {
                var near = _fountains.Any(f => (player.X - f.X) * (player.X - f.X) + (player.Y - f.Y) * (player.Y - f.Y) <= FOUNTAIN_RANGE * FOUNTAIN_RANGE);
                if (near)
                    player.ApplyConditionEffect(ConditionEffectIndex.Healing, 1000);
            }
        }

        public void SendOutMerchant(MerchantData merchantData)
        {
            if (MerchantLists.Shops.TryGetValue(merchantData.TileRegion, out var data))
            {
                // every item of this shop is out on another stall: try again when one comes back
                var items = data.Item1;
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
                merchantData.CurrencyType = data.Item2;
                merchantData.RankRequired = data.Item3;

                merchantData.SellableItem = Random.Shared.NextLength(items);
                merchantData.NewMerchant.SetData(merchantData);
                _ = MerchantLists.Shops[merchantData.TileRegion].Item1.Remove(merchantData.SellableItem);

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
            MerchantLists.Shops[merchantData.TileRegion].Item1.Add(merchantData.SellableItem);
            LeaveWorld(merchantData.NewMerchant);
            merchantData.NewMerchant = null;
            _inactiveStorePoints.Add(merchantData);
        }

    }
}
