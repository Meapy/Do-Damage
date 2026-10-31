//! Cherry blossom falling where the ground says so: grounds marked `<Petals/>` (the Nexus
//! garden's forest floor, tools/build_nexus_garden.py) let petals drift down around us, more the
//! more of that ground is near. A ground can say how thickly (`<Petals>3</Petals>`, round the
//! elder cherry), and petals there start higher up, as from a tall crown. They fall through the
//! world, not across the screen, so they turn with the camera and stay put as we walk; each
//! tumbles as it falls, lands and fades. The gardens players keep to themselves (the vault and the
//! guild halls) have blossom falling all over them, whatever the ground (`map_strength`).

use bsk_data::GameData;
use bsk_render::{Renderer, Tint};
use bsk_world::World;

use crate::view::{Camera, TILE_PX};

/// How far round us the ground is looked at, in tiles, and how far away petals start.
const LOOK: i32 = 6;
const SPREAD: f64 = 11.0;
/// Petals a second at full strength (1), at most this strength, and at most this many at once.
const RATE: f64 = 32.0;
const STRONGEST: f64 = 3.0;
const MAX: usize = 900;
/// How long a landed petal takes to fade, in ms.
const FADE: f64 = 1500.0;

const COLOURS: [u32; 4] = [0xF8B6CF, 0xFFDBE8, 0xE98CB2, 0xFFF3F7];

struct Petal {
    x: f64,
    y: f64,
    z: f64,
    fall: f64,
    sway: f64,
    phase: f64,
    colour: u32,
    landed: Option<f64>,
}

#[derive(Default)]
pub struct Petals {
    petals: Vec<Petal>,
    /// Petals owed to the next frame (spawning is fractional at low strength).
    due: f64,
    /// How much of the ground near us is petal ground, 0 to 1, eased toward its goal.
    strength: f64,
    /// When the ground was last looked at, and what it said.
    looked: i32,
    goal: f64,
    seed: u32,
}

/// How thickly blossom falls all over a map whatever its ground: the vault's and the guild halls'
/// gardens (tools/build_vault.py, build_guild_halls.py) everywhere, other maps by their ground.
pub fn map_strength(map: &str) -> f64 {
    if map == "Vault" || map.starts_with("Guild Hall") {
        1.0
    } else {
        0.0
    }
}

impl Petals {
    fn random(&mut self) -> f64 {
        // xorshift: cheap and good enough for petals
        let mut s = self.seed.max(1);
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        self.seed = s;
        s as f64 / u32::MAX as f64
    }

    /// Moves the petals on by `dt` ms and lets new ones fall near the player; at least
    /// `everywhere`'s worth wherever we stand (`map_strength`).
    pub fn update(&mut self, world: &World, data: &GameData, now: i32, dt: i32, everywhere: f64) {
        let Some(me) = world.player() else {
            self.petals.clear();
            return;
        };
        if self.seed == 0 {
            self.seed = (now as u32) | 1;
        }
        // the ground under and round us, four times a second
        if now - self.looked >= 250 || now < self.looked {
            self.looked = now;
            let (cx, cy) = (me.x as i32, me.y as i32);
            let (mut petal, mut all) = (0.0, 0);
            for y in cy - LOOK..=cy + LOOK {
                for x in cx - LOOK..=cx + LOOK {
                    if (x - cx) * (x - cx) + (y - cy) * (y - cy) > LOOK * LOOK {
                        continue;
                    }
                    all += 1;
                    // `<Petals/>` is 1, `<Petals>3</Petals>` three times as thick
                    if let Some(g) = world.tile(x, y).and_then(|t| data.grounds.get(&t)).filter(|g| g.xml.has("Petals")) {
                        petal += g.xml.text_of("Petals").and_then(|s| s.trim().parse::<f64>().ok()).unwrap_or(1.0);
                    }
                }
            }
            self.goal = if all == 0 { 0.0 } else { (petal / all as f64 * 1.6).min(STRONGEST) }.max(everywhere);
        }
        let secs = dt as f64 / 1000.0;
        self.strength += (self.goal - self.strength) * (secs * 1.5).min(1.0);

        self.due += self.strength * RATE * secs;
        while self.due >= 1.0 {
            self.due -= 1.0;
            if self.petals.len() >= MAX {
                continue;
            }
            // upwind of us, so the breeze carries them across
            let a = self.random() * std::f64::consts::TAU;
            let r = self.random().sqrt() * SPREAD;
            let petal = Petal {
                x: me.x + a.cos() * r - 2.0,
                y: me.y + a.sin() * r - 1.5,
                // from higher where the ground is thicker with them: under a great tree's crown
                z: 2.5 + self.random() * 3.5 * (1.0 + (self.strength - 1.0).max(0.0) * 0.5),
                fall: 0.45 + self.random() * 0.35,
                sway: 0.6 + self.random() * 0.8,
                phase: self.random() * std::f64::consts::TAU,
                colour: COLOURS[(self.random() * COLOURS.len() as f64) as usize % COLOURS.len()],
                landed: None,
            };
            self.petals.push(petal);
        }

        let t = now as f64 / 1000.0;
        for p in &mut self.petals {
            if p.landed.is_some() {
                continue;
            }
            // a breeze toward the south-east, a flutter across it
            let flutter = (t * p.sway * 2.2 + p.phase).sin();
            p.x += (0.55 + flutter * 0.35) * secs;
            p.y += (0.3 - flutter * 0.2) * secs;
            p.z -= p.fall * (0.75 + 0.25 * (t * 3.1 + p.phase).cos().abs()) * secs;
            if p.z <= 0.0 {
                p.z = 0.0;
                p.landed = Some(now as f64);
            }
        }
        let now = now as f64;
        self.petals.retain(|p| p.landed.is_none_or(|at| now - at < FADE) && (p.x - me.x).abs() < SPREAD * 2.0 && (p.y - me.y).abs() < SPREAD * 2.0);
    }

    /// Draws the petals: each a pixel or two of the art's size, tumbling (wide, then narrow).
    pub fn draw(&self, cam: &Camera, renderer: &mut Renderer, now: i32) {
        if self.petals.is_empty() {
            return;
        }
        // one pixel of the art: an 8-pixel sprite at size 100 is 40 screen pixels of a 50 one tile
        let px = (cam.scale / TILE_PX * 5.0) as f32;
        let white = renderer.white();
        let t = now as f64 / 1000.0;
        for p in &self.petals {
            let alpha = match p.landed {
                Some(at) => (1.0 - (now as f64 - at) / FADE).clamp(0.0, 1.0) as f32 * 0.9,
                None => 0.95,
            };
            if alpha <= 0.0 {
                continue;
            }
            let [sx, sy] = cam.to_screen(p.x, p.y, p.z);
            let wide = p.landed.is_some() || (t * p.sway * 3.0 + p.phase).sin() > -0.2;
            let (w, h) = if wide { (2.0 * px, px) } else { (px, px) };
            let quad = bsk_render::Quad::rect((sx - w / 2.0).round(), (sy - h / 2.0).round(), w, h);
            renderer.sprite(white, quad, Tint::color(p.colour, alpha));
            if wide && p.landed.is_none() {
                // its darker heart, so it reads as a petal and not a speck
                let dark = Tint::color(0xC86492, alpha * 0.8);
                renderer.sprite(white, bsk_render::Quad::rect((sx - w / 2.0).round(), (sy - h / 2.0).round(), px, h), dark);
            }
        }
    }
}
