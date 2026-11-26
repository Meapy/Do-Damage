using System;
using System.IO;
using Shared.database.character.inventory;
using Shared.resources;
using Xunit;

namespace Tests
{
    // The super elixirs (health and magic): many doses and no next item, so each counts its doses
    // down on itself (ItemData.Doses, Player.UseItem.cs) instead of going after one.
    public class SuperElixirTests
    {
        private static readonly Lazy<Resources> Res = new Lazy<Resources>(() =>
            new Resources(Path.Combine(AppContext.BaseDirectory, "resources"), true, false));

        private static Item Get(string id)
        {
            var data = Res.Value.GameData;
            return data.Items[data.IdToObjectType[id]];
        }

        [Theory]
        [InlineData("Super Elixir", ActivateEffects.Magic)]
        [InlineData("Super Elixir of Health", ActivateEffects.Heal)]
        public void BothCountTheirDosesDownToTheLast(string id, ActivateEffects effect)
        {
            var elixir = Get(id);
            Assert.True(elixir.Consumable);
            Assert.Null(elixir.SuccessorId);
            Assert.Contains(elixir.ActivateEffects, e => e.Effect == effect && e.Amount == 250);

            // full: every dose, and the first use leaves one fewer on it
            Assert.Equal(elixir.Doses, ItemData.DosesLeft(elixir, null));
            var data = ItemData.WithDoses(elixir, null, elixir.Doses - 1);
            Assert.Equal(id, data.ObjectId);
            Assert.Equal(elixir.Doses - 1, ItemData.DosesLeft(elixir, data));

            // what else the item carries stays; the last dose leaves nothing
            var one = ItemData.WithDoses(elixir, new ItemData { ObjectId = id, Imprints = new[] { 1, 0, 0, 0 } }, 1);
            Assert.Equal(1, one.Imprints[0]);
            Assert.Equal(1, ItemData.DosesLeft(elixir, one));
            Assert.Null(ItemData.WithDoses(elixir, one, 0));
        }

        [Fact]
        public void ElixirsWithANextDoseAreLeftAsTheyWere()
        {
            // Elixir of Health 7 becomes Elixir of Health 6, and so on: nothing counted on it
            Assert.Equal(0, ItemData.DosesLeft(Get("Elixir of Health 7"), null));
            Assert.Equal(0, ItemData.DosesLeft(Get("Elixir of Health 1"), null));
        }
    }
}
