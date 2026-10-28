//! The vault window: every item in the account's vault chests at once, chest by chest, so the
//! player can find and move items without walking from chest to chest. It opens in the player's own vault (the HUD's chest button, or K) and asks the server for
//! the chests each time it opens (`VaultContentsRequest`, answered by `VaultContents`;
//! `VaultContentsHandler.cs`), and again after each move.
//!
//! Items drag between any two chest slots, and to and from the HUD's inventory beside the window
//! (its slots count as the window's while it's open); a click sends an item across, from a chest
//! to the first free inventory slot or from the inventory to the first free chest slot.
//! Moves are ordinary `InvSwap`s to the chests' object ids, which the server allows from anywhere
//! in the vault for its owner's own chests (`InvSwapHandler.OwnVaultSwap`). A search box dims what
//! doesn't match and hides chests with no match. A right-click marks an item's chest in the world
//! (`draw_marker`).

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
    /// Swap two slots, each (object id, slot, item).
    Swap((i32, u8, i32), (i32, u8, i32)),
}

/// A slot the window shows: a chest's (chest, slot) or an inventory slot.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Place {
    Chest(usize, usize),
    Inv(u8),
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Close,
    Search,
    Up,
    Down,
    Slot(Place),
}

/// The slots of a vault chest.
const SLOTS: usize = 8;
/// How long to wait for an answer before taking it that the server can't (an older one).
const NO_ANSWER: i32 = 5000;
/// How long a chest stays marked, and how close counts as reaching it (tiles).
const MARK_MS: i32 = 12000;
const REACHED: f64 = 1.2;
/// How far the mouse moves before a press on an item becomes a drag (pixels).
const DRAG_PX: f64 = 4.0;
/// The vault chest (`Vault Chest`), the HUD button's picture.
pub const VAULT_CHEST: u16 = 0x0504;

/// Whether this map is a vault, where the window and its button are offered.
pub fn is_vault(map: &str) -> bool {
    map == "Vault"
}

/// A chest marked in the world: its number, where it stands, since when, and in which map.
struct Marker {
    chest: usize,
    x: f64,
    y: f64,
    since: i32,
    map: String,
}

/// A vault chest as the server described it: its object id and position, in our own vault.
#[derive(Clone, Copy, Debug)]
struct Chest {
    id: i32,
    pos: Option<(f64, f64)>,
}

/// The vault window.
#[derive(Default)]
pub struct Vault {
    pub open: bool,
    chests: Vec<Chest>,
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
    hover: Option<(Place, i32, [u8; 3])>,
    /// Our id and inventory when last drawn: (slot, item, imprints) for slots 4-11, and the
    /// backpack's 12-19 when we have one.
    me: i32,
    inventory: Vec<(u8, i32, [u8; 3])>,
    backpack: bool,
    /// A press on an item: where, and from which slot; a drag once the mouse moves.
    press: Option<(Place, (f64, f64))>,
    mouse: (f64, f64),
    /// What went wrong with the last move.
    message: Option<String>,
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
        self.press = None;
        self.message = None;
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
        self.chests = v.chests.iter().map(|c| Chest { id: c.id, pos: (c.id >= 0).then_some((c.x as f64, c.y as f64)) }).collect();
        self.items = vec![[None; SLOTS]; self.chests.len()];
        for g in v.items.iter() {
            if let Some(slot) = self.items.get_mut(g.chest as usize).and_then(|c| c.get_mut(g.slot as usize)) {
                *slot = Some((g.item as i32, [g.offensive, g.defensive, g.misc]));
            }
        }
        log::info!("vault: {} chests, {} items", self.chests.len(), v.items.len());
        let row = self.row;
        self.filter(data);
        // a refresh after a move keeps the place
        self.row = row;
        self.scroll(0);
    }

    /// Whether the vault came.
    pub fn loaded(&self) -> bool {
        self.loaded
    }

    /// Whether items can move: we're in our own vault (the server gave the chests' ids).
    fn movable(&self) -> bool {
        self.chests.iter().any(|c| c.pos.is_some())
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

    /// The item in a slot (-1 for none) and its owner's object id.
    fn at(&self, p: Place) -> Option<(i32, i32)> {
        match p {
            Place::Chest(c, s) => Some((self.chests.get(c)?.id, self.items.get(c)?[s].map_or(-1, |(i, _)| i))),
            Place::Inv(s) => Some((self.me, self.inventory.iter().find(|i| i.0 == s).map_or(-1, |i| i.1))),
        }
    }

    fn slot_of(p: Place) -> u8 {
        match p {
            Place::Chest(_, s) => s as u8,
            Place::Inv(s) => s,
        }
    }

    /// Swaps two slots: on the server, and here until its answer comes.
    fn swap(&mut self, a: Place, b: Place) -> Vec<Act> {
        let (Some((ida, ia)), Some((idb, ib))) = (self.at(a), self.at(b)) else { return Vec::new() };
        if a == b || (ia < 0 && ib < 0) || ida < 0 || idb < 0 {
            return Vec::new();
        }
        self.message = None;
        // the window's own copy of the chests (the inventory comes from the player's stats)
        let take = |v: &mut Self, p: Place| match p {
            Place::Chest(c, s) => v.items[c][s].take(),
            Place::Inv(_) => None,
        };
        let (da, db) = (take(self, a), take(self, b));
        let put = |v: &mut Self, p: Place, item: i32, other: Option<(i32, [u8; 3])>| {
            if let Place::Chest(c, s) = p {
                v.items[c][s] = (item >= 0).then(|| other.unwrap_or((item, [0; 3])));
            }
        };
        let inv_imprints = |v: &Self, p: Place| match p {
            Place::Inv(s) => v.inventory.iter().find(|i| i.0 == s).map(|i| (i.1, i.2)),
            _ => None,
        };
        let (fa, fb) = (da.or(inv_imprints(self, a)), db.or(inv_imprints(self, b)));
        put(self, a, ib, fb);
        put(self, b, ia, fa);
        vec![Act::Swap((ida, Self::slot_of(a), ia), (idb, Self::slot_of(b), ib)), Act::Request]
    }

    /// A click on an item sends it across: a chest's to the first free inventory slot, the
    /// inventory's to the first free chest slot.
    fn send_across(&mut self, p: Place) -> Vec<Act> {
        let to = match p {
            Place::Chest(..) => {
                let end = if self.backpack { 20 } else { 12 };
                let free = (4..end).find(|s| !self.inventory.iter().any(|i| i.0 == *s));
                if free.is_none() {
                    self.message = Some("Your inventory is full.".into());
                }
                free.map(Place::Inv)
            }
            Place::Inv(_) => {
                let free = (0..self.items.len()).find_map(|c| self.items[c].iter().position(Option::is_none).map(|s| Place::Chest(c, s)));
                if free.is_none() {
                    self.message = Some("Your vault is full.".into());
                }
                free
            }
        };
        to.map_or_else(Vec::new, |to| self.swap(p, to))
    }

    /// A press (`down`) or release of the left button; `on_panel`: on the HUD, which doesn't close
    /// the window.
    pub fn click(&mut self, down: bool, pos: (f64, f64), on_panel: bool) -> Vec<Act> {
        self.mouse = pos;
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h);
        if !down {
            let Some((from, start)) = self.press.take() else { return Vec::new() };
            let dragged = (pos.0 - start.0).hypot(pos.1 - start.1) > DRAG_PX;
            return match (dragged, hit) {
                (true, Some(Hit::Slot(to))) => self.swap(from, to),
                (false, Some(Hit::Slot(to))) if to == from => self.send_across(from),
                _ => Vec::new(),
            };
        }
        if hit.is_none() && self.rect.is_some_and(|r| !inside(r, pos)) {
            if !on_panel {
                self.open = false;
            }
            return Vec::new();
        }
        match hit {
            Some(Hit::Close) => self.open = false,
            Some(Hit::Up) => self.scroll(-(self.rows.max(1) as i32)),
            Some(Hit::Down) => self.scroll(self.rows.max(1) as i32),
            Some(Hit::Slot(p)) => {
                if self.movable() && self.at(p).is_some_and(|(_, item)| item >= 0) {
                    self.press = Some((p, pos));
                }
            }
            Some(Hit::Search) | None => {}
        }
        Vec::new()
    }

    /// A right-click: on a chest's item, marks the chest in the world.
    pub fn right_click(&mut self, pos: (f64, f64)) {
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h);
        if let Some(Hit::Slot(Place::Chest(chest, _))) = hit {
            if let Some((x, y)) = self.chests.get(chest).and_then(|c| c.pos) {
                self.marker = Some(Marker { chest, x, y, since: self.now, map: self.map.clone() });
                self.open = false;
            }
        }
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

    /// Notes the time and the map, closing the window outside the vault; gives up on an answer
    /// that never came.
    pub fn tick(&mut self, now: i32, map: &str) {
        self.now = now;
        if self.map != map {
            self.map = map.to_owned();
            self.open = false;
            self.press = None;
        }
        if self.asked.is_some_and(|t| now - t > NO_ANSWER) {
            self.asked = None;
            self.unsupported = true;
        }
    }

    /// Reads our inventory.
    fn look(&mut self, me: Option<&GameObj>) {
        self.inventory.clear();
        let Some(me) = me else { return };
        self.me = me.id;
        self.backpack = me.stat_int(crate::hud::stat::HAS_BACKPACK).unwrap_or(0) != 0;
        for slot in 4..if self.backpack { 20 } else { 12 } {
            let item = me.stat_int(crate::hud::slot_stat(slot)).unwrap_or(-1);
            if item > 0 {
                self.inventory.push((slot, item, crate::hud::slot_imprints(me, slot)));
            }
        }
    }

    /// A card: its title and count over a row of eight slots, each `Place` from `place`.
    #[allow(clippy::too_many_arguments)]
    fn card(&mut self, ui: &mut Ui, cr: [f32; 4], title: &str, title_color: u32, slots: [Option<(i32, [u8; 3])>; SLOTS], place: impl Fn(usize) -> Place, s: f32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let (cell, gap, pad, label_h) = (40.0 * s, 4.0 * s, 8.0 * s, 20.0 * s);
        theme::frame(ui.renderer, cr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
        ui.label(title, cr[0] + pad, cr[1] + pad - 2.0 * s, font(12.5), true, title_color);
        let count = format!("{}/{SLOTS}", slots.iter().flatten().count());
        let cw = ui.measure(&count, font(11.5), false);
        ui.label(&count, cr[0] + cr[2] - pad - cw, cr[1] + pad - 1.0 * s, font(11.5), false, color::TEXT_MUTED);
        let dragging = self.press.filter(|(_, start)| (self.mouse.0 - start.0).hypot(self.mouse.1 - start.1) > DRAG_PX).map(|(p, _)| p);
        for (k, slot) in slots.iter().enumerate() {
            let p = place(k);
            let sr = [cr[0] + pad + k as f32 * (cell + gap), cr[1] + pad + label_h, cell, cell];
            let over = inside(sr, self.mouse);
            let hit = slot.is_some_and(|(item, _)| !self.query.trim().is_empty() && self.matches(ui.data, item));
            let edge = if dragging.is_some() && over {
                Some(color::ACCENT)
            } else if hit {
                Some(color::GOLD)
            } else {
                slot.and_then(|(item, _)| crate::screens::tier_edge(ui.data, item))
            };
            ui.slot_box(sr, edge, over, s);
            if let Some((item, imprints)) = *slot {
                if dragging != Some(p) {
                    ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
                    if !self.matches(ui.data, item) {
                        theme::fill(ui.renderer, sr, 6.0 * s, color::BACKGROUND, 0.7);
                    }
                }
                if over && dragging.is_none() {
                    self.hover = Some((p, item, imprints));
                }
            }
            self.hits.push((Hit::Slot(p), sr));
        }
    }

    /// Draws the window; `hud`: the HUD's inventory slots showing (slot, where), which take drags
    /// and clicks as the window's own.
    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, hud: &[(u8, [f32; 4])], mouse: (f64, f64), now: i32) {
        self.hits.clear();
        self.hover = None;
        if !self.open {
            self.rect = None;
            self.press = None;
            return;
        }
        self.mouse = mouse;
        self.look(me);
        // as tall as the chests need (two across), up to 600
        let need = self.items.len().div_ceil(2).max(1) as f32;
        let (dw, dh) = (820.0, (154.0 + need * 96.0).clamp(300.0, 600.0));
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
        let body = [r[0] + 22.0 * s, r[1] + 62.0 * s, r[2] - 44.0 * s, r[3] - 84.0 * s];
        let fr = [body[0], body[1], 300.0 * s, 32.0 * s];
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
        let hint = if self.movable() { "Drag items to and from your inventory, or click one to send it across." } else { "Only in your own vault can items move." };
        let hs = font(12.5);
        let hint = clip(ui, hint, hs, body[0] + body[2] - (fr[0] + fr[2] + 16.0 * s));
        ui.label(&hint, fr[0] + fr[2] + 16.0 * s, ty + (fs - hs) * 0.6, hs, false, color::TEXT_MUTED);

        // The chests, as many across and down as fit.
        let (cell, gap, pad, label_h) = (40.0 * s, 4.0 * s, 8.0 * s, 20.0 * s);
        let card_w = SLOTS as f32 * cell + (SLOTS - 1) as f32 * gap + 2.0 * pad;
        let card_h = label_h + cell + 2.0 * pad;
        let (cgap, rgap) = (16.0 * s, 10.0 * s);
        self.cols = (((body[2] + cgap) / (card_w + cgap)).floor() as usize).max(1);
        let gx = body[0] + (body[2] - (self.cols as f32 * (card_w + cgap) - cgap)).max(0.0) / 2.0;
        let by = body[1] + body[3] - 28.0 * s;
        let gy = fr[1] + fr[3] + 14.0 * s;
        self.rows = (((by - 8.0 * s - gy + rgap) / (card_h + rgap)).floor() as usize).max(1);
        let marked = self.marker.as_ref().map(|m| m.chest);
        for (n, &c) in self.shown.clone().iter().skip(self.row * self.cols).take(self.cols * self.rows).enumerate() {
            let cr = [gx + (n % self.cols) as f32 * (card_w + cgap), gy + (n / self.cols) as f32 * (card_h + rgap), card_w, card_h];
            let title_color = if marked == Some(c) { color::GOLD } else { color::TEXT };
            self.card(ui, cr, &format!("Chest {}", c + 1), title_color, self.items[c], |k| Place::Chest(c, k), s);
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
        // The HUD's inventory: a drag shows where it would land, and the item dragged out of it
        // leaves its slot looking empty.
        let dragging = self.press.filter(|(_, start)| (mouse.0 - start.0).hypot(mouse.1 - start.1) > DRAG_PX).map(|(p, _)| p);
        for &(slot, sr) in hud {
            if dragging == Some(Place::Inv(slot)) {
                theme::fill(ui.renderer, sr, 6.0 * s, color::SLOT_BOTTOM, 0.9);
            }
            if dragging.is_some() && inside(sr, mouse) {
                theme::frame(ui.renderer, sr, 6.0 * s, (2.0 * s).max(1.0), (color::ACCENT, 1.0), ((color::ACCENT, 0.12), (color::ACCENT, 0.12)));
            }
            self.hits.push((Hit::Slot(Place::Inv(slot)), sr));
        }

        // The paging, then what went wrong or where the search found things.
        let total = self.shown.len().div_ceil(self.cols.max(1));
        if total > self.rows {
            let (up, down) = ([gx, by, 56.0 * s, 26.0 * s], [gx + 62.0 * s, by, 56.0 * s, 26.0 * s]);
            ui.button(up, "Up", Kind::Secondary, inside(up, mouse), s * 0.8);
            ui.button(down, "Down", Kind::Secondary, inside(down, mouse), s * 0.8);
            self.hits.push((Hit::Up, up));
            self.hits.push((Hit::Down, down));
            let page = format!("Rows {}-{} of {}", self.row + 1, (self.row + self.rows).min(total), total);
            ui.label(&page, gx + 130.0 * s, by + 5.0 * s, font(12.0), false, color::TEXT_MUTED);
        }
        let right = gx + self.cols as f32 * (card_w + cgap) - cgap;
        let note = if let Some(m) = &self.message {
            Some((m.clone(), color::DANGER))
        } else if !self.query.trim().is_empty() && !self.shown.is_empty() {
            let found = self.shown.iter().map(|c| (c + 1).to_string()).collect::<Vec<_>>().join(", ");
            Some((format!("In chest{} {found}", if self.shown.len() == 1 { "" } else { "s" }), color::GOOD))
        } else {
            None
        };
        if let Some((t, c)) = note {
            let fs = font(13.0);
            let t = clip(ui, &t, fs, (right - gx) * 0.55);
            let w = ui.measure(&t, fs, true);
            ui.label(&t, right - w, by + 4.0 * s, fs, true, c);
        }

        // The item being dragged, under the mouse.
        if let Some((p, start)) = self.press {
            if (mouse.0 - start.0).hypot(mouse.1 - start.1) > DRAG_PX {
                if let Some((_, item)) = self.at(p).filter(|(_, i)| *i >= 0) {
                    ui.item_icon(item, 80, mouse.0 as f32, mouse.1 as f32, s);
                }
            }
        }
        if let Some((place, item, imprints)) = self.hover {
            let mut lines = crate::hud::item_tooltip_imprinted(ui.data, item, me, SlotKind::Shop, imprints);
            lines.extend(crate::hud::imprint_lines(ui.data, item, imprints));
            if let Place::Chest(chest, _) = place {
                lines.push(crate::hud::Line::gap());
                lines.push(crate::hud::Line::new(&format!("In chest {}", chest + 1), 12.0, true, color::GOOD));
                if self.movable() {
                    lines.push(crate::hud::Line::new("Click: to your inventory · Right-click: mark the chest", 12.0, false, color::TEXT_DIM));
                }
            } else if self.movable() {
                lines.push(crate::hud::Line::gap());
                lines.push(crate::hud::Line::new("Click: into your vault", 12.0, false, color::TEXT_DIM));
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
