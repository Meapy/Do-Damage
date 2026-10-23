using System;
using System.Collections.Generic;
using System.Linq;
using Shared.database.leaderboard;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.commands.doomed;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.objects.vendors;
using WorldServer.core.structures;
using WorldServer.logic.doomed;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

// The Doomed Realms worlds that run a game of their own (its wserver/realm/worlds/BattleArena.cs,
// BattleArena2.cs, ZombieMap.cs, ZombieMG.cs, Herding.cs and Shop.cs). Their maps are ported by
// tools/port_doomed.py like the other dungeons; WorldManager.CreateNewWorld asks Create for them.
namespace WorldServer.core.worlds.impl.doomed
{
    public static class DoomedWorlds
    {
        public static World Create(GameServer gameServer, int id, WorldResource resource, World parent)
        {
            switch (resource.IdName)
            {
                case "Battle Arena": return new DoomedBattleArena(gameServer, id, resource, parent, paid: true);
                case "Free Battle Arena": return new DoomedBattleArena(gameServer, id, resource, parent, paid: false);
                case "Zombies": return new DoomedZombies(gameServer, id, resource, parent);
                case "Zombies Minigame": return new DoomedZombieGame(gameServer, id, resource, parent);
                case "Sheep Herding Minigame": return new DoomedHerding(gameServer, id, resource, parent);
                case "Shop": return new DoomedShop(gameServer, id, resource, parent);
                default: return null;
            }
        }

        // Battle Arena portals in the nexus's south yard, under their banners (tools/build_castle_maps.py,
        // ARENA_PORTALS; Doomed Realms only opened them for 30 seconds with its /arena command)
        private static readonly (string Portal, float X, float Y)[] NexusPortals =
        {
            ("Battle Arena Portal", 42.5f, 68.5f),
            ("Free Battle Arena Portal", 54.5f, 68.5f),
        };

        public static void AddNexusPortals(World nexus)
        {
            foreach (var (portal, x, y) in NexusPortals)
                if (nexus.IsPassable(x, y, true))
                    nexus.CreateNewEntity(portal, x, y);
        }

        // A portal keeps the world it opened; once that minigame has started or closed, it opens a new one.
        public static void RefreshPortal(Portal portal)
        {
            var world = portal.WorldInstance;
            if (world == null)
                return;
            if (world.Deleted || portal.GameServer.WorldManager.GetWorld(world.Id) == null || (world is DoomedMinigame m && !m.Joinable))
                portal.WorldInstance = null;
        }

        // a finished arena game (Shared DbArenaRuns): the wave reached, how long from the first wave
        public static void AddArenaScore(GameServer gameServer, int wave, TimeSpan taken, string arena, IEnumerable<string> players) =>
            DbArenaRuns.Add(gameServer.Database.Conn, wave, (int)taken.TotalSeconds, arena, players);

        public static void GiveFame(Player player, int amount)
        {
            if (amount == 0 || player.Client?.Account == null)
                return;
            var acc = player.Client.Account;
            player.GameServer.Database.UpdateCurrency(acc, amount, CurrencyType.Fame)
                .ContinueWith(_ => player.CurrentFame = acc.Fame);
        }

        public static void Notify(Player player, uint color, string message) =>
            player.Client.SendPacket(new Notification() { ObjectId = player.Id, Color = new ARGB(color), Message = message });

        public static void Say(World world, string name, string text)
        {
            foreach (var p in world.Players.Values)
                p.Client.SendPacket(new Text() { Name = name, ObjectId = -1, NumStars = -1, BubbleTime = 0, Recipient = "", Txt = text });
        }
    }

    // A world that counts down in whole seconds, as the Doomed Realms minigames did with timers.
    public abstract class DoomedMinigame : World, IDoomedEventWorld
    {
        private int _countdown = -1;
        private int _countdownMs;
        private Action<int> _onSecond;
        private Action _onDone;
        private bool _goUsed;

        protected DoomedMinigame(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent) { }

        public virtual void BehaviorEvent(string type) { }

        // whether new players can still join (the countdown before the game hasn't ended)
        public virtual bool Joinable => true;

        protected bool CountingDown => _countdown >= 0;

        // onSecond(n) runs for n = seconds .. 1, then onDone
        protected void Countdown(int seconds, Action<int> onSecond, Action onDone)
        {
            _countdown = seconds;
            _countdownMs = 0;
            _goUsed = false;
            _onSecond = onSecond;
            _onDone = onDone;
            onSecond?.Invoke(seconds);
        }

        // cuts the running countdown to `seconds`, once per countdown; false if it couldn't
        protected bool CutCountdown(int seconds)
        {
            if (_countdown <= seconds || _goUsed)
                return false;
            _goUsed = true;
            _countdown = seconds;
            _countdownMs = 0;
            _onSecond?.Invoke(seconds);
            return true;
        }

        // the monsters of the game: enemies that aren't players' allies or walls (destructible walls
        // are enemies in betterSkillys but were walls in Doomed Realms)
        protected IEnumerable<Enemy> Hostiles => Enemies.Values.Where(e => !e.Dead && e.AllyOwnerId == -1 && !e.ObjectDesc.Static && e.ObjectDesc.Class != "Wall");

        protected Entity Spawn(string id, float x, float y)
        {
            if (!GameServer.Resources.GameData.IdToObjectType.TryGetValue(id, out var type))
                return null;
            var e = Entity.Resolve(GameServer, type);
            e.Move(x, y);
            EnterWorld(e);
            return e;
        }

        protected void NotifyAll(uint color, string message)
        {
            foreach (var p in Players.Values)
                DoomedWorlds.Notify(p, color, message);
        }

        protected List<IntPoint> RegionPoints(TileRegion region) =>
            Map.Regions.Where(r => r.Value == region).Select(r => r.Key).ToList();

        protected override void UpdateLogic(ref TickTime time)
        {
            if (_countdown >= 0)
            {
                _countdownMs += time.ElapsedMsDelta;
                while (_countdown >= 0 && _countdownMs >= 1000)
                {
                    _countdownMs -= 1000;
                    _countdown--;
                    if (_countdown > 0)
                        _onSecond?.Invoke(_countdown);
                    else
                    {
                        _countdown = -1;
                        var done = _onDone;
                        _onDone = null;
                        done?.Invoke();
                    }
                }
            }
            base.UpdateLogic(ref time);
        }
    }

    // Battle Arena (paid: fame every wave) and Free Battle Arena: waves of gods and a boss until
    // everyone dies; the wave reached goes on the /leaderboard.
    public sealed class DoomedBattleArena : DoomedMinigame
    {
        private readonly bool _paid;
        private bool _started, _counting;
        private readonly List<string> _participants = new List<string>();
        private DateTime _firstWave; // when the countdown ended, for the leaderboard's time
        public int Wave { get; private set; }

        public DoomedBattleArena(GameServer gameServer, int id, WorldResource resource, World parent, bool paid)
            : base(gameServer, id, resource, parent)
        {
            _paid = paid;
            DefeatToNexus = true; // its world XML is generated (port_doomed_worlds.py)
        }

        public override bool Joinable => !_started;
        public IEnumerable<string> PlayerNames => Players.Values.Select(p => p.Name);

        public override bool AllowedAccess(Client client) => base.AllowedAccess(client) && (Joinable || client.Account.Admin);

        public override void ChatReceived(Player player, string text)
        {
            base.ChatReceived(player, text);
            if (text.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase) && CutCountdown(3))
                NotifyAll(0xffff00ff, player.Name + " said GO!");
        }

        private static bool OutOfBounds(float x, float y) => x <= 3.5f || x >= 29.5f || y <= 3.5f || y >= 29.5f;
        private const float ArenaMiddle = 16.5f;

        private void SpawnWave()
        {
            // the same stages as the public arena (ArenaPools)
            var bosses = ArenaPools.Bosses(Wave);
            foreach (var p in Players.Values)
            {
                p.SendInfo(ArenaPools.WaveMessage(Wave));
                p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, ArenaPools.StartInvulnerableMs(bosses));
            }
            foreach (var name in ArenaPools.Minions(Wave, Players.Count).Concat(bosses))
            {
                // an Oryx fights round where he spawns, so he takes the middle
                if (ArenaPools.IsOryx(name))
                    Spawn(name, ArenaMiddle, ArenaMiddle);
                else
                    Spawn(name, Random.Shared.Next(6, 27) + 0.5f, Random.Shared.Next(6, 27) + 0.5f);
            }
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            base.UpdateLogic(ref time);
            if (Players.Count == 0)
            {
                if (_participants.Count > 0)
                {
                    DoomedWorlds.AddArenaScore(GameServer, Wave, DateTime.UtcNow - _firstWave, IdName, _participants);
                    _participants.Clear();
                }
                return;
            }

            if (!_started && !_counting)
            {
                GameServer.ChatManager.ServerAnnounce($"A {(_paid ? "paid" : "free")} arena game has been started. Closing in 1 minute!");
                _counting = true;
                Countdown(60, s => NotifyAll(0xffff00ff, s + " Second" + (s == 1 ? "" : "s")), () =>
                {
                    _started = true;
                    _counting = false;
                    _firstWave = DateTime.UtcNow;
                });
                return;
            }

            if (!_started || _counting)
                return;

            if (!Hostiles.Any())
            {
                Wave++;
                if (_paid)
                {
                    var pot = Wave * 10 / (Players.Count == 1 ? 1 : 2);
                    foreach (var p in Players.Values)
                    {
                        DoomedWorlds.GiveFame(p, pot);
                        DoomedWorlds.Notify(p, 0xFFFF6600, "+" + pot + " Fame");
                    }
                }
                var ms = _paid ? 6000 : 5000;
                foreach (var p in Players.Values)
                {
                    p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, ms);
                    p.ApplyConditionEffect(ConditionEffectIndex.Healing, ms);
                    if (!_participants.Contains(p.Name))
                        _participants.Add(p.Name);
                }
                _counting = true;
                var gap = ArenaWorld.WaveGapSeconds(Wave);
                Countdown(gap, s => NotifyAll(0xffff00ff, (s == gap ? "Wave " + Wave + " - " : "") + s + " Second" + (s == 1 ? "" : "s")), () =>
                {
                    _counting = false;
                    SpawnWave();
                });
            }
            else
                foreach (var e in Hostiles.Where(e => OutOfBounds(e.X, e.Y)).ToList())
                    LeaveWorld(e);
        }
    }

    // Zombies: endless waves of zombies over the whole map.
    public sealed class DoomedZombies : DoomedMinigame
    {
        private static readonly string[] Zombies = { "Zombie", "Hell Hound", "Crawler" };
        private const string SpecialZombie = "Zombie of Draconis";
        private bool _waiting;
        public int Wave { get; private set; }

        public DoomedZombies(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent) { }

        private void SpawnWave()
        {
            // Doomed Realms threw 1000 zombies at random spots of a 254x254 area; about a ninth landed
            // on this map. Here they all land on it, on open ground.
            var spawned = 0;
            for (var tries = 0; spawned < 110 && tries < 5000; tries++)
            {
                var x = Random.Shared.Next(1, Map.Width - 1) + 0.5f;
                var y = Random.Shared.Next(1, Map.Height - 1) + 0.5f;
                if (!IsPassable(x, y, true))
                    continue;
                Spawn(Zombies[Random.Shared.Next(Zombies.Length)], x, y);
                spawned++;
            }
            if (Wave % 5 == 0) // its unused SpecialZombies: a Zombie of Draconis every fifth wave
            {
                var spawn = GetSpawnPoints();
                if (spawn.Length > 0)
                {
                    var p = spawn[Random.Shared.Next(spawn.Length)].Key;
                    Spawn(SpecialZombie, p.X + 0.5f, p.Y + 0.5f);
                }
            }
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            base.UpdateLogic(ref time);
            if (Players.Count == 0)
            {
                foreach (var e in Hostiles.ToList())
                    LeaveWorld(e);
                Wave = 0;
                _waiting = false;
                return;
            }
            if (Hostiles.Any() || _waiting)
                return;
            Wave++;
            _waiting = true;
            Countdown(5, s => NotifyAll(0xffff00ff, $"Wave {Wave} Starting in {s}..."), () =>
            {
                NotifyAll(0xffff00ff, $"Wave {Wave} Starting...");
                SpawnWave();
                _waiting = false;
            });
        }
    }

    // Zombies Minigame: defend the tower; each zombie that reaches it damages it.
    public sealed class DoomedZombieGame : DoomedMinigame
    {
        private static readonly string[] SpawningZombies = { "Regular Zombie", "Regular Zombie", "Speedy Zombie", "Tank Zombie" };
        private List<IntPoint> _zombieSpawns;
        private IntPoint? _towerPoint;
        private Entity _tower;
        private bool _started, _counting, _finished;
        private int _famePot, _towerHp = 50, _wave, _zombieAmount = 1;

        public DoomedZombieGame(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent) { }

        public override bool AllowedAccess(Client client) => base.AllowedAccess(client) && (Joinable || client.Account.Admin);
        public override bool Joinable => !_started;

        public override void Init()
        {
            base.Init();
            _zombieSpawns = RegionPoints(TileRegion.Defender);
            var enemy = RegionPoints(TileRegion.Enemy);
            if (enemy.Count > 0)
                _towerPoint = enemy.OrderBy(p => p.Y).ThenBy(p => p.X).First();
        }

        private void SpawnZombies()
        {
            if (_zombieSpawns.Count == 0)
                return;
            for (var i = 0; i < _zombieAmount; i++)
            {
                var p = _zombieSpawns[Random.Shared.Next(_zombieSpawns.Count)];
                Spawn(SpawningZombies[Random.Shared.Next(SpawningZombies.Length)], p.X, p.Y);
            }
        }

        public override void BehaviorEvent(string type)
        {
            var parts = type.Split(':');
            if (parts.Length < 2 || parts[0] != "dmg" || _finished || !int.TryParse(parts[1], out var dmg))
                return;
            _towerHp -= dmg;
            if (_towerHp > 0)
            {
                DoomedWorlds.Say(this, "#Zombies", $"Your tower's been damaged! {_towerHp} HP left.");
                return;
            }
            _finished = true;
            DoomedWorlds.Say(this, "#Zombies", $"Your tower's been destroyed! You each earn {_famePot} fame!");
            foreach (var p in Players.Values)
            {
                DoomedWorlds.GiveFame(p, _famePot);
                DoomedWorlds.Notify(p, 0xFFFF6600, "+" + _famePot + " Fame");
            }
            if (_tower != null)
                LeaveWorld(_tower);
            foreach (var e in Hostiles.ToList())
                LeaveWorld(e);
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            base.UpdateLogic(ref time);
            if (Players.Count == 0 || _finished)
                return;

            if (!_started && !_counting)
            {
                GameServer.ChatManager.ServerAnnounce("A zombie minigame has been started. Closing in 1 minute!");
                if (_towerPoint is IntPoint t)
                    _tower = Spawn("Zombie Tower", t.X + 0.5f, t.Y + 0.5f);
                _counting = true;
                Countdown(60, s => NotifyAll(0xffff0000, s + " Second" + (s == 1 ? "" : "s")), () =>
                {
                    _started = true;
                    _counting = false;
                });
                return;
            }

            if (_started && !_counting && !Hostiles.Any())
            {
                _wave++;
                _zombieAmount += Players.Count;
                _famePot += _wave / 4;
                _counting = true;
                Countdown(5, s => NotifyAll(0xffff0000, (s == 5 ? "Wave " + _wave + " - " : "") + s + " Second" + (s == 1 ? "" : "s")), () =>
                {
                    _counting = false;
                    SpawnZombies();
                });
            }
        }
    }

    // Sheep Herding: two minutes to herd sheep into the pen; black sheep empty the fame pot.
    public sealed class DoomedHerding : DoomedMinigame
    {
        private static readonly string[] SpawningSheep = Enumerable.Repeat("Herding Sheep", 14)
            .Concat(new[] { "Black Herding Sheep", "Giant Herding Sheep" }).ToArray();
        private List<IntPoint> _sheepSpawns;
        private bool _started, _counting, _finished;
        private int _famePot, _herdedSheep;

        public DoomedHerding(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent) { }

        public override bool AllowedAccess(Client client) => base.AllowedAccess(client) && (Joinable || client.Account.Admin);
        public override bool Joinable => !_started;

        public override void Init()
        {
            base.Init();
            _sheepSpawns = RegionPoints(TileRegion.Defender);
        }

        public override void BehaviorEvent(string type)
        {
            if (_finished)
                return;
            if (type.StartsWith("sheep"))
            {
                var giant = type.EndsWith("-g");
                var inc = (giant ? 100 : 10) * Players.Count + 10 * _herdedSheep;
                NotifyAll(0xFFFFFF00, "+" + inc + " Fame in Pot");
                _herdedSheep += giant ? 2 : 1;
                _famePot += inc;
            }
            else if (type == "blackSheep")
            {
                NotifyAll(0xFF777700, "Oh no! A black sheep stole all the fame!");
                _famePot = 0;
            }
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            base.UpdateLogic(ref time);
            if (Players.Count == 0 || _finished)
                return;

            if (!_started && !_counting)
            {
                GameServer.ChatManager.ServerAnnounce("A sheep herding minigame has been started. Closing in 1 minute!");
                _counting = true;
                Countdown(60, s => NotifyAll(0xffffff00, s + " Second" + (s == 1 ? "" : "s")), () =>
                {
                    DoomedWorlds.Say(this, "#Sheep Herding", "You have 2 minutes to herd sheep! Watch out for black sheep!");
                    _started = true;
                    Countdown(120, null, () => _counting = false);
                });
                return;
            }

            if (_started && _counting)
            {
                if (Hostiles.Count() < 15 && _sheepSpawns.Count > 0)
                {
                    var p = _sheepSpawns[Random.Shared.Next(_sheepSpawns.Count)];
                    Spawn(SpawningSheep[Random.Shared.Next(SpawningSheep.Length)], p.X, p.Y);
                }
            }
            else if (_started)
            {
                var share = _famePot / Players.Count;
                DoomedWorlds.Say(this, "#Sheep Herding", $"Time's up! You each win {share} fame!");
                foreach (var p in Players.Values)
                {
                    DoomedWorlds.GiveFame(p, share);
                    DoomedWorlds.Notify(p, 0xFFFF6600, "+" + share + " Fame");
                }
                foreach (var e in Hostiles.ToList())
                    LeaveWorld(e);
                _finished = true;
            }
        }
    }

    // Shop: merchants on every Store_1 tile selling one of the Doomed Realms shop lists
    // (DoomedEconomyData.ShopItems), chosen with /shop <name>.
    public sealed class DoomedShop : World, IMerchantWorld
    {
        private readonly List<MerchantData> _inactive = new List<MerchantData>();
        private List<ISellableItem> _items;
        private CurrencyType _currency;
        public string ShopName { get; private set; } = DoomedEconomyData.DefaultShop;

        public DoomedShop(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent) { }

        public static IEnumerable<string> ShopNames => DoomedEconomyData.ShopItems.Select(i => i.Shop).Distinct();

        public void SetShop(string name)
        {
            ShopName = name;
            _items = null;
        }

        public override void Init()
        {
            foreach (var r in Map.Regions.Where(r => r.Value == TileRegion.Store_1))
                _inactive.Add(new MerchantData() { TileRegion = TileRegion.Store_1, Position = r.Key });
            base.Init();
        }

        private void LoadItems()
        {
            var data = GameServer.Resources.GameData;
            _items = new List<ISellableItem>();
            var entries = DoomedEconomyData.ShopItems.Where(i => i.Shop == ShopName && !SealedDungeons.IsSealedItem(i.Item)).ToList();
            _currency = entries.Count > 0 ? entries[0].Currency : CurrencyType.Fame;
            foreach (var e in entries)
                if (data.IdToObjectType.TryGetValue(e.Item, out var type) && data.Items.ContainsKey(type))
                {
                    var item = new ShopItem(e.Item, (ushort)Math.Min(e.Price, ushort.MaxValue));
                    item.SetItem(type);
                    _items.Add(item);
                }
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            if (_items == null)
                LoadItems();
            foreach (var m in _inactive.ToList())
            {
                m.TimeToSpawn -= time.DeltaTime;
                if (m.TimeToSpawn > 0 || _items.Count == 0)
                    continue;
                _inactive.Remove(m);
                m.SellableItem = _items[Random.Shared.Next(_items.Count)];
                _items.Remove(m.SellableItem);
                m.CurrencyType = _currency;
                m.RankRequired = 0;
                m.NewMerchant = new NexusMerchant(GameServer, 0x01ca);
                m.NewMerchant.Move(m.Position.X + 0.5f, m.Position.Y + 0.5f);
                m.NewMerchant.SetData(m);
                EnterWorld(m.NewMerchant);
            }
            base.UpdateLogic(ref time);
        }

        public void ReturnMerchant(MerchantData merchantData)
        {
            merchantData.TimeToSpawn = 10.0f;
            _items?.Add(merchantData.SellableItem);
            LeaveWorld(merchantData.NewMerchant);
            merchantData.NewMerchant = null;
            _inactive.Add(merchantData);
        }
    }
}
