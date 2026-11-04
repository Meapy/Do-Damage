//! Ground tiles as the AIR client draws them (`Square.rebuild3D`, `SquareFace`, `TileRedrawer`):
//! a tile whose neighbours have a higher blend priority gets their texture blended into its
//! corners through the edge masks; animated tiles (water, lava) scroll their texture. Composite
//! tiles take a quarter from each neighbour; tiles with `<Edge>` draw it toward empty squares.

use crate::sprites::Sprites;
use bsk_data::{AnimateKind, GameData, TextureData, TextureRef};
use bsk_render::{AtlasRect, Quad, Renderer, Tint};
use bsk_world::World;
use image::RgbaImage;

const INNER: usize = 0;
const SIDE0: usize = 1;
const SIDE1: usize = 2;
const OUTER: usize = 3;
const INNER_P1: usize = 4;
const INNER_P2: usize = 5;

/// What to draw for one square.
#[derive(Clone, Copy)]
pub struct TileLayers {
    /// The tile's own texture (scrolling if animated), unless the blended image replaces it.
    pub base: Option<(AtlasRect, AnimateKind, f64, f64)>,
    /// The blended corners, over the base.
    pub overlay: Option<AtlasRect>,
    /// How far objects standing here sink, in sprite pixels (`Square.sink_`).
    pub sink: i32,
}

#[derive(Default)]
pub struct Ground {
    /// Per quadrant (0..4): the six mask sets, each a list of 4×4 alpha images.
    masks: Option<Vec<[Vec<RgbaImage>; 6]>>,
    cache: rustc_hash::FxHashMap<([u16; 9], bool), Option<AtlasRect>>,
    composites: rustc_hash::FxHashMap<[u16; 4], Option<AtlasRect>>,
    /// Edged tiles by (type, which of the eight neighbours need no edge).
    edges: rustc_hash::FxHashMap<(u16, u8), Option<AtlasRect>>,
    /// Each square's layers once worked out (outer None: not yet), for the World `world_id`,
    /// up to `log_seen` of its tile log. A square is worked out again when it or a neighbour
    /// changes.
    squares: Vec<Option<Option<TileLayers>>>,
    world_id: u64,
    log_seen: usize,
    /// Connected walls' faces and top texture by object id, with the CONNECT stat and type
    /// they were worked out for (they only change if those do).
    pub walls: rustc_hash::FxHashMap<i32, (i32, u16, std::rc::Rc<WallLook>)>,
}

/// What a connected wall draws: its faces, its side and top textures.
pub struct WallLook {
    pub faces: Vec<crate::connected::Face>,
    pub rotation: i32,
    pub side: Option<AtlasRect>,
    pub top: Option<AtlasRect>,
}

/// The tile type of a composite square (`TileRedrawer`'s 253).
const COMPOSITE: u16 = 253;
/// `getCompositeSig`'s "nothing here".
const NONE: u16 = 255;

/// `BitmapUtil.rotateBitmapData` for square images: `turns` quarter turns clockwise.
fn rotate(img: &RgbaImage, turns: i32) -> RgbaImage {
    let n = img.width();
    RgbaImage::from_fn(n, n, |x, y| {
        let (sx, sy) = match turns.rem_euclid(4) {
            0 => (x, y),
            1 => (y, n - 1 - x),
            2 => (n - 1 - x, n - 1 - y),
            _ => (n - 1 - y, x),
        };
        *img.get_pixel(sx, sy)
    })
}

fn texture0(data: &GameData, sprites: &Sprites, tile: u16) -> Option<RgbaImage> {
    // GroundLibrary.getBitmapData(type): texture 0, even for random textures.
    let ground = data.grounds.get(&tile)?;
    match &ground.texture.pick(0).main {
        TextureRef::Image { set, index } => sprites.tile_src(set, *index).map(|r| sprites.pixels(r)),
        _ => None,
    }
}

/// `TextureData.getTexture(0)` of a ground's Edge, Corner or InnerCorner.
fn image0(sprites: &Sprites, tex: &TextureData) -> Option<RgbaImage> {
    match &tex.pick(0).main {
        TextureRef::Image { set, index } => sprites.tile_src(set, *index).map(|r| sprites.pixels(r)),
        _ => None,
    }
}

/// `copyPixels(src, src.rect, origin, null, null, mergeAlpha = true)`: `src` laid over `out`.
fn draw_over(out: &mut RgbaImage, src: &RgbaImage) {
    let opaque = RgbaImage::from_pixel(4, 4, image::Rgba([0, 0, 0, 255]));
    for py in (0..src.height().min(out.height())).step_by(4) {
        for px in (0..src.width().min(out.width())).step_by(4) {
            copy_masked(out, src, &opaque, px, py);
        }
    }
}

fn texture_rect(sprites: &Sprites, tex: &TextureData, pick: i32) -> Option<AtlasRect> {
    match &tex.pick(pick).main {
        TextureRef::Image { set, index } => sprites.tile(set, *index),
        _ => None,
    }
}

impl Ground {
    fn masks(&mut self, sprites: &Sprites) -> &Vec<[Vec<RgbaImage>; 6]> {
        self.masks.get_or_insert_with(|| {
            let set = |name: &str| -> Vec<RgbaImage> {
                let count = sprites.set(name).map_or(0, |s| s.count);
                (0..count).filter_map(|i| sprites.tile_src(name, i)).map(|r| sprites.pixels(r)).collect()
            };
            let (inner, side, outer, p1, p2) =
                (set("inner_mask"), set("sides_mask"), set("outer_mask"), set("innerP1_mask"), set("innerP2_mask"));
            let rot = |v: &Vec<RgbaImage>, t: i32| v.iter().map(|m| rotate(m, t)).collect::<Vec<_>>();
            [-1, 0, 2, 1]
                .iter()
                .map(|&i| [rot(&inner, i), rot(&side, i - 1), rot(&side, i), rot(&outer, i), rot(&p1, i), rot(&p2, i)])
                .collect()
        })
    }

    /// Forget squares whose tile, or a neighbour's, changed since last time (all of them for a
    /// new map).
    fn sync(&mut self, world: &World) {
        let n = (world.width.max(0) * world.height.max(0)) as usize;
        if self.world_id != world.id() || self.squares.len() != n {
            self.world_id = world.id();
            self.squares = vec![None; n];
            self.log_seen = 0;
            self.walls.clear();
        }
        let log = world.tile_log();
        if self.log_seen == log.len() {
            return;
        }
        let w = world.width;
        for &i in &log[self.log_seen..] {
            let (x, y) = (i as i32 % w, i as i32 / w);
            for dy in -1..=1 {
                for dx in -1..=1 {
                    let (nx, ny) = (x + dx, y + dy);
                    if nx >= 0 && ny >= 0 && nx < w && ny < world.height {
                        self.squares[(nx + ny * w) as usize] = None;
                    }
                }
            }
        }
        self.log_seen = log.len();
    }

    /// The layers for square (x, y).
    pub fn layers(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, x: i32, y: i32) -> Option<TileLayers> {
        self.sync(world);
        if x < 0 || y < 0 || x >= world.width || y >= world.height {
            return None;
        }
        let i = (x + y * world.width) as usize;
        if let Some(cached) = self.squares[i] {
            return cached;
        }
        let layers = self.work_out(world, data, sprites, renderer, x, y);
        self.squares[i] = Some(layers);
        layers
    }

    fn work_out(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, x: i32, y: i32) -> Option<TileLayers> {
        let tile = world.tile(x, y)?;
        let ground = data.grounds.get(&tile)?;
        let animated = ground.animate.kind != AnimateKind::None;
        let base_rect = texture_rect(sprites, &ground.texture, bsk_data::ground::tile_hash(x, y));
        // TileRedrawer.redraw: composite, else edges, else blending.
        let overlay = if tile == COMPOSITE {
            self.composite(world, data, sprites, renderer, x, y)
        } else if ground.edge.is_some() {
            self.edged(world, data, sprites, renderer, x, y)
        } else {
            self.blended(world, data, sprites, renderer, x, y, !animated)
        };
        let sink = if ground.sink { if overlay.is_some() { 6 } else { 12 } } else { 0 };
        let base = if animated {
            base_rect.map(|r| (r, ground.animate.kind, ground.animate.dx, ground.animate.dy))
        } else if overlay.is_some() {
            // The blended image already holds the tile's own texture underneath.
            None
        } else {
            base_rect.map(|r| (r, AnimateKind::None, 0.0, 0.0))
        };
        Some(TileLayers { base, overlay, sink })
    }

    /// `getCompositeSig` and `buildComposite`: each quarter shows the neighbour beside it with
    /// the higher composite priority, or the diagonal one if neither side takes part.
    fn composite(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, x: i32, y: i32) -> Option<AtlasRect> {
        let priority = |t: Option<u16>| t.and_then(|t| data.grounds.get(&t)).map_or(-1, |g| g.composite_priority);
        let at = |dx: i32, dy: i32| world.tile(x + dx, y + dy);
        let (n1, n3, n5, n7) = (at(0, -1), at(-1, 0), at(1, 0), at(0, 1));
        let (p1, p3, p5, p7) = (priority(n1), priority(n3), priority(n5), priority(n7));
        let pick = |pa: i32, a: Option<u16>, pb: i32, b: Option<u16>, diagonal: (i32, i32)| -> u16 {
            if pa < 0 && pb < 0 {
                let d = at(diagonal.0, diagonal.1);
                if priority(d) < 0 { NONE } else { d.unwrap_or(NONE) }
            } else if pa < pb {
                b.unwrap_or(NONE)
            } else {
                a.unwrap_or(NONE)
            }
        };
        let sig = [
            pick(p1, n1, p3, n3, (-1, -1)),
            pick(p1, n1, p5, n5, (1, -1)),
            pick(p3, n3, p7, n7, (-1, 1)),
            pick(p5, n5, p7, n7, (1, 1)),
        ];
        if let Some(cached) = self.composites.get(&sig) {
            return *cached;
        }
        // An opaque black 8×8 with each neighbour's quarter copied in.
        let mut out = RgbaImage::from_pixel(8, 8, image::Rgba([0, 0, 0, 255]));
        for (q, &t) in sig.iter().enumerate() {
            let Some(tex) = (t != NONE).then(|| texture0(data, sprites, t)).flatten() else { continue };
            let (px, py) = ((q as u32 % 2) * 4, (q as u32 / 2) * 4);
            for yy in py..(py + 4).min(tex.height()) {
                for xx in px..(px + 4).min(tex.width()) {
                    let p = tex.get_pixel(xx, yy);
                    out.put_pixel(xx, yy, image::Rgba([p[0], p[1], p[2], 255]));
                }
            }
        }
        let result = renderer.add_image(&out);
        self.composites.insert(sig, result);
        result
    }

    /// `getEdgeSig` and `drawEdges`: the tile with its edge drawn on each side that borders an
    /// empty square (or, in SameTypeEdgeMode, any other type), and corners to match.
    fn edged(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, x: i32, y: i32) -> Option<AtlasRect> {
        let tile = world.tile(x, y)?;
        let ground = data.grounds.get(&tile)?;
        // Bit i set: neighbour i (row by row, 4 = the tile) needs no edge.
        let mut bits = 0u16;
        let mut i = 0;
        for ny in y - 1..=y + 1 {
            for nx in x - 1..=x + 1 {
                if i != 4 {
                    let ok = match world.tile(nx, ny) {
                        None => true,
                        Some(t) if ground.same_type_edge_mode => t == tile,
                        Some(t) => t != NONE,
                    };
                    if ok {
                        bits |= 1 << i;
                    }
                }
                i += 1;
            }
        }
        if bits == 0b1_1110_1111 {
            return None;
        }
        // Pack the eight neighbours into a byte for the cache.
        let key = ((bits & 0b1111) | ((bits >> 1) & 0b1111_0000)) as u8;
        if let Some(cached) = self.edges.get(&(tile, key)) {
            return *cached;
        }
        let result = self.draw_edges(data, sprites, tile, bits).and_then(|img| renderer.add_image(&img));
        self.edges.insert((tile, key), result);
        result
    }

    fn draw_edges(&mut self, data: &GameData, sprites: &Sprites, tile: u16, bits: u16) -> Option<RgbaImage> {
        let ground = data.grounds.get(&tile)?;
        let sig = |i: usize| bits & (1 << i) != 0;
        let mut out = texture0(data, sprites, tile)?;
        // getEdges: the edge faces west (3), turned for north, east and south.
        let edge = image0(sprites, ground.edge.as_ref()?)?;
        let mut edges: [Option<RgbaImage>; 9] = Default::default();
        edges[3] = Some(edge.clone());
        edges[1] = Some(rotate(&edge, 1));
        edges[5] = Some(rotate(&edge, 2));
        edges[7] = Some(rotate(&edge, 3));
        if let Some(corner) = ground.corner.as_ref().and_then(|c| image0(sprites, c)) {
            edges[2] = Some(rotate(&corner, 1));
            edges[8] = Some(rotate(&corner, 2));
            edges[6] = Some(rotate(&corner, 3));
            edges[0] = Some(corner);
        }
        for i in [1, 3, 5, 7] {
            if !sig(i) {
                if let Some(e) = &edges[i] {
                    draw_over(&mut out, e);
                }
            }
        }
        if edges[0].is_some() {
            for (i, a, b) in [(0, 3, 1), (2, 1, 5), (8, 5, 7), (6, 3, 7)] {
                if sig(a) && sig(b) && !sig(i) {
                    if let Some(e) = &edges[i] {
                        draw_over(&mut out, e);
                    }
                }
            }
        }
        // getInnerCorners: the edges, with the inner corner (turned) in the corner slots.
        if let Some(inner) = ground.inner_corner.as_ref().and_then(|c| image0(sprites, c)) {
            let corners = [(0, rotate(&inner, 0)), (2, rotate(&inner, 1)), (8, rotate(&inner, 2)), (6, rotate(&inner, 3))];
            for (i, img) in corners {
                let (a, b) = match i {
                    0 => (3, 1),
                    2 => (1, 5),
                    8 => (5, 7),
                    _ => (3, 7),
                };
                if !sig(a) && !sig(b) {
                    draw_over(&mut out, &img);
                }
            }
        }
        Some(out)
    }

    /// `TileRedrawer.redraw` for plain tiles (`getSig` and `redrawRect`).
    fn blended(&mut self, world: &World, data: &GameData, sprites: &Sprites, renderer: &mut Renderer, x: i32, y: i32, with_background: bool) -> Option<AtlasRect> {
        let tile = world.tile(x, y)?;
        let priority = data.grounds.get(&tile).map_or(-1, |g| g.blend_priority);
        let mut sig = [tile; 9];
        let mut i = 0;
        for ny in y - 1..=y + 1 {
            for nx in x - 1..=x + 1 {
                if (nx, ny) != (x, y) {
                    if let Some(n) = world.tile(nx, ny) {
                        if data.grounds.get(&n).map_or(-1, |g| g.blend_priority) > priority {
                            sig[i] = n;
                        }
                    }
                }
                i += 1;
            }
        }
        if sig.iter().all(|&t| t == tile) {
            return None;
        }
        if let Some(cached) = self.cache.get(&(sig, with_background)) {
            return *cached;
        }
        let result = self.build(data, sprites, &sig, with_background).and_then(|img| renderer.add_image(&img));
        self.cache.insert((sig, with_background), result);
        result
    }

    fn build(&mut self, data: &GameData, sprites: &Sprites, sig: &[u16; 9], with_background: bool) -> Option<RgbaImage> {
        let base = sig[4];
        let s = |i: usize| sig[i] != base;
        let mut redraw = [false; 4];
        if s(1) {
            redraw[0] = true;
            redraw[1] = true;
        }
        if s(3) {
            redraw[0] = true;
            redraw[2] = true;
        }
        if s(5) {
            redraw[1] = true;
            redraw[3] = true;
        }
        if s(7) {
            redraw[2] = true;
            redraw[3] = true;
        }
        redraw[0] |= s(0);
        redraw[1] |= s(2);
        redraw[2] |= s(6);
        redraw[3] |= s(8);
        if !redraw.iter().any(|&r| r) {
            return None;
        }
        let orig = texture0(data, sprites, base)?;
        let mut out = if with_background { orig.clone() } else { RgbaImage::new(orig.width(), orig.height()) };
        // Quadrant, its corner, and the neighbours (n0, n1, n2) around it.
        let quads = [(0, (0, 0), (3, 0, 1)), (1, (4, 0), (1, 2, 5)), (2, (0, 4), (7, 6, 3)), (3, (4, 4), (5, 8, 7))];
        let seed = sig.iter().fold(0u32, |h, &t| h.wrapping_mul(31).wrapping_add(t as u32));
        for (q, (px, py), (a, b, c)) in quads {
            if redraw[q] {
                let pick = (seed.wrapping_add(q as u32 * 7919)) as usize;
                self.redraw_rect(data, sprites, &mut out, q, px, py, base, sig[a], sig[b], sig[c], pick);
            }
        }
        Some(out)
    }

    #[allow(clippy::too_many_arguments)]
    fn redraw_rect(&mut self, data: &GameData, sprites: &Sprites, out: &mut RgbaImage, q: usize, px: u32, py: u32, base: u16, n0: u16, n1: u16, n2: u16, pick: usize) {
        let masks = self.masks(sprites)[q].clone();
        let choose = |kind: usize| masks[kind].get(pick % masks[kind].len().max(1)).cloned();
        let (blend, mask) = if base == n0 && base == n2 {
            (n1, choose(OUTER))
        } else if base != n0 && base != n2 {
            if n0 != n2 {
                if let (Some(t0), Some(m0)) = (texture0(data, sprites, n0), choose(INNER_P1)) {
                    copy_masked(out, &t0, &m0, px, py);
                }
                if let (Some(t2), Some(m2)) = (texture0(data, sprites, n2), choose(INNER_P2)) {
                    copy_masked(out, &t2, &m2, px, py);
                }
                return;
            }
            (n0, choose(INNER))
        } else if base != n0 {
            (n0, choose(SIDE0))
        } else {
            (n2, choose(SIDE1))
        };
        if let (Some(tex), Some(mask)) = (texture0(data, sprites, blend), mask) {
            copy_masked(out, &tex, &mask, px, py);
        }
    }
}

/// `copyPixels(source, rect at (px, py), (px, py), mask, (0, 0), mergeAlpha = true)`: the 4×4
/// block of `src` at (px, py), its alpha multiplied by the mask's, laid over `out`.
fn copy_masked(out: &mut RgbaImage, src: &RgbaImage, mask: &RgbaImage, px: u32, py: u32) {
    for my in 0..mask.height().min(4) {
        for mx in 0..mask.width().min(4) {
            let (x, y) = (px + mx, py + my);
            if x >= src.width() || y >= src.height() || x >= out.width() || y >= out.height() {
                continue;
            }
            let s = src.get_pixel(x, y);
            let a = (s[3] as f32 / 255.0) * (mask.get_pixel(mx, my)[3] as f32 / 255.0);
            let d = out.get_pixel_mut(x, y);
            let da = d[3] as f32 / 255.0;
            let oa = a + da * (1.0 - a);
            if oa > 0.0 {
                for c in 0..3 {
                    d[c] = ((s[c] as f32 * a + d[c] as f32 * da * (1.0 - a)) / oa).round() as u8;
                }
            }
            d[3] = (oa * 255.0).round() as u8;
        }
    }
}

/// `q` grown by `px` screen pixels on every side. Neighbouring tiles share edges that don't
/// always land the same after rounding; along a line of pixel centres that leaves a hairline
/// gap, so ground pieces overlap a little.
fn grown(q: Quad, px: f32) -> Quad {
    let len = |a: [f32; 2]| a[0].hypot(a[1]).max(1e-3);
    let (ex, ey) = (px / len(q.x_axis), px / len(q.y_axis));
    Quad {
        origin: [
            q.origin[0] - q.x_axis[0] * ex - q.y_axis[0] * ey,
            q.origin[1] - q.x_axis[1] * ex - q.y_axis[1] * ey,
        ],
        x_axis: [q.x_axis[0] * (1.0 + 2.0 * ex), q.x_axis[1] * (1.0 + 2.0 * ex)],
        y_axis: [q.y_axis[0] * (1.0 + 2.0 * ey), q.y_axis[1] * (1.0 + 2.0 * ey)],
    }
}

/// Draw a tile texture over `quad`, its texture shifted by (ox, oy) tiles and wrapping, as
/// `SquareFace` does for animated ground. Up to four pieces.
pub fn draw_scrolled(renderer: &mut Renderer, rect: AtlasRect, quad: Quad, ox: f64, oy: f64, tint: Tint) {
    let ox = ox.rem_euclid(1.0) as f32;
    let oy = oy.rem_euclid(1.0) as f32;
    // Along each axis: (start, end) on the tile, and where that piece starts in the texture.
    let spans = |o: f32| -> Vec<(f32, f32, f32)> {
        if o == 0.0 { vec![(0.0, 1.0, 0.0)] } else { vec![(0.0, 1.0 - o, o), (1.0 - o, 1.0, 0.0)] }
    };
    for &(x0, x1, u0) in &spans(ox) {
        for &(y0, y1, v0) in &spans(oy) {
            let origin = [
                quad.origin[0] + quad.x_axis[0] * x0 + quad.y_axis[0] * y0,
                quad.origin[1] + quad.x_axis[1] * x0 + quad.y_axis[1] * y0,
            ];
            let piece = Quad {
                origin,
                x_axis: [quad.x_axis[0] * (x1 - x0), quad.x_axis[1] * (x1 - x0)],
                y_axis: [quad.y_axis[0] * (y1 - y0), quad.y_axis[1] * (y1 - y0)],
            };
            renderer.sprite_part(rect, [u0, v0, u0 + (x1 - x0), v0 + (y1 - y0)], grown(piece, 0.3), tint);
        }
    }
}

/// `SquareFace.draw`'s texture offset for an animated tile at `time` ms.
pub fn scroll(kind: AnimateKind, dx: f64, dy: f64, time: i32) -> (f64, f64) {
    let t = time as f64 / 1000.0;
    match kind {
        AnimateKind::Wave => ((dx * t).sin(), (dy * t).sin()),
        AnimateKind::Flow => (dx * t, dy * t),
        AnimateKind::None => (0.0, 0.0),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rotation_is_clockwise() {
        // A pixel at the top right goes to the bottom right after one clockwise turn.
        let mut img = RgbaImage::new(4, 4);
        img.put_pixel(3, 0, image::Rgba([1, 2, 3, 255]));
        assert_eq!(rotate(&img, 1).get_pixel(3, 3)[3], 255);
        assert_eq!(rotate(&img, -1).get_pixel(0, 0)[3], 255);
        assert_eq!(rotate(&img, 2).get_pixel(0, 3)[3], 255);
    }

    #[test]
    fn edge_key_packs_the_neighbours() {
        // All eight neighbours fine (bit 4, the tile, unused) packs to 0xFF.
        let bits: u16 = 0b1_1110_1111;
        assert_eq!(((bits & 0b1111) | ((bits >> 1) & 0b1111_0000)) as u8, 0xFF);
    }

    #[test]
    fn masked_copy() {
        let mut out = RgbaImage::from_pixel(8, 8, image::Rgba([0, 0, 255, 255]));
        let src = RgbaImage::from_pixel(8, 8, image::Rgba([255, 0, 0, 255]));
        let mut mask = RgbaImage::new(4, 4);
        mask.put_pixel(0, 0, image::Rgba([0, 0, 0, 255]));
        copy_masked(&mut out, &src, &mask, 4, 4);
        assert_eq!(*out.get_pixel(4, 4), image::Rgba([255, 0, 0, 255]), "masked in");
        assert_eq!(*out.get_pixel(5, 4), image::Rgba([0, 0, 255, 255]), "masked out");
        assert_eq!(*out.get_pixel(0, 0), image::Rgba([0, 0, 255, 255]), "other quadrant untouched");
    }
}
