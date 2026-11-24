using System;
using Shared.database.account;
using Shared.database.character.inventory;

namespace Shared.database.character
{
    public class DbChar : RedisObject
    {
        public DbAccount Account { get; private set; }

        public int CharId { get; private set; }
        public DateTime CreateTime { get => GetValue<DateTime>("createTime"); set => SetValue("createTime", value); }
        public bool Dead { get => GetValue<bool>("dead"); set => SetValue("dead", value); }
        public int Experience { get => GetValue<int>("exp"); set => SetValue("exp", value); }
        public int Fame { get => GetValue<int>("fame"); set => SetValue("fame", value); }
        public byte[] FameStats { get => GetValue<byte[]>("fameStats"); set => SetValue("fameStats", value); }
        public int FinalFame { get => GetValue<int>("finalFame"); set => SetValue("finalFame", value); }
        public bool HasBackpack { get => GetValue<bool>("hasBackpack"); set => SetValue("hasBackpack", value); }
        public int HealthStackCount { get => GetValue<int>("hpPotCount"); set => SetValue("hpPotCount", value); }
        public int Health { get => GetValue<int>("hp"); set => SetValue("hp", value); }
        public ushort[] Items { get => GetValue<ushort[]>("items"); set => SetValue("items", value); }
        public ItemData[] Datas { get => GetValue<ItemData[]>("datas"); set => SetValue("datas", value); }
        public DateTime LastSeen { get => GetValue<DateTime>("lastSeen"); set => SetValue("lastSeen", value); }
        public int LDBoostTime { get => GetValue<int>("ldBoost"); set => SetValue("ldBoost", value); }
        public int Level { get => GetValue<int>("level"); set => SetValue("level", value); }
        public int MagicStackCount { get => GetValue<int>("mpPotCount"); set => SetValue("mpPotCount", value); }
        public int MP { get => GetValue<int>("mp"); set => SetValue("mp", value); }
        public ushort ObjectType { get => GetValue<ushort>("charType"); set => SetValue("charType", value); }
        public int PetId { get => GetValue<int>("petId"); set => SetValue("petId", value); }
        // the Pen Keeper's work on the pet (Player.PetPen.cs): the sizes it was raised by
        // the Pen Keeper's raise of a pet from before levels (2.6.4): only read, to turn into food
        public int PetUpgrade { get => GetValue<int>("petUpgrade"); set => SetValue("petUpgrade", value); }
        // the food the pet out has eaten (Shared.pets.PetLevels); -1 for a character saved before
        // levels, whose pet's raise (PetUpgrade) is turned into food once
        public int PetXp { get => GetValue<int>("petXp", -1); set => SetValue("petXp", value); }
        // the sizes the pet out was grown past its stone's (PetLevels.GrownSize; 0 none)
        public int PetGrown { get => GetValue<int>("petGrown"); set => SetValue("petGrown", value); }
        // how the pet out is tuned: steps of damage, HP and MP healing (PetLevels.Tune; empty none)
        public int[] PetTune { get => GetValue<int[]>("petTune"); set => SetValue("petTune", value); }
        public int Skin { get => GetValue<int>("skin"); set => SetValue("skin", value); }

        public int[] Stats { get => GetValue<int[]>("stats"); set => SetValue("stats", value); }
        // how far past its class's maximum each stat may go, from the Infernal potions drunk
        // (Player.AEOvercapStat)
        public int[] Overcap { get => GetValue<int[]>("overcap") ?? new int[8]; set => SetValue("overcap", value); }
        public int Tex1 { get => GetValue<int>("tex1"); set => SetValue("tex1", value); }
        public int Tex2 { get => GetValue<int>("tex2"); set => SetValue("tex2", value); }
        public int XPBoostTime { get => GetValue<int>("xpBoost"); set => SetValue("xpBoost", value); }
        public bool CompletedTrialOfSouls { get => GetValue<bool>("completedTrialOfSouls"); set => SetValue("completedTrialOfSouls", value); }

        public DbChar(DbAccount account, int charId, bool isAsync = false)
        {
            Account = account;
            CharId = charId;

            Init(account.Database, $"char.{account.AccountId}.{charId}", null, isAsync);
        }
    }
}
