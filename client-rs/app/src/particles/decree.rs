//! The King's Royal Decree (the server's `Player.Decree.cs`): `ShowEffect` 41 looks 67 to 69
//! (`relic::look`), each with its scepter's tier in `object_type` and its colour
//! (`Player.DecreeColors`), the King in `source_id`.
//!
//! - 67, the seal: from the King (the target) the royal seal falls on `pos1` over `duration` ms,
//!   `pos2.x` across: a great golden seal coming down out of the sky, its shadow growing under it;
//!   where it lands a flash, shockwaves, coins and dust thrown up, and the seal's mark left on the
//!   ground (a ring of the realm's marks round a gem) fading slowly.
//! - 68, Condemned: the target is Condemned for `duration` ms (`pos2.x` its size): a crown of gold
//!   hangs over its head, its gem in the decree's colour, bobbing, blinking in its last second. The
//!   crowns are drawn over the objects (`draw_crown`, from `view.rs`).
//! - 69, the Crown's Due: a Condemned enemy fell at `pos1`, and the target (one of those who
//!   brought it down, or the King) gains a stack of Tribute, `pos2.x` stacks now: a burst of gold
//!   coins and a coin flying to them; with 16 added to `object_type` (the King, paid mana) a mote of
//!   mana too. A player's stacks (the Tribute stat) are drawn round their feet (`draw_tribute`).

use super::relic::{hash, look, mix, DecalKind, Pen, GRAVITY};
use super::{Motion, Particle, Particles};
use crate::view::Camera;
use bsk_proto::ShowEffect;
use bsk_render::{Renderer, Tint};
use bsk_world::World;
use std::f64::consts::PI;

/// The crown's gold and its shine.
const GOLD: u32 = 0xFFC83C;
const GOLD_LIGHT: u32 = 0xFFF0A0;
const GOLD_DARK: u32 = 0xA86A14;
const MANA: u32 = 0x5AA8FF;
/// How long a seal's mark stays on the ground once it lands, ms.
const MARK_MS: i32 = 1600;
/// The most marks and crowns drawn at once.
const MAX_MARKS: usize = 24;
const MAX_CROWNS: usize = 96;
/// A crown's last ms, blinking as its decree runs out.
const ENDING: i32 = 1000;

/// A seal's mark on the ground.
#[derive(Clone, Copy, Debug)]
pub(super) struct SealLook {
    x: f64,
    y: f64,
    z: f64,
    level: u8,
    radius: f64,
    color: u32,
    lands: i32,
    dim: f32,
    seed: u64,
}

/// A crown over a Condemned enemy.
#[derive(Clone, Copy, Debug)]
pub struct Crown {
    pub id: i32,
    pub start: i32,
    pub until: i32,
    pub color: u32,
}

fn ease(t: f64) -> f64 {
    let t = t.clamp(0.0, 1.0);
    1.0 - (1.0 - t).powi(3)
}

impl Particles {
    /// Looks 67 to 69 (`relic` hands them here).
    pub(super) fn decree_look(&mut self, e: &ShowEffect, which: u8, world: &World, now: i32) {
        let (x, y) = (e.pos1.x as f64, e.pos1.y as f64);
        let color = e.color & 0xFF_FFFF;
        let tier = (e.object_type % 16).min(8) as u8;
        match which {
            look::SEAL => {
                let r = (e.pos2.x as f64).clamp(0.5, 8.0);
                let ms = e.duration.clamp(60, 2000);
                self.seal_fall(x, y, r, ms, color, tier, now);
            }
            look::CONDEMN => {
                let id = e.target_object_id;
                self.crowns.retain(|c| c.id != id);
                if self.crowns.len() >= MAX_CROWNS || self.disabled || self.muting >= 1.0 {
                    return;
                }
                let ms = e.duration.clamp(200, 30_000);
                self.crowns.push(Crown { id, start: now, until: now + ms, color });
                // the crown coming down on it: a ring of gold closing in
                let at = world.objects.get(&id).map_or((x, y), |o| (o.x, o.y));
                self.decal(DecalKind::Ring, at.0, at.1, 0.9, GOLD, now, 260, Some(id));
                for i in 0..6 {
                    let a = 2.0 * PI * i as f64 / 6.0;
                    let mut p = Particle::new(at.0 + 0.6 * a.cos(), at.1 + 0.6 * a.sin(), 1.4, GOLD_LIGHT, 90.0, 300, Motion::Plain)
                        .moving(-0.6 * a.cos() / 300.0, -0.6 * a.sin() / 300.0, 0.0);
                    p.fade = true;
                    self.add(p);
                }
            }
            _ => self.tribute(x, y, e.target_object_id, color, e.object_type >= 16, world, now),
        }
    }

    /// The seal falling on (x, y) over `ms`, then landing.
    #[allow(clippy::too_many_arguments)]
    fn seal_fall(&mut self, x: f64, y: f64, r: f64, ms: i32, color: u32, tier: u8, now: i32) {
        const HIGH: f64 = 4.0;
        let steps = (ms / 16).max(1);
        let size = 260.0 + 90.0 * r;
        for i in 0..steps {
            let t = i as f64 / steps as f64;
            let z = HIGH * (1.0 - t * t);
            let delay = (t * ms as f64) as i32;
            // the seal: a disc of gold, its rim dark, a gem of its colour at its heart
            for (c, s, alpha) in [(color, size * 1.5, 0.25), (GOLD_DARK, size, 1.0), (GOLD, size * 0.82, 1.0), (color, size * 0.4, 1.0), (0xFFFFFF, size * 0.16, 1.0)] {
                let mut p = Particle::new(x, y, z, c, s, 34, Motion::Plain);
                p.delay = delay;
                p.alpha = alpha;
                self.add(p);
            }
            // its shadow growing under it
            if i % 2 == 0 {
                let mut p = Particle::new(x, y, 0.02, 0x100808, size * (0.4 + 0.9 * t), 34, Motion::Plain);
                p.delay = delay;
                p.alpha = 0.25 + 0.25 * t as f32;
                self.add(p);
            }
        }
        // landing
        let light = mix(color, 0xFFFFFF, 0.5);
        for (s, c, life) in [(1300.0 + 200.0 * r, GOLD, 220), (800.0, 0xFFFFFF, 130), (1600.0, color, 260)] {
            let mut p = Particle::new(x, y, 0.3, c, s, life, Motion::Spark);
            p.delay = ms;
            if c == color {
                p.alpha = 0.4;
            }
            self.add(p);
        }
        self.decal(DecalKind::Ring, x, y, r * 1.15, GOLD_LIGHT, now + ms, 380, None);
        self.decal(DecalKind::Ring, x, y, r * 0.7, color, now + ms + 60, 340, None);
        self.decal(DecalKind::Cracks, x, y, r * 0.6, GOLD, now + ms, 900, None);
        self.debris(x, y, 18, &[GOLD, GOLD_LIGHT, GOLD_DARK, color], 90.0, 1.4 + 0.2 * r, 9.0, 900, ms);
        self.puffs(x, y, 8, &[0xD8C8A8, 0xB8A888], 220.0, r * 0.45, 0.6, 900, ms);
        // the High King's and the Last Dynasty's: a second, wider shockwave in their colour
        if tier >= 7 {
            self.decal(DecalKind::Ring, x, y, r * 1.6, light, now + ms + 140, 480, None);
        }
        if self.seals.len() >= MAX_MARKS {
            self.seals.remove(0);
        }
        if !self.disabled && self.muting < 1.0 {
            let (z, level) = self.here;
            let seed = (self.random() * u32::MAX as f64) as u64;
            self.seals.push(SealLook { x, y, z, level, radius: r, color, lands: now + ms, dim: 1.0 - self.muting, seed });
        }
    }

    /// Tribute paid at (x, y) to `to`: coins bursting up and a coin of gold flying to them, and
    /// mana too when it paid the King.
    #[allow(clippy::too_many_arguments)]
    fn tribute(&mut self, x: f64, y: f64, to: i32, color: u32, mana: bool, world: &World, now: i32) {
        self.add(Particle::new(x, y, 0.5, GOLD_LIGHT, 500.0, 120, Motion::Spark));
        self.decal(DecalKind::Ring, x, y, 1.2, GOLD, now, 360, None);
        // the coins: tumbling up and falling back, glinting (a few for each who's paid, so the more
        // brought it down, the richer the burst)
        for i in 0..7 {
            let a = 2.0 * PI * i as f64 / 7.0 + self.pm(0.4);
            let s = (0.5 + 0.8 * self.random()) / 1000.0;
            let c = [GOLD, GOLD_LIGHT, GOLD_DARK, color][i % 4];
            let mut p = Particle::new(x, y, 0.4, c, 110.0, 700 + (self.random() * 200.0) as i32, Motion::Ballistic { g: GRAVITY * 0.8 })
                .moving(a.cos() * s, a.sin() * s, 0.007 + 0.004 * self.random());
            p.fade = true;
            self.add(p);
        }
        let Some((tx, ty)) = world.objects.get(&to).map(|o| (o.x, o.y)) else { return };
        let speed = ((tx - x).hypot(ty - y) / 300.0).max(0.008);
        let mut motes = vec![(GOLD, 190.0, 60), (0xFFFFFF, 80.0, 60)];
        if mana {
            motes.extend([(MANA, 170.0, 160), (0xE0F0FF, 70.0, 160)]);
        }
        for (c, s, delay) in motes {
            let mut p = Particle::new(x, y, 0.8, c, s, 1300, Motion::Seek { id: to, tx, ty, speed });
            p.delay = delay;
            self.add(p);
        }
    }

    pub(super) fn decree_update(&mut self, now: i32) {
        self.seals.retain(|s| now < s.lands + MARK_MS);
        self.crowns.retain(|c| now < c.until);
    }

    /// The crown over object `id`, if it's Condemned.
    pub fn crown(&self, id: i32) -> Option<&Crown> {
        self.crowns.iter().find(|c| c.id == id)
    }

    /// The seals' marks on floor `level` (0 the map).
    pub(super) fn draw_seals(&self, cam: &Camera, renderer: &mut Renderer, level: u8) {
        let now = self.now;
        for s in self.seals.iter().filter(|s| s.level == level && now >= s.lands) {
            let mut pen = Pen { cam, renderer: &mut *renderer, x: s.x, y: s.y, z: s.z, dim: s.dim };
            draw_mark(&mut pen, s, now);
        }
    }
}

/// The seal's mark: a ring of gold with the realm's twelve marks round it, a band of its colour
/// inside, and a gem of its colour at its heart; flaring as it lands, fading.
fn draw_mark(pen: &mut Pen, s: &SealLook, now: i32) {
    let t = (now - s.lands) as f64;
    let open = ease(t / 160.0);
    let fade = (1.0 - (t / MARK_MS as f64)).clamp(0.0, 1.0).powf(0.7) as f32;
    let flare = (1.0 - t / 200.0).max(0.0) as f32;
    let r = s.radius * open;
    let spin = hash(s.seed, 1) * PI + t / 1000.0 * 0.25;
    pen.disc(r, mix(s.color, 0x201008, 0.6), 0.1 * fade);
    pen.band(r * 0.94, r, GOLD, (0.4 + 0.4 * flare) * fade);
    pen.band(r * 0.64, r * 0.68, s.color, 0.3 * fade);
    // the realm's twelve marks between the rings
    for i in 0..12 {
        let a = spin + i as f64 * PI / 6.0;
        let (r0, r1) = (r * 0.72, r * 0.88);
        let w = 0.05;
        pen.quad([pen.at(r0, a - w), pen.at(r1, a - w * 0.6), pen.at(r1, a + w * 0.6), pen.at(r0, a + w)], GOLD_LIGHT, 0.4 * fade);
    }
    // a gem of its colour at its heart
    pen.disc(r * 0.1, s.color, 0.8 * fade);
    if flare > 0.0 {
        pen.disc(r, 0xFFFFFF, 0.25 * flare * fade);
    }
}

fn tri(r: &mut Renderer, pts: [[f32; 2]; 3], rgb: u32, a: f32) {
    if a > 0.005 {
        let white = r.white();
        r.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(rgb, a.min(1.0)));
    }
}

fn quad(r: &mut Renderer, p: [[f32; 2]; 4], rgb: u32, a: f32) {
    tri(r, [p[0], p[1], p[2]], rgb, a);
    tri(r, [p[0], p[2], p[3]], rgb, a);
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

/// The crown over a Condemned enemy whose head is at (cx, top) on the screen, `tile` pixels a
/// tile: dropping into place, bobbing and swaying, its gem in the decree's colour, blinking as
/// its decree runs out.
pub fn draw_crown(r: &mut Renderer, cx: f32, top: f32, tile: f32, crown: &Crown, now: i32, alpha: f32) {
    let age = (now - crown.start) as f32;
    let left = (crown.until - now) as f32;
    let drop = (1.0 - (age / 220.0).min(1.0)).powi(2);
    let blink = if left < ENDING as f32 && (now / 120) % 2 == 0 { 0.35 } else { 1.0 };
    let a = alpha * blink * (left / 200.0).clamp(0.0, 1.0);
    if a <= 0.01 {
        return;
    }
    let bob = 0.04 * tile * (now as f32 * 0.004).sin();
    let sway = 0.08 * (now as f32 * 0.0025).sin();
    let (w, h) = (0.36 * tile, 0.2 * tile);
    let (x, y) = (cx, top - 0.22 * tile - bob - drop * 0.6 * tile);
    // turn a point (dx, dy) from the crown's middle by the sway
    let at = |dx: f32, dy: f32| [x + dx * sway.cos() - dy * sway.sin(), y + dx * sway.sin() + dy * sway.cos()];
    glow(r, x, y, 0.42 * tile, crown.color & 0xFF_FFFF, 0.35 * a);
    // the band
    quad(r, [at(-w / 2.0, 0.0), at(w / 2.0, 0.0), at(w / 2.0, h * 0.42), at(-w / 2.0, h * 0.42)], GOLD_DARK, a);
    quad(r, [at(-w / 2.0, h * 0.04), at(w / 2.0, h * 0.04), at(w / 2.0, h * 0.3), at(-w / 2.0, h * 0.3)], GOLD, a);
    // three points, the middle tallest, a pearl on each
    for (px, ph) in [(-0.38, 0.75), (0.0, 1.0), (0.38, 0.75)] {
        let bx = px * w;
        tri(r, [at(bx - 0.16 * w, 0.02 * h), at(bx + 0.16 * w, 0.02 * h), at(bx, -ph * h)], GOLD, a);
        tri(r, [at(bx - 0.06 * w, 0.0), at(bx + 0.02 * w, 0.0), at(bx - 0.02 * w, -ph * h * 0.8)], GOLD_LIGHT, 0.8 * a);
        let [tx, ty] = at(bx, -ph * h);
        glow(r, tx, ty, 0.06 * tile, GOLD_LIGHT, 0.7 * a);
    }
    // the gem in the band's middle, the decree's colour
    let [gx, gy] = at(0.0, h * 0.18);
    glow(r, gx, gy, 0.1 * tile, crown.color & 0xFF_FFFF, 0.8 * a);
    quad(r, [[gx, gy - 0.05 * tile], [gx + 0.045 * tile, gy], [gx, gy + 0.05 * tile], [gx - 0.045 * tile, gy]], crown.color & 0xFF_FFFF, a);
    quad(r, [[gx, gy - 0.025 * tile], [gx + 0.02 * tile, gy], [gx, gy + 0.01 * tile], [gx - 0.02 * tile, gy]], 0xFFFFFF, 0.8 * a);
}

/// A player's stacks of Tribute (the Tribute stat, 1 to 10), round their feet at (cx, cy) on the
/// screen, `tile` pixels a tile: gold coins circling slowly on the ground, the newest flashing in.
pub fn draw_tribute(r: &mut Renderer, cx: f32, cy: f32, tile: f32, stacks: i32, now: i32, alpha: f32) {
    let n = stacks.clamp(0, 10);
    if n <= 0 || alpha <= 0.01 {
        return;
    }
    let (rx, ry) = (0.42 * tile, 0.16 * tile);
    let spin = now as f32 * 0.0012;
    glow(r, cx, cy, 0.5 * tile, GOLD, 0.12 * alpha * n as f32 / 10.0);
    for i in 0..n {
        let a = spin + i as f32 * std::f32::consts::TAU / n as f32;
        let (x, y) = (cx + rx * a.cos(), cy + ry * a.sin());
        // the back of the ring dimmer, under the player
        let near = if a.sin() > 0.0 { 1.0 } else { 0.55 };
        let s = 0.055 * tile;
        let shine = 0.5 + 0.5 * (now as f32 * 0.006 + i as f32).sin();
        glow(r, x, y, 2.2 * s, GOLD, 0.3 * alpha * near);
        quad(r, [[x, y - s], [x + s * 0.8, y], [x, y + s], [x - s * 0.8, y]], GOLD_DARK, alpha * near);
        quad(r, [[x, y - s * 0.7], [x + s * 0.55, y], [x, y + s * 0.7], [x - s * 0.55, y]], GOLD, alpha * near);
        quad(r, [[x, y - s * 0.35], [x + s * 0.25, y], [x, y + s * 0.2], [x - s * 0.25, y]], GOLD_LIGHT, shine * alpha * near);
    }
}

impl Particles {
    /// `--effect 267` to `269` (tests): a Scepter of the Last Dynasty's decree three tiles right of
    /// us (from 267: the seal, our crown as if Condemned, and the tribute after).
    pub(super) fn test_decree(&mut self, which: u8, world: &World, now: i32) {
        let Some(me) = world.player() else { return };
        let pos1 = bsk_proto::WorldPos { x: me.x as f32 + 3.0, y: me.y as f32 };
        let e = |look: u8, target: i32, duration: i32, object_type: u16| ShowEffect {
            effect_type: 41,
            target_object_id: target,
            pos1,
            pos2: bsk_proto::WorldPos { x: match look { look::CONDEMN => 1.0, look::TRIBUTE => 3.0, _ => 4.5 }, y: look as f32 },
            color: 0xFFFF4A5A,
            duration,
            object_type,
            source_id: Some(me.id),
        };
        match which {
            look::SEAL => {
                self.relic(&e(look::SEAL, me.id, 260, 8), world, now);
                self.delayed.push((now + 260, e(look::CONDEMN, me.id, 3000, 8)));
                self.delayed.push((now + 1800, e(look::TRIBUTE, me.id, 0, 8 + 16)));
            }
            look::CONDEMN => self.relic(&e(look::CONDEMN, me.id, 3000, 8), world, now),
            _ => self.relic(&e(look::TRIBUTE, me.id, 0, 8 + 16), world, now),
        }
    }
}
