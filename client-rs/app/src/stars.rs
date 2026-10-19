//! `FameUtil`: the rank stars. Each class has five class quests, one for each fame in `STARS`
//! that its best character reached; an account's rank is all of them together, and the star's
//! colour moves up one of nine steps for each ninth of the most stars there are, ending in white.

use bsk_render::{Renderer, Tint};

/// The fame a character of a class must reach for each of its class quests.
pub const STARS: [i32; 5] = [20, 150, 400, 800, 2000];

/// `FameUtil.numStars`: the class quests a best fame completes.
pub fn num_stars(fame: i32) -> i32 {
    STARS.iter().take_while(|&&f| fame >= f).count() as i32
}

/// `FameUtil.nextStarFame`: the fame of the next class quest, if one is left.
pub fn next_star_fame(best: i32, current: i32) -> Option<i32> {
    let reached = best.max(current);
    STARS.iter().copied().find(|&f| f > reached)
}

/// `SavedCharactersList.numStars_`: the account's rank, from each class's best fame.
pub fn account_stars(list: &bsk_net::account::CharList) -> i32 {
    list.class_stats.values().map(|&(_, fame)| num_stars(fame)).sum()
}

/// `FameUtil.maxStars`: every class quest of every class.
pub fn max_stars(classes: i32) -> i32 {
    classes * STARS.len() as i32
}

/// The number of classes (`ObjectLibrary.playerChars_`), which sets the colour steps.
pub fn class_count(data: &bsk_data::GameData) -> i32 {
    crate::screens::player_classes(data).len() as i32
}

/// The colour steps, lowest first: light blue, blue, violet, red, red-orange, orange, gold,
/// pale yellow and white (the AIR client had five, `FameUtil.COLORS`, up to yellow).
pub const COLORS: [u32; 9] = [0x8A98DE, 0x314DDB, 0x7B3FD6, 0xC1272D, 0xEE5A24, 0xF7931E, 0xFFD21F, 0xFFF68F, 0xFFFFFF];

/// The rank the server gives staff (`Player.STAFF_STARS`): their star is green.
pub const STAFF: i32 = 1000;

/// The grey of an uncoloured star (the tooltips' and the class quest line's).
pub const GREY: u32 = 0xB3B3B3;

/// The star's colour: the step for each ninth of `max_stars` reached (white only near the top);
/// green for staff.
pub fn color(stars: i32, classes: i32) -> u32 {
    if stars >= STAFF {
        return 0x00FF50;
    }
    let steps = COLORS.len() as i32;
    let step = (stars * steps / max_stars(classes).max(1)).clamp(0, steps - 1);
    COLORS[step as usize]
}

/// `FameUtil.numStarsToIcon`: a star on a faint dark disc, centred on (x, y); `k` is 1 for
/// the AIR client's size (16 px across).
pub fn icon(renderer: &mut Renderer, x: f32, y: f32, k: f32, rgb: u32) {
    let white = renderer.white();
    let disc = Tint::color(0x000000, 0.4);
    let r = 8.0 * k;
    for i in 0..16 {
        let (a0, a1) = (i as f32 / 16.0 * std::f32::consts::TAU, (i + 1) as f32 / 16.0 * std::f32::consts::TAU);
        renderer.triangle(white, [[x, y], [x + r * a0.cos(), y + r * a0.sin()], [x + r * a1.cos(), y + r * a1.sin()]], [[0.5, 0.5]; 3], disc);
    }
    crate::theme::star(renderer, x, y, 6.5 * k, rgb);
}

/// `CharacterBox.getStars`: a class's five class quests in a row from (x, cy), the `done`
/// ones light and the rest dark.
pub fn row(renderer: &mut Renderer, x: f32, cy: f32, k: f32, done: i32) {
    for i in 0..STARS.len() as i32 {
        let c = if i < done { 0xCCCCCC } else { 0x333333 };
        crate::theme::star(renderer, x + (6.5 + 13.0 * i as f32) * k, cy, 6.5 * k, c);
    }
}

/// How wide `row` draws.
pub fn row_width(k: f32) -> f32 {
    13.0 * STARS.len() as f32 * k
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn quests() {
        assert_eq!(num_stars(0), 0);
        assert_eq!(num_stars(19), 0);
        assert_eq!(num_stars(20), 1);
        assert_eq!(num_stars(799), 3);
        assert_eq!(num_stars(5000), 5);
        assert_eq!(next_star_fame(0, 0), Some(20));
        assert_eq!(next_star_fame(150, 30), Some(400));
        assert_eq!(next_star_fame(10, 900), Some(2000));
        assert_eq!(next_star_fame(2000, 0), None);
    }

    #[test]
    fn colours() {
        // 30 classes: 150 stars at most, a step every 16.7
        assert_eq!(color(0, 30), 0x8A98DE);
        assert_eq!(color(16, 30), 0x8A98DE);
        assert_eq!(color(17, 30), 0x314DDB);
        assert_eq!(color(100, 30), 0xFFD21F);
        assert_eq!(color(133, 30), 0xFFF68F);
        assert_eq!(color(134, 30), 0xFFFFFF);
        assert_eq!(color(150, 30), 0xFFFFFF);
        assert_eq!(color(STAFF, 30), 0x00FF50);
    }
}
