using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.logic.behaviors;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// One of an Empowered boss's phases: an attack of the souls' (or the boss's own signature) that
    /// opens once its health falls below <see cref="Below"/>, announced over the boss, and is used
    /// again every <see cref="Every"/> ms (sped up with the boss). Only one phase attack plays at a
    /// time (<see cref="Busy"/> ms, then a breath), the newest phase first, so a late fight stays
    /// readable rather than everything at once.
    /// </summary>
    public sealed class SoulPhase
    {
        public string Name;
        public double Below = 1;
        public string Line;
        public uint Color = BossResurrection.SoulGreen;
        public int First = 1500;
        public int Every = 10000;
        public int Busy = 3000;
        public Action<Arena> Run;

        internal bool Open;
        internal int Wait;
    }

    /// <summary>What a phase attack works with: the boss, its targets, and the telegraphs and shots.</summary>
    public sealed class Arena
    {
        // the shots every Empowered form carries (tools/build_resurrection.py)
        public const int Bolt = 90, FastBolt = 91, Orb = 92, Shard = 93;
        // and a raid boss's own signature shots (SIGNATURE_SHOTS there)
        public const int Sig0 = 100, Sig1 = 101, Sig2 = 102, Sig3 = 103;

        public readonly Enemy Boss;
        public readonly BossInfo Info;
        public readonly Empowered Empowered;
        public readonly Random Rng = Random.Shared;

        public Arena(Enemy boss, BossInfo info, Empowered empowered)
        {
            Boss = boss;
            Info = info;
            Empowered = empowered;
        }

        public World World => Boss.World;
        public Position Pos => new Position(Boss.X, Boss.Y);
        public int Soul => Info.SoulDamage;
        public bool Alive => Boss.World != null && !Boss.Dead;
        public double HealthShare => Boss.MaxHealth > 0 ? Boss.Health / (double)Boss.MaxHealth : 1;

        /// <summary>The players in the fight: near the boss, not hidden, not dead.</summary>
        public List<Player> Targets(float range = 18) =>
            World?.Players.Values.Where(p => !p.IsHidden && p.Health > 0 && p.DistTo(Boss) < range).ToList() ?? new List<Player>();

        public Player Nearest(float range = 18) => Targets(range).OrderBy(p => p.DistTo(Boss)).FirstOrDefault();

        public static Position P(Entity e) => new Position(e.X, e.Y);
        public Position At(double angle, double dist) => At(Pos, angle, dist);
        public static Position At(Position from, double angle, double dist) =>
            new Position(from.X + (float)(Math.Cos(angle) * dist), from.Y + (float)(Math.Sin(angle) * dist));
        public double AngleTo(Position p) => AngleBetween(Pos, p);
        public static double AngleBetween(Position a, Position b) => Math.Atan2(b.Y - a.Y, b.X - a.X);
        public double RandomAngle() => Rng.NextDouble() * Math.PI * 2;
        public static double Deg(double d) => d * Math.PI / 180;
        public bool Walkable(Position p) => World != null && World.IsPassable(p.X, p.Y);

        /// <summary>Runs <paramref name="action"/> in <paramref name="ms"/>, if the boss still fights by then.</summary>
        public void Later(int ms, Action action)
        {
            var world = World;
            if (world == null)
                return;
            if (ms <= 0)
            {
                action();
                return;
            }
            world.StartNewTimer(ms, (w, t) =>
            {
                if (Boss.World == w && !Boss.Dead)
                    action();
            });
        }

        /// <summary>Runs <paramref name="step"/> <paramref name="count"/> times, <paramref name="gap"/> ms apart (the step's number given).</summary>
        public void Repeat(int count, int gap, Action<int> step, int delay = 0)
        {
            for (var i = 0; i < count; i++)
            {
                var k = i;
                Later(delay + i * gap, () => step(k));
            }
        }

        public void Mark(Position at, double radius, int ms, uint color, int style = 0)
        {
            if (World != null)
                Strike.Mark(Boss, at, radius, ms, color, style);
        }

        /// <summary>The most a blast deals before the boss's damage scale and the player's defense:
        /// the big bosses' heaviest blasts (the Void Entity's Singularity, 2.2 times a 234 bolt) took
        /// a full-health player in one hit, enraged.</summary>
        public const int BlastCap = 350;

        /// <summary>What a blast of this factor deals (before the boss's damage scale).</summary>
        public static int BlastDamage(int soul, double factor) => (int)Math.Min(soul * factor, BlastCap);

        /// <summary>A telegraphed blast: the mark fills for <paramref name="ms"/>, then everyone in it takes the soul bolt's damage times <paramref name="factor"/> (<see cref="BlastCap"/> at most).</summary>
        public void Blast(Position at, double radius, int ms, double factor, uint color,
            ConditionEffectIndex effect = 0, int effectMs = 0, int style = 0, Action landed = null)
        {
            Mark(at, radius, ms, color, style);
            var world = World;
            Later(ms, () =>
            {
                Strike.Land(world, Boss, at, radius, BlastDamage(Soul, factor), color, effect, effectMs);
                landed?.Invoke();
            });
        }

        /// <summary>Shots from the boss: <paramref name="count"/> of them <paramref name="inc"/> radians apart, centred on <paramref name="angle"/>.</summary>
        public void Shoot(int index, double angle, int count = 1, double inc = 0) => ShootFrom(Pos, index, angle, count, inc);

        public void ShootFrom(Position from, int index, double angle, int count = 1, double inc = 0)
        {
            if (World != null)
                Strike.FireFrom(Boss, from.X, from.Y, index, angle, count, inc);
        }

        public void Ring(int index, int count, double offset = 0) => RingFrom(Pos, index, count, offset);
        public void RingFrom(Position from, int index, int count, double offset = 0) =>
            ShootFrom(from, index, offset, count, Math.PI * 2 / count);

        public void Throw(Position to, uint color)
        {
            if (World != null)
                Strike.Throw(Boss, to, color);
        }

        public void Lightning(Position to, uint color)
        {
            if (World != null)
                Strike.Bolt(Boss, to, color);
        }

        public void Effect(EffectType type, uint color, Position pos1 = default, Position pos2 = default) =>
            BossResurrection.Effect(Boss, type, color, pos1, pos2);

        public void Wave(double radius, uint color) =>
            Effect(EffectType.AreaBlast, color, new Position((float)radius, 0));

        public void Gather(uint color, double radius = 3) =>
            Effect(EffectType.Concentrate, color, Pos, new Position(Boss.X + (float)radius, Boss.Y));

        public void Say(string line) => BossResurrection.Say(Boss, Empowered.DisplayName(Boss), line, true);
        public void Notify(string text, uint color) => BossResurrection.Notify(Boss, text, color);

        // until when the shield is the phases' own (not the boss's own fight's)
        private long _shieldUntil;

        /// <summary>Untouchable for a moment (unless its own fight has it so already).</summary>
        public void Shield(int ms)
        {
            var now = Environment.TickCount64;
            if (Boss.HasConditionEffect(ConditionEffectIndex.Invulnerable) && now >= _shieldUntil)
                return;
            if (now + ms <= _shieldUntil)
                return;
            Boss.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, ms);
            _shieldUntil = now + ms;
        }

        public void Heal(double share)
        {
            Boss.Health = Math.Min(Boss.MaxHealth, Boss.Health + (int)(Boss.MaxHealth * share));
            Effect(EffectType.Potion, 0xFFFF4FD8);
        }

        /// <summary>A wall of shots: <paramref name="count"/> starts <paramref name="spacing"/> tiles apart across
        /// <paramref name="centre"/>, all flying at <paramref name="angle"/>, with the starts in <paramref name="gaps"/> left out.</summary>
        public void Wall(Position centre, double angle, int count, double spacing, int index, ISet<int> gaps = null)
        {
            var across = angle + Math.PI / 2;
            for (var i = 0; i < count; i++)
            {
                if (gaps != null && gaps.Contains(i))
                    continue;
                var off = (i - (count - 1) / 2.0) * spacing;
                ShootFrom(At(centre, across, off), index, angle);
            }
        }

        /// <summary><paramref name="width"/> neighbouring numbers below <paramref name="count"/>, picked at random, away from the ends.</summary>
        public HashSet<int> Gap(int count, int width)
        {
            var start = Rng.Next(2, Math.Max(3, count - width - 1));
            return new HashSet<int>(Enumerable.Range(start, width));
        }
    }

    /// <summary>The phases every Empowered boss has, its signature ones on top (<see cref="EmpoweredSignatures"/>).</summary>
    public static class EmpoweredPhases
    {
        public static List<SoulPhase> For(BossInfo info)
        {
            var list = new List<SoulPhase>
            {
                SoulHarvest(),
                SoulStorm(),
                DeathSpiral(),
                LastBreath(),
            };
            list.AddRange(EmpoweredSignatures.For(info.Boss));
            return list.OrderByDescending(p => p.Below).ToList();
        }

        // 80%: the souls mark every player, and claim what stays put
        private static SoulPhase SoulHarvest() => new SoulPhase
        {
            Name = "Soul Harvest",
            Below = 0.8,
            Line = "Your souls will join theirs!",
            Every = 9000,
            Busy = 2400,
            Run = a =>
            {
                a.Gather(BossResurrection.SoulGreen, 4);
                foreach (var p in a.Targets())
                {
                    var at = Arena.P(p);
                    a.Throw(at, BossResurrection.SoulGreen);
                    a.Blast(at, 1.9, 1500, 1.4, BossResurrection.SoulGreen, ConditionEffectIndex.Slowed, 1500,
                        landed: () => a.RingFrom(at, Arena.FastBolt, 6, a.RandomAngle()));
                }
            },
        };

        // 60%: four arms of fast, slowing bolts sweeping round
        private static SoulPhase SoulStorm() => new SoulPhase
        {
            Name = "Soul Storm",
            Below = 0.6,
            Line = "The storm of the dead is upon you!",
            Color = BossResurrection.SoulViolet,
            Every = 10000,
            Busy = 2600,
            Run = a =>
            {
                var start = a.RandomAngle();
                a.Gather(BossResurrection.SoulViolet);
                a.Repeat(12, 200, i => a.Shoot(Arena.FastBolt, start + Arena.Deg(14 * i), 4, Math.PI / 2));
            },
        };

        // 30% (with the rage): two pairs of soul shards spiralling against each other
        private static SoulPhase DeathSpiral() => new SoulPhase
        {
            Name = "Death Spiral",
            Below = 0.3,
            Color = 0xFFFF4FD8,
            First = 2000,
            Every = 11000,
            Busy = 3400,
            Run = a =>
            {
                var start = a.RandomAngle();
                a.Wave(4, 0xFFFF4FD8);
                a.Repeat(16, 190, i =>
                {
                    a.Shoot(Arena.Shard, start + Arena.Deg(12 * i), 2, Math.PI);
                    a.Shoot(Arena.Shard, -start - Arena.Deg(12 * i) + Math.PI / 2, 2, Math.PI);
                    if (i % 5 == 0)
                        a.Ring(Arena.Bolt, 12, start + i);
                });
            },
        };

        /// <summary>The radius of Last Breath's marks, and how many there are in each of its rings.</summary>
        public const double LastBreathMark = 1.2;
        public static readonly (double Radius, int Count)[] LastBreathRings = { (3.5, 6), (6.5, 10), (9.5, 14) };

        // 10%: one last burst of everything the souls have, then a requiem every few seconds
        private static SoulPhase LastBreath()
        {
            var first = true;
            return new SoulPhase
            {
                Name = "Last Breath",
                Below = 0.1,
                Line = "IF I FALL, YOU ALL FALL WITH ME!",
                Color = 0xFFFF4FD8,
                First = 300,
                Every = 6500,
                Busy = 2600,
                Run = a =>
                {
                    if (first)
                    {
                        first = false;
                        a.Shield(3200);
                        a.Gather(0xFFFF4FD8, 6);
                        a.Effect(EffectType.Flashing, 0xFFFF4FD8, new Position(0.25f, 10));
                        // a tile or more between the marks of each ring: before, the rings were
                        // solid and the only way out was to outrun all three
                        for (var k = 0; k < LastBreathRings.Length; k++)
                        {
                            var (r, n) = LastBreathRings[k];
                            var off = a.RandomAngle();
                            for (var j = 0; j < n; j++)
                                a.Blast(a.At(off + j * Math.PI * 2 / n, r), LastBreathMark, 1600 + k * 500, 1.6, 0xFFFF4FD8);
                        }
                        a.Repeat(3, 700, i => a.Ring(Arena.Orb, 20, i * Math.PI / 20), 1000);
                        return;
                    }
                    a.Wave(5, 0xFFFF4FD8);
                    a.Ring(Arena.Orb, 16, a.RandomAngle());
                    a.Later(400, () => a.Ring(Arena.Shard, 24, a.RandomAngle()));
                    a.Later(900, () => a.Ring(Arena.Shard, 24, a.RandomAngle()));
                },
            };
        }
    }
}
