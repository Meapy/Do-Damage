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

        // when this thread's loop last came round (Environment.TickCount64): the world watchdog
        // (core/WorldWatchdog.cs) notices a world stuck in a tick, which no player can leave
        private long _lastPass = Environment.TickCount64;

        public string Name => $"{World.IdName} {World.Id}";
        public bool IsStopped => Stopped;
        public long SilentMs => Environment.TickCount64 - Volatile.Read(ref _lastPass);

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
                    Volatile.Write(ref _lastPass, Environment.TickCount64);
                    var ioStart = watch.ElapsedMilliseconds;
                    profile.Begin();
                    // the time now, for the messages (a Pong's round trip): it was last set before the last pass
                    realmTime.TotalElapsedMs = watch.ElapsedMilliseconds;
                    // nothing thrown may leave this loop: it would end the thread unseen (no log, the
                    // world never removed) and freeze everyone in it, as the Nexus on 2026-11-20
                    try
                    {
                        World.ProcessPlayerIO(ref realmTime);
                    }
                    catch (Exception e)
                    {
                        Failed("Messages", e);
                    }
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
                            Failed("Tick", e);
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
                    try
                    {
                        NetworkSendHandler.FlushBatch();
                    }
                    catch (Exception e)
                    {
                        Failed("Sending", e);
                    }
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

        // logged in full (the old Console line had only the stack, no message), at most once a
        // second, with how many were left out: a fault that repeats every pass doesn't flood the log
        private long _nextFailureLog;
        private int _failuresLeftOut;

        private void Failed(string phase, Exception e)
        {
            var now = Environment.TickCount64;
            if (now < _nextFailureLog)
            {
                _failuresLeftOut++;
                return;
            }
            _nextFailureLog = now + 1000;
            var more = _failuresLeftOut > 0 ? $" ({_failuresLeftOut} more since the last)" : "";
            _failuresLeftOut = 0;
            StaticLogger.Instance.Error($"[{Name}] {phase} failed{more}: {e}");
        }

        public void Stop()
        {
            if (Stopped)
                return;
            Stopped = true;
        }
    }
}
