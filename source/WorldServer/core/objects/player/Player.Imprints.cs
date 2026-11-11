using System;
using System.Collections.Generic;
using System.Linq;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // What the imprints on equipped gear do (Shared/resources/Imprints.cs): their stats (in
    // BoostStatManager), their effects on the weapon's shots (PlayerShootHandler, PlayerShoot), on
    // its hits (EnemyHit) and the rest. Also the weapons with <AlternateProjectiles/> (the Enthroned
    // weapons), whose volleys take turns with their projectiles, as the client's do.
    partial class Player
    {
        // the Wand's Battle Mage bolts: the object whose projectile they are (CustomObjects.xml)
        public const string BattleMageBoltId = "Battle Mage Bolt";
        private const int BattleMageDamage = 450;
        private const double BattleMageRate = 0.6;

        public int[] ImprintsAt(int slot) => slot >= 0 && slot < Inventory.Length ? Inventory.Data[slot]?.Imprints : null;

        // Whether the item in this slot carries this imprint, and it fits the item.
        public bool HasImprint(int slot, byte id) => ImprintValue(slot, id) != 0;

        // The value stored for this imprint on the item in this slot (its id, and for the Apotheosis
        // of Balance the stats picked), or 0 when it doesn't carry it or it doesn't fit.
        public int ImprintValue(int slot, byte id)
        {
            var imprints = ImprintsAt(slot);
            var item = Inventory[slot];
            // imprints work on Enthroned gear only
            if (imprints == null || item == null || !item.Ascended)
                return 0;
            foreach (var i in imprints)
                if ((i & 0xFF) == id)
                    return Imprint.Get(id)?.Fits(item) == true ? i : 0;
            return 0;
        }

        public ImprintShot WeaponImprints => ImprintShot.Of(ImprintsAt(0), Inventory[0], CorruptionAt(0));

        // the corruption of the item in this slot (Corruption.cs), or null
        public int[] CorruptionAt(int slot) => slot >= 0 && slot < Inventory.Length ? Inventory.Data[slot]?.Corruption : null;

        // the stats the ring's Ascendant Might and Ascendant Ward raise by half
        private static readonly StatDataType[] MightStats = { StatDataType.Attack, StatDataType.Speed, StatDataType.Dexterity };
        private static readonly StatDataType[] WardStats = { StatDataType.Defense, StatDataType.Vitality, StatDataType.Wisdom };

        // Whether the ring carries Guardian Angel: its own stats count for nothing then (BoostStatManager),
        // ImprintStats giving its +20 health and +1 to the rest instead.
        public bool RingGuarded => HasImprint(3, Imprint.GuardianAngel);

        // The equipped items' imprinted stats, for BoostStatManager: (stat, amount) each.
        public IEnumerable<(StatDataType Stat, int Amount)> ImprintStats()
        {
            // corrupted Enthroned gear: every stat it raises, by its corruption's stats value
            for (var slot = 0; slot < 4; slot++)
            {
                var item = Inventory[slot];
                var c = CorruptionAt(slot);
                if (item == null || !item.Ascended || !Corruption.Any(c))
                    continue;
                var by = Corruption.Of(c)[Corruption.Stats];
                if (by == 0)
                    continue;
                foreach (var b in item.ActivateOnEquips)
                    if (b.Value > 0)
                        yield return ((StatDataType)b.Key, by);
            }
            for (var slot = 0; slot < 4; slot++)
            {
                if (HasImprint(slot, Imprint.Vigor))
                    yield return (StatDataType.MaximumHealth, 25);
                if (HasImprint(slot, Imprint.Bulwark))
                {
                    yield return (StatDataType.Defense, 5);
                    yield return (StatDataType.MaximumHealth, 15);
                }
            }
            if (HasImprint(0, Imprint.BattleMage))
                yield return (StatDataType.Defense, Imprint.BattleMageDefense);
            // the ring: half again of its own stats, or Guardian Angel's in place of them
            var ring = Inventory[3];
            if (ring == null)
                yield break;
            if (RingGuarded)
            {
                yield return (StatDataType.MaximumHealth, 20);
                foreach (var stat in new[] { StatDataType.MaximumMana, StatDataType.Attack, StatDataType.Defense, StatDataType.Speed,
                    StatDataType.Dexterity, StatDataType.Vitality, StatDataType.Wisdom })
                    yield return (stat, 1);
                yield break;
            }
            foreach (var (stats, id) in new[] { (MightStats, Imprint.AscendantMight), (WardStats, Imprint.AscendantWard) })
            {
                if (!HasImprint(3, id))
                    continue;
                foreach (var b in ring.ActivateOnEquips)
                    if (stats.Contains((StatDataType)b.Key) && b.Value > 0)
                        yield return ((StatDataType)b.Key, b.Value / 2);
            }
        }

        // The Apotheosis imprints, after every other boost: for each stat (StatsManager's order),
        // the percent of what gear and boosts add that it adds, and whether it loses a fifth of all
        // of it (the Apotheosis of Balance's two picked stats).
        public (int[] AddedPercent, bool[] Lowered) ImprintScaling()
        {
            var added = new int[8];
            var lowered = new bool[8];
            if (HasImprint(0, Imprint.ApotheosisOfMight))
            {
                added[StatsManager.GetStatIndex(StatDataType.Attack)] += 10;
                added[StatsManager.GetStatIndex(StatDataType.Dexterity)] += 10;
            }
            if (HasImprint(2, Imprint.ApotheosisOfWard))
            {
                added[StatsManager.GetStatIndex(StatDataType.Defense)] += 10;
                added[StatsManager.GetStatIndex(StatDataType.Vitality)] += 10;
            }
            var balance = ImprintValue(3, Imprint.ApotheosisOfBalance);
            if (balance != 0)
            {
                var (raised, low1, low2) = Imprint.Choice(balance);
                if (Imprint.ValidChoice(raised, low1, low2))
                {
                    added[raised] += 10;
                    lowered[low1] = lowered[low2] = true;
                }
            }
            return (added, lowered);
        }

        // --- shooting ---

        // the weapon's volleys so far, and when the last began: the client counts its own the same
        // way, so both pick the same projectile of a weapon whose volleys alternate
        private int _weaponVolleys;
        private int _lastVolleyTime = int.MinValue;
        // the place in its volley of the weapon shot being handled (0 for the first), for a weapon
        // with <VolleyProjectiles>: the client sends a volley's shots in order, with one time
        private int _volleyShot;

        public void NoteWeaponShot(int time)
        {
            if (time == _lastVolleyTime)
            {
                _volleyShot++;
                return;
            }
            _lastVolleyTime = time;
            _volleyShot = 0;
            _weaponVolleys++;
        }

        // The projectile a shot of this item fires: a weapon whose volleys alternate takes its
        // projectiles in turn, one with <VolleyProjectiles> the one for the shot's place in the
        // volley, everything else its first.
        public ProjectileDesc ShotProjectile(Item item, int slot)
        {
            if (slot == 0 && item.AlternateProjectiles && item.Projectiles.Length > 1)
                return item.Projectiles[(_weaponVolleys - 1 + item.Projectiles.Length) % item.Projectiles.Length];
            if (slot == 0 && item.VolleyProjectiles is { Length: > 0 } volley)
            {
                var index = volley[System.Math.Min(_volleyShot, volley.Length - 1)];
                if (index >= 0 && index < item.Projectiles.Length)
                    return item.Projectiles[index];
            }
            return item.Projectiles[0];
        }

        public ProjectileDesc BattleMageBoltDesc
        {
            get
            {
                var data = GameServer.Resources.GameData;
                return data.IdToObjectType.TryGetValue(BattleMageBoltId, out var type) ? data.ObjectDescs[type].Projectiles.GetValueOrDefault(0) : null;
            }
        }

        private int _lastBoltTime = int.MinValue;
        private readonly Dictionary<int, int> _boltCounts = new Dictionary<int, int>();

        // A Battle Mage bolt: only with the imprint on the wand, two a volley at most, at 40% of the
        // wand's rate. Returns its projectile, or null to refuse it.
        public ProjectileDesc BattleMageBolt(int time)
        {
            var desc = BattleMageBoltDesc;
            if (!HasImprint(0, Imprint.BattleMage) || desc == null)
                return null;
            if (time != _lastBoltTime)
            {
                // a little slack: the client times them from its own clock
                var period = 1 / Stats.GetAttackFrequency() / Inventory[0].RateOfFire / BattleMageRate;
                if (_lastBoltTime != int.MinValue && time < _lastBoltTime + period * 0.9)
                    return null;
                _lastBoltTime = time;
                _boltCounts.Clear();
            }
            _boltCounts.TryGetValue(time, out var n);
            if (n >= 2)
                return null;
            _boltCounts[time] = n + 1;
            return desc;
        }

        public void BattleMageShoot(int time, int bulletId, Position start, float angle, ProjectileDesc desc)
        {
            var type = GameServer.Resources.GameData.IdToObjectType[BattleMageBoltId];
            var damage = (int)Client.Random.NextIntRange((uint)desc.MinDamage, (uint)desc.MaxDamage);
            if (!ActiveProjectiles.ContainsKey(Id))
                ActiveProjectiles[Id] = new Dictionary<int, ValidatedProjectile>();
            var proj = new ValidatedProjectile(Id, bulletId, start, angle, type, damage, DamageType.Enemy, desc, false) { Time = time };
            ActiveProjectiles[Id][bulletId] = proj;
            World.BroadcastIfVisibleExclude(new AllyShootMessage(bulletId, Id, type, angle), this, this);
        }

        // --- the weapon's hits ---

        private readonly Dictionary<int, int> _wounds = new Dictionary<int, int>();
        private double _siphoned;
        private const int MaxBloodrush = 10;
        private int _bloodrush;

        // after a shot of the imprinted weapon dealt dmg to the enemy (killed: it died of it)
        private void ImprintHit(Enemy enemy, int dmg, bool killed, ref TickTime time)
        {
            if (HasImprint(0, Imprint.Siphon) && dmg > 0)
            {
                _siphoned += dmg * 0.002;
                var heal = (int)_siphoned;
                if (heal > 0)
                {
                    _siphoned -= heal;
                    Health = Math.Min(Stats[0], Health + heal);
                }
            }

            if (HasImprint(0, Imprint.Bloodrush) && killed)
                Bloodrush();

            if (HasImprint(0, Imprint.Sunder) && !killed && enemy.Defense > 0)
            {
                enemy.Defense -= 1;
                World.StartNewTimer(10000, (w, t) =>
                {
                    if (!enemy.Dead && enemy.World == w)
                        enemy.Defense += 1;
                });
            }

            if (HasImprint(0, Imprint.Wounds) && !killed)
            {
                _wounds.TryGetValue(enemy.Id, out var n);
                n++;
                if (n >= 25)
                {
                    _wounds.Remove(enemy.Id);
                    WoundsBurst(enemy, ref time);
                }
                else
                    _wounds[enemy.Id] = n;     // counted quietly: only the burst shows, in purple
            }
        }

        // the 25th wound: 6000 that ignores defense to the enemy and every enemy within 4 tiles (its
        // numbers purple, as damage that ignores defense is)
        private void WoundsBurst(Enemy enemy, ref TickTime time)
        {
            World.BroadcastIfVisible(new ShowEffect()
            {
                EffectType = EffectType.AreaBlast,
                Color = new ARGB(0xffc81e3c),
                TargetObjectId = enemy.Id,
                Pos1 = new Position() { X = 4 }
            }, enemy);
            var hit = new List<Enemy>();
            enemy.AOE(4, false, e =>
            {
                if (e is Enemy en && !en.Dead && en.Id != enemy.Id)
                    hit.Add(en);
            });
            hit.Insert(0, enemy);
            foreach (var e in hit)
                if (!e.Dead && e.World != null)
                    e.Damage(this, ref time, Imprint.WoundsBurst, true);
        }

        // a kill with Bloodrush: +4 attack, dexterity and speed for 20 s, each kill's on its own time
        // (at most MaxBloodrush at once)
        private void Bloodrush()
        {
            if (_bloodrush >= MaxBloodrush || World == null)
                return;
            _bloodrush++;
            var stats = new[] { StatsManager.GetStatIndex(StatDataType.Attack), StatsManager.GetStatIndex(StatDataType.Dexterity),
                StatsManager.GetStatIndex(StatDataType.Speed) };
            foreach (var i in stats)
                Stats.Boost.ActivateBoost[i].AddOffset(4);
            Stats.ReCalculateValues();
            World.StartNewTimer(20000, (w, t) =>
            {
                _bloodrush--;
                foreach (var i in stats)
                    Stats.Boost.ActivateBoost[i].AddOffset(-4);
                Stats.ReCalculateValues();
            });
        }

        // --- the rest ---

        // when Last Breath can heal again, and Wellspring give magic again (Environment.TickCount64)
        private long _lastBreathReady, _wellspringReady;

        // Wellspring on the ring: 300 magic below 15% of it, every 180 s at most
        private void Wellspring()
        {
            if (Health <= 0 || Mana >= Stats[1] * 0.15 || Environment.TickCount64 < _wellspringReady || !HasImprint(3, Imprint.Wellspring))
                return;
            _wellspringReady = Environment.TickCount64 + 180000;
            Mana = Math.Min(Stats[1], Mana + 300);
            World.BroadcastIfVisible(new ShowEffect() { EffectType = EffectType.Potion, TargetObjectId = Id, Color = new ARGB(0xff6084ff) }, this);
            World.BroadcastIfVisible(new Notification() { ObjectId = Id, Color = new ARGB(0xff6084ff), Message = "+300 Wellspring" }, this);
        }

        // Unbound Will on the ring: whether this use of the ability costs no magic (a 20% chance)
        public bool FreeCast(int slot) => slot == 1 && HasImprint(3, Imprint.UnboundWill) && Random.Shared.NextDouble() < 0.2;

        // Benediction on the ring: each use of the ability, one of these for 4 s that the ability
        // itself doesn't give
        private static readonly ConditionEffectIndex[] Blessings =
        {
            ConditionEffectIndex.Healing, ConditionEffectIndex.Speedy, ConditionEffectIndex.Berserk,
            ConditionEffectIndex.Armored, ConditionEffectIndex.Damaging
        };

        public void Benediction(Item ability, int slot)
        {
            if (slot != 1 || !HasImprint(3, Imprint.Benediction))
                return;
            var gives = new HashSet<ConditionEffectIndex>(ability.ActivateEffects
                .Where(e => e.Effect is ActivateEffects.ConditionEffectSelf or ActivateEffects.ConditionEffectAura && e.ConditionEffect != null)
                .Select(e => e.ConditionEffect.Value));
            var left = Blessings.Where(b => !gives.Contains(b)).ToArray();
            if (left.Length == 0)
                return;
            ApplyConditionEffect(left[Random.Shared.Next(left.Length)], 4000);
        }

        private void LastBreath()
        {
            Wellspring();
            if (Health <= 0 || Health >= Stats[0] * 0.15 || Environment.TickCount64 < _lastBreathReady)
                return;
            for (var slot = 0; slot < 4; slot++)
            {
                if (!HasImprint(slot, Imprint.LastBreath))
                    continue;
                _lastBreathReady = Environment.TickCount64 + 180000;
                Health = Math.Min(Stats[0], Health + 300);
                World.BroadcastIfVisible(new ShowEffect() { EffectType = EffectType.Potion, TargetObjectId = Id, Color = new ARGB(0xffffffff) }, this);
                World.BroadcastIfVisible(new Notification() { ObjectId = Id, Color = new ARGB(0xff00ff00), Message = "+300 Last Breath" }, this);
                return;
            }
        }

        // the Wisp Caller's wisps: object, chance a shot, then when each can come again
        private static readonly string[] Wisps = { "Wisp of Life", "Wisp of Warding", "Wisp of Ruin" };
        private const int WispMs = 5000, WispCooldownMs = Imprint.WispCooldownMs;
        private readonly long[] _wispReady = new long[3];

        // after each shot of the weapon
        private void WispCaller()
        {
            if (!HasImprint(0, Imprint.WispCaller))
                return;
            var now = Environment.TickCount64;
            for (var i = 0; i < Wisps.Length; i++)
            {
                if (now < _wispReady[i] || Random.Shared.NextDouble() >= Imprint.WispChance)
                    continue;
                _wispReady[i] = now + WispMs + WispCooldownMs;
                var wisp = Resolve(GameServer, Wisps[i]);
                if (wisp != null)
                {
                    wisp.AllyOwnerId = Id;
                    wisp.GivesNoXp = true;
                    wisp.ApplyConditionEffect(ConditionEffectIndex.Invincible, -1);
                    wisp.Move(X, Y);
                    wisp.Z = Z;
                    World.EnterWorld(wisp);
                }
                switch (i)
                {
                    case 0:
                        ApplyConditionEffect(ConditionEffectIndex.Healing, WispMs);
                        break;
                    case 1:
                        ApplyConditionEffect(ConditionEffectIndex.Armored, WispMs);
                        break;
                    default:
                        ApplyConditionEffect(ConditionEffectIndex.Curse, WispMs);
                        this.AOE(5, false, e =>
                        {
                            if (e is Enemy en && !en.Dead)
                                en.ApplyConditionEffect(ConditionEffectIndex.Curse, WispMs);
                        });
                        break;
                }
            }
        }
    }
}
