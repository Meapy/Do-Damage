using System;
using System.IO;
using System.Linq;
using System.Text;
using Shared.resources;
using Shared.terrain;
using WorldServer.core.resurrection;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    // The Hall of Judgement (core/resurrection/TombCourt.cs): in every Elder Tomb laid out, the
    // boss room opens onto a causeway across the dark that touches no other room, to a hall with
    // room round it, far enough out to be a walk.
    public class TombCourtTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private readonly ITestOutputHelper _out;

        public TombCourtTests(ITestOutputHelper output) => _out = output;

        private static CourtCell Cell(XmlData data, TerrainTile t)
        {
            if (t.TileObj == "Elder Tomb Wall")
                return CourtCell.Wall;
            if (t.TileId == 0xFF && string.IsNullOrEmpty(t.TileObj))
                return CourtCell.Void;
            return data.Tiles.TryGetValue(t.TileId, out var d) && !d.NoWalk ? CourtCell.Floor : CourtCell.Solid;
        }

        [Fact]
        public void EveryTombHasItsHall()
        {
            var data = Res.Value.GameData;
            var world = data.GetWorld(TombCourt.Dungeon);
            Assert.NotNull(world.RandomLayout);
            Assert.True(world.RandomLayout.Margin >= 50, "the tomb keeps dark round it for the hall");
            var map = world.MapJM[0];
            var source = data.GetWorldData(map);
            for (var seed = 0; seed < 40; seed++)
            {
                var tiles = DungeonGenerator.Import(DungeonGenerator.Generate(data, map, source, world.RandomLayout, new Random(seed)));
                int w = tiles.GetLength(0), h = tiles.GetLength(1);
                var anchor = Enumerable.Range(0, w).SelectMany(x => Enumerable.Range(0, h).Select(y => (x, y)))
                    .First(q => tiles[q.x, q.y].TileObj == "Elder Tomb Boss Anchor");
                var plan = TombCourtPlan.Make(w, h, (x, y) => Cell(data, tiles[x, y]), anchor.x, anchor.y, out var why);
                if (plan == null)
                    Assert.Fail($"seed {seed}: {why}\n{Draw(data, tiles, null)}");

                // the causeway and hall are built on dark only, and touch nothing but the boss room
                foreach (var q in plan.Causeway.Keys.Concat(plan.Hall.Keys).Concat(plan.HallWalls))
                {
                    Assert.Equal(CourtCell.Void, Cell(data, tiles[q.X, q.Y]));
                    for (var dx = -1; dx <= 1; dx++)
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            var n = tiles[q.X + dx, q.Y + dy];
                            var own = plan.Room.Contains((q.X + dx, q.Y + dy)) || plan.Gate.Contains((q.X + dx, q.Y + dy)) || plan.Rejoin.Contains((q.X + dx, q.Y + dy));
                            // (the picture only when it fails: it's a whole map)
                            if (!(own || Cell(data, n) == CourtCell.Void || Cell(data, n) == CourtCell.Wall && IsRoomWall(plan, q.X + dx, q.Y + dy)))
                                Assert.Fail($"seed {seed}: the hall touches another room at {q}\n{Draw(data, tiles, plan)}");
                        }
                }
                Assert.Equal(5, plan.Gate.Count);
                Assert.True(plan.EntryS >= TombCourtPlan.MinWalk, $"seed {seed}: a causeway of only {plan.EntryS:0.0}");
                Assert.Equal(6, plan.Pillars.Count(p => p.Count == 4));
                Assert.True(plan.HallBraziers.Count >= 10, $"seed {seed}: {plan.HallBraziers.Count} braziers in the hall");
                // a spot for each god round the middle (all within reach of its anchor), on the floor
                Assert.Equal(3, plan.Thrones.Count);
                foreach (var (tx, ty) in plan.Thrones)
                    Assert.True(plan.Hall.ContainsKey(((int)tx, (int)ty)), $"seed {seed}: a god's spot off the floor");
                // walls along the causeway, on the dark, and nowhere the floor opens onto the dark
                Assert.NotEmpty(plan.CausewayWalls);
                foreach (var q in plan.CausewayWalls.Keys)
                    Assert.Equal(CourtCell.Void, Cell(data, tiles[q.X, q.Y]));
                bool Built((int, int) n) => plan.Hall.ContainsKey(n) || plan.HallWalls.Contains(n) || plan.Causeway.ContainsKey(n)
                    || plan.CausewayWalls.ContainsKey(n) || plan.Gate.Contains(n) || Cell(data, tiles[n.Item1, n.Item2]) != CourtCell.Void;
                foreach (var q in plan.Hall.Keys.Concat(plan.Causeway.Keys))
                    for (var dx = -1; dx <= 1; dx++)
                        for (var dy = -1; dy <= 1; dy++)
                            if (!Built((q.X + dx, q.Y + dy)))
                                Assert.Fail($"seed {seed}: the floor at {q} opens onto the dark at {(q.X + dx, q.Y + dy)}\n{Draw(data, tiles, plan)}");
                Assert.NotEmpty(plan.CausewayBraziers);
                Assert.True(plan.Sun.Count > 30);
                if (seed < 2)
                    _out.WriteLine($"seed {seed}: {w}x{h}, causeway {plan.EntryS:0.0} squares, clearance {plan.Clearance}\n{Draw(data, tiles, plan)}");
            }
        }

        // a wall of the boss room (beside its floor)
        private static bool IsRoomWall(TombCourtPlan plan, int x, int y)
        {
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (plan.Room.Contains((x + dx, y + dy)))
                        return true;
            return false;
        }

        private static string Draw(XmlData data, TerrainTile[,] tiles, TombCourtPlan plan)
        {
            var sb = new StringBuilder();
            var pillars = plan?.Pillars.SelectMany(p => p).ToHashSet();
            for (var y = 0; y < tiles.GetLength(1); y++)
            {
                for (var x = 0; x < tiles.GetLength(0); x++)
                {
                    var q = (x, y);
                    char c;
                    if (plan != null && plan.Gate.Contains(q)) c = '+';
                    else if (plan != null && plan.Thrones.Any(t => ((int)t.X, (int)t.Y) == q)) c = 'T';
                    else if (plan != null && (plan.HallWalls.Contains(q) || plan.CausewayWalls.ContainsKey(q))) c = 'O';
                    else if (plan != null && pillars.Contains(q)) c = 'P';
                    else if (plan != null && (plan.HallBraziers.Any(b => (b.X, b.Y) == q) || plan.CausewayBraziers.Any(b => (b.X, b.Y) == q))) c = '*';
                    else if (plan != null && plan.Sun.Contains(q)) c = 'o';
                    else if (plan != null && (plan.Causeway.ContainsKey(q) || plan.Hall.ContainsKey(q))) c = ':';
                    else if (tiles[x, y].TileObj == "Elder Tomb Boss Anchor") c = 'A';
                    else c = Cell(data, tiles[x, y]) switch { CourtCell.Void => ' ', CourtCell.Wall => '#', CourtCell.Floor => '.', _ => 'x' };
                    sb.Append(c);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
