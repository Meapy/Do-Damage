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
    // moved here.
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

            foreach (var w in _walkers)
            {
                if (w.Entity.World == null)
                    continue;
                if (w.Path.Count > 0)
                {
                    Step(w, dt);
                    continue;
                }
                w.Wait -= dt;
                if (!w.Talked && w.Wait > 2f)
                    Chat(w);
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

        private void SetOff(Walker w)
        {
            var from = ((int)w.Entity.X, (int)w.Entity.Y);
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
            var other = _walkers.FirstOrDefault(o => o != w && o.Path.Count == 0 && !o.Talked && o.Wait > 2f && o.Entity.World != null
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
