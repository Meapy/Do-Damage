//! The Realm Pass's pet effects (since 2.6.6): what a pet wears round it, the server's `PET_FX`
//! stat on the pet (an id from `pass.json`'s `petEffects`, the owner's choice in the pass's Pets
//! tab). Drawn like the auras (`auras.rs`, whose pen these use), afresh each frame from the time:
//! a part on the ground under the pet (`ground`, before the sprites, so the pet stands in front of
//! it) and a part in the air round it (`air`, over the objects). The trails come from the same
//! footsteps (`auras::Trails`).
//!
//! Everything is soft light: round motes with a halo (`blob`, `orb`), painted strokes (`stroke`)
//! and slim four-pointed stars, moving slowly and easing in and out, so the world's bloom makes
//! them glow rather than sparkle.
//!
//! - Frostpaw: glowing frost paw prints behind it, snowflakes drifting down round it.
//! - Winter's Crown: a halo of light over its head, six slim ice crystals standing on it, turning,
//!   a glint running round.
//! - Kindled: embers rising off it on the heat, a warm light under it.
//! - Phoenix Wings: two wings of flame feathers from its back, beating slowly, embers drifting
//!   off their tips.
//! - Starlit: slim stars blooming and fading round it.
//! - Moonlit Orbit: three crescent moons circling it, passing behind it and in front.
//! - Prismatic: a ribbon of rainbow light flowing behind it as it moves, prisms turning round it.

use crate::auras::{cycle, h, mix, ramp, Pen, Trails, STARS, VIOLET};
use bsk_render::{Renderer, Tint};
use std::f32::consts::{PI, TAU};

pub const FROSTPAW: u8 = 1;
pub const WINTERS_CROWN: u8 = 2;
pub const KINDLED: u8 = 3;
pub const PHOENIX_WINGS: u8 = 4;
pub const STARLIT: u8 = 5;
pub const MOONLIT_ORBIT: u8 = 6;
pub const PRISMATIC: u8 = 7;
/// The highest effect drawn (a newer server's other ids show nothing).
pub const LAST: u8 = 7;

/// A pet to draw round: where its feet are on the screen, a tile's size in pixels there, how big
/// it's drawn (1 for an 8-pixel sprite at size 100), its effect, how opaque.
#[derive(Clone, Copy, Debug)]
pub struct PetWearer {
    pub id: i32,
    pub x: f32,
    pub y: f32,
    pub tile: f32,
    pub size: f32,
    pub fx: u8,
    pub alpha: f32,
}

/// Whether an effect leaves marks behind (its pet's steps are kept, `Trails::walk`).
pub fn trails(fx: u8) -> bool {
    fx == FROSTPAW || fx == PRISMATIC
}

const ICE: u32 = 0xBFE8FF;
const ICE_DEEP: u32 = 0x5AA8F0;
const MOON: u32 = 0xEDE6FF;
const MOON_SHADE: u32 = 0x3A2C6E;
/// The rainbow, round.
const RAINBOW: [u32; 7] = [0xFF6A7E, 0xFFAE5A, 0xFFE87A, 0x7EE89A, 0x6AD0FF, 0x9A8CFF, 0xFF6A7E];
/// Flame from its root to its tip.
const FLAME: [u32; 5] = [0xFFF6D0, 0xFFD066, 0xFF9A3A, 0xF0542A, 0x9A1E2A];

/// Smoothly in and out: 0 at 0 and 1, 1 in the middle.
fn bell(t: f32) -> f32 {
    let s = (t * PI).sin();
    s * s
}

/// A soft round light: full `rgb` in the middle fading to nothing at its edge (an ellipse).
fn blob(p: &mut Pen, x: f32, y: f32, rx: f32, ry: f32, rgb: u32, a: f32) {
    let a = a * p.alpha;
    if a <= 0.004 || rx < 0.3 {
        return;
    }
    let white = p.r.white();
    let mid = Tint::color(rgb, a.min(1.0)).mul;
    let edge = Tint::color(rgb, 0.0).mul;
    const N: usize = 12;
    for i in 0..N {
        let (a0, a1) = (i as f32 * TAU / N as f32, (i + 1) as f32 * TAU / N as f32);
        let p0 = [x + rx * a0.cos(), y + ry * a0.sin()];
        let p1 = [x + rx * a1.cos(), y + ry * a1.sin()];
        p.r.triangle_colors(white, [[x, y], p0, p1], [[0.5, 0.5]; 3], [mid, edge, edge]);
    }
}

/// A mote of light: a wide faint halo, a body, and a white-hot heart.
fn orb(p: &mut Pen, x: f32, y: f32, rad: f32, rgb: u32, a: f32) {
    blob(p, x, y, rad * 3.0, rad * 3.0, rgb, 0.22 * a);
    blob(p, x, y, rad * 1.4, rad * 1.4, mix(rgb, 0xFFFFFF, 0.35), 0.8 * a);
    blob(p, x, y, rad * 0.6, rad * 0.6, 0xFFFFFF, a);
}

/// A slim four-pointed star, its long arms up and down, with a soft light behind it.
fn star(p: &mut Pen, x: f32, y: f32, len: f32, rgb: u32, a: f32) {
    if len < 0.6 {
        return;
    }
    blob(p, x, y, len * 1.1, len * 1.1, rgb, 0.3 * a);
    let w = len * 0.16;
    let c = mix(rgb, 0xFFFFFF, 0.6);
    p.tri([[x, y - len], [x - w, y], [x + w, y]], c, a);
    p.tri([[x, y + len], [x - w, y], [x + w, y]], c, a);
    let l2 = len * 0.62;
    p.tri([[x - l2, y], [x, y - w], [x, y + w]], c, a);
    p.tri([[x + l2, y], [x, y - w], [x, y + w]], c, a);
    blob(p, x, y, w * 1.6, w * 1.6, 0xFFFFFF, a);
}

/// A painted stroke along `pts`, from radius `r0` to `r1`, coloured along `colors`, from alpha
/// `a0` to `a1`: soft round dabs a third of their width apart, so they run together.
#[allow(clippy::too_many_arguments)]
fn stroke(p: &mut Pen, pts: &[[f32; 2]], r0: f32, r1: f32, colors: &[u32], a0: f32, a1: f32) {
    let segs: Vec<f32> = pts.windows(2).map(|s| (s[1][0] - s[0][0]).hypot(s[1][1] - s[0][1])).collect();
    let total: f32 = segs.iter().sum();
    if total <= 0.0 {
        return;
    }
    let mut along = 0.0f32;
    let (mut i, mut into) = (0usize, 0.0f32);
    while i < segs.len() {
        let k = (along / total).clamp(0.0, 1.0);
        let r = (r0 + (r1 - r0) * k).max(0.5);
        let f = if segs[i] > 0.0 { into / segs[i] } else { 0.0 };
        let (a, b) = (pts[i], pts[i + 1]);
        let (x, y) = (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f);
        blob(p, x, y, r, r, ramp(colors, k), a0 + (a1 - a0) * k);
        // the next dab
        let step = (r * 0.35).max(0.75);
        along += step;
        into += step;
        while i < segs.len() && into > segs[i] {
            into -= segs[i];
            i += 1;
        }
    }
}

/// What lies on the ground under a pet, and its marks behind it: drawn before the sprites, so the
/// pet stands in front of it. `to_screen` puts a world spot on the screen.
pub fn ground(r: &mut Renderer, w: &PetWearer, trails: &Trails, to_screen: &dyn Fn(f64, f64) -> [f32; 2], now: i32) {
    let mut p = Pen { r, alpha: w.alpha };
    let (x, y, t) = (w.x, w.y, w.tile * w.size.max(0.5));
    let ms = now as f32;
    match w.fx {
        FROSTPAW => {
            blob(&mut p, x, y, 0.55 * t, 0.22 * t, ICE, 0.32);
            for (mx, my, age, foot) in trails.marks(w.id, now) {
                let [sx, sy] = to_screen(mx, my);
                let tile = w.tile;
                let px = sx + if foot { 0.08 } else { -0.08 } * tile;
                // a print that glows as it's made, then frosts over and fades
                let fade = (1.0 - age) * (1.0 - age);
                let glint = (1.0 - age * 4.0).max(0.0);
                blob(&mut p, px, sy, 0.16 * tile, 0.09 * tile, ICE_DEEP, 0.25 * fade);
                blob(&mut p, px, sy, 0.07 * tile, 0.05 * tile, mix(ICE, 0xFFFFFF, glint), 0.75 * fade);
                for k in [-1.0f32, 0.0, 1.0] {
                    let (tx, ty) = (px + k * 0.055 * tile, sy - 0.075 * tile - (1.0 - k.abs()) * 0.018 * tile);
                    blob(&mut p, tx, ty, 0.03 * tile, 0.026 * tile, 0xFFFFFF, 0.7 * fade);
                }
            }
        }
        WINTERS_CROWN => {
            blob(&mut p, x, y, 0.55 * t, 0.23 * t, ICE, 0.2);
            p.band(x, y, 0.5 * t, 0.21 * t, 0.93, 1.0, 0.0, TAU, ICE, 0.22);
        }
        KINDLED => {
            let breathe = 0.85 + 0.15 * (ms * 0.0021).sin() * (ms * 0.0013).cos();
            blob(&mut p, x, y, 0.62 * t, 0.26 * t, 0xFF8A3A, 0.3 * breathe);
            blob(&mut p, x, y, 0.3 * t, 0.12 * t, 0xFFD27A, 0.35 * breathe);
        }
        PHOENIX_WINGS => {
            let breathe = 0.85 + 0.15 * (ms * 0.0055).sin();
            blob(&mut p, x, y, 0.8 * t, 0.3 * t, 0xFF7A30, 0.28 * breathe);
            wings(&mut p, x, y, t, ms);
        }
        STARLIT => {
            blob(&mut p, x, y, 0.5 * t, 0.2 * t, 0xE6D8FF, 0.28);
        }
        MOONLIT_ORBIT => {
            blob(&mut p, x, y, 0.75 * t, 0.3 * t, VIOLET, 0.16);
            // the path, faint; and the moons on its far side, behind the pet
            p.band(x, y - 0.42 * t, 0.78 * t, 0.26 * t, 0.97, 1.0, 0.0, TAU, MOON, 0.12);
            moons(&mut p, x, y, t, ms, false);
        }
        PRISMATIC => {
            let hue = (ms * 0.00015).fract();
            blob(&mut p, x, y, 0.5 * t, 0.2 * t, ramp(&RAINBOW, hue), 0.2);
            ribbon(&mut p, w, trails, to_screen, now, hue);
        }
        _ => {}
    }
}

/// What floats round a pet, over everything near it.
pub fn air(r: &mut Renderer, w: &PetWearer, now: i32) {
    let mut p = Pen { r, alpha: w.alpha };
    let (x, y, t) = (w.x, w.y, w.tile * w.size.max(0.5));
    let ms = now as f32;
    // where its head is
    let head = y - 0.85 * t;
    match w.fx {
        FROSTPAW => {
            for i in 0..9u32 {
                let (ph, c) = cycle(now, i, 3600);
                let sway = 0.07 * t * (ph * 5.0 + i as f32 * 1.7).sin();
                let sx = x + (h(i, c) - 0.5) * 1.1 * t + sway;
                let sy = y - (1.2 - 1.1 * ph) * t;
                orb(&mut p, sx, sy, (0.026 + 0.014 * h(i + 5, c)) * t, ICE, bell(ph).sqrt());
            }
            // now and then a flake catches the light
            let (ph, c) = cycle(now, 11, 2600);
            if ph < 0.3 {
                let k = bell(ph / 0.3);
                star(&mut p, x + (h(c, 3) - 0.5) * 0.9 * t, y - (0.4 + 0.6 * h(c, 4)) * t, 0.12 * t * k, ICE, k);
            }
        }
        WINTERS_CROWN => {
            let spin = ms * 0.0006;
            let bob = 0.035 * t * (ms * 0.0022).sin();
            let (cx, cy) = (x, head - 0.22 * t + bob);
            let (rx, ry) = (0.3 * t, 0.085 * t);
            blob(&mut p, cx, cy - 0.04 * t, 0.42 * t, 0.2 * t, ICE, 0.22);
            // the crystals behind the halo, then the halo, then those in front
            let crystal = |p: &mut Pen, i: usize, front: bool| {
                let a = spin + i as f32 * TAU / 6.0;
                let depth = a.sin();
                if (depth >= 0.0) != front {
                    return;
                }
                let (sx, sy) = (cx + rx * a.cos(), cy + ry * depth);
                let tall = (0.2 + if i % 2 == 0 { 0.07 } else { 0.0 }) * t;
                let lit = 0.55 + 0.45 * (depth * 0.5 + 0.5);
                blob(p, sx, sy - tall * 0.45, tall * 0.45, tall * 0.55, ICE, 0.25 * lit);
                p.diamond(sx, sy - tall * 0.45, 0.055 * t, tall, 0.0, mix(ICE_DEEP, ICE, 0.5), 0xF4FBFF, 0.85 * lit);
            };
            for i in 0..6 {
                crystal(&mut p, i, false);
            }
            p.band(cx, cy, rx, ry, 0.82, 1.0, 0.0, TAU, ICE, 0.55);
            p.band(cx, cy, rx, ry, 0.9, 0.96, 0.0, TAU, 0xFFFFFF, 0.5);
            for i in 0..6 {
                crystal(&mut p, i, true);
            }
            // a glint running round the halo
            let g = ms * 0.0019;
            orb(&mut p, cx + rx * g.cos(), cy + ry * g.sin(), 0.022 * t, 0xFFFFFF, 0.6 + 0.4 * g.sin().max(0.0));
        }
        KINDLED => {
            blob(&mut p, x, y - 0.4 * t, 0.45 * t, 0.55 * t, 0xFF8A3A, 0.1);
            for i in 0..12u32 {
                let (ph, c) = cycle(now, i, 2400);
                let drift = 0.12 * t * (ph * 3.5 + i as f32).sin() * ph;
                let ex = x + (h(i, c) - 0.5) * 0.7 * t + drift;
                let ey = y - (0.15 + 1.2 * ph) * t;
                let flick = 0.8 + 0.2 * (ms * 0.02 + i as f32 * 2.3).sin();
                let c0 = ramp(&FLAME, 0.1 + 0.8 * ph);
                orb(&mut p, ex, ey, (0.034 - 0.016 * ph) * t, c0, flick * bell(ph.sqrt()));
            }
        }
        PHOENIX_WINGS => {
            // embers drifting off the wing tips
            let by = y - 0.6 * t;
            for i in 0..8u32 {
                let (ph, c) = cycle(now, i, 2200);
                let side = if i % 2 == 0 { 1.0 } else { -1.0 };
                let ex = x + side * (0.55 + 0.35 * h(i, c) + 0.15 * ph) * t;
                let ey = by - (0.35 * h(i + 3, c)) * t - ph * 0.45 * t;
                orb(&mut p, ex, ey, (0.02 - 0.01 * ph) * t, ramp(&FLAME, 0.3 + 0.6 * ph), bell(ph) * 0.9);
            }
        }
        STARLIT => {
            for i in 0..6u32 {
                let (ph, c) = cycle(now, i, 2800);
                let a = h(i, c) * TAU;
                let rr = (0.35 + 0.25 * h(i + 31, c)) * t;
                let (sx, sy) = (x + rr * a.cos(), y - 0.5 * t + 0.55 * rr * a.sin() - ph * 0.12 * t);
                let k = bell(ph);
                star(&mut p, sx, sy, (0.1 + 0.06 * h(i + 7, c)) * t * k, STARS[(i % 3) as usize], k);
            }
            for i in 0..6u32 {
                let (ph, c) = cycle(now, i + 20, 1900);
                let (sx, sy) = (x + (h(i, c) - 0.5) * 1.0 * t, y - (0.2 + 0.9 * h(i + 9, c)) * t - ph * 0.15 * t);
                orb(&mut p, sx, sy, 0.014 * t, 0xE6D8FF, 0.8 * bell(ph));
            }
        }
        MOONLIT_ORBIT => {
            moons(&mut p, x, y, t, ms, true);
        }
        PRISMATIC => {
            let hue = (ms * 0.00015).fract();
            let spin = ms * 0.0009;
            for i in 0..4 {
                let a = spin + i as f32 * TAU / 4.0;
                let (sx, sy) = (x + 0.5 * t * a.cos(), y - 0.5 * t + 0.18 * t * a.sin() + 0.04 * t * (ms * 0.003 + i as f32).sin());
                let near = 0.6 + 0.4 * a.sin().max(0.0);
                let c = ramp(&RAINBOW, (hue + i as f32 / 4.0).fract());
                blob(&mut p, sx, sy, 0.09 * t, 0.09 * t, c, 0.3 * near);
                p.diamond(sx, sy, 0.05 * t, 0.1 * t, spin * 2.0 + i as f32, c, mix(c, 0xFFFFFF, 0.7), 0.85 * near);
            }
        }
        _ => {}
    }
}

/// Two wings of flame from a pet's back, beating slowly: each a fan of curved feathers painted in
/// soft light, gold at the root, crimson at the tip, the upper ones longest.
fn wings(p: &mut Pen, x: f32, y: f32, t: f32, ms: f32) {
    // a slow beat that lingers up and sweeps down
    let raw = (ms * 0.0042).sin();
    let beat = raw.signum() * raw.abs().powf(0.7);
    let (bx, by) = (x, y - 0.62 * t - 0.03 * t * beat);
    const FEATHERS: usize = 8;
    const STEPS: usize = 9;
    for side in [-1.0f32, 1.0] {
        // the wing's own light, behind its feathers
        blob(p, bx + side * 0.55 * t, by - 0.15 * t, 0.65 * t, 0.45 * t, 0xFF6A2A, 0.3);
        for k in (0..FEATHERS).rev() {
            let f = k as f32 / (FEATHERS - 1) as f32;
            // from up and out (f 0, the leading edge) to out and down (f 1); the beat raises the wing
            let theta = 0.72 + 1.1 * f - 0.28 * beat * (1.0 - 0.4 * f);
            let len = (1.05 - 0.45 * f) * t;
            let mut pts = [[0.0f32; 2]; STEPS];
            let mut at = [bx + side * 0.12 * t, by + 0.08 * t * f];
            for (s, pt) in pts.iter_mut().enumerate() {
                *pt = at;
                // each feather curls outward and down toward its tip
                let ang = theta + 0.5 * (s as f32 / STEPS as f32).powi(2);
                at = [at[0] + side * ang.sin() * len / STEPS as f32, at[1] - ang.cos() * len / STEPS as f32];
            }
            let flick = 0.9 + 0.1 * (ms * 0.011 + k as f32 * 1.3).sin();
            stroke(p, &pts, (0.13 - 0.03 * f) * t, 0.03 * t, &FLAME[1..], 0.5 * flick, 0.14);
            // its bright vein
            stroke(p, &pts[..STEPS - 2], 0.045 * t, 0.015 * t, &FLAME[..3], 0.6 * flick, 0.18);
        }
    }
}

/// Three crescent moons on a tilted orbit round a pet: those on the far side (`front` false, with
/// the ground, behind the pet) or the near side (`front`, over it).
fn moons(p: &mut Pen, x: f32, y: f32, t: f32, ms: f32, front: bool) {
    let spin = ms * 0.0008;
    let (cx, cy) = (x, y - 0.42 * t);
    for i in 0..3 {
        let a = spin + i as f32 * TAU / 3.0;
        let depth = a.sin();
        if (depth >= 0.0) != front {
            continue;
        }
        let (rx, ry) = (0.78 * t, 0.26 * t);
        let (mx, my) = (cx + rx * a.cos(), cy + ry * depth);
        let lit = 0.55 + 0.45 * (depth * 0.5 + 0.5);
        let rad = (0.1 + 0.02 * depth) * t;
        // a faint wake along the orbit
        for k in 1..6 {
            let b = a - k as f32 * 0.09;
            let fade = 1.0 - k as f32 / 6.0;
            blob(p, cx + rx * b.cos(), cy + ry * b.sin(), rad * 0.9 * fade, rad * 0.9 * fade, VIOLET, 0.22 * lit * fade);
        }
        blob(p, mx, my, rad * 3.2, rad * 3.2, VIOLET, 0.4 * lit);
        // the moon: a solid disc, lit, with a soft rim
        p.band(mx, my, rad, rad, 0.0, 1.0, 0.0, TAU, MOON, lit);
        blob(p, mx, my, rad * 1.35, rad * 1.35, 0xFFFFFF, 0.35 * lit);
        // its shadow, leaving a crescent lit toward the pet
        let away = if a.cos() >= 0.0 { 1.0 } else { -1.0 };
        p.band(mx + away * rad * 0.38, my - rad * 0.12, rad * 0.8, rad * 0.8, 0.0, 1.0, 0.0, TAU, MOON_SHADE, 0.55 * lit);
    }
}

/// A ribbon of rainbow light along a pet's last steps to where it is, narrowing and fading
/// behind it.
fn ribbon(p: &mut Pen, w: &PetWearer, trails: &Trails, to_screen: &dyn Fn(f64, f64) -> [f32; 2], now: i32, hue: f32) {
    let marks = trails.marks(w.id, now);
    if marks.is_empty() {
        return;
    }
    // oldest first, then the pet itself
    let mut pts: Vec<([f32; 2], f32)> = marks.iter().map(|&(mx, my, age, _)| (to_screen(mx, my), age)).collect();
    pts.push(([w.x, w.y - 0.05 * w.tile], 0.0));
    let width = 0.13 * w.tile;
    let white = p.r.white();
    for layer in 0..2 {
        let (wk, ak) = if layer == 0 { (1.0, 0.28) } else { (0.4, 0.7) };
        for s in pts.windows(2) {
            let ((a, aa), (b, ab)) = (s[0], s[1]);
            let (dx, dy) = (b[0] - a[0], b[1] - a[1]);
            let len = (dx * dx + dy * dy).sqrt();
            if len < 0.5 || len > 3.0 * w.tile {
                continue;
            }
            let (nx, ny) = (-dy / len, dx / len);
            let (wa, wb) = (width * wk * (1.0 - aa), width * wk * (1.0 - ab));
            let col = |age: f32| {
                let c = ramp(&RAINBOW, (hue + age * 0.9).fract());
                let c = if layer == 1 { mix(c, 0xFFFFFF, 0.45) } else { c };
                Tint::color(c, (ak * (1.0 - age) * p.alpha).clamp(0.0, 1.0)).mul
            };
            let (ca, cb) = (col(aa), col(ab));
            let (ea, eb) = (Tint::color(0xFFFFFF, 0.0).mul, Tint::color(0xFFFFFF, 0.0).mul);
            // each side fades out from the middle line, so its edges are soft
            for sgn in [-1.0f32, 1.0] {
                let (oa, ob) = ([a[0] + sgn * nx * wa, a[1] + sgn * ny * wa], [b[0] + sgn * nx * wb, b[1] + sgn * ny * wb]);
                p.r.triangle_colors(white, [a, b, ob], [[0.5, 0.5]; 3], [ca, cb, eb]);
                p.r.triangle_colors(white, [a, ob, oa], [[0.5, 0.5]; 3], [ca, eb, ea]);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_trailing_effects_keep_steps() {
        assert!(trails(FROSTPAW) && trails(PRISMATIC));
        assert!(!trails(STARLIT) && !trails(0));
        assert_eq!(LAST, PRISMATIC);
    }

    #[test]
    fn bells_ease_in_and_out() {
        assert!(bell(0.0) < 1e-6 && bell(1.0) < 1e-6 && (bell(0.5) - 1.0).abs() < 1e-6);
    }
}
