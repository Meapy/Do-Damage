using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.setpieces;
using WorldServer.core.structures;
using WorldServer.core.worlds.impl;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;
using WorldServer.core.miscfile;

namespace WorldServer.core.worlds
{
    public enum RealmState
    {
        Idle,
        Emptying,
        Closed,
        DoNothing
    }

    public sealed partial class RealmManager
    {
        public int _EventCount = 0;
        public RealmWorld World;

        private static readonly Tuple<string, TauntData>[] CriticalEnemies = new Tuple<string, TauntData>[]
        {
            Tuple.Create("Lucky Djinn", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Lucky Djinn"
            }),
            Tuple.Create("Lucky Ent God", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Lucky Ent God"
            }),
            Tuple.Create("Dragon Head", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Dragon Head"
            }),
            Tuple.Create("EH Event Hive", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Killer Bee Nest"
            }),
            Tuple.Create("Garnet Statue", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "The Statues"
            }),
            Tuple.Create("Skull Shrine", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Skull Shrine"
            }),
            Tuple.Create("Cube God", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Cube God"
            }),
            Tuple.Create("Pentaract", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Pentaract"
            }),
            Tuple.Create("Grand Sphinx", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Grand Sphinx"
            }),
            Tuple.Create("Lord of the Lost Lands", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Lord of the Lost Lands"
            }),
            Tuple.Create("Hermit God", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                NumberOfEnemies = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Hermit God"
            }),
            Tuple.Create("Ghost Ship", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Ghost Ship"
            }),
            Tuple.Create("shtrs Defense System", new TauntData()
            {
                Spawn = new string[] {
                    ""
                },
                Final = new string[] {
                    ""
                },
                Killed = new string[] {
                    ""
                },
                NameOfDeath = "Avatar"
            })
        };

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static readonly Dictionary<TerrainType, Tuple<int, Tuple<string, double>[]>> RegionMobs = new Dictionary<TerrainType, Tuple<int, Tuple<string, double>[]>>()
        {
            { TerrainType.ShoreSand, Tuple.Create(
                100, new []
                {
                    Tuple.Create("Pirate", 0.3),
                    Tuple.Create("Piratess", 0.1),
                    Tuple.Create("Snake", 0.2),
                    Tuple.Create("Scorpion Queen", 0.4),
                })
            },
            { TerrainType.ShorePlains, Tuple.Create(
                150, new []
                {
                    Tuple.Create("Bandit Leader", 0.4),
                    Tuple.Create("Red Gelatinous Cube", 0.2),
                    Tuple.Create("Purple Gelatinous Cube", 0.2),
                    Tuple.Create("Green Gelatinous Cube", 0.2),
                })
            },
            { TerrainType.LowPlains, Tuple.Create(
                200, new []
                {
                    Tuple.Create("Hobbit Mage", 0.5),
                    Tuple.Create("Undead Hobbit Mage", 0.4),
                    Tuple.Create("Sumo Master", 0.1),
                })
            },
            { TerrainType.LowForest, Tuple.Create(
                200, new []
                {
                    Tuple.Create("Elf Wizard", 0.2),
                    Tuple.Create("Goblin Mage", 0.2),
                    Tuple.Create("Easily Enraged Bunny", 0.3),
                    Tuple.Create("Forest Nymph", 0.3),
                })
            },
            { TerrainType.LowSand, Tuple.Create(
                200, new []
                {
                    Tuple.Create("Sandsman King", 0.4),
                    Tuple.Create("Giant Crab", 0.2),
                    Tuple.Create("Sand Devil", 0.4),
                })
            },
            { TerrainType.MidPlains, Tuple.Create(
                150, new []
                {
                    Tuple.Create("Fire Sprite", 0.1),
                    Tuple.Create("Ice Sprite", 0.1),
                    Tuple.Create("Magic Sprite", 0.1),
                    Tuple.Create("Pink Blob", 0.07),
                    Tuple.Create("Gray Blob", 0.07),
                    Tuple.Create("Earth Golem", 0.04),
                    Tuple.Create("Paper Golem", 0.04),
                    Tuple.Create("Big Green Slime", 0.08),
                    Tuple.Create("Swarm", 0.05),
                    Tuple.Create("Wasp Queen", 0.2),
                    Tuple.Create("Shambling Sludge", 0.03),
                    Tuple.Create("Orc King", 0.06)
                })
            },
            { TerrainType.MidForest, Tuple.Create(
                150, new []
                {
                    Tuple.Create("Dwarf King", 0.3),
                    Tuple.Create("Metal Golem", 0.05),
                    Tuple.Create("Clockwork Golem", 0.05),
                    Tuple.Create("Werelion", 0.1),
                    Tuple.Create("Horned Drake", 0.3),
                    Tuple.Create("Red Spider", 0.1),
                    Tuple.Create("Black Bat", 0.1)
                })
            },
            { TerrainType.MidSand, Tuple.Create(
                300, new []
                {
                    Tuple.Create("Desert Werewolf", 0.25),
                    Tuple.Create("Fire Golem", 0.1),
                    Tuple.Create("Darkness Golem", 0.1),
                    Tuple.Create("Sand Phantom", 0.2),
                    Tuple.Create("Nomadic Shaman", 0.25),
                    Tuple.Create("Great Lizard", 0.1),
                })
            },
            { TerrainType.HighPlains, Tuple.Create(
                300, new []
                {
                    Tuple.Create("Shield Orc Key", 0.2),
                    Tuple.Create("Urgle", 0.2),
                    Tuple.Create("Undead Dwarf God", 0.6)
                })
            },
            { TerrainType.HighForest, Tuple.Create(
                300, new []
                {
                    Tuple.Create("Ogre King", 0.4),
                    Tuple.Create("Dragon Egg", 0.1),
                    Tuple.Create("Lizard God", 0.5),
                    Tuple.Create("Beer God", 0.08),
                    Tuple.Create("Candy Gnome", 0.02)
                })
            },
            { TerrainType.HighSand, Tuple.Create(
                250, new []
                {
                    Tuple.Create("Minotaur", 0.4),
                    Tuple.Create("Flayer God", 0.4),
                    Tuple.Create("Flamer King", 0.2)
                })
            },
            { TerrainType.Mountains, Tuple.Create(
                125, new []
                {
                    Tuple.Create("White Demon", 0.09),
                    Tuple.Create("Sprite God", 0.09),
                    Tuple.Create("Medusa", 0.09),
                    Tuple.Create("Ent God", 0.09),
                    Tuple.Create("Beholder", 0.09),
                    Tuple.Create("Flying Brain", 0.09),
                    Tuple.Create("Slime God", 0.09),
                    Tuple.Create("Ghost God", 0.09),
                    Tuple.Create("Rock Bot", 0.01),
                    Tuple.Create("Djinn", 0.09),
                    Tuple.Create("Leviathan", 0.09),
                    Tuple.Create("Arena Headless Horseman", 0.09)
                })
            },
        };

        // most the entities spawn the map themselves as behaviour
        // null will default to useing NamedEntitySetPiece which is the name specified there
        
        // todo max per realm?
        private readonly List<Tuple<string, ISetPiece>> _events = new List<Tuple<string, ISetPiece>>()
        {
            Tuple.Create("Cube God", (ISetPiece) null),
            Tuple.Create("Pentaract", (ISetPiece) new Pentaract()),
            Tuple.Create("Lord of the Lost Lands", (ISetPiece) null),
            Tuple.Create("Ghost Ship", (ISetPiece) null),
            Tuple.Create("Grand Sphinx", (ISetPiece) null),
            Tuple.Create("Hermit God", (ISetPiece) null),
            Tuple.Create("Skull Shrine", (ISetPiece) null),
            Tuple.Create("Garnet Statue", (ISetPiece) new GarnetJade()),
            Tuple.Create("Lucky Ent God", (ISetPiece) null),
            Tuple.Create("Dragon Head", (ISetPiece) null),
            Tuple.Create("Lucky Djinn", (ISetPiece) null),
            Tuple.Create("shtrs Defense System", (ISetPiece)new ShtrsDefenseSystem()),
            Tuple.Create("EH Event Hive", (ISetPiece)null)
        };

        // The parts of each event that are alive while it runs: the first is the one counted
        // (one per running event), the others only show it's still going (the Pentaract's
        // towers, the hive's guardian bees).
        private static readonly Dictionary<string, string[]> EventParts = new Dictionary<string, string[]>()
        {
            { "Cube God", new[] { "Cube God" } },
            { "Pentaract", new[] { "Pentaract", "Pentaract Tower" } },
            { "Lord of the Lost Lands", new[] { "Lord of the Lost Lands" } },
            { "Ghost Ship", new[] { "Ghost Ship" } },
            { "Grand Sphinx", new[] { "Grand Sphinx" } },
            { "Hermit God", new[] { "Hermit God" } },
            { "Skull Shrine", new[] { "Skull Shrine" } },
            { "Garnet Statue", new[] { "Garnet Statue" } },
            { "Lucky Ent God", new[] { "Lucky Ent God" } },
            { "Dragon Head", new[] { "Dragon Head" } },
            { "Lucky Djinn", new[] { "Lucky Djinn" } },
            { "shtrs Defense System", new[] { "shtrs Defense System" } },
            { "EH Event Hive", new[] { "EH Event Hive", "EH Blue Guardian Bee", "EH Yellow Guardian Bee", "EH Red Guardian Bee" } },
        };

        // The event monsters whose health follows the players near them (ScaleEventHealth).
        private static readonly string[] ScaledEventMonsters =
        {
            "Cube God", "Pentaract Tower", "Lord of the Lost Lands", "Ghost Ship", "Grand Sphinx", "Hermit God",
            "Skull Shrine", "Garnet Statue", "Jade Statue", "Lucky Ent God", "Lucky Djinn", "Dragon Head",
            "shtrs Defense System", "EH Event Hive", "EH Blue Guardian Bee", "EH Yellow Guardian Bee", "EH Red Guardian Bee"
        };

        // Events too hard for a small group: they only come once the realm has had this many players.
        private static readonly Dictionary<string, int> EventMinPlayers = new Dictionary<string, int>()
        {
            { "shtrs Defense System", 5 },
        };

        // ---- How a realm scales with its players (SETUP-LOCAL.md, "Realms for small groups") ----

        /// <summary>Events to defeat before the realm closes: 8 for 1-3 players, one more per player after, 25 from 20.</summary>
        public static int EventsToClose(int players) => Math.Clamp(players + 5, 8, 25);

        /// <summary>Events running at once: one for a small group, two from 6 players, three from 12.</summary>
        public static int EventsAtOnce(int players) => players >= 12 ? 3 : players >= 6 ? 2 : 1;

        /// <summary>
        /// An event boss's health, as a share of its XML health, for the players who have come
        /// near it: 60% for 1-2, 20% more for each one after, at most 300% (from 14).
        /// </summary>
        public static double EventHealthShare(int playersNear) => Math.Min(3.0, 0.6 + 0.2 * Math.Max(0, playersNear - 2));

        private const int EventGapMs = 15000;           // after an event is spawned or defeated, before the next
        private const int StaleEventMs = 10 * 60000;    // an event nobody has touched for this long doesn't hold up the next
        private const int MaxEventsAlive = 4;           // stale ones included
        private const float ScaleRange = 30;            // tiles: who counts as near an event boss

        private sealed class EventPart
        {
            public Enemy Enemy;
            public string Event;
            public bool Counted;        // the event's first part: one per running event
            public bool Static;         // from the realm's own set pieces, not a spawned event
            public long Since;
            // health scaling: its health on arrival (XML, after rare/epic/legendary), the accounts seen near it
            public bool Scaled;
            public int BaseHealth;
            public HashSet<int> Near;
        }

        private readonly List<EventPart> _parts = new List<EventPart>();
        private readonly Dictionary<ushort, (string Event, bool Counted, bool Scaled)> _partTypes = new Dictionary<ushort, (string, bool, bool)>();
        private long _now;
        private long _lastEventChange = -EventGapMs;
        private long _lastScale;
        private long _lastSample;
        private int _beesDown;
        private int _spawned;

        // the most players the realm has had at once in the last 5 minutes (one sample per 10 s)
        private readonly int[] _playerSamples = new int[30];
        private int _sampleIndex;

        public int RecentPlayers
        {
            get
            {
                var max = World.Players.Count;
                foreach (var n in _playerSamples)
                    max = Math.Max(max, n);
                return Math.Max(1, max);
            }
        }

        // Realm 2 starts with all its events up, and only needs more for a crowd (RealmManager.Realm2.cs)
        public int EventsNeeded => Layout != null ? Math.Max(Layout.EventCount, EventsToClose(RecentPlayers)) : EventsToClose(RecentPlayers);

        /// <summary>For /realmstatus.</summary>
        public string Status()
        {
            var players = RecentPlayers;
            var parts = string.Join(", ", _parts.Where(p => p.Enemy.World == World && !p.Enemy.Dead && (p.Counted || p.Scaled))
                .Select(p => $"{p.Enemy.ObjectDesc.IdName} {p.Enemy.Health}/{p.Enemy.MaxHealth} ({(int)p.Enemy.X}, {(int)p.Enemy.Y})"));
            var spawned = World.Enemies.Values.Count(e => e.Spawned);
            return $"{_EventCount}/{EventsNeeded} events defeated; {World.Players.Count} players now, scaled for {players}, " +
                $"{EventsAtOnce(players)} at once; {World.Enemies.Count} enemies, {spawned} spawned by others; running: {(parts.Length == 0 ? "none" : parts)}" +
                (Layout != null ? "; " + LayoutStatus() : "");
        }

        private int[] EnemyCounts = new int[12];
        private int[] EnemyMaxCounts = new int[12];
        private long LastEnsurePopulationTime;
        private long LastAnnouncementTime;
        private long LastQuestTime;

        public RealmState CurrentState;
        public bool DisableSpawning;

        public RealmManager(RealmWorld world)
        {
            World = world;
            CurrentState = RealmState.Idle;
        }

        public void Update(ref TickTime time)
        {
            switch (CurrentState)
            {
                case RealmState.Idle:
                    {
                        _now = time.TotalElapsedMs;
                        if (time.TotalElapsedMs - LastQuestTime >= 5000)
                        {
                            EnsureQuest();
                            LastQuestTime = time.TotalElapsedMs;
                        }

                        if (time.TotalElapsedMs - _lastScale >= 1000)
                        {
                            ScaleEventHealth();
                            _lastScale = time.TotalElapsedMs;
                        }

                        if (time.TotalElapsedMs - LastAnnouncementTime >= 20000)
                        {
                            HandleAnnouncements();
                            LastAnnouncementTime = time.TotalElapsedMs;
                        }

                        if (time.TotalElapsedMs - LastEnsurePopulationTime >= 60000)
                        {
                            EnsurePopulation();
                            if (Layout != null)
                                LayoutPopulation();
                            LastEnsurePopulationTime = time.TotalElapsedMs;
                        }
                    }
                    break;
                case RealmState.Emptying:
                    {
                        BroadcastMsg("RAAHH MY TROOPS HAVE FAILED ME!");
                        BroadcastMsg("THIS REALM SHALL NOT FALL!!");

                        CurrentState = RealmState.DoNothing;

                        foreach (var e in World.Enemies.Values)
                            World.LeaveWorld(e);
                        World.StartNewTimer(30000, (w, t) => CurrentState = RealmState.Closed);
                    }
                    break;
                case RealmState.Closed:
                    {
                        BroadcastMsg("ENOUGH WAITING!");
                        BroadcastMsg("YOU SHALL MEET YOUR DOOM BY MY HAND!!!");
                        BroadcastMsg("GUARDIANS AWAKEN AND KILL THESE FOOLS!!!");
                        SendPlayersToCastle();

                        CurrentState = RealmState.DoNothing;
                    }
                    break;
                case RealmState.DoNothing:
                    break;
            }
        }

        // Every 5 s: start another event if the realm has fewer running than its players call
        // for. An event is running while any of its parts is alive, so one that ends without a
        // kill (a boss that leaves) no longer holds the realm up.
        public void EnsureQuest()
        {
            if (DisableSpawning)
                return;

            _playerSamples[_sampleIndex] = Math.Max(_playerSamples[_sampleIndex], World.Players.Count);
            if (_now - _lastSample >= 10000)
            {
                _sampleIndex = (_sampleIndex + 1) % _playerSamples.Length;
                _playerSamples[_sampleIndex] = 0;
                _lastSample = _now;
            }

            // a gap between events, which also lets the last one's parts reach the world
            if (_now - _lastEventChange < EventGapMs)
                return;

            if (Layout != null)
            {
                EnsureLayoutEvents();
                return;
            }

            _parts.RemoveAll(p => p.Enemy.World != World || p.Enemy.Dead);
            var alive = 0;
            var fresh = 0;
            var counted = new HashSet<string>();
            foreach (var part in _parts)
            {
                if (!part.Counted || part.Static)
                    continue;
                counted.Add(part.Event);
                alive++;
                // untouched for 10 minutes (out of the way, or too hard): let another come
                if (_now - part.Since < StaleEventMs || Touched(part))
                    fresh++;
            }
            // an event whose counted part has gone while its others fight on (the hive's bees)
            foreach (var evt in _parts.Where(p => !p.Counted && !p.Static && p.Event != null).Select(p => p.Event).Distinct())
                if (!counted.Contains(evt))
                {
                    alive++;
                    fresh++;
                }

            if (fresh >= EventsAtOnce(RecentPlayers) || alive >= MaxEventsAlive)
                return;

            var next = PickEvent();
            if (next == null)
                return;
            SpawnEvent(next.Item1, next.Item2);
            _spawned++;
            _lastEventChange = _now;
        }

        // Has anyone hit this event: its counted part or any part that came with it (the
        // Pentaract's centre can't be hit, only its towers)?
        private bool Touched(EventPart counted) =>
            _parts.Any(p => p.Event == counted.Event && Math.Abs(p.Since - counted.Since) < 5000 && p.Enemy.DamageCounter.TotalDamage > 0);

        // A random event the realm can still have, among those its players can take on.
        private Tuple<string, ISetPiece> PickEvent()
        {
            var players = RecentPlayers;
            var choices = _events.Where(e => !EventMinPlayers.TryGetValue(e.Item1, out var min) || players >= min).ToList();
            if (choices.Count == 0)
                choices = _events;
            if (choices.Count == 0)
                return null;

            var evt = choices[Random.Shared.Next(0, choices.Count)];
            var gameData = World.GameServer.Resources.GameData;
            if (gameData.IdToObjectType.TryGetValue(evt.Item1, out var type) && gameData.ObjectDescs[type].PerRealmMax == 1)
                _events.Remove(evt);
            return evt;
        }

        /// <summary>RealmWorld.AddToWorld: keep track of the parts of events.</summary>
        public void OnEnemyAdded(Enemy enemy)
        {
            if (_partTypes.Count == 0)
                LoadPartTypes();
            if (!_partTypes.TryGetValue(enemy.ObjectType, out var kind))
                return;

            // before the first event, these come from the realm's own set pieces (the Shatters'
            // Defense System in the mountains): scaled, but they don't hold up the events
            var part = new EventPart { Enemy = enemy, Event = kind.Event, Counted = kind.Counted, Static = _spawned == 0, Since = _now, Scaled = kind.Scaled && enemy.MaxHealth > 0 };
            if (part.Scaled)
            {
                part.BaseHealth = enemy.MaxHealth;
                part.Near = new HashSet<int>();
                enemy.RealmScaled = true;
                SetEventHealth(part, 1);
            }
            _parts.Add(part);
        }

        private void LoadPartTypes()
        {
            var ids = World.GameServer.Resources.GameData.IdToObjectType;
            var scaled = new HashSet<string>(ScaledEventMonsters);
            foreach (var (evt, parts) in EventParts)
                for (var i = 0; i < parts.Length; i++)
                    if (ids.TryGetValue(parts[i], out var type))
                        _partTypes[type] = (evt, i == 0, scaled.Contains(parts[i]));
            // scaled, but not a sign the event is running (the Jade Statue can outlive the Garnet one)
            foreach (var name in ScaledEventMonsters)
                if (ids.TryGetValue(name, out var type) && !_partTypes.ContainsKey(type))
                    _partTypes[type] = (null, false, true);
        }

        // Each second: count the players who have come near each event boss (each account once,
        // as ScaleHP2 did by name) and raise its health to the share they call for. It never drops, so
        // walking away doesn't make a boss easier.
        private void ScaleEventHealth()
        {
            foreach (var part in _parts)
            {
                if (!part.Scaled || part.Enemy.World != World || part.Enemy.Dead)
                    continue;
                var before = part.Near.Count;
                foreach (var entity in World.PlayersCollision.HitTest(part.Enemy.X, part.Enemy.Y, ScaleRange))
                    if (entity is Player p && p.SqDistTo(part.Enemy) < ScaleRange * ScaleRange)
                        part.Near.Add(p.AccountId);
                if (part.Near.Count != before)
                    SetEventHealth(part, part.Near.Count);
            }
        }

        private static void SetEventHealth(EventPart part, int playersNear)
        {
            var enemy = part.Enemy;
            var share = EventHealthShare(playersNear);
            var target = Math.Max(1, (int)Math.Round(part.BaseHealth * share));
            enemy.XpScale = (float)Math.Min(1.0, share);
            if (target <= enemy.MaxHealth)
            {
                // lowered only on arrival, at full health
                if (target < enemy.MaxHealth && enemy.Health >= enemy.MaxHealth)
                    enemy.MaxHealth = enemy.Health = target;
                return;
            }
            enemy.Health += target - enemy.MaxHealth;
            enemy.MaxHealth = target;
        }

        public void AnnounceMVP(Enemy eventDead, string name)
        {
            var hitters = eventDead.DamageCounter.GetHitters();
            if (hitters.Count == 0)
                return;

            var mvp = hitters.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            if (mvp == null)
                return;

            var playerCount = hitters.Count;
            var dmgPercentage = (float)Math.Round(100.0 * (hitters[mvp] / (double)eventDead.DamageCounter.TotalDamage), 0);
            if (eventDead.Name.Contains("Pentaract"))
                dmgPercentage = (float)Math.Round(dmgPercentage / 5, 0);

            var sb = new StringBuilder($"{mvp.Name} dealt {dmgPercentage}% damage to {name}");
            if (playerCount > 1)
            {
                var playerAssist = playerCount - 1;
                if (playerAssist == 1)
                    _ = sb.Append(" with one other person helping");
                else
                    _ = sb.Append($" with {playerAssist} people helping");
            }
            else
                _ = sb.Append(" solo");
            _ = sb.Append("!");
            var text = new Text()
            {
                BubbleTime = 0,
                NumStars = -1,
                Name = "Oryx the Mad God",
                Txt = sb.ToString(),
                TextColor = 0xFFFFFF,
                NameColor = 0xFF681F
            };
            World.Broadcast(text);
        }
        public void CountingEvents(string eventDead)
        {
            if (DisableSpawning)
                return;
            _lastEventChange = _now;

            _EventCount++;
            var needed = EventsNeeded;
            RealmBroadcast($"{eventDead} has been defeated! [{_EventCount}/{needed}]");
            if (_EventCount >= needed)
            {
                // everyone here helped close it (quests and achievements)
                if (!World.Closed)
                    foreach (var player in World.Players.Values)
                        player.Quests?.Event(new Shared.quests.QuestEvent(Shared.quests.QuestEvents.RealmClosed));
                _ = World.CloseRealm();
            }
        }

        // the first event comes from EnsureQuest, 15 s in (EventGapMs)
        public void Init()
        {
            var w = World.Map.Width;
            var h = World.Map.Height;
            var stats = new int[12];

            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var tile = World.Map[x, y];
                    if (tile.Terrain != TerrainType.None)
                        stats[(int)tile.Terrain - 1]++;
                }

            foreach (var i in RegionMobs)
            {
                var terrain = i.Key;
                var idx = (int)terrain - 1;
                var enemyCount = stats[idx] / i.Value.Item1;
                EnemyMaxCounts[idx] = enemyCount;
                EnemyCounts[idx] = 0;

                for (var j = 0; j < enemyCount; j++)
                {
                    var objType = GetRandomObjType(i.Value.Item2);

                    if (objType == 0)
                        continue;

                    EnemyCounts[idx] += Spawn(World.GameServer.Resources.GameData.ObjectDescs[objType], terrain, w, h);

                    if (EnemyCounts[idx] >= enemyCount)
                        break;
                }
            }
            if (Layout != null)
                InitLayout();
        }

        public void OnEnemyKilled(Enemy enemy, Player killer)
        {
            // the Killer Bee Nest is one event, done when its third guardian bee dies (the hive
            // itself ends first, and a big hit can skip a bee's 10% call to OnProcEvent)
            var name = enemy.ObjectDesc.IdName;
            if (name == "EH Event Hive")
                return;
            if (name is "EH Blue Guardian Bee" or "EH Yellow Guardian Bee" or "EH Red Guardian Bee")
            {
                if (++_beesDown % 3 == 0)
                    CountingEvents("Killer Bee Nest");
                return;
            }

            foreach (var dat in CriticalEnemies)
                if (enemy.ObjectDesc.IdName == dat.Item1)
                {
                    CountingEvents(dat.Item2.NameOfDeath);
                    //AnnounceMVP(enemy, dat.Item2.NameOfDeath);
                    break;
                }
        }

        public void OnProcEvent(string enemy, Player killer)
        {
            // the hive's guardian bees call this at 10% health: counted by their deaths instead
            if (enemy == "EH Event Hive")
                return;

            foreach (var dat in CriticalEnemies)
                if (enemy == dat.Item1)
                {
                    CountingEvents(dat.Item2.NameOfDeath);
                    break;
                }
        }

        public void OnPlayerEntered(Player player)
        {
            if (Layout != null)
            {
                LayoutWelcome(player);
                return;
            }
            player.SendInfo("Welcome to Do Damage");
            player.SendInfo("Use [WASDQE] to move; click to shoot!");
            player.SendInfo("Type \"/commands\" for more help");
            player.SendEnemy("Oryx the Mad God", "You are a pest to my Realm!");
        }

        private static double GetNormal(Random rand)
        {
            // Use Box-Muller algorithm
            var u1 = GetUniform(rand);
            var u2 = GetUniform(rand);
            var r = Math.Sqrt(-2.0 * Math.Log(u1));
            var theta = 2.0 * Math.PI * u2;

            return r * Math.Sin(theta);
        }

        private static double GetNormal(Random rand, double mean, double standardDeviation) => mean + standardDeviation * GetNormal(rand);

        private static double GetUniform(Random rand) => ((uint)(rand.NextDouble() * uint.MaxValue) + 1.0) * 2.328306435454494e-10;

        private void BroadcastMsg(string message) => World.GameServer.ChatManager.Oryx(World, message);

        // Realm 2's news is its own players' only
        private void RealmBroadcast(string message)
        {
            if (Layout == null)
                World.GameServer.ChatManager.Realm(message);
            else if (!string.IsNullOrWhiteSpace(message))
                foreach (var player in World.Players.Values)
                    player.RealmRecieved(message);
        }

        private void EnsurePopulation()
        {
            RecalculateEnemyCount();

            var state = new int[12];
            var diff = new int[12];
            var c = 0;

            for (var i = 0; i < state.Length; i++)
            {
                if (EnemyCounts[i] > EnemyMaxCounts[i] * 1.5) //Kill some
                {
                    state[i] = 1;
                    diff[i] = EnemyCounts[i] - EnemyMaxCounts[i];
                    c++;
                    continue;
                }

                if (EnemyCounts[i] < EnemyMaxCounts[i] * 0.75) //Add some
                {
                    state[i] = 2;
                    diff[i] = EnemyMaxCounts[i] - EnemyCounts[i];
                    continue;
                }

                state[i] = 0;
            }

            foreach (var i in World.Enemies) //Kill
            {
                var idx = (int)i.Value.Terrain - 1;

                if (idx == -1 || state[idx] == 0 || i.Value.GetNearestEntity(10, true) != null || diff[idx] == 0)
                    continue;

                if (state[idx] == 1)
                {
                    World.LeaveWorld(i.Value);
                    diff[idx]--;
                    if (diff[idx] == 0)
                        c--;
                }

                if (c == 0)
                    break;
            }

            var w = World.Map.Width;
            var h = World.Map.Height;

            for (var i = 0; i < state.Length; i++) //Add
            {
                if (state[i] != 2)
                    continue;

                var x = diff[i];
                var t = (TerrainType)(i + 1);

                for (var j = 0; j < x;)
                {
                    var objType = GetRandomObjType(RegionMobs[t].Item2);

                    if (objType == 0)
                        continue;

                    j += Spawn(World.GameServer.Resources.GameData.ObjectDescs[objType], t, w, h);
                }
            }

            RecalculateEnemyCount();
        }

        private ushort GetRandomObjType(IEnumerable<Tuple<string, double>> dat)
        {
            var p = Random.Shared.NextDouble();

            double n = 0;
            ushort objType = 0;

            foreach (var k in dat)
            {
                n += k.Item2;

                if (n > p)
                {
                    objType = World.GameServer.Resources.GameData.IdToObjectType[k.Item1];
                    break;
                }
            }

            return objType;
        }

        private void HandleAnnouncements()
        {
            if (World.Closed)
                return;

            var taunt = CriticalEnemies[Random.Shared.Next(0, CriticalEnemies.Length)];
            var count = 0;

            foreach (var i in World.Enemies)
            {
                var desc = i.Value.ObjectDesc;

                if (desc == null || desc.IdName != taunt.Item1)
                    continue;

                count++;
            }

            if (count == 0)
                return;

            if (count == 1 && taunt.Item2.Final != null || taunt.Item2.Final != null && taunt.Item2.NumberOfEnemies == null)
            {
                var arr = taunt.Item2.Final;
                var msg = arr[Random.Shared.Next(0, arr.Length)];

                BroadcastMsg(msg);
            }
            else
            {
                var arr = taunt.Item2.NumberOfEnemies;

                if (arr == null)
                    return;

                var msg = arr[Random.Shared.Next(0, arr.Length)];
                msg = msg.Replace("{COUNT}", count.ToString());

                BroadcastMsg(msg);
            }
        }

        private void RecalculateEnemyCount()
        {
            for (var i = 0; i < EnemyCounts.Length; i++)
                EnemyCounts[i] = 0;

            foreach (var i in World.Enemies)
            {
                if (i.Value.Terrain == TerrainType.None)
                    continue;

                EnemyCounts[(int)i.Value.Terrain - 1]++;
            }
        }

        private int Spawn(ObjectDesc desc, TerrainType terrain, int w, int h)
        {
            Entity entity;

            var ret = 0;
            var pt = new IntPoint();

            if (desc.Spawn != null)
            {
                var num = (int)GetNormal(Random.Shared, desc.Spawn.Mean, desc.Spawn.StdDev);

                if (num > desc.Spawn.Max)
                    num = desc.Spawn.Max;
                else if (num < desc.Spawn.Min)
                    num = desc.Spawn.Min;

                do
                {
                    pt.X = Random.Shared.Next(0, w);
                    pt.Y = Random.Shared.Next(0, h);
                } while (World.Map[pt.X, pt.Y].Terrain != terrain || !World.IsPassable(pt.X, pt.Y) || World.AnyPlayerNearby(pt.X, pt.Y));

                for (var k = 0; k < num; k++)
                {
                    entity = Entity.Resolve(World.GameServer, desc.ObjectType);
                    entity.Move(pt.X + (float)(Random.Shared.NextDouble() * 2 - 1) * 5, pt.Y + (float)(Random.Shared.NextDouble() * 2 - 1) * 5);
                    (entity as Enemy).Terrain = terrain;
                    World.EnterWorld(entity);
                    ret++;
                }

                return ret;
            }

            do
            {
                pt.X = Random.Shared.Next(0, w);
                pt.Y = Random.Shared.Next(0, h);
            }
            while (World.Map[pt.X, pt.Y].Terrain != terrain || !World.IsPassable(pt.X, pt.Y) || World.AnyPlayerNearby(pt.X, pt.Y));

            entity = Entity.Resolve(World.GameServer, desc.ObjectType);
            entity.Move(pt.X, pt.Y);
            (entity as Enemy).Terrain = terrain;
            World.EnterWorld(entity);
            ret++;
            return ret;
        }

        private void SendPlayersToCastle()
        {
            if (World.Players.Count == 0)
                return;

            // built on a pool thread (building it here held up the realm and its dungeons)
            World.GameServer.WorldManager.CreateNewWorldAsync("Oryx's Castle", World, castle =>
            {
                if (castle == null)
                {
                    Log.Error("Unable to create Oryx's Castle for closing realm {0}.", World.IdName);
                    return;
                }

                foreach (var player in World.Players.Values.ToArray())
                    player.Reconnect(castle);
            });
        }

        private void SpawnEvent(string name, ISetPiece setpiece, int x = 0, int y = 0)
        {
            if (DisableSpawning)
                return;

            bool Unfit(IntPoint p) => World.Map[p.X, p.Y].Terrain < TerrainType.Mountains || World.Map[p.X, p.Y].Terrain > TerrainType.MidForest || !World.IsPassable(p.X, p.Y, true) || World.AnyPlayerNearby(p.X, p.Y);

            var pt = new IntPoint(x, y);
            // out of sight but not far from someone (25-80 tiles), so a few players don't cross
            // the whole realm for each event; anywhere in the band if nowhere near anyone fits
            var players = World.Players.Values.ToArray();
            for (var tries = 0; players.Length > 0 && Unfit(pt) && tries < 400; tries++)
            {
                var near = players[Random.Shared.Next(players.Length)];
                var angle = Random.Shared.NextDouble() * Math.PI * 2;
                var dist = 25 + Random.Shared.NextDouble() * 55;
                pt.X = Math.Clamp((int)(near.X + Math.Cos(angle) * dist), 0, World.Map.Width - 1);
                pt.Y = Math.Clamp((int)(near.Y + Math.Sin(angle) * dist), 0, World.Map.Height - 1);
            }
            while (Unfit(pt))
            {
                pt.X = Random.Shared.Next(0, World.Map.Width);
                pt.Y = Random.Shared.Next(0, World.Map.Height);
            }

            var sp = setpiece ?? new NamedEntitySetPiece(name);

            pt.X -= (sp.Size - 1) / 2;
            pt.Y -= (sp.Size - 1) / 2;
            sp.RenderSetPiece(World, pt);

            var taunt = $"{name} has been spawned!";

            RealmBroadcast(taunt);
        }

        private struct TauntData
        {
            public string[] Final;
            public string[] Killed;
            public string NameOfDeath;
            public string[] NumberOfEnemies;
            public string[] Spawn;
        }
    }
}
