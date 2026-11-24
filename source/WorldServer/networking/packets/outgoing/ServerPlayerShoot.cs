using Shared;
using Shared.resources;
using WorldServer.core.structures;

namespace WorldServer.networking.packets.outgoing
{
    public sealed class ServerPlayerShoot : OutgoingMessage
    {
        // Client Related Info
        public int ObjectId { get; set; }
        public int BulletId { get; set; }
        public int ContainerType { get; set; }
        public Position StartingPos { get; set; }
        public float Angle { get; set; }
        public int Damage { get; set; }

        // Used for Validation
        public ProjectileDesc ProjectileDesc { get; set; }
        // a Mechanic's turret's shot: its reach, for its damage falling off over it (Turret.Falloff);
        // 0 for none. Not sent: the Rust client works it out from the turret's projectile.
        public float FalloffReach { get; set; }

        public override MessageId MessageId => MessageId.SERVERPLAYERSHOOT;

        public ServerPlayerShoot(int objectId, int bulletId, int containerType, Position startPosition, float angle, int damage, ProjectileDesc projectileDesc)
        {
            ObjectId = objectId;
            BulletId = bulletId;
            ContainerType = containerType;
            StartingPos = startPosition;
            Angle = angle;
            Damage = damage;

            ProjectileDesc = projectileDesc;
        }

        public override void Write(NetworkWriter wtr)
        {
            wtr.Write(BulletId);
            wtr.Write(ObjectId);
            wtr.Write(ContainerType);
            StartingPos.Write(wtr);
            wtr.Write(Angle);
            wtr.Write(Damage);
            // Appended: which of the container's projectiles to draw. Older clients ignore it
            // and draw projectile 0.
            wtr.Write((byte)(ProjectileDesc?.BulletType ?? 0));
        }
    }
}
