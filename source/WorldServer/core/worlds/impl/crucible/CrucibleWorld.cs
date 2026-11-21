using System;
using System.Collections.Generic;
using System.Linq;
using Shared.crucible;
using Shared.database.leaderboard;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.logic;
using WorldServer.logic.behaviors;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.worlds.impl.crucible
{
    // The Crucible: the boss-only tower over the Arena (Shared.crucible.CrucibleRules; its map and art
    // from tools/build_crucible.py; its champions in BehaviorDb.Crucible.cs). Open to those who
    // reached round 50 of the Arena or hold a Gilded Realm Pass (CrucibleAccess).
    //
    // One climb at a time per portal, as the Arena: players gather on the Threshold, the landing at
    // the foot of the tower, for a minute (GO in chat cuts it to 3 s), then climb its five floors in
    // order, each a floating platform far above the last with a champion of its own. Nobody joins
    // once the climb has begun (but admins). A champion's lethal hit sends a player back to the
    // Nexus, as in the Arena; once nobody is left, the climb ends where it got to.
    //
    // A climb, floor by floor:
    // - the ascent: the clients' sky rushes past (CrucibleMessage.Ascend, cruciblesky.rs), the
    //   ground shakes, and halfway through everyone is moved onto the next floor, south of its
    //   middle;
    // - the floor's title and its champion come up on screen (Floor); a moment later the champion
    //   arrives at the floor's middle (its Heart), untouchable for its entrance;
    // - the fight: the champion announces its phases (CrucibleCue -> Cue) and orders the floor's
    //   Heart to change the floor;
    // - the champion falls: the floor is quiet again (its Heart back to Idle) and whatever it left
    //   is taken away; everyone healed and safe through the rest (Cleared); the Ascension
    //   Sigil turns in the middle, and after the rest (GO cuts it) everyone is drawn onto it while
    //   the floor's edge breaks away into the sky, and up they go.
    // After the Sovereign on the fifth floor: the victory (Victory), its time announced to everyone,
    // and a portal home. Every climb goes on the Crucible's leaderboard (DbCrucibleRuns).
    public sealed class CrucibleWorld : DoomedMinigame
    {
        private enum Stage { Lobby, Ascending, Arriving, Fighting, Resting, Departing, Won }

        public const int LobbySeconds = 60;
        public const int RestSeconds = 20;
        public const int AscendMs = 2800;      // the warp on screen; players move halfway through
        public const int ArriveMs = 3000;      // from the floor's title to its champion's arrival
        private const int GoSeconds = 3;

        private Stage _stage = Stage.Lobby;
        private bool _counting;
        private int _floor;                   // 0: the Threshold
        private int _cleared;
        private readonly Dictionary<int, (Entity Heart, Position Centre)> _hearts = new Dictionary<int, (Entity, Position)>();
        private Enemy _champion;
        private Entity _sigil;
        private long _now;
        private long _climbStart, _floorStart;
        private readonly List<string> _climbers = new List<string>();
        private bool _recorded;
        private bool _forced; // an admin's /crucible floor: a test, kept off the board and out of the announcements
        private TickTime _time;

        public CrucibleWorld(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent)
        {
            DefeatToNexus = true;
            MaxPlayers = CrucibleRules.MaxClimbers;
        }

        public override bool Joinable => _stage == Stage.Lobby;

        public override bool AllowedAccess(Client client) =>
            base.AllowedAccess(client) && (CrucibleAccess.IsAdmin(client.Account) || Joinable && CrucibleAccess.MayEnter(client, out _));

        public int Floor => _floor;

        public override void Init()
        {
            base.Init();
            foreach (var f in CrucibleRules.Floors)
            {
                // the map's objects are still waiting to be added as Init runs
                var heart = PendingEntities.Concat(StaticObjects.Values).Concat(Enemies.Values)
                    .FirstOrDefault(e => e.ObjectDesc?.IdName == f.Heart);
                if (heart == null)
                {
                    StaticLogger.Instance.Error($"The Crucible: no {f.Heart} on its map");
                    continue;
                }
                _hearts[f.Number] = (heart, new Position((int)heart.X + 0.5f, (int)heart.Y + 0.5f));
                _pristine[f.Number] = Ground((int)heart.X, (int)heart.Y);
            }
        }

        // each floor's ground as the map has it, put back whenever the floor is quiet again: its
        // painters put back only what they painted, and only where it is still theirs, so this
        // makes sure no lava, tide or broken floor is left over between them
        private readonly Dictionary<int, List<(int X, int Y, ushort Ground)>> _pristine = new Dictionary<int, List<(int, int, ushort)>>();
        private const int FloorReach = 18;

        private List<(int X, int Y, ushort Ground)> Ground(int cx, int cy)
        {
            var list = new List<(int, int, ushort)>();
            for (var y = cy - FloorReach; y <= cy + FloorReach; y++)
                for (var x = cx - FloorReach; x <= cx + FloorReach; x++)
                    if (Map.Contains(x, y))
                        list.Add((x, y, Map[x, y].TileId));
            return list;
        }

        private void RestorePristine(int floor)
        {
            if (!_pristine.TryGetValue(floor, out var ground))
                return;
            var changed = false;
            foreach (var (x, y, id) in ground)
                if (Map[x, y].TileId != id)
                {
                    SetGround(x, y, id);
                    changed = true;
                }
            if (changed)
                foreach (var p in Players.Values)
                    p.UpdateTiles();
        }

        // ---- what the clients are told ----

        private void Send(CrucibleMessage m, Player only = null)
        {
            if (only != null)
            {
                only.Client?.SendPacket(m);
                return;
            }
            foreach (var p in Players.Values)
                p.Client?.SendPacket(m);
        }

        private int Seconds(long from) => (int)((_now - from) / 1000);

        private CrucibleMessage StatusFor() => new CrucibleMessage
        {
            Kind = CrucibleMessage.Status,
            FloorNumber = (byte)_floor,
            Champion = _champion?.ObjectType ?? 0,
            Seconds = _floor == 0 ? 0 : Seconds(_climbStart),
            Text = CrucibleRules.Floor(_floor)?.Name ?? "",
        };

        // a champion's phase begins (CrucibleCue)
        public void Cue(Entity champion, byte cue) =>
            Send(new CrucibleMessage
            {
                Kind = CrucibleMessage.Cue,
                FloorNumber = (byte)_floor,
                CueId = cue,
                Champion = champion?.ObjectType ?? 0,
                Text = CrucibleCues.Name(cue),
            });

        public override void AddToWorld(Entity entity)
        {
            base.AddToWorld(entity);
            if (entity is Player p)
            {
                Send(StatusFor(), p);
                if (_stage == Stage.Lobby && !_counting)
                    p.SendInfo("The Crucible: five floors, five champions. The climb begins a minute after the first arrives; say GO to begin sooner. " +
                        "Each climb begun is one of your day's (one a day, two with a Gilded Realm Pass); leaving before it begins costs nothing.");
            }
        }

        public override void ChatReceived(Player player, string text)
        {
            base.ChatReceived(player, text);
            if (!string.Equals(text?.Trim(), "go", StringComparison.OrdinalIgnoreCase))
                return;
            if ((_stage == Stage.Lobby || _stage == Stage.Resting) && CountingDown && CutCountdown(GoSeconds))
                NotifyAll(0xFFFFB03A, $"{player.Name} is ready!");
        }

        // ---- the climb ----

        protected override void UpdateLogic(ref TickTime time)
        {
            _time = time;
            _now += time.ElapsedMsDelta;
            base.UpdateLogic(ref time);

            if (Players.Count == 0)
            {
                if (_stage != Stage.Lobby && !_recorded)
                    Record();
                return;
            }

            switch (_stage)
            {
                case Stage.Lobby:
                    if (!_counting)
                    {
                        _counting = true;
                        GameServer.ChatManager.Arena("A climb of the Crucible begins in one minute. Its portal stands beside the Arena's.");
                        Countdown(LobbySeconds, s => Send(new CrucibleMessage { Kind = CrucibleMessage.Lobby, Seconds = s }), Begin);
                    }
                    break;
                case Stage.Fighting:
                    if (_champion == null || _champion.World != this || _champion.Dead)
                        FloorCleared();
                    break;
            }
        }

        private void Begin()
        {
            _counting = false;
            _climbStart = _now;
            foreach (var p in Players.Values)
            {
                _climbers.Add(p.Name);
                CrucibleAccess.Spend(p); // one of the day's climbs
            }
            StaticLogger.Instance.Info($"The Crucible: a climb begins with {string.Join(", ", _climbers)}");
            Ascend(1);
        }

        private void Ascend(int floor)
        {
            _stage = Stage.Ascending;
            var spec = CrucibleRules.Floor(floor);
            Send(new CrucibleMessage { Kind = CrucibleMessage.Ascend, FloorNumber = (byte)floor, Text = spec?.Name ?? "" });
            Crucible.Shake(this);
            foreach (var p in Players.Values)
                p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, AscendMs + ArriveMs + 1500);
            StartNewTimer(AscendMs / 2, (w, t) =>
            {
                var from = _floor;
                _floor = floor;
                MoveOnto(floor, t);
                if (from > 0)
                    ResetFloor(from);
            });
            StartNewTimer(AscendMs, (w, t) => Arrive(floor));
        }

        // everyone onto the floor's southern half, side by side, facing its middle
        private void MoveOnto(int floor, TickTime time)
        {
            if (!_hearts.TryGetValue(floor, out var h))
                return;
            var players = Players.Values.ToList();
            for (var i = 0; i < players.Count; i++)
            {
                var dx = (i - (players.Count - 1) / 2f) * 1.6f;
                var at = new Position(h.Centre.X + dx, h.Centre.Y + 9);
                if (!IsPassable(at.X, at.Y))
                    at = new Position(h.Centre.X, h.Centre.Y + 8);
                players[i].TeleportPosition(time, at, true);
            }
        }

        private void Arrive(int floor)
        {
            _stage = Stage.Arriving;
            _floorStart = _now;
            var spec = CrucibleRules.Floor(floor);
            if (spec == null || !_hearts.TryGetValue(floor, out var h))
            {
                StaticLogger.Instance.Error($"The Crucible: no floor {floor}");
                return;
            }
            var type = GameServer.Resources.GameData.IdToObjectType.TryGetValue(spec.Champion, out var t) ? t : (ushort)0;
            Send(new CrucibleMessage { Kind = CrucibleMessage.Floor, FloorNumber = (byte)floor, Champion = type, Seconds = Seconds(_climbStart), Text = spec.Name });
            StartNewTimer(ArriveMs, (w, tt) =>
            {
                if (Players.Count == 0)
                    return;
                _champion = Spawn(spec.Champion, h.Centre.X, h.Centre.Y) as Enemy;
                if (_champion == null)
                {
                    StaticLogger.Instance.Error($"The Crucible: couldn't spawn {spec.Champion}");
                    return;
                }
                Crucible.Shake(this);
                _stage = Stage.Fighting;
            });
        }

        private void FloorCleared()
        {
            var floor = _floor;
            var spec = CrucibleRules.Floor(floor);
            _cleared = floor;
            _champion = null;
            ResetFloor(floor, clearOnly: true);
            var secs = Seconds(_floorStart);
            Send(new CrucibleMessage { Kind = CrucibleMessage.Cleared, FloorNumber = (byte)floor, Seconds = secs, Text = spec?.Name ?? "" });
            foreach (var p in Players.Values)
            {
                p.Health = p.Stats[0];
                p.Mana = p.Stats[1];
                p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, (RestSeconds + 6) * 1000);
            }
            StaticLogger.Instance.Info($"The Crucible: floor {floor} cleared in {secs} s by {string.Join(", ", Players.Values.Select(p => p.Name))}");

            if (floor >= CrucibleRules.Floors.Count)
            {
                Won();
                return;
            }

            _stage = Stage.Resting;
            if (_hearts.TryGetValue(floor, out var h))
                _sigil = Spawn("Ascension Sigil", h.Centre.X, h.Centre.Y);
            Countdown(RestSeconds, s => Send(new CrucibleMessage { Kind = CrucibleMessage.Rest, FloorNumber = (byte)floor, Seconds = s }), () => Depart(floor));
        }

        // everyone onto the sigil, the floor's edge breaking away round them, then up
        private void Depart(int floor)
        {
            _stage = Stage.Departing;
            if (_hearts.TryGetValue(floor, out var h))
            {
                foreach (var p in Players.Values)
                {
                    var a = Random.Shared.NextDouble() * Math.PI * 2;
                    var d = (float)(Random.Shared.NextDouble() * 1.8);
                    p.TeleportPosition(_time, new Position(h.Centre.X + (float)Math.Cos(a) * d, h.Centre.Y + (float)Math.Sin(a) * d), true);
                }
                Break(h.Centre, 3.5, 1900);
            }
            Crucible.Shake(this);
            StartNewTimer(2100, (w, t) =>
            {
                if (_sigil?.World == this)
                    LeaveWorld(_sigil);
                _sigil = null;
                Ascend(floor + 1);
            });
        }

        private void Won()
        {
            _stage = Stage.Won;
            var secs = Seconds(_climbStart);
            Send(new CrucibleMessage { Kind = CrucibleMessage.Victory, FloorNumber = (byte)_floor, Seconds = secs, Text = "The Crucible is conquered" });
            Crucible.Shake(this);
            var names = string.Join(", ", Players.Values.Select(p => p.Name));
            if (!_forced)
                GameServer.ChatManager.Arena($"{names} conquered the Crucible in {DbArenaRuns.FormatTime(secs)}!");
            Record();
            if (_hearts.TryGetValue(_floor, out var h))
                StartNewTimer(4000, (w, t) => Spawn("Nexus Portal", h.Centre.X, h.Centre.Y + 3));
        }

        private void Record()
        {
            if (_recorded || _climbers.Count == 0)
                return;
            _recorded = true;
            var secs = Seconds(_climbStart);
            try
            {
                var db = GameServer.Database.Conn;
                // a climb that cleared nothing isn't one for the board
                var best = _cleared > 0 && DbCrucibleRuns.RankOf(db, _cleared, secs) == 0;
                if (_cleared > 0)
                    DbCrucibleRuns.Add(db, _cleared, secs, _climbers);
                if (best)
                    GameServer.ChatManager.Arena($"New Crucible record: {(_cleared >= CrucibleRules.Floors.Count ? "all five floors" : $"floor {_cleared}")} in {DbArenaRuns.FormatTime(secs)} by {string.Join(", ", _climbers)}!");
            }
            catch (Exception e)
            {
                StaticLogger.Instance.Error($"The Crucible: recording the climb failed: {e.Message}");
            }
            StaticLogger.Instance.Info($"The Crucible: a climb ended at floor {_cleared} after {secs} s ({string.Join(", ", _climbers)})");
        }

        // ---- the floors ----

        // a floor quiet again: its Heart back to Idle (which puts its floor back), and whatever the
        // fight left on it gone; with clearOnly, the floor itself stays as it is (still whole)
        private void ResetFloor(int floor, bool clearOnly = false)
        {
            if (!_hearts.TryGetValue(floor, out var h))
                return;
            if (GameServer.BehaviorDb.Definitions.TryGetValue(h.Heart.ObjectType, out var def))
            {
                var idle = Find(def.Item1, "Idle");
                if (idle != null && h.Heart.CurrentState != null && !h.Heart.CurrentState.Is(idle))
                {
                    h.Heart.SwitchTo(idle);
                    h.Heart.TickState(_time); // now, not next tick: the floor comes back before anyone moves
                }
            }
            foreach (var e in Enemies.Values.Where(e => e.DistTo(h.Centre.X, h.Centre.Y) < 26).ToList())
                LeaveWorld(e);
            if (!clearOnly)
                RestoreBroken();
            RestorePristine(floor);
        }

        private static State Find(State state, string name) =>
            state.Name == name ? state : state.States.Select(s => Find(s, name)).FirstOrDefault(s => s != null);

        // the floor's edge falling away as they leave it: every square further than `keep` from the
        // middle cracks and then falls into the sky, the outermost first, over `ms`; put back once
        // they're gone (the floor below is never seen again, but the map stays whole)
        private readonly List<(int X, int Y, ushort Ground)> _broken = new List<(int, int, ushort)>();

        private void Break(Position centre, double keep, int ms)
        {
            var data = GameServer.Resources.GameData;
            if (!data.IdToTileType.TryGetValue("Crumbling Floor", out var crack) || !data.IdToTileType.TryGetValue("Empty", out var sky))
                return;
            const int reach = FloorReach;
            for (var dy = -reach; dy <= reach; dy++)
                for (var dx = -reach; dx <= reach; dx++)
                {
                    var r = Math.Sqrt(dx * dx + dy * dy);
                    if (r <= keep || r > reach)
                        continue;
                    var (x, y) = ((int)centre.X + dx, (int)centre.Y + dy);
                    if (!Map.Contains(x, y) || !data.Tiles.TryGetValue(Map[x, y].TileId, out var desc) || desc.NoWalk)
                        continue;
                    var ground = Map[x, y].TileId;
                    var at = (int)((reach - r) / (reach - keep) * ms * 0.8) + Random.Shared.Next(250);
                    StartNewTimer(Math.Max(1, at - 450), (w, t) => SetGround(x, y, crack));
                    StartNewTimer(Math.Max(2, at), (w, t) => SetGround(x, y, sky));
                    _broken.Add((x, y, ground));
                }
        }

        private void SetGround(int x, int y, ushort type)
        {
            var tile = Map[x, y];
            if (tile.TileId == type)
                return;
            tile.TileId = type;
            tile.UpdateCount++;
        }

        private void RestoreBroken()
        {
            foreach (var (x, y, ground) in _broken)
                SetGround(x, y, ground);
            _broken.Clear();
        }

        // admins: /crucible floor <n> jumps a climb to that floor (on a test stack)
        public bool ForceFloor(int floor)
        {
            if (floor < 1 || floor > CrucibleRules.Floors.Count || _stage == Stage.Won)
                return false;
            if (_stage == Stage.Lobby)
            {
                _counting = false;
                _climbStart = _now;
                foreach (var p in Players.Values)
                    _climbers.Add(p.Name);
                Countdown(0, null, null);
            }
            _recorded = _forced = true; // a test: kept off the leaderboard
            if (_champion?.World == this)
                LeaveWorld(_champion);
            _champion = null;
            if (_floor > 0)
                ResetFloor(_floor);
            if (_sigil?.World == this)
                LeaveWorld(_sigil);
            _sigil = null;
            Ascend(floor);
            return true;
        }
    }
}
