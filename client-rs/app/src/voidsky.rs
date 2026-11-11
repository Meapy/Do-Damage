//! The sky behind the Black Between (`Background` `SKY_VOID`): the void between the stars, deep
//! indigo fading to black, faint drifts of nebula in the Mysterious One's two colours (his
//! daughter's violet, his son's gold), stars that twinkle, and now and then one falling. It stays
//! put as the camera moves, as the Nexus's sunset does (`sunset.rs`), and is drawn first, so it
//! shows wherever there is no ground, past the map's edges too; the platform's rocky undersides
//! hang into it (`islands.rs`). Drawn in cells the size of the art's pixels, so it reads as pixel
//! art.

use bsk_render::Renderer;

use crate::view::{Camera, TILE_PX};

/// `MapInfo.background` for this sky (Dungeons.xml `<Background>`).
pub const SKY_VOID: u8 = 8;

/// The sky's bands from the top of the screen to the bottom: the deepest black low down, where the
/// drop goes on for ever.
const BANDS: [u32; 12] =
    [0x1A1236, 0x181134, 0x161031, 0x140E2D, 0x120D29, 0x100B24, 0x0E0A20, 0x0C081B, 0x0A0717, 0x080512, 0x06040E, 0x05030A];

/// Nebula clouds: violet and gold, faint.
const NEBULAE: [(u32, f32); 3] = [(0x5A2A90, 0.16), (0x7A3AD0, 0.10), (0x8A6420, 0.10)];

/// A falling star in a window of `STAR_EVERY` ms, most windows.
const STAR_EVERY: i64 = 14_000;
const STAR_MS: f32 = 800.0;

struct Lcg(u32);

impl Lcg {
    fn next(&mut self) -> f32 {
        self.0 = self.0.wrapping_mul(1664525).wrapping_add(1013904223);
        self.0 as f32 / 4294967296.0
    }
}

/// A star: its cell, its brightness (0 dim to 1), and the phase and speed of its twinkle.
struct Star {
    x: f32,
    y: f32,
    bright: f32,
    phase: f32,
    speed: f32,
}

#[derive(Default)]
pub struct VoidSky {
    /// The window size and cell size it was built for.
    built: (u32, u32, u32),
    stars: Vec<Star>,
    /// Nebula puffs: (cell x, cell y, radius in cells, colour index).
    puffs: Vec<(f32, f32, f32, usize)>,
}

impl VoidSky {
    fn build(&mut self, w: u32, h: u32, cell: u32) {
        self.built = (w, h, cell);
        let (cw, ch) = (w as f32 / cell as f32, h as f32 / cell as f32);
        let mut r = Lcg(0xB1AC_B37E);
        self.stars.clear();
        let count = ((cw * ch) / 90.0).clamp(60.0, 900.0) as usize;
        for _ in 0..count {
            self.stars.push(Star {
                x: (r.next() * cw).floor(),
                y: (r.next() * ch).floor(),
                bright: r.next().powf(2.2),
                phase: r.next() * std::f32::consts::TAU,
                speed: 0.6 + r.next() * 2.2,
            });
        }
        self.puffs.clear();
        let clouds = ((cw * ch) / 5000.0).clamp(3.0, 10.0) as usize;
        for i in 0..clouds {
            let (cx, cy) = (r.next() * cw, r.next() * ch);
            let k = i % NEBULAE.len();
            // a cloud is a few overlapping puffs
            for _ in 0..5 {
                self.puffs.push((cx + (r.next() - 0.5) * 30.0, cy + (r.next() - 0.5) * 16.0, 6.0 + r.next() * 12.0, k));
            }
        }
    }

    /// The sky over the whole window, under everything else.
    pub fn draw(&mut self, renderer: &mut Renderer, cam: &Camera, time: i32) {
        let (w, h) = renderer.size();
        let cell = ((cam.scale / TILE_PX * 5.0).round() as u32).clamp(2, 12);
        if self.built != (w, h, cell) {
            self.build(w, h, cell);
        }
        let px = cell as f32;
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
        // the nebulae: each puff a few rings of cells, fainter outward, breathing slowly
        let secs = time as f32 / 1000.0;
        for &(cx, cy, rad, k) in &self.puffs {
            let (colour, alpha) = NEBULAE[k];
            let breathe = 0.85 + 0.15 * (secs * 0.3 + cx * 0.1).sin();
            for ring in 0..3 {
                let rr = rad * (1.0 - ring as f32 * 0.3);
                let a = alpha * breathe * (0.35 + ring as f32 * 0.25);
                let steps = (rr * 1.2) as i32;
                for j in -steps..=steps {
                    let dy = j as f32 / 1.2;
                    let half = (rr * rr - dy * dy).max(0.0).sqrt();
                    renderer.fill(((cx - half) * px).round(), ((cy + dy) * px).round(), (2.0 * half * px).round(), px, colour, a / 3.0);
                }
            }
        }
        for s in &self.stars {
            let twinkle = 0.55 + 0.45 * (secs * s.speed + s.phase).sin();
            let a = (0.25 + 0.75 * s.bright) * twinkle;
            let colour = if s.bright > 0.8 { 0xFFFFFF } else if s.bright > 0.4 { 0xD8D0FF } else { 0x8A80C0 };
            renderer.fill(s.x * px, s.y * px, px, px, colour, a);
            if s.bright > 0.9 {
                // the brightest have a little cross
                renderer.fill((s.x - 1.0) * px, s.y * px, 3.0 * px, px, colour, a * 0.35);
                renderer.fill(s.x * px, (s.y - 1.0) * px, px, 3.0 * px, colour, a * 0.35);
            }
        }
        self.falling_star(renderer, w as f32, h as f32, px, time);
    }

    /// Now and then a star falls: a white head, a trail fading through violet into gold.
    fn falling_star(&self, renderer: &mut Renderer, w: f32, h: f32, px: f32, time: i32) {
        let t = time as i64;
        let window = t.div_euclid(STAR_EVERY);
        let mut r = Lcg((window as u32).wrapping_mul(2_654_435_761) ^ 0x5EED_B1AC);
        if r.next() > 0.8 {
            return;
        }
        let start = r.next() * (STAR_EVERY as f32 - STAR_MS - 500.0);
        let since = (t - window * STAR_EVERY) as f32 - start;
        if !(0.0..STAR_MS).contains(&since) {
            return;
        }
        let p = since / STAR_MS;
        let (x, y) = (w * (0.1 + r.next() * 0.8), h * (0.05 + r.next() * 0.5));
        let dir = if r.next() < 0.5 { -1.0 } else { 1.0 };
        let angle: f32 = 0.5 + r.next() * 0.4;
        let (dx, dy) = (dir * angle.cos(), angle.sin());
        let travel = 70.0 * px;
        let (hx, hy) = (x + dx * travel * p, y + dy * travel * p);
        let fade = (p * std::f32::consts::PI).sin().min(1.0);
        const TRAIL: [u32; 4] = [0xFFFFFF, 0xE0B0FF, 0xB040FF, 0xFFD24A];
        let len = 22;
        for i in 0..len {
            let k = i as f32 / len as f32;
            let (cx, cy) = (hx - dx * k * len as f32 * px * 0.9, hy - dy * k * len as f32 * px * 0.9);
            let colour = TRAIL[((k * TRAIL.len() as f32) as usize).min(TRAIL.len() - 1)];
            renderer.fill((cx / px).round() * px, (cy / px).round() * px, px, px, colour, fade * (1.0 - k).powf(1.4));
        }
    }
}
