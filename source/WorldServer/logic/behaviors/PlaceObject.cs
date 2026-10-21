using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    // On entering its state, puts `objName` back on the map in a rectangle of tiles given relative
    // to the host's spawn point (dx0, dy0 to dx1, dy1), on each one that's empty and that no player
    // stands on: RemoveObject's opposite (Oryx 5 shuts his Holy Gates for his Last Judgement).
    internal class PlaceObject : Behavior
    {
        private readonly string _objName;
        private readonly int _dx0, _dy0, _dx1, _dy1;

        public PlaceObject(string objName, int dx0, int dy0, int dx1, int dy1)
        {
            _objName = objName;
            (_dx0, _dy0, _dx1, _dy1) = (Math.Min(dx0, dx1), Math.Min(dy0, dy1), Math.Max(dx0, dx1), Math.Max(dy0, dy1));
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var dat = host.GameServer.Resources.GameData;
            var objType = dat.IdToObjectType[_objName];
            var map = host.World.Map;
            var spawn = host.SpawnPoint.Value;
            var players = host.World.GetPlayers().ToList();

            for (var y = (int)spawn.Y + _dy0; y <= (int)spawn.Y + _dy1; y++)
                for (var x = (int)spawn.X + _dx0; x <= (int)spawn.X + _dx1; x++)
                {
                    if (!map.Contains(x, y) || map[x, y].ObjType != 0)
                        continue;
                    if (players.Any(p => (int)p.X == x && (int)p.Y == y))
                        continue;

                    var tile = map[x, y];
                    tile.ObjType = objType;
                    tile.ObjDesc = dat.ObjectDescs[objType];
                    tile.UpdateCount++;
                }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        { }
    }
}
