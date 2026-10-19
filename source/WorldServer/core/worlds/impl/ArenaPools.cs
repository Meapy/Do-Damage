using System;
using System.Linq;

namespace WorldServer.core.worlds.impl
{
    // What the arenas (ArenaWorld, the public /arena, and DoomedBattleArena) spawn on each wave: a
    // stage chosen by the wave number, its minions (ceil((wave + players) / 2) of them, picked at
    // random) and one of its bosses. Later stages are harder: realm monsters, then the Godlands, then
    // dungeon bosses, Elder dungeon bosses, and Oryx. Every name must be a monster with a behaviour.
    // A wave ends when its monsters are dead, so nothing here may stay invulnerable for long: Limon
    // and Arachna (invulnerable elements and webs), Oryx's Suit of Armor and the Son of Arachna
    // (invulnerable until a player comes close) are left out.
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
                ["Oryx Brute", "Elder Coffin Creature", "Elder Lion Archer", "Elder Jackal Lord", "Elder Giant Squid", "Elder Sea Mare", "Elder Beam Priestess"],
                ["Oryx the Mad God 2", "Oryx the Mad God 4", "Lord of the Lost Lands", "Elder Thessal the Mermaid Goddess", "Crystal Prisoner"]),
        };

        public static Stage For(int wave) => Stages.Last(s => s.FromWave <= Math.Max(1, wave));

        public static int MinionCount(int wave, int players) => (int)Math.Ceiling((wave + players) / 2.0);

        public static string RandomMinion(int wave) => Pick(For(wave).Minions);

        public static string RandomBoss(int wave) => Pick(For(wave).Bosses);

        private static string Pick(string[] names) => names[Random.Shared.Next(names.Length)];
    }
}
