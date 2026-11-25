using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds.impl
{
    // The Arena's rules (DoomedBattleArena) that don't change from season to season: its loot and
    // fame, the breaks and votes between waves, and the Oryxes. What each wave spawns is the
    // season's (ArenaSeason2; Season 1's stages are in the history). Nothing a wave spawns may stay
    // invulnerable for long, since a wave ends when its monsters are dead. The Oryxes (OryxBosses)
    // have arena versions of their own: lower health, a wake of OryxWakeMs, then only their first
    // phase (ArenaTransition in their behaviours).
    public static class ArenaPools
    {
        // bosses with arena versions (see above): they spawn in the arena's middle, and a wave with
        // one of them keeps everyone invulnerable for OryxWaveInvulnerableMs
        public static readonly string[] OryxBosses =
            { "Oryx the Mad God 2", "Oryx the Mad God 3", "Oryx the Mad God 4", "Oryx the Ascended God" };

        // how long an Oryx spends invulnerable and holding his fire after spawning in an arena (ms)
        public const int OryxWakeMs = 5000;
        public const int OryxWaveInvulnerableMs = 5000;

        public static bool IsOryx(string name) => OryxBosses.Contains(name);

        // the share of the usual loot in the Arena and the Admin Arena (World.LootRate, Dungeons.xml):
        // no one dies there, so it's safe to farm. Loot.Handle applies it (LootHalvesItems says how).
        public const double LootRate = 0.5;

        // and pets (an item with a PermaPet activate, its generator) drop at this much of that again:
        // half as often as the arena's other loot (Loot.Handle)
        public const double PetRate = 0.5;

        // and the share of the usual fame (FameRates): the waves' fame (World.FameRate), and the
        // experience, so fame, from kills at level 20 (World.KillFameRate)
        public const double FameRate = 0.5;
        public const double KillFameRate = 0.75;

        // the waves' bosses and the Oryxes, which keep their drop chances but drop fewer items
        private static readonly HashSet<string> WaveBosses =
            ArenaSeason2.AllBosses.Concat(OryxBosses).ToHashSet();

        // A boss (a wave's boss, an Oryx, or anything with a boss health bar, for the Admin Arena's
        // spawns) under a LootRate rolls its drops as anywhere else, then keeps that share of the
        // items, rounded up, so whoever would get loot still does. Any other monster rolls each drop
        // at the rate instead: it mostly drops one item or none, and halving one item leaves one.
        public static bool LootHalvesItems(Enemy enemy) =>
            enemy.ObjectDesc.HealthBarBoss || WaveBosses.Contains(enemy.ObjectDesc.IdName);

        // Oryx 3, 4 and 5 come every few waves rather than at the end of a dungeon, so under a
        // LootRate they also roll each drop at it: with 0.5, half the chance and half of what drops,
        // about a quarter of the items
        public static readonly string[] HalfChanceBosses = { "Oryx the Mad God 3", "Oryx the Mad God 4", "Oryx the Ascended God" };

        // what a boss under a LootRate rolls its drops at: the rate for Oryx 3, 4 and 5, else 1
        public static double BossChanceRate(Enemy enemy, double worldRate) =>
            HalfChanceBosses.Contains(enemy.ObjectDesc.IdName) ? worldRate : 1;

        // seconds between waves: 5, then 10 more every 10 waves, at most 20
        public static int WaveGapSeconds(int wave) => Math.Min(20, 5 + 10 * (wave / 10));

        // as each wave spawns, everyone in the arena is invulnerable for this long (ms), so the
        // monsters that appear on top of a player can't kill them before they can move
        public const int WaveStartInvulnerableMs = 2500;

        public static int StartInvulnerableMs(string[] bosses) =>
            bosses.Any(IsOryx) ? OryxWaveInvulnerableMs : WaveStartInvulnerableMs;

        // the fame each player gets for reaching a wave (DoomedBattleArena pays it as the wave
        // before is beaten): wave * 10, halved with company, at the Arena's FameRate
        public static int WavePot(int wave, int players, double rate) => FameRates.Fame(wave * 10 / (players == 1 ? 1 : 2), rate);

        // Combining waves (DoomedBattleArena's votes): at most MaxCombine waves in one fight before
        // CombineBeforeWave, and from there (Oryx's stage) at most LateCombine (+2)
        public const int MaxCombine = 5;
        public const int CombineBeforeWave = 30;
        public const int LateCombine = 2;

        public static int MaxCombineAt(int wave) => Math.Clamp(CombineBeforeWave - wave, LateCombine, MaxCombine);

        // Next starts the wave once at least half the players want it
        public static int NextVotesNeeded(int players) => Math.Max(1, (players + 1) / 2);

        // a combine needs more than half of them
        public static int CombineVotesNeeded(int players) => players / 2 + 1;

        // How many waves the next fight combines: the most that more than half the players picked
        // at least (a pick above `max` counts as `max`; no pick counts as 1). One player decides alone.
        public static int CombineDecided(IEnumerable<int> picks, int max)
        {
            var list = picks.Select(k => Math.Clamp(k, 1, max)).ToList();
            for (var k = max; k > 1; k--)
                if (list.Count(p => p >= k) >= CombineVotesNeeded(list.Count))
                    return k;
            return 1;
        }

        // the Arena Chests stand this many tiles south of the arena's middle
        public const int ChestOffset = 4;

        // The wave and time box at the top of the view (both clients' ArenaMenu): a state other than 1
        // sets its time to runtimeMs, and state 1 then starts its clock from there. Send it on
        // joining and as each wave starts; the clock keeps going between.
        public static void SendWaveInfo(Player player, int wave, int runtimeMs, bool running)
        {
            player.Client.SendPacket(new ImminentArenaWave(runtimeMs, wave, 0));
            if (running)
                player.Client.SendPacket(new ImminentArenaWave(runtimeMs, wave, 1));
        }
    }
}
