using Shared.resources;

namespace WorldServer.core.worlds.realm2
{
    /// <summary>
    /// One of Realm 2's event lands: its grounds, scenery, walls, monsters and quests, and the realm
    /// event at its heart, which brings the land with it. The small monsters (Cubelets, Eye
    /// Swarmers, Scarabs, Worker Bees...) aren't in its tables: the bigger ones bring them. The art and the monsters' XML
    /// are tools/build_realm2.py's; their behaviours BehaviorDb.Realm2.cs.
    /// </summary>
    public sealed class Realm2Theme
    {
        public string Key;
        public string Name;
        public string Event;                // the realm event at its heart (CriticalEnemies' id)
        public string Ground, Patches, Arena;
        public string Pools;                // its own pools (tide pools, lava): null for none
        public (string Id, double Weight)[] Decor;
        public double DecorDensity;
        public string Wall;
        public (string Id, double Weight)[] Mobs;
        public string[] Quests;             // one of each is kept in the land
    }

    public static class Realm2Themes
    {
        // the two lands every Realm 2 starts with, where its ordinary realm monsters live: the
        // shore and lowlands, then the midlands and highlands round the two Lucky gods
        public const string Starter = "starter";
        public const string Midlands = "midlands";
        public const string Godlands = "godlands";

        public static readonly Realm2Theme[] Events =
        {
            new Realm2Theme
            {
                Key = "cube", Name = "the Cubic Expanse", Event = "Cube God",
                Ground = "R2 Cube Grid", Patches = "R2 Cube Grid Bright", Arena = "R2 Prism Floor",
                Decor = new[] { ("R2 Cube Crystal", 3.0), ("R2 Floating Cube", 2.0), ("R2 Cube Monolith", 1.0), ("R2 Cube Shards", 3.0) },
                DecorDensity = 0.05, Wall = "R2 Cube Wall",
                Mobs = new[] { ("R2 Prism Sentinel", 2.0), ("R2 Shard Walker", 2.0), ("R2 Cube Overseer", 1.5) },
                Quests = new[] { "R2 Tesseract", "R2 Hypercube Warden" },
            },
            new Realm2Theme
            {
                Key = "penta", Name = "the Watching Dark", Event = "Pentaract",
                Ground = "R2 Dark Soil", Patches = "R2 Ashen Soil", Arena = "R2 Ritual Floor",
                Decor = new[] { ("R2 Dead Tree", 3.0), ("R2 Dead Sapling", 2.0), ("R2 Eye Stalk", 1.5), ("R2 Bone Pile", 1.5) },
                DecorDensity = 0.06, Wall = "R2 Dark Wall",
                Mobs = new[] { ("R2 Floating Eye", 2.0), ("R2 Gazer", 1.5), ("R2 Watcher Stalk", 1.5), ("Beholder", 1.5) },
                Quests = new[] { "R2 Eye of the Pentagram", "R2 Elder Watcher" },
            },
            new Realm2Theme
            {
                Key = "lost", Name = "the Lost Lands", Event = "Lord of the Lost Lands",
                Ground = "R2 Lost Dust", Patches = "R2 Lost Cracked Earth", Arena = "R2 Lost Flagstones",
                Decor = new[] { ("R2 Broken Pillar", 2.0), ("R2 Lost Crystal", 1.5), ("R2 Gravestone", 2.0), ("R2 Withered Shrub", 3.0) },
                DecorDensity = 0.05, Wall = "R2 Lost Wall",
                Mobs = new[] { ("R2 Lost Sentry", 2.0), ("R2 Forsaken Archer", 1.5), ("R2 Wailing Wraith", 1.5), ("R2 Ruin Golem", 1.5) },
                Quests = new[] { "R2 Sentry Captain", "R2 Forgotten Warlord" },
            },
            new Realm2Theme
            {
                Key = "hermit", Name = "the Tidepools", Event = "Hermit God",
                Ground = "R2 Tide Sand", Patches = "R2 Kelp Bed", Arena = "R2 Tide Sand", Pools = "R2 Tide Pool",
                Decor = new[] { ("R2 Coral", 2.0), ("R2 Giant Shell", 1.5), ("R2 Seaweed", 3.0), ("R2 Barnacle Rock", 2.0) },
                DecorDensity = 0.05, Wall = "R2 Coral Wall",
                Mobs = new[] { ("R2 Hermit Brute", 2.0), ("R2 Tide Snapper", 1.5), ("R2 Barnacle Turret", 1.5), ("R2 Kelp Lurker", 1.5) },
                Quests = new[] { "R2 Shellback Champion", "R2 Ancient Snapper" },
            },
            new Realm2Theme
            {
                Key = "ship", Name = "the Wreck Coast", Event = "Ghost Ship",
                Ground = "R2 Wet Shingle", Patches = "R2 Fog Shallows", Arena = "R2 Wreck Planks", Pools = "R2 Fog Shallows",
                Decor = new[] { ("R2 Broken Mast", 1.5), ("R2 Wreck Hull", 1.5), ("R2 Anchor", 1.0), ("R2 Barrel", 2.5) },
                DecorDensity = 0.04, Wall = "R2 Hull Wall",
                Mobs = new[] { ("R2 Drowned Buccaneer", 2.0), ("R2 Bone Gunner", 1.5), ("R2 Wreck Wraith", 1.5) },
                Quests = new[] { "R2 Phantom Quartermaster", "R2 Abyssal Kraken" },
            },
            new Realm2Theme
            {
                Key = "sphinx", Name = "the Sunken Tombs", Event = "Grand Sphinx",
                Ground = "R2 Tomb Sand", Patches = "R2 Dune Sand", Arena = "R2 Sandstone Tiles",
                Decor = new[] { ("R2 Obelisk", 1.0), ("R2 Broken Statue", 1.5), ("R2 Sandstone Block", 2.0), ("R2 Tomb Urn", 2.0) },
                DecorDensity = 0.035, Wall = "R2 Tomb Wall",
                Mobs = new[] { ("R2 Tomb Mummy", 2.0), ("R2 Jackal Guard", 2.0), ("R2 Sun Obelisk", 1.0) },
                Quests = new[] { "R2 Tomb Sentinel", "R2 Sand Scourge" },
            },
            new Realm2Theme
            {
                Key = "skull", Name = "the Bonefields", Event = "Skull Shrine",
                Ground = "R2 Blood Soil", Patches = "R2 Bone Dust", Arena = "R2 Charred Stone",
                Decor = new[] { ("R2 Skull Totem", 1.5), ("R2 Rib Cage", 2.0), ("R2 Bone Brazier", 1.0), ("R2 Blood Spike", 2.0) },
                DecorDensity = 0.045, Wall = "R2 Bone Wall",
                Mobs = new[] { ("R2 Bone Flayer", 2.0), ("R2 Blood Imp", 1.5), ("R2 Shrine Cultist", 1.5) },
                Quests = new[] { "R2 Shrine Keeper", "R2 Marrow Fiend" },
            },
            new Realm2Theme
            {
                Key = "garden", Name = "the Jade Garden", Event = "Garnet Statue",
                Ground = "R2 Moss Grass", Patches = "R2 Petal Grass", Arena = "R2 Garden Stones",
                Decor = new[] { ("R2 Blossom Tree", 3.0), ("R2 Stone Lantern", 1.0), ("R2 Bamboo", 2.0), ("R2 Garden Shrub", 3.0) },
                DecorDensity = 0.07, Wall = "R2 Garden Wall",
                Mobs = new[] { ("R2 Komainu", 2.0), ("R2 Oni Mask", 2.0), ("R2 Kitsune Spirit", 2.0) },
                Quests = new[] { "R2 Oni Warlord", "R2 Jade Golem" },
            },
            new Realm2Theme
            {
                Key = "dragon", Name = "the Ashen Caldera", Event = "Dragon Head",
                Ground = "R2 Obsidian Ground", Patches = "R2 Ash Ground", Arena = "R2 Magma Cracks", Pools = "R2 Lava",
                Decor = new[] { ("R2 Dragon Bones", 1.5), ("R2 Obsidian Spire", 2.0), ("R2 Ember Vent", 1.5), ("R2 Charred Tree", 2.0) },
                DecorDensity = 0.045, Wall = "R2 Obsidian Wall",
                Mobs = new[] { ("R2 Drakeling", 1.5), ("R2 Magma Salamander", 2.0), ("R2 Ember Wyrm", 1.5), ("R2 Obsidian Golem", 1.5) },
                Quests = new[] { "R2 Wyrm Matriarch", "R2 Elder Drake" },
            },
            new Realm2Theme
            {
                Key = "kingdom", Name = "the Forgotten Kingdom", Event = "shtrs Defense System",
                Ground = "R2 Kingdom Slate", Patches = "R2 Kingdom Rubble Floor", Arena = "R2 Kingdom Mosaic",
                Decor = new[] { ("R2 Fallen Column", 2.0), ("R2 Forgotten Statue", 1.5), ("R2 Soul Brazier", 1.0), ("R2 Kingdom Rubble", 3.0) },
                DecorDensity = 0.045, Wall = "R2 Kingdom Wall",
                Mobs = new[] { ("R2 Shattered Knight", 2.0), ("R2 Forgotten Mage", 1.5), ("R2 Stone Gargoyle", 1.5) },
                Quests = new[] { "R2 Forgotten Archmage", "R2 King's Champion" },
            },
            new Realm2Theme
            {
                Key = "bees", Name = "the Honey Meadow", Event = "EH Event Hive",
                Ground = "R2 Meadow Grass", Patches = "R2 Honeycomb Floor", Arena = "R2 Honeycomb Floor", Pools = "R2 Honey Pool",
                Decor = new[] { ("R2 Giant Flower", 3.0), ("R2 Sunflower", 2.0), ("R2 Small Hive", 1.0), ("R2 Clover Bush", 3.0) },
                DecorDensity = 0.07, Wall = "R2 Honeycomb Wall",
                Mobs = new[] { ("R2 Hornet Drone", 2.0), ("R2 Honey Slime", 2.0) },
                Quests = new[] { "R2 Royal Guard Bee", "R2 Hornet Queen" },
            },
        };

        // the ordinary realm's lands, as the realm's map paints them: ground, then scenery
        public static (string Ground, string[] Decor) Land(TerrainType t) => t switch
        {
            TerrainType.ShoreSand => ("Dark Sand", new string[0]),
            TerrainType.ShorePlains => ("Grass", new[] { "Tree A", "Tree B", "Bush", "Shrub", "White Flowers", "Rock Brown" }),
            TerrainType.LowSand => ("Light Sand", new[] { "Saguaro Cactus 1", "Saguaro Cactus 2", "Succulent Plant 1", "Prickly Pear Cactus 1", "Small Saguaro Cactus 1" }),
            TerrainType.LowPlains => ("Grass", new[] { "Rock Brown", "Pink Flowers", "White Flowers", "Large Mushroom", "Small Mushroom", "Green Bush 2" }),
            TerrainType.LowForest => ("Dark Grass", new[] { "Tree A", "Tree B", "Bush", "Shrub", "Green Bush 3" }),
            TerrainType.MidSand => ("Light Sand", new[] { "Prickly Pear Cactus 2", "Succulent Plant 2", "Saguaro Cactus 3", "Small Saguaro Cactus 2" }),
            TerrainType.MidPlains => ("Light Grass", new[] { "Green Bush 2", "Large Mushroom", "Green Bush 8", "Rock Brown", "White Flowers" }),
            TerrainType.MidForest => ("Dark Grass", new[] { "Bush", "Tree B", "Tree A", "Shrub", "Tree Leafless" }),
            TerrainType.HighSand => ("Light Sand", new[] { "Saguaro Cactus 1", "Prickly Pear Cactus 2", "Rock Brown" }),
            TerrainType.HighPlains => ("Yellow Grass", new[] { "Shrub", "Bush", "Tree B", "Tree Leafless", "Yellow Bush 7" }),
            TerrainType.HighForest => ("Bright Grass", new[] { "Rock Brown", "White Flowers", "Small Fir Tree 1", "Large Mushroom", "Fir Tree 2", "Fir Tree 1" }),
            _ => ("Grass", new string[0]),
        };
    }
}
