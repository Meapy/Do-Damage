//! Pet levels and food, as the server works them out (`Shared.pets.PetLevels` and `PetFood`, with
//! the same checks in `source/Tests/PetLevelsTests.cs`): change both together. Every pet hatches at
//! level 1 and grows as the Pen Keeper feeds it, up to its size's last level (Tiny 10, Small 15,
//! Medium 20, Large 25, Oryx 30). Its damage goes from 60% of its kit's (its stone's `petDps`) to
//! 120%, its healing from its stone's `petHp` and `petMp` to 160%, and it's drawn up to a quarter
//! bigger. Feeding costs fame, more the stronger it is (`fame_cost`). The tooltips and the Pen
//! Keeper's window show these; the server decides.

use bsk_data::GameData;

pub const SIZES: [&str; 5] = ["Tiny", "Small", "Medium", "Large", "Oryx"];
const MAX_LEVELS: [i32; 5] = [10, 15, 20, 25, 30];
const DAMAGE_AT_FIRST: f32 = 0.6;
const DAMAGE_AT_LAST: f32 = 1.2;
const HEAL_AT_LAST: f32 = 1.6;

/// What its stone says of a pet: its `PermaPet` activate.
pub fn stone(data: &GameData, item: i32) -> Option<&bsk_data::xml::Element> {
    data.objects.get(&(item as u16))?.xml.children_named("Activate").find(|a| a.text.trim() == "PermaPet")
}

/// The size's index in `SIZES` (None for an unsized pet, which doesn't level).
pub fn size_index(size: Option<&str>) -> Option<usize> {
    SIZES.iter().position(|&s| Some(s) == size)
}

/// The last level a pet of this size reaches (1 for an unsized one).
pub fn max_level(size: Option<&str>) -> i32 {
    size_index(size).map_or(1, |i| MAX_LEVELS[i])
}

/// The last level the pet of this stone reaches.
pub fn stone_max_level(data: &GameData, item: i32) -> i32 {
    max_level(stone(data, item).and_then(|a| a.attr("petSize")))
}

/// The food from `level` to the next: 30 x level^1.5, to the nearest 5.
pub fn xp_to_next(level: i32) -> i32 {
    if level < 1 {
        return 0;
    }
    (30.0 * (level as f64).powf(1.5) / 5.0).round_ties_even() as i32 * 5
}

/// The food a pet has eaten when it reaches `level`.
pub fn xp_at(level: i32) -> i32 {
    (1..level).map(xp_to_next).sum()
}

/// The level `xp` of food makes, no higher than `max`.
pub fn level_for(xp: i32, max: i32) -> i32 {
    let mut level = 1;
    while level < max && xp >= xp_at(level + 1) {
        level += 1;
    }
    level
}

fn along(level: i32, max: i32) -> f32 {
    if max <= 1 {
        return 1.0;
    }
    ((level - 1) as f32 / (max - 1) as f32).clamp(0.0, 1.0)
}

/// Its damage, times its kit's.
pub fn damage_scale(level: i32, max: i32) -> f32 {
    if max <= 1 {
        return 1.0;
    }
    DAMAGE_AT_FIRST + (DAMAGE_AT_LAST - DAMAGE_AT_FIRST) * along(level, max)
}

/// Its healing, times its stone's.
pub fn heal_scale(level: i32, max: i32) -> f32 {
    if max <= 1 {
        return 1.0;
    }
    1.0 + (HEAL_AT_LAST - 1.0) * along(level, max)
}

/// A heal at `level`: its stone's amount, grown.
pub fn heal(amount: i32, level: i32, max: i32) -> i32 {
    ((amount as f32 * heal_scale(level, max)) as f64).round_ties_even() as i32
}

/// Its damage a second at `level`, from its stone's.
pub fn dps(dps: i32, level: i32, max: i32) -> i32 {
    ((dps as f32 * damage_scale(level, max)) as f64).round_ties_even() as i32
}

const FAME_PER_FOOD: f64 = 0.03;
const FAME_GROWTH: f64 = 0.15;

/// The fame `food` costs a pet that has eaten `xp`: 3 for each 100 food it eats at level 1, 15%
/// more at each level after, only what it eats (none past its last level), rounded up.
pub fn fame_cost(xp: i32, food: i32, max: i32) -> i32 {
    if max <= 1 || food <= 0 {
        return 0;
    }
    let end = (xp as i64 + food as i64).min(xp_at(max) as i64);
    let mut fame = 0.0;
    let mut at = xp as i64;
    while at < end {
        let level = level_for(at as i32, max);
        let next = end.min(xp_at(level + 1) as i64);
        fame += (next - at) as f64 * FAME_PER_FOOD * (1.0 + FAME_GROWTH * (level - 1) as f64);
        at = next;
    }
    (fame - 1e-9).ceil() as i32
}

/// The food for a pet from before levels: raised by the Pen Keeper, at its last level; any other
/// two thirds of the way.
pub fn migrated_xp(old_raise: i32, max: i32) -> i32 {
    if max <= 1 {
        0
    } else if old_raise > 0 {
        xp_at(max)
    } else {
        xp_at((1.0 + (max - 1) as f64 * 2.0 / 3.0).round_ties_even() as i32)
    }
}

/// A number in a slot's data (`"PetXp":120`), if it's there.
fn data_int(json: &str, key: &str) -> Option<i32> {
    let i = json.find(&format!("\"{key}\":"))?;
    let rest = json[i + key.len() + 3..].trim_start();
    let end = rest.find(|c: char| !(c.is_ascii_digit() || c == '-')).unwrap_or(rest.len());
    rest[..end].parse().ok()
}

/// The food in a pet stone, from its slot's data (`ItemData.PetXp`, or a raise from before levels).
pub fn stone_xp(data: &GameData, item: i32, json: &str) -> i32 {
    if let Some(xp) = data_int(json, "PetXp") {
        return xp.max(0);
    }
    match data_int(json, "PetUpgrade") {
        Some(up) if up > 0 => migrated_xp(up, stone_max_level(data, item)),
        _ => 0,
    }
}

/// How many are stacked in a slot (`ItemData.Stack`; 0 or none for one).
pub fn stack(json: &str) -> i32 {
    data_int(json, "Stack").unwrap_or(0)
}

const SHARD: &str = "Shard of the Mad God x";
const STONE_XP: [i32; 5] = [150, 400, 1200, 3000, 8000];
/// The weapons' and armours' slot types, whose tiers go to 14 and on.
const LONG_TIERS: [i32; 9] = [1, 2, 3, 6, 7, 8, 14, 17, 24];
/// Untiered gear's food by its bag (BagType 0-9, white 6 the rarest), Ascended gear's.
const UNTIERED_XP: [i32; 10] = [150, 200, 300, 400, 600, 800, 2000, 1000, 1200, 1500];
const ASCENDED_XP: i32 = 3000;

/// One of `item` fed to a pet, 0 if pets don't eat it.
pub fn food(data: &GameData, item: i32) -> i32 {
    let Some(desc) = data.objects.get(&(item as u16)) else { return 0 };
    let (id, xml) = (desc.id.as_str(), &desc.xml);
    if id == "Superburger" {
        return 1500;
    }
    if id == "Ambrosia" {
        return 5000;
    }
    if let Some(n) = id.strip_prefix(SHARD) {
        let quantity = xml.int("Quantity", 0);
        return 200 * if quantity > 0 { quantity } else { n.trim().parse::<i32>().unwrap_or(1).max(1) };
    }
    if let Some(a) = stone(data, item) {
        return size_index(a.attr("petSize")).map_or(0, |i| STONE_XP[i]);
    }
    if id.starts_with("Greater Potion of ") {
        return 150;
    }
    if id == "Potion of Life" || id == "Potion of Mana" {
        return 200;
    }
    if id.starts_with("Potion of ") && xml.has("Potion") {
        return 60;
    }
    let slot = xml.int("SlotType", 0);
    if slot <= 0 || slot == 10 || xml.has("Consumable") {
        return 0;
    }
    match xml.text_of("Tier").and_then(|t| t.trim().parse::<i32>().ok()) {
        Some(t) if t >= 0 => {
            if LONG_TIERS.contains(&slot) {
                20 + 15 * t
            } else {
                20 + 30 * t
            }
        }
        _ if xml.has("Ascended") => ASCENDED_XP,
        _ => UNTIERED_XP[xml.int("BagType", 0).clamp(0, 9) as usize],
    }
}

/// The food in an inventory slot: its item's, times its stack, and a pet stone's half the food
/// in it as well.
pub fn slot_food(data: &GameData, item: i32, json: &str) -> i32 {
    let value = food(data, item) * stack(json).max(1);
    if value > 0 && stone(data, item).is_some() {
        value + stone_xp(data, item, json) / 2
    } else {
        value
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn levels_grow_and_stop_at_the_sizes_last() {
        assert_eq!(SIZES.map(|s| max_level(Some(s))), [10, 15, 20, 25, 30]);
        assert_eq!(max_level(None), 1);
        assert_eq!(max_level(Some("Huge")), 1);
        assert_eq!((xp_to_next(1), xp_to_next(2), xp_to_next(10)), (30, 85, 950));
        assert_eq!((xp_at(1), xp_at(3), xp_at(30)), (0, 115, 56705));
        for max in (10..=30).step_by(5) {
            assert_eq!(level_for(0, max), 1);
            assert_eq!(level_for(xp_at(max), max), max);
            assert_eq!(level_for(i32::MAX / 2, max), max);
            for l in 1..max {
                assert_eq!(level_for(xp_at(l), max), l);
                assert_eq!(level_for(xp_at(l + 1) - 1, max), l);
            }
        }
    }

    #[test]
    fn power_grows_from_its_first_level_to_its_last() {
        assert!((damage_scale(1, 20) - 0.6).abs() < 1e-3);
        assert!((damage_scale(20, 20) - 1.2).abs() < 1e-3);
        assert!((heal_scale(20, 20) - 1.6).abs() < 1e-3);
        assert_eq!(heal(10, 25, 25), 16);
        assert_eq!(dps(1000, 1, 30), 600);
        assert_eq!(dps(1000, 30, 30), 1200);
        assert_eq!((damage_scale(1, 1), heal_scale(1, 1)), (1.0, 1.0));
    }

    #[test]
    fn feeding_costs_more_fame_as_the_pet_grows() {
        assert_eq!(fame_cost(0, 30, 30), 1);
        assert_eq!(fame_cost(0, 1500, 1), 0);
        assert_eq!(fame_cost(xp_at(30), 5000, 30), 0);
        assert!((45..=75).contains(&fame_cost(0, 1500, 30)));
        assert!((210..=270).contains(&fame_cost(xp_at(28), 1500, 30)));
        assert!((170..=200).contains(&fame_cost(0, i32::MAX / 2, 10)));
        assert!((6600..=7000).contains(&fame_cost(0, i32::MAX / 2, 30)));
    }

    #[test]
    fn pets_from_before_levels_keep_their_strength() {
        for max in (10..=30).step_by(5) {
            assert_eq!(level_for(migrated_xp(1, max), max), max);
            let level = level_for(migrated_xp(0, max), max);
            assert!((0.95..=1.05).contains(&damage_scale(level, max)));
        }
        assert_eq!(migrated_xp(2, 1), 0);
    }

    #[test]
    fn slot_data_reads() {
        assert_eq!(data_int(r#"{"Stack":0,"MaxStack":0,"PetXp":1234}"#, "PetXp"), Some(1234));
        assert_eq!(data_int(r#"{"Stack":5}"#, "PetXp"), None);
        assert_eq!(stack(r#"{"Stack":5,"MaxStack":10}"#), 5);
        assert_eq!(stack(""), 0);
    }
}
