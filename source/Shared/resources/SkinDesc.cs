using System.Xml.Linq;
using Shared;

namespace Shared.resources
{
    public class SkinDesc
    {
        public readonly int Cost;
        public readonly ushort PlayerClassType;
        public readonly int Size;
        public readonly ushort Type;
        public readonly int UnlockLevel;

        public string ObjectId;
        // a Realm Pass season's skin (tools/make_pass_skins.py): only the pass gives it, never sold
        public readonly bool PassSkin;

        public SkinDesc(ushort type, XElement e)
        {
            Type = type;
            ObjectId = e.GetAttribute<string>("id");
            PlayerClassType = e.GetValue<ushort>("PlayerClassType");
            UnlockLevel = e.GetValue<int>("UnlockLevel");
            Cost = e.GetValue("Cost", 200);
            Size = e.GetValue("Size", 100);
            PassSkin = e.Element("PassSkin") != null;
        }
    }
}
