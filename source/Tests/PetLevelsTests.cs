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
            Assert.Equal(1, PetLevels.MaxLevel("Giant"));
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
            // every size but Huge stops at 100% damage (140% healing); Huge goes on to 120% (160%)
            Assert.Equal(0.6f, PetLevels.DamageScale(1, 20), 3);
            Assert.Equal(1f, PetLevels.DamageScale(20, 20), 3);
            Assert.Equal(1f, PetLevels.HealScale(1, 20), 3);
            Assert.Equal(1.4f, PetLevels.HealScale(20, 20), 3);
            Assert.Equal(14, PetLevels.Heal(10, 25, 25));
            Assert.Equal(0.6f, PetLevels.DamageScale(1, 30), 3);
            Assert.Equal(1.2f, PetLevels.DamageScale(30, 30), 3);
            Assert.Equal(1.6f, PetLevels.HealScale(30, 30), 3);
            Assert.Equal(1.25f, PetLevels.DrawScale(30, 30), 3);
            for (var max = 10; max < 30; max += 5)
                Assert.Equal(1f, PetLevels.DamageScale(max, max), 3);
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
            // the whole climb, not absurd: about 185 fame for a Tiny pet, 6,820 for a Huge one
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
        public void APetGrowsOneSizeAtATimeFrom1000To25000Fame()
        {
            Assert.Equal((0, 0), PetLevels.GrowPrice(0));
            Assert.Equal((1000, 100), PetLevels.GrowPrice(1));
            Assert.Equal((25000, 1000), PetLevels.GrowPrice(4));
            Assert.Equal((0, 0), PetLevels.GrowPrice(5));
            for (var size = 2; size < PetLevels.Sizes.Length; size++)
            {
                Assert.True(PetLevels.GrowPrice(size).Fame > PetLevels.GrowPrice(size - 1).Fame);
                Assert.True(PetLevels.GrowPrice(size).Gold > PetLevels.GrowPrice(size - 1).Gold);
            }
            Assert.Equal("Tiny", PetLevels.GrownSize("Tiny", 0));
            Assert.Equal("Medium", PetLevels.GrownSize("Tiny", 2));
            Assert.Equal("Huge", PetLevels.GrownSize("Large", 3));
            Assert.Null(PetLevels.GrownSize(null, 2));
            // growing never weakens a pet, and feeding a grown one always makes it stronger
            for (var grown = 1; grown <= 4; grown++)
            {
                var max = PetLevels.MaxLevel(PetLevels.GrownSize("Tiny", grown));
                for (var level = 1; level <= max; level++)
                {
                    Assert.True(PetLevels.DamageScale(level, max, 10) >= PetLevels.DamageScale(Math.Min(level, 10), 10) - 1e-4f);
                    Assert.True(PetLevels.HealScale(level, max, 10) >= PetLevels.HealScale(Math.Min(level, 10), 10) - 1e-4f);
                    if (level > 1)
                    {
                        Assert.True(PetLevels.DamageScale(level, max, 10) > PetLevels.DamageScale(level - 1, max, 10));
                        Assert.True(PetLevels.HealScale(level, max, 10) > PetLevels.HealScale(level - 1, max, 10));
                    }
                }
            }
            // just grown, as it was; then towards its new size's kits (since 2.6.7): a Tiny pet
            // grown to Small 85% of 125 / 50, to Huge 55% of 2,000 / 50 (and 120%)
            Assert.Equal(1f, PetLevels.DamageScale(10, 15, 10), 3);
            Assert.Equal(2.125f, PetLevels.DamageScale(15, 15, 10), 3);
            Assert.Equal(8.5f, PetLevels.DamageScale(25, 25, 10), 3);
            Assert.Equal(1.1f * 4.25f, PetLevels.DamageScale(20, 30, 10), 3);
            Assert.Equal(1.2f * 22f, PetLevels.DamageScale(30, 30, 10), 3);
            // a Large pet grown to Huge: 2.2 times its kit, healing 85% of 22 / 16 more
            Assert.Equal(1f, PetLevels.DamageScale(25, 30, 25), 3);
            Assert.Equal(1.2f * 2.2f, PetLevels.DamageScale(30, 30, 25), 3);
            Assert.Equal(1.6f * 22f / 16f * 0.85f, PetLevels.HealScale(30, 30, 25), 3);
            // a pet never grown is as it was
            Assert.Equal(1f, PetLevels.Grown(25, 25, true));
            Assert.Equal(1f, PetLevels.Grown(1, 1, true));
        }

        [Fact]
        public void OryxAndBossPetsStayAheadOfGrownOnes()
        {
            // a Huge stone's kit counts as at least 1,400 a second; any other is as it says
            Assert.Equal(1.4f, PetLevels.Floor("Huge", 1000), 3);
            Assert.Equal(1f, PetLevels.Floor("Huge", 3000));
            Assert.Equal(1f, PetLevels.Floor("Large", 100));
            Assert.Equal(1f, PetLevels.Floor("Huge", 0));
            var stones = Res.Value.GameData.Items.Values
                .SelectMany(i => i.ActivateEffects.Where(e => e.Effect == ActivateEffects.PermaPet && PetLevels.SizeIndex(e.PetSize) >= 0))
                .ToList();
            // every Huge pet from its own stone at level 30, against every other grown to Huge
            var weakestHuge = stones.Where(e => e.PetSize == "Huge").Min(e => e.PetDps * PetLevels.Floor(e.PetSize, e.PetDps) * PetLevels.DamageScale(30, 30));
            foreach (var e in stones.Where(e => e.PetSize != "Huge"))
            {
                var grown = e.PetDps * PetLevels.DamageScale(30, 30, PetLevels.MaxLevel(e.PetSize));
                Assert.True(grown < weakestHuge, $"{e.ObjectId} grown to Huge does {grown:F0}, the weakest Huge pet {weakestHuge:F0}");
                // and close enough to be worth growing
                Assert.True(grown > weakestHuge * 0.7, $"{e.ObjectId} grown to Huge does only {grown:F0}");
            }
        }

        [Fact]
        public void AMaxedHugePetIsTunedAStepAtATime()
        {
            Assert.True(PetLevels.CanTune("Huge", 30));
            Assert.False(PetLevels.CanTune("Huge", 29));
            Assert.False(PetLevels.CanTune("Large", 30));
            Assert.Equal(new[] { 0, 0, 0 }, PetLevels.Tune(null));
            Assert.True(PetLevels.Untuned(null) && PetLevels.Untuned(new int[0]) && PetLevels.Untuned(new[] { 0, 0, 0 }));
            // healing HP for damage: 5% each way a step
            var tune = PetLevels.Retune(null, 1, 0);
            Assert.Equal(new[] { 1, -1, 0 }, tune);
            Assert.Equal(1.05f, PetLevels.TuneScale(tune, 0), 3);
            Assert.Equal(0.95f, PetLevels.TuneScale(tune, 1), 3);
            Assert.Equal(1f, PetLevels.TuneScale(tune, 2), 3);
            Assert.Equal(1f, PetLevels.TuneScale(null, 0));
            // up to 5 steps either way, and back again
            for (var i = 1; i < PetLevels.TuneMax; i++)
                tune = PetLevels.Retune(tune, 1, 0);
            Assert.Equal(new[] { 5, -5, 0 }, tune);
            Assert.Equal(1.25f, PetLevels.TuneScale(tune, 0), 3);
            Assert.Null(PetLevels.Retune(tune, 1, 2));
            Assert.Null(PetLevels.Retune(tune, 2, 0));
            Assert.Null(PetLevels.Retune(tune, 0, 0));
            Assert.Null(PetLevels.Retune(tune, 0, 3));
            Assert.Equal(new[] { 4, -5, 1 }, PetLevels.Retune(tune, 0, 2));
            // the heal it tunes
            Assert.Equal(19, PetLevels.Heal(10, 30, 30, 0, 1.2f));
        }

        [Fact]
        public void PetsFromBeforeLevelsKeepTheirStrength()
        {
            for (var max = 10; max <= 30; max += 5)
            {
                // a raised one at its last level; any other two thirds of the way (as strong as
                // it was in 2.6.4, when 100% was two thirds of the way for every size)
                Assert.Equal(max, PetLevels.LevelFor(PetLevels.MigratedXp(1, max), max));
                var level = PetLevels.LevelFor(PetLevels.MigratedXp(0, max), max);
                Assert.Equal((int)Math.Round(1 + (max - 1) * 2 / 3.0), level);
                if (max == 30)
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
            var oryx = items.Values.First(i => i.ActivateEffects.Any(e => e.Effect == ActivateEffects.PermaPet && e.PetSize == "Huge"));
            Assert.Equal(8000, PetFood.Value(oryx));
            Assert.Equal(300, PetFood.Value(items["Potion of Attack"], 5));
            // the Realm Pass's treats, fed without fame; the Growth Tonic isn't food
            Assert.Equal(750, PetFood.Value(items["Pet Biscuit"]));
            Assert.Equal(2500, PetFood.Value(items["Hearty Pet Stew"]));
            Assert.Equal(7500, PetFood.Value(items["Royal Pet Feast"]));
            Assert.Equal(1500, PetFood.Value(items["Pet Biscuit"], 2));
            Assert.True(PetFood.Free(items["Royal Pet Feast"]));
            Assert.False(PetFood.Free(items[PetFood.Burger]));
            Assert.Equal(0, PetFood.Value(items[PetFood.GrowthTonic]));
            Assert.False(PetFood.Free(items[PetFood.GrowthTonic]));
        }

        [Fact]
        public void AGrowthTonicGrowsTinySmallAndMediumPets()
        {
            Assert.Equal(new[] { true, true, true, false, false }, PetLevels.Sizes.Select((_, i) => PetFood.TonicGrows(i)).ToArray());
            Assert.False(PetFood.TonicGrows(-1));
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
