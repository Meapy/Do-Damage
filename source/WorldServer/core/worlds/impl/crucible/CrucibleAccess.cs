using System;
using System.Collections.Generic;
using System.Linq;
using Shared.crucible;
using Shared.database.account;
using Shared.database.leaderboard;
using WorldServer.core.objects;
using WorldServer.networking;

namespace WorldServer.core.worlds.impl.crucible
{
    // Who may climb the Crucible (Shared.crucible.CrucibleRules): the furthest Arena round an account
    // has reached, kept in its `arenaBest` field as Arena rounds begin (DoomedBattleArena.SpawnWave),
    // and for the rounds reached before that was kept, read from every season's leaderboard (by the
    // players' names, as the Hall of Champions does), once and then every ten minutes. A Gilded
    // Realm Pass opens it too, as does being an admin.
    public static class CrucibleAccess
    {
        private static readonly object Lock = new object();
        private static Dictionary<string, int> _board;
        private static DateTime _boardAt;
        private static readonly TimeSpan BoardFor = TimeSpan.FromMinutes(10);

        // the best round each name reached on any season's leaderboard (upper-case names)
        private static Dictionary<string, int> Board(GameServer server)
        {
            lock (Lock)
            {
                if (_board != null && DateTime.UtcNow - _boardAt < BoardFor)
                    return _board;
            }
            var best = new Dictionary<string, int>();
            try
            {
                var db = server.Database.Conn;
                foreach (var season in ArenaSeasons.All)
                    foreach (var run in DbArenaRuns.Top(db, season, 5000))
                    {
                        if (run.Wave < 1)
                            continue;
                        foreach (var name in run.Players)
                        {
                            var key = name.Trim().ToUpperInvariant();
                            if (!best.TryGetValue(key, out var had) || run.Wave > had)
                                best[key] = run.Wave;
                        }
                    }
            }
            catch (Exception e)
            {
                utils.StaticLogger.Instance.Error($"Crucible: reading the Arena leaderboards failed: {e.Message}");
            }
            lock (Lock)
            {
                _board = best;
                _boardAt = DateTime.UtcNow;
            }
            return best;
        }

        public static int BestRound(GameServer server, DbAccount acc)
        {
            if (acc == null)
                return 0;
            var kept = acc.ArenaBest;
            if (kept >= CrucibleRules.UnlockRound)
                return kept;
            var board = Board(server).TryGetValue((acc.Name ?? "").ToUpperInvariant(), out var b) ? b : 0;
            if (board > kept)
            {
                acc.ArenaBest = board;
                _ = acc.FlushAsync();
            }
            return Math.Max(kept, board);
        }

        private static bool Gilded(Client client)
        {
            if (client.Player?.Pass != null)
                return client.Player.Pass.IsGilded;
            var json = client.Account?.Pass;
            return !string.IsNullOrEmpty(json) && Shared.pass.PassState.FromJson(json).Gilded;
        }

        // an admin: the account's flag, or rank 6 (the owner's account has only the rank)
        public static bool IsAdmin(DbAccount acc) => acc != null && (acc.Admin || acc.Rank >= (int)Shared.RankingType.Admin);

        public static bool MayEnter(Client client, out string why)
        {
            why = null;
            var acc = client?.Account;
            if (acc == null)
                return false;
            var best = BestRound(client.Player?.GameServer ?? client.GameServer, acc);
            var gilded = Gilded(client);
            if (!CrucibleRules.Unlocked(best, gilded, IsAdmin(acc)))
            {
                why = CrucibleRules.LockedHint(best);
                return false;
            }
            if (ClimbsLeft(client) <= 0)
            {
                why = CrucibleRules.SpentHint(gilded, UntilReset(), EventTimes(client));
                return false;
            }
            return true;
        }

        // ---- climbs a day: one, two with a Gilded Realm Pass, times what the events allow ----

        // what the timed events running now multiply the climbs a day by (serverSettings.events)
        private static int EventTimes(Client client) =>
            Shared.resources.ServerEvents.CrucibleClimbs((client?.Player?.GameServer ?? client?.GameServer)?.Configuration.serverSettings.events, DateTime.UtcNow);

        public static int ClimbsLeft(Client client)
        {
            var acc = client?.Account;
            if (acc == null)
                return 0;
            return CrucibleRules.ClimbsLeft(CrucibleRules.Day(DateTime.UtcNow), acc.CrucibleDay, acc.CrucibleClimbs, Gilded(client), IsAdmin(acc), EventTimes(client));
        }

        public static int DailyClimbs(Client client) => CrucibleRules.DailyClimbs(Gilded(client), EventTimes(client));

        public static TimeSpan UntilReset() => DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;

        // a climb begins with the player in it: one of the day's climbs used
        public static void Spend(Player player)
        {
            var acc = player?.Client?.Account;
            if (acc == null || IsAdmin(acc))
                return;
            var today = CrucibleRules.Day(DateTime.UtcNow);
            acc.CrucibleClimbs = acc.CrucibleDay == today ? acc.CrucibleClimbs + 1 : 1;
            acc.CrucibleDay = today;
            _ = acc.FlushAsync();
        }

        // an Arena round begins with the player in it: the furthest they've reached, kept; the
        // Crucible's door opens as they reach round 50
        public static void Reached(Player player, int round)
        {
            var acc = player.Client?.Account;
            if (acc == null || acc.ArenaBest >= round)
                return;
            var was = acc.ArenaBest;
            acc.ArenaBest = round;
            _ = acc.FlushAsync();
            if (was < CrucibleRules.UnlockRound && round >= CrucibleRules.UnlockRound)
            {
                player.SendInfo($"Round {CrucibleRules.UnlockRound}! The Crucible opens to you: its portal stands across Realm 2's vortex from the Arena's in the Nexus.");
                doomed.DoomedWorlds.Notify(player, 0xFFFFB03A, "The Crucible awaits");
            }
        }
    }
}
