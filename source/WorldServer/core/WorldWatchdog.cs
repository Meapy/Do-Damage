using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WorldServer.core.worlds;

namespace WorldServer.core
{
    /// <summary>
    /// Notices a world thread stuck in its loop (on 2026-11-20 the Nexus's stopped for good: nobody
    /// could enter it, and the players in it, its vaults and its Arenas were frozen, while the rest
    /// of the server ran on). Checked once a second from GameServer.Run: after a minute without a
    /// pass it logs which worlds, asks the world proxy for a rolling restart (players move to a new
    /// world server, those on a stuck world at once: GameServer.StartDrain) and saves a dump of this
    /// process beside the slots (build/live/dumps) to find the cause in (dotnet-dump analyze, on
    /// Linux: `clrstack -all`, `syncblk`).
    /// </summary>
    public sealed class WorldWatchdog
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // a tick takes milliseconds, a slow one a few hundred: a minute is stuck
        private static readonly long StuckMs = (long.TryParse(Environment.GetEnvironmentVariable("BSK_WATCHDOG_SECONDS"), out var s) && s > 0 ? s : 60) * 1000;
        private const int KeptDumps = 2;

        private readonly GameServer _server;
        private readonly Action _askRestart;
        private DateTime _nextCheck;
        private DateTime _nextAsk;
        private bool _dumped;
        private HashSet<RootWorldThread> _stuck = new HashSet<RootWorldThread>();

        public WorldWatchdog(GameServer server, Action askRestart)
        {
            _server = server;
            _askRestart = askRestart;
            TestHang();
        }

        /// <summary>A stuck world thread: its players can't leave it by themselves.</summary>
        public bool IsStuck(World world)
        {
            if (world == null || _stuck.Count == 0)
                return false;
            var thread = _server.WorldManager.ThreadOf(world);
            return thread != null && _stuck.Contains(thread);
        }

        public bool AnyStuck => _stuck.Count > 0;

        public void Tick()
        {
            var now = DateTime.UtcNow;
            if (now < _nextCheck)
                return;
            _nextCheck = now.AddSeconds(1);

            var stuck = _server.WorldManager.StuckThreads(StuckMs);
            var fresh = stuck.Where(t => !_stuck.Contains(t)).ToList();
            _stuck = stuck.ToHashSet();
            if (stuck.Count == 0)
                return;

            foreach (var t in fresh)
                Log.Error($"World watchdog: [{t.Name}] hasn't ticked for {t.SilentMs / 1000} s: its players are frozen.");

            // the proxy starts a new world server and this one hands its players over; asked again
            // every 10 minutes in case that failed
            if (!_server.Draining && now >= _nextAsk && _server.ConnectionListener.BehindProxy)
            {
                Log.Error("World watchdog: asking the world proxy for a rolling restart.");
                _nextAsk = now.AddMinutes(10);
                _askRestart();
            }
            else if (_server.Draining && fresh.Count > 0)
                _server.MoveFrozenPlayers();

            if (!_dumped)
            {
                _dumped = true;
                _ = Task.Run(Dump);
            }
        }

        // The runtime's createdump, on this process: a heap dump (~1.5 GB, the process paused for
        // ~20 s while it's written). Linux lets a child trace its parent only when asked (Yama
        // ptrace_scope 1): PR_SET_PTRACER for the while.
        private static void Dump()
        {
            // the live servers are on Linux; Windows's createdump.exe is only the runtime's own crash
            // dumper (it takes no process id)
            if (!OperatingSystem.IsLinux())
            {
                Log.Warn("World watchdog: dumps are made on Linux only");
                return;
            }
            try
            {
                var exe = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "createdump");
                if (!File.Exists(exe))
                {
                    Log.Warn($"World watchdog: no {exe}: no dump");
                    return;
                }
                var folder = Path.GetFullPath(Path.Combine("..", "dumps"));
                Directory.CreateDirectory(folder);
                foreach (var old in new DirectoryInfo(folder).GetFiles("world-*.dmp").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeptDumps - 1))
                    old.Delete();
                var pid = Environment.ProcessId;
                var path = Path.Combine(folder, $"world-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{pid}.dmp");

                _ = prctl(PR_SET_PTRACER, PR_SET_PTRACER_ANY, 0, 0, 0);
                var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "--withheap", "-f", path, pid.ToString() })
                    start.ArgumentList.Add(arg);
                using var p = Process.Start(start);
                var output = p.StandardOutput.ReadToEndAsync();
                var errors = p.StandardError.ReadToEndAsync();
                p.WaitForExit(5 * 60 * 1000);
                _ = prctl(PR_SET_PTRACER, 0, 0, 0, 0);
                if (p.HasExited && p.ExitCode == 0 && File.Exists(path))
                    Log.Error($"World watchdog: saved a dump, {path}");
                else
                    Log.Error($"World watchdog: no dump ({(p.HasExited ? "exit code " + p.ExitCode : "still running")}): {output.Result}{errors.Result}".Trim());
            }
            catch (Exception e)
            {
                Log.Error($"World watchdog: no dump: {e.Message}");
            }
        }

        private const int PR_SET_PTRACER = 0x59616d61;
        private const ulong PR_SET_PTRACER_ANY = ulong.MaxValue;

        [DllImport("libc", SetLastError = true)]
        private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

        // Testing the watchdog on an isolated stack: BSK_TEST_HANG_NEXUS=<seconds> hangs the Nexus's
        // thread that long after start-up. Never set on the live servers.
        private void TestHang()
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("BSK_TEST_HANG_NEXUS"), out var after) || after <= 0)
                return;
            Log.Warn($"BSK_TEST_HANG_NEXUS: the Nexus's thread hangs in {after} s");
            _ = Task.Run(async () =>
            {
                await Task.Delay(after * 1000);
                _server.WorldManager.Nexus?.Post(() => Thread.Sleep(Timeout.Infinite));
            });
        }
    }
}
