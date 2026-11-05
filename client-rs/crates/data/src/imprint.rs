//! The imprints the Enchanter puts on gear: the client's copy of the server's list
//! (`Shared/resources/Imprints.cs`; keep the ids, names, costs and rules in step). Every Enthroned
//! item (`<Ascended/>`) has an Offensive (red), a Defensive (green), a Misc (purple) and an
//! Apotheosis (light blue) slot, stored in the item's data
//! (`"Imprints":[offensive, defensive, misc, apotheosis]`, 0 for none). A value is the imprint's
//! id, and for the Apotheosis of Balance also the stats picked for it ([`choice`]). [`Shot`] is
//! what the weapon's imprints do to its shots, worked out as the server's `ImprintShot` does.

/// The imprints an item carries, by [`Kind`]: each the value stored (the id in the low byte).
pub type Set = [u32; 4];

/// An imprint's colour, which is also its slot on the item.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Kind {
    Offensive = 0,
    Defensive = 1,
    Misc = 2,
    /// The fourth colour, gear's last step: one for weapons, armour and rings.
    Apotheosis = 3,
}

impl Kind {
    pub const ALL: [Kind; 4] = [Kind::Offensive, Kind::Defensive, Kind::Misc, Kind::Apotheosis];

    pub fn name(self) -> &'static str {
        match self {
            Kind::Offensive => "Offensive",
            Kind::Defensive => "Defensive",
            Kind::Misc => "Misc",
            Kind::Apotheosis => "Apotheosis",
        }
    }

    /// Its colour (`0xRRGGBB`): red, green, purple, the light blue of Oryx 5's crystal.
    pub fn color(self) -> u32 {
        match self {
            Kind::Offensive => 0xd6283a,
            Kind::Defensive => 0x2fb44f,
            Kind::Misc => 0x9446dc,
            Kind::Apotheosis => 0x5ec8f2,
        }
    }

    /// The forge shards an imprint of this colour is paid in, any of them (its cost of them, mixed).
    pub fn shards(self) -> &'static [&'static str] {
        match self {
            Kind::Offensive => &["Ascended Weapon Shard", "Ascended Ability Shard"],
            Kind::Defensive => &["Ascended Armor Shard"],
            Kind::Misc => &["Ascended Accessory Shard"],
            Kind::Apotheosis => &["Ascended Weapon Shard", "Ascended Ability Shard", "Ascended Armor Shard", "Ascended Accessory Shard"],
        }
    }

    /// What an imprint of this colour costs unless it says otherwise: 1 shard for Offensive and
    /// Defensive, 3 for Misc, 5 for Apotheosis.
    pub fn default_cost(self) -> i32 {
        match self {
            Kind::Misc => 3,
            Kind::Apotheosis => 5,
            _ => 1,
        }
    }
}

pub const SWORD: i32 = 1;
pub const DAGGER: i32 = 2;
pub const BOW: i32 = 3;
pub const WAND: i32 = 8;
pub const RING: i32 = 9;
pub const STAFF: i32 = 17;
pub const KATANA: i32 = 24;
pub const BLADES: i32 = 36;
pub const FISTS: i32 = 200;
pub const WEAPONS: &[i32] = &[SWORD, DAGGER, BOW, WAND, STAFF, KATANA, BLADES, FISTS];
pub const ARMORS: &[i32] = &[6, 7, 14];
const RINGS: &[i32] = &[RING];

// ids are stored on items: never renumber or reuse one (2, Blood Pact, is gone)
pub const KEEN_EDGE: u8 = 1;
pub const CLOSE_QUARTERS: u8 = 3;
pub const ASCENDANT_MIGHT: u8 = 4;
pub const VIGOR: u8 = 11;
pub const BULWARK: u8 = 12;
pub const ASCENDANT_WARD: u8 = 13;
pub const LAST_BREATH: u8 = 21;
pub const WISP_CALLER: u8 = 22;
pub const GATHERED_SHOT: u8 = 23;
pub const BATTLE_MAGE: u8 = 24;
pub const SPEARFORM: u8 = 25;
pub const WOUNDS: u8 = 26;
pub const BLOODRUSH: u8 = 27;
pub const SUNDER: u8 = 28;
pub const SIPHON: u8 = 29;
pub const WELLSPRING: u8 = 30;
pub const UNBOUND_WILL: u8 = 31;
pub const BENEDICTION: u8 = 32;
pub const GUARDIAN_ANGEL: u8 = 33;
pub const APOTHEOSIS_OF_MIGHT: u8 = 41;
pub const APOTHEOSIS_OF_WARD: u8 = 42;
pub const APOTHEOSIS_OF_BALANCE: u8 = 43;

/// The Battle Mage's +20 defense.
pub const BATTLE_MAGE_DEFENSE: i32 = 20;
/// The object whose projectile the Battle Mage's bolts are, and their rate against the wand's.
pub const BATTLE_MAGE_BOLT: &str = "Battle Mage Bolt";
pub const BATTLE_MAGE_RATE: f64 = 0.6;
/// The bolts' angle either side of the wand's shot, in degrees (a bow's arc).
pub const BATTLE_MAGE_ARC: f64 = 10.0;
/// What the imprints' own shots look like (the Gathered Shot's arrow, the Spearform thrust; the
/// Battle Mage's bolts have it too): Oryx 5's crystal Sigil (`tools/build_ascended_items.py`).
pub const SIGIL: &str = "Enthroned Sigil";

pub struct Imprint {
    pub id: u8,
    pub name: &'static str,
    pub kind: Kind,
    pub description: &'static str,
    /// The slot types it fits, or `None` for any gear but those in `not_slots`.
    pub slots: Option<&'static [i32]>,
    pub not_slots: &'static [i32],
    /// How many shards it costs, 0 for its colour's ([`Kind::default_cost`]).
    pub cost: i32,
}

impl Imprint {
    pub fn fits(&self, slot_type: i32) -> bool {
        self.slots.is_none_or(|s| s.contains(&slot_type)) && !self.not_slots.contains(&slot_type)
    }

    /// The shards it costs.
    pub fn cost(&self) -> i32 {
        if self.cost > 0 { self.cost } else { self.kind.default_cost() }
    }

    /// Whether stats are picked for it (the Apotheosis of Balance).
    pub fn picks(&self) -> bool {
        self.id == APOTHEOSIS_OF_BALANCE
    }

    /// What it fits, for the window: "Weapons", "Staves", "Any gear".
    pub fn fits_text(&self) -> &'static str {
        match self.slots {
            None if self.not_slots == RINGS => "Any gear but rings",
            None => "Any gear",
            Some(s) if s == WEAPONS => "Weapons",
            Some(s) if s == ARMORS => "Armor",
            Some([RING]) => "Rings",
            Some([STAFF]) => "Staves",
            Some([BOW]) => "Bows",
            Some([WAND]) => "Wands",
            Some([SWORD]) => "Swords",
            Some([DAGGER]) => "Daggers",
            Some([KATANA]) => "Katanas",
            Some([FISTS]) => "Fists",
            Some([BLADES]) => "Dual Blades",
            Some(_) => "Some gear",
        }
    }
}

const fn imp(id: u8, name: &'static str, kind: Kind, description: &'static str, slots: Option<&'static [i32]>) -> Imprint {
    Imprint { id, name, kind, description, slots, not_slots: &[], cost: 0 }
}

pub const ALL: &[Imprint] = &[
    imp(KEEN_EDGE, "Keen Edge", Kind::Offensive, "+5% damage", Some(WEAPONS)),
    imp(CLOSE_QUARTERS, "Close Quarters", Kind::Offensive, "+10% damage, -25% range", Some(WEAPONS)),
    imp(ASCENDANT_MIGHT, "Ascendant Might", Kind::Offensive, "The ring's own Attack, Speed and Dexterity +50%", Some(RINGS)),
    Imprint { not_slots: RINGS, ..imp(VIGOR, "Vigor", Kind::Defensive, "+25 maximum health", None) },
    Imprint { not_slots: RINGS, ..imp(BULWARK, "Bulwark", Kind::Defensive, "+5 defense, +15 maximum health", None) },
    imp(ASCENDANT_WARD, "Ascendant Ward", Kind::Defensive, "The ring's own Defense, Vitality and Wisdom +50%", Some(RINGS)),
    imp(LAST_BREATH, "Last Breath", Kind::Misc, "Below 15% health, heal 300 health (every 180 seconds at most)", Some(RINGS)),
    imp(WELLSPRING, "Wellspring", Kind::Misc, "Below 15% magic, gain 300 magic (every 180 seconds at most)", Some(RINGS)),
    imp(UNBOUND_WILL, "Unbound Will", Kind::Misc, "Each use of your ability: a 20% chance it costs no magic", Some(RINGS)),
    imp(
        BENEDICTION,
        "Benediction",
        Kind::Misc,
        "Each use of your ability blesses you for 4 seconds with one of Healing, Speedy, Berserk, Armored or Damaging, never one your ability gives",
        Some(RINGS),
    ),
    Imprint {
        cost: 20,
        ..imp(
            GUARDIAN_ANGEL,
            "Guardian Angel",
            Kind::Misc,
            "Takes the ring's other imprints, and its stats become +20 maximum health and +1 to every other stat. A death that would end you sends you to the Nexus instead, and the ring is lost",
            Some(RINGS),
        )
    },
    imp(
        WISP_CALLER,
        "Wisp Caller",
        Kind::Misc,
        "Each shot: a 2% chance of each of three wisps circling you for 5 seconds, each back 8 seconds after it goes: green heals you, blue makes you Armored, red curses you and enemies near you",
        Some(&[STAFF]),
    ),
    imp(GATHERED_SHOT, "Gathered Shot", Kind::Misc, "The three arrows fly as one charged arrow doing all three's damage; 95% rate of fire", Some(&[BOW])),
    imp(
        BATTLE_MAGE,
        "Battle Mage",
        Kind::Misc,
        "Range 5, +20 defense. Two more bolts fly beside each shot at 60% of the rate of fire, for 450 damage that ignores defense",
        Some(&[WAND]),
    ),
    imp(SPEARFORM, "Spearform", Kind::Misc, "The sword reaches like a spear: range 8, 30% less damage", Some(&[SWORD])),
    imp(
        WOUNDS,
        "Wounds",
        Kind::Misc,
        "Every hit wounds; the 25th wound on an enemy bursts, 6000 damage that ignores defense to it and everything within 4 tiles",
        Some(&[DAGGER]),
    ),
    imp(
        BLOODRUSH,
        "Bloodrush",
        Kind::Misc,
        "Shots no longer pierce. Each kill: +4 attack, dexterity and speed for 20 seconds, stacking, each with its own time",
        Some(&[KATANA]),
    ),
    imp(
        SUNDER,
        "Sunder",
        Kind::Misc,
        "Range 4, +10% rate of fire. Each hit takes 1 defense from the enemy for 10 seconds, stacking, each with its own time",
        Some(&[FISTS]),
    ),
    imp(SIPHON, "Siphon", Kind::Misc, "Each hit heals you 0.2% of the damage it does", Some(&[BLADES])),
    imp(APOTHEOSIS_OF_MIGHT, "Apotheosis of Might", Kind::Apotheosis, "+10% of the Attack and Dexterity your gear and boosts add", Some(WEAPONS)),
    imp(APOTHEOSIS_OF_WARD, "Apotheosis of Ward", Kind::Apotheosis, "+10% of the Defense and Vitality your gear and boosts add", Some(ARMORS)),
    imp(
        APOTHEOSIS_OF_BALANCE,
        "Apotheosis of Balance",
        Kind::Apotheosis,
        "+10% of what your gear and boosts add to a stat you pick, and -20% of all of two other stats you pick",
        Some(RINGS),
    ),
];

/// An imprint by its id, or by the value stored for it (its id in the low byte).
pub fn get(value: i64) -> Option<&'static Imprint> {
    let id = (value & 0xFF) as u8;
    ALL.iter().find(|i| i.id == id && id != 0)
}

/// The id in a stored value.
pub fn id_of(value: u32) -> u8 {
    (value & 0xFF) as u8
}

/// The value stored for the Apotheosis of Balance with these stats picked (by stat index: HP,
/// MP, Att, Def, Spd, Dex, Vit, Wis), as the server's `Imprint.WithChoice`.
pub fn with_choice(id: u8, raised: u8, lowered: [u8; 2]) -> u32 {
    id as u32 | ((raised as u32 + 1) << 8) | ((lowered[0] as u32 + 1) << 12) | ((lowered[1] as u32 + 1) << 16)
}

/// The stats picked in a stored value, (raised, [lowered, lowered]), when there are three
/// different ones.
pub fn choice(value: u32) -> Option<(u8, [u8; 2])> {
    let at = |shift: u32| ((value >> shift) & 0xF) as i32 - 1;
    let (r, a, b) = (at(8), at(12), at(16));
    let ok = |n: i32| (0..8).contains(&n);
    (ok(r) && ok(a) && ok(b) && r != a && r != b && a != b).then_some((r as u8, [a as u8, b as u8]))
}

/// The imprints in an item's data (the JSON the server sends per slot), by [`Kind`]: 0 for none.
pub fn parse(data: &str) -> Set {
    let mut out = [0u32; 4];
    let Some(i) = data.find("\"Imprints\"") else { return out };
    let rest = &data[i + 10..];
    let Some(open) = rest.find('[') else { return out };
    let Some(close) = rest[open..].find(']') else { return out };
    for (k, n) in rest[open + 1..open + close].split(',').take(4).enumerate() {
        out[k] = n.trim().parse().unwrap_or(0);
    }
    out
}

/// What a weapon's imprints do to its shots (the server's `ImprintShot`): damage as whole
/// percent steps on the roll, in order; range as a lifetime.
#[derive(Clone, Debug, PartialEq)]
pub struct Shot {
    pub damage_percents: Vec<i32>,
    pub range_percent: i32,
    /// tenths of a tile, 0 for the weapon's own
    pub range_tenths: i32,
    pub no_pierce: bool,
    /// one arrow for all of them
    pub gathered: bool,
    pub rate_mult: f64,
    pub battle_mage: bool,
    /// The shots look like the imprints' own ([`SIGIL`]): the Gathered Shot's arrow, the
    /// Spearform thrust.
    pub sigil: bool,
}

impl Default for Shot {
    fn default() -> Self {
        Shot { damage_percents: Vec::new(), range_percent: 100, range_tenths: 0, no_pierce: false, gathered: false, rate_mult: 1.0, battle_mage: false, sigil: false }
    }
}

impl Shot {
    /// The weapon's (`slot_type`, `num_projectiles`) imprints' effect on its shots.
    pub fn of(imprints: Set, slot_type: i32, num_projectiles: i32) -> Shot {
        let mut s = Shot::default();
        for &value in &imprints {
            let Some(imp) = get(value as i64) else { continue };
            if !imp.fits(slot_type) {
                continue;
            }
            match imp.id {
                KEEN_EDGE => s.damage_percents.push(105),
                CLOSE_QUARTERS => {
                    s.damage_percents.push(110);
                    s.range_percent = 75;
                }
                SPEARFORM => {
                    s.damage_percents.push(70);
                    s.range_tenths = 80;
                    s.sigil = true;
                }
                BATTLE_MAGE => {
                    s.range_tenths = 50;
                    s.battle_mage = true;
                }
                SUNDER => {
                    s.range_tenths = 40;
                    s.rate_mult = 1.1;
                }
                BLOODRUSH => s.no_pierce = true,
                GATHERED_SHOT if num_projectiles > 1 => {
                    s.gathered = true;
                    s.damage_percents.push(100 * num_projectiles);
                    s.rate_mult = 0.95;
                    s.sigil = true;
                }
                _ => {}
            }
        }
        s
    }

    pub fn damage(&self, roll: i32) -> i32 {
        self.damage_percents.iter().fold(roll, |r, p| r * p / 100)
    }

    /// The shot's lifetime in ms: the range it's set to, else the weapon's, then scaled.
    pub fn lifetime(&self, speed: i32, lifetime: i32) -> i32 {
        let life = if self.range_tenths > 0 && speed > 0 { self.range_tenths * 1000 / speed } else { lifetime };
        life * self.range_percent / 100
    }

    pub fn num_projectiles(&self, own: i32) -> i32 {
        if self.gathered { 1 } else { own }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_the_servers_item_data() {
        assert_eq!(parse(r#"{"ObjectId":"Enthroned Staff","Stack":0,"MaxStack":0,"Imprints":[1,12,22]}"#), [1, 12, 22, 0]);
        assert_eq!(parse(r#"{"Imprints":[0,0,21,201515]}"#), [0, 0, 21, 201515]);
        assert_eq!(parse("{}"), [0; 4]);
        assert_eq!(parse(r#"{"ObjectId":null,"Stack":0,"MaxStack":0}"#), [0; 4]);
    }

    #[test]
    fn a_choice_round_trips_as_the_server_stores_it() {
        let v = with_choice(APOTHEOSIS_OF_BALANCE, 2, [0, 7]);
        assert_eq!(id_of(v), APOTHEOSIS_OF_BALANCE);
        assert_eq!(get(v as i64).map(|i| i.id), Some(APOTHEOSIS_OF_BALANCE));
        assert_eq!(choice(v), Some((2, [0, 7])));
        assert_eq!(choice(APOTHEOSIS_OF_BALANCE as u32), None);
        // the same stat twice isn't a choice
        assert_eq!(choice(with_choice(APOTHEOSIS_OF_BALANCE, 3, [3, 4])), None);
    }

    #[test]
    fn costs_and_fits() {
        assert_eq!(get(KEEN_EDGE as i64).unwrap().cost(), 1);
        assert_eq!(get(WOUNDS as i64).unwrap().cost(), 3);
        assert_eq!(get(GUARDIAN_ANGEL as i64).unwrap().cost(), 20);
        assert_eq!(get(APOTHEOSIS_OF_WARD as i64).unwrap().cost(), 5);
        // Vigor and Bulwark go on anything but a ring; Blood Pact is gone
        assert!(get(VIGOR as i64).unwrap().fits(6) && !get(VIGOR as i64).unwrap().fits(RING));
        assert!(get(2).is_none());
    }

    #[test]
    fn shots_work_out_as_the_servers() {
        // a bow with Keen Edge and Gathered Shot: 105%, then three arrows' worth, one arrow
        let s = Shot::of([KEEN_EDGE as u32, 0, GATHERED_SHOT as u32, 0], BOW, 3);
        assert_eq!(s.damage(100), 315);
        assert_eq!(s.num_projectiles(3), 1);
        assert_eq!(s.rate_mult, 0.95);
        assert!(s.sigil);
        // Spearform on a sword (speed 100, 400 ms): range 8 is 800 ms, 30% less; on a dagger nothing
        let s = Shot::of([0, 0, SPEARFORM as u32, 0], SWORD, 1);
        assert_eq!((s.lifetime(100, 400), s.damage(333)), (800, 233));
        assert_eq!(Shot::of([0, 0, SPEARFORM as u32, 0], DAGGER, 1), Shot::default());
        // Close Quarters: 75% of the lifetime, +10%
        let s = Shot::of([CLOSE_QUARTERS as u32, 0, 0, 0], WAND, 1);
        assert_eq!((s.lifetime(200, 400), s.damage(100)), (300, 110));
        // Sunder: range 4 at speed 125, 10% faster
        let s = Shot::of([0, 0, SUNDER as u32, 0], FISTS, 1);
        assert_eq!((s.lifetime(125, 400), s.rate_mult), (320, 1.1));
        // Battle Mage: range 5
        assert_eq!(Shot::of([0, 0, BATTLE_MAGE as u32, 0], WAND, 1).lifetime(200, 400), 250);
    }
}
