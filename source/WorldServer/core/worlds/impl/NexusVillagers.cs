using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.worlds.impl
{
    // The Nexus garden's villagers and bees (tools/build_nexus_garden.py), for life about the place.
    // Villagers walk the paths between the map's waypoints (`Nexus Waypoint` tiles: the houses'
    // doors, the squares, the bridges' feet), wait there a while, and pass the time of day with
    // another villager waiting near them, in balloons only (`*Bubble*`), never in the chat. Bees
    // hover over the flowers and wander from one bed to the next. Both are characters but not
    // enemies, and invincible, so pets and shots leave them be; they have no behaviours, and are
    // moved here. So are some who keep to their own ground: the Name Changer and the Character
    // Changer stroll about their corners of the spawn's island (still the NPCs whose windows the
    // client opens), the twins Hana and Sora run about the forest, Sora at Hana's heels, and
    // Amatsumara the smith god, at his anvil by the forge, grunts and mutters as he works.
    public sealed class NexusVillagers
    {
        private static readonly string[] Villagers =
        {
            "NG Villager Monk", "NG Villager Kimono", "NG Villager Farmer", "NG Villager Miko",
            "NG Villager Sage", "NG Villager Merchant", "NG Villager Child", "NG Villager Kimono",
            "NG Villager Farmer", "NG Villager Merchant", "NG Villager Child", "NG Villager Monk",
        };
        private const string BEE = "NG Bee";
        private const int BEES = 14;
        private static readonly HashSet<string> Flowers = new HashSet<string> { "NG Azalea", "NG Pink Flowers", "NG Blue Flowers", "NG Lotus" };

        // those who keep to an area: who, its middle and radius, their speed, and who they follow
        private static readonly (string Id, float X, float Y, float R, float Speed, int Leader)[] Roamers =
        {
            ("NG Name Changer", 64.5f, 57.5f, 3.2f, 1.0f, -1),
            ("NG Character Changer", 72.5f, 57.5f, 3.2f, 1.0f, -1),
            ("NG Twin Hana", 66.5f, 84.5f, 11f, 2.3f, -1),
            ("NG Twin Sora", 66.5f, 84.5f, 11f, 2.3f, 2),
        };
        private const string SMITH = "NG Smith God";
        // the NPCs players use, who stroll about the market's courtyard (made by QuestGivers and
        // MarketBroker): a few steps at a time, long waits, so they're easy to catch
        private static readonly string[] MarketWalkers = { "NG Tinkerer", "NG Chronicler", "NG Market Broker" };
        private static readonly (float X, float Y, float R) MarketArea = (110.5f, 61.5f, 5.5f);

        private static readonly (string, string)[] TwinTalk =
        {
            ("Sora! Catch me if you can!", "Hana, wait for me!"),
            ("Look, a bee! A fat one!", "Don't poke it again..."),
            ("Hehehe!", "Hehehe!"),
            ("The ducks are back on the pond!", "Let's count them! One... two... lots!"),
            ("I'm going to be a hero one day.", "Me too! A bigger one."),
            ("Petals! It's snowing petals!", "Catch one and make a wish!"),
        };

        private static readonly string[] SmithLines =
        {
            "Hnnngh!", "Hah!", "*grunts*", "Hrrah!",
            "The fire hungers. Feed it.",
            "Steel remembers every blow.",
            "Too soft. Again!",
            "Oryx's armies will break on blades I made.",
            "Bring the Divine Forge your spare gear, mortal. It knows what to do with it.",
            "Hmph. Not bad... for mortal hands.",
            "A god's work is never done.",
        };

        private const float WALK_SPEED = 1.8f;      // tiles a second
        private const float CHILD_SPEED = 2.6f;
        private const float TALK_RANGE = 3.5f;
        private const float HEAR_RANGE = 14f;       // players this near see the balloons

        // what villagers say to each other: a line, and the answer
        private static readonly (string, string)[] Talk =
        {
            ("The blossom came early this year.", "Every year it's early, and every year I'm surprised."),
            ("Another hero back from the realm, covered in soot.", "They all come back hungry, too."),
            ("Have you seen the koi? The gold one's grown.", "That one eats better than I do."),
            ("They say Oryx is stirring again.", "They always say that. Pass the tea."),
            ("The forge was roaring all night.", "Someone made something they'll regret, I expect."),
            ("I sold three potions this morning!", "At the Marketplace? Prices are up, then."),
            ("The pagoda's bell rang on its own.", "That's the wind. Probably."),
            ("A stranger stepped out of the vortex today.", "Into Realm 2? Brave or foolish."),
            ("My knees know rain is coming.", "Your knees said that yesterday."),
            ("Have the champions changed this week?", "Go and look at the statues, west of the pond."),
            ("Mind the bees by the azaleas.", "They're friendly. Mostly."),
            ("The Tinkerer has new work posted.", "The Tinkerer always has work. Never pays in tea."),
        };

        private sealed class Walker
        {
            public Entity Entity;
            public List<(float X, float Y)> Path = new List<(float X, float Y)>();
            public float Wait;
            public float Speed;
            public bool Talked;
            // a roamer's own ground, and whom it follows (its leader's trail, a step behind)
            public (float X, float Y, float R)? Area;
            public Walker Leader;
            public readonly Queue<(float At, float X, float Y)> Trail = new Queue<(float At, float X, float Y)>();
            public bool Chats = true;
        }

        private sealed class Bee
        {
            public Entity Entity;
            public (float X, float Y) Home;
            public float Phase, Rate, Stay;
        }

        private readonly World _world;
        private readonly List<Walker> _walkers = new List<Walker>();
        private readonly List<Bee> _bees = new List<Bee>();
        private List<(int X, int Y)> _waypoints = new List<(int X, int Y)>();
        private List<(float X, float Y)> _flowers = new List<(float X, float Y)>();
        private float _clock;
        private bool _started;
        private Entity _smith;
        private float _smithTalk = 8f;

        public NexusVillagers(World world) => _world = world;

        private void Start()
        {
            _started = true;
            _waypoints = _world.Map.Regions.Where(r => r.Value == TileRegion.Nexus_Waypoint).Select(r => (r.Key.X, r.Key.Y)).ToList();
            if (_waypoints.Count >= 2)
            {
                var order = _waypoints.OrderBy(_ => Random.Shared.Next()).ToList();
                for (var i = 0; i < Villagers.Length; i++)
                {
                    var (x, y) = order[i % order.Count];
                    var e = _world.CreateNewEntity(Villagers[i], x + 0.5f, y + 0.5f);
                    if (e == null)
                        continue;
                    _walkers.Add(new Walker
                    {
                        Entity = e,
                        Wait = 2f + (float)Random.Shared.NextDouble() * 10f,
                        Speed = Villagers[i].EndsWith("Child") ? CHILD_SPEED : WALK_SPEED * (0.85f + (float)Random.Shared.NextDouble() * 0.3f),
                    });
                }
            }

            foreach (var (id, x, y, r, speed, leader) in Roamers)
            {
                var (sx, sy) = Near(x, y, r);
                var e = _world.CreateNewEntity(id, sx, sy);
                if (e == null)
                    continue;
                _walkers.Add(new Walker
                {
                    Entity = e,
                    Wait = 1f + (float)Random.Shared.NextDouble() * 4f,
                    Speed = speed,
                    Area = (x, y, r),
                    Leader = leader >= 0 ? _walkers.FirstOrDefault(w => w.Entity.ObjectDesc.IdName == Roamers[leader].Id) : null,
                    Chats = false,
                });
            }
            _smith = _world.Enemies.Values.FirstOrDefault(e => e.ObjectDesc.IdName == SMITH);
            var walkers = _world.StaticObjects.Values.Cast<Entity>().Concat(_world.SellableObjects.Values)
                .Where(e => MarketWalkers.Contains(e.ObjectDesc.IdName)).ToList();
            foreach (var e in walkers)
            {
                e.Walks = true;
                _walkers.Add(new Walker { Entity = e, Wait = 3f + (float)Random.Shared.NextDouble() * 6f, Speed = 0.9f, Area = MarketArea, Chats = false });
            }

            // the bees: over flower beds, found among the map's objects once they're in
            _flowers = _world.StaticObjects.Values.Where(o => Flowers.Contains(o.ObjectDesc.IdName)).Select(o => (o.X, o.Y)).ToList();
            if (_flowers.Count == 0)
                return;
            for (var i = 0; i < BEES; i++)
            {
                var home = _flowers[Random.Shared.Next(_flowers.Count)];
                var e = _world.CreateNewEntity(BEE, home.X, home.Y);
                if (e != null)
                    _bees.Add(new Bee { Entity = e, Home = home, Phase = (float)(Random.Shared.NextDouble() * Math.PI * 2), Rate = 0.8f + (float)Random.Shared.NextDouble() * 0.6f, Stay = 8f + (float)Random.Shared.NextDouble() * 20f });
            }
        }

        public void Update(ref TickTime time)
        {
            var dt = time.DeltaTime;
            _clock += dt;
            // the map's objects come in over the first ticks
            if (!_started)
            {
                if (_clock < 2f)
                    return;
                Start();
            }

            SmithTalk(dt);
            foreach (var w in _walkers)
            {
                if (w.Entity.World == null)
                    continue;
                w.Trail.Enqueue((_clock, w.Entity.X, w.Entity.Y));
                while (w.Trail.Count > 0 && _clock - w.Trail.Peek().At > 1.5f)
                    w.Trail.Dequeue();
                if (w.Leader != null)
                {
                    Follow(w, dt);
                    continue;
                }
                if (w.Path.Count > 0)
                {
                    Step(w, dt);
                    continue;
                }
                w.Wait -= dt;
                if (!w.Talked && w.Wait > 2f)
                {
                    if (w.Chats)
                        Chat(w);
                    else
                        TwinChat(w);
                }
                if (w.Wait <= 0)
                    SetOff(w);
            }

            foreach (var b in _bees)
            {
                if (b.Entity.World == null)
                    continue;
                b.Stay -= dt;
                if (b.Stay <= 0)
                {
                    // off to another flower bed near this one
                    var near = _flowers.Where(f => Dist(f, b.Home) < 12f).ToList();
                    if (near.Count > 0)
                        b.Home = near[Random.Shared.Next(near.Count)];
                    b.Stay = 10f + (float)Random.Shared.NextDouble() * 20f;
                }
                var t = _clock * b.Rate + b.Phase;
                var goal = (X: b.Home.X + MathF.Cos(t) * 1.3f + MathF.Sin(t * 2.3f) * 0.4f, Y: b.Home.Y + MathF.Sin(t * 1.4f) * 0.9f);
                var (x, y) = (b.Entity.X, b.Entity.Y);
                var d = Dist((x, y), goal);
                var step = Math.Min(d, 3.5f * dt);
                if (d > 0.01f)
                    b.Entity.Move(x + (goal.X - x) / d * step, y + (goal.Y - y) / d * step);
            }
        }

        private void Step(Walker w, float dt)
        {
            var left = w.Speed * dt;
            var (x, y) = (w.Entity.X, w.Entity.Y);
            while (left > 0 && w.Path.Count > 0)
            {
                var next = w.Path[0];
                var d = Dist((x, y), next);
                if (d <= left)
                {
                    (x, y) = next;
                    left -= d;
                    w.Path.RemoveAt(0);
                }
                else
                {
                    x += (next.X - x) / d * left;
                    y += (next.Y - y) / d * left;
                    left = 0;
                }
            }
            w.Entity.Move(x, y);
            if (w.Path.Count == 0)
            {
                w.Wait = 6f + (float)Random.Shared.NextDouble() * 14f;
                w.Talked = false;
            }
        }

        // a step behind the leader, along where it walked
        private void Follow(Walker w, float dt)
        {
            var trail = w.Leader.Trail;
            if (trail.Count == 0)
                return;
            var goal = trail.FirstOrDefault(p => _clock - p.At <= 0.7f);
            if (goal.At == 0)
                goal = trail.Last();
            var (x, y) = (w.Entity.X, w.Entity.Y);
            var d = Dist((x, y), (goal.X, goal.Y));
            if (d < 0.35f)
                return;
            var step = Math.Min(d, w.Speed * 1.15f * dt);
            w.Entity.Move(x + (goal.X - x) / d * step, y + (goal.Y - y) / d * step);
        }

        // a free walkable tile near (x, y), within r
        private (float X, float Y) Near(float x, float y, float r)
        {
            for (var tries = 0; tries < 40; tries++)
            {
                var a = Random.Shared.NextDouble() * Math.PI * 2;
                var d = Random.Shared.NextDouble() * r;
                var (tx, ty) = ((int)(x + Math.Cos(a) * d), (int)(y + Math.Sin(a) * d));
                if (Walkable(tx, ty))
                    return (tx + 0.5f, ty + 0.5f);
            }
            return (x, y);
        }

        private void SetOff(Walker w)
        {
            var from = ((int)w.Entity.X, (int)w.Entity.Y);
            if (w.Area.HasValue)
            {
                var (ax, ay, ar) = w.Area.Value;
                // a short stroll somewhere else on its own ground, then a long wait, so players
                // can catch the changers
                var (tx, ty) = Near(ax, ay, ar);
                var stroll = (int)tx == from.Item1 && (int)ty == from.Item2 ? null : FindPath(from, ((int)tx, (int)ty));
                w.Path = stroll ?? new List<(float X, float Y)>();
                w.Wait = w.Chats ? 6f : 5f + (float)Random.Shared.NextDouble() * (w.Speed < 1.5f ? 10f : 4f);
                w.Talked = false;
                return;
            }
            // somewhere not too far, so they're seen about the village rather than always crossing it
            var choices = _waypoints.Where(p => p != from && Dist((p.X, p.Y), from) < 45f).ToList();
            if (choices.Count == 0)
                choices = _waypoints.Where(p => p != from).ToList();
            if (choices.Count == 0)
            {
                w.Wait = 10f;
                return;
            }
            var to = choices[Random.Shared.Next(choices.Count)];
            var path = FindPath(from, to);
            if (path == null)
            {
                w.Wait = 5f;
                return;
            }
            w.Path = path;
        }

        private void Chat(Walker w)
        {
            var other = _walkers.FirstOrDefault(o => o != w && o.Chats && o.Path.Count == 0 && !o.Talked && o.Wait > 2f && o.Entity.World != null
                                                     && Dist((o.Entity.X, o.Entity.Y), (w.Entity.X, w.Entity.Y)) <= TALK_RANGE);
            if (other == null || Random.Shared.NextDouble() > 0.5)
            {
                w.Talked = true;     // not this stop
                return;
            }
            w.Talked = other.Talked = true;
            var (line, answer) = Talk[Random.Shared.Next(Talk.Length)];
            Say(w.Entity, line);
            var answerer = other.Entity;
            _world.StartNewTimer(2500, (world, t) => Say(answerer, answer));
            // they wait for the end of the conversation
            w.Wait = Math.Max(w.Wait, 5f);
            other.Wait = Math.Max(other.Wait, 5f);
        }

        // the twins' banter, when they've stopped running about
        private void TwinChat(Walker w)
        {
            w.Talked = true;
            if (!w.Entity.ObjectDesc.IdName.EndsWith("Hana") || Random.Shared.NextDouble() > 0.4)
                return;
            var sora = _walkers.FirstOrDefault(o => o.Leader == w);
            var (line, answer) = TwinTalk[Random.Shared.Next(TwinTalk.Length)];
            Say(w.Entity, line);
            if (sora?.Entity.World != null)
            {
                var e = sora.Entity;
                _world.StartNewTimer(1800, (world, t) => Say(e, answer));
            }
        }

        // Amatsumara at his anvil: a grunt or a word now and then while anyone is near
        private void SmithTalk(float dt)
        {
            if (_smith?.World == null)
                return;
            _smithTalk -= dt;
            if (_smithTalk > 0)
                return;
            _smithTalk = 14f + (float)Random.Shared.NextDouble() * 22f;
            if (_world.Players.Values.Any(p => p.SqDistTo(_smith) <= 12f * 12f))
                Say(_smith, SmithLines[Random.Shared.Next(SmithLines.Length)]);
        }

        private void Say(Entity speaker, string text)
        {
            if (speaker.World == null)
                return;
            foreach (var p in _world.Players.Values)
                if (p.SqDistTo(speaker) <= HEAR_RANGE * HEAR_RANGE)
                    p.Client.SendPacket(new Text { Name = "#" + speaker.ObjectDesc.DisplayName, ObjectId = speaker.Id, NumStars = -1, BubbleTime = 4, Recipient = "*Bubble*", Txt = text });
        }

        // Breadth-first over the walkable tiles, not through water nor anything standing; the path is
        // then cut to the turns that matter (the tile centres between straight runs).
        private List<(float X, float Y)> FindPath((int X, int Y) from, (int X, int Y) to)
        {
            var map = _world.Map;
            var prev = new Dictionary<(int, int), (int, int)> { [from] = from };
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue(from);
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (queue.Count > 0 && prev.Count < 6000)
            {
                var c = queue.Dequeue();
                if (c == to)
                    break;
                foreach (var (dx, dy) in dirs)
                {
                    var n = (c.X + dx, c.Y + dy);
                    if (prev.ContainsKey(n) || !Walkable(n.Item1, n.Item2))
                        continue;
                    prev[n] = c;
                    queue.Enqueue(n);
                }
            }
            if (!prev.ContainsKey(to))
                return null;
            var tiles = new List<(int X, int Y)>();
            for (var c = to; c != from; c = prev[c])
                tiles.Add(c);
            tiles.Reverse();
            var path = new List<(float X, float Y)>();
            for (var i = 0; i < tiles.Count; i++)
            {
                var last = i == tiles.Count - 1;
                if (!last)
                {
                    var a = i == 0 ? from : tiles[i - 1];
                    var b = tiles[i + 1];
                    if (b.X - tiles[i].X == tiles[i].X - a.X && b.Y - tiles[i].Y == tiles[i].Y - a.Y)
                        continue;   // still going straight
                }
                path.Add((tiles[i].X + 0.5f, tiles[i].Y + 0.5f));
            }
            return path;
        }

        private bool Walkable(int x, int y)
        {
            if (!_world.IsPassable(x, y, true))
                return false;
            var desc = _world.GameServer.Resources.GameData.Tiles[_world.Map[x, y].TileId];
            return !desc.Sink && desc.Speed >= 1f;
        }

        private static float Dist((float X, float Y) a, (float X, float Y) b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
