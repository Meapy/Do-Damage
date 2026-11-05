using System.Collections.Generic;
using System.Linq;

namespace Shared.resources
{
    // An imprint's colour, which is also its slot on the item: every imprintable item has one of
    // each (ItemData.Imprints, by this index).
    public enum ImprintKind
    {
        Offensive = 0,
        Defensive = 1,
        Misc = 2,
        // the fourth colour, gear's last step: one for weapons, armour and rings
        Apotheosis = 3,
    }

    // The imprints the Enchanter in the Nexus puts on gear (ImprintHandlers.cs): an Offensive, a
    // Defensive, a Misc and an Apotheosis slot on every Enthroned item (Item.Ascended: weapon, helm,
    // armour or ring), each imprint fitting the items it names; on anything else they do nothing.
    // They work only on the item they're on, and only while it's equipped; none of them touches an
    // ability's own effects. The client's copy of this list is client-rs/crates/data/src/imprint.rs
    // (keep the ids, names, costs and rules in step); its effects on shooting are worked out on both
    // sides from the same numbers (ImprintShot below, `imprint::shot` there).
    //
    // An imprint is stored as its id, and the Apotheosis ring's as its id and the stats picked for
    // it (Choice): (raised + 1) << 8, (first lowered + 1) << 12, (second lowered + 1) << 16, by stat
    // index (HP, MP, Att, Def, Spd, Dex, Vit, Wis).
    public sealed class Imprint
    {
        public readonly byte Id;
        public readonly string Name;
        public readonly ImprintKind Kind;
        public readonly string Description;
        // the slot types it fits, or null for any gear but those in NotSlots
        public readonly int[] Slots;
        public readonly int[] NotSlots;
        // how many Ascended shards it costs (Shards)
        public readonly int Cost;

        private Imprint(byte id, string name, ImprintKind kind, string description, int[] slots = null, int[] notSlots = null, int cost = 0)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Description = description;
            Slots = slots;
            NotSlots = notSlots;
            Cost = cost > 0 ? cost : DefaultCost(kind);
        }

        // what an imprint of a colour costs unless it says otherwise: 1 shard for Offensive and
        // Defensive, 3 for Misc, 5 for Apotheosis
        public static int DefaultCost(ImprintKind kind) => kind switch
        {
            ImprintKind.Misc => 3,
            ImprintKind.Apotheosis => 5,
            _ => 1,
        };

        public const int SwordSlot = 1, DaggerSlot = 2, BowSlot = 3, WandSlot = 8, RingSlot = 9, StaffSlot = 17,
            KatanaSlot = 24, BladesSlot = 36, FistsSlot = 200;
        public static readonly int[] WeaponSlots = { SwordSlot, DaggerSlot, BowSlot, WandSlot, StaffSlot, KatanaSlot, BladesSlot, FistsSlot };
        public static readonly int[] ArmorSlots = { 6, 7, 14 };
        private static readonly int[] Rings = { RingSlot };

        // ids are stored on items: never renumber or reuse one. Blood Pact (2) is gone (the owner,
        // 2026-11-05): an item that carried it carries nothing there.
        public const byte KeenEdge = 1, CloseQuarters = 3, AscendantMight = 4;
        public const byte Vigor = 11, Bulwark = 12, AscendantWard = 13;
        public const byte LastBreath = 21, WispCaller = 22, GatheredShot = 23, BattleMage = 24, Spearform = 25,
            Wounds = 26, Bloodrush = 27, Sunder = 28, Siphon = 29, Wellspring = 30, UnboundWill = 31,
            Benediction = 32, GuardianAngel = 33;
        public const byte ApotheosisOfMight = 41, ApotheosisOfWard = 42, ApotheosisOfBalance = 43;

        // the numbers the rules below use
        public const int WoundsBurst = 6000, BattleMageDefense = 20, GuardianAngelCost = 20;
        public const double WispChance = 0.02;
        public const int WispCooldownMs = 8000;

        public static readonly Imprint[] All =
        {
            new Imprint(KeenEdge, "Keen Edge", ImprintKind.Offensive, "+5% damage", WeaponSlots),
            new Imprint(CloseQuarters, "Close Quarters", ImprintKind.Offensive, "+10% damage, -25% range", WeaponSlots),
            new Imprint(AscendantMight, "Ascendant Might", ImprintKind.Offensive, "The ring's own Attack, Speed and Dexterity +50%", Rings),
            new Imprint(Vigor, "Vigor", ImprintKind.Defensive, "+25 maximum health", null, Rings),
            new Imprint(Bulwark, "Bulwark", ImprintKind.Defensive, "+5 defense, +15 maximum health", null, Rings),
            new Imprint(AscendantWard, "Ascendant Ward", ImprintKind.Defensive, "The ring's own Defense, Vitality and Wisdom +50%", Rings),
            new Imprint(LastBreath, "Last Breath", ImprintKind.Misc, "Below 15% health, heal 300 health (every 180 seconds at most)", Rings),
            new Imprint(Wellspring, "Wellspring", ImprintKind.Misc, "Below 15% magic, gain 300 magic (every 180 seconds at most)", Rings),
            new Imprint(UnboundWill, "Unbound Will", ImprintKind.Misc, "Each use of your ability: a 20% chance it costs no magic", Rings),
            new Imprint(Benediction, "Benediction", ImprintKind.Misc, "Each use of your ability blesses you for 4 seconds with one of Healing, Speedy, Berserk, Armored or Damaging, never one your ability gives", Rings),
            new Imprint(GuardianAngel, "Guardian Angel", ImprintKind.Misc, "Takes the ring's other imprints, and its stats become +20 maximum health and +1 to every other stat. A death that would end you sends you to the Nexus instead, and the ring is lost", Rings, null, GuardianAngelCost),
            new Imprint(WispCaller, "Wisp Caller", ImprintKind.Misc, "Each shot: a 2% chance of each of three wisps circling you for 5 seconds, each back 8 seconds after it goes: green heals you, blue makes you Armored, red curses you and enemies near you", new[] { StaffSlot }),
            new Imprint(GatheredShot, "Gathered Shot", ImprintKind.Misc, "The three arrows fly as one charged arrow doing all three's damage; 95% rate of fire", new[] { BowSlot }),
            new Imprint(BattleMage, "Battle Mage", ImprintKind.Misc, "Range 5, +20 defense. Two more bolts fly beside each shot at 60% of the rate of fire, for 450 damage that ignores defense", new[] { WandSlot }),
            new Imprint(Spearform, "Spearform", ImprintKind.Misc, "The sword reaches like a spear: range 8, 30% less damage", new[] { SwordSlot }),
            new Imprint(Wounds, "Wounds", ImprintKind.Misc, "Every hit wounds; the 25th wound on an enemy bursts, 6000 damage that ignores defense to it and everything within 4 tiles", new[] { DaggerSlot }),
            new Imprint(Bloodrush, "Bloodrush", ImprintKind.Misc, "Shots no longer pierce. Each kill: +4 attack, dexterity and speed for 20 seconds, stacking, each with its own time", new[] { KatanaSlot }),
            new Imprint(Sunder, "Sunder", ImprintKind.Misc, "Range 4, +10% rate of fire. Each hit takes 1 defense from the enemy for 10 seconds, stacking, each with its own time", new[] { FistsSlot }),
            new Imprint(Siphon, "Siphon", ImprintKind.Misc, "Each hit heals you 0.2% of the damage it does", new[] { BladesSlot }),
            new Imprint(ApotheosisOfMight, "Apotheosis of Might", ImprintKind.Apotheosis, "+10% of the Attack and Dexterity your gear and boosts add", WeaponSlots),
            new Imprint(ApotheosisOfWard, "Apotheosis of Ward", ImprintKind.Apotheosis, "+10% of the Defense and Vitality your gear and boosts add", ArmorSlots),
            new Imprint(ApotheosisOfBalance, "Apotheosis of Balance", ImprintKind.Apotheosis, "+10% of what your gear and boosts add to a stat you pick, and -20% of all of two other stats you pick", Rings),
        };

        private static readonly Dictionary<byte, Imprint> ById = All.ToDictionary(i => i.Id);

        // An imprint by its id, or by the value stored for it (its id in the low byte).
        public static Imprint Get(int value)
        {
            var id = value & 0xFF;
            return id > 0 && ById.TryGetValue((byte)id, out var i) ? i : null;
        }

        public bool Fits(Item item) => (Slots == null || Slots.Contains(item.SlotType)) && (NotSlots == null || !NotSlots.Contains(item.SlotType));

        // Whether the imprint has stats picked for it (the Apotheosis of Balance).
        public bool Picks => Id == ApotheosisOfBalance;

        // The forge shards an imprint of this colour is paid in, any of them (Cost of them, mixed).
        public static string[] Shards(ImprintKind kind) => kind switch
        {
            ImprintKind.Offensive => new[] { "Ascended Weapon Shard", "Ascended Ability Shard" },
            ImprintKind.Defensive => new[] { "Ascended Armor Shard" },
            ImprintKind.Misc => new[] { "Ascended Accessory Shard" },
            _ => new[] { "Ascended Weapon Shard", "Ascended Ability Shard", "Ascended Armor Shard", "Ascended Accessory Shard" },
        };

        // The value stored for the Apotheosis of Balance with these stats picked (stat indices).
        public static int WithChoice(byte id, int raised, int lowered1, int lowered2) =>
            id | ((raised + 1) << 8) | ((lowered1 + 1) << 12) | ((lowered2 + 1) << 16);

        // The stats picked in a stored value: (raised, first lowered, second lowered), -1 for none.
        public static (int Raised, int Lowered1, int Lowered2) Choice(int value) =>
            (((value >> 8) & 0xF) - 1, ((value >> 12) & 0xF) - 1, ((value >> 16) & 0xF) - 1);

        // Whether a choice is one the Apotheosis of Balance takes: three different stats.
        public static bool ValidChoice(int raised, int lowered1, int lowered2) =>
            raised is >= 0 and < 8 && lowered1 is >= 0 and < 8 && lowered2 is >= 0 and < 8
            && raised != lowered1 && raised != lowered2 && lowered1 != lowered2;
    }

    // What a weapon's imprints do to its shots, the same on both sides (the client's
    // `imprint::Shot`): damage as integer percent steps applied to the roll in this order, so client
    // and server agree to the point; range as a lifetime.
    public readonly struct ImprintShot
    {
        public readonly int[] DamagePercents;
        public readonly int RangePercent;
        public readonly int RangeTenths;        // tenths of a tile, 0 for the weapon's own
        public readonly bool NoPierce;
        public readonly bool Gathered;          // one arrow for all of them
        public readonly float RateMult;

        private ImprintShot(int[] damage, int rangePercent, int rangeTenths, bool noPierce, bool gathered, float rateMult)
        {
            DamagePercents = damage;
            RangePercent = rangePercent;
            RangeTenths = rangeTenths;
            NoPierce = noPierce;
            Gathered = gathered;
            RateMult = rateMult;
        }

        public static readonly ImprintShot None = new ImprintShot(System.Array.Empty<int>(), 100, 0, false, false, 1);

        public static ImprintShot Of(int[] imprints, Item weapon)
        {
            if (imprints == null || weapon == null || !weapon.Ascended)
                return None;
            var damage = new List<int>();
            int rangePercent = 100, rangeTenths = 0;
            float rateMult = 1;
            bool noPierce = false, gathered = false;
            foreach (var value in imprints)
            {
                var imp = Imprint.Get(value);
                if (imp == null || !imp.Fits(weapon))
                    continue;
                switch (imp.Id)
                {
                    case Imprint.KeenEdge: damage.Add(105); break;
                    case Imprint.CloseQuarters: damage.Add(110); rangePercent = 75; break;
                    case Imprint.Spearform: damage.Add(70); rangeTenths = 80; break;
                    case Imprint.BattleMage: rangeTenths = 50; break;
                    case Imprint.Sunder: rangeTenths = 40; rateMult = 1.1f; break;
                    case Imprint.Bloodrush: noPierce = true; break;
                    case Imprint.GatheredShot:
                        if (weapon.NumProjectiles > 1)
                        {
                            gathered = true;
                            damage.Add(100 * weapon.NumProjectiles);
                            rateMult = 0.95f;
                        }
                        break;
                }
            }
            return new ImprintShot(damage.ToArray(), rangePercent, rangeTenths, noPierce, gathered, rateMult);
        }

        public int Damage(int roll)
        {
            foreach (var p in DamagePercents)
                roll = roll * p / 100;
            return roll;
        }

        // the shot's lifetime in ms: the range it's set to, else the weapon's, then scaled (whole
        // numbers, as the client works it out)
        public int Lifetime(ProjectileDesc desc)
        {
            var speed = (int)desc.Speed;
            var life = RangeTenths > 0 && speed > 0 ? RangeTenths * 1000 / speed : (int)desc.LifetimeMS;
            return life * RangePercent / 100;
        }

        public int NumProjectiles(Item weapon) => Gathered ? 1 : weapon.NumProjectiles;
    }
}
