using System;

namespace Shared.database
{
    /// <summary>
    /// What a character slot or a vault chest costs. With fame the price doubles every
    /// <see cref="DoublesEvery"/> bought with fame (1000 fame for a slot, then 2000 from the 6th,
    /// 4000 from the 11th...; vault chests from 500); with gold it's always the same and doesn't
    /// count towards the fame price. Slots and chests from items or a new account don't count
    /// either. The counts are the account's <see cref="SlotsFameField"/> and
    /// <see cref="VaultFameField"/>, which only the purchases write.
    /// </summary>
    public static class UnlockPrices
    {
        public const int DoublesEvery = 5;
        public const int SlotGold = 500;
        public const int VaultGold = 250;
        public const string SlotsFameField = "charSlotsFame";
        public const string VaultFameField = "vaultChestsFame";

        // 2^20 times the base is already out of anyone's reach; past it the price stays put
        private const int MaxDoublings = 20;

        public static int FamePrice(int basePrice, int boughtWithFame)
        {
            var doublings = Math.Clamp(boughtWithFame / DoublesEvery, 0, MaxDoublings);
            return (int)Math.Min((long)basePrice << doublings, int.MaxValue);
        }
    }
}
