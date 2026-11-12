using System;
using System.Collections.Generic;
using WorldServer.core.objects;
using WorldServer.core.structures;
using Xunit;

namespace Tests
{
    public class SightPathTests
    {
        private static readonly IntPoint[] Around = { new(0, 0), new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

        private static HashSet<IntPoint> Circle(int radius)
        {
            var ret = new HashSet<IntPoint>();
            for (var x = -radius; x <= radius; x++)
                for (var y = -radius; y <= radius; y++)
                    if (x * x + y * y <= radius * radius)
                        ret.Add(new IntPoint(x, y));
            return ret;
        }

        // the flood fill as it was, with a map-sized array per call
        private static HashSet<IntPoint> MapSized(bool[,] walls, HashSet<IntPoint> sight, int px, int py)
        {
            int w = walls.GetLength(0), h = walls.GetLength(1);
            var points = new HashSet<IntPoint>();
            var map = new bool[w, h];
            void Step(int x, int y)
            {
                if (x < 0 || x >= w || y < 0 || y >= h || map[x, y])
                    return;
                map[x, y] = true;
                var point = new IntPoint(x - px, y - py);
                if (!sight.Contains(point))
                    return;
                points.Add(point);
                if (!walls[x, y])
                    foreach (var p in Around)
                        Step(x + p.X, y + p.Y);
            }
            Step(px, py);
            return points;
        }

        [Fact]
        public void SeesWhatTheMapSizedFillSaw()
        {
            var random = new Random(7);
            bool[] visited = null;
            foreach (var radius in new[] { 15, 15, 20, 40, 15 })
            {
                var sight = Circle(radius);
                for (var run = 0; run < 40; run++)
                {
                    int w = random.Next(5, 120), h = random.Next(5, 120);
                    var walls = new bool[w, h];
                    var density = random.NextDouble() * 0.5;
                    for (var x = 0; x < w; x++)
                        for (var y = 0; y < h; y++)
                            walls[x, y] = random.NextDouble() < density;
                    int px = random.Next(w), py = random.Next(h);

                    var points = new HashSet<IntPoint>();
                    Player.SightPath(points, sight, radius, px, py,
                        (x, y) => x >= 0 && x < w && y >= 0 && y < h, (x, y) => walls[x, y], ref visited);

                    Assert.True(MapSized(walls, sight, px, py).SetEquals(points), $"radius {radius}, {w}x{h} map, player at {px},{py}");
                    Assert.Equal((2 * radius + 1) * (2 * radius + 1), visited.Length);
                }
            }
        }

        [Fact]
        public void KeepsItsGridBetweenSteps()
        {
            bool[] visited = null;
            var sight = Circle(15);
            var points = new HashSet<IntPoint>();
            Player.SightPath(points, sight, 15, 500, 500, (x, y) => x >= 0 && x < 1024 && y >= 0 && y < 1024, (x, y) => false, ref visited);
            var grid = visited;
            points.Clear();
            Player.SightPath(points, sight, 15, 501, 500, (x, y) => x >= 0 && x < 1024 && y >= 0 && y < 1024, (x, y) => false, ref visited);

            Assert.Same(grid, visited);
            Assert.True(sight.SetEquals(points));
        }
    }
}
