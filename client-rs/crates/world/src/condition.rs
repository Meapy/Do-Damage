//! Condition effects (`ConditionEffect.as`): bit `n - 1` of the first batch for effects 1..31,
//! bit `n - 32` of the second batch for effects 32 and up.

pub const DEAD: u32 = 1;
pub const QUIET: u32 = 2;
pub const WEAK: u32 = 3;
pub const SLOWED: u32 = 4;
pub const SICK: u32 = 5;
pub const DAZED: u32 = 6;
pub const STUNNED: u32 = 7;
pub const BLIND: u32 = 8;
pub const HALLUCINATING: u32 = 9;
pub const DRUNK: u32 = 10;
pub const CONFUSED: u32 = 11;
pub const STUN_IMMUNE: u32 = 12;
pub const INVISIBLE: u32 = 13;
pub const PARALYZED: u32 = 14;
pub const SPEEDY: u32 = 15;
pub const BLEEDING: u32 = 16;
pub const HEALING: u32 = 18;
pub const DAMAGING: u32 = 19;
pub const BERSERK: u32 = 20;
pub const PAUSED: u32 = 21;
pub const STASIS: u32 = 22;
pub const INVINCIBLE: u32 = 24;
pub const INVULNERABLE: u32 = 25;
pub const ARMORED: u32 = 26;
pub const ARMOR_BROKEN: u32 = 27;
pub const HEXED: u32 = 28;
pub const NINJA_SPEEDY: u32 = 29;
pub const UNSTABLE: u32 = 30;
pub const DARKNESS: u32 = 31;
pub const PETRIFIED: u32 = 35;
pub const CURSE: u32 = 38;
pub const SILENCED: u32 = 48;
pub const EXPOSED: u32 = 49;
pub const INSPIRED: u32 = 59;
/// A Priest's tome's Speedy: half its speed bonus.
pub const SWIFT: u32 = 61;

pub fn has(conditions: [u32; 2], effect: u32) -> bool {
    if effect < 32 {
        conditions[0] & (1 << (effect - 1)) != 0
    } else {
        conditions[1] & (1 << (effect - 32)) != 0
    }
}

/// An effect's name and the icons it shows over whoever has it (`ConditionEffect.effects_`):
/// indexes into lofiInterface2 (8 × 8), or lofiInterfaceBig (16 × 16) when `big`. Several icons
/// take turns, half a second each.
pub struct Effect {
    pub name: &'static str,
    pub icons: &'static [u32],
    pub big: bool,
}

const fn e(name: &'static str, icons: &'static [u32]) -> Effect {
    Effect { name, icons, big: false }
}

const fn big(name: &'static str, icons: &'static [u32]) -> Effect {
    Effect { name, icons, big: true }
}

/// Every effect, by number.
pub const EFFECTS: [Effect; 62] = [
    e("Nothing", &[]),
    e("Dead", &[]),
    e("Quiet", &[0x20]),
    e("Weak", &[0x22, 0x23, 0x24, 0x25]),
    e("Slowed", &[0x01]),
    e("Sick", &[0x27]),
    e("Dazed", &[0x2c]),
    e("Stunned", &[0x2d]),
    e("Blind", &[0x29]),
    e("Hallucinating", &[0x2a]),
    e("Drunk", &[0x2b]),
    e("Confused", &[0x02]),
    e("Stun Immune", &[]),
    e("Invisible", &[]),
    e("Paralyzed", &[0x35, 0x36]),
    e("Speedy", &[0x00]),
    e("Bleeding", &[0x2e]),
    e("Armor Broken Immune", &[]),
    e("Healing", &[0x2f]),
    e("Damaging", &[0x31]),
    e("Berserk", &[0x32]),
    e("Paused", &[]),
    e("Stasis", &[]),
    e("Stasis Immune", &[]),
    e("Invincible", &[]),
    e("Invulnerable", &[0x11]),
    e("Armored", &[0x10]),
    e("Armor Broken", &[0x37]),
    e("Hexed", &[0x2a]),
    e("Ninja Speedy", &[0x00]),
    e("Unstable", &[0x38]),
    e("Darkness", &[0x39]),
    e("Slowed Immune", &[]),
    e("Dazed Immune", &[]),
    e("Paralyzed Immune", &[]),
    e("Petrify", &[]),
    e("Petrify Immune", &[]),
    big("Pet Disable", &[0x1B]),
    e("Curse", &[0x3a]),
    e("Curse Immune", &[]),
    big("HP Boost", &[0x20]),
    big("MP Boost", &[0x21]),
    big("Att Boost", &[0x22]),
    big("Def Boost", &[0x23]),
    big("Spd Boost", &[0x24]),
    big("Vit Boost", &[0x26]),
    big("Wis Boost", &[0x27]),
    big("Dex Boost", &[0x25]),
    e("Silenced", &[0x21]),
    e("Exposed", &[0x3b]),
    e("Energized", &[0x3c]),
    big("HP Debuff", &[0x30]),
    big("MP Debuff", &[0x31]),
    big("Att Debuff", &[0x32]),
    big("Def Debuff", &[0x33]),
    big("Spd Debuff", &[0x34]),
    big("Vit Debuff", &[0x36]),
    big("Wis Debuff", &[0x37]),
    big("Dex Debuff", &[0x35]),
    e("Inspired", &[0x3e]),
    e("Mana Deplete", &[]),
    e("Swift", &[0x00]),
];

/// The effects set, lowest bit first in each batch (the order `getConditionEffectIcons` lists
/// them).
pub fn active(conditions: [u32; 2]) -> impl Iterator<Item = u32> {
    (1..EFFECTS.len() as u32).filter(move |&n| has(conditions, n))
}

pub fn duration_stat(effect: u32) -> u8 {
    bsk_proto::stat::condition_duration(effect as u8)
}

/// The icons over an object at `time` (ms): (effect, big, index) for each effect that shows one.
pub fn icons(conditions: [u32; 2], time: i32) -> Vec<(u32, bool, u32)> {
    let turn = (time / 500).max(0) as usize;
    active(conditions)
        .filter_map(|n| {
            let e = &EFFECTS[n as usize];
            (!e.icons.is_empty()).then(|| (n, e.big, e.icons[turn % e.icons.len()]))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn icons_cycle() {
        let c = [1 << (PARALYZED - 1) | 1 << (SLOWED - 1), 1 << (CURSE - 32)];
        assert_eq!(
            icons(c, 0),
            vec![
                (SLOWED, false, 0x01),
                (PARALYZED, false, 0x35),
                (CURSE, false, 0x3a)
            ]
        );
        assert_eq!(
            icons(c, 500),
            vec![
                (SLOWED, false, 0x01),
                (PARALYZED, false, 0x36),
                (CURSE, false, 0x3a)
            ]
        );
        assert_eq!(EFFECTS[DRUNK as usize].name, "Drunk");
        assert_eq!(EFFECTS[INSPIRED as usize].name, "Inspired");
    }
}
