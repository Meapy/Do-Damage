using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        public const int DcThresold = 120000000;

        public int LastClientTime = -1;
        public long LastServerTime = -1;

        private const int PingPeriod = 1000;

        private ConcurrentQueue<int> _clientTimeLog = new ConcurrentQueue<int>();
        private int _cnt;
        private ConcurrentQueue<long> _gotoAckTimeout = new ConcurrentQueue<long>();
        private long _latSum;
        private ConcurrentQueue<int> _move = new ConcurrentQueue<int>();
        private long _pingTime = -1;
        private long _pongTime = -1;
        private ConcurrentQueue<int> _serverTimeLog = new ConcurrentQueue<int>();
        private ConcurrentQueue<long> _shootAckTimeout = new ConcurrentQueue<long>();
        private long _sum;
        public int _tps;
        private ConcurrentQueue<long> _updateAckTimeout = new ConcurrentQueue<long>();

        public int Latency { get; private set; }

        /// <summary>The recent round trip to the player's client, ms (sent in each Ping for its ping display).</summary>
        public int RoundTrip { get; private set; } = -1;
        public long TimeMap { get; private set; }

        public void AwaitGotoAck(long serverTime) => _gotoAckTimeout.Enqueue(serverTime + DcThresold);

        public void AwaitMove(int tickId) => _move.Enqueue(tickId);

        public long C2STime(int clientTime) => clientTime + TimeMap;
        public long S2CTime(int serverTime) => serverTime - TimeMap;

        public int GotoAckCount() => _gotoAckTimeout.Count;

        public void GotoAckReceived()
        {
            if (!_gotoAckTimeout.TryDequeue(out var ignored))
                Client.Disconnect("One too many GotoAcks");
        }

        public void MoveReceived(TickTime ticKTime, int time, int moveTickId)
        {
            if (!_move.TryDequeue(out var tickId))
            {
                Client.Disconnect("One too many MovePackets");
                return;
            }

            if (tickId != moveTickId)
            {
                Client.Disconnect("[NewTick -> Move] TickIds don't match");
                return;
            }

            if (moveTickId > TickId)
            {
                Client.Disconnect("[NewTick -> Move] Invalid tickId");
                return;
            }

            var lastClientTime = LastClientTime;
            var lastServerTime = LastServerTime;

            LastClientTime = time;
            LastServerTime = ticKTime.TotalElapsedMs;

            if (lastClientTime == -1)
                return;

            _clientTimeLog.Enqueue(time - lastClientTime);
            _serverTimeLog.Enqueue((int)(ticKTime.TotalElapsedMs - lastServerTime));

            if (_clientTimeLog.Count < 30)
                return;

            if (_clientTimeLog.Count > 30)
            {
                _clientTimeLog.TryDequeue(out var ignore);
                _serverTimeLog.TryDequeue(out ignore);
            }
        }

        private long _lastMoveRejectTime = -1;

        public void RejectMove(TickTime tickTime, float x, float y)
        {
            // at most one snap-back a second, so the client has time to take it
            if (_lastMoveRejectTime != -1 && tickTime.TotalElapsedMs - _lastMoveRejectTime < 1000)
                return;
            _lastMoveRejectTime = tickTime.TotalElapsedMs;

            StaticLogger.Instance.Info($"{Name} is walking on an occupied tile. {x}, {y}; sent back to {X}, {Y}");
            AwaitGotoAck(tickTime.TotalElapsedMs);
            Client.SendPacket(new GotoMessage(Id, new Position(X, Y)));
        }

        public void Pong(TickTime tickTime, int time, int serial)
        {
            _cnt++;

            _sum += tickTime.TotalElapsedMs - time;
            TimeMap = _sum / _cnt;

            _latSum += (tickTime.TotalElapsedMs - serial) / 2;
            Latency = (int)_latSum / _cnt;

            // the Ping's serial is when it was sent: a moving average of the last few round trips
            var rtt = (int)Math.Max(0, tickTime.TotalElapsedMs - serial);
            RoundTrip = RoundTrip < 0 ? rtt : (RoundTrip * 3 + rtt) / 4;

            _pongTime = tickTime.TotalElapsedMs;
        }

        public void UpdateAckReceived()
        {
            if (!_updateAckTimeout.TryDequeue(out var ignored))
                Client.Disconnect("One too many UpdateAcks");
        }

        private bool KeepAlive(TickTime time)
        {
            if (Client.State == ProtocolState.Disconnected)
                return false;

            if (_pingTime == -1)
            {
                _pingTime = time.TotalElapsedMs - PingPeriod;
                _pongTime = time.TotalElapsedMs;
            }

            // check for disconnect timeout
            if (time.TotalElapsedMs - _pongTime > DcThresold)
            {
                Client.Disconnect("Connection timeout. (KeepAlive)");
                return false;
            }

            // check for shootack timeout
            if (_shootAckTimeout.TryPeek(out long timeout))
            {
                if (time.TotalElapsedMs > timeout)
                {
                    Client.Disconnect("Connection timeout. (ShootAck)");
                    return false;
                }
            }

            // check for updateack timeout
            if (_updateAckTimeout.TryPeek(out timeout))
            {
                if (time.TotalElapsedMs > timeout)
                {
                    Client.Disconnect("Connection timeout. (UpdateAck)");
                    return false;
                }
            }

            // check for gotoack timeout
            if (_gotoAckTimeout.TryPeek(out timeout))
            {
                if (time.TotalElapsedMs > timeout)
                {
                    Client.Disconnect("Connection timeout. (GotoAck)");
                    return false;
                }
            }

            if (time.TotalElapsedMs - _pingTime < PingPeriod)
                return true;

            // send ping
            _pingTime = time.TotalElapsedMs;
            Client.SendPacket(new Ping()
            {
                Serial = (int)time.TotalElapsedMs,
                // was Latency, an all-time average of half the round trip; no client used it
                RTT = RoundTrip // -1 until the first Pong
            });
            return UpdateOnPing();
        }

        private bool UpdateOnPing()
        {
            // renew account lock, without holding up the world thread for Redis's answer; the
            // client may have moved on (clients are pooled) by the time it comes
            var client = Client;
            var account = client.Account;
            GameServer.Database.RenewLockAsync(account).ContinueWith(t =>
            {
                if (client.Account != account || client.Player != this)
                    return;
                if (t.IsFaulted || t.IsCanceled)
                    client.Disconnect("RenewLock failed. (Timeout)");
                else if (!t.Result)
                    client.Disconnect("RenewLock failed. (Pong)");
            }, TaskContinuationOptions.ExecuteSynchronously);

            // save character
            if (!(World is TestWorld))
            {
                SaveToCharacter();
                Client.Character?.FlushAsync();
            }
            return true;
        }
    }
}
