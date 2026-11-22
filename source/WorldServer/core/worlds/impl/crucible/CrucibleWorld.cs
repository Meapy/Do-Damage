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
    // The floors are climbed on foot. Nothing above the Threshold is there when the world opens:
    // the five floors and the ways up between them (the map's `Crucible Way` squares, winding from
    // each floor's northern edge to the next one's southern edge) are taken away (`_held`) and
    // come back square by square as the climb gets to them.
    //
    // A climb, floor by floor:
    // - the way up opens (OpenWay; CrucibleMessage.Way, how far along it the leader is, which the
    //   clients' sky follows, cruciblesky.rs): it builds itself ahead of whoever is furthest along
    //   it (`Ahead` steps, a step a few squares), and once it nearly reaches the next floor, that
    //   floor comes together round where the way lands, outwards; behind the last climber the way
    //   falls into the sky, and once everyone is on it, the floor below breaks away (the far side
    //   first). Anyone left on ground that fell is put back on the way; whoever lags `StragglerMs`
    //   behind the first to arrive is brought up, and so is everyone `WayMaxMs` after it opened;
    // - once everyone is on the new floor and it's whole: the floor's title and its champion come
    //   up on screen (Floor); a moment later the champion arrives at the floor's middle (its Heart),
    //   untouchable for its entrance;
    // - the fight: the champion announces its phases (CrucibleCue -> Cue) and orders the floor's
    //   Heart to change the floor;
    // - the champion falls: the floor is quiet again (its Heart back to Idle) and whatever it left
    //   is taken away; everyone healed (Cleared); the Ascension Sigil turns in the middle, and a
    //   moment later the way up opens. Everyone is safe from the clear until the next champion.
    // After the Sovereign on the fifth floor: the victory (Victory), its time announced to everyone,
    // and a portal home. Every climb goes on the Crucible's leaderboard (DbCrucibleRuns). Admins'
    // /crucible floor still jumps (the ascent: CrucibleMessage.Ascend, everyone moved up at once).
    public sealed class CrucibleWorld : DoomedMinigame
    {
        private enum Stage { Lobby, Ascending, Arriving, Fighting, Resting, Walking, Won }

        public const int LobbySeconds = 60;
        public const int AscendMs = 2800;      // an admin's jump on screen; players move halfway through
        public const int ArriveMs = 3000;      // from the floor's title to its champion's arrival
        public const int OpenWayMs = 4000;     // from a floor cleared to its way up opening
        private const int GoSeconds = 3;
        // the ways: how many steps they build at first and ahead of the leader, and at most a tick;
        // how far behind the last climber they fall; how far from its end the next floor starts
        // coming together, how many squares out a tick it spreads
        private const int FirstSteps = 6, Ahead = 9, StepsPerTick = 2, Behind = 7, AssembleFrom = 10;
        private const double SpreadPerTick = 0.9;
        private const int StragglerMs = 20000, WayMaxMs = 150000;

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
            var data = GameServer.Resources.GameData;
            _sky = data.IdToTileType.TryGetValue("Empty", out var sky) ? sky : (ushort)0xFF;
            _crack = data.IdToTileType.TryGetValue("Crumbling Floor", out var crack) ? crack : _sky;
            var way = new HashSet<(int, int)>(Map.Regions.Where(r => r.Value == TileRegion.Crucible_Way).Select(r => (r.Key.X, r.Key.Y)));

            // the platforms: the Threshold (round its Spawn squares) and the five floors (round their Hearts)
            var spawn = Map.Regions.Where(r => r.Value == TileRegion.Spawn).Select(r => r.Key).ToList();
            if (spawn.Count > 0)
                _centres[0] = new Position((float)spawn.Average(q => q.X) + 0.5f, (float)spawn.Average(q => q.Y) + 0.5f);
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
                _centres[f.Number] = _hearts[f.Number].Centre;
                _pristine[f.Number] = Ground((int)heart.X, (int)heart.Y, way);
            }
            foreach (var (n, c) in _centres)
            {
                var reach = n == 0 ? 10 : FloorReach;
                var squares = new HashSet<(int, int)>();
                for (var y = (int)c.Y - reach; y <= (int)c.Y + reach; y++)
                    for (var x = (int)c.X - reach; x <= (int)c.X + reach; x++)
                        if (Map.Contains(x, y) && !way.Contains((x, y)) && (Map[x, y].TileId != _sky || Map[x, y].ObjType != 0))
                            squares.Add((x, y));
                _platforms[n] = squares;
            }

            // the ways: each square to the way up to the floor whose middle is the first north of it,
            // in steps out from the floor below
            var order = _centres.OrderByDescending(c => c.Value.Y).Select(c => c.Key).ToList();
            for (var i = 1; i < order.Count; i++)
            {
                var (from, to) = (order[i - 1], order[i]);
                var (low, high) = (_centres[from].Y, _centres[to].Y);
                var mine = way.Where(q => q.Item2 > high && q.Item2 < low).ToHashSet();
                var w = new Way { From = from, To = to };
                var frontier = mine.Where(q => Beside(q, _platforms[from])).ToList();
                foreach (var q in frontier)
                    w.Step[q] = 0;
                while (frontier.Count > 0)
                {
                    w.Steps.Add(frontier);
                    var next = new List<(int, int)>();
                    foreach (var (x, y) in frontier)
                        foreach (var q in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                            if (mine.Contains(q) && !w.Step.ContainsKey(q))
                            {
                                w.Step[q] = w.Steps.Count;
                                next.Add(q);
                            }
                    frontier = next;
                }
                if (w.Steps.Count == 0 || w.Step.Count != mine.Count)
                {
                    StaticLogger.Instance.Error($"The Crucible: the way up to floor {to} is broken ({w.Step.Count} of {mine.Count} squares reached)");
                    continue;
                }
                w.Mouth = Middle(w.Steps[0]);
                var lands = mine.Where(q => Beside(q, _platforms[to])).ToList();
                w.Landing = lands.Count > 0 ? Middle(lands) : _centres[to];
                _ways[to] = w;
            }

            // nothing above the Threshold until the climb gets to it
            foreach (var q in way.Concat(_platforms.Where(pl => pl.Key > 0).SelectMany(pl => pl.Value)))
                Hold(q.Item1, q.Item2);
        }

        // where each platform's middle is (0: the Threshold), its squares (ground or something on
        // it), the ways up (by the floor they lead to), and the squares taken away until built
        private readonly Dictionary<int, Position> _centres = new Dictionary<int, Position>();
        private readonly Dictionary<int, HashSet<(int X, int Y)>> _platforms = new Dictionary<int, HashSet<(int, int)>>();
        private readonly Dictionary<int, Way> _ways = new Dictionary<int, Way>();
        private readonly Dictionary<(int X, int Y), (ushort Ground, ushort Obj)> _held = new Dictionary<(int, int), (ushort, ushort)>();
        private ushort _sky, _crack;

        private sealed class Way
        {
            public int From, To;
            public readonly List<List<(int X, int Y)>> Steps = new List<List<(int, int)>>();
            public readonly Dictionary<(int X, int Y), int> Step = new Dictionary<(int, int), int>();
            public Position Mouth, Landing;
            public int Head = -1, Fallen = -1;
            public int Last => Steps.Count - 1;
        }

        private static bool Beside((int X, int Y) q, HashSet<(int, int)> squares) =>
            squares.Contains((q.X + 1, q.Y)) || squares.Contains((q.X - 1, q.Y)) || squares.Contains((q.X, q.Y + 1)) || squares.Contains((q.X, q.Y - 1));

        private static Position Middle(List<(int X, int Y)> squares) =>
            new Position((float)squares.Average(q => q.X) + 0.5f, (float)squares.Average(q => q.Y) + 0.5f);

        // each floor's ground as the map has it (not the ways'), put back whenever the floor is quiet
        // again: its painters put back only what they painted, and only where it is still theirs,
        // so this makes sure no lava, tide or broken floor is left over between them
        private readonly Dictionary<int, List<(int X, int Y, ushort Ground)>> _pristine = new Dictionary<int, List<(int, int, ushort)>>();
        private const int FloorReach = 35; // the Apex, the biggest floor, reaches 33 (tools/build_crucible.py, grown)

        private List<(int X, int Y, ushort Ground)> Ground(int cx, int cy, HashSet<(int, int)> way)
        {
            var list = new List<(int, int, ushort)>();
            for (var y = cy - FloorReach; y <= cy + FloorReach; y++)
                for (var x = cx - FloorReach; x <= cx + FloorReach; x++)
                    if (Map.Contains(x, y) && !way.Contains((x, y)))
                        list.Add((x, y, Map[x, y].TileId));
            return list;
        }

        private void RestorePristine(int floor)
        {
            if (!_pristine.TryGetValue(floor, out var ground))
                return;
            var changed = false;
            foreach (var (x, y, id) in ground)
                if (Map[x, y].TileId != id && !_held.ContainsKey((x, y)))
                {
                    SetGround(x, y, id);
                    changed = true;
                }
            if (changed)
                foreach (var p in Players.Values)
                    p.UpdateTiles();
        }

        // ---- squares taken away and built again ----

        // a square taken away (no ground, nothing on it), kept to be built again
        private void Hold(int x, int y)
        {
            var t = Map[x, y];
            if (_held.ContainsKey((x, y)) || t.TileId == _sky && t.ObjType == 0)
                return;
            _held[(x, y)] = (t.TileId, t.ObjType);
            t.TileId = _sky;
            t.ObjType = 0;
            t.UpdateCount++;
            _tilesChanged = true;
        }

        // a square built: its ground and whatever stands on it as the map has them
        private void Build(int x, int y)
        {
            if (!_held.Remove((x, y), out var h))
                return;
            var t = Map[x, y];
            t.TileId = h.Ground;
            if (h.Obj != 0)
            {
                t.ObjType = h.Obj;
                t.ObjId = GetNextEntityId(); // a new object to the clients (its old id was dropped)
            }
            t.UpdateCount++;
            _tilesChanged = true;
        }

        // a square falling into the sky after `delay`: cracking first, then gone (and kept, so an
        // admin's jump can build it again)
        private void Fall(int x, int y, int delay)
        {
            if (_held.ContainsKey((x, y)) || Map[x, y].TileId == _sky)
                return;
            StartNewTimer(Math.Max(1, delay), (w, t) =>
            {
                if (_held.ContainsKey((x, y)) || Map[x, y].TileId == _sky)
                    return;
                SetGround(x, y, _crack);
                _tilesChanged = true;
            });
            var ground = Map[x, y].TileId;
            StartNewTimer(Math.Max(2, delay + 450), (w, t) =>
            {
                if (_held.ContainsKey((x, y)))
                    return;
                var tile = Map[x, y];
                _held[(x, y)] = (ground, tile.ObjType);
                tile.TileId = _sky;
                tile.ObjType = 0; // its object id stays: the clients are told it's gone (Player.GetDrops)
                tile.UpdateCount++;
                _tilesChanged = true;
            });
        }

        private bool _tilesChanged;

        // a whole platform, at once (an admin's jump)
        private void BuildPlatform(int n)
        {
            if (_platforms.TryGetValue(n, out var squares))
                foreach (var (x, y) in squares)
                    Build(x, y);
        }

        // a platform breaking away into the sky, the side furthest from `toward` first, over `ms`
        private void FallPlatform(int n, Position toward, int ms)
        {
            if (!_platforms.TryGetValue(n, out var squares))
                return;
            var far = squares.Select(q => Dist(q, toward)).DefaultIfEmpty(1).Max();
            foreach (var q in squares)
                Fall(q.X, q.Y, (int)((1 - Dist(q, toward) / far) * ms) + Random.Shared.Next(350));
        }

        private static double Dist((int X, int Y) q, Position p) => Math.Sqrt((q.X + 0.5 - p.X) * (q.X + 0.5 - p.X) + (q.Y + 0.5 - p.Y) * (q.Y + 0.5 - p.Y));

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
                        GameServer.ChatManager.Arena("A climb of the Crucible begins in one minute. Its portal stands across Realm 2's vortex from the Arena's.");
                        Countdown(LobbySeconds, s => Send(new CrucibleMessage { Kind = CrucibleMessage.Lobby, Seconds = s }), Begin);
                    }
                    break;
                case Stage.Fighting:
                    if (_champion == null || _champion.World != this || _champion.Dead)
                        FloorCleared();
                    break;
                case Stage.Walking:
                    Walk();
                    break;
            }
            if (_tilesChanged)
            {
                _tilesChanged = false;
                foreach (var p in Players.Values)
                    p.UpdateTiles();
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
            OpenWay(1);
        }

        // ---- the ways up ----

        private Way _way;
        private long _wayOpened, _firstUp;
        private bool _belowFell;
        private int _sentPercent;
        // the floor above coming together: its squares nearest the way's landing first
        private List<((int X, int Y) Q, double D)> _assembly;
        private int _assembled;
        private double _spread;

        private void OpenWay(int floor)
        {
            if (!_ways.TryGetValue(floor, out var w))
            {
                // no way on the map: up as before
                StaticLogger.Instance.Error($"The Crucible: no way up to floor {floor}, ascending instead");
                BuildPlatform(floor);
                Ascend(floor);
                return;
            }
            _stage = Stage.Walking;
            _way = w;
            _wayOpened = _now;
            _firstUp = -1;
            _belowFell = false;
            _sentPercent = -1;
            _assembly = null;
            var spec = CrucibleRules.Floor(floor);
            Send(new CrucibleMessage { Kind = CrucibleMessage.Way, FloorNumber = (byte)floor, Seconds = 0, Text = spec?.Name ?? "" });
            NotifyAll(0xFFFFB03A, $"The way up opens: follow it to {spec?.Name ?? "the next floor"}.");
            Crucible.Shake(this);
            Walk();
        }

        // how far along the way a player is: the step they stand on, -1 short of it (still on the
        // floor below), past its end once on the floor above
        private int Along(Player p, Way w)
        {
            var q = ((int)p.X, (int)p.Y);
            if (w.Step.TryGetValue(q, out var step))
                return step;
            return _platforms.TryGetValue(w.To, out var above) && above.Contains(q) ? w.Last + 1 : -1;
        }

        private void Walk()
        {
            var w = _way;
            var players = Players.Values.ToList();
            int lead = -1, tail = int.MaxValue, up = 0;
            foreach (var p in players)
            {
                var a = Along(p, w);
                lead = Math.Max(lead, a);
                tail = Math.Min(tail, a);
                if (a > w.Last)
                    up++;
                // safe from the clear until the champion arrives
                if (!p.HasConditionEffect(ConditionEffectIndex.Invulnerable))
                    p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, 3000);
            }

            // the way builds itself ahead of whoever is furthest along it
            var want = Math.Min(w.Last, Math.Max(FirstSteps, lead + Ahead));
            for (var k = 0; k < StepsPerTick && w.Head < want; k++)
            {
                w.Head++;
                foreach (var (x, y) in w.Steps[w.Head])
                    Build(x, y);
            }

            // the floor above comes together round where the way meets it
            if (_assembly == null && w.Head >= w.Last - AssembleFrom && _platforms.TryGetValue(w.To, out var above))
            {
                _assembly = above.Select(q => (q, Dist(q, w.Landing))).OrderBy(e => e.Item2).ToList();
                _assembled = 0;
                _spread = 0;
                Crucible.Shake(this);
            }
            if (_assembly != null && _assembled < _assembly.Count)
            {
                _spread += SpreadPerTick;
                while (_assembled < _assembly.Count && _assembly[_assembled].D <= _spread)
                {
                    var (x, y) = _assembly[_assembled++].Q;
                    Build(x, y);
                }
            }
            var whole = _assembly != null && _assembled >= _assembly.Count && w.Head >= w.Last;

            // behind the last of them the way falls into the sky; once they're all on it, the floor below
            if (tail >= 0 && tail != int.MaxValue)
            {
                var fallTo = Math.Min(tail - Behind, w.Head);
                var first = w.Fallen + 1;
                while (w.Fallen < fallTo)
                {
                    w.Fallen++;
                    foreach (var (x, y) in w.Steps[w.Fallen])
                        Fall(x, y, (w.Fallen - first) * 70 + Random.Shared.Next(200));
                }
                if (!_belowFell && tail >= 2)
                {
                    _belowFell = true;
                    if (_sigil?.World == this)
                        LeaveWorld(_sigil);
                    _sigil = null;
                    FallPlatform(w.From, w.Mouth, 2600);
                    Crucible.Shake(this);
                }
            }

            // anyone on ground that fell (or never was) is put back on the way
            foreach (var p in players)
            {
                if (!Map.Contains((int)p.X, (int)p.Y) || Map[(int)p.X, (int)p.Y].TileId != _sky)
                    continue;
                var to = Nearest(p, w);
                if (to != null)
                    p.TeleportPosition(_time, to.Value, true);
            }

            // the sky follows the leader up
            var percent = Math.Clamp((lead + 1) * 100 / (w.Last + 2), 0, 100);
            if (percent >= _sentPercent + 3 || percent == 100 && _sentPercent != 100)
            {
                _sentPercent = percent;
                Send(new CrucibleMessage { Kind = CrucibleMessage.Way, FloorNumber = (byte)w.To, Seconds = percent, Text = CrucibleRules.Floor(w.To)?.Name ?? "" });
            }

            // everyone up and the floor whole: the champion comes; stragglers are brought up
            if (up > 0 && _firstUp < 0)
                _firstUp = _now;
            var late = up > 0 && _now - _firstUp > StragglerMs || _now - _wayOpened > WayMaxMs;
            if (whole && up == players.Count || late)
                Arrived(w, players);
        }

        // the nearest built square on the way or the floor above
        private Position? Nearest(Player p, Way w)
        {
            Position? best = null;
            var bestD = double.MaxValue;
            void Try((int X, int Y) q)
            {
                if (_held.ContainsKey(q) || Map[q.X, q.Y].TileId == _sky || Map[q.X, q.Y].TileId == _crack)
                    return;
                var d = Dist(q, p.Position);
                if (d < bestD)
                    (bestD, best) = (d, new Position(q.X + 0.5f, q.Y + 0.5f));
            }
            for (var s = Math.Max(0, w.Fallen + 1); s <= w.Head; s++)
                foreach (var q in w.Steps[s])
                    Try(q);
            if (best == null && _platforms.TryGetValue(w.To, out var above))
                foreach (var q in above)
                    Try(q);
            return best;
        }

        private void Arrived(Way w, List<Player> players)
        {
            // whatever is left of the way and the floor up at once (if it was hurried)
            while (w.Head < w.Last)
            {
                w.Head++;
                foreach (var (x, y) in w.Steps[w.Head])
                    Build(x, y);
            }
            BuildPlatform(w.To);
            var i = 0;
            foreach (var p in players)
            {
                if (Along(p, w) <= w.Last)
                {
                    var at = new Position(w.Landing.X + (i++ % 3 - 1) * 1.2f, w.Landing.Y - 1.5f);
                    p.TeleportPosition(_time, at, true);
                }
                BringAllies(p, p.Position);
                p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, ArriveMs + 1500);
            }
            // the way falls behind them, from the bottom up
            var first = w.Fallen + 1;
            while (w.Fallen < w.Last)
            {
                w.Fallen++;
                foreach (var (x, y) in w.Steps[w.Fallen])
                    Fall(x, y, (w.Fallen - first) * 40 + Random.Shared.Next(200));
            }
            if (!_belowFell)
            {
                _belowFell = true;
                if (_sigil?.World == this)
                    LeaveWorld(_sigil);
                _sigil = null;
                FallPlatform(w.From, w.Mouth, 2000);
            }
            _way = null;
            _floor = w.To;
            Crucible.Shake(this);
            Arrive(w.To);
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

        // everyone onto the floor's southern half, side by side, facing its middle (9 squares out
        // on the floor as designed, the Orrery's middle ring, grown as the floors are)
        private void MoveOnto(int floor, TickTime time)
        {
            if (!_hearts.TryGetValue(floor, out var h))
                return;
            var players = Players.Values.ToList();
            for (var i = 0; i < players.Count; i++)
            {
                var dx = (i - (players.Count - 1) / 2f) * 1.6f;
                var at = new Position(h.Centre.X + dx, h.Centre.Y + (float)Math.Round(9 * ArenaForms.Grow));
                if (!IsPassable(at.X, at.Y))
                    at = new Position(h.Centre.X, h.Centre.Y + (float)Math.Round(8 * ArenaForms.Grow));
                players[i].TeleportPosition(time, at, true);
                BringAllies(players[i], at);
            }
        }

        // a climber's pet and allies (pets from an item, turrets, decoys: enemies with their
        // AllyOwnerId) come up with them, round where they land, instead of staying on the floor
        // below (where its reset would take them)
        private void BringAllies(Player owner, Position at)
        {
            var allies = Enemies.Values.Where(e => e.AllyOwnerId == owner.Id).Cast<Entity>().ToList();
            if (owner.Pet?.World == this)
                allies.Add(owner.Pet);
            for (var i = 0; i < allies.Count; i++)
            {
                var a = i * 2 * Math.PI / Math.Max(1, allies.Count) + Math.PI / 2;
                var x = at.X + (float)Math.Cos(a) * 1.2f;
                var y = at.Y + (float)Math.Sin(a) * 1.2f;
                if (!IsPassable(x, y))
                    (x, y) = (at.X, at.Y);
                allies[i].Move(x, y);
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
            ResetFloor(floor);
            var secs = Seconds(_floorStart);
            Send(new CrucibleMessage { Kind = CrucibleMessage.Cleared, FloorNumber = (byte)floor, Seconds = secs, Text = spec?.Name ?? "" });
            foreach (var p in Players.Values)
            {
                p.Health = p.Stats[0];
                p.Mana = p.Stats[1];
                p.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, OpenWayMs + 3000);
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
            StartNewTimer(OpenWayMs, (w, t) =>
            {
                if (_stage == Stage.Resting && _floor == floor && Players.Count > 0)
                    OpenWay(floor + 1);
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
        // fight left on it gone
        private void ResetFloor(int floor)
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
            // (but not a climber's own allies: their pets and turrets stay with them)
            foreach (var e in Enemies.Values.Where(e => e.DistTo(h.Centre.X, h.Centre.Y) < 36 && !Climbing(e.AllyOwnerId)).ToList())
                LeaveWorld(e);
            RestorePristine(floor);
        }

        private bool Climbing(int allyOwnerId) => allyOwnerId != -1 && Players.ContainsKey(allyOwnerId);

        private static State Find(State state, string name) =>
            state.Name == name ? state : state.States.Select(s => Find(s, name)).FirstOrDefault(s => s != null);

        private void SetGround(int x, int y, ushort type)
        {
            var tile = Map[x, y];
            if (tile.TileId == type)
                return;
            tile.TileId = type;
            tile.UpdateCount++;
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
            _way = null;
            BuildPlatform(floor);
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
