using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;
using BL = WorldServer.logic.loot;

// The behaviour engine of the Doomed Realms server (its wserver/logic/Behavior.cs and friends),
// rebuilt on betterSkillys so the monsters of the ported dungeons keep their original logic.
// Their definitions are generated into DoomedMonsters.cs by tools/port_doomed.py, nearly verbatim.
//
// Semantics are the original's: every behaviour returns whether it "did something", composites
// (If, IfNot, QueuedBehavior, ...) branch on that, and per-monster memory (cooldowns, keys,
// states) lives in a DrHost attached to the entity. Instance(...) returns a shared node for the
// same arguments, as the original did, so identical nodes share their memory.
namespace WorldServer.logic.doomed
{
    #region Runtime

    internal sealed class DrHost
    {
        private static readonly ConditionalWeakTable<Entity, DrHost> Hosts = new ConditionalWeakTable<Entity, DrHost>();

        public Entity Self { get; private set; }
        public Dictionary<int, object> StateStorage { get; } = new Dictionary<int, object>();
        public Dictionary<int, object> StateTable { get; } = new Dictionary<int, object>();
        public string State = "idle";
        public string SubState = "1";
        public Definition Definition;

        public static DrHost Get(Entity entity) => Hosts.GetValue(entity, e => new DrHost { Self = e });
    }

    internal sealed class Definition
    {
        public readonly Behavior[] Behaviors;
        public readonly ConditionalBehavior[] CondBehaviors;
        public readonly LootBehavior Loot;

        public Definition(Behavior[] behaviors, ConditionalBehavior[] condBehaviors, LootBehavior loot)
        {
            Behaviors = behaviors ?? new Behavior[0];
            CondBehaviors = condBehaviors ?? new ConditionalBehavior[0];
            Loot = loot;
        }
    }

    // The betterSkillys behaviour that runs a Doomed Realms definition.
    internal sealed class DoomedBehaves : WorldServer.logic.Behavior
    {
        private readonly Definition _def;

        public DoomedBehaves(Definition def) => _def = def;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (host.World == null)
                return;
            var h = DrHost.Get(host);
            h.Definition = _def;
            foreach (var b in _def.Behaviors)
                b.Tick(h, time);
            // "Other" conditions are checked every tick (ChefTaunt)
            foreach (var c in _def.CondBehaviors)
                if (c.Condition == BehaviorCondition.Other)
                    c.Behave(BehaviorCondition.Other, h, time, null, null);
        }

        public override void OnDeath(Entity host, ref TickTime time)
        {
            var h = DrHost.Get(host);
            foreach (var c in _def.CondBehaviors)
                if ((c.Condition & BehaviorCondition.OnDeath) != 0)
                    c.Behave(BehaviorCondition.OnDeath, h, time, null, null);
        }
    }

    // Used by the generated BehaviorDb.DoomedMonsters.cs.
    internal static class DoomedSetup
    {
        public static WorldServer.logic.State State(Definition def) => new WorldServer.logic.State(new DoomedBehaves(def));

        public static BL.MobDrops[] Drops(string id, Definition def)
        {
            var extra = DoomedExtraLoot.Extra.TryGetValue(id, out var make) ? make() : new BL.MobDrops[0];
            if (def.Loot == null)
                return extra;
            var data = BehaviorDb.InitGameData;
            var maxHp = data.IdToObjectType.TryGetValue(id, out var type) && data.ObjectDescs.TryGetValue(type, out var desc) ? desc.MaxHP : 0;
            return def.Loot.ToDrops(maxHp).Concat(extra).ToArray();
        }
    }

    internal static class DoomedRuntime
    {
        // Entity.OnChatTextReceived calls this for ChatEvent behaviours.
        public static void OnChat(Entity entity, Player player, string text, TickTime time)
        {
            var h = DrHost.Get(entity);
            if (h.Definition == null)
                return;
            foreach (var c in h.Definition.CondBehaviors)
                if (c.Condition == BehaviorCondition.OnChat)
                    c.Behave(BehaviorCondition.OnChat, h, time, text, player);
        }

        public static void OnHit(Entity entity, Player player, TickTime time)
        {
            var h = DrHost.Get(entity);
            if (h.Definition == null)
                return;
            foreach (var c in h.Definition.CondBehaviors)
                if ((c.Condition & BehaviorCondition.OnHit) != 0)
                    c.Behave(BehaviorCondition.OnHit, h, time, null, player);
        }

        // Object types are written by id in the generated definitions.
        public static ushort? T(string id)
        {
            if (BehaviorDb.InitGameData != null && BehaviorDb.InitGameData.IdToObjectType.TryGetValue(id, out var t))
                return t;
            return null;
        }

        public static void Fire(Entity host, int projectileIndex, double startAngle, double angleInc, int count)
        {
            if (host.World == null || !host.ObjectDesc.Projectiles.TryGetValue(projectileIndex, out var desc))
                return;
            var dmg = (double)BehaviorBase.Rng.Next(desc.MinDamage, desc.MaxDamage);
            if (host.HasConditionEffect(ConditionEffectIndex.Weak))
                dmg /= 2;
            host.World.BroadcastEnemyShootIfVisible(new EnemyShootMessage()
            {
                Spawned = host.Spawned,
                BulletId = host.GetNextBulletId(count),
                OwnerId = host.Id,
                StartingPos = new Position(host.X, host.Y),
                Angle = (float)startAngle,
                Damage = (short)dmg,
                BulletType = (byte)desc.BulletType,
                AngleInc = (float)angleInc,
                NumShots = (byte)count,
                ObjectType = host.ObjectType,
                ProjectileDesc = desc,
            }, host);
        }

        public static Entity Spawn(Entity parent, ushort? objType, float x, float y)
        {
            if (objType == null || parent.World == null)
                return null;
            var entity = Entity.Resolve(parent.GameServer, objType.Value);
            if (entity == null)
                return null;
            if (entity is Enemy child && parent is Enemy enemyParent && enemyParent.Spawned)
                child.Spawned = true;
            entity.Move(x, y);
            entity.SummonedBy(parent);
            parent.World.EnterWorld(entity);
            return entity;
        }
    }

    [Flags]
    public enum BehaviorCondition
    {
        OnSpawn = 1,
        OnHit = 2,
        OnDeath = 4,
        OnChat = 5,
        Other = 8,
    }

    // Doomed Realms condition flag names, mapped to betterSkillys' ConditionEffectIndex by name.
    public enum ConditionEffects
    {
        Dead, Quiet, Weak, Slowed, Sick, Dazed, Stunned, Blind, Hallucinating, Drunk, Confused, StunImmume,
        Invisible, Paralyzed, Speedy, Bleeding, NotUsed, Healing, Damaging, Berserk, Paused, Stasis, StasisImmune,
        Invincible, Invulnerable, Armored, ArmorBroken
    }

    public abstract class BehaviorBase
    {
        private static int _nextKey = 1000;
        private static readonly Dictionary<object, object> Cache = new Dictionary<object, object>();
        internal static readonly Random Rng = new Random();

        protected BehaviorBase() => Key = System.Threading.Interlocked.Increment(ref _nextKey);

        public int Key { get; set; }
        internal DrHost Host { get; set; }

        protected static T Cached<T>(object key, Func<T> make) where T : class
        {
            lock (Cache)
            {
                var k = Tuple.Create(typeof(T), key);
                if (!Cache.TryGetValue(k, out var ret))
                    Cache[k] = ret = make();
                return (T)ret;
            }
        }

        public static ConditionEffectIndex ToIndex(ConditionEffects e)
        {
            var name = e == ConditionEffects.StunImmume ? "StunImmune" : e.ToString();
            return Enum.TryParse(name, out ConditionEffectIndex i) ? i : ConditionEffectIndex.Nothing;
        }

        public static float Dist(Entity a, Entity b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        public static double Dist(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));

        public const int MAGIC_EYE_KEY = -11;
        private bool VisibleToEnemy(Entity e) => !(e is IPlayer p) || p.IsVisibleToEnemy() || Host.StateStorage.ContainsKey(MAGIC_EYE_KEY);

        protected Entity GetNearestEntity(ref float dist, ushort? objType)
        {
            var self = Host.Self;
            if (self.World == null) return null;
            Entity ret = null;
            if (objType == null)
            {
                foreach (var i in self.World.PlayersCollision.HitTest(self.X, self.Y, dist))
                {
                    if (!(i is Player) || !VisibleToEnemy(i)) continue;
                    var d = Dist(i, self);
                    if (d < dist) { dist = d; ret = i; }
                }
            }
            else
            {
                foreach (var i in self.World.EnemiesCollision.HitTest(self.X, self.Y, dist))
                {
                    if (i.ObjectType != objType.Value || i.AllyOwnerId != -1 || i == self) continue;
                    var d = Dist(i, self);
                    if (d < dist) { dist = d; ret = i; }
                }
            }
            return ret;
        }

        protected IEnumerable<Entity> GetNearestEntities(float dist, ushort? objType)
        {
            var self = Host.Self;
            if (self.World == null) yield break;
            if (objType == null)
            {
                foreach (var i in self.World.PlayersCollision.HitTest(self.X, self.Y, dist).ToArray())
                    if (i is Player && VisibleToEnemy(i) && Dist(i, self) < dist)
                        yield return i;
            }
            else
            {
                foreach (var i in self.World.EnemiesCollision.HitTest(self.X, self.Y, dist).ToArray())
                    if (i.ObjectType == objType.Value && Dist(i, self) < dist)
                        yield return i;
            }
        }

        protected IEnumerable<Entity> GetNearestEntitiesByGroup(float dist, string group)
        {
            var self = Host.Self;
            if (self.World == null) yield break;
            foreach (var i in self.World.EnemiesCollision.HitTest(self.X, self.Y, dist).ToArray())
                if (i.ObjectDesc != null && i.ObjectDesc.Group == group && Dist(i, self) < dist)
                    yield return i;
        }

        protected Entity NearestOfGroup(float radius, string group, out float dist)
        {
            Entity ret = null;
            dist = radius;
            foreach (var i in GetNearestEntitiesByGroup(radius, group))
            {
                var d = Dist(i, Host.Self);
                if (i != Host.Self && d < dist) { dist = d; ret = i; }
            }
            return ret;
        }

        protected int CountEntity(float dist, ushort? objType) => GetNearestEntities(dist, objType).Count();
        protected int CountEntity(float dist, string group) => GetNearestEntitiesByGroup(dist, group).Count();

        protected static float GetSpeedMultiplier(Entity entity)
        {
            var ret = 1f;
            if (entity.HasConditionEffect(ConditionEffectIndex.Slowed))
                ret = 0.5f;
            if (entity.HasConditionEffect(ConditionEffectIndex.Paralyzed) || entity.HasConditionEffect(ConditionEffectIndex.Stasis) ||
                entity.HasConditionEffect(ConditionEffectIndex.Petrify))
                ret = 0;
            return ret;
        }

        // movement per tick, as Doomed Realms: speed / 1.5 tiles per second
        protected static float Step(float speed, TickTime time) => speed / 1.5f * (time.ElapsedMsDelta / 1000f);

        protected bool ValidateAndMove(float x, float y)
        {
            if (Host.Self.World == null || Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed))
                return false;
            Host.Self.ValidateAndMove(x, y);
            return true;
        }

        protected void MoveToward(float tx, float ty, float speed, TickTime time)
        {
            if (tx == Host.Self.X && ty == Host.Self.Y)
                return;
            var vect = new Vector2(tx - Host.Self.X, ty - Host.Self.Y);
            if (vect.Length() == 0)
                return;
            vect = Vector2.Normalize(vect) * Step(speed, time);
            ValidateAndMove(Host.Self.X + vect.X, Host.Self.Y + vect.Y);
        }

        protected static void AOE(World world, Position pos, float radius, bool players, Action<Entity> callback)
        {
            var targets = (players ? world.PlayersCollision.HitTest(pos.X, pos.Y, radius) : world.EnemiesCollision.HitTest(pos.X, pos.Y, radius)).ToArray();
            foreach (var i in targets)
            {
                if (players ? !(i is Player) : !(i is Enemy)) continue;
                if (Dist(i.X, i.Y, pos.X, pos.Y) < radius)
                    callback(i);
            }
        }

        protected void Taunt(string text)
        {
            var self = Host.Self;
            if (self.World == null) return;
            if (text.Contains("{PLAYER}"))
            {
                var dist = 10f;
                var player = GetNearestEntity(ref dist, null);
                if (player == null) return;
                text = text.Replace("{PLAYER}", player.Name);
            }
            if (self is Character c)
                text = text.Replace("{HP}", c.Health.ToString());
            self.World.BroadcastIfVisible(new Text()
            {
                Name = "#" + (self.ObjectDesc.DisplayId ?? self.ObjectDesc.IdName),
                ObjectId = self.Id,
                NumStars = -1,
                BubbleTime = 5,
                Recipient = "",
                Txt = text,
                CleanText = ""
            }, self);
        }

        protected void Effect(EffectType type, Entity target, uint color, Position pos1 = default, Position pos2 = default)
        {
            Host.Self.World?.BroadcastIfVisible(new ShowEffect() { EffectType = type, TargetObjectId = target.Id, Color = new ARGB(color), Pos1 = pos1, Pos2 = pos2 }, Host.Self);
        }
    }

    public abstract class Behavior : BehaviorBase
    {
        internal bool Tick(DrHost host, TickTime time)
        {
            var prev = Host;
            Host = host;
            try
            {
                return host.Self.World != null && TickCore(time);
            }
            catch (Exception e)
            {
                WorldServer.utils.StaticLogger.Instance.Error($"[Doomed behaviour] {GetType().Name} on {host.Self.ObjectDesc?.IdName}: {e.Message}");
                return true;
            }
            finally
            {
                Host = prev ?? host;
            }
        }

        internal bool Tick(Entity entity, TickTime time) => Tick(DrHost.Get(entity), time);

        protected abstract bool TickCore(TickTime time);
    }

    public abstract class ConditionalBehavior : BehaviorBase
    {
        public abstract BehaviorCondition Condition { get; }

        internal void Behave(BehaviorCondition cond, DrHost host, TickTime time, string msg, Player player)
        {
            Host = host;
            try
            {
                BehaveCore(cond, time, msg, player);
            }
            catch (Exception e)
            {
                WorldServer.utils.StaticLogger.Instance.Error($"[Doomed behaviour] {GetType().Name} on {host.Self.ObjectDesc?.IdName}: {e.Message}");
            }
        }

        protected abstract void BehaveCore(BehaviorCondition cond, TickTime time, string msg, Player player);
    }

    #endregion

    #region Composites and conditions

    internal class NullBehavior : Behavior
    {
        public static readonly NullBehavior Instance = new NullBehavior();
        protected override bool TickCore(TickTime time) => true;
    }

    internal class RunBehaviors : Behavior
    {
        private readonly Behavior[] behavs;
        public RunBehaviors(params Behavior[] behaviors) => behavs = behaviors;

        protected override bool TickCore(TickTime time)
        {
            foreach (var i in behavs)
                i.Tick(Host, time);
            return true;
        }
    }

    internal class QueuedBehavior : Behavior
    {
        private readonly Behavior[] behavs;
        public QueuedBehavior(params Behavior[] behaviors) => behavs = behaviors;

        protected override bool TickCore(TickTime time)
        {
            var idx = Host.StateStorage.TryGetValue(Key, out var obj) ? (int)obj : 0;
            bool repeat;
            var c = 0;
            var ret = false;
            do
            {
                repeat = false;
                if (behavs[idx].Tick(Host, time))
                {
                    repeat = true;
                    idx++;
                    if (idx == behavs.Length)
                    {
                        idx = 0;
                        ret = true;
                    }
                }
                c++;
            } while (repeat && c < 2);
            Host.StateStorage[Key] = idx;
            return ret;
        }
    }

    internal class SetKey : Behavior
    {
        private readonly int key, val;
        public SetKey(int key, int val) { this.key = key; this.val = val; }

        protected override bool TickCore(TickTime time)
        {
            Host.StateStorage[key] = val;
            Host.StateTable.Clear();
            return true;
        }
    }

    internal class RemoveKey : Behavior
    {
        private readonly int key;
        public RemoveKey(int key) => this.key = key;
        public RemoveKey(Behavior behav) => key = behav.Key;

        protected override bool TickCore(TickTime time)
        {
            Host.StateStorage.Remove(key);
            return true;
        }
    }

    internal class Cooldown : Behavior
    {
        private readonly Behavior behav;
        private readonly int cooldown;
        private Cooldown(int cooldown, Behavior behav) { this.cooldown = cooldown; this.behav = behav; }
        public static Cooldown Instance(int cooldown, Behavior behav = null) => Cached(Tuple.Create(cooldown, behav), () => new Cooldown(cooldown, behav));

        protected override bool TickCore(TickTime time)
        {
            var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : Rng.Next(0, Math.Max(1, cooldown));
            remaining -= time.ElapsedMsDelta;
            bool ret;
            if (remaining <= 0)
            {
                behav?.Tick(Host, time);
                remaining = Rng.Next((int)(cooldown * 0.95), (int)(cooldown * 1.05) + 1);
                ret = true;
            }
            else
                ret = false;
            Host.StateStorage[Key] = remaining;
            return ret;
        }
    }

    internal class RandomDelay : Behavior
    {
        private readonly int min, max;
        private RandomDelay(int min, int max) { this.min = min; this.max = max; }
        public static RandomDelay Instance(int min, int max) => Cached(Tuple.Create(min, max), () => new RandomDelay(min, max));

        protected override bool TickCore(TickTime time)
        {
            var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : Rng.Next(min, max);
            remaining -= time.ElapsedMsDelta;
            var ret = remaining <= 0;
            if (ret)
                remaining = Rng.Next(min, max);
            Host.StateStorage[Key] = remaining;
            return ret;
        }
    }

    internal class CooldownExact : Behavior
    {
        private readonly Behavior behav;
        private readonly int cooldown;
        private CooldownExact(int cooldown, Behavior behav) { this.cooldown = cooldown; this.behav = behav; }
        public static CooldownExact Instance(int cooldown, Behavior behav = null) => Cached(Tuple.Create(cooldown, behav), () => new CooldownExact(cooldown, behav));

        protected override bool TickCore(TickTime time)
        {
            var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : cooldown;
            remaining -= time.ElapsedMsDelta;
            bool ret;
            if (remaining <= 0)
            {
                behav?.Tick(Host, time);
                remaining = cooldown;
                ret = true;
            }
            else
                ret = false;
            Host.StateStorage[Key] = remaining;
            return ret;
        }
    }

    internal class Rand : Behavior
    {
        private readonly Behavior[] behavs;
        private Rand(Behavior[] behavs) => this.behavs = behavs;
        public static Rand Instance(params Behavior[] behavs) => Cached(string.Join(",", behavs.Select(b => b.Key)), () => new Rand(behavs));
        protected override bool TickCore(TickTime time) => behavs[Rng.Next(0, behavs.Length)].Tick(Host, time);
    }

    internal class If : Behavior
    {
        private readonly Behavior cond, result, no;
        private If(Behavior cond, Behavior result, Behavior no) { this.cond = cond; this.result = result; this.no = no; }
        public static If Instance(Behavior cond, Behavior result, Behavior no = null) => Cached(Tuple.Create(cond, result, no), () => new If(cond, result, no));

        protected override bool TickCore(TickTime time)
        {
            if (cond.Tick(Host, time))
                return result.Tick(Host, time);
            return no != null && no.Tick(Host, time);
        }
    }

    internal class IfNot : Behavior
    {
        private readonly Behavior cond, result;
        private IfNot(Behavior cond, Behavior result) { this.cond = cond; this.result = result; }
        public static IfNot Instance(Behavior cond, Behavior result) => Cached(Tuple.Create(cond, result), () => new IfNot(cond, result));
        protected override bool TickCore(TickTime time) => !cond.Tick(Host, time) && result.Tick(Host, time);
    }

    internal class IfExist : Behavior
    {
        private readonly int key;
        private readonly Behavior result, no;
        private IfExist(int key, Behavior result, Behavior no) { this.key = key; this.result = result; this.no = no; }
        public static IfExist Instance(int key, Behavior result, Behavior no = null) => Cached(Tuple.Create(key, result, no), () => new IfExist(key, result, no));

        protected override bool TickCore(TickTime time)
        {
            if (Host.StateStorage.ContainsKey(key))
                return result.Tick(Host, time);
            return no != null && no.Tick(Host, time);
        }
    }

    internal class IfEqual : Behavior
    {
        private readonly int key, value;
        private readonly Behavior result, no;
        private IfEqual(int key, int value, Behavior result, Behavior no) { this.key = key; this.value = value; this.result = result; this.no = no; }
        public static IfEqual Instance(int key, int value, Behavior result, Behavior no = null) => Cached(Tuple.Create(key, value, result, no), () => new IfEqual(key, value, result, no));

        protected override bool TickCore(TickTime time)
        {
            if (!Host.StateStorage.TryGetValue(key, out var obj))
                return false;
            if ((int)obj == value)
                return result.Tick(Host, time);
            return no != null && no.Tick(Host, time);
        }
    }

    internal class IfGreater : Behavior
    {
        private readonly int key, value;
        private readonly Behavior result, no;
        private IfGreater(int key, int value, Behavior result, Behavior no) { this.key = key; this.value = value; this.result = result; this.no = no; }
        public static IfGreater Instance(int key, int value, Behavior result, Behavior no = null) => Cached(Tuple.Create(key, value, result, no), () => new IfGreater(key, value, result, no));

        protected override bool TickCore(TickTime time)
        {
            if (!Host.StateStorage.TryGetValue(key, out var obj))
                return false;
            if ((int)obj > value)
                return result.Tick(Host, time);
            return no != null && no.Tick(Host, time);
        }
    }

    internal abstract class HpCondition : Behavior
    {
        protected Behavior result, no;
        protected abstract bool Met(Character c);

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self is Character c && Met(c))
                return result.Tick(Host, time);
            return no != null && no.Tick(Host, time);
        }
    }

    internal class HpLesser : HpCondition
    {
        private readonly int threshold;
        private HpLesser(int threshold, Behavior result, Behavior no) { this.threshold = threshold; this.result = result; this.no = no; }
        public static HpLesser Instance(int threshold, Behavior result, Behavior no = null) => Cached(Tuple.Create(threshold, result, no), () => new HpLesser(threshold, result, no));
        protected override bool Met(Character c) => c.Health < threshold;
    }

    internal class HpGreaterEqual : HpCondition
    {
        private readonly int threshold;
        private HpGreaterEqual(int threshold, Behavior result, Behavior no) { this.threshold = threshold; this.result = result; this.no = no; }
        public static HpGreaterEqual Instance(int threshold, Behavior result, Behavior no = null) => Cached(Tuple.Create(threshold, result, no), () => new HpGreaterEqual(threshold, result, no));
        protected override bool Met(Character c) => c.Health >= threshold;
    }

    internal class DamageLesserEqual : HpCondition
    {
        private readonly int threshold;
        private DamageLesserEqual(int threshold, Behavior result, Behavior no) { this.threshold = threshold; this.result = result; this.no = no; }
        public static DamageLesserEqual Instance(int threshold, Behavior result, Behavior no = null) => Cached(Tuple.Create(threshold, result, no), () => new DamageLesserEqual(threshold, result, no));
        protected override bool Met(Character c) => c.MaxHealth - c.Health <= threshold;
    }

    internal class HpLesserPercent : HpCondition
    {
        private readonly float threshold;
        private HpLesserPercent(float threshold, Behavior result, Behavior no) { this.threshold = threshold; this.result = result; this.no = no; }
        public static HpLesserPercent Instance(float threshold, Behavior result, Behavior no = null) => Cached(Tuple.Create(threshold, result, no), () => new HpLesserPercent(threshold, result, no));
        protected override bool Met(Character c) => c.Health < threshold * c.MaxHealth;
    }

    internal class Not : Behavior
    {
        private readonly Behavior x;
        private Not(Behavior x) => this.x = x;
        public static Not Instance(Behavior x) => Cached(x, () => new Not(x));
        protected override bool TickCore(TickTime time) => !x.Tick(Host, time);
    }

    internal class And : Behavior
    {
        private readonly Behavior x, y;
        private And(Behavior x, Behavior y) { this.x = x; this.y = y; }
        public static And Instance(Behavior x, Behavior y) => Cached(Tuple.Create(x, y), () => new And(x, y));
        protected override bool TickCore(TickTime time) => x.Tick(Host, time) && y.Tick(Host, time);
    }

    internal class Or : Behavior
    {
        private readonly Behavior x, y;
        private Or(Behavior x, Behavior y) { this.x = x; this.y = y; }
        public static Or Instance(Behavior x, Behavior y) => Cached(Tuple.Create(x, y), () => new Or(x, y));
        protected override bool TickCore(TickTime time) => x.Tick(Host, time) || y.Tick(Host, time);
    }

    internal class False : Behavior
    {
        private readonly Behavior x;
        private False(Behavior x) => this.x = x;
        public static False Instance(Behavior x) => Cached(x, () => new False(x));
        protected override bool TickCore(TickTime time) { x.Tick(Host, time); return false; }
    }

    internal class True : Behavior
    {
        private readonly Behavior x;
        private True(Behavior x) => this.x = x;
        public static True Instance(Behavior x) => Cached(x, () => new True(x));
        protected override bool TickCore(TickTime time) { x.Tick(Host, time); return true; }
    }

    internal class Once : Behavior
    {
        private readonly Behavior x;
        private Once(Behavior x) => this.x = x;
        public static Once Instance(Behavior x) => Cached(x, () => new Once(x));

        protected override bool TickCore(TickTime time)
        {
            if (Host.StateStorage.ContainsKey(Key))
                return false;
            x.Tick(Host, time);
            Host.StateStorage[Key] = true;
            return true;
        }
    }

    internal class Timed : Behavior
    {
        private readonly Behavior behav;
        private readonly int time;
        private Timed(int time, Behavior behav) { this.time = time; this.behav = behav; }
        public static Timed Instance(int time, Behavior behav) => Cached(Tuple.Create(time, behav), () => new Timed(time, behav));

        protected override bool TickCore(TickTime t)
        {
            var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : time;
            remaining -= t.ElapsedMsDelta;
            var ret = behav.Tick(Host, t);
            if (remaining <= 0)
            {
                remaining = time;
                ret = true;
            }
            else if (ret)
                remaining = time;
            Host.StateStorage[Key] = remaining;
            return ret;
        }
    }

    internal class Despawn : Behavior
    {
        public static readonly Despawn Instance = new Despawn();
        protected override bool TickCore(TickTime time) { Host.Self.World.LeaveWorld(Host.Self); return true; }
    }

    internal class Die : Behavior
    {
        public static readonly Die Instance = new Die();

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self is Enemy e)
                e.Death(ref time);
            else
                Host.Self.World.LeaveWorld(Host.Self);
            return true;
        }
    }

    internal class IsEntityPresent : Behavior
    {
        private readonly float radius;
        private readonly ushort? objType;
        private IsEntityPresent(float radius, ushort? objType) { this.radius = radius; this.objType = objType; }
        public static IsEntityPresent Instance(float radius, ushort? objType) => Cached(Tuple.Create(radius, objType), () => new IsEntityPresent(radius, objType));
        protected override bool TickCore(TickTime time) { var d = radius; return GetNearestEntity(ref d, objType) != null; }
    }

    internal class IsEntityNotPresent : Behavior
    {
        private readonly float radius;
        private readonly ushort? objType;
        private IsEntityNotPresent(float radius, ushort? objType) { this.radius = radius; this.objType = objType; }
        public static IsEntityNotPresent Instance(float radius, ushort? objType) => Cached(Tuple.Create(radius, objType), () => new IsEntityNotPresent(radius, objType));
        protected override bool TickCore(TickTime time) { var d = radius; return GetNearestEntity(ref d, objType) == null; }
    }

    internal class EntityLesserThan : Behavior
    {
        private readonly float radius;
        private readonly int count;
        private readonly ushort? objType;
        private EntityLesserThan(float radius, int count, ushort? objType) { this.radius = radius; this.count = count; this.objType = objType; }
        public static EntityLesserThan Instance(float radius, int count, ushort? objType) => Cached(Tuple.Create(radius, count, objType), () => new EntityLesserThan(radius, count, objType));
        protected override bool TickCore(TickTime time) => CountEntity(radius, objType) < count;
    }

    internal class EntityGroupLesserThan : Behavior
    {
        private readonly float radius;
        private readonly int count;
        private readonly string group;
        private EntityGroupLesserThan(float radius, int count, string group) { this.radius = radius; this.count = count; this.group = group; }
        public static EntityGroupLesserThan Instance(float radius, int count, string group) => Cached(Tuple.Create(radius, count, group), () => new EntityGroupLesserThan(radius, count, group));
        protected override bool TickCore(TickTime time) => CountEntity(radius, group) < count;
    }

    internal class WithinSpawn : Behavior
    {
        private readonly float radius;
        private WithinSpawn(float radius) => this.radius = radius;
        public static WithinSpawn Instance(float radius) => Cached(radius, () => new WithinSpawn(radius));

        protected override bool TickCore(TickTime time)
        {
            var sp = Host.Self.SpawnPoint ?? new Position(Host.Self.X, Host.Self.Y);
            return Dist(Host.Self.X, Host.Self.Y, sp.X, sp.Y) < radius;
        }
    }

    internal class CheckConditionEffects : Behavior
    {
        private readonly ConditionEffects[] effects;
        private CheckConditionEffects(ConditionEffects[] effects) => this.effects = effects;
        public static CheckConditionEffects Instance(ConditionEffects[] effects) => Cached(string.Join(",", effects), () => new CheckConditionEffects(effects));
        protected override bool TickCore(TickTime time) => effects.All(e => Host.Self.HasConditionEffect(ToIndex(e)));
    }

    // lets the monster target invisible players (BehaviorBase.GetNearestEntity checks it)
    internal class MagicEye : Behavior
    {
        public static readonly MagicEye Instance = new MagicEye();
        protected override bool TickCore(TickTime time) { Host.StateStorage[BehaviorBase.MAGIC_EYE_KEY] = true; return true; }
    }

    internal class PlaySound : Behavior
    {
        private PlaySound() { }
        public static PlaySound Instance(int soundIndex = 0) => Cached(soundIndex, () => new PlaySound());
        protected override bool TickCore(TickTime time) => true; // monster sounds are played by the client
    }

    // whether the monster stands on a tile of the region
    internal class CheckRegion : Behavior
    {
        private readonly TileRegion region;
        private CheckRegion(TileRegion region) => this.region = region;
        public static CheckRegion Instance(TileRegion region) => Cached(region, () => new CheckRegion(region));

        protected override bool TickCore(TickTime time)
        {
            var map = Host.Self.World?.Map;
            if (map == null || !map.Contains((int)Host.Self.X, (int)Host.Self.Y))
                return false;
            return map[(int)Host.Self.X, (int)Host.Self.Y].Region == region;
        }
    }

    // tells the world's minigame something happened ("sheep", "dmg:2", ...)
    internal class WorldEvent : Behavior
    {
        private readonly string type;
        private WorldEvent(string type) => this.type = type;
        public static WorldEvent Instance(string type) => Cached(type, () => new WorldEvent(type));

        protected override bool TickCore(TickTime time)
        {
            (Host.Self.World as IDoomedEventWorld)?.BehaviorEvent(type);
            return true;
        }
    }

    public interface IDoomedEventWorld
    {
        void BehaviorEvent(string type);
    }

    #endregion

    #region States and events

    internal class State : Behavior
    {
        private readonly string name;
        private readonly Behavior[] behave;
        public State(string name, params Behavior[] behave) { this.name = name; this.behave = behave; }

        protected override bool TickCore(TickTime time)
        {
            if (Host.State != name)
                return false;
            foreach (var i in behave)
                i.Tick(Host, time);
            return true;
        }
    }

    internal class NotState : Behavior
    {
        private readonly string name;
        private readonly Behavior[] behave;
        public NotState(string name, params Behavior[] behave) { this.name = name; this.behave = behave; }

        protected override bool TickCore(TickTime time)
        {
            if (Host.State == name)
                return false;
            foreach (var i in behave)
                i.Tick(Host, time);
            return true;
        }
    }

    internal class SubState : Behavior
    {
        private readonly string name;
        private readonly Behavior[] behave;
        public SubState(string name, params Behavior[] behave) { this.name = name; this.behave = behave; }

        protected override bool TickCore(TickTime time)
        {
            if (Host.SubState != name)
                return false;
            foreach (var i in behave)
                i.Tick(Host, time);
            return true;
        }
    }

    internal class SetState : Behavior
    {
        private readonly string state;
        private SetState(string state) => this.state = state;
        public static SetState Instance(string state) => Cached(state, () => new SetState(state));

        protected override bool TickCore(TickTime time)
        {
            var states = state.Split('.');
            Host.State = states[0];
            if (states.Length > 1)
                Host.SubState = string.IsNullOrEmpty(states[1]) ? "1" : states[1];
            Host.StateTable.Clear();
            return true;
        }
    }

    internal class StateOnce : Behavior
    {
        private readonly Behavior[] x;
        private StateOnce(Behavior[] x) => this.x = x;
        public static StateOnce Instance(params Behavior[] x) => Cached(string.Join(",", x.Select(b => b.Key)), () => new StateOnce(x));

        protected override bool TickCore(TickTime time)
        {
            if (Host.StateTable.ContainsKey(Key))
                return false;
            foreach (var i in x)
                i.Tick(Host, time);
            Host.StateTable[Key] = true;
            return true;
        }
    }

    internal class OnDeath : ConditionalBehavior
    {
        private readonly Behavior behav;
        public OnDeath(Behavior behav) => this.behav = behav;
        public override BehaviorCondition Condition => BehaviorCondition.OnDeath;
        protected override void BehaveCore(BehaviorCondition cond, TickTime time, string msg, Player player) => behav.Tick(Host, time);
    }

    internal class OnHit : ConditionalBehavior
    {
        private readonly Behavior behav;
        public OnHit(Behavior behav) => this.behav = behav;
        public override BehaviorCondition Condition => BehaviorCondition.OnHit;
        protected override void BehaveCore(BehaviorCondition cond, TickTime time, string msg, Player player) => behav.Tick(Host, time);
    }

    // runs the behaviour percent% of the time
    internal class RandomDo : Behavior
    {
        private readonly int percent;
        private readonly Behavior result;
        public RandomDo(int percent, Behavior result) { this.percent = percent; this.result = result; }
        protected override bool TickCore(TickTime time) => Rng.Next(1, 100) <= percent && result.Tick(Host, time);
    }

    internal class ConditionalState : ConditionalBehavior
    {
        private readonly BehaviorCondition cond;
        private readonly string name;
        private readonly Behavior[] behave;
        public ConditionalState(BehaviorCondition cond, string name, params Behavior[] behave) { this.cond = cond; this.name = name; this.behave = behave; }
        public override BehaviorCondition Condition => cond;

        protected override void BehaveCore(BehaviorCondition c, TickTime time, string msg, Player player)
        {
            if (Host.State == name)
                foreach (var i in behave)
                    i.Tick(Host, time);
        }
    }

    internal class ChatEvent : ConditionalBehavior
    {
        private readonly Behavior[] behaves;
        private string[] chat = new string[0];
        private Behavior[] falseBehaves;
        private bool adminOnly;

        public ChatEvent(params Behavior[] behaves) => this.behaves = behaves;
        public override BehaviorCondition Condition => BehaviorCondition.OnChat;
        public ChatEvent SetChats(params string[] chat) { this.chat = chat; return this; }
        public ChatEvent FalseEvent(params Behavior[] falseBehaves) { this.falseBehaves = falseBehaves; return this; }
        public ChatEvent AdminOnly() { adminOnly = true; return this; }

        protected override void BehaveCore(BehaviorCondition cond, TickTime time, string msg, Player player)
        {
            if (msg == null || player == null || adminOnly && player.Client.Account.Rank < 2 && !player.Client.Account.Admin)
                return;
            if (chat.Any(s => string.Equals(msg, s, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var i in behaves)
                    i.Tick(Host, time);
                return;
            }
            if (falseBehaves != null)
                foreach (var f in falseBehaves)
                    f.Tick(Host, time);
        }
    }

    // Bonegrind the Butcher's two lines: when a player comes near, and when its health runs low
    internal class ChefTaunt : ConditionalBehavior
    {
        private readonly int threshold;
        public ChefTaunt(int threshold) => this.threshold = threshold;
        public override BehaviorCondition Condition => BehaviorCondition.Other;

        protected override void BehaveCore(BehaviorCondition cond, TickTime time, string msg, Player player)
        {
            var n = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : 0;
            var dist = 8f;
            if (n == 0 && GetNearestEntity(ref dist, null) != null)
            {
                Taunt("Ah, fresh meat for the minions!");
                Host.StateStorage[Key] = 1;
            }
            else if (n < 2 && Host.Self is Character c && c.Health < threshold)
            {
                Taunt("The meat ain't supposed to bite back! Waaaaa!!");
                Host.StateStorage[Key] = 2;
            }
        }
    }

    internal class DeathTransmute : ConditionalBehavior
    {
        private readonly ushort? objType;
        private readonly int minCount, maxCount;
        public DeathTransmute(ushort? objType, int minCount = 1, int maxCount = 1) { this.objType = objType; this.minCount = minCount; this.maxCount = maxCount; }
        public override BehaviorCondition Condition => BehaviorCondition.OnDeath;

        protected override void BehaveCore(BehaviorCondition cond, TickTime time, string msg, Player player)
        {
            var c = Rng.Next(minCount, maxCount + 1);
            for (var i = 0; i < c; i++)
                DoomedRuntime.Spawn(Host.Self, objType, Host.Self.X, Host.Self.Y);
        }
    }

    internal class DeathPortal : ConditionalBehavior
    {
        private readonly ushort? objType;
        private readonly int percent, timeExist;
        public DeathPortal(ushort? objType, int percent, int timeExist) { this.objType = objType; this.percent = percent; this.timeExist = timeExist; }
        public override BehaviorCondition Condition => BehaviorCondition.OnDeath;

        protected override void BehaveCore(BehaviorCondition cond, TickTime time, string msg, Player player)
        {
            var world = Host.Self.World;
            if (world == null || objType == null || world is DoomedBattleArena)
                return;
            if (Rng.Next(1, 100) > percent)
                return;
            var portal = Entity.Resolve(Host.Self.GameServer, objType.Value);
            if (portal == null)
                return;
            portal.Move(Host.Self.X, Host.Self.Y);
            world.EnterWorld(portal);
            // as DropPortalOnDeath: only a portal dropped in a realm closes
            if (timeExist > 0 && world.IsRealm)
                world.StartNewTimer(timeExist * 1000, (w, t) => w.LeaveWorld(portal));
        }
    }

    #endregion

    #region Attacks

    internal abstract class AttackBase : Behavior
    {
        protected static double Predict(Entity self, Entity target, ProjectileDesc desc)
        {
            // Doomed Realms read 100 ms of position history; betterSkillys keeps the previous position.
            var originalAngle = Math.Atan2(target.PrevY - self.Y, target.PrevX - self.X);
            var newAngle = Math.Atan2(target.Y - self.Y, target.X - self.X);
            var angularVelo = (newAngle - originalAngle) / 0.2;
            return angularVelo * (desc.Speed / 100f);
        }

        protected bool Stunned => Host.Self.HasConditionEffect(ConditionEffectIndex.Stunned);
        protected bool Dazed => Host.Self.HasConditionEffect(ConditionEffectIndex.Dazed);
        protected ProjectileDesc Desc(int index) => Host.Self.ObjectDesc.Projectiles.TryGetValue(index, out var d) ? d : null;
    }

    internal class SimpleAttack : AttackBase
    {
        private readonly float radius;
        private readonly int projectileIndex;
        private SimpleAttack(float radius, int projectileIndex) { this.radius = radius; this.projectileIndex = projectileIndex; }
        public static SimpleAttack Instance(float radius, int projectileIndex = 0) => Cached(Tuple.Create(radius, projectileIndex), () => new SimpleAttack(radius, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            var dist = radius;
            var player = GetNearestEntity(ref dist, null);
            if (player == null) return false;
            DoomedRuntime.Fire(Host.Self, projectileIndex, Math.Atan2(player.Y - Host.Self.Y, player.X - Host.Self.X), 0, 1);
            return true;
        }
    }

    internal class MultiAttack : AttackBase
    {
        private readonly float radius, angle, offset;
        private readonly int numShot, projectileIndex;
        private MultiAttack(float radius, float angle, int numShot, float offset, int projectileIndex) { this.radius = radius; this.angle = angle; this.numShot = numShot; this.offset = offset; this.projectileIndex = projectileIndex; }
        public static MultiAttack Instance(float radius, float angle, int numShot, float offset = 0, int projectileIndex = 0) =>
            Cached(Tuple.Create(radius, angle, numShot, offset, projectileIndex), () => new MultiAttack(radius, angle, numShot, offset, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            var n = Dazed ? Math.Max(1, numShot / 2) : numShot;
            var dist = radius;
            var player = GetNearestEntity(ref dist, null);
            if (player == null) return false;
            var start = Math.Atan2(player.Y - Host.Self.Y, player.X - Host.Self.X) - angle * (n - 1) / 2 + offset;
            DoomedRuntime.Fire(Host.Self, projectileIndex, start, angle, n);
            return true;
        }
    }

    internal class RingAttack : AttackBase
    {
        private readonly int count, projectileIndex;
        private readonly float radius, offset;
        private RingAttack(int count, float radius, float offset, int projectileIndex) { this.count = count; this.radius = radius; this.offset = offset; this.projectileIndex = projectileIndex; }
        public static RingAttack Instance(int count, float radius = 0, float offset = 0, int projectileIndex = 0) =>
            Cached(Tuple.Create(count, radius, offset, projectileIndex), () => new RingAttack(count, radius, offset, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            var dist = radius;
            var entity = radius == 0 ? null : GetNearestEntity(ref dist, null);
            if (entity == null && radius != 0) return false;
            var angle = entity == null ? offset : Math.Atan2(entity.Y - Host.Self.Y, entity.X - Host.Self.X) + offset;
            var n = Dazed ? Math.Max(1, count / 2) : count;
            DoomedRuntime.Fire(Host.Self, projectileIndex, angle, 2 * Math.PI / count, n);
            return true;
        }
    }

    internal class AngleAttack : AttackBase
    {
        private readonly float angle;
        private readonly int projectileIndex;
        private AngleAttack(float angle, int projectileIndex) { this.angle = angle; this.projectileIndex = projectileIndex; }
        public static AngleAttack Instance(float angle, int projectileIndex = 0) => Cached(Tuple.Create(angle, projectileIndex), () => new AngleAttack(angle, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            DoomedRuntime.Fire(Host.Self, projectileIndex, angle, 0, 1);
            return true;
        }
    }

    internal class AngleMultiAttack : AttackBase
    {
        private readonly float angle, projAngle;
        private readonly int numShot, projectileIndex;
        private AngleMultiAttack(float angle, float projAngle, int numShot, int projectileIndex) { this.angle = angle; this.projAngle = projAngle; this.numShot = numShot; this.projectileIndex = projectileIndex; }
        public static AngleMultiAttack Instance(float angle, float projAngle, int numShot, int projectileIndex = 0) =>
            Cached(Tuple.Create(angle, projAngle, numShot, projectileIndex), () => new AngleMultiAttack(angle, projAngle, numShot, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            DoomedRuntime.Fire(Host.Self, projectileIndex, angle - projAngle * (numShot - 1) / 2, projAngle, numShot);
            return true;
        }
    }

    internal class PredictiveAttack : AttackBase
    {
        private readonly float radius, predictFactor;
        private readonly int projectileIndex;
        private PredictiveAttack(float radius, float predictFactor, int projectileIndex) { this.radius = radius; this.predictFactor = predictFactor; this.projectileIndex = projectileIndex; }
        public static PredictiveAttack Instance(float radius, float predictFactor, int projectileIndex = 0) =>
            Cached(Tuple.Create(radius, predictFactor, projectileIndex), () => new PredictiveAttack(radius, predictFactor, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            var dist = radius;
            var entity = GetNearestEntity(ref dist, null);
            var desc = Desc(projectileIndex);
            if (entity == null || desc == null) return false;
            DoomedRuntime.Fire(Host.Self, projectileIndex, Math.Atan2(entity.Y - Host.Self.Y, entity.X - Host.Self.X) + Predict(Host.Self, entity, desc), 0, 1);
            return true;
        }
    }

    internal class PredictiveMultiAttack : AttackBase
    {
        private readonly float radius, angle, predictFactor;
        private readonly int numShot, projectileIndex;
        private PredictiveMultiAttack(float radius, float angle, int numShot, float predictFactor, int projectileIndex) { this.radius = radius; this.angle = angle; this.numShot = numShot; this.predictFactor = predictFactor; this.projectileIndex = projectileIndex; }
        public static PredictiveMultiAttack Instance(float radius, float angle, int numShot, float predictFactor, int projectileIndex = 0) =>
            Cached(Tuple.Create(radius, angle, numShot, predictFactor, projectileIndex), () => new PredictiveMultiAttack(radius, angle, numShot, predictFactor, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            var n = Dazed ? Math.Max(1, numShot / 2) : numShot;
            var dist = radius;
            var entity = GetNearestEntity(ref dist, null);
            var desc = Desc(projectileIndex);
            if (entity == null || desc == null) return false;
            var start = Math.Atan2(entity.Y - Host.Self.Y, entity.X - Host.Self.X) + Predict(Host.Self, entity, desc) - angle * (n - 1) / 2;
            DoomedRuntime.Fire(Host.Self, projectileIndex, start, angle, n);
            return true;
        }
    }

    internal class PredictiveRingAttack : AttackBase
    {
        private readonly int count, projectileIndex;
        private readonly float predictFactor, radius;
        private PredictiveRingAttack(int count, float predictFactor, float radius, int projectileIndex) { this.count = count; this.predictFactor = predictFactor; this.radius = radius; this.projectileIndex = projectileIndex; }
        public static PredictiveRingAttack Instance(int count, float predictFactor, float radius = 0, int projectileIndex = 0) =>
            Cached(Tuple.Create(count, predictFactor, radius, projectileIndex), () => new PredictiveRingAttack(count, predictFactor, radius, projectileIndex));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            var dist = radius;
            var entity = radius == 0 ? null : GetNearestEntity(ref dist, null);
            var desc = Desc(projectileIndex);
            if ((entity == null && radius != 0) || desc == null) return false;
            var angle = entity == null ? 0 : Math.Atan2(entity.Y - Host.Self.Y, entity.X - Host.Self.X) + Predict(Host.Self, entity, desc);
            var n = Dazed ? Math.Max(1, count / 2) : count;
            DoomedRuntime.Fire(Host.Self, projectileIndex, angle, 2 * Math.PI / count, n);
            return true;
        }
    }

    internal class ThrowAttack : AttackBase
    {
        private readonly float bombRadius, sightRadius, offsetAngle;
        private readonly int damage;
        private ThrowAttack(float bombRadius, float sightRadius, int damage, float offsetAngle) { this.bombRadius = bombRadius; this.sightRadius = sightRadius; this.damage = damage; this.offsetAngle = offsetAngle; }
        public static ThrowAttack Instance(float bombRadius, float sightRadius, int damage, float offsetAngle = 0) =>
            Cached(Tuple.Create(bombRadius, sightRadius, damage, offsetAngle), () => new ThrowAttack(bombRadius, sightRadius, damage, offsetAngle));

        protected override bool TickCore(TickTime time)
        {
            if (Stunned) return false;
            var dist = sightRadius;
            var player = GetNearestEntity(ref dist, null);
            if (player == null) return false;
            var self = Host.Self;
            var distance = Dist(self, player);
            var angle = Math.Atan2(player.Y - self.Y, player.X - self.X) + offsetAngle;
            var target = new Position(self.X + (float)Math.Cos(angle) * distance, self.Y + (float)Math.Sin(angle) * distance);
            Effect(EffectType.Throw, self, 0xffff0000, target);
            self.World.StartNewTimer(1500, (world, t) =>
            {
                world.BroadcastIfVisible(new AoeMessage(target, bombRadius, damage, 0, 0, self.ObjectType, new ARGB(0xffff0000)), self);
                AOE(world, target, bombRadius, true, p => (p as IPlayer)?.Damage(damage, self));
            });
            return true;
        }
    }

    internal class TossEnemy : AttackBase
    {
        private readonly float angle, range;
        private readonly ushort? objType;
        private TossEnemy(float angle, float range, ushort? objType) { this.angle = angle; this.range = range; this.objType = objType; }
        public static TossEnemy Instance(float angle, float range, ushort? objType) => Cached(Tuple.Create(angle, range, objType), () => new TossEnemy(angle, range, objType));

        protected override bool TickCore(TickTime time)
        {
            var self = Host.Self;
            var target = new Position(self.X + (float)Math.Cos(angle) * range, self.Y + (float)Math.Sin(angle) * range);
            Effect(EffectType.Throw, self, 0xffffbf00, target);
            self.World.StartNewTimer(1500, (world, t) => DoomedRuntime.Spawn(self, objType, target.X, target.Y));
            return true;
        }
    }

    internal class TossEnemyAtPlayer : AttackBase
    {
        private readonly float range;
        private readonly ushort? objType;
        private TossEnemyAtPlayer(float range, ushort? objType) { this.range = range; this.objType = objType; }
        public static TossEnemyAtPlayer Instance(float range, ushort? objType) => Cached(Tuple.Create(range, objType), () => new TossEnemyAtPlayer(range, objType));

        protected override bool TickCore(TickTime time)
        {
            var dist = range;
            var player = GetNearestEntity(ref dist, null);
            if (player != null)
            {
                var self = Host.Self;
                var target = new Position(player.X, player.Y);
                Effect(EffectType.Throw, self, 0xffffbf00, target);
                self.World.StartNewTimer(1500, (world, t) => DoomedRuntime.Spawn(self, objType, target.X, target.Y));
            }
            return true;
        }
    }

    internal class Heal : Behavior
    {
        private readonly float radius;
        private readonly int amount;
        private readonly ushort? objType;
        private Heal(float radius, int amount, ushort? objType) { this.radius = radius; this.amount = amount; this.objType = objType; }
        public static Heal Instance(float radius, int amount, ushort? objType) => Cached(Tuple.Create(radius, amount, objType), () => new Heal(radius, amount, objType));

        private bool HealOne(Character c, bool trail)
        {
            var hp = Math.Min(c.Health + amount, c.MaxHealth);
            if (hp == c.Health)
                return false;
            var n = hp - c.Health;
            c.Health = hp;
            Effect(EffectType.Potion, c, 0xffffffff);
            if (trail)
                Effect(EffectType.Trail, Host.Self, 0xffffffff, new Position(c.X, c.Y));
            c.World.BroadcastIfVisible(new Notification() { ObjectId = c.Id, Message = "+" + n, Color = new ARGB(0xff00ff00) }, c);
            return true;
        }

        protected override bool TickCore(TickTime time)
        {
            if (objType == null)
                return Host.Self is Character self && HealOne(self, false);
            foreach (var e in GetNearestEntities(radius, objType))
                if (e is Character c && HealOne(c, true))
                    return true;
            return false;
        }
    }

    internal class HealGroup : Behavior
    {
        private readonly float radius;
        private readonly int amount;
        private readonly string group;
        private HealGroup(float radius, int amount, string group) { this.radius = radius; this.amount = amount; this.group = group; }
        public static HealGroup Instance(float radius, int amount, string group) => Cached(Tuple.Create(radius, amount, group), () => new HealGroup(radius, amount, group));

        protected override bool TickCore(TickTime time)
        {
            var ret = false;
            foreach (var c in GetNearestEntitiesByGroup(radius, group).OfType<Enemy>())
            {
                var hp = Math.Min(c.Health + amount, c.MaxHealth);
                if (hp == c.Health)
                    continue;
                var n = hp - c.Health;
                c.Health = hp;
                Effect(EffectType.Potion, c, 0xffffffff);
                Effect(EffectType.Trail, Host.Self, 0xffffffff, new Position(c.X, c.Y));
                c.World.BroadcastIfVisible(new Notification() { ObjectId = c.Id, Message = "+" + n, Color = new ARGB(0xff00ff00) }, c);
                ret = true;
            }
            return ret;
        }
    }

    // the tutorial guns: one shot every 2 to 3.5 s in the direction their name gives
    internal class TutorialTower : Behavior
    {
        public static readonly TutorialTower Instance = new TutorialTower();

        protected override bool TickCore(TickTime time)
        {
            var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : 0;
            remaining -= time.ElapsedMsDelta;
            var ret = false;
            if (remaining <= 0)
            {
                var id = Host.Self.ObjectDesc.IdName;
                var angle = id.StartsWith("South") ? Math.PI / 2 : id.StartsWith("North") ? Math.PI * 3 / 2 : id.StartsWith("West") ? Math.PI : 0;
                DoomedRuntime.Fire(Host.Self, 0, angle, 0, 1);
                remaining = Rng.Next(2000, 3500);
                ret = true;
            }
            Host.StateStorage[Key] = remaining;
            return ret;
        }
    }

    #endregion

    #region Movement

    internal class AngleMove : Behavior
    {
        private readonly float speed, angle, dist;
        private AngleMove(float speed, float angle, float dist) { this.speed = speed; this.angle = angle; this.dist = dist; }
        public static AngleMove Instance(float speed, float angle, float dist) => Cached(Tuple.Create(speed, angle, dist), () => new AngleMove(speed, angle, dist));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            float d;
            if (!Host.StateStorage.TryGetValue(Key, out var o))
                d = dist;
            else
            {
                d = (float)o;
                var dd = Step(speed * GetSpeedMultiplier(Host.Self), time);
                d -= dd;
                ValidateAndMove(Host.Self.X + (float)Math.Cos(angle) * dd, Host.Self.Y + (float)Math.Sin(angle) * dd);
            }
            var ret = d <= 0;
            if (ret)
                d = dist;
            Host.StateStorage[Key] = d;
            return ret;
        }
    }

    internal class ChasingGroup : Behavior
    {
        private readonly float speed, radius, targetRadius;
        private readonly string group;
        private ChasingGroup(float speed, float radius, float targetRadius, string group) { this.speed = speed; this.radius = radius; this.targetRadius = targetRadius; this.group = group; }
        public static ChasingGroup Instance(float speed, float radius, float targetRadius, string group) =>
            Cached(Tuple.Create(speed, radius, targetRadius, group), () => new ChasingGroup(speed, radius, targetRadius, group));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var entity = NearestOfGroup(radius, group, out var dist);
            if (entity == null || dist <= targetRadius)
                return false;
            MoveToward(entity.X + Rng.Next(-2, 2) / 2f, entity.Y + Rng.Next(-2, 2) / 2f, speed * GetSpeedMultiplier(Host.Self), time);
            return true;
        }
    }

    // runs from the nearest target once its health is under the threshold
    internal class Escaping : Behavior
    {
        private readonly float speed, radius;
        private readonly int threshold;
        private readonly ushort? objType;
        private Escaping(float speed, float radius, int threshold, ushort? objType) { this.speed = speed; this.radius = radius; this.threshold = threshold; this.objType = objType; }
        public static Escaping Instance(float speed, float radius, int threshold, ushort? objType) =>
            Cached(Tuple.Create(speed, radius, threshold, objType), () => new Escaping(speed, radius, threshold, objType));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var dist = radius;
            var entity = GetNearestEntity(ref dist, objType);
            if (entity == null || !(Host.Self is Character c) || c.Health >= threshold)
                return false;
            MoveToward(2 * Host.Self.X - entity.X, 2 * Host.Self.Y - entity.Y, speed * GetSpeedMultiplier(Host.Self), time);
            if (!Host.StateStorage.ContainsKey(Key))
            {
                Effect(EffectType.Flashing, Host.Self, 0xff303030, new Position(1, 1000000));
                Host.StateStorage[Key] = true;
            }
            return true;
        }
    }

    // moves away from the nearest target
    internal class Retracting : Behavior
    {
        private readonly float speed, radius;
        private readonly ushort? objType;
        private Retracting(float speed, float radius, ushort? objType) { this.speed = speed; this.radius = radius; this.objType = objType; }
        public static Retracting Instance(float speed, float radius, ushort? objType) => Cached(Tuple.Create(speed, radius, objType), () => new Retracting(speed, radius, objType));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var dist = radius;
            var entity = GetNearestEntity(ref dist, objType);
            if (entity == null || entity.X == Host.Self.X && entity.Y == Host.Self.Y)
                return false;
            MoveToward(2 * Host.Self.X - entity.X, 2 * Host.Self.Y - entity.Y, speed * GetSpeedMultiplier(Host.Self), time);
            return true;
        }
    }

    internal class StrictCirclingGroup : Behavior
    {
        private class CirclingState { public float angle; public WeakReference<Entity> target; }
        private readonly float radius, angularSpeed;
        private readonly string group;
        private StrictCirclingGroup(float radius, float speed, string group) { this.radius = radius; angularSpeed = speed / radius; this.group = group; }
        public static StrictCirclingGroup Instance(float radius, float speed, string group) => Cached(Tuple.Create(radius, speed, group), () => new StrictCirclingGroup(radius, speed, group));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            if (!Host.StateStorage.TryGetValue(Key, out var o))
            {
                var t = NearestOfGroup(radius, group, out _);
                if (t == null) return false;
                Host.StateStorage[Key] = new CirclingState { target = new WeakReference<Entity>(t), angle = (float)(2 * Math.PI * Rng.NextDouble()) };
                return true;
            }
            var state = (CirclingState)o;
            state.angle += angularSpeed * GetSpeedMultiplier(Host.Self) * (time.ElapsedMsDelta / 1000f);
            if (!state.target.TryGetTarget(out var target) || target.World == null || target.Dead)
            {
                Host.StateStorage.Remove(Key);
                return false;
            }
            ValidateAndMove(target.X + (float)Math.Cos(state.angle) * radius, target.Y + (float)Math.Sin(state.angle) * radius);
            if (state.angle >= Math.PI * 2)
                state.angle -= (float)(Math.PI * 2);
            return true;
        }
    }

    // a golem's satellites: one coloured and three gray, once, when none are near
    internal class GolemSatelliteSpawn : Behavior
    {
        public static readonly GolemSatelliteSpawn Instance = new GolemSatelliteSpawn();
        private static readonly string[] Colored = { "Red Satellite", "Green Satellite", "Blue Satellite" };

        protected override bool TickCore(TickTime time)
        {
            if (NearestOfGroup(4, "Golem Satellites", out _) != null)
                return false;
            var ids = Host.Self.GameServer.Resources.GameData.IdToObjectType;
            void Spawn(string id)
            {
                if (ids.TryGetValue(id, out var t))
                    DoomedRuntime.Spawn(Host.Self, t, Host.Self.X, Host.Self.Y);
            }
            Spawn(Colored[Rng.Next(0, Colored.Length)]);
            for (var i = 1; i <= 3; i++)
                Spawn("Gray Satellite " + i);
            return true;
        }
    }

    // keeps a satellite with its golem, and removes it 30 s after the golem is gone. Doomed Realms
    // looked for the group "Golem", which no monster has, so its satellites always left after 30 s.
    internal class GolemSatellites : Behavior
    {
        public static readonly GolemSatellites Instance = new GolemSatellites();

        protected override bool TickCore(TickTime time)
        {
            var golem = NearestOfGroup(8, "Golems", out var dist);
            if (golem == null)
            {
                var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : 30 * 1000;
                remaining -= time.ElapsedMsDelta;
                if (remaining <= 0)
                    Host.Self.World.LeaveWorld(Host.Self);
                Host.StateStorage[Key] = remaining;
                return false;
            }
            if (dist > 4)
                ValidateAndMove(golem.X, golem.Y);
            Host.StateStorage[Key] = 30 * 1000;
            return true;
        }
    }

    internal class Chasing : Behavior
    {
        private readonly float speed, radius, targetRadius;
        private readonly ushort? objType;
        private Chasing(float speed, float radius, float targetRadius, ushort? objType) { this.speed = speed; this.radius = radius; this.targetRadius = targetRadius; this.objType = objType; }
        public static Chasing Instance(float speed, float radius, float targetRadius, ushort? objType) =>
            Cached(Tuple.Create(speed, radius, targetRadius, objType), () => new Chasing(speed, radius, targetRadius, objType));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var s = speed * GetSpeedMultiplier(Host.Self);
            var dist = radius;
            var entity = GetNearestEntity(ref dist, objType);
            if (entity == null || dist <= targetRadius)
                return false;
            MoveToward(entity.X + Rng.Next(-2, 2) / 2f, entity.Y + Rng.Next(-2, 2) / 2f, s, time);
            return true;
        }
    }

    internal class SimpleWandering : Behavior
    {
        private class WanderingState { public float remainingDist; public int x, y; }
        private readonly float speed, dist;
        private SimpleWandering(float speed, float dist) { this.speed = speed; this.dist = dist; }
        public static SimpleWandering Instance(float speed, float dist = 1f) => Cached(Tuple.Create(speed, dist), () => new SimpleWandering(speed, dist));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var s = speed * GetSpeedMultiplier(Host.Self);
            if (!Host.StateStorage.TryGetValue(Key, out var o))
                Host.StateStorage[Key] = o = new WanderingState();
            var state = (WanderingState)o;
            if (state.remainingDist > 0)
            {
                var d = Step(s, time);
                state.remainingDist -= d;
                ValidateAndMove(Host.Self.X + state.x * d, Host.Self.Y + state.y * d);
            }
            if (state.remainingDist > 0)
                return false;
            state.x = Rng.Next(-1, 2);
            state.y = Rng.Next(-1, 2);
            state.remainingDist = dist + dist * (float)(Rng.NextDouble() * 0.1 - 0.05);
            return true;
        }
    }

    internal class SmoothWandering : Behavior
    {
        private class WanderingState { public float angle; public Position beginPos; public float remainingDist; }
        private readonly float radius, speed;
        private SmoothWandering(float radius, float speed) { this.radius = radius; this.speed = speed; }
        public static SmoothWandering Instance(float radius, float speed) => Cached(Tuple.Create(radius, speed), () => new SmoothWandering(radius, speed));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var s = speed * GetSpeedMultiplier(Host.Self);
            if (!Host.StateStorage.TryGetValue(Key, out var o))
                Host.StateStorage[Key] = o = new WanderingState { beginPos = new Position(Host.Self.X, Host.Self.Y) };
            var state = (WanderingState)o;
            if (state.remainingDist > 0)
            {
                var d = Step(s, time);
                state.remainingDist -= d;
                ValidateAndMove(Host.Self.X + (float)Math.Cos(state.angle) * d, Host.Self.Y + (float)Math.Sin(state.angle) * d);
            }
            if (state.remainingDist > 0)
                return false;
            var randAngle = Math.PI * 2 * Rng.NextDouble();
            var randRadius = radius * Rng.NextDouble();
            var dx = (float)(Math.Cos(randAngle) * randRadius) + state.beginPos.X - Host.Self.X;
            var dy = (float)(Math.Sin(randAngle) * randRadius) + state.beginPos.Y - Host.Self.Y;
            state.angle = (float)Math.Atan2(dy, dx);
            state.remainingDist = (float)Math.Sqrt(dx * dx + dy * dy);
            return true;
        }
    }

    internal class Circling : Behavior
    {
        private class CirclingState { public float angle; public WeakReference<Entity> target; }
        private readonly float radius, sight, speed, angularSpeed;
        private readonly ushort? objType;
        private Circling(float radius, float sight, float speed, ushort? objType) { this.radius = radius; this.sight = sight; this.speed = speed; angularSpeed = speed / radius; this.objType = objType; }
        public static Circling Instance(float radius, float sight, float speed, ushort? objType) =>
            Cached(Tuple.Create(radius, sight, speed, objType), () => new Circling(radius, sight, speed, objType));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var s = speed * GetSpeedMultiplier(Host.Self);
            CirclingState state;
            if (!Host.StateStorage.TryGetValue(Key, out var o))
            {
                var dist = sight;
                var t = GetNearestEntity(ref dist, objType);
                if (t == null) return false;
                Host.StateStorage[Key] = state = new CirclingState { target = new WeakReference<Entity>(t), angle = (float)(2 * Math.PI * Rng.NextDouble()) };
            }
            else
                state = (CirclingState)o;
            state.angle += angularSpeed * (time.ElapsedMsDelta / 1000f);
            if (!state.target.TryGetTarget(out var target) || target.World == null || target.Dead)
            {
                Host.StateStorage.Remove(Key);
                return false;
            }
            MoveToward(target.X + (float)Math.Cos(state.angle) * radius, target.Y + (float)Math.Sin(state.angle) * radius, s, time);
            if (state.angle >= Math.PI * 2)
                state.angle -= (float)(Math.PI * 2);
            return true;
        }
    }

    internal class StrictCircling : Behavior
    {
        private class CirclingState { public float angle; public WeakReference<Entity> target; }
        private readonly float radius, speed, angularSpeed;
        private readonly ushort? objType;
        private StrictCircling(float radius, float speed, ushort? objType) { this.radius = radius; this.speed = speed; angularSpeed = speed / radius; this.objType = objType; }
        public static StrictCircling Instance(float radius, float speed, ushort? objType) => Cached(Tuple.Create(radius, speed, objType), () => new StrictCircling(radius, speed, objType));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            if (!Host.StateStorage.TryGetValue(Key, out var o))
            {
                var dist = radius + 1;
                var t = GetNearestEntity(ref dist, objType);
                if (t == null) return false;
                Host.StateStorage[Key] = new CirclingState { target = new WeakReference<Entity>(t), angle = (float)(2 * Math.PI * Rng.NextDouble()) };
                return true;
            }
            var state = (CirclingState)o;
            state.angle += angularSpeed * GetSpeedMultiplier(Host.Self) * (time.ElapsedMsDelta / 1000f);
            if (!state.target.TryGetTarget(out var target) || target.World == null || target.Dead)
            {
                Host.StateStorage.Remove(Key);
                return false;
            }
            ValidateAndMove(target.X + (float)Math.Cos(state.angle) * radius, target.Y + (float)Math.Sin(state.angle) * radius);
            if (state.angle >= Math.PI * 2)
                state.angle -= (float)(Math.PI * 2);
            return true;
        }
    }

    internal class MaintainDist : Behavior
    {
        private readonly float speed, radius, targetRadius;
        private readonly ushort? objType;
        private MaintainDist(float speed, float radius, float targetRadius, ushort? objType) { this.speed = speed; this.radius = radius; this.targetRadius = targetRadius; this.objType = objType; }
        public static MaintainDist Instance(float speed, float radius, float targetRadius, ushort? objType) =>
            Cached(Tuple.Create(speed, radius, targetRadius, objType), () => new MaintainDist(speed, radius, targetRadius, objType));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var s = speed * GetSpeedMultiplier(Host.Self);
            var dist = radius;
            var entity = GetNearestEntity(ref dist, objType);
            if (entity == null || dist == targetRadius)
                return false;
            var tx = entity.X + Rng.Next(-2, 2) / 2f;
            var ty = entity.Y + Rng.Next(-2, 2) / 2f;
            if (dist > targetRadius)
                MoveToward(tx, ty, s, time);
            else
                MoveToward(2 * Host.Self.X - tx, 2 * Host.Self.Y - ty, s, time); // away from it
            return true;
        }
    }

    internal class Charge : Behavior
    {
        private readonly float speed, radius;
        private readonly ushort? objType;
        private Charge(float speed, float radius, ushort? objType) { this.speed = speed; this.radius = radius; this.objType = objType; }
        public static Charge Instance(float speed, float radius, ushort? objType) => Cached(Tuple.Create(speed, radius, objType), () => new Charge(speed, radius, objType));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var s = speed * GetSpeedMultiplier(Host.Self);
            Position target;
            if (!Host.StateStorage.TryGetValue(Key, out var o))
            {
                var dist = radius;
                var entity = GetNearestEntity(ref dist, objType);
                if (entity == null) return true;
                Host.StateStorage[Key] = target = new Position(entity.X, entity.Y);
            }
            else
                target = (Position)o;
            MoveToward(target.X, target.Y, s, time);
            if (Dist(Host.Self.X, Host.Self.Y, target.X, target.Y) < 1)
            {
                Host.StateStorage.Remove(Key);
                return true;
            }
            return false;
        }
    }

    internal class ReturnSpawn : Behavior
    {
        private readonly float speed;
        private ReturnSpawn(float speed) => this.speed = speed;
        public static ReturnSpawn Instance(float speed) => Cached(speed, () => new ReturnSpawn(speed));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.HasConditionEffect(ConditionEffectIndex.Paralyzed)) return true;
            var sp = Host.Self.SpawnPoint ?? new Position(Host.Self.X, Host.Self.Y);
            if (Math.Abs(sp.X - Host.Self.X) <= 1 && Math.Abs(sp.Y - Host.Self.Y) <= 1)
                return false;
            MoveToward(sp.X, sp.Y, speed * GetSpeedMultiplier(Host.Self), time);
            return true;
        }
    }

    #endregion

    #region Spawning, effects, taunts, appearance

    internal class SpawnMinionImmediate : Behavior
    {
        private readonly ushort? objType;
        private readonly float radius;
        private readonly int minCount, maxCount;
        private SpawnMinionImmediate(ushort? objType, float radius, int minCount, int maxCount) { this.objType = objType; this.radius = radius; this.minCount = minCount; this.maxCount = maxCount; }
        public static SpawnMinionImmediate Instance(ushort? objType, float radius, int minCount, int maxCount) =>
            Cached(Tuple.Create(objType, radius, minCount, maxCount), () => new SpawnMinionImmediate(objType, radius, minCount, maxCount));

        protected override bool TickCore(TickTime time)
        {
            var count = Rng.Next(minCount, maxCount + 1);
            for (var i = 0; i < count; i++)
                DoomedRuntime.Spawn(Host.Self, objType, Host.Self.X + (float)(Rng.NextDouble() * 2 - 1) * radius, Host.Self.Y + (float)(Rng.NextDouble() * 2 - 1) * radius);
            return true;
        }
    }

    internal class SpawnRoot : Behavior
    {
        private readonly ushort? objType;
        private readonly float radius, targetRadius;
        private SpawnRoot(ushort? objType, float radius, float targetRadius) { this.objType = objType; this.radius = radius; this.targetRadius = targetRadius; }
        public static SpawnRoot Instance(ushort? objType, float radius, float targetRadius) => Cached(Tuple.Create(objType, radius, targetRadius), () => new SpawnRoot(objType, radius, targetRadius));

        protected override bool TickCore(TickTime time)
        {
            foreach (var i in GetNearestEntities(radius, null).ToArray())
                if (!i.HasConditionEffect(ConditionEffectIndex.Paralyzed))
                    DoomedRuntime.Spawn(Host.Self, objType, i.X + (float)(Rng.NextDouble() * 2 - 1) * targetRadius, i.Y + (float)(Rng.NextDouble() * 2 - 1) * targetRadius);
            return true;
        }
    }

    internal class SpawnMinion : Behavior
    {
        private readonly ushort? objType;
        private readonly float radius;
        private readonly int maxCount, minTick, maxTick;
        private SpawnMinion(ushort? objType, float radius, int maxCount, int minTick, int maxTick) { this.objType = objType; this.radius = radius; this.maxCount = maxCount; this.minTick = minTick; this.maxTick = maxTick; }
        public static SpawnMinion Instance(ushort? objType, float radius, int maxCount, int minTick, int maxTick) =>
            Cached(Tuple.Create(objType, radius, maxCount, minTick, maxTick), () => new SpawnMinion(objType, radius, maxCount, minTick, maxTick));

        protected override bool TickCore(TickTime time)
        {
            var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : 0;
            remaining -= time.ElapsedMsDelta;
            var ret = false;
            if (remaining <= 0)
            {
                if (CountEntity(radius, objType) < maxCount)
                    DoomedRuntime.Spawn(Host.Self, objType, Host.Self.X, Host.Self.Y);
                remaining = Rng.Next(minTick, maxTick);
                ret = true;
            }
            Host.StateStorage[Key] = remaining;
            return ret;
        }
    }

    internal class Reproduce : Behavior
    {
        private readonly ushort? objType;
        private readonly int maxCount, min, max;
        private readonly float radius;
        private Reproduce(ushort? objType, int maxCount, int cooldown, float radius) { this.objType = objType; this.maxCount = maxCount; this.radius = radius; min = (int)(cooldown * 0.95); max = (int)(cooldown * 1.05); }
        public static Reproduce Instance(ushort? objType, int maxCount, int cooldown = 10000, float radius = 5) =>
            Cached(Tuple.Create(objType, maxCount, cooldown, radius), () => new Reproduce(objType, maxCount, cooldown, radius));

        protected override bool TickCore(TickTime time)
        {
            var remaining = Host.StateStorage.TryGetValue(Key, out var o) ? (int)o : Rng.Next(min, max);
            remaining -= time.ElapsedMsDelta;
            var ret = false;
            if (remaining <= 0)
            {
                if (CountEntity(radius, objType) < maxCount)
                {
                    float x = Host.Self.X, y = Host.Self.Y;
                    if (radius != 5)
                    {
                        var angle = Rng.NextDouble() * 2 * Math.PI;
                        x += (float)(radius * 0.5 * Math.Cos(angle));
                        y += (float)(radius * 0.5 * Math.Sin(angle));
                        if (!Host.Self.World.IsPassable(x, y))
                        {
                            x = Host.Self.X;
                            y = Host.Self.Y;
                        }
                    }
                    DoomedRuntime.Spawn(Host.Self, objType, x, y);
                }
                remaining = Rng.Next(min, max);
                ret = true;
            }
            Host.StateStorage[Key] = remaining;
            return ret;
        }
    }

    internal class Transmute : Behavior
    {
        private readonly ushort? objType;
        private readonly int minCount, maxCount;
        public Transmute(ushort? objType, int minCount = 1, int maxCount = 1) { this.objType = objType; this.minCount = minCount; this.maxCount = maxCount; }

        protected override bool TickCore(TickTime time)
        {
            var c = Rng.Next(minCount, maxCount + 1);
            for (var i = 0; i < c; i++)
                DoomedRuntime.Spawn(Host.Self, objType, Host.Self.X, Host.Self.Y);
            Host.Self.World.LeaveWorld(Host.Self);
            return true;
        }
    }

    // Paints one of Doomed Realms' small ground set pieces at the monster ((int)X - offset,
    // (int)Y - offset at the top left), on tiles without an object. Only the Abyss lava bomb uses
    // them: lava spreading from its tile and drawing back (TempLava1 to 8, as rows of the pattern,
    // 1 the first ground and 2 the second).
    internal class MonsterSetPiece : Behavior
    {
        private static readonly Dictionary<string, (string Tile1, string Tile2, string[] Rows)> Pieces = new Dictionary<string, (string, string, string[])>
        {
            ["TempLava1"] = ("Lava Blend", null, new[] { "1" }),
            ["TempLava2"] = ("Lava Blend", null, new[] { ".1.", "111", ".1." }),
            ["TempLava3"] = ("Lava Blend", null, new[] { "..1..", ".111.", "11111", ".111.", "..1.." }),
            ["TempLava4"] = ("Lava Blend", null, new[] { ".111.", "11111", "11111", "11111", ".111." }),
            ["TempLava5"] = ("Lava Blend", "Red Quad", new[] { ".212.", "21112", "11111", "21112", ".212." }),
            ["TempLava6"] = ("Lava Blend", "Red Quad", new[] { "..2..", ".212.", "21112", ".212.", "..2.." }),
            ["TempLava7"] = ("Lava Blend", "Red Quad", new[] { ".2.", "212", ".2." }),
            ["TempLava8"] = ("Red Quad", null, new[] { "1" }),
        };

        private readonly string piece;
        private readonly int offset;
        private MonsterSetPiece(string piece, int offset) { this.piece = piece; this.offset = offset; }
        public static MonsterSetPiece Instance(string piece, int offset) => Cached(Tuple.Create(piece, offset), () => new MonsterSetPiece(piece, offset));

        protected override bool TickCore(TickTime time)
        {
            var world = Host.Self.World;
            if (world is DoomedBattleArena || !Pieces.TryGetValue(piece, out var p))
                return false;
            var dat = Host.Self.GameServer.Resources.GameData;
            ushort? Ground(string id) => id != null && dat.IdToTileType.TryGetValue(id, out var t) ? t : (ushort?)null;
            var grounds = new[] { Ground(p.Tile1), Ground(p.Tile2) };
            int x0 = (int)Host.Self.X - offset, y0 = (int)Host.Self.Y - offset;
            for (var y = 0; y < p.Rows.Length; y++)
                for (var x = 0; x < p.Rows[y].Length; x++)
                {
                    var c = p.Rows[y][x];
                    if (c != '1' && c != '2' || !world.Map.Contains(x0 + x, y0 + y))
                        continue;
                    var ground = grounds[c - '1'];
                    var tile = world.Map[x0 + x, y0 + y];
                    if (ground == null || tile.ObjType != 0 || tile.TileId == ground.Value)
                        continue;
                    tile.TileId = ground.Value;
                    tile.UpdateCount++;
                }
            return true;
        }
    }

    internal class Flashing : Behavior
    {
        private readonly int time;
        private readonly uint color;
        private Flashing(int time, uint color) { this.time = time; this.color = color; }
        public static Flashing Instance(int time, uint color) => Cached(Tuple.Create(time, color), () => new Flashing(time, color));

        protected override bool TickCore(TickTime t)
        {
            var left = Host.StateStorage.TryGetValue(Key, out var obj) ? (int)obj : time;
            bool ret;
            if (left == time)
            {
                Effect(EffectType.Flashing, Host.Self, color, new Position(time / 1000f, 1));
                ret = false;
                left -= t.ElapsedMsDelta;
            }
            else if (left < 0)
            {
                ret = true;
                left = time;
            }
            else
            {
                ret = false;
                left -= t.ElapsedMsDelta;
            }
            Host.StateStorage[Key] = left;
            return ret;
        }
    }

    internal class SetConditionEffect : Behavior
    {
        private readonly ConditionEffectIndex eff;
        private SetConditionEffect(ConditionEffectIndex eff) => this.eff = eff;
        public static SetConditionEffect Instance(ConditionEffectIndex eff) => Cached(eff, () => new SetConditionEffect(eff));
        protected override bool TickCore(TickTime time) { Host.Self.ApplyConditionEffect(eff, -1); return true; }
    }

    internal class UnsetConditionEffect : Behavior
    {
        private readonly ConditionEffectIndex eff;
        private UnsetConditionEffect(ConditionEffectIndex eff) => this.eff = eff;
        public static UnsetConditionEffect Instance(ConditionEffectIndex eff) => Cached(eff, () => new UnsetConditionEffect(eff));
        protected override bool TickCore(TickTime time) { Host.Self.RemoveCondition(eff); return true; }
    }

    internal class SetConditionEffectTimed : Behavior
    {
        private readonly ConditionEffectIndex eff;
        private readonly int time;
        public SetConditionEffectTimed(ConditionEffectIndex eff, int time) { this.eff = eff; this.time = time; }
        protected override bool TickCore(TickTime t) { Host.Self.ApplyConditionEffect(eff, time); return true; }
    }

    internal class SimpleTaunt : Behavior
    {
        private readonly string taunt;
        public SimpleTaunt(string taunt) => this.taunt = taunt;
        protected override bool TickCore(TickTime time) { Taunt(taunt); return true; }
    }

    internal class RandomTaunt : Behavior
    {
        private readonly double prob;
        private readonly string taunt;
        public RandomTaunt(double prob, string taunt) { this.prob = prob; this.taunt = taunt; }

        protected override bool TickCore(TickTime time)
        {
            if (Rng.NextDouble() > prob) return false;
            Taunt(taunt);
            return true;
        }
    }

    internal class SetSize : Behavior
    {
        private readonly int size;
        private SetSize(int size) => this.size = size;
        public static SetSize Instance(int size) => Cached(size, () => new SetSize(size));
        protected override bool TickCore(TickTime time) { if (Host.Self.Size != size) Host.Self.Size = size; return true; }
    }

    internal class GrowSize : Behavior
    {
        private readonly int rate, target;
        private GrowSize(int rate, int target) { this.rate = rate; this.target = target; }
        public static GrowSize Instance(int rate, int target) => Cached(Tuple.Create(rate, target), () => new GrowSize(rate, target));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.Size >= target) return false;
            Host.Self.Size += rate / 2;
            return true;
        }
    }

    internal class ShrinkSize : Behavior
    {
        private readonly int rate, target;
        private ShrinkSize(int rate, int target) { this.rate = rate; this.target = target; }
        public static ShrinkSize Instance(int rate, int target) => Cached(Tuple.Create(rate, target), () => new ShrinkSize(rate, target));

        protected override bool TickCore(TickTime time)
        {
            if (Host.Self.Size <= target) return false;
            Host.Self.Size -= rate / 2;
            return true;
        }
    }

    internal class SetAltTexture : Behavior
    {
        private readonly int index;
        private SetAltTexture(int index) => this.index = index;
        public static SetAltTexture Instance(int index) => Cached(index, () => new SetAltTexture(index));
        protected override bool TickCore(TickTime time) { if (Host.Self.AltTextureIndex != index) Host.Self.AltTextureIndex = index; return true; }
    }

    internal class OrderGroup : Behavior
    {
        private readonly float radius;
        private readonly string group;
        private readonly Behavior behav;
        private OrderGroup(float radius, string group, Behavior behav) { this.radius = radius; this.group = group; this.behav = behav; }
        public static OrderGroup Instance(float radius, string group, Behavior behav) => Cached(Tuple.Create(radius, group, behav), () => new OrderGroup(radius, group, behav));

        protected override bool TickCore(TickTime time)
        {
            var ret = false;
            foreach (var i in GetNearestEntitiesByGroup(radius, group).ToArray())
                ret |= behav.Tick(i, time);
            return ret;
        }
    }

    internal class OrderEntity : Behavior
    {
        private readonly float radius;
        private readonly ushort? objType;
        private readonly Behavior behav;
        private OrderEntity(float radius, ushort? objType, Behavior behav) { this.radius = radius; this.objType = objType; this.behav = behav; }
        public static OrderEntity Instance(float radius, ushort? objType, Behavior behav) => Cached(Tuple.Create(radius, objType, behav), () => new OrderEntity(radius, objType, behav));

        protected override bool TickCore(TickTime time)
        {
            var d = radius;
            var x = GetNearestEntity(ref d, objType);
            return x != null && behav.Tick(x, time);
        }
    }

    internal class OrderAllEntity : Behavior
    {
        private readonly float radius;
        private readonly ushort? objType;
        private readonly Behavior behav;
        private OrderAllEntity(float radius, ushort? objType, Behavior behav) { this.radius = radius; this.objType = objType; this.behav = behav; }
        public static OrderAllEntity Instance(float radius, ushort? objType, Behavior behav) => Cached(Tuple.Create(radius, objType, behav), () => new OrderAllEntity(radius, objType, behav));

        protected override bool TickCore(TickTime time)
        {
            var ret = false;
            foreach (var i in GetNearestEntities(radius, objType).ToArray())
            {
                ret = true;
                behav.Tick(i, time);
            }
            return ret;
        }
    }

    #endregion

    #region Loot (converted to betterSkillys drops)

    public interface ILoot
    {
        IEnumerable<BL.MobDrops> ToDrops(double probability, double threshold);
    }

    internal class TierLoot : ILoot
    {
        private readonly byte tier;
        private readonly ItemType type;
        public TierLoot(byte tier, ItemType type) { this.tier = tier; this.type = type; }
        public IEnumerable<BL.MobDrops> ToDrops(double p, double threshold) { yield return new BL.TierLoot(tier, type, p, 0, threshold); }
    }

    internal class ItemLoot : ILoot
    {
        private readonly string item;
        public ItemLoot(string item) => this.item = item;

        public IEnumerable<BL.MobDrops> ToDrops(double p, double threshold)
        {
            if (BehaviorDb.InitGameData != null && BehaviorDb.InitGameData.IdToObjectType.ContainsKey(item))
                yield return new BL.ItemLoot(item, p, 0, threshold);
        }
    }

    internal class HpPotionLoot : ItemLoot
    {
        public static readonly HpPotionLoot Instance = new HpPotionLoot();
        private HpPotionLoot() : base("Health Potion") { }
    }

    internal class MpPotionLoot : ItemLoot
    {
        public static readonly MpPotionLoot Instance = new MpPotionLoot();
        private MpPotionLoot() : base("Magic Potion") { }
    }

    // a health or a magic potion
    internal class PotionLoot : ILoot
    {
        public static readonly PotionLoot Instance = new PotionLoot();

        public IEnumerable<BL.MobDrops> ToDrops(double p, double threshold) =>
            HpPotionLoot.Instance.ToDrops(p / 2, threshold).Concat(MpPotionLoot.Instance.ToDrops(p / 2, threshold));
    }

    public enum StatPotion { Att, Def, Spd, Vit, Wis, Dex, Life, Mana }

    internal class StatPotionLoot : ItemLoot
    {
        internal static readonly Dictionary<StatPotion, string> Names = new Dictionary<StatPotion, string>
        {
            [StatPotion.Att] = "Potion of Attack", [StatPotion.Def] = "Potion of Defense", [StatPotion.Spd] = "Potion of Speed",
            [StatPotion.Vit] = "Potion of Vitality", [StatPotion.Wis] = "Potion of Wisdom", [StatPotion.Dex] = "Potion of Dexterity",
            [StatPotion.Life] = "Potion of Life", [StatPotion.Mana] = "Potion of Mana"
        };

        public StatPotionLoot(StatPotion pot) : base(Names[pot]) { }
    }

    internal class StatPotionsLoot : ILoot
    {
        private static readonly StatPotion[][] Tiers =
        {
            new[] { StatPotion.Att, StatPotion.Def, StatPotion.Spd },
            new[] { StatPotion.Vit, StatPotion.Wis, StatPotion.Dex },
            new[] { StatPotion.Life, StatPotion.Mana }
        };

        private readonly StatPotion[] pots;
        public StatPotionsLoot(params int[] tiers) => pots = tiers.SelectMany(t => Tiers[t - 1]).Distinct().ToArray();

        // one of them at random: each with an equal share of the chance
        public IEnumerable<BL.MobDrops> ToDrops(double p, double threshold) =>
            pots.SelectMany(pot => new StatPotionLoot(pot).ToDrops(p / pots.Length, threshold));
    }

    internal class LootDef
    {
        public static readonly LootDef Empty = new LootDef(0, 0, 0, 0);
        public readonly Tuple<double, ILoot>[] Loots;

        public LootDef(int baseLootCount, int personMult, int minLootCount, int maxLootCount, params Tuple<double, ILoot>[] loots) => Loots = loots;
    }

    internal class LootBehavior
    {
        public readonly LootDef PublicBag;
        public readonly Tuple<int, LootDef>[] SoulBag;

        public LootBehavior(LootDef pub, params Tuple<int, LootDef>[] soul)
        {
            PublicBag = pub ?? LootDef.Empty;
            SoulBag = soul ?? new Tuple<int, LootDef>[0];
        }

        // Each entry drops with its own chance; soulbound bags need the player to have dealt the
        // Doomed Realms damage threshold, expressed as a share of the monster's health.
        public BL.MobDrops[] ToDrops(int maxHp)
        {
            var drops = new List<BL.MobDrops>();
            foreach (var l in PublicBag.Loots)
                drops.AddRange(l.Item2.ToDrops(l.Item1, 0));
            foreach (var soul in SoulBag)
            {
                var threshold = maxHp > 0 ? Math.Min(0.25, Math.Max(0.001, soul.Item1 / (double)maxHp)) : 0.01;
                foreach (var l in soul.Item2.Loots)
                    drops.AddRange(l.Item2.ToDrops(l.Item1, threshold));
            }
            return drops.ToArray();
        }
    }

    #endregion
}
