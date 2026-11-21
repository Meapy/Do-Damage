using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldServer.core.worlds.impl
{
    // Season 2 of the Arena ("Rise of the Fallen", Shared ArenaSeasons): every wave is a round
    // planned by an ArenaPlanner from the encounters below. An encounter is a dungeon or a corner of
    // the realm: its boss or bosses and its own monsters. The waves fall into tiers that get harder;
    // each tier deals its encounters from a shuffled pile, so none comes twice before the rest have
    // been. On top, a round can take another shape (ArenaFormat: a horde, a clash of two dungeons,
    // a boss rush, old foes back as Legendary ones, a showdown every tenth wave) and a twist
    // (ArenaTwist), and from wave EmpoweredFromWave an Empowered Elder boss may rise in the middle of
    // a fight (EmpoweredChance). Every name must be a monster with a behaviour that can be killed in
    // the ring (ArenaSeason2Tests, and the admin's /arenaprobe, which fights each one there).
    public enum ArenaFormat : byte
    {
        Standard = 0,
        // no boss: three times the monsters, some of them elites
        Horde = 1,
        // two encounters at once, each with its boss
        Clash = 2,
        // three bosses of the tier, one after another
        BossRush = 3,
        // every tenth wave: a fight of its own
        Showdown = 4,
        // a boss and its monsters from an earlier tier, Legendary
        Throwback = 5,
    }

    public enum ArenaTwist : byte
    {
        None = 0,
        // the bosses are Epic (more health and defense), some monsters Rare
        Elite = 1,
        // twice the monsters
        Swarm = 2,
        // the monsters have more defense
        Ironclad = 3,
        // the monsters move half again as fast
        Frenzy = 4,
        // the wave pays twice the fame
        Bounty = 5,
        // the players hit harder and faster for the wave's first BlessingMs
        Blessing = 6,
        // the wave's loot isn't halved
        Golden = 7,
    }

    public sealed class ArenaEncounter
    {
        public readonly string Name;
        public readonly int Tier;
        public readonly string[] Bosses;
        // how many of the bosses come (picked at random); 0 for all of them
        public readonly int Pick;
        public readonly string[] Minions;

        public ArenaEncounter(string name, int tier, string[] bosses, string[] minions, int pick = 1)
        {
            Name = name;
            Tier = tier;
            Bosses = bosses;
            Minions = minions;
            Pick = pick;
        }
    }

    // One wave as planned: what it is called, its shape and twist, the bosses that come with it,
    // the ones that come one after another (a boss rush), the monsters its minions are drawn from
    // and how many of them, and how much health a boss may have.
    public sealed class ArenaRound
    {
        public int Wave;
        public int Tier;
        public string Title;
        public ArenaFormat Format;
        public ArenaTwist Twist;
        public List<string> Bosses = new List<string>();
        public List<string> Relay = new List<string>();
        public string[] MinionPool = Array.Empty<string>();
        public string[] TierPool = Array.Empty<string>();
        public double MinionScale = 1;
        // Legendary bosses (a throwback's)
        public bool Legendary;
        public int HealthCap;

        public IEnumerable<string> AllBosses => Bosses.Concat(Relay);
    }

    public static class ArenaSeason2
    {
        // ---- the tiers: the first wave of each, and how much health a boss may have in it ----
        public static readonly int[] TierFrom = { 1, 6, 12, 20, 30, 40 };
        private static readonly int[] TierCap = { 18_000, 35_000, 60_000, 110_000, 170_000, 260_000 };

        public static int TierOf(int wave)
        {
            var t = 0;
            for (var i = 0; i < TierFrom.Length; i++)
                if (wave >= TierFrom[i])
                    t = i + 1;
            return Math.Max(1, t);
        }

        // From wave 40 a boss may have 3% more health for every wave past it.
        public static int HealthCap(int wave)
        {
            var cap = (double)TierCap[TierOf(wave) - 1];
            if (wave > 40)
                cap *= 1 + 0.03 * (wave - 40);
            return (int)Math.Min(int.MaxValue / 4, cap);
        }

        // each boss of a round that brings several has a share of the cap
        public static double CapShare(int bosses) => bosses <= 1 ? 1 : bosses == 2 ? 0.75 : 0.6;

        // a wave's monsters: as Season 1's, (wave + players) / 2 rounded up, at most MaxMinions
        public const int MaxMinions = 26;
        public static int MinionCount(int wave, int players, double scale) =>
            Math.Clamp((int)Math.Ceiling((wave + players) / 2.0 * scale), 0, MaxMinions);

        // a quarter of a round's monsters come from the whole tier rather than its own encounter
        public const double TierMinionShare = 0.25;

        // ---- Empowered Elder bosses ----
        public const int EmpoweredFromWave = 12;
        // at least this many waves between two
        public const int EmpoweredGap = 5;
        // they rise this long after the fight begins
        public const int EmpoweredDelayMs = 4000;
        // the fame each player gets for one, at the Arena's FameRate
        public const int EmpoweredFame = 400;

        public static double EmpoweredChance(int wave) =>
            wave < EmpoweredFromWave ? 0 : Math.Min(0.08, 0.03 + 0.002 * (wave - EmpoweredFromWave));

        // the Elder bosses that may rise Empowered (BossResurrection), by the tier they start in.
        // Not the Forgotten King, whose rising rebuilds the floor round him (EmpoweredGround).
        public static readonly (string Boss, int Tier)[] EmpoweredPool =
        {
            ("Elder Stheno the Snake Queen", 3),
            ("Elder Septavius the Ghost God", 3),
            ("Elder Archdemon Malphas", 3),
            ("Elder Mixcoatl the Masked God", 3),
            ("Elder Lord Ruthven", 3),
            ("Elder Dreadstump the Pirate King", 3),
            ("Elder Arena Ghost Bride", 4),
            ("Elder Arena Grave Caretaker", 4),
            ("Elder Masked Party God", 4),
            ("Elder NM Red Dragon God", 4),
            ("Elder NM Black Dragon God", 4),
            ("Elder NM Blue Dragon God", 4),
            ("Elder NM Green Dragon God", 5),
            ("Elder Ghost of Skuld", 5),
            ("Elder Thessal the Mermaid Goddess", 5),
            ("Elder Tomb Support", 5),
            ("Elder Tomb Attacker", 5),
            ("Elder Tomb Defender", 5),
        };

        public static string[] EmpoweredFor(int tier) => EmpoweredPool.Where(e => e.Tier <= tier).Select(e => e.Boss).ToArray();

        // ---- twists ----
        public const int TwistFromWave = 6;
        public const int BlessingMs = 20000;

        public static double TwistChance(int wave) =>
            wave < TwistFromWave ? 0 : wave >= 40 ? 1 : Math.Min(0.6, 0.2 + 0.012 * (wave - TwistFromWave));

        // ---- monster pools shared by several encounters ----
        private static readonly string[] Realm =
            { "Ogre King", "Undead Dwarf King", "Night Elf King", "Minotaur", "Flayer God", "Lizard God", "Horned Drake", "Urgle" };
        private static readonly string[] Godlands =
            { "Djinn", "Beholder", "White Demon", "Flying Brain", "Slime God", "Ghost God", "Ent God", "Medusa", "Sprite God", "Leviathan" };
        private static readonly string[] OryxGuard =
            { "Oryx Knight", "Oryx Brute", "Oryx Insect Commander", "Oryx Eye Warrior" };
        private static readonly string[] OryxCourt =
            { "Oryx Noble", "Oryx Aristocrat", "Oryx Sergeant", "Oryx Deacon", "Oryx Officer", "Oryx Major", "Oryx Cleric", "Oryx Cardinal" };
        private static readonly string[] InfernalCourt =
            { "Infernal Officer", "Brimstone Major", "Cinder Cleric", "Doom Cardinal" };
        private static readonly string[] AscendedCourt =
            { "Ascended Noble", "Ascended Aristocrat", "Ascended Sergeant", "Ascended Officer", "Ascended Major", "Ascended Deacon", "Ascended Cleric", "Ascended Cardinal" };
        private static readonly string[] ElderTombGuard =
            { "Elder Jackal Warrior", "Elder Jackal Veteran", "Elder Jackal Assassin", "Elder Lion Archer", "Elder Bloated Mummy", "Elder Beam Priestess" };
        private static readonly string[] Cultists =
            { "LH Red Cultist", "LH Blue Cultist", "LH Yellow Cultist", "LH White Cultist", "LH Purple Cultist", "LH Black Cultist", "LH Green Cultist" };
        private static readonly string[] ElderCultists =
            { "Elder LH Red Cultist", "Elder LH Blue Cultist", "Elder LH Yellow Cultist", "Elder LH White Cultist", "Elder LH Purple Cultist", "Elder LH Black Cultist", "Elder LH Green Cultist" };
        private static readonly string[] ElderShattersGuard =
            { "Elder shtrs Stone Knight", "Elder shtrs Stone Mage", "Elder shtrs Fire Mage", "Elder shtrs Ice Mage", "Elder shtrs Lava Souls" };
        private static readonly string[] ShattersGuard =
            { "shtrs Stone Knight", "shtrs Stone Mage", "shtrs Fire Mage", "shtrs Ice Mage", "shtrs Lava Souls" };

        private static ArenaEncounter E(string name, int tier, string[] bosses, string[] minions, int pick = 1) =>
            new ArenaEncounter(name, tier, bosses, minions, pick);

        // ---- the encounters, by tier ----
        public static readonly ArenaEncounter[] Encounters =
        {
            // 1: the realm
            E("The Lich's Crypt", 1, ["Actual Lich"], ["Undead Dwarf King", "Night Elf King", "Ogre King", "Minotaur"]),
            E("The Ancient Grove", 1, ["Actual Ent Ancient"], ["Horned Drake", "Lizard God", "Urgle", "Flayer God"]),
            E("The Sunken Oasis", 1, ["Oasis Giant"], ["Lizard God", "Minotaur", "Urgle", "Horned Drake"]),
            E("The Deathmage's Circle", 1, ["Deathmage"], ["Flayer God", "Undead Dwarf King", "Night Elf King", "Ogre King"]),
            E("The Prism Expanse", 1, ["R2 Tesseract", "R2 Hypercube Warden"], ["R2 Cubelet", "R2 Prism Sentinel", "R2 Shard Walker", "R2 Cube Overseer"]),
            E("The Watching Eye", 1, ["R2 Eye of the Pentagram", "R2 Elder Watcher"], ["R2 Floating Eye", "R2 Eye Swarmer", "R2 Gazer", "R2 Watcher Stalk"]),
            E("The Forsaken Ruins", 1, ["R2 Sentry Captain", "R2 Forgotten Warlord"], ["R2 Lost Sentry", "R2 Forsaken Archer", "R2 Wailing Wraith", "R2 Ruin Golem"]),
            E("The Shellback Shoals", 1, ["R2 Shellback Champion", "R2 Ancient Snapper"], ["R2 Hermit Brute", "R2 Tide Snapper", "R2 Barnacle Turret", "R2 Kelp Lurker"]),
            E("The Ghost Fleet", 1, ["R2 Phantom Quartermaster", "R2 Abyssal Kraken"], ["R2 Drowned Buccaneer", "R2 Spectral Gull", "R2 Bone Gunner", "R2 Wreck Wraith", "R2 Phantom Sloop"]),
            E("The Sun Tomb", 1, ["R2 Tomb Sentinel", "R2 Sand Scourge"], ["R2 Scarab", "R2 Tomb Mummy", "R2 Jackal Guard", "R2 Sun Obelisk"]),
            E("The Bone Shrine", 1, ["R2 Shrine Keeper", "R2 Marrow Fiend"], ["R2 Bone Flayer", "R2 Skull Wisp", "R2 Blood Imp", "R2 Shrine Cultist"]),
            E("The Oni Gate", 1, ["R2 Oni Warlord", "R2 Jade Golem"], ["R2 Komainu", "R2 Oni Mask", "R2 Kitsune Spirit", "R2 Lantern Wisp"]),

            // 2: the Godlands and the first dungeons
            E("The Godlands", 2, ["Red Demon", "Phoenix Lord", "Actual Ghost King", "Cyclops God"], Godlands),
            E("The Wyrm's Nest", 2, ["R2 Wyrm Matriarch", "R2 Elder Drake"], ["R2 Drakeling", "R2 Magma Salamander", "R2 Ember Wyrm", "R2 Obsidian Golem"]),
            E("The Fallen Court", 2, ["R2 Forgotten Archmage", "R2 King's Champion"], ["R2 Shattered Knight", "R2 Forgotten Mage", "R2 Stone Gargoyle", "R2 Soul Lantern"]),
            E("The Hornet Hive", 2, ["R2 Hornet Queen", "R2 Royal Guard Bee"], ["R2 Worker Bee", "R2 Hornet Drone", "R2 Honey Slime", "R2 Pollen Sprite"]),
            E("The Bandit Camp", 2, ["R2 Bandit King", "R2 Gilded Effigy"], ["R2 Hoard Rat", "R2 Cutpurse", "R2 Bazaar Bruiser", "R2 Gold Gremlin"]),
            E("The Falling Star", 2, ["R2 Star Prism", "R2 Comet Wraith"], ["R2 Star Mote", "R2 Astral Sentinel", "R2 Meteorling", "R2 Void Watcher"]),
            E("The Rotting Glade", 2, ["R2 Mycelial Horror", "R2 Rotcap Elder"], ["R2 Spore Puff", "R2 Rot Shambler", "R2 Shroom Brute", "R2 Blight Webspinner"]),
            E("The Snake Pit", 2, ["Stheno the Snake Queen"], ["Greater Pit Snake", "Greater Pit Viper", "Yellow Python", "Brown Python", "Fire Python", "Snakepit Guard"]),
            E("The Undead Lair", 2, ["Septavius the Ghost God"], ["Lair Ghost Archer", "Lair Skeleton King", "Lair Mummy Pharaoh", "Lair Construct Titan", "Lair Reaper", "Lair Vampire King"]),
            E("The Abyss of Demons", 2, ["Archdemon Malphas"], ["Brute Warrior of the Abyss", "Demon Mage of the Abyss", "Demon Warrior of the Abyss", "Brute of the Abyss", "Malphas Protector"]),
            E("Candyland", 2, ["Gigacorn", "MegaRototo", "Desire Troll", "Swoll Fairy", "Spoiled Creampuff"], ["Candy Gnome", "Big Creampuff", "Rototo", "Unicorn", "Beefy Fairy", "Hard Candy"]),
            E("The Toxic Sewers", 2, ["DS Gulpord the Slime God"], ["DS Goblin Brute", "DS Goblin Knight", "DS Goblin Sorcerer", "DS Alligator", "DS Yellow Slime", "DS Brown Slime"]),
            E("The Mad Lab", 2, ["Dr Terrible"], ["Mini Bot", "Rampage Cyborg", "Escaped Experiment", "Crusher Abomination", "Enforcer Bot 3000"]),
            E("The Ancient Ruins", 2, ["Ruins Boss Golem"], ["Ruins Elemental", "Ruins Scorpion", "Ruins Vulture", "Ruins Warrior", "Ruins Knight", "Ruins Monolith"]),
            E("The Lucky Spirits", 2, ["Lucky Djinn", "Lucky Ent God"], Godlands, pick: 0),
            E("The Cube", 2, ["Cube God"], ["Djinn", "Beholder", "Flying Brain", "Slime God"]),

            // 3: dungeons
            E("The Manor of the Immortals", 3, ["Lord Ruthven"], ["Nosferatu", "Lesser Bald Vampire", "Lil Feratu", "Hellhound", "Armor Guard", "Coffin Creature"]),
            E("The Woodland Labyrinth", 3, ["Murderous Megamoth", "Epic Mama Megamoth", "Epic Larva"], ["Woodland Goblin", "Woodland Goblin Mage", "Woodland Mini Megamoth", "Mini Larva", "Wooland Armor Squirrel"]),
            E("The Deadwater Docks", 3, ["Jon Bilgewater the Pirate King"], ["Deadwater Docks Lieutenant", "Deadwater Docks Veteran", "Deadwater Docks Admiral", "Deadwater Docks Brawler", "Deadwater Docks Captain", "Deadwater Docks Parrot"]),
            E("The Cave of a Thousand Treasures", 3, ["Golden Oryx Effigy"], ["Treasure Plunderer", "Treasure Robber", "Treasure Rat", "Treasure Oryx Defender", "Treasure Enemy"]),
            E("The Ice Cave", 3, ["ic Esben the Unwilling"], ["Big Yeti", "Mini Yeti", "Snow Bat Mama", "ic shielded king"]),
            E("The Cursed Library", 3, ["Library Avalon the Archivist"], ["Library Deholder", "Library Eye of the Edified", "Library Lips of the Lost", "Library Hand of the Helpless", "Library Headless Specter", "Library Soulless Scholar"]),
            E("The Haunted Graves", 3, ["Arena Grave Caretaker"], ["Arena Risen Brawler", "Arena Risen Warrior", "Arena Risen Mage", "Arena Risen Archer"]),
            E("Skuld's Crypt", 3, ["Ghost of Skuld"], ["Zombie Hulk", "Classic Ghost", "Werewolf"]),
            E("The Sphinx's Court", 3, ["Grand Sphinx"], ["Horrid Reaper", "Jackal Warrior", "Jackal Veteran", "Lion Archer"]),
            E("The Beach Bash", 3, ["Masked Party God"], Godlands),
            E("The Ocean Trench", 3, ["Thessal the Mermaid Goddess"], ["Fishman", "Sea Mare", "Giant Squid", "Sea Horse", "Fishman Warrior"]),
            E("The Secluded Thicket", 3, ["Tezcacoatl the Great Basilisk"], ["Adult Basilisk 3", "Adult Basilisk 6", "Basilisk"]),
            E("The Puppet Theatre", 3, ["The Puppet Master"], ["Puppet Knight", "Paladin Puppet", "Warrior Puppet", "Puppet Wizard", "Puppet Priest", "Assassin Puppet"]),
            E("The Garden Statues", 3, ["Garnet Statue", "Jade Statue"], Godlands, pick: 0),
            E("The Highland Brewery", 3, ["Beer God"], ["Ogre King", "Minotaur", "Horned Drake"]),
            E("The Lair of Draconis", 3, ["NM Red Dragon God", "NM Black Dragon God", "NM Blue Dragon God", "NM Green Dragon God"], ["NM Red Dragon Minion", "NM Black Dragon Minion", "NM Blue Dragon Minion", "NM Green Dragon Minion"]),
            E("The Ghost Ship", 3, ["Ghost Ship"], ["Vengeful Spirit", "Beach Spectre", "Ghost of Roger", "Zombie Pirate Sr"]),

            // 4: harder dungeons and the Elder dungeons
            E("The Tomb of the Ancients", 4, ["Tomb Defender", "Tomb Support", "Tomb Attacker"], ["Jackal Warrior", "Jackal Veteran", "Jackal Assassin", "Jackal Lord", "Lion Archer", "Bloated Mummy", "Beam Priestess"]),
            E("The Elder Snake Pit", 4, ["Elder Stheno the Snake Queen"], ["Elder Snakepit Guard", "Greater Pit Snake", "Greater Pit Viper", "Fire Python"]),
            E("The Elder Undead Lair", 4, ["Elder Septavius the Ghost God"], ["Elder Lair Construct Titan", "Elder Lair Reaper", "Lair Ghost Archer", "Lair Skeleton King"]),
            E("The Elder Abyss", 4, ["Elder Archdemon Malphas"], ["Elder Malphas Protector", "Demon Mage of the Abyss", "Brute Warrior of the Abyss"]),
            E("The Elder Manor", 4, ["Elder Lord Ruthven"], ["Elder Nosferatu", "Elder Coffin Creature", "Elder Armor Guard"]),
            E("The Elder Jungle", 4, ["Elder Mixcoatl the Masked God"], ["Elder Great Coil Snake", "Elder Native Sprite God", "Elder Native White Demon"]),
            E("The Elder Beach", 4, ["Elder Masked Party God"], ["Elder Native Sprite God", "Elder Native White Demon"]),
            E("The Ice Tomb", 4, ["Ice Tomb Defender", "Ice Tomb Support", "Ice Tomb Attacker"], ["Ice Golem", "Big Yeti", "Snow Bat Mama"]),
            E("Oryx's Castle", 4, ["Oryx Stone Guardian Left", "Oryx Stone Guardian Right"], OryxGuard, pick: 0),
            E("The Crystal Prison", 4, ["Crystal Prisoner"], ["Crystal Prisoner Steed", "Oryx Knight", "Oryx Eye Warrior"]),
            E("The Lost Lands", 4, ["Lord of the Lost Lands"], ["Oryx Brute", "Henchman of Oryx", "Oryx Insect Commander"]),
            E("The Elder Cemetery", 4, ["Elder Arena Ghost Bride", "Elder Arena Grave Caretaker"], ["Elder Arena Risen Brawler", "Elder Arena Risen Warrior", "Elder Arena Risen Mage", "Elder Arena Risen Archer", "Elder Werewolf"]),
            E("The Parasite Chambers", 4, ["pD Boss"], ["pD Stalker", "pD Walker", "pD Maw", "pD Painling", "pD Blood Slurp"]),
            E("The Elder Dragons", 4, ["Elder NM Red Dragon God", "Elder NM Black Dragon God", "Elder NM Blue Dragon God"], ["Elder NM Red Dragon Minion", "Elder NM Black Dragon Minion", "Elder NM Blue Dragon Minion"]),
            E("The Cult's Lieutenants", 4, ["LH Argus", "LH Gaius", "LH Basaran", "LH Dirge"], Cultists),

            // 5: Oryx and the end of the realm
            E("Oryx the Mad God", 5, ["Oryx the Mad God 2"], OryxCourt),
            E("Oryx's Sanctuary", 5, ["Oryx the Mad God 3"], OryxCourt),
            E("The Shatters: the Bridge", 5, ["shtrs Bridge Sentinel"], ShattersGuard),
            E("The Shatters: Twilight", 5, ["shtrs Twilight Archmage"], ShattersGuard),
            E("The Cultist Hideout", 5, ["LH Malus 2"], Cultists),
            E("The Void", 5, ["LH Void Entity"], ["LH Void Shade", "LH Greater Void Shade", "LH Void Fragment"]),
            E("The Elder Ocean Trench", 5, ["Elder Thessal the Mermaid Goddess"], ["Elder Fishman", "Elder Sea Mare", "Elder Giant Squid", "Elder Fishman Warrior"]),
            E("The Elder Tomb", 5, ["Elder Tomb Defender", "Elder Tomb Support", "Elder Tomb Attacker"], ElderTombGuard),
            E("The Cnidarian Reef", 5, ["CR Royal Cnidarian"], ["CR Blue Cnidarian", "CR Green Cnidarian", "CR Pink Cnidarian", "CR Gold Cnidarian"]),
            E("The Lost Sentry", 5, ["LH Lost Sentry"], ["Elder LH Oryx Admiral", "Elder LH Oryx Armorbearer", "Elder LH Oryx Swordsman"]),
            E("The Elder Graveyard", 5, ["Elder Ghost of Skuld"], ["Elder Zombie Hulk", "Elder Werewolf", "Elder Classic Ghost"]),
            E("The Doorwarden", 5, ["md Janus the Doorwarden"], OryxGuard),

            // 6: legends
            E("The Hash Cellar", 6, ["Oryx the Mad God 4"], InfernalCourt),
            E("Elysium", 6, ["Oryx the Ascended God"], AscendedCourt),
            E("The Elder Shatters", 6, ["Elder shtrs Bridge Sentinel", "Elder shtrs Twilight Archmage"], ElderShattersGuard),
            E("The Elder Cult", 6, ["Elder LH Argus", "Elder LH Gaius", "Elder LH Basaran", "Elder LH Dirge"], ElderCultists, pick: 2),
            E("The Elder Void", 6, ["Elder LH Void Entity"], ["Elder LH Void Shade", "Elder LH Greater Void Shade", "Elder LH Void Fragment"]),
            E("The Elder Triumvirate", 6, ["Elder Stheno the Snake Queen", "Elder Septavius the Ghost God", "Elder Archdemon Malphas"], ["Elder Snakepit Guard", "Elder Lair Construct Titan", "Elder Lair Reaper", "Elder Malphas Protector"], pick: 0),
            E("Limoz's Lair", 6, ["Elder NM Green Dragon God"], ["Elder NM Green Dragon Minion", "Elder NM Red Dragon Minion"]),
            E("The Ivory Wyvern", 6, ["Elder lod Ivory Wyvern"], ["Elder lod Red Soul Flame", "Elder lod Blue Soul Flame", "Elder lod Green Soul Flame", "Elder lod Black Soul Flame"]),
        };

        // ---- showdowns: every tenth wave, by tier (the 50th and later take turns) ----
        public static readonly ArenaEncounter[] Showdowns =
        {
            E("Showdown: The Gods of the Godlands", 2, ["Red Demon", "Phoenix Lord", "Actual Ghost King", "Cyclops God"], Godlands, pick: 0),
            E("Showdown: The Ancients Awaken", 4, ["Tomb Defender", "Tomb Support", "Tomb Attacker"], ["Jackal Lord", "Beam Priestess", "Lion Archer"], pick: 0),
            E("Showdown: The Battle for the Nexus", 5, ["Oryx the Mad God Deux", "NM Green Dragon God Deux", "Archdemon Malphas Deux", "Murderous Megamoth Deux"], OryxGuard, pick: 0),
            E("Showdown: The Mad God's Court", 6, ["Oryx the Mad God 3", "Oryx the Mad God 4", "Oryx the Ascended God"], OryxCourt.Concat(InfernalCourt).ToArray()),
            E("Showdown: The Elder Cult", 6, ["Elder LH Argus", "Elder LH Gaius", "Elder LH Basaran", "Elder LH Dirge", "Elder LH Molek"], ElderCultists, pick: 3),
            E("Showdown: The Elder Ancients", 6, ["Elder Tomb Defender", "Elder Tomb Support", "Elder Tomb Attacker"], ElderTombGuard, pick: 0),
        };

        // Monsters that wait in their dungeon until another orders them awake (the Garden Statues'
        // altar, Malus for his lieutenants): the Arena wakes them into this state as they spawn.
        public static readonly Dictionary<string, string> Wakes = new Dictionary<string, string>
        {
            ["Garnet Statue"] = "Activate",
            ["Jade Statue"] = "Activate",
            ["LH Argus"] = "Awake",
            ["LH Gaius"] = "Awake",
            ["LH Basaran"] = "Awake",
            ["LH Dirge"] = "Awake",
            ["Elder LH Argus"] = "Awake",
            ["Elder LH Gaius"] = "Awake",
            ["Elder LH Basaran"] = "Awake",
            ["Elder LH Dirge"] = "Awake",
        };

        // What a boss leaves behind that its dungeon keeps for good (the dragons' corpses mark
        // Draconis's altar, the Lost Sentry's spectre stays in its hall): never killable, so the Arena takes them away (a wave ends when its
        // monsters are dead).
        public static bool IsLeftover(string id) => id.EndsWith(" Dragon Dead") || id.EndsWith("LH Spectral Sentry");

        // the Oryxes that spawn in the middle and come with a longer invulnerable start (ArenaPools)
        public static bool IsOryx(string name) => ArenaPools.IsOryx(name);

        public static bool IsShowdown(int wave) => wave >= 10 && wave % 10 == 0;

        public static ArenaEncounter[] OfTier(int tier) => Encounters.Where(e => e.Tier == tier).ToArray();

        // every monster Season 2 can spawn, for the tests and /arenaprobe
        public static IEnumerable<string> AllBosses =>
            Encounters.Concat(Showdowns).SelectMany(e => e.Bosses).Concat(EmpoweredPool.Select(e => e.Boss)).Distinct();

        public static IEnumerable<string> AllMonsters =>
            Encounters.Concat(Showdowns).SelectMany(e => e.Bosses.Concat(e.Minions)).Distinct();
    }

    // Plans an Arena's Season 2 rounds, one wave at a time and in order, so the break before a fight
    // can show what it will be. Each tier deals its encounters from a shuffled pile (no repeats until
    // the pile is through), and no round takes the same shape or twist as the one before.
    public sealed class ArenaPlanner
    {
        private readonly Random _rng;
        private readonly Dictionary<int, Queue<ArenaEncounter>> _piles = new Dictionary<int, Queue<ArenaEncounter>>();
        private readonly Queue<ArenaEncounter> _showdowns = new Queue<ArenaEncounter>();
        private readonly Dictionary<int, ArenaRound> _rounds = new Dictionary<int, ArenaRound>();
        private string _lastEncounter;
        private int _planned;

        public ArenaPlanner(Random rng = null) => _rng = rng ?? new Random();

        // the round for this wave, planning every wave before it first
        public ArenaRound Round(int wave)
        {
            wave = Math.Max(1, wave);
            while (_planned < wave)
            {
                _planned++;
                _rounds[_planned] = Plan(_planned, _rounds.TryGetValue(_planned - 1, out var before) ? before : null);
            }
            return _rounds[wave];
        }

        private ArenaEncounter Deal(int tier)
        {
            tier = Math.Clamp(tier, 1, ArenaSeason2.TierFrom.Length);
            if (!_piles.TryGetValue(tier, out var pile) || pile.Count == 0)
            {
                var all = ArenaSeason2.OfTier(tier).OrderBy(_ => _rng.Next()).ToList();
                // the new pile doesn't start with the one just fought
                if (all.Count > 1 && all[0].Name == _lastEncounter)
                    (all[0], all[^1]) = (all[^1], all[0]);
                _piles[tier] = pile = new Queue<ArenaEncounter>(all);
            }
            var e = pile.Dequeue();
            _lastEncounter = e.Name;
            return e;
        }

        private ArenaEncounter Showdown(int tier)
        {
            // the tier's own; from the 50th, the legends' in turn
            var own = ArenaSeason2.Showdowns.Where(s => s.Tier == tier).ToList();
            if (tier < 6 && own.Count > 0)
                return own[_rng.Next(own.Count)];
            if (_showdowns.Count == 0)
                foreach (var s in ArenaSeason2.Showdowns.Where(s => s.Tier >= 6).OrderBy(_ => _rng.Next()))
                    _showdowns.Enqueue(s);
            return _showdowns.Dequeue();
        }

        private string[] PickBosses(ArenaEncounter e)
        {
            var n = e.Pick <= 0 ? e.Bosses.Length : Math.Min(e.Pick, e.Bosses.Length);
            return e.Bosses.OrderBy(_ => _rng.Next()).Take(n).ToArray();
        }

        private ArenaFormat PickFormat(int wave, int tier, ArenaRound before)
        {
            if (ArenaSeason2.IsShowdown(wave))
                return ArenaFormat.Showdown;
            var odds = tier switch
            {
                1 => new[] { (ArenaFormat.Standard, 85), (ArenaFormat.Horde, 15) },
                2 => new[] { (ArenaFormat.Standard, 62), (ArenaFormat.Horde, 14), (ArenaFormat.Clash, 14), (ArenaFormat.Throwback, 10) },
                _ => new[] { (ArenaFormat.Standard, 48), (ArenaFormat.Horde, 12), (ArenaFormat.Clash, 15), (ArenaFormat.BossRush, 14), (ArenaFormat.Throwback, 11) },
            };
            // a different shape from the one before (a standard round may follow one)
            var choices = odds.Where(o => o.Item1 == ArenaFormat.Standard || before == null || o.Item1 != before.Format).ToArray();
            var roll = _rng.Next(choices.Sum(c => c.Item2));
            foreach (var (format, weight) in choices)
            {
                if (roll < weight)
                    return format;
                roll -= weight;
            }
            return ArenaFormat.Standard;
        }

        private ArenaTwist PickTwist(int wave, ArenaFormat format, ArenaRound before)
        {
            if (_rng.NextDouble() >= ArenaSeason2.TwistChance(wave))
                return ArenaTwist.None;
            var twists = Enum.GetValues<ArenaTwist>().Where(t => t != ArenaTwist.None && t != before?.Twist).ToList();
            // a boss rush has no monsters to swarm, a horde no bosses to make elite
            if (format == ArenaFormat.BossRush)
                twists.Remove(ArenaTwist.Swarm);
            if (format == ArenaFormat.Horde)
                twists.Remove(ArenaTwist.Swarm);
            return twists[_rng.Next(twists.Count)];
        }

        private ArenaRound Plan(int wave, ArenaRound before)
        {
            var tier = ArenaSeason2.TierOf(wave);
            var round = new ArenaRound { Wave = wave, Tier = tier, HealthCap = ArenaSeason2.HealthCap(wave) };
            round.Format = PickFormat(wave, tier, before);
            round.TierPool = ArenaSeason2.OfTier(tier).SelectMany(e => e.Minions).Distinct().ToArray();
            switch (round.Format)
            {
                case ArenaFormat.Showdown:
                {
                    var s = Showdown(tier);
                    round.Title = s.Name;
                    round.Bosses.AddRange(PickBosses(s));
                    round.MinionPool = s.Minions;
                    round.MinionScale = 0.5;
                    break;
                }
                case ArenaFormat.Horde:
                {
                    var e = Deal(tier);
                    round.Title = e.Name;
                    round.MinionPool = e.Minions;
                    round.MinionScale = 3;
                    break;
                }
                case ArenaFormat.Clash:
                {
                    var a = Deal(tier);
                    var first = PickBosses(a)[0];
                    // two Oryxes would share the middle: the second dungeon is another
                    var b = Deal(tier);
                    for (var i = 0; i < 4 && (b == a || ArenaPools.IsOryx(first) && b.Bosses.All(ArenaPools.IsOryx)); i++)
                        b = Deal(tier);
                    round.Title = a.Name + " & " + b.Name;
                    round.Bosses.Add(first);
                    var bosses = PickBosses(b);
                    round.Bosses.Add(bosses.FirstOrDefault(x => !(ArenaPools.IsOryx(x) && ArenaPools.IsOryx(first))) ?? bosses[0]);
                    round.MinionPool = a.Minions.Concat(b.Minions).Distinct().ToArray();
                    round.MinionScale = 0.75;
                    break;
                }
                case ArenaFormat.BossRush:
                {
                    var seen = new HashSet<string>();
                    var titles = new List<string>();
                    for (var i = 0; i < 3; i++)
                    {
                        var e = Deal(tier);
                        var boss = PickBosses(e).FirstOrDefault(b => !seen.Contains(b));
                        if (boss == null)
                            continue;
                        seen.Add(boss);
                        titles.Add(e.Name);
                        round.Relay.Add(boss);
                    }
                    round.Title = string.Join(", ", titles);
                    round.MinionScale = 0;
                    break;
                }
                case ArenaFormat.Throwback:
                {
                    var e = Deal(Math.Max(1, tier - 2));
                    round.Title = e.Name;
                    round.Bosses.AddRange(PickBosses(e));
                    round.MinionPool = e.Minions;
                    round.MinionScale = 1.5;
                    round.Legendary = true;
                    round.TierPool = round.MinionPool;
                    break;
                }
                default:
                {
                    var e = Deal(tier);
                    round.Title = e.Name;
                    round.Bosses.AddRange(PickBosses(e));
                    round.MinionPool = e.Minions;
                    break;
                }
            }
            round.Twist = PickTwist(wave, round.Format, before);
            return round;
        }

        // the wave's monsters, for this many players: from the round's pool, a share from the tier's
        public string[] Minions(ArenaRound round, int players)
        {
            var scale = round.MinionScale * (round.Twist == ArenaTwist.Swarm ? 2 : 1);
            var count = round.MinionPool.Length == 0 ? 0 : ArenaSeason2.MinionCount(round.Wave, players, scale);
            var names = new string[count];
            for (var i = 0; i < count; i++)
            {
                var pool = round.TierPool.Length > 0 && _rng.NextDouble() < ArenaSeason2.TierMinionShare ? round.TierPool : round.MinionPool;
                names[i] = pool[_rng.Next(pool.Length)];
            }
            return names;
        }

        // whether an Empowered boss rises in the fight that starts with this wave, and which; null for none
        public string Empowered(int wave, int lastEmpoweredWave)
        {
            if (ArenaSeason2.IsShowdown(wave) || wave - lastEmpoweredWave < ArenaSeason2.EmpoweredGap)
                return null;
            if (_rng.NextDouble() >= ArenaSeason2.EmpoweredChance(wave))
                return null;
            var pool = ArenaSeason2.EmpoweredFor(ArenaSeason2.TierOf(wave));
            return pool.Length == 0 ? null : pool[_rng.Next(pool.Length)];
        }

        public int NextRandom(int max) => _rng.Next(max);
        public double NextDouble() => _rng.NextDouble();
    }
}
