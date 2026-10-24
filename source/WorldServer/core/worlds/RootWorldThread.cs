using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WorldServer.core.connection;
using WorldServer.utils;

namespace WorldServer.core.worlds
{
    public sealed class RootWorldThread
    {
        public const int TICK_TIME_MS = 100; // set this to 200 for 5tps 50 for 20 and 100 for 10 tps, never touch the deltatime value 

        private readonly WorldManager WorldManager;
        private World World;
        private bool Stopped;

        public RootWorldThread(WorldManager worldManager, World world)
        {
            WorldManager = worldManager;
            World = world;

            Run();
        }

        private void Run()
        {
            Task.Factory.StartNew(() =>
            {
                var watch = Stopwatch.StartNew();

                var lastMS = 0L;
                var mre = new ManualResetEvent(false);

                var realmTime = new TickTime();

                var elapsed = 0L;
                var lastTick = 0L;
                var lastReport = 0L;
                var profile = World.Profile;

                // messages sent by this thread go out together at the end of each pass
                NetworkSendHandler.BeginBatch();
                while (!Stopped)
                {
                    var ioStart = watch.ElapsedMilliseconds;
                    profile.Begin();
                    // the time now, for the messages (a Pong's round trip): it was last set before the last pass
                    realmTime.TotalElapsedMs = watch.ElapsedMilliseconds;
                    World.ProcessPlayerIO(ref realmTime);
                    profile.Mark(TickProfile.Phase.Io);
                    // players' messages hold up the tick too (a command, a login)
                    var ioMs = watch.ElapsedMilliseconds - ioStart;
                    if (ioMs > 100)
                        StaticLogger.Instance.Warn($"[{World.IdName} {World.Id}] Slow messages: {ioMs} ms, {World.Players.Count} players");

                    var currentMS = realmTime.TotalElapsedMs = watch.ElapsedMilliseconds;
                    var dt = currentMS - lastMS;
                    lastMS = currentMS;

                    elapsed += dt;
                    if(elapsed >= TICK_TIME_MS)
                    {
                        realmTime.TickCount++;
                        realmTime.ElapsedMsDelta = (int)elapsed;

                        var tickStart = watch.ElapsedMilliseconds;
                        var gap = lastTick == 0 ? TICK_TIME_MS : tickStart - lastTick;
                        lastTick = tickStart;
                        try
                        {
                            if (World.Update(ref realmTime))
                            {
                                Stopped = true;
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"[{World.IdName} {World.Id}] Tick: {e.StackTrace}");
                        }

                        // a slow tick delays every player in this world
                        var tickMs = watch.ElapsedMilliseconds - tickStart;
                        if (tickMs > 200)
                            StaticLogger.Instance.Warn($"[{World.IdName} {World.Id}] Slow tick: {tickMs} ms, {World.Players.Count} players");
                        profile.Tick(tickMs, gap);

                        // every 30 s while anyone is here: where the time went
                        if (tickStart - lastReport >= 30000)
                        {
                            var report = profile.Report();
                            if (World.Players.Count > 0 && lastReport != 0)
                                StaticLogger.Instance.Info($"[{World.IdName} {World.Id}] Tick stats, {World.Players.Count} players, {World.Enemies.Count} enemies: {report}");
                            lastReport = tickStart;
                        }

                        elapsed = 0;
                    }

                    profile.Begin();
                    NetworkSendHandler.FlushBatch();
                    profile.Mark(TickProfile.Phase.Send);

                    // sleep until a player's message arrives or the next tick is due; also with nobody
                    // in this world itself, as its branches' players (a dungeon opened from the Nexus)
                    // signal it too, and a flat 100 ms sleep held their messages until the next tick
                    var untilTick = TICK_TIME_MS - elapsed - (watch.ElapsedMilliseconds - lastMS);
                    if (untilTick > 0)
                        World.WaitForIO((int)untilTick);
                }

                NetworkSendHandler.FlushBatch();
                WorldManager.RemoveWorld(World);

            }, TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            if (Stopped)
                return;
            Stopped = true;
        }
    }
}
