using Microsoft.AspNetCore.Hosting;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using System.Xml.Linq;
using Shared;
using Shared.database;
using Shared.isc;
using Shared.resources;

namespace App
{
    public sealed class CoreService 
    {
        public readonly Logger Logger;
        public readonly Resources Resources;
        public readonly Database Database;
        public readonly ISManager ISManager;
        public readonly ServerConfig Config;
        public readonly StatusHistory Status;
        private readonly Timer LegendSweeper = new Timer(60000);

        public readonly List<ushort> PlayerClasses;
        public readonly Dictionary<ushort, string> _classes;
        public readonly Dictionary<string, string> _classAvailability;
        public readonly XElement ItemCostsXml;

        public CoreService()
        {
            Logger = LogManager.GetCurrentClassLogger();
			
            var isDocker = Environment.GetEnvironmentVariable("IS_DOCKER") != null;
            if(isDocker)
				Config = ServerConfig.ReadFile("/data/server.json");
			else
				Config = ServerConfig.ReadFile("server.json");
            
			Config.serverInfo.instanceId = Guid.NewGuid().ToString();

            LogManager.Configuration.Variables["logDirectory"] = $"{Config.serverSettings.logFolder}/app";
            LogManager.Configuration.Variables["buildConfig"] = Utils.GetBuildConfiguration();

            if (Debugger.IsAttached) // hacky fix to get right path when debugging
                Resources = new Resources($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/resources", false);
            else
                Resources = new Resources(Config.serverSettings.resourceFolder, false);

            Database = new Database(Resources, Config);

            ISManager = new ISManager(Database.Subscriber, Config, true);
            ISManager.OnTick += () => UpdateServersUsage();
            ISManager.Initialize();

            LegendSweeper.Elapsed += (sender, e) => Database.CleanLegends();
            LegendSweeper.Start();

            Status = new StatusHistory(this);

            PlayerClasses = Resources.GameData.ObjectDescs.Values.Where(objDesc => objDesc.Player).Select(objDesc => objDesc.ObjectType).ToList();
            _classes = Resources.GameData.ObjectDescs.Values.Where(objDesc => objDesc.Player).ToDictionary(objDesc => objDesc.ObjectType, objDesc => objDesc.IdName);
            _classAvailability = _classes.ToDictionary(@class => @class.Value, @class => "available");

            var elem = new XElement("ItemCosts");
            foreach (var skin in Resources.GameData.Skins.Values)
            {
                var ca = new XElement("ItemCost", skin.Cost);
                ca.Add(new XAttribute("type", skin.Type));
                elem.Add(ca);
            }

            ItemCostsXml = elem;
        }

        public bool IsProduction()
        {
#if DEBUG
            return false;
#else
            return true;
#endif
        }

        public void UpdateServersUsage()
        {
            var servers = ISManager
                .GetServerList()
                .Where(server => server.instanceId != Config.serverInfo.instanceId)
                .ToArray();

            if (servers.Length == 0)
            {
                Config.serverInfo.players = 0;
                Config.serverInfo.maxPlayers = 0;
                return;
            }

            var players = 0;
            var maxPlayers = 0;
            for (var i = 0; i < servers.Length; i++)
            {
                var server = servers[i];

                players += server.players;
                maxPlayers += server.maxPlayers;
            }

            Config.serverInfo.players = players;
            Config.serverInfo.maxPlayers = maxPlayers;
        }

        // The address of this PC on the local network, or null. Connecting a UDP socket sends nothing;
        // it only picks the interface that reaches the internet.
        private static readonly Lazy<string> LanAddress = new Lazy<string>(() =>
        {
            try
            {
                using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
                socket.Connect("8.8.8.8", 53);
                return ((System.Net.IPEndPoint)socket.LocalEndPoint).Address.ToString();
            }
            catch
            {
                return null;
            }
        });

        private static bool IsPrivate(System.Net.IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] < 32 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254);
        }

        // The world server address to give a client at clientIp. The public address only works from
        // outside: a client on this PC gets 127.0.0.1, and one elsewhere on the local network, or
        // reaching us through our own public address (NAT loopback), gets this PC's LAN address.
        // Otherwise all its game traffic would go out through the router and back in, which loads
        // the router and delays the traffic of players connecting from outside.
        private static string WorldAddressFor(System.Net.IPAddress clientIp, string publicAddress)
        {
            if (clientIp == null)
                return publicAddress;
            if (clientIp.IsIPv4MappedToIPv6)
                clientIp = clientIp.MapToIPv4();
            if (System.Net.IPAddress.IsLoopback(clientIp))
                return "127.0.0.1";
            if (IsPrivate(clientIp) || clientIp.ToString() == publicAddress)
                return LanAddress.Value ?? publicAddress;
            return publicAddress;
        }

        public List<ServerItem> GetServerList(System.Net.IPAddress clientIp = null)
        {
            var ret = new List<ServerItem>();
            // World servers behind one world proxy (a new one taking over from an old one) share
            // its public address and port: they are one server to players.
            var groups = ISManager.GetServerList().Where(_ => _.type == ServerType.World).GroupBy(_ => (_.address, _.port));
            foreach (var group in groups)
            {
                var server = group.First();
                var players = group.Sum(_ => _.players);
                var maxPlayers = group.Max(_ => _.maxPlayers);

                ret.Add(new ServerItem()
                {
                    Name = server.name,
                    Lat = server.latitude,
                    Long = server.longitude,
                    Port = server.port,
                    DNS = WorldAddressFor(clientIp, server.address),
                    Usage = players / (double)maxPlayers,
                    AdminOnly = server.adminOnly,
                    UsageText = group.All(_ => _.IsJustStarted()) ? "- NEW!" : $"{players}/{maxPlayers}"
                });
            }
            ret = ret.OrderBy(_ => _.Port).ToList();
            return ret;
        }
    }
}