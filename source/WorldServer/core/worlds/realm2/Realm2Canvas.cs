using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using Shared.terrain;
using WorldServer.core.structures;

namespace WorldServer.core.worlds.realm2
{
    /// <summary>
    /// A rectangle of tiles to draw Realm 2 on, in world coordinates: the whole map when a Realm 2
    /// opens (Realm2Generator), or the square round an event's land when it comes (Realm2EventPainter),
    /// whose untouched tiles stay unset (the Empty ground) so the world keeps what's there.
    /// </summary>
    public sealed class Realm2Canvas
    {
        public const ushort Unset = 0xFF;

        public readonly int X0, Y0, W, H;
        public readonly TerrainTile[,] Tiles;
        public readonly bool[,] Reserved;           // links, arenas, clearings: no scenery or walls
        public readonly Random Rand;
        private readonly XmlData _data;
        private readonly int _seed;
        private readonly Dictionary<string, TerrainTile> _blank = new Dictionary<string, TerrainTile>();

        public Realm2Canvas(XmlData data, int seed, int x0, int y0, int w, int h, string fill)
        {
            _data = data;
            _seed = seed;
            Rand = new Random(seed);
            X0 = x0;
            Y0 = y0;
            W = w;
            H = h;
            Tiles = new TerrainTile[w, h];
            Reserved = new bool[w, h];
            var t = fill == null ? new TerrainTile { TileId = Unset, Name = "" } : Ground(fill);
            for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    Tiles[x, y] = t;
        }

        public bool Inside(int x, int y) => x >= X0 && y >= Y0 && x < X0 + W && y < Y0 + H;

        public ref TerrainTile At(int x, int y) => ref Tiles[x - X0, y - Y0];

        public bool IsReserved(int x, int y) => Reserved[x - X0, y - Y0];

        public void Reserve(int x, int y)
        {
            if (Inside(x, y))
                Reserved[x - X0, y - Y0] = true;
        }

        public bool IsSet(int x, int y) => Inside(x, y) && At(x, y).TileId != Unset;

        public TerrainTile Ground(string name)
        {
            if (!_blank.TryGetValue(name, out var t))
            {
                if (!_data.IdToTileType.TryGetValue(name, out var id))
                    throw new ArgumentException($"Realm 2: no ground '{name}'");
                t = new TerrainTile { TileId = id, Name = "", Terrain = TerrainType.None, Region = TileRegion.None };
                _blank[name] = t;
            }
            return t;
        }

        public ushort GroundId(string name) => Ground(name).TileId;

        public bool Walkable(int x, int y)
        {
            if (!Inside(x, y))
                return false;
            var id = At(x, y).TileId;
            return id != Unset && _data.Tiles.TryGetValue(id, out var desc) && !desc.NoWalk && desc.MaxDamage == 0;
        }

        public static double Dist(IntPoint c, double x, double y) => Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y));

        // ---- drawing (each keeps to the canvas; `keep` limits a shape to the tiles it accepts)

        public void Set(int x, int y, string ground, TerrainType terrain = TerrainType.None)
        {
            if (!Inside(x, y))
                return;
            var t = Ground(ground);
            t.Terrain = terrain;
            At(x, y) = t;
        }

        public void Disc(IntPoint c, double r, string ground, Func<int, int, bool> keep = null)
        {
            for (var x = (int)(c.X - r); x <= c.X + r; x++)
                for (var y = (int)(c.Y - r); y <= c.Y + r; y++)
                    if (Inside(x, y) && Dist(c, x, y) <= r && (keep == null || keep(x, y)))
                        Set(x, y, ground);
        }

        public void Annulus(IntPoint c, double r0, double r1, string ground)
        {
            for (var x = (int)(c.X - r1); x <= c.X + r1; x++)
                for (var y = (int)(c.Y - r1); y <= c.Y + r1; y++)
                    if (Inside(x, y) && Dist(c, x, y) >= r0 && Dist(c, x, y) <= r1)
                        Set(x, y, ground);
        }

        public void Circle(IntPoint c, double r, string ground) => Annulus(c, r - 0.5, r + 0.5, ground);

        public void Square(IntPoint c, int half, string ground) => Rect(c.X - half, c.Y - half, c.X + half, c.Y + half, ground);

        public void Rect(int x0, int y0, int x1, int y1, string ground)
        {
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    Set(x, y, ground);
        }

        public void Cross(IntPoint c, int reach, string ground, int half = 0)
        {
            for (var i = -reach; i <= reach; i++)
                for (var j = -half; j <= half; j++)
                {
                    Set(c.X + i, c.Y + j, ground);
                    Set(c.X + j, c.Y + i, ground);
                }
        }

        public void Line(double x0, double y0, double x1, double y1, string ground)
        {
            var steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2);
            for (var i = 0; i <= steps; i++)
            {
                var f = (double)i / Math.Max(1, steps);
                Set((int)Math.Round(x0 + (x1 - x0) * f), (int)Math.Round(y0 + (y1 - y0) * f), ground);
            }
        }

        public void Place(int x, int y, string obj)
        {
            if (Walkable(x, y))
                At(x, y).TileObj = obj;
        }

        public void Ring(IntPoint c, double r, int count, string obj, double turn = 0)
        {
            for (var i = 0; i < count; i++)
            {
                var a = Math.PI * 2 * (i + turn) / count;
                Place((int)Math.Round(c.X + Math.Cos(a) * r), (int)Math.Round(c.Y + Math.Sin(a) * r), obj);
            }
        }

        /// <summary>
        /// A way across the void from (ax, ay) to (bx, by), over the tiles `open` accepts: a plank
        /// bridge 5 wide with rails, or a pool to wade.
        /// </summary>
        public void Crossing(double ax, double ay, double bx, double by, bool pool, Func<int, int, bool> open)
        {
            var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            if (len < 1)
                return;
            double ux = (bx - ax) / len, uy = (by - ay) / len;
            var reach = pool ? 14 : 4;
            for (var x = (int)(Math.Min(ax, bx) - reach); x <= Math.Max(ax, bx) + reach; x++)
                for (var y = (int)(Math.Min(ay, by) - reach); y <= Math.Max(ay, by) + reach; y++)
                {
                    if (!Inside(x, y) || !open(x, y))
                        continue;
                    var t = (x - ax) * ux + (y - ay) * uy;
                    var side = Math.Abs((x - ax) * -uy + (y - ay) * ux);
                    if (t < -2 || t > len + 2)
                        continue;
                    if (pool)
                    {
                        // an oval across the gap, widest in the middle
                        var along = (t - len / 2) / (len / 2 + 4);
                        var across = side / (9 + (Noise(x, y, 0.2, 77) - 0.5) * 5);
                        if (along * along + across * across > 1)
                            continue;
                        Set(x, y, "R2 Pool Water");
                    }
                    else if (side <= 2.5)
                        Set(x, y, "R2 Bridge");
                    else if (side <= 3.5)
                        Set(x, y, "R2 Bridge Rail");
                    else
                        continue;
                    Reserve(x, y);
                }
        }

        public string Pick((string Id, double Weight)[] table)
        {
            var roll = Rand.NextDouble() * table.Sum(t => t.Weight);
            foreach (var (id, weight) in table)
            {
                roll -= weight;
                if (roll <= 0)
                    return id;
            }
            return table[^1].Id;
        }

        /// <summary>Smooth value noise in [0, 1], a few octaves (the same at a point however the canvas lies).</summary>
        public double Noise(double x, double y, double freq, int salt)
        {
            double sum = 0, amp = 1, norm = 0;
            for (var o = 0; o < 3; o++)
            {
                sum += amp * Value(x * freq, y * freq, salt + o * 101);
                norm += amp;
                amp *= 0.5;
                freq *= 2;
            }
            return sum / norm;
        }

        private double Value(double x, double y, int salt)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var a = Hash(x0, y0, salt);
            var b = Hash(x0 + 1, y0, salt);
            var c = Hash(x0, y0 + 1, salt);
            var d = Hash(x0 + 1, y0 + 1, salt);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        private double Hash(int x, int y, int salt)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + (salt + _seed) * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
            }
        }

        /// <summary>
        /// The canvas in the map format the worlds load (WorldMapExporter's), keyed through a
        /// dictionary: WorldMapExporter looks each tile up in a list, slow for a whole Realm 2.
        /// </summary>
        public byte[] Export()
        {
            var dict = new List<TerrainTile>();
            var index = new Dictionary<(ushort, string, TerrainType, TileRegion), short>();
            var dat = new byte[W * H * 3];
            var i = 0;
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                {
                    var t = Tiles[x, y];
                    var key = (t.TileId, t.TileObj ?? "", t.Terrain, t.Region);
                    if (!index.TryGetValue(key, out var n))
                    {
                        n = (short)dict.Count;
                        index[key] = n;
                        dict.Add(t);
                    }
                    dat[i] = (byte)(n & 0xff);
                    dat[i + 1] = (byte)(n >> 8);
                    dat[i + 2] = t.Elevation;
                    i += 3;
                }

            var ms = new System.IO.MemoryStream();
            using (var wtr = new System.IO.BinaryWriter(ms))
            {
                wtr.Write((short)dict.Count);
                foreach (var t in dict)
                {
                    wtr.Write(t.TileId);
                    wtr.Write(t.TileObj ?? "");
                    wtr.Write("");
                    wtr.Write((byte)t.Terrain);
                    wtr.Write((byte)t.Region);
                }
                wtr.Write(W);
                wtr.Write(H);
                wtr.Write(dat);
            }
            var buff = Ionic.Zlib.ZlibStream.CompressBuffer(ms.ToArray());
            var ret = new byte[buff.Length + 1];
            Buffer.BlockCopy(buff, 0, ret, 1, buff.Length);
            ret[0] = 2;
            return ret;
        }
    }
}
