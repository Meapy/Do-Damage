using System.Collections.Generic;
using WorldServer.core.net.stats;

namespace WorldServer.core.objects
{
    public class Pet : Entity, IPlayer
    {
        public Pet(GameServer manager, Player player, ushort objType) : base(manager, objType) => PlayerOwner = player;

        public Player PlayerOwner { get; set; }

        // what the Pen Keeper's raise makes of it (Player.PetPower): its damage times this, and its
        // healing of HP and MP (null: its own)
        public float DamageScale { get; set; } = 1;
        public (int Amount, int Ms)? Heal { get; set; }

        public void Damage(int dmg, Entity src) { }

        public bool IsVisibleToEnemy() => false;

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            base.ExportStats(stats, isOtherPlayer);
            if (PlayerOwner != null)
                stats[StatDataType.PetOwner] = PlayerOwner.Id;
        }
    }
}
