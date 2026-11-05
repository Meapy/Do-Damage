//! Our player's walking and collision (`Player.update`, `walkTo`, `modifyMove`, `modifyStep`,
//! `isValidPosition`, `onMove`), step for step.

use crate::condition::{self, has};
use crate::{World, NO_TILE};
use bsk_data::GameData;

const MOVE_THRESHOLD: f64 = 0.4;
const MIN_MOVE_SPEED: f64 = 0.004;
const MAX_MOVE_SPEED: f64 = 0.0096;
const MAX_SINK_LEVEL: i32 = 18;
pub const SPEED_STAT: u8 = 22;

/// The keys held: a direction relative to the screen (x right, y down; each -1, 0 or 1) and
/// the camera angle the screen is turned by.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct MoveInput {
    pub x: f64,
    pub y: f64,
    pub camera_angle: f64,
}

/// Tiles per millisecond (`Player.getMoveSpeed`).
pub fn move_speed(speed_stat: i32, conditions: [u32; 2], multiplier: f64) -> f64 {
    if has(conditions, condition::SLOWED) {
        return MIN_MOVE_SPEED * multiplier;
    }
    let mut speed = MIN_MOVE_SPEED + speed_stat as f64 / 75.0 * (MAX_MOVE_SPEED - MIN_MOVE_SPEED);
    if has(conditions, condition::SPEEDY) || has(conditions, condition::NINJA_SPEEDY) {
        speed *= 1.35;
    }
    speed * multiplier
}

impl World {
    pub(crate) fn walk(&mut self, dt: i32, input: &MoveInput, data: &GameData) -> Option<(f64, f64)> {
        let me = self.me?;
        let player = self.objects.get(&me)?;
        let conditions = player.conditions();
        if has(conditions, condition::PAUSED) {
            return None;
        }
        let (mut rel_x, mut rel_y) = (input.x, input.y);
        if has(conditions, condition::CONFUSED) {
            (rel_x, rel_y) = (-rel_y, -rel_x);
        }
        let (mut vx, mut vy) = (0.0, 0.0);
        let speed = move_speed(player.stat_int(SPEED_STAT).unwrap_or(0), conditions, self.move_multiplier);
        if rel_x != 0.0 || rel_y != 0.0 {
            let angle = input.camera_angle + rel_y.atan2(rel_x);
            vx = speed * angle.cos();
            vy = speed * angle.sin();
        }
        let (x, y) = (player.x, player.y);
        if let Some(ground) = self.square_at(x as i32, y as i32, player.z, data).0.and_then(|t| data.grounds.get(&t)) {
            if ground.push {
                vx -= ground.animate.dx / 1000.0;
                vy -= ground.animate.dy / 1000.0;
            }
        }
        if vx == 0.0 && vy == 0.0 {
            if let Some(p) = self.objects.get_mut(&me) {
                p.moving = false;
            }
            return None;
        }
        let target = (x + dt as f64 * vx, y + dt as f64 * vy);
        let (nx, ny) = if has(conditions, condition::PARALYZED) || has(conditions, condition::PETRIFIED) {
            (x, y)
        } else {
            self.modify_move(x, y, target.0, target.1, data)
        };
        let moved = (nx, ny) != (x, y);
        let now = self.last_update;
        if let Some(p) = self.objects.get_mut(&me) {
            // Walk frames whenever we're pushed somewhere (`moveVec_`), as the AIR client: also
            // into a wall, and in a frame too short to move in (a 0 ms step at high frame rates,
            // which used to flash the standing frame).
            p.moving = true;
            p.walk_speed = speed;
            // the way we walk, but not while shooting: then the way we shot (note_attack) holds,
            // and stays when we stop
            let shooting = now.is_some_and(|t| p.attack_phase(t).is_some());
            if (rel_x != 0.0 || rel_y != 0.0) && !shooting {
                p.facing = vy.atan2(vx);
            }
        }
        if !(moved && self.place(me, nx, ny, data)) {
            return None;
        }
        let _ = self.climb(nx, ny, data);
        Some((nx, ny))
    }

    /// On stacked floors, our height where we now stand: the ground of the floor at our height,
    /// so a stair's steps take us up or down. False if no floor's square there is known.
    pub(crate) fn climb(&mut self, x: f64, y: f64, data: &GameData) -> bool {
        if self.levels.is_none() {
            return true;
        }
        let Some(me) = self.me else { return true };
        let z = self.my_z();
        // the floor at our height, else (put somewhere our height doesn't fit) the nearest we can
        // walk on (not the inside of a tower under its top)
        let walkable = |l: u8| self.level_tile(l, x as i32, y as i32).and_then(|t| data.grounds.get(&t)).is_some_and(|g| !g.no_walk);
        let level = self.level_at(x as i32, y as i32, z, data).filter(|&l| walkable(l)).or_else(|| {
            (0..self.level_count() as u8)
                .filter(|&l| walkable(l))
                .filter_map(|l| self.surface(l, x as i32, y as i32, data).map(|s| ((s - z).abs(), l)))
                .min_by(|a, b| a.0.total_cmp(&b.0))
                .map(|(_, l)| l)
                .or_else(|| self.nearest_level(x as i32, y as i32, z, data))
        });
        let Some(level) = level else { return false };
        let Some(ground) = self.surface(level, x as i32, y as i32, data) else { return false };
        if let Some(p) = self.objects.get_mut(&me) {
            (p.z, p.level) = (ground, level);
        }
        true
    }

    /// `Player.onMove`, called as each Move goes to the server: the ground's speed, and sinking.
    pub fn on_move(&mut self, data: &GameData) {
        let Some(p) = self.player() else { return };
        let ground = self.square_at(p.x as i32, p.y as i32, p.z, data).0.and_then(|t| data.grounds.get(&t));
        let speed = ground.map_or(1.0, |g| g.speed);
        if ground.is_some_and(|g| g.sinking) {
            self.sink_level = (self.sink_level + 1).min(MAX_SINK_LEVEL);
            self.move_multiplier = 0.1 + (1.0 - self.sink_level as f64 / MAX_SINK_LEVEL as f64) * (speed - 0.1);
        } else {
            self.sink_level = 0;
            self.move_multiplier = speed;
        }
    }

    pub fn sink_level(&self) -> i32 {
        self.sink_level
    }

    fn modify_move(&self, x0: f64, y0: f64, x: f64, y: f64, data: &GameData) -> (f64, f64) {
        let (dx, dy) = (x - x0, y - y0);
        if dx < MOVE_THRESHOLD && dx > -MOVE_THRESHOLD && dy < MOVE_THRESHOLD && dy > -MOVE_THRESHOLD {
            return self.modify_step(x0, y0, x, y, data);
        }
        let mut step = MOVE_THRESHOLD / dx.abs().max(dy.abs());
        let mut d = 0.0;
        let mut p = (x0, y0);
        let mut done = false;
        while !done {
            if d + step >= 1.0 {
                step = 1.0 - d;
                done = true;
            }
            p = self.modify_step(p.0, p.1, p.0 + dx * step, p.1 + dy * step, data);
            d += step;
        }
        p
    }

    /// One step from (x0, y0) toward (x, y), sliding along walls at half-tile borders.
    fn modify_step(&self, x0: f64, y0: f64, x: f64, y: f64, data: &GameData) -> (f64, f64) {
        let int = |v: f64| v as i32;
        let x_cross = (x0 % 0.5 == 0.0 && x != x0) || int(x0 / 0.5) != int(x / 0.5);
        let y_cross = (y0 % 0.5 == 0.0 && y != y0) || int(y0 / 0.5) != int(y / 0.5);
        if (!x_cross && !y_cross) || self.is_valid_position(x0, y0, x, y, data) {
            return (x, y);
        }
        let mut next_x = f64::NAN;
        let mut next_y = f64::NAN;
        // Stopped short of a square's border going east or south, but never pushed back from
        // it: the AIR client puts us 0.01 short every time we touch it, so short steps (high
        // frame rates, or sliding along a wall at an angle) crept up and were pushed back each
        // frame, shaking the view. At most 0.001 short, so the position sent stays in our square.
        let short_of = |border: f64, from: f64| (border - 0.01).max(from.min(border - 0.001));
        if x_cross {
            next_x = if x > x0 { int(x * 2.0) as f64 / 2.0 } else { int(x0 * 2.0) as f64 / 2.0 };
            if int(next_x) > int(x0) {
                next_x = short_of(next_x, x0);
            }
        }
        if y_cross {
            next_y = if y > y0 { int(y * 2.0) as f64 / 2.0 } else { int(y0 * 2.0) as f64 / 2.0 };
            if int(next_y) > int(y0) {
                next_y = short_of(next_y, y0);
            }
        }
        if !x_cross {
            return (x, next_y);
        }
        if !y_cross {
            return (next_x, y);
        }
        let x_dist = if x > x0 { x - next_x } else { next_x - x };
        let y_dist = if y > y0 { y - next_y } else { next_y - y };
        if x_dist > y_dist {
            if self.is_valid_position(x0, y0, x, next_y, data) {
                return (x, next_y);
            }
            if self.is_valid_position(x0, y0, next_x, y, data) {
                return (next_x, y);
            }
        } else {
            if self.is_valid_position(x0, y0, next_x, y, data) {
                return (next_x, y);
            }
            if self.is_valid_position(x0, y0, x, next_y, data) {
                return (x, next_y);
            }
        }
        (next_x, next_y)
    }

    /// `Square.isWalkable`: no NoWalk ground and no OccupySquare object. On stacked floors, the
    /// square of the floor at our height: none there is a wall or a drop.
    fn walkable(&self, sx: i32, sy: i32, data: &GameData) -> bool {
        let (tile, occupant) = self.square_at(sx, sy, self.my_z(), data);
        // An unsent square gets GroundLibrary.defaultProps_ (type 255).
        let tile = tile.unwrap_or(NO_TILE);
        let no_walk = data.grounds.get(&tile).map_or(true, |g| g.no_walk);
        let occupied = occupant.and_then(|o| data.objects.get(&o.object_type)).is_some_and(|d| d.occupy_square);
        !no_walk && !occupied
    }

    /// On stacked floors, whether a railing stands between our square at (x0, y0) and the one at
    /// (x, y) beside it: along our ground's side toward it, or its ground's side toward us. A
    /// railing stops us as it looks it should, though the drop behind it could be stepped (a
    /// stairwell's edge).
    fn railing_between(&self, x0: f64, y0: f64, x: f64, y: f64, data: &GameData) -> bool {
        if self.levels.is_none() {
            return false;
        }
        let Some(me) = self.player() else { return false };
        let (ax, ay, bx, by) = (x0 as i32, y0 as i32, x as i32, y as i32);
        let there = self.level_at(bx, by, me.z, data);
        let (dx, dy) = (bx - ax, by - ay);
        [(dx, 0), (0, dy)].into_iter().filter(|&(nx, ny)| nx != 0 || ny != 0).any(|(nx, ny)| {
            self.railed(me.level, ax, ay, nx, ny, data) || there.is_some_and(|l| self.railed(l, bx, by, -nx, -ny, data))
        })
    }

    /// `Player.isValidPosition`, from our square at (x0, y0).
    fn is_valid_position(&self, x0: f64, y0: f64, x: f64, y: f64, data: &GameData) -> bool {
        let out = x < 0.0 || y < 0.0 || x >= self.width as f64 || y >= self.height as f64;
        let same_square = (x as i32, y as i32) == (x0 as i32, y0 as i32);
        if !same_square && (out || !self.walkable(x as i32, y as i32, data) || self.railing_between(x0, y0, x, y, data)) {
            return false;
        }
        let full = |fx: f64, fy: f64| self.is_full_occupy(fx, fy, data);
        let x_frac = x - (x as i32) as f64;
        let y_frac = y - (y as i32) as f64;
        if x_frac < 0.5 {
            if full(x - 1.0, y) {
                return false;
            }
            if y_frac < 0.5 {
                if full(x, y - 1.0) || full(x - 1.0, y - 1.0) {
                    return false;
                }
            } else if y_frac > 0.5 && (full(x, y + 1.0) || full(x - 1.0, y + 1.0)) {
                return false;
            }
        } else if x_frac > 0.5 {
            if full(x + 1.0, y) {
                return false;
            }
            if y_frac < 0.5 {
                if full(x, y - 1.0) || full(x + 1.0, y - 1.0) {
                    return false;
                }
            } else if y_frac > 0.5 && (full(x, y + 1.0) || full(x + 1.0, y + 1.0)) {
                return false;
            }
        } else if y_frac < 0.5 {
            if full(x, y - 1.0) {
                return false;
            }
        } else if y_frac > 0.5 && full(x, y + 1.0) {
            return false;
        }
        true
    }

    /// `Player.isFullOccupy`: outside the map, no tile sent, empty tile, or a FullOccupy object.
    /// The coordinates truncate toward zero, as AS3's int parameters do. On stacked floors a
    /// square with no floor at our height but one higher or lower (a drop, a tower's core or
    /// wall) isn't: it can't be walked onto (`walkable`), but like a wall's ground it doesn't
    /// catch us at its corners, so we don't snag on a stair's core walking up beside it.
    fn is_full_occupy(&self, x: f64, y: f64, data: &GameData) -> bool {
        let (sx, sy) = (x as i32, y as i32);
        let (tile, occupant) = self.square_at(sx, sy, self.my_z(), data);
        match tile {
            None if self.levels.is_some() && x >= 0.0 && y >= 0.0 => {
                !(0..self.level_count() as u8).any(|l| self.level_tile(l, sx, sy).is_some_and(|t| t != NO_TILE))
            }
            None => true,
            Some(NO_TILE) => true,
            Some(_) => occupant.and_then(|o| data.objects.get(&o.object_type)).is_some_and(|d| d.full_occupy),
        }
    }
}
