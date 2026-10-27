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
    }

    // The imprints the Enchanter in the Nexus puts on gear (ImprintHandlers.cs): one Offensive, one
    // Defensive and one Misc slot on every weapon, ability, armour and ring, each imprint fitting the
    // items it names. They work only on the item they're on, and only while it's equipped; none of
    // them touches an ability's own effects. The client's copy of this list is
    // client-rs/crates/data/src/imprint.rs (keep the ids, names and rules in step); its effects on
    // shooting are worked out on both sides from the same numbers (ImprintShot below,
    // `imprint::shot` there).
    public sealed class Imprint
    {
        public readonly byte Id;
        public readonly string Name;
        public readonly ImprintKind Kind;
        public readonly string Description;
        // the slot types it fits, or null for any gear
        public readonly int[] Slots;

        private Imprint(byte id, string name, ImprintKind kind, string description, int[] slots = null)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Description = description;
            Slots = slots;
        }

        public const int SwordSlot = 1, DaggerSlot = 2, BowSlot = 3, WandSlot = 8, RingSlot = 9, StaffSlot = 17,
            KatanaSlot = 24, BladesSlot = 36, FistsSlot = 200;
        public static readonly int[] WeaponSlots = { SwordSlot, DaggerSlot, BowSlot, WandSlot, StaffSlot, KatanaSlot, BladesSlot, FistsSlot };

        // ids are stored on items: never renumber or reuse one
        public const byte KeenEdge = 1, BloodPact = 2, CloseQuarters = 3;
        public const byte Vigor = 11, Bulwark = 12;
        public const byte LastBreath = 21, WispCaller = 22, GatheredShot = 23, BattleMage = 24, Spearform = 25,
            Wounds = 26, Bloodrush = 27, Sunder = 28, Siphon = 29;

        public static readonly Imprint[] All =
        {
            new Imprint(KeenEdge, "Keen Edge", ImprintKind.Offensive, "+5% damage", WeaponSlots),
            new Imprint(BloodPact, "Blood Pact", ImprintKind.Offensive, "+10% damage, -10% maximum health", WeaponSlots),
            new Imprint(CloseQuarters, "Close Quarters", ImprintKind.Offensive, "+8% damage, -20% range", WeaponSlots),
            new Imprint(Vigor, "Vigor", ImprintKind.Defensive, "+20 maximum health"),
            new Imprint(Bulwark, "Bulwark", ImprintKind.Defensive, "+3 defense, +15 maximum health"),
            new Imprint(LastBreath, "Last Breath", ImprintKind.Misc, "Below 15% health, heal 300 health (every 180 seconds at most)", new[] { RingSlot }),
            new Imprint(WispCaller, "Wisp Caller", ImprintKind.Misc, "Each shot: a 10% chance of each of three wisps circling you for 5 seconds, each back 10 seconds after it goes: green heals you, blue makes you Armored, red curses you and enemies near you", new[] { StaffSlot }),
            new Imprint(GatheredShot, "Gathered Shot", ImprintKind.Misc, "The three arrows fly as one charged arrow doing all three's damage; 85% rate of fire", new[] { BowSlot }),
            new Imprint(BattleMage, "Battle Mage", ImprintKind.Misc, "Range 4. Two more bolts fly beside each shot at 40% of the rate of fire, for 450 damage that ignores defense", new[] { WandSlot }),
            new Imprint(Spearform, "Spearform", ImprintKind.Misc, "The sword reaches like a spear: range 8, 40% less damage", new[] { SwordSlot }),
            new Imprint(Wounds, "Wounds", ImprintKind.Misc, "Every hit wounds; the 25th wound on an enemy bursts, 4000 damage that ignores defense to it and everything within 4 tiles", new[] { DaggerSlot }),
            new Imprint(Bloodrush, "Bloodrush", ImprintKind.Misc, "Shots no longer pierce. Each kill: +4 attack, dexterity and speed for 20 seconds, stacking, each with its own time", new[] { KatanaSlot }),
            new Imprint(Sunder, "Sunder", ImprintKind.Misc, "Range 3.5. Each hit takes 1 defense from the enemy for 10 seconds, stacking, each with its own time", new[] { FistsSlot }),
            new Imprint(Siphon, "Siphon", ImprintKind.Misc, "Each hit heals you 0.2% of the damage it does", new[] { BladesSlot }),
        };

        private static readonly Dictionary<byte, Imprint> ById = All.ToDictionary(i => i.Id);

        public static Imprint Get(int id) => id > 0 && id < 256 && ById.TryGetValue((byte)id, out var i) ? i : null;

        public bool Fits(Item item) => Slots == null || Slots.Contains(item.SlotType);

        // The forge shards an imprint of this colour costs, any one of them.
        public static string[] Cost(ImprintKind kind) => kind switch
        {
            ImprintKind.Offensive => new[] { "Ascended Weapon Shard", "Ascended Ability Shard" },
            ImprintKind.Defensive => new[] { "Ascended Armor Shard" },
            _ => new[] { "Ascended Accessory Shard" },
        };
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
            if (imprints == null || weapon == null)
                return None;
            var damage = new List<int>();
            int rangePercent = 100, rangeTenths = 0;
            float rateMult = 1;
            bool noPierce = false, gathered = false;
            foreach (var id in imprints)
            {
                var imp = Imprint.Get(id);
                if (imp == null || !imp.Fits(weapon))
                    continue;
                switch (imp.Id)
                {
                    case Imprint.KeenEdge: damage.Add(105); break;
                    case Imprint.BloodPact: damage.Add(110); break;
                    case Imprint.CloseQuarters: damage.Add(108); rangePercent = 80; break;
                    case Imprint.Spearform: damage.Add(60); rangeTenths = 80; break;
                    case Imprint.BattleMage: rangeTenths = 40; break;
                    case Imprint.Sunder: rangeTenths = 35; break;
                    case Imprint.Bloodrush: noPierce = true; break;
                    case Imprint.GatheredShot:
                        if (weapon.NumProjectiles > 1)
                        {
                            gathered = true;
                            damage.Add(100 * weapon.NumProjectiles);
                            rateMult = 0.85f;
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
