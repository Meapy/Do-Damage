using System.Collections.Generic;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // The Ascendant's halos: a forcefield left where it was cast, in its halo's colour (COLORS). Every player standing in
    // it takes `percent` off each hit (shots, ground, anything else: Player.ShieldedDamage) until it
    // fades. Uses the Trap object (0x0711) as an invisible anchor, as the healing halo it replaced
    // did.
    //
    // Its look is the Rust client's (client-rs/app/src/particles/relic.rs, looks 30 to 32): a
    // ShowEffect Relic with the radius in pos2.x, the time left in Duration and the percent in
    // ObjectType (the client takes it off the numbers it shows for our own hits). Players who come
    // into view while it's up are sent it then (look 32, without the burst of its casting). Older
    // clients, which don't know those looks, see a golden ring pulse at its edge.
    internal class Forcefield : StaticObject
    {
        public const int LOOK_CAST = 30;     // raised: a column of light, shockwaves, then the dome
        public const int LOOK_BLOCK = 31;    // a player in it hit: the field flaring round them
        public const int LOOK_SEEN = 32;     // already up when a player came into view
        public const uint COLOR = 0xFFFFD34A;

        // each tier's field (the halo's Tier, 0 to 8), which the Rust client also knows them by
        // (relic.rs, `field_style`): Worn iron, Rusty, Bronze, Silver, Golden, Eternal Peace's jade,
        // God's Blessing's radiance, the Seraphim's flame and the Archangel's violet
        public static readonly uint[] COLORS =
        {
            0xFF8E949A, 0xFFC0602A, 0xFFD08A3C, 0xFFCFDCEB, 0xFFFFD34A,
            0xFF8CF0C8, 0xFFFFF0A8, 0xFFFF6A28, 0xFFB27CFF,
        };

        public static uint ColorOf(int tier) => tier >= 0 && tier < COLORS.Length ? COLORS[tier] : COLOR;

        // how long a player stays shielded after the field last found them in it: a little more
        // than a tick, so a player on the edge isn't left out between ticks
        private const int HOLD_MS = 250;

        private readonly float radius;
        private readonly int percent;
        private readonly int lifetime;
        private readonly Player owner;
        private readonly uint color;
        private readonly bool vow;
        private readonly HashSet<int> shown = new();
        private int t;
        private int ring;
        private int look;
        private int credit;
        private bool raised;

        // vow: the Ghost Bride's halo's (Player.VowedDamage): no player in it dies of a hit
        public Forcefield(Player player, float radius, int percent, int lifetimeMs, uint color = COLOR, bool vow = false)
            : base(player.GameServer, 0x0711, lifetimeMs, true, true, false)
        {
            this.vow = vow;
            this.radius = radius;
            this.percent = System.Math.Clamp(percent, 0, 90);
            lifetime = lifetimeMs;
            owner = player;
            this.color = color;
        }

        private ShowEffect Look(int which) => new()
        {
            EffectType = EffectType.Relic,
            TargetObjectId = Id,
            Pos1 = new Position(X, Y),
            Pos2 = new Position(radius, which),
            Color = new ARGB(color),
            Duration = System.Math.Max(1, lifetime - t),
            ObjectType = (ushort)percent,
            SourceId = owner.Id,
        };

        public override void Tick(ref TickTime time)
        {
            if (World == null)
                return;

            // the look: to everyone in view as it's raised, then to whoever comes into view
            if (!raised)
            {
                raised = true;
                var cast = Look(LOOK_CAST);
                foreach (var p in World.Players.Values)
                    if (p.SqDistTo(this) < World.CULL_RANGE * World.CULL_RANGE)
                    {
                        p.Client.SendPacket(cast);
                        shown.Add(p.Id);
                    }
            }
            else if (t / 500 >= look)
            {
                ShowEffect seen = null;
                foreach (var p in World.Players.Values)
                    if (!shown.Contains(p.Id) && p.SqDistTo(this) < World.CULL_RANGE * World.CULL_RANGE)
                    {
                        p.Client.SendPacket(seen ??= Look(LOOK_SEEN));
                        shown.Add(p.Id);
                    }
                look = t / 500 + 1;
            }

            // for older clients: a ring in its colour at its edge (the Rust client from this version
            // leaves it out)
            if (t / 1500 >= ring)
            {
                World.BroadcastIfVisible(new ShowEffect()
                {
                    EffectType = EffectType.Trap,
                    Color = new ARGB(color),
                    TargetObjectId = Id,
                    Pos1 = new Position() { X = radius }
                }, this);
                ring++;
            }

            // shield whoever is in it; the caster earns assist credit for the players it covers
            var giveCredit = t / 1000 >= credit;
            if (giveCredit)
                credit++;
            this.AOE(radius, true, entity =>
            {
                if (entity is not Player player)
                    return;
                player.Shield(percent, HOLD_MS, color, vow);
                if (giveCredit)
                    player.BuffedBy(owner, 1500);
            });

            t += time.ElapsedMsDelta;
            base.Tick(ref time);
        }
    }
}
