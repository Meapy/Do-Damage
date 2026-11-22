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
//! server's `AllyAOE` (`BehaviorDb.Allies.cs`), and looks 30 to 32 the Ascendant's forcefield
//! (`Forcefield.cs`, `Player.Forcefield.cs`), and 33 to 38 the Father's ward (`Player.Ward.cs`),
//! whose shell round a warded player `ward.rs` draws from the player's stats.

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
    /// The Ascendant's forcefield raised: its halo falls, a column of light, then the dome.
    pub const FORCEFIELD: u8 = 30;
    /// A player in a forcefield hit: the field flaring round them.
    pub const FIELD_BLOCK: u8 = 31;
    /// A forcefield already up when it came into view: the dome alone, for the time left.
    pub const FIELD_SEEN: u8 = 32;
    /// The Father's censer swung: smoke circling him and rolling out to the ward's edge, a sigil
    /// under him.
    pub const WARD_CAST: u8 = 33;
    /// A player warded: plates of grey steel flying in round them and locking shut.
    pub const WARD_GAIN: u8 = 34;
    /// A blow the ward took: sparks glancing off its plates (`object_type`: the percent left).
    pub const WARD_HIT: u8 = 35;
    /// The ward broken: its plates shattering.
    pub const WARD_BREAK: u8 = 36;
    /// The ward's time up: it dissolves back into smoke.
    pub const WARD_FADE: u8 = 37;
    /// The Censer of the Last Rite's ward breaking: its shards bursting out to the radius.
    pub const LAST_RITE: u8 = 38;
    /// Pestilence of Skuld: the plague climbing out of an enemy that fell to it, a new cloud
    /// where it fell (pos2.x its radius, `duration` how long it lingers).
    pub const CONTAGION: u8 = 39;
    /// The Gravekeeper's Censer: blue grave-flames circling its user (`duration`; pos2.x how many
    /// are left, none putting them out).
    pub const GRAVE_FLAMES: u8 = 40;
    /// The Gravekeeper's Censer: a flame rushing from its user (`source_id`) to an enemy and
    /// bursting on it.
    pub const GRAVE_FLAME: u8 = 41;
    /// Halo of the Ghost Bride: a player its forcefield kept from dying: a veil of light falling
    /// round them while they're untouchable.
    pub const VOW: u8 = 42;
    /// The Vial of Cascading Dusk thrown: the vial arcing from its thrower (the target) to where
    /// it lands (pos1) over `duration` ms, trailing dusk and stars.
    pub const DUSK_THROW: u8 = 43;
    /// The Vial of Cascading Dusk splashing (pos2.x its radius, `color` the splash's shade,
    /// `object_type` which: 0 the vial itself shattering, 1 the four round it, 2 the four beyond).
    pub const DUSK_SPLASH: u8 = 44;
    /// The dusk running out from a splash (pos1) to where the next one lands: pos2.x tiles away
    /// at `object_type` degrees, getting there in `duration` ms.
    pub const DUSK_FLOW: u8 = 45;
    /// A Realm Pass tier reached (`PassTracker`): a column of the season's light (`color`)
    /// bursting up round its player, a ring racing out, stars and sparks.
    pub const PASS_TIER: u8 = 46;
    /// The Realm Pass made Gilded: a crown of gold sparks wheeling round its player, gold
    /// raining out, two rings.
    pub const PASS_GILDED: u8 = 47;
    /// Vaelith's Final Aria (`Player.CruciblePowers.cs`): a verse ringing out from its Bard to
    /// the radius, notes thrown out on it (`object_type` which verse, 0 to 3, gold turning to
    /// violet; the last, the crescendo, draws the Sovereign's star under them for `duration`).
    pub const ARIA: u8 = 48;
    /// Pyrrhax's Meteor Mortar (`Player.CruciblePowers.cs`): the shell fired from its Pirate (the
    /// target) to where it lands (pos1) over `duration` ms: a muzzle blast and a ring of smoke,
    /// then a molten ball arcing over, trailing fire, smoke and dripping slag.
    pub const MORTAR_SHOT: u8 = 49;
    /// The Meteor Mortar's blasts at pos1 (pos2.x the radius): `object_type` 0 the shell landing
    /// (a pool of slag left burning `duration` ms), 1 a meteor falling out of the sky onto it
    /// over `duration` ms and bursting there.
    pub const MORTAR_BLAST: u8 = 50;
    /// Ferrogrim's Bastion Turret (`Player.CruciblePowers.cs`, a Mechanic's turret with
    /// `Turret.Slam`): `object_type` 0 raised where it lands (iron plates flying in and locking,
    /// sparks; the plate, head and link are `turret.rs`'s, as every turret's), 1 its slam
    /// (the ground cracking in a ring, iron and sparks thrown up), pos2.x the slam's reach.
    pub const BASTION: u8 = 51;
    /// Nerissa's Tidepearl: a wave of the tide rolling out from its Druid to pos2.x (`object_type`
    /// which, 0 to 2; the first opens a whirlpool under them).
    pub const TIDE: u8 = 52;
    /// Chronarch's Coat of Stolen Hours: the Orrery's clock on the ground round its Blademaster
    /// (pos2.x its reach), its hand sweeping round twice over `duration` ms.
    pub const HOURS: u8 = 53;
    /// A Mechanic's turret (`Turret.cs`, `Player.Turrets.cs`; drawn by `turret.rs`): its crate
    /// thrown from its Mechanic (the target) to where it lands (pos1) over `duration` ms.
    pub const TURRET_THROW: u8 = 54;
    /// The turret built at pos1 (the target): dust, bolts and welding sparks, a blueprint's ring
    /// out to its reach (pos2.x); `duration` its time left, `source_id` its Mechanic and
    /// `object_type` its blast's radius in hundredths of a tile, for the plate under it.
    pub const TURRET_BUILD: u8 = 55;
    /// A turret already up when it came into view: its plate alone (as `TURRET_BUILD`).
    pub const TURRET_SEEN: u8 = 56;
    /// A turret blowing up (its time up, or a newer one in its place): pos2.x the blast's radius.
    pub const TURRET_BLAST: u8 = 57;
    pub const LAST: u8 = 57;
}

/// Falling, tiles a millisecond each millisecond (12 tiles a second each second).
pub(super) const GRAVITY: f64 = 12.0e-6;

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
    /// Chronarch's clock: a face, its twelve marks and its hand sweeping round twice.
    Clock,
    /// The Tome's ring of stars under a player.
    Stars,
    /// The Chaotic Scripture's rift: a torn, glowing void, the eight arrows of chaos turning round
    /// it.
    Rift,
    /// The Ascendant's forcefield: a dome of light seen from above, brightest at its rim, a lattice
    /// of hexagons shimmering across it, rings of runes turning, ripples running out.
    Forcefield,
    /// A hexagon of the forcefield flaring round a player as it turns a blow.
    FieldFlare,
    /// The Father's rite under him as he swings his censer: rings of grey steel, eight shield
    /// plates turning round a square and the marks of the hours, in his censer's colour.
    WardSigil,
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
        look::FORCEFIELD => 26,
        look::SERPENT_WIND | look::UNDERTOW | look::FORGOTTEN => 32,
        look::DAVY | look::IGNITION | look::PENTAGRAM => 45,
        look::HELLBRAND | look::CHAOS | look::CONTAGION => 60,
        look::GRAVE_FLAMES => 30,
        look::VOW => 50,
        look::DUSK_SPLASH => 90,
        look::OATH => 120,
        look::STARLIGHT => 140,
        _ => 100,
    }
}

/// Between two colours.
pub(super) fn mix(a: u32, b: u32, t: f64) -> u32 {
    let t = t.clamp(0.0, 1.0);
    let ch = |s: u32| -> u32 {
        let (x, y) = ((a >> s) & 0xFF, (b >> s) & 0xFF);
        ((x as f64 + (y as f64 - x as f64) * t).round() as u32) << s
    };
    ch(16) | ch(8) | ch(0)
}

/// The Vial of Cascading Dusk's colours beside its shades: the night under it, a deep violet and
/// the last pink of the sky.
const DUSK_DEEP: u32 = 0x1A0A30;
const DUSK_MID: u32 = 0x5A2A9A;
const DUSK_PINK: u32 = 0xFF8AD8;

/// The grey of the Father's ward: its shadow, its steel, its light and its shine.
pub(crate) const WARD_GREYS: [u32; 4] = [0x6A7078, 0xA8AEB8, 0xD0D4DA, 0xF0F2F6];

/// A number from a decal's seed: the same each frame.
/// What drifts in an Ascendant's forcefield, by its halo's tier (`field_style`).
#[derive(Clone, Copy, PartialEq, Debug)]
enum Drift {
    /// Gold and silver: nothing but its motes.
    Clear,
    /// Worn: grey dust sifting down.
    Dust,
    /// Rusty: flakes of rust falling off it.
    Flakes,
    /// Bronze: specks of verdigris rising.
    Verdigris,
    /// Eternal Peace: pale petals drifting down, swaying.
    Petals,
    /// God's Blessing: shafts of light turning over it, and sparks rising.
    Rays,
    /// The Seraphim: a rim of flame, embers flying up.
    Embers,
    /// The Archangel: stars wheeling round it, a second rim, twinkles.
    Stars,
}

/// An Ascendant's forcefield beyond its colour, which names its halo's tier (`Forcefield.COLORS`):
/// the colour that tints it beside its own, what stands in for white in its glints, how unsteady it
/// is (the lower tiers flicker and their lattice has holes), and what drifts in it.
#[derive(Clone, Copy, Debug)]
struct FieldStyle {
    accent: u32,
    spark: u32,
    unsteady: f64,
    drift: Drift,
}

fn field_style(color: u32) -> FieldStyle {
    let (accent, spark, unsteady, drift) = match color & 0xFF_FFFF {
        0x8E949A => (0x5A6066, 0xDCE0E4, 0.4, Drift::Dust),
        0xC0602A => (0x6E3A1E, 0xF4A868, 0.3, Drift::Flakes),
        0xD08A3C => (0x4FB49A, 0xFFE0B0, 0.12, Drift::Verdigris),
        0xCFDCEB => (0x8FB6E0, 0xFFFFFF, 0.0, Drift::Clear),
        0x8CF0C8 => (0xF0FFF6, 0xFFFFFF, 0.0, Drift::Petals),
        0xFFF0A8 => (0xFFC040, 0xFFFFFF, 0.0, Drift::Rays),
        0xFF6A28 => (0xFFD040, 0xFFF4C8, 0.0, Drift::Embers),
        0xB27CFF => (0x60D8FF, 0xFFFFFF, 0.0, Drift::Stars),
        // the Halo of the Ghost Bride's (`Player.VowColor`): rose-white, petals drifting down
        0xF4E8FF => (0xFFC8E0, 0xFFFFFF, 0.0, Drift::Petals),
        // the Golden Halo's, and any other
        _ => (0xBFEFFF, 0xFFFFFF, 0.0, Drift::Clear),
    };
    FieldStyle { accent, spark, unsteady, drift }
}

pub(super) fn hash(seed: u64, i: u64) -> f64 {
    let mut h = seed ^ i.wrapping_mul(0x9E37_79B9_7F4A_7C15);
    h ^= h >> 33;
    h = h.wrapping_mul(0xFF51_AFD7_ED55_8CCD);
    h ^= h >> 33;
    (h >> 11) as f64 / (1u64 << 53) as f64
}

impl Particles {
    pub(super) fn pick(&mut self, colors: &[u32]) -> u32 {
        colors[(self.random() * colors.len() as f64) as usize % colors.len()]
    }

    pub(super) fn decal(&mut self, kind: DecalKind, x: f64, y: f64, radius: f64, color: u32, start: i32, ms: i32, follow: Option<i32>) {
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
    pub(super) fn debris(&mut self, x: f64, y: f64, n: usize, colors: &[u32], size: f64, speed: f64, up: f64, life: i32, delay: i32) {
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
    pub(super) fn puffs(&mut self, x: f64, y: f64, n: usize, colors: &[u32], size: f64, spread: f64, rise: f64, life: i32, delay: i32) {
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

    /// The Vial of Cascading Dusk's flight from `from` to `to` in `ms`: a tumbling vial (a dark
    /// heart in a violet glow, a white glint turning round it) on an arc, a ribbon of dusk left
    /// hanging behind it, stars shaken loose, drops of dusk falling and its shadow on the floor.
    /// The Meteor Mortar fired (`look::MORTAR_SHOT`): the muzzle's flash and smoke, and the shell
    /// arcing from `from` to `to` over `ms`, molten and trailing fire.
    fn mortar_shot(&mut self, from: (f64, f64), to: (f64, f64), ms: i32) {
        const PEAK: f64 = 3.0;
        let t_all = ms as f64;
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        let len = (dx * dx + dy * dy).sqrt().max(0.01);
        let (ux, uy) = (dx / len, dy / len);
        // the muzzle: a flash, fire thrown forward, a ring of smoke rolling out
        let (mx, my) = (from.0 + ux * 0.5, from.1 + uy * 0.5);
        self.add(Particle::new(mx, my, 0.6, 0xFFF0B0, 520.0, 120, Motion::Spark));
        for i in 0..14 {
            let spread = self.pm(0.6);
            let (f1, f2) = (2.5 + 2.0 * self.random(), 2.5 + 2.0 * self.random());
            let (vx, vy) = (ux * f1 - uy * spread, uy * f2 + ux * spread);
            let c = [0xFFE060, 0xFFA040, 0xFF6A14][i % 3];
            let life = 260 + (self.random() * 160.0) as i32;
            let p = Particle::new(mx, my, 0.6, c, 130.0, life, Motion::Spark).moving(vx / 1000.0, vy / 1000.0, 0.0006);
            self.add(p);
        }
        for i in 0..12 {
            let a = 2.0 * PI * i as f64 / 12.0;
            let c = self.pick(&[0x3A3A3A, 0x545454, 0x6E6A66]);
            let size = 240.0 + 80.0 * self.random();
            let mut p = Particle::new(mx, my, 0.5, c, size, 900, Motion::Plain).moving(a.cos() * 0.0011, a.sin() * 0.0011, 0.0004);
            p.fade = true;
            self.add(p);
        }
        // where it is `t` ms in: along the line, up a parabola PEAK tiles high at the middle
        let at = |t: f64| {
            let f = (t / t_all).clamp(0.0, 1.0);
            (from.0 + dx * f, from.1 + dy * f, 0.6 + 4.0 * PEAK * f * (1.0 - f))
        };
        let steps = (ms / 16).max(1);
        for i in 0..steps {
            let t = i as f64 * t_all / steps as f64;
            let (px, py, pz) = at(t);
            // the shell: a black iron core in a molten glow, a white-hot fleck turning on it
            let spin = t * 0.02;
            for (ox, oy, c, size) in [(0.0, 0.0, 0xFF6A14, 380.0), (0.0, 0.0, 0x2A1008, 230.0), (0.1 * spin.cos(), 0.1 * spin.sin(), 0xFFE0A0, 90.0)] {
                let mut p = Particle::new(px + ox, py + oy, pz, c, size, 40, Motion::Plain);
                p.delay = t as i32;
                self.add(p);
            }
            // fire streaming off it
            let c = self.pick(&[0xFFE060, 0xFFA040, 0xFF6A14, 0xD03010]);
            let (jx, jy, size) = (self.pm(0.08), self.pm(0.08), 150.0 + 60.0 * self.random());
            let (vx, vy) = (self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0);
            let mut p = Particle::new(px + jx, py + jy, pz, c, size, 360, Motion::Spark).moving(vx, vy, 0.0008);
            p.delay = t as i32;
            self.add(p);
            // smoke hanging in its wake
            if i % 3 == 0 {
                let c = self.pick(&[0x2A2A2A, 0x404040, 0x585858]);
                let size = 200.0 + 80.0 * self.random();
                let mut p = Particle::new(px, py, pz, c, size, 900, Motion::Plain).moving(0.0, 0.0, 0.0003);
                p.delay = t as i32;
                p.fade = true;
                self.add(p);
            }
            // slag dripping off it
            if i % 6 == 3 {
                let mut p = Particle::new(px, py, pz, 0xFF8030, 90.0, 900, Motion::Ballistic { g: GRAVITY })
                    .moving(dx / t_all * 0.3, dy / t_all * 0.3, 0.0);
                p.delay = t as i32;
                self.add(p);
            }
        }
    }

    /// The Meteor Mortar's shell landing (`look::MORTAR_BLAST`, 0): a flash, shockwaves, the
    /// ground cracked and scorched, a column of fire, slag and rock thrown out, black smoke, and a
    /// pool of slag left burning for `ms`.
    fn mortar_blast(&mut self, x: f64, y: f64, r: f64, ms: i32, now: i32) {
        let lasts = ms.clamp(600, 10_000);
        self.add(Particle::new(x, y, 0.5, 0xFFF4C0, 1100.0, 160, Motion::Spark));
        self.decal(DecalKind::Ring, x, y, r, 0xFFC060, now, 420, None);
        self.decal(DecalKind::Ring, x, y, r * 1.5, 0xFF6A14, now + 110, 560, None);
        self.decal(DecalKind::Cracks, x, y, r * 0.95, 0xFF6A14, now, lasts, None);
        self.decal(DecalKind::Scorch, x, y, r * 0.8, 0xFF8030, now, lasts, None);
        // the column: fire roaring straight up out of the crater
        for i in 0..40 {
            let a = 2.0 * PI * self.random();
            let d = 0.6 * self.random();
            let c = [0xFFF0A0, 0xFFE060, 0xFFA040, 0xFF6A14, 0xD03010][i % 5];
            let (size, up) = (200.0 + 120.0 * self.random(), 0.008 + 0.006 * self.random());
            let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.1, c, size, 700, Motion::Spark).moving(0.0, 0.0, up);
            p.delay = (i as i32 * 9) % 240;
            self.add(p);
        }
        self.debris(x, y, 44, &[0xFFE060, 0xFFA040, 0xFF6020, 0xD03010], 130.0, r * 1.6, 7.0, 900, 40);
        self.debris(x, y, 18, &[0x3A2A20, 0x5A4030, 0x2A1810], 110.0, r * 1.2, 8.0, 1000, 60);
        self.puffs(x, y, 16, &[0x2A2A2A, 0x404040, 0x585858, 0x6A3020], 300.0, r * 0.6, 1.6, 1600, 140);
        self.flames(x, y, r * 0.5, 10, &[0xFFE060, 0xFF8030, 0xFF6A14], 260.0, 3.0, 600, 80);
        // the pool burning on: embers rising off it while it lasts
        self.aura(look::HELLBRAND, 0, x, y, r * 0.75, 0xFF6A14, now, lasts, false);
    }

    /// A meteor of the Meteor Mortar (`look::MORTAR_BLAST`, 1): falling steeply out of the sky
    /// onto (x, y) over `ms`, trailing fire, and bursting there.
    fn meteor(&mut self, x: f64, y: f64, r: f64, ms: i32, now: i32) {
        const HIGH: f64 = 8.0;
        let (sx, sy) = (x - 1.6, y - 2.4);
        let steps = (ms / 16).max(1);
        for i in 0..steps {
            let f = i as f64 / steps as f64;
            let (px, py, pz) = (sx + (x - sx) * f, sy + (y - sy) * f, 0.3 + HIGH * (1.0 - f));
            let t = (f * ms as f64) as i32;
            for (c, size) in [(0xFF6A14, 330.0), (0x3A1408, 190.0), (0xFFE0A0, 80.0)] {
                let mut p = Particle::new(px, py, pz, c, size, 40, Motion::Plain);
                p.delay = t;
                self.add(p);
            }
            let c = self.pick(&[0xFFE060, 0xFFA040, 0xFF6A14]);
            let (jx, jy, size) = (self.pm(0.1), self.pm(0.1), 140.0 + 60.0 * self.random());
            let mut p = Particle::new(px + jx, py + jy, pz, c, size, 300, Motion::Spark).moving(0.0, 0.0, 0.002);
            p.delay = t;
            self.add(p);
        }
        // the burst where it lands
        let mut flash = Particle::new(x, y, 0.4, 0xFFF0B0, 600.0, 130, Motion::Spark);
        flash.delay = ms;
        self.add(flash);
        self.decal(DecalKind::Ring, x, y, r, 0xFFA040, now + ms, 380, None);
        self.decal(DecalKind::Scorch, x, y, r * 0.7, 0xFF8030, now + ms, 1400, None);
        self.debris(x, y, 20, &[0xFFE060, 0xFFA040, 0xFF6020, 0x3A2A20], 110.0, r * 1.8, 6.0, 700, ms);
        self.puffs(x, y, 6, &[0x2A2A2A, 0x404040], 240.0, r * 0.5, 1.2, 1100, ms + 40);
    }

    fn dusk_throw(&mut self, from: (f64, f64), to: (f64, f64), ms: i32, color: u32) {
        const PEAK: f64 = 2.2;
        let t_all = ms as f64;
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        // where it is `t` ms in: along the line, up a parabola PEAK tiles high at the middle
        let at = |t: f64| {
            let f = (t / t_all).clamp(0.0, 1.0);
            (from.0 + dx * f, from.1 + dy * f, 0.5 + 4.0 * PEAK * f * (1.0 - f))
        };
        // the vial: drawn afresh every 16 ms along its arc, each lasting a frame or two
        let steps = (ms / 16).max(1);
        for i in 0..steps {
            let t = i as f64 * t_all / steps as f64;
            let (px, py, pz) = at(t);
            let spin = t * 0.018;
            for (ox, oy, c, size) in [(0.0, 0.0, color, 300.0), (0.0, 0.0, DUSK_DEEP, 150.0), (0.16 * spin.cos(), 0.16 * spin.sin(), 0xFFFFFF, 70.0)] {
                let mut p = Particle::new(px + ox, py + oy, pz, c, size, 40, Motion::Plain);
                p.delay = t as i32;
                self.add(p);
            }
            // the ribbon it leaves, hanging a moment, sinking and fading
            if i % 2 == 0 {
                let c = self.pick(&[color, DUSK_PINK, DUSK_MID, 0xE8C8FF]);
                let mut p = Particle::new(px + self.pm(0.05), py + self.pm(0.05), pz, c, 150.0 + 60.0 * self.random(), 420, Motion::Spark)
                    .moving(self.pm(0.2) / 1000.0, self.pm(0.2) / 1000.0, -0.0006);
                p.delay = t as i32;
                self.add(p);
            }
            // stars shaken loose, twinkling as they drift down
            if i % 5 == 2 {
                let mut p = Particle::new(px, py, pz, 0xFFF4FF, 80.0, 700, Motion::Ballistic { g: GRAVITY * 0.2 })
                    .moving(self.pm(0.6) / 1000.0, self.pm(0.6) / 1000.0, 0.0008);
                p.delay = t as i32;
                p.fade = true;
                self.add(p);
            }
            // drops of dusk dripping off it
            if i % 7 == 4 {
                let mut p = Particle::new(px, py, pz, DUSK_MID, 90.0, 900, Motion::Ballistic { g: GRAVITY })
                    .moving(dx / t_all * 0.3, dy / t_all * 0.3, 0.0);
                p.delay = t as i32;
                p.fade = true;
                self.add(p);
            }
            // its shadow racing along the floor under it
            if i % 2 == 1 {
                let mut p = Particle::new(px, py, 0.02, 0x100818, 160.0, 40, Motion::Plain);
                p.delay = t as i32;
                p.alpha = 0.45;
                self.add(p);
            }
        }
    }

    /// One of the Vial of Cascading Dusk's splashes at (x, y), out to `r`, in `color`; `stage` 0
    /// the vial's own (the glass shatters, the biggest), 1 and 2 the cascade's, each smaller.
    fn dusk_splash(&mut self, x: f64, y: f64, r: f64, color: u32, stage: u8, now: i32) {
        let k = [1.0, 0.8, 0.65][stage as usize];
        let hot = mix(color, 0xFFFFFF, 0.55);
        // the flash
        for (size, c, life) in [(1100.0 * k, color, 260), (650.0 * k, hot, 190), (320.0 * k, 0xFFFFFF, 130)] {
            self.add(Particle::new(x, y, 0.35, c, size, life, Motion::Spark));
        }
        // the shockwave and a white ring after it, the pool spreading and a ring of stars over it
        self.decal(DecalKind::Ring, x, y, r * 1.2, color, now, 420, None);
        self.decal(DecalKind::Ring, x, y, r * 0.8, 0xF4E0FF, now + 70, 340, None);
        self.decal(DecalKind::Puddle, x, y, r * 0.8, mix(color, DUSK_DEEP, 0.65), now, 1800, None);
        self.decal(DecalKind::Stars, x, y, r * 0.75, hot, now + 60, if stage == 0 { 2200 } else { 1500 }, None);
        // the glass of the vial itself, shattering
        if stage == 0 {
            self.debris(x, y, 22, &[0xFFFFFF, 0xEDE0FF, 0xC8B0FF, 0x9AD8FF], 70.0, 5.5, 7.0, 750, 0);
        }
        // dusk flung out in drops, and a crown of it racing out along the floor
        self.debris(x, y, (26.0 * k) as usize, &[color, DUSK_PINK, DUSK_MID, hot], 110.0 * k, 4.2 * r / 2.5, 5.0, 750, 0);
        for i in 0..16 {
            let a = i as f64 * 2.0 * PI / 16.0 + self.pm(0.1);
            let s = r * 0.0019 * (0.8 + 0.4 * self.random());
            let c = if i % 4 == 0 { 0xFFFFFF } else if i % 2 == 0 { hot } else { color };
            self.add(Particle::new(x, y, 0.2, c, 150.0 * k, 300, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0));
        }
        // twilight climbing out of it in a spiral, stars among it
        let n = (26.0 * k) as i32;
        for i in 0..n {
            let angle = i as f64 * 2.0 * PI / n as f64;
            let c = if i % 5 == 0 { 0xFFFFFF } else if i % 3 == 0 { DUSK_PINK } else { color };
            let rad = 0.25 + r * 0.45 * self.random();
            let motion = Motion::Soul { cx: x, cy: y, angle, speed: if stage == 1 { -0.11 } else { 0.11 }, rad, out: 0.0004, rise: 0.0034 };
            let mut p = Particle::new(x, y, 0.15, c, 120.0 + 70.0 * self.random(), 900 + (self.random() * 400.0) as i32, motion);
            p.alpha = 0.0;
            p.delay = 60 + i * 9;
            self.add(p);
        }
        // mist rolling out over the pool
        self.puffs(x, y, (8.0 * k) as usize, &[DUSK_DEEP, 0x2C1450, mix(color, DUSK_DEEP, 0.5)], 200.0 * k, r * 0.5, 0.5, 1100, 80);
        // and the mist and stars it breathes while the dusk bleeds them
        self.aura(look::DUSK_SPLASH, -1, x, y, r, color, now, 1800, false);
    }

    /// The Vial of Cascading Dusk's dusk running out over the floor from `from` to `to` in `ms`:
    /// a bright head and a wavering ribbon behind it, stars spilling off it.
    fn dusk_flow(&mut self, from: (f64, f64), to: (f64, f64), ms: i32, color: u32) {
        let (dx, dy) = (to.0 - from.0, to.1 - from.1);
        let len = dx.hypot(dy).max(0.01);
        let (nx, ny) = (-dy / len, dx / len);
        let n = ((len * 10.0) as i32).max(1);
        let phase = 2.0 * PI * self.random();
        for i in 0..=n {
            let f = i as f64 / n as f64;
            let sway = (f * 3.0 * PI + phase).sin() * 0.28 * (1.0 - f * 0.6);
            let (px, py) = (from.0 + dx * f + nx * sway, from.1 + dy * f + ny * sway);
            let t = (f * ms as f64) as i32;
            // the head
            let mut head = Particle::new(px, py, 0.35, 0xFFFFFF, 160.0, 34, Motion::Plain);
            head.delay = t;
            self.add(head);
            let mut glow = Particle::new(px, py, 0.3, color, 330.0, 50, Motion::Plain);
            glow.delay = t;
            glow.alpha = 0.8;
            self.add(glow);
            // the ribbon left behind it
            let c = self.pick(&[color, DUSK_PINK, DUSK_MID]);
            let mut p = Particle::new(px, py, 0.25, c, 150.0 + 50.0 * self.random(), 520, Motion::Spark).moving(0.0, 0.0, 0.0004);
            p.delay = t;
            self.add(p);
            if i % 4 == 0 {
                let mut s = Particle::new(px, py, 0.3, 0xFFF4FF, 70.0, 600, Motion::Spark).moving(nx * self.pm(0.6) / 1000.0, ny * self.pm(0.6) / 1000.0, 0.0012);
                s.delay = t;
                self.add(s);
            }
        }
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
        // (a forcefield lasts up to 20 s)
        let ms = e.duration.clamp(0, if matches!(look, look::FORCEFIELD | look::FIELD_SEEN) { 30_000 } else { 10_000 });
        let r = if matches!(look, look::PASS_TIER | look::PASS_GILDED) { r.max(2.0) } else { r };
        let id = e.target_object_id;
        let on = world.objects.get(&id).map(|o| (o.x, o.y));
        let z = world.my_z();
        self.here = (z, world.levels.as_ref().map_or(0, |l| ((z + 0.01) / l.height).floor().max(0.0) as u8));
        // the Mechanic's turrets have a module of their own
        if (look::TURRET_THROW..=look::TURRET_BLAST).contains(&look) {
            self.turret_look(e, look, world, now);
            return;
        }
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
            // Pestilence of Skuld's contagion: the fallen enemy bursts into a sick green cloud,
            // its dead climbing out of it as wisps of mist, the cloud bubbling while it lingers
            look::CONTAGION => {
                self.decal(DecalKind::Puddle, x, y, r * 0.8, color, now, ms.clamp(1200, 6000), None);
                self.decal(DecalKind::Ring, x, y, r, 0xD0FF80, now, 420, None);
                self.puffs(x, y, 16, &[color, 0x4A7A20, 0x2A3A10, 0xB0E060], 300.0, r * 0.5, 0.5, 1600, 0);
                self.debris(x, y, 16, &[color, 0xD0FF80, 0x2A3A10], 100.0, 3.0, 5.0, 700, 0);
                for i in 0..3 {
                    let mut p = Particle::new(x + self.pm(0.5), y + self.pm(0.5), 0.2, 0xE0FFD0, 220.0, 1100, Motion::Plain).moving(self.pm(0.2) / 1000.0, self.pm(0.2) / 1000.0, 0.0022);
                    p.delay = 150 + i * 160;
                    p.fade = true;
                    p.opacity = 0.7;
                    p.grow = 0.1;
                    self.add(p);
                }
                self.aura(look, id, x, y, r, color, now, ms.clamp(600, 6000), false);
            }
            // the Gravekeeper's Censer: blue flames lit round its user, circling it (an aura) as
            // many as are left; with none left they go out in a puff of smoke
            look::GRAVE_FLAMES => {
                let (cx, cy) = on.unwrap_or((x, y));
                let n = r.round() as usize;
                let lit = self.auras.iter().any(|a| a.look == look && a.id == id);
                if n == 0 || ms <= 1 {
                    self.auras.retain(|a| !(a.look == look && a.id == id));
                    if lit {
                        self.puffs(cx, cy, 6, &[0x2440A0, color, 0x606870], 160.0, 0.6, 0.8, 600, 0);
                    }
                } else {
                    if !lit {
                        self.decal(DecalKind::Ring, cx, cy, 1.4, color, now, 400, None);
                        self.flames(cx, cy, 0.9, 24, &[0xC0E8FF, color, 0x2440A0], 200.0, 4.0, 700, 300);
                    }
                    self.aura(look, id, cx, cy, n as f64, color, now, ms, true);
                }
            }
            // ...one rushing out: a streak of blue fire from its user onto the enemy, bursting
            // there in a ring of flame and scorch
            look::GRAVE_FLAME => {
                let from = e.source_id.and_then(|s| world.objects.get(&s)).map(|o| (o.x, o.y)).unwrap_or((x, y));
                self.seekers(from.0, from.1, id, (x, y), 6, &[0xFFFFFF, 0xC0E8FF, color], 170.0, 20);
                self.decal(DecalKind::Ring, x, y, r, color, now + 300, 380, None);
                self.decal(DecalKind::Scorch, x, y, r * 0.6, 0x2440A0, now + 300, 900, None);
                for i in 0..26 {
                    let (a, d) = (2.0 * PI * self.random(), r * 0.6 * self.random().sqrt());
                    let c = self.pick(&[0xFFFFFF, 0xC0E8FF, color, 0x2440A0]);
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.1, c, 160.0 + 80.0 * self.random(), 500, Motion::Spark)
                        .moving(self.pm(0.6) / 1000.0, self.pm(0.6) / 1000.0, (3.0 + 3.0 * self.random()) / 1000.0);
                    p.delay = 300 + i * 6;
                    self.add(p);
                }
                self.debris(x, y, 10, &[color, 0xC0E8FF], 90.0, 3.0, 4.0, 600, 300);
            }
            // the Ghost Bride's vow kept: a ring of light, a veil of petals and lace falling round
            // the player it saved, who shines white, and motes rising off them while they're
            // untouchable
            look::VOW => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.decal(DecalKind::Ring, cx, cy, r * 1.6, color, now, 500, None);
                self.decal(DecalKind::Ring, cx, cy, r * 0.9, 0xFFC8E0, now + 120, 450, None);
                for i in 0..28 {
                    let angle = i as f64 * 2.0 * PI / 28.0;
                    let motion = Motion::Swirl { id, angle, speed: 0.12, rad: 0.75, out: 0.0, rise: -0.0016 };
                    let mut p = Particle::new(cx, cy, 2.2, if i % 3 == 0 { 0xFFC8E0 } else { 0xFFFFFF }, 120.0, 900, motion);
                    p.alpha = 0.0;
                    p.delay = i * 12;
                    self.add(p);
                }
                if on.is_some() {
                    self.flashes.insert(id, super::Flash { start: now, color: 0xFFFFFF, period: 300, repeats: (ms / 300).max(1) });
                }
                self.aura(look, id, cx, cy, r, color, now, ms.max(600), true);
            }
            // the Vial of Cascading Dusk in flight: a glowing vial tumbling along its arc, a ribbon
            // of dusk and falling stars behind it
            look::DUSK_THROW => {
                self.dusk_on = Some((id, now));
                let from = on.unwrap_or((x - 1.0, y - 1.0));
                self.dusk_throw(from, (x, y), ms.clamp(300, 3000), color);
            }
            // the Vial of Cascading Dusk splashing: a flash, the glass shattering (the vial's own
            // splash), dusk flung out and pooling on the floor under a ring of stars, a spiral of
            // twilight climbing out of it and mist rolling off while it bleeds them
            look::DUSK_SPLASH => {
                self.dusk_on = Some((id, now));
                self.dusk_splash(x, y, r, color, e.object_type.min(2) as u8, now);
            }
            // the dusk running out along the floor to the next splash
            look::PASS_TIER => {
                let (cx, cy) = on.unwrap_or((x, y));
                let pale = mix(color, 0xFFFFFF, 0.6);
                self.decal(DecalKind::Ring, cx, cy, r, color, now, 520, None);
                self.decal(DecalKind::Ring, cx, cy, r * 0.6, 0xFFFFFF, now + 80, 380, None);
                self.decal(DecalKind::Stars, cx, cy, 1.1, color, now, 1400, Some(id));
                // the column: light streaming straight up out of the ground round them
                for i in 0..36 {
                    let a = 2.0 * PI * i as f64 / 36.0;
                    let d = 0.25 + 0.35 * self.random();
                    let c = [color, pale, 0xFFFFFF][i % 3];
                    let mut p = Particle::new(cx + d * a.cos(), cy + d * a.sin(), 0.1, c, 110.0 + 70.0 * self.random(), 650, Motion::Spark)
                        .moving(0.0, 0.0, 0.006 + 0.004 * self.random());
                    p.delay = (i as i32 * 17) % 260;
                    self.add(p);
                }
                // a spiral of sparks winding up and out
                for i in 0..16 {
                    let a = i as f64 * 0.8;
                    let mut p = Particle::new(cx, cy, 0.2 + i as f64 * 0.12, pale, 120.0, 700, Motion::Spark)
                        .moving(a.cos() * 0.0025, a.sin() * 0.0025, 0.002);
                    p.delay = i * 30;
                    self.add(p);
                }
                self.debris(cx, cy, 18, &[color, pale, 0xFFFFFF], 110.0, 3.0, 6.0, 800, 120);
                self.aura(look::STARLIGHT, id, cx, cy, 0.9, color, now, 1600, true);
            }
            look::PASS_GILDED => {
                let (cx, cy) = on.unwrap_or((x, y));
                const GOLD: u32 = 0xFFD24A;
                const SHINE: u32 = 0xFFF4C0;
                self.decal(DecalKind::Ring, cx, cy, r * 1.2, GOLD, now, 600, None);
                self.decal(DecalKind::Ring, cx, cy, r * 0.7, SHINE, now + 140, 450, None);
                self.decal(DecalKind::Stars, cx, cy, 1.2, GOLD, now, 2200, Some(id));
                // a crown of gold over their head, its points flaring
                for i in 0..10 {
                    let a = 2.0 * PI * i as f64 / 10.0;
                    let mut p = Particle::new(cx, cy, 1.4, if i % 2 == 0 { SHINE } else { GOLD }, 150.0, 1500, Motion::Swirl { id, angle: a, speed: 0.06, rad: 0.45, out: 0.0, rise: 0.0 });
                    p.fade = true;
                    self.add(p);
                }
                // gold raining out of it, and coins of light thrown up
                self.debris(cx, cy, 30, &[GOLD, SHINE, 0xE0A020], 120.0, 3.5, 8.0, 1100, 100);
                self.puffs(cx, cy, 8, &[GOLD, SHINE], 220.0, 0.8, 1.5, 900, 60);
                self.aura(look::OATH, id, cx, cy, 1.0, GOLD, now, 1800, true);
            }
            look::ARIA => {
                let (cx, cy) = on.unwrap_or((x, y));
                const GOLD: u32 = 0xFFD24A;
                const SHINE: u32 = 0xFFF4C0;
                const VIOLET: u32 = 0xC070FF;
                const PALE: u32 = 0xF0D8FF;
                let verse = e.object_type.min(3) as usize;
                let shade = mix(GOLD, VIOLET, verse as f64 / 3.0);
                // the verse: a ring racing out to its reach, a paler one at its heels
                self.decal(DecalKind::Ring, cx, cy, r, shade, now, 420 + 60 * verse as i32, None);
                self.decal(DecalKind::Ring, cx, cy, r * 0.7, SHINE, now + 90, 360, None);
                // its notes: sparks wheeling out from the Bard, more with each verse
                let n = 10 + 4 * verse;
                for i in 0..n {
                    let a = 2.0 * PI * i as f64 / n as f64 + verse as f64 * 0.4;
                    let c = [SHINE, shade, PALE][i % 3];
                    let spin = if verse % 2 == 0 { 0.05 } else { -0.05 };
                    let mut p = Particle::new(cx, cy, 0.5, c, 120.0 + 40.0 * self.random(), 650, Motion::Swirl { id, angle: a, speed: spin, rad: 0.4, out: r * 0.8 / 650.0, rise: 0.0008 });
                    p.fade = true;
                    p.delay = (i as i32 * 11) % 90;
                    self.add(p);
                }
                // a ring of light round the Bard as it plays
                for i in 0..8 {
                    let a = 2.0 * PI * i as f64 / 8.0;
                    let mut p = Particle::new(cx, cy, 0.9, if i % 2 == 0 { SHINE } else { shade }, 90.0, 500, Motion::Swirl { id, angle: a, speed: 0.12, rad: 0.55, out: 0.0, rise: 0.0 });
                    p.fade = true;
                    self.add(p);
                }
                if verse == 3 {
                    // the crescendo: the Sovereign's star drawn under them, light bursting up,
                    // gold and violet raining out to the edge, the floor ringing twice more
                    self.decal(DecalKind::Pentagram, cx, cy, r * 0.55, GOLD, now, ms.max(1200), Some(id));
                    self.decal(DecalKind::Stars, cx, cy, 1.3, VIOLET, now, ms.max(1200), Some(id));
                    self.decal(DecalKind::Ring, cx, cy, r, VIOLET, now + 220, 520, None);
                    self.decal(DecalKind::Ring, cx, cy, r, GOLD, now + 440, 520, None);
                    for i in 0..32 {
                        let a = 2.0 * PI * i as f64 / 32.0;
                        let d = 0.2 + 0.4 * self.random();
                        let c = [GOLD, SHINE, VIOLET, PALE][i % 4];
                        let mut p = Particle::new(cx + d * a.cos(), cy + d * a.sin(), 0.1, c, 120.0 + 60.0 * self.random(), 700, Motion::Spark)
                            .moving(0.0, 0.0, 0.007 + 0.004 * self.random());
                        p.delay = (i as i32 * 13) % 200;
                        self.add(p);
                    }
                    self.debris(cx, cy, 36, &[GOLD, SHINE, VIOLET, PALE], 120.0, r * 0.9, 6.0, 900, 60);
                    self.aura(look::STARLIGHT, id, cx, cy, 1.0, VIOLET, now, ms.max(1200), true);
                }
            }
            look::BASTION => {
                const IRON: u32 = 0xB4BCC8;
                const DARK: u32 = 0x4A505C;
                const GLOW: u32 = 0xFF9A2A;
                if e.object_type == 0 {
                    // the plates flying in from all round and locking with a flash
                    for i in 0..16 {
                        let a = 2.0 * PI * i as f64 / 16.0;
                        let d = 2.2 + 0.6 * self.random();
                        let c = if i % 2 == 0 { IRON } else { DARK };
                        let life = 260;
                        let p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.8, c, 170.0, life, Motion::Plain)
                            .moving(-d * a.cos() / life as f64, -d * a.sin() / life as f64, -0.6 / life as f64);
                        self.add(p);
                    }
                    let mut flash = Particle::new(x, y, 0.5, 0xFFF0C0, 600.0, 140, Motion::Spark);
                    flash.delay = 260;
                    self.add(flash);
                    self.decal(DecalKind::Ring, x, y, r, GLOW, now + 260, 420, None);
                    self.decal(DecalKind::Cracks, x, y, r * 0.5, GLOW, now + 260, 1500, None);
                    self.debris(x, y, 18, &[GLOW, 0xFFE060, IRON], 100.0, 3.0, 6.0, 600, 260);
                } else {
                    // the slam: a flash, the ground cracking in a ring, iron and sparks thrown
                    // up, dust rolling out
                    self.add(Particle::new(x, y, 0.4, 0xFFF0C0, 520.0, 120, Motion::Spark));
                    self.decal(DecalKind::Ring, x, y, r, GLOW, now, 380, None);
                    self.decal(DecalKind::Ring, x, y, r * 0.6, IRON, now + 80, 320, None);
                    self.decal(DecalKind::Cracks, x, y, r * 0.8, GLOW, now, 1100, None);
                    self.debris(x, y, 16, &[GLOW, 0xFFE060, 0xFF6A14], 110.0, r * 1.4, 5.0, 600, 0);
                    self.debris(x, y, 10, &[IRON, DARK], 120.0, r * 1.1, 6.0, 700, 0);
                    self.puffs(x, y, 8, &[0x6A6058, 0x8A8070, 0x4A4440], 240.0, r * 0.6, 0.8, 900, 40);
                }
            }
            look::TIDE => {
                const FOAM: u32 = 0xD8FFFF;
                const DEEP: u32 = 0x16505A;
                let wave = e.object_type.min(2) as i32;
                let (cx, cy) = (x, y);
                if wave == 0 {
                    self.decal(DecalKind::Whirlpool, cx, cy, 1.6, color, now, 1500, None);
                }
                self.decal(DecalKind::Ring, cx, cy, r, color, now, 520, None);
                self.decal(DecalKind::Ring, cx, cy, r * 0.8, FOAM, now + 70, 460, None);
                self.decal(DecalKind::Puddle, cx, cy, r * 0.55, color, now, 900, None);
                // the wave's crest: water thrown up all along it as it rolls out
                let n = 28;
                for i in 0..n {
                    let a = 2.0 * PI * i as f64 / n as f64 + wave as f64 * 0.11;
                    let c = [FOAM, color, 0x8AF0E0, DEEP][i % 4];
                    let life = 480 + (self.random() * 120.0) as i32;
                    let speed = r * 0.95 / life as f64;
                    let mut p = Particle::new(cx + 0.4 * a.cos(), cy + 0.4 * a.sin(), 0.2, c, 150.0 + 60.0 * self.random(), life, Motion::Ballistic { g: GRAVITY * 0.35 })
                        .moving(speed * a.cos(), speed * a.sin(), 0.0035 + 0.002 * self.random());
                    p.delay = (i as i32 * 7) % 60;
                    self.add(p);
                }
                // foam bubbling up round its Druid
                self.puffs(cx, cy, 6, &[FOAM, 0x8AF0E0], 160.0, 1.0, 1.4, 700, 0);
            }
            look::HOURS => {
                const BRASS: u32 = 0xD0A040;
                let lasts = ms.clamp(400, 5000);
                // the clock, sweeping (the decal), a little past its time to fade
                self.decal(DecalKind::Clock, x, y, r, color, now, lasts * 5 / 4, Some(id));
                self.decal(DecalKind::Ring, x, y, r, color, now, 380, None);
                // the moment stopped: gears and motes of light hanging still round them, falling
                // as time comes back
                for i in 0..18 {
                    let a = 2.0 * PI * self.random();
                    let d = r * (0.3 + 0.65 * self.random());
                    let c = [color, BRASS, 0xD8FFFF][i % 3];
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.6 + 1.2 * self.random(), c, 90.0 + 50.0 * self.random(), lasts, Motion::Ballistic { g: GRAVITY * 0.02 });
                    p.fade = true;
                    p.delay = (i as i32 * 23) % 200;
                    self.add(p);
                }
                // a ring of light round the Blademaster
                for i in 0..10 {
                    let a = 2.0 * PI * i as f64 / 10.0;
                    let mut p = Particle::new(x, y, 1.0, if i % 2 == 0 { color } else { BRASS }, 100.0, lasts, Motion::Swirl { id, angle: a, speed: -0.08, rad: 0.6, out: 0.0, rise: 0.0 });
                    p.fade = true;
                    self.add(p);
                }
            }
            look::MORTAR_SHOT => {
                let from = on.unwrap_or((x - 1.0, y - 1.0));
                self.mortar_shot(from, (x, y), ms.clamp(200, 2000));
            }
            look::MORTAR_BLAST => {
                if e.object_type == 0 {
                    self.mortar_blast(x, y, r, ms, now);
                } else {
                    self.meteor(x, y, r, ms.clamp(150, 2000), now);
                }
            }
            look::DUSK_FLOW => {
                self.dusk_on = Some((id, now));
                let a = (e.object_type as f64).to_radians();
                self.dusk_flow((x, y), (x + r * a.cos(), y + r * a.sin()), ms.clamp(150, 2000), color);
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
            // the Ascendant's forcefield: a halo drops out of the sky onto the spot, strikes a
            // column of light, and the dome bursts open from it in a double shockwave, golden
            // light skittering out to its edge and feathers of light drifting down
            look::FORCEFIELD => {
                const LAND: i32 = 280;
                let st = field_style(color);
                let white = st.spark;
                let pale = mix(color, white, 0.6);
                self.fields.retain(|f| f.0 != id);
                self.fields.push((id, now + ms + 500));
                // the halo, falling and turning
                for i in 0..18 {
                    let a = i as f64 * 2.0 * PI / 18.0;
                    let c = if i % 3 == 0 { white } else { color };
                    let motion = Motion::Soul { cx: x, cy: y, angle: a, speed: 0.18, rad: 0.7, out: 0.0012, rise: -4.0 / LAND as f64 };
                    let mut p = Particle::new(x + 0.7 * a.cos(), y + 0.7 * a.sin(), 4.0, c, 170.0, LAND, motion);
                    p.alpha = 1.0;
                    self.add(p);
                }
                // the column of light it strikes, white at its heart
                for row in 0..22 {
                    for (ox, c, size) in [(0.0, white, 260.0), (-0.22, pale, 230.0), (0.22, pale, 230.0), (-0.4, color, 170.0), (0.4, st.accent, 170.0)] {
                        let mut p = Particle::new(x + ox, y, row as f64 * 0.32, c, size, 520 - row * 6, Motion::Plain).moving(0.0, 0.0, 0.0015);
                        p.delay = LAND + row * 9;
                        p.fade = true;
                        self.add(p);
                    }
                }
                // the flash where it lands
                for (size, c, life) in [(1100.0, color, 260), (650.0, white, 180)] {
                    let mut p = Particle::new(x, y, 0.3, c, size, life, Motion::Spark);
                    p.delay = LAND;
                    self.add(p);
                }
                // the shockwaves, the second paler and a beat behind
                self.decal(DecalKind::Ring, x, y, r * 1.08, color, now + LAND, 520, None);
                self.decal(DecalKind::Ring, x, y, r * 0.9, white, now + LAND + 110, 420, None);
                // light skittering out along the floor to the dome's edge
                for i in 0..36 {
                    let a = i as f64 * 2.0 * PI / 36.0 + self.pm(0.05);
                    let s = (r / 300.0) * (0.85 + 0.3 * self.random());
                    let c = if i % 4 == 0 { white } else if i % 2 == 0 { pale } else { color };
                    let mut p = Particle::new(x, y, 0.15, c, 140.0, 300, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
                    p.delay = LAND;
                    self.add(p);
                }
                // feathers of light, drifting down over it
                for _ in 0..26 {
                    let (a, d) = (2.0 * PI * self.random(), r * 0.9 * self.random().sqrt());
                    let fall = 1400 + (self.random() * 900.0) as i32;
                    let c = self.pick(&[white, pale, color, st.accent]);
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 3.0 + 1.5 * self.random(), c, 100.0, fall, Motion::Plain)
                        .moving(self.pm(0.25) / 1000.0, self.pm(0.25) / 1000.0, -3.2 / fall as f64);
                    p.delay = LAND + (self.random() * 500.0) as i32;
                    p.fade = true;
                    self.add(p);
                }
                // the dome itself, and the wall of light rising off its rim, for as long as it lasts
                self.decal(DecalKind::Forcefield, x, y, r, color, now + LAND, (ms - LAND).max(600), None);
                self.aura(look, id, x, y, r, color, now + LAND, (ms - LAND).max(600), false);
            }
            // a forcefield already up: the dome as it is now, for the time it has left
            look::FIELD_SEEN => {
                self.fields.retain(|f| f.0 != id);
                self.fields.push((id, now + ms + 500));
                // (started as if opened half a second ago: no opening)
                self.decal(DecalKind::Forcefield, x, y, r, color, now - 500, ms.max(600) + 500, None);
                self.aura(look::FORCEFIELD, id, x, y, r, color, now, ms.max(600), false);
            }
            // a blow turned aside: a hexagon of the field flares round the player, light glancing off
            look::FIELD_BLOCK => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.decal(DecalKind::FieldFlare, cx, cy, r, color, now, 380, Some(id));
                let st = field_style(color);
                for i in 0..10 {
                    let a = i as f64 * 2.0 * PI / 10.0 + self.pm(0.2);
                    let c = if i % 2 == 0 { st.spark } else if i % 3 == 0 { st.accent } else { color };
                    let mut p = Particle::new(cx + r * 0.8 * a.cos(), cy + r * 0.8 * a.sin(), 0.5 + 0.3 * self.random(), c, 90.0, 260, Motion::Spark)
                        .moving(a.cos() * 0.004, a.sin() * 0.004, 0.002);
                    p.fade = true;
                    self.add(p);
                }
                if on.is_some() {
                    self.flashes.insert(id, super::Flash { start: now, color: 0xFFF2B8, period: 120, repeats: 1 });
                }
            }
            // the censer swung: its smoke circling the Father twice as it rises, rolling out across
            // the floor to the ward's edge in a ring, a grey shockwave and one in the censer's
            // colour, the sigil of the rite turning under him, and the censer's fire flaring
            look::WARD_CAST => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.ward_on = Some((id, now));
                for i in 0..28 {
                    let c = if i % 5 == 0 { color } else { WARD_GREYS[1 + i as usize % 3] };
                    let motion = Motion::Swirl { id, angle: i as f64 * 0.45, speed: 0.30, rad: 0.7, out: 0.0005, rise: 0.0009 };
                    let mut p = Particle::new(cx, cy, 0.35, c, 170.0, 560, motion);
                    p.alpha = 0.0;
                    p.delay = i * 8;
                    self.add(p);
                }
                for i in 0..44 {
                    let a = i as f64 * 2.0 * PI / 44.0 + self.pm(0.05);
                    let s = r / 650.0 * (0.85 + 0.3 * self.random());
                    let c = if i % 6 == 0 { color } else { WARD_GREYS[i % 3] };
                    let mut p = Particle::new(cx, cy, 0.2, c, 200.0, 650, Motion::Plain).moving(a.cos() * s, a.sin() * s, 0.0004);
                    p.fade = true;
                    p.opacity = 0.6;
                    p.grow = 0.25;
                    p.delay = 140;
                    self.add(p);
                }
                for i in 0..10 {
                    let a = i as f64 * 2.0 * PI / 10.0 + self.pm(0.1);
                    let s = r / 520.0 * (0.85 + 0.3 * self.random());
                    let c = if i % 3 == 0 { WARD_GREYS[3] } else { color };
                    let mut p = Particle::new(cx, cy, 0.15, c, 120.0, 320, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
                    p.delay = 140;
                    self.add(p);
                }
                self.decal(DecalKind::Ring, cx, cy, r, WARD_GREYS[2], now + 140, 620, None);
                self.decal(DecalKind::Ring, cx, cy, r * 0.82, color, now + 230, 520, None);
                self.decal(DecalKind::WardSigil, cx, cy, 1.7, color, now, 1300, Some(id));
                for (size, c, life) in [(700.0, color, 220), (420.0, WARD_GREYS[3], 160)] {
                    let mut p = Particle::new(cx, cy, 0.7, c, size, life, Motion::Spark);
                    p.delay = 120;
                    self.add(p);
                }
                self.puffs(cx, cy, 10, &WARD_GREYS[..3], 220.0, 0.35, 1.8, 1300, 80);
            }
            // a player warded: incense streaming to them from the Father who raised it, then plates
            // of steel flying in from all round and locking shut with a clang of light
            look::WARD_GAIN => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.wards.insert(id, super::WardLook { color, gained: now, hit: i32::MIN / 2, left: 100 });
                let from = e.source_id.filter(|s| *s != id).and_then(|s| world.objects.get(&s)).map(|o| (o.x, o.y));
                if let Some((sx, sy)) = from {
                    self.seekers(sx, sy, id, (cx, cy), 7, &[WARD_GREYS[2], WARD_GREYS[3], color], 110.0, 22);
                }
                let lag = if from.is_some() { 140 } else { 0 };
                for i in 0..14 {
                    let a = i as f64 * 2.0 * PI / 14.0;
                    let z = 0.25 + 0.45 * (i % 3) as f64;
                    let c = if i % 4 == 0 { color } else { WARD_GREYS[1 + i as usize % 3] };
                    let mut p = Particle::new(cx + 1.7 * a.cos(), cy + 1.7 * a.sin(), z, c, 150.0, 900, Motion::Seek { id, tx: cx, ty: cy, speed: 0.007 })
                        .moving(-a.sin() * 0.003, a.cos() * 0.003, 0.001);
                    p.delay = lag + i * 10;
                    self.add(p);
                }
                for (size, c, life) in [(520.0, WARD_GREYS[2], 200), (300.0, 0xFFFFFF, 140)] {
                    let mut p = Particle::new(cx, cy, 0.5, c, size, life, Motion::Spark);
                    p.delay = lag + 270;
                    self.add(p);
                }
                self.decal(DecalKind::Ring, cx, cy, 1.1, WARD_GREYS[3], now + lag + 270, 320, Some(id));
            }
            // a blow taken: sparks glancing off the plate it struck
            look::WARD_HIT => {
                let (cx, cy) = on.unwrap_or((x, y));
                let left = (e.object_type as i32).clamp(0, 100);
                match self.wards.get_mut(&id) {
                    Some(w) => {
                        w.hit = now;
                        w.left = left;
                    }
                    None => {
                        self.wards.insert(id, super::WardLook { color, gained: now - 2000, hit: now, left });
                    }
                }
                let a0 = 2.0 * PI * self.random();
                for _ in 0..9 {
                    let b = a0 + self.pm(0.7);
                    let c = self.pick(&[0xFFFFFF, WARD_GREYS[3], WARD_GREYS[2], color]);
                    let mut p = Particle::new(cx + 0.55 * a0.cos(), cy + 0.55 * a0.sin(), 0.45 + 0.35 * self.random(), c, 85.0, 260, Motion::Spark)
                        .moving(b.cos() * 0.0045, b.sin() * 0.0045, 0.002);
                    p.fade = true;
                    self.add(p);
                }
            }
            // broken: the plates burst off, tumbling and bouncing, a white flash, dust
            look::WARD_BREAK => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.wards.remove(&id);
                for i in 0..24 {
                    let a = i as f64 * 2.0 * PI / 24.0 + self.pm(0.12);
                    let s = 2.0 + 2.5 * self.random();
                    let c = if i % 5 == 0 { color } else { WARD_GREYS[i % 4] };
                    let z = 0.25 + 0.9 * self.random();
                    let life = 700 + (self.random() * 400.0) as i32;
                    let mut p = Particle::new(cx + 0.55 * a.cos(), cy + 0.55 * a.sin(), z, c, 120.0 + 60.0 * self.random(), life, Motion::Ballistic { g: GRAVITY * 0.6 })
                        .moving(a.cos() * s / 1000.0, a.sin() * s / 1000.0, (1.5 + 2.5 * self.random()) / 1000.0);
                    p.fade = true;
                    self.add(p);
                }
                for (size, c, life) in [(800.0, color, 220), (520.0, 0xFFFFFF, 150)] {
                    self.add(Particle::new(cx, cy, 0.5, c, size, life, Motion::Spark));
                }
                self.decal(DecalKind::Ring, cx, cy, 1.6, WARD_GREYS[3], now, 360, Some(id));
                self.puffs(cx, cy, 6, &WARD_GREYS[..3], 180.0, 0.6, 0.6, 700, 40);
            }
            // its time up: the shell sighs back into the smoke it was made of
            look::WARD_FADE => {
                let (cx, cy) = on.unwrap_or((x, y));
                self.wards.remove(&id);
                self.puffs(cx, cy, 12, &WARD_GREYS[..3], 200.0, 0.45, 1.3, 1100, 0);
                for i in 0..10 {
                    let a = i as f64 * 2.0 * PI / 10.0;
                    let c = if i % 3 == 0 { color } else { WARD_GREYS[2] };
                    let mut p = Particle::new(cx + 0.5 * a.cos(), cy + 0.5 * a.sin(), 0.3 + 0.5 * self.random(), c, 80.0, 700, Motion::Spark).moving(0.0, 0.0, 0.0018);
                    p.fade = true;
                    self.add(p);
                }
            }
            // the Last Rite: the broken ward bursts outward, shards of steel and violet fire tearing
            // out to the radius in a double shockwave, the rite's sigil burning into the floor
            look::LAST_RITE => {
                let (cx, cy) = on.unwrap_or((x, y));
                let hot = mix(color, 0xFFFFFF, 0.55);
                for (size, c, life) in [(1300.0, color, 260), (700.0, 0xFFFFFF, 170)] {
                    self.add(Particle::new(cx, cy, 0.5, c, size, life, Motion::Spark));
                }
                for i in 0..28 {
                    let a = i as f64 * 2.0 * PI / 28.0 + self.pm(0.05);
                    let s = (r / 260.0) * (0.8 + 0.4 * self.random());
                    let c = if i % 4 == 0 { 0xFFFFFF } else if i % 2 == 0 { hot } else { WARD_GREYS[2] };
                    let mut p = Particle::new(cx, cy, 0.4, c, 150.0, 260, Motion::Sparker).moving(a.cos() * s, a.sin() * s, 0.0);
                    p.fade = true;
                    self.add(p);
                }
                self.decal(DecalKind::Ring, cx, cy, r * 1.05, color, now, 480, None);
                self.decal(DecalKind::Ring, cx, cy, r * 0.8, 0xFFFFFF, now + 90, 380, None);
                self.decal(DecalKind::WardSigil, cx, cy, r, color, now, 900, None);
                self.debris(cx, cy, 22, &[color, hot, WARD_GREYS[2], WARD_GREYS[0]], 120.0, 4.5, 5.5, 900, 0);
                self.puffs(cx, cy, 10, &[0x2A2236, 0x463C58, WARD_GREYS[0]], 280.0, r * 0.5, 0.9, 1200, 60);
            }
            _ => {}
        }
    }

    /// A forcefield fading away: its dome shatters, shards of light thrown off its rim and
    /// falling.
    fn field_shatter(&mut self, a: Aura) {
        let st = field_style(a.color);
        let pale = mix(a.color, st.spark, 0.6);
        let n = (14.0 + a.radius * 5.0) as usize;
        for i in 0..n {
            let ang = i as f64 * 2.0 * PI / n as f64 + self.pm(0.1);
            let (sx, sy) = (a.x + a.radius * ang.cos(), a.y + a.radius * ang.sin());
            let c = self.pick(&[a.color, pale, st.spark, st.accent]);
            let s = 0.6 + 1.2 * self.random();
            let z = 0.3 + 1.6 * self.random();
            let mut p = Particle::new(sx, sy, z, c, 120.0 + 60.0 * self.random(), 700 + (self.random() * 400.0) as i32, Motion::Ballistic { g: GRAVITY * 0.5 })
                .moving(ang.cos() * s / 1000.0, ang.sin() * s / 1000.0, (1.0 + 2.0 * self.random()) / 1000.0);
            p.fade = true;
            self.add(p);
        }
        for i in 0..12 {
            let ang = 2.0 * PI * self.random();
            let d = a.radius * self.random().sqrt();
            let c = if i % 3 == 0 { st.spark } else { pale };
            let mut p = Particle::new(a.x + d * ang.cos(), a.y + d * ang.sin(), 0.1, c, 110.0, 800, Motion::Plain).moving(0.0, 0.0, 0.0022);
            p.fade = true;
            self.add(p);
        }
    }

    /// What drifts in a forcefield of the style `st`, by its tier: dust, rust, petals, embers...
    fn field_drift(&mut self, a: Aura, st: FieldStyle, fading: f64) {
        let (x, y, r) = (a.x, a.y, a.radius);
        let (ang, d) = (2.0 * PI * self.random(), r * 0.92 * self.random().sqrt());
        let (px, py) = (x + d * ang.cos(), y + d * ang.sin());
        match st.drift {
            Drift::Clear => {}
            Drift::Dust => {
                if self.random() < 0.5 {
                    let c = self.pick(&[0x6A7076, 0x8E949A, 0xB0B4B8]);
                    let mut p = Particle::new(px, py, 2.2 + self.random(), c, 70.0 * fading, 1800, Motion::Plain).moving(self.pm(0.1) / 1000.0, self.pm(0.1) / 1000.0, -0.0012);
                    p.fade = true;
                    self.add(p);
                }
                // a spark spitting off its rim now and then, as a failing field does
                if self.random() < 0.12 {
                    let a2 = 2.0 * PI * self.random();
                    let (sx, sy) = (x + r * a2.cos(), y + r * a2.sin());
                    for _ in 0..4 {
                        let b = a2 + self.pm(0.9);
                        let mut p = Particle::new(sx, sy, 0.3, st.spark, 80.0, 260, Motion::Spark).moving(b.cos() * 0.003, b.sin() * 0.003, 0.002);
                        p.fade = true;
                        self.add(p);
                    }
                }
            }
            Drift::Flakes => {
                if self.random() < 0.6 {
                    let c = self.pick(&[0x8A4020, 0xC0602A, 0x5A2A10, 0xD88040]);
                    let fall = 1300 + (self.random() * 600.0) as i32;
                    let mut p = Particle::new(px, py, 2.6 + self.random(), c, (60.0 + 50.0 * self.random()) * fading, fall, Motion::Ballistic { g: GRAVITY * 0.08 })
                        .moving(self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0, 0.0);
                    p.fade = true;
                    self.add(p);
                }
            }
            Drift::Verdigris => {
                if self.random() < 0.45 {
                    let c = self.pick(&[st.accent, 0x2E8A70, 0x9AE8D0]);
                    let mut p = Particle::new(px, py, 0.1, c, 70.0 * fading, 1400, Motion::Plain).moving(0.0, 0.0, 0.0014);
                    p.fade = true;
                    self.add(p);
                }
            }
            Drift::Petals => {
                if self.random() < 0.45 {
                    let c = self.pick(&[0xF0FFF6, 0xC8FFE8, a.color, 0xFFE8F4]);
                    let fall = 2200 + (self.random() * 800.0) as i32;
                    let motion = Motion::Soul { cx: px, cy: py, angle: 2.0 * PI * self.random(), speed: 0.004, rad: 0.25, out: 0.0, rise: -2.6 / fall as f64 };
                    let mut p = Particle::new(px, py, 2.8, c, (90.0 + 40.0 * self.random()) * fading, fall, motion);
                    p.fade = true;
                    self.add(p);
                }
            }
            Drift::Rays => {
                if self.random() < 0.5 {
                    let c = self.pick(&[0xFFFFFF, st.accent, a.color]);
                    let mut p = Particle::new(px, py, 0.1, c, 90.0 * fading, 900, Motion::Spark).moving(0.0, 0.0, 0.0035);
                    p.fade = true;
                    self.add(p);
                }
            }
            Drift::Embers => {
                let a2 = 2.0 * PI * self.random();
                let rr = r * (0.9 + 0.12 * self.random());
                let c = self.pick(&[a.color, st.accent, 0xFF3010, st.spark]);
                let mut p = Particle::new(x + rr * a2.cos(), y + rr * a2.sin(), 0.1, c, (110.0 + 60.0 * self.random()) * fading, 700, Motion::Spark)
                    .moving(self.pm(0.4) / 1000.0, self.pm(0.4) / 1000.0, 0.004 + 0.002 * self.random());
                p.fade = true;
                self.add(p);
            }
            Drift::Stars => {
                if self.random() < 0.55 {
                    let c = self.pick(&[0xFFFFFF, st.accent, a.color, 0xFFE0FF]);
                    let mut p = Particle::new(px, py, 0.3 + 2.0 * self.random(), c, (120.0 + 80.0 * self.random()) * fading, 600, Motion::Spark);
                    p.fade = true;
                    self.add(p);
                }
            }
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
            // the dusk pool breathing out mist, now and then a star climbing out of it
            look::DUSK_SPLASH => {
                let (ang, d) = (2.0 * PI * self.random(), a.radius * 0.75 * self.random().sqrt());
                let (px, py) = (x + d * ang.cos(), y + d * ang.sin());
                if self.random() < 0.22 {
                    let mut p = Particle::new(px, py, 0.1, 0xFFF4FF, 70.0 + 50.0 * self.random(), 900, Motion::Spark).moving(0.0, 0.0, 0.0016);
                    p.alpha = 0.9;
                    self.add(p);
                } else {
                    let c = self.pick(&[a.color, DUSK_DEEP, DUSK_MID, DUSK_PINK]);
                    let mut p = Particle::new(px, py, 0.05, c, (70.0 + 50.0 * self.random()) * left.max(0.35), 800, Motion::Plain)
                        .moving(self.pm(0.15) / 1000.0, self.pm(0.15) / 1000.0, 0.0009);
                    p.fade = true;
                    p.opacity = 0.45;
                    p.grow = 0.02;
                    self.add(p);
                }
            }
            // the plague cloud bubbling
            look::CONTAGION => {
                let (ang, d) = (2.0 * PI * self.random(), a.radius * 0.8 * self.random().sqrt());
                let c = self.pick(&[a.color, 0x4A7A20, 0xD0FF80]);
                let mut p = Particle::new(x + d * ang.cos(), y + d * ang.sin(), 0.05, c, 100.0 + 60.0 * self.random(), 800, Motion::Plain).moving(0.0, 0.0, 0.0012);
                p.fade = true;
                self.add(p);
            }
            // the grave-flames circling their keeper, as many as are left (the radius)
            look::GRAVE_FLAMES => {
                let n = a.radius.round().max(1.0) as usize;
                let spin = (now - a.start) as f64 * 0.004;
                for k in 0..n {
                    let ang = spin + k as f64 * 2.0 * PI / n as f64;
                    let (fx, fy) = (x + 0.9 * ang.cos(), y + 0.9 * ang.sin());
                    let c = self.pick(&[0xFFFFFF, 0xC0E8FF, a.color, a.color, 0x2440A0]);
                    let mut p = Particle::new(fx + self.pm(0.06), fy + self.pm(0.06), 0.45, c, 150.0, 300, Motion::Spark).moving(0.0, 0.0, 0.0025);
                    p.fade = true;
                    self.add(p);
                }
            }
            // white motes rising off the player the vow keeps
            look::VOW => {
                let angle = 2.0 * PI * self.random();
                let motion = Motion::Swirl { id: a.id, angle, speed: 0.08, rad: 0.55, out: 0.0, rise: 0.0022 };
                let mut p = Particle::new(x, y, 0.0, self.pick(&[0xFFFFFF, a.color, 0xFFC8E0]), 100.0, 700, motion);
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
            // the forcefield's wall: motes of light rising off its rim, so it stands up off the
            // floor; now and then one drifting up inside it, or a glint racing round the rim
            look::FORCEFIELD => {
                let st = field_style(a.color);
                let pale = mix(a.color, st.spark, 0.6);
                let fading = left.clamp(0.25, 1.0);
                let ang = 2.0 * PI * self.random();
                let rr = a.radius * (0.97 + 0.03 * self.random());
                let c = self.pick(&[a.color, pale, st.spark, a.color]);
                let rise = 0.0016 + 0.0016 * self.random();
                let mut p = Particle::new(x + rr * ang.cos(), y + rr * ang.sin(), 0.05, c, (100.0 + 50.0 * self.random()) * fading, 900, Motion::Plain).moving(0.0, 0.0, rise);
                p.fade = true;
                self.add(p);
                if self.random() < 0.3 {
                    let (ang, d) = (2.0 * PI * self.random(), a.radius * 0.85 * self.random().sqrt());
                    let c = self.pick(&[pale, st.accent, st.spark]);
                    let mut p = Particle::new(x + d * ang.cos(), y + d * ang.sin(), 0.1, c, 70.0, 1200, Motion::Plain)
                        .moving(self.pm(0.1) / 1000.0, self.pm(0.1) / 1000.0, 0.0012);
                    p.fade = true;
                    self.add(p);
                }
                if self.random() < 0.05 {
                    let ang = 2.0 * PI * self.random();
                    let dir = if self.random() < 0.5 { 1.0 } else { -1.0 };
                    let motion = Motion::Soul { cx: x, cy: y, angle: ang, speed: 0.09 * dir, rad: a.radius, out: 0.0, rise: 0.0 };
                    for k in 0..4 {
                        let mut p = Particle::new(x, y, 0.15, if k == 0 { st.spark } else { a.color }, 150.0 - k as f64 * 25.0, 520, motion.clone());
                        p.alpha = 0.0;
                        p.delay = k * 18;
                        self.add(p);
                    }
                }
                self.field_drift(a, st, fading);
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
        // a forcefield's time is up: it shatters
        for a in auras.iter().filter(|a| a.look == look::FORCEFIELD && now >= a.until) {
            self.muting = a.muting;
            self.field_shatter(*a);
            self.muting = 0.0;
        }
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

    /// `--effect 14x` (tests): tier x's forcefield (0 Worn to 8 Archangel, `Forcefield.COLORS`)
    /// raised on us, and raised again once it has faded.
    pub(super) fn test_field(&mut self, tier: u8, world: &World, now: i32) {
        const COLORS: [u32; 9] = [0x8E949A, 0xC0602A, 0xD08A3C, 0xCFDCEB, 0xFFD34A, 0x8CF0C8, 0xFFF0A8, 0xFF6A28, 0xB27CFF];
        let Some(me) = world.player() else { return };
        if self.fields.iter().any(|f| f.0 == me.id && now < f.1) {
            return;
        }
        let color = 0xFF00_0000 | COLORS[tier.min(8) as usize];
        let pos1 = bsk_proto::WorldPos { x: me.x as f32, y: me.y as f32 };
        let e = ShowEffect { effect_type: 41, target_object_id: me.id, pos1, pos2: bsk_proto::WorldPos { x: 6.5, y: look::FORCEFIELD as f32 }, color, duration: 12_000, object_type: 0, source_id: None };
        self.relic(&e, world, now);
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
            look::FORCEFIELD | look::FIELD_SEEN => (6.5, 0xFFFFD34A, 8000, false),
            look::FIELD_BLOCK => (0.9, 0xFFFFD34A, 0, true),
            look::WARD_CAST => (6.0, 0xFFE8C860, 7000, true),
            look::WARD_GAIN => (0.9, 0xFFE8C860, 7000, true),
            look::WARD_HIT => (0.9, 0xFFE8C860, 0, true),
            look::WARD_BREAK => (1.1, 0xFFE8C860, 0, true),
            look::WARD_FADE => (1.0, 0xFFE8C860, 0, true),
            look::LAST_RITE => (3.0, 0xFFC8A0FF, 0, true),
            look::CONTAGION => (3.0, 0xFF8FA547, 3000, false),
            look::GRAVE_FLAMES => (3.0, 0xFF60A0FF, 9000, true),
            look::GRAVE_FLAME => (1.5, 0xFF60A0FF, 0, false),
            look::VOW => (1.5, 0xFFF4E8FF, 1500, true),
            look::DUSK_THROW => (0.0, 0xFFC060FF, 1000, false),
            look::DUSK_SPLASH => (2.5, 0xFFC060FF, 0, false),
            look::DUSK_FLOW => (3.5, 0xFF9A48F0, 500, false),
            look::PASS_TIER => (3.0, 0xFF8FD0FF, 2200, true),
            look::PASS_GILDED => (3.0, 0xFFFFD24A, 2200, true),
            look::ARIA => (9.0, 0xFFC070FF, 3000, true),
            look::MORTAR_SHOT => (0.0, 0xFFFF6A14, 700, false),
            look::MORTAR_BLAST => (3.5, 0xFFFF6A14, 3000, false),
            look::BASTION => (3.5, 0xFFFF9A2A, 0, true),
            look::TIDE => (5.5, 0xFF46C2B4, 0, true),
            look::HOURS => (6.0, 0xFF4AF0FF, 1600, true),
            look::TURRET_THROW | look::TURRET_BUILD | look::TURRET_SEEN | look::TURRET_BLAST => {
                self.test_turret(which, world, now);
                return;
            }
            _ => return,
        };
        // (centred on us, where a shot can see it, but for what flies between us and somewhere else)
        let flies = matches!(which, look::SOUL_GATHER | look::SOUL_RELEASE | look::BLOOD | look::GRAVE_FLAME | look::DUSK_THROW);
        let pos1 = if mine || !flies { bsk_proto::WorldPos { x: mx, y: my } } else { bsk_proto::WorldPos { x: mx + 2.5, y: my } };
        // (a grave-flame flies from us)
        let source_id = (which == look::GRAVE_FLAME).then_some(me.id);
        let e = ShowEffect { effect_type: 41, target_object_id: me.id, pos1, pos2: bsk_proto::WorldPos { x: r, y: which as f32 }, color, duration: ms, object_type: 0, source_id };
        self.relic(&e, world, now);
        // the pentagram's heart erupts as it fills, as the server has it
        if which == look::PENTAGRAM {
            let e = ShowEffect { pos2: bsk_proto::WorldPos { x: 3.0, y: look::ERUPTION as f32 }, ..e };
            self.delayed.push((now + 1900, e));
        }
        // the vial's whole cascade where it lands, timed as the server's `CascadeDusk` has it
        if which == look::DUSK_THROW {
            let (cx, cy) = (pos1.x, pos1.y);
            let shades = [0xFFC060FF, 0xFF9A48F0, 0xFF6A50E0];
            let at = |a: f32, d: f32| bsk_proto::WorldPos { x: cx + a.cos() * d, y: cy + a.sin() * d };
            let fx = |pos1, r: f32, which: u8, color: u32, duration: i32, object_type: u16| ShowEffect {
                effect_type: 41,
                target_object_id: me.id,
                pos1,
                pos2: bsk_proto::WorldPos { x: r, y: which as f32 },
                color,
                duration,
                object_type,
                source_id: None,
            };
            let q = std::f32::consts::FRAC_PI_2;
            self.delayed.push((now + 1000, fx(pos1, 2.5, look::DUSK_SPLASH, shades[0], 0, 0)));
            for i in 0..4 {
                let a = i as f32 * q;
                self.delayed.push((now + 1000, fx(pos1, 3.5, look::DUSK_FLOW, shades[1], 500, (a.to_degrees().round() as i32).rem_euclid(360) as u16)));
                self.delayed.push((now + 1500, fx(at(a, 3.5), 3.0, look::DUSK_SPLASH, shades[1], 0, 1)));
                for side in [-0.5f32, 0.5] {
                    let to = at(a + side * q, 6.5);
                    let from = at(a, 3.5);
                    let (dx, dy) = (to.x - from.x, to.y - from.y);
                    let deg = (dy.atan2(dx).to_degrees().round() as i32).rem_euclid(360) as u16;
                    self.delayed.push((now + 1500, fx(from, dx.hypot(dy), look::DUSK_FLOW, shades[2], 500, deg)));
                }
                self.delayed.push((now + 2000, fx(at(a + q / 2.0, 6.5), 3.0, look::DUSK_SPLASH, shades[2], 0, 2)));
            }
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
pub(super) struct Pen<'a> {
    pub(super) cam: &'a Camera,
    pub(super) renderer: &'a mut Renderer,
    pub(super) x: f64,
    pub(super) y: f64,
    pub(super) z: f64,
    /// Every alpha times this (another player's decal, faded).
    pub(super) dim: f32,
}

impl Pen<'_> {
    /// The point `r` tiles out at angle `a`.
    pub(super) fn at(&self, r: f64, a: f64) -> [f32; 2] {
        self.cam.to_screen(self.x + r * a.cos(), self.y + r * a.sin(), self.z)
    }

    pub(super) fn pt(&self, x: f64, y: f64) -> [f32; 2] {
        self.cam.to_screen(self.x + x, self.y + y, self.z)
    }

    pub(super) fn tri(&mut self, pts: [[f32; 2]; 3], rgb: u32, alpha: f32) {
        let alpha = alpha * self.dim;
        if alpha <= 0.005 {
            return;
        }
        let white = self.renderer.white();
        self.renderer.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(rgb, alpha.min(1.0)));
    }

    pub(super) fn quad(&mut self, p: [[f32; 2]; 4], rgb: u32, alpha: f32) {
        self.tri([p[0], p[1], p[2]], rgb, alpha);
        self.tri([p[0], p[2], p[3]], rgb, alpha);
    }

    /// A filled circle of radius `r` round (ox, oy) from the spot.
    pub(super) fn disc_at(&mut self, ox: f64, oy: f64, r: f64, rgb: u32, alpha: f32) {
        let sides = if r < 0.6 { 14 } else { 36 };
        let c = self.pt(ox, oy);
        for i in 0..sides {
            let (a0, a1) = (2.0 * PI * i as f64 / sides as f64, 2.0 * PI * (i + 1) as f64 / sides as f64);
            let (p0, p1) = (self.pt(ox + r * a0.cos(), oy + r * a0.sin()), self.pt(ox + r * a1.cos(), oy + r * a1.sin()));
            self.tri([c, p0, p1], rgb, alpha);
        }
    }

    pub(super) fn disc(&mut self, r: f64, rgb: u32, alpha: f32) {
        self.disc_at(0.0, 0.0, r, rgb, alpha);
    }

    /// A ring between `r0` and `r1`.
    pub(super) fn band(&mut self, r0: f64, r1: f64, rgb: u32, alpha: f32) {
        const SIDES: usize = 40;
        let r0 = r0.max(0.0);
        for i in 0..SIDES {
            let (a0, a1) = (2.0 * PI * i as f64 / SIDES as f64, 2.0 * PI * (i + 1) as f64 / SIDES as f64);
            self.quad([self.at(r1, a0), self.at(r1, a1), self.at(r0, a1), self.at(r0, a0)], rgb, alpha);
        }
    }

    /// A line `w` tiles wide between two points (tiles from the spot).
    pub(super) fn line(&mut self, a: (f64, f64), b: (f64, f64), w: f64, rgb: u32, alpha: f32) {
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
        DecalKind::Clock => {
            // the face (its last fifth fading), the rim and an inner ring of brass
            let a = envelope(t, 0.08, 0.2);
            let turns = (t / 0.8).min(1.0) * 2.0;
            pen.disc(r, 0x0A1A2A, 0.35 * a);
            pen.band(r - 0.14, r, d.color, 0.85 * a);
            pen.band(r * 0.6 - 0.06, r * 0.6, 0xD0A040, 0.6 * a);
            // the twelve marks, the quarters longer
            for k in 0..12 {
                let ang = -PI / 2.0 + k as f64 * PI / 6.0;
                let (len, w) = if k % 3 == 0 { (0.55, 0.035) } else { (0.3, 0.025) };
                pen.quad([pen.at(r - 0.18, ang - w), pen.at(r - 0.18 - len, ang - w), pen.at(r - 0.18 - len, ang + w), pen.at(r - 0.18, ang + w)], 0xD8FFFF, 0.9 * a);
            }
            // the hand, from 12 o'clock clockwise, and the light it leaves behind
            let hand = -PI / 2.0 + turns * 2.0 * PI;
            if t < 0.8 {
                for k in 1..8 {
                    let back = hand - k as f64 * 0.07;
                    pen.quad([pen.at(0.2, back - 0.07), pen.at(r - 0.25, back - 0.07), pen.at(r - 0.25, back), pen.at(0.2, back)], d.color, 0.32 * a * (1.0 - k as f32 / 8.0));
                }
            }
            pen.quad([pen.at(0.25, hand - 1.3), pen.at(r - 0.25, hand - 0.03), pen.at(r - 0.25, hand + 0.03), pen.at(0.25, hand + 1.3)], 0xF0FFFF, 0.95 * a);
            // the hour hand, turning a twelfth as slowly, and the pin
            let hour = -PI / 2.0 + turns * PI / 6.0;
            pen.quad([pen.at(0.2, hour - 1.3), pen.at(r * 0.45, hour - 0.06), pen.at(r * 0.45, hour + 0.06), pen.at(0.2, hour + 1.3)], 0xD0A040, 0.9 * a);
            pen.disc(0.22, 0xD0A040, a);
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
        DecalKind::Forcefield => draw_forcefield(pen, d, now),
        DecalKind::FieldFlare => {
            let a = envelope(t, 0.0, 0.7);
            let grow = 1.0 - (1.0 - (t / 0.35).min(1.0)).powi(2);
            let rr = r * (0.55 + 0.45 * grow);
            let st = field_style(d.color);
            let pale = mix(d.color, st.accent, 0.5);
            let corner = |i: usize, rad: f64| {
                let ang = PI / 6.0 + i as f64 * PI / 3.0;
                (rad * ang.cos(), rad * ang.sin())
            };
            for i in 0..6 {
                let (p0, p1) = (corner(i, rr), corner(i + 1, rr));
                pen.tri([pen.pt(0.0, 0.0), pen.pt(p0.0, p0.1), pen.pt(p1.0, p1.1)], d.color, 0.22 * a);
                pen.line(p0, p1, 0.14, d.color, 0.45 * a);
                pen.line(p0, p1, 0.05, st.spark, 0.95 * a);
                let (q0, q1) = (corner(i, rr * 0.55), corner(i + 1, rr * 0.55));
                pen.line(q0, q1, 0.04, pale, 0.7 * a);
            }
        }
        DecalKind::WardSigil => {
            let a = envelope(t, 0.12, 0.5);
            let spin = secs * 0.8;
            let open = 1.0 - (1.0 - (t / 0.2).min(1.0)).powi(2);
            let rr = r * (0.6 + 0.4 * open);
            pen.disc(rr, WARD_GREYS[0], 0.18 * a);
            pen.band(rr * 0.93, rr, WARD_GREYS[2], 0.8 * a);
            pen.band(rr * 0.9, rr * 0.92, d.color, 0.7 * a);
            pen.band(rr * 0.6, rr * 0.65, d.color, 0.6 * a);
            // eight shield plates turning round it, each a kite pointing out, interlocked at their
            // shoulders, round a plain square
            for k in 0..8 {
                let mid = spin + k as f64 * PI / 4.0;
                let (inner, shoulder, tip) = (pen.at(rr * 0.3, mid), pen.at(rr * 0.6, mid), pen.at(rr * 0.88, mid));
                let (l, rt) = (pen.at(rr * 0.55, mid - 0.32), pen.at(rr * 0.55, mid + 0.32));
                let fill = if k % 2 == 0 { WARD_GREYS[2] } else { WARD_GREYS[1] };
                pen.tri([inner, l, tip], fill, 0.55 * a);
                pen.tri([inner, tip, rt], mix(fill, 0x000000, 0.15), 0.55 * a);
                pen.tri([l, shoulder, rt], WARD_GREYS[3], 0.35 * a);
            }
            for k in 0..4 {
                let a0 = -spin + PI / 4.0 + k as f64 * PI / 2.0;
                let a1 = a0 + PI / 2.0;
                let s = rr * 0.24;
                pen.line((s * a0.cos(), s * a0.sin()), (s * a1.cos(), s * a1.sin()), rr * 0.04, WARD_GREYS[3], 0.8 * a);
            }
            for i in 0..24 {
                let ang = -spin * 0.5 + i as f64 * 2.0 * PI / 24.0;
                let (i0, i1) = (rr * if i % 2 == 0 { 0.68 } else { 0.72 }, rr * 0.84);
                pen.line((i0 * ang.cos(), i0 * ang.sin()), (i1 * ang.cos(), i1 * ang.sin()), rr * 0.025, d.color, 0.6 * a);
            }
            pen.disc(rr * 0.18, d.color, 0.45 * a);
            pen.disc(rr * 0.09, 0xFFFFFF, 0.6 * a);
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

/// How far a spring has gone at `x` (0 to 1): past its end a little, then back (`easeOutBack`).
fn overshoot(x: f64) -> f64 {
    let x = x.clamp(0.0, 1.0);
    let (c1, c3) = (1.70158, 2.70158);
    1.0 + c3 * (x - 1.0).powi(3) + c1 * (x - 1.0).powi(2)
}

/// The Ascendant's forcefield (`DecalKind::Forcefield`): springing open, a dome of light brighter
/// toward its rim, a lattice of hexagons with ripples of light running out across it and cells
/// glinting, two rings of runes turning against each other, a ring of six hex cells at its heart; it
/// flickers in its last two seconds and fades.
fn draw_forcefield(pen: &mut Pen, d: &Decal, now: i32) {
    let since = (now - d.start) as f64;
    let left = d.ms as f64 - since;
    let secs = since / 1000.0;
    let r = d.radius;
    let rr = r * overshoot(since / 420.0);
    if rr <= 0.05 {
        return;
    }
    let st = field_style(d.color);
    let white = st.spark;
    let pale = mix(d.color, white, 0.6);
    let azure = st.accent;
    let fade = ((left / 700.0).min(1.0) * (since / 120.0).min(1.0)).clamp(0.0, 1.0) as f32;
    let flicker = if left < 2000.0 { 0.55 + 0.45 * ((secs * 23.0).sin() * (secs * 7.0).cos()).abs() as f32 } else { 1.0 };
    // the lower tiers' fields stutter all along: now and then a dip, as a failing light does
    let stutter = if st.unsteady > 0.0 {
        let n = hash(d.seed ^ 0x5157, (since / 60.0) as u64);
        if n < st.unsteady * 0.25 { 0.3 } else { 1.0 - (st.unsteady * 0.35 * n) as f32 }
    } else {
        1.0
    };
    let breathe = 0.88 + 0.12 * (secs * 2.6).sin() as f32;
    let a = fade * flicker * stutter;

    // the dome's body: faint in the middle, brighter toward the rim
    const BANDS: usize = 7;
    for k in 0..BANDS {
        let (f0, f1) = (k as f64 / BANDS as f64, (k + 1) as f64 / BANDS as f64);
        let alpha = 0.035 + 0.17 * f1.powi(3) as f32;
        pen.band(rr * f0, rr * f1, mix(d.color, azure, 0.25 * (1.0 - f1)), alpha * a * breathe);
    }
    // a flash as it opens
    if since < 500.0 {
        pen.disc(rr, white, 0.35 * (1.0 - since / 500.0) as f32 * fade);
    }

    // God's Blessing: shafts of light turning slowly over the dome
    if st.drift == Drift::Rays {
        let spin = secs * 0.25;
        for i in 0..12 {
            let ang = spin + i as f64 * PI / 6.0;
            let wide = 0.09 + 0.04 * (secs * 1.3 + i as f64).sin();
            pen.tri([pen.pt(0.0, 0.0), pen.at(rr, ang - wide), pen.at(rr, ang + wide)], if i % 2 == 0 { white } else { azure }, 0.1 * a * breathe);
        }
    }

    // the lattice: hexagons across the dome, clearer toward its rim, ripples of light running out
    // and a cell glinting here and there
    let s = 0.62;
    let (w, h) = (s * 3f64.sqrt(), s * 1.5);
    let n = (rr / h).ceil() as i32 + 1;
    let corner = |cx: f64, cy: f64, i: usize| {
        let ang = PI / 6.0 + i as f64 * PI / 3.0;
        (cx + s * ang.cos(), cy + s * ang.sin())
    };
    let glint_step = (secs * 3.0).floor() as u64;
    for row in -n..=n {
        for col in -n..=n {
            let cx = (col as f64 + if row.rem_euclid(2) == 1 { 0.5 } else { 0.0 }) * w;
            let cy = row as f64 * h;
            let dist = cx.hypot(cy);
            if dist + s > rr {
                continue;
            }
            let cell = ((row + 64) * 131 + col + 64) as u64;
            // a worn or rusted field's lattice has holes in it, here and there
            if st.unsteady > 0.0 && hash(d.seed ^ 0xC0DE, cell) < st.unsteady * 0.6 {
                continue;
            }
            let edge = (dist / rr).powf(1.4) as f32;
            let wave = ((dist * 1.9 - secs * 5.5).sin().max(0.0)).powi(6) as f32;
            let alpha = (0.1 + 0.35 * edge + 0.55 * wave) * a;
            if hash(d.seed, cell * 7 + glint_step) > 0.94 {
                let glow = (1.0 - (secs * 3.0).fract()) as f32;
                for i in 0..6 {
                    let (p0, p1) = (corner(cx, cy, i), corner(cx, cy, i + 1));
                    pen.tri([pen.pt(cx, cy), pen.pt(p0.0, p0.1), pen.pt(p1.0, p1.1)], pale, 0.28 * glow * a);
                }
            }
            // three sides each, so shared sides aren't drawn twice
            for i in 0..3 {
                let (p0, p1) = (corner(cx, cy, i), corner(cx, cy, i + 1));
                pen.line(p0, p1, 0.045, if wave > 0.4 { white } else if (row + col) % 3 == 0 { mix(pale, azure, 0.5) } else { pale }, alpha);
            }
        }
    }

    // ripples running out from its heart
    let phase = (secs / 1.6).fract();
    let pr = rr * phase;
    pen.band(pr - 0.14, pr, pale, 0.35 * (1.0 - phase) as f32 * a);

    // the rings of runes: golden arcs turning one way, pale ones the other, a rune at each end
    let arcs = |pen: &mut Pen, rad: f64, spin: f64, n: usize, len: f64, width: f64, rgb: u32, alpha: f32| {
        for k in 0..n {
            let start = spin + k as f64 * 2.0 * PI / n as f64;
            const STEPS: usize = 10;
            for j in 0..STEPS {
                let (a0, a1) = (start + len * j as f64 / STEPS as f64, start + len * (j + 1) as f64 / STEPS as f64);
                pen.quad([pen.at(rad + width / 2.0, a0), pen.at(rad + width / 2.0, a1), pen.at(rad - width / 2.0, a1), pen.at(rad - width / 2.0, a0)], rgb, alpha);
            }
            for end in [start, start + len] {
                let (c, sn) = (end.cos(), end.sin());
                let (px, py) = (rad * c, rad * sn);
                let k = width * 1.6;
                pen.quad([pen.pt(px + k * c, py + k * sn), pen.pt(px - k * sn, py + k * c), pen.pt(px - k * c, py - k * sn), pen.pt(px + k * sn, py - k * c)], white, alpha);
            }
        }
    };
    arcs(pen, rr * 0.86, secs * 0.6, 3, 1.1, 0.07, d.color, 0.8 * a);
    arcs(pen, rr * 0.74, -secs * 0.9, 4, 0.7, 0.05, mix(pale, azure, 0.4), 0.65 * a);

    // the Archangel's: a third ring of runes, and stars wheeling round outside its rim
    if st.drift == Drift::Stars {
        arcs(pen, rr * 0.62, secs * 1.3, 6, 0.4, 0.04, azure, 0.6 * a);
        for i in 0..8 {
            let ang = -secs * 0.5 + i as f64 * PI / 4.0;
            let (sx, sy) = ((rr + 0.35) * ang.cos(), (rr + 0.35) * ang.sin());
            let tw = (0.55 + 0.45 * (secs * 5.0 + i as f64 * 1.9).sin()) as f32;
            let k = 0.1 + 0.14 * tw as f64;
            pen.line((sx - k, sy), (sx + k, sy), 0.05, white, 0.9 * a * tw);
            pen.line((sx, sy - k), (sx, sy + k), 0.05, white, 0.9 * a * tw);
            pen.disc_at(sx, sy, 0.07, if i % 2 == 0 { azure } else { d.color }, a);
        }
    }

    // the rim: a bright edge, white at its line, glowing out a little
    pen.band(rr, rr + 0.22, d.color, 0.16 * a * breathe);
    pen.band(rr - 0.18, rr, d.color, 0.8 * a * breathe);
    pen.band(rr - 0.06, rr, white, 0.75 * a);
    if st.drift == Drift::Stars {
        pen.band(rr + 0.5, rr + 0.56, azure, 0.45 * a * breathe);
    }

    // the Seraphim's: tongues of flame licking up all round the rim
    if st.drift == Drift::Embers {
        const TONGUES: usize = 40;
        for i in 0..TONGUES {
            let ang = i as f64 * 2.0 * PI / TONGUES as f64 + secs * 0.15;
            let lick = 0.5 + 0.5 * (secs * 9.0 + i as f64 * 2.3).sin() * (secs * 4.0 + i as f64 * 0.7).cos();
            let tall = 0.18 + 0.38 * lick;
            let wide = PI / TONGUES as f64 * 0.9;
            let tip = pen.at(rr + tall, ang + 0.04 * (secs * 6.0 + i as f64).sin());
            pen.tri([pen.at(rr - 0.05, ang - wide), tip, pen.at(rr - 0.05, ang + wide)], if i % 2 == 0 { d.color } else { azure }, 0.55 * a);
            let tip = pen.at(rr + tall * 0.55, ang);
            pen.tri([pen.at(rr - 0.03, ang - wide * 0.5), tip, pen.at(rr - 0.03, ang + wide * 0.5)], white, 0.5 * a);
        }
    }

    // a ring of six hex cells of the lattice turning at its heart
    let spin = secs * 0.8;
    pen.disc(0.55, pale, 0.25 * a * breathe);
    for cell in 0..6 {
        let at = spin + cell as f64 * PI / 3.0;
        let (cx, cy) = (0.42 * at.cos(), 0.42 * at.sin());
        let color = if cell % 2 == 0 { white } else { mix(white, azure, 0.5) };
        for i in 0..6 {
            let (a0, a1) = (spin + i as f64 * PI / 3.0, spin + (i + 1) as f64 * PI / 3.0);
            pen.line((cx + 0.17 * a0.cos(), cy + 0.17 * a0.sin()), (cx + 0.17 * a1.cos(), cy + 0.17 * a1.sin()), 0.045, color, 0.85 * a);
        }
    }
}

/// The Crucible's champions (`BehaviorDb.Crucible.cs`), by their object ids: which one, for their
/// auras and their phases' bursts (`champion_auras`, `champion_cue`); and its portal in the Nexus.
#[derive(Clone, Copy, PartialEq, Debug)]
enum Champion {
    Ferrogrim,
    Nerissa,
    Pyrrhax,
    Chronarch,
    Vaelith,
    /// one of the four ghosts on the Apex, in its floor's colour
    Echo(u32),
    /// the Crucible Portal, sunk into the Nexus's floor
    Portal,
}

/// The Crucible's squares we knew had ground, to tell ground built (the ways up and the floors
/// coming together, `CrucibleWorld`) and ground fallen from ground just come into sight.
#[derive(Default)]
pub struct GroundWatch {
    world: u64,
    seen: usize,
    known: Vec<bool>,
    /// Since when squares come in as sight does (arriving, a jump), not as they're built.
    settling: i32,
    at: (f64, f64),
}

fn champion(id: &str) -> Option<Champion> {
    Some(match id {
        "Ferrogrim, the Iron Warden" => Champion::Ferrogrim,
        "Nerissa, the Tidecaller" => Champion::Nerissa,
        "Pyrrhax, the Forge Tyrant" => Champion::Pyrrhax,
        "Chronarch, the Hour Thief" => Champion::Chronarch,
        "Vaelith, the Crucible Sovereign" => Champion::Vaelith,
        "Echo of Ferrogrim" => Champion::Echo(0xBEC8E6),
        "Echo of Nerissa" => Champion::Echo(0x78F0DC),
        "Echo of Pyrrhax" => Champion::Echo(0xFFAA5A),
        "Echo of Chronarch" | "Hour Echo" => Champion::Echo(0x78E6FF),
        "Crucible Portal" => Champion::Portal,
        _ => return None,
    })
}

impl Particles {
    /// Every frame (`dt` ms): what each of the Crucible's champions near us gives off, by who it
    /// is and which form it's in (its `AltTexture`, stat 61: the phase it's in): Ferrogrim's forge
    /// sparks (a whirl of them in his Tempest), the sea round Nerissa (a maelstrom of spray), the
    /// fire off Pyrrhax (flames all over him once his Core is molten), Chronarch's gears turning
    /// round him (the other way in his Reversal), Vaelith's feathers of flame falling (rising light
    /// once Ascended, a spiral of the void once Unbound), and wisps off the echoes. Each builds as
    /// its fight goes on (`fury`, how much of its health is gone): thicker as it's hurt, with more
    /// of its floor joining in once a third is gone and again at two thirds. And the Crucible
    /// Portal's embers, the four floors' colours circling it and its pulse of gold.
    pub fn champion_auras(&mut self, world: &World, data: &bsk_data::GameData, dt: i32) {
        if self.disabled || dt <= 0 {
            return;
        }
        let Some(me) = world.player() else { return };
        let (mx, my) = (me.x, me.y);
        let near: Vec<(i32, f64, f64, f64, Champion, i32, f64)> = world
            .objects
            .values()
            .filter(|o| (o.x - mx).hypot(o.y - my) <= 18.0)
            .filter_map(|o| {
                let c = champion(&data.objects.get(&o.object_type)?.id)?;
                let (max, hp) = (o.stat_int(0).unwrap_or(0), o.stat_int(1).unwrap_or(0));
                let fury = if max > 0 { (1.0 - hp as f64 / max as f64).clamp(0.0, 1.0) } else { 0.0 };
                Some((o.id, o.x, o.y, o.z, c, o.stat_int(61).unwrap_or(0), fury))
            })
            .collect();
        let dt = dt.min(100) as f64;
        let ticked = |now: i32, every: i32| now / every != (now - dt as i32) / every;
        for (id, x, y, z, who, form, fury) in near {
            // its particles and decals on its own floor and height, not ours or the last effect's
            // (on the Nexus's stacked floors the portal's pulse and embers were a floor up, 3 tiles
            // off it, seen from the spawn)
            let z = if world.levels.is_some() { z } else { 0.0 };
            self.floor = z;
            self.here = (z, world.levels.as_ref().map_or(0, |l| ((z + 0.01) / l.height).floor().max(0.0) as u8));
            // about `rate` a second, as dt allows, more as the fight goes on
            let more = 1.0 + 1.5 * fury;
            let due = |p: &mut Self, rate: f64| {
                let n = rate * more * dt / 1000.0;
                let whole = n.floor() as usize;
                whole + usize::from(p.random() < n - n.floor())
            };
            let (wounded, desperate) = (fury >= 0.34, fury >= 0.66);
            let now = self.now;
            match who {
                Champion::Ferrogrim => {
                    // sparks spat from the forge in his chest and off his hammer
                    for _ in 0..due(self, if form > 0 { 26.0 } else { 9.0 }) {
                        let c = self.pick(&[0xFFE29A, 0xFF9A2A, 0xD8600E]);
                        let (vx, vy) = (self.pm(1.2) / 1000.0, self.pm(1.2) / 1000.0);
                        let p = Particle::new(x + self.pm(0.5), y + self.pm(0.3), 1.0 + self.random() * 0.8, c, 70.0 + 40.0 * self.random(), 500, Motion::Ballistic { g: GRAVITY * 0.6 })
                            .moving(vx, vy, 0.002 + 0.002 * self.random());
                        self.add(p);
                    }
                    if wounded {
                        // his seams split: molten iron dripping off him and hissing on the floor
                        for _ in 0..due(self, 4.0) {
                            let c = self.pick(&[0xFFE29A, 0xFF9A2A]);
                            let p = Particle::new(x + self.pm(0.7), y + self.pm(0.4), 0.9, c, 90.0, 700, Motion::Ballistic { g: GRAVITY });
                            self.add(p);
                        }
                    }
                    if desperate && ticked(now, 1600) {
                        // the forge's heart beating through the floor
                        self.decal(DecalKind::Ring, x, y, 2.6, 0xFF9A2A, now, 420, Some(id));
                        self.decal(DecalKind::Cracks, x, y, 1.8, 0xFF9A2A, now, 900, None);
                    }
                    if form > 0 {
                        // the Tempest: a ring of fire whirling round him with the hammer
                        for _ in 0..due(self, 30.0) {
                            let a = 2.0 * PI * self.random();
                            let c = self.pick(&[0xFFE29A, 0xFF9A2A]);
                            let mut p = Particle::new(x, y, 0.8, c, 110.0, 450, Motion::Swirl { id, angle: a, speed: 0.22, rad: 1.6 + 0.4 * self.random(), out: 0.0, rise: 0.0004 });
                            p.fade = true;
                            self.add(p);
                        }
                    }
                }
                Champion::Nerissa => {
                    // droplets circling her, and bubbles rising off her
                    let (rate, speed, out) = if form > 0 { (40.0, 0.18, 0.0018) } else { (12.0, 0.07, 0.0002) };
                    for _ in 0..due(self, rate) {
                        let a = 2.0 * PI * self.random();
                        let c = self.pick(&[0x46C2B4, 0x8AF0E0, 0xD8FFFF, 0x1A8288]);
                        let mut p = Particle::new(x, y, 0.3 + self.random() * 1.2, c, 80.0 + 50.0 * self.random(), 900, Motion::Swirl { id, angle: a, speed, rad: 1.0 + 0.5 * self.random(), out, rise: 0.0006 });
                        p.fade = true;
                        self.add(p);
                    }
                    for _ in 0..due(self, 4.0) {
                        let mut p = Particle::new(x + self.pm(0.6), y + self.pm(0.4), 0.2, 0xD8FFFF, 90.0, 900, Motion::Plain).moving(0.0, 0.0, 0.0018);
                        p.fade = true;
                        self.add(p);
                    }
                    if wounded && ticked(now, 1800) {
                        // the sea answering her: ripples spreading out round her
                        self.decal(DecalKind::Ring, x, y, 2.2, 0x8AF0E0, now, 700, Some(id));
                        self.decal(DecalKind::Ring, x, y, 3.4, 0x46C2B4, now + 250, 800, Some(id));
                    }
                    if desperate && self.random() < dt / 260.0 {
                        // columns of water rising round her and falling back as spray
                        let a = 2.0 * PI * self.random();
                        let (cx, cy) = (x + 1.8 * a.cos(), y + 1.8 * a.sin());
                        self.flames(cx, cy, 0.35, 3, &[0xD8FFFF, 0x8AF0E0, 0x46C2B4], 160.0, 4.0, 500, 0);
                        for i in 0..6 {
                            let b = 2.0 * PI * i as f64 / 6.0;
                            let mut p = Particle::new(cx, cy, 1.6, 0xD8FFFF, 70.0, 600, Motion::Ballistic { g: GRAVITY * 0.6 })
                                .moving(b.cos() * 0.0015, b.sin() * 0.0015, 0.006);
                            p.delay = 150;
                            self.add(p);
                        }
                    }
                }
                Champion::Pyrrhax => {
                    // embers and smoke off him; flames all over him once molten, slag dripping
                    for _ in 0..due(self, if form > 0 { 30.0 } else { 12.0 }) {
                        let c = self.pick(&[0xFFF2A6, 0xFF9A24, 0xE0400C]);
                        let p = Particle::new(x + self.pm(0.8), y + self.pm(0.5), 0.6 + self.random() * 1.2, c, 80.0 + 50.0 * self.random(), 900, Motion::Spark)
                            .moving(self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0, 0.0022 + 0.0015 * self.random());
                        self.add(p);
                    }
                    for _ in 0..due(self, 2.5) {
                        let c = self.pick(&[0x2A1A18, 0x3A2420, 0x4A3028]);
                        let mut p = Particle::new(x + self.pm(0.5), y + self.pm(0.3), 1.8, c, 220.0, 1400, Motion::Plain).moving(0.0, 0.0, 0.0009);
                        p.fade = true;
                        self.add(p);
                    }
                    if wounded && ticked(now, 2200) {
                        // the floor scorching under him
                        let (sx, sy) = (x + self.pm(0.6), y + self.pm(0.6));
                        self.decal(DecalKind::Scorch, sx, sy, 1.4, 0xE0400C, now, 2400, None);
                    }
                    if desperate && self.random() < dt / 300.0 {
                        // lava bursting up through the floor round him
                        let a = 2.0 * PI * self.random();
                        let (cx, cy) = (x + 2.2 * a.cos(), y + 2.2 * a.sin());
                        self.decal(DecalKind::Ring, cx, cy, 0.8, 0xFF9A24, now, 300, None);
                        self.debris(cx, cy, 10, &[0xFFF2A6, 0xFF9A24, 0xE0400C], 90.0, 2.0, 7.0, 700, 0);
                    }
                    if form > 0 && self.random() < dt / 120.0 {
                        self.flames(x, y, 0.8, 2, &[0xFFF2A6, 0xFF9A24, 0xE0400C], 200.0, 3.0, 500, 0);
                        let p = Particle::new(x + self.pm(0.6), y + self.pm(0.4), 0.8, 0xFF9A24, 90.0, 800, Motion::Ballistic { g: GRAVITY });
                        self.add(p);
                    }
                }
                Champion::Chronarch => {
                    // brass and light turning round him like a clock's works (backwards, and
                    // magenta, in his Reversal)
                    let back = form > 0;
                    for _ in 0..due(self, 10.0) {
                        let a = 2.0 * PI * self.random();
                        let c = if back { self.pick(&[0xFF6AE8, 0xF0B0FF, 0xD0A040]) } else { self.pick(&[0x4AF0FF, 0xD0A040, 0xF2D282]) };
                        let mut p = Particle::new(x, y, 1.0, c, 90.0, 1400, Motion::Swirl { id, angle: a, speed: if back { -0.06 } else { 0.06 }, rad: 1.4, out: 0.0, rise: 0.0 });
                        p.fade = true;
                        self.add(p);
                    }
                    // a tick: once a second the works pulse (twice, as time runs short)
                    if ticked(now, if desperate { 500 } else { 1000 }) {
                        let c = if back { 0xFF6AE8 } else { 0x4AF0FF };
                        self.decal(DecalKind::Ring, x, y, 1.6, c, now, 300, Some(id));
                    }
                    if wounded && ticked(now, 3000) {
                        // the clock's face showing under him
                        let c = if back { 0xFF6AE8 } else { 0xD0A040 };
                        self.decal(DecalKind::Clock, x, y, 2.8, c, now, 2600, Some(id));
                    }
                    if desperate {
                        // time tearing round him: light running both ways at once
                        for _ in 0..due(self, 8.0) {
                            let a = 2.0 * PI * self.random();
                            let speed = if self.random() < 0.5 { 0.14 } else { -0.14 };
                            let c = self.pick(&[0xFFFFFF, 0x4AF0FF, 0xFF6AE8]);
                            let mut p = Particle::new(x, y, 0.6, c, 70.0, 600, Motion::Swirl { id, angle: a, speed, rad: 2.2, out: 0.0, rise: 0.0 });
                            p.fade = true;
                            self.add(p);
                        }
                    }
                }
                Champion::Vaelith => {
                    let (rate, colors): (f64, &[u32]) = match form {
                        0 => (8.0, &[0xFFD24A, 0xFFF0A0, 0xFFA024]),
                        1 => (16.0, &[0xFFF0A0, 0xFFFFFF, 0xFFD24A]),
                        _ => (22.0, &[0xA828D8, 0xF0A0FF, 0x5A0E7A]),
                    };
                    for _ in 0..due(self, rate) {
                        let c = self.pick(colors);
                        let p = if form >= 2 {
                            // the void spiralling up round him
                            let a = 2.0 * PI * self.random();
                            let mut p = Particle::new(x, y, 0.2, c, 100.0, 1000, Motion::Swirl { id, angle: a, speed: 0.12, rad: 1.2, out: 0.0004, rise: 0.0025 });
                            p.fade = true;
                            p
                        } else if form == 1 {
                            // light streaming up off him
                            Particle::new(x + self.pm(0.7), y + self.pm(0.4), 0.3, c, 90.0, 800, Motion::Spark).moving(0.0, 0.0, 0.004)
                        } else {
                            // feathers of flame drifting down from his wings
                            let mut p = Particle::new(x + self.pm(1.4), y + self.pm(0.6), 2.2, c, 100.0, 1600, Motion::Ballistic { g: GRAVITY * 0.05 })
                                .moving(self.pm(0.3) / 1000.0, self.pm(0.3) / 1000.0, 0.0);
                            p.fade = true;
                            p
                        };
                        self.add(p);
                    }
                    let glow = if form >= 2 { 0xA828D8 } else { 0xFFD24A };
                    if wounded && ticked(now, 3200) {
                        // the tower's sigil burning under him
                        self.decal(DecalKind::Sigil, x, y, 3.0, glow, now, 2800, Some(id));
                    }
                    if desperate && self.random() < dt / 240.0 {
                        // pillars of light rising round him
                        let a = 2.0 * PI * self.random();
                        let (cx, cy) = (x + 2.6 * a.cos(), y + 2.6 * a.sin());
                        for i in 0..10 {
                            let c = if i % 3 == 0 { 0xFFFFFF } else { glow };
                            let mut p = Particle::new(cx + self.pm(0.15), cy + self.pm(0.15), 0.2, c, 110.0, 700, Motion::Spark).moving(0.0, 0.0, 0.006 + 0.002 * self.random());
                            p.delay = i * 30;
                            self.add(p);
                        }
                    }
                }
                Champion::Echo(tint) => {
                    for _ in 0..due(self, 8.0) {
                        let c = mix(tint, 0xFFFFFF, self.random() * 0.5);
                        let mut p = Particle::new(x + self.pm(0.6), y + self.pm(0.4), 0.4 + self.random(), c, 120.0, 900, Motion::Plain).moving(0.0, 0.0, 0.0012);
                        p.fade = true;
                        self.add(p);
                    }
                }
                Champion::Portal => {
                    // embers rising off the vortex
                    for _ in 0..due(self, 14.0) {
                        let (a, d) = (2.0 * PI * self.random(), 1.3 * self.random().sqrt());
                        let c = self.pick(&[0xFFF8DC, 0xFFD66E, 0xFF8C1E, 0xC83410]);
                        let p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.0, c, 60.0 + 50.0 * self.random(), 1300, Motion::Spark)
                            .moving(self.pm(0.15) / 1000.0, self.pm(0.15) / 1000.0, 0.0018 + 0.0014 * self.random());
                        self.add(p);
                    }
                    // the four floors' colours circling its lip
                    for _ in 0..due(self, 6.0) {
                        let a = 2.0 * PI * self.random();
                        let c = self.pick(&[0xA4AFC0, 0x46C2B4, 0xFF9A24, 0x4AF0FF]);
                        let mut p = Particle::new(x, y, 0.15, c, 80.0, 1600, Motion::Swirl { id, angle: a, speed: 0.05, rad: 1.85, out: 0.0, rise: 0.0006 });
                        p.fade = true;
                        self.add(p);
                    }
                    // and every two seconds a pulse of gold out of it
                    if ticked(now, 2000) {
                        self.decal(DecalKind::Ring, x, y, 2.3, 0xFFD24A, now, 700, Some(id));
                    }
                }
            }
        }
        self.floor = world.my_z();
    }

    /// A phase begun (`CrucibleMessage` Cue `cue`, on the champion of type `champion`): its own
    /// burst round the champion, in its floor's colours.
    /// Every frame, in the Crucible: ground built near us (its ways laid ahead of us, a floor
    /// coming together) rises out of the sky with light streaming up into it and a breath of dust
    /// as it settles, now and then a ring of light; ground falling away breaks into pieces that
    /// tumble down into the sky with embers. Squares just come into sight (arriving, walking on,
    /// a jump) aren't building: only those that appear within `NEAR` of us, after we've settled.
    pub fn crucible_ground(&mut self, world: &World, data: &bsk_data::GameData) {
        const NEAR: f64 = 13.0;
        const MOST: usize = 48; // squares a frame given effects
        if world.background != crate::cruciblesky::SKY_CRUCIBLE {
            self.crucible.world = 0;
            return;
        }
        let Some(me) = world.player() else { return };
        let (w, h) = (world.width, world.height);
        let n = (w * h).max(0) as usize;
        let log = world.tile_log();
        let now = self.now;
        let watch = &mut self.crucible;
        if watch.world != world.id() || watch.known.len() != n || watch.seen > log.len() {
            watch.world = world.id();
            watch.seen = log.len();
            watch.known = (0..n).map(|i| world.tile(i as i32 % w, i as i32 / w).is_some()).collect();
            watch.settling = now;
            watch.at = (me.x, me.y);
            return;
        }
        if (me.x - watch.at.0).hypot(me.y - watch.at.1) > 4.0 {
            watch.settling = now;
        }
        watch.at = (me.x, me.y);
        let settled = now - watch.settling > 1200;
        let fresh: Vec<u32> = log[watch.seen..].to_vec();
        watch.seen = log.len();
        let (mut built, mut fell) = (Vec::new(), Vec::new());
        for i in fresh {
            let i = i as usize;
            if i >= n {
                continue;
            }
            let (x, y) = (i as i32 % w, i as i32 / w);
            let was = self.crucible.known[i];
            let tile = world.tile(x, y);
            self.crucible.known[i] = tile.is_some();
            let near = (x as f64 + 0.5 - me.x).hypot(y as f64 + 0.5 - me.y) <= NEAR;
            if !settled || !near {
                continue;
            }
            match (was, tile) {
                (false, Some(t)) if built.len() < MOST => built.push((x, y, t)),
                (true, None) if fell.len() < MOST => fell.push((x, y)),
                _ => {}
            }
        }
        let shade = |c: u32, k: f64| {
            let ch = |s: u32| ((((c >> s) & 255) as f64 * k).min(255.0) as u32) << s;
            ch(16) | ch(8) | ch(0)
        };
        for (x, y, t) in built {
            let c = data.grounds.get(&t).and_then(|g| g.color).unwrap_or(0x8A7A6A);
            let (cx, cy) = (x as f64 + 0.5, y as f64 + 0.5);
            // light streaming up into it out of the sky below
            for _ in 0..2 {
                let col = self.pick(&[0xFFE29A, 0xFFB03A, shade(c, 2.2)]);
                let mut p = Particle::new(cx + self.pm(0.4), cy + self.pm(0.4), -1.6 - self.random(), col, 80.0 + 40.0 * self.random(), 650, Motion::Plain)
                    .moving(0.0, 0.0, 0.0032 + 0.0015 * self.random());
                p.fade = true;
                self.add(p);
            }
            // a breath of dust as it settles
            if self.random() < 0.3 {
                self.puffs(cx, cy, 1, &[shade(c, 1.4), shade(c, 0.9)], 150.0, 0.5, 0.35, 700, 0);
            }
            if self.random() < 0.08 {
                self.decal(DecalKind::Ring, cx, cy, 1.1, 0xFFB03A, now, 520, None);
            }
        }
        for (x, y) in fell {
            let (cx, cy) = (x as f64 + 0.5, y as f64 + 0.5);
            // pieces of it tumbling down into the sky, an ember or two with them
            for k in 0..3 {
                let col = if k == 2 { self.pick(&[0xFF9A2A, 0xFFE29A]) } else { self.pick(&[0x4A3E50, 0x2A2230, 0x6A5A70]) };
                let size = if k == 2 { 60.0 } else { 120.0 + 90.0 * self.random() };
                let mut p = Particle::new(cx + self.pm(0.4), cy + self.pm(0.4), 0.0, col, size, 900 + (self.random() * 400.0) as i32, Motion::Rising { acc: -9.0 - 6.0 * self.random() })
                    .moving(self.pm(0.0008), self.pm(0.0008), 0.0012 * self.random());
                p.fade = true;
                self.add(p);
            }
        }
    }

    pub fn champion_cue(&mut self, cue: u8, champion: u16, world: &World, now: i32) {
        if self.disabled {
            return;
        }
        let Some(o) = world.objects.values().find(|o| o.object_type == champion) else { return };
        let (x, y, id) = (o.x, o.y, o.id);
        self.here = (world.my_z(), self.here.1);
        match cue {
            // Ferrogrim: the anvil rings: a shockwave, the floor cracking, sparks everywhere
            1..=4 => {
                let glow = if cue == 4 { 0xFFE29A } else { 0xFF9A2A };
                self.add(Particle::new(x, y, 0.6, 0xFFF0C0, 900.0, 160, Motion::Spark));
                self.decal(DecalKind::Ring, x, y, 5.0, glow, now, 520, None);
                self.decal(DecalKind::Ring, x, y, 3.0, 0xB4BCC8, now + 100, 420, None);
                self.decal(DecalKind::Cracks, x, y, 3.5, glow, now, 1800, None);
                self.debris(x, y, 40, &[0xFFE29A, 0xFF9A2A, 0xD8600E, 0xA4AFC0], 120.0, 6.0, 6.0, 900, 0);
                self.puffs(x, y, 10, &[0x5C6476, 0x3C4250], 260.0, 1.4, 1.0, 1200, 60);
            }
            // Nerissa: the sea rises: a column of water, a whirlpool opening, spray far and wide
            5..=8 => {
                self.decal(DecalKind::Whirlpool, x, y, if cue >= 7 { 4.0 } else { 2.5 }, 0x46C2B4, now, 1800, Some(id));
                self.decal(DecalKind::Ring, x, y, 6.0, 0x8AF0E0, now, 600, None);
                for i in 0..40 {
                    let a = 2.0 * PI * self.random();
                    let d = 0.6 * self.random();
                    let c = [0xD8FFFF, 0x46C2B4, 0x8AF0E0, 0x1A8288][i % 4];
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.2, c, 150.0 + 80.0 * self.random(), 900, Motion::Ballistic { g: GRAVITY * 0.5 })
                        .moving(a.cos() * 0.002, a.sin() * 0.002, 0.008 + 0.005 * self.random());
                    p.delay = (i as i32 * 9) % 200;
                    self.add(p);
                }
            }
            // Pyrrhax: the forge erupts: fire roaring up, the ground scorched, slag flung out
            // (meteors falling round him for his Meteor Storm)
            9..=12 => {
                self.mortar_blast(x, y, 3.5, 1600, now);
                if cue == 10 {
                    for k in 0..6 {
                        let a = 2.0 * PI * k as f64 / 6.0 + self.random();
                        let d = 3.0 + 3.0 * self.random();
                        self.meteor(x + d * a.cos(), y + d * a.sin(), 1.4, 450 + k * 120, now);
                    }
                }
            }
            // Chronarch: the hour strikes: the clock on the floor round him, its hand sweeping
            13..=16 => {
                let c = if cue == 16 { 0xFF6AE8 } else { 0x4AF0FF };
                self.decal(DecalKind::Clock, x, y, 5.0, c, now, 2000, Some(id));
                self.decal(DecalKind::Ring, x, y, 5.0, c, now, 420, None);
                self.debris(x, y, 24, &[c, 0xD0A040, 0xF2D282], 100.0, 4.0, 5.0, 900, 0);
            }
            // Vaelith: the Sovereign's decree: his star drawn on the floor, a column of light; the
            // echoes each in their floor's colour; the Crucible breaking; the void unbound
            _ => {
                let c = match cue {
                    18 => 0xFF9A2A,
                    19 => 0x46C2B4,
                    20 => 0xFF6A14,
                    21 => 0x4AF0FF,
                    23 => 0xA828D8,
                    _ => 0xFFD24A,
                };
                self.decal(DecalKind::Pentagram, x, y, 4.5, c, now, 2200, Some(id));
                self.decal(DecalKind::Stars, x, y, 1.6, c, now, 2200, Some(id));
                self.decal(DecalKind::Ring, x, y, 7.0, c, now, 650, None);
                if cue == 22 {
                    self.decal(DecalKind::Cracks, x, y, 7.0, 0xFFD24A, now, 3000, None);
                }
                if cue == 23 {
                    self.decal(DecalKind::BlackHole, x, y, 2.5, c, now, 2000, Some(id));
                }
                for i in 0..48 {
                    let a = 2.0 * PI * i as f64 / 48.0;
                    let d = 0.3 + 0.5 * self.random();
                    let col = [c, 0xFFFFFF, mix(c, 0xFFFFFF, 0.5)][i % 3];
                    let mut p = Particle::new(x + d * a.cos(), y + d * a.sin(), 0.1, col, 140.0 + 80.0 * self.random(), 800, Motion::Spark)
                        .moving(0.0, 0.0, 0.009 + 0.006 * self.random());
                    p.delay = (i as i32 * 7) % 260;
                    self.add(p);
                }
                self.debris(x, y, 30, &[c, 0xFFFFFF, mix(c, 0x000000, 0.4)], 120.0, 7.0, 7.0, 1000, 80);
            }
        }
    }
}
