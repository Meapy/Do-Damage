using System;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // Floods an arena from its edges: on entering the state, the arena's floor (the ground at its
    // centre, CircleArena.Centre) from `radius` out to `reach` tiles becomes `ground`. The Crystal
    // Worm Mother's chamber fills with Crystallised Water this way as she loses health (RealmEye),
    // each flood leaving less room to move.
    internal class FloodArena : Behavior
    {
        private readonly string _ground;
        private readonly double _radius;
        private readonly double _reach;

        public FloodArena(string ground, double radius, double reach = 18)
        {
            _ground = ground;
            _radius = radius;
            _reach = reach;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var world = host.World;
            if (world == null || !host.GameServer.Resources.GameData.IdToTileType.TryGetValue(_ground, out var water))
                return;
            var centre = CircleArena.Centre(host);
            int cx = (int)centre.X, cy = (int)centre.Y;
            if (!world.Map.Contains(cx, cy))
                return;
            var floor = world.Map[cx, cy].TileId;
            var r = (int)Math.Ceiling(_reach);
            for (var x = cx - r; x <= cx + r; x++)
                for (var y = cy - r; y <= cy + r; y++)
                {
                    if (!world.Map.Contains(x, y))
                        continue;
                    var dx = x + 0.5 - centre.X;
                    var dy = y + 0.5 - centre.Y;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    var tile = world.Map[x, y];
                    if (d < _radius || d > _reach || tile.TileId != floor || tile.ObjType != 0)
                        continue;
                    tile.TileId = water;
                    tile.UpdateCount++;
                }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
        }
    }
}
