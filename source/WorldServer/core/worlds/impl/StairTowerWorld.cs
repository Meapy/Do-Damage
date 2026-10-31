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
    /// The Stair Tower (tools/build_stair_tower.py), the prototype of real height: floors stacked
    /// one above another over the same ground (World.Levels), each with its boss, joined by
    /// stairs. Admins open it with /dungeon testadmin1. Everything here has a height (Entity.Z):
    /// it walks on the floor at its height, climbs the stairs' rising ground, and neither sees
    /// nor reaches what is on another floor. Its map is the lowest floor; "&lt;map&gt; 1.jm",
    /// "&lt;map&gt; 2.jm" and so on are the ones above, <see cref="FloorHeight"/> tiles apart. Here:
    /// - a floor's door (Tower_Door tiles on that floor) opens when its boss (the monster that
    ///   stood on its Tower_Boss tile) is gone;
    /// - players are told the floor they reach the first time.
    /// </summary>
    public sealed class StairTowerWorld : World
    {
        public const string Name = "testadmin1";

        // build_stair_tower.py, FLOOR_HEIGHT
        private const float FloorHeight = 3;

        private sealed class Floor
        {
            public int Level;
            public IntPoint BossAt;
            public Enemy Boss;
            public bool Open;
            public readonly List<IntPoint> Door = new List<IntPoint>();
        }

        private readonly List<Floor> _floors = new List<Floor>();
        // the highest floor each player has reached
        private readonly Dictionary<int, int> _reached = new Dictionary<int, int>();

        public StairTowerWorld(GameServer gameServer, int id, WorldResource resource, World parent)
            : base(gameServer, id, resource, parent)
        {
        }

        public override bool LoadMapFromData(WorldResource resource)
        {
            if (!base.LoadMapFromData(resource))
                return false;
            StackLevels(FloorHeight);
            var map = resource.MapJM[0][..^".jm".Length];
            for (var i = 1; AddLevel($"{map} {i}.jm"); i++)
            {
            }
            return true;
        }

        public override void Init()
        {
            base.Init();
            for (var level = 0; level < Levels.Count; level++)
            {
                var regions = Levels[level].Regions;
                foreach (var at in regions.Where(r => r.Value == TileRegion.Tower_Boss).Select(r => r.Key))
                    _floors.Add(new Floor { Level = level, BossAt = at });
                var floor = _floors.FirstOrDefault(f => f.Level == level);
                foreach (var at in regions.Where(r => r.Value == TileRegion.Tower_Door).Select(r => r.Key))
                    floor?.Door.Add(at);
            }
        }

        public override void AddToWorld(Entity entity)
        {
            base.AddToWorld(entity);
            if (entity is Player player)
                player.Client.SendPacket(Layers());
        }

        private LayersMessage Layers()
        {
            var msg = new LayersMessage { Height = LevelHeight };
            for (var level = 1; level < Levels.Count; level++)
            {
                var map = Levels[level];
                for (var y = 0; y < map.Height; y++)
                    for (var x = 0; x < map.Width; x++)
                    {
                        var tile = map[x, y];
                        if (tile.TileId != 0xFF)
                            msg.Tiles.Add(((byte)level, (short)x, (short)y, tile.TileId));
                    }
            }
            return msg;
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
                    Arrived(player);
            foreach (var id in _reached.Keys.Where(id => !Players.ContainsKey(id)).ToList())
                _reached.Remove(id);
        }

        private void WatchBoss(Floor floor)
        {
            var z = floor.Level * LevelHeight;
            if (floor.Boss == null)
            {
                floor.Boss = Enemies.Values
                    .Where(e => !e.Dead && System.MathF.Abs(e.Z - z) < 0.5f && e.SqDistTo(floor.BossAt.X + 0.5f, floor.BossAt.Y + 0.5f) < 9)
                    .OrderBy(e => e.SqDistTo(floor.BossAt.X + 0.5f, floor.BossAt.Y + 0.5f))
                    .FirstOrDefault();
                return;
            }
            if (!floor.Boss.Dead && GetEntity(floor.Boss.Id) != null)
                return;
            floor.Open = true;
            foreach (var at in floor.Door)
            {
                var tile = Levels[floor.Level][at.X, at.Y];
                tile.ObjType = 0;
                tile.UpdateCount++;
            }
            if (floor.Door.Count > 0)
                foreach (var player in Players.Values)
                    player.SendInfo($"The stairs up from floor {floor.Level + 1} are open.");
        }

        private void Arrived(Player player)
        {
            var level = LevelAt((int)player.X, (int)player.Y, player.Z);
            // standing on a floor itself, not on its stairs
            if (level < 0 || System.MathF.Abs(player.Z - level * LevelHeight) > 0.1f)
                return;
            _reached.TryGetValue(player.Id, out var best);
            if (level <= best)
                return;
            _reached[player.Id] = level;
            player.Client.SendPacket(new Notification { ObjectId = player.Id, Color = new ARGB(0xFFFFD700), Message = $"Floor {level + 1}" });
        }
    }
}
