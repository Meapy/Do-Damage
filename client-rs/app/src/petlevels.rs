//! Pet levels and food, as the server works them out (`Shared.pets.PetLevels` and `PetFood`, with
//! the same checks in `source/Tests/PetLevelsTests.cs`): change both together. Every pet hatches at
//! level 1 and grows as the Pen Keeper feeds it, up to its size's last level (Tiny 10, Small 15,
//! Medium 20, Large 25, Huge 30). Its damage goes from 60% of its kit's (its stone's `petDps`) to
//! 100% (Huge pets 120%), its healing from its stone's `petHp` and `petMp` to 140% (160%),
//! and it's drawn bigger (`power`). Feeding costs fame, more the stronger it is (`fame_cost`). At
//! its last level it can be grown to the next size (`grown_size`), levelling on to that size's
//! last without weakening, and climbing towards what pets of its new size do (`grown`). A Huge
//! stone's kit counts as at least `HUGE_FLOOR` a second (`floor`), and a Huge pet at its last
//! level can be tuned, 5% of one stat moved to another a step (`tune_scale`). The tooltips and
//! the Pen Keeper's window show these; the server decides.

use bsk_data::GameData;

pub const SIZES: [&str; 5] = ["Tiny", "Small", "Medium", "Large", "Huge"];
const MAX_LEVELS: [i32; 5] = [10, 15, 20, 25, 30];
const DAMAGE_AT_FIRST: f32 = 0.6;
const DAMAGE_AT_LAST: f32 = 1.2;
const HEAL_AT_LAST: f32 = 1.6;
/// What every pet really does of the damage, healing and bleeding above (the server's
/// `PetLevels.EffectivePercent`), a whole percent so heals round as the server's do.
const EFFECTIVE_PERCENT: i32 = 90;
const EFFECTIVENESS: f32 = EFFECTIVE_PERCENT as f32 / 100.0;

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

/// A pet of size `size` grown `grown` sizes (the server's `PetLevels.GrownSize`): no bigger than
/// Huge, an unsized one stays unsized.
pub fn grown_size(size: Option<&str>, grown: i32) -> Option<&'static str> {
    size_index(size).map(|i| SIZES[(i + grown.max(0) as usize).min(SIZES.len() - 1)])
}

/// The last level the pet of this stone reaches, grown `grown` sizes.
pub fn stone_max_level(data: &GameData, item: i32, grown: i32) -> i32 {
    max_level(grown_size(stone(data, item).and_then(|a| a.attr("petSize")), grown))
}

/// The sizes a pet stone's pet was grown, from its slot's data (`ItemData.PetGrown`).
pub fn stone_grown(json: &str) -> i32 {
    data_int(json, "PetGrown").unwrap_or(0).max(0)
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

/// The share of the climb from 60% to 120% damage that 100% is: only Huge pets go past.
const FULL_SHARE: f32 = (1.0 - DAMAGE_AT_FIRST) / (DAMAGE_AT_LAST - DAMAGE_AT_FIRST);

/// How far up its power a pet of last level `max` is at `level`, 0 to 1 (120% damage); `base_max`
/// is its stone's own last level (0: `max`, never grown). Every size but Huge stops at 100%
/// damage, at its stone's last level, and a grown pet stays there until it's Huge; an
/// Huge pet climbs on to 120% (from 100% at its stone's last level, if grown). The server's
/// `PetLevels.Power`.
pub fn power(level: i32, max: i32, base_max: i32) -> f32 {
    if max <= 1 {
        return 1.0;
    }
    let base_max = if base_max <= 1 || base_max > max { max } else { base_max };
    let oryx = max >= MAX_LEVELS[MAX_LEVELS.len() - 1];
    if oryx && base_max == max {
        along(level, max)
    } else if !oryx || level <= base_max {
        FULL_SHARE * along(level, base_max)
    } else {
        FULL_SHARE + (1.0 - FULL_SHARE) * ((level - base_max) as f32 / (max - base_max) as f32).clamp(0.0, 1.0)
    }
}

/// The damage a second each size's kits are made for, and the HP a second each heals at level 1
/// (the server's `PetLevels.SizeDps` and `SizeHeal`).
const SIZE_DPS: [f32; 5] = [50.0, 125.0, 250.0, 500.0, 2000.0];
const SIZE_HEAL: [f32; 5] = [4.5, 6.0, 9.0, 16.0, 22.0];
/// A grown pet's share of what pets of its new size do more than its stone's (from 2.6.7).
const GROWN_SHARE: f32 = 0.85;
const GROWN_HUGE_SHARE: f32 = 0.55;
const HEAL_SHARE: f32 = 0.85;

fn grown_at(b: usize, k: usize, damage: bool) -> f32 {
    if k <= b {
        1.0
    } else if damage {
        SIZE_DPS[k] / SIZE_DPS[b] * if k == SIZES.len() - 1 { GROWN_HUGE_SHARE } else { GROWN_SHARE }
    } else {
        SIZE_HEAL[k] / SIZE_HEAL[b] * HEAL_SHARE
    }
}

/// What growing has made of a pet at `level` whose stone's last level is `base_max`, times its
/// kit's damage (or healing): 1 up to its stone's last level, then climbing through each size it
/// grew to, level by level (the server's `PetLevels.Grown`).
pub fn grown(level: i32, base_max: i32, damage: bool) -> f32 {
    let Some(b) = MAX_LEVELS.iter().position(|&m| m == base_max) else { return 1.0 };
    let mut scale = 1.0;
    let mut k = b + 1;
    while k < SIZES.len() && level > MAX_LEVELS[k - 1] {
        let t = ((level - MAX_LEVELS[k - 1]) as f32 / (MAX_LEVELS[k] - MAX_LEVELS[k - 1]) as f32).clamp(0.0, 1.0);
        let from = grown_at(b, k - 1, damage);
        scale = from + (grown_at(b, k, damage) - from) * t;
        k += 1;
    }
    scale
}

fn base_max_of(max: i32, base_max: i32) -> i32 {
    if base_max <= 1 || base_max > max {
        max
    } else {
        base_max
    }
}

/// Its damage, times its kit's.
pub fn damage_scale(level: i32, max: i32, base_max: i32) -> f32 {
    if max <= 1 {
        return 1.0;
    }
    (DAMAGE_AT_FIRST + (DAMAGE_AT_LAST - DAMAGE_AT_FIRST) * power(level, max, base_max)) * grown(level, base_max_of(max, base_max), true)
}

/// Its healing, times its stone's.
pub fn heal_scale(level: i32, max: i32, base_max: i32) -> f32 {
    if max <= 1 {
        return 1.0;
    }
    (1.0 + (HEAL_AT_LAST - 1.0) * power(level, max, base_max)) * grown(level, base_max_of(max, base_max), false)
}

/// A heal at `level`: its stone's amount, grown, times `tune` (its tuning's `tune_scale`).
pub fn heal(amount: i32, level: i32, max: i32, base_max: i32, tune: f32) -> i32 {
    ((amount as f32 * heal_scale(level, max, base_max) * tune) as f64).round_ties_even() as i32
}

/// Its damage a second at `level`, from its stone's.
pub fn dps(dps: i32, level: i32, max: i32, base_max: i32) -> i32 {
    ((dps as f32 * damage_scale(level, max, base_max)) as f64).round_ties_even() as i32
}

/// The least a Huge stone's kit is taken as doing, a second (from 2.6.7).
pub const HUGE_FLOOR: i32 = 1400;

/// Times its kit's damage for a pet whose stone is `size` and says it does `dps` a second: a
/// Huge stone's (an Oryx's, a boss's) at least `HUGE_FLOOR` (the server's `PetLevels.Floor`).
pub fn floor(size: Option<&str>, dps: i32) -> f32 {
    if size == Some(SIZES[SIZES.len() - 1]) && dps > 0 && dps < HUGE_FLOOR {
        HUGE_FLOOR as f32 / dps as f32
    } else {
        1.0
    }
}

/// Tuning (from 2.6.7): each step moves `TUNE_STEP`% of one stat (0 damage, 1 HP healing, 2 MP
/// healing) to another, at most `TUNE_MAX` steps either way on each.
pub const TUNE_STEP: i32 = 5;
pub const TUNE_MAX: i32 = 5;

/// Whether a pet of this size (as grown) at this level can be tuned: Huge, at its last level.
pub fn can_tune(size: Option<&str>, level: i32) -> bool {
    size == Some(SIZES[SIZES.len() - 1]) && level >= MAX_LEVELS[MAX_LEVELS.len() - 1]
}

/// Times what stat `stat` would be untuned, for a pet tuned `tune` steps.
pub fn tune_scale(tune: [i32; 3], stat: usize) -> f32 {
    1.0 + tune.get(stat).map_or(0, |v| (*v).clamp(-TUNE_MAX, TUNE_MAX)) as f32 * TUNE_STEP as f32 / 100.0
}

/// How a pet stone's pet was tuned, from its slot's data (`ItemData.PetTune`, `[1,-1,0]`).
pub fn stone_tune(json: &str) -> [i32; 3] {
    let mut tune = [0; 3];
    let Some(i) = json.find("\"PetTune\":[") else { return tune };
    let rest = &json[i + 11..];
    let list = &rest[..rest.find(']').unwrap_or(rest.len())];
    for (k, v) in list.split(',').take(3).enumerate() {
        tune[k] = v.trim().parse::<i32>().unwrap_or(0).clamp(-TUNE_MAX, TUNE_MAX);
    }
    tune
}

/// A heal or a bleed as the pet really does it: times `EFFECTIVE_PERCENT`, rounded half away from
/// zero in whole numbers (the server's `PetLevels.Effective`).
pub fn effective(amount: i32) -> i32 {
    (amount * EFFECTIVE_PERCENT + if amount < 0 { -50 } else { 50 }) / 100
}

/// Its damage a second at `level` as it really does it (what the server's Pen Keeper window sends).
pub fn effective_dps(dps: f32, level: i32, max: i32, base_max: i32) -> i32 {
    ((dps * damage_scale(level, max, base_max) * EFFECTIVENESS) as f64).round_ties_even() as i32
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
pub(crate) fn data_int(json: &str, key: &str) -> Option<i32> {
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
        Some(up) if up > 0 => migrated_xp(up, stone_max_level(data, item, 0)),
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
/// The Realm Pass's treats (from 2.6.6): fed without fame.
const TREATS: [(&str, i32); 3] = [("Pet Biscuit", 750), ("Hearty Pet Stew", 2500), ("Royal Pet Feast", 7500)];
/// The Realm Pass's Growth Tonic: not food; the Pen Keeper grows a Tiny, Small or Medium pet at
/// its last level a size with it, for nothing (`tonic_grows`).
pub const GROWTH_TONIC: &str = "Growth Tonic";

/// Whether feeding the item `id` costs no fame (the Realm Pass's treats).
pub fn treat_free(id: &str) -> bool {
    TREATS.iter().any(|(t, _)| *t == id)
}

/// Whether a Growth Tonic grows a pet of size index `size` (`SIZES`): Tiny, Small and Medium.
pub fn tonic_grows(size: usize) -> bool {
    size < 3
}

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
    if let Some((_, xp)) = TREATS.iter().find(|(t, _)| *t == id) {
        return *xp;
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
        assert_eq!(max_level(Some("Giant")), 1);
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
        // every size but Huge stops at 100% damage (140% healing); Huge goes on to 120% (160%)
        assert!((damage_scale(1, 20, 0) - 0.6).abs() < 1e-3);
        assert!((damage_scale(20, 20, 0) - 1.0).abs() < 1e-3);
        assert!((heal_scale(20, 20, 0) - 1.4).abs() < 1e-3);
        assert!((heal_scale(30, 30, 0) - 1.6).abs() < 1e-3);
        assert_eq!(heal(10, 25, 25, 0, 1.0), 14);
        assert_eq!(dps(1000, 1, 30, 0), 600);
        assert_eq!(dps(1000, 30, 30, 0), 1200);
        assert_eq!((damage_scale(1, 1, 0), heal_scale(1, 1, 0)), (1.0, 1.0));
    }

    #[test]
    fn growing_never_weakens_a_pet() {
        for grown_by in 1..=4 {
            let max = max_level(grown_size(Some("Tiny"), grown_by));
            for level in 2..=max {
                assert!(damage_scale(level, max, 10) >= damage_scale(level.min(10), 10, 0) - 1e-4);
                assert!(damage_scale(level, max, 10) > damage_scale(level - 1, max, 10));
                assert!(heal_scale(level, max, 10) > heal_scale(level - 1, max, 10));
            }
        }
        // just grown, as it was; then towards its new size's kits (the server's checks)
        let near = |a: f32, b: f32| (a - b).abs() < 1e-3;
        assert!(near(damage_scale(10, 15, 10), 1.0));
        assert!(near(damage_scale(15, 15, 10), 2.125));
        assert!(near(damage_scale(25, 25, 10), 8.5));
        assert!(near(damage_scale(20, 30, 10), 1.1 * 4.25));
        assert!(near(damage_scale(30, 30, 10), 1.2 * 22.0));
        assert!(near(damage_scale(25, 30, 25), 1.0));
        assert!(near(damage_scale(30, 30, 25), 1.2 * 2.2));
        assert!(near(heal_scale(30, 30, 25), 1.6 * 22.0 / 16.0 * 0.85));
        assert!(near(grown(25, 25, true), 1.0));
    }

    #[test]
    fn huge_stones_have_a_floor_and_maxed_huge_pets_tune() {
        assert!((floor(Some("Huge"), 1000) - 1.4).abs() < 1e-3);
        assert_eq!((floor(Some("Huge"), 3000), floor(Some("Large"), 100), floor(Some("Huge"), 0)), (1.0, 1.0, 1.0));
        assert!(can_tune(Some("Huge"), 30) && !can_tune(Some("Huge"), 29) && !can_tune(Some("Large"), 30));
        assert!((tune_scale([5, -5, 0], 0) - 1.25).abs() < 1e-3);
        assert!((tune_scale([1, -1, 0], 1) - 0.95).abs() < 1e-3);
        assert_eq!(heal(10, 30, 30, 0, 1.2), 19);
        assert_eq!(stone_tune(r#"{"Stack":0,"PetXp":56705,"PetGrown":1,"PetTune":[2,-3,1]}"#), [2, -3, 1]);
        assert_eq!(stone_tune(r#"{"Stack":0,"PetXp":5}"#), [0, 0, 0]);
    }

    #[test]
    fn every_pet_does_nine_tenths() {
        assert_eq!(effective_dps(1000.0, 30, 30, 0), 1080);
        assert_eq!(effective_dps(50.0, 1, 1, 0), 45);
        assert_eq!((effective(15), effective(30), effective(45), effective(60)), (14, 27, 41, 54));
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
            assert_eq!(level, (1.0 + (max - 1) as f64 * 2.0 / 3.0).round() as i32);
        }
        assert_eq!(migrated_xp(2, 1), 0);
    }

    #[test]
    fn slot_data_reads() {
        assert_eq!(data_int(r#"{"Stack":0,"MaxStack":0,"PetXp":1234}"#, "PetXp"), Some(1234));
        assert_eq!(data_int(r#"{"Stack":5}"#, "PetXp"), None);
        assert_eq!(stone_grown(r#"{"Stack":0,"PetXp":1234,"PetGrown":2}"#), 2);
        assert_eq!(stone_grown(r#"{"Stack":0,"PetXp":1234}"#), 0);
        assert_eq!((grown_size(Some("Tiny"), 2), grown_size(Some("Large"), 3), grown_size(None, 1)), (Some("Medium"), Some("Huge"), None));
        assert_eq!(stack(r#"{"Stack":5,"MaxStack":10}"#), 5);
        assert_eq!(stack(""), 0);
    }

    #[test]
    fn the_realm_pass_treats_are_free_and_the_tonic_grows_small_pets() {
        assert!(treat_free("Royal Pet Feast") && treat_free("Pet Biscuit"));
        assert!(!treat_free("Superburger") && !treat_free(GROWTH_TONIC));
        assert_eq!((0..5).map(tonic_grows).collect::<Vec<_>>(), [true, true, true, false, false]);
    }
}
