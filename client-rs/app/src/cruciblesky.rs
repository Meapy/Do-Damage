//! The sky round the Crucible's floors (`Background` `SKY_CRUCIBLE`): the tower's open air, its
//! colours changing floor by floor as the climb goes up (dusk over the Threshold, a forge-lit
//! twilight round the Iron Bastion, a sea-green deep round the Drowned Sanctum, smoke and fire
//! round the Ember Foundry, a starry night round the Clockwork Orrery, a golden dawn above the
//! clouds round the Apex). Embers drift up past the floor; banks of cloud drift by below. In an
//! ascent (`crucible::Climb::sky`) the colours blend from one floor's to the next, and the clouds
//! and embers stream down past us as we rise; on a way up, the same, slowly, as far as the climb
//! has walked it. It stays put as the camera moves and is drawn first,
//! wherever there is no ground; the floors' rocky undersides hang into it (`islands.rs`). Drawn in
//! cells the size of the art's pixels, as `voidsky.rs` is.

use bsk_render::Renderer;

use crate::view::{Camera, TILE_PX};

/// `MapInfo.background` for this sky (Dungeons.xml `<Background>`).
pub const SKY_CRUCIBLE: u8 = bsk_world::BACKGROUND_OPEN_AIR;

/// Each floor's sky (0 the Threshold): top, horizon, embers, cloud, stars (how many show).
#[derive(Clone, Copy)]
struct Palette {
    top: u32,
    low: u32,
    ember: u32,
    cloud: u32,
    stars: f32,
}

const PALETTES: [Palette; 6] = [
    Palette { top: 0x161220, low: 0x3A2430, ember: 0xFFB060, cloud: 0x4A3446, stars: 0.5 },
    Palette { top: 0x10141C, low: 0x4A2A1C, ember: 0xFF9A2A, cloud: 0x3A3E4A, stars: 0.3 },
    Palette { top: 0x041A22, low: 0x0E4A54, ember: 0x8AF0E0, cloud: 0x1A5A66, stars: 0.2 },
    Palette { top: 0x160604, low: 0x6A1E0A, ember: 0xFFB03A, cloud: 0x4A1A12, stars: 0.0 },
    Palette { top: 0x080618, low: 0x2A2050, ember: 0xF2D282, cloud: 0x2A2448, stars: 1.0 },
    Palette { top: 0x3A2A6A, low: 0xF0A860, ember: 0xFFF0B0, cloud: 0xFFD8B0, stars: 0.0 },
];

fn mix(a: u32, b: u32, t: f32) -> u32 {
    let ch = |s: u32| {
        let (x, y) = (((a >> s) & 255) as f32, ((b >> s) & 255) as f32);
        ((x + (y - x) * t).round() as u32).min(255) << s
    };
    ch(16) | ch(8) | ch(0)
}

fn blend(a: &Palette, b: &Palette, t: f32) -> Palette {
    Palette {
        top: mix(a.top, b.top, t),
        low: mix(a.low, b.low, t),
        ember: mix(a.ember, b.ember, t),
        cloud: mix(a.cloud, b.cloud, t),
        stars: a.stars + (b.stars - a.stars) * t,
    }
}

struct Lcg(u32);

impl Lcg {
    fn next(&mut self) -> f32 {
        self.0 = self.0.wrapping_mul(1664525).wrapping_add(1013904223);
        self.0 as f32 / 4294967296.0
    }
}

#[derive(Default)]
pub struct CrucibleSky {
    built: (u32, u32, u32),
    /// (cell x, cell y, size, speed, phase)
    embers: Vec<(f32, f32, f32, f32, f32)>,
    /// (cell x, cell y, brightness)
    stars: Vec<(f32, f32, f32)>,
    /// (cell x, cell y, radius, speed)
    clouds: Vec<(f32, f32, f32, f32)>,
    /// Which floor's colours, an ascent's or a walk's progress (from, to, 0 to 1), and how fast
    /// everything streams down past us (0 to 1; an ascent's own rush when 0): set each frame from
    /// the climb (`crucible::Climb::sky`).
    pub state: (u8, Option<(u8, u8, f32)>, f32),
    /// How far the ascents have carried everything down (seconds at full rush), and the last frame's time.
    rushed: f32,
    last: i32,
}

impl CrucibleSky {
    fn build(&mut self, w: u32, h: u32, cell: u32) {
        self.built = (w, h, cell);
        let (cw, ch) = (w as f32 / cell as f32, h as f32 / cell as f32);
        let mut r = Lcg(0xC2_0C1B);
        self.embers = (0..((cw * ch) / 260.0).clamp(30.0, 300.0) as usize)
            .map(|_| (r.next() * cw, r.next() * ch, 1.0 + (r.next() * 1.6).floor(), 0.6 + r.next() * 1.8, r.next() * 6.28))
            .collect();
        self.stars = (0..((cw * ch) / 120.0).clamp(40.0, 600.0) as usize).map(|_| (r.next() * cw, r.next() * ch, r.next())).collect();
        self.clouds = (0..((cw * ch) / 2600.0).clamp(4.0, 16.0) as usize)
            .map(|_| (r.next() * cw, r.next() * ch, 8.0 + r.next() * 16.0, 0.3 + r.next() * 0.7))
            .collect();
    }

    pub fn draw(&mut self, renderer: &mut Renderer, cam: &Camera, time: i32) {
        let (w, h) = renderer.size();
        let cell = ((cam.scale / TILE_PX * 5.0).round() as u32).clamp(2, 12);
        if self.built != (w, h, cell) {
            self.build(w, h, cell);
        }
        let px = cell as f32;
        let (cw, ch) = (w as f32 / px, h as f32 / px);
        let (floor, ascent, drift) = self.state;
        let at = |f: u8| PALETTES[(f as usize).min(PALETTES.len() - 1)];
        // in an ascent: the next floor's colours come in through its middle, and everything
        // streams down past us, fastest at its height
        let (pal, rush) = match ascent {
            Some((from, to, t)) if drift > 0.0 => (blend(&at(from), &at(to), t * t * (3.0 - 2.0 * t)), drift),
            Some((from, to, t)) => {
                let k = ((t - 0.3) / 0.4).clamp(0.0, 1.0);
                (blend(&at(from), &at(to), k * k * (3.0 - 2.0 * k)), (1.0 - (2.0 * t - 1.0).abs()).powf(0.8))
            }
            None => (at(floor), 0.0),
        };
        let secs = time as f32 / 1000.0;
        // the rush builds up a distance of its own: positions from time * rush would jump about as
        // the rush changes
        let dt = ((time - self.last).clamp(0, 100)) as f32 / 1000.0;
        self.last = time;
        self.rushed += dt * rush;
        let rushed = self.rushed;

        // the bands, top to horizon
        const BANDS: usize = 14;
        let rows = ch.ceil() as usize;
        let per = (rows as f32 / BANDS as f32).max(1.0);
        let mut y = 0usize;
        for i in 0..BANDS {
            let end = if i + 1 == BANDS { rows } else { ((i + 1) as f32 * per).round() as usize };
            let t = i as f32 / (BANDS - 1) as f32;
            if end > y {
                renderer.fill(0.0, y as f32 * px, w as f32, (end - y) as f32 * px, mix(pal.top, pal.low, t * t), 1.0);
            }
            y = end;
        }
        // stars, high up, where the floor's sky has them
        if pal.stars > 0.01 {
            for &(x, y, b) in &self.stars {
                if y > ch * 0.7 {
                    continue;
                }
                let tw = 0.6 + 0.4 * (secs * (1.0 + b * 2.0) + x).sin();
                let a = pal.stars * (0.2 + 0.8 * b * b) * tw * (1.0 - y / (ch * 0.7));
                renderer.fill(x.floor() * px, y.floor() * px, px, px, if b > 0.85 { 0xFFFFFF } else { 0xC8C0E8 }, a);
            }
        }
        // banks of cloud drifting by below, streaming down in an ascent
        let drift = secs * 0.8;
        let fall = rushed * 90.0;
        for &(x0, y0, rad, speed) in &self.clouds {
            let x = (x0 + drift * speed).rem_euclid(cw + rad * 4.0) - rad * 2.0;
            let y = (y0 + fall * speed + secs * 0.4 * speed).rem_euclid(ch + rad * 2.0) - rad;
            for ring in 0..3 {
                let rr = rad * (1.0 - ring as f32 * 0.28);
                let a = 0.1 + ring as f32 * 0.06;
                let steps = (rr * 0.6) as i32;
                for j in -steps..=steps {
                    let dy = j as f32 / 0.6;
                    let half = (rr * rr - dy * dy).max(0.0).sqrt() * 1.8;
                    renderer.fill(((x - half) * px).round(), ((y + dy * 0.5) * px).round(), (2.0 * half * px).round(), px, pal.cloud, a);
                }
            }
        }
        // embers rising (or, in an ascent, rushing down past us, stretched into streaks)
        for &(x0, y0, size, speed, phase) in &self.embers {
            let sway = (secs * 0.7 + phase).sin() * 2.0;
            let up = secs * speed * 3.0;
            let down = rushed * speed * 60.0;
            let y = (y0 - up + down).rem_euclid(ch + 8.0) - 4.0;
            let x = (x0 + sway).rem_euclid(cw);
            let flick = 0.55 + 0.45 * (secs * speed * 3.0 + phase).sin();
            let a = (0.35 + 0.5 * flick) * (0.4 + 0.6 * (y / ch));
            let len = 1.0 + rush * 10.0 * speed;
            renderer.fill(x.floor() * px, (y - len).floor() * px, size * px, (size + len) * px, pal.ember, a * (1.0 - rush * 0.4));
        }
    }
}
