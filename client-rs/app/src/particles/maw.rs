//! The Sharklord's Maw of the Deep (the server's `Player.Maw.cs`): `ShowEffect` 41 looks 70 and
//! 71 (`relic::look`), each with the spell's tier in `object_type` (16 more for a Megalodon), its
//! colour (`Player.MawColors`) and the Sharklord in `source_id`.
//!
//! - 70, the Maw: from the Sharklord (the target) a fin cuts through the floor to `pos1` over
//!   `duration` ms, a wake of foam spreading behind it and a whirlpool opening ahead; there a
//!   shark's jaws, `pos2.x` across, burst up out of the water and snap shut, throwing up a column
//!   of spray. A Megalodon's are bigger, the gums red, the fin taller, and blood in the water.
//! - 71, the feeding: the Sharklord bit `pos2.x` enemies round `pos1`: motes of their blood fly
//!   to them.
//!
//! The Frenzy stat (0 to 5) is drawn round a Sharklord's feet as fins circling them
//! (`draw_frenzy`, from `view.rs`), and to a Sharklord every Bloodied enemy (under half its
//! health) drips blood over its head (`draw_blood`).

use super::relic::{hash, look, mix, DecalKind, Pen, GRAVITY};
use super::{Motion, Particle, Particles};
use crate::view::Camera;
use bsk_proto::ShowEffect;
use bsk_render::{Renderer, Tint};
use bsk_world::World;
use std::f64::consts::PI;

/// The most Frenzy (the server's `Player.FrenzyMax`).
pub const FRENZY_MAX: i32 = 5;
const FOAM: u32 = 0xEAFBFF;
const HIDE: u32 = 0x4A6A90;
const HIDE_LIGHT: u32 = 0x9CB8D8;
const TOOTH: u32 = 0xF8F8F0;
const BLOOD: u32 = 0xC8102C;
const BLOOD_LIGHT: u32 = 0xFF5068;
/// How long the jaws stay once they bite, ms.
const JAW_MS: i32 = 700;
/// The jaws open this long, then snap shut.
const OPEN_MS: f64 = 120.0;
const SNAP_MS: f64 = 90.0;
/// The most Maws drawn at once.
const MAX_MAWS: usize = 24;

/// A Maw as it's drawn on the floor: the fin's way and the jaws where it bites.
#[derive(Clone, Copy, Debug)]
pub(super) struct MawLook {
    start: i32,
    /// When the jaws burst up.
    bites: i32,
    from: (f64, f64),
    to: (f64, f64),
    radius: f64,
    color: u32,
    megalodon: bool,
    z: f64,
    level: u8,
    dim: f32,
    seed: u64,
}

impl MawLook {
    fn until(&self) -> i32 {
        self.bites + JAW_MS
    }
}

fn ease(t: f64) -> f64 {
    let t = t.clamp(0.0, 1.0);
    1.0 - (1.0 - t).powi(3)
}

impl Particles {
    /// Looks 70 and 71 (`relic` hands them here).
    pub(super) fn maw_look(&mut self, e: &ShowEffect, which: u8, world: &World, now: i32) {
        let to = (e.pos1.x as f64, e.pos1.y as f64);
        let color = e.color & 0xFF_FFFF;
        let megalodon = e.object_type >= 16;
        if which == look::FEED {
            self.feed(to, e.target_object_id, (e.pos2.x as usize).clamp(1, 3), megalodon, world);
            return;
        }
        let radius = (e.pos2.x as f64).clamp(0.5, 8.0);
        let ms = e.duration.clamp(40, 1000);
        let from = world.objects.get(&e.target_object_id).map_or((to.0 - 2.0, to.1), |o| (o.x, o.y));
        let (z, level) = self.here;
        if self.maws.len() >= MAX_MAWS {
            self.maws.remove(0);
        }
        if !self.disabled && self.muting < 1.0 {
            let seed = (self.random() * u32::MAX as f64) as u64;
            self.maws.push(MawLook { start: now, bites: now + ms, from, to, radius, color, megalodon, z, level, dim: 1.0 - self.muting, seed });
        }
        self.maw_way(from, to, ms, color);
        self.maw_bite(to, radius, color, megalodon, ms, now);
    }

    /// Along the fin's way: spray kicked up off both sides as it passes.
    fn maw_way(&mut self, from: (f64, f64), to: (f64, f64), ms: i32, color: u32) {
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        let dist = dx.hypot(dy);
        if dist < 0.3 {
            return;
        }
        let (ux, uy) = (dx / dist, dy / dist);
        // a few fine droplets peeling off each side of the fin, white, low and quick
        let steps = (dist / 0.9).ceil() as usize;
        for i in 0..steps {
            let f = (i as f64 + 0.5) / steps as f64;
            let (x, y) = (from.0 + dx * f, from.1 + dy * f);
            for side in [-1.0, 1.0] {
                let s = (0.35 + 0.25 * self.random()) / 1000.0;
                let (vx, vy) = (-uy * side * s, ux * side * s);
                let mut p = Particle::new(x, y, 0.05, FOAM, 38.0, 260, Motion::Ballistic { g: GRAVITY * 0.5 }).moving(vx, vy, 0.0025);
                p.delay = (f * ms as f64) as i32;
                p.fade = true;
                p.opacity = 0.8;
                self.add(p);
            }
        }
        let _ = color;
    }

    /// Where it bites: the water drawing into a small whirl as the fin nears; as the jaws shut, one
    /// clean ring of foam and a handful of droplets. A Megalodon's adds a thin crimson ring and a
    /// few dark drops, nothing more.
    fn maw_bite(&mut self, (x, y): (f64, f64), r: f64, color: u32, megalodon: bool, ms: i32, now: i32) {
        let deep = mix(color, 0x061420, 0.7);
        let shut = ms + (OPEN_MS + SNAP_MS) as i32;
        self.decal(DecalKind::Whirlpool, x, y, r * 0.55, deep, now + ms / 2, ms / 2 + (OPEN_MS + SNAP_MS) as i32 + 200, None);
        self.decal(DecalKind::Ring, x, y, r * 0.95, FOAM, now + shut, 320, None);
        for i in 0..10 {
            let a = 2.0 * PI * i as f64 / 10.0 + self.pm(0.2);
            let s = (0.9 + 0.5 * self.random()) / 1000.0;
            let mut p = Particle::new(x + 0.3 * r * a.cos(), y + 0.3 * r * a.sin(), 0.2, if i % 3 == 0 { mix(color, 0xFFFFFF, 0.5) } else { FOAM }, 46.0, 420, Motion::Ballistic { g: GRAVITY * 0.7 })
                .moving(a.cos() * s, a.sin() * s, 0.004 + 0.002 * self.random());
            p.delay = shut;
            p.fade = true;
            self.add(p);
        }
        if megalodon {
            self.decal(DecalKind::Ring, x, y, r * 1.25, BLOOD, now + shut + 60, 420, None);
            for i in 0..4 {
                let a = 2.0 * PI * i as f64 / 4.0 + self.pm(0.4);
                let s = (0.6 + 0.4 * self.random()) / 1000.0;
                let mut p = Particle::new(x, y, 0.25, BLOOD, 40.0, 520, Motion::Ballistic { g: GRAVITY * 0.8 }).moving(a.cos() * s, a.sin() * s, 0.003);
                p.delay = shut;
                p.fade = true;
                self.add(p);
            }
        }
    }

    /// The Sharklord (`to`) feeding on `n` enemies bitten round `at`: a faint thread of red motes
    /// drawn back to them.
    fn feed(&mut self, at: (f64, f64), to: i32, n: usize, megalodon: bool, world: &World) {
        let mut p = Particle::new(at.0, at.1, 0.4, BLOOD_LIGHT, 90.0, 220, Motion::Spark);
        p.opacity = 0.6;
        self.add(p);
        let Some((tx, ty)) = world.objects.get(&to).map(|o| (o.x, o.y)) else { return };
        let speed = ((tx - at.0).hypot(ty - at.1) / 420.0).max(0.008);
        for i in 0..n * if megalodon { 3 } else { 2 } {
            let (ox, oy) = (self.pm(0.5), self.pm(0.5));
            let mut p = Particle::new(at.0 + ox, at.1 + oy, 0.5, if i % 2 == 0 { BLOOD_LIGHT } else { 0xFFD0D8 }, 55.0, 1400, Motion::Seek { id: to, tx, ty, speed });
            p.delay = 60 * i as i32;
            p.alpha = 0.85;
            self.add(p);
        }
    }
    pub(super) fn maw_update(&mut self, now: i32) {
        self.maws.retain(|m| now < m.until());
    }

    /// The Maws on floor `level` (0 the map): the fins on their way and the jaws.
    pub(super) fn draw_maws(&self, cam: &Camera, renderer: &mut Renderer, level: u8) {
        let now = self.now;
        for m in self.maws.iter().filter(|m| m.level == level && now >= m.start) {
            if now < m.bites + 60 {
                draw_fin(cam, renderer, m, now);
            }
            if now >= m.bites {
                let mut pen = Pen { cam, renderer: &mut *renderer, x: m.to.0, y: m.to.1, z: m.z, dim: m.dim };
                draw_jaws(&mut pen, m, now);
            }
        }
    }
}

/// The fin cutting through the floor toward the bite, its wake of foam spreading behind; it
/// sinks as it arrives.
fn draw_fin(cam: &Camera, renderer: &mut Renderer, m: &MawLook, now: i32) {
    let travel = (m.bites - m.start).max(1) as f64;
    let t = ((now - m.start) as f64 / travel).min(1.0);
    let (dx, dy) = (m.to.0 - m.from.0, m.to.1 - m.from.1);
    let dist = dx.hypot(dy).max(1e-6);
    let (ux, uy) = (dx / dist, dy / dist);
    let (x, y) = (m.from.0 + dx * t, m.from.1 + dy * t);
    let sink = if now > m.bites { 1.0 - (now - m.bites) as f64 / 60.0 } else { (1.0 - (t - 0.85).max(0.0) / 0.15 * 0.5).min(1.0) };
    let h = if m.megalodon { 0.95 } else { 0.6 } * sink.clamp(0.0, 1.0) * ease(t / 0.12);
    // the wake: two fine lines of foam opening back from the fin, fading toward their ends
    let mut pen = Pen { cam, renderer: &mut *renderer, x, y, z: m.z, dim: m.dim };
    let back = (1.4 * t * dist / dist.max(1.4)).min(1.8);
    for side in [-1.0, 1.0] {
        let tip = (-ux * back - uy * side * back * 0.35, -uy * back + ux * side * back * 0.35);
        for k in 0..4 {
            let (f0, f1) = (k as f64 / 4.0, (k + 1) as f64 / 4.0);
            pen.line((tip.0 * f0, tip.1 * f0), (tip.0 * f1, tip.1 * f1), 0.05, FOAM, 0.5 * (1.0 - f0 as f32) * sink.clamp(0.0, 1.0) as f32);
        }
    }
    if h <= 0.02 {
        return;
    }
    // the fin itself: a blade of hide standing out of the floor, swept back, its leading edge lit,
    // a thin line of foam where it cuts the water
    let at = |ox: f64, oy: f64, oz: f64| cam.to_screen(x + ox, y + oy, m.z + oz);
    let len = if m.megalodon { 0.7 } else { 0.5 };
    let front = at(ux * len * 0.35, uy * len * 0.35, 0.0);
    let rear = at(-ux * len * 0.65, -uy * len * 0.65, 0.0);
    let tip = at(-ux * len * 0.45, -uy * len * 0.45, h);
    let mid = at(-ux * len * 0.05, -uy * len * 0.05, h * 0.55);
    let hide = if m.megalodon { mix(HIDE, 0x10182A, 0.5) } else { HIDE };
    let a = m.dim;
    tri(renderer, [front, rear, tip], hide, a);
    tri(renderer, [front, mid, tip], HIDE_LIGHT, 0.55 * a);
    let mut pen = Pen { cam, renderer: &mut *renderer, x, y, z: m.z, dim: m.dim };
    pen.line((ux * len * 0.4, uy * len * 0.4), (-ux * len * 0.7, -uy * len * 0.7), 0.06, FOAM, 0.7);
}

/// The jaws rising out of the whirl: two rows of slim white teeth, upper and lower, a thin shadow
/// under each, opening and snapping shut over the bite, then sinking back. No fill but the teeth:
/// the floor shows through.
fn draw_jaws(pen: &mut Pen, m: &MawLook, now: i32) {
    let t = (now - m.bites) as f64;
    let r = m.radius;
    let open = ease(t / OPEN_MS);
    // how far apart the jaws are: wide, then shut
    let gap = if t < OPEN_MS { open } else { 1.0 - ease((t - OPEN_MS) / SNAP_MS) };
    let fade = (1.0 - ((t - (JAW_MS as f64 - 300.0)).max(0.0) / 300.0)).clamp(0.0, 1.0) as f32;
    let line = if m.megalodon { 0x5A0A18 } else { 0x0C1A2C };
    // a faint shadow of the shark below
    pen.disc(r * 0.6 * open, mix(m.color, 0x020810, 0.8), 0.18 * fade);
    let n = if m.megalodon { 11 } else { 9 };
    let wobble = 0.03 * (hash(m.seed, 3) - 0.5);
    let (rx, ry) = (r * (0.6 + 0.3 * open), r * 0.32);
    for sy in [-1.0f64, 1.0] {
        let off = sy * r * (0.05 + 0.3 * gap);
        let arc = |pen: &Pen, u: f64, lift: f64| {
            let a = PI * u;
            pen.pt(rx * a.cos(), off + sy * (ry * a.sin() - lift) + wobble)
        };
        // the shadow of the jaw, a thin line under its teeth
        let segs = 14;
        for i in 0..segs {
            let (u0, u1) = (0.1 + 0.8 * i as f64 / segs as f64, 0.1 + 0.8 * (i + 1) as f64 / segs as f64);
            let q = [arc(pen, u0, -0.03 * r), arc(pen, u1, -0.03 * r), arc(pen, u1, 0.02 * r), arc(pen, u0, 0.02 * r)];
            pen.quad(q, line, 0.55 * fade);
        }
        // the teeth: slim, longest in the middle, pointing at the other jaw
        for i in 0..n {
            let u = 0.14 + 0.72 * (i as f64 + 0.5) / n as f64;
            let w = 0.022;
            let mid = 1.0 - (2.0 * (u - 0.5)).abs();
            let len = r * (0.07 + 0.08 * mid) * if m.megalodon { 1.3 } else { 1.0 };
            let (b0, b1, tip) = (arc(pen, u - w, 0.0), arc(pen, u + w, 0.0), arc(pen, u, len));
            pen.tri([b0, b1, tip], TOOTH, 0.95 * fade);
            // a sliver of shade down one side of each tooth
            pen.tri([arc(pen, u, 0.0), b1, tip], 0xB8C4D0, 0.5 * fade);
        }
    }
}
fn tri(r: &mut Renderer, pts: [[f32; 2]; 3], rgb: u32, a: f32) {
    if a > 0.005 {
        let white = r.white();
        r.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(rgb, a.min(1.0)));
    }
}

fn glow(r: &mut Renderer, x: f32, y: f32, rad: f32, rgb: u32, a: f32) {
    if a <= 0.005 {
        return;
    }
    let white = r.white();
    let mid = Tint::color(rgb, a.min(1.0)).mul;
    let edge = Tint::color(rgb, 0.0).mul;
    const N: usize = 14;
    for i in 0..N {
        let (a0, a1) = (i as f32 * std::f32::consts::TAU / N as f32, (i + 1) as f32 * std::f32::consts::TAU / N as f32);
        r.triangle_colors(white, [[x, y], [x + rad * a0.cos(), y + rad * a0.sin()], [x + rad * a1.cos(), y + rad * a1.sin()]], [[0.5, 0.5]; 3], [mid, edge, edge]);
    }
}

/// A Sharklord's Frenzy (the Frenzy stat, 1 to 5) round their feet at (cx, cy) on the screen,
/// `tile` pixels a tile: fins circling them through a ring of churned water, faster for each
/// stack; at a full Frenzy the water runs red.
pub fn draw_frenzy(r: &mut Renderer, cx: f32, cy: f32, tile: f32, stacks: i32, now: i32, alpha: f32) {
    let n = stacks.clamp(0, FRENZY_MAX);
    if n <= 0 || alpha <= 0.01 {
        return;
    }
    let full = n >= FRENZY_MAX;
    let (rx, ry) = (0.55 * tile, 0.21 * tile);
    let spin = now as f32 * (0.0016 + 0.0005 * n as f32);
    let water = if full { BLOOD } else { 0x3CA0F0 };
    glow(r, cx, cy, 0.62 * tile, water, (0.06 + 0.03 * n as f32) * alpha);
    for i in 0..n {
        let a = spin + i as f32 * std::f32::consts::TAU / n as f32;
        let (x, y) = (cx + rx * a.cos(), cy + ry * a.sin());
        // the back of the ring dimmer, behind the Sharklord
        let near = if a.sin() > 0.0 { 1.0 } else { 0.5 };
        // moving round: its heading on the screen, the fin swept back against it
        let dir = if (-a.sin()) * rx >= 0.0 { 1.0 } else { -1.0 };
        let h = 0.2 * tile;
        let w = 0.11 * tile;
        // the foam it pushes ahead, and its wake
        glow(r, x + dir * w * 0.6, y, 0.08 * tile, FOAM, 0.4 * alpha * near);
        glow(r, x - dir * w * 1.4, y, 0.1 * tile, if full { BLOOD_LIGHT } else { FOAM }, 0.22 * alpha * near);
        let hide = if full { mix(HIDE, 0x501020, 0.35) } else { HIDE };
        tri(r, [[x + dir * w * 0.5, y], [x - dir * w, y], [x - dir * w * 0.55, y - h]], hide, alpha * near);
        tri(r, [[x + dir * w * 0.5, y], [x - dir * w * 0.05, y - h * 0.45], [x - dir * w * 0.55, y - h]], HIDE_LIGHT, 0.7 * alpha * near);
    }
}

/// A Bloodied enemy as a Sharklord sees it, its head at (cx, top) on the screen: a drop of blood
/// hanging over it, swelling and falling, again and again.
pub fn draw_blood(r: &mut Renderer, cx: f32, top: f32, tile: f32, id: i32, now: i32, alpha: f32) {
    if alpha <= 0.01 {
        return;
    }
    let period = 900;
    let ph = ((now + id.wrapping_mul(137)).rem_euclid(period)) as f32 / period as f32;
    let y0 = top - 0.18 * tile;
    let s = 0.06 * tile * (0.7 + 0.5 * ph.min(0.6) / 0.6);
    let fall = ((ph - 0.6).max(0.0) / 0.4).powi(2) * 0.35 * tile;
    let a = alpha * if ph > 0.85 { (1.0 - ph) / 0.15 } else { 1.0 };
    let (x, y) = (cx, y0 + fall);
    glow(r, x, y, 2.4 * s, BLOOD, 0.35 * a);
    tri(r, [[x, y - 1.8 * s], [x + s, y], [x - s, y]], BLOOD, a);
    tri(r, [[x - s, y], [x + s, y], [x, y + s]], BLOOD, a);
    tri(r, [[x - 0.4 * s, y - 0.3 * s], [x - 0.1 * s, y - 0.9 * s], [x - 0.1 * s, y]], BLOOD_LIGHT, 0.8 * a);
}

impl Particles {
    /// `--effect 270` and `271` (tests): a Megalodon Tooth's Maw from us to four tiles right of us
    /// (270 as a Megalodon, its feeding after), or only the feeding (271).
    pub(super) fn test_maw(&mut self, which: u8, world: &World, now: i32) {
        let Some(me) = world.player() else { return };
        let pos1 = bsk_proto::WorldPos { x: me.x as f32 + 4.0, y: me.y as f32 };
        let e = |look: u8, x: f32, duration: i32| ShowEffect {
            effect_type: 41,
            target_object_id: me.id,
            pos1,
            pos2: bsk_proto::WorldPos { x, y: look as f32 },
            color: 0xFFFF3C50,
            duration,
            object_type: 8 + 16,
            source_id: Some(me.id),
        };
        if which == look::MAW {
            self.relic(&e(look::MAW, 5.25, 220), world, now);
            self.delayed.push((now + 220, e(look::FEED, 3.0, 0)));
        } else {
            self.relic(&e(look::FEED, 3.0, 0), world, now);
        }
    }
}
