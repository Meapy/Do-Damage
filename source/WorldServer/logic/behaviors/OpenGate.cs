using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;


namespace WorldServer.logic.behaviors
{
    public class OpenGate : Behavior
    {
        private int xMin;
        private int xMax;
        private int yMin;
        private int yMax;

        private string target;
        private int area;

        private bool usearea;

        public OpenGate(int xMin, int xMax, int yMin, int yMax)
        {
            this.xMin = xMin;
            this.xMax = xMax;
            this.yMin = yMin;
            this.yMax = yMax;
        }

        public OpenGate(string target, int area = 10)
        {
            this.target = target;
            this.area = area;
            this.usearea = true;
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            // the squares' objects go with their ground back (Wmap.ClearObject), and the players are
            // told: with the object only cleared on the server, the Shatters' gates stayed shut for
            // everyone after its first boss
            var map = host.World.Map;
            var opened = false;
            if (usearea)
            {
                var type = host.GameServer.Resources.GameData.IdToObjectType[target];
                for (int x = (int)host.X - area; x <= (int)host.X + area; x++)
                {
                    for (int y = (int)host.Y - area; y <= (int)host.Y + area; y++)
                    {
                        if (map.Contains(x, y) && map[x, y].ObjType == type)
                        {
                            map.ClearObject(x, y);
                            opened = true;
                        }
                    }
                }
            }
            else
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        if (map.Contains(x, y) && map[x, y].ObjType != 0)
                        {
                            map.ClearObject(x, y);
                            opened = true;
                        }
                    }
                }
            }
            if (opened)
                foreach (var player in host.World.Players.Values)
                    player.UpdateTiles();
        }
    }
}