//! The Druid's pearls (the server's `TidePearl.cs` and `Player.TidePearls.cs`): `ShowEffect` 41
//! looks 61 to 65 (`relic::look`): the pearl thrown, its pool opening where it lands (or already
//! down when it came into view), the pearl recalled and flying home, and back in its Druid's hand.
//! For as long as a pool lies, what's drawn there:
//!
//! - the pool: water in the pearl's colour, its foam rim, arms of the tide curling in, and a
//!   ripple running out with each of its pulses (the server's `TidePearl.PULSE_MS`, from when it
//!   landed);
//! - the pearl hovering over its middle, bobbing, glowing as each pulse goes out;
//! - the way it will come home: a faint lane from the pearl to its Druid, as wide as what it
//!   strikes (the server's `PearlRecallWidth`), with motes running along it, brightening and
//!   blinking in its last second (stand so your enemies are in it);
//! - for our own, its time left in an arc round the rim.
//!
//! Each tier is its colour (`TidePearl.COLORS`, sent as the effect's colour; `object_type` the
//! tier, 9 for Nerissa's). A pool goes when its time is up or with its recall.

use super::relic::{hash, look, mix, DecalKind, Pen, GRAVITY};
use super::{Motion, Particle, Particles};
use crate::view::Camera;
use bsk_proto::ShowEffect;
use bsk_render::Renderer;
use bsk_world::World;
use std::f64::consts::PI;

/// How often a pool pulses, ms (the server's `TidePearl.PULSE_MS`).
const PULSE_MS: i32 = 500;
/// How wide the recalled pearl strikes either side of its way, tiles (the server's
/// `PearlRecallWidth`).
const LANE: f64 = 1.1;
/// The last ms of a pool: its lane blinking, the pearl straining to come home.
const CALLING: i32 = 1000;
/// The most pools drawn at once.
const MAX_POOLS: usize = 48;
/// Nerissa's Tidepearl's tier, as the server sends it.
const NERISSA: u8 = 9;
const FOAM: u32 = 0xE8FFFF;

/// A pool lying, as it's drawn.
#[derive(Clone, Copy, Debug)]
pub(super) struct PoolLook {
    id: i32,
    owner: i32,
    tier: u8,
    color: u32,
    start: i32,
    until: i32,
    radius: f64,
    x: f64,
    y: f64,
    z: f64,
    level: u8,
    /// Where its Druid is, and whether they're in view.
    ox: f64,
    oy: f64,
    owner_seen: bool,
    /// Its anchor is in view (always, for the `--effect` test's, which has none).
    shown: bool,
    mine: bool,
    dim: f32,
    seed: u64,
    /// When it next gives off a bubble, and the last pulse it's shown.
    next: i32,
    pulse: i32,
}

/// The pearl's own colours: its shadow side, its body and its shine.
fn pearl_shades(tier: u8, color: u32) -> (u32, u32, u32) {
    match tier {
        // Total Chaos: the purest of life and the darkest of evil
        8 => (0x101014, 0xF4F4FA, 0xFFFFFF),
        // the Infinite Shadows: black, a violet sheen
        5 => (0x0A0812, 0x2A2438, 0xB8A8E8),
        _ => (mix(color, 0x0A1A2A, 0.55), color, mix(color, 0xFFFFFF, 0.7)),
    }
}

/// The pool's water: deep under the pearl's colour (the Shadows' and Chaos' darker still).
fn water(tier: u8, color: u32) -> (u32, u32) {
    match tier {
        5 => (0x0C0818, 0x5A4A8A),
        8 => (0x08080C, 0xD8D8E8),
        _ => (mix(color, 0x08243A, 0.62), mix(color, FOAM, 0.55)),
    }
}

/// A ring between `r0` and `r1` from angle `a0` to `a1`.
fn arc(pen: &mut Pen, r0: f64, r1: f64, a0: f64, a1: f64, rgb: u32, alpha: f32) {
    let n = (((a1 - a0).abs() / (2.0 * PI)) * 48.0).ceil().max(1.0) as usize;
    for i in 0..n {
        let (b0, b1) = (a0 + (a1 - a0) * i as f64 / n as f64, a0 + (a1 - a0) * (i + 1) as f64 / n as f64);
        let q = [pen.at(r1, b0), pen.at(r1, b1), pen.at(r0, b1), pen.at(r0, b0)];
        pen.quad(q, rgb, alpha);
    }
}

fn ease(t: f64) -> f64 {
    let t = t.clamp(0.0, 1.0);
    1.0 - (1.0 - t).powi(3)
}

impl Particles {
    /// Looks 61 to 65 (`relic` hands them here).
    pub(super) fn tide_look(&mut self, e: &ShowEffect, which: u8, world: &World, now: i32) {
        let (x, y) = (e.pos1.x as f64, e.pos1.y as f64);
        let color = e.color & 0xFF_FFFF;
        let tier = e.object_type.min(NERISSA as u16) as u8;
        let id = e.target_object_id;
        let ms = e.duration.clamp(0, 30_000);
        match which {
            look::PEARL_THROW => {
                let from = world.objects.get(&id).map_or((x - 1.5, y - 1.5), |o| (o.x, o.y));
                self.pearl_throw(from, (x, y), ms.clamp(100, 2000), tier, color);
            }
            look::PEARL_POOL | look::PEARL_SEEN => {
                let landed = which == look::PEARL_POOL;
                let owner = e.source_id.unwrap_or(0);
                if id >= 0 {
                    self.pools.retain(|p| p.id != id);
                    // the plain rings the server sends round it, for older clients, aren't drawn
                    self.fields.retain(|f| f.0 != id);
                    self.fields.push((id, now + ms + 500));
                }
                let radius = (e.pos2.x as f64).clamp(0.5, 8.0);
                if landed {
                    self.pearl_splash(x, y, radius, tier, color, now);
                }
                if self.pools.len() >= MAX_POOLS || self.disabled || self.muting >= 1.0 {
                    return;
                }
                let (z, level) = self.here;
                let seed = (self.random() * u32::MAX as f64) as u64;
                let (ox, oy, owner_seen) = world.objects.get(&owner).map_or((x, y, false), |o| (o.x, o.y, true));
                self.pools.push(PoolLook {
                    id,
                    owner,
                    tier,
                    color,
                    start: now,
                    until: now + ms,
                    radius,
                    x,
                    y,
                    z,
                    level,
                    ox,
                    oy,
                    owner_seen,
                    shown: id < 0 || world.objects.contains_key(&id),
                    mine: world.me == Some(owner),
                    dim: 1.0 - self.muting,
                    seed,
                    next: now + 100,
                    pulse: 0,
                });
            }
            look::PEARL_RECALL => {
                let (rx, ry) = self.pools.iter().find(|p| p.id == id).map_or((x, y), |p| (p.x, p.y));
                let radius = self.pools.iter().find(|p| p.id == id).map_or(2.5, |p| p.radius);
                self.pools.retain(|p| p.id != id);
                let owner = e.source_id.unwrap_or(-1);
                self.pearl_recall((rx, ry), radius, owner, world, ms.clamp(80, 2000), tier, color, now);
            }
            _ => {
                let at = world.objects.get(&id).map_or((x, y), |o| (o.x, o.y));
                self.pearl_home(at, id, e.object_type.min(20) as i32, tier, color, now);
            }
        }
    }

    /// The pearl flying from `from` to `to` in `ms`: a glowing orb on a low arc, spinning out a
    /// ribbon of water and motes of its colour, its shadow running along the floor.
    fn pearl_throw(&mut self, from: (f64, f64), to: (f64, f64), ms: i32, tier: u8, color: u32) {
        const PEAK: f64 = 1.3;
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        let (shadow, body, shine) = pearl_shades(tier, color);
        let steps = (ms / 16).max(1);
        for i in 0..steps {
            let t = i as f64 * ms as f64 / steps as f64;
            let f = t / ms as f64;
            let (px, py, pz) = (from.0 + dx * f, from.1 + dy * f, 0.5 + 4.0 * PEAK * f * (1.0 - f));
            for (c, size) in [(color, 260.0), (shadow, 170.0), (body, 140.0), (shine, 60.0)] {
                let mut p = Particle::new(px, py, pz, c, size, 34, Motion::Plain);
                p.delay = t as i32;
                if c == color {
                    p.alpha = 0.35;
                }
                self.add(p);
            }
            // the ribbon of water it trails, spiralling round its way
            let spin = f * 2.0 * PI * 2.0;
            let (sx, sy) = (0.18 * spin.cos(), 0.18 * spin.sin());
            let c = self.pick(&[color, FOAM, shine]);
            let mut p = Particle::new(px + sx, py + sy, pz, c, 90.0, 260, Motion::Ballistic { g: GRAVITY * 0.3 }).moving(self.pm(0.4) / 1000.0, self.pm(0.4) / 1000.0, 0.0);
            p.delay = t as i32;
            p.fade = true;
            self.add(p);
            if i % 2 == 1 {
                let mut p = Particle::new(px, py, 0.02, 0x101820, 150.0, 34, Motion::Plain);
                p.delay = t as i32;
                p.alpha = 0.35;
                self.add(p);
            }
        }
    }

    /// The pearl landing at (x, y), its pool `r` across opening: a white flash, a crown of water
    /// thrown up all round, rings racing out, foam, the floor washed.
    fn pearl_splash(&mut self, x: f64, y: f64, r: f64, tier: u8, color: u32, now: i32) {
        let (deep, foam) = water(tier, color);
        for (size, c, life) in [(900.0, color, 180), (520.0, 0xFFFFFF, 110)] {
            self.add(Particle::new(x, y, 0.35, c, size, life, Motion::Spark));
        }
        self.decal(DecalKind::Ring, x, y, r * 1.15, foam, now, 420, None);
        self.decal(DecalKind::Ring, x, y, r * 0.7, color, now + 60, 380, None);
        self.decal(DecalKind::Puddle, x, y, r * 0.75, color, now, 900, None);
        // the crown: water thrown up and out in a ring, falling back
        let n = 26;
        for i in 0..n {
            let a = 2.0 * PI * i as f64 / n as f64 + self.pm(0.1);
            let c = [foam, color, FOAM, deep][i % 4];
            let s = (r * 0.55 + self.random() * 0.6) / 450.0;
            let mut p = Particle::new(x + 0.3 * a.cos(), y + 0.3 * a.sin(), 0.15, c, 120.0 + 60.0 * self.random(), 520 + (self.random() * 160.0) as i32, Motion::Ballistic { g: GRAVITY * 0.55 })
                .moving(a.cos() * s, a.sin() * s, 0.0045 + 0.002 * self.random());
            p.fade = true;
            self.add(p);
        }
        // a column of spray at its heart
        self.debris(x, y, 14, &[FOAM, foam, color], 80.0, 1.0, 9.0, 700, 0);
        self.puffs(x, y, 6, &[FOAM, foam], 180.0, r * 0.35, 0.8, 800, 60);
        if tier == NERISSA {
            self.decal(DecalKind::Whirlpool, x, y, r, color, now, 1200, None);
        }
    }

    /// The pearl called home from its pool at `from` to its Druid `owner` over `ms`: the pool
    /// sucked down into a whirl, then the pearl streaking back (`Motion::Seek`, turning after its
    /// Druid as they move), the water of its pool pulled after it in a wake, a lane of spray along
    /// its way.
    #[allow(clippy::too_many_arguments)]
    fn pearl_recall(&mut self, from: (f64, f64), r: f64, owner: i32, world: &World, ms: i32, tier: u8, color: u32, now: i32) {
        let (deep, foam) = water(tier, color);
        let (shadow, body, shine) = pearl_shades(tier, color);
        let (x, y) = from;
        self.decal(DecalKind::Whirlpool, x, y, r * 0.8, deep, now, 450, None);
        self.decal(DecalKind::Ring, x, y, r, foam, now, 260, None);
        let to = world.objects.get(&owner).map_or((x + 3.0, y), |o| (o.x, o.y));
        let (dx, dy) = (to.0 - x, to.1 - y);
        let dist = dx.hypot(dy).max(0.5);
        let speed = dist / ms as f64;
        // the pearl
        for (c, size, alpha) in [(color, 300.0, 0.4), (shadow, 190.0, 1.0), (body, 150.0, 1.0), (shine, 70.0, 1.0)] {
            let mut p = Particle::new(x, y, 0.6, c, size, ms * 3 + 300, Motion::Seek { id: owner, tx: to.0, ty: to.1, speed });
            p.alpha = alpha;
            self.add(p);
        }
        // its pool's water, pulled after it
        for i in 0..14 {
            let (a, d) = (2.0 * PI * self.random(), r * 0.8 * self.random());
            let c = self.pick(&[color, foam, FOAM, deep]);
            let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.2 + 0.4 * self.random(), c, 100.0 + 70.0 * self.random(), ms * 3 + 300, Motion::Seek { id: owner, tx: to.0, ty: to.1, speed: speed * (0.75 + 0.2 * self.random()) });
            p.delay = 20 + i * 12;
            self.add(p);
        }
        // spray thrown up along its way as it passes, out to the lane's edges
        let (ux, uy) = (dx / dist, dy / dist);
        let n = (dist * 4.0).clamp(4.0, 48.0) as usize;
        for i in 0..n {
            let f = (i as f64 + 0.5) / n as f64;
            let side = if i % 2 == 0 { 1.0 } else { -1.0 };
            let s = (0.6 + 0.8 * self.random()) / 1000.0;
            let c = self.pick(&[foam, FOAM, color]);
            let mut p = Particle::new(x + dx * f, y + dy * f, 0.25, c, 90.0 + 40.0 * self.random(), 380, Motion::Ballistic { g: GRAVITY * 0.5 })
                .moving(-uy * side * s * LANE * 1.5, ux * side * s * LANE * 1.5, 0.003 + 0.002 * self.random());
            p.delay = (f * ms as f64) as i32;
            p.fade = true;
            self.add(p);
        }
    }

    /// The pearl back in its Druid's hand at `at`, after striking `struck` enemies: water
    /// spiralling up round them, healing light, and the more it struck, the bigger the wave
    /// breaking round them.
    fn pearl_home(&mut self, at: (f64, f64), id: i32, struck: i32, tier: u8, color: u32, now: i32) {
        let (_, foam) = water(tier, color);
        let (x, y) = at;
        let surge = struck.min(5) as f64 / 5.0;
        self.add(Particle::new(x, y, 0.6, 0xFFFFFF, 420.0 + 300.0 * surge, 120, Motion::Spark));
        self.add(Particle::new(x, y, 0.6, color, 650.0 + 400.0 * surge, 180, Motion::Spark));
        self.decal(DecalKind::Ring, x, y, 1.2 + 1.6 * surge, foam, now, 380, None);
        if struck >= 3 {
            self.decal(DecalKind::Whirlpool, x, y, 1.4 + surge, color, now, 700, Some(id));
            self.decal(DecalKind::Ring, x, y, 2.6 + surge, color, now + 80, 420, None);
        }
        // the water winding up round them
        let n = 10 + 4 * struck.min(5) as usize;
        for i in 0..n {
            let a = 2.0 * PI * i as f64 / n as f64;
            let c = [color, foam, FOAM, 0x9CFFB0][i % 4];
            let mut p = Particle::new(x, y, 0.1, c, 110.0, 560, Motion::Swirl { id, angle: a, speed: 0.14, rad: 0.55 + 0.3 * surge, out: 0.0004, rise: 0.0022 });
            p.fade = true;
            p.delay = (i as i32 * 9) % 80;
            self.add(p);
        }
        // healing motes
        for _ in 0..(4 + 2 * struck.min(5)) {
            let mut p = Particle::new(x + self.pm(0.5), y + self.pm(0.5), 0.3, self.pick(&[0x9CFFB0, 0xFFFFFF, foam]), 70.0, 700, Motion::Rising { acc: -2.0 })
                .moving(0.0, 0.0, 0.0015 + 0.001 * self.random());
            p.fade = true;
            self.add(p);
        }
    }

    /// The pools moved on to `now`: where they and their Druids are, their pulses and bubbles.
    pub(super) fn tide_update(&mut self, world: &World, now: i32) {
        self.pools.retain(|p| now < p.until + 250);
        // (x, y, radius, tier, colour, dim, kind: 0 a bubble, 1 a pulse)
        let mut fx: Vec<(f64, f64, f64, u8, u32, f32, u8)> = Vec::new();
        for p in self.pools.iter_mut() {
            if p.id >= 0 {
                match world.objects.get(&p.id) {
                    Some(o) => {
                        (p.x, p.y) = (o.x, o.y);
                        p.shown = true;
                    }
                    None => p.shown = false,
                }
            }
            match world.objects.get(&p.owner) {
                Some(o) if !o.dead => {
                    (p.ox, p.oy) = (o.x, o.y);
                    p.owner_seen = true;
                }
                _ => p.owner_seen = false,
            }
            if !p.shown {
                continue;
            }
            let pulse = (now - p.start) / PULSE_MS;
            if pulse > p.pulse && now < p.until {
                p.pulse = pulse;
                fx.push((p.x, p.y, p.radius, p.tier, p.color, p.dim, 1));
            }
            if now >= p.next {
                p.next = now + 70;
                fx.push((p.x, p.y, p.radius, p.tier, p.color, p.dim, 0));
            }
        }
        let muting = self.muting;
        for (x, y, r, tier, color, dim, kind) in fx {
            self.muting = 1.0 - dim;
            let (deep, foam) = water(tier, color);
            match kind {
                // a pulse: spray leaping in from the rim, and a glint off the pearl
                1 => {
                    for i in 0..8 {
                        let a = 2.0 * PI * i as f64 / 8.0 + self.pm(0.2);
                        let c = self.pick(&[foam, FOAM, color]);
                        let mut p = Particle::new(x + r * 0.9 * a.cos(), y + r * 0.9 * a.sin(), 0.1, c, 90.0, 380, Motion::Ballistic { g: GRAVITY * 0.5 })
                            .moving(-a.cos() * r / 900.0, -a.sin() * r / 900.0, 0.003);
                        p.fade = true;
                        self.add(p);
                    }
                    self.add(Particle::new(x, y, 0.55, 0xFFFFFF, 220.0, 90, Motion::Spark));
                }
                // a bubble rising somewhere in it, now and then a mote circling the pearl
                _ => {
                    let (a, d) = (2.0 * PI * self.random(), r * 0.9 * self.random().sqrt());
                    let c = self.pick(&[foam, FOAM, color, deep]);
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.02, c, 60.0 + 50.0 * self.random(), 600, Motion::Plain).moving(0.0, 0.0, 0.0007);
                    p.fade = true;
                    p.opacity = 0.7;
                    self.add(p);
                    if self.random() < 0.3 {
                        let motion = Motion::Orbit { cx: x, cy: y, angle: 2.0 * PI * self.random(), speed: 0.09, rad: 0.35, rise: 0.0, max_rad: 0.45 };
                        let mut m = Particle::new(x, y, 0.5, self.pick(&[color, FOAM]), 60.0, 500, motion);
                        m.fade = true;
                        self.add(m);
                    }
                }
            }
        }
        self.muting = muting;
    }

    /// The pools on floor `level` (0 the map), with the ground marks.
    pub(super) fn draw_pools(&self, cam: &Camera, renderer: &mut Renderer, level: u8) {
        let now = self.now;
        for p in self.pools.iter().filter(|p| p.shown && p.level == level && now >= p.start) {
            let mut pen = Pen { cam, renderer: &mut *renderer, x: p.x, y: p.y, z: p.z, dim: p.dim };
            draw_pool(&mut pen, p, now);
            let bob = 0.45 + 0.07 * ((now - p.start) as f64 / 1000.0 * 3.2 + hash(p.seed, 3) * 6.0).sin();
            let mut over = Pen { cam, renderer: &mut *renderer, x: p.x, y: p.y, z: p.z + bob, dim: p.dim };
            draw_pearl(&mut over, p, now);
        }
    }
}

fn draw_pool(pen: &mut Pen, p: &PoolLook, now: i32) {
    let secs = (now - p.start) as f64 / 1000.0;
    let left = (p.until - now).max(0);
    let total = (p.until - p.start).max(1);
    let open = ease((now - p.start) as f64 / 300.0);
    let fading = ((left as f64) / 250.0).clamp(0.0, 1.0) as f32;
    let calling = left < CALLING;
    let blink = calling && (now / 110) % 2 == 0;
    let (deep, foam) = water(p.tier, p.color);
    let r = p.radius * open;

    // the way home to its Druid: a faint lane as wide as it strikes, motes running along it
    if p.owner_seen {
        let (ox, oy) = (p.ox - p.x, p.oy - p.y);
        let d = ox.hypot(oy);
        if d > 0.6 {
            let (ux, uy) = (ox / d, oy / d);
            let start = (ux * 0.3, uy * 0.3);
            let end = (ox - ux * 0.3, oy - uy * 0.3);
            let mine = if p.mine { 1.0 } else { 0.5 };
            let k = mine * fading * open as f32;
            let lane = if calling { if blink { 0.2 } else { 0.12 } } else { 0.07 };
            pen.line(start, end, LANE * 2.0, p.color, lane * k);
            for side in [-1.0, 1.0] {
                let (sx, sy) = (-uy * LANE * side, ux * LANE * side);
                pen.line((start.0 + sx, start.1 + sy), (end.0 + sx, end.1 + sy), 0.04, foam, (if calling { 0.5 } else { 0.22 }) * k);
            }
            // motes running home along it, faster as its time runs out
            let rate = if calling { 2.2 } else { 0.8 };
            for j in 0..4 {
                let f = (secs * rate + j as f64 / 4.0).fract();
                let (mx, my) = (start.0 + (end.0 - start.0) * f, start.1 + (end.1 - start.1) * f);
                pen.disc_at(mx, my, 0.1, p.color, 0.4 * k);
                pen.disc_at(mx, my, 0.045, 0xFFFFFF, 0.8 * k);
            }
        }
    }

    // the water: deep in the middle, lighter to its foaming rim
    pen.disc(r, deep, 0.42 * fading);
    pen.disc(r * 0.72, mix(deep, p.color, 0.25), 0.25 * fading);
    // the tide's arms curling in (Nerissa's turning the faster)
    let spin = hash(p.seed, 5) * 2.0 * PI + secs * if p.tier == NERISSA { 2.4 } else { 1.5 };
    pen.arms(r * 0.95, 4, spin, &[foam, p.color], 0.3 * fading);
    // the rim of foam, bubbling round
    pen.band(r * 0.9, r, foam, 0.45 * fading);
    for i in 0..18 {
        let a = -spin * 0.6 + i as f64 * 2.0 * PI / 18.0;
        let w = 0.05 + 0.04 * ((secs * 4.0 + i as f64 * 1.7).sin() * 0.5 + 0.5);
        pen.disc_at(r * 0.95 * a.cos(), r * 0.95 * a.sin(), w, FOAM, 0.55 * fading);
    }
    // a ripple running out with each pulse
    let phase = ((now - p.start) % PULSE_MS) as f64 / PULSE_MS as f64;
    let rr = r * (0.15 + 0.85 * ease(phase));
    pen.band(rr - 0.07, rr + 0.03, FOAM, (0.55 * (1.0 - phase)) as f32 * fading);
    if phase < 0.18 {
        pen.disc(r, p.color, (0.12 * (1.0 - phase / 0.18)) as f32 * fading);
    }
    // our own: its time left round the rim, red-white as it's called home
    if p.mine {
        let top = -PI / 2.0;
        let frac = left as f64 / total as f64;
        arc(pen, r + 0.05, r + 0.13, top, top + 2.0 * PI * frac, if blink { 0xFFFFFF } else { p.color }, 0.85 * fading);
    }
    // the pearl's shadow on the water
    pen.disc(0.2, 0x000000, 0.3 * fading);
}

/// The pearl hovering over its pool (`pen` at its height): a glow, its body, the shadow side and
/// its shine; flaring as each pulse goes out, straining toward its Druid as it's called.
fn draw_pearl(pen: &mut Pen, p: &PoolLook, now: i32) {
    let left = (p.until - now).max(0);
    let fading = ((left as f64) / 250.0).clamp(0.0, 1.0) as f32;
    let k = ease((now - p.start) as f64 / 250.0);
    let (shadow, body, shine) = pearl_shades(p.tier, p.color);
    let phase = ((now - p.start) % PULSE_MS) as f64 / PULSE_MS as f64;
    let flare = (1.0 - phase / 0.25).max(0.0);
    // called: trembling toward its Druid
    let (jx, jy) = if left < CALLING && p.owner_seen {
        let (dx, dy) = (p.ox - p.x, p.oy - p.y);
        let d = dx.hypot(dy).max(0.01);
        let shake = 0.05 * ((now as f64) / 25.0).sin() * (1.0 - left as f64 / CALLING as f64);
        (dx / d * shake, dy / d * shake)
    } else {
        (0.0, 0.0)
    };
    let s = 0.2 * k;
    pen.disc_at(jx, jy, s * (1.8 + 0.6 * flare), p.color, (0.22 + 0.3 * flare) as f32 * fading);
    pen.disc_at(jx, jy, s, shadow, 0.95 * fading);
    pen.disc_at(jx - s * 0.12, jy - s * 0.15, s * 0.82, body, 0.95 * fading);
    pen.disc_at(jx - s * 0.35, jy - s * 0.38, s * 0.3, shine, 0.9 * fading);
    if flare > 0.0 {
        pen.disc_at(jx, jy, s * 0.6, 0xFFFFFF, (0.5 * flare) as f32 * fading);
    }
}

impl Particles {
    /// `--effect 261` to `265` (tests): the pearl looks, round a spot three tiles off us, as a
    /// Pearl of Total Chaos's (from 261: the whole of it, thrown, lying, recalled and home).
    pub(super) fn test_tide(&mut self, which: u8, world: &World, now: i32) {
        let Some(me) = world.player() else { return };
        let pos1 = bsk_proto::WorldPos { x: me.x as f32 + 3.0, y: me.y as f32 };
        let e = ShowEffect {
            effect_type: 41,
            target_object_id: if matches!(which, look::PEARL_THROW | look::PEARL_HOME) { me.id } else { -1 },
            pos1,
            pos2: bsk_proto::WorldPos { x: 3.5, y: which as f32 },
            color: 0xFFE8E8F0,
            duration: match which {
                look::PEARL_THROW => 380,
                look::PEARL_RECALL => 200,
                look::PEARL_HOME => 0,
                _ => 5000,
            },
            object_type: if which == look::PEARL_HOME { 4 } else { 8 },
            source_id: Some(me.id),
        };
        self.relic(&e, world, now);
        if which == look::PEARL_THROW {
            let at = |look: u8, duration: i32, object_type: u16, target: i32| ShowEffect {
                target_object_id: target,
                pos2: bsk_proto::WorldPos { x: 3.5, y: look as f32 },
                duration,
                object_type,
                ..e
            };
            self.delayed.push((now + 380, at(look::PEARL_POOL, 5000, 8, -1)));
            self.delayed.push((now + 5380, at(look::PEARL_RECALL, 200, 8, -1)));
            self.delayed.push((now + 5580, at(look::PEARL_HOME, 0, 4, me.id)));
        }
    }
}
