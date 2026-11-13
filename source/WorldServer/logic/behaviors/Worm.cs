using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    // Segmented monsters (the Fungal Cavern's Crystal Worm Mother and Child): the head lays its body
    // out behind it (SpawnWorm), each segment trails the one ahead of it (WormSegment), and the
    // head can circle the arena it started in (CircleArena). Unlike the Rock Dragon's chain, whose
    // every segment is its own type protecting the one before, the segments here may all be one
    // type: each follows the entity that is its ParentEntity.

    // Lays out the head's body once, the first time its state is entered: `segments` in order from
    // the neck to the tail, `spacing` tiles apart, trailing back along `angle` (degrees).
    internal class SpawnWorm : Behavior
    {
        private readonly string[] _segments;
        private readonly float _spacing;
        private readonly double _angle;

        public SpawnWorm(float spacing, double angle, params string[] segments)
        {
            _segments = segments;
            _spacing = spacing;
            _angle = angle * Math.PI / 180;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            if (state is bool)
                return;
            state = true;
            Entity prev = host;
            for (var i = 0; i < _segments.Length; i++)
            {
                var segment = Entity.Resolve(host.GameServer, _segments[i]);
                if (segment == null)
                    continue;
                var d = _spacing * (i + 1);
                segment.Move(host.X + (float)(Math.Cos(_angle) * d), host.Y + (float)(Math.Sin(_angle) * d));
                if (segment is Enemy e)
                {
                    e.ParentEntity = prev as Enemy;
                    if (host is Enemy h)
                        e.Terrain = h.Terrain;
                }
                segment.SummonedBy(host);
                host.World.EnterWorld(segment);
                prev = segment;
            }
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }

    // A segment keeps `spacing` tiles behind the one ahead of it. When that one dies the body closes
    // up behind the next one forward; when the head is gone the segment goes too.
    internal class WormSegment : Behavior
    {
        private readonly float _spacing;

        public WormSegment(float spacing = 1.2f) => _spacing = spacing;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (host is not Enemy self)
                return;
            var ahead = self.ParentEntity;
            while (ahead != null && (ahead.Dead || ahead.World != host.World))
                ahead = ahead.ParentEntity;
            if (ahead == null)
            {
                self.Death(ref time);
                return;
            }
            self.ParentEntity = ahead;
            var dx = host.X - ahead.X;
            var dy = host.Y - ahead.Y;
            var d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d <= _spacing)
                return;
            if (d < 0.001f)
            {
                dx = 1;
                d = 1;
            }
            host.Move(ahead.X + dx / d * _spacing, ahead.Y + dy / d * _spacing);
        }
    }

    // Circles the spot where the host first used this behaviour (the arena's centre for a head
    // placed there), `radius` tiles out, at `speed`; clockwise on the screen unless told otherwise.
    internal class CircleArena : CycleBehavior
    {
        private static readonly ConditionalWeakTable<Entity, StrongBox<Vector2>> Centres = new ConditionalWeakTable<Entity, StrongBox<Vector2>>();
        private readonly float _speed;
        private readonly float _radius;
        private readonly int _direction;

        public CircleArena(double speed, double radius, bool clockwise = true)
        {
            _speed = (float)speed;
            _radius = (float)radius;
            _direction = clockwise ? 1 : -1;
        }

        public static Vector2 Centre(Entity host) => Centres.GetValue(host, h => new StrongBox<Vector2>(new Vector2(h.X, h.Y))).Value;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            Status = CycleStatus.NotStarted;
            if (host.HasConditionEffect(ConditionEffectIndex.Paralyzed))
                return;
            var c = Centre(host);
            var angle = Math.Atan2(host.Y - c.Y, host.X - c.X);
            var step = host.GetSpeed(_speed) * time.DeltaTime;
            angle += _direction * step / _radius;
            var target = new Vector2(c.X + (float)(Math.Cos(angle) * _radius), c.Y + (float)(Math.Sin(angle) * _radius));
            var vect = target - new Vector2(host.X, host.Y);
            var len = vect.Length();
            if (len > step && len > 0)
                vect *= step / len;
            host.ValidateAndMove(host.X + vect.X, host.Y + vect.Y);
            Status = CycleStatus.InProgress;
        }
    }

    // Brings `child` up in the arena's centre (CircleArena.Centre of the host) each time the state
    // is entered: the Crystal Worm Mother calls her mate and her young into the middle of the room.
    internal class SpawnAtArenaCentre : Behavior
    {
        private readonly string _child;

        public SpawnAtArenaCentre(string child) => _child = child;

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            var c = CircleArena.Centre(host);
            var entity = Entity.Resolve(host.GameServer, _child);
            if (entity == null)
                return;
            entity.Move(c.X, c.Y);
            if (entity is Enemy e && host is Enemy h)
                e.Terrain = h.Terrain;
            host.World.EnterWorld(entity);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state) { }
    }
}
