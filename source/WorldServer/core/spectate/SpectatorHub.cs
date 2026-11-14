using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.objects;
using WorldServer.core.terrain;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.spectate
{
    /// <summary>
    /// One player watching the Scrying Glass: who they watch (an account), and what of that
    /// player's view they have been sent, so each tick sends them only what changed, as the
    /// watched player's own client is sent. The feed state belongs to the watched player's world
    /// thread and is only touched under the spectator's lock.
    /// </summary>
    public sealed class Spectator
    {
        public readonly Client Client;
        // the account watched (0: no one); set on the Nexus's thread, read by the watched one's
        public volatile int Target;
        // picked with the big view's arrows, rather than the glass's own choice
        public bool Picked;
        // the big view is open: in its chat and counted as watching
        public bool Big;

        // what the picture was last built from (the player in their world), and its serial
        public Player FedFrom;
        public ushort Serial;
        public readonly Dictionary<int, byte> Tiles = new Dictionary<int, byte>();
        public readonly HashSet<Entity> Objects = new HashSet<Entity>();
        public readonly HashSet<WmapTile> Statics = new HashSet<WmapTile>();
        public int SightStamp = -1;
        public int Ticks;
        public (byte, byte, ushort, bool) Status = (255, 0, 0, false);
        // reused each tick
        public readonly List<Entity> Gone = new List<Entity>();
        public readonly List<WmapTile> GoneStatics = new List<WmapTile>();

        public Spectator(Client client) => Client = client;
    }

    /// <summary>
    /// The Scrying Glass in the Nexus (tools/build_nexus_islands.py puts it on the spawn deck):
    /// it shows a player out adventuring, a new one picked at random every <see cref="ChannelSeconds"/>,
    /// to everyone in the Nexus whose client asks (SPECTATE 1, while the glass is in view). Its big
    /// view (SPECTATE 2) shows the same, with arrows through everyone who can be watched (3, 4), a
    /// button to stay on the one shown rather than follow the glass's picks (6, and 7 to follow again) and
    /// a chat of its own between those watching (SPECTATE_SAY, SPECTATE_CHAT).
    ///
    /// What a viewer is sent is the watched player's view, built on that player's world thread
    /// after their own update (<see cref="Feed"/>): the tiles, objects and stats their client
    /// gets, with only the stats anyone may see of the player themselves, plus the shots and
    /// effects they're sent (<see cref="Forward"/>), each wrapped in SPECTATE_FEED.
    ///
    /// Who can be watched: players on this world server outside the Nexus, vaults and guild halls
    /// (the Nexus's own when no one is out), not hidden staff, and not those who said
    /// <c>/glass off</c>.
    /// </summary>
    public sealed class SpectatorHub
    {
        private const int ChannelSeconds = 45;
        private const int HistoryLines = 30;
        private const int MaxLine = 200;
        // tiles remembered per viewer (a long walk through a realm): past this they're sent again
        private const int MaxTiles = 200_000;

        private readonly GameServer _server;
        private readonly ConcurrentDictionary<Client, Spectator> _spectators = new ConcurrentDictionary<Client, Spectator>();

        // the Nexus's thread only, from here down
        private List<(int Account, string Name)> _list = new List<(int, string)>();
        private int _channel;
        private long _channelUntil;
        private long _nextRefresh;
        private long _now;
        private readonly List<SpectateChat> _history = new List<SpectateChat>();
        // when each account was last announced as watching: once a minute at most, so opening
        // and closing the view doesn't fill the chat
        private readonly Dictionary<int, long> _announced = new Dictionary<int, long>();
        private const long AnnounceMs = 60_000;
        private readonly Random _random = new Random();

        public SpectatorHub(GameServer server) => _server = server;

        public bool Any => !_spectators.IsEmpty;

        public static bool CanBeShown(Player p)
        {
            var w = p?.World;
            var c = p?.Client;
            if (w == null || c?.Account == null || c.State == ProtocolState.Disconnected || p.IsHidden || c.Account.HideFromGlass)
                return false;
            return !(w is VaultWorld) && w.InstanceType != WorldResourceInstanceType.Guild && w.InstanceType != WorldResourceInstanceType.Test;
        }

        // Everyone who can be watched, by name: those out of the Nexus, else those in it.
        private List<(int, string)> Watchable()
        {
            var away = new List<(int, string)>();
            var home = new List<(int, string)>();
            foreach (var client in _server.ConnectionManager.Clients.Keys)
            {
                var p = client.Player;
                if (!CanBeShown(p))
                    continue;
                (p.World is NexusWorld ? home : away).Add((client.Account.AccountId, p.Name));
            }
            var list = away.Count > 0 ? away : home;
            list.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        private int IndexOf(int account) => _list.FindIndex(e => e.Account == account);

        /// <summary>The main Nexus's tick: the list, the glass's own pick, everyone's target and status.</summary>
        public void Tick(NexusWorld nexus, long totalMs)
        {
            _now = totalMs;
            if (_spectators.IsEmpty || totalMs < _nextRefresh)
                return;
            _nextRefresh = totalMs + 1000;
            Refresh(nexus);
        }

        private void Refresh(NexusWorld nexus)
        {
            _list = Watchable();
            if (_list.Count == 0)
                _channel = 0;
            else if (IndexOf(_channel) < 0 || _now >= _channelUntil)
            {
                // someone else than now, if there is anyone else
                var pick = _random.Next(_list.Count);
                if (_list.Count > 1 && _list[pick].Account == _channel)
                    pick = (pick + 1 + _random.Next(_list.Count - 1)) % _list.Count;
                _channel = _list[pick].Account;
                _channelUntil = _now + ChannelSeconds * 1000;
            }

            var watchers = (ushort)_spectators.Values.Count(s => s.Big);
            foreach (var s in _spectators.Values)
            {
                if (s.Client.State == ProtocolState.Disconnected || s.Client.Player?.World != nexus)
                {
                    Remove(s.Client);
                    continue;
                }
                if (s.Picked && IndexOf(s.Target) < 0)
                {
                    // the one picked has gone: the next one along
                    s.Picked = _list.Count > 0;
                    SetTarget(s, _channel);
                }
                else if (!s.Picked)
                    SetTarget(s, _channel);
                SendStatus(s, watchers);
            }
        }

        private void SendStatus(Spectator s, ushort watchers)
        {
            var i = IndexOf(s.Target);
            var status = ((byte)(i + 1), (byte)Math.Min(255, _list.Count), watchers, s.Picked);
            if (status == s.Status)
                return;
            s.Status = status;
            s.Client.SendPacket(new SpectateStatus { Index = status.Item1, Count = status.Item2, Watchers = watchers, Picked = s.Picked });
        }

        private static void SetTarget(Spectator s, int account)
        {
            lock (s)
            {
                if (s.Target == account)
                    return;
                s.Target = account;
                if (account != 0)
                    return;
                // no one: the picture goes blank at once (a player's own thread starts the next)
                s.Serial++;
                s.FedFrom = null;
                s.Client.SendPacket(new SpectateInfo { Serial = s.Serial, TargetId = 0 });
            }
        }

        /// <summary>A viewer's SPECTATE (on the Nexus's thread).</summary>
        public void Act(Client client, byte action)
        {
            var player = client.Player;
            if (!(player?.World is NexusWorld nexus) || !nexus.IsMain)
                return;
            _spectators.TryGetValue(client, out var s);
            switch (action)
            {
                case 0:
                    Remove(client);
                    return;
                case 1:
                case 2:
                    if (s == null)
                    {
                        s = new Spectator(client);
                        if (!_spectators.TryAdd(client, s))
                            return;
                        _list = Watchable();
                    }
                    if (action == 2 && !s.Big)
                    {
                        s.Big = true;
                        foreach (var line in _history)
                            client.SendPacket(line);
                        var account = client.Account.AccountId;
                        if (!_announced.TryGetValue(account, out var at) || _now - at >= AnnounceMs)
                        {
                            _announced[account] = _now;
                            if (_announced.Count > 500)
                                _announced.Clear();
                            Say(new SpectateChat { Kind = 1, Name = player.Name, Stars = player.Stars, NameColor = NameColor(player) }, keep: false);
                        }
                    }
                    if (!s.Picked)
                        SetTarget(s, IndexOf(_channel) >= 0 ? _channel : _list.FirstOrDefault().Account);
                    break;
                case 3:
                case 4:
                    if (s == null || !s.Big || _list.Count == 0)
                        return;
                    var i = IndexOf(s.Target);
                    var step = action == 3 ? 1 : -1;
                    i = i < 0 ? 0 : ((i + step) % _list.Count + _list.Count) % _list.Count;
                    s.Picked = true;
                    SetTarget(s, _list[i].Account);
                    break;
                case 6:
                    // stay on the one shown: the glass's own picks pass them by
                    if (s == null || !s.Big || s.Target == 0)
                        return;
                    s.Picked = true;
                    break;
                case 7:
                    // let the glass pick again
                    if (s == null)
                        return;
                    s.Picked = false;
                    SetTarget(s, _channel);
                    break;
                case 5:
                    if (s == null)
                        return;
                    s.Big = false;
                    s.Picked = false;
                    SetTarget(s, _channel);
                    break;
                default:
                    return;
            }
            _nextRefresh = 0;
            Refresh(nexus);
        }

        private void Remove(Client client)
        {
            _spectators.TryRemove(client, out _);
        }

        private static int NameColor(Player p) => p.Client.Account.Admin ? 0xF52476 : p.ColorNameChat;

        /// <summary>A line typed in the big view's chat (on the Nexus's thread).</summary>
        public void Chat(Client client, string text, ref TickTime time)
        {
            var player = client.Player;
            if (player == null || !_spectators.TryGetValue(client, out var s) || !s.Big)
                return;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
                return;
            if (text[0] == '/')
            {
                _server.CommandManager.Execute(player, time, text);
                return;
            }
            if (!player.NameChosen)
            {
                player.SendError("Please choose a name before chatting.");
                return;
            }
            if (player.Muted)
            {
                player.SendError("Muted. You can not talk at this time.");
                return;
            }
            if (text.Length > MaxLine)
                text = text.Substring(0, MaxLine);
            if (player.CompareAndCheckSpam(text, time.TotalElapsedMs))
                return;
            StaticLogger.Instance.Info($"[Scrying Glass] {player.Name}: {text}");
            Say(new SpectateChat { Kind = 0, Name = player.Name, Stars = player.Stars, NameColor = NameColor(player), Text = text });
        }

        // to everyone with the big view open; players' lines are kept for those who open it later
        private void Say(SpectateChat line, bool keep = true)
        {
            if (keep)
            {
                _history.Add(line);
                if (_history.Count > HistoryLines)
                    _history.RemoveAt(0);
            }
            foreach (var s in _spectators.Values)
                if (s.Big)
                    s.Client.SendPacket(line);
        }

        /// <summary>
        /// After <paramref name="target"/>'s own update and tick (their world's thread): what each
        /// viewer watching them hasn't been sent yet of what they see, and the tick's moves and stats.
        /// </summary>
        public void Feed(Player target, List<ObjectStats> statuses)
        {
            var watched = false;
            // the tick's moves and stats are the same for everyone watching: built once
            NewTickMessage tick = null;
            foreach (var s in _spectators.Values)
            {
                if (s.Target != target.AccountId)
                    continue;
                watched = true;
                tick ??= Tick(target, statuses);
                lock (s)
                {
                    if (s.Target == target.AccountId && s.Client.State != ProtocolState.Disconnected)
                        Step(s, target, tick);
                }
            }
            target.Spectated = watched;
        }

        // the tick as the player got it, with only the stats anyone may see of the player
        private static NewTickMessage Tick(Player target, List<ObjectStats> statuses)
        {
            var open = target.PublicStats ??= new HashSet<StatDataType>(target.ExportStats(true).Stats.Select(kv => kv.Key));
            var tick = new NewTickMessage(target.TickId, target.TickTime);
            tick.Statuses.Capacity = statuses.Count;
            foreach (var st in statuses)
            {
                if (st.Id != target.Id)
                {
                    tick.Statuses.Add(st);
                    continue;
                }
                tick.Statuses.Add(new ObjectStats(st.Id, st.X, st.Y, st.Stats.Where(kv => open.Contains(kv.Key)).ToArray()));
            }
            return tick;
        }

        private static void Start(Spectator s, Player target)
        {
            var world = target.World;
            s.Serial++;
            s.FedFrom = target;
            s.Tiles.Clear();
            s.Objects.Clear();
            s.Statics.Clear();
            s.SightStamp = -1;
            s.Ticks = 0;
            s.Client.SendPacket(new SpectateInfo
            {
                Serial = s.Serial,
                TargetId = target.Id,
                Name = target.Name,
                World = world.GetDisplayName(),
                Map = world.IdName,
                Size = (short)Math.Max(world.Map.Width, world.Map.Height),
                Background = world.Background,
                ObjectType = target.ObjectType
            });
            if (world.Levels != null)
                s.Client.SendPacket(new SpectateFeed(s.Serial, world.Layers()));
        }

        private static void Step(Spectator s, Player target, NewTickMessage tick)
        {
            var world = target.World;
            if (world == null)
                return;
            if (s.FedFrom != target)
                Start(s, target);
            s.Ticks++;

            var update = new Update();
            if (s.Tiles.Count > MaxTiles)
                s.Tiles.Clear();
            if (s.SightStamp != target.SightStamp)
            {
                s.SightStamp = target.SightStamp;
                var map = world.Map;
                foreach (var point in target.SightTiles)
                {
                    var tile = map[point.X, point.Y];
                    if (!tile.Exists)
                        continue;
                    var key = point.X << 16 | point.Y;
                    var known = s.Tiles.TryGetValue(key, out var count);
                    // the void is only sent over ground sent before (as Player.AddSightTile)
                    if (known ? count >= tile.UpdateCount : tile.TileId == 0xFF)
                        continue;
                    s.Tiles[key] = tile.UpdateCount;
                    update.Tiles.Add(new TileData(point.X, point.Y, tile.TileId));
                }
                // the map's own objects in sight (as Player.GetNewStaticObjects), every floor's
                foreach (var point in target.SightTiles)
                {
                    AddStatic(s, update, map[point.X, point.Y], point.X, point.Y);
                    for (var i = 1; i < (world.Levels?.Count ?? 0); i++)
                        AddStatic(s, update, world.Levels[i][point.X, point.Y], point.X, point.Y);
                }
            }
            // the map's objects broken since (a wall shot down), now and then
            if (s.Ticks % 20 == 0)
            {
                s.GoneStatics.Clear();
                foreach (var t in s.Statics)
                    if (t.ObjType == 0)
                        s.GoneStatics.Add(t);
                foreach (var t in s.GoneStatics)
                {
                    s.Statics.Remove(t);
                    if (t.ObjId != 0)
                        update.Drops.Add(t.ObjId);
                }
            }

            var objects = target.KnownObjects;
            foreach (var e in objects)
                if (!e.Dead && s.Objects.Add(e))
                    update.NewObjs.Add(e.ToDefinition(e is Player));
            s.Gone.Clear();
            foreach (var e in s.Objects)
                if (e.Dead || !objects.Contains(e))
                    s.Gone.Add(e);
            foreach (var e in s.Gone)
            {
                s.Objects.Remove(e);
                update.Drops.Add(e.Id);
            }
            if (update.Tiles.Count > 0 || update.NewObjs.Count > 0 || update.Drops.Count > 0)
                s.Client.SendPacket(new SpectateFeed(s.Serial, update));

            s.Client.SendPacket(new SpectateFeed(s.Serial, tick));
        }

        private static void AddStatic(Spectator s, Update update, WmapTile tile, int x, int y)
        {
            if (tile.Exists && tile.ObjId != 0 && tile.ObjType != 0 && s.Statics.Add(tile))
                update.NewObjs.Add(tile.ToObjectDef(x, y));
        }

        /// <summary>
        /// A message sent to a watched player (their world's thread): the shots and effects among
        /// them go on to those watching, as part of the picture.
        /// </summary>
        public void Forward(Player target, OutgoingMessage message)
        {
            if (!(message is EnemyShootMessage || message is ServerPlayerShoot || message is AllyShootMessage
                || message is GotoMessage || message is ShowEffect || message is AoeMessage))
                return;
            foreach (var s in _spectators.Values)
            {
                if (s.Target != target.AccountId)
                    continue;
                lock (s)
                {
                    if (s.FedFrom == target && s.Target == target.AccountId)
                        s.Client.SendPacket(new SpectateFeed(s.Serial, message));
                }
            }
        }
    }
}
