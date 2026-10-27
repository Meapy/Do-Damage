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

        public int Imprint(ImprintKind kind) => Imprints != null && (int)kind < Imprints.Length ? Imprints[(int)kind] : 0;

        public string GetData() => JsonConvert.SerializeObject(this);

        public static ItemData CreateData(string json) => JsonConvert.DeserializeObject<ItemData>(json);
    }
}
