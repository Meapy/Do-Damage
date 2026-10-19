using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Shared.resources
{
    public class BreakDownData
    {
        public readonly int Fame;
        public readonly string ItemName;

        public BreakDownData(XElement e)
        {
            Fame = e.GetAttribute("fame", 0);
            ItemName = e.Value;
        }
    }

    public class Item
    {
        public ActivateEffect[] ActivateEffects;
        public ActivateEffect[] OnPlayerHitActivateEffects;
        public ActivateEffect[] OnPlayerShootActivateEffects;
        public ActivateEffect[] OnPlayerAbilityActivateEffects;
        public ActivateEffect[] OnPlayerPassiveActivateEffects;
        public ActivateEffect[] OnEnemyHitActivateEffects;
        public float ArcGap;
        public bool Backpack;
        public int BagType;
        public string Class;
        public bool Consumable;
        public float Cooldown;
        public string Description;
        public string DisplayId;
        public string DisplayName;
        public int Doses;
        public int FameBonus;
        public bool InvUse;
        public bool LDBoosted;
        public bool LTBoosted;
        public int MpCost;
        public int MpEndCost;
        public int NumProjectiles;
        public string ObjectId;
        public ushort ObjectType;
        public bool Potion;
        public ProjectileDesc[] Projectiles;
        public int Quantity;
        public int QuantityLimit;
        public float RateOfFire;
        public bool Resurrects;
        public int SlotType;
        public bool Soulbound;
        // can't be traded, sold on the market or dropped for others: admin items and the OP ones
        // around them. Everything else can, Soulbound or not (Soulbound now only means its loot
        // drops in a private bag). The client's copy of this rule is untradeable() in
        // client-rs/crates/data/src/object.rs.
        public bool Untradeable;
        public KeyValuePair<int, int>[] ActivateOnEquips;
        public string SuccessorId;
        public int Texture1;
        public int Texture2;
        public int Tier;
        public float Timer;
        public bool TypeOfConsumable;
        public bool Usable;
        public bool XpBoost;
        public int ManaCostPerSecond;
        public BreakDownData BreakDownData;

        // NOWA ZMIENNA DLA DOŚWIADCZENIA
        public int Exp;

        // OP items without the <Admin/> tag: Doomed Realms' admin gear, rewards and shop items with
        // far more stats than anything else, and the developers' test items
        public static readonly HashSet<string> OpItems = new HashSet<string>
        {
            "Doomie", "Doomie Amulet", "Doomie Ninjaspell", "Doomie Robe", "Doomie Staff", "Doomie Star",
            "Doomie Stick", "Doomie Yolo", "Nuke", "God Rushing", "Doomed Gods", "Doomed Hero Badge",
            "Class Test", "Test Auraa Generator", "XP Booster Test",
            "Apple", "Gift", "Keys", "TKeys", "LH Lost Halls Debug Item", "Test TP", "Shatters TP",
            "Test Close", "Test Ring", "Snowy Spawner", "InvinceMe", "tots", "frpk", "frs", "tgs",
            "Prod Enemy Item", "Test Horn", "GH1 Key", "GH2 Key", "GH3 Key", "GH4 Key",
        };

        public Item(ushort type, XElement e)
        {
            ObjectType = type;
            ObjectId = e.GetAttribute<string>("id");
            Class = e.GetValue<string>("Class");
            DisplayId = e.GetValue<string>("DisplayId");
            DisplayName = string.IsNullOrWhiteSpace(DisplayId) ? ObjectId : DisplayId;
            Texture1 = e.GetValue<int>("Tex1");
            Texture2 = e.GetValue<int>("Tex2");
            SlotType = e.GetValue<int>("SlotType");
            Description = e.GetValue<string>("Description");
            Consumable = e.HasElement("Consumable");
            Soulbound = e.HasElement("Soulbound");
            Untradeable = e.HasElement("Admin") || OpItems.Contains(ObjectId);
            Potion = e.HasElement("Potion");
            Usable = e.HasElement("Usable");
            Resurrects = e.HasElement("Resurrects");
            RateOfFire = e.GetValue<float>("RateOfFire");
            Tier = e.GetValue("Tier", -1);
            BagType = e.GetValue<int>("BagType");
            FameBonus = e.GetValue<int>("FameBonus");
            NumProjectiles = e.GetValue("NumProjectiles", 1);
            ArcGap = e.GetValue("ArcGap", 11.25f);
            MpCost = e.GetValue<int>("MpCost");
            Cooldown = e.GetValue("Cooldown", 0.5f);
            Doses = e.GetValue<int>("Doses");
            SuccessorId = e.GetValue<string>("SuccessorId");
            Backpack = e.HasElement("Backpack");
            LDBoosted = e.HasElement("LDBoosted");
            LTBoosted = e.HasElement("LTBoosted");
            XpBoost = e.HasElement("XpBoost");
            Timer = e.GetValue<float>("Timer");
            ManaCostPerSecond = e.GetValue("MpCostPerSecond", 0);
            MpEndCost = e.GetValue("MpEndCost", 0);
            InvUse = e.HasElement("InvUse");
            TypeOfConsumable = InvUse || Consumable;

            // WCZYTYWANIE WARTOŚCI EXP Z XML
            Exp = e.GetValue("Exp", 0);

            ActivateOnEquips = e.Elements("ActivateOnEquip").Select(_ => new KeyValuePair<int, int>(_.GetAttribute<int>("stat"), _.GetAttribute<int>("amount"))).ToArray();
            ActivateEffects = e.Elements("Activate").Select(_ => new ActivateEffect(_)).ToArray();
            OnPlayerHitActivateEffects = e.Elements("OnPlayerHitActivate").Select(_ => new ActivateEffect(_)).ToArray();
            OnPlayerAbilityActivateEffects = e.Elements("OnPlayerAbilityActivate").Select(_ => new ActivateEffect(_)).ToArray();
            OnPlayerShootActivateEffects = e.Elements("OnPlayerShootActivate").Select(_ => new ActivateEffect(_)).ToArray();
            OnPlayerPassiveActivateEffects = e.Elements("OnPlayerPassiveHitActivate").Select(_ => new ActivateEffect(_)).ToArray();
            OnEnemyHitActivateEffects = e.Elements("OnEnemyHitActivate").Select(_ => new ActivateEffect(_)).ToArray();
            Projectiles = e.Elements("Projectile").Select(_ => new ProjectileDesc(_)).ToArray();
            Quantity = e.GetValue("Quantity", 0);
            QuantityLimit = e.GetValue("QuantityLimit", 0);
            if (e.Element("BreakDown") != null)
                BreakDownData = new BreakDownData(e.Element("BreakDown"));
        }
    }
}