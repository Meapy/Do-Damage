using System;
using System.Collections.Generic;
using Shared;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.objects.connection;

namespace WorldServer.core.terrain
{
    // One tile's data, kept in Wmap's tile array. References (descriptions, configs) are stored as
    // indexes into the map's palettes so that the array holds no pointers for the GC to walk.
    internal struct WmapTileData
    {
        public int ObjId;
        public int ObjCfg;
        public ushort TileId;
        public ushort TileDesc;
        public ushort ObjType;
        public ushort ObjDesc;
        public ushort OriginalDesc;
        public byte Region; // TileRegion and TerrainType, a byte each in the map file
        public byte Terrain;
        public byte Elevation;
        public byte UpdateCount;
        public bool Spawned;
    }

    // A view of one tile of a map: reads and writes go to the map's arrays. The default value
    // stands for a position outside the map (Exists is false).
    public readonly struct WmapTile : IEquatable<WmapTile>
    {
        private readonly Wmap _map;
        private readonly int _index;

        internal WmapTile(Wmap map, int index)
        {
            _map = map;
            _index = index;
        }

        public bool Exists => _map != null;

        private ref WmapTileData D => ref _map.TileData[_index];

        public ushort TileId { get => D.TileId; set => D.TileId = value; }
        public TileDesc TileDesc { get => _map.TileDescs[D.TileDesc]; set => D.TileDesc = (ushort)_map.TileDescs.IndexOf(value); }

        // static entity stuff
        public int ObjId { get => D.ObjId; set => D.ObjId = value; }
        public ushort ObjType { get => D.ObjType; set => D.ObjType = value; }
        public string ObjCfg { get => _map.ObjCfgs[D.ObjCfg]; set => D.ObjCfg = _map.ObjCfgs.IndexOf(value); }
        public ObjectDesc ObjDesc { get => _map.ObjDescs[D.ObjDesc]; set => D.ObjDesc = (ushort)_map.ObjDescs.IndexOf(value); }

        public TileRegion Region { get => (TileRegion)D.Region; set => D.Region = (byte)value; }
        public TerrainType Terrain { get => (TerrainType)D.Terrain; set => D.Terrain = (byte)value; }
        public byte Elevation { get => D.Elevation; set => D.Elevation = value; }
        public byte UpdateCount { get => D.UpdateCount; set => D.UpdateCount = value; }
        public bool Spawned { get => D.Spawned; set => D.Spawned = value; }

        public bool Equals(WmapTile other) => _map == other._map && _index == other._index;
        public override bool Equals(object obj) => obj is WmapTile other && Equals(other);
        public override int GetHashCode() => _index;

        internal void Init(WmapDesc desc, ushort descIndex)
        {
            D.OriginalDesc = descIndex;
            TileId = desc.TileId;
            TileDesc = desc.TileDesc;
            ObjType = desc.ObjType;
            ObjDesc = desc.ObjDesc;
            ObjCfg = desc.ObjCfg;
            Terrain = desc.Terrain;
            Region = desc.Region;
            Elevation = desc.Elevation;
            UpdateCount = 2;
        }

        // Puts the tile back as the map file had it (keeping its object description), as a fresh
        // tile: no object id and not spawned.
        public void Restore()
        {
            var desc = _map.Descs[D.OriginalDesc];
            TileId = desc.TileId;
            TileDesc = desc.TileDesc;
            ObjType = desc.ObjType;
            ObjCfg = desc.ObjCfg;
            Terrain = desc.Terrain;
            Region = desc.Region;
            Elevation = desc.Elevation;
            ObjId = 0;
            Spawned = false;
            UpdateCount++;
            _map.ObjectDefs.Remove(_index);
        }

        // The ground the map file gives the tile: under a connected wall the loader puts the
        // Composite ground (0xFD) instead, which shows black once the wall is taken away.
        public void RestoreGround()
        {
            var desc = _map.Descs[D.OriginalDesc];
            TileId = desc.TileId;
            TileDesc = desc.TileDesc;
        }

        public void InitConnection(Wmap map, int x, int y)
        {
            var objDesc = ObjDesc;
            var objCfg = ObjCfg;
            if (objDesc == null || !objDesc.Connects || objCfg.Contains("conn:"))
                return;

            var objType = ObjType;
            var connStr = ConnectionComputer.GetConnString((dx, dy) => map.Contains(x + dx, y + dy) && map[x + dx, y + dy].ObjType == objType);
            ObjCfg = $"{objCfg};{connStr};";
        }

        public void CopyTo(WmapTile tile)
        {
            tile.TileId = TileId;
            tile.TileDesc = TileDesc;
            tile.ObjType = ObjType;
            tile.ObjDesc = ObjDesc;
            tile.ObjCfg = ObjCfg;
            tile.Terrain = Terrain;
            tile.Region = Region;
            tile.Elevation = Elevation;
            tile._map.ObjectDefs.Remove(tile._index);
        }

        public ObjectDef ToObjectDef(int x, int y)
        {
            if (_map.ObjectDefs.TryGetValue(_index, out var cached))
                return cached;

            var stats = new List<KeyValuePair<StatDataType, object>>();

            var objCfg = ObjCfg;
            if (!string.IsNullOrEmpty(objCfg))
                foreach (var item in objCfg.Split(';'))
                {
                    var kv = item.Split(':');

                    switch (kv[0])
                    {
                        case "hp":
                            var hp = Utils.GetInt(kv[1]);
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.Health, hp));
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MaximumHealth, hp));
                            break;

                        case "name":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.Name, kv[1]));
                            break;

                        case "size":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.Size, Math.Min(500, Utils.GetInt(kv[1]))));
                            break;

                        case "eff":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.ConditionBatch1, Utils.GetInt(kv[1])));
                            break;

                        case "conn":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.ObjectConnection, Utils.GetInt(kv[1])));
                            break;

                        case "mtype":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MerchandiseType, Utils.GetInt(kv[1])));
                            break;

                        case "mcost":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MerchandisePrice, Math.Max(0, Utils.GetInt(kv[1]))));
                            break;

                        case "mcur":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MerchandiseCurrency, Utils.GetInt(kv[1])));
                            break;

                        case "mamnt":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MerchandiseCount, Utils.GetInt(kv[1])));
                            break;

                        case "mtime":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MerchandiseMinsLeft, Utils.GetInt(kv[1])));
                            break;

                        case "mdisc":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MerchandiseDiscount, Utils.GetInt(kv[1])));
                            break;

                        case "mrank":
                        case "stars":
                            stats.Add(new KeyValuePair<StatDataType, object>(StatDataType.MerchandiseRankReq, Utils.GetInt(kv[1])));
                            break;
                    }
                }

            var def = new ObjectDef()
            {
                ObjectType = ObjType,
                Stats = new ObjectStats()
                {
                    Id = ObjId,
                    X = x + 0.5f,
                    Y = y + 0.5f,
                    Stats = stats.ToArray()
                }
            };
            _map.ObjectDefs[_index] = def;
            return def;
        }
    }

    // The distinct values of a reference-typed tile field in one map; tiles store their index.
    // Index 0 is null.
    public sealed class WmapPalette<T> where T : class
    {
        private readonly List<T> _items = new List<T>() { null };
        private readonly Dictionary<T, int> _indexes = new Dictionary<T, int>();

        public T this[int index] => _items[index];

        public int IndexOf(T value)
        {
            if (value == null)
                return 0;
            if (!_indexes.TryGetValue(value, out var index))
            {
                index = _items.Count;
                _items.Add(value);
                _indexes[value] = index;
            }
            return index;
        }
    }
}
