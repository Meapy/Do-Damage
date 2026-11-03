using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds.impl
{
    /// <summary>
    /// The bar on top of the islands' Arena tower (tools/build_nexus_islands.py): a barman behind
    /// the counter (on the map's Nexus_Barman tile) who greets whoever comes in and, every
    /// THROW_EVERY seconds while anyone is in his bar (its Nexus_Bar tiles), throws a drink at them:
    /// a bottle arcs over to the middle of the people there and splashes SPLASH tiles round, and
    /// everyone the splash catches gets the same drink, one picked a throw: Speedy, Hallucinating
    /// or Stasis for EFFECT_MS, with "PARTY!" over their heads. Fun only: no damage, no stats, no
    /// items; a player immune to Stasis just gets the "PARTY!". Nobody in the bar, he waits. It all
    /// happens on his floor (DistTo between floors is out of reach). A map without the markers has
    /// no bar.
    /// </summary>
    public sealed class NexusBar
    {
        private const string BARMAN = "NI Barman";
        private const string NAME = "#Barman Kenji";
        // the bottle he throws (its art arcs over in the Rust client; others see sparks)
        private const string BOTTLE = "NI Thrown Sake";
        private const float THROW_EVERY = 20f;
        // with someone just come into an empty bar, the first throw comes this much later
        private const float FIRST_THROW = 4f;
        private const int FLIGHT_MS = 1100;
        private const float SPLASH = 2.6f;
        private const int EFFECT_MS = 15000;
        private const float GREET_RANGE = 6f;
        private const float GREET_AGAIN = 300f;
        private const float HEAR_RANGE = 14f;
        private const float SAME_FLOOR = 1.5f;      // as Entity.LevelReach
        private const uint SAKE = 0xFFE8D7A8;
        private static readonly ARGB PARTY = new ARGB(0xFFFF6EC7);

        private static readonly (ConditionEffectIndex Effect, string Line)[] Drinks =
        {
            (ConditionEffectIndex.Speedy, "That one's got a kick! Off you run!"),
            (ConditionEffectIndex.Hallucinating, "Ah. That was the strong stuff. Sorry."),
            (ConditionEffectIndex.Stasis, "Whoops, the frozen sake. Hold still, it passes."),
        };

        private static readonly string[] Lines =
        {
            "Drinks are on me!",
            "PARTY!",
            "Kanpai!",
            "Best view in the Nexus, and the best sake in the sky.",
            "Champions drink free. Everyone else drinks free too.",
            "Mind the railing after the third round.",
            "I aim for the middle. Usually.",
            "Another round coming up!",
        };

        private readonly World _world;
        private readonly HashSet<(int, int)> _bar;
        private readonly (float X, float Y, float Z) _spot;
        private Entity _barman;
        private ushort _bottle;
        private float _throw = THROW_EVERY, _talk = 12f, _greetCheck, _clock;
        private readonly Dictionary<int, float> _greeted = new Dictionary<int, float>();

        private NexusBar(World world, (float X, float Y, float Z) spot, HashSet<(int, int)> bar)
        {
            _world = world;
            _spot = spot;
            _bar = bar;
        }

        /// <summary>The map's bar, or null if it has none.</summary>
        public static NexusBar Find(World world)
        {
            if (world.Marker(TileRegion.Nexus_Barman) is not { } spot)
                return null;
            var bar = world.RegionTiles(TileRegion.Nexus_Bar).Select(t => (t.Point.X, t.Point.Y)).ToHashSet();
            var nexusBar = new NexusBar(world, spot, bar);
            nexusBar.Init();
            return nexusBar;
        }

        private void Init()
        {
            _barman = _world.CreateNewEntity(BARMAN, _spot.X, _spot.Y, _world.Levels == null ? (float?)null : _spot.Z);
            _bottle = _world.GameServer.Resources.GameData.IdToObjectType.TryGetValue(BOTTLE, out var t) ? t : (ushort)0;
        }

        // in the bar: on one of its squares, on its floor
        private bool InBar(Player p) =>
            !p.Dead && _bar.Contains(((int)p.X, (int)p.Y)) && MathF.Abs(p.Z - _spot.Z) <= SAME_FLOOR;

        public void Update(ref TickTime time)
        {
            if (_barman?.World == null)
                return;
            var dt = time.DeltaTime;
            _clock += dt;

            _greetCheck -= dt;
            if (_greetCheck <= 0)
            {
                _greetCheck = 0.5f;
                Greet();
            }

            var guests = _world.Players.Values.Where(InBar).ToList();
            _throw -= dt;
            if (guests.Count == 0)
            {
                // an empty bar: he waits, and throws soon after someone comes in
                _throw = Math.Max(_throw, FIRST_THROW);
                return;
            }
            if (_throw <= 0)
            {
                _throw = THROW_EVERY;
                Throw(guests);
            }

            _talk -= dt;
            if (_talk <= 0)
            {
                _talk = 25f + (float)Random.Shared.NextDouble() * 20f;
                Say(Lines[Random.Shared.Next(Lines.Length)]);
            }
        }

        // a bottle over to the middle of the guests, splashing everyone there when it lands
        private void Throw(List<Player> guests)
        {
            var at = new Position(guests.Average(p => p.X), guests.Average(p => p.Y));
            var from = new Position(_barman.X, _barman.Y);
            var drink = Drinks[Random.Shared.Next(Drinks.Length)];
            Send(new ShowEffect
            {
                EffectType = EffectType.BeachBall,
                TargetObjectId = _barman.Id,
                Pos1 = at,
                Pos2 = from,
                Color = new ARGB(SAKE),
                Duration = FLIGHT_MS,
                ObjectType = _bottle,
            });
            _world.StartNewTimer(FLIGHT_MS, (world, t) => Splash(at, drink));
        }

        private void Splash(Position at, (ConditionEffectIndex Effect, string Line) drink)
        {
            if (_barman?.World == null)
                return;
            Send(new ShowEffect
            {
                EffectType = EffectType.Diffuse,
                TargetObjectId = _barman.Id,
                Pos1 = at,
                Pos2 = new Position(at.X + SPLASH, at.Y),
                Color = new ARGB(SAKE),
            });
            var caught = _world.Players.Values
                .Where(p => !p.Dead && MathF.Abs(p.Z - _spot.Z) <= SAME_FLOOR && p.DistTo(at.X, at.Y) <= SPLASH)
                .ToList();
            foreach (var p in caught)
            {
                p.ApplyConditionEffect(drink.Effect, EFFECT_MS);
                var party = new Notification { ObjectId = p.Id, PlayerId = p.Id, Color = PARTY, Message = "PARTY!" };
                foreach (var watcher in _world.Players.Values)
                    if (watcher.SqDistTo(p) <= HEAR_RANGE * HEAR_RANGE)
                        watcher.Client.SendPacket(party);
            }
            if (caught.Count > 0)
                Say(drink.Line);
            utils.StaticLogger.Instance.Info($"{_world.IdName}: the barman's drink ({drink.Effect}) caught {caught.Count} in the bar");
        }

        // whoever comes near the counter: a welcome, at most every GREET_AGAIN seconds each
        private void Greet()
        {
            foreach (var id in _greeted.Where(g => g.Value <= _clock).Select(g => g.Key).ToList())
                _greeted.Remove(id);
            foreach (var p in _world.Players.Values)
            {
                if (_greeted.ContainsKey(p.Id) || p.SqDistTo(_barman) > GREET_RANGE * GREET_RANGE)
                    continue;
                _greeted[p.Id] = _clock + GREET_AGAIN;
                p.Client.SendPacket(Speech($"Welcome to the top, {p.Name}! Pull up a stool. Drinks come to you, every twenty seconds or so."));
            }
        }

        private void Say(string text)
        {
            var speech = Speech(text);
            foreach (var p in _world.Players.Values)
                if (p.SqDistTo(_barman) <= HEAR_RANGE * HEAR_RANGE)
                    p.Client.SendPacket(speech);
        }

        private void Send(OutgoingMessage msg)
        {
            foreach (var p in _world.Players.Values)
                if (p.SqDistTo(_barman) <= 20f * 20f)
                    p.Client.SendPacket(msg);
        }

        private Text Speech(string text) =>
            new Text { Name = NAME, ObjectId = _barman.Id, NumStars = -1, BubbleTime = 5, Recipient = "*Bubble*", Txt = text };
    }
}
