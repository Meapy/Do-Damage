//! The Changelog window (what changed in each version), and the small note that says the game
//! was updated. Both go over everything else, on the screens and in the game.

use crate::hud::Ui;
use crate::theme::{self, color};
use bsk_update::Entry;
use winit::keyboard::KeyCode;

/// How long the "updated" note stays, ms.
const NOTE_FOR: i32 = 10_000;

#[derive(Default)]
pub struct ChangelogUi {
    pub open: bool,
    /// Lines scrolled down.
    scroll: usize,
    /// The most it can scroll, as last drawn.
    max_scroll: usize,
    close: [f32; 4],
    panel: [f32; 4],
    /// The note: its text, until when, and where it was drawn.
    note: Option<(String, i32)>,
    note_rect: Option<[f32; 4]>,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// A line of the window: a version's heading, or a change.
enum Row {
    Heading { version: String, date: String, tag: Option<&'static str> },
    /// A group's title, over its changes.
    Group(String),
    Change(String, bool),
    Gap,
}

impl ChangelogUi {
    pub fn show(&mut self) {
        self.open = true;
        self.scroll = 0;
        self.note = None;
    }

    /// Show the "updated" note for a while.
    pub fn note(&mut self, text: String, now: i32) {
        self.note = Some((text, now + NOTE_FOR));
    }

    /// A left click; true if the window or the note took it.
    pub fn click(&mut self, pos: (f64, f64)) -> bool {
        if self.open {
            if inside(self.close, pos) || !inside(self.panel, pos) {
                self.open = false;
            }
            return true;
        }
        if self.note_rect.is_some_and(|r| inside(r, pos)) {
            self.show();
            return true;
        }
        false
    }

    /// A key while open; true if it was taken.
    pub fn key(&mut self, code: KeyCode) -> bool {
        if !self.open {
            return false;
        }
        match code {
            KeyCode::Escape | KeyCode::Enter | KeyCode::NumpadEnter => self.open = false,
            KeyCode::ArrowDown => self.scroll = (self.scroll + 1).min(self.max_scroll),
            KeyCode::ArrowUp => self.scroll = self.scroll.saturating_sub(1),
            KeyCode::PageDown => self.scroll = (self.scroll + 8).min(self.max_scroll),
            KeyCode::PageUp => self.scroll = self.scroll.saturating_sub(8),
            KeyCode::Home => self.scroll = 0,
            _ => {}
        }
        true
    }

    /// The mouse wheel (notches, up positive) while open.
    pub fn wheel(&mut self, notches: i32) {
        self.scroll = (self.scroll as i32 - notches * 3).clamp(0, self.max_scroll as i32) as usize;
    }

    /// Draw the window, when open. `coming` is a newer version that's downloaded and starts at
    /// the next portal; `current` is the running one.
    #[allow(clippy::too_many_arguments)]
    pub fn draw(&mut self, ui: &mut Ui, w: f32, h: f32, entries: &[Entry], current: &str, coming: Option<&str>, mouse: (f64, f64)) {
        if !self.open {
            return;
        }
        let s = crate::screens::scale(w, h);
        ui.rect(0.0, 0.0, w, h, 0x000000, 0.55);
        let pw = (640.0 * s).min(w - 32.0);
        let ph = (h - 80.0 * s).min(760.0 * s).max(200.0);
        let r = [(w - pw) / 2.0, (h - ph) / 2.0, pw, ph];
        self.panel = r;
        theme::panel(ui.renderer, r, s);
        let pad = 26.0 * s;
        ui.label("What's new", r[0] + pad, r[1] + 20.0 * s, (24.0 * s).round(), true, color::TEXT);
        let sub = format!("You're playing version {current}");
        ui.label(&sub, r[0] + pad, r[1] + 54.0 * s, (13.0 * s).round(), false, color::TEXT_DIM);
        // Close, top right.
        let c = [r[0] + r[2] - 44.0 * s, r[1] + 16.0 * s, 28.0 * s, 28.0 * s];
        self.close = c;
        let over = inside(c, mouse);
        theme::fill(ui.renderer, c, 14.0 * s, if over { color::DANGER } else { color::SECONDARY }, 0.95);
        theme::cross(ui.renderer, c[0] + c[2] / 2.0, c[1] + c[3] / 2.0, 5.0 * s, (2.0 * s).max(1.5), 0xFFFFFF);
        theme::fill(ui.renderer, [r[0] + pad, r[1] + 80.0 * s, r[2] - 2.0 * pad, s.max(1.0)], 0.0, color::BORDER, 1.0);

        // Everything as rows, wrapped to the width; then the rows that fit, from the scroll.
        let size = (14.0 * s).round().max(9.0);
        let head = (17.0 * s).round().max(10.0);
        let text_w = r[2] - 2.0 * pad - 22.0 * s;
        let mut rows = Vec::new();
        for e in entries {
            let newer = bsk_update::compare_versions(&e.version, current) == std::cmp::Ordering::Greater;
            let tag = if e.version == current {
                Some("Installed")
            } else if newer && coming.is_some_and(|v| bsk_update::compare_versions(&e.version, v) != std::cmp::Ordering::Greater) {
                Some("Starts at your next portal")
            } else if newer {
                Some("Coming soon")
            } else {
                None
            };
            rows.push(Row::Heading { version: e.version.clone(), date: e.date.clone(), tag });
            for (i, change) in e.changes.iter().enumerate() {
                for (title, _) in e.groups.iter().filter(|g| g.1 == i) {
                    rows.push(Row::Group(title.clone()));
                }
                for (i, line) in crate::npc::wrap(ui, change, size, text_w).into_iter().enumerate() {
                    rows.push(Row::Change(line, i == 0));
                }
            }
            rows.push(Row::Gap);
        }
        let row_h = |row: &Row| match row {
            Row::Heading { .. } => head * 1.9,
            Row::Group(..) => size * 2.0,
            Row::Change(..) => size * 1.45,
            Row::Gap => size * 0.9,
        };
        let top = r[1] + 94.0 * s;
        let room = r[1] + r[3] - pad - top;
        // The most it can scroll: until the last rows just fill the space.
        let mut fill = 0.0;
        let mut last_start = rows.len();
        while last_start > 0 && fill + row_h(&rows[last_start - 1]) <= room {
            last_start -= 1;
            fill += row_h(&rows[last_start]);
        }
        self.max_scroll = last_start;
        self.scroll = self.scroll.min(self.max_scroll);
        let mut y = top;
        for row in rows.iter().skip(self.scroll) {
            let rh = row_h(row);
            if y + rh > top + room + 0.5 {
                break;
            }
            match row {
                Row::Heading { version, date, tag } => {
                    let label = format!("Version {version}");
                    ui.label(&label, r[0] + pad, y + 2.0 * s, head, true, color::ACCENT);
                    let x = r[0] + pad + ui.measure(&label, head, true) + 12.0 * s;
                    if let Some(tag) = tag {
                        let ts = (12.0 * s).round().max(8.0);
                        let tw = ui.measure(tag, ts, true);
                        let pill = [x, y + 3.0 * s, tw + 16.0 * s, ts * 1.5];
                        let (bg, fg) = if *tag == "Installed" { (color::SECONDARY, color::TEXT_DIM) } else { (color::GOOD, 0x0B2414) };
                        theme::fill(ui.renderer, pill, pill[3] / 2.0, bg, 0.95);
                        ui.label(tag, pill[0] + 8.0 * s, pill[1] + ts * 0.18, ts, true, fg);
                    }
                    if !date.is_empty() {
                        let ds = (12.0 * s).round().max(8.0);
                        let dw = ui.measure(date, ds, false);
                        ui.label(date, r[0] + r[2] - pad - dw, y + 5.0 * s, ds, false, color::TEXT_MUTED);
                    }
                }
                Row::Group(title) => {
                    ui.label(title, r[0] + pad, y + size * 0.5, size, true, color::TEXT_DIM);
                }
                Row::Change(line, first) => {
                    if *first {
                        let d = 5.0 * s;
                        theme::fill(ui.renderer, [r[0] + pad + 4.0 * s, y + size * 0.5 - d / 2.0 + 1.0 * s, d, d], d / 2.0, color::TEXT_DIM, 1.0);
                    }
                    ui.label(line, r[0] + pad + 22.0 * s, y, size, false, color::TEXT);
                }
                Row::Gap => {}
            }
            y += rh;
        }
        // A scroll bar when it doesn't all fit.
        if self.max_scroll > 0 {
            let track = [r[0] + r[2] - 12.0 * s, top, 4.0 * s, room];
            theme::fill(ui.renderer, track, 2.0 * s, 0xFFFFFF, 0.06);
            let shown = (rows.len() - self.max_scroll).max(1) as f32 / rows.len() as f32;
            let at = self.scroll as f32 / self.max_scroll as f32;
            let th = (room * shown).max(24.0 * s);
            theme::fill(ui.renderer, [track[0], top + (room - th) * at, track[2], th], 2.0 * s, color::TEXT_MUTED, 0.9);
        }
    }

    /// The "updated" note, top-left at (x, y), while it lasts. Clicking it opens the window.
    pub fn draw_note(&mut self, ui: &mut Ui, x: f32, y: f32, s: f32, mouse: (f64, f64), now: i32) {
        self.note_rect = None;
        let Some((text, until)) = &self.note else { return };
        if now >= *until || self.open {
            self.note = None;
            return;
        }
        // Fades out over its last second.
        let alpha = ((*until - now) as f32 / 1000.0).min(1.0);
        let size = (14.0 * s).round().max(9.0);
        let link = "See what's new";
        let tw = ui.measure(text, size, true);
        let lw = ui.measure(link, size, true);
        let r = [x, y, tw.max(lw) + 28.0 * s, size * 2.9 + 14.0 * s];
        let over = inside(r, mouse);
        theme::shadow(ui.renderer, r, 8.0 * s, 10.0 * s, 0.4 * alpha);
        theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (if over { color::ACCENT } else { color::BORDER_HI }, alpha), ((color::PANEL_TOP, 0.95 * alpha), (color::PANEL_BOTTOM, 0.95 * alpha)));
        let text = text.clone();
        ui.label(&text, r[0] + 14.0 * s, r[1] + 7.0 * s, size, true, color::TEXT);
        ui.label(link, r[0] + 14.0 * s, r[1] + 7.0 * s + size * 1.45, size, true, if over { color::ACCENT_HI } else { color::ACCENT });
        self.note_rect = Some(r);
    }
}
