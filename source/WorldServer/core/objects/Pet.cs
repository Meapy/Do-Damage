using System.Collections.Generic;
using Shared.pets;
using WorldServer.core.net.stats;

namespace WorldServer.core.objects
{
    public class Pet : Entity, IPlayer
    {
        public Pet(GameServer manager, Player player, ushort objType) : base(manager, objType)
        {
            PlayerOwner = player;
            _effect = new StatTypeValue<int>(this, StatDataType.PetEffect, 0);
        }

        // the Realm Pass pet effect it wears (its owner's: Player.PetEffect)
        private readonly StatTypeValue<int> _effect;
        public int Effect
        {
            get => _effect.GetValue();
            set => _effect.SetValue(value);
        }

        public Player PlayerOwner { get; set; }

        // what its level makes of it (Player.PetPower, Shared.pets.PetLevels): its damage times
        // this, and its healing of HP and of MP, its stone's grown (null: its behaviour's own)
        public float DamageScale { get; set; } = 1;
        public (int Amount, int Ms)? HealHp { get; set; }
        public (int Amount, int Ms)? HealMp { get; set; }
        // whether it heals by its stone's numbers (any sized pet): then no number there is no healing
        public bool OwnHealing { get; set; }

        /// <summary>A pet's hit: amount times its level's DamageScale and PetLevels.Effectiveness.</summary>
        public static int Hit(Entity host, int amount) => (int)(amount * ((host as Pet)?.DamageScale ?? 1) * PetLevels.Effectiveness);

        public void Damage(int dmg, Entity src) { }

        public bool IsVisibleToEnemy(bool ignoreInvisible = false) => false;

        protected override void ExportStats(IDictionary<StatDataType, object> stats, bool isOtherPlayer)
        {
            base.ExportStats(stats, isOtherPlayer);
            if (PlayerOwner != null)
                stats[StatDataType.PetOwner] = PlayerOwner.Id;
            if (Effect != 0)
                stats[StatDataType.PetEffect] = Effect;
        }
    }
}
