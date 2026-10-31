using Shared.resources;
using System;
using System.Collections.Generic;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.logic;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    public class Enemy : Character
    {
        private float _bleeding = 0;

        protected StatTypeValue<int> _defense;
        public int Defense
        {
            get => _defense.GetValue();
            set => _defense.SetValue(value);
        }

        private StatTypeValue<int> _glowcolor;
        public int GlowEnemy
        {
            get => _glowcolor.GetValue();
            set => _glowcolor.SetValue(value);
        }

        public DamageCounter DamageCounter { get; private set; }
        public Enemy ParentEntity { get; set; }

        public TerrainType Terrain { get; set; }

        public bool IsEpic { get; set; }
        public bool IsLegendary { get; set; }
        public bool IsRare { get; set; }
        // multiplies the XML damage of this enemy's Shoot behaviours (set by BossStats)
        public float DamageScale { get; set; } = 1f;
        // a realm event boss whose health the realm sets by the players near it (RealmManager),
        // in place of its own ScaleHP2
        public bool RealmScaled { get; set; }
        // multiplies the experience it gives (below 1 while a realm event has less health than
        // its XML, so a smaller boss isn't worth as much)
        public float XpScale { get; set; } = 1f;
        // the Training Dummy's: tells each player their damage over 5 seconds
        public DpsMeter DpsMeter { get; private set; }

        // A mystic orb's shred (ShredArmor): the defense it had, the share taken off, and until when.
        // The Defense stat itself is lowered, so the client's damage numbers follow it.
        private int _shredFrom;
        private int _shredTo;
        private double _shredShare;
        private long _shredUntil;

        public Enemy(GameServer manager, ushort objType)
            : base(manager, objType)
        {
            DamageCounter = new DamageCounter(this);
            _glowcolor = new StatTypeValue<int>(this, StatDataType.GlowEnemy, 0);
            _defense = new StatTypeValue<int>(this, StatDataType.Defense, ObjectDesc.Defense);
            if (ObjectDesc.IdName == DpsMeter.ObjectId)
                DpsMeter = new DpsMeter(this);
        }

        public override void Init(World owner)
        {
            base.Init(owner);
            if (ObjectDesc.Quest || ObjectDesc.Hero || ObjectDesc.Encounter)
                ClasifyEnemy();
        }

        /// <summary>Takes this share (0.08 for 8%) off its defense for so long: the strongest shred
        /// holds, a weaker one only keeps it going for longer.</summary>
        public void ShredArmor(double share, int durationMs)
        {
            var now = Environment.TickCount64;
            if (_shredUntil == 0)
                _shredFrom = Defense;
            _shredShare = Math.Max(_shredShare, share);
            _shredUntil = Math.Max(_shredUntil, now + durationMs);
            _shredTo = Math.Max(0, _shredFrom - (int)Math.Ceiling(_shredFrom * _shredShare));
            Defense = _shredTo;
        }

        private void ShredTick()
        {
            if (_shredUntil == 0 || Environment.TickCount64 < _shredUntil)
                return;
            // given back, unless something else set its defense meanwhile (BossStats)
            if (Defense == _shredTo)
                Defense = _shredFrom;
            _shredUntil = 0;
            _shredShare = 0;
        }

        public override void Tick(ref TickTime time)
        {
            DpsMeter?.Tick();
            ShredTick();

            // objects without MaxHitPoints (anchors, spawners, traps) start at 0 HP; they end
            // through their behaviour instead of dying on their first tick
            if (Health == 0 && MaxHealth > 0 && !Dead)
                Death(ref time);

            // the Bleeding condition's own drain; not while it only shows a weapon's bleed (Bleed)
            if (HasConditionEffect(ConditionEffectIndex.Bleeding) && (_bleeds == null || _bleeds.Count == 0))
            {
                if (_bleeding > 1)
                {
                    Health -= (int)_bleeding;
                    _bleeding -= (int)_bleeding;
                }

                _bleeding += time.DeltaTime * 5;
            }

            base.Tick(ref time);
        }

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            base.ExportStats(stats, isOtherPlayer);
            stats[StatDataType.GlowEnemy] = GlowEnemy;
        }

        public void ClasifyEnemy()
        {
            var chance = Random.Shared.NextDouble();
            if (chance < 0.2)
            {
                var type = Random.Shared.Next(0, 3);
                switch (type)
                {
                    case 2:
                        {
                            IsLegendary = true;
                            GlowEnemy = 0xD865A5;
                        }
                        break;
                    case 1:
                        {
                            IsEpic = true;
                            GlowEnemy = 0xC183AF;
                        }
                        break;
                    case 0:
                        {
                            IsRare = true;
                            GlowEnemy = 0x82D9BC;
                        }
                        break;
                }

                Size += (type + 1) * 25;
                MaxHealth *= type + 1;
                Health = MaxHealth;
            }
        }

        public void ClasifyEnemyJson(string clasify)
        {
            var clasified = clasify.ToLowerInvariant();

            if (clasified == "legendary")
            {
                IsLegendary = true;

                Size = Size <= 0 ? Size : Random.Shared.Next(Size + 200, Size + 300);

                MaxHealth *= 2;
                Health = MaxHealth;
                Defense += 10;
                GlowEnemy = 0xD865A5;
            }
            else if (clasified == "epic")
            {
                IsEpic = true;

                Size = Size <= 0 ? Size : Random.Shared.Next(Size + 100, Size + 200);

                MaxHealth = (int)Math.Round(MaxHealth * 1.5, 0);
                Health = MaxHealth;
                Defense += 5;
                GlowEnemy = 0xC183AF;
            }
            else if (clasified == "rare")
            {
                IsRare = true;

                Size = Size <= 0 ? Size : Random.Shared.Next(Size, Size + 100);

                MaxHealth = (int)Math.Round(MaxHealth * 1.25, 0);
                Defense += 3;
                GlowEnemy = 0x82D9BC;
            }
        }

        public int Damage(Player from, ref TickTime time, int dmg, bool noDef, bool itsPoison = false, params ConditionEffect[] effs)
        {
            if (!itsPoison && HasConditionEffect(ConditionEffectIndex.Invincible))
                return 0;

            if (!HasConditionEffect(ConditionEffectIndex.Paused) && !HasConditionEffect(ConditionEffectIndex.Stasis))
            {
                var def = Defense;
                var dmgd = StatsManager.DamageWithDefense(this, dmg, noDef, def);

                var effDmg = dmgd;

                if (effDmg > Health)
                    effDmg = Health;

                if (!HasConditionEffect(ConditionEffectIndex.Invulnerable))
                    Health -= effDmg;

                ApplyConditionEffect(effs);
                World.BroadcastIfVisible(new DamageMessage()
                {
                    TargetId = Id,
                    Effects = 0,
                    DamageAmount = effDmg,
                    Kill = Health <= 0,
                    BulletId = 0,
                    ObjectId = from.Id,
                    Pierce = noDef          // the client shows damage that ignored defense in purple
                }, this);

                DamageCounter?.HitBy(from, effDmg);
                logic.doomed.DoomedRuntime.OnHit(this, from, time);

                if (Health <= 0 && World != null)
                    Death(ref time);

                return effDmg;
            }

            return 0;
        }

        private Dictionary<int, BleedState> _bleeds;

        private sealed class BleedState
        {
            public int Total, DurationMs, Elapsed, SinceHit, Dealt;
        }

        // Pets' bleeds (PetStrike's, and pet projectiles with <Bleed>): total damage over durationMs,
        // ignoring defense, a share each second, credited to from like any hit (so it counts for
        // loot). A bleed from the same player starts again rather than stacking. The Bleeding
        // condition only shows it (its icon over the enemy): Tick doesn't drain health for it while
        // a bleed runs, as that drain credits no one.
        public void Bleed(Player from, int total, int durationMs)
        {
            if (Dead || World == null || total <= 0)
                return;
            _bleeds ??= new Dictionary<int, BleedState>();
            var fresh = !_bleeds.TryGetValue(from.Id, out var b);
            if (fresh)
                _bleeds[from.Id] = b = new BleedState();
            b.Total = total;
            b.DurationMs = Math.Max(1, durationMs);
            b.Elapsed = b.SinceHit = b.Dealt = 0;
            ApplyConditionEffect(ConditionEffectIndex.Bleeding, b.DurationMs);
            if (!fresh)
                return;

            WorldTimer tmr = null;
            bool tick(World w, TickTime t)
            {
                if (Dead || World != w || from.World != w)
                {
                    _bleeds.Remove(from.Id);
                    return true;
                }
                b.Elapsed += t.ElapsedMsDelta;
                b.SinceHit += t.ElapsedMsDelta;
                var done = b.Elapsed >= b.DurationMs;
                if (b.SinceHit >= 1000 || done)
                {
                    b.SinceHit = 0;
                    var owed = (int)((long)b.Total * Math.Min(b.Elapsed, b.DurationMs) / b.DurationMs) - b.Dealt;
                    if (owed > 0)
                    {
                        b.Dealt += owed;
                        Damage(from, ref t, owed, true);
                    }
                }
                if (done || Dead)
                {
                    _bleeds.Remove(from.Id);
                    return true;
                }
                tmr.Reset();
                return false;
            }
            tmr = World.StartNewTimer(0, tick);
        }

        public event EventHandler<BehaviorEventArgs> OnDeath;

        public void Death(ref TickTime time)
        {
            if (!Dead)
            {
                DamageCounter.Death();
                CurrentState?.OnDeath(this, ref time);
                OnDeath?.Invoke(this, new BehaviorEventArgs(this, time));
                if (GameServer.BehaviorDb.Definitions.TryGetValue(ObjectType, out var loot))
                    loot.Item2?.Handle(this, time);
                if (ObjectDesc.Quest)
                    World?.BossDied(this);
            }
            Dead = true;
            World.LeaveWorld(this);
        }
    }
}
