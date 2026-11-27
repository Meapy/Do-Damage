using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.resources;

namespace Shared.database.character.inventory
{
    public class ItemData
    {

        public string ObjectId { get; set; }

        public int Stack { get; set; }
        public int MaxStack { get; set; }

        // the imprints on the item (Shared/resources/Imprints.cs), by ImprintKind: 0 for none; left
        // out of the JSON when it has none
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int[] Imprints { get; set; }

        // a pet stone given back by the Pen Keeper or /removepet: the food its pet had eaten
        // (Shared.pets.PetLevels, Player.PetPen.cs); left out when there is none
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? PetXp { get; set; }

        // a pet stone given back: the sizes its pet was grown past the stone's
        // (PetLevels.GrownSize); left out when none
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? PetGrown { get; set; }

        // a pet stone given back: how its Huge pet was tuned, steps of damage, HP and MP healing
        // (PetLevels.Tune); left out when none
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int[] PetTune { get; set; }

        // a pet stone given back before levels (2.6.4): the sizes the Pen Keeper raised its pet by,
        // turned into food when it's used (PetLevels.MigratedXp)
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? PetUpgrade { get; set; }

        // its corruption (Shared/resources/Corruption.cs: damage, range, rate of fire, stats), left out
        // when it has none
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int[] Corruption { get; set; }

        // a unique corruption: 1, a staff whose shots fly from where it aims back to its wielder
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Unique { get; set; }

        // an item of many doses with no next one (the super elixirs): the doses it has left, counted
        // down as it's used (Player.UseItem.cs); left out while it's full
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Doses { get; set; }

        // The doses an item of many doses with no next one has left before it's used (0 for any other
        // item, which goes or becomes its next as before): its count, or all of them while it's full.
        public static int DosesLeft(Item item, ItemData data)
        {
            if (item == null || item.Doses <= 1 || item.SuccessorId != null)
                return 0;
            return data?.Doses is int left && left > 0 ? Math.Min(left, item.Doses) : item.Doses;
        }

        // Its data once it has `left` doses: none when it's used up, else a copy keeping the rest.
        public static ItemData WithDoses(Item item, ItemData data, int left)
        {
            if (left <= 0)
                return null;
            var after = data == null ? new ItemData { ObjectId = item.ObjectId } : CreateData(data.GetData());
            after.Doses = left;
            return after;
        }

        public int Imprint(ImprintKind kind) => Imprints != null && (int)kind < Imprints.Length ? Imprints[(int)kind] : 0;

        public string GetData() => JsonConvert.SerializeObject(this);

        public static ItemData CreateData(string json) => JsonConvert.DeserializeObject<ItemData>(json);
    }
}
