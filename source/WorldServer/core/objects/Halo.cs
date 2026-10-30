using WorldServer.core.net.datas;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;
using WorldServer.utils;

namespace WorldServer.core.objects
{
    // Doomed Realms Ascendant halo: a golden circle left where it was cast that heals the players
    // inside it by a fixed amount every 2 seconds until it expires. Uses the Trap object (0x0711)
    // as an invisible anchor, as the original did.
    internal class Halo : StaticObject
    {
        private const uint COLOR = 0xffffd700;

        private readonly float radius;
        private readonly int amount;
        private readonly Player owner;
        private int ring;
        private int heal;
        private int t;

        public Halo(Player player, float radius, int amount, int lifetimeMs)
            : base(player.GameServer, 0x0711, lifetimeMs, true, true, false)
        {
            this.radius = radius;
            this.amount = amount;
            owner = player;
        }

        public override void Tick(ref TickTime time)
        {
            if (t / 500 >= ring)
            {
                World.BroadcastIfVisible(new ShowEffect()
                {
                    EffectType = EffectType.Trap,
                    Color = new ARGB(COLOR),
                    TargetObjectId = Id,
                    Pos1 = new Position() { X = radius }
                }, this);
                ring++;
            }

            if (t / 2000 >= heal)
            {
                this.AOE(radius, true, entity =>
                {
                    if (entity is Player player)
                        Player.ActivateHealHp(player, amount, from: owner);
                });
                World.BroadcastIfVisible(new ShowEffect()
                {
                    EffectType = EffectType.AreaBlast,
                    Color = new ARGB(COLOR),
                    TargetObjectId = Id,
                    Pos1 = new Position() { X = radius }
                }, this);
                heal++;
            }

            t += time.ElapsedMsDelta;
            base.Tick(ref time);
        }
    }
}
