using Ionic.Zlib;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using Shared;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.objects.connection;
using WorldServer.core.objects.vendors;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.core.terrain
{
    public class Wmap
    {
        public Dictionary<IntPoint, TileRegion> Regions { get; } = new Dictionary<IntPoint, TileRegion>();

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // The tiles, row by row (index y * Width + x); WmapTile is a view of one of them.
        internal WmapTileData[] TileData;
        internal List<WmapDesc> Descs;
        internal readonly WmapPalette<TileDesc> TileDescs = new WmapPalette<TileDesc>();
        internal readonly WmapPalette<ObjectDesc> ObjDescs = new WmapPalette<ObjectDesc>();
        internal readonly WmapPalette<string> ObjCfgs = new WmapPalette<string>();
        // Static objects' definitions, by tile index, made when a player first sees them.
        internal readonly Dictionary<int, ObjectDef> ObjectDefs = new Dictionary<int, ObjectDef>();
        private readonly World World;
        private XmlData XmlData => World.GameServer.Resources.GameData;
        private Tuple<IntPoint, ushort, string>[] Entities;

        public int Height { get; private set; }
        public int Width { get; private set; }
        // Outside the map, the tile doesn't exist (Exists is false).
        public WmapTile this[int x, int y] => Contains(x, y) ? new WmapTile(this, y * Width + x) : default;

        public Wmap(World world) => World = world;

        public bool Contains(float x, float y) => !(x < 0 || x >= Width || y < 0 || y >= Height);
        public bool Contains(int x, int y) => !(x < 0 || x >= Width || y < 0 || y >= Height);

        public int Load(Stream stream, int idBase)
        {
            var ver = stream.ReadByte();

            if (ver < 0 || ver > 2)
                throw new NotSupportedException($"WMap version {ver}");

            using (var rdr = new BinaryReader(new ZlibStream(stream, CompressionMode.Decompress)))
            {
                var dict = new List<WmapDesc>();
                var c = rdr.ReadInt16();

                for (var i = 0; i < c; i++)
                {
                    var desc = new WmapDesc()
                    {
                        TileId = rdr.ReadUInt16()
                    };

                    desc.TileDesc = XmlData.Tiles[desc.TileId];

                    var obj = rdr.ReadString();

                    desc.ObjType = 0;

                    if (XmlData.IdToObjectType.ContainsKey(obj))
                        desc.ObjType = XmlData.IdToObjectType[obj];
                    else if (!string.IsNullOrEmpty(obj))
                        Log.Warn($"Object: {obj} not found.");

                    desc.ObjCfg = rdr.ReadString();
                    desc.Terrain = (TerrainType)rdr.ReadByte();
                    desc.Region = (TileRegion)rdr.ReadByte();

                    if (ver == 1)
                        desc.Elevation = rdr.ReadByte();

                    XmlData.ObjectDescs.TryGetValue(desc.ObjType, out desc.ObjDesc);
                    dict.Add(desc);
                }

                Width = rdr.ReadInt32();
                Height = rdr.ReadInt32();

                TileData = new WmapTileData[Width * Height];
                Descs = dict;

                var enCount = 0;
                var entities = new List<Tuple<IntPoint, ushort, string>>();

                for (short y = 0; y < Height; y++)
                    for (short x = 0; x < Width; x++)
                    {
                        var descIndex = (ushort)rdr.ReadInt16();
                        var tile = new WmapTile(this, y * Width + x);
                        tile.Init(dict[descIndex], descIndex);
                        if (ver == 2)
                            tile.Elevation = rdr.ReadByte();

                        if (tile.Region != 0)
                            Regions.Add(new IntPoint(x, y), tile.Region);

                        var desc = tile.ObjDesc;

                        if (tile.ObjType != 0 && (desc == null || !desc.Static || desc.Enemy))
                        {
                            entities.Add(new Tuple<IntPoint, ushort, string>(new IntPoint(x, y), tile.ObjType, tile.ObjCfg));
                            if (desc == null || !(desc.Enemy && desc.Static))
                                tile.ObjType = 0;
                        }

                        if (tile.ObjType != 0 && (desc == null || !(desc.Enemy && desc.Static)))
                        {
                            enCount++;
                            tile.ObjId = idBase + enCount;
                        }

                        if (desc != null && desc.Connects)
                            tile.TileId = 0xFD;
                    }

                for (var x = 0; x < Width; x++)
                    for (var y = 0; y < Height; y++)
                        this[x, y].InitConnection(this, x, y);

                Entities = entities.ToArray();

                return enCount;
            }
        }

        // typically this method is used with setpieces. It's data is
        // copied to the supplied world at the said position
        public void ProjectOntoWorld(World world, IntPoint pos)
        {
            pos.X -= Width / 2;
            pos.Y -= Height / 2;
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    var projX = pos.X + x;
                    var projY = pos.Y + y;

                    if (!world.Map.Contains(projX, projY))
                        continue;

                    var tile = world.Map[projX, projY];
                    var spTile = this[x, y];

                    tile.ObjType = 0;
                    tile.UpdateCount++;
                    if (spTile.TileId == 0xFF)
                        continue;

                    spTile.CopyTo(tile);

                    if (spTile.ObjId != 0)
                        tile.ObjId = world.GetNextEntityId();

                    // A setpiece's own spawn tiles (the Avatar's arena has four) would make players
                    // enter the world on it.
                    if (tile.Region == TileRegion.Spawn)
                        tile.Region = TileRegion.None;

                    var at = new IntPoint(projX, projY);
                    if (tile.Region != 0)
                        world.Map.Regions[at] = tile.Region;
                    else
                        world.Map.Regions.Remove(at);
                }

            CreateEntities(pos);
        }


        public void CreateEntities(IntPoint offset = new IntPoint())
        {
            foreach (var i in Entities)
            {
                var entity = World.CreateNewEntity(i.Item2, i.Item1.X + 0.5f + offset.X, i.Item1.Y + 0.5f + offset.Y);

                if (i.Item3 != null)
                    foreach (var item in i.Item3.Split(';'))
                    {
                        var kv = item.Split(':');

                        var type = kv[0];
                        var value = kv.Length == 1 ? string.Empty : kv[1];

                        switch (type)
                        {
                            case "hp":
                                (entity as Enemy).Health = Utils.GetInt(value);
                                (entity as Enemy).MaxHealth = (entity as Enemy).Health;
                                break;

                            case "name":
                                entity.Name = value;
                                break;
                            case "size":
                                entity.Size = Math.Min(500, Utils.GetInt(value));
                                break;

                            case "eff":
                                entity.ApplyPermanentConditionEffect((ConditionEffectIndex)ulong.Parse(value));
                                break;

                            case "conn":
                                (entity as ConnectedObject).Connection = ConnectedObjectInfo.Infos[(uint)Utils.GetInt(value)];
                                break;

                            case "mtype":
                                (entity as SellableMerchant).Item = (ushort)Utils.GetInt(value);
                                break;

                            case "mcost":
                                (entity as SellableObject).Price = Math.Max(0, Utils.GetInt(value));
                                break;

                            case "mcur":
                                (entity as SellableObject).Currency = (CurrencyType)Utils.GetInt(value);
                                break;

                            case "mamnt":
                                (entity as SellableMerchant).Count = Utils.GetInt(value);
                                break;

                            case "mtime":
                                (entity as SellableMerchant).TimeLeft = Utils.GetInt(value);
                                break;

                            case "mdisc": // not implemented
                                break;

                            case "mrank":
                            case "stars": // provided for backwards compatibility with older maps
                                (entity as SellableObject).RankRequired = Utils.GetInt(value);
                                break;

                            case "xOffset":
                                var xo = float.Parse(value);
                                entity.Move(entity.X + xo, entity.Y);
                                break;

                            case "yOffset":
                                var yo = float.Parse(value);
                                entity.Move(entity.X, entity.Y + yo);
                                break;

                            case "mtax":
                                (entity as SellableObject).Tax = Utils.FromString(value);
                                break;
                        }
                    }
            }
        }

        public void Clear()
        {
            TileData = null;
            ObjectDefs.Clear();
        }
    }
}
