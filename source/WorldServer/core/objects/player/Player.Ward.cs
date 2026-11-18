using System;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.net.stats;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // The Father's ward (his censers, Activate Ward): a grey shield over the HP bar. Every point of
    // damage that gets past defense and any forcefield comes off it before health, until it's spent
    // (it breaks) or its time is up (it fades). A ward raised over one still up keeps the stronger,
    // and its time starts again. Damage over time (bleeding, suffocation) goes through it.
    //
    // Everyone sees it: the stats Ward (points left) and WardMax (points when raised), which the
    // Rust client draws as a grey bar over the HP bar and a shell of steel plates round the player,
    // and its looks (ShowEffect Relic, client-rs/app/src/particles/relic.rs, 33 to 38):
    //   33 the censer swung: smoke and chains of light out to its range, at the Father
    //   34 a player warded: plates of grey steel flying in and locking round them
    //   35 a blow turned: the shell rippling, sparks off it (ObjectType: the percent left)
    //   36 broken: the shell shattering
    //   37 faded: the shell dissolving back into smoke
    //   38 the Censer of the Last Rite's ward breaking: its shards bursting out (radius in pos2.x)
    // The colour of each is the censer's tier's (ACCENTS), what glints in the grey.
    public partial class Player
    {
        public const int LOOK_WARD_CAST = 33;
        public const int LOOK_WARD_GAIN = 34;
        public const int LOOK_WARD_HIT = 35;
        public const int LOOK_WARD_BREAK = 36;
        public const int LOOK_WARD_FADE = 37;
        public const int LOOK_LAST_RITE = 38;

        // each tier's glints in the grey (the censer's Tier, 0 to 8), which the Rust client also knows
        // them by (relic.rs, `ward_style`): tin, iron, pewter, silver, the Cathedral's gold, the
        // Bulwark's sapphire, the Vigil's dawn, the Iron Choir's embers and the Last Rite's violet fire
        public static readonly uint[] WARD_ACCENTS =
        {
            0xFFA8A49A, 0xFF7C8288, 0xFF9AA6B4, 0xFFDCE4EE, 0xFFE8C860,
            0xFF6CA8FF, 0xFFFFF0C8, 0xFFFFA040, 0xFFC8A0FF,
        };

        public static uint WardAccent(int tier) => tier >= 0 && tier < WARD_ACCENTS.Length ? WARD_ACCENTS[tier] : WARD_ACCENTS[3];

        private StatTypeValue<int> _ward;
        private StatTypeValue<int> _wardMax;

        public int Ward
        {
            get => _ward.GetValue();
            private set => _ward.SetValue(value);
        }

        public int WardMax
        {
            get => _wardMax.GetValue();
            private set => _wardMax.SetValue(value);
        }

        private long _wardUntil;
        private long _wardFxAt;
        private uint _wardColor = WARD_ACCENTS[0];
        // the Last Rite: what its ward does to the enemies round it when it breaks, and who raised it
        private int _wardBurst;
        private float _wardBurstRadius;
        private Player _wardBy;
        private bool _wardBursting;

        // a censer swung: a ward of `amount` (Wisdom adds to it, as a tome's heal) for `duration`
        // over the Father and every ally within `range`
        private void AEWard(Item item, ActivateEffect eff)
        {
            var amount = eff.UseWisMod ? (int)UseWisMod(eff.Amount, 0) : eff.Amount;
            var ms = eff.DurationMS > 0 ? eff.DurationMS : 5000;
            var color = WardAccent(item.Tier);
            var at = new Position(X, Y);
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at,
                Pos2 = new Position(eff.Range, LOOK_WARD_CAST), Color = new ARGB(color),
                Duration = ms, ObjectType = (ushort)Math.Max(0, item.Tier), SourceId = Id,
            }, ref at);
            // (and for older clients, which don't know the look: the plain blast a tome makes)
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.AreaBlast, TargetObjectId = Id,
                Color = new ARGB(0xFFB8BCC4), Pos1 = new Position { X = eff.Range },
            }, this);

            this.AOE(eff.Range, true, entity =>
            {
                if (entity is not Player player || player.Dead)
                    return;
                player.GiveWard(this, amount, ms, color, item.Tier, eff.TotalDamage, eff.Radius);
                if (player != this)
                    player.BuffedBy(this, ms);
            });
        }

        // warded by `by`: the stronger of this ward and one still up, its time from now
        public void GiveWard(Player by, int amount, int ms, uint color, int tier, int burst = 0, float burstRadius = 0)
        {
            if (amount <= 0 || World == null)
                return;
            var now = Environment.TickCount64;
            if (Ward <= 0 || amount >= Ward)
            {
                Ward = amount;
                WardMax = amount;
                _wardColor = color;
                _wardBurst = burst;
                _wardBurstRadius = burstRadius > 0 ? burstRadius : 3;
                _wardBy = by;
            }
            _wardUntil = now + ms;
            var at = new Position(X, Y);
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at,
                Pos2 = new Position(0.9f, LOOK_WARD_GAIN), Color = new ARGB(_wardColor),
                Duration = ms, ObjectType = (ushort)Math.Max(0, tier), SourceId = by.Id,
            }, ref at);
        }

        // what of a hit of `dmg` gets through the ward to health
        public int WardAbsorb(int dmg)
        {
            if (dmg <= 0 || Ward <= 0)
                return dmg;
            var now = Environment.TickCount64;
            if (now >= _wardUntil)
                return dmg;
            var taken = Math.Min(Ward, dmg);
            Ward -= taken;
            dmg -= taken;
            if (Ward <= 0)
            {
                WardFx(LOOK_WARD_BREAK, 1.1f, 0);
                if (_wardBurst > 0)
                    _wardBursting = true;
                WardMax = 0;
            }
            else if (now >= _wardFxAt)
            {
                // the shell ripples round the player it turned a blow from, at most every 200 ms
                _wardFxAt = now + 200;
                WardFx(LOOK_WARD_HIT, 0.9f, WardMax > 0 ? Ward * 100 / WardMax : 0);
            }
            return dmg;
        }

        private void WardFx(int look, float radius, int value)
        {
            if (World == null)
                return;
            var at = new Position(X, Y);
            World.BroadcastIfVisible(new ShowEffect
            {
                EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at,
                Pos2 = new Position(radius, look), Color = new ARGB(_wardColor),
                ObjectType = (ushort)Math.Clamp(value, 0, ushort.MaxValue), SourceId = Id,
            }, ref at);
        }

        // each tick: a ward whose time is up fades, and the Last Rite's broken ward bursts (here, not
        // in the hit that broke it, so enemies aren't hurt in the middle of handling a shot)
        private void WardTick(ref TickTime time)
        {
            if (_wardBursting)
            {
                _wardBursting = false;
                var from = _wardBy != null && _wardBy.World == World ? _wardBy : this;
                var dmg = _wardBurst;
                var radius = _wardBurstRadius;
                var at = new Position(X, Y);
                World.BroadcastIfVisible(new ShowEffect
                {
                    EffectType = EffectType.Relic, TargetObjectId = Id, Pos1 = at,
                    Pos2 = new Position(radius, LOOK_LAST_RITE), Color = new ARGB(_wardColor), SourceId = from.Id,
                }, ref at);
                var t = time;
                this.AOE(radius, false, entity =>
                {
                    if (entity is Enemy e)
                        e.Damage(from, ref t, dmg, false);
                });
                _wardBurst = 0;
            }
            if (Ward > 0 && Environment.TickCount64 >= _wardUntil)
            {
                WardFx(LOOK_WARD_FADE, 1.0f, 0);
                Ward = 0;
                WardMax = 0;
                _wardBurst = 0;
            }
        }
    }
}
