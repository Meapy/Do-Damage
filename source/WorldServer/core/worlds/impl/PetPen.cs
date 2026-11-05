using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;

namespace WorldServer.core.worlds.impl
{
    // The pet pen east of the Arena court (tools/build_nexus_islands.py): the Pen Keeper at its gate
    // (on the map's Nexus_Pen_Keeper tile; his work is Player.PetPen.cs), and inside it (its
    // Nexus_Pet_Pen squares) pets of every kind that come and go: one appears now and then, ambles
    // about the pen and is gone again after a while, a few at a time, in no order.
    public sealed class PetPen
    {
        private const string KEEPER = "The Pen Keeper";
        private const int MAX_PETS = 12;
        private const float SPEED = 0.8f;

        private sealed class PenPet
        {
            public Entity Entity;
            public float Left;
            public float Wait;
            public (float X, float Y)? Goal;
        }

        private readonly World _world;
        private readonly List<(float X, float Y, float Z)> _floor = new();
        private readonly List<PenPet> _pets = new();
        private ushort[] _kinds = Array.Empty<ushort>();
        private float _next;
        private bool _started;
        private float _clock;

        public PetPen(World world) => _world = world;

        private void Start()
        {
            _started = true;
            foreach (var (p, z) in _world.RegionTiles(TileRegion.Nexus_Pet_Pen))
                _floor.Add((p.X + 0.5f, p.Y + 0.5f, z));
            if (_world.Marker(TileRegion.Nexus_Pen_Keeper) is { } at)
                _world.CreateNewEntity(KEEPER, at.X, at.Y, _world.Levels != null ? at.Z : null);
            // every pet a generator gives (its object, which draws as the pet)
            var data = _world.GameServer.Resources.GameData;
            _kinds = data.Items.Values
                .SelectMany(i => i.ActivateEffects.Where(e => e.Effect == ActivateEffects.PermaPet && e.PetSize != null && e.ObjectId != null))
                .Select(e => data.IdToObjectType.TryGetValue(e.ObjectId, out var t) ? t : (ushort)0)
                .Where(t => t != 0 && data.ObjectDescs.ContainsKey(t))
                .Distinct().ToArray();
        }

        public void Update(ref TickTime time)
        {
            var dt = time.DeltaTime;
            _clock += dt;
            if (!_started)
            {
                if (_clock < 2f)
                    return;
                Start();
            }
            if (_floor.Count == 0 || _kinds.Length == 0)
                return;

            _next -= dt;
            if (_next <= 0)
            {
                // a full pen soon after a restart, then one every few seconds as others go
                _next = _pets.Count < MAX_PETS / 2 ? 0.5f : 2f + (float)Random.Shared.NextDouble() * 4f;
                if (_pets.Count < MAX_PETS)
                    Arrive();
            }
            foreach (var p in _pets.ToList())
            {
                p.Left -= dt;
                if (p.Left <= 0 || p.Entity.World == null)
                {
                    if (p.Entity.World != null)
                        _world.LeaveWorld(p.Entity);
                    _pets.Remove(p);
                    continue;
                }
                Amble(p, dt);
            }
        }

        private void Arrive()
        {
            var kind = _kinds[Random.Shared.Next(_kinds.Length)];
            var at = _floor[Random.Shared.Next(_floor.Count)];
            // a plain object of the pet's kind: drawn as the pet, without a pet's ways (it follows
            // nobody and fights nothing)
            var e = new StaticObject(_world.GameServer, kind, null, false, false, false);
            e.SwitchTo(null);
            e.Walks = true;
            e.Move(at.X, at.Y);
            e.Z = at.Z;
            _world.EnterWorld(e);
            _pets.Add(new PenPet { Entity = e, Left = 25f + (float)Random.Shared.NextDouble() * 35f, Wait = 1f + (float)Random.Shared.NextDouble() * 3f });
        }

        // a short walk to a square of the pen, a rest, another
        private void Amble(PenPet p, float dt)
        {
            if (p.Goal is not { } goal)
            {
                p.Wait -= dt;
                if (p.Wait > 0)
                    return;
                var (gx, gy, _) = _floor[Random.Shared.Next(_floor.Count)];
                if (MathF.Abs(gx - p.Entity.X) + MathF.Abs(gy - p.Entity.Y) > 6)
                    return;
                p.Goal = (gx, gy);
                return;
            }
            var (x, y) = (p.Entity.X, p.Entity.Y);
            var d = MathF.Sqrt((goal.X - x) * (goal.X - x) + (goal.Y - y) * (goal.Y - y));
            var step = SPEED * dt;
            if (d <= step)
            {
                p.Entity.Move(goal.X, goal.Y);
                p.Goal = null;
                p.Wait = 1.5f + (float)Random.Shared.NextDouble() * 4f;
                return;
            }
            p.Entity.Move(x + (goal.X - x) / d * step, y + (goal.Y - y) / d * step);
        }
    }
}
