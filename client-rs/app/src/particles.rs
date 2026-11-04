//! Particles (`objects/particles`): the sparks of projectile hits and deaths, the server's
//! effects (`ShowEffect`: heals, novas, thrown grenades, lightning...) and area attacks (`Aoe`).
//!
//! The AIR client adds each effect to the map as an object that spawns particles; here an
//! effect spawns its particles at once into one list. A particle is a small square of colour
//! with a dark outline (`TextureRedrawer.redrawSolidSquare`), standing `z` tiles above its spot.
//! Sizes are the AIR client's (`size / 100 × 5` pixels at 50 pixels a tile) and scale with the map.

use crate::sprites::Sprites;
use crate::view::Camera;
use bsk_data::{GameData, TextureData, TextureRef};
use bsk_proto::ShowEffect;
use bsk_render::{AtlasRect, Quad, Renderer, Tint};
use bsk_world::{Impact, World};
use std::collections::HashMap;
use std::f64::consts::PI;

#[derive(Clone, Debug)]
enum Motion {
    /// Moves by its velocity.
    Plain,
    /// Moves, shrinking as it ages (`SparkParticle`).
    Spark,
    /// Moves, leaving sparks behind (`SparkerParticle`).
    Sparker,
    /// Arcs up and down while it flies, leaving sparks; not drawn itself (`ThrowParticle`).
    Throw,
    /// Stays beside an object as it rises (`HealParticle`).
    Follow { id: i32, angle: f64, dist: f64 },
    /// Drifts along a line, swaying (`StreamParticle`).
    Stream { px: f64, py: f64, xd: f64, yd: f64, period: f64 },
    /// Speeds toward an object until it reaches it (`FlowParticle`).
    Flow { id: i32, max_dist: f64, speed: f64 },
    /// Moves, its rise changing by `acc` tiles a second each second (`ParticleGenerator`).
    Rising { acc: f64 },
    /// Spirals in on a point as it sinks (`VortexParticle`), growing to `max_size` pixels.
    Vortex { cx: f64, cy: f64, ax: f64, ay: f64, max_size: f64 },
    /// Circles a point, the circle widening to `max_rad` (`CircleParticle`); `speed` is
    /// radians a frame at 60 frames a second.
    Orbit { cx: f64, cy: f64, angle: f64, speed: f64, rad: f64, rise: f64, max_rad: f64 },
}

/// A particle drawn as a picture: its frames (none for a blank one), each with its size in
/// pixels at 50 a tile and how long it shows, ms; standing on its spot, or centred on it.
#[derive(Clone, Debug)]
struct Image {
    frames: Vec<(Option<AtlasRect>, f32, f32, i32)>,
    standing: bool,
}

#[derive(Clone, Debug)]
struct Particle {
    x: f64,
    y: f64,
    z: f64,
    /// Tiles a millisecond.
    vx: f64,
    vy: f64,
    vz: f64,
    color: u32,
    /// The AIR client's size: `size / 100 × 5` pixels.
    size: f64,
    life: i32,
    left: i32,
    motion: Motion,
    image: Option<Image>,
    /// How opaque it's drawn (a pet's shot's sparks are as faint as the shot).
    alpha: f32,
}

impl Particle {
    fn new(x: f64, y: f64, z: f64, color: u32, size: f64, life: i32, motion: Motion) -> Self {
        Particle { x, y, z, vx: 0.0, vy: 0.0, vz: 0.0, color, size, life, left: life, motion, image: None, alpha: 1.0 }
    }

    fn with_image(mut self, image: Option<Image>) -> Self {
        self.image = image;
        self
    }

    fn moving(mut self, vx: f64, vy: f64, vz: f64) -> Self {
        (self.vx, self.vy, self.vz) = (vx, vy, vz);
        self
    }
}

/// `FlashDescription`: an object tinted toward a colour, pulsing.
#[derive(Clone, Copy, Debug)]
struct Flash {
    start: i32,
    color: u32,
    period: i32,
    repeats: i32,
}

/// A hit whose colours come from a texture, worked out when the sprites are at hand.
#[derive(Clone, Debug)]
struct Pending {
    impact: Impact,
    /// The struck object's type and size, if it was an object.
    target: Option<(u16, i32)>,
    /// How opaque its sparks are.
    alpha: f32,
}

/// Every live particle, and the effects on objects and the camera.
#[derive(Default)]
pub struct Particles {
    list: Vec<Particle>,
    pending: Vec<Pending>,
    /// Texture colours by object type (`BloodComposition`), and by projectile look.
    colors: HashMap<u16, Vec<u32>>,
    flashes: HashMap<i32, Flash>,
    /// `Camera.startJitter`: when the shaking started.
    jitter_since: Option<i32>,
    rng: u64,
    /// Options: particles off.
    pub disabled: bool,
    /// Options: other players' pets' effects off.
    pub hide_pets: bool,
    /// Options: how pets' shots look, and whether other players' pets' show.
    pub pet_shots: (crate::options::PetShots, bool),
    /// The last heal burst (`ShowEffect` 1): when, its colour, and its particles in `list`. A
    /// pet's heal or strike sends one beside the line from the pet, so it goes with that line.
    last_burst: Option<(i32, u32, std::ops::Range<usize>)>,
    /// A hidden pet's line (when, colour): the burst that follows it is hidden too.
    pet_line: Option<(i32, u32)>,
    /// Objects' own effects (`<Effect>`), by object id.
    emitters: HashMap<i32, Emitter>,
}

/// An object's effect at work.
#[derive(Clone, Copy, Debug)]
enum Emitter {
    /// `ParticleGenerator`: seconds running, particles made.
    Custom { total: f64, made: i64 },
    /// `XMLEffect`: seconds to the next particle.
    Xml { wait: f64 },
    /// `FountainEffect`, `VentEffect`, `VortexEffect`, `SkyBeamEffect`: a particle every
    /// `period` seconds; seconds to the next, and (sky beam) the radius they fall in.
    Steady { wait: f64, period: f64, radius: f64 },
    /// One-off effects (`CircleEffect`, `ExplosionComplexEffect`), already given off.
    Done,
}

/// How far from us objects' effects run, tiles.
const EFFECT_RANGE: f64 = 22.0;

/// At most this many particles at once, so a busy fight stays smooth.
const MAX_PARTICLES: usize = 6000;

impl Particles {
    /// A random number in [0, 1).
    fn random(&mut self) -> f64 {
        if self.rng == 0 {
            self.rng = 0x9E37_79B9_7F4A_7C15;
        }
        self.rng ^= self.rng << 13;
        self.rng ^= self.rng >> 7;
        self.rng ^= self.rng << 17;
        (self.rng >> 11) as f64 / (1u64 << 53) as f64
    }

    /// `RandomUtil.plusMinus`.
    fn pm(&mut self, range: f64) -> f64 {
        self.random() * range * 2.0 - range
    }

    fn add(&mut self, p: Particle) {
        if !self.disabled && self.list.len() < MAX_PARTICLES {
            self.list.push(p);
        }
    }

    /// A new world: nothing carries over.
    pub fn clear(&mut self) {
        self.list.clear();
        self.pending.clear();
        self.flashes.clear();
        self.emitters.clear();
        self.jitter_since = None;
    }

    /// `SparkerParticle`s from `start` to `end`.
    fn sparker(&mut self, start: (f64, f64), end: (f64, f64), color: u32, size: f64, life: i32) {
        let life = life.max(1);
        let (vx, vy) = ((end.0 - start.0) / life as f64, (end.1 - start.1) / life as f64);
        self.add(Particle::new(start.0, start.1, 0.0, color, size, life, Motion::Sparker).moving(vx, vy, 0.0));
    }

    #[allow(clippy::too_many_arguments)]
    /// `SparkParticle`: drifting `dx`, `dy` tiles a second, shrinking.
    fn spark(&mut self, x: f64, y: f64, z: f64, color: u32, size: f64, life: i32, dx: f64, dy: f64) {
        self.add(Particle::new(x, y, z, color, size, life, Motion::Spark).moving(dx / 1000.0, dy / 1000.0, 0.0));
    }

    /// `AOEEffect` (and `NovaEffect`): sparkers out to the edge of a circle.
    fn nova(&mut self, x: f64, y: f64, radius: f64, color: u32, size: f64) {
        let n = (4.0 + radius * 2.0) as i32;
        for i in 0..n {
            let a = i as f64 * 2.0 * PI / n as f64;
            self.sparker((x, y), (x + radius * a.cos(), y + radius * a.sin()), color, size, 200);
        }
    }

    /// `Aoe`: the blast of an area attack.
    pub fn aoe(&mut self, x: f64, y: f64, radius: f64, color: u32) {
        self.nova(x, y, radius, color & 0xFF_FFFF, 200.0);
    }

    /// Projectiles struck something (the world's `impacts`): sparks once the colours are known.
    pub fn impacts(&mut self, world: &mut World) {
        for impact in world.impacts.drain(..) {
            let target = impact.target.and_then(|id| world.objects.get(&id)).map(|o| (o.object_type, o.stat_int(2).unwrap_or(100)));
            if impact.target.is_some() && target.is_none() {
                continue;
            }
            let alpha = match impact.pet_owner {
                Some(owner) if Some(owner) == world.me => self.pet_shots.0.look(true).1,
                Some(_) if self.pet_shots.1 => self.pet_shots.0.look(false).1,
                Some(_) => 0.0,
                None => 1.0,
            };
            if alpha > 0.0 {
                self.pending.push(Pending { impact, target, alpha });
            }
        }
    }

    /// `Damage` from the server for someone else's hit: an explosion of the target's colours.
    pub fn damaged(&mut self, world: &World, target_id: i32, kill: bool) {
        let Some(o) = world.objects.get(&target_id) else { return };
        let impact = Impact { x: o.x, y: o.y, target: Some(target_id), kill, look: String::new(), angle: 0.0, speed: 0, pet_owner: None };
        self.pending.push(Pending { impact, target: Some((o.object_type, o.stat_int(2).unwrap_or(100))), alpha: 1.0 });
    }

    /// Turn the hits waiting for their colours into sparks (`HitEffect`, `ExplosionEffect`).
    pub fn realize(&mut self, sprites: &Sprites, data: &GameData) {
        for Pending { impact, target, alpha } in std::mem::take(&mut self.pending) {
            let (colors, size, count) = match target {
                Some((t, size)) => (self.blood(sprites, data, t), size as f64, if impact.kill { 30 } else { 10 }),
                None => {
                    let Some(&t) = data.object_ids.get(&impact.look) else { continue };
                    (self.texture_colors(sprites, data, t), 100.0, 3)
                }
            };
            if colors.is_empty() {
                continue;
            }
            let explode = impact.kill || impact.speed == 0;
            for _ in 0..count {
                let color = colors[(self.random() * colors.len() as f64) as usize % colors.len()];
                let life = 200 + (self.random() * 100.0) as i32;
                let (vx, vy) = if explode {
                    (self.random() - 0.5, self.random() - 0.5)
                } else {
                    // Back the way the projectile came (`HitEffect`).
                    let s = impact.speed as f64 / 600.0;
                    (s * (impact.angle + PI).cos() + (self.random() - 0.5) * 0.4, s * (impact.angle + PI).sin() + (self.random() - 0.5) * 0.4)
                };
                let mut p = Particle::new(impact.x, impact.y, 0.5, color, size, life, Motion::Plain).moving(vx * 0.008, vy * 0.008, 0.0);
                p.alpha = alpha;
                self.add(p);
            }
        }
    }

    /// `BloodComposition.getBloodComposition`: the texture's colours, some swapped for blood.
    fn blood(&mut self, sprites: &Sprites, data: &GameData, object_type: u16) -> Vec<u32> {
        let colors = self.texture_colors(sprites, data, object_type);
        let Some(d) = data.objects.get(&object_type) else { return colors };
        let prob: f64 = d.xml.text_of("BloodProb").and_then(|p| p.trim().parse().ok()).unwrap_or(0.0);
        if prob <= 0.0 {
            return colors;
        }
        let blood = d.xml.text_of("BloodColor").map_or(0xFF0000, |c| bsk_data::xml::as3_int(c) as u32) & 0xFF_FFFF;
        (0..colors.len()).map(|i| if self.random() < prob { blood } else { colors[i] }).collect()
    }

    /// Every opaque pixel's colour in an object's texture (`BloodComposition.getColors`).
    fn texture_colors(&mut self, sprites: &Sprites, data: &GameData, object_type: u16) -> Vec<u32> {
        if let Some(c) = self.colors.get(&object_type) {
            return c.clone();
        }
        let colors = data.objects.get(&object_type).map_or_else(Vec::new, |d| pixels(sprites, &d.texture));
        self.colors.insert(object_type, colors.clone());
        colors
    }

    /// `GameServerConnection.onShowEffect`.
    pub fn show_effect(&mut self, e: &ShowEffect, world: &World, now: i32) {
        let color = e.color & 0xFF_FFFF;
        let at = |id: i32| world.objects.get(&id).map(|o| (o.x, o.y));
        let (p1, p2) = ((e.pos1.x as f64, e.pos1.y as f64), (e.pos2.x as f64, e.pos2.y as f64));
        let go = at(e.target_object_id);
        // Other players' pets' effects (Doomed pets: PetHeal, PetStrike): the line from the pet,
        // and the burst on whoever it healed or hit, sent with it.
        if self.hide_pets {
            if e.effect_type == 7 && world.is_others_pet(e.target_object_id) {
                if let Some((_, _, range)) = self.last_burst.take().filter(|b| b.0 == now && b.1 == color) {
                    if range.end <= self.list.len() {
                        self.list.drain(range);
                    }
                }
                self.pet_line = Some((now, color));
                return;
            }
            if e.effect_type == 1 && self.pet_line.take() == Some((now, color)) {
                return;
            }
        }
        match e.effect_type {
            // HealEffect: ten rising squares around the object.
            1 => {
                let Some(_) = go else { return };
                let start = self.list.len();
                for i in 0..10 {
                    let angle = 2.0 * PI * i as f64 / 10.0;
                    let size = (3 + (self.random() * 5.0) as i32) as f64 * 20.0;
                    let dist = 0.3 + 0.4 * self.random();
                    let (z, vz) = (self.random() * 0.3, 0.1 + self.random() * 0.1);
                    let motion = Motion::Follow { id: e.target_object_id, angle, dist };
                    self.add(Particle::new(0.0, 0.0, z, color, size, 1000, motion).moving(0.0, 0.0, vz * 0.008));
                }
                self.last_burst = Some((now, color, start..self.list.len()));
            }
            // TeleportEffect: blue squares rising where it happened.
            2 => {
                for _ in 0..20 {
                    let (angle, dist) = (2.0 * PI * self.random(), 0.7 * self.random());
                    let life = 500 + (1000.0 * self.random()) as i32;
                    let p = Particle::new(p1.0 + dist * angle.cos(), p1.1 + dist * angle.sin(), 0.0, 0x0000FF, 50.0, life, Motion::Plain);
                    self.add(p.moving(0.0, 0.0, 0.1 * 0.008));
                }
            }
            // StreamEffect: squares drifting from pos1 to pos2.
            3 => {
                for _ in 0..5 {
                    let size = (3 + (self.random() * 5.0) as i32) as f64 * 20.0;
                    let life = 1500 + (self.random() * 3000.0) as i32;
                    let (dx, dy) = ((p2.0 - p1.0) / life as f64, (p2.1 - p1.1) / life as f64);
                    let speed = (dx * dx + dy * dy).sqrt().max(1e-9);
                    let motion = Motion::Stream { px: p1.0, py: p1.1, xd: dy / speed * 0.25, yd: -dx / speed * 0.25, period: 0.25 + self.random() * 0.5 };
                    self.add(Particle::new(p1.0, p1.1, 1.85, color, size, life, motion).moving(dx, dy, 0.0));
                }
            }
            // ThrowEffect: a grenade's arc from the thrower to where it lands.
            4 => {
                let start = go.unwrap_or(p2);
                let life = if e.duration == 0 { 1500 } else { e.duration * 1000 };
                let (vx, vy) = ((p1.0 - start.0) / life as f64, (p1.1 - start.1) / life as f64);
                self.add(Particle::new(start.0, start.1, 0.0, color, 0.0, life, Motion::Throw).moving(vx, vy, 0.0));
            }
            // NovaEffect: radius in pos1.x.
            5 => {
                if let Some((x, y)) = go {
                    self.nova(x, y, p1.0, color, 10.0);
                }
            }
            // PoisonEffect: sparks bursting off the object.
            6 => {
                if let Some((x, y)) = go {
                    for _ in 0..10 {
                        let (dx, dy) = (self.pm(4.0), self.pm(4.0));
                        self.spark(x, y, 0.75, color, 100.0, 400, dx, dy);
                    }
                }
            }
            // LineEffect: sparks along the line from the object to pos1.
            7 => {
                if let Some((x, y)) = go {
                    for i in 0..30 {
                        let t = i as f64 / 30.0;
                        let (dx, dy) = (self.pm(1.0), self.pm(1.0));
                        self.spark(x + (p1.0 - x) * t, y + (p1.1 - y) * t, 0.5, color, 100.0, 700, dx, dy);
                    }
                }
            }
            // BurstEffect: from pos1 out to the circle through pos2.
            8 => {
                if go.is_some() {
                    let r = (p2.0 - p1.0).hypot(p2.1 - p1.1);
                    for i in 0..24 {
                        let a = i as f64 * 2.0 * PI / 24.0;
                        let life = 100 + (self.random() * 200.0) as i32;
                        self.sparker(p1, (p1.0 + r * a.cos(), p1.1 + r * a.sin()), color, 100.0, life);
                    }
                }
            }
            // FlowEffect: squares speeding from pos1 to the object.
            9 => {
                if let Some((x, y)) = go {
                    for _ in 0..5 {
                        let size = (3 + (self.random() * 5.0) as i32) as f64 * 20.0;
                        let max_dist = (x - p1.0).hypot(y - p1.1);
                        let motion = Motion::Flow { id: e.target_object_id, max_dist, speed: self.random() * 5.0 };
                        self.add(Particle::new(p1.0, p1.1, 0.5, color, size, 10_000, motion));
                    }
                }
            }
            // RingEffect: a thin ring of radius pos1.x.
            10 => {
                if let Some((x, y)) = go {
                    let r = p1.0;
                    for i in 0..12 {
                        let a = i as f64 * 2.0 * PI / 12.0;
                        let (c, s) = (a.cos(), a.sin());
                        self.sparker((x + r * 0.9 * c, y + r * 0.9 * s), (x + r * c, y + r * s), color, 0.0, 200);
                    }
                }
            }
            // LightningEffect: a jagged line of sparks to pos1, pos2.x their size.
            11 => {
                if let Some((x, y)) = go {
                    let dist = (p1.0 - x).hypot(p1.1 - y);
                    let n = ((dist * 3.0) as i32).max(1);
                    for i in 0..n {
                        let t = i as f64 / n as f64;
                        let k = i.min(n - i) as f64;
                        let (jx, jy) = (self.pm(dist / 200.0 * k), self.pm(dist / 200.0 * k));
                        let life = (1000.0 - t * 900.0) as i32;
                        self.spark(x + (p1.0 - x) * t + jx, y + (p1.1 - y) * t + jy, 0.5, color, p2.0, life, 0.0, 0.0);
                    }
                }
            }
            // CollapseEffect: from the circle through pos2 into pos1.
            12 => {
                if go.is_some() {
                    let r = (p2.0 - p1.0).hypot(p2.1 - p1.1);
                    for i in 0..24 {
                        let a = i as f64 * 2.0 * PI / 24.0;
                        self.sparker((p1.0 + r * a.cos(), p1.1 + r * a.sin()), p1, color, 300.0, 200);
                    }
                }
            }
            // ConeBlastEffect: a 60° fan toward pos1, radius pos2.x.
            13 => {
                if let Some((x, y)) = go {
                    let aim = (p1.1 - y).atan2(p1.0 - x);
                    let arc = PI / 3.0;
                    for i in 0..7 {
                        let a = aim - arc / 2.0 + i as f64 * arc / 7.0;
                        self.sparker((x, y), (x + p2.0 * a.cos(), y + p2.0 * a.sin()), color, 200.0, 100);
                    }
                }
            }
            // The camera shakes (`Camera.startJitter`).
            14 => {
                self.jitter_since.get_or_insert(now);
            }
            // FlashDescription: period pos1.x seconds, pos1.y times.
            15 if go.is_some() => {
                let flash = Flash { start: now, color, period: ((p1.0 * 1000.0) as i32).max(1), repeats: p1.1 as i32 };
                self.flashes.insert(e.target_object_id, flash);
            }
            // ThrowProjectileEffect: an object thrown in an arc; drawn as a throw.
            16 => {
                let life = if e.duration > 0 { e.duration } else { 1000 };
                let (vx, vy) = ((p1.0 - p2.0) / life as f64, (p1.1 - p2.1) / life as f64);
                self.add(Particle::new(p2.0, p2.1, 0.0, 0xFFFFFF, 0.0, life, Motion::Throw).moving(vx, vy, 0.0));
            }
            // InspireEffect: the object flashes gold.
            17 if go.is_some() => {
                let flash = Flash { start: now, color, period: ((p2.0 * 1000.0) as i32).max(1), repeats: (p2.1 as i32).max(1) };
                self.flashes.insert(e.target_object_id, flash);
            }
            _ => {}
        }
    }

    /// `--effect <type>` (tests): show that effect on us (0: an area attack beside us).
    pub fn test_effect(&mut self, effect_type: u8, world: &World, now: i32) {
        let Some(me) = world.player() else { return };
        let (x, y) = (me.x as f32, me.y as f32);
        if effect_type == 0 {
            self.aoe(me.x + 2.0, me.y, 2.0, 0xFF0000);
            return;
        }
        let e = ShowEffect {
            effect_type,
            target_object_id: me.id,
            pos1: bsk_proto::WorldPos { x: x + 3.0, y: y - 2.0 },
            pos2: bsk_proto::WorldPos { x: if matches!(effect_type, 11 | 13) { 3.0 } else { x + 1.5 }, y },
            color: 0xFF40C0FF,
            duration: 0,
            object_type: 0,
        };
        let e = match effect_type {
            5 | 10 => ShowEffect { pos1: bsk_proto::WorldPos { x: 3.0, y: 0.0 }, ..e },
            8 | 12 => ShowEffect { pos1: bsk_proto::WorldPos { x, y }, pos2: bsk_proto::WorldPos { x: x + 2.5, y }, ..e },
            15 => ShowEffect { pos1: bsk_proto::WorldPos { x: 0.5, y: 4.0 }, ..e },
            _ => e,
        };
        self.show_effect(&e, world, now);
    }

    /// Move every particle `dt` ms on; drop the finished ones.
    pub fn update(&mut self, world: &World, now: i32, dt: i32) {
        let dt = dt.max(0);
        let mut spawned = Vec::new();
        let mut list = std::mem::take(&mut self.list);
        list.retain_mut(|p| {
            p.left -= dt;
            if p.left <= 0 {
                return false;
            }
            let d = dt as f64;
            match p.motion {
                Motion::Plain | Motion::Spark => {
                    p.x += p.vx * d;
                    p.y += p.vy * d;
                    p.z += p.vz * d;
                }
                Motion::Rising { acc } => {
                    p.vz += acc / 1000.0 * d / 1000.0;
                    p.x += p.vx * d;
                    p.y += p.vy * d;
                    p.z += p.vz * d;
                }
                Motion::Vortex { cx, cy, ax, ay, max_size } => {
                    let to = (cx - p.x).atan2(cy - p.y);
                    p.x += (to.sin() * 4.0 + ax) * d * 0.0006;
                    p.y += (to.cos() * 4.0 + ay) * d * 0.0006;
                    p.z -= 0.5 * d * 0.0006;
                    // Its size grows in pixels; the AIR units are 20 a pixel.
                    p.size = (p.size + d * 0.01 * 20.0).min(max_size * 20.0);
                    if p.z <= 0.0 {
                        return false;
                    }
                }
                Motion::Orbit { cx, cy, ref mut angle, speed, ref mut rad, rise, max_rad } => {
                    p.x = cx + *rad * angle.cos();
                    p.y = cy + *rad * angle.sin();
                    *angle += speed * d / (1000.0 / 60.0);
                    *rad = (*rad + rise * d / 1000.0).min(max_rad);
                }
                Motion::Sparker => {
                    p.x += p.vx * d;
                    p.y += p.vy * d;
                    spawned.push((p.x, p.y, p.z, p.color, 100.0 * (p.z + 1.0), 600));
                }
                Motion::Throw => {
                    p.z = (p.left as f64 / p.life as f64 * PI).sin() * 2.0;
                    p.x += p.vx * d;
                    p.y += p.vy * d;
                    spawned.push((p.x, p.y, p.z, p.color, 100.0 * (p.z + 1.0), 400));
                }
                Motion::Follow { id, angle, dist } => {
                    let Some(o) = world.objects.get(&id) else { return false };
                    p.x = o.x + dist * angle.cos();
                    p.y = o.y + dist * angle.sin();
                    p.z += p.vz * d;
                }
                Motion::Stream { ref mut px, ref mut py, xd, yd, period } => {
                    *px += p.vx * d;
                    *py += p.vy * d;
                    let f = (p.left as f64 / 1000.0 / period).sin();
                    p.x = *px + xd * f;
                    p.y = *py + yd * f;
                }
                Motion::Flow { id, ref mut max_dist, ref mut speed } => {
                    let Some(o) = world.objects.get(&id) else { return false };
                    let to = (o.x - p.x).hypot(o.y - p.y);
                    if to < 0.5 {
                        return false;
                    }
                    *speed += 8.0 * d / 1000.0;
                    *max_dist -= *speed * d / 1000.0;
                    let target = (to - *speed * d / 1000.0).min(*max_dist).max(0.0);
                    let k = target / to;
                    p.x = o.x - (o.x - p.x) * k;
                    p.y = o.y - (o.y - p.y) * k;
                }
            }
            true
        });
        self.list = list;
        for (x, y, z, color, size, life) in spawned {
            let (dx, dy) = (self.pm(1.0), self.pm(1.0));
            self.spark(x, y, z, color, size, life, dx, dy);
        }
        // In i64: a flash that repeats "forever" (a huge count) overflowed i32.
        self.flashes.retain(|_, f| now as i64 <= f.start as i64 + f.period as i64 * f.repeats as i64);
    }

    /// Objects' own effects (`ParticleEffect.fromProps`): start them for objects near us, give
    /// off what's due, and forget objects that have gone.
    pub fn object_effects(&mut self, world: &World, data: &GameData, sprites: &Sprites, dt: i32) {
        let Some(me) = world.player() else { return };
        let (mx, my) = (me.x, me.y);
        let secs = dt.max(0) as f64 / 1000.0;
        self.emitters.retain(|id, _| world.objects.get(id).is_some_and(|o| (o.x - mx).hypot(o.y - my) <= EFFECT_RANGE + 4.0));
        if self.disabled {
            return;
        }
        let near: Vec<(i32, f64, f64, &bsk_data::EffectProps, f64)> = world
            .objects
            .values()
            .filter(|o| (o.x - mx).hypot(o.y - my) <= EFFECT_RANGE)
            .filter(|o| !(self.hide_pets && world.others_pet(o)))
            .filter_map(|o| {
                let d = data.objects.get(&o.object_type)?;
                d.effect.as_ref().map(|e| (o.id, o.x, o.y, e, d.z))
            })
            .collect();
        for (id, x, y, e, z) in near {
            let state = match self.emitters.get(&id) {
                Some(s) => *s,
                None => {
                    let s = match e.id.as_str() {
                        "CustomParticles" => Emitter::Custom { total: 0.0, made: 0 },
                        "XMLEffect" => Emitter::Xml { wait: 0.0 },
                        "Fountain" | "FountainSnowy" | "Vent" | "Vortex" => Emitter::Steady { wait: 0.0, period: 0.05, radius: 0.0 },
                        "SkyBeam" => Emitter::Steady { wait: 0.0, period: 0.03, radius: e.min_radius },
                        _ => Emitter::Done,
                    };
                    self.emitters.insert(id, s);
                    // One-off effects go off when the object first comes near.
                    if matches!(s, Emitter::Done) {
                        self.one_off(x, y, e);
                    }
                    s
                }
            };
            match state {
                Emitter::Custom { total, made } => {
                    let total = total + secs;
                    let due = (e.rate * total) as i64 - made;
                    let image = e.bitmap.as_ref().and_then(|(set, index)| still(sprites, set, *index, e.size as f32 / 100.0 * 5.0));
                    for _ in 0..due.min(60) {
                        let life = ((e.life + e.life_variance * (2.0 * self.random() - 1.0)) * 1000.0) as i32;
                        let vx = e.speed + e.speed_variance * (2.0 * self.random() - 1.0);
                        let vy = e.speed + e.speed_variance * (2.0 * self.random() - 1.0);
                        let vz = e.rise + e.rise_variance * (2.0 * self.random() - 1.0);
                        let px = x + e.range_x as f64 * (2.0 * self.random() - 1.0);
                        let py = y + e.range_y as f64 * (2.0 * self.random() - 1.0);
                        // A square of `size` pixels: that's `size × 20` in the AIR units.
                        let p = Particle::new(px, py, z + e.z_offset, e.color & 0xFF_FFFF, e.size as f64 * 20.0, life.max(1), Motion::Rising { acc: e.rise_acc });
                        self.add(p.moving(vx / 1000.0, vy / 1000.0, vz / 1000.0).with_image(image.clone()));
                    }
                    self.emitters.insert(id, Emitter::Custom { total, made: made + due });
                }
                Emitter::Xml { wait } => {
                    let wait = wait - secs;
                    if wait >= 0.0 {
                        self.emitters.insert(id, Emitter::Xml { wait });
                        continue;
                    }
                    self.emitters.insert(id, Emitter::Xml { wait: e.cooldown });
                    let Some(desc) = data.particles.get(&e.particle) else { continue };
                    // XMLParticle: drifts half a tile a second in some direction.
                    let a = 2.0 * PI * self.random();
                    let image = self.animated(sprites, desc);
                    let life = (desc.duration * 1000.0) as i32;
                    let p = Particle::new(x, y, desc.z, 0xFFFFFF, 0.0, life.max(1), Motion::Plain);
                    self.add(p.moving(a.cos() * 0.5 / 1000.0, a.sin() * 0.5 / 1000.0, 0.0).with_image(image));
                }
                Emitter::Steady { mut wait, period, radius } => {
                    wait -= secs;
                    let mut n = 0;
                    while wait < 0.0 && n < 20 {
                        wait += period;
                        n += 1;
                        self.steady(x, y, e, radius);
                    }
                    let radius = (radius + e.rise * secs).min(e.max_radius);
                    self.emitters.insert(id, Emitter::Steady { wait: wait.max(-period), period, radius });
                }
                Emitter::Done => {}
            }
        }
    }

    /// One particle of a steady effect.
    fn steady(&mut self, x: f64, y: f64, e: &bsk_data::EffectProps, radius: f64) {
        match e.id.as_str() {
            // FountainEffect: thrown up at 6.5 tiles a second, falling back under gravity.
            "Fountain" | "FountainSnowy" => {
                let a = 2.0 * PI * self.random();
                let color = if e.id == "FountainSnowy" { 0xFFFFFF } else { 0x4165D5 };
                let p = Particle::new(x, y, 0.75, color, 100.0, 1380, Motion::Rising { acc: -9.8 });
                self.add(p.moving(a.cos() * 0.8 / 1000.0, a.sin() * 0.8 / 1000.0, 6.5 / 1000.0));
            }
            // VentEffect: bubbles rising a tile.
            "Vent" => {
                let (a, d, r) = (self.random() * PI, self.random() * 0.4, self.random());
                let speed = (2.5 - r * 1.5).floor().max(1.0);
                let p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.0, 0x26CAFF, 75.0 + r * 50.0, (1000.0 / speed) as i32, Motion::Plain);
                self.add(p.moving(0.0, 0.0, speed / 1000.0));
            }
            // VortexEffect: from six tiles out, swirling in and down.
            "Vortex" => {
                let a = 2.0 * PI * self.random();
                let (px, py) = (x + a.cos() * 6.0, y + a.sin() * 6.0);
                let turn = (x - px).atan2(y - py) + PI / 2.0 - PI / 6.0;
                let pull = 2.5 + 0.5 * self.random();
                let max_size = 3.5 + 2.0 * self.random();
                let motion = Motion::Vortex { cx: x, cy: y, ax: turn.sin() * pull, ay: turn.cos() * pull, max_size };
                self.add(Particle::new(px, py, 1.0, e.color & 0xFF_FFFF, 0.0, 3400, motion));
            }
            // SkyBeamEffect: falling from on high within a widening circle.
            "SkyBeam" => {
                let (a, d) = (2.0 * PI * self.random(), self.random() * radius);
                let speed = (e.speed + (e.max_radius - d)).max(0.1);
                let life = (e.z_offset / speed * 1000.0) as i32;
                let p = Particle::new(x + d * a.cos(), y + d * a.sin(), e.z_offset, e.color & 0xFF_FFFF, 80.0 + self.random() * 40.0, life.max(1), Motion::Plain);
                self.add(p.moving(0.0, 0.0, -speed / 1000.0));
            }
            _ => {}
        }
    }

    /// Start a one-off effect for a newly seen object: `CircleEffect` or `ExplosionComplexEffect`.
    fn one_off(&mut self, x: f64, y: f64, e: &bsk_data::EffectProps) {
        match e.id.as_str() {
            "Circle" | "Heart" => {
                let n = e.amount.max(1);
                for i in 0..n {
                    let angle = 2.0 * PI / n as f64 * i as f64;
                    let size = 100.0 + self.random() * 20.0;
                    let z = 0.2 + self.random() * 0.2;
                    let motion = Motion::Orbit { cx: x, cy: y, angle, speed: e.speed, rad: e.min_radius, rise: e.rise, max_rad: e.max_radius };
                    self.add(Particle::new(x + e.min_radius * angle.cos(), y + e.min_radius * angle.sin(), z, e.color & 0xFF_FFFF, size, (e.life * 1000.0) as i32, motion));
                }
            }
            // SnowflakeEffect: six arms reaching out, sparking as they go.
            "Snowflake" => {
                let life = ((e.life * 1000.0) as i32).max(1);
                let r = e.min_radius + self.random() * (e.max_radius - e.min_radius);
                for i in 0..6 {
                    let a = i as f64 * 2.0 * PI / 6.0;
                    let size = e.size as f64;
                    let p = Particle::new(x, y, 0.0, e.color & 0xFF_FFFF, size, life, Motion::Sparker);
                    self.add(p.moving(r * a.cos() / life as f64, r * a.sin() / life as f64, 0.0));
                }
            }
            "ExplosionComplex" => {
                let life = ((e.life * 1000.0) as i32).max(1);
                for _ in 0..e.amount.max(1) {
                    let a = self.random() * PI * 2.0;
                    let r = e.min_radius + self.random() * (e.max_radius - e.min_radius);
                    let (vx, vy) = (r * a.cos() / life as f64, r * a.sin() / life as f64);
                    self.add(Particle::new(x, y, 0.2, e.color & 0xFF_FFFF, e.size as f64, life, Motion::Plain).moving(vx, vy, 0.0));
                }
            }
            _ => {}
        }
    }

    /// An `XMLEffect` particle's picture: one of its animations (each as likely as its
    /// `prob`), or its texture.
    fn animated(&mut self, sprites: &Sprites, desc: &bsk_data::ParticleDesc) -> Option<Image> {
        let k = desc.size as f32 / 100.0 * 5.0;
        let frame = |tex: &TextureData| -> (Option<AtlasRect>, f32, f32) {
            match &tex.pick(0).main {
                TextureRef::Image { set, index } => match (sprites.tile(set, *index), sprites.tile_src(set, *index)) {
                    (Some(r), Some(src)) => (Some(r), src.w as f32 * k, src.h as f32 * k),
                    _ => (None, 0.0, 0.0),
                },
                _ => (None, 0.0, 0.0),
            }
        };
        let pick = self.random();
        let mut acc = 0.0;
        let anim = desc.animations.iter().find(|a| {
            acc += a.prob;
            pick < acc
        });
        let frames: Vec<_> = match anim {
            Some(a) => a.frames.iter().map(|f| {
                let (r, w, h) = frame(&f.texture);
                (r, w, h, f.time.max(1))
            }).collect(),
            None => {
                let (r, w, h) = frame(&desc.texture);
                vec![(r, w, h, i32::MAX)]
            }
        };
        Some(Image { frames, standing: true })
    }

    /// `Camera.update`'s shake: up to half a tile, building up over ten seconds.
    pub fn jitter(&mut self, now: i32) -> (f64, f64) {
        let Some(since) = self.jitter_since else { return (0.0, 0.0) };
        let j = ((now - since) as f64 * 0.5 / 10_000.0).min(0.5);
        (self.pm(j), self.pm(j))
    }

    /// `FlashDescription.apply`: the tint an object wears now, if it flashes.
    pub fn flash_tint(&self, object_id: i32, now: i32) -> Option<Tint> {
        let f = self.flashes.get(&object_id)?;
        let t = (now - f.start).rem_euclid(f.period) as f32 / f.period as f32;
        let k = (t * std::f32::consts::PI).sin() * 0.5;
        let [r, g, b] = bsk_render::rgb_f32(f.color);
        Some(Tint { mul: [1.0 - k, 1.0 - k, 1.0 - k, 1.0], add: [k * r, k * g, k * b] })
    }

    /// Draw every particle: a square of colour with a dark edge.
    pub fn draw(&self, cam: &Camera, renderer: &mut Renderer) {
        let k = cam.scale / 50.0;
        for p in &self.list {
            if let Some(img) = &p.image {
                // The frame showing now, by how long the particle has been about.
                let mut t = p.life - p.left;
                let frame = img.frames.iter().find(|f| {
                    if t < f.3 {
                        return true;
                    }
                    t -= f.3;
                    false
                });
                if let Some(&(Some(rect), w, h, _)) = frame.or(img.frames.last()) {
                    let [sx, sy] = cam.to_screen(p.x, p.y, p.z);
                    let (w, h) = (w * k as f32, h * k as f32);
                    let top = if img.standing { sy - h } else { sy - h / 2.0 };
                    renderer.sprite(rect, Quad::rect((sx - w / 2.0).round(), top.round(), w, h), Tint::NONE);
                }
                continue;
            }
            let size = match p.motion {
                Motion::Throw => continue,
                Motion::Spark => p.size * p.left as f64 / p.life as f64,
                _ => p.size,
            };
            let side = (size / 100.0 * 5.0).floor() * k;
            let [sx, sy] = cam.to_screen(p.x, p.y, p.z);
            let edge = (1.0 * k).max(1.0) as f32;
            let half = (side / 2.0) as f32;
            renderer.fill(sx - half - edge, sy - half - edge, 2.0 * (half + edge), 2.0 * (half + edge), 0x000000, 0.6 * p.alpha);
            if side > 0.0 {
                renderer.fill(sx - half, sy - half, 2.0 * half, 2.0 * half, p.color, p.alpha);
            }
        }
    }
}

/// A still picture for a particle: tile `index` of `set`, `k` times its size.
fn still(sprites: &Sprites, set: &str, index: u32, k: f32) -> Option<Image> {
    let (rect, src) = (sprites.tile(set, index)?, sprites.tile_src(set, index)?);
    Some(Image { frames: vec![(Some(rect), src.w as f32 * k, src.h as f32 * k, i32::MAX)], standing: false })
}

/// The colours of every opaque pixel of a texture's first frame.
fn pixels(sprites: &Sprites, tex: &TextureData) -> Vec<u32> {
    let src = match &tex.pick(0).main {
        TextureRef::Image { set, index } => sprites.tile_src(set, *index),
        TextureRef::Animated { set, index } => sprites.char_src(set, *index, 0, 0),
        _ => None,
    };
    let Some(src) = src else { return Vec::new() };
    sprites.pixels(src).pixels().filter(|p| p[3] != 0).map(|p| u32::from_be_bytes([0, p[0], p[1], p[2]])).collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use bsk_proto::WorldPos;

    fn effect(effect_type: u8, target: i32, pos1: (f32, f32), pos2: (f32, f32)) -> ShowEffect {
        ShowEffect {
            effect_type,
            target_object_id: target,
            pos1: WorldPos { x: pos1.0, y: pos1.1 },
            pos2: WorldPos { x: pos2.0, y: pos2.1 },
            color: 0xFFFF0000,
            duration: 0,
            object_type: 0,
        }
    }

    #[test]
    fn effects_spawn_and_fade() {
        let world = World::new(10, 10, "test", 1);
        let mut p = Particles::default();
        // A grenade (no thrower known: from pos2), and an area attack.
        p.show_effect(&effect(4, 99, (5.0, 5.0), (1.0, 1.0)), &world, 0);
        p.aoe(5.0, 5.0, 2.0, 0xFFFF0000);
        assert_eq!(p.list.len(), 1 + 8);
        p.update(&world, 100, 100);
        // The throw and the sparkers leave sparks behind.
        assert!(p.list.len() > 9);
        p.update(&world, 2100, 2000);
        assert!(p.list.is_empty());
        // Effects on unknown objects do nothing.
        p.show_effect(&effect(5, 99, (3.0, 0.0), (0.0, 0.0)), &world, 0);
        assert!(p.list.is_empty());
    }

    #[test]
    fn objects_give_off_their_effects() {
        let mut data = GameData::default();
        data.add_objects(
            r#"<Objects>
                <Object type="0x20" id="Hero"><Class>Player</Class><Player/></Object>
                <Object type="0x30" id="Fount"><Class>GameObject</Class>
                    <Effect color="0x6FA8DC" rate="20" life=".5" size="8" rangeX="1" rangeY="1">CustomParticles</Effect></Object>
                <Object type="0x31" id="Ring"><Class>GameObject</Class>
                    <Effect color="0xFF0000" amount="12" life="2" minRadius="1" maxRadius="3" rise="1" speed="0.05">Circle</Effect></Object>
            </Objects>"#,
        )
        .unwrap();
        let mut world = World::new(10, 10, "test", 1);
        let tiles: Vec<_> = (0..100).map(|i| bsk_proto::GroundTile { x: i % 10, y: i / 10, tile_type: 0 }).collect();
        let obj = |id: i32, t: i32, x: f32| bsk_proto::ObjectData {
            object_type: t,
            status: bsk_proto::ObjectStatus { object_id: id, pos: WorldPos { x, y: 5.5 }, stats: Default::default() },
        };
        let update = bsk_proto::Update { tiles: tiles.into(), new_objs: vec![obj(1, 0x20, 5.5), obj(2, 0x30, 6.5), obj(3, 0x31, 3.5)].into(), drops: Default::default() };
        world.apply_update(&update, &data, 0);
        world.me = Some(1);
        let sprites = Sprites::default();
        let mut p = Particles::default();
        // The ring goes off at once; the fountain gives off 20 a second.
        p.object_effects(&world, &data, &sprites, 0);
        assert_eq!(p.list.len(), 12);
        p.object_effects(&world, &data, &sprites, 1000);
        assert_eq!(p.list.len(), 12 + 20);
        // Gone objects stop.
        world.apply_update(&bsk_proto::Update { drops: vec![2].into(), ..Default::default() }, &data, 0);
        p.object_effects(&world, &data, &sprites, 1000);
        assert_eq!(p.list.len(), 32);
        assert!(!p.emitters.contains_key(&2));
    }

    #[test]
    fn flash_and_jitter() {
        let world = World::new(10, 10, "test", 1);
        let mut p = Particles::default();
        p.show_effect(&effect(14, 0, (0.0, 0.0), (0.0, 0.0)), &world, 1000);
        let (jx, jy) = p.jitter(21_000);
        assert!(jx.abs() <= 0.5 && jy.abs() <= 0.5);
        assert!(p.flash_tint(7, 0).is_none());
    }
}
