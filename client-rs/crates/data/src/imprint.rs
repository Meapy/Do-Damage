//! The imprints the Enchanter puts on gear: the client's copy of the server's list
//! (`Shared/resources/Imprints.cs`; keep the ids, names and rules in step). Every Enthroned item
//! (`<Ascended/>`) has an Offensive (red), a Defensive (green) and a Misc (purple) slot,
//! stored in the item's data (`"Imprints":[offensive, defensive, misc]`, 0 for none). [`Shot`]
//! is what the weapon's imprints do to its shots, worked out as the server's `ImprintShot` does.

/// An imprint's colour, which is also its slot on the item.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Kind {
    Offensive = 0,
    Defensive = 1,
    Misc = 2,
}

impl Kind {
    pub const ALL: [Kind; 3] = [Kind::Offensive, Kind::Defensive, Kind::Misc];

    pub fn name(self) -> &'static str {
        match self {
            Kind::Offensive => "Offensive",
            Kind::Defensive => "Defensive",
            Kind::Misc => "Misc",
        }
    }

    /// Its colour (`0xRRGGBB`): red, green, purple.
    pub fn color(self) -> u32 {
        match self {
            Kind::Offensive => 0xd6283a,
            Kind::Defensive => 0x2fb44f,
            Kind::Misc => 0x9446dc,
        }
    }

    /// The forge shards an imprint of this colour costs, any one of them.
    pub fn cost(self) -> &'static [&'static str] {
        match self {
            Kind::Offensive => &["Ascended Weapon Shard", "Ascended Ability Shard"],
            Kind::Defensive => &["Ascended Armor Shard"],
            Kind::Misc => &["Ascended Accessory Shard"],
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

pub const KEEN_EDGE: u8 = 1;
pub const BLOOD_PACT: u8 = 2;
pub const CLOSE_QUARTERS: u8 = 3;
pub const VIGOR: u8 = 11;
pub const BULWARK: u8 = 12;
pub const LAST_BREATH: u8 = 21;
pub const WISP_CALLER: u8 = 22;
pub const GATHERED_SHOT: u8 = 23;
pub const BATTLE_MAGE: u8 = 24;
pub const SPEARFORM: u8 = 25;
pub const WOUNDS: u8 = 26;
pub const BLOODRUSH: u8 = 27;
pub const SUNDER: u8 = 28;
pub const SIPHON: u8 = 29;

/// The object whose projectile the Battle Mage's bolts are, and their rate against the wand's.
pub const BATTLE_MAGE_BOLT: &str = "Battle Mage Bolt";
pub const BATTLE_MAGE_RATE: f64 = 0.4;
/// The bolts' angle either side of the wand's shot, in degrees (a bow's arc).
pub const BATTLE_MAGE_ARC: f64 = 10.0;

pub struct Imprint {
    pub id: u8,
    pub name: &'static str,
    pub kind: Kind,
    pub description: &'static str,
    /// The slot types it fits, or `None` for any gear.
    pub slots: Option<&'static [i32]>,
}

impl Imprint {
    pub fn fits(&self, slot_type: i32) -> bool {
        self.slots.is_none_or(|s| s.contains(&slot_type))
    }

    /// What it fits, for the window: "Weapons", "Staves", "Any gear".
    pub fn fits_text(&self) -> &'static str {
        match self.slots {
            None => "Any gear",
            Some(s) if s == WEAPONS => "Weapons",
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

pub const ALL: &[Imprint] = &[
    Imprint { id: KEEN_EDGE, name: "Keen Edge", kind: Kind::Offensive, description: "+5% damage", slots: Some(WEAPONS) },
    Imprint { id: BLOOD_PACT, name: "Blood Pact", kind: Kind::Offensive, description: "+10% damage, -10% maximum health", slots: Some(WEAPONS) },
    Imprint { id: CLOSE_QUARTERS, name: "Close Quarters", kind: Kind::Offensive, description: "+8% damage, -20% range", slots: Some(WEAPONS) },
    Imprint { id: VIGOR, name: "Vigor", kind: Kind::Defensive, description: "+20 maximum health", slots: None },
    Imprint { id: BULWARK, name: "Bulwark", kind: Kind::Defensive, description: "+3 defense, +15 maximum health", slots: None },
    Imprint {
        id: LAST_BREATH,
        name: "Last Breath",
        kind: Kind::Misc,
        description: "Below 15% health, heal 300 health (every 180 seconds at most)",
        slots: Some(&[RING]),
    },
    Imprint {
        id: WISP_CALLER,
        name: "Wisp Caller",
        kind: Kind::Misc,
        description: "Each shot: a 10% chance of each of three wisps circling you for 5 seconds, each back 10 seconds after it goes: green heals you, blue makes you Armored, red curses you and enemies near you",
        slots: Some(&[STAFF]),
    },
    Imprint {
        id: GATHERED_SHOT,
        name: "Gathered Shot",
        kind: Kind::Misc,
        description: "The three arrows fly as one charged arrow doing all three's damage; 85% rate of fire",
        slots: Some(&[BOW]),
    },
    Imprint {
        id: BATTLE_MAGE,
        name: "Battle Mage",
        kind: Kind::Misc,
        description: "Range 4. Two more bolts fly beside each shot at 40% of the rate of fire, for 450 damage that ignores defense",
        slots: Some(&[WAND]),
    },
    Imprint {
        id: SPEARFORM,
        name: "Spearform",
        kind: Kind::Misc,
        description: "The sword reaches like a spear: range 8, 40% less damage",
        slots: Some(&[SWORD]),
    },
    Imprint {
        id: WOUNDS,
        name: "Wounds",
        kind: Kind::Misc,
        description: "Every hit wounds; the 25th wound on an enemy bursts, 4000 damage that ignores defense to it and everything within 4 tiles",
        slots: Some(&[DAGGER]),
    },
    Imprint {
        id: BLOODRUSH,
        name: "Bloodrush",
        kind: Kind::Misc,
        description: "Shots no longer pierce. Each kill: +4 attack, dexterity and speed for 20 seconds, stacking, each with its own time",
        slots: Some(&[KATANA]),
    },
    Imprint {
        id: SUNDER,
        name: "Sunder",
        kind: Kind::Misc,
        description: "Range 3.5. Each hit takes 1 defense from the enemy for 10 seconds, stacking, each with its own time",
        slots: Some(&[FISTS]),
    },
    Imprint { id: SIPHON, name: "Siphon", kind: Kind::Misc, description: "Each hit heals you 0.2% of the damage it does", slots: Some(&[BLADES]) },
];

pub fn get(id: i32) -> Option<&'static Imprint> {
    ALL.iter().find(|i| i.id as i32 == id)
}

/// The imprints in an item's data (the JSON the server sends per slot), by [`Kind`]: 0 for none.
pub fn parse(data: &str) -> [u8; 3] {
    let mut out = [0u8; 3];
    let Some(i) = data.find("\"Imprints\"") else { return out };
    let rest = &data[i + 10..];
    let Some(open) = rest.find('[') else { return out };
    let Some(close) = rest[open..].find(']') else { return out };
    for (k, n) in rest[open + 1..open + close].split(',').take(3).enumerate() {
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
}

impl Default for Shot {
    fn default() -> Self {
        Shot { damage_percents: Vec::new(), range_percent: 100, range_tenths: 0, no_pierce: false, gathered: false, rate_mult: 1.0, battle_mage: false }
    }
}

impl Shot {
    /// The weapon's (`slot_type`, `num_projectiles`) imprints' effect on its shots.
    pub fn of(imprints: [u8; 3], slot_type: i32, num_projectiles: i32) -> Shot {
        let mut s = Shot::default();
        for &id in &imprints {
            let Some(imp) = get(id as i32) else { continue };
            if !imp.fits(slot_type) {
                continue;
            }
            match imp.id {
                KEEN_EDGE => s.damage_percents.push(105),
                BLOOD_PACT => s.damage_percents.push(110),
                CLOSE_QUARTERS => {
                    s.damage_percents.push(108);
                    s.range_percent = 80;
                }
                SPEARFORM => {
                    s.damage_percents.push(60);
                    s.range_tenths = 80;
                }
                BATTLE_MAGE => {
                    s.range_tenths = 40;
                    s.battle_mage = true;
                }
                SUNDER => s.range_tenths = 35,
                BLOODRUSH => s.no_pierce = true,
                GATHERED_SHOT if num_projectiles > 1 => {
                    s.gathered = true;
                    s.damage_percents.push(100 * num_projectiles);
                    s.rate_mult = 0.85;
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
        assert_eq!(parse(r#"{"ObjectId":"Enthroned Spear","Stack":0,"MaxStack":0,"Imprints":[1,12,22]}"#), [1, 12, 22]);
        assert_eq!(parse("{}"), [0, 0, 0]);
        assert_eq!(parse(r#"{"ObjectId":null,"Stack":0,"MaxStack":0}"#), [0, 0, 0]);
    }

    #[test]
    fn shots_work_out_as_the_servers() {
        // a bow with Keen Edge and Gathered Shot: 105%, then three arrows' worth, one arrow
        let s = Shot::of([KEEN_EDGE, 0, GATHERED_SHOT], BOW, 3);
        assert_eq!(s.damage(100), 315);
        assert_eq!(s.num_projectiles(3), 1);
        assert_eq!(s.rate_mult, 0.85);
        // Spearform on a sword (speed 100, 400 ms): range 8 is 800 ms; on a dagger it does nothing
        let s = Shot::of([0, 0, SPEARFORM], SWORD, 1);
        assert_eq!((s.lifetime(100, 400), s.damage(333)), (800, 199));
        assert_eq!(Shot::of([0, 0, SPEARFORM], DAGGER, 1), Shot::default());
        // Close Quarters: 80% of the lifetime
        assert_eq!(Shot::of([CLOSE_QUARTERS, 0, 0], WAND, 1).lifetime(200, 400), 320);
        // Sunder: range 3.5 at speed 125
        assert_eq!(Shot::of([0, 0, SUNDER], FISTS, 1).lifetime(125, 400), 280);
    }
}
