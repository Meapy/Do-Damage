//! What hangs under floating islands (a world of stacked floors in the sunset sky, the Nexus
//! islands: `sunset::SKY_SUNSET`), worked out here from the floors' squares, which the client
//! always has (an object would only come while the player is near it):
//!
//! - each island's rocky underside: a cone of rock from under its rim down into the sky, round
//!   its outline (not square by square, so its sides are smooth whatever the rim's steps), its
//!   upper band soil and roots, the rest rock, hazier the deeper it hangs;
//! - under a bridge's deck (grounds with `<Piers/>`): piers every `PIER_EVERY` squares fading
//!   into the haze below; under a wooden one cross beams, under a stone one (`<Piers>stone</Piers>`)
//!   arches between its piers.
//!
//! All of it is drawn before any floor, back to front (`view::draw`), so the islands and
//! everything on them stand in front of it.

use bsk_data::GameData;
use bsk_render::{AtlasRect, Renderer};
use bsk_world::World;

use crate::sprites::Sprites;
use crate::view::Camera;

/// The floors' slab under their edges (`view::SLAB`): the rock starts below it.
const SLAB: f64 = 0.35;
/// The cone's rings: how far in from the rim each is, and how far below it, for an island 20
/// squares across its middle (smaller ones hang less deep). The view shows a tile of height as
/// a tile up the screen, so the rock only shows below an island's edge where it goes down more
/// than in.
const INWARD: [f64; 8] = [0.1, 0.55, 1.5, 2.9, 4.7, 6.9, 9.4, 12.0];
const DEPTH: [f64; 8] = [0.0, 1.2, 2.9, 5.0, 7.5, 10.2, 12.6, 14.2];
/// Bridges: a pier every this many squares along, how long, how wide (wood, stone); the stone
/// arches' depth at their crown and at the piers.
const PIER_EVERY: i32 = 4;
const PIER_LENGTH: f64 = 5.0;
const PIER_WIDTH: f64 = 0.3;
const STONE_PIER_WIDTH: f64 = 0.62;
const ARCH_CROWN: f64 = 0.3;
const ARCH_SPRING: f64 = 1.7;
/// The haze the deep rock fades into (the sky's lavender).
const HAZE: u32 = 0xC6B4D4;

/// One island's underside: its rings of points, each round its outline, from under its rim down.
struct Island {
    rings: Vec<Vec<[f64; 3]>>,
    /// Its extent on the map, for what's in view.
    bounds: [f64; 4],
}

/// A bridge's pier (a post fading down into the haze), cross beam (under the deck, from a to b)
/// or arch band (from the deck's underside down to the arch's curve).
#[derive(Clone, Copy)]
enum Timber {
    Pier { top: [f64; 3], length: f64, width: f64, rect: Option<AtlasRect> },
    Beam { a: [f64; 3], b: [f64; 3], rect: Option<AtlasRect> },
    Arch { a: [f64; 3], b: [f64; 3], drop_a: f64, drop_b: f64, rect: Option<AtlasRect> },
}

#[derive(Default)]
pub struct Undersides {
    world_id: u64,
    layers_gen: u32,
    log_seen: usize,
    /// Draws since the map's squares last changed and were caught up with (a new square redoes
    /// the islands: not more than every few draws while they stream in).
    since: u32,
    islands: Vec<Island>,
    timbers: Vec<Timber>,
}

fn hash(x: i32, y: i32, k: u32) -> u32 {
    let mut h = (x as u32).wrapping_mul(374_761_393) ^ (y as u32).wrapping_mul(668_265_263) ^ k.wrapping_mul(2_246_822_519);
    h = (h ^ (h >> 13)).wrapping_mul(1_274_126_177);
    h ^ (h >> 16)
}

fn unit(h: u32) -> f64 {
    (h & 0xFFFF) as f64 / 65535.0
}

/// A triangle to draw: depth on the screen, picture, corners, their uvs and tints.
type Tri = (f32, AtlasRect, [[f32; 2]; 3], [[f32; 2]; 3], [[f32; 4]; 3]);

/// Two triangles over the quad p0 p1 p2 p3 (clockwise), the picture across it, the top edge's
/// tint `t0` and the bottom's `t1`.
fn quad(out: &mut Vec<Tri>, depth: f32, rect: AtlasRect, p: [[f32; 2]; 4], v: (f32, f32), t0: [f32; 4], t1: [f32; 4]) {
    out.push((depth, rect, [p[0], p[1], p[2]], [[0.0, 0.0], [1.0, 0.0], [1.0, v.1]], [t0, t0, t1]));
    out.push((depth, rect, [p[0], p[2], p[3]], [[0.0, 0.0], [1.0, v.1], [0.0, v.0]], [t0, t1, t1]));
}

impl Undersides {
    fn sync(&mut self, world: &World, data: &GameData, sprites: &Sprites) {
        let fresh = self.world_id != world.id() || self.layers_gen != world.layers_gen();
        let grown = world.tile_log().len() != self.log_seen;
        self.since = self.since.saturating_add(1);
        if !fresh && (!grown || self.since < 20) {
            return;
        }
        self.since = 0;
        self.world_id = world.id();
        self.layers_gen = world.layers_gen();
        self.log_seen = world.tile_log().len();
        let bare: rustc_hash::FxHashSet<u16> =
            data.grounds.iter().filter(|(_, g)| g.xml.has("Piers") || g.xml.has("NoUnderside")).map(|(&t, _)| t).collect();
        self.islands.clear();
        self.timbers.clear();
        for level in 0..world.level_count() as u8 {
            self.islands.extend(islands(world, &bare, level));
            bridges(world, data, sprites, level, &mut self.timbers);
        }
        log::debug!("undersides: {} islands, {} bridge pieces", self.islands.len(), self.timbers.len());
    }

    /// The undersides in view, back to front.
    #[allow(clippy::too_many_arguments)]
    pub fn draw(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, cam: &Camera, x0: i32, x1: i32, y0: i32, y1: i32) {
        self.sync(world, data, sprites);
        let Some(rock) = data.ground_ids.get("NI Sky Rock").and_then(|t| crate::ground::plain(data, sprites, *t, 0, 0)) else { return };
        let soil = data.ground_ids.get("NI Island Rim").and_then(|t| crate::ground::face(data, sprites, *t, 0, 0)).map_or(rock, |f| f.0);
        let white = renderer.white();
        let [hr, hg, hb] = bsk_render::rgb_f32(HAZE);
        let hazed = |k: f32, t: f32, a: f32| {
            let haze = 0.6 * t.clamp(0.0, 1.0);
            [k * (1.0 - haze) + hr * haze, k * (1.0 - haze) + hg * haze, k * (1.0 - haze) + hb * haze, a]
        };
        let mut tris: Vec<Tri> = Vec::new();
        let (vx0, vx1, vy0, vy1) = (x0 as f64, x1 as f64, y0 as f64, y1 as f64);
        let last = (INWARD.len() - 1) as f32;
        for island in &self.islands {
            let [bx0, by0, bx1, by1] = island.bounds;
            if bx1 < vx0 || bx0 > vx1 || by1 < vy0 || by0 > vy1 {
                continue;
            }
            for k in 0..island.rings.len() - 1 {
                let (upper, lower) = (&island.rings[k], &island.rings[k + 1]);
                let n = upper.len();
                for j in 0..n {
                    let (p, q, r, s) = (upper[j], upper[(j + 1) % n], lower[(j + 1) % n], lower[j]);
                    let sc = |w: [f64; 3]| cam.to_screen(w[0], w[1], w[2]);
                    let pts = [sc(p), sc(q), sc(r), sc(s)];
                    // facing away (as a floor's sides, `view::floors`)
                    let (ax, ay) = (pts[1][0] - pts[0][0], pts[1][1] - pts[0][1]);
                    let (dx, dy) = (pts[3][0] - pts[0][0], pts[3][1] - pts[0][1]);
                    if ax * dy - ay * dx >= 0.0 {
                        continue;
                    }
                    let lit = shade(normal(p, q, s));
                    let (tu, tl) = (hazed(lit * (1.0 - 0.45 * k as f32 / last), k as f32 / last, 1.0), hazed(lit * (1.0 - 0.45 * (k + 1) as f32 / last), (k + 1) as f32 / last, 1.0));
                    let depth = cam.to_screen((p[0] + r[0]) / 2.0, (p[1] + r[1]) / 2.0, 0.0)[1];
                    quad(&mut tris, depth, if k == 0 { soil } else { rock }, pts, (1.0, 1.0), tu, tl);
                }
            }
        }
        let in_view = |a: [f64; 3], b: [f64; 3]| !(a[0].max(b[0]) < vx0 - 2.0 || a[0].min(b[0]) > vx1 + 2.0 || a[1].max(b[1]) < vy0 - 2.0 || a[1].min(b[1]) > vy1 + 2.0);
        for t in &self.timbers {
            match *t {
                Timber::Pier { top, length, width, rect } => {
                    if !in_view(top, top) {
                        continue;
                    }
                    let (a, b) = (cam.to_screen(top[0], top[1], top[2]), cam.to_screen(top[0], top[1], top[2] - length));
                    let w = (width * cam.scale) as f32 / 2.0;
                    let depth = cam.to_screen(top[0], top[1], 0.0)[1];
                    let pts = [[a[0] - w, a[1]], [a[0] + w, a[1]], [b[0] + w, b[1]], [b[0] - w, b[1]]];
                    quad(&mut tris, depth, rect.unwrap_or(white), pts, (length as f32, length as f32), hazed(0.62, 0.0, 1.0), hazed(0.5, 1.0, 0.0));
                }
                Timber::Beam { a, b, rect } => {
                    if !in_view(a, b) {
                        continue;
                    }
                    let (p, q) = (cam.to_screen(a[0], a[1], a[2]), cam.to_screen(b[0], b[1], b[2]));
                    let w = (PIER_WIDTH * cam.scale) as f32 / 2.0;
                    let (dx, dy) = (q[0] - p[0], q[1] - p[1]);
                    let len = dx.hypot(dy).max(1e-3);
                    let (nx, ny) = (-dy / len * w, dx / len * w);
                    let depth = cam.to_screen((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0, 0.0)[1];
                    let c = hazed(0.55, 0.0, 1.0);
                    let pts = [[p[0] - nx, p[1] - ny], [q[0] - nx, q[1] - ny], [q[0] + nx, q[1] + ny], [p[0] + nx, p[1] + ny]];
                    quad(&mut tris, depth, rect.unwrap_or(white), pts, (1.0, 1.0), c, c);
                }
                Timber::Arch { a, b, drop_a, drop_b, rect } => {
                    if !in_view(a, b) {
                        continue;
                    }
                    let pts = [
                        cam.to_screen(a[0], a[1], a[2]),
                        cam.to_screen(b[0], b[1], b[2]),
                        cam.to_screen(b[0], b[1], b[2] - drop_b),
                        cam.to_screen(a[0], a[1], a[2] - drop_a),
                    ];
                    // facing away
                    let (ax, ay) = (pts[1][0] - pts[0][0], pts[1][1] - pts[0][1]);
                    let (dx, dy) = (pts[3][0] - pts[0][0], pts[3][1] - pts[0][1]);
                    if ax * dy - ay * dx >= 0.0 {
                        continue;
                    }
                    let k = shade([b[1] - a[1], a[0] - b[0], 0.0]);
                    let c = hazed(k * 0.92, 0.1, 1.0);
                    let depth = cam.to_screen((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0, 0.0)[1];
                    quad(&mut tris, depth, rect.unwrap_or(white), pts, (drop_a as f32, drop_b as f32), c, c);
                }
            }
        }
        tris.sort_by(|a, b| a.0.total_cmp(&b.0));
        for (_, rect, pts, uvs, muls) in tris {
            renderer.triangle_colors(rect, pts, uvs, muls);
        }
    }
}

/// `view::shade` for a face whose normal is `n`.
fn shade(n: [f64; 3]) -> f32 {
    let len = (1.0f64 + 9.0 + 4.0).sqrt();
    let light = [1.0 / len, 3.0 / len, 2.0 / len];
    let nl = (n[0] * n[0] + n[1] * n[1] + n[2] * n[2]).sqrt().max(1e-9);
    let dot = (n[0] * light[0] + n[1] * light[1] + n[2] * light[2]) / nl;
    (0.62 + 0.38 * dot.max(0.0)) as f32
}

fn normal(v0: [f64; 3], v1: [f64; 3], v2: [f64; 3]) -> [f64; 3] {
    let u = [v1[0] - v0[0], v1[1] - v0[1], v1[2] - v0[2]];
    let v = [v2[0] - v0[0], v2[1] - v0[1], v2[2] - v0[2]];
    [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
}

/// Whether rock hangs under a square of a floor: ground with nothing on a floor below it, that
/// isn't a bridge's deck (`<Piers/>`) nor asks for none (`<NoUnderside/>`).
fn rocky(world: &World, bare: &rustc_hash::FxHashSet<u16>, level: u8, x: i32, y: i32) -> bool {
    let Some(tile) = world.level_tile(level, x, y) else { return false };
    if bare.contains(&tile) {
        return false;
    }
    !(0..level).any(|l| world.level_tile(l, x, y).is_some())
}

/// Each island of a floor (its squares of rock, joined side by side) and its underside's rings:
/// round its middle, a point every square or so of its outline, each ring further in and lower.
fn islands(world: &World, bare: &rustc_hash::FxHashSet<u16>, level: u8) -> Vec<Island> {
    let (w, h) = (world.width.max(0), world.height.max(0));
    let floor_h = world.levels.as_ref().map_or(1.0, |l| l.height);
    let base = level as f64 * floor_h - SLAB;
    let mut seen = vec![false; (w * h) as usize];
    let mut out = Vec::new();
    for sy in 0..h {
        for sx in 0..w {
            if seen[(sx + sy * w) as usize] || !rocky(world, bare, level, sx, sy) {
                continue;
            }
            // the island: every square of rock joined to this one
            let mut squares = Vec::new();
            let mut stack = vec![(sx, sy)];
            seen[(sx + sy * w) as usize] = true;
            while let Some((x, y)) = stack.pop() {
                squares.push((x, y));
                for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
                    let (nx, ny) = (x + dx, y + dy);
                    if nx < 0 || ny < 0 || nx >= w || ny >= h || seen[(nx + ny * w) as usize] || !rocky(world, bare, level, nx, ny) {
                        continue;
                    }
                    seen[(nx + ny * w) as usize] = true;
                    stack.push((nx, ny));
                }
            }
            if squares.len() < 6 {
                continue;
            }
            let n = squares.len() as f64;
            let c = [squares.iter().map(|s| s.0 as f64 + 0.5).sum::<f64>() / n, squares.iter().map(|s| s.1 as f64 + 0.5).sum::<f64>() / n];
            // the outline: how far out the island reaches at each angle (its edge squares' corners)
            let inside: rustc_hash::FxHashSet<(i32, i32)> = squares.iter().copied().collect();
            let mut points = Vec::new();
            for &(x, y) in &squares {
                if [(1, 0), (-1, 0), (0, 1), (0, -1)].iter().all(|(dx, dy)| inside.contains(&(x + dx, y + dy))) {
                    continue;
                }
                for (ox, oy) in [(0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0)] {
                    points.push((x as f64 + ox, y as f64 + oy));
                }
            }
            let bins = (points.len() as f64 * 0.22).clamp(40.0, 240.0) as usize;
            let mut r = vec![-1.0f64; bins];
            for &(px, py) in &points {
                let a = (py - c[1]).atan2(px - c[0]).rem_euclid(std::f64::consts::TAU);
                let j = ((a / std::f64::consts::TAU * bins as f64) as usize).min(bins - 1);
                r[j] = r[j].max((px - c[0]).hypot(py - c[1]));
            }
            // empty angles from their neighbours, then smoothed a little
            for j in 0..bins {
                if r[j] < 0.0 {
                    let (mut lo, mut hi) = ((j + bins - 1) % bins, (j + 1) % bins);
                    while r[lo] < 0.0 && lo != j {
                        lo = (lo + bins - 1) % bins;
                    }
                    while r[hi] < 0.0 && hi != j {
                        hi = (hi + 1) % bins;
                    }
                    r[j] = (r[lo].max(0.0) + r[hi].max(0.0)) / 2.0;
                }
            }
            let r: Vec<f64> = (0..bins).map(|j| 0.5 * r[j] + 0.25 * (r[(j + bins - 1) % bins] + r[(j + 1) % bins])).collect();
            let mean = r.iter().sum::<f64>() / bins as f64;
            let size = (mean / 10.0).clamp(0.35, 1.6);
            let seed = (sx * 7 + sy * 13) as u32;
            let rings: Vec<Vec<[f64; 3]>> = (0..INWARD.len())
                .map(|k| {
                    (0..bins)
                        .map(|j| {
                            let a = (j as f64 + 0.5) / bins as f64 * std::f64::consts::TAU;
                            // rock is ragged: a little in or out, a little up or down, more lower down
                            let wob = k as f64 / INWARD.len() as f64;
                            let jr = (unit(hash(j as i32, k as i32, seed)) - 0.5) * 0.7 * wob;
                            let jz = (unit(hash(j as i32, k as i32, seed + 1)) - 0.5) * 1.0 * wob;
                            let rad = (r[j] - INWARD[k] * size.max(0.6) + jr).max(0.4 * (1.0 - wob));
                            let z = base - if k == 0 { 0.0 } else { DEPTH[k] * size + jz };
                            [c[0] + a.cos() * rad, c[1] + a.sin() * rad, z]
                        })
                        .collect()
                })
                .collect();
            let reach = r.iter().fold(0.0f64, |m, v| m.max(*v)) + 1.0;
            let depth = DEPTH[DEPTH.len() - 1] * size + 2.0;
            out.push(Island { rings, bounds: [c[0] - reach, c[1] - reach - floor_h * level as f64, c[0] + reach, c[1] + reach + depth] });
        }
    }
    out
}

/// Under a floor's bridges (grounds with `<Piers/>`): on their open sides, every `PIER_EVERY`
/// squares along, a pier into the haze; under wood a beam across to the far side's, under stone
/// (`<Piers>stone</Piers>`) an arch along each side from pier to pier.
fn bridges(world: &World, data: &GameData, sprites: &Sprites, level: u8, out: &mut Vec<Timber>) {
    for y in 0..world.height {
        for x in 0..world.width {
            let Some(tile) = world.level_tile(level, x, y) else { continue };
            let Some(piers) = data.grounds.get(&tile).and_then(|g| g.xml.child("Piers")) else { continue };
            let stone = piers.text.trim() == "stone";
            let Some(s) = world.surface(level, x, y, data) else { continue };
            let deck = |px: i32, py: i32| {
                (0..world.level_count() as u8).filter_map(|l| world.surface(l, px, py, data)).any(|v| (v - s).abs() <= bsk_world::STEP)
            };
            // its height where it meets the next square along (a sloped deck)
            let at = |px: i32, py: i32| (0..world.level_count() as u8).filter_map(|l| world.surface(l, px, py, data)).find(|v| (v - s).abs() <= 0.3);
            let rect = crate::ground::face(data, sprites, tile, x, y).map(|(r, _)| r);
            let top = s - SLAB;
            for (nx, ny) in [(0, -1), (0, 1), (-1, 0), (1, 0)] {
                if deck(x + nx, y + ny) {
                    continue;
                }
                // the way along this side, and whether the bridge runs that way (its ends are no sides)
                let (ax, ay) = (ny.abs(), nx.abs());
                if !deck(x + ax, y + ay) && !deck(x - ax, y - ay) {
                    continue;
                }
                let along = if ax != 0 { x } else { y };
                let edge = (x as f64 + 0.5 + nx as f64 * 0.5, y as f64 + 0.5 + ny as f64 * 0.5);
                if stone {
                    // the arch along this side: deep at the piers, shallow at the crown
                    let drop = |u: f64| {
                        let phase = u.rem_euclid(PIER_EVERY as f64) / PIER_EVERY as f64;
                        ARCH_CROWN + (ARCH_SPRING - ARCH_CROWN) * (1.0 - (phase * std::f64::consts::PI).sin()).powf(1.6)
                    };
                    let (u0, u1) = (along as f64, along as f64 + 1.0);
                    let ha = at(x - ax, y - ay).map_or(s, |v| (v + s) / 2.0) - SLAB;
                    let hb = at(x + ax, y + ay).map_or(s, |v| (v + s) / 2.0) - SLAB;
                    let (pa, pb) = if ax != 0 {
                        ([x as f64, edge.1, ha], [x as f64 + 1.0, edge.1, hb])
                    } else {
                        ([edge.0, y as f64, ha], [edge.0, y as f64 + 1.0, hb])
                    };
                    // wound as the floors' sides are (clockwise round the bridge), so the far
                    // side's faces away
                    let (a, b, da, db) = if (nx, ny) == (0, -1) || (nx, ny) == (1, 0) { (pa, pb, drop(u0), drop(u1)) } else { (pb, pa, drop(u1), drop(u0)) };
                    out.push(Timber::Arch { a, b, drop_a: da, drop_b: db, rect });
                }
                if along.rem_euclid(PIER_EVERY) != 0 {
                    continue;
                }
                let (px, py) = (x as f64 + 0.5 + nx as f64 * 0.4, y as f64 + 0.5 + ny as f64 * 0.4);
                if stone {
                    out.push(Timber::Pier { top: [px, py, top - ARCH_SPRING + 0.05], length: PIER_LENGTH + 1.0, width: STONE_PIER_WIDTH, rect });
                    continue;
                }
                out.push(Timber::Pier { top: [px, py, top], length: PIER_LENGTH, width: PIER_WIDTH, rect });
                // the cross beam: to the far side, the way across (drawn once, from the north or west side)
                if nx > 0 || ny > 0 {
                    continue;
                }
                let (mut fx, mut fy) = (x, y);
                while deck(fx - nx, fy - ny) && (fx - x).abs() + (fy - y).abs() < 9 {
                    fx -= nx;
                    fy -= ny;
                }
                if (fx, fy) != (x, y) {
                    let (qx, qy) = (fx as f64 + 0.5 - nx as f64 * 0.4, fy as f64 + 0.5 - ny as f64 * 0.4);
                    out.push(Timber::Beam { a: [px, py, top - 0.12], b: [qx, qy, top - 0.12], rect });
                }
            }
        }
    }
}
