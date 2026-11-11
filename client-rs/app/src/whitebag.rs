//! A white bag landing near us: a bright crystal "ting" (`white_bag`, made by
//! tools/make_white_bag_sound.py) and the edges of the world view lighting up in a white bag's
//! white and blue for a moment. The bar down the right is left alone: only the world flashes.

use bsk_render::Renderer;

/// How long the flash lasts, ms.
pub const FLASH_MS: i32 = 1400;
/// The sound it plays.
pub const SOUND: &str = "white_bag";
/// The bags that set it off: a white bag, and its boosted look.
pub const BAGS: [&str; 2] = ["Loot Bag 6", "Loot Bag 6 Boost"];

/// A white bag's colours: the bright edge, and the blue it fades through.
const EDGE: u32 = 0xFFFFFF;
const TINT: u32 = 0x8FD3FF;

/// How bright the flash is `t` ms in, 0 to 1: up in a blink, then two soft pulses dying away.
fn strength(t: i32) -> f32 {
    if !(0..FLASH_MS).contains(&t) {
        return 0.0;
    }
    let rise = (t as f32 / 90.0).min(1.0);
    let u = t as f32 / FLASH_MS as f32;
    let fall = (1.0 - u).powi(2);
    let pulse = 0.8 + 0.2 * (t as f32 / 1000.0 * std::f32::consts::TAU * 2.0).cos();
    rise * fall * pulse
}

/// Draw the flash `t` ms in round `view` (x, y, w, h: the world, without the bar), `s` the HUD's
/// scale.
pub fn draw(renderer: &mut Renderer, view: [f32; 4], s: f32, t: i32) {
    let a = strength(t);
    if a <= 0.0 {
        return;
    }
    let [x, y, w, h] = view;
    let depth = (110.0 * s).min(w.min(h) / 4.0);
    let white = renderer.white();
    let col = |rgb: u32, alpha: f32| {
        let [r, g, b] = bsk_render::rgb_f32(rgb);
        [r, g, b, alpha]
    };
    let (edge, mid, clear) = (col(EDGE, 0.95 * a), col(TINT, 0.6 * a), col(TINT, 0.0));
    // each side: a thin bright band at the edge, the blue fading inwards from it
    let band = depth * 0.3;
    let mut quad = |p: [[f32; 2]; 4], c: [[f32; 4]; 4]| {
        renderer.triangle_colors(white, [p[0], p[1], p[2]], [[0.5, 0.5]; 3], [c[0], c[1], c[2]]);
        renderer.triangle_colors(white, [p[0], p[2], p[3]], [[0.5, 0.5]; 3], [c[0], c[2], c[3]]);
    };
    for (outer, inner) in [(0.0, band), (band, depth)] {
        let (co, ci) = if outer == 0.0 { (edge, mid) } else { (mid, clear) };
        // top, bottom, left, right: the outer edge first
        quad([[x, y + outer], [x + w, y + outer], [x + w, y + inner], [x, y + inner]], [co, co, ci, ci]);
        quad([[x, y + h - outer], [x + w, y + h - outer], [x + w, y + h - inner], [x, y + h - inner]], [co, co, ci, ci]);
        quad([[x + outer, y], [x + outer, y + h], [x + inner, y + h], [x + inner, y]], [co, co, ci, ci]);
        quad([[x + w - outer, y], [x + w - outer, y + h], [x + w - inner, y + h], [x + w - inner, y]], [co, co, ci, ci]);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn flash_rises_and_dies_away() {
        assert_eq!(strength(-1), 0.0);
        assert!(strength(100) > 0.6);
        assert!(strength(FLASH_MS - 50) < 0.05);
        assert_eq!(strength(FLASH_MS), 0.0);
    }
}
