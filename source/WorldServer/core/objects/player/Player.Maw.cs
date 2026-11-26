using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The Sharklord: Blood in the Water, the Frenzy and the Maw of the Deep.
    //
    // Blood in the Water: to a Sharklord an enemy under half its health is Bloodied, and every
    // shot of theirs that strikes one does BloodiedPercent more (after defence; the Rust client
    // works out the same for its own shots, combat.rs, and drips blood over the Bloodied it sees).
    //
    // The Frenzy: every FrenzyBloodHits-th shot that lands on a Bloodied enemy, every enemy the
    // Sharklord hurt that dies (by anyone's hand), and the Maw's bites (below) work them into a
    // Frenzy, up to FrenzyMax. Each stack is FrenzySpeed Speed and FrenzyDex Dexterity (a boost, so
    // the Speed and Dexterity stats carry it). FrenzyHoldMs after the last new stack they calm, one
    // every FrenzyFadeStepMs. The count is the Frenzy stat, sent to everyone: the Rust client draws
    // the fins circling the Sharklord (particles/maw.rs).
    //
    // The Maw of the Deep (<Activate maxDistance radius totalDamage impactDamage amount>DeepMaw</Activate>,
    // the Sharklord's oceanic spells): a fin cuts through the ground from the Sharklord to where
    // they aim (at most `maxDistance` tiles), striking every enemy within FinPathWidth of its way
    // for `totalDamage`; where it ends a shark bursts up out of a whirlpool and bites everything
    // within `radius` for `impactDamage`, twice that to the Bloodied. The Sharklord feeds: `amount`
    // health for each enemy bitten, at most FeedMax of them, and a stack of Frenzy for each, at
    // most BiteFrenzyMax.
    //
    // The Megalodon: cast with a full Frenzy, it's all spent and the shark that rises is a
    // Megalodon: MegalodonPercent of the radius and the bite, twice the feeding, and everything
    // bitten Bleeds (MegalodonBleedPercent of the bite more over MegalodonBleedMs) and is Slowed.
    // Keep the Frenzy and its speed, or spend it on the Megalodon.
    //
    // Its looks are the Rust client's (particles/maw.rs): a ShowEffect Relic with the look in
    // pos2.y, the spell's tier in ObjectType (16 more for a Megalodon), its colour (MawColors) and
    // the Sharklord in SourceId.
    public partial class Player
    {
        public const ushort SharklordType = 0xD24E;
        public const int LOOK_MAW = 70;    // the fin from the target (the Sharklord) to pos1 over Duration, the bite there pos2.x across
        public const int LOOK_FEED = 71;   // the Sharklord (the target) feeding on pos2.x enemies bitten round pos1
        public const int FrenzyMax = 5;
        public const int FrenzySpeed = 4, FrenzyDex = 4;
        public const int FrenzyBloodHits = 4;
        public const int FrenzyHoldMs = 5000, FrenzyFadeStepMs = 1000;
        public const int BloodiedPercent = 20;
        public const float FinPathWidth = 1.0f, FinSpeed = 24f;   // tiles a second
        public const int FinMinMs = 120, FinMaxMs = 420;
        public const int FeedMax = 3, BiteFrenzyMax = 2;
        public const int MegalodonPercent = 150, MegalodonBleedPercent = 50, MegalodonBleedMs = 3000, MegalodonSlowMs = 2500;

        // the spells' colours by tier, the water the shark rises from: the reef's green, the
        // shallows' teal, the open sea's aqua, blue, deep blue, the mako's bright cyan, the great
        // white's foam, the abyss's violet, the Megalodon Tooth's old blood; maw.rs knows them too
        public static readonly uint[] MawColors =
        {
            0xFF8FE0C8, 0xFF5CD6C8, 0xFF48C8E8, 0xFF3CA0F0, 0xFF3070E0,
            0xFF40E0FF, 0xFFE0F4FF, 0xFF8C5CFF, 0xFFFF3C50,
        };

        public bool IsSharklord => ObjectType == SharklordType;

        private StatTypeValue<int> _frenzy;
        public int Frenzy
        {
            get => _frenzy.GetValue();
            private set
            {
                _frenzy.SetValue(Math.Clamp(value, 0, FrenzyMax));
                FrenzyBoost();
            }
        }

        private int _frenzyBoosted;
        private int _frenzyShot = -1, _frenzyBloodHits;
        private long _frenzyGained, _frenzyFadedAt;

        // Bloodied to a Sharklord: under half its health.
        public static bool Bloodied(int health, int maxHealth) => maxHealth > 0 && health > 0 && health * 2 < maxHealth;

        // A Sharklord's shot on an enemy with `health` of `maxHealth`: `dmg` (after defence), more if it's Bloodied.
        public static int BloodiedDamage(int dmg, int health, int maxHealth) =>
            dmg > 0 && Bloodied(health, maxHealth) ? dmg * (100 + BloodiedPercent) / 100 : dmg;

        // The Frenzy's Speed and Dexterity, kept as a boost (BoostStatManager): moved by the change only.
        private void FrenzyBoost()
        {
            var now = _frenzy.GetValue();
            var delta = now - _frenzyBoosted;
            if (delta == 0 || Stats?.Boost == null)
                return;
            _frenzyBoosted = now;
            Stats.Boost.ActivateBoost[4].AddOffset(delta * FrenzySpeed);
            Stats.Boost.ActivateBoost[5].AddOffset(delta * FrenzyDex);
            Stats.ReCalculateValues();
        }

        private void GainFrenzy(int n = 1)
        {
            if (!IsSharklord || n <= 0)
                return;
            _frenzyGained = Now;
            if (Frenzy < FrenzyMax)
                Frenzy += n;
        }

        // one of the Sharklord's shots (`bulletId`) struck an enemy that was Bloodied
        private void FrenzyBloodHit(int bulletId)
        {
            if (!IsSharklord || bulletId == _frenzyShot)
                return;
            _frenzyShot = bulletId;
            _frenzyGained = Now;
            if (++_frenzyBloodHits < FrenzyBloodHits)
                return;
            _frenzyBloodHits = 0;
            GainFrenzy();
        }

        // an enemy this Sharklord hurt fell (Enemy.Death)
        internal void FrenzyKill() => GainFrenzy();

        private void FrenzyTick()
        {
            if (Frenzy <= 0)
                return;
            var now = Now;
            if (now - _frenzyGained < FrenzyHoldMs || now - _frenzyFadedAt < FrenzyFadeStepMs)
                return;
            _frenzyFadedAt = now;
            Frenzy--;
        }

        // How long the fin takes to reach a spot `dist` tiles off.
        public static int FinMs(float dist) => Math.Clamp((int)(dist / FinSpeed * 1000), FinMinMs, FinMaxMs);

        // The bite on one enemy: `bite`, twice it on the Bloodied.
        public static int BiteDamage(int bite, int health, int maxHealth) => Bloodied(health, maxHealth) ? bite * 2 : bite;

        // How much a bite of `bitten` enemies feeds the Sharklord (`amount` each, at most FeedMax; twice for a Megalodon).
        public static int FeedHeal(int amount, int bitten, bool megalodon) =>
            Math.Max(0, amount) * Math.Clamp(bitten, 0, FeedMax) * (megalodon ? 2 : 1);

        // The look's ObjectType: the tier, 16 more for a Megalodon.
        public static ushort MawLookCode(int tier, bool megalodon) => (ushort)(Math.Clamp(tier, 0, 15) + (megalodon ? 16 : 0));

        private void AEDeepMaw(TickTime time, Item item, Position target, ActivateEffect eff)
        {
            var world = World;
            if (world == null)
                return;
            var from = new Position(X, Y);
            var spot = ClampAbilityTarget(target, eff.MaximumDistance > 0 ? eff.MaximumDistance : 8);
            var tier = Math.Clamp(item.Tier, 0, MawColors.Length - 1);
            var color = MawColors[tier];
            var megalodon = Frenzy >= FrenzyMax;
            if (megalodon)
            {
                Frenzy = 0;
                _frenzyBloodHits = 0;
            }
            var radius = (eff.Radius > 0 ? eff.Radius : 3) * (megalodon ? MegalodonPercent / 100f : 1f);
            var bite = eff.ImpactDamage * (megalodon ? MegalodonPercent : 100) / 100;
            var (dx, dy) = (spot.X - from.X, spot.Y - from.Y);
            var len = MathF.Sqrt(dx * dx + dy * dy);
            var ms = FinMs(len);

            world.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic,
                TargetObjectId = Id,
                Pos1 = spot,
                Pos2 = new Position(radius, LOOK_MAW),
                Color = new ARGB(color),
                Duration = ms,
                ObjectType = MawLookCode(tier, megalodon),
                SourceId = Id,
            }, ref from);

            // the fin's way: every enemy beside it, struck as it passes
            if (len > 0.3f && eff.TotalDamage > 0)
                foreach (var e in EnemiesNear(from.X + dx / 2, from.Y + dy / 2, len / 2 + FinPathWidth))
                {
                    if (!TidePearl.OnWay(from.X, from.Y, spot.X, spot.Y, e.X, e.Y, FinPathWidth, out var u))
                        continue;
                    var enemy = e;
                    var damage = eff.TotalDamage;
                    After(Math.Max(1, (int)(u * ms)), (w, t) => RelicDamage(enemy, damage, ref t, false));
                }

            // the bite
            var amount = eff.Amount;
            After(ms, (w, t) =>
            {
                if (World != w || Dead)
                    return;
                var bitten = 0;
                foreach (var e in EnemiesNear(spot.X, spot.Y, radius))
                {
                    bitten++;
                    var dmg = BiteDamage(bite, e.Health, e.MaxHealth);
                    RelicDamage(e, dmg, ref t, false);
                    if (megalodon && !e.Dead)
                    {
                        e.Bleed(this, dmg * MegalodonBleedPercent / 100, MegalodonBleedMs);
                        e.ApplyConditionEffect(ConditionEffectIndex.Slowed, MegalodonSlowMs);
                    }
                }
                if (bitten == 0)
                    return;
                w.BroadcastIfVisible(new ShowEffect
                {
                    EffectType = EffectType.Relic,
                    TargetObjectId = Id,
                    Pos1 = spot,
                    Pos2 = new Position(Math.Min(bitten, FeedMax), LOOK_FEED),
                    Color = new ARGB(color),
                    ObjectType = MawLookCode(tier, megalodon),
                    SourceId = Id,
                }, ref spot);
                ActivateHealHp(this, FeedHeal(amount, bitten, megalodon));
                GainFrenzy(Math.Min(bitten, BiteFrenzyMax));
            });
        }
    }
}
