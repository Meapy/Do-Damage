using System;
using System.Collections.Generic;
using System.Linq;
using Shared.database.leaderboard;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.commands.doomed;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.objects.containers;
using WorldServer.core.objects.vendors;
using WorldServer.core.resurrection;
using WorldServer.core.structures;
using WorldServer.core.terrain;
using WorldServer.logic.doomed;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

// The Doomed Realms worlds that run a game of their own (its wserver/realm/worlds/BattleArena.cs,
// BattleArena2.cs, ZombieMap.cs, ZombieMG.cs, Herding.cs and Shop.cs). Their worlds are in
// custom/DoomedRealmsWorlds.xml; WorldManager.CreateNewWorld asks Create for them.
namespace WorldServer.core.worlds.impl.doomed
{
    public static class DoomedWorlds
    {
        public static World Create(GameServer gameServer, int id, WorldResource resource, World parent)
        {
            switch (resource.IdName)
            {
                case ArenaName: return new DoomedBattleArena(gameServer, id, resource, parent);
                case "Zombies": return new DoomedZombies(gameServer, id, resource, parent);
                case "Zombies Minigame": return new DoomedZombieGame(gameServer, id, resource, parent);
                case "Sheep Herding Minigame": return new DoomedHerding(gameServer, id, resource, parent);
                case "Shop": return new DoomedShop(gameServer, id, resource, parent);
                default: return null;
            }
        }

        // the Arena (Doomed Realms' Battle Arena) and its portal: the
        // Nexus garden's, a sand pit sunk into the shrine's floor (tools/build_nexus_garden.py; the
        // old standing one was Doomed Realms' "Battle Arena Portal")
        public const string ArenaName = "Arena";
        public const string ArenaPortal = "NG Arena Portal";

        // the Arena portal in the shrine of the realm portals, on the map's Arena_Portal tile
        // (tools/build_nexus.py ARENA_PORTALS, build_nexus_islands.py), else where the garden had
        // it, west of Realm 2's vortex (Doomed Realms only opened it for 30 seconds with its /arena
        // command)
        private static readonly (string Portal, TileRegion Marker, float X, float Y)[] NexusPortals =
        {
            (ArenaPortal, TileRegion.Arena_Portal, 34.5f, 31.5f),
        };

        public static void AddNexusPortals(World nexus)
        {
            foreach (var (portal, marker, x, y) in NexusPortals)
            {
                var (px, py, pz) = nexus.Marker(marker) ?? (x, y, 0f);
                if (nexus.IsPassable(px, py, true))
                    nexus.CreateNewEntity(portal, px, py, nexus.Levels == null ? (float?)null : pz);
            }
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

        // /arena: into the game the Nexus's Arena portal leads to, opening one (for the portal too)
        // when there is none, so the command and the portal always lead to the same game
        public static void JoinArena(Player player)
        {
            var nexus = player.GameServer.WorldManager.Nexus;
            nexus.Post(() =>
            {
                var portal = nexus.Portals.Values.FirstOrDefault(p => p.ObjectDesc.IdName == ArenaPortal);
                if (portal == null)
                {
                    player.SendError("The Arena is closed.");
                    return;
                }
                RefreshPortal(portal);
                if (portal.WorldInstance != null)
                {
                    player.Reconnect(portal.WorldInstance);
                    return;
                }
                if (portal.Opening != null)
                {
                    player.SendInfo("The Arena is opening: try again in a moment.");
                    return;
                }
                portal.Opening = new List<Player>();
                player.GameServer.WorldManager.CreateNewWorldAsync(ArenaName, nexus, built =>
                {
                    var waiting = portal.Opening;
                    portal.Opening = null;
                    if (built == null)
                    {
                        player.SendError("[Bug] Unable to open the Arena.");
                        return;
                    }
                    portal.WorldInstance = built;
                    player.Reconnect(built);
                    // anyone who took the portal meanwhile goes in too
                    foreach (var p in waiting ?? new List<Player>())
                        if (p.Client != null && !p.Dead && p.World == nexus)
                            p.Reconnect(built);
                });
            });
        }

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

        // whether this countdown was cut already (CutCountdown works once per countdown)
        protected bool CountdownCut => _goUsed;

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

    // The Arena (Doomed Realms' Battle Arena): waves of monsters and bosses until everyone is
    // defeated, fame after every wave; the wave reached goes on the season's leaderboard.
    //
    // Each wave is a round of the season (ArenaSeason2, planned by an ArenaPlanner): an
    // encounter's bosses and monsters, in one of several shapes (a horde, a clash of two dungeons,
    // a boss rush that brings its bosses one after another, a throwback's Legendary boss, a
    // showdown every tenth wave) and maybe with a twist (elite bosses, a swarm, armoured or hasted
    // monsters, double fame, a blessing, full loot). Bosses have at most the tier's health
    // (ArenaSeason2.HealthCap), and from wave 12 an Empowered Elder boss (BossResurrection) may rise
    // in the middle of a fight, for fame of its own and a count on the leaderboard.
    //
    // Between waves (and in the minute before the first) the players vote (Vote, the Rust client's
    // Arena panel): Next starts the wave once at least half of them want it (the countdown is cut to
    // 3 s, as GO in chat did; GO still counts as a Next vote), and they can combine the next waves
    // into one fight: each picks +1, +2 or +5 waves, and the fight brings the most waves that more
    // than half of them picked at least (a pick for +5 also backs +2), so a solo player decides
    // alone and nobody is dragged into more than most want. A combined fight spawns every one of
    // its waves' monsters at once and pays each wave's fame and quest progress as it would have
    // one by one, so combining only saves time. From ArenaPools.CombineBeforeWave (Oryx) on,
    // waves come one at a time. While the votes are open, subscribers see what the next fight
    // will be (ARENA_ROUND).
    //
    // Two Arena Chests stand inside the ring (ArenaChest): anyone in the Arena can leave items in
    // them for the others, and they don't go the way loot bags do. When the Arena closes (everyone
    // has left), what's still in them is lost.
    public sealed class DoomedBattleArena : DoomedMinigame
    {
        private bool _started, _counting;
        private readonly List<string> _participants = new List<string>();
        private DateTime _firstWave; // when the countdown ended, for the leaderboard's time
        public int Wave { get; private set; }

        // The map is the Admin Arena's (its <MapJM>): a walled ring with four
        // gateways, each shut by a pair of Oryx Doors (spiked gates drawn wider than their tile, so
        // two cover the gateway), and the spawn points outside it in the forest. The doors come off
        // when the world opens, so the gateways look and are open through the countdown; when it
        // ends they go back, invisible blockers fill the tiles between them, and anyone left outside
        // or in a gateway is moved just inside. All of it is read from the map, so another map with
        // Oryx Doors works too. Static tile objects, so both clients' collision agrees.
        private const string GapObject = "Iinvisible NoWalk";
        private const int SpawnRadius = 18; // monsters spawn this close to the middle
        private List<(IntPoint At, ushort Type)> _doors;
        private List<IntPoint> _gates;      // the gateways' tiles between and beside the doors
        private HashSet<IntPoint> _inside;  // what's inside the ring, gateways not included
        private List<IntPoint> _floor;      // inside, walkable and not lava
        private IntPoint _middle;
        private bool _closed;
        private TickTime _time;
        private readonly Dictionary<int, long> _movedAt = new Dictionary<int, long>();

        // the votes, by player object id: Next for this break, and the combine picks, which stay
        // from break to break until changed; who gets ARENA_VOTES; the first wave of the fight
        // running; the last wave whose fame was paid; whether the first wave comes straight away
        private readonly HashSet<int> _nextVotes = new HashSet<int>();
        private readonly Dictionary<int, int> _combinePicks = new Dictionary<int, int>();
        private readonly HashSet<int> _voteSubscribers = new HashSet<int>();
        private int _fightFrom;
        private int _paidWave;
        private bool _skipFirstGap;

        // ---- Season 2's rounds ----
        private readonly ArenaPlanner _planner = new ArenaPlanner();
        // a boss rush's bosses still to come, the one fighting, and the wait before the next
        private readonly Queue<(string Name, ArenaRound Round)> _relay = new Queue<(string, ArenaRound)>();
        private Enemy _relayBoss;
        private int _relayWaitMs;
        // an Empowered boss to rise in this fight (and when), the one risen, the last wave one rose
        // in, and how many the players have slain
        private string _empoweredNext;
        private long _empoweredAt;
        private Enemy _empowered;
        private int _lastEmpoweredWave = -100;
        private int _empoweredSlain;
        // the bosses' health caps and the twists' defense, kept up while they live
        private readonly Dictionary<Enemy, (int Cap, int Defense, int Ticks)> _tuned = new Dictionary<Enemy, (int, int, int)>();
        // the Golden twist's full loot until the fight ends
        private bool _golden;
        // the admins' tests (/arenatest): the next fight's wave and an Empowered boss to force
        private int _forceWave;
        private string _forceEmpowered;
        // a game an admin changed with /arenatest: kept off the leaderboard
        private bool _tested;
        // what the last preview sent was, so it goes again only when it changes
        private string _previewSent;
        private ArenaProbe _probe;
        // the last round fought, for the leaderboard (where the run fell)
        private string _lastRound = "";

        public const int IroncladDefense = 12;

        public DoomedBattleArena(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent)
        {
            // (set here rather than in its world XML)
            DefeatToNexus = true;
            LootRate = ArenaPools.LootRate;
            FameRate = ArenaPools.FameRate;
            KillFameRate = ArenaPools.KillFameRate;
        }

        public override bool Joinable => !_started && _probe == null;
        public IEnumerable<string> PlayerNames => Players.Values.Select(p => p.Name);
        public int EmpoweredSlain => _empoweredSlain;

        public override void Init()
        {
            base.Init();
            FindGates(); // the doors come off before anyone arrives
            PlaceChests();
        }

        // the Arena Chests: on the floor nearest a few tiles south of the middle, two tiles apart
        private void PlaceChests()
        {
            if (!GameServer.Resources.GameData.IdToObjectType.TryGetValue(ArenaChest.ObjectId, out var type))
                return;
            var taken = new HashSet<IntPoint>();
            foreach (var dx in new[] { -1, 1 })
            {
                var (tx, ty) = (_middle.X + dx + 0.5f, _middle.Y + ArenaPools.ChestOffset + 0.5f);
                var spot = _floor.Where(f => !taken.Contains(f))
                    .OrderBy(f => (f.X + 0.5f - tx) * (f.X + 0.5f - tx) + (f.Y + 0.5f - ty) * (f.Y + 0.5f - ty)).First();
                taken.Add(spot);
                var chest = new ArenaChest(GameServer, type);
                chest.Move(spot.X + 0.5f, spot.Y + 0.5f);
                EnterWorld(chest);
            }
        }

        public override bool AllowedAccess(Client client) => base.AllowedAccess(client) && (Joinable || client.Account.Admin);

        // GO in chat (the old way, and the only one in older clients): a vote for the next wave
        public override void ChatReceived(Player player, string text)
        {
            base.ChatReceived(player, text);
            if (text.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase) && VotingOpen && _nextVotes.Add(player.Id))
            {
                CheckNext();
                SendVotes();
            }
        }

        // the votes are open in the countdown before the first wave and between waves
        private bool VotingOpen => _counting && CountingDown && Players.Count > 0;

        // the wave the next fight starts at
        private int NextWave => _started ? Wave : _forceWave > 0 ? _forceWave : 1;

        // the most waves the next fight may combine: up to ArenaPools.MaxCombine, never into the Oryx stage
        private int MaxCombine => ArenaPools.MaxCombineAt(NextWave);

        private int NextNeeded => ArenaPools.NextVotesNeeded(Players.Count);

        // enough want the next wave: it's on its way (the countdown cut, or under 3 s already)
        private bool Starting => VotingOpen && (CountdownCut || _nextVotes.Count(Players.ContainsKey) >= NextNeeded);

        private int CombineNeeded => ArenaPools.CombineVotesNeeded(Players.Count);

        // how many waves the next fight combines as the votes stand (ArenaPools.CombineDecided)
        private int Combine => ArenaPools.CombineDecided(Players.Keys.Select(id => _combinePicks.TryGetValue(id, out var k) ? k : 1), MaxCombine);

        // ARENA_VOTE: 0 Next (again to take it back), 1 to 5 a combine pick (+1 is no combining),
        // 255 to get ARENA_VOTES (and ARENA_ROUND) from now on
        public void Vote(Player player, byte action)
        {
            if (action == 255)
            {
                _voteSubscribers.Add(player.Id);
                SendVotes(player);
                if (_started && !_counting && Wave > 0)
                    player.Client?.SendPacket(RoundMessage(ArenaRoundMessage.Begins, _fightFrom, Wave));
                return;
            }
            if (!VotingOpen || !Players.ContainsKey(player.Id))
                return;
            if (action == 0)
            {
                if (Starting)
                    return; // enough voted: it's starting
                if (!_nextVotes.Add(player.Id))
                    _nextVotes.Remove(player.Id);
                CheckNext();
            }
            else if (action <= ArenaPools.MaxCombine)
                _combinePicks[player.Id] = action;
            else
                return;
            SendVotes();
        }

        // enough Next votes: the countdown goes down to its last 3 s
        private void CheckNext()
        {
            if (!VotingOpen || CountdownCut || _nextVotes.Count(Players.ContainsKey) < NextNeeded)
                return;
            if (CutCountdown(3))
                NotifyAll(0xffff00ff, Players.Count == 1 ? "Next wave!" : "Voted: next wave!");
        }

        // forgets the votes of players who left (and starts the wave if those left now have enough)
        private void PruneVotes()
        {
            _voteSubscribers.RemoveWhere(id => !Players.ContainsKey(id));
            var gone = _nextVotes.Where(id => !Players.ContainsKey(id))
                .Concat(_combinePicks.Keys.Where(id => !Players.ContainsKey(id))).ToList();
            if (gone.Count == 0)
                return;
            foreach (var id in gone)
            {
                _nextVotes.Remove(id);
                _combinePicks.Remove(id);
            }
            CheckNext();
            SendVotes();
        }

        private ArenaVotes VotesMessage() => new ArenaVotes
        {
            Open = VotingOpen,
            NextWave = NextWave,
            MaxCombine = (byte)MaxCombine,
            Combine = (byte)Combine,
            NextNeeded = (byte)Math.Min(255, NextNeeded),
            CombineNeeded = (byte)Math.Min(255, CombineNeeded),
            Starting = Starting,
            Entries = Players.Values.Take(255).Select(p => (p.Id, p.Name, _nextVotes.Contains(p.Id),
                (byte)(_combinePicks.TryGetValue(p.Id, out var k) ? k : 0))).ToArray(),
        };

        // ARENA_VOTES to one subscriber, or to every one; while the votes are open, what the next
        // fight will be too (ARENA_ROUND), when it changed or to a new subscriber
        private void SendVotes(Player only = null)
        {
            var to = only != null ? new[] { only } : Players.Values.Where(p => _voteSubscribers.Contains(p.Id)).ToArray();
            if (to.Length == 0)
                return;
            var message = VotesMessage();
            foreach (var p in to)
                p.Client?.SendPacket(message);
            if (!VotingOpen)
            {
                _previewSent = null;
                return;
            }
            var last = NextWave + Combine - 1;
            var key = NextWave + "-" + last;
            if (only == null && key == _previewSent)
                return;
            _previewSent = key;
            var preview = RoundMessage(ArenaRoundMessage.Next, NextWave, last);
            foreach (var p in to)
                p.Client?.SendPacket(preview);
        }

        private ushort TypeOf(string id) => GameServer.Resources.GameData.IdToObjectType.TryGetValue(id, out var t) ? t : (ushort)0;

        // ARENA_ROUND for the waves from..to: their rounds as planned
        private ArenaRoundMessage RoundMessage(byte kind, int from, int to) => new ArenaRoundMessage
        {
            Kind = kind,
            Season = (byte)ArenaSeasons.Current.Number,
            Wave = from,
            LastWave = to,
            Rounds = Enumerable.Range(from, Math.Max(1, to - from + 1)).Select(w => _planner.Round(w))
                .Select(r => (r.Wave, r.Title, (byte)r.Format, (byte)r.Twist, r.AllBosses.Distinct().Select(TypeOf).Where(t => t != 0).ToArray())).ToArray(),
        };

        // ARENA_ROUND with one boss (an Empowered one rising, a boss rush's next)
        private void SendBoss(byte kind, Enemy boss)
        {
            var message = new ArenaRoundMessage
            {
                Kind = kind,
                Season = (byte)ArenaSeasons.Current.Number,
                Wave = _fightFrom,
                LastWave = Wave,
                Rounds = new[] { (Wave, boss.ObjectDesc.DisplayId ?? boss.ObjectDesc.IdName, (byte)0, (byte)0, new[] { boss.ObjectType }) },
            };
            foreach (var p in Players.Values.Where(p => _voteSubscribers.Contains(p.Id)))
                p.Client?.SendPacket(message);
        }

        // the wave and time box: the clock runs from the end of the countdown
        private void SendWaveInfo(Player p) =>
            ArenaPools.SendWaveInfo(p, Wave, _started ? (int)(DateTime.UtcNow - _firstWave).TotalMilliseconds : 0, _started);

        public override void AddToWorld(Entity entity)
        {
            base.AddToWorld(entity);
            if (entity is Player p)
            {
                SendWaveInfo(p);
                SendVotes(); // one more player: more votes needed
            }
        }

        private bool OutOfBounds(float x, float y) => !_inside.Contains(new IntPoint((int)x, (int)y));

        private bool Blocks(WmapTile tile) =>
            GameServer.Resources.GameData.Tiles[tile.TileId].NoWalk ||
            tile.ObjType != 0 && tile.ObjDesc != null && (tile.ObjDesc.FullOccupy || tile.ObjDesc.OccupySquare || tile.ObjDesc.EnemyOccupySquare);

        // finds the gateways (from each Oryx Door along its wall, up to the next object), the middle
        // (between the doors) and the inside of the ring (from the middle, gateways shut)
        private void FindGates()
        {
            if (_gates != null)
                return;
            var gates = new HashSet<IntPoint>();
            var doors = new List<(IntPoint At, ushort Type)>();
            for (var y = 0; y < Map.Height; y++)
                for (var x = 0; x < Map.Width; x++)
                {
                    var id = Map[x, y].ObjDesc?.IdName;
                    if (Map[x, y].ObjType == 0 || id == null || !id.StartsWith("Oryx Door "))
                        continue;
                    doors.Add((new IntPoint(x, y), Map[x, y].ObjType));
                    // doors 0 and 180 face each other across a gateway in a wall that runs east-west
                    var across = id.EndsWith(" 0") || id.EndsWith(" 180");
                    foreach (var dir in new[] { -1, 1 })
                        for (var i = 1; i <= 5; i++)
                        {
                            var (gx, gy) = across ? (x + dir * i, y) : (x, y + dir * i);
                            if (!Map.Contains(gx, gy) || Map[gx, gy].ObjType != 0 || Blocks(Map[gx, gy]))
                                break;
                            gates.Add(new IntPoint(gx, gy));
                        }
                }
            _gates = gates.ToList();
            _doors = doors;
            _middle = doors.Count == 0
                ? new IntPoint(Map.Width / 2, Map.Height / 2)
                : new IntPoint((int)doors.Average(d => d.At.X), (int)doors.Average(d => d.At.Y));

            _inside = new HashSet<IntPoint> { _middle };
            var todo = new Queue<IntPoint>();
            todo.Enqueue(_middle);
            while (todo.Count > 0)
            {
                var p = todo.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = new IntPoint(p.X + dx, p.Y + dy);
                    if (!Map.Contains(n.X, n.Y) || _inside.Contains(n) || gates.Contains(n) || Blocks(Map[n.X, n.Y]))
                        continue;
                    _inside.Add(n);
                    todo.Enqueue(n);
                }
            }
            var tiles = GameServer.Resources.GameData.Tiles;
            _floor = _inside.Where(p => !tiles[Map[p.X, p.Y].TileId].Damaging && IsPassable(p.X, p.Y, true)).ToList();
            if (_floor.Count == 0)
                _floor.Add(_middle);
            SetGates(false); // the doors were found shut (in the map); the inside, with them shut
        }

        private void SetGates(bool closed)
        {
            var gap = GameServer.Resources.GameData.IdToObjectType[GapObject];
            foreach (var (at, type) in _doors)
                SetObject(at, closed ? type : (ushort)0);
            foreach (var g in _gates)
                SetObject(g, closed ? gap : (ushort)0);
            _closed = closed;
        }

        // A tile's static object. One put down gets a new object id, which players are sent as a new
        // object; one taken off keeps its id until players are sent its drop.
        private void SetObject(IntPoint at, ushort type)
        {
            var tile = Map[at.X, at.Y];
            if (tile.ObjType == type)
                return;
            tile.ObjType = type;
            tile.ObjDesc = type == 0 ? null : GameServer.Resources.GameData.ObjectDescs[type];
            if (type != 0)
                tile.ObjId = GetNextEntityId();
            tile.UpdateCount++;
        }

        // moves a player who isn't inside the ring (outside, or in a gateway) to the nearest floor
        // inside it; once every 2 s at most, while the Goto is on its way
        private void MoveInside(Player p)
        {
            if (!OutOfBounds(p.X, p.Y))
                return;
            var now = _time.TotalElapsedMs;
            if (_movedAt.TryGetValue(p.Id, out var at) && now - at < 2000)
                return;
            _movedAt[p.Id] = now;
            var to = _floor.OrderBy(f => (f.X + 0.5f - p.X) * (f.X + 0.5f - p.X) + (f.Y + 0.5f - p.Y) * (f.Y + 0.5f - p.Y)).First();
            p.TeleportPosition(_time, to.X + 0.5f, to.Y + 0.5f, true);
        }

        private void CloseGates()
        {
            foreach (var p in Players.Values)
                MoveInside(p);
            SetGates(true);
            NotifyAll(0xffff00ff, "The gates are shut!");
        }

        // a random floor tile inside the ring, within SpawnRadius of the middle
        private (float X, float Y) SpawnSpot()
        {
            var near = _floor.Where(f => (f.X - _middle.X) * (f.X - _middle.X) + (f.Y - _middle.Y) * (f.Y - _middle.Y) <= SpawnRadius * SpawnRadius).ToList();
            if (near.Count == 0)
                near = _floor;
            var f = near[Random.Shared.Next(near.Count)];
            return (f.X + 0.5f, f.Y + 0.5f);
        }

        public (float X, float Y) Middle => (_middle.X + 0.5f, _middle.Y + 0.5f);

        // a round's chat line: "Wave 12 has begun: The Snake Pit (Boss Rush, Elite)."
        public static string RoundLine(ArenaRound r)
        {
            var extras = new List<string>();
            if (r.Format != ArenaFormat.Standard && r.Format != ArenaFormat.Showdown)
                extras.Add(FormatName(r.Format));
            if (r.Twist != ArenaTwist.None)
                extras.Add(TwistName(r.Twist));
            return $"Wave {r.Wave} has begun: {r.Title}" + (extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "") + ".";
        }

        public static string FormatName(ArenaFormat f) => f switch
        {
            ArenaFormat.Horde => "Horde",
            ArenaFormat.Clash => "Clash",
            ArenaFormat.BossRush => "Boss Rush",
            ArenaFormat.Showdown => "Showdown",
            ArenaFormat.Throwback => "Throwback",
            _ => "Standard",
        };

        public static string TwistName(ArenaTwist t) => t switch
        {
            ArenaTwist.Elite => "Elite",
            ArenaTwist.Swarm => "Swarm",
            ArenaTwist.Ironclad => "Ironclad",
            ArenaTwist.Frenzy => "Frenzy",
            ArenaTwist.Bounty => "Bounty",
            ArenaTwist.Blessing => "Blessing",
            ArenaTwist.Golden => "Golden",
            _ => "",
        };

        // the fight: `count` waves from Wave on (combined by the votes), all of their monsters at once
        private void SpawnWave(int count)
        {
            _fightFrom = Wave;
            var waves = Enumerable.Range(Wave, Math.Max(1, count)).ToList();
            Wave = waves.Last();
            var rounds = waves.Select(_planner.Round).ToList();
            var bosses = rounds.SelectMany(r => r.Bosses).ToArray();
            _lastRound = rounds.Last().Title;
            foreach (var p in Players.Values)
            {
                foreach (var r in rounds)
                    p.SendInfo(RoundLine(r));
                p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, ArenaPools.StartInvulnerableMs(bosses));
                if (rounds.Any(r => r.Twist == ArenaTwist.Blessing))
                {
                    p.ApplyConditionEffect(ConditionEffectIndex.Damaging, ArenaSeason2.BlessingMs);
                    p.ApplyConditionEffect(ConditionEffectIndex.Berserk, ArenaSeason2.BlessingMs);
                }
            }
            foreach (var p in Players.Values)
                SendWaveInfo(p);
            var message = RoundMessage(ArenaRoundMessage.Begins, _fightFrom, Wave);
            foreach (var p in Players.Values.Where(p => _voteSubscribers.Contains(p.Id)))
                p.Client?.SendPacket(message);

            _golden = rounds.Any(r => r.Twist == ArenaTwist.Golden);
            LootRate = _golden ? 1 : ArenaPools.LootRate;

            foreach (var r in rounds)
            {
                foreach (var name in _planner.Minions(r, Players.Count))
                    SpawnMonster(name, r, false, 1);
                foreach (var name in r.Bosses)
                    SpawnMonster(name, r, true, ArenaSeason2.CapShare(r.Bosses.Count));
                foreach (var name in r.Relay)
                    _relay.Enqueue((name, r));
            }
            _relayBoss = null;
            _relayWaitMs = 0;
            NextRelay(false);

            // an Empowered boss, now and then (or as an admin asked)
            _empoweredNext = _forceEmpowered ?? _planner.Empowered(_fightFrom, _lastEmpoweredWave);
            _forceEmpowered = null;
            if (_empoweredNext != null)
            {
                _empoweredAt = _time.TotalElapsedMs + ArenaSeason2.EmpoweredDelayMs;
                _lastEmpoweredWave = Wave;
                NotifyAll(0xFFFF4FD8, "The souls stir...");
            }
        }

        // a wave's monster, with its round's twist; a boss also with its health cap
        private Enemy SpawnMonster(string name, ArenaRound round, bool boss, double capShare)
        {
            var oryx = ArenaPools.IsOryx(name);
            var (x, y) = oryx ? Middle : SpawnSpot();
            if (Spawn(name, x, y) is not Enemy e)
                return null;
            Wake(e);
            var cap = 0;
            if (boss)
            {
                var scale = 1.0;
                if (round.Legendary)
                {
                    e.ClasifyEnemyJson("legendary");
                    scale = 2;
                }
                else if (round.Twist == ArenaTwist.Elite)
                {
                    e.ClasifyEnemyJson("epic");
                    scale = 1.5;
                }
                // an Oryx's arena version keeps the health it has
                if (!oryx)
                    cap = (int)(round.HealthCap * capShare * scale);
            }
            else if (round.Twist == ArenaTwist.Elite && _planner.NextRandom(3) == 0
                || round.Format == ArenaFormat.Horde && _planner.NextRandom(5) == 0)
                e.ClasifyEnemyJson(round.Format == ArenaFormat.Horde && round.Twist == ArenaTwist.Elite ? "epic" : "rare");
            if (round.Twist == ArenaTwist.Frenzy)
                e.ApplyConditionEffect(ConditionEffectIndex.Speedy, -1);
            var defense = round.Twist == ArenaTwist.Ironclad ? IroncladDefense : 0;
            if (cap > 0 || defense > 0)
                _tuned[e] = (cap, defense, 0);
            return e;
        }

        // Keeps each boss under its cap (at the same share of its health, so its phases come as
        // ever), whatever its behaviour sets; the Ironclad defense goes on once its behaviour has
        // started (which may set its defense).
        private void Tune()
        {
            foreach (var e in _tuned.Keys.ToList())
            {
                if (e.Dead || e.World != this)
                {
                    _tuned.Remove(e);
                    continue;
                }
                var (cap, defense, ticks) = _tuned[e];
                if (cap > 0 && e.MaxHealth > cap)
                {
                    var share = Math.Clamp(e.Health / (double)Math.Max(1, e.MaxHealth), 0, 1);
                    e.MaxHealth = cap;
                    e.Health = Math.Max(1, (int)Math.Round(cap * share));
                }
                if (defense > 0 && ticks == 2)
                    e.Defense += defense;
                _tuned[e] = (cap, defense, ticks + 1);
            }
        }

        // a boss rush's next boss, once the one before has fallen (after a breath)
        private void NextRelay(bool announce)
        {
            if (_relay.Count == 0)
                return;
            var (name, round) = _relay.Dequeue();
            _relayBoss = SpawnMonster(name, round, true, 0.7);
            if (_relayBoss == null)
                return;
            if (announce)
            {
                NotifyAll(0xFFFFB347, "Next challenger!");
                SendBoss(ArenaRoundMessage.NextChallenger, _relayBoss);
            }
        }

        private void UpdateRelay(int dt)
        {
            if (_relay.Count == 0 || _relayBoss != null && !_relayBoss.Dead && _relayBoss.World == this)
                return;
            if (_relayWaitMs <= 0)
            {
                _relayWaitMs = 2500;
                return;
            }
            _relayWaitMs -= dt;
            if (_relayWaitMs <= 0)
                NextRelay(true);
        }

        // the Empowered boss rising, and its fall
        private void UpdateEmpowered()
        {
            if (_empoweredNext != null && _time.TotalElapsedMs >= _empoweredAt)
            {
                var name = _empoweredNext;
                _empoweredNext = null;
                var info = BossResurrection.InfoFor(name);
                if (info == null)
                    return;
                var (x, y) = Middle;
                _empowered = BossResurrection.RaiseAt(this, info, new Position(x, y - 3), 0, "the Arena");
                if (_empowered == null)
                    return;
                SendBoss(ArenaRoundMessage.EmpoweredRises, _empowered);
                GameServer.ChatManager.Arena($"Empowered {info.Name} has risen in the Arena at wave {Wave}! ({string.Join(", ", PlayerNames)})");
            }
            if (_empowered != null && (_empowered.Dead || _empowered.World != this))
            {
                var name = Empowered.DisplayName(_empowered);
                _empowered = null;
                _empoweredSlain++;
                var fame = FameRates.Fame(ArenaSeason2.EmpoweredFame, FameRate);
                foreach (var p in Players.Values)
                {
                    DoomedWorlds.GiveFame(p, fame);
                    DoomedWorlds.Notify(p, 0xFFFF4FD8, $"Empowered slain! +{fame} Fame");
                }
                GameServer.ChatManager.Arena($"{name} has fallen in the Arena to {string.Join(", ", PlayerNames)}!");
            }
        }

        // the fight is over once its monsters are dead and no boss is still to come
        private bool FightOver => !Hostiles.Any() && _relay.Count == 0 && _empoweredNext == null && _empowered == null;

        // a game's end: its run on the season's leaderboard, and a new best announced
        private void Finish()
        {
            if (_tested)
            {
                _participants.Clear();
                return;
            }
            var db = GameServer.Database.Conn;
            var season = ArenaSeasons.Current;
            var seconds = (int)(DateTime.UtcNow - _firstWave).TotalSeconds;
            var fell = _lastRound;
            var solo = _participants.Count == 1;
            var best = Wave >= 10 && DbArenaRuns.RankOf(db, season, Wave, seconds, solo) == 0;
            DbArenaRuns.Add(db, season, Wave, seconds, IdName, _participants, _empoweredSlain, fell);
            if (best)
                GameServer.ChatManager.Arena($"New Season {season.Number} record ({(solo ? "solo" : "group")}): wave {Wave} in {DbArenaRuns.FormatTime(seconds)} by {string.Join(", ", _participants)}!");
            _participants.Clear();
        }

        // the round planned for a wave (/arenatest plan)
        public ArenaRound Planned(int wave) => _planner.Round(wave);

        // /arenatest: the next fight starts at this wave
        public bool ForceWave(int wave)
        {
            if (wave < 1)
                return false;
            _tested = true;
            if (_started)
            {
                if (!_counting)
                    return false;
                Wave = wave;
                _paidWave = wave;
            }
            else
                _forceWave = wave;
            SendVotes();
            return true;
        }

        // /arenatest empowered: the next fight brings this Empowered boss
        public bool ForceEmpowered(string boss)
        {
            if (BossResurrection.InfoFor(boss) == null)
                return false;
            _forceEmpowered = boss;
            _tested = true;
            return true;
        }

        // /arenatest probe: fights each boss in the ring (ArenaProbe), instead of the game
        public void StartProbe(Player admin, IEnumerable<string> bosses, int seconds)
        {
            _probe = new ArenaProbe(this, admin, bosses.ToList(), seconds);
            _tested = true;
            _counting = false;
            _started = true;
        }

        public Entity SpawnForProbe(string name)
        {
            var (x, y) = ArenaPools.IsOryx(name) ? Middle : (Middle.X + 4, Middle.Y);
            var e = Spawn(name, x, y);
            if (e != null)
                Wake(e);
            return e;
        }

        // a monster its dungeon would order awake, woken (ArenaSeason2.Wakes)
        private void Wake(Entity e)
        {
            if (!ArenaSeason2.Wakes.TryGetValue(e.ObjectDesc.IdName, out var name)
                || !GameServer.BehaviorDb.Definitions.TryGetValue(e.ObjectType, out var def))
                return;
            var state = FindState(def.Item1, name);
            if (state != null)
                e.SwitchTo(state);
        }

        private static logic.State FindState(logic.State state, string name)
        {
            if (state.Name == name)
                return state;
            foreach (var child in state.States)
                if (FindState(child, name) is { } found)
                    return found;
            return null;
        }

        public IEnumerable<Enemy> ProbeHostiles => Hostiles;

        // what bosses leave for good in their dungeons, taken away (ArenaSeason2.IsLeftover)
        private void ClearLeftovers()
        {
            foreach (var e in Hostiles.Where(e => ArenaSeason2.IsLeftover(e.ObjectDesc.IdName)).ToList())
                LeaveWorld(e);
        }

        public bool ProbeOutOfBounds(Entity e) => OutOfBounds(e.X, e.Y);

        protected override void UpdateLogic(ref TickTime time)
        {
            _time = time; // for the teleports at the countdown's end, which base.UpdateLogic runs
            FindGates();
            var wasOpen = VotingOpen;
            base.UpdateLogic(ref time);
            PruneVotes();
            ClearLeftovers();
            if (_probe != null)
            {
                _probe.Tick(ref time);
                if (_probe.Done)
                    _probe = null;
                return;
            }
            if (_closed)
                foreach (var p in Players.Values)
                    MoveInside(p); // someone let in after the start (admins, /summon) spawns outside
            if (Players.Count == 0)
            {
                if (_participants.Count > 0)
                    Finish();
                return;
            }

            if (!_started && !_counting)
            {
                GameServer.ChatManager.Arena($"An Arena game (Season {ArenaSeasons.Current.Number}) starts in one minute. Type /arena to join.");
                _counting = true;
                Countdown(60, s => NotifyAll(0xffff00ff, s + " Second" + (s == 1 ? "" : "s")), () =>
                {
                    // a Next vote started the game: the first wave comes without another wait
                    _skipFirstGap = CountdownCut;
                    _started = true;
                    _counting = false;
                    _firstWave = DateTime.UtcNow;
                    if (_forceWave > 0)
                    {
                        // an admin's test: from that wave, with nothing paid or counted before it
                        Wave = _forceWave - 1;
                        _paidWave = Wave;
                        _fightFrom = _forceWave;
                    }
                    CloseGates();
                    foreach (var p in Players.Values)
                        SendWaveInfo(p);
                });
                _nextVotes.Clear();
                SendVotes();
                return;
            }

            if (wasOpen && !VotingOpen)
                SendVotes(); // the votes have closed: the panel goes

            if (!_started || _counting)
                return;

            Tune();
            UpdateRelay(time.ElapsedMsDelta);
            UpdateEmpowered();

            if (FightOver)
            {
                // the waves just beaten, each for quests and achievements (none at the start)
                if (Wave > 0)
                    for (var w = Math.Max(1, _fightFrom); w <= Wave; w++)
                    {
                        var wave = new Shared.quests.QuestEvent(Shared.quests.QuestEvents.ArenaWave) { Value = w, Occurrence = Shared.quests.QuestEvent.NewOccurrence() };
                        foreach (var p in Players.Values)
                            p.Quests?.Event(wave);
                    }
                if (_golden)
                {
                    _golden = false;
                    LootRate = ArenaPools.LootRate;
                }
                Wave++;
                // each wave's fame pot, as one by one: after a combined fight, the waves it took in
                // too; a Bounty wave's twice over
                var pot = 0;
                for (; _paidWave < Wave; _paidWave++)
                {
                    var wavePot = ArenaPools.WavePot(_paidWave + 1, Players.Count, FameRate);
                    if (_paidWave >= 1 && _planner.Round(_paidWave).Twist == ArenaTwist.Bounty)
                        wavePot *= 2;
                    pot += wavePot;
                }
                foreach (var p in Players.Values)
                {
                    // the Hall of Champions' Arena board: more of each wave's fame
                    var mine = (int)Math.Round(pot * (1 + p.Champion.ArenaFame));
                    DoomedWorlds.GiveFame(p, mine);
                    DoomedWorlds.Notify(p, 0xFFFF6600, "+" + mine + " Fame");
                }
                var ms = 6000;
                foreach (var p in Players.Values)
                {
                    p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, ms);
                    p.ApplyConditionEffect(ConditionEffectIndex.Healing, ms);
                    if (!_participants.Contains(p.Name))
                        _participants.Add(p.Name);
                }
                _counting = true;
                _nextVotes.Clear();
                var gap = _skipFirstGap ? 3 : ArenaPools.WaveGapSeconds(Wave);
                _skipFirstGap = false;
                Countdown(gap, s => NotifyAll(0xffff00ff, (s == gap ? "Wave " + Wave + " - " : "") + s + " Second" + (s == 1 ? "" : "s")), () =>
                {
                    var count = Combine;
                    _counting = false;
                    SpawnWave(count);
                });
                SendVotes();
            }
            else
                foreach (var e in Hostiles.Where(e => OutOfBounds(e.X, e.Y)).ToList())
                    LeaveWorld(e);
        }
    }

    // /arenatest probe: each boss in turn, in an Arena that hasn't started, struck for a share of its
    // health every half second (as players would, so invulnerable phases hold) with everything it
    // spawned, until they're all dead or the time runs out. What happened goes to the admin and the
    // log: how long it took, its longest untouchable stretch, the most monsters at once, and what was
    // left, out of the ring, or still standing.
    public sealed class ArenaProbe
    {
        private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
        private readonly DoomedBattleArena _arena;
        private readonly Player _admin;
        private readonly List<string> _bosses;
        private readonly int _limitMs;
        private int _index = -1;
        private int _elapsed, _strikeIn, _invulnMs, _invulnMax, _peak, _wait;
        private Entity _boss;
        private int _bossMax;
        private readonly List<string> _results = new List<string>();
        public bool Done { get; private set; }

        // a hit for this share of a monster's max health every half second: 20 s for one without tricks
        private const double Share = 0.025;

        public ArenaProbe(DoomedBattleArena arena, Player admin, List<string> bosses, int seconds)
        {
            _arena = arena;
            _admin = admin;
            _bosses = bosses;
            _limitMs = Math.Max(10, seconds) * 1000;
        }

        private void Report(string line)
        {
            Log.Info("ArenaProbe: " + line);
            _results.Add(line);
            _admin.SendInfo(line);
        }

        public void Tick(ref TickTime time)
        {
            if (Done)
                return;
            if (_admin.World != _arena)
            {
                Done = true;
                Log.Info("ArenaProbe: stopped, the admin left.");
                return;
            }
            var dt = time.ElapsedMsDelta;
            if (_index < 0 || _boss == null)
            {
                // between bosses: the ring cleared, then the next
                _wait -= dt;
                if (_wait > 0)
                    return;
                foreach (var e in _arena.ProbeHostiles.ToList())
                    _arena.LeaveWorld(e);
                _index++;
                if (_index >= _bosses.Count)
                {
                    Done = true;
                    Report($"Probe done: {_bosses.Count} bosses.");
                    return;
                }
                var (mx, my) = _arena.Middle;
                _admin.TeleportPosition(time, mx, my + 2, true);
                _boss = _arena.SpawnForProbe(_bosses[_index]);
                if (_boss == null)
                {
                    Report($"{_bosses[_index]} | NO OBJECT");
                    _wait = 500;
                    return;
                }
                _bossMax = (_boss as Enemy)?.MaxHealth ?? 0;
                (_elapsed, _strikeIn, _invulnMs, _invulnMax, _peak) = (0, 500, 0, 0, 1);
                return;
            }

            _elapsed += dt;
            var hostiles = _arena.ProbeHostiles.ToList();
            _peak = Math.Max(_peak, hostiles.Count);
            var boss = _boss as Enemy;
            if (boss != null && !boss.Dead && boss.World == _arena)
            {
                _bossMax = Math.Max(_bossMax, boss.MaxHealth);
                var untouchable = boss.HasConditionEffect(ConditionEffectIndex.Invulnerable) || boss.HasConditionEffect(ConditionEffectIndex.Invincible)
                    || boss.HasConditionEffect(ConditionEffectIndex.Stasis) || boss.HasConditionEffect(ConditionEffectIndex.Paused);
                _invulnMs = untouchable ? _invulnMs + dt : 0;
                _invulnMax = Math.Max(_invulnMax, _invulnMs);
            }
            _strikeIn -= dt;
            if (_strikeIn <= 0)
            {
                _strikeIn += 500;
                foreach (var e in hostiles)
                    if (!e.Dead && e.MaxHealth > 0)
                        e.Damage(_admin, ref time, Math.Max(10, (int)(e.MaxHealth * Share)), true);
            }
            var outside = hostiles.Where(_arena.ProbeOutOfBounds).Select(e => e.ObjectDesc.IdName).Distinct().ToList();
            if (hostiles.Count == 0)
            {
                Report($"{_bosses[_index]} | cleared in {_elapsed / 1000.0:0.0} s | hp {_bossMax} | untouchable up to {_invulnMax / 1000.0:0.0} s | peak {_peak}");
                _boss = null;
                _wait = 1500;
            }
            else if (_elapsed >= _limitMs)
            {
                var left = string.Join(", ", hostiles.GroupBy(e => e.ObjectDesc.IdName).Select(g =>
                {
                    var e = g.First();
                    var flags = (e.HasConditionEffect(ConditionEffectIndex.Invulnerable) ? " inv" : "") + (e.HasConditionEffect(ConditionEffectIndex.Invincible) ? " invc" : "")
                        + (e.HasConditionEffect(ConditionEffectIndex.Stasis) ? " stasis" : "") + (_arena.ProbeOutOfBounds(e) ? " OUT" : "");
                    return $"{g.Key} x{g.Count()} ({e.Health}/{e.MaxHealth}{flags})";
                }));
                Report($"{_bosses[_index]} | TIMEOUT {_limitMs / 1000} s | hp {_bossMax} | untouchable up to {_invulnMax / 1000.0:0.0} s | peak {_peak} | left: {left}" + (outside.Count > 0 ? " | out: " + string.Join(", ", outside) : ""));
                _boss = null;
                _wait = 1500;
            }
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

        private const string KeysShop = "keys";

        public static IEnumerable<string> ShopNames =>DoomedEconomyData.ShopItems.Select(i => i.Shop).Distinct();

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
            // the keys shop sells the Bazaar's keys at its prices (MerchantLists.KeyShopPrices), not
            // the few Doomed Realms' key shop had
            var entries = ShopName == KeysShop
                ? MerchantLists.KeyShopPrices.Select(k => (Shop: KeysShop, Item: k.Key, k.Price, Currency: CurrencyType.Fame)).ToList()
                : DoomedEconomyData.ShopItems.Where(i => i.Shop == ShopName && !SealedDungeons.IsSealedItem(i.Item)).ToList();
            _currency = entries.Count > 0 ? entries[0].Currency : CurrencyType.Fame;
            foreach (var e in entries)
                if (data.IdToObjectType.TryGetValue(e.Item, out var type) && data.Items.ContainsKey(type))
                {
                    var item = new ShopItem(e.Item, (ushort)Math.Min(e.Price, ushort.MaxValue));
                    item.SetItem(type);
                    _items.Add(item);
                }
            // more items than stalls (the keys): each stall shows its item for a few minutes, as
            // the Bazaar's do, so all of them come round
            var rotates = _items.Count > _inactive.Count;
            foreach (var m in _inactive)
                m.Rotates = rotates;
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
