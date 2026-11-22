using System;
using System.IO;
using System.Linq;
using Shared.pets;
using Shared.resources;
using Xunit;

namespace Tests
{
    // Pet levels and food (Shared.pets.PetLevels, PetFood), and every pet's stone: sized, with its
    // own healing, about half as much mana as health. The numbers here are the Rust client's too
    // (client-rs/app/src/petlevels.rs has the same checks): change both together.
    public class PetLevelsTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        [Fact]
        public void LevelsGrowAndStopAtTheSizesLast()
        {
            Assert.Equal(new[] { 10, 15, 20, 25, 30 }, PetLevels.Sizes.Select(PetLevels.MaxLevel));
            Assert.Equal(1, PetLevels.MaxLevel(null));
            Assert.Equal(1, PetLevels.MaxLevel("Huge"));
            // 30 x level^1.5, to the nearest 5
            Assert.Equal(30, PetLevels.XpToNext(1));
            Assert.Equal(85, PetLevels.XpToNext(2));
            Assert.Equal(950, PetLevels.XpToNext(10));
            Assert.Equal(0, PetLevels.XpAt(1));
            Assert.Equal(115, PetLevels.XpAt(3));
            for (var max = 10; max <= 30; max += 5)
            {
                Assert.Equal(1, PetLevels.LevelFor(0, max));
                Assert.Equal(max, PetLevels.LevelFor(PetLevels.XpAt(max), max));
                Assert.Equal(max, PetLevels.LevelFor(int.MaxValue / 2, max));
                for (var l = 1; l < max; l++)
                {
                    Assert.Equal(l, PetLevels.LevelFor(PetLevels.XpAt(l), max));
                    Assert.Equal(l, PetLevels.LevelFor(PetLevels.XpAt(l + 1) - 1, max));
                }
            }
            // what the client's window and tooltips show for the hardest climb
            Assert.Equal(56705, PetLevels.XpAt(30));
        }

        [Fact]
        public void PowerGrowsFromItsFirstLevelToItsLast()
        {
            Assert.Equal(0.6f, PetLevels.DamageScale(1, 20), 3);
            Assert.Equal(1.2f, PetLevels.DamageScale(20, 20), 3);
            Assert.Equal(1f, PetLevels.HealScale(1, 20), 3);
            Assert.Equal(1.6f, PetLevels.HealScale(20, 20), 3);
            Assert.Equal(1.25f, PetLevels.DrawScale(20, 20), 3);
            Assert.Equal(16, PetLevels.Heal(10, 25, 25));
            // an unsized pet is as it always was
            Assert.Equal(1f, PetLevels.DamageScale(1, 1));
            Assert.Equal(1f, PetLevels.HealScale(1, 1));
        }

        [Fact]
        public void EveryPetDoesNineTenths()
        {
            Assert.Equal(0.9f, PetLevels.Effectiveness);
            // the heals of the sizes' stones, as the client's tooltips show them (petlevels.rs):
            // halves up, 13.5 to 14 and 40.5 to 41
            Assert.Equal(new[] { 14, 18, 27, 41, 54 }, new[] { 15, 20, 30, 45, 60 }.Select(PetLevels.Effective).ToArray());
        }

        [Fact]
        public void FeedingCostsMoreFameAsThePetGrows()
        {
            // 3 for each 100 food at level 1, 15% more a level after
            Assert.Equal(1, PetLevels.FameCost(0, 30, 30));
            // an unsized pet doesn't grow, so isn't fed
            Assert.Equal(0, PetLevels.FameCost(0, 1500, 1));
            Assert.Equal(0, PetLevels.FameCost(PetLevels.XpAt(30), 5000, 30));
            Assert.Equal(0, PetLevels.FameCost(0, 0, 30));
            // a Superburger near the top costs several times what it does at the start
            var early = PetLevels.FameCost(0, 1500, 30);
            var late = PetLevels.FameCost(PetLevels.XpAt(28), 1500, 30);
            Assert.InRange(early, 45, 75);
            Assert.InRange(late, 210, 270);
            // the whole climb, not absurd: about 185 fame for a Tiny pet, 6,820 for an Oryx
            Assert.InRange(PetLevels.FameCost(0, int.MaxValue / 2, 10), 170, 200);
            Assert.InRange(PetLevels.FameCost(0, int.MaxValue / 2, 30), 6600, 7000);
            // in one feed or many, about the same
            var many = 0;
            for (var xp = 0; xp < PetLevels.XpAt(20); xp += 1500)
                many += PetLevels.FameCost(xp, 1500, 20);
            Assert.InRange(many - PetLevels.FameCost(0, PetLevels.XpAt(20), 20), 0, 40);
        }

        [Fact]
        public void ThePenGrowsOneSizeAtATimeForFameOrGold()
        {
            Assert.Equal((0, 0), PetLevels.PenPrice(0));
            Assert.Equal((2000, 200), PetLevels.PenPrice(1));
            Assert.Equal((20000, 1000), PetLevels.PenPrice(4));
            Assert.Equal((0, 0), PetLevels.PenPrice(5));
            for (var size = 2; size < PetLevels.Sizes.Length; size++)
            {
                Assert.True(PetLevels.PenPrice(size).Fame > PetLevels.PenPrice(size - 1).Fame);
                Assert.True(PetLevels.PenPrice(size).Gold > PetLevels.PenPrice(size - 1).Gold);
            }
        }

        [Fact]
        public void PetsFromBeforeLevelsKeepTheirStrength()
        {
            for (var max = 10; max <= 30; max += 5)
            {
                // a raised one at its last level; any other about as strong as it was (100%)
                Assert.Equal(max, PetLevels.LevelFor(PetLevels.MigratedXp(1, max), max));
                var level = PetLevels.LevelFor(PetLevels.MigratedXp(0, max), max);
                Assert.InRange(PetLevels.DamageScale(level, max), 0.95f, 1.05f);
            }
            Assert.Equal(0, PetLevels.MigratedXp(2, 1));
        }

        [Fact]
        public void FoodIsWorthWhatItSays()
        {
            var items = Res.Value.GameData.Items.Values.ToDictionary(i => i.ObjectId);
            Assert.Equal(1500, PetFood.Value(items[PetFood.Burger]));
            Assert.Equal(5000, PetFood.Value(items[PetFood.Ambrosia]));
            Assert.Equal(200, PetFood.Value(items["Shard of the Mad God x1"]));
            Assert.Equal(1000, PetFood.Value(items["Shard of the Mad God x5"]));
            Assert.Equal(60, PetFood.Value(items["Potion of Attack"]));
            Assert.Equal(200, PetFood.Value(items["Potion of Life"]));
            Assert.Equal(0, PetFood.Value(items.Values.First(i => i.ObjectId.EndsWith(" Clothing Dye"))));
            // a tiered weapon, a tiered ring
            var sword = items.Values.First(i => i.SlotType == 1 && i.Tier == 12);
            Assert.Equal(200, PetFood.Value(sword));
            var ring = items.Values.First(i => i.SlotType == 9 && i.Tier == 6);
            Assert.Equal(200, PetFood.Value(ring));
            // untiered gear by its bag: a white bag's the most, Ascended gear more
            var white = items.Values.First(i => i.Tier < 0 && i.SlotType == 1 && i.BagType == 6 && !i.Ascended && !i.Consumable);
            Assert.Equal(2000, PetFood.Value(white));
            var cyan = items.Values.First(i => i.Tier < 0 && i.SlotType > 0 && i.SlotType != 10 && i.BagType == 4 && !i.Consumable);
            Assert.Equal(600, PetFood.Value(cyan));
            var ascended = items.Values.First(i => i.Ascended && i.SlotType > 0 && i.SlotType != 10 && !i.Consumable && i.Tier < 0);
            Assert.Equal(PetFood.AscendedXp, PetFood.Value(ascended));
            // another pet's stone, by its size
            var oryx = items.Values.First(i => i.ActivateEffects.Any(e => e.Effect == ActivateEffects.PermaPet && e.PetSize == "Oryx"));
            Assert.Equal(8000, PetFood.Value(oryx));
            Assert.Equal(300, PetFood.Value(items["Potion of Attack"], 5));
        }

        [Fact]
        public void EveryPetHasItsOwnHealingAndAboutHalfAsMuchMana()
        {
            var stones = Res.Value.GameData.Items.Values
                .SelectMany(i => i.ActivateEffects.Where(e => e.Effect == ActivateEffects.PermaPet && e.PetSize != null).Select(e => (i, e)))
                .ToList();
            Assert.True(stones.Count > 150);
            foreach (var (item, e) in stones)
            {
                Assert.True(PetLevels.SizeIndex(e.PetSize) >= 0, $"{item.ObjectId}: size {e.PetSize}");
                Assert.True(e.PetHp > 0 && e.PetHpMs >= 1000, $"{item.ObjectId} heals no HP");
                Assert.True(e.PetDps > 0, $"{item.ObjectId} has no damage a second");
                // mana: about half its health a second, never more than 65% of it
                var hp = e.PetHp * 1000.0 / e.PetHpMs;
                var mp = e.PetMpMs > 0 ? e.PetMp * 1000.0 / e.PetMpMs : 0;
                Assert.True(mp <= hp * 0.65 + 0.01, $"{item.ObjectId}: {mp:F1} MP a second, {hp:F1} HP");
            }
            // on the whole, half
            var share = stones.Average(s => s.e.PetMpMs > 0 ? (s.e.PetMp * 1000.0 / s.e.PetMpMs) / (s.e.PetHp * 1000.0 / s.e.PetHpMs) : 0);
            Assert.InRange(share, 0.42, 0.58);
            // not every pet of a size heals the same
            foreach (var size in PetLevels.Sizes.Take(4))
            {
                var heals = stones.Where(s => s.e.PetSize == size).Select(s => (s.e.PetHp, s.e.PetHpMs, s.e.PetMp, s.e.PetMpMs)).Distinct().Count();
                Assert.True(heals > 3, $"{size} pets heal alike");
            }
        }
    }
}
