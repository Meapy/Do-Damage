using System;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.core.setpieces
{
    /// <summary>
    /// A realm event that brings a patch of its Realm 2 land into the realm: a ragged circle of the
    /// land's grounds (its arena's at the heart), a scatter of its scenery towards the edge, and the
    /// boss in the middle. Only open ground is painted, so trees, walls and water stay as they were.
    /// </summary>
    public sealed class RealmEventGrounds : ISetPiece
    {
        private const int Radius = 13;
        private const int ArenaRadius = 6;

        private readonly string _boss;
        private readonly string _ground, _patches, _arena;
        private readonly string[] _decor;

        private RealmEventGrounds(string boss, string ground, string patches, string arena, params string[] decor)
        {
            _boss = boss;
            _ground = ground;
            _patches = patches;
            _arena = arena;
            _decor = decor;
        }

        public static RealmEventGrounds ThievesHoard() =>
            new RealmEventGrounds("Gilded Thief", "R2 Bazaar Sand", "R2 Coin Strewn", "R2 Bazaar Carpet",
                "R2 Coin Pile", "R2 Coin Pile", "R2 Loot Chest", "R2 Bazaar Tent", "R2 Gilded Urn");

        public static RealmEventGrounds StarfallCrater() =>
            new RealmEventGrounds("Starborn Colossus", "R2 Starlit Rock", "R2 Stardust Ground", "R2 Crater Glass",
                "R2 Meteor Rock", "R2 Meteor Rock", "R2 Star Shard", "R2 Stardust");

        public static RealmEventGrounds Blightwood() =>
            new RealmEventGrounds("Blight Mother", "R2 Blight Moss", "R2 Mycelium", "R2 Rot Soil",
                "R2 Giant Toadstool", "R2 Blight Puffball", "R2 Spore Patch", "R2 Glowcap", "R2 Rot Stump");

        public override int Size => Radius * 2 + 1;

        public override void RenderSetPiece(World world, IntPoint pos)
        {
            var data = world.GameServer.Resources.GameData;
            var cx = pos.X + Radius;
            var cy = pos.Y + Radius;
            // a wobble on the edge, so it isn't a perfect circle
            var phase = Random.Shared.NextDouble() * Math.PI * 2;

            for (var x = pos.X; x < pos.X + Size; x++)
                for (var y = pos.Y; y < pos.Y + Size; y++)
                {
                    if (!world.Map.Contains(x, y))
                        continue;
                    var dx = x - cx;
                    var dy = y - cy;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    var edge = Radius - 1.5 + Math.Sin(Math.Atan2(dy, dx) * 5 + phase) * 1.5;
                    if (d > edge || !world.IsPassable(x, y))
                        continue;

                    var tile = world.Map[x, y];
                    var ground = d <= ArenaRadius ? _arena : Random.Shared.NextDouble() < 0.3 ? _patches : _ground;
                    if (data.IdToTileType.TryGetValue(ground, out var groundType))
                        tile.TileId = groundType;
                    tile.ObjType = 0;
                    if (d > ArenaRadius + 2 && Random.Shared.NextDouble() < 0.07
                        && data.IdToObjectType.TryGetValue(_decor[Random.Shared.Next(_decor.Length)], out var decor))
                    {
                        tile.ObjType = decor;
                        tile.ObjDesc = data.ObjectDescs[decor];
                        if (tile.ObjId == 0)
                            tile.ObjId = world.GetNextEntityId();
                    }
                    tile.UpdateCount++;
                }

            // players only get tiles again when they step onto another one: send the new ground to
            // whoever already sees it
            foreach (var player in world.Players.Values)
                player.UpdateTiles();

            var boss = Entity.Resolve(world.GameServer, _boss);
            if (boss == null)
                return;
            boss.Move(cx + 0.5f, cy + 0.5f);
            world.EnterWorld(boss);
        }
    }
}
