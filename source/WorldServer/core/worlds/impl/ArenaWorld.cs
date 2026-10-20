using Shared.resources;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.objects.inventory;
using WorldServer.core.objects.vendors;
using WorldServer.core.structures;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds.impl
{
    public sealed class ArenaWorld : World
    {
        public enum ArenaState
        {
            NotStarted,
            CountDown,
            Start,
            Rest,
            Spawn,
            Fight,
            Over
        }

        private enum CountDownState
        {
            NotifyMinute,
            NotifyThirty,
            StartGame,
            Done
        }

        private readonly Dictionary<int, string[]> _waveRewards = new Dictionary<int, string[]>
        {
            { 5, new string[] {"Spider Den Key", "Pirate Cave Key", "Forest Maze Key"} },
            { 10, new string[] {"Snake Pit Key", "Sprite World Key", "Undead Lair Key", "Abyss of Demons Key", "Lab Key", "Beachzone Key"} },
            { 20, new string[] {"Bella's Key", "Tomb of the Ancients Key", "Shaitan's Key", "The Crawling Depths Key", "Candy Key"} },
            { 25, new string[] {"Loot Drop Potion" } },
            { 30, new string[] {"Jeebs' Arena Key", "Shatters Key", "Asylum Key"}},
            { 50, new string[] {"Backpack"} }
        };

        private CountDownState _countDown;
        public ArenaState CurrentState { get; private set; }
        public static ArenaWorld Instance { get; private set; }

        private List<IntPoint> _outerSpawn;
        private List<IntPoint> _centralSpawn;

        private int _wave;
        private long _restTime;
        private long _time;
        private int _startingPlayers;
        private readonly HashSet<string> _participants = new HashSet<string>(); // for the leaderboard
        private bool _lastOneStanding = false;
        private bool _reset = false;
        private bool _goUsed = false; // "GO" cuts a countdown to 3 seconds, once per countdown

        // seconds between waves: 5, then 10 more every 10 waves, at most 20
        public static int WaveGapSeconds(int wave) => Math.Min(20, 5 + 10 * (wave / 10));

        public ArenaWorld(GameServer gameServer, int id, WorldResource resource)
            : base(gameServer, id, resource)
        {
            CurrentState = ArenaState.NotStarted;
            _wave = 1;
            Instance = this;
        }

        public override void Init()
        {
            base.Init();
            AddSpawns();
        }

        private void AddSpawns()
        {
            _outerSpawn = new List<IntPoint>();
            _centralSpawn = new List<IntPoint>();
            var w = Map.Width;
            var h = Map.Height;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (Map[x, y].Region == TileRegion.Arena_Central_Spawn)
                        _centralSpawn.Add(new IntPoint(x, y));

                    if (Map[x, y].Region == TileRegion.Arena_Edge_Spawn)
                        _outerSpawn.Add(new IntPoint(x, y));
                }
        }

        public override bool AllowedAccess(Client client)
        {
            return base.AllowedAccess(client) &&
                  _countDown != CountDownState.Done || client.Account.Admin;
        }

        public override void AddToWorld(Entity entity)
        {
            base.AddToWorld(entity);
            if (entity is Player plr)
                plr.Client.SendPacket(new ImminentArenaWave((int)_time, _wave, (int)CurrentState));
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            base.UpdateLogic(ref time);
            if (CurrentState == ArenaState.Over && !_reset)
                Over(time);
            if (Players.Count <= 0)
                return;
            _time += time.ElapsedMsDelta;
            switch (CurrentState)
            {
                case ArenaState.NotStarted:
                    CurrentState = ArenaState.CountDown;
                    break;
                case ArenaState.CountDown:
                    Countdown(time);
                    break;
                case ArenaState.Start:
                    Start(time);
                    break;
                case ArenaState.Rest:
                    Rest(time);
                    break;
                case ArenaState.Spawn:
                    Spawn(time);
                    break;
                case ArenaState.Fight:
                    Fight(time);
                    break;
                case ArenaState.Over:
                    break;
                default:
                    CurrentState = ArenaState.Start;
                    break;
            }
        }

        private void Over(TickTime time)
        {
            foreach (Enemy en in Enemies.Values)
                LeaveWorld(en);

            _reset = true;
            // the run goes on the arena leaderboard (DbArenaRuns): the wave reached, the time since
            // the game started (_time is reset then)
            if (_startingPlayers > 0 && _participants.Count > 0)
                doomed.DoomedWorlds.AddArenaScore(GameServer, _wave, TimeSpan.FromMilliseconds(_time), "Arena", _participants);
            _participants.Clear();
            _time = 0;
            _restTime = 0;
            _wave = 1;
            _startingPlayers = 0;
            _countDown = CountDownState.NotifyMinute;
            _goUsed = false;
            CurrentState = ArenaState.NotStarted;
        }

        public override void ChatReceived(Player player, string text)
        {
            base.ChatReceived(player, text);
            if (!text.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase) || _goUsed)
                return;

            if (CurrentState == ArenaState.CountDown && _countDown != CountDownState.Done && _time < 57000)
            {
                _time = 57000;
                _countDown = CountDownState.StartGame; // past the minute and 30-second notices
            }
            else if (CurrentState == ArenaState.Rest && _time - _restTime < WaveGapSeconds(_wave) * 1000 - 3000)
                _restTime = _time - (WaveGapSeconds(_wave) * 1000 - 3000);
            else
                return;

            _goUsed = true;
            foreach (var plr in Players.Values)
                plr.SendInfo($"{player.Name} said GO: starting in 3 seconds.");
        }

        private void Countdown(TickTime time)
        {
            switch (_countDown)
            {
                case CountDownState.NotifyMinute:
                    _countDown = CountDownState.NotifyThirty;
                    GameServer.ChatManager.Arena("A public arena game is starting in one minute. Type /arena to join.");
                    break;
                case CountDownState.NotifyThirty:
                    if (_time < 30000)
                        return;
                    _countDown = CountDownState.StartGame;
                    foreach (var plr in Players.Values)
                        plr.SendInfo("The arena will start in 30 seconds.");
                    break;
                case CountDownState.StartGame:
                    if (_time < 60000)
                        return;
                    _countDown = CountDownState.Done;
                    _time = 0;
                    _restTime = 0;
                    _reset = false;
                    _startingPlayers = Players.Count;
                    _participants.UnionWith(Players.Values.Select(p => p.Name));
                    CurrentState = ArenaState.Start;
                    Broadcast(new ImminentArenaWave((int)_time, _wave, (int)CurrentState));
                    break;
                case CountDownState.Done:
                    break;
            }
        }

        private void Start(TickTime time)
        {
            CurrentState = ArenaState.Rest;
            Rest(time, true);
        }

        private void Fight(TickTime time)
        {
            if (Players.Count <= 1 && _startingPlayers > 1 && !_lastOneStanding)
            {
                _lastOneStanding = true;
                var plr = Players.FirstOrDefault().Value;
                if (plr != null && _startingPlayers > 1)
                    GameServer.ChatManager.ServerAnnounce($"Congrats, {plr.Name} for being the last survivor in the arena! (out of: {_startingPlayers} people)");
            }

            if (!Enemies.Any(e => e.Value.ObjectDesc.Enemy))
            {
                _wave++;
                _restTime = _time;
                CurrentState = ArenaState.Rest;
                Rest(time, true);
            }
        }

        private void HandleWaveRewards()
        {
            if (!_waveRewards.ContainsKey(_wave))
                return;

            // initialize reward items
            var gameData = GameServer.Resources.GameData;
            var items = new List<Item>();
            foreach (var reward in _waveRewards[_wave])
            {
                ushort itemType;
                Item item = null;

                if (!gameData.IdToObjectType.TryGetValue(reward, out itemType))
                    continue;

                if (!gameData.Items.TryGetValue(itemType, out item))
                    continue;

                items.Add(item);
            }

            if (items.Count <= 0)
                return;

            var r = new Random();
            foreach (var player in Players.Values)
            {
                var item = items[r.Next(0, items.Count)];
                var changes = player.Inventory.CreateTransaction();
                var slot = changes.GetAvailableInventorySlot(item);
                if (slot != -1)
                {
                    changes[slot] = item;
                    Inventory.Execute(changes);
                }
                else
                {
                    player.SendError("You were unable to get an award from the Arena as your inventory was full.");
                }
            }
        }

        private void Rest(TickTime time, bool recover = false)
        {
            if (recover)
            {
                _participants.UnionWith(Players.Values.Select(p => p.Name));
                foreach (var plr in Players.Values)
                    plr.ApplyConditionEffect(ConditionEffectIndex.Healing, 5000);
                _goUsed = false;
                foreach (var plr in Players.Values)
                    plr.SendInfo($"Wave {_wave} starts in {WaveGapSeconds(_wave)} seconds. Type GO to start in 3.");
                Broadcast(new ImminentArenaWave((int)_time, _wave, (int)CurrentState));
                HandleWaveRewards();
                return;
            }
            if (_time - _restTime < WaveGapSeconds(_wave) * 1000)
                return;
            CurrentState = ArenaState.Spawn;
        }

        private void Spawn(TickTime time)
        {
            foreach (var plr in Players.Values)
            {
                plr.SendInfo(ArenaPools.WaveMessage(_wave));
                plr.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, ArenaPools.WaveStartInvulnerableMs);
            }
            SpawnEnemies();
            SpawnBosses();
            CurrentState = ArenaState.Fight;
        }

        // the wave's monsters (ArenaPools): minions at the edge, the bosses in the middle
        private void SpawnEnemies()
        {
            foreach (var name in ArenaPools.Minions(_wave, Players.Count))
                SpawnAt(name, _outerSpawn);
        }

        private void SpawnBosses()
        {
            foreach (var name in ArenaPools.Bosses(_wave))
                SpawnAt(name, _centralSpawn);
        }

        private void SpawnAt(string name, List<IntPoint> points)
        {
            if (!GameServer.Resources.GameData.IdToObjectType.TryGetValue(name, out var id) || points.Count == 0)
                return;
            var pos = points[Random.Shared.Next(points.Count)];
            var enemy = Entity.Resolve(GameServer, id);
            enemy.Move(pos.X + 0.5f, pos.Y + 0.5f);
            EnterWorld(enemy);
        }

        public override void LeaveWorld(Entity entity)
        {
            base.LeaveWorld(entity);
            if (!(entity is Player)) return;
            if (_countDown == CountDownState.Done && Players.Count <= 1)
                CurrentState = ArenaState.Over;
        }
    }
}
