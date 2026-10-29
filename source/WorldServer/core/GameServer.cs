using NLog;
using Shared;
using Shared.database;
using Shared.isc;
using Shared.isc.data;
using Shared.resources;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WorldServer.core.commands;
using WorldServer.core.connection;
using WorldServer.core.miscfile;
using WorldServer.core.objects.vendors;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.logic;
using WorldServer.logic.loot;
using WorldServer.utils;

namespace WorldServer.core
{
    public sealed class GameServer
    {
        static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public string InstanceId { get; private set; }
        public ServerConfig Configuration { get; private set; }
        public Resources Resources { get; private set; }
        public DamageEvent DamageEvent { get; private set; }
        public Database Database { get; private set; }
        public MarketSweeper MarketSweeper { get; private set; }
        public ConnectionManager ConnectionManager { get; private set; }
        public ConnectionListener ConnectionListener { get; private set; }
        public ChatManager ChatManager { get; private set; }
        public BehaviorDb BehaviorDb { get; private set; }
        public CommandManager CommandManager { get; private set; }
        public DbEvents DbEvents { get; private set; }
        public ISManager InterServerManager { get; private set; }
        public WorldManager WorldManager { get; private set; }
        public SignalListener SignalListener { get; private set; }
        private bool Running { get; set; } = true;
        public DateTime RestartCloseTime { get; private set; }

        public GameServer(string[] appArgs)
        {
			var configPath = appArgs.Length == 0 ? "wServer.json" : appArgs[0];
			Configuration = ServerConfig.ReadFile(configPath);
			
            LogManager.Configuration.Variables["logDirectory"] = $"{Configuration.serverSettings.logFolder}/wServer";
            LogManager.Configuration.Variables["buildConfig"] = Utils.GetBuildConfiguration();

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                StaticLogger.Instance.Fatal(((Exception)args.ExceptionObject).StackTrace.ToString());
            };

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.Name = "Entry";

            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.SetMinThreads(250, completionPortThreads);

            Resources = new Resources(Configuration.serverSettings.resourceFolder, true, true);
            DamageEvent = new DamageEvent(Configuration.serverSettings, Resources.GameData);
            Database = new Database(Resources, Configuration);
            MarketSweeper = new MarketSweeper(Database);
            ConnectionManager = new ConnectionManager(this);
            ConnectionListener = new ConnectionListener(this);
            ChatManager = new ChatManager(this);
            BehaviorDb = new BehaviorDb(this);
            CommandManager = new CommandManager();
            DbEvents = new DbEvents(this);
            core.champions.ChampionCache.Start(this);

            InstanceId = Configuration.serverInfo.instanceId = Guid.NewGuid().ToString();
            Console.WriteLine($"[Set] InstanceId [{InstanceId}]");

            InterServerManager = new ISManager(Database.Subscriber, Configuration);
            WorldManager = new WorldManager(this);
            
			SignalListener = OperatingSystem.IsWindows() ? new SignalListenerWindows(this) : new SignalListenerLinux(this);
        }

        public bool IsWhitelisted(int accountId) => Configuration.serverSettings.whitelist.Contains(accountId);

        public void Run()
        {
#if DEBUG
            if (!Directory.Exists("GenerateXMLS"))
                _ = Directory.CreateDirectory("GenerateXMLS");

            var f = File.CreateText("GenerateXMLS/EmbeddedData_ObjectsCXML.xml");
            f.Write(Resources.GameData.ObjectCombinedXML.ToString());
            f.Close();

            var f3 = File.CreateText("GenerateXMLS/EmbeddedData_SkinsCXML.xml");
            f3.Write(Resources.GameData.SkinsCombinedXML.ToString());
            f3.Close();

            var f4 = File.CreateText("GenerateXMLS/EmbeddedData_PlayersCXML.xml");
            f4.Write(Resources.GameData.CombinedXMLPlayers.ToString());
            f4.Close();

            var f2 = File.CreateText("GenerateXMLS/EmbeddedData_GroundsCXML.xml");
            f2.Write(Resources.GameData.GroundCombinedXML.ToString());
            f2.Close();

            Log.Info("XMLs have been generated.");
#endif

            CommandManager.Initialize(this);
            Loot.Initialize(this);
            MobDrops.Initialize(this);
            BehaviorDb.Initialize();
            MerchantLists.Initialize(this);
            WorldManager.Initialize();
            InterServerManager.Initialize();
            InterServerManager.AddHandler<KickMsg>(Channel.Kick, ConnectionManager.OnKick);
            ChatManager.Initialize();
            ConnectionListener.Initialize();
            MarketSweeper.Start();
            ConnectionListener.Start();
            InterServerManager.JoinNetwork();

            var timeout = TimeSpan.FromHours(Configuration.serverSettings.restartTime);

            var utcNow = DateTime.UtcNow;
            var startedAt = utcNow;
            RestartCloseTime = utcNow.Add(timeout);
            var restartsIn = utcNow.Add(TimeSpan.FromMinutes(5));

            var restart = false;
            var restartRequested = false;
            var nextRestartRequest = RestartCloseTime;

            var watch = Stopwatch.StartNew();
            while (Running)
            {
                // behind the world proxy: a restart starts a new world server beside this one
                // (the proxy does it, tools/rolling_restart.py), which takes over without kicking
                if (ConnectionListener.BehindProxy)
                {
                    // asked again every 30 minutes until a new server replaces this one, in case
                    // that restart failed
                    if (!Draining && Configuration.serverSettings.restartTime > 0 && DateTime.UtcNow >= nextRestartRequest)
                    {
                        Log.Info(restartRequested
                            ? "Still not replaced: asking the world proxy again for a rolling restart."
                            : "Asking the world proxy for a rolling restart.");
                        restartRequested = true;
                        nextRestartRequest = DateTime.UtcNow.AddMinutes(30);
                        TryWriteFile(RESTART_REQUEST, "");
                    }
                    if (DrainTick())
                        break;
                }
                // server close event
                else if (!restart && DateTime.UtcNow >= RestartCloseTime)
                {
                    ChatManager.ServerAnnounce("The server will restart in 5 minutes.");

                    Log.Info("Server restart procedure is commencing.");
                    ConnectionListener.Disable();
                    restart = true;
                }

                if (restart && DateTime.UtcNow >= restartsIn)
                    break;

                var current = watch.ElapsedMilliseconds;

                ConnectionManager.Tick(current);

                var logicTime = (int)(watch.ElapsedMilliseconds - current);
                var sleepTime = Math.Max(0, 200 - logicTime);

                Thread.Sleep(sleepTime);
            }

            if (restart)
                Log.Info("A server restart has been triggered.");
            else
                Log.Info("A server shutdown has been triggered.");
            Dispose();

            // on Linux the service manager (systemd) starts it again
            if (restart && OperatingSystem.IsWindows())
                StartAgain();

            Log.Info("Program has been terminated.");
            Thread.Sleep(10000);
        }

        // Starts this server again the way it was started: WorldServer.exe, or `dotnet WorldServer.dll`
        // (the isolated test stacks, which keep off the live servers' process names), by full path.
        private static void StartAgain()
        {
            try
            {
                var exe = Environment.ProcessPath;
                var args = Environment.GetCommandLineArgs().Skip(1).ToList();
                if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    args.Insert(0, typeof(GameServer).Assembly.Location);
                var start = new ProcessStartInfo(exe) { WorkingDirectory = Environment.CurrentDirectory };
                foreach (var arg in args)
                    start.ArgumentList.Add(arg);
                Process.Start(start);
                Log.Info($"Started again: {exe} {string.Join(" ", args)}");
            }
            catch (Exception e)
            {
                Log.Error($"Couldn't start the server again: {e.Message}");
            }
        }

        // Files in the working folder, between this server and the world proxy's tools.
        private const string DRAIN_REQUEST = "drain.request"; // written by the tools: hand players over (its text: minutes to allow)
        private const string RESTART_REQUEST = "restart.request"; // written here: the restart time came
        private const string DRAIN_STATUS = "drain.status"; // written here while handing over: players left

        // Being replaced by a newer world server behind the world proxy: players move to it the
        // next time they go to the Nexus or a realm, and everyone left goes at the deadline.
        public volatile bool Draining;
        private DateTime DrainStarted;
        private DateTime DrainDeadline;
        private DateTime NextDrainCheck;
        private DateTime NextDrainNotice;
        private bool DeadlineWarned5, DeadlineWarned1, DeadlineReached;

        // Where a draining server sends players to the new one instead (the Nexus there).
        public bool HandsOver(int gameId) => gameId == World.NEXUS_ID || WorldManager.GetWorld(gameId) is RealmWorld;

        private static void TryWriteFile(string path, string text)
        {
            try { File.WriteAllText(path, text); }
            catch (Exception e) { Log.Error($"Couldn't write {path}: {e.Message}"); }
        }

        // Once a second: picks up a drain request and moves the hand-over along. True when this
        // server is empty and can stop.
        private bool DrainTick()
        {
            var now = DateTime.UtcNow;
            if (now < NextDrainCheck)
                return false;
            NextDrainCheck = now.AddSeconds(1);

            if (File.Exists(DRAIN_REQUEST))
            {
                var minutes = 60.0;
                try
                {
                    if (double.TryParse(File.ReadAllText(DRAIN_REQUEST).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                        minutes = Math.Max(0, m);
                    File.Delete(DRAIN_REQUEST);
                }
                catch (Exception e)
                {
                    Log.Error($"Couldn't read {DRAIN_REQUEST}: {e.Message}");
                }
                StartDrain(now, now.AddMinutes(minutes));
            }

            if (!Draining)
                return false;

            var left = ConnectionManager.Clients.Keys.Where(_ => _.Player != null).ToArray();
            TryWriteFile(DRAIN_STATUS, ConnectionManager.GetPlayerCount().ToString());

            var remaining = DrainDeadline - now;
            if (!DeadlineReached && remaining <= TimeSpan.Zero)
            {
                DeadlineReached = true;
                Log.Info($"Hand-over deadline: moving the last {left.Length} players to the new server.");
                foreach (var client in left)
                    MoveToNewServer(client);
            }
            else if (!DeadlineWarned1 && remaining <= TimeSpan.FromMinutes(1) && DrainDeadline - DrainStarted > TimeSpan.FromMinutes(1))
            {
                DeadlineWarned1 = DeadlineWarned5 = true;
                ChatManager.ServerAnnounce("In 1 minute you'll be moved to the Nexus of the updated server. Finish up!");
            }
            else if (!DeadlineWarned5 && remaining <= TimeSpan.FromMinutes(5) && DrainDeadline - DrainStarted > TimeSpan.FromMinutes(5))
            {
                DeadlineWarned5 = true;
                ChatManager.ServerAnnounce("In 5 minutes you'll be moved to the Nexus of the updated server.");
            }
            else if (now >= NextDrainNotice && !DeadlineReached)
            {
                NextDrainNotice = now.AddMinutes(10);
                ChatManager.ServerAnnounce($"A server update is waiting. Go to the Nexus to switch to it; you'll be moved there in {Math.Ceiling(remaining.TotalMinutes)} minutes at the latest.");
            }

            // empty (nobody playing or on the way between worlds here): stop. Stragglers that
            // didn't leave a minute after the deadline are let go.
            var empty = ConnectionManager.GetPlayerCount() == 0 && now - DrainStarted > TimeSpan.FromSeconds(5);
            if (empty || DeadlineReached && now - DrainDeadline > TimeSpan.FromMinutes(1))
            {
                Log.Info(empty ? "Handed every player over; stopping." : "Hand-over time is up; stopping.");
                foreach (var client in ConnectionManager.Clients.Keys.ToArray())
                    client.Disconnect("Hand-over time is up");
                try { File.Delete(DRAIN_STATUS); } catch (Exception) { }
                return true;
            }
            return false;
        }

        private void StartDrain(DateTime now, DateTime deadline)
        {
            if (Draining)
            {
                // asked again (rolling_restart.py --now): only ever sooner
                if (deadline < DrainDeadline)
                {
                    DrainDeadline = deadline;
                    NextDrainNotice = now;
                }
                return;
            }

            Draining = true;
            DrainStarted = now;
            DrainDeadline = deadline;
            NextDrainNotice = now.AddMinutes(10);
            Log.Info($"Handing players over to the new world server (deadline in {(deadline - now).TotalMinutes:0.#} minutes).");

            // the new server sweeps the market now
            MarketSweeper.Stop();

            var minutes = Math.Ceiling((deadline - now).TotalMinutes);
            if (minutes > 0)
                ChatManager.ServerAnnounce($"The server has been updated! You'll switch to the new version the next time you go to the Nexus (in {minutes} minutes at the latest). Your current world stays as it is.");

            // nobody is busy in the Nexus: those players switch now
            foreach (var client in ConnectionManager.Clients.Keys.ToArray())
                if (client.Player?.World is NexusWorld)
                    MoveToNewServer(client);
        }

        private void MoveToNewServer(networking.Client client)
        {
            if (client.Player == null)
                return;
            client.Reconnect(new networking.packets.outgoing.Reconnect()
            {
                Host = "",
                Port = Configuration.serverInfo.port,
                GameId = World.NEXUS_ID,
                Name = "Nexus"
            });
        }

        public void Stop()
        {
            if (!Running)
                return;
            Running = false;
        }

        public void Dispose()
        {
            Log.Info("Disposed 'ConnectionListener'.");
            ConnectionListener.Shutdown();

            Log.Info("Disposed 'InterServerManager'.");
            InterServerManager.Shutdown();

            Log.Info("Disposed 'Resources'.");
            Resources.Dispose();

            Log.Info("Disposed 'Database'.");
            Database.Dispose();

            Log.Info("Disposed 'MarketSweeper'.");
            MarketSweeper.Stop();

            Log.Info("Disposed 'ChatManager'.");
            ChatManager.Dispose();

            Log.Info("Disposed 'WorldManager'.");
            WorldManager.Dispose();

            Log.Info("Disposed 'Configuration'.");
            Configuration = null;
        }
    }
}
