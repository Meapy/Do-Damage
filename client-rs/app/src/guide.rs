//! The Gilded Realm Pass's dungeon guide: in a dungeon, the world server sends the shortest walk
//! from us to the dungeon's boss (`DungeonGuide`, `Player.Guide.cs`, found on the map the
//! server knows, doors and all), and this draws it as a trail of small arrows on the ground that
//! flow toward the boss, bright by our feet and fading out ahead of us, with a chip at the foot
//! of the view: the boss's picture, an arrow turned the way to walk, and how far it is.
//! It goes within 30 tiles of walking of the boss, and once anyone has hurt the boss (the server
//! then sends no path). Options, Gameplay switches it off (`GuideSubscribe`: then the server
//! sends nothing).
//!
//! The path is every second tile along the walk, sent again each time we or the boss move a few
//! tiles, so between messages the trail is cut to where we are now: it starts at our feet and
//! drops the tiles we've passed.

use crate::hud::{Layout, Ui};
use crate::i18n::tn;
use crate::theme::{self, color};
use crate::view::{Camera, TILE_PX};
use bsk_proto::DungeonGuide;
use bsk_render::Tint;

/// How far ahead of us the trail is drawn (tiles of walking), and how far apart its arrows are.
const AHEAD: f32 = 46.0;
const SPACING: f32 = 1.8;
/// How fast the arrows flow toward the boss (tiles a second).
const FLOW: f32 = 3.2;
/// The boss is this close (tiles of walking): the trail goes, the rest is in sight
/// (`Player.GuideNear`, which also stops sending it once anyone has hurt the boss).
const NEAR: f32 = 30.0;
/// A path older than this (ms) goes: the server sends again every few seconds while we're in
/// a dungeon with a boss.
const STALE_MS: i32 = 16_000;

/// The ember of the Realm Pass's gilding, and its shadow.
const TRAIL: u32 = 0xFFD24A;
const TRAIL_EDGE: u32 = 0x3A1A00;

#[derive(Default)]
pub struct Guide {
    /// The walk's tiles (their centres), the boss's own place last.
    path: Vec<(f32, f32)>,
    /// The boss, for the chip's picture.
    boss: u16,
    at: i32,
}

/// One arrow of the trail: where it stands, which way it points (a unit step in the world),
/// and how far along the walk it is from us (tiles).
#[derive(Clone, Copy, Debug, PartialEq)]
struct Mark {
    x: f32,
    y: f32,
    dx: f32,
    dy: f32,
    along: f32,
}

impl Guide {
    pub fn set(&mut self, g: &DungeonGuide, now: i32) {
        self.path = g.points.iter().map(|p| (p.x as f32 + 0.5, p.y as f32 + 0.5)).collect();
        if !self.path.is_empty() {
            self.path.push((g.x, g.y));
        }
        self.boss = g.object_type;
        self.at = now;
    }

    pub fn clear(&mut self) {
        self.path.clear();
    }

    /// Whether there's a walk to show.
    pub fn active(&self, now: i32) -> bool {
        !self.path.is_empty() && now - self.at < STALE_MS
    }

    /// The walk from `me`: our feet, then the path's tiles still ahead of us.
    fn ahead(&self, me: (f32, f32)) -> Vec<(f32, f32)> {
        let d2 = |a: (f32, f32), b: (f32, f32)| (a.0 - b.0).powi(2) + (a.1 - b.1).powi(2);
        let Some((mut i, _)) = self.path.iter().enumerate().min_by(|a, b| d2(*a.1, me).total_cmp(&d2(*b.1, me))) else { return vec![me] };
        // nearest to the tile behind us: the next one is the way on
        if i + 1 < self.path.len() && d2(self.path[i + 1], me) < d2(self.path[i + 1], self.path[i]) {
            i += 1;
        }
        std::iter::once(me).chain(self.path[i..].iter().copied()).collect()
    }

    /// Whether the trail shows from `me`: a fresh path, the boss still far off.
    fn shown(&self, me: (f32, f32), now: i32) -> bool {
        self.active(now) && self.remaining(me) >= NEAR
    }

    /// Tiles of walking left from `me`.
    pub fn remaining(&self, me: (f32, f32)) -> f32 {
        self.ahead(me).windows(2).map(|w| (w[1].0 - w[0].0).hypot(w[1].1 - w[0].1)).sum()
    }

    /// The trail's arrows at `now` (ms): spaced `SPACING` along the walk, sliding on toward the
    /// boss as time passes.
    fn marks(&self, me: (f32, f32), now: i32) -> Vec<Mark> {
        let line = self.ahead(me);
        let offset = (now as f32 / 1000.0 * FLOW).rem_euclid(SPACING);
        let mut out = Vec::new();
        let (mut start, mut s) = (0.0f32, offset);
        for w in line.windows(2) {
            let (dx, dy) = (w[1].0 - w[0].0, w[1].1 - w[0].1);
            let len = dx.hypot(dy);
            if len < 1e-4 {
                continue;
            }
            while s < start + len && s <= AHEAD {
                let k = s - start;
                out.push(Mark { x: w[0].0 + dx / len * k, y: w[0].1 + dy / len * k, dx: dx / len, dy: dy / len, along: s });
                s += SPACING;
            }
            start += len;
            if s > AHEAD {
                break;
            }
        }
        out
    }

    /// The trail and the chip, over the world (after the pings).
    pub fn draw(&self, ui: &mut Ui, cam: &Camera, me: (f64, f64), lay: &Layout, now: i32) {
        let me = (me.0 as f32, me.1 as f32);
        if !self.shown(me, now) {
            return;
        }
        let left = self.remaining(me);
        let k = ((cam.scale / TILE_PX) as f32).max(0.6);
        let white = ui.renderer.white();
        let t = now as f32 / 1000.0;
        // each arrow, drawn where the world is on the screen (so a turned camera turns them)
        let flat = |x: f32, y: f32| cam.to_screen(x as f64, y as f64, 0.0);
        for m in self.marks(me, now) {
            // fade in by our feet (the character shouldn't be buried), and out ahead
            let fade = ((m.along - 1.0) / 1.6).clamp(0.0, 1.0) * ((AHEAD - m.along) / 18.0).clamp(0.0, 1.0);
            // a brightness that runs along the trail, toward the boss
            let wave = 0.62 + 0.38 * (m.along * 0.9 - t * 6.0).sin();
            let a = fade * wave;
            if a < 0.03 {
                continue;
            }
            let [px, py] = flat(m.x, m.y);
            let [qx, qy] = flat(m.x + m.dx * 0.5, m.y + m.dy * 0.5);
            let (vx, vy) = (qx - px, qy - py);
            let vl = vx.hypot(vy);
            if vl < 1e-3 {
                continue;
            }
            let (vx, vy) = (vx / vl, vy / vl);
            let (nx, ny) = (-vy, vx);
            // a chevron: the head and its notch
            let (len, wide) = (11.0 * k, 8.5 * k);
            // a soft glow under it, brighter where the wave passes
            theme::fill(ui.renderer, [px - 11.0 * k, py - 11.0 * k, 22.0 * k, 22.0 * k], 11.0 * k, TRAIL, 0.13 * a);
            let head = |l: f32, w: f32| [[px + vx * l, py + vy * l], [px - vx * l * 0.7 + nx * w, py - vy * l * 0.7 + ny * w], [px - vx * l * 0.7 - nx * w, py - vy * l * 0.7 - ny * w]];
            ui.renderer.triangle(white, head(len + 2.0 * k, wide + 2.0 * k), [[0.5, 0.5]; 3], Tint::color(TRAIL_EDGE, 0.55 * a));
            ui.renderer.triangle(white, head(len, wide), [[0.5, 0.5]; 3], Tint::color(TRAIL, 0.92 * a));
            // the notch, cut back out of its tail
            let (bx, by) = (px - vx * len * 0.7, py - vy * len * 0.7);
            let notch = [[bx + vx * len * 0.75, by + vy * len * 0.75], [bx + nx * wide * 1.05 - vx * 0.5 * k, by + ny * wide * 1.05 - vy * 0.5 * k], [bx - nx * wide * 1.05 - vx * 0.5 * k, by - ny * wide * 1.05 - vy * 0.5 * k]];
            ui.renderer.triangle(white, notch, [[0.5, 0.5]; 3], Tint::color(TRAIL_EDGE, 0.5 * a));
        }
        self.draw_chip(ui, cam, me, left, lay, now);
    }

    /// The chip at the foot of the view: the boss, the way to walk, and how far.
    fn draw_chip(&self, ui: &mut Ui, cam: &Camera, me: (f32, f32), left: f32, lay: &Layout, now: i32) {
        let s = lay.s;
        let tiles = left.round().max(1.0) as usize;
        let text = tn!(tiles, "{n} tile to the boss|{n} tiles to the boss");
        let size = (13.0 * s).round().max(9.0);
        let tw = ui.measure(&text, size, true);
        let (h, pad) = (34.0 * s, 10.0 * s);
        let w = pad * 3.0 + 28.0 * s + 22.0 * s + tw;
        let r = [((lay.view_w() - w) / 2.0).round(), (lay.h - h - 14.0 * s).round(), w, h];
        let pulse = 0.5 + 0.5 * (now as f32 / 300.0).sin();
        theme::shadow(ui.renderer, r, h / 2.0, 10.0 * s, 0.4);
        theme::fill(ui.renderer, [r[0] - 2.0 * s, r[1] - 2.0 * s, r[2] + 4.0 * s, r[3] + 4.0 * s], h / 2.0 + 2.0 * s, TRAIL, 0.10 + 0.08 * pulse);
        theme::frame(ui.renderer, r, h / 2.0, (1.5 * s).max(1.0), (TRAIL, 0.9), ((color::WARM_TOP, 0.94), (color::PANEL_BOTTOM, 0.94)));
        crate::hud::object_icon(ui, self.boss, r[0] + pad + 14.0 * s, r[1] + h / 2.0, 26.0 * s);
        // an arrow turned the way to walk: toward the walk five tiles on, as the screen shows it
        let line = self.ahead(me);
        let mut goal = *line.last().unwrap_or(&me);
        let mut acc = 0.0;
        for w in line.windows(2) {
            acc += (w[1].0 - w[0].0).hypot(w[1].1 - w[0].1);
            if acc >= 5.0 {
                goal = w[1];
                break;
            }
        }
        let [ax, ay] = cam.to_screen(me.0 as f64, me.1 as f64, 0.0);
        let [bx, by] = cam.to_screen(goal.0 as f64, goal.1 as f64, 0.0);
        let (vx, vy) = (bx - ax, by - ay);
        let vl = vx.hypot(vy).max(1e-3);
        let (vx, vy) = (vx / vl, vy / vl);
        let (nx, ny) = (-vy, vx);
        let (cx, cy) = (r[0] + pad * 2.0 + 28.0 * s + 8.0 * s, r[1] + h / 2.0);
        let white = ui.renderer.white();
        let tri = |l: f32, wd: f32| [[cx + vx * l, cy + vy * l], [cx - vx * l * 0.6 + nx * wd, cy - vy * l * 0.6 + ny * wd], [cx - vx * l * 0.6 - nx * wd, cy - vy * l * 0.6 - ny * wd]];
        ui.renderer.triangle(white, tri(12.0 * s, 9.0 * s), [[0.5, 0.5]; 3], Tint::color(TRAIL_EDGE, 0.8));
        ui.renderer.triangle(white, tri(10.0 * s, 7.0 * s), [[0.5, 0.5]; 3], Tint::color(TRAIL, 1.0));
        ui.label(&text, cx + 18.0 * s, r[1] + (h - size * 1.22) / 2.0, size, true, color::TEXT);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use bsk_proto::GuidePoint;

    fn guide(points: &[(u16, u16)], boss: (f32, f32)) -> Guide {
        let mut g = Guide::default();
        let msg = DungeonGuide {
            object_type: 0x0932,
            x: boss.0,
            y: boss.1,
            length: points.len() as i32 * 2,
            points: points.iter().map(|&(x, y)| GuidePoint { x, y }).collect::<Vec<_>>().into(),
        };
        g.set(&msg, 1000);
        g
    }

    #[test]
    fn the_trail_starts_where_we_stand_and_drops_what_we_passed() {
        // a walk east along y 10, then north to the boss at (20, 4)
        let g = guide(&[(10, 10), (12, 10), (14, 10), (16, 10), (18, 10), (18, 8), (18, 6)], (20.5, 4.5));
        // at the start: the whole walk, 8 east and some north
        let all = g.remaining((9.5, 10.5));
        assert!(all > 14.0 && all < 18.0, "{all}");
        // having walked to the fourth tile: the first three are behind us
        let later = g.remaining((16.5, 10.5));
        assert!(later < all - 6.0, "{later} {all}");
        // nothing behind: the line begins at our feet
        assert_eq!(g.ahead((16.5, 10.5))[0], (16.5, 10.5));
        assert_eq!(g.ahead((16.5, 10.5))[1], (16.5, 10.5));
        // standing by the boss: next to none left
        assert!(g.remaining((20.0, 4.0)) < NEAR);
    }

    #[test]
    fn the_arrows_flow_along_the_walk_toward_the_boss() {
        let g = guide(&[(10, 10), (12, 10), (14, 10), (16, 10), (18, 10), (20, 10), (22, 10), (24, 10)], (26.5, 10.5));
        let me = (9.5, 10.5);
        let a = g.marks(me, 0);
        assert!(!a.is_empty());
        // spaced, pointing east, in order of distance
        assert!(a.windows(2).all(|w| (w[1].along - w[0].along - SPACING).abs() < 1e-3));
        assert!(a.iter().all(|m| m.dx > 0.99 && m.dy.abs() < 1e-3));
        // a moment later they've moved on toward the boss
        let b = g.marks(me, 100);
        assert!(b[0].along > a[0].along || b[0].along < SPACING / 2.0);
        assert!(b.iter().all(|m| m.along <= AHEAD));
        // none further than the trail goes
        let far = guide(&[(10, 10), (60, 10), (110, 10), (160, 10)], (200.5, 10.5));
        assert!(far.marks(me, 0).iter().all(|m| m.along <= AHEAD));
    }

    #[test]
    fn the_trail_goes_within_thirty_tiles_of_the_boss() {
        // a walk of 40 tiles east to the boss
        let pts: Vec<(u16, u16)> = (0..20).map(|i| (10 + i * 2, 10)).collect();
        let g = guide(&pts, (50.5, 10.5));
        assert!(g.shown((9.5, 10.5), 1000));
        // 25 tiles of walking left: near enough to see
        assert!(!g.shown((25.5, 10.5), 1000));
    }

    #[test]
    fn an_empty_path_or_an_old_one_shows_nothing() {
        let mut g = guide(&[(10, 10), (12, 10)], (14.5, 10.5));
        assert!(g.active(2000));
        assert!(!g.active(1000 + STALE_MS));
        g.set(&DungeonGuide::default(), 3000);
        assert!(!g.active(3000));
        let mut g = guide(&[(10, 10)], (12.5, 10.5));
        g.clear();
        assert!(!g.active(1100));
    }
}
