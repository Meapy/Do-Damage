//! Drawing the world (`Map.draw`): ground tiles, then shadows, then objects and walls sorted by
//! their screen depth.
//!
//! The camera (`Camera.configure`, without perspective) maps a world point (x, y, z) to the
//! screen as `scale × (r·d, u·d − z)`, d being the offset from the camera, r = (cos a, sin a) and
//! u = (−sin a, cos a). That's affine, so every tile, wall face and sprite is one quad.

use crate::connected;
use crate::ground::Ground;
use crate::redraw::{Dye, Redrawer, PAD};
use crate::sprites::{SrcRect, Sprites};
use crate::text::{Fonts, Style, Text};
use bsk_assets::Facing;
use bsk_data::{GameData, Model, ObjectClass, ObjectDesc, TextureData, TextureRef};
use bsk_render::{Filter, Quad, Renderer, Tint};
use bsk_world::condition::{self, has};
use bsk_world::{GameObj, Projectile, World};

/// Pixels per tile at map scale 1, as in the AIR client.
pub const TILE_PX: f64 = 50.0;

#[derive(Debug, Clone, Copy)]
pub struct Camera {
    pub x: f64,
    pub y: f64,
    pub angle: f64,
    /// Pixels per tile.
    pub scale: f64,
    /// Where the camera point is on the screen.
    pub cx: f64,
    pub cy: f64,
    /// The height the camera point is at: ours, on stacked floors.
    pub z: f64,
    /// The angle's sine and cosine, worked out once (every point drawn uses them).
    sin: f64,
    cos: f64,
}

impl Camera {
    pub fn new(x: f64, y: f64, angle: f64, scale: f64, cx: f64, cy: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Camera { x, y, angle, scale, cx, cy, z: 0.0, sin, cos }
    }

    /// The same view looking at the ground `z` tiles up (ours, on stacked floors).
    pub fn at_height(self, z: f64) -> Self {
        Camera { z, ..self }
    }

    /// The same view moved by under a pixel so the world's origin is on a whole pixel: the
    /// ground's pixels then keep their places as the camera follows us, instead of shimmering
    /// with every fraction of a pixel it moves.
    pub fn snapped(self) -> Self {
        let (o, _, _) = self.affine();
        let (dx, dy) = ((self.cx - o[0].round()) / self.scale, (self.cy - o[1].round()) / self.scale + self.z);
        let (s, c) = (self.sin, self.cos);
        Camera { x: dx * c - dy * s, y: dx * s + dy * c, ..self }
    }

    pub fn to_screen(&self, x: f64, y: f64, z: f64) -> [f32; 2] {
        let (s, c) = (self.sin, self.cos);
        let (dx, dy) = (x - self.x, y - self.y);
        let sx = dx * c + dy * s;
        let sy = -dx * s + dy * c - (z - self.z);
        [(self.cx + sx * self.scale) as f32, (self.cy + sy * self.scale) as f32]
    }

    /// `to_screen` as an affine map: where (0, 0, 0) lands, and the steps for one tile along x
    /// and along y (z goes straight up the screen by `scale`). Exact, and cheap for many points.
    fn affine(&self) -> ([f64; 2], [f64; 2], [f64; 2]) {
        let (s, c) = (self.sin, self.cos);
        let o = [self.cx - (self.x * c + self.y * s) * self.scale, self.cy - (-self.x * s + self.y * c - self.z) * self.scale];
        (o, [c * self.scale, -s * self.scale], [s * self.scale, c * self.scale])
    }

    /// The ground point under a screen point (`Map.pSTopW`), at the camera's height.
    pub fn to_world(&self, px: f64, py: f64) -> (f64, f64) {
        let (s, c) = (self.sin, self.cos);
        let (sx, sy) = ((px - self.cx) / self.scale, (py - self.cy) / self.scale);
        (self.x + sx * c - sy * s, self.y + sx * s + sy * c)
    }

    /// The quad through world points p0, p1 (along the texture's x) and p3 (along its y).
    fn quad(&self, p0: [f64; 3], p1: [f64; 3], p3: [f64; 3]) -> Quad {
        let o = self.to_screen(p0[0], p0[1], p0[2]);
        let a = self.to_screen(p1[0], p1[1], p1[2]);
        let b = self.to_screen(p3[0], p3[1], p3[2]);
        Quad { origin: o, x_axis: [a[0] - o[0], a[1] - o[1]], y_axis: [b[0] - o[0], b[1] - o[1]] }
    }

    /// The world area (in tiles from the camera) that can be on screen.
    fn radius(&self, w: f64, h: f64) -> f64 {
        let far_x = self.cx.max(w - self.cx);
        let far_y = self.cy.max(h - self.cy) + 2.0 * self.scale;
        (far_x * far_x + far_y * far_y).sqrt() / self.scale + 1.0
    }
}

/// `Lighting3D.shadeValue`: 0.75 ambient plus the rest from a light along (1, 3, 2).
fn shade(normal: [f64; 3]) -> f32 {
    shade_with(normal, 0.25)
}

/// The same light, `contrast` of it directional and the rest ambient (`<ModelContrast>`: the
/// Nexus garden's models take more, so their roofs and walls read as solid).
fn shade_with(normal: [f64; 3], contrast: f64) -> f32 {
    let len = (1.0f64 + 9.0 + 4.0).sqrt();
    let light = [1.0 / len, 3.0 / len, 2.0 / len];
    let n_len = (normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]).sqrt();
    let dot = (normal[0] * light[0] + normal[1] * light[1] + normal[2] * light[2]) / n_len;
    (1.0 - contrast + contrast * dot.max(0.0)) as f32
}

/// The ground under a model in shadow (`<ModelShadow>` its opacity): every corner cast down onto
/// the ground away from the light, toward the south-west so it shows past the model in this
/// view, and the outline of all of them filled. Drawn before anything stands on the ground.
fn draw_model_shadow(scene: &Scene, renderer: &mut Renderer, obj: &GameObj, desc: &ObjectDesc, model: &Model, alpha: f32) {
    let cam = scene.camera;
    let (s, c) = desc.rotation.to_radians().sin_cos();
    let mut pts: Vec<[f64; 2]> = model
        .vertices
        .iter()
        .map(|(p, _)| {
            let (x, y) = (p[0] * c - p[1] * s, p[0] * s + p[1] * c);
            [obj.x + x - p[2] * 0.42, obj.y + y + p[2] * 0.3]
        })
        .collect();
    // the convex hull (monotone chain)
    pts.sort_by(|a, b| a[0].total_cmp(&b[0]).then(a[1].total_cmp(&b[1])));
    pts.dedup_by(|a, b| (a[0] - b[0]).abs() < 1e-6 && (a[1] - b[1]).abs() < 1e-6);
    if pts.len() < 3 {
        return;
    }
    let cross = |o: [f64; 2], a: [f64; 2], b: [f64; 2]| (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
    let mut hull: Vec<[f64; 2]> = Vec::new();
    for pass in 0..2 {
        let start = hull.len();
        let iter: Box<dyn Iterator<Item = &[f64; 2]>> = if pass == 0 { Box::new(pts.iter()) } else { Box::new(pts.iter().rev()) };
        for &p in iter {
            while hull.len() >= start + 2 && cross(hull[hull.len() - 2], hull[hull.len() - 1], p) <= 0.0 {
                hull.pop();
            }
            hull.push(p);
        }
        hull.pop();
    }
    let white = renderer.white();
    // on stacked floors, on its floor's ground
    let base = if scene.world.levels.is_some() { obj.z } else { 0.0 };
    let screen: Vec<[f32; 2]> = hull.iter().map(|p| cam.to_screen(p[0], p[1], base)).collect();
    let tint = Tint::color(0x0A0612, alpha);
    for k in 1..screen.len().saturating_sub(1) {
        renderer.triangle(white, [screen[0], screen[k], screen[k + 1]], [[0.5, 0.5]; 3], tint);
    }
}

/// `Plane3D.computeNormalVec` for a face given by its first three corners.
fn normal(v0: [f64; 3], v1: [f64; 3], v2: [f64; 3]) -> [f64; 3] {
    let u = [v1[0] - v0[0], v1[1] - v0[1], v1[2] - v0[2]];
    let v = [v2[0] - v0[0], v2[1] - v0[1], v2[2] - v0[2]];
    [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
}

/// Text rising over an object (`CharacterStatusText`): damage numbers, notifications.
#[derive(Debug, Clone)]
pub struct Floater {
    pub object_id: i32,
    pub text: String,
    pub color: u32,
    pub at: i32,
    /// How long it shows, ms.
    pub life: i32,
}

/// Damage numbers: red, or purple for a hit that ignored defense (an `ArmorPiercing` shot).
pub const DAMAGE_COLOR: u32 = 0xFF0000;
pub const PIERCED_COLOR: u32 = 0xC060FF;

impl Floater {
    /// A damage number: red, for a second.
    pub fn damage(object_id: i32, amount: i32, at: i32) -> Self {
        Floater::hit(object_id, amount, false, at)
    }

    /// A shot's damage number, purple when the shot ignored defense.
    pub fn hit(object_id: i32, amount: i32, pierced: bool, at: i32) -> Self {
        let color = if pierced { PIERCED_COLOR } else { DAMAGE_COLOR };
        Floater { object_id, text: format!("-{amount}"), color, at, life: 1000 }
    }

    pub fn is_damage(&self) -> bool {
        (self.color == DAMAGE_COLOR || self.color == PIERCED_COLOR) && self.text.starts_with('-')
    }
}

/// `SpeechBalloon`: what someone said, over their head for a few seconds.
#[derive(Debug, Clone)]
pub struct Bubble {
    pub object_id: i32,
    pub text: String,
    /// Background, outline, text.
    pub colors: [u32; 3],
    pub at: i32,
    pub life: i32,
}

pub struct Scene<'a> {
    pub sprites: &'a Sprites,
    pub data: &'a GameData,
    pub world: &'a World,
    pub camera: Camera,
    pub time: i32,
    /// (object id, amount, colour, when) of the damage numbers to show.
    pub floaters: &'a [Floater],
    pub bubbles: &'a [Bubble],
    pub particles: &'a crate::particles::Particles,
    pub show: Show,
    /// We're hallucinating: everything shows as a random character (`Camera.isHallucinating_`).
    pub hallucinating: bool,
    /// On stacked floors, the floors from which up are see-through this frame (`roof`): what
    /// stands on them fades with them.
    pub roof: Option<u8>,
    /// How opaque we ourselves are drawn (less as we die).
    pub me_alpha: f32,
}

impl Scene<'_> {
    /// How far the ground lifts what stands at (x, y) (`<Lift>` grounds), eased between the
    /// middles of the tiles round it so a bridge's arch is climbed smoothly.
    pub fn lift(&self, x: f64, y: f64) -> f64 {
        let lift = |tx: i32, ty: i32| self.world.tile(tx, ty).and_then(|t| self.data.grounds.get(&t)).map_or(0.0, |g| g.lift);
        let (fx, fy) = (x - 0.5, y - 0.5);
        let (x0, y0) = (fx.floor(), fy.floor());
        let (ax, ay) = (fx - x0, fy - y0);
        let (x0, y0) = (x0 as i32, y0 as i32);
        let (a, b, c, d) = (lift(x0, y0), lift(x0 + 1, y0), lift(x0, y0 + 1), lift(x0 + 1, y0 + 1));
        if a == 0.0 && b == 0.0 && c == 0.0 && d == 0.0 {
            return 0.0;
        }
        (a * (1.0 - ax) + b * ax) * (1.0 - ay) + (c * (1.0 - ax) + d * ax) * ay
    }

    /// How high an object stands: on stacked floors its height, eased between squares so a stair
    /// is climbed smoothly; elsewhere the ground's lift.
    pub fn height(&self, obj: &GameObj) -> f64 {
        if self.world.levels.is_some() {
            height_of(self.world, self.data, obj)
        } else {
            self.lift(obj.x, obj.y)
        }
    }

    /// Where an object stands on the screen: its ground point, raised by its height.
    pub fn stand(&self, obj: &GameObj) -> [f32; 2] {
        self.camera.to_screen(obj.x, obj.y, self.height(obj))
    }
}

/// What the options let the player hide.
#[derive(Clone, Copy, Debug)]
pub struct Show {
    pub shadows: bool,
    pub hp_bars: bool,
    pub bubbles: bool,
    /// Other players' projectiles (`allyShots`).
    pub ally_shots: bool,
    /// Other players' pets' projectiles.
    pub pet_shots: bool,
    /// How every pet's projectiles look.
    pub pet_shot_look: crate::options::PetShots,
    /// How opaque other players and other players' pets are.
    pub player_alpha: f32,
    pub pet_alpha: f32,
    pub damage_numbers: bool,
}

/// The image an object shows now, before redrawing.
struct Look {
    src: SrcRect,
    mirror: bool,
    /// The frame's dye mask, for sheets that have one.
    mask: Option<SrcRect>,
    /// Two frames wide (the second attack frame with its sword bit); the body is the first
    /// half (or the second, mirrored).
    wide: bool,
    /// Dyes for the mask other than the object's own (an item a merchant shows).
    tex: Option<(i32, i32)>,
}

const SEC_TO_DIRS: [[Dir; 3]; 8] = {
    use Dir::*;
    [[Left, Up, Down], [Up, Left, Down], [Up, Right, Down], [Right, Up, Down], [Right, Down, Down], [Down, Right, Right], [Down, Left, Left], [Left, Down, Down]]
};

#[derive(Clone, Copy, PartialEq)]
enum Dir {
    Right,
    Left,
    Down,
    Up,
}

impl Scene<'_> {
    fn texture_src(&self, tex: &TextureData, pick: i32) -> Option<SrcRect> {
        match &tex.pick(pick).main {
            TextureRef::Image { set, index } => self.sprites.tile_src(set, *index),
            TextureRef::Animated { set, index } => self.sprites.char_src(set, *index, 0, 0),
            _ => None,
        }
    }

    /// `GameObject.getTexture` for animated characters: the frame for the facing (relative to
    /// the camera), walking or standing.
    fn look(&self, obj: &GameObj, desc: &ObjectDesc) -> Option<Look> {
        // A skin (TEXTURE_STAT) is the object type whose texture replaces the class's.
        let skin = obj.stat_int(76).filter(|&t| t != 0).and_then(|t| self.data.objects.get(&(t as u16)));
        let mut tex = skin.map_or(&desc.texture, |s| &s.texture);
        // ALT_TEXTURE (setAltTexture): one of the type's <AltTexture>s instead, 0 for the normal.
        if let Some(alt) = obj.stat_int(61).filter(|&a| a != 0).and_then(|a| tex.alt.get(&a)) {
            tex = alt;
        }
        let tex = tex.pick(obj.id);
        // Merchant.getTexture: a merchant shows what it sells.
        if desc.class == bsk_data::ObjectClass::Merchant {
            if let Some(item) = obj.stat_int(crate::hud::merch::TYPE).filter(|&t| t > 0).and_then(|t| self.data.objects.get(&(t as u16))) {
                // Cloth and dyes in their own colours (getRedrawnTextureFromType).
                let dye = crate::redraw::item_dye(self.sprites, item);
                return self.texture_src(&item.texture, obj.id).map(|src| Look {
                    src,
                    mirror: false,
                    mask: dye.map(|d| d.mask),
                    wide: false,
                    tex: dye.map(|d| (d.tex1, d.tex2)),
                });
            }
        }
        let TextureRef::Animated { set, index } = &tex.main else {
            // An <Animation> frame, while one plays.
            let tex = obj
                .anim_frame
                .and_then(|(a, f)| desc.animations.get(a)?.frames.get(f))
                .map_or(tex, |frame| frame.texture.pick(obj.id));
            return self.texture_src(tex, obj.id).map(|src| Look { src, mirror: false, mask: None, wide: false, tex: None });
        };
        let sheet = self.sprites.char_sheet(set)?;
        let rows = sheet.rows();
        // Which row holds which direction, and which directions exist (AnimatedChar's dict_).
        let row_of = |d: Dir| -> Option<(u32, bool)> {
            match (sheet.facing, d) {
                (Facing::Right, Dir::Right) => Some((0, false)),
                (Facing::Right, Dir::Left) => Some((0, true)),
                (Facing::Right, Dir::Down) => (rows >= 2).then_some((1, false)),
                (Facing::Down, Dir::Down) => Some((0, false)),
                (Facing::Down, Dir::Right) => (rows >= 2).then_some((1, false)),
                (Facing::Down, Dir::Left) => (rows >= 2).then_some((1, true)),
                (_, Dir::Up) => (rows >= 3).then_some((2, false)),
            }
        };
        // Attacking (GameObject.getTexture): face the attack, play the attack frames.
        let attack_phase = obj.attack_phase(self.time);
        let attacking = attack_phase.is_some();
        let facing = if attacking && !desc.dont_face_attacks { obj.attack_angle } else { obj.facing };
        let rel = bound_to_pi(facing - self.camera.angle);
        let sec = ((rel / std::f64::consts::FRAC_PI_4 + 4.0) as i32).rem_euclid(8) as usize;
        let dirs = SEC_TO_DIRS[sec];
        let (dir, (row, mirror)) = dirs.iter().find_map(|&d| row_of(d).map(|r| (d, r)))?;
        let symmetric = matches!(dir, Dir::Down | Dir::Up);

        if let Some(p) = attack_phase {
            if let Some(look) = self.attack_look(set, *index, row, mirror, p) {
                return Some(look);
            }
        }
        let walking = obj.moving;
        let frame = if walking {
            let period = self.walk_period(obj, desc);
            let p = (self.time.rem_euclid(period)) as f64 / period as f64;
            let walk2 = self.sprites.char_src(set, *index, row, 2)?;
            if p < 0.5 || !self.sprites.is_blank(walk2) {
                if p < 0.5 { 1 } else { 2 }
            } else if symmetric {
                // walk1 mirrored stands in for a blank walk2.
                let src = self.sprites.char_src(set, *index, row, 1)?;
                let mask = self.sprites.char_mask_src(set, *index, row, 1);
                return Some(Look { src, mirror: !mirror, mask, wide: false, tex: None });
            } else {
                0
            }
        } else {
            0
        };
        let src = self.sprites.char_src(set, *index, row, frame)?;
        let mask = self.sprites.char_mask_src(set, *index, row, frame);
        Some(Look { src, mirror, mask, wide: false, tex: None })
    }

    /// How long a walk cycle (both frames) takes, ms. Players: `Player.getTexture`'s
    /// 3.5 / move speed (365 ms at the fastest, 875 at the slowest), never quicker than
    /// `MIN_ANIM_CYCLE` however fast they go; others: `GameObject.getTexture`'s 0.5 / speed,
    /// rounded up to a multiple of 400.
    fn walk_period(&self, obj: &GameObj, desc: &ObjectDesc) -> i32 {
        let speed = if desc.class != ObjectClass::Player {
            let w = (0.5 / obj.walk_speed.max(1e-6)).min(1e6) as i32;
            return w + (400 - w % 400);
        } else if Some(obj.id) == self.world.me {
            obj.walk_speed
        } else {
            bsk_world::move_speed(obj.stat_int(bsk_world::SPEED_STAT).unwrap_or(0), obj.conditions(), 1.0)
        };
        ((3.5 / speed.max(1e-6)) as i32).clamp(bsk_world::MIN_ANIM_CYCLE, 2000)
    }

    /// `AnimatedChar.loadDir`'s attack frames, `p` of the way through a pass: frame 4, then frame 5 (two frames wide with frame 6,
    /// the sword bit, when that isn't blank). `None` if both are blank (then it walks instead).
    fn attack_look(&self, set: &str, index: u32, row: u32, mirror: bool, p: f64) -> Option<Look> {
        let a1 = self.sprites.char_src(set, index, row, 4).filter(|r| !self.sprites.is_blank(*r));
        let a2 = self.sprites.char_src(set, index, row, 5).filter(|r| !self.sprites.is_blank(*r));
        let frames: Vec<SrcRect> = a1.into_iter().chain(a2).collect();
        if frames.is_empty() {
            return None;
        }
        let i = ((p.clamp(0.0, 0.99999) * frames.len() as f64) as usize).min(frames.len() - 1);
        let mut src = frames[i];
        let frame = if Some(src) == a1 { 4 } else { 5 };
        let mut mask = self.sprites.char_mask_src(set, index, row, frame);
        let mut wide = false;
        if frame == 5 {
            if let Some(sword) = self.sprites.char_src(set, index, row, 6).filter(|r| !self.sprites.is_blank(*r)) {
                src.w += sword.w;
                if let Some(m) = &mut mask {
                    m.w += sword.w;
                }
                wide = true;
            }
        }
        Some(Look { src, mirror, mask, wide, tex: None })
    }
}

fn bound_to_pi(a: f64) -> f64 {
    let mut a = a;
    while a < -std::f64::consts::PI {
        a += std::f64::consts::TAU;
    }
    while a > std::f64::consts::PI {
        a -= std::f64::consts::TAU;
    }
    a
}

enum Item<'a> {
    Sprite { obj: &'a GameObj, desc: &'a ObjectDesc },
    Wall { obj: &'a GameObj, desc: &'a ObjectDesc },
    Connected { obj: &'a GameObj, desc: &'a ObjectDesc },
    Model { obj: &'a GameObj, desc: &'a ObjectDesc, model: &'a Model },
    Projectile(&'a Projectile),
    /// Where the ground drops on stacked floors: part of a floor's edge or a step's side, drawn
    /// with the floor at its foot.
    Face { level: u8, plate: Plate },
    /// A flat-coloured triangle on stacked floors (a tower's roof tiers), with its floor.
    Tri { level: u8, pts: [[f32; 2]; 3], tint: Tint },
}

/// A textured quad, worked out before drawing.
#[derive(Clone, Copy)]
struct Plate {
    rect: bsk_render::AtlasRect,
    quad: Quad,
    tint: Tint,
}

/// On stacked floors, how high an object stands: the ground of its floor round it, eased between
/// the middles of the squares (as `Scene::lift`), so it climbs a stair smoothly. Squares with no
/// ground at its height count as its own.
pub fn height_of(world: &World, data: &GameData, obj: &GameObj) -> f64 {
    let surface = |tx: i32, ty: i32| {
        world.surface(obj.level, tx, ty, data).filter(|s| (s - obj.z).abs() <= bsk_world::STEP).unwrap_or(obj.z)
    };
    let (fx, fy) = (obj.x - 0.5, obj.y - 0.5);
    let (x0, y0) = (fx.floor(), fy.floor());
    let (ax, ay) = (fx - x0, fy - y0);
    let (x0, y0) = (x0 as i32, y0 as i32);
    let (a, b, c, d) = (surface(x0, y0), surface(x0 + 1, y0), surface(x0, y0 + 1), surface(x0 + 1, y0 + 1));
    (a * (1.0 - ax) + b * ax) * (1.0 - ay) + (c * (1.0 - ax) + d * ax) * ay
}

/// On stacked floors, the lowest floor above an object that is drawn over it, if any: one with
/// ground where the screen shows that floor at the object's middle. A floor `h` tiles up is drawn
/// `h` tiles further down the screen than the ground under it, so that is the square `h` tiles
/// down the screen from the object (less its own height, and half a tile for its middle).
fn covered_by(world: &World, data: &GameData, cam: &Camera, obj: &GameObj) -> Option<u8> {
    (obj.level + 1..world.level_count() as u8).find(|&l| floor_over(world, data, cam, obj, l, 0.0, 0.5))
}

/// Points of a sprite to test for cover, as (across the screen, up) in tiles: its middle and
/// head, at its middle and either side.
const SPRITE_POINTS: [(f64, f64); 6] = [(0.0, 0.5), (0.0, 1.0), (-0.4, 0.5), (0.4, 0.5), (-0.4, 1.0), (0.4, 1.0)];

/// Whether floor `l` is drawn over any of a sprite's `SPRITE_POINTS`.
fn floor_over_sprite(world: &World, data: &GameData, cam: &Camera, obj: &GameObj, l: u8) -> bool {
    SPRITE_POINTS.iter().any(|&(across, up)| floor_over(world, data, cam, obj, l, across, up))
}

/// Whether floor `l` is drawn over a point of an object, `across` tiles to its right on the screen
/// and `up` above its feet: whether one of its squares, `s` high, lies `s - h` tiles down the
/// screen from the point (`h` the point's height), where it is drawn. Its squares are from its own
/// height to a floor higher (steps), so the distances between are tried a quarter of a tile apart.
fn floor_over(world: &World, data: &GameData, cam: &Camera, obj: &GameObj, l: u8, across: f64, up: f64) -> bool {
    let Some(floor_h) = world.levels.as_ref().map(|lv| lv.height) else { return false };
    let h = obj.z + up;
    let (px, py) = (obj.x + cam.cos * across, obj.y + cam.sin * across);
    let square = |t: f64| ((px - cam.sin * t).floor() as i32, (py + cam.cos * t).floor() as i32);
    let low = l as f64 * floor_h - h;
    (0..=(floor_h * 4.0) as i32).map(|k| low + k as f64 * 0.25).filter(|&t| t > 0.0).any(|t| {
        let (x, y) = square(t);
        world.surface(l, x, y, data).is_some_and(|s| s > h && square(s - h) == (x, y))
    })
}

/// On stacked floors, the floor an object is drawn with: its own, or higher as long as the floor
/// above isn't drawn over any of it (`SPRITE_POINTS`). So it stays under a floor over it, but
/// on a stair's top steps it's drawn over the floor it's climbing to, not under its edge.
fn draw_level(world: &World, data: &GameData, cam: &Camera, obj: &GameObj) -> u8 {
    let top = world.level_count() as u8 - 1;
    let mut level = obj.level.min(top);
    while level < top && !floor_over_sprite(world, data, cam, obj, level + 1) {
        level += 1;
    }
    level
}

/// `see_through_from` for a frame's `Scene.roof`: none off stacked floors.
pub fn roof(world: &World, data: &GameData, cam: &Camera) -> Option<u8> {
    world.levels.as_ref()?;
    see_through_from(world, data, cam)
}

/// How much of a thing on stacked floors shows: faded on a floor that is see-through over us
/// (`Scene.roof`), so the player under it or inside a tower sees past it; never the player.
fn under_roof(scene: &Scene, obj: &GameObj) -> f32 {
    if Some(obj.id) == scene.world.me {
        return 1.0;
    }
    roof_alpha(scene.roof, obj.level)
}

/// How much of floor `level`, and what is on it, shows under see-through floors from `roof` up:
/// the lowest of them `ROOF_ALPHA`, those above it fainter still (`HIGH_ROOF_ALPHA`), so the
/// floors stacked over a tower's stair don't add up to a wall.
fn roof_alpha(roof: Option<u8>, level: u8) -> f32 {
    match roof {
        Some(r) if level == r => ROOF_ALPHA,
        Some(r) if level > r => HIGH_ROOF_ALPHA,
        _ => 1.0,
    }
}

const ROOF_ALPHA: f32 = 0.35;
const HIGH_ROOF_ALPHA: f32 = 0.18;
/// Ground that stays solid (`<StaySolid/>`: a stair's steps, a bridge's deck) does so within
/// this much of our height: the steps we climb to, not the stair's turn far over our head.
const NEAR_SOLID: f64 = 1.5;
/// How much of ourselves shows over the see-through floors drawn over us, so we're never lost
/// under them.
const GHOST_ALPHA: f32 = 0.55;

/// On stacked floors, the floors from which up are drawn see-through: those over us when one is
/// drawn over any of us, or every floor above the lowest when the player asked (`/floors`,
/// `World.see_through`), to see what is under them, ours too.
fn see_through_from(world: &World, data: &GameData, cam: &Camera) -> Option<u8> {
    if world.see_through {
        return Some(1);
    }
    let me = world.player()?;
    let over = (me.level + 1..world.level_count() as u8).find(|&l| floor_over_sprite(world, data, cam, me, l));
    // inside a tower (a floor's `<Column/>` ground over us, or beside us under an opening in
    // it, as its stairwell): its walls stand round us, so they and the floors from it up are
    // see-through, to see the stair inside
    let (x, y) = (me.x.floor() as i32, me.y.floor() as i32);
    let column = |l: u8, x: i32, y: i32| world.level_tile(l, x, y).and_then(|t| data.grounds.get(&t)).is_some_and(|g| g.xml.has("Column"));
    let tower = (me.level + 1..world.level_count() as u8)
        .find(|&l| column(l, x, y) || (world.level_tile(l, x, y).is_none() && (-1..=1).any(|dy| (-1..=1).any(|dx| column(l, x + dx, y + dy)))));
    match (over, tower) {
        (Some(a), Some(b)) => Some(a.min(b)),
        (a, b) => a.or(b),
    }
}

/// How thick a floor is where its edge hangs over the floor below (or over nothing): its face
/// goes this far down, not to the ground, so what is under it shows.
const SLAB: f64 = 0.35;
/// A railing (`<Rail>`, its colour): along an edge that drops at least this far (`World::railed`).
const RAIL_DROP: f64 = bsk_world::RAIL_DROP;
const RAIL_HEIGHT: f64 = 0.4;
/// The railing's bars, how thick (tiles), and its posts, how wide.
const RAIL_BAR: f64 = 0.07;
const RAIL_POST: f64 = 0.09;
/// How far a post's cap (`<RailCap>`) stands over its rail.
const RAIL_CAP: f64 = 0.12;
/// How bright the ground is under a floor above it.
const SHADE: f32 = 0.55;
/// Stacked floors' ground and edges are worked out this many squares square at a time.
const CHUNK: i32 = 16;

/// Stacked floors' ground, faces and railings, which only change with the tiles: worked out a
/// chunk of `CHUNK` × `CHUNK` squares of a floor at a time, the first time it comes into view, and
/// kept until its squares (or those round it) change, so the busiest map (the Nexus islands)
/// doesn't work out every edge every frame.
#[derive(Default)]
pub struct FloorCache {
    world_id: u64,
    layers_gen: u32,
    log_seen: usize,
    chunks: rustc_hash::FxHashMap<(u8, i32, i32), std::rc::Rc<Chunk>>,
}

/// A square of ground on a floor: where, how high, whether a floor above shades it and whether
/// it stays solid when the floors over us go see-through (`<StaySolid/>`). A ground with
/// `<Slope/>` (a bridge's deck) slopes from square to square instead of stepping: `corners` its
/// height at its north-west, north-east, south-east and south-west corners.
#[derive(Clone, Copy)]
struct Top {
    x: i32,
    y: i32,
    s: f64,
    shaded: bool,
    solid: bool,
    corners: Option<[f64; 4]>,
    /// Another ground drawn here instead of the square's own: the water under a little bridge
    /// (`<Under>`, the deck's ground naming it), level with its floor.
    under: Option<u16>,
}

/// Where ground drops: a square's side from its top (at `a`, and at `b` where a sloped deck
/// leans) down to `bottom` (as far below at `b`), its texture and light. A tower's doorway
/// (`<Doorway/>`) is dark at its foot.
#[derive(Clone, Copy)]
struct Edge {
    a: [f64; 2],
    b: [f64; 2],
    top: f64,
    top_b: f64,
    bottom: f64,
    rect: bsk_render::AtlasRect,
    shade: f32,
    solid: bool,
    door: bool,
}

/// A railing along a drop, from `a` to `b` (its top rail's height at each end: a bridge's rising
/// deck has it slope with the deck), `height` over the deck (`<RailHeight>`, else `RAIL_HEIGHT`),
/// its colour and its posts' caps' (`<RailCap>`: the arched bridges' brass); or a stone parapet
/// (`<Parapet/>`) in the ground's side texture, standing in from the drop (`n` the way out to
/// it), its ends where it stops (`ends`, at `a` and at `b`) closed.
#[derive(Clone, Copy)]
struct Rail {
    a: [f64; 3],
    b: [f64; 3],
    n: [f64; 2],
    height: f64,
    color: u32,
    cap: Option<u32>,
    solid: bool,
    parapet: Option<bsk_render::AtlasRect>,
    ends: [bool; 2],
}

/// A roof tier or a cornice along a tower's wall (`<FaceDown/>`, `<Column/>`) at a floor's height:
/// from the wall (a to b, `z` high) sloping down and out along `n`, in its `<Eave>` colour.
#[derive(Clone, Copy)]
struct Eave {
    a: [f64; 2],
    b: [f64; 2],
    n: [f64; 2],
    z: f64,
    color: u32,
    solid: bool,
}

/// A timber bracket under a balcony, from the tower's wall (`p`, low) out to the balcony's
/// underside (`q`), `t` the way along the wall.
#[derive(Clone, Copy)]
struct Brace {
    p: [f64; 3],
    q: [f64; 3],
    t: [f64; 2],
    solid: bool,
}

/// How far a tower's roof tiers reach out, and drop as they do; their tiles' colours.
const EAVE_OUT: f64 = 0.55;
const EAVE_DROP: f64 = 0.4;
const EAVE_TEAL: u32 = 0x2E5852;
/// How thick a stone parapet is (as the garden's stone bridges' walls), and how much paler its
/// stone is than the ground's side (theirs is pale granite, over a darker deck); a doorway's height.
const PARAPET_THICK: f64 = 0.34;
const PARAPET_PALE: f32 = 1.3;
const DOORWAY: f64 = 1.25;
/// A balcony's brackets: how far down the wall they start, how far out they reach, how wide.
const BRACE_DROP: f64 = 1.1;
const BRACE_OUT: f64 = 1.0;
const BRACE_WIDTH: f64 = 0.22;
const BRACE_COLOR: u32 = 0x4A2E22;

#[derive(Default)]
struct Chunk {
    tops: Vec<Top>,
    edges: Vec<Edge>,
    rails: Vec<Rail>,
    eaves: Vec<Eave>,
    braces: Vec<Brace>,
}

/// A square of a floor to draw: its ground (scrolling if it flows) and blended corners; a sloped
/// deck's corners on the screen.
struct TopDraw {
    base: Option<(bsk_render::AtlasRect, bsk_data::AnimateKind, f64, f64)>,
    overlay: Option<bsk_render::AtlasRect>,
    quad: Quad,
    corners: Option<[[f32; 2]; 4]>,
    tint: Tint,
    /// How far above its floor it is (a bridge's arch, a stair's step): 0 for the floor's own
    /// ground (and the water under a little bridge).
    raised: f64,
}

impl TopDraw {
    /// One of its pictures over the square: flat, or as two triangles over a sloped deck.
    fn draw(&self, renderer: &mut Renderer, rect: bsk_render::AtlasRect, scroll: Option<(f64, f64)>) {
        match (self.corners, scroll) {
            (Some(c), _) => {
                renderer.triangle(rect, [c[0], c[1], c[2]], [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0]], self.tint);
                renderer.triangle(rect, [c[0], c[2], c[3]], [[0.0, 0.0], [1.0, 1.0], [0.0, 1.0]], self.tint);
            }
            (None, Some((ox, oy))) => crate::ground::draw_scrolled(renderer, rect, self.quad, ox, oy, self.tint),
            (None, None) => renderer.sprite(rect, self.quad, self.tint),
        }
    }
}

impl FloorCache {
    /// Forget what changed: everything for another world or new floors, else the chunks round
    /// each square of the map sent since (on every floor: an edge above looks at the ground below).
    fn sync(&mut self, world: &World) {
        if self.world_id != world.id() || self.layers_gen != world.layers_gen() {
            self.world_id = world.id();
            self.layers_gen = world.layers_gen();
            self.chunks.clear();
            self.log_seen = world.tile_log().len();
            return;
        }
        let log = world.tile_log();
        if self.log_seen >= log.len() {
            self.log_seen = log.len();
            return;
        }
        let w = world.width.max(1);
        let mut dirty = rustc_hash::FxHashSet::default();
        for &i in &log[self.log_seen..] {
            let (x, y) = (i as i32 % w, i as i32 / w);
            for (dx, dy) in [(-1, -1), (1, -1), (-1, 1), (1, 1)] {
                dirty.insert(((x + dx).div_euclid(CHUNK), (y + dy).div_euclid(CHUNK)));
            }
        }
        self.chunks.retain(|(_, cx, cy), _| !dirty.contains(&(*cx, *cy)));
        self.log_seen = log.len();
    }

    fn chunk(&mut self, scene: &Scene, level: u8, cx: i32, cy: i32) -> std::rc::Rc<Chunk> {
        self.chunks.entry((level, cx, cy)).or_insert_with(|| std::rc::Rc::new(build_chunk(scene, level, cx, cy))).clone()
    }
}

/// Whether the ground on a floor at (x, y), `s` high, drops toward its side (nx, ny): nothing
/// beside it as high (within `by`), on any floor.
#[allow(clippy::too_many_arguments)]
fn drops(world: &World, data: &GameData, x: i32, y: i32, s: f64, nx: i32, ny: i32, by: f64) -> bool {
    world.drops(x, y, s, nx, ny, by, data)
}

/// Whether a square's ground (on whichever floor is within `reach` of height `s`) has `tag`, and
/// that ground's height.
fn ground_near(world: &World, data: &GameData, x: i32, y: i32, s: f64, reach: f64, tag: &str) -> Option<f64> {
    (0..world.level_count() as u8).find_map(|l| {
        let v = world.surface(l, x, y, data).filter(|v| (v - s).abs() <= reach)?;
        let t = world.level_tile(l, x, y)?;
        data.grounds.get(&t).filter(|g| tag.is_empty() || g.xml.has(tag)).map(|_| v)
    })
}

/// A sloped deck's corner (`<Slope/>`), from the four squares round it on the floor within a
/// step of this one's height: where the deck lands on its bank (ground that isn't a deck nor
/// water), the bank's height, so it lands flush; else the mean of those near the highest of them
/// (the deck's next squares, water left out where there's deck). Worked out from the four alone,
/// so every square sharing the corner gives it the same height and the deck has no cracks.
fn corner(world: &World, data: &GameData, cx: i32, cy: i32, s: f64) -> f64 {
    // (height, water, deck)
    let squares: Vec<(f64, bool, bool)> = [(cx - 1, cy - 1), (cx, cy - 1), (cx - 1, cy), (cx, cy)]
        .iter()
        .filter_map(|&(x, y)| {
            (0..world.level_count() as u8).find_map(|l| {
                let v = world.surface(l, x, y, data).filter(|v| (v - s).abs() <= bsk_world::STEP)?;
                let g = data.grounds.get(&world.level_tile(l, x, y)?)?;
                Some((v, g.speed < 1.0, g.xml.has("Slope")))
            })
        })
        .collect();
    let bank: Vec<f64> = squares.iter().filter(|&&(_, water, deck)| !water && !deck).map(|&(v, ..)| v).collect();
    if !bank.is_empty() && squares.iter().any(|&(.., deck)| deck) {
        return bank.iter().sum::<f64>() / bank.len() as f64;
    }
    let land = squares.iter().any(|&(_, water, _)| !water);
    let round: Vec<f64> = squares.into_iter().filter(|&(_, water, _)| !(land && water)).map(|(v, ..)| v).collect();
    let Some(top) = round.iter().copied().reduce(f64::max) else { return s };
    let near: Vec<f64> = round.into_iter().filter(|&v| top - v <= 0.3).collect();
    near.iter().sum::<f64>() / near.len() as f64
}

/// A chunk's ground, faces and railings (`FloorCache`).
fn build_chunk(scene: &Scene, level: u8, cx: i32, cy: i32) -> Chunk {
    let (world, data) = (scene.world, scene.data);
    let count = world.level_count() as u8;
    let floor_h = world.levels.as_ref().map_or(1.0, |l| l.height);
    let mut chunk = Chunk::default();
    let rail_of = |tile: u16| data.grounds.get(&tile).and_then(|g| g.xml.child("Rail")).map(|r| bsk_data::xml::as3_int(&r.text) as u32);
    for y in cy * CHUNK..(cy + 1) * CHUNK {
        for x in cx * CHUNK..(cx + 1) * CHUNK {
            let Some(s) = world.surface(level, x, y, data) else { continue };
            let Some(tile) = world.level_tile(level, x, y) else { continue };
            let ground = data.grounds.get(&tile);
            let has = |tag: &str| ground.is_some_and(|g| g.xml.has(tag));
            let solid = has("StaySolid");
            let face_down = has("FaceDown");
            let column = has("Column");
            let sloped = has("Slope");
            let eave_color = ground.and_then(|g| g.xml.child("Eave")).map_or(EAVE_TEAL, |e| bsk_data::xml::as3_int(&e.text) as u32);
            let shaded = (level + 1..count).any(|l| world.surface(l, x, y, data).is_some());
            let corners = sloped.then(|| [corner(world, data, x, y, s), corner(world, data, x + 1, y, s), corner(world, data, x + 1, y + 1, s), corner(world, data, x, y + 1, s)]);
            // a little bridge over a pond: the pond on under it (`<Under>`), so there's water, not
            // sky, below its arch
            let under = ground.and_then(|g| g.xml.child("Under")).and_then(|u| data.ground_ids.get(u.text.trim())).copied();
            if let Some(u) = under {
                chunk.tops.push(Top { x, y, s: level as f64 * floor_h, shaded, solid, corners: None, under: Some(u) });
            }
            chunk.tops.push(Top { x, y, s, shaded, solid, corners, under: None });
            let (fx, fy) = (x as f64, y as f64);
            // the ground under this square, on the floors below (a tower's walls go down to it)
            let below = (0..level).filter_map(|l| world.surface(l, x, y, data)).fold(None, |m: Option<f64>, v| Some(m.map_or(v, |m| m.max(v))));
            // the sides, in Wall.addWall order (north, east, south, west), going round the square
            // clockwise: each from a to b, its normal, the way along it, and its corners' indices
            let sides = [
                ([fx, fy], [fx + 1.0, fy], (0, -1), (1, 0), (0, 1)),
                ([fx + 1.0, fy], [fx + 1.0, fy + 1.0], (1, 0), (0, 1), (1, 2)),
                ([fx + 1.0, fy + 1.0], [fx, fy + 1.0], (0, 1), (-1, 0), (2, 3)),
                ([fx, fy + 1.0], [fx, fy], (-1, 0), (0, -1), (3, 0)),
            ];
            let rail = rail_of(tile);
            let rail_height = ground.map_or(RAIL_HEIGHT, |g| g.xml.number("RailHeight", RAIL_HEIGHT));
            let rail_cap = ground.and_then(|g| g.xml.child("RailCap")).map(|c| bsk_data::xml::as3_int(&c.text) as u32);
            // how far the ground must drop beside it for a railing (`<RailDrop>`: a little bridge
            // over a pond is railed all along)
            let rail_drop = ground.map_or(RAIL_DROP, |g| g.xml.number("RailDrop", RAIL_DROP));
            // how thick its edge is where it hangs over lower ground (`<Slab>`: a little bridge's
            // deck is thin)
            let slab = ground.map_or(SLAB, |g| g.xml.number("Slab", SLAB));
            // a railing goes where the ground drops (`World::railed`, which also stops us there)
            let railed = |nx: i32, ny: i32| world.railed(level, x, y, nx, ny, data);
            let parapet = has("Parapet").then(|| crate::ground::face(data, scene.sprites, tile, x, y).map(|f| f.0)).flatten();
            for (a, b, (nx, ny), (ax, ay), (ca, cb)) in sides {
                // the height of the deck at this side's ends: a sloped deck's corners, else level
                let (ha, hb) = corners.map_or((s, s), |c| (c[ca], c[cb]));
                // a railing where it drops: its top level with the ground here, or at either end
                // halfway to the next square's where the railing carries on along a rising deck
                if let Some(color) = rail.filter(|_| railed(nx, ny)) {
                    let end = |dir: i32| {
                        let (px, py) = (x + ax * dir, y + ay * dir);
                        let next = world.level_at(px, py, s, data).and_then(|l| {
                            let t = world.level_tile(l, px, py)?;
                            let h = world.surface(l, px, py, data)?;
                            (rail_of(t).is_some() && drops(world, data, px, py, h, nx, ny, rail_drop)).then_some(h)
                        });
                        next.map_or(s, |h| (s + h) / 2.0) + rail_height
                    };
                    let (ra, rb) = if corners.is_some() { (ha + rail_height, hb + rail_height) } else { (end(-1), end(1)) };
                    // a parapet's end is closed where the next square along has none on this side
                    let open = |dir: i32| {
                        let (px, py) = (x + ax * dir, y + ay * dir);
                        !world.level_at(px, py, s, data).is_some_and(|l| {
                            world.level_tile(l, px, py).and_then(|t| data.grounds.get(&t)).is_some_and(|g| g.xml.has("Parapet"))
                                && world.railed(l, px, py, nx, ny, data)
                        })
                    };
                    let ends = if parapet.is_some() { [open(-1), open(1)] } else { [false; 2] };
                    chunk.rails.push(Rail {
                        a: [a[0], a[1], ra],
                        b: [b[0], b[1], rb],
                        n: [nx as f64, ny as f64],
                        height: rail_height,
                        color,
                        cap: rail_cap,
                        solid,
                        parapet,
                        ends,
                    });
                }
                // a tower's walls (`<Column/>`): every side not on its own kind goes down to the
                // ground under it, from under the balcony beside it (or its own top's edge), with
                // a cornice at each floor it passes and a bracket under the balcony
                if column && !ground_near(world, data, x + nx, y + ny, s, 0.05, "Column").is_some() {
                    let deck = ground_near(world, data, x + nx, y + ny, s, 0.3, "").is_some();
                    let top = if deck { s - SLAB } else { s };
                    let bottom = below.unwrap_or(level as f64 * floor_h - SLAB);
                    let Some((rect, _)) = crate::ground::face(data, scene.sprites, tile, x, y) else { continue };
                    let shade = shade(normal([a[0], a[1], top], [b[0], b[1], top], [b[0], b[1], bottom]));
                    chunk.edges.push(Edge { a, b, top, top_b: top, bottom, rect, shade, solid, door: has("Doorway") });
                    let mut k = (bottom / floor_h + 0.2).ceil();
                    while k * floor_h < top - 0.2 {
                        chunk.eaves.push(Eave { a, b, n: [nx as f64, ny as f64], z: k * floor_h, color: eave_color, solid });
                        k += 1.0;
                    }
                    if deck {
                        let m = [(a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0];
                        let p = [m[0], m[1], top - BRACE_DROP];
                        let q = [m[0] + nx as f64 * BRACE_OUT, m[1] + ny as f64 * BRACE_OUT, top];
                        chunk.braces.push(Brace { p, q, t: [ax as f64, ay as f64], solid });
                    }
                    continue;
                }
                // ground as high beside it: no drop
                if !drops(world, data, x, y, s, nx, ny, 0.01) {
                    continue;
                }
                // a sloped deck's next square a little lower: the slope covers it, no step
                if sloped && ground_near(world, data, x + nx, y + ny, s, 0.3, "Slope").is_some() {
                    continue;
                }
                // a step's riser goes down to the floor it stands on; an edge over lower ground,
                // or over nothing, is a slab; a ground with `<FaceDown/>` (a tower's top) has its
                // sides go down to the ground under it, a wall
                let base = level as f64 * floor_h;
                let lower = (0..count).filter_map(|l| world.surface(l, x + nx, y + ny, data)).filter(|&v| v < s).fold(None, |m: Option<f64>, v| Some(m.map_or(v, |m| m.max(v))));
                let bottom = if face_down {
                    lower.unwrap_or(0.0).max(below.unwrap_or(0.0))
                } else {
                    match lower {
                        Some(l) if l >= base - 0.01 && !sloped => l,
                        Some(l) => (s - slab).max(l),
                        None => s - slab,
                    }
                };
                if s - bottom < 0.01 {
                    continue;
                }
                // its stone (or soil), lit as a wall's side; else the ground itself, darker
                let Some((rect, stone)) = crate::ground::face(data, scene.sprites, tile, x, y) else { continue };
                let shade = shade(normal([a[0], a[1], s], [b[0], b[1], s], [b[0], b[1], bottom])) * if stone { 1.0 } else { 0.8 };
                // a sloped deck's slab leans with it, as deep at both ends
                let (top, top_b, bottom) = if corners.is_some() { (ha, hb, ha - (s - bottom)) } else { (s, s, bottom) };
                chunk.edges.push(Edge { a, b, top, top_b, bottom, rect, shade, solid, door: false });
                // a tower's wall: a roof tier at each floor it passes, and under its top
                if face_down {
                    let mut k = (bottom / floor_h + 0.2).ceil();
                    while k * floor_h <= s + 0.01 {
                        let z = (k * floor_h).min(s - 0.12);
                        chunk.eaves.push(Eave { a, b, n: [nx as f64, ny as f64], z, color: eave_color, solid });
                        k += 1.0;
                    }
                }
            }
        }
    }
    chunk
}

/// On stacked floors: each floor's ground in view at its height (a stair's steps each at theirs,
/// a bridge's deck sloping), blended and flowing as the map's is, and what stands at its edges
/// where the ground drops beside it, each drawn with its floor at the depth it sorts at among what
/// stands there: the faces (a step's riser down to the floor it stands on, in pieces a tile tall;
/// a slab `SLAB` thick where the edge hangs over lower ground or nothing; a tower's walls down to
/// the ground), the railings of grounds with a `<Rail>`, a post at each corner, or a stone
/// parapet, a tower's cornices and its balcony's brackets. The floors over our head are
/// see-through (`see_through_from`), but for grounds that stay solid (`<StaySolid/>`).
#[allow(clippy::type_complexity, clippy::too_many_arguments)]
fn floors(scene: &Scene, renderer: &mut Renderer, ground: &mut Ground, x0: i32, x1: i32, y0: i32, y1: i32) -> (Vec<Vec<TopDraw>>, Vec<(u8, f32, Plate)>, Vec<(u8, f32, [[f32; 2]; 3], Tint)>) {
    let (world, data, cam) = (scene.world, scene.data, scene.camera);
    let count = world.level_count() as u8;
    let floor_h = world.levels.as_ref().map_or(1.0, |l| l.height);
    let roof = scene.roof;
    let me_z = world.player().map(|p| p.z);
    let white = renderer.white();
    let (sw, sh) = renderer.size();
    let margin = cam.scale * 1.5;
    let mut tops: Vec<Vec<TopDraw>> = (0..count).map(|_| Vec::new()).collect();
    let mut faces = Vec::new();
    let mut posts = rustc_hash::FxHashSet::default();
    let mut tris = Vec::new();
    // the floor a piece from `low` up is drawn with: the one it stands beside, so what is on that
    // floor in front of it stays in front
    let piece_of = |low: f64, level: u8| (((low + 0.01) / floor_h).floor().max(0.0) as u8).min(level);
    ground.floors.sync(world);
    let (cx0, cx1) = (x0.max(0).div_euclid(CHUNK), x1.min(world.width - 1).div_euclid(CHUNK));
    let (cy0, cy1) = (y0.max(0).div_euclid(CHUNK), y1.min(world.height - 1).div_euclid(CHUNK));
    for level in 0..count {
        let faded = roof_alpha(roof, level);
        // see-through over us, unless the ground stays solid (`<StaySolid/>`: stairs, bridges)
        // near our height (`NEAR_SOLID`); `/floors` shows through everything
        let keep = |solid: bool, h: f64| solid && me_z.is_none_or(|z| (h - z).abs() <= NEAR_SOLID);
        let alpha = |solid: bool| if !(solid && !world.see_through) { faded } else { 1.0 };
        let flat = |color: u32, k: f32, solid: bool| {
            let [r, g, b] = bsk_render::rgb_f32(color);
            Tint { mul: [r * k, g * k, b * k, alpha(solid)], add: [0.0; 3] }
        };
        for cy in cy0..=cy1 {
            for cx in cx0..=cx1 {
                let chunk = ground.floors.chunk(scene, level, cx, cy);
                for t in &chunk.tops {
                    let (fx, fy) = (t.x as f64, t.y as f64);
                    let mid = cam.to_screen(fx + 0.5, fy + 0.5, t.s);
                    if mid[0] < -margin as f32 || mid[1] < -margin as f32 || mid[0] > sw as f32 + margin as f32 || mid[1] > sh as f32 + margin as f32 {
                        continue;
                    }
                    let layers = match t.under {
                        // the water under a little bridge: its own texture, flowing as it does
                        Some(u) => {
                            let Some(g) = data.grounds.get(&u) else { continue };
                            let Some(rect) = crate::ground::plain(data, scene.sprites, u, t.x, t.y) else { continue };
                            crate::ground::TileLayers { base: Some((rect, g.animate.kind, g.animate.dx, g.animate.dy)), overlay: None, sink: 0 }
                        }
                        None => {
                            let Some(layers) = ground.layers_on(world, data, scene.sprites, renderer, level, t.x, t.y) else { continue };
                            layers
                        }
                    };
                    let lit = if t.shaded { SHADE } else { 1.0 };
                    let quad = cam.quad([fx, fy, t.s], [fx + 1.0, fy, t.s], [fx, fy + 1.0, t.s]);
                    let corners = t.corners.map(|c| {
                        [cam.to_screen(fx, fy, c[0]), cam.to_screen(fx + 1.0, fy, c[1]), cam.to_screen(fx + 1.0, fy + 1.0, c[2]), cam.to_screen(fx, fy + 1.0, c[3])]
                    });
                    let tint = Tint { mul: [lit, lit, lit, alpha(keep(t.solid, t.s))], add: [0.0; 3] };
                    let raised = t.s - level as f64 * floor_h;
                    tops[level as usize].push(TopDraw { base: layers.base, overlay: layers.overlay, quad, corners, tint, raised });
                }
                for e in &chunk.edges {
                    let (a, b) = (e.a, e.b);
                    // facing away
                    let q = cam.quad([a[0], a[1], e.top], [b[0], b[1], e.top], [a[0], a[1], e.top - 1.0]);
                    if q.x_axis[0] * q.y_axis[1] - q.x_axis[1] * q.y_axis[0] >= 0.0 {
                        continue;
                    }
                    let depth = cam.to_screen((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0, 0.0)[1];
                    let tint = Tint { mul: [e.shade, e.shade, e.shade, alpha(keep(e.solid, e.top))], add: [0.0; 3] };
                    if (e.top - e.top_b).abs() > 1e-6 {
                        // a sloped deck's slab: one piece, leaning with it
                        let quad = cam.quad([a[0], a[1], e.top], [b[0], b[1], e.top_b], [a[0], a[1], e.bottom]);
                        faces.push((piece_of(e.bottom, level), depth, Plate { rect: e.rect, quad, tint }));
                        continue;
                    }
                    let mut top = e.top;
                    while top - e.bottom > 0.01 {
                        let low = (top - 1.0).max(e.bottom);
                        let quad = cam.quad([a[0], a[1], top], [b[0], b[1], top], [a[0], a[1], low]);
                        faces.push((piece_of(low, level), depth, Plate { rect: e.rect, quad, tint }));
                        top = low;
                    }
                    // a doorway at a tower's foot: dark within, a lintel over it
                    if e.door {
                        let (ia, ib) = ([a[0] + (b[0] - a[0]) * 0.18, a[1] + (b[1] - a[1]) * 0.18], [a[0] + (b[0] - a[0]) * 0.82, a[1] + (b[1] - a[1]) * 0.82]);
                        let dark = cam.quad([ia[0], ia[1], e.bottom + DOORWAY], [ib[0], ib[1], e.bottom + DOORWAY], [ia[0], ia[1], e.bottom]);
                        faces.push((piece_of(e.bottom, level), depth + 0.01, Plate { rect: white, quad: dark, tint: flat(0x140E14, 1.0, keep(e.solid, e.top)) }));
                        let lintel = cam.quad([ia[0], ia[1], e.bottom + DOORWAY + 0.12], [ib[0], ib[1], e.bottom + DOORWAY + 0.12], [ia[0], ia[1], e.bottom + DOORWAY]);
                        faces.push((piece_of(e.bottom, level), depth + 0.01, Plate { rect: white, quad: lintel, tint: flat(BRACE_COLOR, 1.0, keep(e.solid, e.top)) }));
                    }
                }
                for e in &chunk.eaves {
                    let depth = cam.to_screen((e.a[0] + e.b[0]) / 2.0, (e.a[1] + e.b[1]) / 2.0, 0.0)[1] + 0.02;
                    let out = |p: [f64; 2]| [p[0] + e.n[0] * EAVE_OUT, p[1] + e.n[1] * EAVE_OUT];
                    // reaching out past the corners too, so neighbouring sides' tiers meet
                    let along = [e.b[0] - e.a[0], e.b[1] - e.a[1]];
                    let (oa, ob) = (out([e.a[0] - along[0] * EAVE_OUT, e.a[1] - along[1] * EAVE_OUT]), out([e.b[0] + along[0] * EAVE_OUT, e.b[1] + along[1] * EAVE_OUT]));
                    let lit = shade(normal([e.a[0], e.a[1], e.z], [e.b[0], e.b[1], e.z], [oa[0], oa[1], e.z - EAVE_DROP]));
                    let piece = piece_of(e.z - 0.21, level);
                    let tint = flat(e.color, lit, keep(e.solid, e.z));
                    // the slope as two triangles, its outer edge the longer
                    let p = |w: [f64; 3]| cam.to_screen(w[0], w[1], w[2]);
                    let (wa, wb) = (p([e.a[0], e.a[1], e.z + 0.12]), p([e.b[0], e.b[1], e.z + 0.12]));
                    let (xa, xb) = (p([oa[0], oa[1], e.z - EAVE_DROP]), p([ob[0], ob[1], e.z - EAVE_DROP]));
                    tris.push((piece, depth, [wa, wb, xb], tint));
                    tris.push((piece, depth, [wa, xb, xa], tint));
                    // the dark line of its edge
                    let rim = cam.quad([oa[0], oa[1], e.z - EAVE_DROP], [ob[0], ob[1], e.z - EAVE_DROP], [oa[0], oa[1], e.z - EAVE_DROP - 0.08]);
                    faces.push((piece, depth + 0.01, Plate { rect: white, quad: rim, tint: flat(e.color, lit * 0.5, keep(e.solid, e.z)) }));
                }
                for br in &chunk.braces {
                    let depth = cam.to_screen(br.q[0], br.q[1], 0.0)[1] + 0.03;
                    let h = [br.t[0] * BRACE_WIDTH / 2.0, br.t[1] * BRACE_WIDTH / 2.0];
                    let quad = cam.quad([br.p[0] - h[0], br.p[1] - h[1], br.p[2]], [br.p[0] + h[0], br.p[1] + h[1], br.p[2]], [br.q[0] - h[0], br.q[1] - h[1], br.q[2]]);
                    faces.push((piece_of(br.p[2], level), depth, Plate { rect: white, quad, tint: flat(BRACE_COLOR, 0.9, keep(br.solid, br.p[2])) }));
                }
                for r in &chunk.rails {
                    let depth = cam.to_screen((r.a[0] + r.b[0]) / 2.0, (r.a[1] + r.b[1]) / 2.0, 0.0)[1];
                    let tint = |k: f32| flat(r.color, k, keep(r.solid, r.a[2]));
                    // a stone parapet, as the garden's stone bridges' walls: `PARAPET_THICK` of
                    // the ground's side stone standing in from the drop, its faces that face us lit
                    // as walls, its ends closed where it stops, a lighter coping along its top
                    if let Some(rect) = r.parapet {
                        let h = r.height;
                        let at = |p: [f64; 3], inward: f64, down: f64| [p[0] - r.n[0] * inward, p[1] - r.n[1] * inward, p[2] - down];
                        let stone = |k: f32| Tint { mul: [k, k, k, alpha(keep(r.solid, r.a[2]))], add: [0.0; 3] };
                        // a wall's side from p to q (each at its top), down to the deck, if it faces us
                        let mut side = |p: [f64; 3], q: [f64; 3]| {
                            let quad = cam.quad(p, q, [p[0], p[1], p[2] - h]);
                            if quad.x_axis[0] * quad.y_axis[1] - quad.x_axis[1] * quad.y_axis[0] < 0.0 {
                                let lit = shade(normal(p, q, [q[0], q[1], q[2] - h]));
                                faces.push((level, depth, Plate { rect, quad, tint: stone(lit * PARAPET_PALE) }));
                            }
                        };
                        let t = PARAPET_THICK;
                        side(at(r.a, 0.0, 0.0), at(r.b, 0.0, 0.0));
                        side(at(r.b, t, 0.0), at(r.a, t, 0.0));
                        if r.ends[0] {
                            side(at(r.a, t, 0.0), at(r.a, 0.0, 0.0));
                        }
                        if r.ends[1] {
                            side(at(r.b, 0.0, 0.0), at(r.b, t, 0.0));
                        }
                        let coping = cam.quad(at(r.a, -0.03, 0.0), at(r.b, -0.03, 0.0), at(r.a, t + 0.03, 0.0));
                        faces.push((level, depth + 0.001, Plate { rect, quad: coping, tint: stone(PARAPET_PALE * 1.2) }));
                        continue;
                    }
                    // the top rail, and a thinner one halfway down
                    let bar = cam.quad(r.a, r.b, [r.a[0], r.a[1], r.a[2] - RAIL_BAR]);
                    faces.push((level, depth, Plate { rect: white, quad: bar, tint: tint(1.0) }));
                    let mid = |p: [f64; 3]| [p[0], p[1], p[2] - r.height * 0.5];
                    let (ma, mb) = (mid(r.a), mid(r.b));
                    let bar = cam.quad(ma, mb, [ma[0], ma[1], ma[2] - RAIL_BAR * 0.6]);
                    faces.push((level, depth, Plate { rect: white, quad: bar, tint: tint(0.8) }));
                    // a post at each corner, once, with its cap (`<RailCap>`) a little over the rail
                    for p in [r.a, r.b] {
                        if !posts.insert(((p[0] * 2.0) as i32, (p[1] * 2.0) as i32, level)) {
                            continue;
                        }
                        let top = cam.to_screen(p[0], p[1], p[2] + 0.02);
                        let foot = cam.to_screen(p[0], p[1], p[2] - r.height - 0.02);
                        let w = (RAIL_POST * cam.scale) as f32;
                        let quad = Quad { origin: [top[0] - w / 2.0, top[1]], x_axis: [w, 0.0], y_axis: [foot[0] - top[0], foot[1] - top[1]] };
                        let depth = cam.to_screen(p[0], p[1], 0.0)[1] + 0.01;
                        faces.push((level, depth, Plate { rect: white, quad, tint: tint(0.7) }));
                        if let Some(cap) = r.cap {
                            let crown = cam.to_screen(p[0], p[1], p[2] + RAIL_CAP);
                            let cw = w * 1.6;
                            let quad = Quad { origin: [crown[0] - cw / 2.0, crown[1]], x_axis: [cw, 0.0], y_axis: [top[0] - crown[0], top[1] - crown[1]] };
                            faces.push((level, depth + 0.001, Plate { rect: white, quad, tint: flat(cap, 1.0, keep(r.solid, r.a[2])) }));
                        }
                    }
                }
            }
        }
    }
    (tops, faces, tris)
}

#[allow(clippy::too_many_arguments)]
pub fn draw(scene: &Scene, renderer: &mut Renderer, redrawer: &mut Redrawer, ground: &mut Ground, fonts: &Fonts, text: &mut Text) {
    let (w, h) = renderer.size();
    let cam = scene.camera;
    let radius = cam.radius(w as f64, h as f64);
    let world = scene.world;
    let (x0, x1) = ((cam.x - radius).floor() as i32, (cam.x + radius).ceil() as i32);
    let (y0, y1) = ((cam.y - radius).floor() as i32, (cam.y + radius).ceil() as i32);

    // Stacked floors (World.levels): which floor each thing is drawn with, and how far its
    // height can carry it across the screen.
    let layered = world.levels.is_some();
    let floor_h = world.levels.as_ref().map_or(1.0, |l| l.height);
    let top_level = world.level_count() as u8 - 1;
    let level_of = |z: f64| ((z / floor_h).round().max(0.0) as u8).min(top_level);
    let raise = if layered { top_level as f64 * floor_h + 1.0 } else { 0.0 };
    // something a floor above is drawn over is out of sight (its bars, name and words too),
    // unless the floors over us are see-through (`see_through_from`)
    let see_through = layered && scene.roof.is_some();
    let hidden = |obj: &GameObj| layered && !see_through && Some(obj.id) != world.me && covered_by(world, scene.data, &cam, obj).is_some();


    // Objects and walls, back to front by the screen y of their ground point.
    let mut items: Vec<(f32, Item)> = Vec::new();
    for obj in world.objects.values() {
        let Some(desc) = scene.data.objects.get(&obj.object_type) else { continue };
        // a big flat object shows while any of it is on the screen; on stacked floors one on
        // another floor shows further off
        let reach = radius + desc.ground_size / 2.0 + raise;
        if (obj.x - cam.x).abs() > reach || (obj.y - cam.y).abs() > reach {
            continue;
        }
        let depth = cam.to_screen(obj.x, obj.y, 0.0)[1];
        let model = desc.model.as_ref().and_then(|m| scene.data.models.get(m));
        if let Some(model) = model {
            items.push((depth, Item::Model { obj, desc, model }));
        } else if matches!(desc.class, ObjectClass::ConnectedWall | ObjectClass::CaveWall) {
            items.push((depth, Item::Connected { obj, desc }));
        } else if desc.class.is_wall() {
            items.push((depth, Item::Wall { obj, desc }));
        } else {
            items.push((depth, Item::Sprite { obj, desc }));
        }
    }
    for p in &world.projectiles {
        let ally = !p.damages_players && Some(p.owner_id) != world.me;
        if ally && !scene.show.ally_shots {
            continue;
        }
        if world.pet_shot(p, scene.data) && ((ally && !scene.show.pet_shots) || scene.show.pet_shot_look == crate::options::PetShots::Hidden) {
            continue;
        }
        if (p.x - cam.x).abs() <= radius + raise && (p.y - cam.y).abs() <= radius + raise {
            items.push((cam.to_screen(p.x, p.y, 0.0)[1], Item::Projectile(p)));
        }
    }
    // the faces where the floors' ground drops, among what stands at their foot
    let (floor_tops, floor_faces, floor_tris) = if layered { floors(scene, renderer, ground, x0, x1, y0 - raise as i32, y1 + raise as i32) } else { Default::default() };
    for (level, depth, plate) in floor_faces {
        items.push((depth, Item::Face { level, plate }));
    }
    for (level, depth, pts, tint) in floor_tris {
        items.push((depth, Item::Tri { level, pts, tint }));
    }
    items.sort_by(|a, b| a.0.total_cmp(&b.0));
    crate::prof::mark("v.sort");
    let item_level = |item: &Item| match item {
        // what moves (players, monsters, bags) with the highest floor not drawn over it
        // (`draw_level`); walls and pillars with their own, so the floor above covers their tops
        Item::Sprite { obj, desc } if !desc.is_static => {
            if layered { draw_level(world, scene.data, &cam, obj) } else { 0 }
        }
        Item::Sprite { obj, .. } | Item::Wall { obj, .. } | Item::Connected { obj, .. } | Item::Model { obj, .. } => obj.level.min(top_level),
        Item::Projectile(p) => level_of(p.floor),
        Item::Face { level, .. } | Item::Tri { level, .. } => *level,
    };
    // Map.draw: DrawUnder objects first, then the shadows, then everything else.
    // ...and models that ask to be drawn under everything (`<DrawUnder>`: a bridge's deck, which
    // what crosses it is drawn on)
    let under = |item: &Item| match item {
        Item::Sprite { desc, .. } => desc.draw_under,
        Item::Model { desc, .. } => desc.draw_under,
        _ => false,
    };
    // A world of floating islands: the sky first (`sunset`), then what hangs into it under the
    // islands (`<SkyUnder/>` models: their rocky undersides, the bridges' piers), back to front,
    // before any floor: everything else stands in front of them.
    let sky_under = |item: &Item| matches!(item, Item::Model { desc, .. } if desc.sky_under);
    if world.background == crate::sunset::SKY_SUNSET {
        ground.sky.draw(renderer, &cam, scene.time);
        // rock hangs below its floor, so it shows from further up the screen
        let below = 14;
        ground.undersides.draw(world, scene.data, scene.sprites, renderer, &cam, x0, x1, y0 - raise as i32 - below, y1 + raise as i32);
    }
    for (_, item) in items.iter().filter(|(_, i)| sky_under(i)) {
        if let Item::Model { obj, desc, model } = item {
            draw_model(scene, renderer, obj, desc, model);
        }
    }
    crate::prof::mark("v.sky");
    // Where each sprite's top is drawn (x, y), for its condition icons.
    let mut tops: Vec<(&GameObj, [f32; 2])> = Vec::new();
    let k = (cam.scale / TILE_PX) as f32;
    // On stacked floors (World.levels) each floor in turn from the lowest: its ground at its
    // height, then what stands on it, with the faces where ground drops to it (a floor's open
    // edge, a stair's steps). A flat map is the one floor.
    for level in 0..=top_level {
        if layered {
            // the floor's ground, then its blended corners over it (Map.draw's two passes)
            // the floor's own ground, its blended corners over it, then what is raised above it (a
            // bridge's arch, a stair's steps) from the lowest up: a higher top is never behind a
            // lower one, so the water under a little bridge or the pond beside it isn't drawn
            // over its deck whichever way the camera turns
            let tops = &floor_tops[level as usize];
            let flat = |t: &&TopDraw| t.raised < 0.01;
            for t in tops.iter().filter(flat) {
                if let Some((rect, kind, dx, dy)) = t.base {
                    t.draw(renderer, rect, Some(crate::ground::scroll(kind, dx, dy, scene.time)));
                }
            }
            for t in tops.iter().filter(flat) {
                if let Some(rect) = t.overlay {
                    t.draw(renderer, rect, None);
                }
            }
            let mut raised: Vec<&TopDraw> = tops.iter().filter(|t| t.raised >= 0.01).collect();
            raised.sort_by(|a, b| a.raised.total_cmp(&b.raised));
            for t in raised {
                if let Some((rect, kind, dx, dy)) = t.base {
                    t.draw(renderer, rect, Some(crate::ground::scroll(kind, dx, dy, scene.time)));
                }
                if let Some(rect) = t.overlay {
                    t.draw(renderer, rect, None);
                }
            }
        } else {
        // Ground: all base faces, then the blended overlays (Map.draw's two passes).
        let mut overlays = Vec::new();
        let (o, ex, ey) = cam.affine();
        // Only squares on the screen: a tile's centre within a tile of the window's edges.
        let (sw, sh, margin) = (w as f64, h as f64, cam.scale);
        for ty in y0.max(0)..=y1.min(world.height - 1) {
            let y = ty as f64;
            // Along a row the tile centres move in a straight line: the columns on screen are
            // where both screen coordinates are in range.
            let (mut lo, mut hi) = (x0.max(0) as f64, x1.min(world.width - 1) as f64);
            for (base, step, max) in [(o[0] + (y + 0.5) * ey[0] + 0.5 * ex[0], ex[0], sw), (o[1] + (y + 0.5) * ey[1] + 0.5 * ex[1], ex[1], sh)] {
                if step.abs() < 1e-9 {
                    if base < -margin || base > max + margin {
                        hi = lo - 1.0;
                    }
                    continue;
                }
                let (a, b) = ((-margin - base) / step, (max + margin - base) / step);
                lo = lo.max(a.min(b).floor());
                hi = hi.min(a.max(b).ceil());
            }
            if hi < lo {
                continue;
            }
            for tx in lo as i32..=hi as i32 {
                let x = tx as f64;
                let (cx, cy) = (o[0] + (x + 0.5) * ex[0] + (y + 0.5) * ey[0], o[1] + (x + 0.5) * ex[1] + (y + 0.5) * ey[1]);
                if cx < -margin || cy < -margin || cx > sw + margin || cy > sh + margin {
                    continue;
                }
                let Some(layers) = ground.layers(world, scene.data, scene.sprites, renderer, tx, ty) else { continue };
                let origin = [(o[0] + x * ex[0] + y * ey[0]) as f32, (o[1] + x * ex[1] + y * ey[1]) as f32];
                let quad = Quad { origin, x_axis: [ex[0] as f32, ex[1] as f32], y_axis: [ey[0] as f32, ey[1] as f32] };
                if let Some((rect, kind, dx, dy)) = layers.base {
                    let (ox, oy) = crate::ground::scroll(kind, dx, dy, scene.time);
                    crate::ground::draw_scrolled(renderer, rect, quad, ox, oy, Tint::NONE);
                }
                if let Some(rect) = layers.overlay {
                    overlays.push((rect, quad));
                }
            }
        }
        for (rect, quad) in overlays {
            renderer.sprite(rect, quad, Tint::NONE);
        }
        crate::prof::mark("v.ground");
        }
        let here = |item: &Item| item_level(item) == level && !sky_under(item);
        for (_, item) in items.iter().filter(|(_, i)| under(i) && here(i)) {
            match item {
                Item::Sprite { obj, desc } => {
                    if let Some(top) = draw_sprite(scene, renderer, redrawer, ground, obj, desc) {
                        tops.push((obj, top));
                    }
                }
                Item::Model { obj, desc, model } => draw_model(scene, renderer, obj, desc, model),
                _ => {}
            }
        }
        if scene.show.shadows {
            for (_, item) in items.iter().filter(|(_, i)| here(i)) {
                if let Item::Model { obj, desc, model } = item {
                    let alpha = desc.xml.number("ModelShadow", 0.0) as f32;
                    if alpha > 0.0 {
                        draw_model_shadow(scene, renderer, obj, desc, model, alpha);
                    }
                }
            }
        }
        if let Some(shadow) = redrawer.shadow(renderer).filter(|_| scene.show.shadows) {
            for (_, item) in items.iter().filter(|(_, i)| !under(i) && here(i)) {
                let Item::Sprite { obj, desc } = item else { continue };
                if desc.shadow_size <= 0 {
                    continue;
                }
                // 30 × 15 px half-axes at size 100, under the object's ground point.
                let size = obj.stat_int(2).unwrap_or(desc.min_size) as f32;
                let s = size / 100.0 * (desc.shadow_size as f32 / 100.0);
                let (sw, sh) = (30.0 * s * k, 15.0 * s * k);
                let [px, py] = scene.stand(obj);
                let alpha = fade(scene, obj, desc);
                if alpha <= 0.0 {
                    continue;
                }
                renderer.sprite(shadow, Quad::rect(px - sw, py - sh, 2.0 * sw, 2.0 * sh), Tint::color(desc.shadow_color, alpha));
            }
        }
        for (_, item) in items.iter().filter(|(_, i)| !under(i) && here(i)) {
            match item {
                Item::Wall { obj, desc } => draw_wall(scene, renderer, obj, desc),
                Item::Connected { obj, desc } => draw_connected(scene, renderer, ground, obj, desc),
                Item::Model { obj, desc, model } => draw_model(scene, renderer, obj, desc, model),
                Item::Projectile(p) => draw_projectile(scene, renderer, redrawer, p),
                Item::Face { plate, .. } => renderer.sprite(plate.rect, plate.quad, plate.tint),
                Item::Tri { pts, tint, .. } => renderer.triangle(renderer.white(), *pts, [[0.5, 0.5]; 3], *tint),
                Item::Sprite { obj, desc } => {
                    if let Some(top) = draw_sprite(scene, renderer, redrawer, ground, obj, desc) {
                        tops.push((obj, top));
                    }
                }
            }
        }
    }
    // under see-through floors drawn over us, ourselves again over them, faint, so we're never
    // lost under them (inside a tower, under the bar)
    if let (Some(_), Some(me)) = (scene.roof, world.player()) {
        if let Some(desc) = scene.data.objects.get(&me.object_type) {
            if layered && covered_by(world, scene.data, &cam, me).is_some() {
                draw_sprite_faded(scene, renderer, redrawer, ground, me, desc, GHOST_ALPHA);
            }
        }
    }
    crate::prof::mark("v.objects");
    scene.particles.draw(&cam, renderer);
    crate::prof::mark("v.particles");
    // What follows (icons, bars, numbers, balloons, names) is read, not lit: it doesn't glow.
    renderer.glow_done();

    // Condition icons (`GameObject.drawConditionIcons`): in a row centred on the sprite's top,
    // each taking turns through its icons; none while paused.
    // Whole screen pixels per icon pixel, a little under the AIR client's x2, so the art stays
    // sharp.
    let icon_scale = ((1.5 * k).round() as u32).max(1);
    for (obj, [x, y]) in &tops {
        let c = obj.conditions();
        if c == [0, 0] || has(c, condition::PAUSED) || hidden(obj) {
            continue;
        }
        // The 16 x 16 icons (the stat boosts) at three quarters of the 8 x 8 ones' scale, so they're
        // only a little bigger; each icon takes its own width, with a gap, so none covers another.
        let big_scale = (icon_scale * 3 / 4).max(1);
        let icons: Vec<_> = condition::icons(c, scene.time)
            .into_iter()
            .filter_map(|(big, index)| {
                let src = scene.sprites.tile_src(if big { "lofiInterfaceBig" } else { "lofiInterface2" }, index)?;
                redrawer.icon(scene.sprites, renderer, src, if big { big_scale } else { icon_scale })
            })
            .collect();
        let gap = (2.0 * icon_scale as f32).max(3.0);
        let total = icons.iter().map(|i| i.w as f32).sum::<f32>() + gap * icons.len().saturating_sub(1) as f32;
        let mut left = x - total / 2.0;
        for icon in &icons {
            let (w, h) = (icon.w as f32, icon.h as f32);
            renderer.sprite(icon.rect, Quad::rect(left.round(), (y - h / 2.0).round(), w, h), Tint::NONE);
            left += w + gap;
        }
    }

    crate::prof::mark("v.icons");
    // HP bars (`GameObject.drawHpBar`) under monsters and players.
    for (_, item) in items.iter().filter(|_| scene.show.hp_bars) {
        let Item::Sprite { obj, desc } = item else { continue };
        if !(desc.enemy || desc.player) || desc.no_mini_map || fade(scene, obj, desc) <= 0.0 || hidden(obj) {
            continue;
        }
        let c = obj.conditions();
        if bsk_world::condition::has(c, bsk_world::condition::INVINCIBLE)
            || (!desc.player && bsk_world::condition::has(c, bsk_world::condition::INVULNERABLE))
        {
            continue;
        }
        let (Some(hp), Some(max)) = (obj.stat_int(1), obj.stat_int(0)) else { continue };
        let max = if desc.player { max } else { max.max(hp) };
        let offset = if desc.player && Some(obj.id) != world.me { 21.0 } else { 5.0 };
        let [px, py] = scene.stand(obj);
        let (half, tall, border) = (20.0 * k, 5.0 * k, 1.2 * k);
        let top = py + offset * k;
        renderer.fill(px - half - border, top - border, 2.0 * (half + border), tall + 2.0 * border, 0x111111, 1.0);
        if hp > 0 && max > 0 {
            let f = hp as f32 / max as f32;
            let color = if f < 0.2 { 0xE01010 } else if f < 0.5 { 0xFF8010 } else { 0x10FF00 };
            renderer.fill(px - half, top, 2.0 * half * f.min(1.0), tall, color, 1.0);
        }
    }

    crate::prof::mark("v.hp");
    // Damage numbers and notifications, rising above the object.
    let style = Style { size: (18.0 * k).round().max(10.0), bold: true, color: 0xFF0000 };
    for f in scene.floaters.iter().filter(|f| scene.show.damage_numbers || !f.is_damage()) {
        let Some(obj) = world.objects.get(&f.object_id).filter(|o| !hidden(o)) else { continue };
        let age = (scene.time - f.at) as f32 / 1000.0;
        let [px, py] = scene.stand(obj);
        let y = py - (55.0 + 40.0 * age) * k;
        text.draw_centered(fonts, renderer, &f.text, px, y, Style { color: f.color, ..style });
    }

    // Speech balloons: the text wrapped to 150 px, in a cut-corner box with a tail, above the
    // speaker.
    for b in scene.bubbles.iter().filter(|_| scene.show.bubbles) {
        let Some(obj) = world.objects.get(&b.object_id).filter(|o| !hidden(o)) else { continue };
        let size = (14.0 * k).round().max(9.0);
        let style = Style { size, bold: false, color: b.colors[2] };
        let max_w = 150.0 * k;
        let mut lines: Vec<String> = vec![String::new()];
        for word in b.text.split(' ') {
            let line = lines.last_mut().unwrap();
            let candidate = if line.is_empty() { word.to_owned() } else { format!("{line} {word}") };
            if !line.is_empty() && crate::emotes::measure(scene.sprites, fonts, text, renderer, &candidate, style) > max_w {
                lines.push(word.to_owned());
            } else {
                *line = candidate;
            }
        }
        let line_h = size * 1.2;
        let w = lines.iter().map(|l| crate::emotes::measure(scene.sprites, fonts, text, renderer, l, style)).fold(0.0, f32::max);
        let h = line_h * lines.len() as f32;
        let [px, py] = scene.stand(obj);
        let size_pct = obj.stat_int(2).unwrap_or(100) as f32 / 100.0;
        let bottom = py - 40.0 * size_pct * k - 2.0 * k - 18.0 * k;
        let (bx, by) = (px - w / 2.0 - 6.0 * k, bottom - h - 12.0 * k);
        let rect = [bx, by, w + 12.0 * k, h + 12.0 * k];
        crate::hud::cut_rect(renderer, [rect[0] - 2.0 * k, rect[1] - 2.0 * k, rect[2] + 4.0 * k, rect[3] + 4.0 * k], 5.0 * k, [true; 4], 0x000000, 0.35);
        crate::hud::cut_rect(renderer, rect, 4.0 * k, [true; 4], b.colors[1], 1.0);
        crate::hud::cut_rect(renderer, [rect[0] + k, rect[1] + k, rect[2] - 2.0 * k, rect[3] - 2.0 * k], 3.0 * k, [true; 4], b.colors[0], 1.0);
        // The tail, pointing down at the speaker.
        let white = renderer.white();
        let tail_top = by + h + 12.0 * k - k;
        renderer.triangle(white, [[px - 8.0 * k, tail_top], [px + 8.0 * k, tail_top], [px, tail_top + 12.0 * k]], [[0.5, 0.5]; 3], Tint::color(b.colors[1], 1.0));
        renderer.triangle(white, [[px - 6.0 * k, tail_top - k], [px + 6.0 * k, tail_top - k], [px, tail_top + 9.0 * k]], [[0.5, 0.5]; 3], Tint::color(b.colors[0], 1.0));
        for (i, line) in lines.iter().enumerate() {
            crate::emotes::draw(scene.sprites, fonts, text, renderer, line, bx + 6.0 * k, by + 6.0 * k + line_h * i as f32, style, scene.time);
        }
    }

    // Names (`GameObject.drawName`): bold 16 px, centred under the object. Other players' are
    // coloured for party, guild or a chosen name, with their rank star before.
    let me = world.player();
    let my_guild = me.and_then(|m| m.stat_str(62)).filter(|g| !g.is_empty());
    let my_party = me.and_then(|m| m.stat_int(121)).filter(|&p| p != 0);
    let mut classes = None;
    for (_, item) in &items {
        let Item::Sprite { obj, desc } = item else { continue };
        let player = desc.player && Some(obj.id) != world.me;
        // Portal.draw: the Nexus's realm portals show their name and player count.
        let realm_portal = desc.class == ObjectClass::Portal && desc.xml.has("NexusPortal");
        if !(desc.show_name || player || realm_portal) || fade(scene, obj, desc) <= 0.0 || hidden(obj) {
            continue;
        }
        let Some(name) = obj.name() else { continue };
        let color = if !player {
            0xFFFFFF
        } else if my_party.is_some() && obj.stat_int(121) == my_party {
            0xFFC0CB
        } else if my_guild.is_some() && obj.stat_str(62) == my_guild {
            0xA6FF5D
        } else if obj.stat_int(56).unwrap_or(0) != 0 {
            0xFCDF00
        } else {
            0xFFFFFF
        };
        let style = Style { size: (16.0 * k).round().max(9.0), bold: true, color };
        let [px, mut py] = scene.stand(obj);
        if desc.ground_size > 1.0 {
            // under a portal sunk into the floor, not across its middle
            py += (desc.ground_size / 2.0 * cam.scale) as f32;
        }
        if !player {
            text.draw_centered(fonts, renderer, name, px, py, style);
            continue;
        }
        // The rank star sits in the first 20 px; the text after it.
        let w = text.measure(fonts, renderer, name, style);
        let left = px - (w + 20.0 * k) / 2.0;
        let classes = *classes.get_or_insert_with(|| crate::stars::class_count(scene.data));
        crate::stars::icon(renderer, left + 10.0 * k, py + 12.0 * k, k, crate::stars::color(obj.stat_int(30).unwrap_or(0), classes));
        text.draw_centered(fonts, renderer, name, left + 20.0 * k + w / 2.0, py, style);
    }
    crate::prof::mark("v.text");
    let _ = PAD;
}

/// `GameObject.draw` for sprite objects. Returns where the sprite's top centre is drawn.
/// How opaque the options draw this object: other players and other players' pets can be
/// faded (1 for everything else).
fn fade(scene: &Scene, obj: &GameObj, desc: &ObjectDesc) -> f32 {
    let own = if desc.player && Some(obj.id) != scene.world.me {
        scene.show.player_alpha
    } else if scene.world.others_pet(obj) {
        scene.show.pet_alpha
    } else if Some(obj.id) == scene.world.me {
        scene.me_alpha
    } else {
        1.0
    };
    own * under_roof(scene, obj)
}

fn draw_sprite(scene: &Scene, renderer: &mut Renderer, redrawer: &mut Redrawer, ground: &mut Ground, obj: &GameObj, desc: &ObjectDesc) -> Option<[f32; 2]> {
    draw_sprite_faded(scene, renderer, redrawer, ground, obj, desc, 1.0)
}

/// `draw_sprite`, `faded` as much again.
fn draw_sprite_faded(scene: &Scene, renderer: &mut Renderer, redrawer: &mut Redrawer, ground: &mut Ground, obj: &GameObj, desc: &ObjectDesc, faded: f32) -> Option<[f32; 2]> {
    let cam = scene.camera;
    let alpha = fade(scene, obj, desc) * faded;
    if alpha <= 0.0 {
        return None;
    }
    let mut look = scene.look(obj, desc)?;
    if desc.draw_on_ground {
        // Flat on its square, like a tile; or, `GroundSize` tiles across, centred on it. On
        // stacked floors, on its floor.
        let rect = scene.sprites.atlas(look.src)?;
        let z = if scene.world.levels.is_some() { obj.z } else { 0.0 };
        // turned on the ground about its middle by its `<Rotation>` (the islands' realm caverns
        // north of the shrine's middle, their steps away from it)
        let (h, (cx, cy)) = if desc.ground_size > 1.0 { (desc.ground_size / 2.0, (obj.x, obj.y)) } else { (0.5, (obj.x.floor() + 0.5, obj.y.floor() + 0.5)) };
        let (s, c) = desc.rotation.to_radians().sin_cos();
        let at = |dx: f64, dy: f64| [cx + dx * c - dy * s, cy + dx * s + dy * c, z];
        let quad = cam.quad(at(-h, -h), at(h, -h), at(-h, h));
        renderer.sprite(rect, quad, Tint::NONE);
        return None;
    }
    let mut size = obj.stat_int(2).unwrap_or(desc.min_size);
    // Hallucinating: a random character instead (getHallucinatingTexture), one per object.
    if scene.hallucinating {
        let pick = ((obj.id as u32).wrapping_mul(2_654_435_761) >> 8) % 239;
        if let Some(src) = scene.sprites.tile_src("lofiChar8x8", pick) {
            size = (size as f32 * (look.src.w as f32 / src.w as f32).min(1.5)) as i32;
            look = Look { src, mirror: false, mask: None, wide: false, tex: None };
        }
    }
    let (tex1, tex2) = look.tex.unwrap_or((obj.stat_int(32).unwrap_or(0), obj.stat_int(33).unwrap_or(0)));
    let dye = look.mask.map(|mask| Dye { mask, tex1, tex2 });
    // A player's GLOW_COLOR (Player.getTexture's outlineGlow): red for a legend, or their /glow.
    // An enemy's GLOW_ENEMY_COLOR (GameObject.getTexture): green, purple or pink for a rare,
    // epic or legendary one.
    let glow_stat = if desc.player { Some(59) } else if desc.enemy { Some(115) } else { None };
    let glow = glow_stat.and_then(|s| obj.stat_int(s)).filter(|&g| g > 0 && g <= 0xFF_FFFF).unwrap_or(0) as u32;
    let sprite = redrawer.get_glowing(scene.sprites, renderer, look.src, look.mirror, size, dye, glow)?;
    // The canvas's bottom centre stands on the object (raised by its z); sprites scale with the
    // map. On sinking ground (water) the sprite is drawn lower with its bottom cut off, unless it
    // flies.
    let k = (cam.scale / TILE_PX) as f32;
    let lift = scene.height(obj);
    let [px, py] = cam.to_screen(obj.x, obj.y, desc.z + lift);
    let mut sink = 0.0;
    // on stacked floors, how far above its floor's ground it stands (a bridge's deck): standing
    // on it, it sinks into water as on a flat map
    let floor_h = scene.world.levels.as_ref().map_or(0.0, |l| l.height);
    let level = if floor_h > 0.0 { obj.level } else { 0 };
    if (lift - level as f64 * floor_h).abs() < 1e-6 && !desc.flying && !desc.protect_from_sink() {
        if let Some(l) = ground.layers_on(scene.world, scene.data, scene.sprites, renderer, level, obj.x as i32, obj.y as i32) {
            sink = (l.sink as f32).min(sprite.h as f32);
        }
    }
    let (sw, sh) = (sprite.w as f32 * k, sprite.h as f32 * k);
    // A fallen boss's remains (`<Corpse/>`): lying on its side on the ground, grey, the souls'
    // glow pulsing faintly through it.
    if desc.corpse {
        let pulse = ((scene.time as f32 / 650.0).sin() * 0.5 + 0.5) * 0.14;
        let tint = Tint { mul: [0.8, 0.8, 0.8, 0.95 * alpha], add: [pulse * 0.25, pulse, pulse * 0.7] };
        let (cx, cy) = (px.round(), (py - sw * 0.42).round());
        let quad = Quad::rotated(cx, cy, sw, sh, -std::f32::consts::FRAC_PI_2);
        renderer.sprite_filtered(sprite.rect, [0.0, 0.0, 1.0, 1.0], quad, tint, Filter::Grey);
        return Some([px.round(), cy - sw / 2.0]);
    }
    let cut = 1.0 - sink / sprite.h as f32;
    // A wide attack frame: its body (the first half, or the second when mirrored) is centred.
    let shift = if look.wide {
        let body = (sprite.w - 2 * PAD) as f32 / 2.0 * k;
        if look.mirror { -body / 2.0 } else { body / 2.0 }
    } else {
        0.0
    };
    // On a whole pixel, so the art keeps its shape as the object moves. The point it stands on
    // is rounded apart from the sprite's size: it then lands where the ground's pixels do, and we
    // (at the camera point, a whole pixel) stay put instead of wobbling a pixel to and fro as
    // the snapped camera's leftover fraction rounds the sum one way or the other.
    let quad = Quad::rect(px.round() + (shift - sw / 2.0).round(), py.round() + (sink * k - sh).round(), sw, sh * cut);
    let mut tint = scene.particles.flash_tint(obj.id, scene.time).unwrap_or(Tint::NONE);
    // Conditions (Player.getTexture, GameObject.getTexture): grey in stasis (and, for players,
    // paused or petrified), red when cursed, faint when invisible; a player low on health
    // pulses red.
    let c = obj.conditions();
    let mut filter = Filter::None;
    if desc.player {
        if let (Some(hp), Some(max)) = (obj.stat_int(1), obj.stat_int(0)) {
            if (hp as f32) < max as f32 * 0.2 {
                let rv = ((scene.time as f32 / 200.0).sin().abs() * 10.0).floor() / 10.0;
                let o = rv * 128.0 / 255.0;
                tint.add = [tint.add[0] + o, tint.add[1] - o, tint.add[2] - o];
            }
        }
        if has(c, condition::CURSE) {
            filter = Filter::Red;
        }
        if has(c, condition::PAUSED) || has(c, condition::STASIS) || has(c, condition::PETRIFIED) {
            filter = Filter::Grey;
        } else if has(c, condition::INVISIBLE) {
            tint.mul[3] *= 0.4;
        }
    } else if has(c, condition::STASIS) {
        filter = Filter::Grey;
    }
    tint.mul[3] *= alpha;
    renderer.sprite_filtered(sprite.rect, [0.0, 0.0, 1.0, cut], quad, tint, filter);
    // Centred on where it stands, not on the canvas: a wide attack frame's canvas is shifted, and
    // the condition icons above would jump to and fro while it shoots.
    Some([px.round(), quad.origin[1]])
}

/// `Projectile.draw` with projOutline (the AIR client's default): the outlined texture centred
/// on the projectile (half a tile up), turned the way it's moving (plus the object's
/// AngleCorrection, and its spin if it has a Rotation).
fn draw_projectile(scene: &Scene, renderer: &mut Renderer, redrawer: &mut Redrawer, p: &Projectile) {
    let cam = scene.camera;
    let Some(desc) = scene.world.projectile_desc(p, scene.data) else { return };
    let look = p.look.or_else(|| scene.data.object_ids.get(&desc.object_id).copied());
    let Some(look_desc) = look.and_then(|t| scene.data.objects.get(&t)) else {
        return;
    };
    let Some(src) = scene.texture_src(&look_desc.texture, p.bullet_id) else { return };
    let mut size = if desc.size > 0 {
        desc.size
    } else {
        scene.data.objects.get(&p.container_type).map_or(100, |c| c.xml.int("Size", 100))
    };
    if src.w >= 16 {
        size = (size as f64 / (src.w as f64 / 8.0)) as i32;
    }
    // Pets' shots (a boss pet's are its boss's) smaller and see-through, as the options say:
    // never as solid as a monster's, so they don't hide the fight or pass for a danger.
    let mut tint = Tint::NONE;
    if scene.world.pet_shot(p, scene.data) {
        let (scale, alpha) = scene.show.pet_shot_look.look(Some(p.owner_id) == scene.world.me);
        size = ((size as f32 * scale).round() as i32).max(1);
        tint.mul[3] = alpha;
    }
    let Some(sprite) = redrawer.get_padded(scene.sprites, renderer, src, false, size, None, true) else { return };
    let spin = if look_desc.rotation == 0.0 { 0.0 } else { scene.time as f64 / look_desc.rotation };
    let angle = p.heading - cam.angle + look_desc.angle_correction + spin;
    let k = (cam.scale / TILE_PX) as f32;
    let [px, py] = cam.to_screen(p.x, p.y, p.z + p.floor);
    let quad = Quad::rotated(px, py, sprite.w as f32 * k, sprite.h as f32 * k, angle as f32);
    renderer.sprite(sprite.rect, quad, tint);
}

/// `Object3D.draw`: the model turned by the object's Rotation (degrees) and placed at it; faces
/// in model order, those facing away skipped, each lit like a wall face. Textured faces show
/// the object's texture; "Solid" ones its colour.
fn draw_model(scene: &Scene, renderer: &mut Renderer, obj: &GameObj, desc: &ObjectDesc, model: &Model) {
    let cam = scene.camera;
    // on stacked floors, see-through with its floor when that floor is over us (the bar, the
    // tower's cap)
    let alpha = under_roof(scene, obj);
    let texture = scene.texture_src(&desc.texture, obj.id).and_then(|s| scene.sprites.atlas(s));
    let contrast = desc.xml.number("ModelContrast", 0.25);
    let (s, c) = desc.rotation.to_radians().sin_cos();
    // on stacked floors, up on its floor
    let base = if scene.world.levels.is_some() { obj.z } else { 0.0 };
    let world: Vec<[f64; 3]> = model
        .vertices
        .iter()
        .map(|(p, _)| [p[0] * c - p[1] * s + obj.x, p[0] * s + p[1] * c + obj.y, p[2] + base])
        .collect();
    let screen: Vec<[f32; 2]> = world.iter().map(|p| cam.to_screen(p[0], p[1], p[2])).collect();
    for face in &model.faces {
        let ix = &face.indices;
        let (i0, i1, il) = (ix[0], ix[1], ix[ix.len() - 1]);
        let (u, v) = (sub(screen[i1], screen[i0]), sub(screen[il], screen[i0]));
        if u[0] * v[1] - u[1] * v[0] < 0.0 {
            continue;
        }
        let shade = if face.glow { 1.0 } else { shade_with(normal(world[i0], world[i1], world[il]), contrast) };
        let (src, tint) = match (face.textured, texture) {
            (true, Some(t)) => (t, Tint { mul: [shade, shade, shade, alpha], add: [0.0; 3] }),
            _ => {
                let [r, g, b] = bsk_render::rgb_f32(desc.color.unwrap_or(0));
                (renderer.white(), Tint { mul: [r * shade, g * shade, b * shade, alpha], add: [0.0; 3] })
            }
        };
        let uv = |i: usize| {
            let t = model.vertices[i].1;
            [t[0] as f32, t[1] as f32]
        };
        for k in 1..ix.len() - 1 {
            let (a, b) = (ix[k], ix[k + 1]);
            renderer.triangle(src, [screen[i0], screen[a], screen[b]], [uv(i0), uv(a), uv(b)], tint);
        }
    }
}

/// `ConnectedObject.draw`: the faces for the shape the CONNECT stat gives, turned to fit. Faces
/// that look at unknown ground are black.
fn draw_connected(scene: &Scene, renderer: &mut Renderer, ground: &mut Ground, obj: &GameObj, desc: &ObjectDesc) {
    let Some(connect) = obj.stat_int(41) else { return };
    let cached = ground.walls.get(&obj.id).filter(|c| c.0 == connect && c.1 == obj.object_type).map(|c| c.2.clone());
    let look = match cached {
        Some(look) => look,
        None => {
            let Some(look) = connected_look(scene, obj, desc, connect) else { return };
            let look = std::rc::Rc::new(look);
            ground.walls.insert(obj.id, (connect, obj.object_type, look.clone()));
            look
        }
    };
    let cam = scene.camera;
    let crate::ground::WallLook { faces, rotation, side, top } = &*look;
    let (rotation, side, top) = (*rotation, *side, *top);
    connected_faces(scene, renderer, obj, faces, rotation, side, top, &cam);
}

/// A connected wall's faces for its shape, and its textures (all fixed while its CONNECT stat
/// is).
fn connected_look(scene: &Scene, obj: &GameObj, desc: &ObjectDesc, connect: i32) -> Option<crate::ground::WallLook> {
    let (shape, rotation) = connected::shape(connect)?;
    let side = scene.texture_src(&desc.texture, obj.id).and_then(|s| scene.sprites.atlas(s));
    let faces = if desc.class == ObjectClass::CaveWall {
        // Math.random in the AIR client; seeded by the object here so it holds still.
        let mut seed = (obj.id as u32).wrapping_mul(0x9E37_79B9) | 1;
        let mut random = || {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            (seed >> 8) as f64 / (1u32 << 24) as f64
        };
        connected::cave_wall(shape, rotation, obj.x as i32, obj.y as i32, &mut random)
    } else {
        connected::connected_wall(shape, connected::Indents::from_xml(&desc.xml))
    };
    let top = desc
        .xml
        .child(shape.top_texture_tag())
        .and_then(|t| {
            let index = t.child("Index").map_or(0, |i| bsk_data::xml::as3_int(&i.text));
            scene.sprites.tile_src(t.text_of("File")?, index as u32)
        })
        .and_then(|s| scene.sprites.atlas(s))
        .or(side);
    Some(crate::ground::WallLook { faces, rotation, side, top })
}

#[allow(clippy::too_many_arguments)]
fn connected_faces(
    scene: &Scene,
    renderer: &mut Renderer,
    obj: &GameObj,
    faces: &[connected::Face],
    rotation: i32,
    side: Option<bsk_render::AtlasRect>,
    top: Option<bsk_render::AtlasRect>,
    cam: &Camera,
) {
    let (s, c) = (rotation as f64 * 90.0).to_radians().sin_cos();
    let turn = |p: connected::V3| [p[0] * c - p[1] * s, p[0] * s + p[1] * c, p[2]];
    let to_world = |p: connected::V3| {
        let t = turn(p);
        [t[0] + obj.x, t[1] + obj.y, t[2]]
    };
    for face in faces {
        let basis = face.basis.map(to_world);
        let bs = basis.map(|p| cam.to_screen(p[0], p[1], p[2]));
        let (u, v) = (sub(bs[1], bs[0]), sub(bs[2], bs[0]));
        let det = u[0] * v[1] - u[1] * v[0];
        if det <= 0.0 {
            continue;
        }
        let n = normal(basis[0], basis[1], basis[2]);
        let look = face.normal.map(turn).unwrap_or(n);
        let len = look[0].hypot(look[1]).hypot(look[2]);
        let step = |d: f64| if d > 0.4 * len { 1.0 } else if d < -0.4 * len { -1.0 } else { 0.0 };
        let (nx, ny) = (step(look[0]), step(look[1]));
        let unknown = (nx != 0.0 || ny != 0.0)
            && scene.world.tile((obj.x + nx).floor() as i32, (obj.y + ny).floor() as i32).is_none();
        let shade = shade(n);
        let (src, tint) = match (unknown, if face.top { top } else { side }) {
            (false, Some(t)) => (t, Tint { mul: [shade, shade, shade, 1.0], add: [0.0; 3] }),
            _ => (renderer.white(), Tint::color(0x000000, 1.0)),
        };
        let screen: Vec<[f32; 2]> = face
            .verts
            .iter()
            .map(|&p| {
                let w = to_world(p);
                cam.to_screen(w[0], w[1], w[2])
            })
            .collect();
        let uv_at = |i: usize| -> [f32; 2] {
            match face.basis_uv {
                None => [face.uvs[i][0] as f32, face.uvs[i][1] as f32],
                // Software drawing: the texture follows the basis corners on the screen.
                Some(b) => {
                    let d = sub(screen[i], bs[0]);
                    let (p, q) = ((d[0] * v[1] - d[1] * v[0]) / det, (u[0] * d[1] - u[1] * d[0]) / det);
                    let (p, q) = (p as f64, q as f64);
                    let at = |k: usize| b[0][k] + p * (b[1][k] - b[0][k]) + q * (b[2][k] - b[0][k]);
                    [at(0) as f32, at(1) as f32]
                }
            }
        };
        for t in &face.tris {
            renderer.triangle_wrapped(src, t.map(|i| screen[i]), t.map(uv_at), tint);
        }
    }
}

fn sub(a: [f32; 2], b: [f32; 2]) -> [f32; 2] {
    [a[0] - b[0], a[1] - b[1]]
}

/// `Wall.draw`: a textured cube. Sides that face away aren't drawn; sides against another wall
/// or unknown ground are black.
fn draw_wall(scene: &Scene, renderer: &mut Renderer, obj: &GameObj, desc: &ObjectDesc) {
    let cam = scene.camera;
    let (xi, yi) = (obj.x.floor(), obj.y.floor());
    // on stacked floors, up on its floor
    let z = if scene.world.levels.is_some() { obj.z } else { 0.0 };
    let side = scene.texture_src(&desc.texture, obj.id).and_then(|s| scene.sprites.atlas(s));
    let top = desc
        .top
        .as_ref()
        .and_then(|t| scene.texture_src(t, obj.id))
        .and_then(|s| scene.sprites.atlas(s))
        .or(side);
    let Some(side) = side else { return };
    // `<WallHeight>`: a pillar, as tall as a floor, in pieces a tile tall, its sides all stone
    let tall = desc.xml.number("WallHeight", 1.0);

    // Sides, in Wall.addWall order: north, east, south, west; neighbours at sqX/sqY.
    let corners = [
        ([xi, yi], [xi + 1.0, yi], (0, -1)),
        ([xi + 1.0, yi], [xi + 1.0, yi + 1.0], (1, 0)),
        ([xi + 1.0, yi + 1.0], [xi, yi + 1.0], (0, 1)),
        ([xi, yi + 1.0], [xi, yi], (-1, 0)),
    ];
    for (a, b, (nx, ny)) in corners {
        let v0 = [a[0], a[1], z + 1.0];
        let v1 = [b[0], b[1], z + 1.0];
        let v3 = [a[0], a[1], z];
        let quad = cam.quad(v0, v1, v3);
        // Back faces: the projected corners wind the other way.
        let cross = quad.x_axis[0] * quad.y_axis[1] - quad.x_axis[1] * quad.y_axis[0];
        if cross >= 0.0 {
            continue;
        }
        if tall != 1.0 {
            let s = shade(normal(v0, v1, [b[0], b[1], z]));
            let mut low = z;
            while low < z + tall - 0.01 {
                let high = (low + 1.0).min(z + tall);
                let quad = cam.quad([a[0], a[1], high], [b[0], b[1], high], [a[0], a[1], low]);
                renderer.sprite(side, quad, Tint { mul: [s, s, s, 1.0], add: [0.0; 3] });
                low = high;
            }
            continue;
        }
        let (sx, sy) = (xi as i32 + nx, yi as i32 + ny);
        let neighbour_wall = scene
            .world
            .occupant_at(obj.level, sx, sy)
            .and_then(|o| scene.data.objects.get(&o.object_type))
            .is_some_and(|d| d.class.is_wall());
        // black against another wall, or against ground not yet seen; on stacked floors a side
        // with nothing beyond faces open air, and shows its stone
        let unknown = scene.world.levels.is_none() && scene.world.level_tile(obj.level, sx, sy).is_none();
        if unknown || neighbour_wall {
            renderer.sprite(renderer.white(), quad, Tint::color(0x000000, 1.0));
            continue;
        }
        let s = shade(normal(v0, v1, [b[0], b[1], z]));
        renderer.sprite(side, quad, Tint { mul: [s, s, s, 1.0], add: [0.0; 3] });
    }
    if let Some(top) = top {
        let z = z + tall;
        let v0 = [xi, yi, z];
        let s = shade(normal(v0, [xi + 1.0, yi, z], [xi + 1.0, yi + 1.0, z]));
        let quad = cam.quad(v0, [xi + 1.0, yi, z], [xi, yi + 1.0, z]);
        renderer.sprite(top, quad, Tint { mul: [s, s, s, 1.0], add: [0.0; 3] });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn camera_projection() {
        let cam = Camera::new(10.0, 10.0, 0.0, 50.0, 400.0, 300.0);
        assert_eq!(cam.to_screen(10.0, 10.0, 0.0), [400.0, 300.0]);
        assert_eq!(cam.to_screen(11.0, 10.0, 0.0), [450.0, 300.0]);
        assert_eq!(cam.to_screen(10.0, 11.0, 1.0), [400.0, 300.0], "one tile south, one up");
        // Turned a quarter: east goes up the screen.
        let cam = Camera::new(10.0, 10.0, std::f64::consts::FRAC_PI_2, 50.0, 400.0, 300.0);
        let p = cam.to_screen(11.0, 10.0, 0.0);
        assert!((p[0] - 400.0).abs() < 1e-3 && (p[1] - 250.0).abs() < 1e-3, "{p:?}");
    }

    #[test]
    fn camera_snaps_to_pixels() {
        for angle in [0.0, 0.7, std::f64::consts::FRAC_PI_2] {
            let cam = Camera::new(10.123, 7.871, angle, 47.3, 400.4, 300.0);
            let snapped = cam.snapped();
            let (o, _, _) = snapped.affine();
            assert!((o[0] - o[0].round()).abs() < 1e-6 && (o[1] - o[1].round()).abs() < 1e-6, "{o:?}");
            // Moved by under a pixel.
            let moved = (snapped.x - cam.x).hypot(snapped.y - cam.y) * cam.scale;
            assert!(moved <= 0.75, "{moved}");
        }
    }

    #[test]
    fn raised_camera_keeps_its_point() {
        // looking at the ground 6 tiles up: what stands there is drawn at the camera point
        for angle in [0.0, 0.7] {
            let cam = Camera::new(10.123, 7.871, angle, 47.3, 400.4, 300.0).at_height(6.0).snapped();
            let [x, y] = cam.to_screen(10.123, 7.871, 6.0);
            assert!((x - 400.4).abs() < 1.0 && (y - 300.0).abs() < 1.0, "{x} {y}");
        }
    }

    #[test]
    fn wall_shading() {
        // Top face: normal up, 0.75 + 0.25 · 2/√14.
        let top = shade(normal([0.0, 0.0, 1.0], [1.0, 0.0, 1.0], [1.0, 1.0, 1.0]));
        assert!((top - (0.75 + 0.25 * 2.0 / 14f32.sqrt())).abs() < 1e-6);
    }
}
