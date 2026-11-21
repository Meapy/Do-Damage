using System;
using System.Collections.Generic;
using WorldServer.core.worlds;

namespace WorldServer.core.guide
{
    // The shortest walk between two squares of a world, for the Gilded pass's dungeon guide
    // (Player.Guide.cs): A* over the map, eight ways (a diagonal step only past two open
    // squares, as a player can't squeeze between corners), through ground that can be walked on
    // and squares no object fills. A dungeon's doors and barriers are objects that fill their
    // square too, so when no way is open the search is made again with such squares passable at
    // a cost (it crosses as few as it can): the guide then points through the door.
    public static class GuidePath
    {
        // squares looked at before giving up (a big map's farthest corner is well under it)
        private const int MaxExpanded = 120000;
        private const float Diagonal = 1.4142f, ObjectCost = 6f;

        public const byte Unknown = 0, Wall = 1, Open = 2, Blocked = 3;

        /// <summary>The squares from (sx, sy) to within one square of (tx, ty), both included,
        /// or null when there is no way.</summary>
        public static List<(int X, int Y)> Find(World world, int sx, int sy, int tx, int ty)
        {
            var map = world.Map;
            if (!map.Contains(sx, sy) || !map.Contains(tx, ty))
                return null;
            var w = map.Width;
            var tiles = world.GameServer.Resources.GameData.Tiles;
            var memo = new byte[w * map.Height];

            byte Cell(int x, int y)
            {
                if (!map.Contains(x, y))
                    return Wall;
                var i = y * w + x;
                var c = memo[i];
                if (c != Unknown)
                    return c;
                var tile = world.GroundTile(x, y);
                if (!tile.Exists || !tiles.TryGetValue(tile.TileId, out var ground) || ground.NoWalk)
                    c = Wall;
                else
                {
                    var desc = tile.ObjType != 0 ? tile.ObjDesc : null;
                    c = desc != null && (desc.FullOccupy || desc.OccupySquare) ? Blocked : Open;
                }
                return memo[i] = c;
            }

            return Search(w, map.Height, sx, sy, tx, ty, Cell, false) ?? Search(w, map.Height, sx, sy, tx, ty, Cell, true);
        }

        // One A*: `through` lets it cross squares an object fills, at a cost.
        public static List<(int X, int Y)> Search(int w, int h, int sx, int sy, int tx, int ty, Func<int, int, byte> cell, bool through)
        {
            var size = w * h;
            var g = new float[size];
            Array.Fill(g, float.MaxValue);
            var from = new int[size];
            Array.Fill(from, -1);
            var closed = new bool[size];

            float Heuristic(int x, int y)
            {
                var dx = Math.Abs(x - tx);
                var dy = Math.Abs(y - ty);
                return Math.Max(dx, dy) + (Diagonal - 1f) * Math.Min(dx, dy);
            }

            var start = sy * w + sx;
            g[start] = 0;
            var open = new PriorityQueue<int, float>();
            open.Enqueue(start, Heuristic(sx, sy));
            var expanded = 0;
            while (open.TryDequeue(out var cur, out _))
            {
                if (closed[cur])
                    continue;
                closed[cur] = true;
                int cx = cur % w, cy = cur / w;
                if (Math.Max(Math.Abs(cx - tx), Math.Abs(cy - ty)) <= 1 && (cur == start || cell(cx, cy) != Wall))
                {
                    var path = new List<(int, int)>();
                    for (var at = cur; at != -1; at = from[at])
                        path.Add((at % w, at / w));
                    path.Reverse();
                    return path;
                }
                if (++expanded > MaxExpanded)
                    return null;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        var next = ny * w + nx;
                        if (closed[next])
                            continue;
                        var c = cell(nx, ny);
                        if (c == Wall || c == Blocked && !through)
                            continue;
                        var step = 1f;
                        if (dx != 0 && dy != 0)
                        {
                            // no cutting a corner: both squares beside the step must be open ground
                            var a = cell(cx + dx, cy);
                            var b = cell(cx, cy + dy);
                            if (a == Wall || b == Wall || !through && (a == Blocked || b == Blocked))
                                continue;
                            step = Diagonal;
                        }
                        if (c == Blocked)
                            step += ObjectCost;
                        var ng = g[cur] + step;
                        if (ng >= g[next])
                            continue;
                        g[next] = ng;
                        from[next] = cur;
                        open.Enqueue(next, ng + Heuristic(nx, ny));
                    }
            }
            return null;
        }
    }
}
