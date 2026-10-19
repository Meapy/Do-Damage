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
    /// The angle's sine and cosine, worked out once (every point drawn uses them).
    sin: f64,
    cos: f64,
}

impl Camera {
    pub fn new(x: f64, y: f64, angle: f64, scale: f64, cx: f64, cy: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Camera { x, y, angle, scale, cx, cy, sin, cos }
    }

    /// The same view moved by under a pixel so the world's origin is on a whole pixel: the
    /// ground's pixels then keep their places as the camera follows us, instead of shimmering
    /// with every fraction of a pixel it moves.
    pub fn snapped(self) -> Self {
        let (o, _, _) = self.affine();
        let (dx, dy) = ((self.cx - o[0].round()) / self.scale, (self.cy - o[1].round()) / self.scale);
        let (s, c) = (self.sin, self.cos);
        Camera { x: dx * c - dy * s, y: dx * s + dy * c, ..self }
    }

    pub fn to_screen(&self, x: f64, y: f64, z: f64) -> [f32; 2] {
        let (s, c) = (self.sin, self.cos);
        let (dx, dy) = (x - self.x, y - self.y);
        let sx = dx * c + dy * s;
        let sy = -dx * s + dy * c - z;
        [(self.cx + sx * self.scale) as f32, (self.cy + sy * self.scale) as f32]
    }

    /// `to_screen` as an affine map: where (0, 0, 0) lands, and the steps for one tile along x
    /// and along y (z goes straight up the screen by `scale`). Exact, and cheap for many points.
    fn affine(&self) -> ([f64; 2], [f64; 2], [f64; 2]) {
        let (s, c) = (self.sin, self.cos);
        let o = [self.cx - (self.x * c + self.y * s) * self.scale, self.cy - (-self.x * s + self.y * c) * self.scale];
        (o, [c * self.scale, -s * self.scale], [s * self.scale, c * self.scale])
    }

    /// The ground point under a screen point (`Map.pSTopW`).
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
    let len = (1.0f64 + 9.0 + 4.0).sqrt();
    let light = [1.0 / len, 3.0 / len, 2.0 / len];
    let n_len = (normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]).sqrt();
    let dot = (normal[0] * light[0] + normal[1] * light[1] + normal[2] * light[2]) / n_len;
    (0.75 + 0.25 * dot.max(0.0)) as f32
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

impl Floater {
    /// A damage number: red, for a second.
    pub fn damage(object_id: i32, amount: i32, at: i32) -> Self {
        Floater { object_id, text: format!("-{amount}"), color: 0xFF0000, at, life: 1000 }
    }
}

/// Whether a condition effect helps whoever has it (its text shows green, not red): speed,
/// healing, damage and defence buffs, invisibility, the immunities and stat boosts.
pub fn helps(effect: u32) -> bool {
    use bsk_world::condition::*;
    matches!(effect, SPEEDY | HEALING | DAMAGING | BERSERK | INVULNERABLE | INVINCIBLE | ARMORED | NINJA_SPEEDY | INVISIBLE | INSPIRED)
        || matches!(effect, 12 | 17 | 23 | 32 | 33 | 34 | 36 | 39 | 50)
        || (40..=47).contains(&effect)
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
        const ATTACK_PERIOD: i32 = 300;
        let attacking = self.time < obj.attack_start + ATTACK_PERIOD;
        let facing = if attacking && !desc.dont_face_attacks { obj.attack_angle } else { obj.facing };
        let rel = bound_to_pi(facing - self.camera.angle);
        let sec = ((rel / std::f64::consts::FRAC_PI_4 + 4.0) as i32).rem_euclid(8) as usize;
        let dirs = SEC_TO_DIRS[sec];
        let (dir, (row, mirror)) = dirs.iter().find_map(|&d| row_of(d).map(|r| (d, r)))?;
        let symmetric = matches!(dir, Dir::Down | Dir::Up);

        if attacking {
            if let Some(look) = self.attack_look(set, *index, row, mirror, obj) {
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
    /// 3.5 / move speed (365 ms at the fastest, 875 at the slowest); others:
    /// `GameObject.getTexture`'s 0.5 / speed, rounded up to a multiple of 400.
    fn walk_period(&self, obj: &GameObj, desc: &ObjectDesc) -> i32 {
        let speed = if desc.class != ObjectClass::Player {
            let w = (0.5 / obj.walk_speed.max(1e-6)).min(1e6) as i32;
            return w + (400 - w % 400);
        } else if Some(obj.id) == self.world.me {
            obj.walk_speed
        } else {
            bsk_world::move_speed(obj.stat_int(bsk_world::SPEED_STAT).unwrap_or(0), obj.conditions(), 1.0)
        };
        ((3.5 / speed.max(1e-6)) as i32).clamp(100, 2000)
    }

    /// `AnimatedChar.loadDir`'s attack frames: frame 4, then frame 5 (two frames wide with frame 6,
    /// the sword bit, when that isn't blank). `None` if both are blank (then it walks instead).
    fn attack_look(&self, set: &str, index: u32, row: u32, mirror: bool, obj: &GameObj) -> Option<Look> {
        let a1 = self.sprites.char_src(set, index, row, 4).filter(|r| !self.sprites.is_blank(*r));
        let a2 = self.sprites.char_src(set, index, row, 5).filter(|r| !self.sprites.is_blank(*r));
        let frames: Vec<SrcRect> = a1.into_iter().chain(a2).collect();
        if frames.is_empty() {
            return None;
        }
        let p = ((self.time - obj.attack_start).rem_euclid(300)) as f64 / 300.0;
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
}

#[allow(clippy::too_many_arguments)]
pub fn draw(scene: &Scene, renderer: &mut Renderer, redrawer: &mut Redrawer, ground: &mut Ground, fonts: &Fonts, text: &mut Text) {
    let (w, h) = renderer.size();
    let cam = scene.camera;
    let radius = cam.radius(w as f64, h as f64);
    let world = scene.world;
    let (x0, x1) = ((cam.x - radius).floor() as i32, (cam.x + radius).ceil() as i32);
    let (y0, y1) = ((cam.y - radius).floor() as i32, (cam.y + radius).ceil() as i32);

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

    // Objects and walls, back to front by the screen y of their ground point.
    let mut items: Vec<(f32, Item)> = Vec::new();
    for obj in world.objects.values() {
        if (obj.x - cam.x).abs() > radius || (obj.y - cam.y).abs() > radius {
            continue;
        }
        let Some(desc) = scene.data.objects.get(&obj.object_type) else { continue };
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
        if (p.x - cam.x).abs() <= radius && (p.y - cam.y).abs() <= radius {
            items.push((cam.to_screen(p.x, p.y, 0.0)[1], Item::Projectile(p)));
        }
    }
    items.sort_by(|a, b| a.0.total_cmp(&b.0));
    crate::prof::mark("v.sort");

    // Map.draw: DrawUnder objects first, then the shadows, then everything else.
    let under = |item: &Item| matches!(item, Item::Sprite { desc, .. } if desc.draw_under);
    // Where each sprite's top is drawn (x, y), for its condition icons.
    let mut tops: Vec<(&GameObj, [f32; 2])> = Vec::new();
    for (_, item) in items.iter().filter(|(_, i)| under(i)) {
        if let Item::Sprite { obj, desc } = item {
            if let Some(top) = draw_sprite(scene, renderer, redrawer, ground, obj, desc) {
                tops.push((obj, top));
            }
        }
    }
    let k = (cam.scale / TILE_PX) as f32;
    if let Some(shadow) = redrawer.shadow(renderer).filter(|_| scene.show.shadows) {
        for (_, item) in items.iter().filter(|(_, i)| !under(i)) {
            let Item::Sprite { obj, desc } = item else { continue };
            if desc.shadow_size <= 0 {
                continue;
            }
            // 30 × 15 px half-axes at size 100, under the object's ground point.
            let size = obj.stat_int(2).unwrap_or(desc.min_size) as f32;
            let s = size / 100.0 * (desc.shadow_size as f32 / 100.0);
            let (sw, sh) = (30.0 * s * k, 15.0 * s * k);
            let [px, py] = cam.to_screen(obj.x, obj.y, 0.0);
            let alpha = fade(scene, obj, desc);
            if alpha <= 0.0 {
                continue;
            }
            renderer.sprite(shadow, Quad::rect(px - sw, py - sh, 2.0 * sw, 2.0 * sh), Tint::color(desc.shadow_color, alpha));
        }
    }
    for (_, item) in items.iter().filter(|(_, i)| !under(i)) {
        match item {
            Item::Wall { obj, desc } => draw_wall(scene, renderer, obj, desc),
            Item::Connected { obj, desc } => draw_connected(scene, renderer, ground, obj, desc),
            Item::Model { obj, desc, model } => draw_model(scene, renderer, obj, desc, model),
            Item::Projectile(p) => draw_projectile(scene, renderer, redrawer, p),
            Item::Sprite { obj, desc } => {
                if let Some(top) = draw_sprite(scene, renderer, redrawer, ground, obj, desc) {
                    tops.push((obj, top));
                }
            }
        }
    }
    crate::prof::mark("v.objects");
    scene.particles.draw(&cam, renderer);
    crate::prof::mark("v.particles");

    // Condition icons (`GameObject.drawConditionIcons`): in a row centred on the sprite's top,
    // each taking turns through its icons; none while paused.
    // Whole screen pixels per icon pixel, a little under the AIR client's x2, so the art stays
    // sharp.
    let icon_scale = ((1.5 * k).round() as u32).max(1);
    for (obj, [x, y]) in &tops {
        let c = obj.conditions();
        if c == [0, 0] || has(c, condition::PAUSED) {
            continue;
        }
        let icons: Vec<_> = condition::icons(c, scene.time)
            .into_iter()
            .filter_map(|(big, index)| scene.sprites.tile_src(if big { "lofiInterfaceBig" } else { "lofiInterface2" }, index))
            .filter_map(|src| redrawer.icon(scene.sprites, renderer, src, icon_scale))
            .collect();
        let step = 8.0 * icon_scale as f32 + 3.0;
        let left = x - step * icons.len() as f32 / 2.0;
        for (i, icon) in icons.iter().enumerate() {
            let cx = left + step * (i as f32 + 0.5);
            let (w, h) = (icon.w as f32, icon.h as f32);
            renderer.sprite(icon.rect, Quad::rect((cx - w / 2.0).round(), (y - h / 2.0).round(), w, h), Tint::NONE);
        }
    }

    crate::prof::mark("v.icons");
    // HP bars (`GameObject.drawHpBar`) under monsters and players.
    for (_, item) in items.iter().filter(|_| scene.show.hp_bars) {
        let Item::Sprite { obj, desc } = item else { continue };
        if !(desc.enemy || desc.player) || desc.no_mini_map || fade(scene, obj, desc) <= 0.0 {
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
        let [px, py] = cam.to_screen(obj.x, obj.y, 0.0);
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
    for f in scene.floaters.iter().filter(|f| scene.show.damage_numbers || !(f.color == 0xFF0000 && f.text.starts_with('-'))) {
        let Some(obj) = world.objects.get(&f.object_id) else { continue };
        let age = (scene.time - f.at) as f32 / 1000.0;
        let [px, py] = cam.to_screen(obj.x, obj.y, 0.0);
        let y = py - (55.0 + 40.0 * age) * k;
        text.draw_centered(fonts, renderer, &f.text, px, y, Style { color: f.color, ..style });
    }

    // Speech balloons: the text wrapped to 150 px, in a cut-corner box with a tail, above the
    // speaker.
    for b in scene.bubbles.iter().filter(|_| scene.show.bubbles) {
        let Some(obj) = world.objects.get(&b.object_id) else { continue };
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
        let [px, py] = cam.to_screen(obj.x, obj.y, 0.0);
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
        if !(desc.show_name || player || realm_portal) || fade(scene, obj, desc) <= 0.0 {
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
        let [px, py] = cam.to_screen(obj.x, obj.y, 0.0);
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
    if desc.player && Some(obj.id) != scene.world.me {
        scene.show.player_alpha
    } else if scene.world.others_pet(obj) {
        scene.show.pet_alpha
    } else {
        1.0
    }
}

fn draw_sprite(scene: &Scene, renderer: &mut Renderer, redrawer: &mut Redrawer, ground: &mut Ground, obj: &GameObj, desc: &ObjectDesc) -> Option<[f32; 2]> {
    let cam = scene.camera;
    let alpha = fade(scene, obj, desc);
    if alpha <= 0.0 {
        return None;
    }
    let mut look = scene.look(obj, desc)?;
    if desc.draw_on_ground {
        // Flat on its square, like a tile.
        let rect = scene.sprites.atlas(look.src)?;
        let (x, y) = (obj.x.floor(), obj.y.floor());
        renderer.sprite(rect, cam.quad([x, y, 0.0], [x + 1.0, y, 0.0], [x, y + 1.0, 0.0]), Tint::NONE);
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
    let sprite = redrawer.get(scene.sprites, renderer, look.src, look.mirror, size, dye)?;
    // The canvas's bottom centre stands on the object (raised by its z); sprites scale with the
    // map. On sinking ground (water) the sprite is drawn lower with its bottom cut off, unless it
    // flies.
    let k = (cam.scale / TILE_PX) as f32;
    let [px, py] = cam.to_screen(obj.x, obj.y, desc.z);
    let mut sink = 0.0;
    if !desc.flying && !desc.protect_from_sink() {
        if let Some(l) = ground.layers(scene.world, scene.data, scene.sprites, renderer, obj.x as i32, obj.y as i32) {
            sink = (l.sink as f32).min(sprite.h as f32);
        }
    }
    let (sw, sh) = (sprite.w as f32 * k, sprite.h as f32 * k);
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
/// on the projectile (half a tile up), turned to its direction (plus the object's
/// AngleCorrection, and its spin if it has a Rotation).
fn draw_projectile(scene: &Scene, renderer: &mut Renderer, redrawer: &mut Redrawer, p: &Projectile) {
    let cam = scene.camera;
    let Some(desc) = scene.world.projectile_desc(p, scene.data) else { return };
    let Some(look_desc) = scene.data.object_ids.get(&desc.object_id).and_then(|t| scene.data.objects.get(t)) else {
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
    let angle = p.angle - cam.angle + look_desc.angle_correction + spin;
    let k = (cam.scale / TILE_PX) as f32;
    let [px, py] = cam.to_screen(p.x, p.y, p.z);
    let quad = Quad::rotated(px, py, sprite.w as f32 * k, sprite.h as f32 * k, angle as f32);
    renderer.sprite(sprite.rect, quad, tint);
}

/// `Object3D.draw`: the model turned by the object's Rotation (degrees) and placed at it; faces
/// in model order, those facing away skipped, each lit like a wall face. Textured faces show
/// the object's texture; "Solid" ones its colour.
fn draw_model(scene: &Scene, renderer: &mut Renderer, obj: &GameObj, desc: &ObjectDesc, model: &Model) {
    let cam = scene.camera;
    let texture = scene.texture_src(&desc.texture, obj.id).and_then(|s| scene.sprites.atlas(s));
    let (s, c) = desc.rotation.to_radians().sin_cos();
    let world: Vec<[f64; 3]> = model
        .vertices
        .iter()
        .map(|(p, _)| [p[0] * c - p[1] * s + obj.x, p[0] * s + p[1] * c + obj.y, p[2]])
        .collect();
    let screen: Vec<[f32; 2]> = world.iter().map(|p| cam.to_screen(p[0], p[1], p[2])).collect();
    for face in &model.faces {
        let ix = &face.indices;
        let (i0, i1, il) = (ix[0], ix[1], ix[ix.len() - 1]);
        let (u, v) = (sub(screen[i1], screen[i0]), sub(screen[il], screen[i0]));
        if u[0] * v[1] - u[1] * v[0] < 0.0 {
            continue;
        }
        let shade = shade(normal(world[i0], world[i1], world[il]));
        let (src, tint) = match (face.textured, texture) {
            (true, Some(t)) => (t, Tint { mul: [shade, shade, shade, 1.0], add: [0.0; 3] }),
            _ => {
                let [r, g, b] = bsk_render::rgb_f32(desc.color.unwrap_or(0));
                (renderer.white(), Tint { mul: [r * shade, g * shade, b * shade, 1.0], add: [0.0; 3] })
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
    let side = scene.texture_src(&desc.texture, obj.id).and_then(|s| scene.sprites.atlas(s));
    let top = desc
        .top
        .as_ref()
        .and_then(|t| scene.texture_src(t, obj.id))
        .and_then(|s| scene.sprites.atlas(s))
        .or(side);
    let Some(side) = side else { return };

    // Sides, in Wall.addWall order: north, east, south, west; neighbours at sqX/sqY.
    let corners = [
        ([xi, yi], [xi + 1.0, yi], (0, -1)),
        ([xi + 1.0, yi], [xi + 1.0, yi + 1.0], (1, 0)),
        ([xi + 1.0, yi + 1.0], [xi, yi + 1.0], (0, 1)),
        ([xi, yi + 1.0], [xi, yi], (-1, 0)),
    ];
    for (a, b, (nx, ny)) in corners {
        let v0 = [a[0], a[1], 1.0];
        let v1 = [b[0], b[1], 1.0];
        let v3 = [a[0], a[1], 0.0];
        let quad = cam.quad(v0, v1, v3);
        // Back faces: the projected corners wind the other way.
        let cross = quad.x_axis[0] * quad.y_axis[1] - quad.x_axis[1] * quad.y_axis[0];
        if cross >= 0.0 {
            continue;
        }
        let (sx, sy) = (xi as i32 + nx, yi as i32 + ny);
        let neighbour_wall = scene
            .world
            .occupant(sx, sy)
            .and_then(|o| scene.data.objects.get(&o.object_type))
            .is_some_and(|d| d.class.is_wall());
        if scene.world.tile(sx, sy).is_none() || neighbour_wall {
            renderer.sprite(renderer.white(), quad, Tint::color(0x000000, 1.0));
            continue;
        }
        let s = shade(normal(v0, v1, [b[0], b[1], 0.0]));
        renderer.sprite(side, quad, Tint { mul: [s, s, s, 1.0], add: [0.0; 3] });
    }
    if let Some(top) = top {
        let v0 = [xi, yi, 1.0];
        let s = shade(normal(v0, [xi + 1.0, yi, 1.0], [xi + 1.0, yi + 1.0, 1.0]));
        let quad = cam.quad(v0, [xi + 1.0, yi, 1.0], [xi, yi + 1.0, 1.0]);
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
    fn wall_shading() {
        // Top face: normal up, 0.75 + 0.25 · 2/√14.
        let top = shade(normal([0.0, 0.0, 1.0], [1.0, 0.0, 1.0], [1.0, 1.0, 1.0]));
        assert!((top - (0.75 + 0.25 * 2.0 / 14f32.sqrt())).abs() < 1e-6);
    }
}
