//! The realm's events list (`RealmEvents`), at the top left under the quest's health (and, near
//! it, who hit it): how many events are defeated of the number that closes the realm, then, while
//! we're far from the quest (`hud::QUEST_NEAR`; near it the list folds to its count, and a click on
//! the header shows it anyway), each running event, the side
//! quests (the Lost Sentry, light blue) nearest first, then the one Realm 2 waystone nearest the
//! quest, five rows at a time (the wheel scrolls the rest). A click on an event points the quest
//! arrow at it (again: back to the nearest, as before), a click on the waystone teleports there,
//! and the header folds the list away. The minimap marks them all (`draw_minimap_marks`), and a
//! click on any waystone there teleports to it (`minimap_click`).

use bsk_proto::{RealmEventEntry, RealmEvents};

use crate::hud::{self, Layout, Ui};
use crate::theme::{self, color};

/// The list's width (the boss's box's), its header and a row, HUD units; rows shown at once.
const WIDTH: f32 = 250.0;
const HEADER: f32 = 34.0;
const ROW: f32 = 22.0;
const ROWS: usize = 5;

/// An event's colours on the list and the minimap: events in the ember accent, side quests light
/// blue, waystones cyan.
pub const EVENT_COLOR: u32 = 0xFF7A3D;
pub const SIDE_COLOR: u32 = 0x8FD8FF;
pub const BEACON_COLOR: u32 = 0x5CF2E6;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Act {
    /// Point the quest arrow at this event (0: the nearest again).
    Pick(i32),
    /// Teleport to this waystone.
    Teleport(i32),
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Part {
    Header,
    Entry(usize),
}

pub struct EventsPanel {
    /// The list is unfolded (the header always shows).
    pub open: bool,
    /// We're near the quest, so the list folds itself; `peek`: a click on the header shows it
    /// anyway (until we're away from the quest again).
    near: bool,
    peek: bool,
    /// The first row shown.
    scroll: usize,
    /// Where it was last drawn (its top), if it was: the clicks go by it; and its box, for the wheel.
    drawn_at: Option<f32>,
    drawn_box: [f32; 4],
    /// The entries shown, in order (indexes into them): the events and side quests nearest
    /// first, then the waystone nearest the quest, as last drawn.
    order: Vec<usize>,
}

impl Default for EventsPanel {
    fn default() -> Self {
        EventsPanel { open: true, near: false, peek: false, scroll: 0, drawn_at: None, drawn_box: [0.0; 4], order: Vec::new() }
    }
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

impl EventsPanel {
    fn parts(&self, lay: &Layout, y0: f32) -> Vec<(Part, [f32; 4])> {
        let s = lay.s;
        let w = WIDTH * s;
        let mut out = vec![(Part::Header, [0.0, y0, w, HEADER * s])];
        if self.unfolded() {
            let n = self.order.len();
            for (k, &i) in self.order.iter().skip(self.scroll.min(n)).take(ROWS).enumerate() {
                out.push((Part::Entry(i), [0.0, y0 + (HEADER + 2.0 + k as f32 * ROW) * s, w, ROW * s]));
            }
        }
        out
    }

    /// Whether the rows show: unfolded, and away from the quest unless peeked at.
    fn unfolded(&self) -> bool {
        if self.near { self.peek } else { self.open }
    }

    /// The whole list's box.
    fn body(&self, lay: &Layout, y0: f32) -> [f32; 4] {
        let parts = self.parts(lay, y0);
        let (first, last) = (parts[0].1, parts[parts.len() - 1].1);
        [first[0], first[1], first[2], last[1] + last[3] - first[1] + if parts.len() > 1 { 4.0 * lay.s } else { 0.0 }]
    }

    /// Not drawn this frame (we're near the quest, or not in a realm): no clicks or wheel.
    pub fn hide(&mut self) {
        self.drawn_at = None;
    }

    /// A click on the list: whether the list took it, and what it asks for (none when it only
    /// folded it).
    pub fn click(&mut self, lay: &Layout, info: Option<&RealmEvents>, pos: (f64, f64)) -> (bool, Option<Act>) {
        let (Some(info), Some(y0)) = (info, self.drawn_at) else { return (false, None) };
        let hit = self.parts(lay, y0).into_iter().find(|(_, r)| inside(*r, pos));
        match hit {
            Some((Part::Header, _)) => {
                if self.near {
                    self.peek = !self.peek;
                } else {
                    self.open = !self.open;
                }
                (true, None)
            }
            Some((Part::Entry(i), _)) => {
                let e = &info.entries.0[i];
                let act = match e.kind {
                    2 => Act::Teleport(e.object_id),
                    _ if info.picked == e.object_id => Act::Pick(0),
                    _ => Act::Pick(e.object_id),
                };
                (true, Some(act))
            }
            None => (false, None),
        }
    }

    /// The wheel over the list scrolls it a row a notch; whether it was over the list (0 notches
    /// only asks).
    pub fn wheel(&mut self, info: Option<&RealmEvents>, pos: (f64, f64), notches: i32) -> bool {
        if info.is_none() {
            return false;
        }
        if self.drawn_at.is_none() || !inside(self.drawn_box, pos) {
            return false;
        }
        let max = self.order.len().saturating_sub(ROWS) as i32;
        self.scroll = (self.scroll as i32 - notches).clamp(0, max) as usize;
        true
    }

    /// Draws the list from `y0` down; returns how far down it reaches. `quest`: what the quest
    /// arrow follows now, marked even when the server picked it (the nearest).
    #[allow(clippy::too_many_arguments)]
    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, y0: f32, near: bool, me: Option<(f64, f64)>, quest: Option<i32>, info: &RealmEvents, mouse: (f64, f64)) -> f32 {
        let s = lay.s;
        if near != self.near {
            self.near = near;
            self.peek = false;
        }
        self.order = shown(info, me, quest);
        self.scroll = self.scroll.min(self.order.len().saturating_sub(ROWS));
        self.drawn_at = Some(y0);
        let parts = self.parts(lay, y0);
        let body = self.body(lay, y0);
        self.drawn_box = body;
        hud::hud_box(ui, body, s);
        for (part, r) in &parts {
            let over = inside(*r, mouse);
            match part {
                Part::Header => {
                    // "Realm Events 3 / 13", a bar filling toward the close, and the fold's sign
                    let size = lay.font(12.0);
                    ui.label("Realm Events", r[0] + 8.0 * s, r[1] + 5.0 * s, size, true, if over { color::TEXT } else { color::ACCENT_HI });
                    let count = format!("{} / {}", info.defeated, info.needed);
                    let cw = ui.measure(&count, size, true);
                    ui.label(&count, r[0] + r[2] - cw - 22.0 * s, r[1] + 5.0 * s, size, true, color::TEXT);
                    ui.label(if self.unfolded() { "-" } else { "+" }, r[0] + r[2] - 14.0 * s, r[1] + 4.0 * s, size, true, color::TEXT_DIM);
                    let bar = [r[0] + 8.0 * s, r[1] + 22.0 * s, r[2] - 16.0 * s, 6.0 * s];
                    theme::fill(ui.renderer, bar, 3.0 * s, color::TRACK, 1.0);
                    let frac = if info.needed > 0 { (info.defeated as f32 / info.needed as f32).clamp(0.0, 1.0) } else { 0.0 };
                    if frac > 0.0 {
                        theme::fill(ui.renderer, [bar[0], bar[1], bar[2] * frac, bar[3]], 3.0 * s, color::ACCENT, 1.0);
                    }
                }
                Part::Entry(i) => {
                    let e: &RealmEventEntry = &info.entries.0[*i];
                    let picked = e.kind != 2 && info.picked == e.object_id;
                    let followed = e.kind != 2 && quest == Some(e.object_id);
                    let tint = match e.kind {
                        1 => SIDE_COLOR,
                        2 => BEACON_COLOR,
                        _ => EVENT_COLOR,
                    };
                    if picked || followed || over {
                        theme::fill(ui.renderer, [r[0] + 4.0 * s, r[1], r[2] - 14.0 * s, r[3]], 4.0 * s, tint, if picked { 0.26 } else if followed { 0.14 } else { 0.1 });
                    }
                    theme::fill(ui.renderer, [r[0] + 6.0 * s, r[1] + 4.0 * s, 3.0 * s, r[3] - 8.0 * s], s, tint, 1.0);
                    hud::object_icon(ui, e.object_type, r[0] + 22.0 * s, r[1] + r[3] / 2.0, 18.0 * s);
                    let size = lay.font(11.0);
                    let mut name = e.name.clone();
                    let room = r[2] - 106.0 * s;
                    if ui.measure(&name, size, false) > room {
                        while name.chars().count() > 3 && ui.measure(&format!("{name}…"), size, false) > room {
                            name.pop();
                        }
                        name.push('…');
                    }
                    let strong = picked || followed;
                    ui.label(&name, r[0] + 34.0 * s, r[1] + 4.0 * s, size, strong, if strong { color::TEXT } else { color::TEXT_DIM });
                    // how far, or what a click does
                    let right = if e.kind == 2 {
                        "Teleport".to_owned()
                    } else if let Some((x, y)) = me {
                        format!("{:.0} tiles", (e.x as f64 - x).hypot(e.y as f64 - y))
                    } else {
                        String::new()
                    };
                    let rw = ui.measure(&right, size, false);
                    ui.label(&right, r[0] + r[2] - rw - 14.0 * s, r[1] + 4.0 * s, size, false, if e.kind == 2 && over { color::TEXT } else { tint });
                }
            }
        }
        // the scroll bar, when there are more than five
        let n = self.order.len();
        if self.unfolded() && n > ROWS {
            let top = y0 + (HEADER + 2.0) * s;
            let track = [body[0] + body[2] - 7.0 * s, top, 3.0 * s, ROWS as f32 * ROW * s];
            theme::fill(ui.renderer, track, 1.5 * s, 0x000000, 0.5);
            let h = track[3] * ROWS as f32 / n as f32;
            let at = (track[3] - h) * self.scroll as f32 / (n - ROWS) as f32;
            theme::fill(ui.renderer, [track[0], track[1] + at, track[2], h], 1.5 * s, color::ACCENT, 0.9);
        }
        body[1] + body[3]
    }
}

/// The entries the list shows, in order: the events and side quests nearest us first, then the
/// one waystone nearest the quest our arrow follows (or us, with no quest among them). The minimap
/// shows every waystone, to teleport to any.
fn shown(info: &RealmEvents, me: Option<(f64, f64)>, quest: Option<i32>) -> Vec<usize> {
    let entries = &info.entries.0;
    let dist = |e: &RealmEventEntry, from: Option<(f64, f64)>| from.map_or(0.0, |(x, y)| (e.x as f64 - x).hypot(e.y as f64 - y));
    let mut order: Vec<usize> = (0..entries.len()).filter(|&i| entries[i].kind != 2).collect();
    order.sort_by(|&a, &b| dist(&entries[a], me).total_cmp(&dist(&entries[b], me)));
    let aim = quest
        .and_then(|q| entries.iter().find(|e| e.kind != 2 && e.object_id == q))
        .map(|e| (e.x as f64, e.y as f64))
        .or(me);
    let waystone = (0..entries.len()).filter(|&i| entries[i].kind == 2).min_by(|&a, &b| dist(&entries[a], aim).total_cmp(&dist(&entries[b], aim)));
    order.extend(waystone);
    order
}

/// Where an entry's mark is on the minimap (at the box's edge when it's out of it), and its half
/// size.
fn mark_at(s: f32, mini: &hud::MiniView, e: &RealmEventEntry) -> (f32, f32, f32) {
    let [x0, y0, side, _] = mini.rect;
    let (px, py) = mini.to_map(e.x as f64, e.y as f64);
    let d = if e.kind == 2 { 4.5 * s } else { 3.5 * s };
    (px.clamp(x0 + d, x0 + side - d), py.clamp(y0 + d, y0 + side - d), d)
}

/// The waystone whose minimap mark is under `pos`, the nearest the mouse.
fn waystone_at(s: f32, mini: &hud::MiniView, info: &RealmEvents, pos: (f64, f64)) -> Option<usize> {
    let (mx, my) = (pos.0 as f32, pos.1 as f32);
    let entries = &info.entries.0;
    (0..entries.len())
        .filter(|&i| entries[i].kind == 2)
        .map(|i| {
            let (px, py, d) = mark_at(s, mini, &entries[i]);
            (i, (px - mx).abs().max((py - my).abs()), d)
        })
        .filter(|(_, off, d)| *off <= d + 2.0 * s)
        .min_by(|a, b| a.1.total_cmp(&b.1))
        .map(|(i, _, _)| i)
}

/// The events on the minimap: a square in the event's colour, ringed, at the box's edge when
/// they're out of it; the side quests' stay however far away they are. The waystones are diamonds,
/// lit and named under the mouse, which can teleport to them (`minimap_click`).
pub fn draw_minimap_marks(ui: &mut Ui, s: f32, mini: &hud::MiniView, info: Option<&RealmEvents>, mouse: (f64, f64)) {
    let Some(info) = info else { return };
    let hover = waystone_at(s, mini, info, mouse);
    for (i, e) in info.entries.0.iter().enumerate() {
        let (px, py, d) = mark_at(s, mini, e);
        if e.kind == 2 {
            // a diamond, rows narrowing to its points, ringed
            let lit = hover == Some(i);
            let (ring, c) = if lit { (0xFFFFFF, 0xD8FFFB) } else { (0x000000, BEACON_COLOR) };
            let step = s.max(1.0);
            for (half, colour, alpha) in [(d + step, ring, if lit { 1.0 } else { 0.8 }), (d, c, 1.0)] {
                let mut y = -half;
                while y < half {
                    let w = half - (y + step / 2.0).abs();
                    if w > 0.0 {
                        ui.rect(px - w, py + y, 2.0 * w, step, colour, alpha);
                    }
                    y += step;
                }
            }
            continue;
        }
        let c = if e.kind == 1 { SIDE_COLOR } else { EVENT_COLOR };
        ui.rect(px - d - s, py - d - s, 2.0 * (d + s), 2.0 * (d + s), 0x000000, 0.8);
        ui.rect(px - d, py - d, 2.0 * d, 2.0 * d, c, 1.0);
    }
    // the waystone under the mouse: its name and what a click does, under its mark
    if let Some(i) = hover {
        let e = &info.entries.0[i];
        let (px, py, d) = mark_at(s, mini, e);
        let text = format!("{}: click to teleport", e.name);
        let size = 11.0 * s;
        let w = ui.measure(&text, size, true) + 12.0 * s;
        let [x0, _, side, _] = mini.rect;
        let r = [(px - w / 2.0).clamp(x0 + side - w.max(side), x0 + side - w), py + d + 4.0 * s, w, 18.0 * s];
        theme::fill(ui.renderer, r, 4.0 * s, 0x000000, 0.85);
        ui.label(&text, r[0] + 6.0 * s, r[1] + 3.0 * s, size, true, BEACON_COLOR);
    }
}

/// A click on the minimap: the waystone under it, to teleport to.
pub fn minimap_click(s: f32, mini: &hud::MiniView, info: Option<&RealmEvents>, pos: (f64, f64)) -> Option<i32> {
    let info = info?;
    waystone_at(s, mini, info, pos).map(|i| info.entries.0[i].object_id)
}
