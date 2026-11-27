//! The sky round the Ice Tomb (`Background` `SKY_AURORA`): a polar night over the abyss the tomb
//! hangs in. Deep blue fading to black far below, cold stars, curtains of the aurora waving
//! across it (green at their hem, rays rising into violet), snow drifting down through the dark,
//! and a haze of ice mist low down. It stays put as the camera moves and is drawn first, so it
//! shows wherever there is no ground, as the Black Between's void does (`voidsky.rs`); the
//! glacier the tomb is cut into hangs into it (`islands::ICE`). Drawn in cells the size of the
//! art's pixels.

use bsk_render::Renderer;

use crate::view::{Camera, TILE_PX};

/// `MapInfo.background` for this sky (Dungeons.xml `<Background>`).
pub const SKY_AURORA: u8 = 10;

/// The sky's bands from the top of the screen to the bottom.
const BANDS: [u32; 12] =
    [0x0C1A3A, 0x0B1836, 0x0A1632, 0x09142D, 0x081228, 0x071023, 0x060E1E, 0x050C1A, 0x040A16, 0x030812, 0x02060E, 0x02050B];

/// The curtains: (height of the hem down the screen 0..1, amplitude in cells, wavelength in
/// cells, drift speed, ray height in cells, brightness).
const CURTAINS: [(f32, f32, f32, f32, f32, f32); 3] =
    [(0.30, 7.0, 70.0, 0.11, 26.0, 0.55), (0.46, 5.0, 52.0, -0.08, 20.0, 0.42), (0.20, 4.0, 90.0, 0.06, 16.0, 0.30)];
/// The hem's green, the rays' green, the tops' violet.
const HEM: u32 = 0x7CFFC8;
const RAY: u32 = 0x3AD89A;
const TOP: u32 = 0x8A5AE0;

struct Lcg(u32);

impl Lcg {
    fn next(&mut self) -> f32 {
        self.0 = self.0.wrapping_mul(1664525).wrapping_add(1013904223);
        self.0 as f32 / 4294967296.0
    }
}

struct Star {
    x: f32,
    y: f32,
    bright: f32,
    phase: f32,
    speed: f32,
}

/// A snowflake: where it starts (0..1 of the window), how fast it falls (windows a second), how
/// far it sways, its size in cells.
struct Flake {
    x: f32,
    y: f32,
    fall: f32,
    sway: f32,
    phase: f32,
    size: f32,
}

#[derive(Default)]
pub struct AuroraSky {
    built: (u32, u32, u32),
    stars: Vec<Star>,
    flakes: Vec<Flake>,
}

fn mix(a: u32, b: u32, t: f32) -> u32 {
    let t = t.clamp(0.0, 1.0);
    let ch = |s: u32| {
        let (x, y) = (((a >> s) & 0xFF) as f32, ((b >> s) & 0xFF) as f32);
        ((x + (y - x) * t).round() as u32) << s
    };
    ch(16) | ch(8) | ch(0)
}

impl AuroraSky {
    fn build(&mut self, w: u32, h: u32, cell: u32) {
        self.built = (w, h, cell);
        let (cw, ch) = (w as f32 / cell as f32, h as f32 / cell as f32);
        let mut r = Lcg(0x1CE_70B5);
        self.stars.clear();
        let count = ((cw * ch) / 110.0).clamp(50.0, 700.0) as usize;
        for _ in 0..count {
            self.stars.push(Star {
                x: (r.next() * cw).floor(),
                // fewer low down, where the mist is
                y: (r.next().powf(1.6) * ch).floor(),
                bright: r.next().powf(2.4),
                phase: r.next() * std::f32::consts::TAU,
                speed: 0.5 + r.next() * 1.8,
            });
        }
        self.flakes.clear();
        let flakes = ((cw * ch) / 260.0).clamp(30.0, 260.0) as usize;
        for _ in 0..flakes {
            self.flakes.push(Flake {
                x: r.next(),
                y: r.next(),
                fall: 0.035 + r.next() * 0.05,
                sway: 1.0 + r.next() * 3.0,
                phase: r.next() * std::f32::consts::TAU,
                size: if r.next() < 0.18 { 2.0 } else { 1.0 },
            });
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
        let (cw, ch) = (w as f32 / px, h as f32 / px);
        let rows = ch.ceil() as usize;
        let per = (rows as f32 / BANDS.len() as f32).max(1.0);
        let mut y = 0usize;
        for (i, &c) in BANDS.iter().enumerate() {
            let end = if i + 1 == BANDS.len() { rows } else { ((i + 1) as f32 * per).round() as usize };
            if end > y {
                renderer.fill(0.0, y as f32 * px, w as f32, (end - y) as f32 * px, c, 1.0);
            }
            y = end;
        }
        let secs = (time % 3_600_000) as f32 / 1000.0;
        for s in &self.stars {
            let twinkle = 0.55 + 0.45 * (secs * s.speed + s.phase).sin();
            let a = (0.2 + 0.8 * s.bright) * twinkle;
            let colour = if s.bright > 0.8 { 0xFFFFFF } else if s.bright > 0.4 { 0xCFE6FF } else { 0x7A90C0 };
            renderer.fill(s.x * px, s.y * px, px, px, colour, a);
            if s.bright > 0.92 {
                renderer.fill((s.x - 1.0) * px, s.y * px, 3.0 * px, px, colour, a * 0.3);
                renderer.fill(s.x * px, (s.y - 1.0) * px, px, 3.0 * px, colour, a * 0.3);
            }
        }
        self.curtains(renderer, cw, ch, px, secs);
        // ice mist low down, breathing
        let mist_rows = (ch * 0.22).ceil() as i32;
        for i in 0..mist_rows {
            let k = i as f32 / mist_rows.max(1) as f32;
            let a = 0.10 * k * k * (0.85 + 0.15 * (secs * 0.4 + i as f32 * 0.3).sin());
            renderer.fill(0.0, (ch - mist_rows as f32 + i as f32) * px, w as f32, px, 0x6A9AC8, a);
        }
        // snow drifting down through the dark
        for f in &self.flakes {
            let fy = ((f.y + secs * f.fall) % 1.0) * ch;
            let fx = (f.x * cw + (secs * 0.6 + f.phase).sin() * f.sway + secs * 0.8) % cw;
            let a = 0.35 + 0.35 * (fy / ch);
            renderer.fill(fx.floor() * px, fy.floor() * px, f.size * px, f.size * px, 0xEAF4FF, a);
        }
    }

    /// The aurora's curtains: each a hem winding across the sky and drifting, rays standing up
    /// off it, brightest at the hem, green going violet as they rise; the light ripples along.
    fn curtains(&self, renderer: &mut Renderer, cw: f32, ch: f32, px: f32, secs: f32) {
        const SEGMENTS: usize = 6;
        let cols = cw.ceil() as i32;
        for &(height, amp, wave, drift, rays, bright) in &CURTAINS {
            let breathe = 0.75 + 0.25 * (secs * 0.23 + height * 9.0).sin();
            for x in 0..cols {
                let xf = x as f32;
                let phase = xf / wave * std::f32::consts::TAU + secs * drift * 6.0;
                // three waves out of step, so the hem never repeats
                let hem = ch * height
                    + amp * phase.sin()
                    + amp * 0.45 * (phase * 2.3 + secs * 0.5).sin()
                    + amp * 0.8 * (xf * 0.031 - secs * 0.07 + height * 11.0).sin();
                // the light rippling along the hem, and fading at the curtain's ends
                let ripple = 0.55 + 0.45 * (xf * 0.21 - secs * 1.7 + height * 5.0).sin();
                // the curtain thins out in places along its length, and towards the window's sides
                let ends = ((xf / cols as f32 * std::f32::consts::PI).sin()).powf(0.6)
                    * (0.35 + 0.65 * (0.5 + 0.5 * (xf * 0.019 + secs * 0.05 + height * 7.0).sin()));
                // rays of uneven height, flickering
                let tall = rays * (0.55 + 0.25 * (xf * 0.13 + secs * 0.9).sin() + 0.2 * (xf * 0.71 + secs * 2.3).sin().abs());
                let a = bright * breathe * ripple * ends;
                if a < 0.02 {
                    continue;
                }
                renderer.fill(xf * px, hem.floor() * px, px, px, HEM, a);
                let seg = tall / SEGMENTS as f32;
                for s in 0..SEGMENTS {
                    let k = s as f32 / SEGMENTS as f32;
                    let colour = if k < 0.5 { mix(HEM, RAY, k * 2.0) } else { mix(RAY, TOP, (k - 0.5) * 2.0) };
                    let top = hem - seg * (s + 1) as f32;
                    renderer.fill(xf * px, top.floor() * px, px, seg.ceil() * px, colour, a * 0.45 * (1.0 - k).powf(1.3));
                }
            }
        }
    }
}
