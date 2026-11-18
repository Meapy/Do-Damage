//! The Father's ward (the server's `Player.Ward.cs`): a grey shield that takes damage before a
//! player's health. The server sends anyone's in two stats, the points left (`WARD`) and the points
//! it had when raised (`WARD_MAX`); this draws it on the HP bars (a grey layer over the top of the
//! health, from the left, as long as the ward is against the most health) and as a shell of steel plates round the
//! player, a plate falling away for each twelfth of it spent. Its looks as it's raised, struck,
//! broken and fades are `particles/relic.rs` (looks 33 to 38), which also keeps its colour.

use crate::particles::WardLook;
use crate::particles::relic::WARD_GREYS;
use bsk_render::{Renderer, Tint};
use std::f32::consts::PI;

/// The stats (`StatDataType.Ward`, `WardMax`).
pub const WARD: u8 = 80;
pub const WARD_MAX: u8 = 81;

/// The ward's grey on the HP bars, and its lighter top.
pub const BAR: u32 = 0xC4C9D0;
pub const BAR_LIGHT: u32 = 0xEEF0F3;

/// How much of an HP bar the ward covers, from its left: the ward against the most health, at
/// most all of it. `None` with no ward.
pub fn cover(max: i32, ward: i32) -> Option<f32> {
    if ward <= 0 || max <= 0 {
        return None;
    }
    Some((ward as f32 / max as f32).min(1.0))
}

/// How much of the bar's height the ward's layer takes, from its top: the health shows under it.
pub const LAYER: f32 = 0.55;

/// The shell round a warded player, centred on (cx, cy) on the screen, `rad` pixels across its
/// middle: twelve plates of grey steel (as many as are left of `frac`, the rest gone, their place
/// faintly marked), a sheen running round them, the censer's colour on its inner edge. It closes in
/// as it's raised and swells and whitens when it takes a blow (`look`); low, it flickers.
pub fn shell(renderer: &mut Renderer, cx: f32, cy: f32, rad: f32, frac: f32, look: Option<WardLook>, now: i32, alpha: f32) {
    const PLATES: usize = 12;
    let color = look.map_or(WARD_GREYS[2], |l| l.color & 0xFF_FFFF);
    let since_gain = look.map_or(10_000, |l| now - l.gained);
    let since_hit = look.map_or(10_000, |l| now - l.hit);
    let g = (since_gain as f32 / 320.0).clamp(0.0, 1.0);
    let h = 1.0 - (since_hit as f32 / 260.0).clamp(0.0, 1.0);
    let mut a = alpha * g.max(0.2);
    if frac < 0.3 {
        a *= 0.75 + 0.25 * (now as f32 * 0.03).sin();
    }
    let rad = rad * (1.0 + 0.6 * (1.0 - g).powi(2)) * (1.0 + 0.08 * h);
    let (rx, ry) = (rad, rad * 1.1);
    let white = renderer.white();
    let tri = |r: &mut Renderer, pts: [[f32; 2]; 3], rgb: u32, al: f32| {
        if al > 0.005 {
            r.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(rgb, al.min(1.0)));
        }
    };
    let at = |ang: f32, k: f32| [cx + rx * k * ang.cos(), cy + ry * k * ang.sin()];
    // the air inside it, a little grey (and white as a blow lands)
    for i in 0..24 {
        let (a0, a1) = (i as f32 * 2.0 * PI / 24.0, (i + 1) as f32 * 2.0 * PI / 24.0);
        tri(renderer, [[cx, cy], at(a0, 0.78), at(a1, 0.78)], WARD_GREYS[2], (0.07 + 0.2 * h) * a);
    }
    let spin = now as f32 * 0.0004;
    let sheen = now as f32 * 0.0028;
    let lit = (frac.clamp(0.0, 1.0) * PLATES as f32).ceil() as usize;
    for i in 0..PLATES {
        let gap = 0.05;
        let (a0, a1) = (spin + i as f32 * 2.0 * PI / PLATES as f32 + gap, spin + (i + 1) as f32 * 2.0 * PI / PLATES as f32 - gap);
        let mid = (a0 + a1) / 2.0;
        let q = [at(a0, 1.0), at(a1, 1.0), at(a1, 0.78), at(a0, 0.78)];
        if i >= lit {
            // a plate gone: only where it was
            tri(renderer, [q[0], q[1], q[2]], WARD_GREYS[0], 0.12 * a);
            tri(renderer, [q[0], q[2], q[3]], WARD_GREYS[0], 0.12 * a);
            continue;
        }
        // lit from the upper left, a sheen running round
        let light = 0.5 + 0.5 * (mid + 0.75 * PI).cos();
        let mut c = mix(WARD_GREYS[1], WARD_GREYS[3], light);
        let d = ((mid - sheen).rem_euclid(2.0 * PI) - PI).abs();
        if d > PI - 0.5 {
            c = mix(c, 0xFFFFFF, (d - (PI - 0.5)) / 0.5);
        }
        c = mix(c, 0xFFFFFF, h * 0.7);
        let al = (0.62 + 0.3 * h) * a;
        tri(renderer, [q[0], q[1], q[2]], c, al);
        tri(renderer, [q[0], q[2], q[3]], c, al);
        // its outer edge bright, its inner edge in the censer's colour
        let (o0, o1, i0, i1) = (at(a0, 1.0), at(a1, 1.0), at(a0, 0.78), at(a1, 0.78));
        let (p0, p1) = (at(a0, 0.95), at(a1, 0.95));
        tri(renderer, [o0, o1, p1], WARD_GREYS[3], 0.8 * a);
        tri(renderer, [o0, p1, p0], WARD_GREYS[3], 0.8 * a);
        let (n0, n1) = (at(a0, 0.83), at(a1, 0.83));
        tri(renderer, [i0, i1, n1], color, 0.75 * a);
        tri(renderer, [i0, n1, n0], color, 0.75 * a);
    }
}

fn mix(a: u32, b: u32, t: f32) -> u32 {
    let t = t.clamp(0.0, 1.0);
    let ch = |s: u32| -> u32 {
        let (x, y) = (((a >> s) & 0xFF) as f32, ((b >> s) & 0xFF) as f32);
        ((x + (y - x) * t).round() as u32) << s
    };
    ch(16) | ch(8) | ch(0)
}

#[cfg(test)]
mod tests {
    use super::cover;

    #[test]
    fn ward_covers_the_bar_from_the_left() {
        assert_eq!(cover(1000, 0), None);
        assert_eq!(cover(0, 50), None);
        assert_eq!(cover(1000, 250), Some(0.25));
        // more than the most health: all of it
        assert_eq!(cover(100, 400), Some(1.0));
    }
}
