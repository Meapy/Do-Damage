//! The vault window: every item in the account's vault chests at once, chest by chest, so the
//! player can find an item without walking from chest to chest. It opens anywhere (the HUD's
//! chest button, or K) and asks the server for the chests each time it opens
//! (`VaultContentsRequest`, answered by `VaultContents`; `VaultContentsHandler.cs`).
//!
//! Read-only: nothing here moves an item. A search box dims what doesn't match and hides chests
//! with no match. In the player's own vault the server also says where each chest stands, and
//! clicking an item marks its chest in the world (`draw_marker`).

use crate::hud::{Layout, SlotKind, Ui};
use crate::theme::{self, color, Kind};
use bsk_proto::VaultContents;
use bsk_world::{GameObj, World};
use winit::keyboard::KeyCode;

/// What the window asks of the server.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Act {
    /// Everything in the vault.
    Request,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Close,
    Search,
    Up,
    Down,
    /// An item, by chest.
    Item(usize),
}

/// The slots of a vault chest.
const SLOTS: usize = 8;
/// How long to wait for an answer before taking it that the server can't (an older one).
const NO_ANSWER: i32 = 5000;
/// How long a chest stays marked, and how close counts as reaching it (tiles).
const MARK_MS: i32 = 12000;
const REACHED: f64 = 1.2;
/// The vault chest (`Vault Chest`), the HUD button's picture.
pub const VAULT_CHEST: u16 = 0x0504;

/// A chest marked in the world: its number, where it stands, since when, and in which map.
struct Marker {
    chest: usize,
    x: f64,
    y: f64,
    since: i32,
    map: String,
}

/// The vault window.
#[derive(Default)]
pub struct Vault {
    pub open: bool,
    /// Each chest's position, when we're in our vault.
    chests: Vec<Option<(f64, f64)>>,
    /// Each chest's slots: the item and its imprints.
    items: Vec<[Option<(i32, [u8; 3])>; SLOTS]>,
    loaded: bool,
    /// When we last asked, while no answer has come.
    asked: Option<i32>,
    /// The server never answered: it doesn't know the request.
    unsupported: bool,
    query: String,
    /// The chests shown (by number), the first row drawn, and how many rows and columns fit.
    shown: Vec<usize>,
    row: usize,
    rows: usize,
    cols: usize,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
    hover: Option<(usize, i32, [u8; 3])>,
    marker: Option<Marker>,
    map: String,
    now: i32,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

impl Vault {
    /// Opens the window and asks for the vault as it is now.
    pub fn show(&mut self, now: i32) -> Option<Act> {
        self.open = true;
        self.row = 0;
        if self.unsupported {
            return None;
        }
        self.asked = Some(now);
        Some(Act::Request)
    }

    /// A new connection: forget the vault and whether the server knows the request.
    pub fn reset(&mut self) {
        *self = Vault::default();
    }

    /// The server's answer.
    pub fn contents(&mut self, v: VaultContents, data: &bsk_data::GameData) {
        self.asked = None;
        self.loaded = true;
        self.chests = v.chests.iter().map(|p| (p.x >= 0.0 && p.y >= 0.0).then_some((p.x as f64, p.y as f64))).collect();
        self.items = vec![[None; SLOTS]; self.chests.len()];
        for g in v.items.iter() {
            if let Some(slot) = self.items.get_mut(g.chest as usize).and_then(|c| c.get_mut(g.slot as usize)) {
                *slot = Some((g.item as i32, [g.offensive, g.defensive, g.misc]));
            }
        }
        log::info!("vault: {} chests, {} items", self.chests.len(), v.items.len());
        log::debug!("vault chests at {:?}", self.chests);
        self.filter(data);
    }

    /// Whether the vault came.
    pub fn loaded(&self) -> bool {
        self.loaded
    }

    /// Whether we're in our vault (the server said where the chests stand).
    fn in_vault(&self) -> bool {
        self.chests.iter().any(Option::is_some)
    }

    fn matches(&self, data: &bsk_data::GameData, item: i32) -> bool {
        let q = self.query.trim().to_lowercase();
        q.is_empty() || data.objects.get(&(item as u16)).is_some_and(|d| d.display_id.to_lowercase().contains(&q))
    }

    /// The chests with an item matching the search (all of them with none).
    fn filter(&mut self, data: &bsk_data::GameData) {
        let searching = !self.query.trim().is_empty();
        self.shown = (0..self.items.len())
            .filter(|&c| !searching || self.items[c].iter().flatten().any(|&(item, _)| self.matches(data, item)))
            .collect();
        self.row = 0;
    }

    pub fn click(&mut self, pos: (f64, f64)) -> Option<Act> {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.open = false;
            return None;
        }
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h)?;
        match hit {
            Hit::Close => self.open = false,
            Hit::Search => {}
            Hit::Up => self.scroll(-(self.rows.max(1) as i32)),
            Hit::Down => self.scroll(self.rows.max(1) as i32),
            Hit::Item(chest) => {
                // In the vault: mark the chest and let the player go to it.
                if let Some(Some((x, y))) = self.chests.get(chest) {
                    self.marker = Some(Marker { chest, x: *x, y: *y, since: self.now, map: self.map.clone() });
                    self.open = false;
                }
            }
        }
        None
    }

    pub fn scroll(&mut self, rows: i32) {
        let total = self.shown.len().div_ceil(self.cols.max(1));
        let max = total.saturating_sub(self.rows.max(1));
        self.row = (self.row as i32 + rows).clamp(0, max as i32) as usize;
    }

    /// A key while the window is open: typing into the search, Escape to close.
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, data: &bsk_data::GameData) {
        match code {
            KeyCode::Escape => self.open = false,
            KeyCode::Backspace => {
                self.query.pop();
                self.filter(data);
            }
            _ => {
                let before = self.query.len();
                for c in text.unwrap_or("").chars() {
                    if !c.is_control() && self.query.chars().count() < 32 {
                        self.query.push(c);
                    }
                }
                if self.query.len() != before {
                    self.filter(data);
                }
            }
        }
    }

    /// Notes the time and the map; gives up on an answer that never came.
    pub fn tick(&mut self, now: i32, map: &str) {
        self.now = now;
        if self.map != map {
            self.map = map.to_owned();
        }
        if self.asked.is_some_and(|t| now - t > NO_ANSWER) {
            self.asked = None;
            self.unsupported = true;
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.hits.clear();
        self.hover = None;
        if !self.open {
            self.rect = None;
            return;
        }
        let (dw, dh) = (820.0, 600.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        // The header: the title, how much the vault holds, the close button.
        let mid = r[1] + 32.0 * s;
        let ts = font(22.0);
        ui.label("Vault", r[0] + 22.0 * s, (mid - ui.digit_middle(ts, true)).round(), ts, true, color::GOLD);
        if self.loaded {
            let n = self.items.iter().flatten().flatten().count();
            let total = self.items.len() * SLOTS;
            let chests = self.items.len();
            let t = format!("{n} of {total} slots filled · {chests} chest{}", if chests == 1 { "" } else { "s" });
            let fs = font(13.0);
            let x = r[0] + 22.0 * s + ui.measure("Vault", ts, true) + 16.0 * s;
            ui.label(&t, x, (mid - ui.digit_middle(fs, false)).round(), fs, false, color::TEXT_DIM);
        }
        let cb = [r[0] + r[2] - 42.0 * s, (mid - 14.0 * s).round(), 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));

        // The search, with a hint beside it.
        let body = [r[0] + 22.0 * s, r[1] + 62.0 * s, r[2] - 44.0 * s, r[3] - 110.0 * s];
        let fr = [body[0], body[1], 320.0 * s, 32.0 * s];
        ui.field_box(fr, true, s);
        let fs = font(14.0);
        let ty = fr[1] + (fr[3] - fs * 1.22) / 2.0;
        if self.query.is_empty() {
            ui.label("Search your vault...", fr[0] + 10.0 * s, ty, fs, false, color::TEXT_MUTED);
        } else {
            let q = self.query.clone();
            ui.label(&q, fr[0] + 10.0 * s, ty, fs, false, color::TEXT);
        }
        if (now / 500) % 2 == 0 {
            let cx = fr[0] + 10.0 * s + ui.measure(&self.query.clone(), fs, false) + s;
            ui.rect(cx, fr[1] + 7.0 * s, (1.5 * s).max(1.0), fr[3] - 14.0 * s, color::ACCENT, 1.0);
        }
        self.hits.push((Hit::Search, fr));
        let hint = if self.in_vault() { "Click an item to mark its chest." } else { "To move items, go to your vault." };
        let hs = font(12.5);
        ui.label(hint, fr[0] + fr[2] + 16.0 * s, ty + (fs - hs) * 0.6, hs, false, color::TEXT_MUTED);

        // The chests: cards of a label over a row of eight slots, as many across and down as fit.
        let cell = 40.0 * s;
        let gap = 4.0 * s;
        let pad = 8.0 * s;
        let label_h = 20.0 * s;
        let card_w = SLOTS as f32 * cell + (SLOTS - 1) as f32 * gap + 2.0 * pad;
        let card_h = label_h + cell + 2.0 * pad;
        let (cgap, rgap) = (16.0 * s, 10.0 * s);
        let gy = fr[1] + fr[3] + 14.0 * s;
        let by = body[1] + body[3] - 28.0 * s;
        self.cols = (((body[2] + cgap) / (card_w + cgap)).floor() as usize).max(1);
        self.rows = (((by - 8.0 * s - gy + rgap) / (card_h + rgap)).floor() as usize).max(1);
        let gx = body[0] + (body[2] - (self.cols as f32 * (card_w + cgap) - cgap)) / 2.0;
        let marked = self.marker.as_ref().map(|m| m.chest);
        for (n, &c) in self.shown.clone().iter().skip(self.row * self.cols).take(self.cols * self.rows).enumerate() {
            let cr = [gx + (n % self.cols) as f32 * (card_w + cgap), gy + (n / self.cols) as f32 * (card_h + rgap), card_w, card_h];
            theme::frame(ui.renderer, cr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
            let filled = self.items[c].iter().flatten().count();
            let ls = font(12.5);
            let title = format!("Chest {}", c + 1);
            ui.label(&title, cr[0] + pad, cr[1] + pad - 2.0 * s, ls, true, if marked == Some(c) { color::GOLD } else { color::TEXT });
            let count = format!("{filled}/{SLOTS}");
            let cw = ui.measure(&count, font(11.5), false);
            ui.label(&count, cr[0] + cr[2] - pad - cw, cr[1] + pad - 1.0 * s, font(11.5), false, color::TEXT_MUTED);
            for k in 0..SLOTS {
                let sr = [cr[0] + pad + k as f32 * (cell + gap), cr[1] + pad + label_h, cell, cell];
                let slot = self.items[c][k];
                let over = slot.is_some() && inside(sr, mouse);
                let hit = slot.is_some_and(|(item, _)| !self.query.trim().is_empty() && self.matches(ui.data, item));
                let edge = if hit { Some(color::GOLD) } else { slot.and_then(|(item, _)| crate::screens::tier_edge(ui.data, item)) };
                ui.slot_box(sr, edge, over, s);
                if let Some((item, imprints)) = slot {
                    ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
                    if !self.matches(ui.data, item) {
                        theme::fill(ui.renderer, sr, 6.0 * s, color::BACKGROUND, 0.7);
                    }
                    if over {
                        self.hover = Some((c, item, imprints));
                    }
                    self.hits.push((Hit::Item(c), sr));
                }
            }
        }
        let msg_size = font(13.0);
        if self.unsupported {
            ui.label("This server can't show the vault yet.", gx, gy + 6.0 * s, msg_size, false, color::TEXT_MUTED);
        } else if !self.loaded {
            ui.label("Opening your vault...", gx, gy + 6.0 * s, msg_size, false, color::TEXT_MUTED);
        } else if self.items.is_empty() {
            ui.label("You have no vault chests yet.", gx, gy + 6.0 * s, msg_size, false, color::TEXT_MUTED);
        } else if self.shown.is_empty() {
            ui.label("Nothing in your vault matches.", gx, gy + 6.0 * s, msg_size, false, color::TEXT_MUTED);
        }

        // The paging, and where the search found things.
        let total = self.shown.len().div_ceil(self.cols.max(1));
        if total > self.rows {
            let (up, down) = ([body[0], by, 56.0 * s, 26.0 * s], [body[0] + 62.0 * s, by, 56.0 * s, 26.0 * s]);
            ui.button(up, "Up", Kind::Secondary, inside(up, mouse), s * 0.8);
            ui.button(down, "Down", Kind::Secondary, inside(down, mouse), s * 0.8);
            self.hits.push((Hit::Up, up));
            self.hits.push((Hit::Down, down));
            let page = format!("Rows {}-{} of {}", self.row + 1, (self.row + self.rows).min(total), total);
            ui.label(&page, body[0] + 130.0 * s, by + 5.0 * s, font(12.0), false, color::TEXT_MUTED);
        }
        if !self.query.trim().is_empty() && !self.shown.is_empty() {
            let found = self.shown.iter().map(|c| (c + 1).to_string()).collect::<Vec<_>>().join(", ");
            let t = format!("In chest{} {found}", if self.shown.len() == 1 { "" } else { "s" });
            let fs = font(13.0);
            let t = clip(ui, &t, fs, body[2] * 0.55);
            let w = ui.measure(&t, fs, true);
            ui.label(&t, body[0] + body[2] - w, by + 4.0 * s, fs, true, color::GOOD);
        }

        if let Some((chest, item, imprints)) = self.hover {
            let mut lines = crate::hud::item_tooltip_imprinted(ui.data, item, me, SlotKind::Shop, imprints);
            lines.extend(crate::hud::imprint_lines(ui.data, item, imprints));
            lines.push(crate::hud::Line::gap());
            lines.push(crate::hud::Line::new(&format!("In chest {}", chest + 1), 12.0, true, color::GOOD));
            if self.chests.get(chest).is_some_and(Option::is_some) {
                lines.push(crate::hud::Line::new("Click to mark the chest", 12.0, false, color::TEXT_DIM));
            }
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }

    /// The marked chest in the world: a gold ring at its foot and its number over it, until the
    /// player reaches it, leaves the map or the time runs out. Off the screen, its number waits at
    /// the edge of the view (`view`: its width and height) with dots pointing the way.
    pub fn draw_marker(&mut self, ui: &mut Ui, world: &World, cam: &crate::view::Camera, view: (f32, f32)) {
        let Some(m) = &self.marker else { return };
        let reached = world.player().is_some_and(|me| (me.x - m.x).hypot(me.y - m.y) < REACHED);
        if m.map != self.map || self.now - m.since > MARK_MS || reached {
            self.marker = None;
            return;
        }
        let k = (cam.scale / crate::view::TILE_PX) as f32;
        let [px, py] = cam.to_screen(m.x, m.y, 0.0);
        let t = (self.now - m.since) as f32 / 1000.0;
        let pulse = 0.5 + 0.5 * (t * 5.0).sin();
        let label = format!("Chest {}", m.chest + 1);
        let size = (13.0 * k).round().max(10.0);
        let w = ui.measure(&label, size, true) + 14.0 * k;
        let h = size * 1.22 + 6.0 * k;
        let margin = 24.0 * k;
        let on_screen = px > margin && py > margin && px < view.0 - margin && py < view.1 - margin;
        let lr = if on_screen {
            let rad = (14.0 + 4.0 * pulse) * k;
            let ring = [px - rad, py - rad * 0.6, rad * 2.0, rad * 1.2];
            theme::frame(ui.renderer, ring, rad * 0.6, (2.5 * k).max(1.5), (color::GOLD, 0.6 + 0.4 * pulse), ((color::GOLD, 0.12), (color::GOLD, 0.12)));
            let bob = (t * 4.0).sin() * 3.0 * k;
            [(px - w / 2.0).round(), (py - 44.0 * k - h + bob).round(), w.round(), h.round()]
        } else {
            // where the line from the middle of the view to the chest leaves it
            let (cx, cy) = (view.0 / 2.0, view.1 / 2.0);
            let (dx, dy) = (px - cx, py - cy);
            let reach = ((cx - margin - w / 2.0) / dx.abs().max(1e-3)).min((cy - margin - h) / dy.abs().max(1e-3));
            let (ex, ey) = (cx + dx * reach, cy + dy * reach);
            let len = dx.hypot(dy).max(1e-3);
            for n in 0..3 {
                let d = (w / 2.0).max(h) + (8.0 + 9.0 * n as f32 + 4.0 * pulse) * k;
                let r = 3.0 * k;
                theme::fill(ui.renderer, [ex + dx / len * d - r, ey + dy / len * d - r, 2.0 * r, 2.0 * r], r, color::GOLD, 0.9 - 0.25 * n as f32);
            }
            [(ex - w / 2.0).round(), (ey - h / 2.0).round(), w.round(), h.round()]
        };
        theme::fill(ui.renderer, lr, h / 2.0, color::GOLD, 0.95);
        ui.label(&label, lr[0] + 7.0 * k, lr[1] + 3.0 * k, size, true, color::ACCENT_TEXT);
    }
}

/// `text` cut to `width`, with "..." when cut.
fn clip(ui: &mut Ui, text: &str, size: f32, width: f32) -> String {
    if ui.measure(text, size, true) <= width {
        return text.to_owned();
    }
    let mut t = text.to_owned();
    while !t.is_empty() && ui.measure(&format!("{t}..."), size, true) > width {
        t.pop();
    }
    format!("{t}...")
}
