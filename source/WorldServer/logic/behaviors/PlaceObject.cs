using System;
using System.Collections.Generic;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // On entering its state, puts `objName` back on the map in a rectangle of tiles given relative
    // to the host's spawn point (dx0, dy0 to dx1, dy1), on each one that's empty: RemoveObject's
    // opposite (Oryx 5 shuts his Holy Gates). A tile a player stands on is filled as soon as they
    // step off it, while the state lasts; with `clear`, players in the rectangle are first moved out
    // of it toward the spawn point, so nobody is left on the wrong side. With `removeOnExit` the
    // tiles it filled are emptied again when the state ends.
    internal class PlaceObject : Behavior
    {
        private readonly string _objName;
        private readonly int _dx0, _dy0, _dx1, _dy1;
        private readonly bool _clear;
        private readonly bool _removeOnExit;

        public PlaceObject(string objName, int dx0, int dy0, int dx1, int dy1, bool clear = false, bool removeOnExit = false)
        {
            _objName = objName;
            (_dx0, _dy0, _dx1, _dy1) = (Math.Min(dx0, dx1), Math.Min(dy0, dy1), Math.Max(dx0, dx1), Math.Max(dy0, dy1));
            _clear = clear;
            _removeOnExit = removeOnExit;
        }

        private sealed class Tiles
        {
            public List<(int X, int Y)> Pending = new List<(int, int)>();
            public List<(int X, int Y)> Placed = new List<(int, int)>();
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var spawn = host.SpawnPoint.Value;
            var (x0, y0) = ((int)spawn.X + _dx0, (int)spawn.Y + _dy0);
            var (x1, y1) = ((int)spawn.X + _dx1, (int)spawn.Y + _dy1);
            bool Inside(float x, float y) => (int)x >= x0 && (int)x <= x1 && (int)y >= y0 && (int)y <= y1;

            if (_clear)
                foreach (var player in host.World.Players.Values.Where(p => Inside(p.X, p.Y)))
                {
                    // step toward the spawn point until clear of the rectangle, onto an empty tile
                    var a = Math.Atan2(spawn.Y - player.Y, spawn.X - player.X);
                    var (x, y) = (player.X, player.Y);
                    for (var i = 0; i < 40 && (Inside(x, y) || host.World.Map[(int)x, (int)y].ObjType != 0); i++)
                        (x, y) = (x + (float)Math.Cos(a) * 0.5f, y + (float)Math.Sin(a) * 0.5f);
                    player.TeleportPosition(time, new Position((int)x + 0.5f, (int)y + 0.5f), true);
                }

            var tiles = new Tiles();
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    tiles.Pending.Add((x, y));
            state = tiles;
            Place(host, tiles);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state is Tiles tiles && tiles.Pending.Count > 0)
                Place(host, tiles);
        }

        protected override void OnStateExit(Entity host, TickTime time, ref object state)
        {
            if (_removeOnExit && state is Tiles tiles)
            {
                var objType = host.GameServer.Resources.GameData.IdToObjectType[_objName];
                foreach (var (x, y) in tiles.Placed)
                {
                    var tile = host.World.Map[x, y];
                    if (tile.ObjType != objType)
                        continue;
                    tile.ObjType = 0;
                    tile.UpdateCount++;
                }
            }
            state = null;
        }

        private void Place(Entity host, Tiles tiles)
        {
            var dat = host.GameServer.Resources.GameData;
            var objType = dat.IdToObjectType[_objName];
            var map = host.World.Map;

            for (var i = tiles.Pending.Count - 1; i >= 0; i--)
            {
                var (x, y) = tiles.Pending[i];
                if (map.Contains(x, y) && map[x, y].ObjType == 0)
                {
                    if (host.World.Players.Values.Any(p => (int)p.X == x && (int)p.Y == y))
                        continue;
                    var tile = map[x, y];
                    tile.ObjType = objType;
                    tile.ObjDesc = dat.ObjectDescs[objType];
                    tile.UpdateCount++;
                    tiles.Placed.Add((x, y));
                }
                tiles.Pending.RemoveAt(i);
            }
        }
    }
}
