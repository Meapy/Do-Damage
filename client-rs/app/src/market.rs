//! The Marketplace window, opened at the Market Broker in the Nexus's market square
//! (`MarketNPC`): players sell items to each other for fame (`Marketplace.cs` on the server).
//!
//! - Browse: every listing, searched by name, filtered by kind (weapons, abilities...) and
//!   tier, sorted by newest, price or time left; an item's tooltip on hover; Buy asks once more
//!   before it buys.
//! - Sell: pick an item from the inventory or the vault, set a price and how long it lasts
//!   (1, 3 or 7 days); the window shows what you get after the tax.
//! - My listings, with a button to take each back; the Box, where bought, taken-back and
//!   unsold items wait when the inventory is full (imprints and all); and the History.
//!
//! The window asks for everything when it opens and every few seconds while open
//! (`MarketBrowse`); the server sends the listings only when they changed (by version), and the
//! box and history each time (`MarketListings`). Each action is answered by `MarketResult`,
//! shown at the foot of the window.

use crate::forge::Category;
use crate::hud::{Layout, SlotKind, Ui};
use crate::theme::{self, color, Kind};
use bsk_data::GameData;
use bsk_proto::{MarketBoxItem, MarketHistory, MarketListing, MarketListings, MarketResult, VaultContents};
use bsk_world::GameObj;
use std::collections::HashMap;
use winit::keyboard::KeyCode;

/// What the window asks of the server.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Act {
    /// The listings if they changed since this version, the box and the history.
    Browse(i32),
    Sell { from_vault: bool, chest: i32, slot: u8, price: i32, hours: i32 },
    /// Buy this listing at the price we saw.
    Buy(i32, i32),
    Cancel(i32),
    /// Collect a box item (0: all that fit).
    Collect(i32),
    /// The vault's contents (`VaultContentsRequest`), for selling from it.
    Vault,
}

/// What the server said.
#[derive(Clone, Debug)]
pub enum Event {
    Listings(MarketListings),
    Result(MarketResult),
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum Tab {
    #[default]
    Browse,
    Sell,
    Mine,
    Box,
    History,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum Sort {
    #[default]
    Newest,
    Cheapest,
    Dearest,
    EndingSoon,
}

const SORTS: [(Sort, &str); 4] = [(Sort::Newest, "Newest"), (Sort::Cheapest, "Cheapest"), (Sort::Dearest, "Dearest"), (Sort::EndingSoon, "Ending soon")];

/// The tier filter: untiered gear (UT and set pieces), or a band of tiers.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum Tier {
    #[default]
    Any,
    Untiered,
    High,
    Mid,
    Low,
}

const TIERS: [(Tier, &str); 5] = [(Tier::Any, "Any tier"), (Tier::Untiered, "UT"), (Tier::High, "T12+"), (Tier::Mid, "T8-11"), (Tier::Low, "T7 and below")];

const CATEGORIES: [(Category, &str); 6] = [
    (Category::All, "All"),
    (Category::Weapons, "Weapons"),
    (Category::Abilities, "Abilities"),
    (Category::Armor, "Armor"),
    (Category::Rings, "Rings"),
    (Category::Other, "Other"),
];

/// How long a listing lasts: the server takes 1 hour to 7 days.
const DAYS: [u8; 3] = [1, 3, 7];
/// How often the open window asks again, and how long it waits for a first answer before
/// taking it that the server doesn't know the Marketplace's messages (an older one).
const REFRESH: i32 = 3000;
const NO_ANSWER: i32 = 5000;
/// How long a Buy waits for its second click.
const CONFIRM_MS: i32 = 3000;
/// `FAME_STAT`: the account's fame.
const FAME_STAT: u8 = 39;

/// An item that can be listed: an inventory slot, or a vault chest's slot.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Pick {
    Inv(u8),
    Vault(i32, u8),
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Tab(Tab),
    Close,
    Search,
    Price,
    Category(Category),
    Tier(Tier),
    Sort(Sort),
    Buy(i32),
    Cancel(i32),
    Collect(i32),
    CollectAll,
    Source(bool),
    Pick(Pick),
    Days(u8),
    List,
    Up,
    Down,
}

/// The Marketplace window.
#[derive(Default)]
pub struct Market {
    pub open: bool,
    tab: Tab,
    query: String,
    typing_price: bool,
    listings: Vec<MarketListing>,
    version: i32,
    /// The server's clock less ours (seconds), for the time left.
    offset: i64,
    tax: u8,
    limit: u8,
    boxed: Vec<MarketBoxItem>,
    history: Vec<MarketHistory>,
    loaded: bool,
    /// Just opened: ask on the next tick.
    fresh: bool,
    /// When we last asked, and whether that's still unanswered.
    asked: i32,
    waiting: bool,
    unsupported: bool,
    category: Category,
    tier: Tier,
    sort: Sort,
    kinds: HashMap<i32, Category>,
    /// The listings that pass the search and filters, sorted (indices into `listings`); redone
    /// when `dirty`.
    shown: Vec<usize>,
    dirty: bool,
    /// The first row of the list drawn, and how many fit.
    row: usize,
    rows: usize,
    /// A Buy clicked once: the listing and when.
    confirm: Option<(i32, i32)>,
    /// Selling: from the vault (else the inventory), the vault's items as the server sent them
    /// (chest, slot, item, imprints), whether we asked for them, the item picked, the price and
    /// the days.
    from_vault: bool,
    vault: Vec<(i32, u8, i32, [u8; 3])>,
    vault_asked: bool,
    pick: Option<(Pick, i32)>,
    price: String,
    days: u8,
    /// Our name, to tell our own listings apart.
    me: String,
    message: Option<(String, bool)>,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
    hover: Option<(i32, [u8; 3], String)>,
    now: i32,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

fn unix_now() -> i64 {
    web_time::SystemTime::now().duration_since(web_time::UNIX_EPOCH).map_or(0, |d| d.as_secs() as i64)
}

/// A fame amount with thousands separated: 12,500.
pub fn fame(n: i32) -> String {
    let digits = n.unsigned_abs().to_string();
    let mut out = String::new();
    for (i, c) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i).is_multiple_of(3) {
            out.push(',');
        }
        out.push(c);
    }
    if n < 0 {
        format!("-{out}")
    } else {
        out
    }
}

/// Time left as players read it: "2d 4h", "5h 20m", "12m", "ended".
fn left(seconds: i64) -> String {
    if seconds <= 0 {
        "ended".into()
    } else if seconds >= 86400 {
        format!("{}d {}h", seconds / 86400, seconds % 86400 / 3600)
    } else if seconds >= 3600 {
        format!("{}h {}m", seconds / 3600, seconds % 3600 / 60)
    } else {
        format!("{}m", (seconds / 60).max(1))
    }
}

/// How long ago, as the history shows it.
fn ago(seconds: i64) -> String {
    if seconds < 60 {
        "just now".into()
    } else if seconds < 3600 {
        format!("{}m ago", seconds / 60)
    } else if seconds < 86400 {
        format!("{}h ago", seconds / 3600)
    } else {
        format!("{}d ago", seconds / 86400)
    }
}

/// An item's tier as the list shows it: "T12", "UT", or nothing for the rest.
fn tier_label(data: &GameData, item: i32) -> String {
    let Some(d) = data.objects.get(&(item as u16)) else { return String::new() };
    if let Some(t) = d.xml.text_of("Tier") {
        return format!("T{}", t.trim());
    }
    let slot = d.xml.int("SlotType", 0);
    if slot != 0 && slot != 10 && !d.xml.has("Consumable") {
        return "UT".into();
    }
    String::new()
}

fn name_of(data: &GameData, item: i32) -> String {
    data.objects.get(&(item as u16)).map_or_else(|| "Unknown item".into(), |d| d.display_id.clone())
}

impl Market {
    /// Opens the window; the next tick asks for the listings.
    pub fn show(&mut self, data: &GameData) {
        self.open = true;
        self.message = None;
        self.confirm = None;
        self.typing_price = self.tab == Tab::Sell;
        if self.kinds.is_empty() {
            self.kinds = crate::forge::slot_categories(data);
        }
        if self.days == 0 {
            self.days = 3;
        }
        self.fresh = !self.unsupported;
    }

    fn ask(&mut self, now: i32, again: bool) -> Vec<Act> {
        self.asked = now;
        if !again {
            self.waiting = true;
        }
        vec![Act::Browse(if self.loaded { self.version } else { -1 })]
    }

    /// Asks again every few seconds while open; gives up on a server that never answers.
    pub fn tick(&mut self, now: i32) -> Vec<Act> {
        self.now = now;
        if let Some((_, at)) = self.confirm {
            if now - at > CONFIRM_MS {
                self.confirm = None;
            }
        }
        if !self.open || self.unsupported {
            return Vec::new();
        }
        if std::mem::take(&mut self.fresh) {
            return self.ask(now, false);
        }
        if self.waiting && now - self.asked > NO_ANSWER {
            self.waiting = false;
            if !self.loaded {
                self.unsupported = true;
                self.message = Some(("This server's Marketplace is out of date: it can't be used from here.".into(), true));
            }
            return Vec::new();
        }
        if now - self.asked > REFRESH {
            return self.ask(now, true);
        }
        Vec::new()
    }

    /// Whether `--type` goes in now: into the price, once on the Sell tab.
    pub fn takes_typing(&self) -> bool {
        self.loaded && self.tab == Tab::Sell
    }

    /// Whether the window wants the vault's contents (selling from the vault).
    pub fn wants_vault(&self) -> bool {
        self.open && self.vault_asked
    }

    pub fn vault_contents(&mut self, v: VaultContents) {
        self.vault_asked = false;
        self.vault = v
            .items
            .0
            .iter()
            .filter(|g| (g.chest as usize) < v.chests.len() && !v.chests[g.chest as usize].gift)
            .map(|g| (g.chest as i32, g.slot, g.item as i32, [g.offensive, g.defensive, g.misc]))
            .collect();
    }

    /// The server's answers.
    pub fn event(&mut self, e: Event) -> Vec<Act> {
        match e {
            Event::Listings(m) => {
                self.waiting = false;
                self.offset = m.now as i64 - unix_now();
                self.tax = m.tax;
                self.limit = m.limit;
                if !m.unchanged || !self.loaded {
                    self.listings = m.listings.0;
                    self.version = m.version;
                    self.dirty = true;
                }
                self.loaded = true;
                self.boxed = m.boxed.0;
                self.history = m.history.0;
                Vec::new()
            }
            Event::Result(r) => {
                self.message = Some((r.message, !r.ok));
                if r.ok {
                    self.pick = None;
                    self.price.clear();
                    if self.from_vault {
                        self.vault_asked = true;
                        let mut acts = self.ask(self.now, true);
                        acts.push(Act::Vault);
                        return acts;
                    }
                }
                self.ask(self.now, true)
            }
        }
    }

    fn server_now(&self) -> i64 {
        unix_now() + self.offset
    }

    fn mine(&self, l: &MarketListing) -> bool {
        !self.me.is_empty() && l.seller.eq_ignore_ascii_case(&self.me)
    }

    /// The listings that pass the search and filters, in order.
    fn refilter(&mut self, data: &GameData) {
        let q = self.query.trim().to_lowercase();
        let mut shown: Vec<usize> = (0..self.listings.len())
            .filter(|&i| {
                let l = &self.listings[i];
                let Some(d) = data.objects.get(&l.item_type) else { return false };
                if !q.is_empty() && !d.display_id.to_lowercase().contains(&q) && !l.seller.to_lowercase().contains(&q) {
                    return false;
                }
                let slot = d.xml.int("SlotType", 0);
                let cat = self.kinds.get(&slot).copied().unwrap_or(Category::Other);
                if self.category != Category::All && cat != self.category {
                    return false;
                }
                let tier = d.xml.text_of("Tier").and_then(|t| t.trim().parse::<i32>().ok());
                match self.tier {
                    Tier::Any => true,
                    Tier::Untiered => tier.is_none() && cat != Category::Other,
                    Tier::High => tier.is_some_and(|t| t >= 12),
                    Tier::Mid => tier.is_some_and(|t| (8..=11).contains(&t)),
                    Tier::Low => tier.is_some_and(|t| t <= 7),
                }
            })
            .collect();
        let ls = &self.listings;
        match self.sort {
            Sort::Newest => shown.sort_by_key(|&i| (std::cmp::Reverse(ls[i].start), std::cmp::Reverse(ls[i].id))),
            Sort::Cheapest => shown.sort_by_key(|&i| (ls[i].price, std::cmp::Reverse(ls[i].start))),
            Sort::Dearest => shown.sort_by_key(|&i| (std::cmp::Reverse(ls[i].price), std::cmp::Reverse(ls[i].start))),
            Sort::EndingSoon => shown.sort_by_key(|&i| ls[i].end),
        }
        self.shown = shown;
        self.row = self.row.min(self.shown.len().saturating_sub(1));
        self.dirty = false;
    }

    pub fn click(&mut self, pos: (f64, f64), data: &GameData) -> Vec<Act> {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.open = false;
            return Vec::new();
        }
        let Some(hit) = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h) else { return Vec::new() };
        match hit {
            Hit::Tab(t) => {
                self.tab = t;
                self.row = 0;
                self.confirm = None;
                self.typing_price = t == Tab::Sell;
                if t == Tab::Sell && self.from_vault {
                    self.vault_asked = true;
                    return vec![Act::Vault];
                }
            }
            Hit::Close => self.open = false,
            Hit::Search => self.typing_price = false,
            Hit::Price => self.typing_price = true,
            Hit::Category(c) => {
                self.category = c;
                self.dirty = true;
                self.row = 0;
            }
            Hit::Tier(t) => {
                self.tier = t;
                self.dirty = true;
                self.row = 0;
            }
            Hit::Sort(s) => {
                self.sort = s;
                self.dirty = true;
                self.row = 0;
            }
            Hit::Buy(id) => {
                let Some(l) = self.listings.iter().find(|l| l.id == id) else { return Vec::new() };
                if self.confirm.is_some_and(|(c, _)| c == id) {
                    self.confirm = None;
                    self.message = Some((format!("Buying {}...", name_of(data, l.item_type as i32)), false));
                    return vec![Act::Buy(id, l.price)];
                }
                self.confirm = Some((id, self.now));
            }
            Hit::Cancel(id) => return vec![Act::Cancel(id)],
            Hit::Collect(id) => return vec![Act::Collect(id)],
            Hit::CollectAll => return vec![Act::Collect(0)],
            Hit::Source(vault) => {
                self.from_vault = vault;
                self.pick = None;
                if vault {
                    self.vault_asked = true;
                    return vec![Act::Vault];
                }
            }
            Hit::Pick(p) => {
                let item = match p {
                    Pick::Vault(c, s) => self.vault.iter().find(|v| v.0 == c && v.1 == s).map_or(-1, |v| v.2),
                    Pick::Inv(_) => self.pick.map_or(-1, |(_, i)| i),
                };
                self.pick = if self.pick.is_some_and(|(q, _)| q == p) { None } else { Some((p, item)) };
                self.typing_price = true;
            }
            Hit::Days(d) => self.days = d,
            Hit::List => {
                let price: i32 = self.price.parse().unwrap_or(0);
                let Some((p, _)) = self.pick else {
                    self.message = Some(("Pick an item to sell first.".into(), true));
                    return Vec::new();
                };
                if price <= 0 {
                    self.message = Some(("Set a price in fame.".into(), true));
                    return Vec::new();
                }
                let hours = self.days as i32 * 24;
                return vec![match p {
                    Pick::Inv(slot) => Act::Sell { from_vault: false, chest: 0, slot, price, hours },
                    Pick::Vault(chest, slot) => Act::Sell { from_vault: true, chest, slot, price, hours },
                }];
            }
            Hit::Up => self.row = self.row.saturating_sub(self.rows.max(1)),
            Hit::Down => self.row += self.rows.max(1),
        }
        Vec::new()
    }

    pub fn scroll(&mut self, rows: i32) {
        self.row = (self.row as i32 + rows).max(0) as usize;
    }

    /// A key while the window is open: typing into the search or the price, Escape to close.
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, _data: &GameData) {
        match code {
            KeyCode::Escape => self.open = false,
            KeyCode::Backspace => {
                if self.typing_price {
                    self.price.pop();
                } else {
                    self.query.pop();
                    self.dirty = true;
                }
            }
            KeyCode::Tab => self.typing_price = !self.typing_price && self.tab == Tab::Sell,
            _ => {
                for c in text.unwrap_or("").chars() {
                    if self.typing_price {
                        if c.is_ascii_digit() && self.price.len() < 8 && !(self.price.is_empty() && c == '0') {
                            self.price.push(c);
                        }
                    } else if !c.is_control() && self.query.chars().count() < 32 {
                        self.query.push(c);
                        self.dirty = true;
                        self.row = 0;
                    }
                }
            }
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn chip(&mut self, ui: &mut Ui, r: [f32; 4], label: &str, on: bool, mouse: (f64, f64), hit: Hit, size: f32) {
        let fill = if on { color::ACCENT } else if inside(r, mouse) { color::SECONDARY } else { color::CHIP };
        theme::fill(ui.renderer, r, r[3] / 2.0, fill, 1.0);
        let lw = ui.measure(label, size, true);
        ui.label(label, r[0] + (r[2] - lw) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
        self.hits.push((hit, r));
    }

    /// A row of chips from `x`; returns the x after them.
    #[allow(clippy::too_many_arguments)]
    fn chips(&mut self, ui: &mut Ui, x: f32, y: f32, items: &[(Hit, &str, bool)], s: f32, mouse: (f64, f64)) -> f32 {
        let (h, gap, size) = (24.0 * s, 5.0 * s, (11.5 * s).round().max(9.0));
        let mut cx = x;
        for &(hit, label, on) in items {
            let w = ui.measure(label, size, true) + 16.0 * s;
            self.chip(ui, [cx, y, w, h], label, on, mouse, hit, size);
            cx += w + gap;
        }
        cx
    }

    fn field(ui: &mut Ui, r: [f32; 4], text: &str, placeholder: &str, focused: bool, s: f32, now: i32) {
        ui.field_box(r, focused, s);
        let fs = (14.0 * s).round();
        let ty = r[1] + (r[3] - fs * 1.22) / 2.0;
        if text.is_empty() && !focused {
            ui.label(placeholder, r[0] + 10.0 * s, ty, fs, false, color::TEXT_MUTED);
        } else {
            ui.label(text, r[0] + 10.0 * s, ty, fs, false, color::TEXT);
            if focused && (now / 500) % 2 == 0 {
                let cx = r[0] + 10.0 * s + ui.measure(text, fs, false) + s;
                ui.rect(cx, r[1] + 7.0 * s, (1.5 * s).max(1.0), r[3] - 14.0 * s, color::ACCENT, 1.0);
            }
        }
    }

    /// An item's slot: its icon, stack count and the hover tooltip.
    #[allow(clippy::too_many_arguments)]
    fn item(&mut self, ui: &mut Ui, r: [f32; 4], item: i32, stack: i16, imprints: [u8; 3], edge: Option<u32>, mouse: (f64, f64), s: f32, note: &str) {
        let over = inside(r, mouse);
        ui.slot_box(r, edge.or_else(|| crate::screens::tier_edge(ui.data, item)), over, s);
        ui.item_icon(item, 80, r[0] + r[2] / 2.0, r[1] + r[3] / 2.0, s * r[2] / (56.0 * s));
        if stack > 1 {
            let t = format!("{stack}");
            let fs = (11.0 * s).round().max(9.0);
            let w = ui.measure(&t, fs, true);
            ui.label(&t, r[0] + r[2] - w - 4.0 * s, r[1] + r[3] - fs * 1.3, fs, true, color::TEXT);
        }
        if over {
            self.hover = Some((item, imprints, note.to_owned()));
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.hits.clear();
        self.hover = None;
        self.now = now;
        if !self.open {
            self.rect = None;
            return;
        }
        if let Some(name) = me.and_then(|m| m.name()) {
            if self.me != name {
                self.me = name.to_owned();
                self.dirty = true;
            }
        }
        if self.dirty {
            let data = ui.data;
            self.refilter(data);
        }
        let (dw, dh) = (900.0, 600.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);

        // The title, our fame and the close button.
        ui.label("Marketplace", r[0] + 22.0 * s, r[1] + 14.0 * s, font(22.0), true, color::TEXT);
        let fame_now = me.map_or(0, |m| m.stat_int(FAME_STAT).unwrap_or(0));
        let ft = format!("{} fame", fame(fame_now));
        let fw = ui.measure(&ft, font(15.0), true);
        ui.label(&ft, r[0] + r[2] - fw - 62.0 * s, r[1] + 19.0 * s, font(15.0), true, color::ACCENT);
        let cb = [r[0] + r[2] - 42.0 * s, r[1] + 14.0 * s, 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));

        // The tabs, with counts.
        let mine = self.listings.iter().filter(|l| self.mine(l)).count();
        let tabs = [
            (Tab::Browse, format!("Browse ({})", self.listings.len())),
            (Tab::Sell, "Sell".to_owned()),
            (Tab::Mine, format!("My listings ({mine}/{})", self.limit.max(1))),
            (Tab::Box, if self.boxed.is_empty() { "Box".to_owned() } else { format!("Box ({})", self.boxed.len()) }),
            (Tab::History, "History".to_owned()),
        ];
        let mut x = r[0] + 180.0 * s;
        for (t, label) in tabs {
            let ts = font(13.0);
            let tw = ui.measure(&label, ts, true) + 26.0 * s;
            let tr = [x, r[1] + 16.0 * s, tw, 28.0 * s];
            let on = t == self.tab;
            let attention = t == Tab::Box && !self.boxed.is_empty() && !on;
            theme::fill(ui.renderer, tr, tr[3] / 2.0, if on { color::ACCENT } else if inside(tr, mouse) { color::SECONDARY } else { color::CHIP }, 1.0);
            ui.label(&label, tr[0] + 13.0 * s, tr[1] + (tr[3] - ts * 1.22) / 2.0, ts, true, if on { color::ACCENT_TEXT } else if attention { color::GOOD } else { color::TEXT_DIM });
            self.hits.push((Hit::Tab(t), tr));
            x += tw + 6.0 * s;
        }

        let body = [r[0] + 22.0 * s, r[1] + 58.0 * s, r[2] - 44.0 * s, r[3] - 104.0 * s];
        if !self.loaded {
            let t = if self.unsupported { "" } else { "Asking the Market Broker..." };
            ui.label(t, body[0], body[1] + 10.0 * s, font(14.0), false, color::TEXT_MUTED);
        } else {
            match self.tab {
                Tab::Browse => self.draw_browse(ui, body, s, mouse),
                Tab::Sell => self.draw_sell(ui, body, s, me, mouse, now),
                Tab::Mine => self.draw_mine(ui, body, s, mouse),
                Tab::Box => self.draw_box(ui, body, s, mouse),
                Tab::History => self.draw_history(ui, body, s, mouse),
            }
        }

        // The foot: the server's last word, or what the tab is about.
        let foot_y = r[1] + r[3] - 36.0 * s;
        theme::fill(ui.renderer, [r[0] + 16.0 * s, foot_y - 8.0 * s, r[2] - 32.0 * s, (1.0 * s).max(1.0)], 0.0, color::BORDER, 1.0);
        let (text, col) = match &self.message {
            Some((m, err)) => (m.clone(), if *err { color::DANGER } else { color::GOOD }),
            None => (
                match self.tab {
                    Tab::Browse => "Click Buy, then again to confirm. Items go to your inventory, or your box when it's full.".to_owned(),
                    Tab::Sell => format!("The Market Broker keeps {}% of each sale. Unsold items come back to your box.", self.tax),
                    Tab::Mine => "Taking a listing back returns it to your inventory, or your box when it's full.".to_owned(),
                    Tab::Box => "Items wait here, imprints and all, until you collect them.".to_owned(),
                    Tab::History => "Your last 50 Marketplace events.".to_owned(),
                },
                color::TEXT_MUTED,
            ),
        };
        ui.label(&text, r[0] + 22.0 * s, foot_y, font(13.0), true, col);

        if let Some((item, imprints, note)) = self.hover.clone() {
            let mut lines = crate::hud::item_tooltip_imprinted(ui.data, item, me, SlotKind::Shop, imprints);
            lines.extend(crate::hud::imprint_lines(ui.data, item, imprints));
            if !note.is_empty() {
                lines.push(crate::hud::Line::gap());
                lines.push(crate::hud::Line::new(&note, 12.0, true, color::GOOD));
            }
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }

    /// Paging buttons and "a-b of n" under a list; the rows that fit are counted by the caller.
    fn pager(&mut self, ui: &mut Ui, x: f32, y: f32, total: usize, s: f32, mouse: (f64, f64)) {
        if total <= self.rows {
            return;
        }
        let (up, down) = ([x, y, 56.0 * s, 24.0 * s], [x + 62.0 * s, y, 56.0 * s, 24.0 * s]);
        ui.button(up, "Up", Kind::Secondary, inside(up, mouse), s * 0.75);
        ui.button(down, "Down", Kind::Secondary, inside(down, mouse), s * 0.75);
        self.hits.push((Hit::Up, up));
        self.hits.push((Hit::Down, down));
        let end = (self.row + self.rows).min(total);
        let t = format!("{}-{} of {}", self.row + 1, end, total);
        ui.label(&t, x + 128.0 * s, y + 4.0 * s, (12.0 * s).round().max(9.0), false, color::TEXT_MUTED);
    }

    fn draw_browse(&mut self, ui: &mut Ui, b: [f32; 4], s: f32, mouse: (f64, f64)) {
        let font = |size: f32| (size * s).round().max(9.0);
        // The search and the filters.
        let fr = [b[0], b[1], 260.0 * s, 32.0 * s];
        let q = self.query.clone();
        Self::field(ui, fr, &q, "Search items or sellers", !self.typing_price, s, self.now);
        self.hits.push((Hit::Search, fr));
        let cats: Vec<(Hit, &str, bool)> = CATEGORIES.iter().map(|&(c, l)| (Hit::Category(c), l, c == self.category)).collect();
        self.chips(ui, fr[0] + fr[2] + 12.0 * s, b[1] + 4.0 * s, &cats, s, mouse);
        let tiers: Vec<(Hit, &str, bool)> = TIERS.iter().map(|&(t, l)| (Hit::Tier(t), l, t == self.tier)).collect();
        let x = self.chips(ui, b[0], b[1] + 42.0 * s, &tiers, s, mouse);
        ui.label("Sort", x + 10.0 * s, b[1] + 46.0 * s, font(12.0), true, color::TEXT_MUTED);
        let sorts: Vec<(Hit, &str, bool)> = SORTS.iter().map(|&(t, l)| (Hit::Sort(t), l, t == self.sort)).collect();
        self.chips(ui, x + 44.0 * s, b[1] + 42.0 * s, &sorts, s, mouse);

        // The listings.
        let top = b[1] + 78.0 * s;
        let list = [b[0], top, b[2], b[1] + b[3] - top - 30.0 * s];
        theme::frame(ui.renderer, list, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
        let row_h = 52.0 * s;
        self.rows = (((list[3] - 12.0 * s) / row_h).floor() as usize).max(1);
        self.row = self.row.min(self.shown.len().saturating_sub(self.rows));
        if self.shown.is_empty() {
            let t = if self.listings.is_empty() { "Nothing is for sale yet. Be the first: list an item on the Sell tab." } else { "Nothing matches: try another search or filter." };
            ui.label(t, list[0] + 16.0 * s, list[1] + 16.0 * s, font(14.0), false, color::TEXT_MUTED);
        }
        let now = self.server_now();
        let shown: Vec<MarketListing> = self.shown.iter().skip(self.row).take(self.rows).map(|&i| self.listings[i].clone()).collect();
        for (i, l) in shown.iter().enumerate() {
            let rr = [list[0] + 6.0 * s, list[1] + 6.0 * s + i as f32 * row_h, list[2] - 12.0 * s, row_h - 4.0 * s];
            if inside(rr, mouse) {
                theme::fill(ui.renderer, rr, 6.0 * s, 0xFFFFFF, 0.06);
            } else if i % 2 == 0 {
                theme::fill(ui.renderer, rr, 6.0 * s, 0xFFFFFF, 0.025);
            }
            let ir = [rr[0] + 4.0 * s, rr[1] + 2.0 * s, rr[3] - 4.0 * s, rr[3] - 4.0 * s];
            let imprints = [l.offensive, l.defensive, l.misc];
            let note = format!("Sold by {}", l.seller);
            self.item(ui, ir, l.item_type as i32, l.stack, imprints, None, mouse, s, &note);
            let name = name_of(ui.data, l.item_type as i32);
            let name = if l.stack > 1 { format!("{name} ×{}", l.stack) } else { name };
            let tx = ir[0] + ir[2] + 12.0 * s;
            ui.label(&name, tx, rr[1] + 6.0 * s, font(14.0), true, color::TEXT);
            let tier = tier_label(ui.data, l.item_type as i32);
            let sub = if tier.is_empty() { format!("{} · {} left", l.seller, left(l.end as i64 - now)) } else { format!("{tier} · {} · {} left", l.seller, left(l.end as i64 - now)) };
            ui.label(&sub, tx, rr[1] + 26.0 * s, font(12.0), false, color::TEXT_MUTED);
            let price = format!("{} fame", fame(l.price));
            let pw = ui.measure(&price, font(15.0), true);
            ui.label(&price, rr[0] + rr[2] - 132.0 * s - pw, rr[1] + (rr[3] - font(15.0) * 1.22) / 2.0, font(15.0), true, color::ACCENT);
            let bb = [rr[0] + rr[2] - 118.0 * s, rr[1] + 8.0 * s, 112.0 * s, rr[3] - 16.0 * s];
            if self.mine(l) {
                ui.label("Yours", bb[0] + 36.0 * s, bb[1] + 6.0 * s, font(13.0), true, color::TEXT_MUTED);
            } else {
                let confirming = self.confirm.is_some_and(|(c, _)| c == l.id);
                ui.button(bb, if confirming { "Confirm" } else { "Buy" }, if confirming { Kind::Danger } else { Kind::Primary }, inside(bb, mouse), s * 0.85);
                self.hits.push((Hit::Buy(l.id), bb));
            }
        }
        self.pager(ui, b[0], list[1] + list[3] + 4.0 * s, self.shown.len(), s, mouse);
    }

    #[allow(clippy::too_many_arguments)]
    fn draw_sell(&mut self, ui: &mut Ui, b: [f32; 4], s: f32, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let left_w = 420.0 * s;
        // Where from: the inventory or the vault.
        self.chips(ui, b[0], b[1], &[(Hit::Source(false), "Inventory", !self.from_vault), (Hit::Source(true), "Vault", self.from_vault)], s, mouse);
        let cell = 48.0 * s;
        let gap = 6.0 * s;
        let gy = b[1] + 36.0 * s;
        let mut items: Vec<(Pick, i32, [u8; 3], String)> = Vec::new();
        if self.from_vault {
            for &(chest, slot, item, imprints) in &self.vault {
                items.push((Pick::Vault(chest, slot), item, imprints, format!("In vault chest {}", chest + 1)));
            }
        } else if let Some(me) = me {
            let backpack = me.stat_int(crate::hud::stat::HAS_BACKPACK).unwrap_or(0) != 0;
            for slot in 4..if backpack { 20 } else { 12 } {
                let item = me.stat_int(crate::hud::slot_stat(slot)).unwrap_or(-1);
                if item > 0 {
                    items.push((Pick::Inv(slot), item, [0; 3], if slot < 12 { format!("Inventory slot {}", slot - 3) } else { format!("Backpack slot {}", slot - 11) }));
                }
            }
        }
        let cols = ((left_w + gap) / (cell + gap)).floor().max(1.0) as usize;
        let max_rows = ((b[3] - 36.0 * s - 30.0 * s) / (cell + gap)).floor().max(1.0) as usize;
        if items.is_empty() {
            let t = if self.from_vault && self.vault_asked { "Looking in your vault..." } else if self.from_vault { "Your vault is empty, or open: list from it once you've left it." } else { "Your inventory is empty." };
            ui.label(t, b[0], gy + 6.0 * s, font(13.0), false, color::TEXT_MUTED);
        }
        let start = if self.from_vault { self.row * cols } else { 0 };
        self.rows = max_rows;
        for (i, (pick, item, imprints, note)) in items.iter().skip(start).take(cols * max_rows).enumerate() {
            let sr = [b[0] + (i % cols) as f32 * (cell + gap), gy + (i / cols) as f32 * (cell + gap), cell, cell];
            let picked = self.pick.is_some_and(|(p, _)| p == *pick);
            let untradeable = ui.data.objects.get(&(*item as u16)).is_some_and(|d| d.untradeable());
            self.item(ui, sr, *item, 0, *imprints, if picked { Some(color::GOOD) } else { None }, mouse, s, note);
            if picked {
                theme::fill(ui.renderer, [sr[0] + s, sr[1] + s, sr[2] - 2.0 * s, sr[3] - 2.0 * s], 4.0 * s, color::GOOD, 0.2);
            }
            if untradeable {
                theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.6);
            } else {
                self.hits.push((Hit::Pick(*pick), sr));
            }
        }
        if self.from_vault && items.len() > cols * max_rows {
            let total_rows = items.len().div_ceil(cols);
            self.row = self.row.min(total_rows.saturating_sub(max_rows));
            self.pager(ui, b[0], b[1] + b[3] - 26.0 * s, total_rows, s, mouse);
        }
        // Remember the item behind an inventory pick, for the summary.
        if let Some((Pick::Inv(slot), _)) = self.pick {
            if let Some(&(_, item, _, _)) = items.iter().find(|(p, ..)| *p == Pick::Inv(slot)) {
                self.pick = Some((Pick::Inv(slot), item));
            } else {
                self.pick = None;
            }
        }

        // The listing: the item, the price, the days, what we'd get.
        let px = b[0] + left_w + 24.0 * s;
        let pr = [px, b[1], b[0] + b[2] - px, b[3]];
        theme::frame(ui.renderer, pr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
        let ix = pr[0] + 18.0 * s;
        let mut y = pr[1] + 16.0 * s;
        match self.pick {
            Some((_, item)) if item > 0 => {
                let ir = [ix, y, 56.0 * s, 56.0 * s];
                self.item(ui, ir, item, 0, [0; 3], Some(color::ACCENT), mouse, s, "");
                ui.label(&name_of(ui.data, item), ix + 68.0 * s, y + 8.0 * s, font(16.0), true, color::TEXT);
                let tier = tier_label(ui.data, item);
                let sub = if self.from_vault { format!("{tier} · from your vault") } else { format!("{tier} · from your inventory") };
                ui.label(sub.trim_start_matches(" · "), ix + 68.0 * s, y + 32.0 * s, font(12.0), false, color::TEXT_MUTED);
            }
            _ => ui.label("Pick an item on the left to sell it.", ix, y + 18.0 * s, font(14.0), false, color::TEXT_MUTED),
        }
        y += 76.0 * s;
        ui.label("Price in fame", ix, y, font(12.0), true, color::TEXT_DIM);
        let fr = [ix, y + 20.0 * s, pr[2] - 36.0 * s, 34.0 * s];
        let price = self.price.clone();
        let shown = price.parse::<i32>().map_or(String::new(), fame);
        Self::field(ui, fr, &shown, "How much fame?", self.typing_price, s, now);
        self.hits.push((Hit::Price, fr));
        y += 66.0 * s;
        ui.label("Lasts", ix, y, font(12.0), true, color::TEXT_DIM);
        let days: Vec<(Hit, String, bool)> = DAYS.iter().map(|&d| (Hit::Days(d), if d == 1 { "1 day".to_owned() } else { format!("{d} days") }, d == self.days)).collect();
        let days_ref: Vec<(Hit, &str, bool)> = days.iter().map(|(h, l, on)| (*h, l.as_str(), *on)).collect();
        self.chips(ui, ix, y + 20.0 * s, &days_ref, s, mouse);
        y += 60.0 * s;
        let p: i32 = self.price.parse().unwrap_or(0);
        if p > 0 {
            let net = p - p * self.tax as i32 / 100;
            ui.label(&format!("Buyers pay {} fame", fame(p)), ix, y, font(13.0), false, color::TEXT_DIM);
            ui.label(&format!("You get {} fame ({}% tax)", fame(net), self.tax), ix, y + 20.0 * s, font(14.0), true, color::ACCENT);
        }
        let mine = self.listings.iter().filter(|l| self.mine(l)).count();
        ui.label(&format!("{mine} of {} listings in use", self.limit), ix, pr[1] + pr[3] - 92.0 * s, font(12.0), false, color::TEXT_MUTED);
        let lb = [ix, pr[1] + pr[3] - 66.0 * s, pr[2] - 36.0 * s, 44.0 * s];
        ui.button(lb, "List on the Marketplace", Kind::Primary, inside(lb, mouse), s);
        self.hits.push((Hit::List, lb));
    }

    fn draw_mine(&mut self, ui: &mut Ui, b: [f32; 4], s: f32, mouse: (f64, f64)) {
        let font = |size: f32| (size * s).round().max(9.0);
        let now = self.server_now();
        let mine: Vec<MarketListing> = self.listings.iter().filter(|l| self.mine(l)).cloned().collect();
        theme::frame(ui.renderer, b, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
        if mine.is_empty() {
            ui.label("You're not selling anything. List an item on the Sell tab.", b[0] + 16.0 * s, b[1] + 16.0 * s, font(14.0), false, color::TEXT_MUTED);
        }
        let row_h = 52.0 * s;
        for (i, l) in mine.iter().enumerate() {
            let rr = [b[0] + 6.0 * s, b[1] + 6.0 * s + i as f32 * row_h, b[2] - 12.0 * s, row_h - 4.0 * s];
            if rr[1] + rr[3] > b[1] + b[3] {
                break;
            }
            if i % 2 == 0 {
                theme::fill(ui.renderer, rr, 6.0 * s, 0xFFFFFF, 0.025);
            }
            let ir = [rr[0] + 4.0 * s, rr[1] + 2.0 * s, rr[3] - 4.0 * s, rr[3] - 4.0 * s];
            self.item(ui, ir, l.item_type as i32, l.stack, [l.offensive, l.defensive, l.misc], None, mouse, s, "");
            let tx = ir[0] + ir[2] + 12.0 * s;
            ui.label(&name_of(ui.data, l.item_type as i32), tx, rr[1] + 6.0 * s, font(14.0), true, color::TEXT);
            let net = l.price - l.price * self.tax as i32 / 100;
            ui.label(&format!("{} fame · you get {} · {} left", fame(l.price), fame(net), left(l.end as i64 - now)), tx, rr[1] + 26.0 * s, font(12.0), false, color::TEXT_MUTED);
            let bb = [rr[0] + rr[2] - 118.0 * s, rr[1] + 8.0 * s, 112.0 * s, rr[3] - 16.0 * s];
            ui.button(bb, "Take back", Kind::Danger, inside(bb, mouse), s * 0.85);
            self.hits.push((Hit::Cancel(l.id), bb));
        }
    }

    fn draw_box(&mut self, ui: &mut Ui, b: [f32; 4], s: f32, mouse: (f64, f64)) {
        let font = |size: f32| (size * s).round().max(9.0);
        let now = self.server_now();
        let list = [b[0], b[1], b[2], b[3] - 50.0 * s];
        theme::frame(ui.renderer, list, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
        if self.boxed.is_empty() {
            ui.label("Your box is empty.", list[0] + 16.0 * s, list[1] + 16.0 * s, font(14.0), false, color::TEXT_MUTED);
        }
        let row_h = 52.0 * s;
        let boxed = self.boxed.clone();
        for (i, it) in boxed.iter().enumerate() {
            let rr = [list[0] + 6.0 * s, list[1] + 6.0 * s + i as f32 * row_h, list[2] - 12.0 * s, row_h - 4.0 * s];
            if rr[1] + rr[3] > list[1] + list[3] {
                break;
            }
            if i % 2 == 0 {
                theme::fill(ui.renderer, rr, 6.0 * s, 0xFFFFFF, 0.025);
            }
            let ir = [rr[0] + 4.0 * s, rr[1] + 2.0 * s, rr[3] - 4.0 * s, rr[3] - 4.0 * s];
            self.item(ui, ir, it.item_type as i32, it.stack, [it.offensive, it.defensive, it.misc], None, mouse, s, "");
            let tx = ir[0] + ir[2] + 12.0 * s;
            ui.label(&name_of(ui.data, it.item_type as i32), tx, rr[1] + 6.0 * s, font(14.0), true, color::TEXT);
            let why = match it.why {
                1 => "Bought",
                2 => "Taken back",
                3 => "Didn't sell in time",
                _ => "Waiting",
            };
            ui.label(&format!("{why} · {}", ago(now - it.time as i64)), tx, rr[1] + 26.0 * s, font(12.0), false, color::TEXT_MUTED);
            let bb = [rr[0] + rr[2] - 118.0 * s, rr[1] + 8.0 * s, 112.0 * s, rr[3] - 16.0 * s];
            ui.button(bb, "Collect", Kind::Primary, inside(bb, mouse), s * 0.85);
            self.hits.push((Hit::Collect(it.box_id), bb));
        }
        if !self.boxed.is_empty() {
            let ab = [b[0] + b[2] - 200.0 * s, b[1] + b[3] - 40.0 * s, 200.0 * s, 36.0 * s];
            ui.button(ab, "Collect all that fit", Kind::Primary, inside(ab, mouse), s * 0.9);
            self.hits.push((Hit::CollectAll, ab));
        }
    }

    fn draw_history(&mut self, ui: &mut Ui, b: [f32; 4], s: f32, mouse: (f64, f64)) {
        let font = |size: f32| (size * s).round().max(9.0);
        let now = self.server_now();
        theme::frame(ui.renderer, b, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
        if self.history.is_empty() {
            ui.label("Nothing yet.", b[0] + 16.0 * s, b[1] + 16.0 * s, font(14.0), false, color::TEXT_MUTED);
        }
        let row_h = 40.0 * s;
        self.rows = (((b[3] - 40.0 * s) / row_h).floor() as usize).max(1);
        self.row = self.row.min(self.history.len().saturating_sub(self.rows));
        let history: Vec<MarketHistory> = self.history.iter().skip(self.row).take(self.rows).cloned().collect();
        for (i, h) in history.iter().enumerate() {
            let rr = [b[0] + 6.0 * s, b[1] + 6.0 * s + i as f32 * row_h, b[2] - 12.0 * s, row_h - 4.0 * s];
            if i % 2 == 0 {
                theme::fill(ui.renderer, rr, 6.0 * s, 0xFFFFFF, 0.025);
            }
            let ir = [rr[0] + 4.0 * s, rr[1] + 2.0 * s, rr[3] - 4.0 * s, rr[3] - 4.0 * s];
            self.item(ui, ir, h.item_type as i32, 0, [0; 3], None, mouse, s, "");
            let name = name_of(ui.data, h.item_type as i32);
            let (text, col) = match h.kind {
                1 => (format!("Listed {name} for {} fame", fame(h.price)), color::TEXT_DIM),
                2 => (format!("Sold {name} to {} for {} fame: you got {}", h.other, fame(h.price), fame(h.net)), color::GOOD),
                3 => (format!("Bought {name} from {} for {} fame", h.other, fame(h.price)), color::ACCENT),
                4 => (format!("Took back {name}"), color::TEXT_DIM),
                5 => (format!("{name} didn't sell in time"), color::TEXT_MUTED),
                _ => (name, color::TEXT_DIM),
            };
            let tx = ir[0] + ir[2] + 10.0 * s;
            ui.label(&text, tx, rr[1] + (rr[3] - font(13.0) * 1.22) / 2.0, font(13.0), true, col);
            let t = ago(now - h.time as i64);
            let tw = ui.measure(&t, font(12.0), false);
            ui.label(&t, rr[0] + rr[2] - tw - 10.0 * s, rr[1] + (rr[3] - font(12.0) * 1.22) / 2.0, font(12.0), false, color::TEXT_MUTED);
        }
        self.pager(ui, b[0], b[1] + b[3] - 28.0 * s, self.history.len(), s, mouse);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use bsk_proto::List16;

    fn listing(id: i32, price: i32, start: i32, end: i32, seller: &str) -> MarketListing {
        MarketListing { id, item_type: 1, price, seller: seller.into(), start, end, stack: 0, offensive: 0, defensive: 0, misc: 0 }
    }

    fn answer(listings: Vec<MarketListing>, version: i32, unchanged: bool) -> Event {
        Event::Listings(MarketListings { version, now: 1000, unchanged, tax: 5, limit: 10, listings: List16(listings), boxed: List16(Vec::new()), history: List16(Vec::new()) })
    }

    #[test]
    fn fame_and_time_read_as_players_read_them() {
        assert_eq!(fame(1234567), "1,234,567");
        assert_eq!(fame(950), "950");
        assert_eq!(fame(-12000), "-12,000");
        assert_eq!(left(2 * 86400 + 4 * 3600 + 5), "2d 4h");
        assert_eq!(left(3 * 3600 + 20 * 60), "3h 20m");
        assert_eq!(left(30), "1m");
        assert_eq!(left(-5), "ended");
    }

    #[test]
    fn listings_are_kept_until_the_version_moves_and_buying_asks_twice() {
        let mut m = Market { open: true, ..Default::default() };
        assert_eq!(m.ask(0, false), vec![Act::Browse(-1)]);
        m.event(answer(vec![listing(1, 50, 1, 9, "A"), listing(2, 10, 3, 5, "B")], 4, false));
        assert_eq!((m.listings.len(), m.version), (2, 4));
        // unchanged: the listings stay as they were
        m.event(answer(Vec::new(), 4, true));
        assert_eq!(m.listings.len(), 2);
        assert_eq!(m.ask(10, true), vec![Act::Browse(4)]);
        // sorted by price
        m.sort = Sort::Cheapest;
        m.shown = vec![0, 1];
        let ls = &m.listings;
        let mut shown = m.shown.clone();
        shown.sort_by_key(|&i| ls[i].price);
        assert_eq!(shown, vec![1, 0]);
        // Buy: a first click only asks to confirm
        m.hits = vec![(Hit::Buy(2), [0.0, 0.0, 10.0, 10.0])];
        let data = GameData::default();
        assert!(m.click((5.0, 5.0), &data).is_empty());
        assert_eq!(m.click((5.0, 5.0), &data), vec![Act::Buy(2, 10)]);
    }

    #[test]
    fn a_server_that_never_answers_is_given_up_on() {
        let mut m = Market { open: true, ..Default::default() };
        m.ask(0, false);
        assert!(m.tick(NO_ANSWER + 1).is_empty());
        assert!(m.unsupported);
        assert!(m.message.as_ref().is_some_and(|(_, err)| *err));
    }
}
