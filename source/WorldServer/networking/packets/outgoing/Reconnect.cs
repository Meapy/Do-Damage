using Shared;
using System;

namespace WorldServer.networking.packets.outgoing
{
    public class Reconnect : OutgoingMessage
    {
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public int GameId { get; set; }
        public int KeyTime { get; set; }
        public byte[] Key { get; private set; }

        public override MessageId MessageId => MessageId.RECONNECT;

        public Reconnect()
        {
            Key = Guid.NewGuid().ToByteArray();
        }

        // An empty key: the client joins as from the character screen (the Nexus), so it can
        // land on another world server than the one that sent this.
        public void ClearKey() => Key = Array.Empty<byte>();

        // A key another world server keeps for this player (a visit between servers).
        public void SetKey(byte[] key) => Key = key;

        public override void Write(NetworkWriter wtr)
        {
            wtr.WriteUTF16(Name);
            wtr.WriteUTF16(Host);
            wtr.Write(Port);
            wtr.Write(GameId);
            wtr.Write(KeyTime);
            wtr.Write((short)Key.Length);
            wtr.Write(Key);
        }
    }
}
