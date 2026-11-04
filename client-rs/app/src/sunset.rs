//! The sky behind a world of floating islands (the Nexus islands, `Background` `SKY_SUNSET`):
//! a daytime sunset, blue high up fading through lavender into pink and peach low down, soft
//! clouds drifting slowly across it in two layers (the far ones slower, fainter), and now and then
//! a shooting star in the game's pinks. It stays put as the camera moves and climbs (a background
//! that followed the camera jittered as the camera's height changed). Drawn first, under
//! everything, so it shows wherever there is no ground (and the islands' undersides hang into
//! it). Like the screens' night sky (`sky.rs`) it is drawn in cells the size of the art's pixels,
//! so it reads as pixel art.

use bsk_render::Renderer;

use crate::view::{Camera, TILE_PX};

/// `MapInfo.background` for this sky (Worlds.xml / Dungeons.xml `<Background>`).
pub const SKY_SUNSET: u8 = 7;

/// The sky's bands from the top of the screen (high: blue) to the bottom (low: the sunset).
const BANDS: [u32; 20] = [
    0x27407F, 0x2D4889, 0x345193, 0x3C5B9D, 0x4666A8, 0x5172B2, 0x5E7FBB, 0x6C8CC4, 0x7C98CB, 0x8EA3D1,
    0xA1ACD5, 0xB4B2D6, 0xC6B4D4, 0xD5B1CD, 0xE2ADC6, 0xEDA9BD, 0xF5AAB2, 0xFAB1A8, 0xFDBCA2, 0xFFC9A1,
];

/// Clouds: far (small, pale, faint, slow) and near; their tones (shadow, body, light) and how
/// opaque they are.
const FAR: ([u32; 3], f32) = ([0xC6A9D2, 0xDCC3E2, 0xEEDDF0], 0.55);
const NEAR: ([u32; 3], f32) = ([0xE6A8C2, 0xF8D7E2, 0xFFF1F1], 0.8);

/// A shooting star in a window of `STAR_EVERY` ms, most windows (so one every 10 to 30 s or so).
const STAR_EVERY: i64 = 20_000;
const STAR_MS: f32 = 900.0;

struct Lcg(u32);

impl Lcg {
    fn next(&mut self) -> f32 {
        self.0 = self.0.wrapping_mul(1664525).wrapping_add(1013904223);
        self.0 as f32 / 4294967296.0
    }
}

/// A cloud: puffs rasterised into rows of cells, each row's runs of shadow, body and light.
struct Cloud {
    near: bool,
    /// Its place in the wrapping band of sky, and its width, in cells.
    x: f32,
    y: f32,
    w: f32,
    /// Cells a second it drifts.
    speed: f32,
    /// (row, first cell, last cell, tone 0-2).
    runs: Vec<(i32, i32, i32, u8)>,
}

#[derive(Default)]
pub struct Sunset {
    /// The window size and cell size it was built for.
    built: (u32, u32, u32),
    clouds: Vec<Cloud>,
    /// How far the clouds wrap round, in cells.
    wrap: f32,
}

impl Sunset {
    fn build(&mut self, w: u32, h: u32, cell: u32) {
        self.built = (w, h, cell);
        let (cw, ch) = (w as f32 / cell as f32, h as f32 / cell as f32);
        self.wrap = cw * 1.6 + 60.0;
        self.clouds.clear();
        let mut r = Lcg(0x5EED_1A5);
        let count = ((cw * ch) / 2600.0).clamp(10.0, 40.0) as usize;
        for i in 0..count {
            let near = i % 3 == 0;
            let scale = if near { 1.0 } else { 0.6 };
            let w = (24.0 + r.next() * 30.0) * scale;
            let mut runs = Vec::new();
            // a few round puffs side by side, the middle ones tallest, on a flat base
            let puffs = 3 + (r.next() * 3.0) as i32;
            let mut circles = Vec::new();
            for p in 0..puffs {
                let t = (p as f32 + 0.5) / puffs as f32;
                let rad = w * (0.16 + 0.14 * (1.0 - (t * 2.0 - 1.0).abs())) * (0.85 + r.next() * 0.3);
                circles.push((w * t, 0.0 - rad * 0.35, rad));
            }
            let top = circles.iter().map(|c| c.1 - c.2).fold(0.0f32, f32::min).floor() as i32;
            let base = 2;
            for row in top..=base {
                let y = row as f32 + 0.5;
                // the cloud's extent along this row: the union of the puffs (and the flat base)
                let mut lo = f32::MAX;
                let mut hi = f32::MIN;
                for &(cx, cy, rad) in &circles {
                    let dy = y - cy;
                    if dy.abs() <= rad && y <= base as f32 {
                        let dx = (rad * rad - dy * dy).sqrt();
                        lo = lo.min(cx - dx);
                        hi = hi.max(cx + dx);
                    }
                }
                if row >= 0 {
                    lo = lo.min(w * 0.08);
                    hi = hi.max(w * 0.92);
                }
                if lo > hi {
                    continue;
                }
                let (a, b) = (lo.round() as i32, hi.round() as i32);
                // shadow along the bottom two rows, light along the top of each puff
                let tone = if row >= base - 1 { 0 } else if row <= top + 1 { 2 } else { 1 };
                runs.push((row, a, b, tone));
                // a light rim on the upper left of the body rows
                if tone == 1 && row < 0 {
                    runs.push((row, a, a + ((b - a) / 5).max(1), 2));
                }
            }
            self.clouds.push(Cloud {
                near,
                x: r.next() * self.wrap,
                y: ch * (0.04 + r.next() * 0.92),
                w,
                speed: if near { 1.2 + r.next() * 0.8 } else { 0.5 + r.next() * 0.5 },
                runs,
            });
        }
        // far ones first
        self.clouds.sort_by_key(|c| c.near);
    }

    /// The sky over the whole window, under everything else.
    pub fn draw(&mut self, renderer: &mut Renderer, cam: &Camera, time: i32) {
        let (w, h) = renderer.size();
        let cell = ((cam.scale / TILE_PX * 5.0).round() as u32).clamp(2, 12);
        if self.built != (w, h, cell) {
            self.build(w, h, cell);
        }
        let px = cell as f32;
        // the bands, each a whole number of cells tall
        let rows = (h as f32 / px).ceil() as usize;
        let per = (rows as f32 / BANDS.len() as f32).max(1.0);
        let mut y = 0usize;
        for (i, &c) in BANDS.iter().enumerate() {
            let end = if i + 1 == BANDS.len() { rows } else { ((i + 1) as f32 * per).round() as usize };
            if end > y {
                renderer.fill(0.0, y as f32 * px, w as f32, (end - y) as f32 * px, c, 1.0);
            }
            y = end;
        }

        self.shooting_star(renderer, w as f32, h as f32, px, time);

        // drifting with time only, a whole pixel at a time (not a cell: smooth)
        let secs = (time as f64 / 1000.0) as f32;
        for c in &self.clouds {
            let (tones, alpha) = if c.near { NEAR } else { FAR };
            let drift = c.x + secs * c.speed;
            let sx = ((drift * px).rem_euclid(self.wrap * px) - c.w * px - 30.0 * px).round();
            let sy = (c.y * px).round();
            if sx > w as f32 || sx + c.w * px < 0.0 {
                continue;
            }
            for &(row, a, b, tone) in &c.runs {
                renderer.fill(sx + a as f32 * px, sy + row as f32 * px, (b - a + 1) as f32 * px, px, tones[tone as usize], alpha);
            }
        }
    }

    /// A streak across the upper sky every so often: a white head, a trail fading through pink.
    fn shooting_star(&self, renderer: &mut Renderer, w: f32, h: f32, px: f32, time: i32) {
        let t = time as i64;
        let window = t.div_euclid(STAR_EVERY);
        let mut r = Lcg((window as u32).wrapping_mul(2_654_435_761) ^ 0xA5A5_1234);
        if r.next() > 0.85 {
            return;
        }
        let start = r.next() * (STAR_EVERY as f32 - STAR_MS - 500.0);
        let since = (t - window * STAR_EVERY) as f32 - start;
        if !(0.0..STAR_MS).contains(&since) {
            return;
        }
        let p = since / STAR_MS;
        let (x, y) = (w * (0.15 + r.next() * 0.7), h * (0.05 + r.next() * 0.3));
        let dir = if r.next() < 0.5 { -1.0 } else { 1.0 };
        let angle: f32 = 0.35 + r.next() * 0.3;
        let (dx, dy) = (dir * angle.cos(), angle.sin());
        let travel = 90.0 * px;
        let (hx, hy) = (x + dx * travel * p, y + dy * travel * p);
        // brightest in the middle of its flight
        let fade = (p * std::f32::consts::PI).sin().min(1.0);
        const TRAIL: [u32; 4] = [0xFFFFFF, 0xFFD6EA, 0xFF9BC8, 0xC77DFF];
        let len = 26;
        for i in 0..len {
            let k = i as f32 / len as f32;
            let (cx, cy) = (hx - dx * k * len as f32 * px * 0.9, hy - dy * k * len as f32 * px * 0.9);
            let colour = TRAIL[((k * TRAIL.len() as f32) as usize).min(TRAIL.len() - 1)];
            let a = fade * (1.0 - k).powf(1.4);
            renderer.fill((cx / px).round() * px, (cy / px).round() * px, px, px, colour, a);
        }
        // the head's glow
        renderer.fill((hx / px).round() * px - px, (hy / px).round() * px, 3.0 * px, px, 0xFFFFFF, fade * 0.5);
        renderer.fill((hx / px).round() * px, (hy / px).round() * px - px, px, 3.0 * px, 0xFFFFFF, fade * 0.5);
    }
}

