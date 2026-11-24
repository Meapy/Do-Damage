using System;
using System.Collections.Generic;
using System.Linq;
using Shared.database.character.inventory;
using Shared.pets;
using Shared.resources;
using WorldServer.core.net.datas;
using WorldServer.core.objects.inventory;
using WorldServer.core.structures;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    // Pet levels and the Pen Keeper by the Arena court (PetPen.cs places him). Every pet hatches at
    // level 1 and grows as it's fed, up to its size's last level (Shared.pets.PetLevels: its
    // damage, healing and size grow with it; PetFood: what each item is worth). He takes a pet back
    // (as /removepet), puts in the pet of a stone, and feeds the pet out whatever is picked from the
    // inventory. Shards of the Mad God used anywhere (UseItem) go to him, whatever pet is out, and
    // he keeps them for the whole account (DbAccount.PenShards) until they're fed or traded, 25 for
    // an Ambrosia. A pet's food stays with it: on the character while it's out (PetXp), in its
    // stone's data when it's given back (ItemData.PetXp). Pets from before levels (2.6.4) came with
    // a raise instead (PetUpgrade: a Superburger or an Ambrosia), turned into food once
    // (PetLevels.MigratedXp). Feeding costs fame, more as the pet grows (PetLevels.FameCost), and
    // he feeds only pets of the sizes the account's pen takes (DbAccount.PenSize): Tiny at first,
    // each bigger size bought with fame or gold (PetLevels.PenPrice). Any pet can be put in. A pet
    // at its last level can be grown to the next size, for fame or gold (PetLevels.GrowPrice): it
    // levels on to the new size's last (PetGrown, kept with its food). The Realm Pass's treats
    // (PetFood.Free) are fed without fame, and its Growth Tonic grows a Tiny, Small or Medium pet
    // at its last level for nothing (13). A Huge pet at its last level can be tuned (14, since
    // 2.6.7): a step of its damage, HP or MP healing moved to another for fame (PetLevels.Retune;
    // PetTune, kept with its food).
    partial class Player
    {
        // the shards he trades for an Ambrosia (PetPenData.ShardsNeeded)
        public const int ShardsForAmbrosia = 25;
        // how each size is drawn, in a character's heights: a grown pet is drawn its new size's
        // height against its stone's
        private static readonly float[] SizeHeights = { 0.5f, 0.75f, 1.0f, 1.4f, 1.7f };

        /// <summary>The food the pet out has eaten (PetLevels).</summary>
        public int PetXp { get; set; }

        /// <summary>The sizes the pet out was grown past its stone's (PetLevels.GrownSize).</summary>
        public int PetGrown { get; set; }

        /// <summary>How the pet out is tuned: steps of damage, HP and MP healing (PetLevels.Tune;
        /// null none).</summary>
        public int[] PetTune { get; set; }
        // the data of a pet stone being used (UseItem takes the item out of its slot first)
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

        /// <summary>The biggest pets the account's pen takes (PetLevels.Sizes' index).</summary>
        private int PenSize => Math.Clamp(Client.Account.PenSize, 0, PetLevels.Sizes.Length - 1);

        /// <summary>The PermaPet of the stone that gives this pet (null for none).</summary>
        public ActivateEffect PetStone(int petType) =>
            PetGenerator(petType)?.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.PermaPet);

        /// <summary>This pet's size, grown `grown` sizes past its stone's (null for an unsized one).</summary>
        public string PetSizeOf(int petType, int grown = 0) => PetLevels.GrownSize(PetStone(petType)?.PetSize, grown);

        /// <summary>The last level this pet reaches, grown `grown` sizes (1 for an unsized one,
        /// which doesn't level).</summary>
        public int PetMaxLevel(int petType, int grown = 0) => PetLevels.MaxLevel(PetSizeOf(petType, grown));

        /// <summary>The food a pet stone's data brings: its own, else a raise from before levels.</summary>
        public int PetXpOf(int petType, ItemData data) =>
            data?.PetXp ?? (data?.PetUpgrade is > 0 ? PetLevels.MigratedXp(data.PetUpgrade.Value, PetMaxLevel(petType)) : 0);

        /// <summary>The pet out's level.</summary>
        public int PetLevel => PetLevels.LevelFor(PetXp, PetMaxLevel(PetId, PetGrown));

        /// <summary>What its level makes of the pet out: its damage times, its healing of HP and
        /// MP (null: its behaviour's own), and how much bigger it's drawn (its level's growth,
        /// and a grown pet its new size's height against its stone's).</summary>
        public (float Damage, (int Amount, int Ms)? Hp, (int Amount, int Ms)? Mp, float Draw) PetPower()
        {
            var stone = PetStone(PetId);
            var size = PetLevels.GrownSize(stone?.PetSize, PetGrown);
            var max = PetLevels.MaxLevel(size);
            if (stone == null || max <= 1)
                return (1, null, null, 1);
            var level = PetLevels.LevelFor(PetXp, max);
            var baseMax = PetLevels.MaxLevel(stone.PetSize);
            // its tuning counts only while it's a Huge pet at its last level
            var tune = PetLevels.CanTune(size, level) ? PetTune : null;
            (int, int)? Heal(int amount, int ms, int stat) => amount > 0 && ms > 0 ? (PetLevels.Heal(amount, level, max, baseMax, PetLevels.TuneScale(tune, stat)), ms) : null;
            var grown = SizeHeights[PetLevels.SizeIndex(size)] / SizeHeights[PetLevels.SizeIndex(stone.PetSize)];
            var damage = PetLevels.DamageScale(level, max, baseMax) * PetLevels.Floor(stone.PetSize, stone.PetDps) * PetLevels.TuneScale(tune, 0);
            return (damage, Heal(stone.PetHp, stone.PetHpMs, 1), Heal(stone.PetMp, stone.PetMpMs, 2),
                PetLevels.DrawScale(level, max, baseMax) * grown);
        }

        /// <summary>What growing the pet out to the next size costs in fame and gold, (0, 0) when
        /// it can't be: none out, unsized, not at its last level, or Huge already.</summary>
        public (int Fame, int Gold) PetGrowPrice()
        {
            var size = PetLevels.SizeIndex(PetId == 0 ? null : PetSizeOf(PetId, PetGrown));
            if (size < 0 || PetXp < PetLevels.XpAt(PetLevels.MaxLevel(PetLevels.Sizes[size])))
                return (0, 0);
            return PetLevels.GrowPrice(size + 1);
        }

        /// <summary>The Pen Keeper's answer: the pet out and its level, what it does now and at
        /// its last level, the shards he keeps, and what the last act came to.</summary>
        public PetPenData PetPenState(bool ok = true, string message = "")
        {
            var data = new PetPenData { Pet = (ushort)PetId, Shards = PenShards, ShardsNeeded = ShardsForAmbrosia, Ok = ok, Message = message ?? "" };
            var stone = PetId == 0 ? null : PetStone(PetId);
            var baseSize = PetLevels.SizeIndex(stone?.PetSize);
            var size = PetLevels.SizeIndex(PetLevels.GrownSize(stone?.PetSize, PetGrown));
            // the sizes as before levels, for older clients (0 Tiny to 3 Large, 255 any other):
            // its stone's, and as grown
            data.BaseSize = (byte)(baseSize is >= 0 and <= 3 ? baseSize : 255);
            data.Size = (byte)(size is >= 0 and <= 3 ? size : 255);
            data.PenSize = (byte)PenSize;
            (data.PenFame, data.PenGold) = PetLevels.PenPrice(PenSize + 1);
            data.PetSize = (byte)(size >= 0 ? size : 255);
            (data.GrowFame, data.GrowGold) = PetGrowPrice();
            if (stone == null)
                return data;
            var max = PetLevels.MaxLevel(PetLevels.GrownSize(stone.PetSize, PetGrown));
            var baseMax = PetLevels.MaxLevel(stone.PetSize);
            var level = PetLevels.LevelFor(PetXp, max);
            data.Level = (byte)level;
            data.MaxLevel = (byte)max;
            data.Xp = PetXp;
            data.XpLevel = PetLevels.XpAt(level);
            data.XpNext = PetLevels.XpAt(Math.Min(level + 1, max));
            // what it really does: its level's (an Oryx's or a boss's at least PetLevels.Floor),
            // as tuned, times PetLevels.Effectiveness (as Pet.Hit and PetHeal do)
            var dps = PetDps(stone) * PetLevels.Floor(stone.PetSize, PetDps(stone));
            var sizeName = PetLevels.GrownSize(stone.PetSize, PetGrown);
            var canTune = PetLevels.CanTune(sizeName, level);
            var tune = canTune ? PetLevels.Tune(PetTune) : null;
            // its last level's numbers as it would be tuned there (it's tuned only at its last)
            var lastTune = PetLevels.CanTune(sizeName, max) ? PetTune : null;
            data.Dps = (int)Math.Round(dps * PetLevels.DamageScale(level, max, baseMax) * PetLevels.TuneScale(tune, 0) * PetLevels.Effectiveness);
            data.DpsMax = (int)Math.Round(dps * PetLevels.DamageScale(max, max, baseMax) * PetLevels.TuneScale(lastTune, 0) * PetLevels.Effectiveness);
            data.Hp = (short)PetLevels.Effective(PetLevels.Heal(stone.PetHp, level, max, baseMax, PetLevels.TuneScale(tune, 1)));
            data.HpMax = (short)PetLevels.Effective(PetLevels.Heal(stone.PetHp, max, max, baseMax, PetLevels.TuneScale(lastTune, 1)));
            data.HpMs = (short)stone.PetHpMs;
            data.Mp = (short)PetLevels.Effective(PetLevels.Heal(stone.PetMp, level, max, baseMax, PetLevels.TuneScale(tune, 2)));
            data.MpMax = (short)PetLevels.Effective(PetLevels.Heal(stone.PetMp, max, max, baseMax, PetLevels.TuneScale(lastTune, 2)));
            data.MpMs = (short)stone.PetMpMs;
            // its tuning (shown once it can be tuned) and what a step costs
            var kept = PetLevels.Tune(PetTune);
            (data.TuneDamage, data.TuneHp, data.TuneMp) = ((sbyte)kept[0], (sbyte)kept[1], (sbyte)kept[2]);
            data.TuneFame = canTune ? PetLevels.TuneFame : 0;
            return data;
        }

        // the damage a second its kit does, as its stone says (petDps)
        private static int PetDps(ActivateEffect stone) => stone?.PetDps ?? 0;

        /// <summary>The Pen Keeper's work (PetPenActHandler): 0 asks, 1 takes the pet back, 2 puts in
        /// the pet of the stone in inventory slot `slot`, 7 feeds the pet the item in `slot`, 8 feeds
        /// it the shards he keeps, 4 gives him the inventory's shards (fed to the pet, if one is out
        /// and still growing), 6 trades 25 of the shards he keeps for an Ambrosia, 9 and 10 make the
        /// pen take the next size of pets, for fame and for gold, 11 and 12 grow the pet out (at its
        /// last level) to the next size, for fame and for gold, 13 grows it with the Growth Tonic in
        /// `slot` (since 2.6.6), 14 tunes a Huge pet at its last level, moving a step from stat
        /// `slot` / 3 to stat `slot` % 3 (0 damage, 1 HP, 2 MP; since 2.6.7). From before levels: 3
        /// and 5 (raise for a Superburger, an Ambrosia) feed it the first of those.</summary>
        public PetPenData PetPenAct(int action, int slot)
        {
            switch (action)
            {
                case 1:
                    if (PetId == 0)
                        return PetPenState(false, "You don't have a pet.");
                    if (!RoomForPetStone(PetId))
                        return PetPenState(false, "Make room in your inventory for its stone first.");
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
                        // its slot takes the old pet's stone
                        Inventory[slot] = null;
                        if (slot < Inventory.Data.Length)
                            Inventory.Data[slot] = null;
                        if (PetId != 0)
                            RemovePet();
                        PetId = petType;
                        PetXp = PetXpOf(petType, data);
                        PetGrown = Math.Max(0, data?.PetGrown ?? 0);
                        PetTune = PetLevels.Untuned(data?.PetTune) ? null : PetLevels.Tune(data.PetTune);
                        SpawnPetIfAttached(World);
                        return PetPenState(true, $"{GameServer.Resources.GameData.ObjectDescs[petType].DisplayName} is yours now.");
                    }
                case 3:
                    return FeedFirst(PetFood.Burger);
                case 5:
                    return FeedFirst(PetFood.Ambrosia);
                case 7:
                    return Feed(slot);
                case 9:
                case 10:
                    return GrowPen(action == 10);
                case 11:
                case 12:
                    return GrowPet(action == 12);
                case 13:
                    return GrowWithTonic(slot);
                case 14:
                    return TunePet(slot / PetLevels.TuneStats, slot % PetLevels.TuneStats);
                case 4:
                    {
                        var slots = Enumerable.Range(4, Inventory.Length - 4).Where(s => Inventory[s]?.ObjectId?.StartsWith(PetFood.ShardPrefix) == true).ToList();
                        if (slots.Count == 0)
                            return PetPenState(false, "You have no Shards of the Mad God with you.");
                        // fed to a pet that's out, still growing and fed here, else kept for the account
                        if (CanFeed() == null)
                            return FeedSlots(slots);
                        return GiveShards(slots);
                    }
                case 8:
                    {
                        if (PenShards <= 0)
                            return PetPenState(false, "The Pen Keeper keeps no shards for you.");
                        var check = CanFeed();
                        if (check != null)
                            return PetPenState(false, check);
                        var given = PenShards;
                        var unpaid = Unpaid(given * PetFood.ShardXp);
                        if (unpaid != null)
                            return PetPenState(false, unpaid);
                        PenShards = 0;
                        return Grow(given * PetFood.ShardXp, $"{given} shards");
                    }
                case 6:
                    {
                        if (PenShards < ShardsForAmbrosia)
                            return PetPenState(false, $"The Pen Keeper needs {ShardsForAmbrosia - PenShards} more shards for an Ambrosia.");
                        if (!GameServer.Resources.GameData.IdToObjectType.TryGetValue(PetFood.Ambrosia, out var type))
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

        // why the pet out can't be fed now, or null
        private string CanFeed()
        {
            if (PetId == 0)
                return "Put a pet in first.";
            var max = PetMaxLevel(PetId, PetGrown);
            if (max <= 1)
                return "This pet doesn't grow.";
            if (PetXp >= PetLevels.XpAt(max))
                return "Your pet is at its last level already.";
            var size = PetLevels.SizeIndex(PetSizeOf(PetId, PetGrown));
            if (size > PenSize)
                return $"Your pen only takes {PetLevels.Sizes[PenSize]} pets: grow it to feed {PetLevels.Sizes[size]} ones.";
            return null;
        }

        // the pen takes the next size of pets, for fame or gold
        private PetPenData GrowPen(bool gold)
        {
            var next = PenSize + 1;
            if (next >= PetLevels.Sizes.Length)
                return PetPenState(false, "Your pen already takes every pet.");
            var (fame, credits) = PetLevels.PenPrice(next);
            var acc = Client.Account;
            var unpaid = PayPen("Growing the pen", fame, credits, gold);
            if (unpaid != null)
                return PetPenState(false, unpaid);
            acc.PenSize = next;
            acc.FlushAsync();
            WorldServer.utils.StaticLogger.Instance.Info($"Pet pen: <{Name} {AccountId}> grew the pen to {PetLevels.Sizes[next]} for {(gold ? $"{credits} gold" : $"{fame} fame")}");
            return PetPenState(true, $"Your pen takes {PetLevels.Sizes[next]} pets now: the Pen Keeper will feed them.");
        }

        // takes `fame` or `gold` for `what`; why it can't, or null
        private string PayPen(string what, int fame, int credits, bool gold)
        {
            var acc = Client.Account;
            if (gold)
            {
                if (Credits < credits)
                    return $"{what} costs {credits:N0} gold: you have {Credits:N0}.";
                // taken at once, so a second press before the database answers can't spend it again
                Credits -= credits;
                GameServer.Database.UpdateCurrency(acc, -credits, CurrencyType.Gold).ContinueWith(_ => Credits = acc.Credits);
            }
            else
            {
                if (CurrentFame < fame)
                    return $"{what} costs {fame:N0} fame: you have {CurrentFame:N0}.";
                CurrentFame -= fame;
                DoomedWorlds.GiveFame(this, -fame);
            }
            return null;
        }

        // the pet out, at its last level, grows to the next size, for fame or gold
        private PetPenData GrowPet(bool gold)
        {
            if (PetId == 0)
                return PetPenState(false, "Put a pet in first.");
            var size = PetLevels.SizeIndex(PetSizeOf(PetId, PetGrown));
            if (size < 0)
                return PetPenState(false, "This pet doesn't grow.");
            if (size >= PetLevels.Sizes.Length - 1)
                return PetPenState(false, "Your pet is as big as pets get.");
            var (fame, credits) = PetGrowPrice();
            if (fame <= 0)
                return PetPenState(false, "Feed your pet to its last level first.");
            var unpaid = PayPen("Growing your pet", fame, credits, gold);
            if (unpaid != null)
                return PetPenState(false, unpaid);
            return Grown(size, gold ? $"{credits} gold" : $"{fame} fame");
        }

        // the pet out (of size index `size`) grows to the next size, paid with `paid`
        private PetPenData Grown(int size, string paid)
        {
            PetGrown++;
            var next = PetLevels.Sizes[size + 1];
            SpawnPetIfAttached(World);
            if (Pet != null)
                World.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.AreaBlast, TargetObjectId = Pet.Id, Pos1 = new Position { X = 2.5f }, Color = new ARGB(0xffffd24a) }, Pet);
            var name = GameServer.Resources.GameData.ObjectDescs.TryGetValue((ushort)PetId, out var d) ? d.DisplayName : "Your pet";
            WorldServer.utils.StaticLogger.Instance.Info($"Pet pen: <{Name} {AccountId}> grew {name} to {next} for {paid}");
            return PetPenState(true, size + 1 > PenSize
                ? $"{name} is a {next} pet now, up to level {PetLevels.MaxLevel(next)}: grow your pen to feed it."
                : $"{name} is a {next} pet now: feed it up to level {PetLevels.MaxLevel(next)}!");
        }

        // the stats' names in the Pen Keeper's answers (PetLevels' tuning order)
        private static readonly string[] TuneNames = { "damage", "HP healing", "MP healing" };

        // the pet out, Huge and at its last level, moves a step of stat `from` to stat `to`, for fame
        private PetPenData TunePet(int from, int to)
        {
            if (PetId == 0)
                return PetPenState(false, "Put a pet in first.");
            var size = PetSizeOf(PetId, PetGrown);
            if (!PetLevels.CanTune(size, PetLevel))
                return PetPenState(false, "Only a Huge pet at its last level can be tuned.");
            var tune = PetLevels.Retune(PetTune, from, to);
            if (tune == null)
                return PetPenState(false, from == to || from >= PetLevels.TuneStats || to >= PetLevels.TuneStats
                    ? "Pick two different things to tune."
                    : $"Tuning goes {PetLevels.TuneStep * PetLevels.TuneMax}% either way at most.");
            var unpaid = PayPen("Tuning your pet", PetLevels.TuneFame, 0, false);
            if (unpaid != null)
                return PetPenState(false, unpaid);
            PetTune = PetLevels.Untuned(tune) ? null : tune;
            SpawnPetIfAttached(World);
            var name = GameServer.Resources.GameData.ObjectDescs.TryGetValue((ushort)PetId, out var d) ? d.DisplayName : "Your pet";
            WorldServer.utils.StaticLogger.Instance.Info($"Pet pen: <{Name} {AccountId}> tuned {name} {TuneNames[from]} to {TuneNames[to]} ({string.Join("/", tune)}) for {PetLevels.TuneFame} fame");
            return PetPenState(true, $"{name} trades {PetLevels.TuneStep}% {TuneNames[from]} for {PetLevels.TuneStep}% {TuneNames[to]}.");
        }

        // the pet out, at its last level, grows to the next size with the Growth Tonic in `slot`
        // (a Tiny, Small or Medium pet: PetFood.TonicGrows)
        private PetPenData GrowWithTonic(int slot)
        {
            if (slot < 4 || slot >= Inventory.Length || Inventory[slot]?.ObjectId != PetFood.GrowthTonic)
                return PetPenState(false, "Pick a Growth Tonic from your inventory.");
            if (PetId == 0)
                return PetPenState(false, "Put a pet in first.");
            var size = PetLevels.SizeIndex(PetSizeOf(PetId, PetGrown));
            if (size < 0)
                return PetPenState(false, "This pet doesn't grow.");
            if (!PetFood.TonicGrows(size))
                return PetPenState(false, "A Growth Tonic only grows Tiny, Small and Medium pets.");
            if (PetGrowPrice().Fame <= 0)
                return PetPenState(false, "Feed your pet to its last level first.");
            Inventory[slot] = null;
            if (slot < Inventory.Data.Length)
                Inventory.Data[slot] = null;
            return Grown(size, "a Growth Tonic");
        }

        private PetPenData FeedFirst(string id)
        {
            var slot = FindInventory(i => i.ObjectId == id);
            return slot < 0 ? PetPenState(false, $"You have no {id} with you.") : Feed(slot);
        }

        /// <summary>Feeds the pet out the item in inventory slot `slot` (its whole stack).</summary>
        private PetPenData Feed(int slot) => FeedSlots(new List<int> { slot });

        private PetPenData FeedSlots(List<int> slots)
        {
            var check = CanFeed();
            if (check != null)
                return PetPenState(false, check);
            var xp = 0;
            string what = null;
            // what it all comes to first, then the fame, then it's eaten
            foreach (var slot in slots)
            {
                if (slot < 4 || slot >= Inventory.Length || Inventory[slot] == null)
                    return PetPenState(false, "Pick something from your inventory to feed it.");
                var value = SlotFood(slot);
                if (value <= 0)
                    return PetPenState(false, $"Pets won't eat {Inventory[slot].DisplayName}.");
                xp += value;
                what = slots.Count == 1 ? Inventory[slot].DisplayName : $"{slots.Count} stacks of shards";
            }
            // the Realm Pass's treats are fed without fame
            var unpaid = slots.All(slot => PetFood.Free(Inventory[slot])) ? null : Unpaid(xp);
            if (unpaid != null)
                return PetPenState(false, unpaid);
            foreach (var slot in slots)
            {
                Inventory[slot] = null;
                if (slot < Inventory.Data.Length)
                    Inventory.Data[slot] = null;
            }
            return Grow(xp, what);
        }

        // the food in inventory slot `slot`: its item's times its stack, a pet stone's half the
        // food in it as well
        private int SlotFood(int slot)
        {
            var item = Inventory[slot];
            var data = slot < Inventory.Data.Length ? Inventory.Data[slot] : null;
            var value = PetFood.Value(item, data?.Stack ?? 0);
            if (value <= 0)
                return 0;
            // a raised stone is worth the food in it as well
            var eff = item.ActivateEffects.FirstOrDefault(e => e.Effect == ActivateEffects.PermaPet);
            if (eff?.ObjectId != null && data != null && GameServer.Resources.GameData.IdToObjectType.TryGetValue(eff.ObjectId, out var t))
                value += PetXpOf(t, data) / 2;
            return value;
        }

        /// <summary>The fame feeding the pet out `xp` of food costs (PetLevels.FameCost).</summary>
        public int FeedFame(int xp) => PetLevels.FameCost(PetXp, xp, PetMaxLevel(PetId, PetGrown));

        // takes the fame for `xp` of food; why it can't, or null
        private string Unpaid(int xp)
        {
            var fame = FeedFame(xp);
            if (fame <= 0)
                return null;
            if (CurrentFame < fame)
                return $"Feeding this costs {fame:N0} fame: you have {CurrentFame:N0}.";
            // taken at once, so a second feed before the database answers can't spend it again
            CurrentFame -= fame;
            DoomedWorlds.GiveFame(this, -fame);
            _feedFame = fame;
            return null;
        }

        // the fame the last feed took, for its message
        private int _feedFame;

        // the pet out eats `xp` of food (no further than its last level) and grows
        private PetPenData Grow(int xp, string what)
        {
            var max = PetMaxLevel(PetId, PetGrown);
            var before = PetLevel;
            PetXp = Math.Min(PetXp + xp, PetLevels.XpAt(max));
            var after = PetLevel;
            var name = GameServer.Resources.GameData.ObjectDescs.TryGetValue((ushort)PetId, out var d) ? d.DisplayName : "Your pet";
            if (after > before)
            {
                SpawnPetIfAttached(World);
                if (Pet != null)
                    World.BroadcastIfVisible(new ShowEffect { EffectType = EffectType.AreaBlast, TargetObjectId = Pet.Id, Pos1 = new Position { X = 1.5f }, Color = new ARGB(0xffffd24a) }, Pet);
                return PetPenState(true, after >= max
                    ? $"{name} ate {what}{Paid()} and reached its last level, {max}!{(PetGrowPrice().Fame > 0 ? " It can grow to the next size now." : "")}"
                    : $"{name} ate {what}{Paid()} and grew to level {after}!");
            }
            return PetPenState(true, $"{name} ate {what} (+{xp:N0}){Paid()}.");
        }

        private string Paid()
        {
            var fame = _feedFame;
            _feedFame = 0;
            return fame > 0 ? $" for {fame:N0} fame" : "";
        }

        /// <summary>Every Shard of the Mad God in these inventory slots to the Pen Keeper, who keeps
        /// them for the account. From his window, or a shard used anywhere (UseItem).</summary>
        public PetPenData GiveShards(IEnumerable<int> slots)
        {
            var given = 0;
            foreach (var s in slots)
            {
                var it = s >= 4 && s < Inventory.Length ? Inventory[s] : null;
                if (it == null || !it.ObjectId.StartsWith(PetFood.ShardPrefix))
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

        /// <summary>/removepet and the Pen Keeper: the pet goes back into its stone, with its food.</summary>
        public void RemovePet()
        {
            var petType = PetId;
            var xp = PetXp;
            var grown = PetGrown;
            var tune = PetTune;
            PetId = 0;
            PetXp = 0;
            PetGrown = 0;
            PetTune = null;
            if (Pet != null)
                World.LeaveWorld(Pet);
            Pet = null;
            if (!GivePetGeneratorBack(petType, xp, grown, tune))
                SendInfo("Your pet runs away.");
        }
    }
}
