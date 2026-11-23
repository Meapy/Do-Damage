//! The Realm Pass's looks round a player (`pass.rs` has the pass itself): the aura they wear (the
//! server's `AURA` stat, an id from `pass.json`) and the effects of its hand-drawn skins (a skin
//! whose XML has `<PassSkin>`, by its look). Everything here is drawn afresh each frame from the
//! time, glowing, like the Father's ward (`ward.rs`): a part on the ground under the player
//! (`ground`, with the shadows) and a part in the air round them (`air`, over the objects). Only
//! the footprints and trails need memory (`Trails`).
//!
//! - Snowfall: snowflakes drifting down round you, a ring of frost under you.
//! - Frostcrown: six ice shards wheeling round you over a turning rune circle, frost rising.
//! - Cinders: embers rising off you, a warm glow under you.
//! - Emberheart: a ring of flame round your feet, three fire wisps circling you, sparks.
//! - Stardust: stars twinkling in and out round you.
//! - Constellation: seven stars wheeling round you, joined by lines of light, a shooting star now
//!   and then, over a violet glow.
//! - The Frostcrown Archmage: frost motes spiralling up round him, his crown glinting, cold mist
//!   at his feet, ice crystals in his footsteps.
//! - The Winterguard: frost mist rolling round his feet, ice falling from his mantle, his breath
//!   steaming, frozen footprints behind him.
//! - The Aurora Ranger: the northern lights rippling over her head and streaming behind her as
//!   she runs, motes of it drifting.

use bsk_render::{Renderer, Tint};
use std::collections::{HashMap, VecDeque};
use std::f32::consts::{PI, TAU};

pub const SNOWFALL: u8 = 1;
pub const FROSTCROWN: u8 = 2;
pub const CINDERS: u8 = 3;
pub const EMBERHEART: u8 = 4;
pub const STARDUST: u8 = 5;
pub const CONSTELLATION: u8 = 6;
/// The highest aura drawn (a newer server's other ids show nothing).
pub const LAST: u8 = 6;

/// A Realm Pass skin's effects, by its `<PassSkin>` look.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SkinLook {
    Archmage,
    Winterguard,
    Aurora,
}

impl SkinLook {
    pub fn named(s: &str) -> Option<Self> {
        Some(match s.trim() {
            "archmage" => SkinLook::Archmage,
            "winterguard" => SkinLook::Winterguard,
            "aurora" => SkinLook::Aurora,
            _ => return None,
        })
    }
}

/// Someone to draw round: where their feet are on the screen, a tile's size in pixels there,
/// how big they are drawn (1 at size 100), their aura and skin, how opaque.
#[derive(Clone, Copy, Debug)]
pub struct Wearer {
    pub id: i32,
    pub x: f32,
    pub y: f32,
    pub tile: f32,
    pub size: f32,
    pub aura: u8,
    pub skin: Option<SkinLook>,
    pub alpha: f32,
}

/// The marks a wearer leaves behind (footprints, a trail of light), in world tiles, with when.
#[derive(Default)]
pub struct Trails {
    by: HashMap<i32, Trail>,
}

#[derive(Default)]
struct Trail {
    last: Option<(f64, f64)>,
    marks: VecDeque<(f64, f64, i32, bool)>,
    seen: i32,
    left_foot: bool,
}

/// How long a footprint or trail mark lasts, ms, and how far apart they're left, tiles.
const MARK_MS: i32 = 1800;
const STEP: f64 = 0.38;

impl Trails {
    /// A wearer at (x, y) now: a new mark once they've moved a step.
    pub fn walk(&mut self, id: i32, x: f64, y: f64, now: i32) {
        let t = self.by.entry(id).or_default();
        t.seen = now;
        match t.last {
            Some((lx, ly)) if (x - lx).hypot(y - ly) < STEP => {}
            Some((lx, ly)) if (x - lx).hypot(y - ly) > 3.0 => t.last = Some((x, y)),
            _ => {
                if t.last.is_some() {
                    t.left_foot = !t.left_foot;
                    t.marks.push_back((x, y, now, t.left_foot));
                    if t.marks.len() > 24 {
                        t.marks.pop_front();
                    }
                }
                t.last = Some((x, y));
            }
        }
        t.marks.retain(|m| now - m.2 < MARK_MS);
    }

    /// Forgets those not seen for a while.
    pub fn prune(&mut self, now: i32) {
        self.by.retain(|_, t| now - t.seen < 5000);
    }

    /// A wearer's marks: where, how old (0 to 1), and which foot.
    pub(crate) fn marks(&self, id: i32, now: i32) -> Vec<(f64, f64, f32, bool)> {
        self.by.get(&id).map_or_else(Vec::new, |t| {
            t.marks.iter().map(|&(x, y, at, foot)| (x, y, ((now - at) as f32 / MARK_MS as f32).clamp(0.0, 1.0), foot)).collect()
        })
    }
}

/// A tile's size in pixels at `k` 1 (`view.rs`'s `TILE_PX`).
pub const TILE: f32 = 50.0;

/// A number in 0..1 from two, the same each time.
pub(crate) fn h(a: u32, b: u32) -> f32 {
    let mut x = a.wrapping_mul(0x9E37_79B1) ^ b.wrapping_mul(0x85EB_CA77);
    x ^= x >> 15;
    x = x.wrapping_mul(0x2C1B_3C6D);
    x ^= x >> 12;
    (x & 0xFFFF) as f32 / 65536.0
}

/// Where a repeating particle `i` of `n` is in its life of `period` ms: (0..1 through it, which
/// life it's on).
pub(crate) fn cycle(now: i32, i: u32, period: i32) -> (f32, u32) {
    let t = now as i64 + (i as i64 * 7919) % period as i64;
    ((t.rem_euclid(period as i64)) as f32 / period as f32, (t.div_euclid(period as i64)) as u32)
}

pub(crate) fn mix(a: u32, b: u32, t: f32) -> u32 {
    let t = t.clamp(0.0, 1.0);
    let ch = |s: u32| -> u32 {
        let (x, y) = (((a >> s) & 0xFF) as f32, ((b >> s) & 0xFF) as f32);
        ((x + (y - x) * t).round() as u32) << s
    };
    ch(16) | ch(8) | ch(0)
}

/// Through a list of colours, 0 the first and 1 the last.
pub(crate) fn ramp(colors: &[u32], t: f32) -> u32 {
    let t = t.clamp(0.0, 1.0) * (colors.len() - 1) as f32;
    let i = (t.floor() as usize).min(colors.len() - 2);
    mix(colors[i], colors[i + 1], t - i as f32)
}

pub(crate) struct Pen<'a> {
    pub(crate) r: &'a mut Renderer,
    pub(crate) alpha: f32,
}

impl Pen<'_> {
    pub(crate) fn tri(&mut self, pts: [[f32; 2]; 3], rgb: u32, a: f32) {
        let a = a * self.alpha;
        if a > 0.004 {
            let white = self.r.white();
            self.r.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(rgb, a.min(1.0)));
        }
    }

    /// A square dot.
    pub(crate) fn dot(&mut self, x: f32, y: f32, size: f32, rgb: u32, a: f32) {
        let a = a * self.alpha;
        if a > 0.004 && size > 0.2 {
            self.r.fill(x - size / 2.0, y - size / 2.0, size, size, rgb, a.min(1.0));
        }
    }

    /// A twinkle: a plus of light, its arms `len` long, a bright heart.
    pub(crate) fn twinkle(&mut self, x: f32, y: f32, len: f32, rgb: u32, a: f32) {
        let t = (len * 0.22).max(1.0);
        let a = a * self.alpha;
        if a <= 0.004 || len < 0.5 {
            return;
        }
        self.r.fill(x - len, y - t / 2.0, 2.0 * len, t, rgb, (a * 0.8).min(1.0));
        self.r.fill(x - t / 2.0, y - len, t, 2.0 * len, rgb, (a * 0.8).min(1.0));
        self.r.fill(x - t, y - t, 2.0 * t, 2.0 * t, 0xFFFFFF, a.min(1.0));
    }

    /// A soft glow: brightest in the middle, nothing at the edge (an ellipse).
    pub(crate) fn glow(&mut self, x: f32, y: f32, rx: f32, ry: f32, rgb: u32, a: f32) {
        let a = a * self.alpha;
        if a <= 0.004 {
            return;
        }
        let white = self.r.white();
        let mid = Tint::color(rgb, a.min(1.0)).mul;
        let edge = Tint::color(rgb, 0.0).mul;
        const N: usize = 20;
        for i in 0..N {
            let (a0, a1) = (i as f32 * TAU / N as f32, (i + 1) as f32 * TAU / N as f32);
            let p0 = [x + rx * a0.cos(), y + ry * a0.sin()];
            let p1 = [x + rx * a1.cos(), y + ry * a1.sin()];
            self.r.triangle_colors(white, [[x, y], p0, p1], [[0.5, 0.5]; 3], [mid, edge, edge]);
        }
    }

    /// An ellipse's band, from `k0` to `k1` of its radii, from angle `from` to `to`.
    #[allow(clippy::too_many_arguments)]
    pub(crate) fn band(&mut self, x: f32, y: f32, rx: f32, ry: f32, k0: f32, k1: f32, from: f32, to: f32, rgb: u32, a: f32) {
        let n = (((to - from).abs() / TAU) * 32.0).ceil().max(2.0) as usize;
        let at = |ang: f32, k: f32| [x + rx * k * ang.cos(), y + ry * k * ang.sin()];
        for i in 0..n {
            let a0 = from + (to - from) * i as f32 / n as f32;
            let a1 = from + (to - from) * (i + 1) as f32 / n as f32;
            self.tri([at(a0, k0), at(a0, k1), at(a1, k1)], rgb, a);
            self.tri([at(a0, k0), at(a1, k1), at(a1, k0)], rgb, a);
        }
    }

    /// A diamond (an ice shard), `w` wide and `ht` tall, tilted by `tilt`.
    #[allow(clippy::too_many_arguments)]
    pub(crate) fn diamond(&mut self, x: f32, y: f32, w: f32, ht: f32, tilt: f32, rgb: u32, light: u32, a: f32) {
        let (s, c) = tilt.sin_cos();
        let p = |dx: f32, dy: f32| [x + dx * c - dy * s, y + dx * s + dy * c];
        let (top, bot, l, r) = (p(0.0, -ht / 2.0), p(0.0, ht / 2.0), p(-w / 2.0, 0.0), p(w / 2.0, 0.0));
        self.tri([top, l, bot], rgb, a);
        self.tri([top, r, bot], light, a);
    }

    /// A line of light from one point to another, `w` thick.
    pub(crate) fn line(&mut self, a: [f32; 2], b: [f32; 2], w: f32, rgb: u32, al: f32) {
        let (dx, dy) = (b[0] - a[0], b[1] - a[1]);
        let len = (dx * dx + dy * dy).sqrt().max(0.001);
        let (nx, ny) = (-dy / len * w / 2.0, dx / len * w / 2.0);
        let q = [[a[0] + nx, a[1] + ny], [b[0] + nx, b[1] + ny], [b[0] - nx, b[1] - ny], [a[0] - nx, a[1] - ny]];
        self.tri([q[0], q[1], q[2]], rgb, al);
        self.tri([q[0], q[2], q[3]], rgb, al);
    }

    /// A strip of light standing up from (x, y), `ht` tall, fading out at the top.
    pub(crate) fn curtain(&mut self, x: f32, y: f32, w: f32, ht: f32, rgb: u32, a: f32) {
        let a = a * self.alpha;
        if a <= 0.004 {
            return;
        }
        let white = self.r.white();
        let lo = Tint::color(rgb, a.min(1.0)).mul;
        let hi = Tint::color(rgb, 0.0).mul;
        let (l, r, top) = (x - w / 2.0, x + w / 2.0, y - ht);
        self.r.triangle_colors(white, [[l, y], [r, y], [r, top]], [[0.5, 0.5]; 3], [lo, lo, hi]);
        self.r.triangle_colors(white, [[l, y], [r, top], [l, top]], [[0.5, 0.5]; 3], [lo, hi, hi]);
    }
}

pub(crate) const FROST: [u32; 4] = [0x3C8CE0, 0x8FD0FF, 0xCFEFFF, 0xFFFFFF];
pub(crate) const FIRE: [u32; 4] = [0xFFF2A0, 0xFFB040, 0xFF6020, 0xB02010];
pub(crate) const STARS: [u32; 3] = [0xFFFFFF, 0xE6D8FF, 0xFFE9A8];
pub(crate) const VIOLET: u32 = 0xB89BFF;
const AURORA: [u32; 4] = [0x6EF0BA, 0x30AA96, 0x7FA0FF, 0xA478F0];

/// What lies on the ground under a wearer, and their marks behind them. `to_screen` puts a world
/// spot on the screen.
pub fn ground(r: &mut Renderer, w: &Wearer, trails: &Trails, to_screen: &dyn Fn(f64, f64) -> [f32; 2], now: i32) {
    let mut p = Pen { r, alpha: w.alpha };
    let (x, y, t) = (w.x, w.y, w.tile * w.size);
    let ms = now as f32;
    match w.aura {
        SNOWFALL => {
            p.glow(x, y, 0.62 * t, 0.28 * t, 0xCFEFFF, 0.22 + 0.06 * (ms * 0.002).sin());
            for i in 0..8u32 {
                let a = i as f32 * TAU / 8.0 + ms * 0.0003;
                let tw = (ms * 0.004 + i as f32 * 1.7).sin().max(0.0);
                p.twinkle(x + 0.55 * t * a.cos(), y + 0.24 * t * a.sin(), 0.05 * t * tw, 0xFFFFFF, 0.8 * tw);
            }
        }
        FROSTCROWN => {
            p.glow(x, y, 0.8 * t, 0.36 * t, 0x8FD0FF, 0.28);
            let spin = ms * 0.0006;
            p.band(x, y, 0.72 * t, 0.32 * t, 0.9, 1.0, 0.0, TAU, 0x8FD0FF, 0.55);
            p.band(x, y, 0.72 * t, 0.32 * t, 0.62, 0.68, 0.0, TAU, 0xCFEFFF, 0.4);
            // the runes: ticks between the rings, turning
            for i in 0..12 {
                let a = spin + i as f32 * TAU / 12.0;
                p.band(x, y, 0.72 * t, 0.32 * t, 0.7, 0.88, a, a + 0.12, if i % 3 == 0 { 0xFFFFFF } else { 0xCFEFFF }, 0.6);
            }
            for i in 0..3 {
                let a = -spin * 1.6 + i as f32 * TAU / 3.0;
                p.band(x, y, 0.72 * t, 0.32 * t, 0.4, 0.5, a, a + 0.9, 0x8FD0FF, 0.35);
            }
        }
        CINDERS => {
            let flick = 0.8 + 0.2 * (ms * 0.017).sin() * (ms * 0.011).cos();
            p.glow(x, y, 0.6 * t, 0.26 * t, 0xFF8030, 0.26 * flick);
        }
        EMBERHEART => {
            p.glow(x, y, 0.95 * t, 0.42 * t, 0xFF6A2A, 0.32);
            p.band(x, y, 0.75 * t, 0.33 * t, 0.86, 1.0, 0.0, TAU, 0xFF8030, 0.55);
            // flames licking up all round the ring (the far half first)
            for i in 0..18u32 {
                let a = i as f32 * TAU / 18.0 + ms * 0.0004;
                let (fx, fy) = (x + 0.75 * t * a.cos(), y + 0.33 * t * a.sin());
                let tall = (0.16 + 0.14 * h(i, (now / 90) as u32)) * t;
                let wide = 0.07 * t;
                let c = ramp(&FIRE, h(i + 50, (now / 120) as u32) * 0.6);
                p.tri([[fx - wide, fy], [fx + wide, fy], [fx + 0.02 * t * (ms * 0.01 + i as f32).sin(), fy - tall]], c, 0.75);
                p.tri([[fx - wide * 0.5, fy], [fx + wide * 0.5, fy], [fx, fy - tall * 0.6]], 0xFFF2A0, 0.8);
            }
        }
        STARDUST => {
            p.glow(x, y, 0.55 * t, 0.24 * t, 0xE6D8FF, 0.18);
        }
        CONSTELLATION => {
            p.glow(x, y, 0.9 * t, 0.4 * t, VIOLET, 0.3);
            p.band(x, y, 0.8 * t, 0.36 * t, 0.94, 1.0, 0.0, TAU, VIOLET, 0.35);
            for i in 0..10u32 {
                let a = i as f32 * TAU / 10.0 - ms * 0.0002;
                let tw = 0.5 + 0.5 * (ms * 0.003 + i as f32 * 2.1).sin();
                p.twinkle(x + 0.8 * t * a.cos(), y + 0.36 * t * a.sin(), 0.05 * t * (0.5 + tw), 0xFFFFFF, 0.5 + 0.5 * tw);
            }
        }
        _ => {}
    }
    let Some(skin) = w.skin else { return };
    let tile = w.tile;
    match skin {
        SkinLook::Archmage => {
            p.glow(x, y, 0.55 * t, 0.24 * t, 0x8FD0FF, 0.2);
            for (mx, my, age, _) in trails.marks(w.id, now) {
                let [sx, sy] = to_screen(mx, my);
                let a = 1.0 - age;
                p.diamond(sx, sy - 0.04 * tile, 0.08 * tile, 0.16 * tile, 0.0, 0x8FD0FF, 0xFFFFFF, 0.7 * a);
                p.twinkle(sx, sy - 0.12 * tile, 0.06 * tile * a, 0xCFEFFF, a);
            }
        }
        SkinLook::Winterguard => {
            // frost mist rolling round his feet
            for i in 0..6u32 {
                let (ph, c) = cycle(now, i, 2600);
                let a = h(i, c) * TAU + ph * 0.8;
                let d = (0.2 + 0.35 * h(i + 9, c)) * t;
                p.glow(x + d * a.cos(), y + 0.45 * d * a.sin(), (0.22 + 0.2 * ph) * t, (0.1 + 0.08 * ph) * t, 0xE8F4FF, 0.22 * (ph * PI).sin());
            }
            for (mx, my, age, left) in trails.marks(w.id, now) {
                let [sx, sy] = to_screen(mx, my);
                let off = if left { -0.08 } else { 0.08 } * tile;
                p.glow(sx + off, sy, 0.09 * tile, 0.05 * tile, 0xCFEFFF, 0.7 * (1.0 - age));
                p.dot(sx + off, sy, 0.05 * tile, 0xFFFFFF, 0.6 * (1.0 - age));
            }
        }
        SkinLook::Aurora => {
            // the lights streaming behind her
            let marks = trails.marks(w.id, now);
            for (i, (mx, my, age, _)) in marks.iter().enumerate() {
                let [sx, sy] = to_screen(*mx, *my);
                let c = ramp(&AURORA, (i as f32 * 0.17 + ms * 0.0005).fract());
                p.glow(sx, sy - 0.2 * tile, 0.22 * tile, 0.14 * tile, c, 0.45 * (1.0 - age));
                p.curtain(sx, sy - 0.05 * tile, 0.12 * tile, (0.5 + 0.2 * (ms * 0.004 + i as f32).sin()) * tile * (1.0 - age), c, 0.5 * (1.0 - age));
            }
            p.glow(x, y, 0.5 * t, 0.22 * t, 0x6EF0BA, 0.18);
        }
    }
}

/// What floats in the air round a wearer.
pub fn air(r: &mut Renderer, w: &Wearer, now: i32) {
    let mut p = Pen { r, alpha: w.alpha };
    let (x, y, t) = (w.x, w.y, w.tile * w.size);
    let ms = now as f32;
    match w.aura {
        SNOWFALL => {
            for i in 0..14u32 {
                let (ph, c) = cycle(now, i, 2800);
                let fx = x + (h(i, c) - 0.5) * 1.4 * t + 0.08 * t * (ph * 6.0 + i as f32).sin();
                let fy = y - (1.55 - 1.5 * ph) * t;
                let a = (ph * PI).sin();
                if i % 3 == 0 {
                    p.twinkle(fx, fy, 0.045 * t, 0xFFFFFF, a);
                } else {
                    p.dot(fx, fy, 0.05 * t, if i % 2 == 0 { 0xFFFFFF } else { 0xCFEFFF }, 0.9 * a);
                }
            }
        }
        FROSTCROWN => {
            let spin = ms * 0.0013;
            for i in 0..6 {
                let a = spin + i as f32 * TAU / 6.0;
                let depth = a.sin();
                let bob = 0.05 * t * (ms * 0.003 + i as f32).sin();
                let (sx, sy) = (x + 0.62 * t * a.cos(), y - 0.55 * t + 0.2 * t * depth + bob);
                let fade = 0.55 + 0.45 * (depth * 0.5 + 0.5);
                // a wisp of frost behind each
                for k in 1..4 {
                    let b = a - k as f32 * 0.14;
                    p.dot(x + 0.62 * t * b.cos(), y - 0.55 * t + 0.2 * t * b.sin() + bob, 0.04 * t, 0xCFEFFF, 0.35 * fade / k as f32);
                }
                p.diamond(sx, sy, 0.11 * t, 0.24 * t, 0.25 * (a * 2.0).sin(), 0x5AA8F0, 0xE8F8FF, fade);
            }
            // frost rising off the circle
            for i in 0..8u32 {
                let (ph, c) = cycle(now, i, 1900);
                let a = h(i, c) * TAU;
                p.dot(x + 0.68 * t * a.cos(), y + 0.3 * t * a.sin() - ph * 0.9 * t, 0.045 * t * (1.0 - ph), 0xCFEFFF, (ph * PI).sin());
            }
        }
        CINDERS => {
            for i in 0..16u32 {
                let (ph, c) = cycle(now, i, 1700);
                let fx = x + (h(i, c) - 0.5) * 0.9 * t + 0.12 * t * (ph * 5.0 + i as f32).sin() * ph;
                let fy = y - (0.05 + 1.35 * ph) * t;
                let flick = 0.7 + 0.3 * h(i, (now / 60) as u32);
                p.dot(fx, fy, (0.06 - 0.035 * ph) * t, ramp(&FIRE, ph), flick * (1.0 - ph * ph));
            }
        }
        EMBERHEART => {
            let spin = ms * 0.0022;
            for i in 0..3 {
                let a = spin + i as f32 * TAU / 3.0;
                for k in 0..7 {
                    let b = a - k as f32 * 0.16;
                    let (wx, wy) = (x + 0.58 * t * b.cos(), y - 0.45 * t + 0.22 * t * b.sin() - 0.03 * t * k as f32);
                    let size = (0.12 - 0.014 * k as f32) * t;
                    p.dot(wx, wy, size, ramp(&FIRE, k as f32 / 7.0), 0.95 - k as f32 * 0.12);
                }
                let (wx, wy) = (x + 0.58 * t * a.cos(), y - 0.45 * t + 0.22 * t * a.sin());
                p.glow(wx, wy, 0.16 * t, 0.16 * t, 0xFFB040, 0.5);
            }
            for i in 0..12u32 {
                let (ph, c) = cycle(now, i, 1300);
                let a = h(i, c) * TAU;
                p.dot(x + 0.7 * t * a.cos() * (1.0 - 0.4 * ph), y + 0.3 * t * a.sin() - ph * 1.5 * t, 0.04 * t, ramp(&FIRE, ph * 0.8), 1.0 - ph);
            }
        }
        STARDUST => {
            for i in 0..12u32 {
                let (ph, c) = cycle(now, i, 1500);
                let (sx, sy) = (x + (h(i, c) - 0.5) * 1.3 * t, y - (0.1 + h(i + 31, c) * 1.25) * t);
                let k = (ph * PI).sin();
                p.twinkle(sx, sy - ph * 0.15 * t, 0.07 * t * k, STARS[(i % 3) as usize], k);
            }
        }
        CONSTELLATION => {
            let spin = ms * 0.0005;
            let mut pts = Vec::with_capacity(7);
            for i in 0..7 {
                let a = spin + i as f32 * TAU / 7.0;
                let lift = [0.0, 0.18, 0.05, 0.25, 0.1, 0.22, 0.02][i] * t;
                pts.push(([x + 0.72 * t * a.cos(), y - 0.6 * t + 0.24 * t * a.sin() - lift], a.sin()));
            }
            for i in 0..7 {
                let (a, da) = pts[i];
                let (b, db) = pts[(i + 1) % 7];
                let pulse = 0.25 + 0.15 * (ms * 0.002 + i as f32).sin();
                p.line(a, b, 0.018 * t, VIOLET, pulse * (0.6 + 0.2 * (da + db)));
            }
            for (i, (pt, d)) in pts.iter().enumerate() {
                let tw = 0.75 + 0.25 * (ms * 0.006 + i as f32 * 1.3).sin();
                p.glow(pt[0], pt[1], 0.1 * t, 0.1 * t, VIOLET, 0.5 * tw);
                p.twinkle(pt[0], pt[1], 0.07 * t * tw, 0xFFFFFF, 0.65 + 0.35 * d.max(0.0));
            }
            // now and then a shooting star over their head
            let (ph, c) = cycle(now, 0, 3400);
            if ph < 0.25 {
                let k = ph / 0.25;
                let dir = if h(c, 7) < 0.5 { 1.0 } else { -1.0 };
                let (sx, sy) = (x - dir * (0.9 - 1.8 * k) * t, y - (1.6 - 0.3 * k) * t);
                for j in 0..6 {
                    let back = j as f32 * 0.06 * t;
                    p.dot(sx - dir * back, sy - back * 0.18, (0.06 - 0.008 * j as f32) * t, if j == 0 { 0xFFFFFF } else { 0xE6D8FF }, (1.0 - j as f32 / 6.0) * (1.0 - k));
                }
            }
        }
        _ => {}
    }
    let Some(skin) = w.skin else { return };
    match skin {
        SkinLook::Archmage => {
            // frost motes spiralling up round him
            for i in 0..10u32 {
                let (ph, _) = cycle(now, i, 2200);
                let a = ph * TAU * 1.5 + i as f32 * TAU / 10.0;
                let depth = a.sin();
                let rad = (0.48 - 0.18 * ph) * t;
                let (mx, my) = (x + rad * a.cos(), y - (0.05 + 1.0 * ph) * t + 0.16 * t * depth);
                p.dot(mx, my, 0.05 * t, ramp(&FROST, 0.5 + 0.5 * depth), (ph * PI).sin() * (0.5 + 0.5 * depth.max(0.0)));
            }
            // his crown glinting, a cold light over it
            let crown = y - 0.92 * t;
            p.glow(x, crown, 0.22 * t, 0.16 * t, 0x8FD0FF, 0.35 + 0.1 * (ms * 0.003).sin());
            for i in 0..3u32 {
                let (ph, c) = cycle(now, i, 1300);
                let k = (ph * PI).sin();
                p.twinkle(x + (h(i, c) - 0.5) * 0.4 * t, crown - h(i + 5, c) * 0.15 * t, 0.07 * t * k, 0xFFFFFF, k);
            }
        }
        SkinLook::Winterguard => {
            // ice falling from his mantle
            for i in 0..8u32 {
                let (ph, c) = cycle(now, i, 1600);
                let side = if i % 2 == 0 { -1.0 } else { 1.0 };
                let fx = x + side * (0.3 + 0.2 * h(i, c)) * t;
                let fy = y - (0.55 - 0.55 * ph) * t;
                p.diamond(fx, fy, 0.035 * t, 0.07 * t, ph * 3.0, 0x8FD0FF, 0xFFFFFF, 0.9 * (1.0 - ph));
            }
            // his breath steaming from the helm, every couple of seconds
            let (ph, _) = cycle(now, 3, 2400);
            if ph < 0.45 {
                let k = ph / 0.45;
                p.glow(x + 0.08 * t * k, y - (0.72 + 0.12 * k) * t, (0.08 + 0.12 * k) * t, (0.05 + 0.07 * k) * t, 0xF0F8FF, 0.5 * (1.0 - k));
            }
            // his eyes burning cold
            p.glow(x, y - 0.66 * t, 0.16 * t, 0.07 * t, 0x8FF6FF, 0.25 + 0.12 * (ms * 0.005).sin());
        }
        SkinLook::Aurora => {
            // the lights over her head: curtains rippling green to violet
            let n = 22;
            for j in 0..n {
                let k = j as f32 / (n - 1) as f32;
                let arc = (k - 0.5) * 2.0;
                let bx = x + arc * 0.7 * t;
                let by = y - (1.0 + 0.12 * (1.0 - arc * arc)) * t + 0.05 * t * (k * 9.0 + ms * 0.003).sin();
                let tall = (0.28 + 0.16 * (k * 7.0 - ms * 0.0026).sin().abs()) * t;
                let c = ramp(&AURORA, (k + ms * 0.0001).fract());
                let edge = 1.0 - arc.abs().powi(3);
                p.curtain(bx, by, 0.075 * t, tall, c, 0.42 * edge);
            }
            for i in 0..8u32 {
                let (ph, c) = cycle(now, i, 2000);
                let mx = x + (h(i, c) - 0.5) * 1.3 * t;
                let my = y - (0.9 + 0.5 * h(i + 3, c)) * t + ph * 0.6 * t;
                p.dot(mx, my, 0.04 * t, ramp(&AURORA, h(i + 7, c)), (ph * PI).sin());
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn looks_by_name_and_steps_by_distance() {
        assert_eq!(SkinLook::named("aurora"), Some(SkinLook::Aurora));
        assert_eq!(SkinLook::named("nope"), None);
        let mut t = Trails::default();
        t.walk(1, 10.0, 10.0, 0);
        t.walk(1, 10.1, 10.0, 50);
        assert!(t.marks(1, 60).is_empty());
        t.walk(1, 10.5, 10.0, 100);
        t.walk(1, 11.0, 10.0, 200);
        assert_eq!(t.marks(1, 210).len(), 2);
        // they fade, and go
        assert!(t.marks(1, 210)[0].2 < 0.2);
        t.walk(1, 11.0, 10.0, 200 + MARK_MS + 10);
        assert!(t.marks(1, 200 + MARK_MS + 20).is_empty());
        // a jump (a teleport) leaves none
        t.walk(1, 40.0, 40.0, 5000);
        assert!(t.marks(1, 5010).is_empty());
    }

    #[test]
    fn cycles_wrap() {
        let (a, c0) = cycle(0, 0, 1000);
        let (b, c1) = cycle(1500, 0, 1000);
        assert!((a - 0.0).abs() < 1e-6 && (b - 0.5).abs() < 1e-6 && c1 == c0 + 1);
    }
}
