//! What hangs under floating islands (a world of stacked floors in the sunset sky, the Nexus
//! islands: `sunset::SKY_SUNSET`), worked out here from the floors' squares, which the client
//! always has (an object would only come while the player is near it):
//!
//! - each island's rocky underside: under every square of ground with nothing on a floor below
//!   it, a column of rock as deep as the square is far in from the island's edge (`depth`), so
//!   the columns step down to a ragged point, square by square as the rim above them (nothing
//!   between them for the sky to show through). Each column a little deeper or shallower than its
//!   neighbours, now and then a spur hanging lower, its stone a shade lighter or darker; the top
//!   band soil and roots under the rim, the rest rock, hazier the deeper it hangs;
//! - under a bridge's deck (grounds with `<Piers/>`): under wood, piers every `PIER_EVERY`
//!   squares fading into the haze below and cross beams; under stone (`<Piers>stone</Piers>`)
//!   arches along its sides, and no piers.
//!
//! All of it is drawn before any floor, back to front (`view::draw`), so the islands and
//! everything on them stand in front of it.

use std::rc::Rc;

use bsk_data::GameData;
use bsk_render::{AtlasRect, Renderer};
use bsk_world::World;

use crate::sprites::Sprites;
use crate::view::Camera;

/// Squares worked out at a time (as `view::FloorCache`).
const CHUNK: i32 = 16;
/// The floors' slab under their edges (`view::SLAB`): the rock starts below it.
const SLAB: f64 = 0.35;
/// How deep the rock hangs: at the edge, more per square further in, at most. The view shows a
/// tile of height as a tile up the screen, so the rock only shows below an island's edge where it
/// deepens more than a tile a square in.
const EDGE_DEPTH: f64 = 0.6;
const PER_SQUARE: f64 = 1.55;
const MAX_DEPTH: f64 = 12.0;
/// The soil band under the rim, how deep.
const SOIL: f64 = 0.8;
/// Bridges: a pier every this many squares along, how long, how wide; the stone arches' depth at
/// their crown and every `PIER_EVERY` squares.
const PIER_EVERY: i32 = 4;
const PIER_LENGTH: f64 = 5.0;
const PIER_WIDTH: f64 = 0.3;
const ARCH_CROWN: f64 = 0.3;
const ARCH_SPRING: f64 = 1.7;
/// What the undersides are made of, by the sky they hang into: the rock's ground (its pictures),
/// the band of soil (or dressed stone) just under the rim (a ground's `<Face>`, else its pictures),
/// and the haze the deep rock fades into.
pub struct Style {
    pub rock: &'static str,
    pub soil: &'static str,
    pub haze: u32,
}

/// The Nexus islands in the sunset: grey rock, soil and roots, fading into the sky's lavender.
pub const SUNSET: Style = Style { rock: "NI Sky Rock", soil: "NI Island Rim", haze: 0xC6B4D4 };
/// The Black Between's platform over the void (tools/build_black_between.py): dark violet stone
/// veined with starlight under a band of dressed stone and gold, fading into the void's black.
pub const VOID: Style = Style { rock: "BB Underside Rock", soil: "BB Underside Rim", haze: 0x07051A };

/// A side of a column of rock, from a to b (going round its square clockwise), from `top` down to
/// `bottom`: `deep` how deep its foot is (0 to 1 of `MAX_DEPTH`, for the haze), `tone` its stone's
/// shade (about 1), `under_rim` how far below the rim its top is (for the soil band).
#[derive(Clone, Copy)]
struct RockFace {
    a: [f64; 2],
    b: [f64; 2],
    top: f64,
    bottom: f64,
    deep: f32,
    tone: f32,
    under_rim: f64,
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
    /// every floor's distances: not more than every few draws while they stream in).
    since: u32,
    /// Each floor's squares' distance in from its edge (255: no rock), worked out when the floors
    /// come or the map's squares change.
    dist: Vec<Vec<u8>>,
    chunks: rustc_hash::FxHashMap<(u8, i32, i32), Rc<Vec<RockFace>>>,
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
        self.chunks.clear();
        self.timbers.clear();
        self.dist = (0..world.level_count() as u8).map(|l| distances(world, &bare, l)).collect();
        for level in 0..world.level_count() as u8 {
            bridges(world, data, sprites, level, &mut self.timbers);
        }
        log::debug!(
            "undersides: {:?} squares of rock a floor, {} bridge pieces",
            self.dist.iter().map(|d| d.iter().filter(|&&v| v != 255).count()).collect::<Vec<_>>(),
            self.timbers.len()
        );
    }

    fn chunk(&mut self, world: &World, level: u8, cx: i32, cy: i32) -> Rc<Vec<RockFace>> {
        let dist = &self.dist;
        self.chunks.entry((level, cx, cy)).or_insert_with(|| Rc::new(columns(world, dist, level, cx, cy))).clone()
    }

    /// The undersides in view, back to front.
    #[allow(clippy::too_many_arguments)]
    pub fn draw(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, cam: &Camera, x0: i32, x1: i32, y0: i32, y1: i32, style: &Style) {
        self.sync(world, data, sprites);
        let Some(rock) = data.ground_ids.get(style.rock).and_then(|t| crate::ground::plain(data, sprites, *t, 0, 0)) else { return };
        let soil = data
            .ground_ids
            .get(style.soil)
            .and_then(|t| crate::ground::face(data, sprites, *t, 0, 0).map(|f| f.0).or_else(|| crate::ground::plain(data, sprites, *t, 0, 0)))
            .unwrap_or(rock);
        let white = renderer.white();
        let [hr, hg, hb] = bsk_render::rgb_f32(style.haze);
        let hazed = |k: f32, t: f32, a: f32| {
            let haze = 0.6 * t.clamp(0.0, 1.0);
            [k * (1.0 - haze) + hr * haze, k * (1.0 - haze) + hg * haze, k * (1.0 - haze) + hb * haze, a]
        };
        let mut tris: Vec<Tri> = Vec::new();
        let (vx0, vx1, vy0, vy1) = (x0 as f64, x1 as f64, y0 as f64, y1 as f64);
        let (cx0, cx1) = (x0.max(0).div_euclid(CHUNK), x1.min(world.width - 1).div_euclid(CHUNK));
        let (cy0, cy1) = (y0.max(0).div_euclid(CHUNK), y1.min(world.height - 1).div_euclid(CHUNK));
        for level in 0..world.level_count() as u8 {
            for cy in cy0..=cy1 {
                for cx in cx0..=cx1 {
                    let faces = self.chunk(world, level, cx, cy);
                    for f in faces.iter() {
                        let (a, b) = (f.a, f.b);
                        let depth = cam.to_screen((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0, 0.0)[1];
                        let lit = shade([b[1] - a[1], a[0] - b[0], 0.0]) * f.tone;
                        // facing away (as a floor's sides, `view::floors`): the whole face, once
                        let (p0, p1, p3) = (cam.to_screen(a[0], a[1], f.top), cam.to_screen(b[0], b[1], f.top), cam.to_screen(a[0], a[1], f.bottom));
                        if (p1[0] - p0[0]) * (p3[1] - p0[1]) - (p1[1] - p0[1]) * (p3[0] - p0[0]) >= 0.0 {
                            continue;
                        }
                        // a tile tall at a time, the soil band just under the rim first (never a
                        // sliver of it: a face starting all but at its foot is rock from the top),
                        // the rest rock, darker and hazier the deeper
                        let mut top = f.top;
                        while top - f.bottom > 0.01 {
                            let under = f.under_rim + (f.top - top);
                            let in_soil = under < SOIL - 0.05;
                            let low = if in_soil { top - (SOIL - under) } else { top - 1.0 }.max(f.bottom);
                            let pts = [cam.to_screen(a[0], a[1], top), cam.to_screen(b[0], b[1], top), cam.to_screen(b[0], b[1], low), cam.to_screen(a[0], a[1], low)];
                            let at = |z: f64| (f.deep * ((f.top - z) / (f.top - f.bottom).max(0.01)) as f32).clamp(0.0, 1.0);
                            let (tu, tl) = (hazed(lit * (1.0 - 0.45 * at(top)), at(top), 1.0), hazed(lit * (1.0 - 0.45 * at(low)), at(low), 1.0));
                            quad(&mut tris, depth, if in_soil { soil } else { rock }, pts, (1.0, 1.0), tu, tl);
                            top = low;
                        }
                    }
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

/// Whether rock hangs under a square of a floor: ground with nothing on a floor below it, that
/// isn't a bridge's deck (`<Piers/>`) nor asks for none (`<NoUnderside/>`).
fn rocky(world: &World, bare: &rustc_hash::FxHashSet<u16>, level: u8, x: i32, y: i32) -> bool {
    let Some(tile) = world.level_tile(level, x, y) else { return false };
    if bare.contains(&tile) {
        return false;
    }
    !(0..level).any(|l| world.level_tile(l, x, y).is_some())
}

/// How far each square of a floor's rock is in from its edge (a breadth-first walk in from the
/// squares beside no rock); 255 where there is none.
fn distances(world: &World, bare: &rustc_hash::FxHashSet<u16>, level: u8) -> Vec<u8> {
    let (w, h) = (world.width.max(0), world.height.max(0));
    let rocky = |x: i32, y: i32| rocky(world, bare, level, x, y);
    let mut dist = vec![255u8; (w * h) as usize];
    let mut queue = std::collections::VecDeque::new();
    for y in 0..h {
        for x in 0..w {
            if rocky(x, y) && [(1, 0), (-1, 0), (0, 1), (0, -1)].iter().any(|(dx, dy)| !rocky(x + dx, y + dy)) {
                dist[(x + y * w) as usize] = 0;
                queue.push_back((x, y));
            }
        }
    }
    while let Some((x, y)) = queue.pop_front() {
        let d = dist[(x + y * w) as usize];
        for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
            let (nx, ny) = (x + dx, y + dy);
            if nx < 0 || ny < 0 || nx >= w || ny >= h {
                continue;
            }
            let i = (nx + ny * w) as usize;
            if dist[i] == 255 && rocky(nx, ny) {
                dist[i] = d.saturating_add(1).min(254);
                queue.push_back((nx, ny));
            }
        }
    }
    dist
}

/// How far below its floor's slab the rock under a square hangs: deeper the further in, each
/// column a little more or less than its neighbours, now and then one hanging lower still.
fn depth(dist: u8, x: i32, y: i32) -> f64 {
    let d = dist as f64;
    let spur = if hash(x, y, 11) % 9 == 0 { 0.8 + unit(hash(x, y, 12)) * 1.6 } else { 0.0 };
    (EDGE_DEPTH + d * PER_SQUARE + (unit(hash(x, y, 7)) - 0.4) * 1.1 + spur).clamp(EDGE_DEPTH * 0.6, MAX_DEPTH - unit(hash(x, y, 8)) * 2.0)
}

/// A chunk's columns of rock: each square's sides, from its neighbour's foot (or from under the
/// slab at the edge) down to its own, so the columns meet with nothing between them.
fn columns(world: &World, dist: &[Vec<u8>], level: u8, cx: i32, cy: i32) -> Vec<RockFace> {
    let mut faces = Vec::new();
    let floor_h = world.levels.as_ref().map_or(1.0, |l| l.height);
    // under a floor's slab on stacked floors (the Nexus islands, whose floors draw their slab's
    // sides); on a flat map (the Black Between) nothing draws a slab, so the rock starts right at
    // the ground, or a gap shows between them
    let base = level as f64 * floor_h - if world.levels.is_some() { SLAB } else { 0.0 };
    let (w, h) = (world.width, world.height);
    let Some(dist) = dist.get(level as usize) else { return faces };
    let at = |x: i32, y: i32| -> Option<u8> {
        if x < 0 || y < 0 || x >= w || y >= h {
            return None;
        }
        Some(dist[(x + y * w) as usize]).filter(|&d| d != 255)
    };
    for y in cy * CHUNK..(cy + 1) * CHUNK {
        for x in cx * CHUNK..(cx + 1) * CHUNK {
            let Some(d) = at(x, y) else { continue };
            let bottom = base - depth(d, x, y);
            let (fx, fy) = (x as f64, y as f64);
            let tone = 0.9 + unit(hash(x, y, 13)) as f32 * 0.2;
            let sides = [
                ([fx, fy], [fx + 1.0, fy], (0, -1)),
                ([fx + 1.0, fy], [fx + 1.0, fy + 1.0], (1, 0)),
                ([fx + 1.0, fy + 1.0], [fx, fy + 1.0], (0, 1)),
                ([fx, fy + 1.0], [fx, fy], (-1, 0)),
            ];
            let deep = ((base - bottom) / MAX_DEPTH) as f32;
            for (a, b, (nx, ny)) in sides {
                // down from the neighbour's foot (or from under the slab at the edge)
                let top = match at(x + nx, y + ny) {
                    Some(n) => base - depth(n, x + nx, y + ny),
                    None => base,
                };
                if top - bottom > 0.01 {
                    faces.push(RockFace { a, b, top, bottom, deep, tone, under_rim: base - top });
                }
            }
        }
    }
    faces
}

/// Under a floor's bridges (grounds with `<Piers/>`): under wood, on their open sides every
/// `PIER_EVERY` squares along, a pier into the haze and a beam across to the far side's; under
/// stone (`<Piers>stone</Piers>`) an arch along each side, deepest every `PIER_EVERY` squares, and
/// no piers.
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
                // a stone bridge stands on its arches alone: no piers down into the sky
                if stone {
                    continue;
                }
                let (px, py) = (x as f64 + 0.5 + nx as f64 * 0.4, y as f64 + 0.5 + ny as f64 * 0.4);
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
