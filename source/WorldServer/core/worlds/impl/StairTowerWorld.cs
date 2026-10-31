using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds.impl
{
    /// <summary>
    /// The Stair Tower (tools/build_stair_tower.py), the prototype of a dungeon of floors: three
    /// floors one above the other, each with its boss, joined by stairs that climb for 20 seconds.
    /// Admins open it with /dungeon testadmin1. The floors lie one after another up the flat map,
    /// and the height is all show; what's real is the order, kept here:
    /// - a floor's door (Tower_Door tiles, north of its boss) opens when its boss (the monster that
    ///   stood on its Tower_Boss tile) is gone;
    /// - a stairwell loops: whoever crosses its loop row (Stair_Loop) before they've climbed for
    ///   <see cref="ClimbMs"/> since its first row (Stair_Start) is moved back down
    ///   <see cref="Segment"/> tiles, onto stairs that look the same, with no teleport's flash
    ///   (<see cref="Player.Shift"/>). Once their time is up they walk on to the next floor.
    /// </summary>
    public sealed class StairTowerWorld : World
    {
        public const string Name = "testadmin1";

        // the stairwells repeat every Segment tiles up (build_stair_tower.py, SEGMENT)
        private const int Segment = 8;
        private const int ClimbMs = 20000;

        private sealed class Floor
        {
            public int Number;
            public IntPoint BossAt;
            public Enemy Boss;
            public bool Open;
            public readonly List<IntPoint> Door = new List<IntPoint>();
        }

        private sealed class Stairs
        {
            public int To;      // the floor they lead to
            public int X0, X1;
            public int StartY, LoopY;
        }

        private sealed class Climb
        {
            public Stairs Stairs;
            public long Since;
            public bool Through;
            // just moved back down: moves sent before the client had the Goto still put it at the
            // loop row, so it isn't looped again until it's seen below it
            public bool Moved;
        }

        private readonly List<Floor> _floors = new List<Floor>();
        private readonly List<Stairs> _stairs = new List<Stairs>();
        private readonly Dictionary<int, Climb> _climbs = new Dictionary<int, Climb>();

        public StairTowerWorld(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent)
        {
        }

        public override void Init()
        {
            base.Init();
            var regions = Map.Regions;
            // floor 1 is the southernmost (the largest y)
            var n = 0;
            foreach (var at in regions.Where(r => r.Value == TileRegion.Tower_Boss).Select(r => r.Key).OrderByDescending(p => p.Y))
                _floors.Add(new Floor { Number = ++n, BossAt = at });
            // a door belongs to the floor south of it
            foreach (var at in regions.Where(r => r.Value == TileRegion.Tower_Door).Select(r => r.Key))
                _floors.Where(f => f.BossAt.Y > at.Y).OrderBy(f => f.BossAt.Y).FirstOrDefault()?.Door.Add(at);
            // a stairwell's loop row is the nearest above its first row
            var loops = regions.Where(r => r.Value == TileRegion.Stair_Loop).Select(r => r.Key).ToList();
            foreach (var row in regions.Where(r => r.Value == TileRegion.Stair_Start).Select(r => r.Key).GroupBy(p => p.Y).OrderByDescending(g => g.Key))
            {
                var loopY = loops.Where(p => p.Y < row.Key).Max(p => p.Y);
                _stairs.Add(new Stairs
                {
                    To = _stairs.Count + 2,
                    X0 = row.Min(p => p.X),
                    X1 = row.Max(p => p.X),
                    StartY = row.Key,
                    LoopY = loopY
                });
            }
        }

        protected override void UpdateLogic(ref TickTime time)
        {
            // before the monsters move: the bosses are found where the map put them, on the first
            // tick (map entities join the world then)
            foreach (var floor in _floors)
                if (!floor.Open)
                    WatchBoss(floor);
            base.UpdateLogic(ref time);
            foreach (var player in Players.Values)
                if (!player.Dead)
                    Climbing(player, time);
            foreach (var id in _climbs.Keys.Where(id => !Players.ContainsKey(id)).ToList())
                _climbs.Remove(id);
        }

        private void WatchBoss(Floor floor)
        {
            if (floor.Boss == null)
            {
                floor.Boss = Enemies.Values
                    .Where(e => !e.Dead && e.DistTo(floor.BossAt.X + 0.5f, floor.BossAt.Y + 0.5f) < 3)
                    .OrderBy(e => e.DistTo(floor.BossAt.X + 0.5f, floor.BossAt.Y + 0.5f))
                    .FirstOrDefault();
                return;
            }
            if (!floor.Boss.Dead && GetEntity(floor.Boss.Id) != null)
                return;
            floor.Open = true;
            foreach (var at in floor.Door)
            {
                var tile = Map[at.X, at.Y];
                tile.ObjType = 0;
                tile.UpdateCount++;
            }
            if (floor.Door.Count > 0)
                foreach (var player in Players.Values)
                    player.SendInfo($"The stairs up from floor {floor.Number} are open.");
        }

        private void Climbing(Player player, TickTime time)
        {
            var tx = (int)player.X;
            var ty = (int)player.Y;
            foreach (var stairs in _stairs)
            {
                if (tx < stairs.X0 || tx > stairs.X1)
                    continue;
                _climbs.TryGetValue(player.Id, out var climb);
                if (ty == stairs.StartY && climb?.Stairs != stairs)
                {
                    _climbs[player.Id] = new Climb { Stairs = stairs, Since = time.TotalElapsedMs };
                    Notify(player, "The stairs climb into the dark...", 0xFFB0B0C0);
                    return;
                }
                if (ty > stairs.LoopY + Segment / 2 || ty < stairs.LoopY - 2)
                {
                    if (climb?.Stairs == stairs && ty > stairs.LoopY)
                        climb.Moved = false;
                    continue;
                }
                // at the loop row
                if (climb?.Stairs != stairs)
                    _climbs[player.Id] = climb = new Climb { Stairs = stairs, Since = time.TotalElapsedMs };
                if (climb.Through || climb.Moved || ty > stairs.LoopY)
                    return;
                if (time.TotalElapsedMs - climb.Since >= ClimbMs)
                {
                    climb.Through = true;
                    Notify(player, $"Floor {stairs.To}", 0xFFFFD700);
                    return;
                }
                climb.Moved = true;
                player.Shift(time, new Position(player.X, player.Y + Segment));
                return;
            }
        }

        private static void Notify(Player player, string message, uint color) =>
            player.Client.SendPacket(new Notification { ObjectId = player.Id, Color = new ARGB(color), Message = message });
    }
}
