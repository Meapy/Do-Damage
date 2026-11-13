//! The Necromantic items' powers at work (`ShowEffect` 41, sent by the server's
//! `Player.RelicPowers.cs` `RelicFx`): each power's own look, by the number in pos2.y (the
//! server's `Fx`, the same numbers as `look` here). A look is particles, maybe a picture on the
//! ground (a `Decal`, drawn with the ground marks, under what stands on it) and maybe an `Aura`
//! that keeps giving off particles round an object for a while.
//!
//! pos1 is where it happens, pos2.x its radius, `duration` how long it lasts in ms (the
//! Phylactery's: how many souls), and the target object is the one it follows or flies to.
//!
//! Not only the Necromantic items: look 29 is the Chaotic Scripture's Chaos Beam, sent by the
//! server's `AllyAOE` (`BehaviorDb.Allies.cs`).

use super::{Motion, Particle, Particles};
use crate::view::Camera;
use bsk_proto::ShowEffect;
use bsk_render::{Renderer, Tint};
use bsk_world::World;
use std::f64::consts::PI;

/// The looks, as the server numbers them (`Player.RelicPowers.cs` `Fx`).
pub mod look {
    pub const CANNON: u8 = 1;
    pub const VENOM: u8 = 2;
    pub const SERPENT_WIND: u8 = 3;
    pub const STONE_GAZE: u8 = 4;
    pub const SOUL_GATHER: u8 = 5;
    pub const SOUL_RELEASE: u8 = 6;
    pub const PHASE: u8 = 7;
    pub const HELLBRAND: u8 = 8;
    pub const BLOOD: u8 = 9;
    pub const MASQUERADE: u8 = 10;
    pub const DAVY: u8 = 11;
    pub const TOXIC_SCALES: u8 = 12;
    pub const SKY_BOLT: u8 = 13;
    pub const IGNITION: u8 = 14;
    pub const SHELL_READY: u8 = 15;
    pub const SHELL_BREAK: u8 = 16;
    pub const UNDERTOW: u8 = 17;
    pub const OATH: u8 = 18;
    pub const STARLIGHT: u8 = 19;
    pub const EARTHSHAKER: u8 = 20;
    pub const BLOBOMB: u8 = 21;
    pub const FIRE: u8 = 22;
    pub const ICE: u8 = 23;
    pub const FORGOTTEN: u8 = 24;
    pub const PILLAR: u8 = 25;
    pub const PENTAGRAM: u8 = 26;
    pub const ERUPTION: u8 = 27;
    pub const SINGULARITY: u8 = 28;
    /// The Chaotic Scripture's Chaos Beam (`AllyAOE`, not a Necromantic power).
    pub const CHAOS: u8 = 29;
    pub const LAST: u8 = 29;
}

/// Falling, tiles a millisecond each millisecond (12 tiles a second each second).
const GRAVITY: f64 = 12.0e-6;

/// What a decal draws.
#[derive(Clone, Copy, Debug, PartialEq)]
pub(super) enum DecalKind {
    /// A shockwave: a ring racing out to the radius, fading.
    Ring,
    /// A splash on the floor: a blot of blobs, fading.
    Puddle,
    /// The Soulfire Staff's brand: a burning circle of runes turning under the enemy.
    Sigil,
    /// A whirlpool: dark water, arms of foam turning in.
    Whirlpool,
    /// The Crown of Geb's cracks, glowing as they open, then dark.
    Cracks,
    /// The Skull of the Final Ritual's pentagram, drawn line by line.
    Pentagram,
    /// The Bow of the Event Horizon's black hole: a dark heart, a turning disc of light, pulses
    /// falling in.
    BlackHole,
    /// Frost spreading in spikes.
    Frost,
    /// Scorched ground, its embers dying.
    Scorch,
    /// The Bulwark's golden ward under a player.
    Ward,
    /// The Tome's ring of stars under a player.
    Stars,
    /// The Chaotic Scripture's rift: a torn, glowing void, the eight arrows of chaos turning round
    /// it.
    Rift,
}

/// A picture on the ground: `DecalKind` at x, y, out to `radius`, from `start` for `ms`
/// (following object `follow` while it's there).
#[derive(Clone, Copy, Debug)]
pub(super) struct Decal {
    kind: DecalKind,
    x: f64,
    y: f64,
    radius: f64,
    color: u32,
    start: i32,
    ms: i32,
    z: f64,
    level: u8,
    follow: Option<i32>,
    seed: u64,
    /// How opaque (another player's, faded by the options).
    dim: f32,
}

/// Particles given off round an object (or a spot) until `until`, a few every `period(look)`.
#[derive(Clone, Copy, Debug)]
pub(super) struct Aura {
    look: u8,
    id: i32,
    x: f64,
    y: f64,
    radius: f64,
    color: u32,
    start: i32,
    until: i32,
    next: i32,
    follow: bool,
    /// How faint its particles are made (`Particles::muting`).
    muting: f32,
}

/// How often an aura gives off a particle, ms.
fn period(look: u8) -> i32 {
    match look {
        look::PHASE | look::SINGULARITY => 22,
        look::SERPENT_WIND | look::UNDERTOW | look::FORGOTTEN => 32,
        look::DAVY | look::IGNITION | look::PENTAGRAM => 45,
        look::HELLBRAND | look::CHAOS => 60,
        look::OATH => 120,
        look::STARLIGHT => 140,
        _ => 100,
    }
}

/// Between two colours.
fn mix(a: u32, b: u32, t: f64) -> u32 {
    let t = t.clamp(0.0, 1.0);
    let ch = |s: u32| -> u32 {
        let (x, y) = ((a >> s) & 0xFF, (b >> s) & 0xFF);
        ((x as f64 + (y as f64 - x as f64) * t).round() as u32) << s
    };
    ch(16) | ch(8) | ch(0)
}

/// A number from a decal's seed: the same each frame.
fn hash(seed: u64, i: u64) -> f64 {
    let mut h = seed ^ i.wrapping_mul(0x9E37_79B9_7F4A_7C15);
    h ^= h >> 33;
    h = h.wrapping_mul(0xFF51_AFD7_ED55_8CCD);
    h ^= h >> 33;
    (h >> 11) as f64 / (1u64 << 53) as f64
}

impl Particles {
    fn pick(&mut self, colors: &[u32]) -> u32 {
        colors[(self.random() * colors.len() as f64) as usize % colors.len()]
    }

    fn decal(&mut self, kind: DecalKind, x: f64, y: f64, radius: f64, color: u32, start: i32, ms: i32, follow: Option<i32>) {
        if self.disabled || self.decals.len() >= 64 || self.muting >= 1.0 {
            return;
        }
        let seed = (self.random() * u32::MAX as f64) as u64;
        let (z, level) = self.here;
        self.decals.push(Decal { kind, x, y, radius, color, start, ms: ms.max(1), z, level, follow, seed, dim: 1.0 - self.muting });
    }

    fn aura(&mut self, look: u8, id: i32, x: f64, y: f64, radius: f64, color: u32, now: i32, ms: i32, follow: bool) {
        if self.disabled || self.auras.len() >= 48 || self.muting >= 1.0 {
            return;
        }
        // the same power again on the same object: the newer one carries on
        self.auras.retain(|a| !(a.look == look && a.id == id && follow));
        self.auras.push(Aura { look, id, x, y, radius, color, start: now, until: now + ms.max(1), next: now, follow, muting: self.muting });
    }

    /// Pieces thrown up and out, falling back and bouncing.
    #[allow(clippy::too_many_arguments)]
    fn debris(&mut self, x: f64, y: f64, n: usize, colors: &[u32], size: f64, speed: f64, up: f64, life: i32, delay: i32) {
        for _ in 0..n {
            let a = 2.0 * PI * self.random();
            let s = speed * (0.35 + 0.65 * self.random());
            let vz = up * (0.6 + 0.6 * self.random());
            let color = self.pick(colors);
            let l = life / 2 + (self.random() * life as f64 / 2.0) as i32;
            let size = size * (0.7 + 0.6 * self.random());
            let mut p = Particle::new(x, y, 0.3, color, size, l.max(1), Motion::Ballistic { g: GRAVITY })
                .moving(a.cos() * s / 1000.0, a.sin() * s / 1000.0, vz / 1000.0);
            p.delay = delay;
            p.fade = true;
            self.add(p);
        }
    }

    /// Smoke or mist: puffs drifting out and up, growing as they fade.
    #[allow(clippy::too_many_arguments)]
    fn puffs(&mut self, x: f64, y: f64, n: usize, colors: &[u32], size: f64, spread: f64, rise: f64, life: i32, delay: i32) {
        for i in 0..n {
            let a = 2.0 * PI * self.random();
            let s = spread * (0.3 + 0.7 * self.random());
            let color = self.pick(colors);
            let l = life * 2 / 3 + (self.random() * life as f64 / 3.0) as i32;
            let (jx, jy) = (self.pm(0.25), self.pm(0.25));
            let mut p = Particle::new(x + jx, y + jy, 0.2 + self.random() * 0.4, color, size * (0.7 + 0.5 * self.random()), l.max(1), Motion::Plain)
                .moving(a.cos() * s / 1000.0, a.sin() * s / 1000.0, rise * (0.6 + 0.8 * self.random()) / 1000.0);
            p.fade = true;
            p.opacity = 0.5;
            p.grow = size * 0.8 / l.max(1) as f64;
            p.delay = delay + (i as i32 * 15) % 120;
            self.add(p);
        }
    }

    /// Flames licking up from a disc, each shrinking as it rises.
    #[allow(clippy::too_many_arguments)]
    fn flames(&mut self, x: f64, y: f64, radius: f64, n: usize, colors: &[u32], size: f64, rise: f64, life: i32, spread_ms: i32) {
        for _ in 0..n {
            let (a, d) = (2.0 * PI * self.random(), radius * self.random().sqrt());
            let color = self.pick(colors);
            let l = life / 2 + (self.random() * life as f64 / 2.0) as i32;
            let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.1, color, size * (0.7 + 0.6 * self.random()), l.max(1), Motion::Spark)
                .moving(self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0, rise * (0.6 + 0.8 * self.random()) / 1000.0);
            p.delay = (self.random() * spread_ms as f64) as i32;
            self.add(p);
        }
    }

    /// Wisps flying from (x, y) to object `id` (or the spot `to`), curving, trailing sparks.
    #[allow(clippy::too_many_arguments)]
    fn seekers(&mut self, x: f64, y: f64, id: i32, to: (f64, f64), n: usize, colors: &[u32], size: f64, gap: i32) {
        for i in 0..n {
            let a = 2.0 * PI * self.random();
            let color = self.pick(colors);
            let s = 0.004 + 0.003 * self.random();
            let mut p = Particle::new(x + self.pm(0.3), y + self.pm(0.3), 0.5, color, size, 2500, Motion::Seek { id, tx: to.0, ty: to.1, speed: 0.008 })
                .moving(a.cos() * s, a.sin() * s, 0.004 + 0.003 * self.random());
            p.delay = i as i32 * gap;
            self.add(p);
        }
    }

    /// Lightning out of the sky onto (x, y): a jagged stroke from seven tiles up, a branch or
    /// two, and a flash where it lands.
    fn sky_bolt(&mut self, x: f64, y: f64, color: u32, k: f64) {
        const STEPS: usize = 28;
        let (mut px, mut py) = (x + self.pm(0.8), y + self.pm(0.8));
        for i in 0..=STEPS {
            let t = i as f64 / STEPS as f64;
            let z = 7.0 * (1.0 - t);
            px += self.pm(0.22);
            py += self.pm(0.22);
            px += (x - px) * t * 0.4;
            py += (y - py) * t * 0.4;
            let c = if i % 3 == 0 { 0xFFFFFF } else { color };
            let life = 220 + (t * 220.0) as i32;
            self.add(Particle::new(px, py, z, c, 170.0 * k, life, Motion::Spark));
            // a fork, reaching down and away
            if i > 5 && i < STEPS - 6 && self.random() < 0.14 {
                let (dx, dy) = (self.pm(0.35), self.pm(0.35));
                let (mut bx, mut by, mut bz) = (px, py, z);
                for _ in 0..6 {
                    bx += dx + self.pm(0.08);
                    by += dy + self.pm(0.08);
                    bz -= 0.22;
                    self.add(Particle::new(bx, by, bz.max(0.0), color, 110.0 * k, 260, Motion::Spark));
                }
            }
        }
        // the strike: a white heart, sparks skittering off
        self.add(Particle::new(x, y, 0.3, 0xFFFFFF, 420.0 * k, 160, Motion::Spark));
        self.debris(x, y, 10, &[0xFFFFFF, color], 90.0 * k, 4.0, 3.0, 400, 0);
    }

    /// The Chaos Beam slamming down onto (x, y): a column of violet light round a white-hot core,
    /// falling out of the sky in a few frames, chaos spiralling down round it, then the burst.
    fn chaos_beam(&mut self, x: f64, y: f64, r: f64, color: u32) {
        const ROWS: i32 = 48;
        const FALL: i32 = 90;
        let dark = mix(color, 0x000000, 0.55);
        let hot = mix(color, 0xFFFFFF, 0.55);
        for row in 0..ROWS {
            let z = 9.0 * (1.0 - row as f64 / ROWS as f64);
            let delay = row * FALL / ROWS;
            // the core, white, and round it the beam's violet and its dark edge, overlapping so
            // they read as one column
            for (ox, c, size, life) in [(0.0, 0xFFFFFF, 260.0, 260), (-0.12, hot, 280.0, 320), (0.12, hot, 280.0, 320), (-0.24, color, 260.0, 400), (0.24, color, 260.0, 400), (-0.34, dark, 190.0, 380), (0.34, dark, 190.0, 380)] {
                let mut p = Particle::new(x + ox + self.pm(0.03), y, z, c, size, life, Motion::Plain);
                p.delay = delay;
                p.fade = true;
                self.add(p);
            }
        }
        // chaos winding down round the beam as it lands
        for i in 0..22 {
            let c = self.pick(&[color, 0xFF60D0, 0xFFFFFF, dark]);
            let life = 380 + (self.random() * 160.0) as i32;
            let z0 = 4.0 + 4.0 * self.random();
            let speed = if i % 2 == 0 { 0.22 } else { -0.22 };
            let motion = Motion::Soul { cx: x, cy: y, angle: 2.0 * PI * self.random(), speed, rad: 0.55 + 0.35 * self.random(), out: 0.0006, rise: -z0 / life as f64 };
            let mut p = Particle::new(x, y, z0, c, 120.0, life, motion);
            p.alpha = 0.0;
            p.delay = (i * 4) % FALL;
            self.add(p);
        }
        // the strike: a white flash, violet light flung out along the floor, shards thrown up,
        // dark smoke rolling out
        for (size, c, life) in [(900.0, color, 220), (560.0, 0xFFFFFF, 160)] {
            let mut p = Particle::new(x, y, 0.3, c, size, life, Motion::Spark);
            p.delay = FALL;
            self.add(p);
        }
        for i in 0..28 {
            let a = i as f64 * 2.0 * PI / 28.0 + self.pm(0.08);
            let s = (r / 260.0) * (0.75 + 0.5 * self.random());
            let c = if i % 4 == 0 { 0xFFFFFF } else if i % 2 == 0 { hot } else { color };
            let mut p = Particle::new(x, y, 0.15, c, 140.0, 260, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
            p.delay = FALL;
            self.add(p);
        }
        self.debris(x, y, 22, &[color, 0xFF60D0, 0xFFFFFF, 0x301040], 110.0, 4.5, 6.0, 800, FALL);
        self.puffs(x, y, 12, &[0x1A0828, 0x2C1040, dark], 300.0, r * 0.6, 0.8, 1300, FALL + 40);
    }

    /// `ShowEffect` 41: the power `look` (pos2.y) at work.
    pub(super) fn relic(&mut self, e: &ShowEffect, world: &World, now: i32) {
        let look = e.pos2.y.round() as i32;
        if !(1..=look::LAST as i32).contains(&look) {
            return;
        }
        let look = look as u8;
        let (x, y) = (e.pos1.x as f64, e.pos1.y as f64);
        let r = (e.pos2.x as f64).clamp(0.2, 8.0);
        let color = e.color & 0xFF_FFFF;
        let ms = e.duration.clamp(0, 10_000);
        let id = e.target_object_id;
        let on = world.objects.get(&id).map(|o| (o.x, o.y));
        let z = world.my_z();
        self.here = (z, world.levels.as_ref().map_or(0, |l| ((z + 0.01) / l.height).floor().max(0.0) as u8));
        match look {
            // Broadside: a cannonball drops onto the target and bursts: a shockwave, fire thrown
            // out, black smoke rolling up and seawater sprayed high
            look::CANNON => {
                let mut ball = Particle::new(x - 0.8, y - 0.8, 4.5, 0x181818, 260.0, 140, Motion::Plain).moving(0.8 / 140.0, 0.8 / 140.0, -4.5 / 140.0);
                ball.fade = false;
                self.add(ball);
                self.decal(DecalKind::Ring, x, y, r, 0xFFC060, now + 120, 420, None);
                self.decal(DecalKind::Scorch, x, y, r * 0.6, 0xFF8030, now + 120, 900, None);
                self.add({
                    let mut p = Particle::new(x, y, 0.4, 0xFFF0A0, 700.0, 140, Motion::Spark);
                    p.delay = 120;
                    p
                });
                self.debris(x, y, 26, &[0xFFE060, 0xFFA040, 0xFF6020, 0xD03010], 120.0, 5.5, 5.0, 700, 120);
                self.puffs(x, y, 10, &[0x2A2A2A, 0x404040, 0x585858], 260.0, 1.2, 1.2, 1300, 150);
                self.debris(x, y, 14, &[0x60D0E0, 0xB0F0FF, 0x3090B0], 80.0, 2.0, 9.0, 900, 130);
            }
            // Widowfang: the venom bursts: a green splash on the floor, droplets flung out, the
            // poison bubbling up in green and violet
            look::VENOM => {
                self.decal(DecalKind::Puddle, x, y, r, color, now, 1600, None);
                self.decal(DecalKind::Ring, x, y, r * 1.1, 0xB050FF, now, 350, None);
                self.debris(x, y, 22, &[color, 0xB050FF, 0x40A020, 0xD0FF80], 110.0, 3.5, 4.5, 700, 0);
                for i in 0..12 {
                    let (a, d) = (2.0 * PI * self.random(), r * 0.7 * self.random());
                    let c = if i % 3 == 0 { 0xB050FF } else { color };
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.05, c, 90.0 + 60.0 * self.random(), 700, Motion::Plain).moving(0.0, 0.0, 0.0012);
                    p.delay = 150 + i * 90;
                    p.fade = true;
                    self.add(p);
                }
                if world.objects.contains_key(&id) {
                    self.flashes.insert(id, super::Flash { start: now, color, period: 250, repeats: 2 });
                }
            }
            // the Quiver: a gust of feathers flung out, then the serpent's wind winding up round
            // its user (an aura) while the dexterity lasts
            look::SERPENT_WIND => {
                let (cx, cy) = on.unwrap_or((x, y));
                for i in 0..18 {
                    let a = i as f64 * 2.0 * PI / 18.0;
                    let c = [color, 0x80FFD0, 0xFFD060][i % 3];
                    let mut p = Particle::new(cx, cy, 0.4, c, 130.0, 500, Motion::Spark).moving(a.cos() * 0.006, a.sin() * 0.006, 0.0015);
                    p.fade = true;
                    self.add(p);
                }
                self.aura(look, id, cx, cy, r, color, now, ms.max(500), true);
            }
            // Gorgon's Gaze: the stare turns flesh to stone: the enemy greys and chips of stone
            // break off it
            look::STONE_GAZE => {
                self.debris(x, y, 12, &[0xA8B0A0, 0x80887C, 0xD0D4C8, 0x606858], 130.0, 2.4, 4.0, 900, 0);
                self.puffs(x, y, 4, &[0xB8C0B0, 0x90988A], 200.0, 0.6, 0.5, 700, 0);
                self.decal(DecalKind::Ring, x, y, r, 0xC0D0B0, now, 420, None);
                if world.objects.contains_key(&id) {
                    self.flashes.insert(id, super::Flash { start: now, color: 0x9098A0, period: 300, repeats: 1 });
                }
            }
            // the Phylactery: a kill's soul torn loose, flying to its keeper
            look::SOUL_GATHER => {
                self.puffs(x, y, 6, &[0x7DFFCF, 0xC0FFF0], 160.0, 0.6, 1.0, 700, 0);
                self.decal(DecalKind::Ring, x, y, 0.8, color, now, 350, None);
                self.seekers(x, y, id, on.unwrap_or((x, y)), 3, &[color, 0xE0FFF8], 200.0, 70);
            }
            // the Phylactery's souls let go: they fly from its keeper to the target, then burst in a
            // ring of ghostly light and spiral up, howling
            look::SOUL_RELEASE => {
                let n = ms.clamp(1, 5) as usize;
                let from = on.unwrap_or((x, y));
                self.seekers(from.0, from.1, -1, (x, y), n * 3, &[color, 0xE0FFF8, 0x40C0A0], 180.0, 40);
                let hit = 380;
                self.decal(DecalKind::Ring, x, y, r, color, now + hit, 550, None);
                self.decal(DecalKind::Ring, x, y, r * 0.6, 0xE0FFF8, now + hit + 120, 450, None);
                for i in 0..(10 * n).min(45) {
                    let angle = 2.0 * PI * self.random();
                    let rad = 0.3 + self.random() * r * 0.8;
                    let c = if i % 4 == 0 { 0xFFFFFF } else { color };
                    let motion = Motion::Soul { cx: x, cy: y, angle, speed: 0.06 + 0.05 * self.random(), rad, out: 0.0008, rise: 0.0025 };
                    let mut p = Particle::new(x, y, 0.2, c, 150.0 + 80.0 * self.random(), 900 + (self.random() * 500.0) as i32, motion);
                    p.alpha = 0.0;
                    p.delay = hit + (self.random() * 150.0) as i32;
                    self.add(p);
                }
            }
            // the Prism: out of the world: a flash of cyan, the player shimmering (an aura) and
            // glittering
            look::PHASE => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.decal(DecalKind::Ring, cx, cy, 1.4, color, now, 300, None);
                for i in 0..16 {
                    let a = i as f64 * 2.0 * PI / 16.0;
                    self.add(Particle::new(cx, cy, 0.5, if i % 2 == 0 { color } else { 0xFFFFFF }, 120.0, 350, Motion::Spark).moving(a.cos() * 0.007, a.sin() * 0.007, 0.0));
                }
                if on.is_some() {
                    self.flashes.insert(id, super::Flash { start: now, color, period: 160, repeats: (ms / 160).max(3) });
                }
                self.aura(look, id, cx, cy, r, color, now, ms.max(400), true);
            }
            // the Soulfire Staff's brand: a burning circle of runes under the enemy, embers rising
            look::HELLBRAND => {
                self.decal(DecalKind::Sigil, x, y, r, color, now, ms.max(600), Some(id));
                self.flames(x, y, 0.4, 10, &[0xFF3020, 0xFF8030, 0xFFD060], 160.0, 2.5, 500, 100);
                self.aura(look, id, x, y, r, color, now, ms.max(600), true);
            }
            // the Sanguine Wand: blood drawn from the wound to its wielder
            look::BLOOD => {
                self.debris(x, y, 5, &[0xA00818, color], 80.0, 1.5, 2.5, 400, 0);
                self.seekers(x, y, id, on.unwrap_or((x, y)), 3, &[color, 0xFF3050, 0x900010], 120.0, 50);
            }
            // the Masquerade Robe: gone in a puff of rosy smoke and a shower of confetti
            look::MASQUERADE => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.puffs(cx, cy, 16, &[0xE0A0C8, 0xC080B0, 0xF0D0E8, 0x806078], 280.0, 1.6, 0.8, 1100, 0);
                for _ in 0..34 {
                    let a = 2.0 * PI * self.random();
                    let s = 0.8 + 2.2 * self.random();
                    let c = self.pick(&[color, 0xFFD24A, 0x60E0FF, 0xB070FF, 0xFFFFFF]);
                    let mut p = Particle::new(cx, cy, 0.8, c, 80.0, 1100 + (self.random() * 500.0) as i32, Motion::Ballistic { g: GRAVITY * 0.25 })
                        .moving(a.cos() * s / 1000.0, a.sin() * s / 1000.0, (3.0 + 2.0 * self.random()) / 1000.0);
                    p.fade = true;
                    self.add(p);
                }
            }
            // the Ring of the Drowned Captain: the sea claws its wearer back: a whirlpool opens
            // under them, water spouts round its edge and the sea's spray falls back
            look::DAVY => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.decal(DecalKind::Whirlpool, cx, cy, r * 0.8, color, now, ms.max(800), Some(id));
                self.decal(DecalKind::Ring, cx, cy, r, 0xB0FFF0, now, 450, None);
                for i in 0..10 {
                    let a = i as f64 * 2.0 * PI / 10.0;
                    let (sx, sy) = (cx + r * 0.75 * a.cos(), cy + r * 0.75 * a.sin());
                    for k in 0..6 {
                        let c = self.pick(&[color, 0xB0FFF0, 0x2080A0]);
                        let mut p = Particle::new(sx, sy, 0.1, c, 120.0, 900, Motion::Ballistic { g: GRAVITY })
                            .moving(self.pm(0.5) / 1000.0, self.pm(0.5) / 1000.0, (5.0 + k as f64 * 0.9) / 1000.0);
                        p.delay = i * 40 + k * 25;
                        p.fade = true;
                        self.add(p);
                    }
                }
                self.aura(look, id, cx, cy, r, color, now, ms.max(800), true);
            }
            // the Veridian Wyrmhide: a cloud of venom bursts off its scales, glinting scales flying
            look::TOXIC_SCALES => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.decal(DecalKind::Ring, cx, cy, r, color, now, 380, None);
                self.puffs(cx, cy, 30, &[color, 0x208030, 0x60D060, 0x104818], 190.0, r * 0.9, 0.5, 1200, 0);
                for i in 0..12 {
                    let a = i as f64 * 2.0 * PI / 12.0 + self.random() * 0.3;
                    self.add(Particle::new(cx, cy, 0.5, if i % 2 == 0 { 0xB0FFB0 } else { 0x40F070 }, 90.0, 420, Motion::Spark).moving(a.cos() * 0.007, a.sin() * 0.007, 0.0));
                }
            }
            // the Azure Tempest Seal, the Scepter of the Ivory Wyvern: lightning out of the sky
            look::SKY_BOLT => {
                self.sky_bolt(x, y, color, r);
                self.decal(DecalKind::Ring, x, y, 0.9 * r, color, now, 260, None);
                self.decal(DecalKind::Scorch, x, y, 0.45 * r, 0x6080C0, now, 500, None);
                if world.objects.contains_key(&id) {
                    self.flashes.insert(id, super::Flash { start: now, color: 0xFFFFFF, period: 120, repeats: 2 });
                }
            }
            // the Wyrmfire Katana: the enemy catches fire and burns while it bleeds
            look::IGNITION => {
                self.flames(x, y, 0.5, 14, &[0xFFE060, 0xFF8030, 0xFF4010], 200.0, 3.0, 450, 80);
                self.aura(look, id, x, y, r, color, now, ms.max(500), true);
            }
            // the Obsidian Wyrmplate's shell forming: black glass shards close round its wearer
            look::SHELL_READY => {
                let (cx, cy) = on.unwrap_or((x, y));
                for i in 0..14 {
                    let a = i as f64 * 2.0 * PI / 14.0;
                    let (sx, sy) = (cx + r * a.cos(), cy + r * a.sin());
                    let c = if i % 3 == 0 { color } else { 0x281838 };
                    let mut p = Particle::new(sx, sy, 0.6 + 0.4 * self.random(), c, 150.0, 2000, Motion::Seek { id, tx: cx, ty: cy, speed: 0.003 })
                        .moving(-a.sin() * 0.002, a.cos() * 0.002, 0.0);
                    p.delay = i * 20;
                    self.add(p);
                }
                self.decal(DecalKind::Ring, cx, cy, 0.9, color, now + ms.max(300), 300, None);
            }
            // ...and shattering as it takes a blow
            look::SHELL_BREAK => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.decal(DecalKind::Ring, cx, cy, r, color, now, 320, None);
                self.debris(cx, cy, 20, &[0x281838, 0x403058, color, 0xE0D8FF], 140.0, 5.0, 4.0, 700, 0);
                if on.is_some() {
                    self.flashes.insert(id, super::Flash { start: now, color: 0xE0D8FF, period: 140, repeats: 2 });
                }
            }
            // the Orb of the Drowned Tide: a whirlpool drags at everything on it, the water swirling
            // in
            look::UNDERTOW => {
                self.decal(DecalKind::Whirlpool, x, y, r, color, now, ms.max(800), None);
                self.decal(DecalKind::Ring, x, y, r, 0xC0F0FF, now, 300, None);
                self.aura(look, id, x, y, r, color, now, ms.max(800), false);
            }
            // the Bulwark of Bes: a golden ward over each player it reaches; round its user, the
            // oath's ring rolling out
            look::OATH => {
                let (cx, cy) = on.unwrap_or((x, y));
                if r > 1.5 {
                    self.decal(DecalKind::Ring, cx, cy, r, color, now, 500, None);
                }
                self.decal(DecalKind::Ward, cx, cy, 0.85, color, now, ms.max(600), Some(id));
                for i in 0..12 {
                    let a = i as f64 * 2.0 * PI / 12.0;
                    let mut p = Particle::new(cx + 0.7 * a.cos(), cy + 0.7 * a.sin(), 0.0, if i % 2 == 0 { color } else { 0xFFF4C0 }, 140.0, 700, Motion::Plain).moving(0.0, 0.0, 0.003);
                    p.fade = true;
                    self.add(p);
                }
                self.aura(look, id, cx, cy, 0.8, color, now, ms.max(600), true);
            }
            // the Tome of the Night Sky: stars fall onto each player it reaches, and a ring of
            // them turns under their feet
            look::STARLIGHT => {
                let (cx, cy) = on.unwrap_or((x, y));
                if r > 1.5 {
                    self.decal(DecalKind::Ring, cx, cy, r, color, now, 500, None);
                }
                self.decal(DecalKind::Stars, cx, cy, 0.9, color, now, ms.max(600), Some(id));
                // seven shooting stars, each a bright head and a tail of fainter, smaller sparks
                // following it down
                for i in 0..7 {
                    let (dx, dy) = (self.pm(0.7), self.pm(0.7));
                    let fall = 300 + (self.random() * 80.0) as i32;
                    for k in 0..5 {
                        let c = if k == 0 { 0xFFFFFF } else { color };
                        let mut p = Particle::new(cx + dx - 1.0, cy + dy - 1.0, 6.0, c, 190.0 - k as f64 * 30.0, fall, Motion::Plain)
                            .moving(1.0 / fall as f64, 1.0 / fall as f64, -6.0 / fall as f64);
                        p.delay = i * 70 + k * 22;
                        p.alpha = 1.0 - k as f32 * 0.17;
                        self.add(p);
                    }
                    // a twinkle where it lands
                    let mut p = Particle::new(cx + dx, cy + dy, 0.2, 0xFFFFFF, 260.0, 250, Motion::Spark);
                    p.delay = i * 70 + fall;
                    self.add(p);
                }
                self.aura(look, id, cx, cy, 0.9, color, now, ms.max(600), true);
            }
            // the Crown of Geb: the ground splits round its wearer, rocks and dust thrown up
            look::EARTHSHAKER => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.decal(DecalKind::Cracks, cx, cy, r, color, now, ms.max(900), None);
                self.decal(DecalKind::Ring, cx, cy, r, 0xE0C090, now, 450, None);
                self.debris(cx, cy, 26, &[0x705030, 0x8A6A48, 0x4A3828, color], 170.0, r * 1.4, 6.0, 900, 0);
                for i in 0..16 {
                    let a = i as f64 * 2.0 * PI / 16.0;
                    self.puffs(cx + r * 0.8 * a.cos(), cy + r * 0.8 * a.sin(), 1, &[0xC8B090, 0xA89070], 240.0, 0.4, 0.4, 900, 40);
                }
                self.flashes.insert(id, super::Flash { start: now, color, period: 200, repeats: 1 });
            }
            // the Snare of the Bridge Sentinel: the blobomb splatters: goo flung everywhere and left
            // on the floor
            look::BLOBOMB => {
                self.decal(DecalKind::Puddle, x, y, r * 0.8, color, now, 1800, None);
                self.decal(DecalKind::Ring, x, y, r, 0xFFFFFF, now, 380, None);
                self.add(Particle::new(x, y, 0.4, 0xFFFFFF, 800.0, 150, Motion::Spark));
                self.debris(x, y, 30, &[color, 0xFFFFFF, 0xB8C0D8, 0x9098B8], 200.0, r * 1.6, 6.5, 900, 0);
            }
            // the Twilight Cataclysm, fire: a pillar of flame on the target, the ground scorched
            look::FIRE => {
                self.decal(DecalKind::Scorch, x, y, r, color, now, 1500, None);
                self.decal(DecalKind::Ring, x, y, r, 0xFFD060, now, 350, None);
                self.flames(x, y, r * 0.85, 80, &[0xFFF0A0, 0xFFC040, color, 0xE03010], 200.0, 4.5, 1000, 600);
                self.flames(x, y, 0.7, 30, &[0xFFFFFF, 0xFFE080, 0xFFC040], 240.0, 8.0, 800, 400);
                self.puffs(x, y, 8, &[0x302020, 0x483030], 260.0, 0.8, 2.0, 1200, 300);
            }
            // ...ice: spikes of ice burst out across the ground, frost spreading, snow falling
            look::ICE => {
                self.decal(DecalKind::Frost, x, y, r, color, now, 1800, None);
                self.decal(DecalKind::Ring, x, y, r, 0xE0F8FF, now, 350, None);
                for i in 0..14 {
                    let a = i as f64 * 2.0 * PI / 14.0 + self.pm(0.15);
                    let d = r * (0.6 + 0.4 * self.random());
                    self.sparker((x, y), (x + d * a.cos(), y + d * a.sin()), if i % 2 == 0 { 0xFFFFFF } else { color }, 160.0, 260);
                }
                for _ in 0..30 {
                    let (a, d) = (2.0 * PI * self.random(), r * self.random().sqrt());
                    let fall = 900 + (self.random() * 700.0) as i32;
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 2.5 + self.random(), self.pick(&[0xFFFFFF, 0xD0F0FF, color]), 90.0, fall, Motion::Plain)
                        .moving(self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0, -2.8 / fall as f64);
                    p.delay = (self.random() * 300.0) as i32;
                    p.fade = true;
                    self.add(p);
                }
            }
            // the Shroud of the Forgotten King: the Realm forgets its wearer: they fade into a
            // red-black mist (an aura) while it lasts
            look::FORGOTTEN => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.puffs(cx, cy, 12, &[0x400808, 0x200000, color], 260.0, 1.4, 0.6, 900, 0);
                if on.is_some() {
                    self.flashes.insert(id, super::Flash { start: now, color: 0x300000, period: 600, repeats: (ms / 600).max(1) });
                }
                self.aura(look, id, cx, cy, r, color, now, ms.max(600), true);
            }
            // the Seal of the Risen Colossus: a marble pillar strikes up out of the floor and
            // crumbles, chips and dust flying
            look::PILLAR => {
                self.decal(DecalKind::Ring, x, y, r, color, now, 320, None);
                for row in 0..12 {
                    for col in 0..3 {
                        let ox = (col as f64 - 1.0) * 0.22;
                        let c = if (row + col) % 4 == 0 { 0xB8B8C8 } else if col == 1 { 0xFFFFFF } else { color };
                        let mut p = Particle::new(x + ox, y, row as f64 * 0.2, c, 260.0, 380, Motion::Plain);
                        p.delay = row * 14;
                        p.fade = true;
                        self.add(p);
                    }
                }
                self.debris(x, y, 12, &[0xFFFFFF, 0xC8C8D8, 0x9898A8], 110.0, 2.5, 4.0, 700, 300);
                self.puffs(x, y, 5, &[0xE8E8F0, 0xC0C0D0], 220.0, 0.8, 0.3, 700, 250);
            }
            // the Skull of the Final Ritual: a pentagram drawn in fire on the ground, line by line,
            // embers rising off it until its heart erupts
            look::PENTAGRAM => {
                self.decal(DecalKind::Pentagram, x, y, r, color, now, ms.max(600) + 150, None);
                self.aura(look, id, x, y, r, color, now, ms.max(600), false);
            }
            // ...its heart erupting: a column of red and black fire, a shockwave, smoke
            look::ERUPTION => {
                self.decal(DecalKind::Ring, x, y, r, color, now, 450, None);
                self.decal(DecalKind::Scorch, x, y, r * 0.8, 0xA01010, now, 1200, None);
                self.flames(x, y, 0.8, 50, &[0xFF2020, 0xFF6030, 0x600000, 0xFFC0A0], 280.0, 9.0, 700, 200);
                self.debris(x, y, 18, &[0xFF2020, 0x400000, 0xFF8040], 130.0, 4.0, 6.0, 800, 0);
                self.puffs(x, y, 10, &[0x200000, 0x401010, 0x101010], 300.0, 1.0, 2.2, 1300, 120);
            }
            // the Bow of the Event Horizon: a black hole tears open, swallowing light
            look::SINGULARITY => {
                self.decal(DecalKind::BlackHole, x, y, r, color, now, ms.max(800), None);
                self.aura(look, id, x, y, r, color, now, ms.max(800), false);
            }
            // the Chaotic Scripture's Chaos Beam: the beam slams down, a shockwave races out to
            // the blast's edge, and a rift stays torn open a moment, chaos crackling off it
            look::CHAOS => {
                self.chaos_beam(x, y, r, color);
                self.decal(DecalKind::Ring, x, y, r, color, now + 90, 420, None);
                self.decal(DecalKind::Ring, x, y, r * 0.7, 0xFFFFFF, now + 110, 300, None);
                self.decal(DecalKind::Rift, x, y, r, color, now + 60, 1400, None);
                self.aura(look, id, x, y, r, color, now + 90, 1100, false);
                // the plain lightning and blast that come with it (for older clients): not drawn
                self.chaos_on = Some((id, now));
            }
            _ => {}
        }
    }

    /// An aura's next particle.
    fn aura_tick(&mut self, a: Aura, now: i32) {
        let (x, y) = (a.x, a.y);
        let left = (a.until - now) as f64 / (a.until - a.start).max(1) as f64;
        match a.look {
            // feathered wind spiralling up round its user
            look::SERPENT_WIND => {
                let c = self.pick(&[a.color, 0x80FFD0, 0xFFD060, 0x20A070]);
                let angle = 2.0 * PI * self.random();
                let motion = Motion::Swirl { id: a.id, angle, speed: 0.13 + 0.05 * self.random(), rad: 0.55 + 0.35 * self.random(), out: 0.0002, rise: 0.0024 };
                let mut p = Particle::new(x, y, 0.05, c, 110.0, 650, motion);
                p.alpha = 0.0;
                self.add(p);
            }
            // a shimmer of light up and down the player
            look::PHASE => {
                let angle = 2.0 * PI * self.random();
                let motion = Motion::Swirl { id: a.id, angle, speed: 0.25, rad: 0.45, out: 0.0, rise: if self.random() < 0.5 { 0.003 } else { -0.001 } };
                let mut p = Particle::new(x, y, self.random() * 1.2, if self.random() < 0.5 { a.color } else { 0xFFFFFF }, 90.0, 300, motion);
                p.alpha = 0.0;
                self.add(p);
            }
            // embers off the brand
            look::HELLBRAND => {
                let (ang, d) = (2.0 * PI * self.random(), a.radius * (0.6 + 0.4 * self.random()));
                let c = self.pick(&[a.color, 0xFF8030, 0xFFD060]);
                let mut p = Particle::new(x + d * ang.cos(), y + d * ang.sin(), 0.05, c, 100.0, 700, Motion::Spark).moving(0.0, 0.0, 0.0025);
                p.fade = true;
                self.add(p);
            }
            // the enemy burning
            look::IGNITION => {
                self.flames(x, y, 0.45, 2, &[0xFFE060, 0xFF8030, a.color], 180.0 * left.max(0.4), 3.0, 420, 0);
            }
            // spray off the whirlpool round its wearer, bubbles rising
            look::DAVY => {
                let (ang, d) = (2.0 * PI * self.random(), a.radius * 0.7 * self.random().sqrt());
                let c = self.pick(&[a.color, 0xB0FFF0, 0x2080A0]);
                let mut p = Particle::new(x + d * ang.cos(), y + d * ang.sin(), 0.05, c, 90.0, 600, Motion::Plain).moving(0.0, 0.0, 0.0018);
                p.fade = true;
                self.add(p);
            }
            // water drawn round and into the whirlpool
            look::UNDERTOW => {
                let angle = 2.0 * PI * self.random();
                let rad = a.radius * (0.8 + 0.2 * self.random());
                let c = self.pick(&[a.color, 0xC0F0FF, 0x2060A0, 0xFFFFFF]);
                let life = 700;
                let motion = Motion::Soul { cx: x, cy: y, angle, speed: 0.09, rad, out: -rad * 0.9 / life as f64, rise: -0.0002 };
                let mut p = Particle::new(x, y, 0.15, c, 110.0, life, motion);
                p.alpha = 0.0;
                self.add(p);
            }
            // golden motes rising off the ward
            look::OATH => {
                let angle = 2.0 * PI * self.random();
                let motion = Motion::Swirl { id: a.id, angle, speed: 0.05, rad: a.radius, out: 0.0, rise: 0.002 };
                let mut p = Particle::new(x, y, 0.0, if self.random() < 0.6 { a.color } else { 0xFFF4C0 }, 110.0, 800, motion);
                p.alpha = 0.0;
                self.add(p);
            }
            // a star twinkling about the player
            look::STARLIGHT => {
                let angle = 2.0 * PI * self.random();
                let motion = Motion::Swirl { id: a.id, angle, speed: 0.03, rad: 0.4 + 0.5 * self.random(), out: 0.0, rise: 0.0006 };
                let mut p = Particle::new(x, y, 0.3 + self.random() * 1.0, if self.random() < 0.5 { 0xFFFFFF } else { a.color }, 120.0, 500, motion);
                p.alpha = 0.0;
                self.add(p);
            }
            // the red-black mist round the forgotten
            look::FORGOTTEN => {
                let angle = 2.0 * PI * self.random();
                let c = self.pick(&[a.color, 0x400808, 0x200000, 0x701818]);
                let motion = Motion::Swirl { id: a.id, angle, speed: 0.04, rad: 0.3 + 0.4 * self.random(), out: 0.0004, rise: 0.0012 };
                let mut p = Particle::new(x, y, 0.1, c, 200.0, 800, motion);
                p.alpha = 0.0;
                p.grow = 0.15;
                self.add(p);
            }
            // embers rising off the pentagram's lines
            look::PENTAGRAM => {
                let k = (self.random() * 5.0) as usize % 5;
                let point = |i: usize| {
                    let ang = (-90.0 + i as f64 * 144.0) * PI / 180.0;
                    (x + a.radius * ang.cos(), y + a.radius * ang.sin())
                };
                let (p0, p1) = (point(k), point(k + 1));
                let t = self.random();
                let c = self.pick(&[a.color, 0xFF8040, 0x600000]);
                let mut p = Particle::new(p0.0 + (p1.0 - p0.0) * t, p0.1 + (p1.1 - p0.1) * t, 0.05, c, 110.0, 650, Motion::Spark).moving(0.0, 0.0, 0.002);
                p.fade = true;
                self.add(p);
            }
            // chaos crackling out of the rift: a jagged arc along the floor, a mote rising
            look::CHAOS => {
                let base = 2.0 * PI * self.random();
                let len = a.radius * (0.45 + 0.5 * self.random()) * left.max(0.3);
                let (mut px, mut py) = (x + 0.3 * base.cos(), y + 0.3 * base.sin());
                for k in 1..=7 {
                    let f = k as f64 / 7.0;
                    let ang = base + self.pm(0.5);
                    let (tx, ty) = (x + (0.3 + len * f) * ang.cos(), y + (0.3 + len * f) * ang.sin());
                    let c = if k % 3 == 0 { 0xFFFFFF } else { self.pick(&[a.color, 0xFF60D0]) };
                    for j in 0..2 {
                        let t = j as f64 / 2.0;
                        self.add(Particle::new(px + (tx - px) * t, py + (ty - py) * t, 0.05, c, 80.0, 140 + k * 12, Motion::Spark));
                    }
                    (px, py) = (tx, ty);
                }
                let (ang, d) = (2.0 * PI * self.random(), a.radius * 0.5 * self.random().sqrt());
                let c = self.pick(&[a.color, 0x301040, 0xFFFFFF]);
                let mut p = Particle::new(x + d * ang.cos(), y + d * ang.sin(), 0.05, c, 110.0, 700, Motion::Spark).moving(0.0, 0.0, 0.003);
                p.fade = true;
                self.add(p);
            }
            // light pulled round and down into the black hole
            look::SINGULARITY => {
                let angle = 2.0 * PI * self.random();
                let rad = a.radius * (0.9 + 0.4 * self.random());
                let c = self.pick(&[a.color, 0xE0C0FF, 0xFFFFFF, 0x4020A0]);
                let life = 600;
                let motion = Motion::Soul { cx: x, cy: y, angle, speed: 0.12, rad, out: -(rad - 0.25) / life as f64, rise: -0.0004 };
                let mut p = Particle::new(x, y, 0.4, c, 100.0, life, motion);
                p.alpha = 0.0;
                self.add(p);
            }
            _ => {}
        }
    }

    /// The auras and decals, moved on to `now`: what follows an object goes with it.
    pub(super) fn relic_update(&mut self, world: &World, now: i32) {
        self.decals.retain(|d| now - d.start < d.ms);
        for d in self.decals.iter_mut() {
            if let Some(o) = d.follow.and_then(|id| world.objects.get(&id)) {
                (d.x, d.y) = (o.x, o.y);
            }
        }
        let mut auras = std::mem::take(&mut self.auras);
        auras.retain(|a| now < a.until);
        for a in auras.iter_mut() {
            if a.follow {
                match world.objects.get(&a.id) {
                    Some(o) => (a.x, a.y) = (o.x, o.y),
                    None => {
                        a.until = 0;
                        continue;
                    }
                }
            }
            let mut n = 0;
            self.muting = a.muting;
            while a.next <= now && n < 6 {
                a.next += period(a.look);
                n += 1;
                self.aura_tick(*a, now);
            }
            self.muting = 0.0;
            a.next = a.next.max(now - period(a.look));
        }
        auras.retain(|a| a.until > 0);
        // (an aura started meanwhile, from aura_tick, can't happen: they only make particles)
        self.auras = auras;
    }

    /// `--effect 1xx` (tests): look xx as the server would send it, on us or beside us.
    pub(super) fn test_relic(&mut self, which: u8, world: &World, now: i32) {
        let Some(me) = world.player() else { return };
        let (mx, my) = (me.x as f32, me.y as f32);
        // radius, colour, ms; and whether it happens on us rather than two tiles off
        let (r, color, ms, mine): (f32, u32, i32, bool) = match which {
            look::CANNON => (2.0, 0xFFFFA040, 0, false),
            look::VENOM => (1.5, 0xFF70E040, 0, false),
            look::SERPENT_WIND => (1.0, 0xFF40E0A0, 4000, true),
            look::STONE_GAZE => (0.8, 0xFFA8B0A0, 0, false),
            look::SOUL_GATHER => (0.5, 0xFF7DFFCF, 0, false),
            look::SOUL_RELEASE => (4.0, 0xFF7DFFCF, 5, false),
            look::PHASE => (1.0, 0xFF80FFFF, 1000, true),
            look::HELLBRAND => (1.2, 0xFFFF3020, 2000, false),
            look::BLOOD => (0.5, 0xFFD01030, 0, false),
            look::MASQUERADE => (1.5, 0xFFFF60C0, 0, true),
            look::DAVY => (3.0, 0xFF40E0C0, 1500, true),
            look::TOXIC_SCALES => (3.0, 0xFF30C040, 0, true),
            look::SKY_BOLT => (1.0, 0xFF60A0FF, 0, false),
            look::IGNITION => (0.8, 0xFFFF6020, 2000, false),
            look::SHELL_READY => (1.5, 0xFF9080C0, 700, true),
            look::SHELL_BREAK => (1.5, 0xFF9080C0, 0, true),
            look::UNDERTOW => (3.5, 0xFF40C0FF, 1500, false),
            look::OATH => (5.0, 0xFFFFD050, 4000, true),
            look::STARLIGHT => (6.0, 0xFFB0C8FF, 4000, true),
            look::EARTHSHAKER => (4.0, 0xFFC09040, 1200, true),
            look::BLOBOMB => (3.0, 0xFFE8E8FF, 0, false),
            look::FIRE => (3.5, 0xFFFF7020, 0, false),
            look::ICE => (3.5, 0xFF60C8FF, 0, false),
            look::FORGOTTEN => (1.0, 0xFFE04040, 5000, true),
            look::PILLAR => (2.5, 0xFFE8E8FF, 0, false),
            look::PENTAGRAM => (2.5, 0xFFFF2020, 1900, false),
            look::ERUPTION => (3.0, 0xFFFF2020, 0, false),
            look::SINGULARITY => (3.0, 0xFF8040FF, 3200, false),
            look::CHAOS => (2.5, 0xFFB44CFF, 0, false),
            _ => return,
        };
        // (centred on us, where a shot can see it, but for what flies between us and somewhere else)
        let flies = matches!(which, look::SOUL_GATHER | look::SOUL_RELEASE | look::BLOOD);
        let pos1 = if mine || !flies { bsk_proto::WorldPos { x: mx, y: my } } else { bsk_proto::WorldPos { x: mx + 2.5, y: my } };
        let e = ShowEffect { effect_type: 41, target_object_id: me.id, pos1, pos2: bsk_proto::WorldPos { x: r, y: which as f32 }, color, duration: ms, object_type: 0, source_id: None };
        self.relic(&e, world, now);
        // the pentagram's heart erupts as it fills, as the server has it
        if which == look::PENTAGRAM {
            let e = ShowEffect { pos2: bsk_proto::WorldPos { x: 3.0, y: look::ERUPTION as f32 }, ..e };
            self.delayed.push((now + 1900, e));
        }
    }

    /// The decals on floor `level` (0 the map), after its ground.
    pub(super) fn draw_decals(&self, cam: &Camera, renderer: &mut Renderer, level: u8) {
        for d in self.decals.iter().filter(|d| d.level == level && self.now >= d.start) {
            let t = ((self.now - d.start) as f64 / d.ms as f64).clamp(0.0, 1.0);
            let mut pen = Pen { cam, renderer, x: d.x, y: d.y, z: d.z, dim: d.dim };
            draw_decal(&mut pen, d, t, self.now);
        }
    }
}

/// Draws on the ground round a spot.
struct Pen<'a> {
    cam: &'a Camera,
    renderer: &'a mut Renderer,
    x: f64,
    y: f64,
    z: f64,
    /// Every alpha times this (another player's decal, faded).
    dim: f32,
}

impl Pen<'_> {
    /// The point `r` tiles out at angle `a`.
    fn at(&self, r: f64, a: f64) -> [f32; 2] {
        self.cam.to_screen(self.x + r * a.cos(), self.y + r * a.sin(), self.z)
    }

    fn pt(&self, x: f64, y: f64) -> [f32; 2] {
        self.cam.to_screen(self.x + x, self.y + y, self.z)
    }

    fn tri(&mut self, pts: [[f32; 2]; 3], rgb: u32, alpha: f32) {
        let alpha = alpha * self.dim;
        if alpha <= 0.005 {
            return;
        }
        let white = self.renderer.white();
        self.renderer.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(rgb, alpha.min(1.0)));
    }

    fn quad(&mut self, p: [[f32; 2]; 4], rgb: u32, alpha: f32) {
        self.tri([p[0], p[1], p[2]], rgb, alpha);
        self.tri([p[0], p[2], p[3]], rgb, alpha);
    }

    /// A filled circle of radius `r` round (ox, oy) from the spot.
    fn disc_at(&mut self, ox: f64, oy: f64, r: f64, rgb: u32, alpha: f32) {
        let sides = if r < 0.6 { 14 } else { 36 };
        let c = self.pt(ox, oy);
        for i in 0..sides {
            let (a0, a1) = (2.0 * PI * i as f64 / sides as f64, 2.0 * PI * (i + 1) as f64 / sides as f64);
            let (p0, p1) = (self.pt(ox + r * a0.cos(), oy + r * a0.sin()), self.pt(ox + r * a1.cos(), oy + r * a1.sin()));
            self.tri([c, p0, p1], rgb, alpha);
        }
    }

    fn disc(&mut self, r: f64, rgb: u32, alpha: f32) {
        self.disc_at(0.0, 0.0, r, rgb, alpha);
    }

    /// A ring between `r0` and `r1`.
    fn band(&mut self, r0: f64, r1: f64, rgb: u32, alpha: f32) {
        const SIDES: usize = 40;
        let r0 = r0.max(0.0);
        for i in 0..SIDES {
            let (a0, a1) = (2.0 * PI * i as f64 / SIDES as f64, 2.0 * PI * (i + 1) as f64 / SIDES as f64);
            self.quad([self.at(r1, a0), self.at(r1, a1), self.at(r0, a1), self.at(r0, a0)], rgb, alpha);
        }
    }

    /// A line `w` tiles wide between two points (tiles from the spot).
    fn line(&mut self, a: (f64, f64), b: (f64, f64), w: f64, rgb: u32, alpha: f32) {
        let (dx, dy) = (b.0 - a.0, b.1 - a.1);
        let len = dx.hypot(dy).max(1e-6);
        let (nx, ny) = (-dy / len * w / 2.0, dx / len * w / 2.0);
        self.quad([self.pt(a.0 + nx, a.1 + ny), self.pt(b.0 + nx, b.1 + ny), self.pt(b.0 - nx, b.1 - ny), self.pt(a.0 - nx, a.1 - ny)], rgb, alpha);
    }

    /// Spiral arms turning by `spin`, curling in from `r` to the middle.
    fn arms(&mut self, r: f64, n: usize, spin: f64, colors: &[u32], alpha: f32) {
        for arm in 0..n {
            let colour = colors[arm % colors.len()];
            let base = spin + arm as f64 * 2.0 * PI / n as f64;
            let steps = 14;
            for k in 0..steps {
                let (t0, t1) = (k as f64 / steps as f64, (k + 1) as f64 / steps as f64);
                let (a0, a1) = (base + t0 * 2.4, base + t1 * 2.4);
                let (r0, r1) = (r * (1.0 - t0 * 0.9), r * (1.0 - t1 * 0.9));
                let w = 0.2 * r * (1.0 - t0 * 0.7);
                let q = [self.at(r0, a0), self.at(r1, a1), self.at((r1 - w).max(0.0), a1 + 0.12), self.at((r0 - w).max(0.0), a0 + 0.12)];
                self.quad(q, colour, alpha * (1.0 - t0 as f32 * 0.5));
            }
        }
    }
}

/// How visible a lasting decal is at `t`: rising over the first `fade_in`, falling over the last
/// `fade_out` of its time.
fn envelope(t: f64, fade_in: f64, fade_out: f64) -> f32 {
    let up = if fade_in > 0.0 { (t / fade_in).min(1.0) } else { 1.0 };
    let down = if fade_out > 0.0 { ((1.0 - t) / fade_out).min(1.0) } else { 1.0 };
    (up * down).clamp(0.0, 1.0) as f32
}

fn draw_decal(pen: &mut Pen, d: &Decal, t: f64, now: i32) {
    let secs = (now - d.start) as f64 / 1000.0;
    let r = d.radius;
    match d.kind {
        DecalKind::Ring => {
            let e = 1.0 - (1.0 - t).powi(3);
            let rr = r * (0.15 + 0.85 * e);
            let a = (1.0 - t) as f32;
            pen.disc(rr, d.color, 0.12 * a);
            pen.band(rr - 0.08 - 0.25 * (1.0 - t), rr, d.color, 0.85 * a);
            pen.band(rr - 0.05, rr, 0xFFFFFF, 0.5 * a);
        }
        DecalKind::Puddle => {
            let a = envelope(t, 0.05, 0.5);
            let spread = (t / 0.12).min(1.0);
            let dark = mix(d.color, 0x000000, 0.45);
            pen.disc(r * 0.55 * spread, dark, 0.45 * a);
            for i in 0..8 {
                let ang = hash(d.seed, i) * 2.0 * PI;
                let dist = r * (0.35 + 0.35 * hash(d.seed, i + 20)) * spread;
                let br = r * (0.12 + 0.18 * hash(d.seed, i + 40)) * spread;
                pen.disc_at(dist * ang.cos(), dist * ang.sin(), br, dark, 0.45 * a);
                pen.disc_at(dist * ang.cos(), dist * ang.sin(), br * 0.6, d.color, 0.45 * a);
            }
            pen.disc(r * 0.35 * spread, d.color, 0.5 * a);
            pen.disc_at(-0.1 * r, -0.1 * r, r * 0.12 * spread, mix(d.color, 0xFFFFFF, 0.6), 0.5 * a);
        }
        DecalKind::Sigil => {
            let a = envelope(t, 0.12, 0.3);
            let grow = 0.6 + 0.4 * (t / 0.12).min(1.0);
            let r = r * grow;
            let flicker = 0.8 + 0.2 * (secs * 17.0).sin() as f32;
            pen.disc(r, d.color, 0.16 * a);
            pen.band(r - 0.1, r, d.color, 0.9 * a * flicker);
            pen.band(r * 0.68 - 0.06, r * 0.68, 0xFF9040, 0.8 * a);
            // runes between the rings, turning
            let spin = secs * 1.4;
            for i in 0..8 {
                let ang = spin + i as f64 * PI / 4.0;
                let (c, s) = (ang.cos(), ang.sin());
                let (r0, r1) = (r * 0.74, r * 0.92);
                let m = (r0 + r1) / 2.0;
                pen.line((r0 * c, r0 * s), (r1 * c, r1 * s), 0.07, 0xFFD060, 0.9 * a);
                pen.line((m * c - 0.08 * s, m * s + 0.08 * c), (m * c + 0.08 * s, m * s - 0.08 * c), 0.06, 0xFFD060, 0.9 * a);
            }
            // a triangle inside, turning the other way
            for i in 0..3 {
                let (a0, a1) = (-spin * 0.8 + i as f64 * 2.0 * PI / 3.0, -spin * 0.8 + (i + 1) as f64 * 2.0 * PI / 3.0);
                let rr = r * 0.62;
                pen.line((rr * a0.cos(), rr * a0.sin()), (rr * a1.cos(), rr * a1.sin()), 0.07, d.color, 0.85 * a * flicker);
            }
        }
        DecalKind::Whirlpool => {
            let a = envelope(t, 0.15, 0.25);
            let rr = r * (0.3 + 0.7 * (t / 0.15).min(1.0));
            let spin = -secs * 5.0;
            pen.disc(rr, mix(d.color, 0x001830, 0.75), 0.55 * a);
            pen.disc(rr * 0.5, 0x000818, 0.6 * a);
            pen.arms(rr, 4, spin, &[d.color, 0xD0FFFF], 0.75 * a);
            pen.band(rr - 0.12, rr, mix(d.color, 0xFFFFFF, 0.5), 0.8 * a);
        }
        DecalKind::Cracks => {
            let a = envelope(t, 0.0, 0.4);
            let open = (t / 0.18).min(1.0);
            let glow = (1.0 - t / 0.5).max(0.0) as f32;
            for i in 0..10u64 {
                let base = i as f64 * 2.0 * PI / 10.0 + hash(d.seed, i) * 0.4;
                let len = r * (0.6 + 0.4 * hash(d.seed, i + 10)) * open;
                let mut prev = (0.0, 0.0);
                for k in 1..=5u64 {
                    let f = k as f64 / 5.0;
                    let ang = base + (hash(d.seed, i * 7 + k) - 0.5) * 0.7;
                    let next = (len * f * ang.cos(), len * f * ang.sin());
                    let w = 0.13 * (1.0 - f * 0.7);
                    pen.line(prev, next, w, 0x1A1008, 0.85 * a);
                    pen.line(prev, next, w * 0.45, mix(0xFF7020, d.color, 0.3), 0.95 * glow * a);
                    prev = next;
                }
            }
            pen.disc(0.45 * open, 0x1A1008, 0.6 * a);
        }
        DecalKind::Pentagram => {
            let a = envelope(t, 0.05, 0.08);
            let pulse = 0.85 + 0.15 * (secs * 9.0).sin() as f32;
            let circle = (t / 0.3).min(1.0);
            pen.disc(r, 0x300000, 0.28 * a);
            // the circle, drawn round
            let sides = 48;
            let drawn = (circle * sides as f64) as usize;
            for i in 0..drawn {
                let (a0, a1) = (-PI / 2.0 + 2.0 * PI * i as f64 / sides as f64, -PI / 2.0 + 2.0 * PI * (i + 1) as f64 / sides as f64);
                pen.quad([pen.at(r + 0.05, a0), pen.at(r + 0.05, a1), pen.at(r - 0.07, a1), pen.at(r - 0.07, a0)], d.color, 0.9 * a * pulse);
            }
            // the star, line after line
            let point = |i: usize| {
                let ang = (-90.0 + i as f64 * 144.0) * PI / 180.0;
                (r * ang.cos(), r * ang.sin())
            };
            let lines = ((t - 0.25) / 0.55 * 5.0).clamp(0.0, 5.0);
            for k in 0..5 {
                let part = (lines - k as f64).clamp(0.0, 1.0);
                if part <= 0.0 {
                    break;
                }
                let (p0, p1) = (point(k), point(k + 1));
                let end = (p0.0 + (p1.0 - p0.0) * part, p0.1 + (p1.1 - p0.1) * part);
                pen.line(p0, end, 0.2, d.color, 0.35 * a);
                pen.line(p0, end, 0.09, mix(d.color, 0xFFD0A0, 0.4), 0.95 * a * pulse);
                pen.disc_at(p0.0, p0.1, 0.16, 0xFFC0A0, 0.9 * a);
            }
            if t > 0.8 {
                let heat = ((t - 0.8) / 0.2) as f32;
                pen.disc(r * 0.5, d.color, 0.45 * heat * a);
            }
        }
        DecalKind::BlackHole => {
            let a = envelope(t, 0.1, 0.12);
            let rr = r * (0.3 + 0.7 * (t / 0.1).min(1.0));
            let spin = secs * 4.0;
            pen.disc(rr, mix(d.color, 0x000000, 0.7), 0.3 * a);
            // pulses falling in, six over its life
            let phase = (t * 6.0).fract();
            pen.band(rr * (1.0 - phase) * 0.95, rr * (1.0 - phase), mix(d.color, 0xFFFFFF, 0.4), 0.7 * a * (phase as f32));
            pen.arms(rr * 0.9, 3, spin, &[d.color, 0xE0C0FF, 0xFFD0FF], 0.7 * a);
            pen.band(rr * 0.42, rr * 0.52, 0xFFFFFF, 0.75 * a);
            pen.disc(rr * 0.42, 0x000000, 0.95 * a);
        }
        DecalKind::Frost => {
            let a = envelope(t, 0.0, 0.45);
            let open = (t / 0.12).min(1.0);
            pen.disc(r * open, mix(d.color, 0xFFFFFF, 0.5), 0.22 * a);
            for i in 0..14u64 {
                let ang = i as f64 * 2.0 * PI / 14.0 + hash(d.seed, i) * 0.3;
                let len = r * (0.55 + 0.45 * hash(d.seed, i + 30)) * open;
                let (c, s) = (ang.cos(), ang.sin());
                let w = 0.16;
                pen.tri([pen.pt(-s * w, c * w), pen.pt(len * c, len * s), pen.pt(s * w, -c * w)], 0xE8FAFF, 0.75 * a);
                // a side spike halfway
                let m = len * 0.5;
                let side = ang + if i % 2 == 0 { 0.6 } else { -0.6 };
                pen.line((m * c, m * s), (m * c + 0.35 * open * side.cos(), m * s + 0.35 * open * side.sin()), 0.06, d.color, 0.8 * a);
            }
            pen.band(r * open - 0.08, r * open, 0xFFFFFF, 0.6 * a);
        }
        DecalKind::Scorch => {
            let a = envelope(t, 0.0, 0.6);
            pen.disc(r, 0x1A0C06, 0.28 * a);
            pen.disc(r * 0.6, 0x0A0402, 0.22 * a);
            let embers = (1.0 - t / 0.6).max(0.0) as f32;
            pen.band(r - 0.12, r, d.color, 0.7 * embers);
        }
        DecalKind::Ward => {
            let a = envelope(t, 0.1, 0.2);
            let spin = secs * 1.2;
            let pulse = 0.75 + 0.25 * (secs * 5.0).sin() as f32;
            pen.disc(r, d.color, 0.14 * a);
            pen.band(r - 0.08, r, d.color, 0.9 * a * pulse);
            // a hexagon inside, turning
            for i in 0..6 {
                let (a0, a1) = (spin + i as f64 * PI / 3.0, spin + (i + 1) as f64 * PI / 3.0);
                let rr = r * 0.75;
                pen.line((rr * a0.cos(), rr * a0.sin()), (rr * a1.cos(), rr * a1.sin()), 0.06, 0xFFF4C0, 0.85 * a);
            }
        }
        DecalKind::Rift => {
            let a = envelope(t, 0.0, 0.45);
            let open = 1.0 - (1.0 - (t / 0.1).min(1.0)).powi(2);
            let flicker = 0.75 + 0.25 * ((secs * 23.0).sin() * (secs * 7.0).cos()) as f32;
            let glow = mix(d.color, 0xFFFFFF, 0.45);
            let rr = r * open;
            // darkened ground out to the blast's edge
            pen.disc(rr, mix(d.color, 0x000000, 0.8), 0.35 * a);
            pen.band(rr - 0.1, rr, d.color, 0.7 * a * flicker);
            // the tears, glowing and jagged, out from the void
            for i in 0..7u64 {
                let base = i as f64 * 2.0 * PI / 7.0 + hash(d.seed, i) * 0.6;
                let len = rr * (0.55 + 0.4 * hash(d.seed, i + 10));
                let mut prev = (0.0, 0.0);
                for k in 1..=5u64 {
                    let f = k as f64 / 5.0;
                    let ang = base + (hash(d.seed, i * 7 + k) - 0.5) * 0.8;
                    let next = (len * f * ang.cos(), len * f * ang.sin());
                    let w = 0.16 * (1.0 - f * 0.75);
                    pen.line(prev, next, w * 1.8, d.color, 0.35 * a * flicker);
                    pen.line(prev, next, w * 0.6, glow, 0.95 * a);
                    prev = next;
                }
            }
            // the eight arrows of chaos, turning
            let spin = secs * 1.6;
            for i in 0..8 {
                let ang = spin + i as f64 * PI / 4.0;
                let (c, s) = (ang.cos(), ang.sin());
                let (r0, r1) = (rr * 0.42, rr * 0.86);
                pen.line((r0 * c, r0 * s), (r1 * c, r1 * s), 0.07, glow, 0.85 * a * flicker);
                let head = 0.2;
                let tip = (r1 * c + head * c, r1 * s + head * s);
                pen.tri([pen.pt(tip.0, tip.1), pen.pt(r1 * c - head * s, r1 * s + head * c), pen.pt(r1 * c + head * s, r1 * s - head * c)], glow, 0.9 * a * flicker);
            }
            // the void at its heart
            pen.band(rr * 0.42 - 0.06, rr * 0.42, glow, 0.8 * a);
            pen.disc(rr * 0.36, 0x0A0012, 0.85 * a);
            pen.disc(rr * 0.18 * (1.0 + 0.15 * (secs * 11.0).sin()), d.color, 0.55 * a * flicker);
        }
        DecalKind::Stars => {
            let a = envelope(t, 0.1, 0.2);
            let spin = secs * 0.9;
            pen.band(r - 0.04, r, d.color, 0.5 * a);
            for i in 0..6u64 {
                let ang = spin + i as f64 * PI / 3.0;
                let (sx, sy) = (r * ang.cos(), r * ang.sin());
                let tw = (0.6 + 0.4 * (secs * 6.0 + i as f64 * 1.7).sin()) as f32;
                let k = 0.16 * tw as f64 + 0.06;
                pen.line((sx - k, sy), (sx + k, sy), 0.05, 0xFFFFFF, 0.95 * a * tw);
                pen.line((sx, sy - k), (sx, sy + k), 0.05, 0xFFFFFF, 0.95 * a * tw);
                pen.disc_at(sx, sy, 0.06, d.color, a);
            }
        }
    }
}
