using System;
using System.Linq;

namespace WorldServer.core.worlds.impl
{
    // What the arenas (ArenaWorld, the public /arena, and DoomedBattleArena) spawn on each wave: a
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

        // the chat line both arenas send as a wave spawns
        public static string WaveMessage(int wave) => $"Wave {wave} has begun ({For(wave).Name}).";

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
