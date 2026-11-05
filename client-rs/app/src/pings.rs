//! Pings: the middle mouse button marks a place for the player's party and guild in the world,
//! and the players near them (`PingMark`, answered with `PingShow` to everyone who sees it, us
//! too).
//!
//! A click pings what's under the mouse: an enemy is Danger, a loot bag Loot, a portal Go here,
//! ourselves Help, anything else Look here; a ping on an object follows it while it's in sight.
//! A click on the minimap is Go here, at that spot. Held (or dragged), the button opens the ping
//! wheel round the spot: let go over a kind to ping it, in the middle to cancel.
//!
//! A ping shows for `LIFE` ms: a pulsing ring on the ground and a pin over it with its kind's
//! icon, who sent it and what it says, at the view's edge when it's off screen, and on the
//! minimap; with a sound. Options choose whose pings show (`PingsFrom`) and whether they sound.
//! We only ping once the server has said it knows pings (it answers our `PingMark` 255), and
//! keep to its limits ourselves (`Marks::take`).

use crate::hud::{MiniView, Ui};
use crate::i18n::tr;
use crate::options::PingsFrom;
use crate::theme::{self, color};
use bsk_data::{GameData, ObjectClass};
use bsk_render::Tint;
use bsk_world::World;

pub const LOOK: u8 = 0;
pub const DANGER: u8 = 1;
pub const GO: u8 = 2;
pub const LOOT: u8 = 3;
pub const HELP: u8 = 4;
/// How many kinds there are (the server refuses others).
pub const KINDS: u8 = 5;
/// The kind that says we can show pings, and that the server answers to say it knows them.
pub const HELLO: u8 = 255;

/// How long a ping shows, ms, the last `FADE` of it fading.
pub const LIFE: i32 = 6000;
const FADE: i32 = 900;
/// The most pings one player has showing (an older one goes), and in all.
const PER_PLAYER: usize = 3;
const MOST: usize = 24;

/// The server's limits (`Player.Pings.cs`): at most `BURST` pings in `WINDOW` ms, each `GAP` ms
/// after the last.
const BURST: usize = 4;
const WINDOW: i32 = 5000;
const GAP: i32 = 250;

/// A press shorter than this, that moves less than `DRAG` pixels (times the HUD's scale), is a
/// click; longer, or further, opens the wheel.
const HOLD: i32 = 220;
const DRAG: f64 = 10.0;

/// The wheel's kinds, clockwise from the top.
const WHEEL: [u8; 5] = [LOOK, DANGER, GO, LOOT, HELP];

/// A kind's colour.
pub fn tint(kind: u8) -> u32 {
    match kind {
        DANGER => color::DANGER,
        GO => 0x5AB8FF,
        LOOT => color::GOLD,
        HELP => color::GOOD,
        _ => color::ACCENT,
    }
}

/// A kind as the player reads it.
pub fn label(kind: u8) -> &'static str {
    match kind {
        DANGER => tr("Danger"),
        GO => tr("Go here"),
        LOOT => tr("Loot"),
        HELP => tr("Help"),
        _ => tr("Look here"),
    }
}

/// The sound a kind makes.
pub fn sound(kind: u8) -> &'static str {
    if kind == DANGER {
        "ping_danger"
    } else {
        "ping"
    }
}

/// A ping that came (`PingShow`).
#[derive(Clone, Debug, PartialEq)]
pub struct Mark {
    pub from: i32,
    pub name: String,
    pub kind: u8,
    pub x: f64,
    pub y: f64,
    pub z: f64,
    /// The object it follows (0: none).
    pub target: i32,
    /// 0 someone nearby, 1 our guild, 2 our party, 3 ourselves.
    pub relation: u8,
    /// When it came (the game's clock, ms).
    pub at: i32,
}

/// The pings showing in this world.
#[derive(Default)]
pub struct Marks {
    pub list: Vec<Mark>,
    /// The server knows pings (it answered our `PingMark` 255 in this world).
    pub ready: bool,
    /// Pings that came since the last `arrived`: (kind, relation), for their sounds.
    arrived: Vec<(u8, u8)>,
    /// When our last pings went, oldest first (the server's limits).
    sent: Vec<i32>,
}

impl Marks {
    /// A new world: none of the old one's pings, and the server asked again.
    pub fn clear(&mut self) {
        self.list.clear();
        self.arrived.clear();
        self.ready = false;
    }

    /// A `PingShow`.
    pub fn show(&mut self, p: &bsk_proto::PingShow, now: i32) {
        if p.kind == HELLO {
            self.ready = true;
            return;
        }
        if p.kind >= KINDS || !(p.x.is_finite() && p.y.is_finite() && p.z.is_finite()) {
            return;
        }
        // their oldest goes when they have too many showing; the oldest of all when there are too many
        if self.list.iter().filter(|m| m.from == p.player_id).count() >= PER_PLAYER {
            if let Some(i) = self.list.iter().position(|m| m.from == p.player_id) {
                self.list.remove(i);
            }
        }
        if self.list.len() >= MOST {
            self.list.remove(0);
        }
        self.list.push(Mark {
            from: p.player_id,
            name: p.name.clone(),
            kind: p.kind,
            x: p.x as f64,
            y: p.y as f64,
            z: p.z as f64,
            target: p.target_id,
            relation: p.relation,
            at: now,
        });
        self.arrived.push((p.kind, p.relation));
    }

    /// The pings that came since last asked: (kind, relation).
    pub fn arrived(&mut self) -> Vec<(u8, u8)> {
        std::mem::take(&mut self.arrived)
    }

    /// Pings that have had their time go; the ones following an object keep to it.
    pub fn tick(&mut self, world: Option<&World>, now: i32) {
        self.list.retain(|m| now - m.at < LIFE);
        if let Some(w) = world {
            for m in &mut self.list {
                if let Some(o) = (m.target != 0).then(|| w.objects.get(&m.target)).flatten() {
                    (m.x, m.y, m.z) = (o.x, o.y, o.z);
                }
            }
        }
    }

    /// Whether we may ping now, within the server's limits; if so, it counts.
    pub fn take(&mut self, now: i32) -> bool {
        if self.sent.last().is_some_and(|&t| now - t < GAP) {
            return false;
        }
        if self.sent.len() >= BURST {
            if now - self.sent[0] < WINDOW {
                return false;
            }
            self.sent.remove(0);
        }
        self.sent.push(now);
        true
    }

    /// The pings to show: ours always, the others' as the options say.
    fn shown(&self, from: PingsFrom) -> impl Iterator<Item = &Mark> {
        self.list.iter().filter(move |m| m.relation == 3 || from.shows(m.relation))
    }

    /// In the world: each ping's ring and pin, or a pin at the view's edge pointing to it.
    pub fn draw(&self, ui: &mut Ui, cam: &crate::view::Camera, view: (f32, f32), from: PingsFrom, now: i32) {
        let k = ((cam.scale / crate::view::TILE_PX) as f32 * 1.15).max(0.75);
        for m in self.shown(from) {
            let age = now - m.at;
            let alpha = fade(age);
            let c = tint(m.kind);
            let [px, py] = cam.to_screen(m.x, m.y, m.z);
            let margin = 30.0 * k;
            // the pin stands over the spot, so it shows a little before the spot comes into view
            let on_screen = px > margin && px < view.0 - margin && py > margin + 50.0 * k && py < view.1 - margin;
            if on_screen {
                // rings spreading on the ground for the first two seconds
                if age < 2000 {
                    for n in 0..2 {
                        let phase = ((age + n * 500) % 1000) as f32 / 1000.0;
                        let r = (10.0 + 34.0 * phase) * k;
                        ellipse(ui, px, py, r + k, (5.0 * k).max(2.5), 0x000000, (1.0 - phase) * 0.35 * alpha);
                        ellipse(ui, px, py, r, (3.5 * k).max(2.0), c, (1.0 - phase) * alpha);
                    }
                }
                ellipse(ui, px, py, 11.0 * k, (4.5 * k).max(2.5), 0x000000, 0.4 * alpha);
                ellipse(ui, px, py, 10.0 * k, (3.0 * k).max(2.0), c, 0.95 * alpha);
                let dot = 3.5 * k;
                theme::fill(ui.renderer, [px - dot, py - dot * 0.6, 2.0 * dot, 1.2 * dot], dot * 0.6, c, alpha);
                // the pin: it pops in, then bobs
                let pop = 1.0 + 0.45 * (1.0 - age as f32 / 180.0).max(0.0);
                let bob = (age as f32 / 1000.0 * 4.0).sin() * 2.0 * k;
                let r = 13.0 * k * pop;
                let (bx, by) = (px, py - 48.0 * k + bob);
                let stem = [px - 1.5 * k, by + r, 3.0 * k, (py - by - r).max(0.0)];
                ui.rect(stem[0] - k, stem[1], stem[2] + 2.0 * k, stem[3], 0x000000, 0.4 * alpha);
                ui.rect(stem[0], stem[1], stem[2], stem[3], c, 0.9 * alpha);
                badge(ui, m.kind, bx, by, r, alpha);
                tag(ui, m, bx, by - r - 4.0 * k, k, alpha, true);
            } else {
                // where the line from the camera's point to the ping leaves the inset view
                let (l, t, r, b) = (margin, margin + 60.0 * k, view.0 - margin, view.1 - margin);
                let (cx, cy) = (cam.cx as f32, cam.cy as f32);
                let (dx, dy) = (px - cx, py - cy);
                let mut f = 1.0f32;
                if dx > 0.0 { f = f.min((r - cx) / dx); }
                if dx < 0.0 { f = f.min((l - cx) / dx); }
                if dy > 0.0 { f = f.min((b - cy) / dy); }
                if dy < 0.0 { f = f.min((t - cy) / dy); }
                let (ex, ey) = (cx + dx * f, cy + dy * f);
                let a = dy.atan2(dx);
                let (ca, sa) = (a.cos(), a.sin());
                let rad = 11.0 * k * (1.0 + 0.35 * (1.0 - age as f32 / 180.0).max(0.0));
                // an arrow out of the badge, toward the ping
                let tip = rad + 10.0 * k;
                let white = ui.renderer.white();
                let tri = |len: f32, wide: f32| [[ex + ca * len, ey + sa * len], [ex - sa * wide + ca * rad * 0.6, ey + ca * wide + sa * rad * 0.6], [ex + sa * wide + ca * rad * 0.6, ey - ca * wide + sa * rad * 0.6]];
                ui.renderer.triangle(white, tri(tip + 2.0 * k, 8.5 * k), [[0.5, 0.5]; 3], Tint::color(0x000000, 0.6 * alpha));
                ui.renderer.triangle(white, tri(tip, 6.5 * k), [[0.5, 0.5]; 3], Tint::color(c, alpha));
                badge(ui, m.kind, ex, ey, rad, alpha);
                // its name toward the middle of the view, out of the way of the arrow
                tag(ui, m, ex - ca * (rad + 10.0 * k), ey - sa * (rad + 6.0 * k) - if sa < -0.3 { 0.0 } else { rad }, k, alpha, false);
            }
        }
    }

    /// On the minimap: a dot in the ping's colour (at the box's edge when out of it), ringed
    /// while new.
    pub fn draw_minimap(&self, ui: &mut Ui, mini: &MiniView, s: f32, from: PingsFrom, now: i32) {
        let [x0, y0, side, _] = mini.rect;
        for m in self.shown(from) {
            let age = now - m.at;
            let alpha = fade(age);
            let c = tint(m.kind);
            let d = 4.0 * s;
            let (mx, my) = mini.to_map(m.x, m.y);
            let (px, py) = (mx.clamp(x0 + d, x0 + side - d), my.clamp(y0 + d, y0 + side - d));
            if age < 2000 && (px, py) == (mx, my) {
                let phase = (age % 1000) as f32 / 1000.0;
                let r = (5.0 + 9.0 * phase) * s;
                let r = r.min(px - x0).min(x0 + side - px).min(py - y0).min(y0 + side - py);
                if r > d {
                    ring(ui, px, py, r, r, (1.5 * s).max(1.0), c, (1.0 - phase) * alpha);
                }
            }
            theme::fill(ui.renderer, [px - d - s, py - d - s, 2.0 * (d + s), 2.0 * (d + s)], d + s, 0x000000, 0.85 * alpha);
            theme::fill(ui.renderer, [px - d, py - d, 2.0 * d, 2.0 * d], d, c, alpha);
        }
    }
}

/// How opaque a ping is at this age: whole, then fading over its last `FADE`.
fn fade(age: i32) -> f32 {
    ((LIFE - age) as f32 / FADE as f32).clamp(0.0, 1.0)
}

/// A ring on the ground: an ellipse, as the view's squashed circles are.
fn ellipse(ui: &mut Ui, x: f32, y: f32, r: f32, width: f32, c: u32, alpha: f32) {
    ring(ui, x, y, r, r * 0.6, width, c, alpha);
}

/// An outline of an ellipse (radii `rx`, `ry`), `width` thick inward, in triangles.
#[allow(clippy::too_many_arguments)]
pub fn ring(ui: &mut Ui, x: f32, y: f32, rx: f32, ry: f32, width: f32, c: u32, alpha: f32) {
    if alpha <= 0.0 || rx <= 0.0 || ry <= 0.0 {
        return;
    }
    let (ix, iy) = ((rx - width).max(0.0), (ry - width * ry / rx).max(0.0));
    let n = ((rx.max(ry) * 0.8) as usize).clamp(16, 64);
    let white = ui.renderer.white();
    let tint = Tint::color(c, alpha);
    let uv = [[0.5, 0.5]; 3];
    let at = |i: usize, a: f32, b: f32| {
        let t = i as f32 / n as f32 * std::f32::consts::TAU;
        [x + t.cos() * a, y + t.sin() * b]
    };
    for i in 0..n {
        let (o0, o1, i0, i1) = (at(i, rx, ry), at(i + 1, rx, ry), at(i, ix, iy), at(i + 1, ix, iy));
        ui.renderer.triangle(white, [o0, o1, i1], uv, tint);
        ui.renderer.triangle(white, [o0, i1, i0], uv, tint);
    }
}

/// A kind's badge: a disc in its colour, outlined, with its icon.
fn badge(ui: &mut Ui, kind: u8, x: f32, y: f32, r: f32, alpha: f32) {
    let o = r + (r / 6.0).max(1.5);
    theme::fill(ui.renderer, [x - o, y - o, 2.0 * o, 2.0 * o], o, 0x000000, 0.7 * alpha);
    theme::fill(ui.renderer, [x - r, y - r, 2.0 * r, 2.0 * r], r, tint(kind), alpha);
    icon(ui, kind, x, y, r, 0x1A0E08, alpha);
}

/// A kind's icon in a disc of radius `r`: an eye (look), ! (danger), an arrow down (go), a gem
/// (loot), a cross (help).
pub fn icon(ui: &mut Ui, kind: u8, x: f32, y: f32, r: f32, c: u32, alpha: f32) {
    let white = ui.renderer.white();
    let tint = Tint::color(c, alpha);
    let uv = [[0.5, 0.5]; 3];
    match kind {
        DANGER => {
            let w = 0.26 * r;
            theme::fill(ui.renderer, [x - w / 2.0, y - 0.62 * r, w, 0.78 * r], w / 2.0, c, alpha);
            theme::fill(ui.renderer, [x - w / 2.0, y + 0.3 * r, w, w], w / 2.0, c, alpha);
        }
        GO => {
            ui.renderer.triangle(white, [[x - 0.55 * r, y - 0.38 * r], [x + 0.55 * r, y - 0.38 * r], [x, y + 0.55 * r]], uv, tint);
        }
        LOOT => {
            ui.renderer.triangle(white, [[x - 0.55 * r, y - 0.1 * r], [x + 0.55 * r, y - 0.1 * r], [x, y + 0.62 * r]], uv, tint);
            ui.renderer.triangle(white, [[x - 0.55 * r, y - 0.1 * r], [x + 0.55 * r, y - 0.1 * r], [x + 0.3 * r, y - 0.5 * r]], uv, tint);
            ui.renderer.triangle(white, [[x - 0.55 * r, y - 0.1 * r], [x + 0.3 * r, y - 0.5 * r], [x - 0.3 * r, y - 0.5 * r]], uv, tint);
        }
        HELP => {
            let (long, w) = (1.2 * r, 0.32 * r);
            theme::fill(ui.renderer, [x - w / 2.0, y - long / 2.0, w, long], w / 3.0, c, alpha);
            theme::fill(ui.renderer, [x - long / 2.0, y - w / 2.0, long, w], w / 3.0, c, alpha);
        }
        _ => {
            // an eye: its outline, and the pupil
            ring(ui, x, y, 0.7 * r, 0.42 * r, (0.16 * r).max(1.2), c, alpha);
            let p = 0.25 * r;
            theme::fill(ui.renderer, [x - p, y - p, 2.0 * p, 2.0 * p], p, c, alpha);
        }
    }
}

/// Who sent a ping and what it says, in a dark pill whose bottom is at `y` (centred on `x`).
/// Text doesn't fade, so it goes once the ping is half faded.
fn tag(ui: &mut Ui, m: &Mark, x: f32, y: f32, k: f32, alpha: f32, kind_too: bool) {
    if alpha < 0.5 {
        return;
    }
    let (big, small) = ((12.0 * k).round().max(10.0), (10.5 * k).round().max(9.0));
    let what = label(m.kind);
    let name = if m.relation == 3 { tr("You") } else { m.name.as_str() };
    let who = match m.relation {
        3 => color::TEXT,
        2 => color::GOOD,
        1 => 0xA8D8FF,
        _ => color::TEXT_DIM,
    };
    let ww = if kind_too { ui.measure(what, big, true) } else { 0.0 };
    let nw = ui.measure(name, small, false);
    let pad = 5.0 * k;
    let w = ww.max(nw) + 2.0 * pad;
    let h = if kind_too { big * 1.22 + small * 1.22 + pad } else { small * 1.22 + pad };
    let r = [(x - w / 2.0).round(), (y - h).round(), w.round(), h.round()];
    theme::fill(ui.renderer, r, 5.0 * k, 0x000000, 0.55 * alpha);
    let mut ty = r[1] + pad / 2.0;
    if kind_too {
        ui.label(what, (x - ww / 2.0).round(), ty.round(), big, true, tint(m.kind));
        ty += big * 1.22;
    }
    ui.label(name, (x - nw / 2.0).round(), ty.round(), small, false, who);
}

/// Where a ping goes: a world point at a height, and the object there (0: none).
#[derive(Clone, Copy, Debug, PartialEq)]
struct Spot {
    x: f64,
    y: f64,
    z: f64,
    target: i32,
    /// What a click there pings.
    kind: u8,
}

/// The middle button held down: where and when, and what's there.
#[derive(Clone, Copy, Debug)]
struct Press {
    at: i32,
    pos: (f64, f64),
    spot: Spot,
}

/// The middle button, and the ping wheel.
#[derive(Default)]
pub struct Input {
    /// The button's presses and releases since the last frame (pressed, where).
    pub events: Vec<(bool, (f64, f64))>,
    press: Option<Press>,
}

/// What `Input::update` sees of the frame.
pub struct Frame<'a> {
    pub world: &'a World,
    pub data: &'a GameData,
    pub cam: &'a crate::view::Camera,
    pub mini: Option<MiniView>,
    /// Whether a point is over the HUD (not the world).
    pub on_hud: &'a dyn Fn((f64, f64)) -> bool,
    /// The HUD's scale.
    pub s: f32,
    pub now: i32,
}

impl Input {
    /// Whether the wheel is showing.
    pub fn wheel_open(&self, mouse: (f64, f64), s: f32, now: i32) -> bool {
        self.press.is_some_and(|p| wheel_due(p, mouse, s, now))
    }

    /// Let go of the button without pinging (a window opened, the world changed).
    pub fn cancel(&mut self) {
        self.press = None;
        self.events.clear();
    }

    /// The button's presses and releases this frame: the ping to send, if one is made.
    pub fn update(&mut self, f: &Frame) -> Option<bsk_proto::PingMark> {
        let mut out = None;
        for (down, pos) in std::mem::take(&mut self.events) {
            if down {
                self.press = spot_at(f, pos).map(|spot| Press { at: f.now, pos, spot });
                log::debug!("ping press at {pos:?}: {:?}", self.press.map(|p| p.spot));
                continue;
            }
            let Some(p) = self.press.take() else { continue };
            let kind = if wheel_due(p, pos, f.s, f.now) { wheel_pick(p.pos, pos, f.s) } else { Some(p.spot.kind) };
            // the wheel's kind goes where the button went down, on what was there
            if let Some(kind) = kind {
                let s = p.spot;
                out = Some(bsk_proto::PingMark { kind, x: s.x as f32, y: s.y as f32, z: s.z as f32, target_id: s.target });
            }
        }
        out
    }

    /// The wheel round where the button went down, the kind under the mouse lit.
    pub fn draw(&self, ui: &mut Ui, mouse: (f64, f64), s: f32, now: i32) {
        let Some(p) = self.press.filter(|&p| wheel_due(p, mouse, s, now)) else { return };
        let (cx, cy) = (p.pos.0 as f32, p.pos.1 as f32);
        let open = ((now - p.at - HOLD) as f32 / 120.0).clamp(0.0, 1.0).max(if moved(p, mouse, s) { 1.0 } else { 0.0 });
        let grow = 0.7 + 0.3 * open;
        let orbit = 66.0 * s * grow;
        let back = orbit + 34.0 * s;
        theme::fill(ui.renderer, [cx - back, cy - back, 2.0 * back, 2.0 * back], back, 0x000000, 0.45);
        ring(ui, cx, cy, back, back, (1.5 * s).max(1.0), color::BORDER_HI, 0.9);
        let picked = wheel_pick(p.pos, mouse, s);
        // the middle: cancel
        let hub = 16.0 * s;
        theme::fill(ui.renderer, [cx - hub, cy - hub, 2.0 * hub, 2.0 * hub], hub, if picked.is_none() { color::CHIP_HI } else { color::CHIP }, 0.95);
        let x = 5.0 * s;
        let white = ui.renderer.white();
        for (a, b) in [((-x, -x), (x, x)), ((-x, x), (x, -x))] {
            // a thin cross, two long triangles each
            let (dx, dy) = (b.0 - a.0, b.1 - a.1);
            let len = (dx * dx + dy * dy).sqrt();
            let (nx, ny) = (-dy / len * 1.2 * s, dx / len * 1.2 * s);
            let pts = [[cx + a.0 + nx, cy + a.1 + ny], [cx + b.0 + nx, cy + b.1 + ny], [cx + b.0 - nx, cy + b.1 - ny]];
            let pts2 = [[cx + a.0 + nx, cy + a.1 + ny], [cx + b.0 - nx, cy + b.1 - ny], [cx + a.0 - nx, cy + a.1 - ny]];
            ui.renderer.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(color::TEXT_DIM, 0.9));
            ui.renderer.triangle(white, pts2, [[0.5, 0.5]; 3], Tint::color(color::TEXT_DIM, 0.9));
        }
        let size = (11.5 * s).round().max(9.0);
        for (i, &kind) in WHEEL.iter().enumerate() {
            let a = wheel_angle(i);
            let (bx, by) = (cx + a.cos() * orbit, cy + a.sin() * orbit);
            let lit = picked == Some(kind);
            let r = if lit { 21.0 * s } else { 17.0 * s } * grow;
            let o = r + 2.0 * s;
            theme::fill(ui.renderer, [bx - o, by - o, 2.0 * o, 2.0 * o], o, if lit { 0xFFFFFF } else { 0x000000 }, if lit { 0.9 } else { 0.7 });
            if lit {
                theme::fill(ui.renderer, [bx - r, by - r, 2.0 * r, 2.0 * r], r, tint(kind), 1.0);
                icon(ui, kind, bx, by, r, 0x1A0E08, 1.0);
            } else {
                theme::fill(ui.renderer, [bx - r, by - r, 2.0 * r, 2.0 * r], r, color::PANEL_TOP, 0.95);
                icon(ui, kind, bx, by, r, tint(kind), 1.0);
            }
            if open >= 1.0 {
                let text = label(kind);
                let w = ui.measure(text, size, lit);
                // under the bubble, or over it for the top one
                let ty = if a.sin() < -0.9 { by - o - size * 1.3 } else { by + o + 2.0 * s };
                ui.label(text, (bx - w / 2.0).round(), ty.round(), size, lit, if lit { tint(kind) } else { color::TEXT });
            }
        }
    }
}

/// Whether the press has been held, or dragged, long enough for the wheel.
fn wheel_due(p: Press, mouse: (f64, f64), s: f32, now: i32) -> bool {
    now - p.at >= HOLD || moved(p, mouse, s)
}

fn moved(p: Press, mouse: (f64, f64), s: f32) -> bool {
    (mouse.0 - p.pos.0).hypot(mouse.1 - p.pos.1) >= DRAG * s as f64
}

/// The screen angle of the wheel's `i`th kind, clockwise from the top.
fn wheel_angle(i: usize) -> f32 {
    -std::f32::consts::FRAC_PI_2 + i as f32 * std::f32::consts::TAU / WHEEL.len() as f32
}

/// The wheel's kind the mouse points at from its middle (none in the middle: cancel).
fn wheel_pick(centre: (f64, f64), mouse: (f64, f64), s: f32) -> Option<u8> {
    let (dx, dy) = ((mouse.0 - centre.0) as f32, (mouse.1 - centre.1) as f32);
    if dx.hypot(dy) < 22.0 * s {
        return None;
    }
    let a = dy.atan2(dx);
    let off = |i: usize| {
        let d = (a - wheel_angle(i)).rem_euclid(std::f32::consts::TAU);
        d.min(std::f32::consts::TAU - d)
    };
    (0..WHEEL.len()).min_by(|&i, &j| off(i).total_cmp(&off(j))).map(|i| WHEEL[i])
}

/// What a press at `pos` would ping: on the minimap the spot it shows (Go here); over the world
/// what's under the mouse, or the ground; over the rest of the HUD nothing.
fn spot_at(f: &Frame, pos: (f64, f64)) -> Option<Spot> {
    if let Some(mini) = f.mini.filter(|m| inside(m.rect, pos)) {
        let (x, y) = mini.from_map(pos.0 as f32, pos.1 as f32);
        let (w, h) = (f.world.width as f64, f.world.height as f64);
        return Some(Spot { x: x.clamp(0.0, w - 0.01), y: y.clamp(0.0, h - 0.01), z: f.cam.z, target: 0, kind: GO });
    }
    if (f.on_hud)(pos) {
        return None;
    }
    if let Some((target, kind)) = object_under(f, pos) {
        let o = &f.world.objects[&target];
        return Some(Spot { x: o.x, y: o.y, z: o.z, target, kind });
    }
    let (x, y) = f.cam.to_world(pos.0, pos.1);
    Some(Spot { x, y, z: f.cam.z, target: 0, kind: LOOK })
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// What a click on an object pings, if it's something to ping: an enemy, a loot bag, a portal, a
/// player (ourselves: Help).
pub fn kind_of(data: &GameData, world: &World, id: i32, object_type: u16) -> Option<u8> {
    let d = data.objects.get(&object_type)?;
    if Some(id) == world.me {
        Some(HELP)
    } else if d.enemy && d.class == ObjectClass::Character {
        Some(DANGER)
    } else if d.class == ObjectClass::Container && d.xml.has("Loot") {
        Some(LOOT)
    } else if matches!(d.class, ObjectClass::Portal | ObjectClass::GuildHallPortal) {
        Some(GO)
    } else if d.player {
        Some(LOOK)
    } else {
        None
    }
}

/// The object to ping under the mouse: the nearest whose sprite the mouse is on, about.
fn object_under(f: &Frame, pos: (f64, f64)) -> Option<(i32, u8)> {
    let scale = f.cam.scale;
    let mut best: Option<(i32, u8, f64)> = None;
    for o in f.world.objects.values() {
        let Some(kind) = kind_of(f.data, f.world, o.id, o.object_type) else { continue };
        let [sx, sy] = f.cam.to_screen(o.x, o.y, o.z);
        // a sprite stands on its spot, its middle about half its height up
        let size = (o.stat_int(2).unwrap_or(100) as f64 / 100.0).clamp(0.5, 3.0);
        let h = 0.8 * scale * size;
        let (cx, cy) = (sx as f64, sy as f64 - h / 2.0);
        let r = (h * 0.6).max(0.45 * scale);
        let d2 = (pos.0 - cx).powi(2) + (pos.1 - cy).powi(2);
        if d2 <= r * r && best.is_none_or(|b| d2 < b.2) {
            best = Some((o.id, kind, d2));
        }
    }
    best.map(|(id, kind, _)| (id, kind))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn show(kind: u8, player_id: i32) -> bsk_proto::PingShow {
        bsk_proto::PingShow { player_id, name: "Tester".into(), kind, x: 10.0, y: 12.0, z: 0.0, target_id: 0, relation: 0 }
    }

    #[test]
    fn hello_says_ready() {
        let mut m = Marks::default();
        assert!(!m.ready);
        m.show(&show(HELLO, 1), 0);
        assert!(m.ready && m.list.is_empty());
        m.clear();
        assert!(!m.ready);
    }

    #[test]
    fn marks_keep_to_their_limits() {
        let mut m = Marks::default();
        for i in 0..5 {
            m.show(&show(LOOK, 7), i * 10);
        }
        // a player keeps their newest few
        assert_eq!(m.list.len(), PER_PLAYER);
        assert_eq!(m.list[0].at, 20);
        assert_eq!(m.arrived().len(), 5);
        assert!(m.arrived().is_empty());
        // unknown kinds are left out
        m.show(&show(KINDS, 8), 0);
        assert_eq!(m.list.len(), PER_PLAYER);
        // and each goes in its time
        m.tick(None, 20 + LIFE);
        assert_eq!(m.list.len(), 2);
    }

    #[test]
    fn sending_keeps_to_the_servers_limits() {
        let mut m = Marks::default();
        assert!(m.take(1000));
        assert!(!m.take(1000 + GAP - 1));
        let mut t = 1000 + GAP;
        for _ in 1..BURST {
            assert!(m.take(t));
            t += GAP;
        }
        assert!(!m.take(t), "the burst is spent");
        assert!(m.take(1000 + WINDOW), "the first is a window old");
    }

    #[test]
    fn the_wheel_picks_by_direction() {
        let c = (100.0, 100.0);
        assert_eq!(wheel_pick(c, (105.0, 100.0), 1.0), None, "the middle cancels");
        assert_eq!(wheel_pick(c, (100.0, 40.0), 1.0), Some(WHEEL[0]), "up");
        assert_eq!(wheel_pick(c, (160.0, 80.0), 1.0), Some(WHEEL[1]), "upper right");
        assert_eq!(wheel_pick(c, (130.0, 160.0), 1.0), Some(WHEEL[2]), "lower right");
        assert_eq!(wheel_pick(c, (70.0, 160.0), 1.0), Some(WHEEL[3]), "lower left");
        assert_eq!(wheel_pick(c, (40.0, 80.0), 1.0), Some(WHEEL[4]), "upper left");
    }

    #[test]
    fn a_quick_click_is_no_wheel() {
        let p = Press { at: 0, pos: (50.0, 50.0), spot: Spot { x: 0.0, y: 0.0, z: 0.0, target: 0, kind: LOOK } };
        assert!(!wheel_due(p, (52.0, 51.0), 1.0, HOLD - 1));
        assert!(wheel_due(p, (52.0, 51.0), 1.0, HOLD));
        assert!(wheel_due(p, (70.0, 50.0), 1.0, 10), "dragged");
    }
}
