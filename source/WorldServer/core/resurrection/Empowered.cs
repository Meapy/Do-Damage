using System;
using System.Linq;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.logic.doomed;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.resurrection
{
    /// <summary>
    /// A boss raised by a Necromantic Relic. It runs its Elder form's behaviour a quarter faster
    /// (Entity.BehaviourTime), with more health (<see cref="EmpoweredHealth"/>: 2.5 times for the
    /// small bosses, tapering to 1.5 times at 200k and 1.25 times at 800k), a little more defense
    /// (less for a boss that's already heavily armoured) and shots 30% harder, glowing violet. On
    /// top, the souls
    /// that brought it back fight for it:
    /// - Soul Nova: two rings of soul bolts, every 7 seconds (5 enraged);
    /// - Wailing Souls: two (three enraged) ghosts that chase the players, every 11 seconds (8 enraged),
    ///   six at most;
    /// - Soul Storm, below 60% health: four arms of fast, slowing bolts sweeping round, every 10 seconds.
    /// Below 30% it is enraged: faster still, harder shots, a hotter glow.
    /// Its loot is its Elder form's, with every player's boost raised by 1 (twice the chances) and its
    /// own drops rolled again (BossResurrection.Initialize).
    /// </summary>
    public sealed class Empowered
    {
        // defense: up to 10 more, less the more the boss already has (none at 100)
        private const int DefenseBonusMax = 10;
        private const int RiseMs = 2500;
        private const int MaxSouls = 6;
        private const int GlowCalm = 0x9B5CFF;
        private const int GlowEnraged = 0xFF4FD8;

        private readonly BossInfo _info;
        private EmpoweredHealth _health;
        private int _setDefense = -1;
        private bool _enraged;
        private int _rising = RiseMs;
        private int _novaIn = 4000;
        private int _secondRingIn = -1;
        private float _ringAngle;
        private int _soulsIn = 6500;
        private int _stormIn = 3000;
        private int _storm;
        private int _stormShotIn;
        private float _stormAngle;
        private float _haste = 1.25f;

        public Empowered(BossInfo info) => _info = info;

        public float DamageMultiplier => _enraged ? 1.45f : 1.3f;
        public double LootBoost => 1.0;
        public bool Paused => _rising > 0;

        public TickTime Hasten(TickTime time)
        {
            time.ElapsedMsDelta = (int)Math.Round(time.ElapsedMsDelta * _haste);
            return time;
        }

        private string DisplayName(Enemy boss) => boss.ObjectDesc.DisplayId ?? boss.ObjectDesc.IdName;

        // just entered the world, where it rises for a moment, untouchable, before it fights
        public void Risen(Enemy boss, string raiser)
        {
            // never a rare, epic or legendary roll on top of this
            boss.IsLegendary = boss.IsEpic = boss.IsRare = false;
            boss.Size = boss.ObjectDesc.Size;
            boss.MaxHealth = boss.Health = boss.ObjectDesc.MaxHP;
            Apply(boss);
            boss.ApplyConditionEffect(ConditionEffectIndex.Invulnerable, RiseMs);
            BossResurrection.Effect(boss, EffectType.Flashing, BossResurrection.SoulGreen, new Position(0.2f, 12));
            BossResurrection.Notify(boss, "EMPOWERED", 0xFFFF4FD8);
            BossResurrection.Say(boss, DisplayName(boss), Pick(RiseLines), true);
            var msg = $"{DisplayName(boss)} has risen! (raised by {raiser})";
            boss.World.ForeachPlayer(p => p.SendInfo(msg));
        }

        private static readonly string[] RiseLines =
        {
            "I... LIVE... AGAIN! And this time, none of you leave!",
            "You dragged me back from the dark. You will regret it!",
            "The souls have made me STRONGER! Kneel!",
            "Death could not hold me. What hope do YOU have?",
        };
        private static readonly string[] RageLines =
        {
            "NO! I will not fall a second time!",
            "Souls of the fallen, GIVE ME EVERYTHING!",
            "You cannot kill what is already dead!",
        };

        private static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];

        /// <summary>The defense an Empowered boss adds to its Elder form's.</summary>
        public static int DefenseBonusFor(int defense) =>
            Math.Max(0, (int)Math.Round(DefenseBonusMax * (1 - defense / 100.0)));

        // its health, defense and glow, put back whenever its behaviour sets them afresh
        private void Apply(Enemy boss)
        {
            if (boss.MaxHealth > 0)
            {
                _health ??= new EmpoweredHealth(boss.ObjectDesc.MaxHP > 0 ? boss.ObjectDesc.MaxHP : boss.MaxHealth);
                var (max, health) = _health.Update(boss.MaxHealth, boss.Health);
                if (max != boss.MaxHealth)
                    boss.MaxHealth = max;
                if (health != boss.Health)
                    boss.Health = health;
            }
            if (boss.Defense != _setDefense)
            {
                boss.Defense += DefenseBonusFor(boss.Defense);
                _setDefense = boss.Defense;
            }
            var glow = _enraged ? GlowEnraged : GlowCalm;
            if (boss.GlowEnemy != glow)
                boss.GlowEnemy = glow;
        }

        public void Tick(Enemy boss, ref TickTime time)
        {
            if (boss.World == null || boss.Dead)
                return;
            Apply(boss);
            var dt = time.ElapsedMsDelta;
            if (_rising > 0)
            {
                _rising -= dt;
                return;
            }
            if (boss.HasConditionEffect(ConditionEffectIndex.Stunned))
                return;

            if (!_enraged && boss.Health < boss.MaxHealth * 0.3)
            {
                _enraged = true;
                _haste = 1.45f;
                Apply(boss);
                BossResurrection.Notify(boss, "ENRAGED", 0xFFFF4FD8);
                BossResurrection.Effect(boss, EffectType.AreaBlast, 0xFFFF4FD8, new Position(5, 0));
                BossResurrection.Say(boss, DisplayName(boss), Pick(RageLines), true);
            }

            // only with someone to fight
            if (boss.GetNearestEntity(20, true) == null)
                return;

            dt = (int)(dt * _haste);

            _novaIn -= dt;
            if (_novaIn <= 0)
            {
                _ringAngle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                Ring(boss, _ringAngle);
                _secondRingIn = 450;
                _novaIn = _enraged ? 5000 : 7000;
            }
            if (_secondRingIn > 0 && (_secondRingIn -= dt) <= 0)
                Ring(boss, _ringAngle + (float)(Math.PI / 18));

            _soulsIn -= dt;
            if (_soulsIn <= 0)
            {
                SummonSouls(boss, _enraged ? 3 : 2);
                _soulsIn = _enraged ? 8000 : 11000;
            }

            if (boss.Health < boss.MaxHealth * 0.6)
            {
                _stormIn -= dt;
                if (_stormIn <= 0 && _storm <= 0)
                {
                    _storm = 2400;
                    _stormShotIn = 0;
                    _stormAngle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                    _stormIn = 10000;
                    BossResurrection.Effect(boss, EffectType.Concentrate, BossResurrection.SoulViolet, new Position(boss.X, boss.Y), new Position(boss.X + 3, boss.Y));
                }
                if (_storm > 0)
                {
                    _storm -= dt;
                    _stormShotIn -= dt;
                    if (_stormShotIn <= 0)
                    {
                        DoomedRuntime.Fire(boss, BossResurrection.SoulBolt + 1, _stormAngle, Math.PI / 2, 4);
                        _stormAngle += (float)(14 * Math.PI / 180);
                        _stormShotIn = 200;
                    }
                }
            }
        }

        private static void Ring(Enemy boss, float angle)
        {
            DoomedRuntime.Fire(boss, BossResurrection.SoulBolt, angle, Math.PI * 2 / 18, 18);
            BossResurrection.Effect(boss, EffectType.Trap, BossResurrection.SoulGreen, new Position(2.5f, 0));
        }

        private void SummonSouls(Enemy boss, int count)
        {
            var world = boss.World;
            if (BossResurrection.WailingSoulType == 0)
                return;
            var alive = world.Enemies.Values.Count(e => e.ObjectType == BossResurrection.WailingSoulType && e.DistTo(boss) < 30);
            count = Math.Min(count, MaxSouls - alive);
            for (var i = 0; i < count; i++)
            {
                if (Entity.Resolve(boss.GameServer, BossResurrection.WailingSoulType) is not Enemy soul)
                    return;
                var a = Random.Shared.NextDouble() * Math.PI * 2;
                soul.Move(boss.X + (float)Math.Cos(a) * 2, boss.Y + (float)Math.Sin(a) * 2);
                soul.SummonedBy(boss);
                soul.SpawnedByBehavior = true;
                world.EnterWorld(soul);
                soul.IsLegendary = soul.IsEpic = soul.IsRare = false;
                soul.MaxHealth = soul.Health = Math.Max(400, (int)(boss.MaxHealth * 0.012));
                soul.DamageScale = _info.SoulDamage * 0.6f / 100f;
                BossResurrection.Effect(soul, EffectType.Concentrate, BossResurrection.SoulGreen, new Position(soul.X, soul.Y), new Position(soul.X + 1.5f, soul.Y));
            }
            if (count > 0)
                BossResurrection.Say(boss, "Escaping Souls", "We hunt for our master!", false);
        }

        // killed a second time: the souls are free, and their ghosts go with it
        public void Died(Enemy boss)
        {
            var world = boss.World;
            foreach (var soul in world.Enemies.Values.Where(e => e.ObjectType == BossResurrection.WailingSoulType).ToList())
                world.LeaveWorld(soul);
            BossResurrection.Effect(boss, EffectType.AreaBlast, BossResurrection.SoulGreen, new Position(7, 0));
            BossResurrection.Notify(boss, "The souls are free", BossResurrection.SoulGreen);
            var msg = $"{DisplayName(boss)} has fallen for good. The souls are at peace.";
            world.ForeachPlayer(p => p.SendInfo(msg));
        }
    }

    /// <summary>
    /// An Empowered boss's health. Its Elder form's is multiplied by <see cref="Scale"/>: 2.5 for
    /// a boss of 20k or less, 1.5 at 200k (the Tomb's, the Ivory Wyvern), 1.25 at 800k (the Elder
    /// Void Entity) and no lower, on a log scale between. What its behaviour adds for more players
    /// (ScaleHP2, ScaleHP, BossStats) is added as it is, as for the Elder boss: before, the whole
    /// pool was multiplied again each time a player came near, so a group's boss grew 2.5 times
    /// per player (3.4 million health for three players on the Ivory Wyvern).
    /// </summary>
    public sealed class EmpoweredHealth
    {
        private static readonly (double Hp, double Scale)[] Curve = { (20000, 2.5), (200000, 1.5), (800000, 1.25) };

        public readonly double Scale;
        private readonly int _bonus;
        // the max health last set
        private int _setMax = -1;

        public EmpoweredHealth(int elderHp)
        {
            Scale = ScaleFor(elderHp);
            _bonus = (int)Math.Min(int.MaxValue / 4, Math.Round(elderHp * (Scale - 1)));
        }

        public static double ScaleFor(int elderHp)
        {
            if (elderHp <= Curve[0].Hp)
                return Curve[0].Scale;
            for (var i = 1; i < Curve.Length; i++)
                if (elderHp <= Curve[i].Hp)
                {
                    var (h0, s0) = Curve[i - 1];
                    var (h1, s1) = Curve[i];
                    var t = Math.Log(elderHp / h0) / Math.Log(h1 / h0);
                    return s0 + (s1 - s0) * t;
                }
            return Curve[^1].Scale;
        }

        /// <summary>The boss's max health and health now, given what its behaviour has left them at.</summary>
        public (int Max, int Health) Update(int max, int health)
        {
            if (max == _setMax)
                return (max, health);
            if (_setMax < 0)
            {
                // first time: the Elder boss's health and the bonus on top, at the same share
                var share = Math.Clamp(health / (double)max, 0, 1);
                var first = max + _bonus;
                _setMax = first;
                return (first, Math.Max(1, (int)Math.Round(first * share)));
            }
            // the behaviour added (or took off) this much: it stands, the bonus stays as it was
            var delta = max - _setMax;
            var newMax = Math.Max(_bonus + 1, max);
            if (delta < 0)
                health = (int)Math.Round(newMax * Math.Clamp(health / (double)Math.Max(1, max), 0, 1));
            _setMax = newMax;
            return (newMax, Math.Clamp(health, 1, newMax));
        }
    }
}
