//! The Mechanic's turrets (the server's `Turret.cs` and `Player.Turrets.cs`): `ShowEffect` 41
//! looks 54 to 57 (`relic::look`), the crate thrown, the turret built, one already up and its
//! blast; and for as long as each stands, what's drawn round it:
//!
//! - its plate on the ground: a ring of gear teeth in its tier's colour, turning (fast while it's
//!   overclocked), its health running down round it (it wears down, and allies heal it: the
//!   server's `Turret.cs`), and in its last seconds the plate blinking red with its blast's reach;
//! - while its Mechanic is within `LINK_RANGE` (the server's `Turret.LINK_RANGE`, where it's
//!   overclocked), a cable of light along the floor from them to it, pulses running down it, and
//!   sparks crackling off it;
//! - a flash at its muzzle for each shot (`turret_shot`, from the shots' `ServerPlayerShoot`);
//! - for our own, its reach in a ring for a moment after it's built.
//!
//! Each tier is its colour (`Turret.COLORS`). Looks 55 and 56 carry how long its health lasts at its
//! plain wear; while its object is in view, its health (stats 1 and 0) says how long it has left. A
//! turret's plate is kept while its object is out of view (the server sends look 56 again when it
//! comes back) and goes when its health would have run out, or with its blast.

use super::relic::{hash, look, mix, DecalKind, Pen, GRAVITY};
use super::{Motion, Particle, Particles};
use crate::view::Camera;
use bsk_proto::ShowEffect;
use bsk_render::Renderer;
use bsk_world::World;
use std::f64::consts::PI;

/// How near its Mechanic overclocks a turret (the server's `Turret.LINK_RANGE`).
pub(super) const LINK_RANGE: f64 = 7.0;
/// The plate's radius, tiles.
const PLATE: f64 = 0.62;
/// A turret's last ms: blinking red, its blast's reach shown.
const WARNING: i32 = 3000;
/// How long our new turret's reach shows, ms.
const REACH_MS: i32 = 1800;
/// The most turrets drawn at once.
const MAX_TURRETS: usize = 48;

/// A turret standing, as it's drawn.
#[derive(Clone, Copy, Debug)]
pub(super) struct TurretLook {
    id: i32,
    owner: i32,
    color: u32,
    start: i32,
    until: i32,
    reach: f64,
    blast: f64,
    x: f64,
    y: f64,
    z: f64,
    level: u8,
    /// Where its Mechanic is, whether they're near enough to overclock it, and since when.
    ox: f64,
    oy: f64,
    linked: bool,
    since: i32,
    /// Its object is in view (always, for the `--effect` test's, which has none).
    shown: bool,
    /// Ours, just built: its reach in a ring for a moment.
    ring: bool,
    dim: f32,
    seed: u64,
    /// When it next gives off a spark, and when it last fired.
    next: i32,
    shot: i32,
    /// Which way its head points (world radians): its last shot's way, sweeping slowly when idle.
    aim: f64,
    /// Its health over its maximum, while its object shows them; and how many ms a point of its
    /// health lasts at its plain wear (from the look's time left and the health it first shows).
    health: Option<f64>,
    ms_per_hp: Option<f64>,
}

/// How high a turret's head turns over the ground, tiles: the base's turntable at size 80 (row 8
/// of its 16, `draw_turrets.py`); its shots fly at this height, out of the barrel.
pub const HEAD_Z: f64 = 0.64;

#[cfg(test)]
impl TurretLook {
    pub(super) fn linked_for_tests(&self) -> bool {
        self.linked
    }
}

/// A colour's metal: its shadow, its body and its shine.
fn metal(color: u32) -> (u32, u32, u32) {
    (mix(color, 0x101418, 0.78), mix(color, 0x3A4048, 0.55), mix(color, 0xFFFFFF, 0.45))
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

/// A dashed circle of radius `r`, `n` dashes turning by `spin`.
fn dashes(pen: &mut Pen, r: f64, w: f64, n: usize, spin: f64, rgb: u32, alpha: f32) {
    for i in 0..n {
        let a = spin + i as f64 * 2.0 * PI / n as f64;
        arc(pen, r - w / 2.0, r + w / 2.0, a, a + PI / n as f64, rgb, alpha);
    }
}

fn ease(t: f64) -> f64 {
    let t = t.clamp(0.0, 1.0);
    1.0 - (1.0 - t).powi(3)
}

impl Particles {
    /// Looks 54 to 57 (`relic` hands them here).
    pub(super) fn turret_look(&mut self, e: &ShowEffect, which: u8, world: &World, now: i32) {
        let (x, y) = (e.pos1.x as f64, e.pos1.y as f64);
        let color = e.color & 0xFF_FFFF;
        let id = e.target_object_id;
        let ms = e.duration.clamp(0, 60_000);
        match which {
            look::TURRET_THROW => {
                let from = world.objects.get(&id).map_or((x - 1.5, y - 1.5), |o| (o.x, o.y));
                self.crate_throw(from, (x, y), ms.clamp(100, 2000), color);
            }
            look::TURRET_BUILD | look::TURRET_SEEN => {
                let built = which == look::TURRET_BUILD;
                let owner = e.source_id.unwrap_or(0);
                if id >= 0 {
                    self.turrets.retain(|t| t.id != id);
                }
                if self.turrets.len() >= MAX_TURRETS || self.disabled || self.muting >= 1.0 {
                    return;
                }
                let (z, level) = self.here;
                let seed = (self.random() * u32::MAX as f64) as u64;
                let (ox, oy) = world.objects.get(&owner).map_or((x, y), |o| (o.x, o.y));
                self.turrets.push(TurretLook {
                    id,
                    owner,
                    color,
                    start: if built { now } else { now - 1000 },
                    until: now + ms,
                    reach: (e.pos2.x as f64).clamp(0.0, 20.0),
                    blast: (e.object_type as f64 / 100.0).clamp(0.0, 8.0),
                    x,
                    y,
                    z,
                    level,
                    ox,
                    oy,
                    linked: false,
                    since: now,
                    shown: id < 0 || world.objects.contains_key(&id),
                    ring: built && world.me == Some(owner),
                    dim: 1.0 - self.muting,
                    seed,
                    next: now + 400,
                    shot: i32::MIN / 2,
                    aim: 0.0,
                    health: None,
                    ms_per_hp: None,
                });
                if built {
                    self.turret_built(x, y, color, now);
                }
            }
            _ => {
                self.turrets.retain(|t| t.id != id);
                self.turret_blast(x, y, (e.pos2.x as f64).clamp(0.5, 8.0), color, now);
            }
        }
    }

    /// The crate flying from `from` to `to` in `ms`: a box in the turret's colours tumbling on an
    /// arc, sparks and smoke trailing it, its shadow racing along the floor.
    fn crate_throw(&mut self, from: (f64, f64), to: (f64, f64), ms: i32, color: u32) {
        const PEAK: f64 = 1.6;
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        let t_all = ms as f64;
        let (shadow, _, shine) = metal(color);
        let wood = mix(color, 0x8A6A40, 0.55);
        let steps = (ms / 16).max(1);
        for i in 0..steps {
            let t = i as f64 * t_all / steps as f64;
            let f = t / t_all;
            let (px, py, pz) = (from.0 + dx * f, from.1 + dy * f, 0.6 + 4.0 * PEAK * f * (1.0 - f));
            let spin = f * 2.0 * PI * 1.5;
            for (ox, oy, c, size) in [(0.0, 0.0, shadow, 240.0), (0.0, 0.0, wood, 190.0), (0.1 * spin.cos(), 0.1 * spin.sin(), shine, 70.0), (0.0, 0.0, color, 60.0)] {
                let mut p = Particle::new(px + ox, py + oy, pz, c, size, 34, Motion::Plain);
                p.delay = t as i32;
                self.add(p);
            }
            if i % 2 == 0 {
                let c = self.pick(&[color, shine, 0xFFE8A0]);
                let mut p = Particle::new(px, py, pz, c, 70.0, 220, Motion::Spark).moving(self.pm(0.6) / 1000.0, self.pm(0.6) / 1000.0, -0.0004);
                p.delay = t as i32;
                self.add(p);
            }
            if i % 3 == 1 {
                let mut p = Particle::new(px, py, pz, 0x8A8A8A, 110.0, 500, Motion::Plain).moving(0.0, 0.0, 0.0006);
                p.delay = t as i32;
                p.fade = true;
                p.opacity = 0.35;
                p.grow = 0.12;
                self.add(p);
            }
            if i % 2 == 1 {
                let mut p = Particle::new(px, py, 0.02, 0x101010, 170.0, 34, Motion::Plain);
                p.delay = t as i32;
                p.alpha = 0.4;
                self.add(p);
            }
        }
    }

    /// The turret built at (x, y): the crate slamming down in a ring of dust, bolts thrown up,
    /// welding sparks in three bursts as it unfolds, a blueprint's light spiralling up round it,
    /// then a ring of its colour as it locks.
    fn turret_built(&mut self, x: f64, y: f64, color: u32, now: i32) {
        let (_, body, shine) = metal(color);
        let print = mix(color, 0x9AD8FF, 0.6);
        for (size, c, life) in [(800.0, color, 180), (460.0, 0xFFFFFF, 120)] {
            self.add(Particle::new(x, y, 0.3, c, size, life, Motion::Spark));
        }
        self.decal(DecalKind::Ring, x, y, 1.4, 0xC8B898, now, 380, None);
        self.puffs(x, y, 8, &[0x8A8070, 0xA89C88, 0x6A6458], 200.0, 0.9, 0.5, 900, 0);
        self.debris(x, y, 12, &[0x9AA0A8, 0x6A7078, 0xD0D4DA, body, color], 70.0, 2.5, 4.0, 600, 0);
        // welding: three bursts of white-hot sparks as it unfolds
        for (k, delay) in [90, 200, 310].into_iter().enumerate() {
            let a = k as f64 * 2.1 + self.pm(0.4);
            let (wx, wy, wz) = (x + 0.3 * a.cos(), y + 0.2 * a.sin(), 0.4 + 0.5 * self.random());
            let mut flash = Particle::new(wx, wy, wz, 0xFFFFFF, 220.0, 70, Motion::Spark);
            flash.delay = delay;
            self.add(flash);
            for _ in 0..9 {
                let (b, s) = (2.0 * PI * self.random(), 0.002 + 0.003 * self.random());
                let c = self.pick(&[0xFFFFFF, 0xFFF0A0, 0xFFC040, shine]);
                let mut p = Particle::new(wx, wy, wz, c, 55.0, 380, Motion::Ballistic { g: GRAVITY * 0.6 }).moving(b.cos() * s, b.sin() * s, 0.002 + 0.002 * self.random());
                p.delay = delay;
                p.fade = true;
                self.add(p);
            }
        }
        // the blueprint's light spiralling up round it
        for i in 0..14 {
            let angle = i as f64 * 2.0 * PI / 14.0;
            let c = if i % 3 == 0 { 0xFFFFFF } else if i % 2 == 0 { color } else { print };
            let motion = Motion::Soul { cx: x, cy: y, angle, speed: 0.16, rad: 0.55, out: -0.0003, rise: 0.0026 };
            let mut p = Particle::new(x, y, 0.1, c, 90.0, 520, motion);
            p.alpha = 0.0;
            p.delay = i * 12;
            self.add(p);
        }
        // locked: a ring of its colour and a flash
        self.decal(DecalKind::Ring, x, y, 1.0, color, now + 380, 320, None);
        let mut ding = Particle::new(x, y, 0.8, 0xFFFFFF, 300.0, 120, Motion::Spark);
        ding.delay = 380;
        self.add(ding);
    }

    /// A turret blowing up at (x, y) out to `r`: a white flash and a fireball, shockwaves, the
    /// floor scorched, shrapnel and gears flung out, fire racing out along the floor, black smoke.
    fn turret_blast(&mut self, x: f64, y: f64, r: f64, color: u32, now: i32) {
        let (shadow, body, shine) = metal(color);
        for (size, c, life) in [(1300.0, 0xFFB040, 240), (800.0, 0xFFFFFF, 160), (650.0, color, 200)] {
            self.add(Particle::new(x, y, 0.45, c, size, life, Motion::Spark));
        }
        self.decal(DecalKind::Ring, x, y, r * 1.1, 0xFFA040, now, 460, None);
        self.decal(DecalKind::Ring, x, y, r * 0.8, 0xFFFFFF, now + 60, 340, None);
        self.decal(DecalKind::Ring, x, y, r * 0.95, color, now + 120, 380, None);
        self.decal(DecalKind::Scorch, x, y, r * 0.55, 0xFF8030, now, 1600, None);
        self.puffs(x, y, 10, &[0xFFD060, 0xFF8030, 0xFF5020], 280.0, r * 0.35, 1.6, 700, 0);
        self.puffs(x, y, 10, &[0x2A2A2A, 0x404040, 0x585858], 300.0, r * 0.4, 1.2, 1500, 120);
        self.debris(x, y, 26, &[0x9AA0A8, 0x6A7078, 0xD0D4DA, shadow, body, color], 90.0, 2.2 * r, 6.0, 900, 0);
        // a few gears, bigger and slower
        self.debris(x, y, 5, &[body, shine], 150.0, 1.4 * r, 7.0, 1100, 0);
        for i in 0..20 {
            let a = i as f64 * 2.0 * PI / 20.0 + self.pm(0.08);
            let s = (r / 300.0) * (0.8 + 0.4 * self.random());
            let c = if i % 4 == 0 { 0xFFFFFF } else if i % 2 == 0 { 0xFFC040 } else { color };
            self.add(Particle::new(x, y, 0.15, c, 140.0, 300, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0));
        }
    }

    /// A shot from (x, y) at `angle`: if it's a turret's, a flash at its muzzle, sparks thrown
    /// along the shot (more while it's overclocked) and now and then a wisp of smoke.
    pub fn turret_shot(&mut self, x: f64, y: f64, angle: f64, now: i32) {
        let Some(t) = self.turrets.iter_mut().find(|t| t.shown && (t.x - x).hypot(t.y - y) < 0.9) else { return };
        t.shot = now;
        t.aim = angle;
        let (color, linked, dim) = (t.color, t.linked, t.dim);
        let (_, _, shine) = metal(color);
        let muting = self.muting;
        self.muting = muting.max(1.0 - dim);
        let (cx, cy, z) = (x + angle.cos() * 0.15, y + angle.sin() * 0.15, 0.7);
        self.add(Particle::new(cx, cy, z, 0xFFFFFF, 150.0, 60, Motion::Spark));
        self.add(Particle::new(cx, cy, z, color, 260.0, 90, Motion::Spark));
        for _ in 0..if linked { 4 } else { 2 } {
            let a = angle + self.pm(0.45);
            let s = 0.005 + 0.004 * self.random();
            let c = self.pick(&[color, shine, 0xFFFFFF]);
            self.add(Particle::new(cx, cy, z, c, 70.0, 140, Motion::Spark).moving(a.cos() * s, a.sin() * s, 0.001));
        }
        if self.random() < 0.3 {
            let mut p = Particle::new(cx, cy, z, 0x9A9A9A, 110.0, 500, Motion::Plain).moving(angle.cos() * 0.0004, angle.sin() * 0.0004, 0.0008);
            p.fade = true;
            p.opacity = 0.4;
            p.grow = 0.15;
            self.add(p);
        }
        self.muting = muting;
    }

    /// The turrets moved on to `now`: where they and their Mechanics are, whether each is
    /// overclocked, and the sparks they give off.
    pub(super) fn turret_update(&mut self, world: &World, now: i32, dt: i32) {
        self.turrets.retain(|t| now < t.until + 200);
        let mut sparks: Vec<(f64, f64, u32, f32, u8)> = Vec::new();
        for t in self.turrets.iter_mut() {
            // idle a while: its head sweeps slowly round, looking for something to shoot
            if now - t.shot > 1500 {
                t.aim += 0.0006 * dt.clamp(0, 100) as f64;
            }
            if t.id >= 0 {
                match world.objects.get(&t.id) {
                    Some(o) => {
                        (t.x, t.y) = (o.x, o.y);
                        t.shown = true;
                        // its health: how much is left, and so how long it has (healed, it has longer)
                        if let (Some(hp), Some(max)) = (o.stat_int(1), o.stat_int(0)) {
                            if max > 0 && hp < i32::MAX {
                                let hp = hp.max(0) as f64;
                                t.health = Some((hp / max as f64).clamp(0.0, 1.0));
                                let per = *t.ms_per_hp.get_or_insert(((t.until - now).max(1) as f64 / hp.max(1.0)).clamp(1.0, 1000.0));
                                t.until = now + (hp * per) as i32;
                            }
                        }
                    }
                    None => t.shown = false,
                }
            }
            let linked = match world.objects.get(&t.owner) {
                Some(o) if !o.dead => {
                    (t.ox, t.oy) = (o.x, o.y);
                    (o.x - t.x).hypot(o.y - t.y) <= LINK_RANGE
                }
                _ => false,
            };
            if linked != t.linked {
                t.linked = linked;
                t.since = now;
                if linked && t.shown {
                    sparks.push((t.x, t.y, t.color, t.dim, 0));
                }
            }
            if t.shown && now >= t.next {
                let warning = t.until - now < WARNING;
                t.next = now + if t.linked { 130 } else if warning { 220 } else { 600 };
                sparks.push((t.x, t.y, t.color, t.dim, if warning { 2 } else if t.linked { 1 } else { 3 }));
            }
        }
        let muting = self.muting;
        for (x, y, color, dim, kind) in sparks {
            self.muting = 1.0 - dim;
            let (_, _, shine) = metal(color);
            match kind {
                // powered up: a ring of sparks bursting off it
                0 => {
                    for i in 0..10 {
                        let a = i as f64 * 2.0 * PI / 10.0;
                        let c = if i % 2 == 0 { 0xFFFFFF } else { color };
                        self.add(Particle::new(x, y, 0.6, c, 80.0, 260, Motion::Spark).moving(a.cos() * 0.004, a.sin() * 0.004, 0.001));
                    }
                }
                // overclocked: a spark crackling off its head
                1 => {
                    let (a, s) = (2.0 * PI * self.random(), 0.002 + 0.002 * self.random());
                    let c = self.pick(&[0xFFFFFF, shine, color]);
                    let mut p = Particle::new(x + self.pm(0.2), y + self.pm(0.15), 0.55 + 0.4 * self.random(), c, 60.0, 200, Motion::Spark).moving(a.cos() * s, a.sin() * s, 0.002);
                    p.fade = true;
                    self.add(p);
                }
                // its last seconds: red sparks and smoke
                2 => {
                    let mut p = Particle::new(x + self.pm(0.25), y + self.pm(0.2), 0.6, self.pick(&[0xFF3020, 0xFF8040]), 70.0, 300, Motion::Ballistic { g: GRAVITY * 0.5 })
                        .moving(self.pm(1.0) / 1000.0, self.pm(1.0) / 1000.0, 0.003);
                    p.fade = true;
                    self.add(p);
                    let mut s = Particle::new(x, y, 0.8, 0x505050, 120.0, 700, Motion::Plain).moving(self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0, 0.0010);
                    s.fade = true;
                    s.opacity = 0.4;
                    s.grow = 0.12;
                    self.add(s);
                }
                // idle: a puff of steam now and then
                _ => {
                    if self.random() < 0.5 {
                        let mut s = Particle::new(x + self.pm(0.2), y, 0.85, 0xD8DCE0, 90.0, 800, Motion::Plain).moving(0.0, 0.0, 0.0008);
                        s.fade = true;
                        s.opacity = 0.3;
                        s.grow = 0.1;
                        self.add(s);
                    }
                }
            }
        }
        self.muting = muting;
    }

    /// Turret `id`'s head: which way it points (world radians) and how far it's kicked back by its
    /// last shot (tiles).
    pub fn turret_head(&self, id: i32, now: i32) -> Option<(f64, f64)> {
        let t = self.turrets.iter().find(|t| t.id == id)?;
        let kick = 0.08 * (1.0 - ((now - t.shot) as f64 / 120.0).clamp(0.0, 1.0));
        Some((t.aim, kick))
    }

    /// The turrets' plates and links on floor `level` (0 the map), with the ground marks.
    pub(super) fn draw_turrets(&self, cam: &Camera, renderer: &mut Renderer, level: u8) {
        let now = self.now;
        for t in self.turrets.iter().filter(|t| t.shown && t.level == level && now >= t.start) {
            let mut pen = Pen { cam, renderer: &mut *renderer, x: t.x, y: t.y, z: t.z, dim: t.dim };
            draw_turret(&mut pen, t, now);
        }
    }
}

fn draw_turret(pen: &mut Pen, t: &TurretLook, now: i32) {
    let secs = (now - t.start) as f64 / 1000.0;
    let k = ease((now - t.start) as f64 / 400.0);
    let left = (t.until - now).max(0);
    let total = (t.until - t.start).max(1);
    let fading = ((left as f64) / 250.0).clamp(0.0, 1.0) as f32;
    let warning = left < WARNING;
    // blinking faster as the end nears
    let blink = warning && (now / if left < 1000 { 70 } else { 140 }) % 2 == 0;
    let (shadow, body, shine) = metal(t.color);
    let red = 0xFF3020;

    // the link to its Mechanic: a cable along the floor, light wavering round it, pulses running
    // down it to the turret
    if t.linked {
        let (ox, oy) = (t.ox - t.x, t.oy - t.y);
        let d = ox.hypot(oy);
        let fade = (((LINK_RANGE - d) / 1.0).clamp(0.0, 1.0) * ((now - t.since) as f64 / 250.0).clamp(0.0, 1.0)) as f32 * fading;
        if d > PLATE && fade > 0.0 {
            let (ux, uy) = (ox / d, oy / d);
            let (start, end) = ((ux * PLATE * 0.9, uy * PLATE * 0.9), (ox - ux * 0.35, oy - uy * 0.35));
            pen.line(start, end, 0.09, 0x101418, 0.45 * fade);
            pen.line(start, end, 0.24, t.color, 0.12 * fade);
            // the wavering light, jumping a little every 70 ms
            const SEGS: u64 = 10;
            let jump = (now / 70) as u64;
            let mut prev = start;
            for i in 1..=SEGS {
                let f = i as f64 / SEGS as f64;
                let off = if i == SEGS { 0.0 } else { (hash(t.seed, jump * 31 + i) - 0.5) * 0.2 * (f * PI).sin() };
                let p = (start.0 + (end.0 - start.0) * f - uy * off, start.1 + (end.1 - start.1) * f + ux * off);
                pen.line(prev, p, 0.06, mix(t.color, 0xFFFFFF, 0.35), 0.6 * fade);
                prev = p;
            }
            // pulses running from the Mechanic to the turret
            for j in 0..3 {
                let f = (secs * 1.4 + j as f64 / 3.0).fract();
                let (px, py) = (end.0 + (start.0 - end.0) * f, end.1 + (start.1 - end.1) * f);
                pen.disc_at(px, py, 0.13, t.color, 0.35 * fade);
                pen.disc_at(px, py, 0.06, 0xFFFFFF, 0.85 * fade);
            }
        }
    }

    // our new turret's reach, for a moment
    let age = now - t.start;
    if t.ring && age < REACH_MS && t.reach > 0.0 {
        let a = (1.0 - age as f32 / REACH_MS as f32) * ease(age as f64 / 300.0) as f32;
        dashes(pen, t.reach * (0.85 + 0.15 * ease(age as f64 / 400.0)), 0.05, 56, secs * 0.25, mix(t.color, 0xFFFFFF, 0.3), 0.55 * a);
    }
    // its blast's reach in its last seconds
    if warning && t.blast > 0.0 {
        let a = if blink { 0.45 } else { 0.2 };
        dashes(pen, t.blast, 0.06, 28, -secs * 0.8, red, a * fading);
    }

    // the plate: a shadow, a ring of gear teeth turning, a dark face with four bolts
    let r = PLATE * k;
    let spin = hash(t.seed, 7) * 2.0 * PI + secs * if t.linked { 3.0 } else { 0.9 };
    let rim = if blink { red } else { body };
    pen.disc(r * 1.08, 0x000000, 0.35 * fading);
    for i in 0..10 {
        let a = spin + i as f64 * 2.0 * PI / 10.0;
        arc(pen, r * 0.8, r, a - 0.13, a + 0.13, rim, 0.9 * fading);
    }
    pen.band(r * 0.6, r * 0.84, rim, 0.9 * fading);
    pen.band(r * 0.8, r * 0.84, shine, 0.5 * fading);
    pen.disc(r * 0.6, shadow, 0.85 * fading);
    for i in 0..4 {
        let a = -spin * 0.5 + i as f64 * PI / 2.0 + PI / 4.0;
        pen.disc_at(r * 0.45 * a.cos(), r * 0.45 * a.sin(), 0.04, shine, 0.8 * fading);
    }
    // overclocked: its heart glowing, pulsing; a shot: a flash of it
    let glow = if t.linked { 0.3 + 0.15 * (secs * 9.0).sin() } else { 0.0 } + 0.4 * (1.0 - ((now - t.shot) as f64 / 120.0).clamp(0.0, 1.0));
    if glow > 0.01 {
        pen.disc(r * 0.38, t.color, glow as f32 * fading);
    }
    // its health (or without it, the time it has left), running down round it
    let frac = t.health.unwrap_or(left as f64 / total as f64);
    let top = -PI / 2.0;
    arc(pen, r * 0.64, r * 0.76, top, top + 2.0 * PI * frac, if blink { 0xFFFFFF } else if warning { red } else { t.color }, 0.95 * fading);
}

impl Particles {
    /// `--effect 254` to `257` (tests): the turret looks, on a spot three tiles off us, as a
    /// Skystorm Turret's (its colour, reach and blast), ours.
    pub(super) fn test_turret(&mut self, which: u8, world: &World, now: i32) {
        let Some(me) = world.player() else { return };
        let pos1 = bsk_proto::WorldPos { x: me.x as f32 + 3.0, y: me.y as f32 };
        let (r, ms, object_type) = match which {
            look::TURRET_THROW => (0.0, 350, 0),
            look::TURRET_BLAST => (3.5, 0, 350),
            _ => (8.96, 8000, 350),
        };
        let e = ShowEffect {
            effect_type: 41,
            target_object_id: if which == look::TURRET_THROW { me.id } else { -1 },
            pos1,
            pos2: bsk_proto::WorldPos { x: r, y: which as f32 },
            color: 0xFF9A8CFF,
            duration: ms,
            object_type,
            source_id: Some(me.id),
        };
        self.relic(&e, world, now);
        // the whole of it: thrown, built, firing, blowing up
        if which == look::TURRET_THROW {
            let build = ShowEffect { target_object_id: -1, pos2: bsk_proto::WorldPos { x: 8.96, y: look::TURRET_BUILD as f32 }, duration: 6000, object_type: 350, ..e };
            self.delayed.push((now + 350, build));
            let blast = ShowEffect { target_object_id: -1, pos2: bsk_proto::WorldPos { x: 3.5, y: look::TURRET_BLAST as f32 }, duration: 0, ..e };
            self.delayed.push((now + 6350, blast));
        }
    }
}
