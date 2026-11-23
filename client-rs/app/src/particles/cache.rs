//! The Crucible's caches (the server's `CrucibleCaches.cs`): what the tower drops beside its
//! climbers in a champion's fight, to be broken, each launching its charge at the champion.
//! `ShowEffect` 41 looks 58 to 60 (`relic::look`), `object_type` the kind each time:
//!
//! - 58, a cache dropping: a shaft of its light out of the sky onto pos1 and the cache falling
//!   down it over `duration` ms, its landing marked, dust thrown up where it lands;
//! - 59, a cache broken: it bursts at pos1 (wood, glass, ice, iron, glaze or gold flying), and its
//!   charge arcs over to the champion (the target), homing on it wherever it goes, to land in
//!   `duration` ms; where it lands, its blast (pos2.x the blast's reach), each kind its own: the
//!   keg's fireball, the jar's lightning out of the sky, the cask's frost, the crate's iron shards,
//!   the urn's hex, the reliquary's column of gold;
//! - 60, a cache lost (its floor fell, its time ran out, its champion fell first): a puff of it.
//!
//! And while each stands (`cache_auras`, every frame): what it gives off, by kind (the keg's fuse
//! spitting, the jar crackling, the cask's frost, the crate's embers, the urn's hex, the
//! reliquary's gold), a ring of its colour pulsing under it so it's seen in a busy fight, faster
//! and hotter in its last seconds.
//!
//! The kinds are numbered as the server's `CrucibleCache.Kinds` and `tools/build_crucible.py`'s
//! CACHES (change all three together); the words over the champion as a charge lands are the
//! server's, worded here (`callout`).

use super::relic::{look, mix, DecalKind, GRAVITY};
use super::{Motion, Particle, Particles};
use bsk_proto::ShowEffect;
use bsk_world::World;
use std::f64::consts::PI;

/// The caches, as the server numbers them.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub(super) enum Kind {
    /// The Powder Keg: the biggest blow.
    Keg,
    /// The Stormglass Jar: lightning that stuns.
    Jar,
    /// The Frost Cask: frozen in place, then slowed.
    Cask,
    /// The Rending Crate: armor broken.
    Crate,
    /// The Hex Urn: cursed and dazed.
    Urn,
    /// The Gilded Reliquary: rare, all of it at once.
    Reliquary,
}

const KINDS: [Kind; 6] = [Kind::Keg, Kind::Jar, Kind::Cask, Kind::Crate, Kind::Urn, Kind::Reliquary];

impl Kind {
    fn from_number(n: u16) -> Option<Kind> {
        KINDS.get(n as usize).copied()
    }

    fn named(id: &str) -> Option<Kind> {
        Some(match id {
            "Powder Keg" => Kind::Keg,
            "Stormglass Jar" => Kind::Jar,
            "Frost Cask" => Kind::Cask,
            "Rending Crate" => Kind::Crate,
            "Hex Urn" => Kind::Urn,
            "Gilded Reliquary" => Kind::Reliquary,
            _ => return None,
        })
    }

    /// Its colours: its light, its hottest, its darkest, and what it's made of.
    fn colors(self) -> (u32, u32, u32, [u32; 3]) {
        match self {
            Kind::Keg => (0xFF6A14, 0xFFE060, 0x2A1008, [0x86522C, 0x55301A, 0xB42A1A]),
            Kind::Jar => (0x6AD8FF, 0xFFFFFF, 0x0A2A40, [0xBFF0FF, 0x5AAECC, 0xD0A040]),
            Kind::Cask => (0xA8E8FF, 0xFFFFFF, 0x2A5A7A, [0xCFEAFF, 0x86B4D4, 0xFFFFFF]),
            Kind::Crate => (0xFF9A2A, 0xFFE29A, 0x242831, [0xA4AFC0, 0x646E80, 0x3A414F]),
            Kind::Urn => (0xC060FF, 0xF0C8FF, 0x1A0A26, [0x62248E, 0x3A1456, 0x9A50D0]),
            Kind::Reliquary => (0xFFD04A, 0xFFF8D0, 0x7A4E10, [0xFFE68A, 0xD8A428, 0xFFFFFF]),
        }
    }

    /// How big its blow is drawn (the keg's and the reliquary's are the big ones).
    fn weight(self) -> f64 {
        match self {
            Kind::Keg => 1.3,
            Kind::Reliquary => 1.5,
            Kind::Crate => 1.05,
            _ => 1.0,
        }
    }
}

/// A charge in flight from its cache to its champion.
#[derive(Clone, Copy, Debug)]
pub(super) struct Shell {
    kind: Kind,
    from: (f64, f64),
    /// Where its champion is (followed as it moves; where it was last seen once it's gone).
    to: (f64, f64),
    target: i32,
    start: i32,
    ms: i32,
    radius: f64,
    /// Where it was drawn last frame (x, y, height), to trail it from there.
    last: (f64, f64, f64),
}

impl Shell {
    /// Where it is a fraction `f` of the way: along the line to its champion, up an arc as high
    /// as the way is long (more or less), dipping onto the champion at the end.
    fn at(&self, f: f64) -> (f64, f64, f64) {
        let f = f.clamp(0.0, 1.0);
        let (dx, dy) = (self.to.0 - self.from.0, self.to.1 - self.from.1);
        let peak = (1.2 + 0.14 * dx.hypot(dy)).clamp(1.4, 4.0);
        // quick off the cache, slowing at the top, falling onto the champion
        let g = f * f * (3.0 - 2.0 * f) * 0.35 + f * 0.65;
        (self.from.0 + dx * g, self.from.1 + dy * g, 0.6 + 4.0 * peak * f * (1.0 - f) + 0.6 * f)
    }
}

/// The words the server sends over a champion as a cache's charge lands on it, in our language
/// (`None`: not a cache's).
pub fn callout(text: &str) -> Option<&'static str> {
    use crate::i18n::tr;
    Some(match text {
        "Blasted!" => tr("Blasted!"),
        "Stunned!" => tr("Stunned!"),
        "Frozen!" => tr("Frozen!"),
        "Armor Broken!" => tr("Armor Broken!"),
        "Hexed!" => tr("Hexed!"),
        "Sovereign's Wrath!" => tr("Sovereign's Wrath!"),
        _ => return None,
    })
}

/// A cache's last stretch, ms, before it crumbles (the server's `CrucibleCache.LifeMs` its whole
/// time): it hurries.
const LAST_MS: i32 = 4000;

impl Particles {
    /// Looks 58 to 60 (`relic` hands them here).
    pub(super) fn cache_look(&mut self, e: &ShowEffect, which: u8, world: &World, now: i32) {
        let Some(kind) = Kind::from_number(e.object_type) else { return };
        let (x, y) = (e.pos1.x as f64, e.pos1.y as f64);
        let ms = e.duration.clamp(0, 5000);
        match which {
            look::CACHE_DROP => self.cache_drop(kind, x, y, ms.max(200), now),
            look::CACHE_LAUNCH => {
                self.cache_break(kind, x, y, now);
                // the champion it flies to; out of sight, the burst is all we see
                if let Some(o) = world.objects.get(&e.target_object_id).filter(|o| !o.dead) {
                    if self.shells.len() < 24 && !self.disabled {
                        let s = Shell {
                            kind,
                            from: (x, y),
                            to: (o.x, o.y),
                            target: o.id,
                            start: now,
                            ms: ms.max(150),
                            radius: (e.pos2.x as f64).clamp(1.0, 6.0),
                            last: (x, y, 0.6),
                        };
                        self.shells.push(s);
                    }
                }
            }
            _ => self.cache_lost(kind, x, y),
        }
    }

    /// Every frame: the charges in flight move on, homing on their champions, and land.
    pub(super) fn cache_update(&mut self, world: &World, now: i32) {
        if self.shells.is_empty() {
            return;
        }
        let mut shells = std::mem::take(&mut self.shells);
        shells.retain_mut(|s| {
            if let Some(o) = world.objects.get(&s.target).filter(|o| !o.dead) {
                s.to = (o.x, o.y);
            }
            let f = (now - s.start) as f64 / s.ms as f64;
            let at = s.at(f);
            self.shell_trail(s.kind, s.last, at, now);
            s.last = at;
            if f < 1.0 {
                return true;
            }
            self.cache_blast(s.kind, s.to.0, s.to.1, s.radius, now);
            false
        });
        shells.append(&mut self.shells);
        self.shells = shells;
    }

    /// A shaft of the cache's light out of the sky onto (x, y), the cache falling down it in
    /// `ms`, its spot marked, and dust and sparks where it lands.
    fn cache_drop(&mut self, kind: Kind, x: f64, y: f64, ms: i32, now: i32) {
        const HIGH: f64 = 8.0;
        let (light, hot, _, stuff) = kind.colors();
        self.decal(DecalKind::Ring, x, y, 1.1, light, now, ms, None);
        // the shaft: thin, faint, standing for the fall
        for row in 0..24 {
            let z = HIGH * row as f64 / 24.0;
            for (ox, c, size) in [(0.0, hot, 120.0), (-0.12, light, 140.0), (0.12, light, 140.0)] {
                let mut p = Particle::new(x + ox, y, z, c, size, ms + 150, Motion::Plain);
                p.fade = true;
                p.opacity = 0.35;
                p.delay = row * 4;
                self.add(p);
            }
        }
        // the cache falling, a glowing heart in it, sparks streaming up off it
        let steps = (ms / 16).max(1);
        for i in 0..steps {
            let f = i as f64 / steps as f64;
            let z = HIGH * (1.0 - f * f);
            let t = (f * ms as f64) as i32;
            for (c, size) in [(light, 320.0), (stuff[1], 220.0), (hot, 90.0)] {
                let mut p = Particle::new(x, y, z, c, size, 40, Motion::Plain);
                p.delay = t;
                self.add(p);
            }
            if i % 2 == 0 {
                let c = self.pick(&[light, hot]);
                let mut p = Particle::new(x + self.pm(0.15), y + self.pm(0.15), z, c, 90.0, 300, Motion::Spark).moving(0.0, 0.0, 0.003);
                p.delay = t;
                self.add(p);
            }
        }
        // the thud: a ring of dust, a flash of its light
        let mut flash = Particle::new(x, y, 0.3, hot, 520.0, 140, Motion::Spark);
        flash.delay = ms;
        self.add(flash);
        self.decal(DecalKind::Ring, x, y, 1.6, light, now + ms, 380, None);
        self.puffs(x, y, 8, &[0x5A5048, 0x6E6460, 0x48403A], 180.0, 1.4, 0.3, 700, ms);
        self.debris(x, y, 8, &[light, hot], 70.0, 2.5, 3.0, 450, ms);
    }

    /// A cache broken at (x, y): a flash, pieces of it flying, a puff of its smoke.
    fn cache_break(&mut self, kind: Kind, x: f64, y: f64, now: i32) {
        let (light, hot, dark, stuff) = kind.colors();
        self.add(Particle::new(x, y, 0.5, hot, 640.0, 120, Motion::Spark));
        self.add(Particle::new(x, y, 0.5, light, 900.0, 180, Motion::Spark));
        self.decal(DecalKind::Ring, x, y, 1.8, light, now, 320, None);
        self.debris(x, y, 18, &stuff, 95.0, 4.0, 6.0, 700, 0);
        self.debris(x, y, 8, &[light, hot], 70.0, 3.0, 5.0, 450, 0);
        self.puffs(x, y, 5, &[mix(dark, 0x404040, 0.5), mix(light, 0x303030, 0.6)], 220.0, 0.8, 0.8, 800, 30);
        // its charge gathering for a breath before it leaps
        for i in 0..10 {
            let a = 2.0 * PI * i as f64 / 10.0;
            let mut p = Particle::new(x + 1.0 * a.cos(), y + 1.0 * a.sin(), 0.4, light, 120.0, 160, Motion::Plain)
                .moving(-a.cos() * 0.006, -a.sin() * 0.006, 0.0);
            p.fade = true;
            self.add(p);
        }
    }

    /// A cache lost: a puff of it, nothing more.
    fn cache_lost(&mut self, kind: Kind, x: f64, y: f64) {
        let (light, _, dark, stuff) = kind.colors();
        self.puffs(x, y, 7, &[mix(dark, 0x505050, 0.5), 0x5A5048, mix(light, 0x404040, 0.6)], 200.0, 0.9, 0.6, 900, 0);
        self.debris(x, y, 7, &stuff, 70.0, 1.6, 2.5, 450, 0);
    }

    /// The charge drawn from `a` to `b` (its last frame's place and this one's), each kind its
    /// own: a burning keg trailing fire and smoke, a ball of lightning crackling, a shard of ice
    /// shedding snow, iron shards whirling round an ember, a hex spiralling, a comet of gold.
    fn shell_trail(&mut self, kind: Kind, a: (f64, f64, f64), b: (f64, f64, f64), now: i32) {
        let (light, hot, dark, stuff) = kind.colors();
        let len = (b.0 - a.0).hypot(b.1 - a.1).hypot(b.2 - a.2);
        let steps = ((len / 0.2).ceil() as usize).clamp(1, 12);
        let w = kind.weight();
        for i in 1..=steps {
            let f = i as f64 / steps as f64;
            let (x, y, z) = (a.0 + (b.0 - a.0) * f, a.1 + (b.1 - a.1) * f, a.2 + (b.2 - a.2) * f);
            // its heart: a glow, a core and a white-hot fleck
            for (c, size, life) in [(light, 360.0 * w, 50), (dark, 170.0 * w, 40), (hot, 100.0 * w, 40)] {
                self.add(Particle::new(x, y, z, c, size, life, Motion::Plain));
            }
            match kind {
                Kind::Keg => {
                    let c = self.pick(&[0xFFE060, 0xFFA040, 0xFF6A14, 0xD03010]);
                    let (vx, vy) = (self.pm(0.4) / 1000.0, self.pm(0.4) / 1000.0);
                    let p = Particle::new(x + self.pm(0.1), y + self.pm(0.1), z, c, 150.0 + 70.0 * self.random(), 380, Motion::Spark).moving(vx, vy, 0.0008);
                    self.add(p);
                    if self.random() < 0.35 {
                        let c = self.pick(&[0x2A2A2A, 0x404040, 0x585858]);
                        let mut p = Particle::new(x, y, z, c, 220.0, 900, Motion::Plain).moving(0.0, 0.0, 0.0003);
                        p.fade = true;
                        p.grow = 0.12;
                        self.add(p);
                    }
                }
                Kind::Jar => {
                    // crackling: sparks leaping off it, and now and then a little arc
                    for _ in 0..2 {
                        let (ox, oy, oz) = (self.pm(0.45), self.pm(0.45), self.pm(0.35));
                        let c = self.pick(&[light, hot, 0x9AE8FF]);
                        self.add(Particle::new(x + ox, y + oy, (z + oz).max(0.1), c, 80.0, 120, Motion::Spark));
                    }
                    if self.random() < 0.12 {
                        let (mut px, mut py, mut pz) = (x, y, z);
                        for _ in 0..5 {
                            px += self.pm(0.18);
                            py += self.pm(0.18);
                            pz = (pz + self.pm(0.15)).max(0.1);
                            self.add(Particle::new(px, py, pz, hot, 70.0, 110, Motion::Spark));
                        }
                    }
                }
                Kind::Cask => {
                    // snow shed as it goes, falling, and a breath of frost
                    let c = self.pick(&[0xFFFFFF, light, 0xDFF4FF]);
                    let p = Particle::new(x + self.pm(0.2), y + self.pm(0.2), z, c, 70.0, 900, Motion::Ballistic { g: GRAVITY * 0.15 })
                        .moving(self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0, 0.0);
                    self.add(p);
                    if self.random() < 0.3 {
                        let mut p = Particle::new(x, y, z, 0xE8F8FF, 200.0, 700, Motion::Plain);
                        p.fade = true;
                        p.opacity = 0.5;
                        p.grow = 0.15;
                        self.add(p);
                    }
                }
                Kind::Crate => {
                    // three shards of its iron whirling round it, sparks thrown off
                    for k in 0..3 {
                        let ang = now as f64 * 0.025 + k as f64 * 2.0 * PI / 3.0;
                        let c = stuff[k % 3];
                        self.add(Particle::new(x + 0.35 * ang.cos(), y + 0.35 * ang.sin(), z, c, 120.0, 60, Motion::Plain));
                    }
                    if self.random() < 0.5 {
                        let c = self.pick(&[0xFFE29A, 0xFF9A2A]);
                        let p = Particle::new(x, y, z, c, 70.0, 450, Motion::Ballistic { g: GRAVITY * 0.5 })
                            .moving(self.pm(1.2) / 1000.0, self.pm(1.2) / 1000.0, 0.002);
                        self.add(p);
                    }
                }
                Kind::Urn => {
                    // two strands of the hex winding round its path, a dark smoke after
                    for k in 0..2 {
                        let ang = now as f64 * 0.02 + k as f64 * PI;
                        let c = if k == 0 { light } else { hot };
                        let mut p = Particle::new(x + 0.3 * ang.cos(), y + 0.3 * ang.sin(), z, c, 100.0, 500, Motion::Plain);
                        p.fade = true;
                        self.add(p);
                    }
                    if self.random() < 0.3 {
                        let mut p = Particle::new(x, y, z, mix(dark, light, 0.25), 200.0, 800, Motion::Plain).moving(0.0, 0.0, 0.0002);
                        p.fade = true;
                        p.opacity = 0.6;
                        p.grow = 0.1;
                        self.add(p);
                    }
                }
                Kind::Reliquary => {
                    // a comet: a long tail of gold, stars shaken loose
                    let mut p = Particle::new(x, y, z, light, 260.0, 520, Motion::Plain);
                    p.fade = true;
                    p.grow = -0.3;
                    self.add(p);
                    if self.random() < 0.4 {
                        let p = Particle::new(x + self.pm(0.3), y + self.pm(0.3), z, 0xFFFFFF, 110.0, 600, Motion::Spark)
                            .moving(self.pm(0.5) / 1000.0, self.pm(0.5) / 1000.0, -0.0005);
                        self.add(p);
                    }
                }
            }
        }
    }

    /// A charge landing on its champion at (x, y), `r` the blast's reach.
    fn cache_blast(&mut self, kind: Kind, x: f64, y: f64, r: f64, now: i32) {
        let (light, hot, dark, stuff) = kind.colors();
        let w = kind.weight();
        self.add(Particle::new(x, y, 0.6, hot, 900.0 * w, 140, Motion::Spark));
        self.add(Particle::new(x, y, 0.6, light, 1300.0 * w, 200, Motion::Spark));
        self.decal(DecalKind::Ring, x, y, r, light, now, 420, None);
        self.decal(DecalKind::Ring, x, y, r * 1.4, hot, now + 100, 520, None);
        match kind {
            Kind::Keg => {
                // a fireball: a column of fire, scorched and cracked ground, burning staves and
                // black smoke rolling up
                self.decal(DecalKind::Scorch, x, y, r * 0.8, 0xFF8030, now, 2200, None);
                self.decal(DecalKind::Cracks, x, y, r * 0.9, 0xFF6A14, now, 1800, None);
                for i in 0..44 {
                    let a = 2.0 * PI * self.random();
                    let d = 0.8 * self.random();
                    let c = [0xFFF0A0, 0xFFE060, 0xFFA040, 0xFF6A14, 0xD03010][i % 5];
                    let up = 0.007 + 0.007 * self.random();
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.2, c, 220.0 + 140.0 * self.random(), 650, Motion::Spark).moving(0.0, 0.0, up);
                    p.delay = (i as i32 * 7) % 200;
                    self.add(p);
                }
                self.debris(x, y, 40, &[0xFFE060, 0xFFA040, 0xFF6020, 0xD03010], 130.0, r * 1.8, 7.5, 900, 30);
                self.debris(x, y, 16, &stuff, 110.0, r * 1.4, 8.0, 1000, 40);
                self.puffs(x, y, 18, &[0x2A2A2A, 0x404040, 0x585858, 0x6A3020], 320.0, r * 0.7, 1.8, 1700, 120);
                self.flames(x, y, r * 0.6, 14, &[0xFFE060, 0xFF8030, 0xFF6A14], 260.0, 3.0, 700, 120);
            }
            Kind::Jar => {
                // lightning out of the sky onto it, three strokes in quick succession, sparks
                // racing out along the floor
                let bolts = [(0.0, 0.0), (0.6, -0.4), (-0.5, 0.5)];
                for (k, (ox, oy)) in bolts.into_iter().enumerate() {
                    if k == 0 {
                        self.sky_bolt(x + ox, y + oy, light, 1.3);
                    } else {
                        // the later strokes a moment after: their particles held back
                        let before = self.list.len();
                        self.sky_bolt(x + ox, y + oy, light, 1.0);
                        for p in self.list[before..].iter_mut() {
                            p.delay += k as i32 * 110;
                        }
                    }
                }
                for i in 0..24 {
                    let a = i as f64 * 2.0 * PI / 24.0 + self.pm(0.1);
                    let s = (r / 280.0) * (0.7 + 0.6 * self.random());
                    let c = if i % 3 == 0 { 0xFFFFFF } else { light };
                    let p = Particle::new(x, y, 0.15, c, 110.0, 280, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
                    self.add(p);
                }
                self.decal(DecalKind::Scorch, x, y, r * 0.5, light, now, 1200, None);
                self.debris(x, y, 16, &stuff, 80.0, r * 1.5, 6.0, 650, 0);
            }
            Kind::Cask => {
                // frost bursting out across the floor in spikes, shards of ice thrown high, a
                // cold mist rolling out
                self.decal(DecalKind::Frost, x, y, r * 1.1, light, now, 2600, None);
                self.debris(x, y, 34, &[0xFFFFFF, 0xDFF4FF, light, 0x86B4D4], 110.0, r * 1.7, 8.0, 1000, 0);
                self.puffs(x, y, 16, &[0xFFFFFF, 0xE8F8FF, 0xCFEAFF], 300.0, r * 0.8, 0.4, 1800, 60);
                for i in 0..18 {
                    let a = i as f64 * 2.0 * PI / 18.0;
                    let s = (r / 320.0) * (0.8 + 0.4 * self.random());
                    let p = Particle::new(x, y, 0.2, 0xFFFFFF, 130.0, 320, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
                    self.add(p);
                }
            }
            Kind::Crate => {
                // its iron bursting into shards that race out and fall, the ground cracked, sparks
                self.decal(DecalKind::Cracks, x, y, r, 0xC8CCD4, now, 1600, None);
                for i in 0..30 {
                    let a = i as f64 * 2.0 * PI / 30.0 + self.pm(0.08);
                    let s = (r / 240.0) * (0.75 + 0.5 * self.random());
                    let c = stuff[i % 3];
                    let p = Particle::new(x, y, 0.6, c, 140.0, 330, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
                    self.add(p);
                }
                self.debris(x, y, 26, &stuff, 120.0, r * 1.6, 7.0, 950, 0);
                self.debris(x, y, 20, &[0xFFE29A, 0xFF9A2A, light], 70.0, r * 2.0, 6.0, 600, 0);
                self.puffs(x, y, 8, &[0x3A414F, 0x585858], 240.0, r * 0.5, 0.9, 1100, 80);
            }
            Kind::Urn => {
                // the hex closing on it: its circle of runes under it, souls of violet winding up
                // round it, dark smoke
                self.decal(DecalKind::Sigil, x, y, r * 0.9, light, now, 2400, None);
                for i in 0..26 {
                    let c = self.pick(&[light, hot, 0x8A2AC8]);
                    let life = 700 + (self.random() * 400.0) as i32;
                    let speed = if i % 2 == 0 { 0.18 } else { -0.18 };
                    let motion = Motion::Soul { cx: x, cy: y, angle: 2.0 * PI * self.random(), speed, rad: 0.5 + 0.6 * self.random(), out: 0.0008, rise: 0.003 };
                    let mut p = Particle::new(x, y, 0.2, c, 130.0, life, motion);
                    p.alpha = 0.0;
                    p.delay = (i * 12) % 200;
                    self.add(p);
                }
                self.puffs(x, y, 12, &[dark, 0x2C1040, mix(light, dark, 0.6)], 280.0, r * 0.6, 0.9, 1500, 60);
                self.debris(x, y, 14, &stuff, 90.0, r * 1.3, 5.0, 700, 0);
            }
            Kind::Reliquary => {
                // the Sovereign's answer: a column of gold slamming down on it, the tower's star
                // under it, gold flung out
                self.chaos_beam(x, y, r, light);
                self.decal(DecalKind::Stars, x, y, r * 1.1, light, now, 2600, None);
                self.decal(DecalKind::Ring, x, y, r * 1.9, 0xFFFFFF, now + 200, 650, None);
                self.debris(x, y, 36, &[0xFFE68A, 0xD8A428, 0xFFFFFF, 0xEC5CFF, 0x46C2B4], 110.0, r * 1.8, 8.0, 1100, 90);
            }
        }
    }

    /// Every frame (`dt` ms): what each cache near us gives off while it stands.
    pub fn cache_auras(&mut self, world: &World, data: &bsk_data::GameData, dt: i32) {
        if self.disabled || dt <= 0 {
            return;
        }
        let Some(me) = world.player() else { return };
        let (mx, my) = (me.x, me.y);
        let now = self.now;
        let near: Vec<(i32, f64, f64, Kind, bool)> = world
            .objects
            .values()
            .filter(|o| !o.dead && (o.x - mx).hypot(o.y - my) <= 18.0)
            .filter_map(|o| {
                let kind = Kind::named(&data.objects.get(&o.object_type)?.id)?;
                let ending = o.stat_int_remaining(bsk_proto::stat::LIFETIME_MS, now).is_some_and(|ms| ms < LAST_MS);
                Some((o.id, o.x, o.y, kind, ending))
            })
            .collect();
        if near.is_empty() {
            return;
        }
        let z = world.my_z();
        self.here = (z, world.levels.as_ref().map_or(0, |l| ((z + 0.01) / l.height).floor().max(0.0) as u8));
        let dt = dt.min(100) as f64;
        let ticked = |every: i32| now / every != (now - dt as i32) / every;
        for (id, x, y, kind, ending) in near {
            let (light, hot, dark, _) = kind.colors();
            let more = if ending { 3.0 } else { 1.0 };
            let due = |p: &mut Self, rate: f64| {
                let n = rate * more * dt / 1000.0;
                n.floor() as usize + usize::from(p.random() < n - n.floor())
            };
            // a ring of its colour pulsing under it, quicker as its time runs out
            if ticked(if ending { 450 } else { 1400 }) {
                let c = if ending { mix(light, 0xFF3020, 0.5) } else { light };
                self.decal(DecalKind::Ring, x, y, 1.0, c, now, 500, Some(id));
            }
            match kind {
                Kind::Keg => {
                    // the fuse spitting sparks off its lid
                    for _ in 0..due(self, 16.0) {
                        let c = self.pick(&[0xFFE060, 0xFFB040, 0xFF6A14]);
                        let p = Particle::new(x + 0.2, y, 1.05, c, 60.0 + 30.0 * self.random(), 380, Motion::Ballistic { g: GRAVITY * 0.5 })
                            .moving(self.pm(1.0) / 1000.0, self.pm(1.0) / 1000.0, 0.002 + 0.002 * self.random());
                        self.add(p);
                    }
                }
                Kind::Jar => {
                    for _ in 0..due(self, 10.0) {
                        let (ox, oy) = (self.pm(0.35), self.pm(0.2));
                        let c = self.pick(&[light, hot]);
                        let z = 0.3 + 0.6 * self.random();
                        self.add(Particle::new(x + ox, y + oy, z, c, 70.0, 140, Motion::Spark));
                    }
                }
                Kind::Cask => {
                    for _ in 0..due(self, 6.0) {
                        let c = self.pick(&[0xFFFFFF, 0xE8F8FF, light]);
                        let mut p = Particle::new(x + self.pm(0.4), y + self.pm(0.3), 0.2 + 0.5 * self.random(), c, 140.0, 1100, Motion::Plain)
                            .moving(self.pm(0.25) / 1000.0, self.pm(0.25) / 1000.0, -0.0002);
                        p.fade = true;
                        p.opacity = 0.55;
                        p.grow = 0.08;
                        self.add(p);
                    }
                }
                Kind::Crate => {
                    for _ in 0..due(self, 7.0) {
                        let c = self.pick(&[0xFFE29A, 0xFF9A2A, 0xD8600E]);
                        let p = Particle::new(x + self.pm(0.4), y + 0.1, 0.45, c, 60.0, 700, Motion::Spark).moving(self.pm(0.2) / 1000.0, 0.0, 0.0012);
                        self.add(p);
                    }
                }
                Kind::Urn => {
                    for _ in 0..due(self, 9.0) {
                        let c = self.pick(&[light, hot, mix(light, dark, 0.4)]);
                        let mut p = Particle::new(x, y, 0.9, c, 90.0, 900, Motion::Swirl { id, angle: 2.0 * PI * self.random(), speed: 0.12, rad: 0.25, out: 0.0002, rise: 0.0012 });
                        p.fade = true;
                        self.add(p);
                    }
                }
                Kind::Reliquary => {
                    for _ in 0..due(self, 12.0) {
                        let c = self.pick(&[light, hot, 0xFFFFFF]);
                        let p = Particle::new(x + self.pm(0.6), y + self.pm(0.3), 0.2, c, 70.0 + 40.0 * self.random(), 900, Motion::Spark).moving(0.0, 0.0, 0.0016);
                        self.add(p);
                    }
                    if ticked(900) {
                        self.decal(DecalKind::Stars, x, y, 1.2, light, now, 900, Some(id));
                    }
                }
            }
        }
    }
}
