using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds.impl
{
    // What the Arena (DoomedBattleArena) spawns on each wave: a
    // stage chosen by the wave number, its minions (ceil((wave + players) / 2) of them, picked at
    // random, a couple from earlier stages) and one of its bosses (two from wave 30). Later stages are harder: realm monsters, then the Godlands, then
    // dungeon bosses, Elder dungeon bosses, Oryx, and Oryx's Court (the courts of Oryx 3, 4 and 5).
    // Every name must be a monster with a behaviour. A wave ends when its monsters are dead, so
    // nothing here may stay invulnerable for long: Limon and Arachna (invulnerable elements and
    // webs), Oryx's Suit of Armor and the Son of Arachna (invulnerable until a player comes close)
    // are left out. The Oryxes (OryxBosses) have arena versions of their own: lower health, a wake
    // of OryxWakeMs, then only their first phase (ArenaTransition in their behaviours).
    public static class ArenaPools
    {
        public sealed class Stage
        {
            public readonly int FromWave;
            public readonly string Name;
            public readonly string[] Minions;
            public readonly string[] Bosses;

            public Stage(int fromWave, string name, string[] minions, string[] bosses)
            {
                FromWave = fromWave;
                Name = name;
                Minions = minions;
                Bosses = bosses;
            }
        }

        public static readonly Stage[] Stages =
        {
            new Stage(1, "Realm",
                ["Ogre King", "Undead Dwarf King", "Night Elf King", "Minotaur", "Flayer God", "Lizard God", "Horned Drake", "Urgle"],
                ["Deathmage", "Actual Lich", "Actual Ent Ancient", "Oasis Giant"]),
            new Stage(5, "Godlands",
                ["Djinn", "Beholder", "White Demon", "Flying Brain", "Slime God", "Ghost God", "Ent God", "Medusa", "Sprite God", "Leviathan"],
                ["Red Demon", "Phoenix Lord", "Actual Ghost King", "Cyclops God", "Henchman of Oryx"]),
            new Stage(10, "Dungeons",
                ["Medusa", "Leviathan", "Sprite God", "Ent God", "Beholder", "Mini Bot", "Rock Bot", "Haunted Spirit", "Oryx Knight"],
                ["Stheno the Snake Queen", "Septavius the Ghost God", "Archdemon Malphas", "Mixcoatl the Masked God", "Oryx Brute", "Dr Terrible"]),
            new Stage(15, "Hard dungeons",
                ["Henchman of Oryx", "Oryx Insect Commander", "Kage Kami", "Elder Jackal Warrior", "Elder Jackal Veteran", "Elder Jackal Assassin", "Elder Native Sprite God", "Elder Fishman"],
                ["Elder Tree", "Gigacorn", "Crystal Prisoner", "Cube God", "Lord Ruthven", "Elder Mixcoatl the Masked God"]),
            new Stage(20, "Elder dungeons",
                ["Oryx Brute", "Oryx Insect Commander", "Elder Bloated Mummy", "Elder Coffin Creature", "Elder Lion Archer", "Elder Jackal Assassin", "Elder Fishman"],
                ["Elder Stheno the Snake Queen", "Elder Septavius the Ghost God", "Elder Archdemon Malphas", "Elder Lord Ruthven", "Murderous Megamoth", "Grand Sphinx",
                 "Thessal the Mermaid Goddess", "Tomb Support", "Tomb Defender", "Tomb Attacker"]),
            new Stage(30, "Oryx",
                ["Oryx Brute", "Elder Coffin Creature", "Elder Lion Archer", "Elder Jackal Lord", "Elder Giant Squid", "Elder Sea Mare", "Elder Beam Priestess",
                 "Oryx Noble", "Oryx Aristocrat", "Oryx Sergeant", "Oryx Deacon"],
                ["Oryx the Mad God 2", "Oryx the Mad God 3", "Lord of the Lost Lands", "Elder Thessal the Mermaid Goddess", "Crystal Prisoner"]),
            new Stage(40, "Oryx's Court",
                ["Oryx Officer", "Oryx Major", "Oryx Cleric", "Oryx Cardinal",
                 "Infernal Officer", "Brimstone Major", "Cinder Cleric", "Doom Cardinal",
                 "Ascended Noble", "Ascended Aristocrat", "Ascended Sergeant", "Ascended Officer",
                 "Ascended Major", "Ascended Deacon", "Ascended Cleric", "Ascended Cardinal"],
                ["Oryx the Mad God 3", "Oryx the Mad God 4", "Oryx the Ascended God"]),
        };

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

        // and the share of the usual fame (FameRates): the waves' fame (World.FameRate), and the
        // experience, so fame, from kills at level 20 (World.KillFameRate)
        public const double FameRate = 0.5;
        public const double KillFameRate = 0.75;

        // the waves' bosses and the Oryxes, which keep their drop chances but drop fewer items
        private static readonly HashSet<string> WaveBosses =
            Stages.SelectMany(s => s.Bosses).Concat(OryxBosses).ToHashSet();

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

        // up to this many of a wave's minions come from earlier stages: the stage just before, and
        // the Godlands from then on (Godlands monsters turn up in every later stage)
        public const int CarriedMinions = 2;
        private const string PersistentStage = "Godlands";

        // from this wave on, an Elder dungeons boss comes with the stage's own boss
        public const int ElderBossFromWave = 30;

        // as each wave spawns, everyone in the arena is invulnerable for this long (ms), so the
        // monsters that appear on top of a player can't kill them before they can move
        public const int WaveStartInvulnerableMs = 2500;

        public static int StartInvulnerableMs(string[] bosses) =>
            bosses.Any(IsOryx) ? OryxWaveInvulnerableMs : WaveStartInvulnerableMs;
        private const string ElderBossStage = "Elder dungeons";

        public static Stage For(int wave) => Stages.Last(s => s.FromWave <= Math.Max(1, wave));

        // the chat line the Arena sends as a wave spawns
        public static string WaveMessage(int wave) => $"Wave {wave} has begun ({For(wave).Name}).";

        // and as several combined waves spawn together
        public static string WavesMessage(int from, int to) =>
            $"Waves {from}-{to} have begun ({string.Join(", ", Enumerable.Range(from, to - from + 1).Select(w => For(w).Name).Distinct())}).";

        // the fame each player gets for reaching a wave (DoomedBattleArena pays it as the wave
        // before is beaten): wave * 10, halved with company, at the Arena's FameRate
        public static int WavePot(int wave, int players, double rate) => FameRates.Fame(wave * 10 / (players == 1 ? 1 : 2), rate);

        // Combining waves (DoomedBattleArena's votes): at most MaxCombine waves in one fight, and
        // only waves before CombineBeforeWave (the Oryx stage, where two Oryxes would share the middle)
        public const int MaxCombine = 5;
        public const int CombineBeforeWave = 30;

        public static int MaxCombineAt(int wave) => Math.Clamp(CombineBeforeWave - wave, 1, MaxCombine);

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

        public static int MinionCount(int wave, int players) => (int)Math.Ceiling((wave + players) / 2.0);

        // the wave's minions: MinionCount of them, the first CarriedMinions (when the count leaves at
        // least one for the current stage) from the earlier stages' pools
        public static string[] Minions(int wave, int players)
        {
            var count = MinionCount(wave, players);
            var carried = CarriedPool(wave);
            var fromEarlier = carried.Length == 0 ? 0 : Math.Min(CarriedMinions, count - 1);
            var current = For(wave).Minions;
            var names = new string[count];
            for (var i = 0; i < count; i++)
                names[i] = Pick(i < fromEarlier ? carried : current);
            return names;
        }

        public static string[] Bosses(int wave)
        {
            var boss = Pick(For(wave).Bosses);
            if (wave < ElderBossFromWave)
                return [boss];
            return [boss, Pick(Stages.First(s => s.Name == ElderBossStage).Bosses)];
        }

        private static string[] CarriedPool(int wave)
        {
            var index = Array.IndexOf(Stages, For(wave));
            if (index == 0)
                return [];
            var persistent = Array.FindIndex(Stages, s => s.Name == PersistentStage);
            var pool = Stages[index - 1].Minions.AsEnumerable();
            if (persistent < index - 1)
                pool = pool.Concat(Stages[persistent].Minions);
            return pool.Except(Stages[index].Minions).ToArray();
        }

        private static string Pick(string[] names) => names[Random.Shared.Next(names.Length)];
    }
}
