//! The Monk's Flowing Strike (the server's `Player.Flow.cs`): `ShowEffect` 41 look 66
//! (`relic::look::FLOW`), sent as the Monk sets off, so the Monk is still where they started: they
//! flow to `pos1` over `duration` ms and a fountain bursts there, `pos2.x` across. `object_type`
//! is the fountain's tier plus 16 for each Chi spent (0 to 5), the colour the tier's
//! (`Player.FlowColors`). What's drawn:
//!
//! - the way: a streak of water along the floor, the Monk's afterimages left along it, spray
//!   flung off its sides and a white slash wherever it passes;
//! - where they land: a column of water shooting up and falling back all round, a shockwave out
//!   to the burst's edge, and under it a lotus of light opening and turning, a petal of gold
//!   lit for each Chi spent;
//! - a Flow (all five spent): the lotus all gold and twice as bright, five orbs of chi flung out
//!   from its heart, a second shockwave and a whirl of gold.

use super::relic::{mix, DecalKind, Pen, GRAVITY};
use super::{Motion, Particle, Particles};
use crate::view::Camera;
use bsk_proto::ShowEffect;
use bsk_render::Renderer;
use bsk_world::World;
use std::f64::consts::PI;

/// The most Chi a strike spends (the server's `Player.ChiMax`).
pub const CHI_MAX: u8 = 5;
/// The chi's own gold, beside the fountain's colour.
pub const CHI_GOLD: u32 = 0xFFD86A;
const FOAM: u32 = 0xEAFBFF;
/// How long the way's streak and the lotus stay, ms.
const WAY_MS: i32 = 520;
const LOTUS_MS: i32 = 1100;
/// The most strikes drawn at once.
const MAX_FLOWS: usize = 24;

/// A strike as it's drawn on the floor: its way and the lotus where it lands.
#[derive(Clone, Copy, Debug)]
pub(super) struct FlowLook {
    start: i32,
    /// When the Monk lands (the burst).
    lands: i32,
    from: (f64, f64),
    to: (f64, f64),
    radius: f64,
    color: u32,
    chi: u8,
    z: f64,
    level: u8,
    dim: f32,
}

impl FlowLook {
    fn flow(&self) -> bool {
        self.chi >= CHI_MAX
    }

    fn until(&self) -> i32 {
        self.lands + LOTUS_MS
    }
}

fn ease(t: f64) -> f64 {
    let t = t.clamp(0.0, 1.0);
    1.0 - (1.0 - t).powi(3)
}

impl Particles {
    /// Look 66 (`relic` hands it here).
    pub(super) fn flow_look(&mut self, e: &ShowEffect, world: &World, now: i32) {
        let to = (e.pos1.x as f64, e.pos1.y as f64);
        let color = e.color & 0xFF_FFFF;
        let chi = ((e.object_type / 16) as u8).min(CHI_MAX);
        let radius = (e.pos2.x as f64).clamp(0.5, 8.0);
        let ms = e.duration.clamp(40, 1000);
        // where the Monk is now, before the server's Goto moves them
        let from = world.objects.get(&e.target_object_id).map_or((to.0 - 1.5, to.1), |o| (o.x, o.y));
        let (z, level) = self.here;
        if self.flows.len() >= MAX_FLOWS {
            self.flows.remove(0);
        }
        if !self.disabled && self.muting < 1.0 {
            self.flows.push(FlowLook { start: now, lands: now + ms, from, to, radius, color, chi, z, level, dim: 1.0 - self.muting });
        }
        self.flow_way(from, to, ms, color);
        self.flow_burst(to, radius, color, chi, ms, now);
    }

    /// Along the way: spray flung off both sides as the Monk passes, a white slash at the
    /// front, motes of their colour hanging behind.
    fn flow_way(&mut self, from: (f64, f64), to: (f64, f64), ms: i32, color: u32) {
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        let dist = dx.hypot(dy);
        if dist < 0.2 {
            return;
        }
        let (ux, uy) = (dx / dist, dy / dist);
        let n = (dist * 5.0).clamp(4.0, 60.0) as usize;
        for i in 0..n {
            let f = (i as f64 + 0.5) / n as f64;
            let (x, y) = (from.0 + dx * f, from.1 + dy * f);
            let delay = (f * ms as f64) as i32;
            for side in [-1.0, 1.0] {
                let s = (0.8 + 0.9 * self.random()) / 1000.0;
                let c = self.pick(&[color, FOAM, mix(color, 0xFFFFFF, 0.5)]);
                let mut p = Particle::new(x, y, 0.3, c, 80.0 + 50.0 * self.random(), 420, Motion::Ballistic { g: GRAVITY * 0.55 })
                    .moving(-uy * side * s - ux * s * 0.3, ux * side * s - uy * s * 0.3, 0.0035 + 0.002 * self.random());
                p.delay = delay;
                p.fade = true;
                self.add(p);
            }
            if i % 2 == 0 {
                let mut p = Particle::new(x + self.pm(0.15), y + self.pm(0.15), 0.4 + 0.3 * self.random(), color, 110.0, 520, Motion::Plain).moving(0.0, 0.0, 0.0004);
                p.delay = delay;
                p.fade = true;
                p.opacity = 0.8;
                self.add(p);
            }
            // the slash at the front
            let mut s = Particle::new(x, y, 0.5, 0xFFFFFF, 200.0, 90, Motion::Spark);
            s.delay = delay;
            self.add(s);
        }
    }

    /// The fountain bursting at `at` once the Monk lands (`ms` on), `r` across, with `chi` spent.
    fn flow_burst(&mut self, at: (f64, f64), r: f64, color: u32, chi: u8, ms: i32, now: i32) {
        let (x, y) = at;
        let flow = chi >= CHI_MAX;
        let boost = 1.0 + 0.2 * chi as f64;
        let light = mix(color, 0xFFFFFF, 0.55);
        // the flash
        for (size, c, life) in [(1100.0 * boost, if flow { CHI_GOLD } else { color }, 200), (650.0 * boost, 0xFFFFFF, 120)] {
            let mut p = Particle::new(x, y, 0.4, c, size, life, Motion::Spark);
            p.delay = ms;
            self.add(p);
        }
        self.decal(DecalKind::Ring, x, y, r * 1.1, light, now + ms, 420, None);
        self.decal(DecalKind::Ring, x, y, r * 0.65, color, now + ms + 70, 380, None);
        self.decal(DecalKind::Puddle, x, y, r * 0.6, color, now + ms, 800, None);
        // the column of water: thrown high from its heart, falling back all round
        let n = 34 + 6 * chi as usize;
        for i in 0..n {
            let a = 2.0 * PI * i as f64 / n as f64 + self.pm(0.15);
            let out = (r * (0.35 + 0.4 * self.random())) / 900.0;
            let up = (0.008 + 0.006 * self.random()) * (0.85 + 0.15 * boost);
            let c = [color, FOAM, light, 0xFFFFFF][i % 4];
            let mut p = Particle::new(x + 0.15 * a.cos(), y + 0.15 * a.sin(), 0.2, c, 110.0 + 70.0 * self.random(), 900 + (self.random() * 300.0) as i32, Motion::Ballistic { g: GRAVITY * 0.7 })
                .moving(a.cos() * out, a.sin() * out, up);
            p.delay = ms + (i as i32 % 5) * 12;
            p.fade = true;
            self.add(p);
        }
        // a low wave of spray racing out along the floor
        for i in 0..24 {
            let a = 2.0 * PI * i as f64 / 24.0;
            let s = r / 380.0;
            let mut p = Particle::new(x, y, 0.1, self.pick(&[FOAM, color]), 90.0, 380, Motion::Ballistic { g: GRAVITY * 0.3 }).moving(a.cos() * s, a.sin() * s, 0.002);
            p.delay = ms;
            p.fade = true;
            self.add(p);
        }
        // a mote of gold for each Chi spent, rising from the burst
        for i in 0..chi as usize {
            let a = 2.0 * PI * i as f64 / chi.max(1) as f64;
            let mut p = Particle::new(x + 0.6 * a.cos(), y + 0.6 * a.sin(), 0.3, CHI_GOLD, 170.0, 700, Motion::Rising { acc: -1.0 }).moving(0.0, 0.0, 0.003);
            p.delay = ms + 40 * i as i32;
            p.fade = true;
            self.add(p);
        }
        if flow {
            // five orbs of chi flung out, trailing sparks, and a second shockwave
            for i in 0..5 {
                let a = 2.0 * PI * i as f64 / 5.0 - PI / 2.0;
                let s = r * 1.1 / 450.0;
                let mut p = Particle::new(x, y, 0.5, CHI_GOLD, 260.0, 460, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
                p.delay = ms;
                self.add(p);
            }
            self.decal(DecalKind::Ring, x, y, r * 1.5, CHI_GOLD, now + ms + 140, 520, None);
            self.decal(DecalKind::Whirlpool, x, y, r * 0.9, CHI_GOLD, now + ms, 900, None);
            self.debris(x, y, 16, &[CHI_GOLD, 0xFFFFFF, light], 90.0, 1.3, 10.0, 900, ms);
        }
    }

    pub(super) fn flow_update(&mut self, now: i32) {
        self.flows.retain(|f| now < f.until());
    }

    /// The strikes on floor `level` (0 the map): their ways and lotuses.
    pub(super) fn draw_flows(&self, cam: &Camera, renderer: &mut Renderer, level: u8) {
        let now = self.now;
        for f in self.flows.iter().filter(|f| f.level == level && now >= f.start) {
            draw_way(cam, renderer, f, now);
            if now >= f.lands {
                let mut pen = Pen { cam, renderer: &mut *renderer, x: f.to.0, y: f.to.1, z: f.z, dim: f.dim };
                draw_lotus(&mut pen, f, now);
            }
        }
    }
}

/// The way: a streak of water narrowing behind the Monk, and three afterimages of them standing
/// along it, fading from the oldest.
fn draw_way(cam: &Camera, renderer: &mut Renderer, f: &FlowLook, now: i32) {
    let t = (now - f.start) as f64;
    let travel = (f.lands - f.start).max(1) as f64;
    let (dx, dy) = (f.to.0 - f.from.0, f.to.1 - f.from.1);
    let dist = dx.hypot(dy);
    if dist < 0.2 || t > travel + WAY_MS as f64 {
        return;
    }
    let head = (t / travel).min(1.0);
    let fade = (1.0 - ((t - travel).max(0.0) / WAY_MS as f64)) as f32;
    let mut pen = Pen { cam, renderer: &mut *renderer, x: f.from.0, y: f.from.1, z: f.z, dim: f.dim };
    let end = (dx * head, dy * head);
    // the streak, in slices narrowing toward where they set off
    let slices = 12;
    for i in 0..slices {
        let (a, b) = (i as f64 / slices as f64, (i + 1) as f64 / slices as f64);
        let w = 0.15 + 0.75 * b;
        let p0 = (end.0 * a, end.1 * a);
        let p1 = (end.0 * b, end.1 * b);
        pen.line(p0, p1, w, f.color, 0.28 * b as f32 * fade);
        pen.line(p0, p1, w * 0.35, FOAM, 0.45 * b as f32 * fade);
    }
    // the afterimages: a body and a head of light where they were
    for k in 1..=3 {
        let at = head - 0.22 * k as f64;
        if at <= 0.0 {
            continue;
        }
        let (ax, ay) = (dx * at, dy * at);
        let a = (0.55 - 0.13 * k as f32) * fade;
        let mut ghost = Pen { cam, renderer: &mut *renderer, x: f.from.0 + ax, y: f.from.1 + ay, z: f.z + 0.3, dim: f.dim };
        ghost.disc(0.3, f.color, a * 0.6);
        ghost.disc(0.18, 0xFFFFFF, a * 0.35);
        let mut top = Pen { cam, renderer: &mut *renderer, x: f.from.0 + ax, y: f.from.1 + ay, z: f.z + 0.72, dim: f.dim };
        top.disc(0.17, f.color, a * 0.7);
    }
}

/// Where they landed: a lotus of light opening out to the burst's edge and turning, eight petals,
/// a petal of gold for each Chi spent (all gold for a Flow), rings round its heart.
fn draw_lotus(pen: &mut Pen, f: &FlowLook, now: i32) {
    let t = (now - f.lands) as f64;
    let open = ease(t / 220.0);
    let fade = (1.0 - ((t - (LOTUS_MS as f64 - 350.0)).max(0.0) / 350.0)) as f32;
    let r = f.radius * open;
    let spin = t / 1000.0 * if f.flow() { 1.1 } else { 0.6 };
    let flow = f.flow();
    let k = if flow { 1.4 } else { 1.0 };
    // the water under it
    pen.disc(r, mix(f.color, 0x081828, 0.5), 0.22 * fade);
    // its petals: long kites from the heart to the edge
    for i in 0..8 {
        let a = spin + i as f64 * PI / 4.0;
        let gold = flow || (i as u8) < f.chi;
        let c = if gold { CHI_GOLD } else { f.color };
        let kite = [pen.at(r * 0.12, a), pen.at(r * 0.55, a - 0.32), pen.at(r * 0.98, a), pen.at(r * 0.55, a + 0.32)];
        pen.quad(kite, c, (0.32 * k) as f32 * fade);
        let inner = [pen.at(r * 0.2, a), pen.at(r * 0.52, a - 0.15), pen.at(r * 0.85, a), pen.at(r * 0.52, a + 0.15)];
        pen.quad(inner, mix(c, 0xFFFFFF, 0.6), (0.4 * k) as f32 * fade);
    }
    // a second ring of petals, smaller, between them, turning the other way
    for i in 0..8 {
        let a = -spin + (i as f64 + 0.5) * PI / 4.0;
        let kite = [pen.at(r * 0.1, a), pen.at(r * 0.35, a - 0.3), pen.at(r * 0.62, a), pen.at(r * 0.35, a + 0.3)];
        pen.quad(kite, if flow { 0xFFFFFF } else { mix(f.color, 0xFFFFFF, 0.3) }, 0.25 * fade);
    }
    // rings: the edge, and a ripple running out
    pen.band(r * 0.95, r, mix(f.color, 0xFFFFFF, 0.5), 0.5 * fade);
    let ph = (t / 450.0).fract();
    pen.band(r * (0.2 + 0.8 * ph) - 0.06, r * (0.2 + 0.8 * ph), FOAM, (0.5 * (1.0 - ph)) as f32 * fade);
    // its heart
    pen.disc(0.35 * open, if flow { CHI_GOLD } else { f.color }, 0.6 * fade);
    pen.disc(0.18 * open, 0xFFFFFF, 0.85 * fade);
}

impl Particles {
    /// `--effect 266` (tests): a Flow from three tiles left of us to us, with all five Chi spent,
    /// as a Heart of the Fountains'.
    pub(super) fn test_flow(&mut self, world: &World, now: i32) {
        let Some(me) = world.player() else { return };
        let e = ShowEffect {
            effect_type: 41,
            target_object_id: -1,
            pos1: bsk_proto::WorldPos { x: me.x as f32, y: me.y as f32 },
            pos2: bsk_proto::WorldPos { x: 3.5, y: super::relic::look::FLOW as f32 },
            color: 0xFFFFF4D6,
            duration: 160,
            object_type: 8 + 16 * CHI_MAX as u16,
            source_id: Some(me.id),
        };
        self.relic(&e, world, now);
    }
}
