using System;
using System.Collections.Generic;
using WorldServer.core.structures;

namespace WorldServer.core.worlds.census
{
    //TODO: thread-safe?
    public class CollisionMap<T> where T : ICollidable<T>
    {
        public const int CHUNK_SIZE = 16;

        private const int ACTIVE_RADIUS = 3;

        private readonly int cH;
        private readonly int cW;
        private readonly byte type;

        private CollisionNode<T>[,] chunks;
        private int h;
        private int w;

        public CollisionMap(byte type, int w, int h)
        {
            this.type = type;
            this.w = w;
            this.h = h;

            chunks = new CollisionNode<T>[
                cW = (w + CHUNK_SIZE - 1) / CHUNK_SIZE,
                cH = (h + CHUNK_SIZE - 1) / CHUNK_SIZE];
        }

        public HashSet<T> GetActiveChunks(CollisionMap<T> from)
        {
            if (from.w != w || from.h != h)
                throw new ArgumentException("from");

            // reused, as GetChunksAround's is: a new set every tick of every world was garbage
            var ret = _around;
            ret.Clear();

            for (var y = 0; y < cH; y++)
                for (var x = 0; x < cW; x++)
                    if (from.chunks[x, y] != null)
                    {
                        for (var i = -ACTIVE_RADIUS; i <= ACTIVE_RADIUS; i++)
                            for (var j = -ACTIVE_RADIUS; j <= ACTIVE_RADIUS; j++)
                            {
                                if (x + j < 0 || x + j >= cW || y + i < 0 || y + i >= cH)
                                    continue;

                                var node = chunks[x + j, y + i];
                                while (node != null)
                                {
                                    ret.Add((T)node.Parent);
                                    node = node.Next;
                                }
                            }
                    }

            return ret;
        }

        private readonly HashSet<T> _around = new HashSet<T>();

        /// <summary>
        /// The entities in the chunks within <c>radius</c> chunks of each point, like
        /// GetActiveChunks but with a radius per point. The set is reused by the next call.
        /// </summary>
        public HashSet<T> GetChunksAround(IEnumerable<(float X, float Y, int Radius)> points)
        {
            _around.Clear();
            foreach (var (px, py, r) in points)
            {
                var cx = (int)px / CHUNK_SIZE;
                var cy = (int)py / CHUNK_SIZE;
                for (var y = Math.Max(0, cy - r); y <= Math.Min(cH - 1, cy + r); y++)
                    for (var x = Math.Max(0, cx - r); x <= Math.Min(cW - 1, cx + r); x++)
                        for (var node = chunks[x, y]; node != null; node = node.Next)
                            _around.Add((T)node.Parent);
            }
            return _around;
        }

        public Area HitTest(Position pos, double radius) => HitTest(pos.X, pos.Y, radius);

        /// <summary>
        /// Everything in the chunks within <paramref name="radius"/> of the point. A struct, so a
        /// foreach over it allocates nothing: monsters' AI asks this many times a tick.
        /// </summary>
        public Area HitTest(double _x, double _y, double radius)
        {
            int xl = Math.Max(0, (int)(_x - radius) / CHUNK_SIZE);
            int xh = Math.Min(cW - 1, (int)(_x + radius) / CHUNK_SIZE);
            int yl = Math.Max(0, (int)(_y - radius) / CHUNK_SIZE);
            int yh = Math.Min(cH - 1, (int)(_y + radius) / CHUNK_SIZE);
            return new Area(chunks, xl, xh, yl, yh);
        }

        public readonly struct Area : IEnumerable<T>
        {
            private readonly CollisionNode<T>[,] _chunks;
            private readonly int _xl, _xh, _yl, _yh;

            public Area(CollisionNode<T>[,] chunks, int xl, int xh, int yl, int yh)
            {
                _chunks = chunks;
                _xl = xl;
                _xh = xh;
                _yl = yl;
                _yh = yh;
            }

            public Enumerator GetEnumerator() => new Enumerator(_chunks, _xl, _xh, _yl, _yh);

            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            // chunk by chunk, row by row, each chunk's list in order, as the iterator before it did
            public struct Enumerator : IEnumerator<T>
            {
                private readonly CollisionNode<T>[,] _chunks;
                private readonly int _xl, _xh, _yh;
                private int _x, _y;
                private CollisionNode<T> _next;

                public Enumerator(CollisionNode<T>[,] chunks, int xl, int xh, int yl, int yh)
                {
                    _chunks = chunks;
                    _xl = xl;
                    _xh = xh;
                    _yh = yh;
                    _x = xl - 1;
                    _y = yl;
                    _next = null;
                    Current = default;
                }

                public T Current { get; private set; }

                object System.Collections.IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    while (_next == null)
                    {
                        if (++_x > _xh)
                        {
                            _x = _xl;
                            _y++;
                        }
                        if (_y > _yh || _xl > _xh)
                            return false;
                        _next = _chunks[_x, _y];
                    }
                    Current = (T)_next.Parent;
                    _next = _next.Next;
                    return true;
                }

                public void Reset() => throw new NotSupportedException();

                public void Dispose() { }
            }
        }

        public IEnumerable<T> HitTest(double _x, double _y)
        {
            if (_x < 0 || _x >= w || _y <= 0 || _y >= h)
                yield break;

            var x = (int)_x / CHUNK_SIZE;
            var y = (int)_y / CHUNK_SIZE;
            var node = chunks[x, y];

            while (node != null)
            {
                yield return (T)node.Parent;
                node = node.Next;
            }
        }

        public void Insert(T obj, float newX, float newY)
        {
            if (obj.CollisionNode != null)
                return;

            var x = (int)(newX / CHUNK_SIZE);
            var y = (int)(newY / CHUNK_SIZE);

            obj.CollisionNode = new CollisionNode<T>()
            {
                Data = GetData(x, y),
                Parent = obj
            };
            obj.Parent = this;

            if (chunks[x, y] == null)
                chunks[x, y] = obj.CollisionNode;
            else
                chunks[x, y].InsertAfter(obj.CollisionNode);
        }

        public void Move(T obj, float newX, float newY)
        {
            if (obj.CollisionNode == null)
            {
                Insert(obj, newX, newY);
                return;
            }

            if (obj.Parent != this)
                return;

            var x = (int)(newX / CHUNK_SIZE);
            var y = (int)(newY / CHUNK_SIZE);
            var newDat = GetData(x, y);

            if (obj.CollisionNode.Data != newDat)
            {
                var oldX = (int)(obj.X / CHUNK_SIZE);
                var oldY = (int)(obj.Y / CHUNK_SIZE);

                if (chunks[oldX, oldY] == obj.CollisionNode)
                    chunks[oldX, oldY] = obj.CollisionNode.Remove();
                else
                    obj.CollisionNode.Remove();

                if (chunks[x, y] == null)
                    chunks[x, y] = obj.CollisionNode;
                else
                    chunks[x, y].InsertAfter(obj.CollisionNode);

                obj.CollisionNode.Data = newDat;
            }
        }

        public void Remove(T obj)
        {
            if (obj.CollisionNode == null)
                return;

            if (obj.Parent != this)
                return;

            var x = (int)(obj.X / CHUNK_SIZE);
            var y = (int)(obj.Y / CHUNK_SIZE);

            if (chunks[x, y] == obj.CollisionNode)
                chunks[x, y] = obj.CollisionNode.Remove();
            else
                obj.CollisionNode.Remove();
        }

        private int GetData(int chunkX, int chunkY) => chunkX | chunkY << 8 | type << 16;
    }
}
