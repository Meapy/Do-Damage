using System.Collections.Generic;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // A Druid's pearl lying where it was thrown (Player.TidePearls.cs): a tide pool round it that
    // pulses every PULSE_MS for its pearl's duration, hurting and slowing the enemies in it and
    // mending the players (and turrets) in it, until it's recalled: its time up, or one pearl too
    // many thrown. Then the pearl rushes back to its Druid, striking everything on the way.
    //
    // Uses the Trap object (0x0711) as an invisible anchor, as the forcefield does; its look is the
    // Rust client's (client-rs/app/src/particles/tide.rs, looks 61 to 65): a ShowEffect Relic with
    // the radius in pos2.x, the time left in Duration, the pearl's tier in ObjectType (9 for
    // Nerissa's) and its Druid in SourceId. Players who come into view while it's down are sent
    // it then (look 63, without the splash). Older clients, which don't know those looks, see a
    // ring in its colour pulse at its edge.
    public class TidePearl : StaticObject
    {
        public const int LOOK_THROW = 61;   // flying from its Druid to pos1 over Duration
        public const int LOOK_POOL = 62;    // landed: the splash, then the pool
        public const int LOOK_SEEN = 63;    // already down when a player came into view
        public const int LOOK_RECALL = 64;  // flying back from pos1 to its Druid (SourceId) over Duration
        public const int LOOK_HOME = 65;    // back in its Druid's hand (ObjectType: how many it struck)
        public const int PULSE_MS = 500;

        public const int NERISSA = 9;
        // each tier's pearl, as its sprite (drLofiObj6 0xd0 to 0xd8): Magic (blue), Lightning
        // (yellow), Nature (green), Alleviation (violet), Silent Life (cyan), Infinite Shadows
        // (shadow), Wasted Blood (red), Blessed Heavens (white gold), Total Chaos (white and
        // black); then Nerissa's sea green. The Rust client knows them by tier too (tide.rs).
        public static readonly uint[] COLORS =
        {
            0xFF7087FF, 0xFFE4F675, 0xFF8DF574, 0xFFDD44FF, 0xFF37EFFF,
            0xFF6A5A9A, 0xFFFF3737, 0xFFFFF0C8, 0xFFE8E8F0, 0xFF46C2B4,
        };

        // whether (px, py) is within `width` of the way from (fx, fy) to (tx, ty), and how far along
        // it (0 at the start, 1 at the end): what a recalled pearl strikes, and when
        public static bool OnWay(float fx, float fy, float tx, float ty, float px, float py, float width, out float u)
        {
            var (dx, dy) = (tx - fx, ty - fy);
            var len2 = dx * dx + dy * dy;
            u = len2 > 0.0001f ? System.Math.Clamp(((px - fx) * dx + (py - fy) * dy) / len2, 0, 1) : 0;
            var (cx, cy) = (fx + dx * u, fy + dy * u);
            return (px - cx) * (px - cx) + (py - cy) * (py - cy) <= width * width;
        }

        public static int TierOf(Item item) =>
            item?.RelicPower == "Tidepearl" ? NERISSA : System.Math.Clamp(item?.Tier ?? 0, 0, 8);

        public readonly Player Owner;
        public readonly int Tier;
        public readonly uint Color;
        public readonly float Radius;
        public readonly int Lifetime;
        public readonly ActivateEffect Eff;
        private readonly HashSet<int> shown = new();
        private int t;
        private int pulses;
        private int ring;
        private int look;
        private bool landed;

        // recalled or cancelled: it's on its way back (or gone) and does no more
        public bool Gone { get; private set; }

        public TidePearl(Player owner, ActivateEffect eff, Item item)
            : base(owner.GameServer, 0x0711, null, true, false, false)
        {
            Owner = owner;
            Eff = eff;
            Tier = TierOf(item);
            Color = COLORS[Tier];
            Radius = eff.Radius > 0 ? eff.Radius : 3;
            Lifetime = eff.DurationMS > 0 ? eff.DurationMS : 4000;
        }

        public int TimeLeft => System.Math.Max(0, Lifetime - t);

        private ShowEffect Look(int which) => new()
        {
            EffectType = EffectType.Relic,
            TargetObjectId = Id,
            Pos1 = new Position(X, Y),
            Pos2 = new Position(Radius, which),
            Color = new ARGB(Color),
            Duration = System.Math.Max(1, TimeLeft),
            ObjectType = (ushort)Tier,
            SourceId = Owner.Id,
        };

        // never landed (thrown, then one too many thrown before it got there): nothing to do
        public void Cancel() => Gone = true;

        // called back to its Druid: it leaves the pool and flies home, striking on the way
        public void Recall(ref TickTime time)
        {
            if (Gone)
                return;
            Gone = true;
            if (World == null)
                return;
            Owner.RecallPearl(this, ref time);
            World.LeaveWorld(this);
        }

        public override void Tick(ref TickTime time)
        {
            if (World == null || Gone)
                return;
            // its Druid gone (died, left the world): the pearl goes with them
            if (Owner.World != World || Owner.Dead)
            {
                Gone = true;
                World.LeaveWorld(this);
                return;
            }

            if (!landed)
            {
                landed = true;
                var splash = Look(LOOK_POOL);
                foreach (var p in World.Players.Values)
                    if (p.SqDistTo(this) < World.CULL_RANGE * World.CULL_RANGE)
                    {
                        p.Client.SendPacket(splash);
                        shown.Add(p.Id);
                    }
                Owner.PearlLands(this, ref time);
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

            // for older clients: a ring in its colour at its edge (the Rust client from this
            // version leaves it out)
            if (t / 1000 >= ring)
            {
                World.BroadcastIfVisible(new ShowEffect()
                {
                    EffectType = EffectType.Trap,
                    Color = new ARGB(Color),
                    TargetObjectId = Id,
                    Pos1 = new Position() { X = Radius }
                }, this);
                ring++;
            }

            // the pulses: the first PULSE_MS after it lands, the last as its time runs out
            t += time.ElapsedMsDelta;
            while (pulses < Lifetime / PULSE_MS && t >= (pulses + 1) * PULSE_MS && !Gone)
            {
                pulses++;
                Owner.PearlPulse(this, ref time);
            }

            if (t >= Lifetime)
            {
                Recall(ref time);
                return;
            }
            base.Tick(ref time);
        }
    }
}
