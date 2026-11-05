using System;
using System.Collections.Generic;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // A round arena flooding from its rim inwards: on entering the state, the floor within `outer`
    // tiles of the host's spawn and further than `start` turns to `tileId`; then `steps` times over
    // `durationMs`, evenly, the edge moves in, until only the floor within `end` is left. Walls,
    // and grounds nobody walks on, are left as they are. When the state ends the floor comes back.
    // Oryx 5's Last Judgement (BehaviorDb.Elysium.cs) leaves no safe corner this way, and once it's
    // survived (the Concession) the temple is walkable again.
    internal class LavaRing : Behavior
    {
        private readonly string _tileId;
        private readonly double _outer;
        private readonly double _start;
        private readonly double _end;
        private readonly int _steps;
        private readonly int _durationMs;

        public LavaRing(string tileId, double outer, double start, double end, int steps, int durationMs)
        {
            _tileId = tileId;
            _outer = outer;
            _start = start;
            _end = end;
            _steps = Math.Max(1, steps);
            _durationMs = durationMs;
        }

        private sealed class Flood
        {
            public int Step;
            public int Elapsed;
            // the squares turned to lava, and their ground before
            public readonly List<(int X, int Y, ushort Ground)> Painted = new();
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var f = new Flood();
            state = f;
            Paint(host, _start, f);
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            var map = host.World?.Map;
            if (state is not Flood f || map == null)
                return;
            foreach (var (x, y, ground) in f.Painted)
            {
                var tile = map[x, y];
                tile.TileId = ground;
                tile.UpdateCount++;
            }
            f.Painted.Clear();
            // players work out their tiles again only when they step onto another one
            foreach (var player in host.World.Players.Values)
                player.UpdateTiles();
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is not Flood f || f.Step >= _steps)
                return;
            f.Elapsed += time.ElapsedMsDelta;
            if (f.Elapsed < (f.Step + 1) * _durationMs / _steps)
                return;
            f.Step++;
            Paint(host, _start - (_start - _end) * f.Step / _steps, f);
        }

        private void Paint(Entity host, double inner, Flood f)
        {
            // only in the boss's own temple: never in an Arena game, whatever brings him there
            if (WorldServer.logic.transitions.ArenaTransition.InArena(host))
                return;
            var map = host.World?.Map;
            var data = host.GameServer.Resources.GameData;
            if (map == null || !data.IdToTileType.TryGetValue(_tileId, out var lava))
                return;
            var centre = host.SpawnPoint.Value;
            var r = (int)Math.Ceiling(_outer);
            for (var y = (int)centre.Y - r; y <= (int)centre.Y + r; y++)
                for (var x = (int)centre.X - r; x <= (int)centre.X + r; x++)
                {
                    if (!map.Contains(x, y))
                        continue;
                    var d = Math.Sqrt((x + 0.5 - centre.X) * (x + 0.5 - centre.X) + (y + 0.5 - centre.Y) * (y + 0.5 - centre.Y));
                    if (d <= inner || d > _outer)
                        continue;
                    var tile = map[x, y];
                    if (tile.TileId == lava || !data.Tiles.TryGetValue(tile.TileId, out var desc) || desc.NoWalk)
                        continue;
                    if (tile.ObjType != 0 && tile.ObjDesc != null && tile.ObjDesc.FullOccupy)
                        continue;
                    f.Painted.Add((x, y, tile.TileId));
                    tile.TileId = lava;
                    tile.UpdateCount++;
                }
        }
    }
}
