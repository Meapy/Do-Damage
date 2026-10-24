using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.utils;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds.impl
{
    // The Support Bot by the Nexus spawn: it tells new players about /gear (now and then, and to
    // each player who comes near) and every ten minutes puts out a wave of GIFT_COUNT XP gifts
    // around the spawn (an admin's "/spawn 100 xp gift"), a new wave only once every gift of the
    // last one has been shot down. Both are objects the client already knows (Support Bot from
    // Doomed Realms' pets, XP Gift from the testing objects: 600 HP, no attacks, the usual kill
    // experience); the bot's pet behaviour is cleared, so it stands still. Halfway between the
    // gift checks, every ten minutes too, it opens a portal to a random dungeon south of the
    // spawn, which stays until the next one replaces it or its dungeon is cleared. Players inside
    // stay: a dungeon lives while it has players, whatever happens to its portal.
    public sealed class NexusGuide
    {
        private const string BOT = "Support Bot";
        private const string GIFT = "XP Gift";
        private const string NAME = "#Support Bot";
        private const int GIFT_COUNT = 100;
        private const float GIFT_INTERVAL = 600f;   // seconds
        private const float TALK_INTERVAL = 45f;
        private const float TALK_RANGE = 15f;
        private const float GREET_RANGE = 5f;
        private const float GREET_AGAIN = 300f;     // a player is greeted again after this long

        // beside the spawn rug (tools/build_castle_maps.py, CENTRE (48, 42), the rug 5 tiles
        // square, the plaza 10.5 tiles round): the bot left of the Armoire, the gifts on the open
        // plaza tiles around the rug
        private static readonly (float X, float Y) BotSpot = (46.5f, 46.5f);
        private static readonly (float X, float Y) Centre = (48.5f, 42.5f);
        private const float GIFT_MIN_RADIUS = 3.5f;
        private const float GIFT_MAX_RADIUS = 10f;
        // the dungeon portal: on the path south of the rug, past the Armoire
        private static readonly (float X, float Y) PortalSpot = (48.5f, 49.5f);

        // the dungeons it picks from: those realm monsters drop, and a few more of the same kind
        private static readonly string[] DungeonPortals =
        {
            "Pirate Cave Portal", "Forest Maze Portal", "Spider Den Portal", "Snake Pit Portal", "Glowing Portal",
            "Undead Lair Portal", "Abyss of Demons Portal", "Toxic Sewers Portal", "Treasure Cave Portal",
            "Mad Lab Portal", "Manor of the Immortals Portal", "Puppet Theatre Portal", "Haunted Cemetery Portal",
            "Woodland Labyrinth", "The Crawling Depths", "Candyland Portal", "Ice Cave Portal",
            "Tomb of the Ancients Portal", "Ocean Trench Portal", "Davy Jones' Locker Portal", "Mountain Temple Portal",
        };

        // tips for new players, said in turn, each followed by the XP gifts' news (GiftNews)
        private static readonly string[] Lines =
        {
            "New here? Type /gear for free starter gear!",
            "Type /godlands to jump straight to the Godlands, the realm's toughest and richest part. Get some levels first!",
            "Type /realm to go to a realm, and /nexus to come back here from anywhere.",
            "Stand by the fountains around the spawn to heal.",
            "Type /vault to store items in your vault; they stay safe when a character dies.",
            "Type /shop to see the shops: abilities, weapons, armor, potions and keys for fame.",
            "The Divine Forge in the south yard, west of the Arena portal, forges items and buys the ones you don't need for fame.",
            "Type /tell <name> <message> to whisper, and /trade <name> to trade.",
            "Type /commands to see every command you can use.",
            "Do Damage is open source: github.com/Meapy/Do-Damage",
            "Elder dungeons drop much better loot and have a chance of dropping a potion that will max a stat!"
        };

        private readonly World _world;
        private Entity _bot;
        private readonly List<Entity> _gifts = new List<Entity>();
        private float _giftTimer;
        private float _dungeonTimer = GIFT_INTERVAL / 2;
        private Entity _portal;
        private float _clearedFor;
        private const float CLEARED_WAIT = 15f;     // seconds with no boss left before a dungeon counts as cleared
        private List<ushort> _dungeons;
        private float _talkTimer = TALK_INTERVAL;
        private float _greetCheck;
        private int _line;
        private bool _giftNewsNext;
        private readonly Dictionary<int, float> _greeted = new Dictionary<int, float>();   // player id, time left
        private float _clock;

        public NexusGuide(World world) => _world = world;

        public void Init()
        {
            var (x, y) = FindSpot(BotSpot);
            _bot = _world.CreateNewEntity(BOT, x, y);
            if (_bot == null)
                return;
            _bot.SwitchTo(null);    // not the pet that follows its owner
            _bot.ApplyPermanentConditionEffect(ConditionEffectIndex.Invincible);

            // only portals whose dungeon this server has
            var data = _world.GameServer.Resources.GameData;
            _dungeons = DungeonPortals
                .Select(n => data.IdToObjectType.TryGetValue(n, out var t) ? t : (ushort)0)
                .Where(t => t != 0 && data.Portals.TryGetValue(t, out var d) && d.DungeonName != null && data.GetWorld(d.DungeonName) != null)
                .ToList();
        }

        public void Update(ref TickTime time)
        {
            if (_bot == null)
                return;
            var dt = time.DeltaTime;
            _clock += dt;

            _giftTimer -= dt;
            if (_giftTimer <= 0)
            {
                _giftTimer = GIFT_INTERVAL;
                _gifts.RemoveAll(g => g.Dead);
                if (_gifts.Count == 0)
                    SpawnGifts();
            }

            _dungeonTimer -= dt;
            if (_dungeonTimer <= 0)
            {
                _dungeonTimer = GIFT_INTERVAL;
                OpenDungeon();
            }

            CheckCleared(dt);

            _talkTimer -= dt;
            if (_talkTimer <= 0)
            {
                _talkTimer = TALK_INTERVAL;
                if (_giftNewsNext)
                    SayNear(GiftNews());
                else
                {
                    SayNear(Lines[_line]);
                    _line = (_line + 1) % Lines.Length;
                }
                _giftNewsNext = !_giftNewsNext;
            }

            _greetCheck -= dt;
            if (_greetCheck <= 0)
            {
                _greetCheck = 0.5f;
                Greet();
            }
        }

        // the portal's dungeon is cleared once a boss has died there and no boss has been left for
        // CLEARED_WAIT seconds (a boss that spawns after another dies keeps it open), or it closed
        // with none left: close the portal
        private void CheckCleared(float dt)
        {
            if (!(_portal is Portal portal) || portal.Dead || !(portal.WorldInstance is World dungeon))
                return;
            _clearedFor = dungeon.QuestKills > 0 && dungeon.Quests.Count == 0 ? _clearedFor + dt : 0;
            // a cleared dungeon whose players have all left (so it closed) doesn't wait
            if (_clearedFor == 0 || (_clearedFor < CLEARED_WAIT && !dungeon.Deleted))
                return;
            _world.LeaveWorld(_portal);
            var name = portal.PortalDescr.DungeonName ?? portal.ObjectDesc.IdName;
            StaticLogger.Instance.Info($"Nexus: {name} ({dungeon.Id}) was cleared; its portal closed");
            if (!name.StartsWith("The "))
                name = "The " + name;
            SayNear($"{name} has been cleared, so I closed its portal. The next one opens in {Span(_dungeonTimer)}.");
        }

        // a portal to a random dungeon, in place of the last one
        private void OpenDungeon()
        {
            if (_dungeons.Count == 0)
                return;
            if (_portal != null && !_portal.Dead)
                _world.LeaveWorld(_portal);
            var (x, y) = FindSpot(PortalSpot);
            _portal = _world.CreateNewEntity(_dungeons[Random.Shared.Next(_dungeons.Count)], x, y);
            _clearedFor = 0;
            if (_portal == null)
                return;
            var name = (_portal.ObjectDesc as PortalDesc)?.DungeonName ?? _portal.ObjectDesc.IdName;
            StaticLogger.Instance.Info($"Nexus: opened a {name} portal");
            SayNear($"I opened a dungeon portal next to the spawn: {name}! It stays open for {Span(GIFT_INTERVAL)}.");
        }

        // how many gifts are left, and when the next ones come: new gifts come at the next check
        // (every GIFT_INTERVAL) that finds the last ones all shot down
        private string GiftNews()
        {
            _gifts.RemoveAll(g => g.Dead);
            var wait = Span(_giftTimer);
            if (_gifts.Count == 0)
                return $"All the XP gifts are gone! {GIFT_COUNT} new ones come in {wait}. Next dungeon portal in {Span(_dungeonTimer)}.";
            var left = _gifts.Count == 1 ? "1 XP gift is" : $"{_gifts.Count} XP gifts are";
            return $"{left} left around the spawn! Shoot them all in the next {wait} and {GIFT_COUNT} new ones come. Next dungeon portal in {Span(_dungeonTimer)}.";
        }

        private static string Span(float seconds)
        {
            var s = Math.Max(1, (int)MathF.Ceiling(seconds));
            if (s < 60)
                return s == 1 ? "1 second" : $"{s} seconds";
            var m = (s + 30) / 60;
            return m == 1 ? "1 minute" : $"{m} minutes";
        }

        // GIFT_COUNT gifts, each on its own open plaza tile (no object on it) around the rug
        private void SpawnGifts()
        {
            var tiles = new List<(int X, int Y)>();
            for (var y = (int)(Centre.Y - GIFT_MAX_RADIUS); y <= Centre.Y + GIFT_MAX_RADIUS; y++)
                for (var x = (int)(Centre.X - GIFT_MAX_RADIUS); x <= Centre.X + GIFT_MAX_RADIUS; x++)
                {
                    var d = MathF.Sqrt((x + 0.5f - Centre.X) * (x + 0.5f - Centre.X) + (y + 0.5f - Centre.Y) * (y + 0.5f - Centre.Y));
                    if (d < GIFT_MIN_RADIUS || d > GIFT_MAX_RADIUS || !_world.Map.Contains(x, y) || _world.Map[x, y].ObjType != 0)
                        continue;
                    if (((int)BotSpot.X == x && (int)BotSpot.Y == y) || ((int)PortalSpot.X == x && (int)PortalSpot.Y == y))
                        continue;
                    if (_world.IsPassable(x + 0.5, y + 0.5, true))
                        tiles.Add((x, y));
                }
            foreach (var (x, y) in tiles.OrderBy(_ => Random.Shared.Next()).Take(GIFT_COUNT))
            {
                var gift = _world.CreateNewEntity(GIFT, x + 0.5f, y + 0.5f);
                if (gift != null)
                    _gifts.Add(gift);
            }
            if (_gifts.Count > 0)
                SayNear($"{_gifts.Count} XP gifts! Shoot them for experience.");
        }

        // each player who comes near hears about /gear, at most once every GREET_AGAIN seconds
        private void Greet()
        {
            foreach (var id in _greeted.Where(g => g.Value <= _clock).Select(g => g.Key).ToList())
                _greeted.Remove(id);
            foreach (var player in _world.Players.Values)
            {
                if (_greeted.ContainsKey(player.Id) || player.SqDistTo(_bot) > GREET_RANGE * GREET_RANGE)
                    continue;
                _greeted[player.Id] = _clock + GREET_AGAIN;
                var line = player.Level < commands.doomed.GearCommand.MAX_LEVEL
                    ? $"Hi {player.Name}! Type /gear for free starter gear, then /realm to go fight. /godlands takes you to the Godlands."
                    : $"Hi {player.Name}! Type /godlands to go straight to the Godlands.";
                player.Client.SendPacket(Speech(line));
            }
        }

        private void SayNear(string text)
        {
            var packet = Speech(text);
            foreach (var player in _world.Players.Values)
                if (player.SqDistTo(_bot) < TALK_RANGE * TALK_RANGE)
                    player.Client.SendPacket(packet);
        }

        private Text Speech(string text) =>
            new Text() { Name = NAME, ObjectId = _bot.Id, NumStars = -1, BubbleTime = 5, Recipient = "", Txt = text };

        // the spot, or the nearest open tile to it if the map has changed
        private (float X, float Y) FindSpot((float X, float Y) spot)
        {
            for (var r = 0; r <= 3; r++)
                for (var dy = -r; dy <= r; dy++)
                    for (var dx = -r; dx <= r; dx++)
                        if (_world.IsPassable(spot.X + dx, spot.Y + dy, true))
                            return (spot.X + dx, spot.Y + dy);
            return spot;
        }
    }
}
