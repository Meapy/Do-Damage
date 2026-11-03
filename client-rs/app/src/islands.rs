//! What hangs under floating islands (a world of stacked floors in the sunset sky, the Nexus
//! islands: `sunset::SKY_SUNSET`), worked out here from the floors' squares, which the client
//! always has (an object would only come while the player is near it):
//!
//! - each island's rocky underside, tapering down into the sky: under every square of ground
//!   with nothing below it, a column of rock as deep as the square is far in from the island's
//!   edge (`depth`), so the columns step down to a ragged point; their sides in rock
//!   (`NI Sky Rock`'s texture), hazier the deeper they hang;
//! - a ring of soft clouds round the rim, under the islands' edges;
//! - under a bridge's deck (grounds with `<Piers/>`), cross beams and evenly spaced piers
//!   reaching down into the clouds.
//!
//! All of it is drawn before any floor, back to front (`view::draw`), so the islands and
//! everything on them stand in front of it.

use std::rc::Rc;

use bsk_data::GameData;
use bsk_render::{AtlasRect, Quad, Renderer, Tint};
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
/// Bridges: a pier every this many squares along, this long and wide.
const PIER_EVERY: i32 = 4;
const PIER_LENGTH: f64 = 4.5;
const PIER_WIDTH: f64 = 0.3;
/// The haze the deep rock fades into (the sky's lavender).
const HAZE: u32 = 0xC6B4D4;

#[derive(Clone, Copy)]
struct RockFace {
    a: [f64; 2],
    b: [f64; 2],
    top: f64,
    bottom: f64,
    /// How deep its foot is, 0 to 1 (`MAX_DEPTH` below its floor): the haze it takes.
    deep: f32,
}

#[derive(Clone, Copy)]
struct Puff {
    x: f64,
    y: f64,
    z: f64,
    /// Radius in tiles.
    r: f64,
    seed: u32,
}

/// A bridge's pier (a post down into the clouds) or cross beam (under the deck, from a to b).
#[derive(Clone, Copy)]
struct Timber {
    a: [f64; 3],
    b: [f64; 3],
    rect: Option<AtlasRect>,
}

#[derive(Default)]
struct Chunk {
    faces: Vec<RockFace>,
    puffs: Vec<Puff>,
    timbers: Vec<Timber>,
}

#[derive(Default)]
pub struct Undersides {
    world_id: u64,
    layers_gen: u32,
    log_seen: usize,
    /// Draws since the map's squares last changed and were caught up with (a new square
    /// redoes every floor's distances: not more than every few draws while they stream in).
    since: u32,
    /// Each floor's squares' distance in from its edge (255: no ground), worked out when the
    /// floors come or the map's squares change.
    dist: Vec<Vec<u8>>,
    chunks: rustc_hash::FxHashMap<(u8, i32, i32), Rc<Chunk>>,
}

fn hash(x: i32, y: i32, k: u32) -> u32 {
    let mut h = (x as u32).wrapping_mul(374_761_393) ^ (y as u32).wrapping_mul(668_265_263) ^ k.wrapping_mul(2_246_822_519);
    h = (h ^ (h >> 13)).wrapping_mul(1_274_126_177);
    h ^ (h >> 16)
}

fn unit(h: u32) -> f64 {
    (h & 0xFFFF) as f64 / 65535.0
}

impl Undersides {
    fn sync(&mut self, world: &World, data: &GameData) {
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
        self.chunks.clear();
        self.dist = (0..world.level_count() as u8).map(|l| distances(world, data, l)).collect();
        log::debug!("undersides: {:?} squares of rock a floor", self.dist.iter().map(|d| d.iter().filter(|&&v| v != 255).count()).collect::<Vec<_>>());
    }

    fn chunk(&mut self, world: &World, data: &GameData, sprites: &Sprites, level: u8, cx: i32, cy: i32) -> Rc<Chunk> {
        let dist = &self.dist;
        self.chunks.entry((level, cx, cy)).or_insert_with(|| Rc::new(build(world, data, sprites, dist, level, cx, cy))).clone()
    }

    /// The undersides in view, back to front, then the rim's clouds.
    #[allow(clippy::too_many_arguments)]
    pub fn draw(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, cam: &Camera, x0: i32, x1: i32, y0: i32, y1: i32) {
        self.sync(world, data);
        let Some(rock) = data.ground_ids.get("NI Sky Rock").and_then(|t| crate::ground::plain(data, sprites, *t, 0, 0)) else { return };
        let (cx0, cx1) = (x0.max(0).div_euclid(CHUNK), x1.min(world.width - 1).div_euclid(CHUNK));
        let (cy0, cy1) = (y0.max(0).div_euclid(CHUNK), y1.min(world.height - 1).div_euclid(CHUNK));
        let white = renderer.white();
        let [hr, hg, hb] = bsk_render::rgb_f32(HAZE);
        let mut quads: Vec<(f32, AtlasRect, Quad, Tint)> = Vec::new();
        let mut puffs = Vec::new();
        for level in 0..world.level_count() as u8 {
            for cy in cy0..=cy1 {
                for cx in cx0..=cx1 {
                    let chunk = self.chunk(world, data, sprites, level, cx, cy);
                    for f in &chunk.faces {
                        let (a, b) = (f.a, f.b);
                        let q = cam_quad(cam, [a[0], a[1], f.top], [b[0], b[1], f.top], [a[0], a[1], f.top - 1.0]);
                        if q.x_axis[0] * q.y_axis[1] - q.x_axis[1] * q.y_axis[0] >= 0.0 {
                            continue;
                        }
                        let depth = cam.to_screen((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0, 0.0)[1];
                        // lit as a wall's side, darker below, and fading into the haze
                        let n = [b[1] - a[1], a[0] - b[0], 0.0];
                        let lit = shade(n);
                        let mut top = f.top;
                        while top - f.bottom > 0.01 {
                            let low = (top - 1.0).max(f.bottom);
                            let t = (f.deep * ((f.top - low) / (f.top - f.bottom).max(0.01)) as f32).clamp(0.0, 1.0);
                            let k = lit * (1.0 - 0.25 * t) * (1.0 - 0.55 * t);
                            let haze = 0.55 * t;
                            let tint = Tint { mul: [k, k, k, 1.0], add: [hr * haze, hg * haze, hb * haze] };
                            let quad = cam_quad(cam, [a[0], a[1], top], [b[0], b[1], top], [a[0], a[1], low]);
                            quads.push((depth, rock, quad, tint));
                            top = low;
                        }
                    }
                    for t in &chunk.timbers {
                        let depth = cam.to_screen((t.a[0] + t.b[0]) / 2.0, (t.a[1] + t.b[1]) / 2.0, 0.0)[1];
                        let (p, q) = (cam.to_screen(t.a[0], t.a[1], t.a[2]), cam.to_screen(t.b[0], t.b[1], t.b[2]));
                        let w = (PIER_WIDTH * cam.scale) as f32;
                        // a post (down the screen) or a beam (across): its width square to its length
                        let (dx, dy) = (q[0] - p[0], q[1] - p[1]);
                        let len = dx.hypot(dy).max(1e-3);
                        let (nx, ny) = (-dy / len * w / 2.0, dx / len * w / 2.0);
                        let quad = Quad { origin: [p[0] - nx, p[1] - ny], x_axis: [2.0 * nx, 2.0 * ny], y_axis: [dx, dy] };
                        let tint = Tint { mul: [0.62, 0.55, 0.6, 1.0], add: [hr * 0.08, hg * 0.08, hb * 0.08] };
                        quads.push((depth, t.rect.unwrap_or(white), quad, tint));
                    }
                    puffs.extend(chunk.puffs.iter().copied());
                }
            }
        }
        quads.sort_by(|a, b| a.0.total_cmp(&b.0));
        if self.since == 1 {
            log::debug!("undersides: {} pieces, {} clouds in view", quads.len(), puffs.len());
        }
        for (_, rect, quad, tint) in quads {
            renderer.sprite(rect, quad, tint);
        }
        // the clouds round the rims, back to front, over the rock
        puffs.sort_by(|a, b| a.y.total_cmp(&b.y));
        let cell = ((cam.scale / crate::view::TILE_PX * 5.0).round() as f32).clamp(2.0, 12.0);
        for p in puffs {
            let [sx, sy] = cam.to_screen(p.x, p.y, p.z);
            let (w, h) = renderer.size();
            let r = (p.r * cam.scale) as f32;
            if sx + r < 0.0 || sy + r < 0.0 || sx - r > w as f32 || sy - r > h as f32 {
                continue;
            }
            crate::sunset::puff(renderer, sx, sy, r, cell, p.seed, 0.92);
        }
    }
}

fn cam_quad(cam: &Camera, p0: [f64; 3], p1: [f64; 3], p3: [f64; 3]) -> Quad {
    let o = cam.to_screen(p0[0], p0[1], p0[2]);
    let a = cam.to_screen(p1[0], p1[1], p1[2]);
    let b = cam.to_screen(p3[0], p3[1], p3[2]);
    Quad { origin: o, x_axis: [a[0] - o[0], a[1] - o[1]], y_axis: [b[0] - o[0], b[1] - o[1]] }
}

/// `view::shade` for a side facing `n` (horizontal).
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
fn distances(world: &World, data: &GameData, level: u8) -> Vec<u8> {
    let (w, h) = (world.width.max(0), world.height.max(0));
    let bare: rustc_hash::FxHashSet<u16> =
        data.grounds.iter().filter(|(_, g)| g.xml.has("Piers") || g.xml.has("NoUnderside")).map(|(&t, _)| t).collect();
    let rocky = |l: u8, x: i32, y: i32| rocky(world, &bare, l, x, y);
    let mut dist = vec![255u8; (w * h) as usize];
    let mut queue = std::collections::VecDeque::new();
    for y in 0..h {
        for x in 0..w {
            if !rocky(level, x, y) {
                continue;
            }
            let edge = [(1, 0), (-1, 0), (0, 1), (0, -1)].iter().any(|(dx, dy)| !rocky(level, x + dx, y + dy));
            if edge {
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
            if dist[i] == 255 && rocky(level, nx, ny) {
                dist[i] = d.saturating_add(1).min(254);
                queue.push_back((nx, ny));
            }
        }
    }
    dist
}

/// How far below its floor the rock under a square hangs: deeper the further in, ragged.
fn depth(dist: u8, x: i32, y: i32) -> f64 {
    let d = dist as f64;
    (EDGE_DEPTH + d * PER_SQUARE + (unit(hash(x, y, 7)) - 0.4) * 1.1).clamp(EDGE_DEPTH * 0.6, MAX_DEPTH - unit(hash(x, y, 8)) * 2.0)
}

fn build(world: &World, data: &GameData, sprites: &Sprites, dist: &[Vec<u8>], level: u8, cx: i32, cy: i32) -> Chunk {
    let mut chunk = Chunk::default();
    let floor_h = world.levels.as_ref().map_or(1.0, |l| l.height);
    let base = level as f64 * floor_h - SLAB;
    let w = world.width;
    let Some(dist) = dist.get(level as usize) else { return chunk };
    let at = |x: i32, y: i32| -> Option<u8> {
        if x < 0 || y < 0 || x >= w || y >= world.height {
            return None;
        }
        Some(dist[(x + y * w) as usize]).filter(|&d| d != 255)
    };
    for y in cy * CHUNK..(cy + 1) * CHUNK {
        for x in cx * CHUNK..(cx + 1) * CHUNK {
            let Some(tile) = world.level_tile(level, x, y) else { continue };
            let Some(ground) = data.grounds.get(&tile) else { continue };
            if ground.xml.has("Piers") {
                bridge(world, data, sprites, &mut chunk, level, x, y, tile);
                continue;
            }
            let Some(d) = at(x, y) else { continue };
            let bottom = base - depth(d, x, y);
            let (fx, fy) = (x as f64, y as f64);
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
                    chunk.faces.push(RockFace { a, b, top, bottom, deep });
                }
            }
            // a cloud under the rim, now and then
            if d == 0 && hash(x, y, 3) % 3 == 0 {
                let out = sides.iter().find(|s| at(x + s.2 .0, y + s.2 .1).is_none()).map_or((0, 0), |s| s.2);
                let r = 0.9 + unit(hash(x, y, 5)) * 1.1;
                chunk.puffs.push(Puff {
                    x: fx + 0.5 + out.0 as f64 * 0.45,
                    y: fy + 0.5 + out.1 as f64 * 0.45,
                    z: base - 0.5 - unit(hash(x, y, 6)) * 0.9,
                    r,
                    seed: hash(x, y, 9),
                });
            }
        }
    }
    chunk
}

/// Under a bridge's deck square: on its edges, every `PIER_EVERY` squares along, a pier down into
/// the clouds (a cloud at its foot), and a beam across under the deck to the far edge's.
#[allow(clippy::too_many_arguments)]
fn bridge(world: &World, data: &GameData, sprites: &Sprites, chunk: &mut Chunk, level: u8, x: i32, y: i32, tile: u16) {
    let Some(s) = world.surface(level, x, y, data) else { return };
    let deck = |px: i32, py: i32| {
        (0..world.level_count() as u8).filter_map(|l| world.surface(l, px, py, data)).any(|v| (v - s).abs() <= bsk_world::STEP)
    };
    let rect = crate::ground::face(data, sprites, tile, x, y).map(|(r, _)| r);
    let top = s - SLAB;
    // which way it runs: along x if its north or south side is open
    let (ns_open, we_open) = (!deck(x, y - 1) || !deck(x, y + 1), !deck(x - 1, y) || !deck(x + 1, y));
    for (open, along, (nx, ny)) in [(!deck(x, y - 1), x, (0, -1)), (!deck(x, y + 1), x, (0, 1)), (!deck(x - 1, y), y, (-1, 0)), (!deck(x + 1, y), y, (1, 0))] {
        if !open || along.rem_euclid(PIER_EVERY) != 0 {
            continue;
        }
        // only on the sides that run along the bridge, not its ends
        if (ny != 0 && !ns_open) || (nx != 0 && !we_open) || (ny != 0 && we_open && !ns_open) {
            continue;
        }
        let (px, py) = (x as f64 + 0.5 + nx as f64 * 0.38, y as f64 + 0.5 + ny as f64 * 0.38);
        chunk.timbers.push(Timber { a: [px, py, top], b: [px, py, top - PIER_LENGTH], rect });
        chunk.puffs.push(Puff { x: px, y: py, z: top - PIER_LENGTH + 0.2, r: 0.8 + unit(hash(x, y, 11)) * 0.5, seed: hash(x, y, 12) });
        // the cross beam: to the far side, the way across
        let (mut fx, mut fy) = (x, y);
        while deck(fx - nx, fy - ny) && (fx - x).abs() + (fy - y).abs() < 8 {
            fx -= nx;
            fy -= ny;
        }
        if (fx, fy) != (x, y) && (nx > 0 || ny > 0) {
            let (qx, qy) = (fx as f64 + 0.5 - nx as f64 * 0.38, fy as f64 + 0.5 - ny as f64 * 0.38);
            chunk.timbers.push(Timber { a: [px, py, top - 0.12], b: [qx, qy, top - 0.12], rect });
        }
    }
}
