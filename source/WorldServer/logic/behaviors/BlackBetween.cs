using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // The Black Between's own attacks (BehaviorDb.BlackBetween.cs): telegraphed strikes from the sky,
    // the floor that turns, Aurel's Solar Cyclone and his leap. The telegraphs are ground marks
    // (ShowEffect GroundMark): a circle on the floor filling in from its edge until whatever is
    // coming lands, which clients from 2.4.7 draw; older clients see only the throw and the blast.
    internal static class Strike
    {
        // the client's default throw (ShowEffect Throw with no duration) takes 1.5 s
        public const int ThrowMs = 1500;

        // style (pos2.y): 0 a plain circle filling in; 1 the Mysterious One's rift, opening out
        public static void Mark(Entity host, Position at, double radius, int ms, uint color, int style = 0)
        {
            var effect = new ShowEffect
            {
                EffectType = EffectType.GroundMark,
                TargetObjectId = host.Id,
                Pos1 = at,
                Pos2 = new Position((float)radius, style),
                Color = new ARGB(color),
                Duration = ms,
            };
            host.World.BroadcastIfVisible(effect, ref at);
        }

        public static void Throw(Entity from, Position to, uint color) =>
            from.World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Throw,
                Color = new ARGB(color),
                TargetObjectId = from.Id,
                Pos1 = to,
                Pos2 = new Position(222, 0),
            }, from);

        // the blast where it lands: everyone within `radius` of `at` takes `damage` (the host's
        // damage scale applied) and the effect
        public static void Land(World world, Entity host, Position at, double radius, int damage, uint color,
            ConditionEffectIndex effect = 0, int effectMs = 0)
        {
            if (world == null || host.World != world)
                return;
            var dmg = (int)(damage * ((host as Enemy)?.DamageScale ?? 1));
            world.BroadcastIfVisible(new AoeMessage(at, (float)radius, dmg, 0, 0, host.ObjectType, new ARGB(color)), ref at);
            world.AOE(at, (float)radius, true, p =>
            {
                (p as IPlayer).Damage(dmg, host);
                if (effect != 0 && !p.HasConditionEffect(ConditionEffectIndex.Invincible) && !p.HasConditionEffect(ConditionEffectIndex.Stasis))
                    p.ApplyConditionEffect(new ConditionEffect(effect, effectMs));
            });
        }

        // a bolt of lightning from the host down to `at` (ShowEffect Lightning)
        public static void Bolt(Entity host, Position at, uint color) =>
            host.World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Lightning,
                TargetObjectId = host.Id,
                Pos1 = at,
                Pos2 = new Position(170, 0),
                Color = new ARGB(color),
            }, ref at);

        // a volley of the host's projectile `index`: `count` shots `inc` radians apart, centred on `angle`
        public static void Fire(Entity host, int index, double angle, int count, double inc) =>
            FireFrom(host, host.X, host.Y, index, angle, count, inc);

        // the same from anywhere (Aurel's sword flying in from the arena's edge)
        public static void FireFrom(Entity host, float x, float y, int index, double angle, int count = 1, double inc = 0)
        {
            if (host.World == null || !host.ObjectDesc.Projectiles.TryGetValue(index, out var desc))
                return;
            var dmg = (double)Random.Shared.Next(desc.MinDamage, desc.MaxDamage);
            if (host is Enemy enemy)
                dmg *= enemy.DamageScale;
            host.World.BroadcastEnemyShootIfVisible(new EnemyShootMessage
            {
                Spawned = host.Spawned,
                BulletId = host.GetNextBulletId(count),
                OwnerId = host.Id,
                StartingPos = new Position(x, y),
                Angle = (float)(angle - inc * (count - 1) / 2),
                Damage = (short)dmg,
                BulletType = (byte)desc.BulletType,
                AngleInc = (float)inc,
                NumShots = (byte)count,
                ObjectType = host.ObjectType,
                ProjectileDesc = desc,
            }, host);
        }

        public static bool Walkable(Entity host, double x, double y) =>
            host.World != null && host.World.IsPassable(x, y);

        // moves the host to `to` over `ms` in a few steps, then calls `then` (a jump in one go
        // left clients drawing it oddly: its old sprite, or blown up)
        public static void Glide(Entity host, Position to, int ms, Action then)
        {
            var world = host.World;
            const int steps = 4;
            var (fx, fy) = (host.X, host.Y);
            for (var i = 1; i <= steps; i++)
            {
                var k = i / (float)steps;
                var last = i == steps;
                world.StartNewTimer(ms * i / steps, (w, t) =>
                {
                    if (host.World != world)
                        return;
                    host.Move(fx + (to.X - fx) * k, fy + (to.Y - fy) * k);
                    if (last)
                        then();
                });
            }
        }
    }

    // Starshards, meteors and the Supernova: each strike is marked on the floor, thrown from one of
    // `origins` (the nearest of each name; the host when none is about) and lands when the mark has
    // filled. Where they land, round the host's spawn point:
    // - Diamond: inside the diamond `extent` tiles to each point (Vesper's Fourfold Veil);
    // - Scatter: anywhere within `extent` tiles;
    // - Players: on a player within `extent` tiles of the host;
    // - Host: one great blast round the host itself (no throw).
    // The mark and the blast are `color`; the throw's trail is `throwColor` (the mark's colour if
    // none is given). With `lightning` nothing is thrown: when the mark has filled, a bolt comes
    // down from the host (Aurel's divine lightning). `style` is the mark's (Strike.Mark). With
    // `twoTone` the strikes take `color` and `throwColor` in turn, each trailing the other (the
    // Mysterious One's: his daughter's violet and his son's gold).
    internal class Starfall : Behavior
    {
        public enum Area { Diamond, Scatter, Players, Host }

        private readonly Area _area;
        private readonly double _extent;
        private readonly double _radius;
        private readonly int _damage;
        private readonly int _fillMs;
        private readonly Cooldown _coolDown;
        private readonly int _perVolley;
        private readonly uint _color;
        private readonly ConditionEffectIndex _effect;
        private readonly int _effectMs;
        private readonly string[] _origins;
        private readonly int _coolDownOffset;
        private readonly uint _throwColor;
        private readonly bool _lightning;
        private readonly int _style;
        private readonly bool _twoTone;

        public Starfall(Area area, double extent, double radius, int damage, Cooldown coolDown = new Cooldown(), int perVolley = 1,
            uint color = 0xFFFF3030, ConditionEffectIndex effect = 0, int effectMs = 0, string[] origins = null,
            int fillMs = Strike.ThrowMs, int coolDownOffset = 0, uint throwColor = 0, bool lightning = false, int style = 0,
            bool twoTone = false)
        {
            _twoTone = twoTone;
            _lightning = lightning;
            _style = style;
            _throwColor = throwColor == 0 ? color : throwColor;
            _area = area;
            _extent = extent;
            _radius = radius;
            _damage = damage;
            _coolDown = coolDown.Normalize(300);
            _perVolley = perVolley;
            _color = color;
            _effect = effect;
            _effectMs = effectMs;
            _origins = origins;
            _fillMs = fillMs;
            _coolDownOffset = coolDownOffset;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = _coolDownOffset;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state - time.ElapsedMsDelta;
            if (cool > 0)
            {
                state = cool;
                return;
            }
            state = _coolDown.Next(Random);
            for (var i = 0; i < _perVolley; i++)
                if (Target(host, out var at))
                    Drop(host, at);
        }

        private bool Target(Entity host, out Position at)
        {
            var c = host.SpawnPoint.Value;
            at = default;
            switch (_area)
            {
                case Area.Host:
                    at = new Position(host.X, host.Y);
                    return true;
                case Area.Players:
                    var players = host.World.Players.Values.Where(p => p.DistTo(host) <= _extent && p.IsVisibleToEnemy()).ToList();
                    if (players.Count == 0)
                        return false;
                    var pick = players[Random.Next(players.Count)];
                    at = new Position(pick.X, pick.Y);
                    return true;
                case Area.Diamond:
                    for (var tries = 0; tries < 20; tries++)
                    {
                        var dx = (Random.NextDouble() * 2 - 1) * _extent;
                        var dy = (Random.NextDouble() * 2 - 1) * _extent;
                        if (Math.Abs(dx) + Math.Abs(dy) > _extent - 0.8)
                            continue;
                        at = new Position(c.X + (float)dx, c.Y + (float)dy);
                        return true;
                    }
                    return false;
                default:
                    var a = Random.NextDouble() * 2 * Math.PI;
                    var d = _extent * Math.Sqrt(Random.NextDouble());
                    at = new Position(c.X + (float)(d * Math.Cos(a)), c.Y + (float)(d * Math.Sin(a)));
                    return Strike.Walkable(host, at.X, at.Y);
            }
        }

        private int _turn;

        private void Drop(Entity host, Position at)
        {
            var (color, trail) = _twoTone && (_turn++ & 1) == 1 ? (_throwColor, _color) : (_color, _throwColor);
            Strike.Mark(host, at, _radius, _fillMs, color, _style);
            if (_area != Area.Host && !_lightning)
            {
                Entity from = host;
                if (_origins != null)
                {
                    var all = _origins.SelectMany(n => host.GetNearestEntitiesByName(40, n)).ToList();
                    if (all.Count > 0)
                        from = all[Random.Next(all.Count)];
                }
                Strike.Throw(from, at, trail);
            }
            var world = host.World;
            world.StartNewTimer(_fillMs, (w, t) =>
            {
                if (_lightning && host.World == world)
                    Strike.Bolt(host, at, _color);
                Strike.Land(world, host, at, _radius, _damage, color, _effect, _effectMs);
            });
        }
    }

    // Turns the floor round the host's spawn point to `tileId` where `where(dx, dy)` says so, within
    // `reach` tiles, for as long as the state lasts; then the floor comes back. Walls, grounds nobody
    // walks on and squares a solid object fills are left alone. (Vesper's Veil, as LavaRing floods
    // Oryx 5's temple.)
    internal class PaintFloor : Behavior
    {
        private readonly string _tileId;
        private readonly double _reach;
        private readonly Func<int, int, bool> _where;

        public PaintFloor(string tileId, double reach, Func<int, int, bool> where)
        {
            _tileId = tileId;
            _reach = reach;
            _where = where;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var painted = new List<(int X, int Y, ushort Ground)>();
            state = painted;
            var data = host.GameServer.Resources.GameData;
            if (!data.IdToTileType.TryGetValue(_tileId, out var tileType))
                return;
            var c = host.SpawnPoint.Value;
            var r = (int)Math.Ceiling(_reach);
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= _reach * _reach && _where(dx, dy))
                        Floor.Set(host, (int)c.X + dx, (int)c.Y + dy, tileType, painted);
            Floor.Refresh(host);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is List<(int X, int Y, ushort Ground)> painted)
                Floor.Restore(host, painted);
            state = null;
        }
    }

    internal static class Floor
    {
        // turn (x, y) to `tileType` if it's walkable floor not filled by a solid object, remembering
        // what it was; false when it's left alone
        public static bool Set(Entity host, int x, int y, ushort tileType, List<(int X, int Y, ushort Ground)> painted)
        {
            var map = host.World?.Map;
            var data = host.GameServer.Resources.GameData;
            if (map == null || !map.Contains(x, y))
                return false;
            var tile = map[x, y];
            if (tile.TileId == tileType || !data.Tiles.TryGetValue(tile.TileId, out var desc) || desc.NoWalk)
                return false;
            if (tile.ObjType != 0 && tile.ObjDesc != null && tile.ObjDesc.FullOccupy)
                return false;
            painted?.Add((x, y, tile.TileId));
            tile.TileId = tileType;
            tile.UpdateCount++;
            return true;
        }

        public static void Put(Entity host, int x, int y, ushort tileType)
        {
            var map = host.World?.Map;
            if (map == null || !map.Contains(x, y) || map[x, y].TileId == tileType)
                return;
            var tile = map[x, y];
            tile.TileId = tileType;
            tile.UpdateCount++;
        }

        public static void Restore(Entity host, List<(int X, int Y, ushort Ground)> painted)
        {
            var map = host.World?.Map;
            if (map == null)
                return;
            foreach (var (x, y, ground) in painted)
            {
                var tile = map[x, y];
                tile.TileId = ground;
                tile.UpdateCount++;
            }
            painted.Clear();
            Refresh(host);
        }

        // players work out the tiles under them again only when they step onto another one
        public static void Refresh(Entity host)
        {
            if (host.World == null)
                return;
            foreach (var player in host.World.Players.Values)
                player.UpdateTiles();
        }
    }

    // The Black Between (the Mysterious One at 40%): the platform goes dark. First the path's start
    // lights up (`warnMs`), then every walkable square within `radius` of the host's spawn turns to
    // `voidTile` (it burns) but for the Constellation Path: stars lit `ahead` squares in front of its
    // head and still lit `behind` it, the head moving one square every `stepMs`, winding round the
    // platform. When the state ends the floor comes back.
    internal class ConstellationPath : Behavior
    {
        private readonly string _voidTile;
        private readonly string _pathTile;
        private readonly double _radius;
        private readonly int _warnMs;
        private readonly int _stepMs;
        private readonly int _ahead;
        private readonly int _behind;
        // the circle round the middle the path keeps out of: the Mysterious One stands there through
        // the phase, and a path through him can't be walked
        private readonly double _clear;

        public ConstellationPath(string voidTile, string pathTile, double radius, int warnMs = 2500, int stepMs = 330,
            int ahead = 6, int behind = 5, double clear = 4)
        {
            _clear = clear;
            _voidTile = voidTile;
            _pathTile = pathTile;
            _radius = radius;
            _warnMs = warnMs;
            _stepMs = stepMs;
            _ahead = ahead;
            _behind = behind;
        }

        private sealed class Walk
        {
            public readonly List<(int X, int Y)> Path = new();
            public readonly Dictionary<(int X, int Y), ushort> Before = new();
            public HashSet<(int X, int Y)> Lit = new();
            public int Elapsed;
            public int Head;
            public bool Dark;
            public ushort Void;
            public ushort Star;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var data = host.GameServer.Resources.GameData;
            if (!data.IdToTileType.TryGetValue(_voidTile, out var voidType) || !data.IdToTileType.TryGetValue(_pathTile, out var star))
                return;
            var w = new Walk { Void = voidType, Star = star };
            state = w;
            var c = host.SpawnPoint.Value;
            var (cx, cy) = ((int)c.X, (int)c.Y);
            // the path: a walk with momentum from a point south of the middle, turning now and then,
            // kept in the ring between the clear circle round the middle (where he stands) and the
            // edge; it doesn't cross its own recent steps
            var (x, y) = (cx, cy + (int)(_radius * 0.45));
            var dirs = new[] { (1, 0), (0, 1), (-1, 0), (0, -1) };
            var dir = Random.Next(4);
            var recent = new Queue<(int, int)>();
            for (var i = 0; i < 400; i++)
            {
                w.Path.Add((x, y));
                recent.Enqueue((x, y));
                if (recent.Count > 14)
                    recent.Dequeue();
                var options = new List<int>();
                foreach (var turn in new[] { 0, 0, 0, 1, 3 })
                {
                    var d = (dir + turn) % 4;
                    var (nx, ny) = (x + dirs[d].Item1, y + dirs[d].Item2);
                    if (!InRing(host, cx, cy, nx, ny) || recent.Contains((nx, ny)))
                        continue;
                    options.Add(d);
                }
                // boxed in: any way still in the ring, even back over a recent step (never towards
                // him or off the edge); only if there's none, straight on
                if (options.Count == 0)
                {
                    for (var d = 0; d < 4; d++)
                        if (InRing(host, cx, cy, x + dirs[d].Item1, y + dirs[d].Item2))
                            options.Add(d);
                    // and not straight back the way it came, while there's another way
                    var back = (dir + 2) % 4;
                    if (options.Count > 1)
                        options.Remove(back);
                }
                if (options.Count == 0)
                    options.Add(dir);
                dir = options[Random.Next(options.Count)];
                (x, y) = (x + dirs[dir].Item1, y + dirs[dir].Item2);
            }
            Light(host, w);
            Floor.Refresh(host);
            // everyone on the platform onto the path's start, so no one begins out in the dark
            var (px, py) = w.Path[0];
            foreach (var player in host.World.Players.Values)
                if (player.DistTo(c.X, c.Y) <= _radius + 3)
                    player.TeleportPosition(time, new Position(px + 0.5f + Random.Next(-1, 2), py + 0.5f + Random.Next(-1, 2)), true);
        }

        // a square the path may take: in the ring round the middle, its ground walkable, and not
        // within the clear circle (a square or its neighbours, as the path is lit three wide)
        private bool InRing(Entity host, int cx, int cy, int x, int y)
        {
            var far = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return far <= _radius - 3.5 && far >= _clear + 1 && Strike.Walkable(host, x + 0.5, y + 0.5);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Walk w)
                return;
            w.Elapsed += time.ElapsedMsDelta;
            if (!w.Dark)
            {
                if (w.Elapsed < _warnMs)
                    return;
                w.Dark = true;
                w.Elapsed = 0;
                Darken(host, w);
                return;
            }
            var head = Math.Min(w.Path.Count - _ahead - 1, w.Elapsed / _stepMs);
            if (head == w.Head)
                return;
            w.Head = head;
            Light(host, w);
            Floor.Refresh(host);
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (state is Walk w && host.World?.Map != null)
            {
                foreach (var ((x, y), ground) in w.Before)
                    Floor.Put(host, x, y, ground);
                Floor.Refresh(host);
            }
            state = null;
        }

        // the squares lit now: the path's window round its head, three wide (each step and its four
        // neighbours), and while the walk is young a little patch round its start
        private HashSet<(int X, int Y)> Window(Walk w)
        {
            var lit = new HashSet<(int X, int Y)>();
            for (var i = Math.Max(0, w.Head - _behind); i <= Math.Min(w.Path.Count - 1, w.Head + _ahead); i++)
            {
                var (x, y) = w.Path[i];
                lit.Add((x, y));
                lit.Add((x + 1, y));
                lit.Add((x - 1, y));
                lit.Add((x, y + 1));
                lit.Add((x, y - 1));
            }
            if (w.Head < _behind + 3)
            {
                var (sx, sy) = w.Path[0];
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        if (dx * dx + dy * dy <= 5)
                            lit.Add((sx + dx, sy + dy));
            }
            return lit;
        }

        private void Light(Entity host, Walk w)
        {
            var map = host.World.Map;
            var lit = Window(w);
            foreach (var p in lit)
            {
                if (!map.Contains(p.X, p.Y))
                    continue;
                if (!w.Before.ContainsKey(p))
                {
                    var tile = map[p.X, p.Y];
                    if (!host.GameServer.Resources.GameData.Tiles.TryGetValue(tile.TileId, out var desc) || desc.NoWalk
                        || tile.ObjType != 0 && tile.ObjDesc != null && tile.ObjDesc.FullOccupy)
                        continue;
                    w.Before[p] = tile.TileId;
                }
                Floor.Put(host, p.X, p.Y, w.Star);
            }
            if (w.Dark)
                foreach (var p in w.Lit)
                    if (!lit.Contains(p) && w.Before.ContainsKey(p))
                        Floor.Put(host, p.X, p.Y, w.Void);
            w.Lit = lit;
        }

        private void Darken(Entity host, Walk w)
        {
            var map = host.World.Map;
            var c = host.SpawnPoint.Value;
            var r = (int)Math.Ceiling(_radius);
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > _radius * _radius)
                        continue;
                    var p = ((int)c.X + dx, (int)c.Y + dy);
                    if (w.Lit.Contains(p) || !map.Contains(p.Item1, p.Item2))
                        continue;
                    var tile = map[p.Item1, p.Item2];
                    if (tile.TileId == w.Void)
                        continue;
                    if (!host.GameServer.Resources.GameData.Tiles.TryGetValue(tile.TileId, out var desc) || desc.NoWalk
                        || tile.ObjType != 0 && tile.ObjDesc != null && tile.ObjDesc.FullOccupy)
                        continue;
                    if (!w.Before.ContainsKey(p))
                        w.Before[p] = tile.TileId;
                    Floor.Put(host, p.Item1, p.Item2, w.Void);
                }
            Floor.Refresh(host);
        }
    }

    // Aurel's Solar Cyclone: `passes` dashes from rim to rim, each aimed through a player, each
    // marked along its line `telegraphMs` before it starts. While he dashes he sprays his projectile
    // `projectile` in a turning spiral, and anyone he passes through takes `contactDamage`. Within
    // `radius` tiles of the host's spawn point, or of the nearest `centreOn` (the Observatory
    // Heart: the whole arena, whoever throws the tornado). With a `twin` (the nearest of that name),
    // it dashes too, along the same line turned a quarter round the centre (so when he goes from
    // north to south, it goes from east to west), spraying its own projectile 0.
    internal class CycloneDash : Behavior
    {
        private readonly double _radius;
        private readonly double _speed;
        private readonly int _telegraphMs;
        private readonly int _passes;
        private readonly int _projectile;
        private readonly int _fireMs;
        private readonly int _contactDamage;
        private readonly int _pauseMs;
        private readonly string _centreOn;
        private readonly string _twin;

        public CycloneDash(double radius, double speed = 16, int telegraphMs = 800, int passes = 3, int projectile = 2,
            int fireMs = 100, int contactDamage = 260, int pauseMs = 500, string centreOn = null, string twin = null)
        {
            _centreOn = centreOn;
            _twin = twin;
            _radius = radius;
            _speed = speed;
            _telegraphMs = telegraphMs;
            _passes = passes;
            _projectile = projectile;
            _fireMs = fireMs;
            _contactDamage = contactDamage;
            _pauseMs = pauseMs;
        }

        private enum Phase { Pick, Reposition, Telegraph, Dash, Pause, Done }

        private sealed class Run
        {
            public Phase Phase;
            public int Left;
            public int Pass;
            public float Dx, Dy;
            public float EndX, EndY;
            public int Fire;
            public double Spin;
            public HashSet<int> Hit = new();
            public float StartX, StartY;
            public Entity Twin;
            public float TwinStartX, TwinStartY, TwinEndX, TwinEndY;
            public int WaitedForTwin;
            public bool Reported;
        }

        private Position Centre(Entity host)
        {
            if (_centreOn != null && host.GetNearestEntityByName(60, _centreOn) is Entity c)
                return new Position(c.X, c.Y);
            return host.SpawnPoint.Value;
        }

        // the hosts whose dashes are all done (CycloneDoneTransition takes them out)
        internal static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> Finished = new();

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            state = new Run();
            Finished.TryRemove(host.Id, out _);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Run r)
                return;
            switch (r.Phase)
            {
                case Phase.Done:
                    // (the spectral Cyclone's ids are never taken out: keep the list short)
                    if (r.Reported)
                        return;
                    r.Reported = true;
                    if (Finished.Count > 500)
                        Finished.Clear();
                    Finished[host.Id] = true;
                    return;
                case Phase.Pick:
                    // a moment for the twin to come in (it's summoned as the Cyclone starts)
                    if (_twin != null && r.WaitedForTwin < 600 && host.GetNearestEntityByName(60, _twin) == null)
                    {
                        r.WaitedForTwin += time.ElapsedMsDelta;
                        return;
                    }
                    if (r.Pass >= _passes || !Aim(host, r))
                    {
                        r.Phase = Phase.Done;
                        return;
                    }
                    // he flies to the line's start (a jump there in one go left clients drawing his
                    // old sprite rather than the tornado), then it's marked
                    r.Phase = Phase.Reposition;
                    r.Hit.Clear();
                    return;
                case Phase.Reposition:
                    var hop = (float)(_speed * 2 * time.ElapsedMsDelta / 1000.0);
                    var away = Math.Sqrt((r.StartX - host.X) * (r.StartX - host.X) + (r.StartY - host.Y) * (r.StartY - host.Y));
                    if (away > hop)
                    {
                        host.Move(host.X + (float)((r.StartX - host.X) / away * hop), host.Y + (float)((r.StartY - host.Y) / away * hop));
                        if (r.Twin != null)
                        {
                            var tAway = Math.Sqrt((r.TwinStartX - r.Twin.X) * (r.TwinStartX - r.Twin.X) + (r.TwinStartY - r.Twin.Y) * (r.TwinStartY - r.Twin.Y));
                            if (tAway > 0.01)
                            {
                                var k = Math.Min(1, hop * 1.5 / tAway);
                                r.Twin.Move(r.Twin.X + (float)((r.TwinStartX - r.Twin.X) * k), r.Twin.Y + (float)((r.TwinStartY - r.Twin.Y) * k));
                            }
                        }
                        return;
                    }
                    host.Move(r.StartX, r.StartY);
                    r.Twin?.Move(r.TwinStartX, r.TwinStartY);
                    MarkLine(host, r.StartX, r.StartY, r.EndX, r.EndY);
                    if (r.Twin != null)
                        MarkLine(host, r.TwinStartX, r.TwinStartY, r.TwinEndX, r.TwinEndY);
                    r.Phase = Phase.Telegraph;
                    r.Left = _telegraphMs;
                    return;
                case Phase.Telegraph:
                    r.Left -= time.ElapsedMsDelta;
                    if (r.Left <= 0)
                        r.Phase = Phase.Dash;
                    return;
                case Phase.Dash:
                    var step = (float)(_speed * time.ElapsedMsDelta / 1000.0);
                    var remain = Math.Sqrt((r.EndX - host.X) * (r.EndX - host.X) + (r.EndY - host.Y) * (r.EndY - host.Y));
                    if (remain <= step)
                    {
                        host.Move(r.EndX, r.EndY);
                        r.Phase = Phase.Pause;
                        r.Left = _pauseMs;
                        r.Pass++;
                    }
                    else
                        host.Move(host.X + r.Dx * step, host.Y + r.Dy * step);
                    // the twin as far along its line as he is along his
                    var twin = r.Twin?.World == host.World ? r.Twin : null;
                    if (twin != null)
                    {
                        var total = Math.Sqrt((r.EndX - r.StartX) * (r.EndX - r.StartX) + (r.EndY - r.StartY) * (r.EndY - r.StartY));
                        var done = total < 0.01 ? 1 : 1 - Math.Sqrt((r.EndX - host.X) * (r.EndX - host.X) + (r.EndY - host.Y) * (r.EndY - host.Y)) / total;
                        twin.Move(r.TwinStartX + (float)((r.TwinEndX - r.TwinStartX) * done), r.TwinStartY + (float)((r.TwinEndY - r.TwinStartY) * done));
                    }
                    r.Fire -= time.ElapsedMsDelta;
                    if (r.Fire <= 0)
                    {
                        r.Fire = _fireMs;
                        r.Spin += 40 * Math.PI / 180;
                        Strike.Fire(host, _projectile, r.Spin, 2, Math.PI);
                        if (twin != null)
                            Strike.Fire(twin, 0, -r.Spin, 2, Math.PI);
                    }
                    if (_contactDamage > 0)
                    {
                        var dmg = (int)(_contactDamage * ((host as Enemy)?.DamageScale ?? 1));
                        host.AOE(1.3f, true, p =>
                        {
                            if (r.Hit.Add(p.Id))
                                (p as IPlayer).Damage(dmg, host);
                        });
                        twin?.AOE(1.3f, true, p =>
                        {
                            if (r.Hit.Add(-p.Id - 1))
                                (p as IPlayer).Damage(dmg, host);
                        });
                    }
                    return;
                case Phase.Pause:
                    r.Left -= time.ElapsedMsDelta;
                    if (r.Left <= 0)
                        r.Phase = Phase.Pick;
                    return;
            }
        }

        private void MarkLine(Entity host, float x0, float y0, float x1, float y1)
        {
            var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            for (var d = 0.0; d <= len && d <= 40; d += 1.6)
                Strike.Mark(host, new Position((float)(x0 + (x1 - x0) * d / len), (float)(y0 + (y1 - y0) * d / len)), 0.9, _telegraphMs, 0xFFFFD24A);
        }

        // a line from where he stands through a player, on to the far rim (and his twin's, a quarter
        // turn round the centre)
        private bool Aim(Entity host, Run r)
        {
            var c = Centre(host);
            var players = host.World.Players.Values.Where(p => p.DistTo(c.X, c.Y) <= _radius + 2 && p.IsVisibleToEnemy()).ToList();
            if (players.Count == 0)
                return false;
            var target = players[Random.Next(players.Count)];
            double dx = target.X - host.X, dy = target.Y - host.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1.5)
            {
                (dx, dy) = (target.X - c.X, target.Y - c.Y);
                len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.1)
                    (dx, dy, len) = (1, 0, 1);
            }
            (dx, dy) = (dx / len, dy / len);
            // the whole chord of the circle `radius - 1.5` round the centre through the player, that
            // way: from where it enters (he's whisked there first) to where it leaves
            double ox = target.X - c.X, oy = target.Y - c.Y, rr = _radius - 1.5;
            var b = ox * dx + oy * dy;
            var disc = b * b - (ox * ox + oy * oy - rr * rr);
            if (disc <= 0)
                return false;
            var (t0, t1) = (-b - Math.Sqrt(disc), -b + Math.Sqrt(disc));
            if (t1 - t0 < 3)
                return false;
            r.Dx = (float)dx;
            r.Dy = (float)dy;
            r.StartX = (float)(target.X + dx * t0);
            r.StartY = (float)(target.Y + dy * t0);
            r.EndX = (float)(target.X + dx * t1);
            r.EndY = (float)(target.Y + dy * t1);
            r.Twin = _twin == null ? null : host.GetNearestEntityByName(60, _twin);
            if (r.Twin != null)
            {
                // (x, y) round the centre a quarter turn: (-y, x)
                r.TwinStartX = c.X - (r.StartY - c.Y);
                r.TwinStartY = c.Y + (r.StartX - c.X);
                r.TwinEndX = c.X - (r.EndY - c.Y);
                r.TwinEndY = c.Y + (r.EndX - c.X);
            }
            return true;
        }
    }

    // Aurel's Sword Lanes: his sword flies across the arena (round the host's spawn point, `radius`
    // tiles) along every second column, each lane marked in gold `telegraphMs` before, the lanes one
    // after another from west to east, up and down in turn; then along every second row, south to
    // north, west and east in turn. Each round after the first uses the other half of the lanes, so
    // the safe squares move. Fired from the arena's edge (`projectile` of the host's), so it needs
    // no sword object.
    internal class SwordLanes : Behavior
    {
        private readonly double _radius;
        private readonly int _projectile;
        private readonly int _rounds;
        private readonly int _telegraphMs;
        private readonly int _laneMs;
        private readonly int _gapMs;

        public SwordLanes(double radius, int projectile, int rounds = 2, int telegraphMs = 900, int laneMs = 110, int gapMs = 900)
        {
            _radius = radius;
            _projectile = projectile;
            _rounds = rounds;
            _telegraphMs = telegraphMs;
            _laneMs = laneMs;
            _gapMs = gapMs;
        }

        private sealed class Plan
        {
            public readonly List<(int At, bool Fire, bool Vertical, int Offset, int Dir)> Events = new();
            public int Elapsed;
            public int Next;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var plan = new Plan();
            state = plan;
            var t = 600;
            var reach = (int)Math.Floor(_radius) - 1;
            for (var round = 0; round < _rounds; round++)
                foreach (var vertical in new[] { true, false })
                {
                    var i = 0;
                    for (var k = -reach + round % 2; k <= reach; k += 2, i++)
                    {
                        var dir = i % 2 == 0 ? 1 : -1;
                        plan.Events.Add((t + i * _laneMs, false, vertical, k, dir));
                        plan.Events.Add((t + i * _laneMs + _telegraphMs, true, vertical, k, dir));
                    }
                    t += i * _laneMs + _telegraphMs + _gapMs;
                }
            plan.Events.Sort((a, b) => a.At.CompareTo(b.At));
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Plan plan)
                return;
            plan.Elapsed += time.ElapsedMsDelta;
            var c = host.SpawnPoint.Value;
            while (plan.Next < plan.Events.Count && plan.Events[plan.Next].At <= plan.Elapsed)
            {
                var (_, fire, vertical, k, dir) = plan.Events[plan.Next++];
                var half = Math.Sqrt(Math.Max(0, _radius * _radius - k * k));
                if (!fire)
                {
                    for (var d = -half + 0.5; d <= half; d += 2)
                    {
                        var at = vertical ? new Position(c.X + k, c.Y + (float)d) : new Position(c.X + (float)d, c.Y + k);
                        Strike.Mark(host, at, 0.6, _telegraphMs, 0xFFFFD24A);
                    }
                    continue;
                }
                // from the platform's far edge, across (not from beyond it: clients end a shot as soon as
                // it is over a square with no ground, the void)
                var from = (float)Math.Max(0, half - 0.6);
                if (vertical)
                    Strike.FireFrom(host, c.X + k, c.Y - dir * from, _projectile, dir * Math.PI / 2);
                else
                    Strike.FireFrom(host, c.X - dir * from, c.Y + k, _projectile, dir > 0 ? 0 : Math.PI);
            }
        }
    }

    // Vesper's Fourfold Veil, as it rises: anyone on the platform outside the diamond (`diamond`
    // tiles to each point round the host's spawn) is moved into the middle of the nearest quarter
    // of it, so no one starts on the burning floor.
    internal class GatherIntoQuadrants : Behavior
    {
        private readonly int _diamond;
        private readonly double _reach;

        public GatherIntoQuadrants(int diamond, double reach)
        {
            _diamond = diamond;
            _reach = reach;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var c = host.SpawnPoint.Value;
            var into = _diamond / 2.0f - 0.5f;
            foreach (var player in host.World.Players.Values)
            {
                float dx = player.X - c.X, dy = player.Y - c.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > _reach)
                    continue;
                var inside = Math.Abs(dx) + Math.Abs(dy) < _diamond - 0.5;
                if (inside)
                    continue;
                var sx = Math.Abs(dx) < 0.5 ? (Random.Next(2) == 0 ? -1 : 1) : Math.Sign(dx);
                var sy = Math.Abs(dy) < 0.5 ? (Random.Next(2) == 0 ? -1 : 1) : Math.Sign(dy);
                player.TeleportPosition(time, new Position(c.X + sx * into, c.Y + sy * into), true);
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }

    // As the fight starts and the causeway goes (PaintFloor to the void): anyone on it between the
    // platform and the pad, `pad` tiles south of the middle and on, is moved onto the platform at
    // the causeway's mouth, `mouth` tiles south of the middle, since the void can't be stood on (to
    // the client a square with no ground is solid: they'd be stuck). Whoever is on the pad stays.
    internal class ClearTheCauseway : Behavior
    {
        private readonly double _platformR;
        private readonly double _pad;
        private readonly double _mouth;

        public ClearTheCauseway(double platformR, double pad, double mouth)
        {
            _platformR = platformR;
            _pad = pad;
            _mouth = mouth;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var c = host.SpawnPoint.Value;
            foreach (var player in host.World.Players.Values.ToArray())
            {
                float dx = player.X - c.X, dy = player.Y - c.Y;
                if (dy <= 0 || dx * dx + dy * dy <= _platformR * _platformR || dy >= _pad)
                    continue;
                player.TeleportPosition(time, new Position(c.X + Math.Clamp(dx, -1f, 1f), c.Y + (float)_mouth), true);
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }

    // Puts the pets within `range` of the host under `effect` for `durationMs`, renewed every
    // `coolDown` while the state lasts (pets in stasis neither heal nor attack: the survival phases).
    internal class PetAura : Behavior
    {
        private readonly double _range;
        private readonly ConditionEffectIndex _effect;
        private readonly int _durationMs;
        private readonly Cooldown _coolDown;

        public PetAura(double range, ConditionEffectIndex effect, int durationMs, Cooldown coolDown = new Cooldown())
        {
            _range = range;
            _effect = effect;
            _durationMs = durationMs;
            _coolDown = coolDown.Normalize(500);
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = 0;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state - time.ElapsedMsDelta;
            if (cool > 0)
            {
                state = cool;
                return;
            }
            state = _coolDown.Next(Random);
            foreach (var pet in host.World.Pets.Values)
                if (pet.DistTo(host) <= _range)
                    pet.ApplyConditionEffect(new ConditionEffect(_effect, _durationMs));
        }
    }

    // When the host dies, `objName` stands on the square `dx`, `dy` from its spawn point, as a map
    // object (a square's object, as the map's own are: the Cosmic Anvil, which the server finds by
    // its square, DoomedEconomy.NearObject).
    internal class PlaceOnDeath : Behavior
    {
        private readonly string _objName;
        private readonly int _dx;
        private readonly int _dy;

        public PlaceOnDeath(string objName, int dx, int dy)
        {
            _objName = objName;
            _dx = dx;
            _dy = dy;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var world = host.World;
            var data = host.GameServer.Resources.GameData;
            if (world == null || !data.IdToObjectType.TryGetValue(_objName, out var objType))
                return;
            var c = host.SpawnPoint.Value;
            var (x, y) = ((int)c.X + _dx, (int)c.Y + _dy);
            if (!world.Map.Contains(x, y) || world.Map[x, y].ObjType != 0)
                return;
            var tile = world.Map[x, y];
            tile.ObjType = objType;
            tile.ObjDesc = data.ObjectDescs[objType];
            tile.ObjId = world.GetNextEntityId();
            tile.UpdateCount++;
            foreach (var player in world.Players.Values)
                player.UpdateTiles();
        }
    }

    // Oryx the Ascended God's fall, in his Elysium: a quarter of the time (every time while a server
    // event says so: ServerEvent.mysteriousOne), the Mysterious One's Rift opens a few seconds later,
    // `ahead` tiles north of where Oryx began (the middle of his temple), and he steps out of it (the
    // Mysterious One Visitant, BehaviorDb.BlackBetween.cs). The Rift is the way into the Black
    // Between, open until the Elysium closes. Never in an Arena game.
    internal class OpenTheBetween : Behavior
    {
        private readonly double _chance;
        private readonly float _ahead;

        public OpenTheBetween(double chance = 0.25, float ahead = 6)
        {
            _chance = chance;
            _ahead = ahead;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var world = host.World;
            if (world == null || WorldServer.logic.transitions.ArenaTransition.InArena(host) || world.IdName != "Oryx's Elysium")
                return;
            var always = Shared.resources.ServerEvents.Active(host.GameServer.Configuration.serverSettings.events, DateTime.UtcNow)
                .Any(e => e.mysteriousOne);
            if (!always && Random.NextDouble() >= _chance)
                return;
            var c = host.SpawnPoint.Value;
            var at = new Position(c.X, c.Y - _ahead);
            if (!world.IsPassable(at.X, at.Y))
                at = new Position(host.X, host.Y);
            var server = host.GameServer;
            world.StartNewTimer(3500, (w, t) =>
            {
                var rift = Entity.Resolve(server, "Rift of the Between (Open)");
                var visitant = Entity.Resolve(server, "Mysterious One Visitant");
                if (rift == null || visitant == null)
                    return;
                rift.Move(at.X, at.Y);
                w.EnterWorld(rift);
                visitant.Move(at.X, at.Y);
                w.EnterWorld(visitant);
            });
        }
    }

    // Vesper's Starfall Waltz: on entering the state the host blinks to a random walkable spot
    // between `min` and `max` tiles from its spawn point.
    internal class BlinkAround : Behavior
    {
        private readonly double _min;
        private readonly double _max;

        public BlinkAround(double min, double max)
        {
            _min = min;
            _max = max;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var c = host.SpawnPoint.Value;
            for (var tries = 0; tries < 20; tries++)
            {
                var a = Random.NextDouble() * 2 * Math.PI;
                var d = _min + Random.NextDouble() * (_max - _min);
                var (x, y) = (c.X + (float)(d * Math.Cos(a)), c.Y + (float)(d * Math.Sin(a)));
                if (!Strike.Walkable(host, x, y))
                    continue;
                host.Move(x, y);
                return;
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }

    // Aurel's Zenith Leap: he marks where a player will be when the mark has filled (`fillMs`: where
    // they're heading, at the pace they're going, not where they stand), and then lands there with a
    // blast (`damage` within `radius`, breaking armour) and a ring of his projectile `projectile`.
    // With an `echo` (an object that looks just like him), a second mark goes down on another
    // player (alone: across the arena from the first), the echo leaps to one and he to the other,
    // which at random, so no one can tell which is him; the echo then strikes and is gone (its own
    // behaviour).
    internal class LeapStrike : Behavior
    {
        private readonly double _range;
        private readonly double _radius;
        private readonly int _damage;
        private readonly int _fillMs;
        private readonly Cooldown _coolDown;
        private readonly int _projectile;
        private readonly int _ring;

        private readonly string _echo;

        public LeapStrike(double range, double radius, int damage, int fillMs, Cooldown coolDown, int projectile, int ring = 12,
            string echo = null)
        {
            _echo = echo;
            _range = range;
            _radius = radius;
            _damage = damage;
            _fillMs = fillMs;
            _coolDown = coolDown.Normalize(4000);
            _projectile = projectile;
            _ring = ring;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = 0;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var cool = (int)state - time.ElapsedMsDelta;
            if (cool > 0)
            {
                state = cool;
                return;
            }
            state = _coolDown.Next(Random);
            var players = host.World.Players.Values.Where(p => p.DistTo(host) <= _range && p.IsVisibleToEnemy()).ToList();
            if (players.Count == 0)
                return;
            var target = players[Random.Next(players.Count)];
            var c = host.SpawnPoint.Value;
            var at = Lead(host, target, c);
            var world = host.World;
            Entity echo = null;
            var echoAt = at;
            if (_echo != null)
            {
                var others = players.Where(p => p != target).ToList();
                echoAt = others.Count > 0 ? Lead(host, others[Random.Next(others.Count)], c)
                    : new Position(2 * c.X - at.X, 2 * c.Y - at.Y);
                if (!Strike.Walkable(host, echoAt.X, echoAt.Y))
                    echoAt = new Position(c.X, c.Y);
                echo = Entity.Resolve(host.GameServer, _echo);
                if (echo != null)
                {
                    echo.Move(host.X, host.Y);
                    echo.SummonedBy(host);
                    world.EnterWorld(echo);
                    // which of the two is him
                    if (Random.Next(2) == 0)
                        (at, echoAt) = (echoAt, at);
                    Strike.Mark(host, echoAt, _radius, _fillMs, 0xFFFFD24A);
                }
            }
            Strike.Mark(host, at, _radius, _fillMs, 0xFFFFD24A);
            world.StartNewTimer(_fillMs, (w, t) =>
            {
                if (host.World != world)
                    return;
                Leap(host, host, at, world);
                if (echo != null && echo.World == world)
                    Leap(echo, host, echoAt, world);
            });
        }

        // where `target` will be when the mark has filled: a tick's movement (PrevX/PrevY, the last
        // tick's place) for each tick of the mark, most of the way, so a player who keeps going runs
        // into it; where they stand if that's off the platform
        private Position Lead(Entity host, Entity target, Position c)
        {
            var ticks = _fillMs / 100.0 * 0.85;
            var at = new Position(target.X + (float)((target.X - target.PrevX) * ticks), target.Y + (float)((target.Y - target.PrevY) * ticks));
            if (!Strike.Walkable(host, at.X, at.Y) || at.DistTo(c.X, c.Y) > 16)
                at = new Position(target.X, target.Y);
            return at;
        }

        // `who` (him, or his echo) glides down onto `at`: the blast (as his: `owner`'s damage) and a
        // ring of `who`'s projectile
        private void Leap(Entity who, Entity owner, Position at, World world)
        {
            if (who.HasConditionEffect(ConditionEffectIndex.Stunned))
                return;
            void Land()
            {
                Strike.Land(world, owner, at, _radius, _damage, 0xFFFFD24A, ConditionEffectIndex.ArmorBroken, 3000);
                Strike.Fire(who, who == owner ? _projectile : 0, 0, _ring, 2 * Math.PI / _ring);
            }
            if (Strike.Walkable(who, at.X, at.Y))
                Strike.Glide(who, at, 200, Land);
            else
                Land();
        }
    }

    // to the target state once the host's CycloneDash has made all its dashes: the Cyclone ends as
    // its last dash does, not on a timer that could cut one short (which sent the twin away
    // mid-flight and left him wherever he stopped)
    internal class CycloneDoneTransition : logic.Transition
    {
        public CycloneDoneTransition(string targetState) : base(targetState) { }

        protected override bool TickCore(Entity host, TickTime time, ref object state) =>
            CycloneDash.Finished.TryRemove(host.Id, out _);
    }

    // Before each sickening survival phase: the Mysterious One (his herald while his children
    // fight) says so, and everyone on the platform is made invincible for `invincibleMs` and healed
    // to full; then the phase begins. Once, as the state starts (nothing for 0 ms).
    internal class RealmHeal : Behavior
    {
        public const string Line = "A heal, from your realm.";
        private readonly int _invincibleMs;

        public RealmHeal(int invincibleMs) => _invincibleMs = invincibleMs;

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            if (world == null || _invincibleMs <= 0)
                return;
            var speaker = host.ObjectDesc.IdName == "The Mysterious One" ? host
                : host.GetNearestEntityByName(80, "Mysterious One Herald") ?? host.GetNearestEntityByName(80, "The Mysterious One") ?? host;
            var name = speaker.ObjectDesc.DisplayId ?? speaker.ObjectDesc.IdName;
            world.Broadcast(new Text { Name = "#" + name, ObjectId = speaker.Id, NumStars = -1, BubbleTime = 0, Recipient = "", Txt = Line });
            foreach (var p in world.Players.Values.Where(p => p.DistTo(host) <= 40 && p.Health > 0))
            {
                p.ApplyConditionEffect(ConditionEffectIndex.Invincible, _invincibleMs);
                Player.ActivateHealHp(p, p.Stats[0] - p.Health);
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }
}
