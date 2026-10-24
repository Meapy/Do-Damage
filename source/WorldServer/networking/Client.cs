using NLog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Shared.database.account;
using Shared.database.character;
using WorldServer.core;
using WorldServer.core.connection;
using WorldServer.core.miscfile;
using WorldServer.core.net;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.networking
{
    public partial class Client
    {
        internal object DcLock = new object();

        //Temporary connection state
        internal int TargetWorld = -1;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private NetworkHandler _handler;
        private ConnectionListener _server;
        private volatile ProtocolState _state;

        public Client(ConnectionListener server, GameServer gameServer, SocketAsyncEventArgs send, SocketAsyncEventArgs receive)
        {
            _server = server;
            GameServer = gameServer;

            _handler = new NetworkHandler(this, send, receive);
        }

        public int PacketSpamAmount { get; set; }
        public int Rank { get; internal set; }
        public DbAccount Account { get; internal set; }
        public DbChar Character { get; internal set; }
        public GameServer GameServer { get; private set; }
        public string IpAddress { get; internal set; }
        public Player Player { get; internal set; }
        public ClientRandom Random { get; internal set; }
        public Socket Socket { get; private set; }
        public ProtocolState State { get => _state; internal set => _state = value; }

        public void SetSocket(Socket skt)
        {
            Socket = skt;

            try
            {
                IpAddress = ((IPEndPoint)skt.RemoteEndPoint).Address.ToString();
            }
            catch
            {
                IpAddress = "";
            }

            _handler.SetSocket(Socket);
        }

        public bool IsReady()
        {
            if (State == ProtocolState.Disconnected)
                return false;
            if (State == ProtocolState.Ready && Player?.World == null)
                return false;
            return true;
        }


        public async void SendFailure(string text, int errorId = FailureMessage.MessageWithDisconnect)
        {
            SendPacket(new FailureMessage(errorId, text));

            if (errorId == FailureMessage.MessageWithDisconnect || errorId == FailureMessage.ForceCloseGame)
            {
                await Task.Delay(1000);
                Disconnect($"SendFailure: {text}");
            }
        }

        public void SendPacket(OutgoingMessage pkt)
        {
            if (State != ProtocolState.Disconnected)
                _handler.SendPacket(pkt);
        }

        public void SendPackets(IEnumerable<OutgoingMessage> pkts)
        {
            if (State != ProtocolState.Disconnected)
                _handler.SendPackets(pkts);
        }

        private void Save() // only when disconnect
        {
            var acc = Account;

            if (Character == null || Player == null || Player.World is TestWorld)
            {
                GameServer.Database.ReleaseLock(acc);
                return;
            }

            Player.SaveToCharacter();
            acc.RefreshLastSeen();
            acc.FlushAsync();

            if (GameServer != null && GameServer.Database != null && Player.FameCounter != null && Player.FameCounter.ClassStats != null)
                if (GameServer.Database.SaveCharacter(acc, Character, Player.FameCounter.ClassStats, true))
                    GameServer.Database.ReleaseLock(acc);
        }

        public void Reconnect(Reconnect pkt)
        {
            if (Account == null)
            {
                Disconnect("Tried to reconnect an client with a null account...");
                return;
            }

            // a world server that is being replaced hands its players over to the new one, behind
            // the world proxy, when they go to the Nexus or a realm
            if (GameServer.Draining && GameServer.HandsOver(pkt.GameId))
            {
                pkt.GameId = World.NEXUS_ID;
                pkt.Name = "Nexus";
                pkt.Host = "";
                pkt.ClearKey();
                Player?.SendInfo("Moving you to the updated server...");
                SendPacket(pkt);
                return;
            }

            // an Arena game or minigame that has already started turns latecomers away here, with a
            // message, rather than disconnecting them at the Hello
            var target = GameServer.WorldManager.GetWorld(pkt.GameId);
            if (target is DoomedMinigame && !target.CanEnter(this))
            {
                Player?.SendError("That game has already started. Wait for the next one.");
                return;
            }

            //Log.Trace("Reconnecting client ({0}) @ {1} to {2}...", Account.Name, IP, pkt.Name);
            GameServer.ConnectionManager.AddReconnect(Account.AccountId, pkt);
            SendPacket(pkt);
        }

        public void Disconnect(string reason = "", bool forceShow = false)
        {
            if(forceShow)
                Log.Error("Disconnecting client ({0}) @ {1}... {2}", Account?.Name ?? " ", IpAddress, reason);
            if (State == ProtocolState.Disconnected)
                return;

            using (TimedLock.Lock(DcLock))
            {
                // send what the world thread queued for this client (e.g. a death or failure
                // message) before the socket closes; a failed send disconnects in there already
                _handler.Flush();
                if (State == ProtocolState.Disconnected)
                    return;
                State = ProtocolState.Disconnected;

#if DEBUG
                if (!forceShow && !string.IsNullOrEmpty(reason))
                    Log.Warn("Disconnecting client ({0}) @ {1}... {2}", Account?.Name ?? " ", IpAddress, reason);
#endif

                if (Account != null)
                    try
                    {
                        Save();
                    }
                    catch (Exception e)
                    {
                        var msg = $"{e.Message}\n{e.StackTrace}";
                        Log.Error(msg);
                    }
                //StopTask_ = true;
                GameServer.ConnectionManager.Disconnect(this);

                _server.Disconnect(this);
            }
        }

        public void Reset()
        {
            PacketSpamAmount = 0;
            Account = null;
            Character = null;
            IpAddress = null;
            Player?.CleanupPlayerUpdate();
            Player = null;
            Random = null;
            Socket = null;

            _handler.Reset();
        }

    }
}
