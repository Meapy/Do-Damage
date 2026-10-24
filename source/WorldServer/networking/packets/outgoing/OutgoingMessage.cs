using Shared;
using NLog;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Text;

namespace WorldServer.networking.packets.outgoing
{
    public abstract class OutgoingMessage
    {

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public abstract MessageId MessageId { get; }

        // one stream and writer per thread, reused for every message it writes (a new pair per
        // message, for every player every tick, was a good part of the server's garbage)
        [ThreadStatic]
        private static MemoryStream _stream;
        [ThreadStatic]
        private static NetworkWriter _writer;

        public int? Write(byte[] buff, int offset)
        {
            var s = _stream ??= new MemoryStream(4096);
            var wtr = _writer ??= new NetworkWriter(s);
            s.Position = 0;
            s.SetLength(0);

            Write(wtr);
            wtr.Flush();

            var bodyLength = (int)s.Position;
            var packetLength = bodyLength + 5;

            if (packetLength > buff.Length - offset)
                return 0;

            try
            {
                Buffer.BlockCopy(s.GetBuffer(), 0, buff, offset + 5, bodyLength);
                BinaryPrimitives.WriteInt32BigEndian(buff.AsSpan(offset, 4), packetLength);
                buff[offset + 4] = (byte)MessageId;
            }
            catch (ArgumentOutOfRangeException)
            {
                return 0;
            }
            finally
            {
                // a huge message (a big map's tiles) needn't keep its buffer for good
                if (s.Capacity > 1 << 20)
                {
                    _stream = null;
                    _writer = null;
                }
            }
            return packetLength;
        }

        public abstract void Write(NetworkWriter wtr);

        public override string ToString()
        {
            var ret = new StringBuilder("{");
            var arr = GetType().GetProperties();
            for (var i = 0; i < arr.Length; i++)
            {
                if (i != 0) ret.Append(", ");
                ret.AppendFormat("{0}: {1}", arr[i].Name, arr[i].GetValue(this, null));
            }
            ret.Append("}");
            return ret.ToString();
        }
    }
}
