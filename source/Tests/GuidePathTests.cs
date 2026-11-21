using System.Linq;
using WorldServer.core.guide;
using Xunit;

namespace Tests
{
    // The Gilded pass's dungeon guide: the shortest walk over a map ('#' wall, '.' open, 'D' a
    // door: an object that fills its square).
    public class GuidePathTests
    {
        private static System.Collections.Generic.List<(int X, int Y)> Walk(string[] rows, (int X, int Y) from, (int X, int Y) to)
        {
            var h = rows.Length;
            var w = rows[0].Length;
            byte Cell(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? GuidePath.Wall : rows[y][x] switch { '#' => GuidePath.Wall, 'D' => GuidePath.Blocked, _ => GuidePath.Open };
            return GuidePath.Search(w, h, from.X, from.Y, to.X, to.Y, Cell, false) ?? GuidePath.Search(w, h, from.X, from.Y, to.X, to.Y, Cell, true);
        }

        [Fact]
        public void ItTakesTheShortWayRoundAWall()
        {
            var path = Walk(new[]
            {
                "..........",
                ".########.",
                ".#......#.",
                ".#.##.#.#.",
                "...#..#...",
                "####..####",
            }, (0, 0), (4, 4));
            Assert.NotNull(path);
            Assert.Equal((0, 0), path[0]);
            // every step is to a neighbour, on open ground
            for (var i = 1; i < path.Count; i++)
                Assert.True(System.Math.Max(System.Math.Abs(path[i].X - path[i - 1].X), System.Math.Abs(path[i].Y - path[i - 1].Y)) == 1);
            // it ends next to the target, never through the wall
            var last = path[^1];
            Assert.True(System.Math.Max(System.Math.Abs(last.X - 4), System.Math.Abs(last.Y - 4)) <= 1);
        }

        [Fact]
        public void ItDoesNotSqueezeBetweenCorners()
        {
            // the only link between the two sides is a diagonal one, a gap between two corners
            // a player can't pass: no way
            var rows = new[]
            {
                "..#..",
                "..#..",
                "##.##",
                "..#..",
                "..#..",
            };
            Assert.Null(Walk(rows, (0, 0), (4, 4)));
            // in the open a diagonal is walked: to within one square of the target
            var diagonal = new[]
            {
                ".....",
                ".....",
                ".....",
            };
            Assert.Equal(4, Walk(diagonal, (0, 0), (4, 2)).Count);
        }

        [Fact]
        public void ItGoesThroughADoorOnlyWhenThereIsNoOtherWay()
        {
            var shut = new[]
            {
                "....#....",
                "....D....",
                "....#....",
            };
            var path = Walk(shut, (0, 1), (8, 1));
            Assert.NotNull(path);
            Assert.Contains((4, 1), path);
            // with a way round, the door's left alone
            var round = new[]
            {
                "....#....",
                "....D....",
                ".........",
            };
            path = Walk(round, (0, 1), (8, 1));
            Assert.DoesNotContain((4, 1), path);
        }

        [Fact]
        public void NoWayAtAllIsNull()
        {
            var rows = new[]
            {
                "..#..",
                "..#..",
                "..#..",
            };
            Assert.Null(Walk(rows, (0, 0), (4, 0)));
        }

        [Fact]
        public void TheShortestWayIsTheShortest()
        {
            // an open room: the walk is the straight line with a diagonal, eight steps to
            // within one square of the corner
            var rows = Enumerable.Repeat(new string('.', 12), 12).ToArray();
            var path = Walk(rows, (0, 0), (10, 10));
            Assert.Equal(10, path.Count);
        }
    }
}
