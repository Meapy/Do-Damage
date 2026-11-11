using System;
using System.Collections.Generic;
using System.Linq;
using Shared.database.character.inventory;
using Shared.resources;
using WorldServer.core.objects.inventory;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // The Pen Keeper by the Arena court (PetPen.cs places him): he takes a pet back (as /removepet),
    // puts in the pet of a generator, and raises a pet's size without changing its look or its
    // shots: Small to Medium for a Superburger, Medium to Large for an Ambrosia (from 2.5.1; it was
    // 25 Shards of the Mad God). Shards go to him whenever they're given (his window) or used
    // (anywhere, UseItem), whatever pet is out, and he keeps them for the whole account
    // (DbAccount.PenShards) until they're traded, 25 for an Ambrosia. A raised pet hits as hard as the size
    // it's raised to (twice as hard a size) and heals as that size does (SizeHeal). The raise stays
    // with the pet: on the character while it's out (PetUpgrade), on its generator when it's given
    // back (ItemData.PetUpgrade).
    partial class Player
    {
        // the shards he trades for an Ambrosia (PetPenData.ShardsNeeded)
        public const int ShardsForAmbrosia = 25;
        public const string PetBurger = "Superburger";
        public const string PetAmbrosia = "Ambrosia";
        public const string PetShardPrefix = "Shard of the Mad God x";
        public static readonly string[] PetSizes = { "Tiny", "Small", "Medium", "Large" };
        // each size's healing of HP and MP, and how often (tools/port_doomed_pets.py PET_SIZES)
        private static readonly (int Amount, int Ms)[] SizeHeal = { (15, 2000), (20, 2000), (30, 2000), (45, 1500) };

        public int PetUpgrade { get; set; }
        // the data of a pet generator being used (UseItem takes the item out of its slot first)
        private ItemData _usedPetData;

        /// <summary>The shards the Pen Keeper keeps for the account.</summary>
        private int PenShards
        {
            get => Client.Account.PenShards;
            set
            {
                Client.Account.PenShards = value;
                Client.Account.FlushAsync();
            }
        }

        /// <summary>The size a pet hatches at (its generator's petSize), 0 Tiny to 3 Large, -1 unknown.</summary>
        public int PetBaseSize(int petType)
        {
            var size = PetGenerator(petType)?.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.PermaPet)?.PetSize;
            return size == null ? -1 : Array.IndexOf(PetSizes, size);
        }

        /// <summary>What the pet's raise makes of it: its damage times, and its healing (null: its own).</summary>
        public (float Damage, (int Amount, int Ms)? Heal) PetPower()
        {
            var size = PetBaseSize(PetId);
            if (PetUpgrade <= 0 || size < 0)
                return (1, null);
            var to = Math.Min(size + PetUpgrade, SizeHeal.Length - 1);
            return (MathF.Pow(2, to - size), SizeHeal[to]);
        }

        public PetPenData PetPenState(bool ok = true, string message = "")
        {
            var size = PetId == 0 ? -1 : PetBaseSize(PetId);
            return new PetPenData
            {
                Pet = (ushort)PetId,
                BaseSize = (byte)(size < 0 ? 255 : size),
                Size = (byte)(size < 0 ? 255 : Math.Min(size + PetUpgrade, 3)),
                Shards = PenShards,
                ShardsNeeded = ShardsForAmbrosia,
                Ok = ok,
                Message = message ?? ""
            };
        }

        /// <summary>The Pen Keeper's work (PetPenActHandler): 0 asks, 1 takes the pet back, 2 puts in
        /// the pet of the generator in inventory slot `slot`, 3 raises it Small to Medium, 4 gives him
        /// the inventory's shards, 5 raises it Medium to Large for an Ambrosia, 6 trades 25 of the
        /// shards he keeps for an Ambrosia, into the inventory.</summary>
        public PetPenData PetPenAct(int action, int slot)
        {
            switch (action)
            {
                case 1:
                    if (PetId == 0)
                        return PetPenState(false, "You don't have a pet.");
                    RemovePet();
                    return PetPenState(true, "Your pet is back in its stone.");
                case 2:
                    {
                        if (slot < 4 || slot >= Inventory.Length || Inventory[slot] == null)
                            return PetPenState(false, "Pick a pet stone from your inventory.");
                        var item = Inventory[slot];
                        var eff = item.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.PermaPet);
                        if (eff?.ObjectId == null || !GameServer.Resources.GameData.IdToObjectType.TryGetValue(eff.ObjectId, out var petType))
                            return PetPenState(false, "That isn't a pet stone.");
                        if (PetId == petType)
                            return PetPenState(false, "That pet is already yours.");
                        var data = slot < Inventory.Data.Length ? Inventory.Data[slot] : null;
                        if (PetId != 0)
                            RemovePet();
                        Inventory[slot] = null;
                        if (slot < Inventory.Data.Length)
                            Inventory.Data[slot] = null;
                        PetId = petType;
                        PetUpgrade = data?.PetUpgrade ?? 0;
                        SpawnPetIfAttached(World);
                        return PetPenState(true, $"{GameServer.Resources.GameData.ObjectDescs[petType].DisplayName} is yours now.");
                    }
                case 3:
                    {
                        if (PetId == 0)
                            return PetPenState(false, "Put a pet in first.");
                        var size = PetBaseSize(PetId) + PetUpgrade;
                        if (size != 1)
                            return PetPenState(false, size < 1 ? "Only a Small pet can be raised to Medium." : "Your pet is past Small already.");
                        var burger = FindInventory(i => i.ObjectId == PetBurger);
                        if (burger < 0)
                            return PetPenState(false, "Raising a pet to Medium takes a Superburger.");
                        Inventory[burger] = null;
                        PetUpgrade++;
                        SpawnPetIfAttached(World);
                        return PetPenState(true, "Your pet grew to Medium strength!");
                    }
                case 4:
                    return GiveShards(Enumerable.Range(4, Inventory.Length - 4));
                case 5:
                    {
                        if (PetId == 0)
                            return PetPenState(false, "Put a pet in first.");
                        if (PetBaseSize(PetId) + PetUpgrade != 2)
                            return PetPenState(false, "Only a Medium pet can be raised to Large.");
                        var ambrosia = FindInventory(i => i.ObjectId == PetAmbrosia);
                        if (ambrosia < 0)
                            return PetPenState(false, $"Raising a pet to Large takes an Ambrosia: he trades one for {ShardsForAmbrosia} shards.");
                        Inventory[ambrosia] = null;
                        PetUpgrade++;
                        SpawnPetIfAttached(World);
                        return PetPenState(true, "Your pet grew to Large strength!");
                    }
                case 6:
                    {
                        if (PenShards < ShardsForAmbrosia)
                            return PetPenState(false, $"The Pen Keeper needs {ShardsForAmbrosia - PenShards} more shards for an Ambrosia.");
                        if (!GameServer.Resources.GameData.IdToObjectType.TryGetValue(PetAmbrosia, out var type))
                            return PetPenState(false, "The Pen Keeper has no Ambrosia to give.");
                        var end = HasBackpack ? InventoryConstants.SWAP_FIRST : 12;
                        var free = -1;
                        for (var s = 4; s < end && free < 0; s++)
                            if (Inventory[s] == null)
                                free = s;
                        if (free < 0)
                            return PetPenState(false, "Make room in your inventory for the Ambrosia.");
                        PenShards -= ShardsForAmbrosia;
                        Inventory[free] = GameServer.Resources.GameData.Items[type];
                        return PetPenState(true, $"The Pen Keeper trades you an Ambrosia for {ShardsForAmbrosia} shards.");
                    }
                default:
                    return PetPenState();
            }
        }

        /// <summary>Every Shard of the Mad God in these inventory slots to the Pen Keeper, who keeps
        /// them for the account. From his window, or a shard used anywhere (UseItem).</summary>
        public PetPenData GiveShards(IEnumerable<int> slots)
        {
            var given = 0;
            foreach (var s in slots)
            {
                var it = s >= 4 && s < Inventory.Length ? Inventory[s] : null;
                if (it == null || !it.ObjectId.StartsWith(PetShardPrefix))
                    continue;
                given += Math.Max(1, it.Quantity);
                Inventory[s] = null;
            }
            if (given == 0)
                return PetPenState(false, "You have no Shards of the Mad God with you.");
            PenShards += given;
            return PetPenState(true, $"The Pen Keeper takes {given} shards: he keeps {PenShards} for you.");
        }

        private int FindInventory(Func<Item, bool> what)
        {
            for (var s = 4; s < Inventory.Length; s++)
                if (Inventory[s] != null && what(Inventory[s]))
                    return s;
            return -1;
        }

        /// <summary>/removepet and the Pen Keeper: the pet goes back into its generator, with its raise.</summary>
        public void RemovePet()
        {
            var petType = PetId;
            var upgrade = PetUpgrade;
            PetId = 0;
            PetUpgrade = 0;
            if (Pet != null)
                World.LeaveWorld(Pet);
            Pet = null;
            if (!GivePetGeneratorBack(petType, upgrade))
                SendInfo("Your pet runs away.");
        }
    }
}
