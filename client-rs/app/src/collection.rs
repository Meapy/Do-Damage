//! The Collection (the world server's `Player.Collection.cs`), opened at the Collection stand in
//! the vault: every piece of gear there is to collect, the ones the account has had in colour and
//! the rest dark. Two views: "All", every piece by kind (an outline of each kind filters to it),
//! and "Dungeons", the untiered gear by where it drops (hardest first), the realm's and the Divine
//! Forge's after them; a piece from two places shows in both. Hovering a piece shows its tooltip.
//!
//! The client asks once each time it opens (`CollectionRequest`); the list itself comes from the
//! server (`tools/build_collection.py`), so new gear needs no client release.

use crate::hud::{Layout, SlotKind, Ui};
use crate::i18n::{tf, tr};
use crate::theme::{self, color, Kind};
use bsk_render::Tint;
use bsk_world::GameObj;
use std::collections::HashSet;
use winit::keyboard::KeyCode;

/// How long to wait for the server before saying it can't show the Collection (ms).
const NO_ANSWER: i32 = 5000;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum View {
    #[default]
    All,
    Dungeons,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Close,
    View(View),
    Slot(Option<i32>),
    Up,
    Down,
}

/// The Collection window.
#[derive(Default)]
pub struct Collection {
    pub open: bool,
    data: Option<bsk_proto::Collection>,
    have: HashSet<u16>,
    view: View,
    /// The kind "All" shows (a slot type), or every kind.
    slot: Option<i32>,
    /// The slot types in the list, each with a piece to draw its outline from.
    slots: Vec<(i32, u16)>,
    /// The first row (line) drawn, how many fit, and how many there are.
    row: usize,
    rows: usize,
    total: usize,
    asked: Option<i32>,
    unsupported: bool,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
    hover: Option<u16>,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

fn slot_of(data: &bsk_data::GameData, item: u16) -> i32 {
    data.objects.get(&item).map_or(0, |d| d.xml.int("SlotType", 0))
}

impl Collection {
    /// Opens the window; true when the server should be asked (`CollectionRequest`).
    pub fn show(&mut self) -> bool {
        self.open = true;
        self.row = 0;
        if self.unsupported {
            return false;
        }
        // timed from the next draw (`tick`)
        self.asked = Some(i32::MIN);
        true
    }

    /// A new connection: forget it all but the view.
    pub fn reset(&mut self) {
        *self = Collection { view: self.view, ..Collection::default() };
    }

    /// The server's answer.
    pub fn contents(&mut self, c: bsk_proto::Collection, data: &bsk_data::GameData) {
        self.asked = None;
        self.have = c.have.iter().copied().collect();
        self.slots.clear();
        for &item in c.all.iter() {
            let slot = slot_of(data, item);
            if !self.slots.iter().any(|&(s, _)| s == slot) {
                self.slots.push((slot, item));
            }
        }
        log::info!("collection: {} of {} pieces, {} places", self.have.len(), c.all.len(), c.groups.len());
        self.data = Some(c);
    }

    pub fn tick(&mut self, now: i32) {
        if self.asked == Some(i32::MIN) {
            self.asked = Some(now);
        }
        if self.asked.is_some_and(|t| now - t > NO_ANSWER) {
            self.asked = None;
            self.unsupported = true;
        }
    }

    /// A press of the left button: outside the window closes it.
    pub fn click(&mut self, pos: (f64, f64)) {
        match self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h) {
            Some(Hit::Close) => self.open = false,
            Some(Hit::View(v)) => {
                self.view = v;
                self.row = 0;
            }
            Some(Hit::Slot(s)) => {
                self.slot = s;
                self.row = 0;
            }
            Some(Hit::Up) => self.scroll(-(self.rows.max(1) as i32)),
            Some(Hit::Down) => self.scroll(self.rows.max(1) as i32),
            None => {
                if self.rect.is_some_and(|r| !inside(r, pos)) {
                    self.open = false;
                }
            }
        }
    }

    pub fn key(&mut self, code: KeyCode) {
        match code {
            KeyCode::Escape => self.open = false,
            KeyCode::PageUp | KeyCode::ArrowUp => self.scroll(-1),
            KeyCode::PageDown | KeyCode::ArrowDown => self.scroll(1),
            _ => {}
        }
    }

    pub fn scroll(&mut self, rows: i32) {
        let max = self.total.saturating_sub(self.rows.max(1));
        self.row = (self.row as i32 + rows).clamp(0, max as i32) as usize;
    }

    fn chip(&mut self, ui: &mut Ui, r: [f32; 4], label: &str, on: bool, mouse: (f64, f64), size: f32, hit: Hit) {
        let fill = if on { color::ACCENT } else if inside(r, mouse) { color::SECONDARY } else { color::CHIP };
        theme::fill(ui.renderer, r, r[3] / 2.0, fill, 1.0);
        let lw = ui.measure(label, size, true);
        ui.label(label, r[0] + (r[2] - lw) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
        self.hits.push((hit, r));
    }

    /// A piece in its slot: in colour if collected, dark if not.
    fn piece(&mut self, ui: &mut Ui, sr: [f32; 4], item: u16, mouse: (f64, f64), s: f32) {
        let got = self.have.contains(&item);
        let over = inside(sr, mouse);
        let edge = if got { crate::screens::tier_edge(ui.data, item as i32) } else { None };
        ui.slot_box(sr, edge, over, s);
        let (cx, cy) = (sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0);
        if got {
            ui.item_icon(item as i32, 80, cx, cy, s * sr[2] / (40.0 * s));
        } else {
            ui.item_icon_tinted(item as i32, 80, cx, cy, s * sr[2] / (40.0 * s), Tint { mul: [0.16, 0.15, 0.2, 0.8], add: [0.0; 3] });
        }
        if over {
            self.hover = Some(item);
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.hits.clear();
        self.hover = None;
        if !self.open {
            self.rect = None;
            return;
        }
        self.tick(now);
        let (dw, dh) = (860.0, 600.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);

        // the title, the views, how many are collected, the close button
        let mid = r[1] + 32.0 * s;
        let ts = font(22.0);
        ui.label(tr("Collection"), r[0] + 22.0 * s, (mid - ui.digit_middle(ts, true)).round(), ts, true, color::GOLD);
        let views = [(View::All, tr("All")), (View::Dungeons, tr("Dungeons"))];
        let cs = font(13.0);
        let widths: Vec<f32> = views.iter().map(|(_, l)| ui.measure(l, cs, true) + 36.0 * s).collect();
        let gap = 8.0 * s;
        let mut x = (r[0] + (r[2] - widths.iter().sum::<f32>() - gap) / 2.0).round();
        for (&(v, label), w) in views.iter().zip(widths) {
            let vr = [x, (mid - 14.0 * s).round(), w, 28.0 * s];
            self.chip(ui, vr, label, v == self.view, mouse, cs, Hit::View(v));
            x += w + gap;
        }
        let cb = [r[0] + r[2] - 42.0 * s, (mid - 14.0 * s).round(), 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));
        let Some(data) = self.data.take() else {
            let msg = if self.unsupported { tr("This server can't show the Collection yet.") } else { tr("Asking for your collection...") };
            ui.label(msg, r[0] + 22.0 * s, r[1] + 70.0 * s, font(13.0), false, color::TEXT_MUTED);
            return;
        };
        let count = tf!("{have} of {all} collected", have = self.have.len(), all = data.all.len());
        let cw = ui.measure(&count, font(13.0), true);
        ui.label(&count, cb[0] - 14.0 * s - cw, (mid - ui.digit_middle(font(13.0), true)).round(), font(13.0), true, color::TEXT_DIM);

        let body = [r[0] + 22.0 * s, r[1] + 62.0 * s, r[2] - 44.0 * s, r[3] - 104.0 * s];
        match self.view {
            View::All => self.draw_all(ui, &data, body, s, mouse),
            View::Dungeons => self.draw_places(ui, &data, body, s, mouse),
        }
        // the paging
        let by = r[1] + r[3] - 36.0 * s;
        if self.total > self.rows {
            let (up, down) = ([body[0], by, 56.0 * s, 26.0 * s], [body[0] + 62.0 * s, by, 56.0 * s, 26.0 * s]);
            ui.button(up, tr("Up"), Kind::Secondary, inside(up, mouse), s * 0.8);
            ui.button(down, tr("Down"), Kind::Secondary, inside(down, mouse), s * 0.8);
            self.hits.push((Hit::Up, up));
            self.hits.push((Hit::Down, down));
            let page = tf!("Rows {from}-{to} of {all}", from = self.row + 1, to = (self.row + self.rows).min(self.total), all = self.total);
            ui.label(&page, body[0] + 130.0 * s, by + 5.0 * s, font(12.0), false, color::TEXT_MUTED);
        }
        if let Some(item) = self.hover {
            let mut lines = crate::hud::item_tooltip_imprinted(ui.data, item as i32, me, SlotKind::Shop, [0; 3]);
            lines.push(crate::hud::Line::gap());
            if self.have.contains(&item) {
                lines.push(crate::hud::Line::new(tr("Collected"), 12.0, true, color::GOOD));
            } else {
                lines.push(crate::hud::Line::new(tr("Not collected yet"), 12.0, true, color::TEXT_MUTED));
            }
            let places: Vec<&str> = data.groups.iter().filter(|g| g.items.contains(&item)).map(|g| g.name.as_str()).take(4).collect();
            if !places.is_empty() {
                lines.push(crate::hud::Line::new(&places.join(", "), 12.0, false, color::TEXT_DIM));
            }
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item as i32), crate::screens::tier_edge(ui.data, item as i32), mouse);
        }
        self.data = Some(data);
    }

    /// Every piece, by kind: the kinds' outlines along the top, the pieces under them.
    fn draw_all(&mut self, ui: &mut Ui, data: &bsk_proto::Collection, body: [f32; 4], s: f32, mouse: (f64, f64)) {
        let size = (12.0 * s).round().max(9.0);
        let (h, gap) = (34.0 * s, 6.0 * s);
        let aw = ui.measure(tr("All"), size, true) + 24.0 * s;
        let mut x = body[0];
        let mut y = body[1];
        self.chip(ui, [x, y, aw, h], tr("All"), self.slot.is_none(), mouse, size, Hit::Slot(None));
        x += aw + gap;
        for (slot, item) in self.slots.clone() {
            if x + h > body[0] + body[2] {
                x = body[0];
                y += h + gap;
            }
            let cr = [x, y, h, h];
            let on = self.slot == Some(slot);
            let over = inside(cr, mouse);
            theme::fill(ui.renderer, cr, 8.0 * s, if on { color::ACCENT } else if over { color::SECONDARY } else { color::CHIP }, 1.0);
            // the kind's outline, in the window's colours
            let rgb = bsk_render::rgb_f32(if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
            ui.item_icon_tinted(item as i32, 80, cr[0] + h / 2.0, cr[1] + h / 2.0, s * 0.7, Tint { mul: [0.0, 0.0, 0.0, 1.0], add: rgb });
            if over {
                let name = crate::forge::slot_name(slot);
                let nw = ui.measure(name, size, true);
                let tip = [cr[0] + h / 2.0 - nw / 2.0 - 6.0 * s, cr[1] - 22.0 * s, nw + 12.0 * s, 20.0 * s];
                theme::fill(ui.renderer, tip, 5.0 * s, color::CHIP, 0.96);
                ui.label(name, tip[0] + 6.0 * s, tip[1] + 2.0 * s, size, true, color::TEXT);
            }
            self.hits.push((Hit::Slot(Some(slot)), cr));
            x += h + gap;
        }
        let gy = y + h + 12.0 * s;
        let items: Vec<u16> = data.all.iter().copied().filter(|&i| self.slot.map_or(true, |s| slot_of(ui.data, i) == s)).collect();
        let (cell, cgap) = (42.0 * s, 6.0 * s);
        let cols = (((body[2] + cgap) / (cell + cgap)).floor() as usize).max(1);
        self.rows = (((body[1] + body[3] - gy + cgap) / (cell + cgap)).floor() as usize).max(1);
        self.total = items.len().div_ceil(cols);
        self.scroll(0);
        for (n, &item) in items.iter().skip(self.row * cols).take(cols * self.rows).enumerate() {
            let sr = [body[0] + (n % cols) as f32 * (cell + cgap), gy + (n / cols) as f32 * (cell + cgap), cell, cell];
            self.piece(ui, sr, item, mouse, s);
        }
    }

    /// The untiered pieces by place: a line for each, its name and how many are collected on the
    /// left, its pieces on the right (more lines when they don't fit).
    fn draw_places(&mut self, ui: &mut Ui, data: &bsk_proto::Collection, body: [f32; 4], s: f32, mouse: (f64, f64)) {
        let (cell, cgap) = (34.0 * s, 4.0 * s);
        let name_w = 250.0 * s;
        let cols = (((body[2] - name_w + cgap) / (cell + cgap)).floor() as usize).max(1);
        // the lines: (group, the first of its pieces on this line)
        let mut lines: Vec<(usize, usize)> = Vec::new();
        for (g, group) in data.groups.iter().enumerate() {
            for first in (0..group.items.len().max(1)).step_by(cols) {
                lines.push((g, first));
            }
        }
        let line_h = cell + cgap;
        self.rows = ((body[3] / line_h).floor() as usize).max(1);
        self.total = lines.len();
        self.scroll(0);
        let size = (13.0 * s).round().max(9.0);
        for (n, &(g, first)) in lines.iter().skip(self.row).take(self.rows).enumerate() {
            let group = &data.groups[g];
            let y = body[1] + n as f32 * line_h;
            if first == 0 {
                if n > 0 {
                    ui.rect(body[0], y - cgap / 2.0, body[2], s.max(1.0), color::BORDER, 0.6);
                }
                if group.icon != 0 {
                    crate::hud::object_icon(ui, group.icon, body[0] + 14.0 * s, y + cell / 2.0, 26.0 * s);
                }
                let got = group.items.iter().filter(|i| self.have.contains(i)).count();
                let all = group.items.len();
                let tag = format!("{got}/{all}");
                let tw = ui.measure(&tag, size, true);
                let full = got == all;
                let level = if group.level > 0 { format!("{}", group.level) } else { String::new() };
                let lw = if level.is_empty() { 0.0 } else { ui.measure(&level, size, true) + 10.0 * s };
                ui.label_fit(&group.name, body[0] + 34.0 * s, y + (cell - size * 1.22) / 2.0, name_w - 34.0 * s - tw - lw - 18.0 * s, size, true, if full { color::GOLD } else { color::TEXT });
                ui.label(&tag, body[0] + name_w - 12.0 * s - tw - lw, y + (cell - size * 1.22) / 2.0, size, true, if full { color::GOOD } else { color::TEXT_MUTED });
                if !level.is_empty() {
                    ui.label(&level, body[0] + name_w - 12.0 * s - lw + 8.0 * s, y + (cell - size * 1.22) / 2.0, size, true, color::ACCENT);
                }
            }
            for (k, &item) in group.items.iter().skip(first).take(cols).enumerate() {
                let sr = [body[0] + name_w + k as f32 * (cell + cgap), y, cell, cell];
                self.piece(ui, sr, item, mouse, s);
            }
        }
    }
}
