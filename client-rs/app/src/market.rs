//! The marketplace (`MemMarket`, its Buy and Sell tabs), opened from the market NPC.
//!
//! Buy: find an item by name, see who sells it and for how much, and buy. Sell: pick items
//! from the inventory, set a price in fame, and put them up for a day; your offers are listed
//! with a button to take each back. The server checks everything; its answers show at the
//! bottom of the window.

use crate::hud::{Layout, Ui};
use crate::theme::{self, color, Kind};
use bsk_data::GameData;
use bsk_proto::MarketData;
use bsk_world::GameObj;
use winit::keyboard::KeyCode;

/// What the player asked of the market.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Act {
    /// The offers for an item type.
    Search(i32),
    Buy(i32),
    /// Sell the items in these slots for this much fame, each.
    Add(Vec<u8>, i32),
    Remove(i32),
    MyOffers,
}

/// What the server said.
#[derive(Clone, Debug)]
pub enum Event {
    Search(Vec<MarketData>, String),
    Buy(i32, String, i32),
    Add(i32, String),
    Remove(i32, String),
    MyOffers(Vec<MarketData>),
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum Tab {
    #[default]
    Buy,
    Sell,
}

/// `SortUtils.SORT_CHOICES`.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum Sort {
    #[default]
    Cheapest,
    Dearest,
    Newest,
    EndingSoon,
}

impl Sort {
    fn label(self) -> &'static str {
        match self {
            Sort::Cheapest => "Lowest price",
            Sort::Dearest => "Highest price",
            Sort::Newest => "Just added",
            Sort::EndingSoon => "Ending soon",
        }
    }

    fn next(self) -> Self {
        match self {
            Sort::Cheapest => Sort::Dearest,
            Sort::Dearest => Sort::Newest,
            Sort::Newest => Sort::EndingSoon,
            Sort::EndingSoon => Sort::Cheapest,
        }
    }

    fn apply(self, offers: &mut [MarketData]) {
        match self {
            Sort::Cheapest => offers.sort_by_key(|o| o.price),
            Sort::Dearest => offers.sort_by_key(|o| std::cmp::Reverse(o.price)),
            Sort::Newest => offers.sort_by_key(|o| std::cmp::Reverse(o.start_time)),
            Sort::EndingSoon => offers.sort_by_key(|o| o.time_left),
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Tab(Tab),
    Close,
    SearchField,
    PriceField,
    Item(u16),
    ItemsUp,
    ItemsDown,
    Buy(i32),
    Sort,
    Slot(u8),
    Sell,
    Remove(i32),
}

/// `GeneralUtils.isBanned`: what the market doesn't take.
pub fn is_banned(data: &GameData, item: u16) -> bool {
    const NO_MARKET: [u16; 11] = [0xa22, 0xa23, 0x5000, 0xab0, 0xaeb, 0x223b, 0x225e, 0x225b, 0x224c, 0x223c, 0x223d];
    const WEAPONS: [i32; 6] = [1, 2, 3, 8, 17, 24];
    const ABILITIES: [i32; 15] = [4, 5, 9, 11, 12, 13, 15, 16, 18, 19, 20, 21, 22, 23, 25];
    const ARMOR: [i32; 3] = [6, 7, 14];
    if NO_MARKET.contains(&item) {
        return true;
    }
    let Some(d) = data.objects.get(&item) else { return true };
    if d.untradeable() {
        return true;
    }
    let slot = d.xml.int("SlotType", 0);
    if slot == 10 {
        return !d.xml.has("Potion");
    }
    if d.xml.has("Tier") {
        let tier = d.xml.int("Tier", 0);
        if WEAPONS.contains(&slot) {
            return tier <= 7;
        }
        if ABILITIES.contains(&slot) {
            return tier <= 3;
        }
        if ARMOR.contains(&slot) {
            return tier <= 8;
        }
    }
    false
}

/// The market window.
#[derive(Default)]
pub struct Market {
    pub open: bool,
    tab: Tab,
    query: String,
    /// Items that can be sold whose names hold the query, by name; and the first row shown.
    matches: Vec<u16>,
    row: usize,
    /// The item whose offers show, and them.
    item: Option<u16>,
    offers: Vec<MarketData>,
    sort: Sort,
    /// The Sell tab: the price typed, the slots picked, our offers.
    price: String,
    picked: Vec<u8>,
    mine: Vec<MarketData>,
    /// Which field takes typing: the search (false) or the price (true).
    typing_price: bool,
    /// The server's last word: (text, is it an error).
    message: Option<(String, bool)>,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
    hover_item: Option<i32>,
    /// Rows of items that fit, as last drawn (a page).
    rows: usize,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// Hours until a Unix time.
fn hours_left(until: i32) -> f64 {
    let now = web_time::SystemTime::now().duration_since(web_time::UNIX_EPOCH).map_or(0, |d| d.as_secs() as i64);
    ((until as i64 - now) as f64 / 3600.0).max(0.0)
}

const COLS: usize = 4;

impl Market {
    pub fn show(&mut self, data: &GameData) {
        *self = Market { open: true, ..Default::default() };
        self.find(data);
    }

    /// The items whose names hold the query (all of them for an empty one).
    fn find(&mut self, data: &GameData) {
        let q = self.query.trim().to_lowercase();
        let mut items: Vec<(&str, u16)> = data
            .objects
            .values()
            .filter(|d| d.xml.has("Item") && !is_banned(data, d.object_type))
            .filter(|d| q.is_empty() || d.display_id.to_lowercase().contains(&q))
            .map(|d| (d.display_id.as_str(), d.object_type))
            .collect();
        items.sort();
        self.matches = items.into_iter().map(|(_, t)| t).collect();
        self.row = 0;
    }

    /// The server's answers.
    pub fn event(&mut self, e: Event) -> Vec<Act> {
        match e {
            Event::Search(mut offers, why) => {
                self.sort.apply(&mut offers);
                self.offers = offers;
                if !why.is_empty() {
                    self.message = Some((why, true));
                }
            }
            Event::Buy(code, why, id) => {
                if code == -1 {
                    self.offers.retain(|o| o.id != id);
                    self.message = Some((if why.is_empty() { "Bought!".into() } else { why }, false));
                } else {
                    self.message = Some((why, true));
                }
            }
            Event::Add(code, why) => {
                self.message = Some((why, code != -1));
                if code == -1 {
                    self.picked.clear();
                    return vec![Act::MyOffers];
                }
            }
            Event::Remove(code, why) => {
                if !why.is_empty() {
                    self.message = Some((why, code != -1));
                }
                return vec![Act::MyOffers];
            }
            Event::MyOffers(mut offers) => {
                Sort::Newest.apply(&mut offers);
                self.mine = offers;
            }
        }
        Vec::new()
    }

    pub fn click(&mut self, pos: (f64, f64)) -> Option<Act> {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.open = false;
            return None;
        }
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h)?;
        match hit {
            Hit::Tab(t) => {
                self.tab = t;
                self.typing_price = t == Tab::Sell;
                self.message = None;
                if t == Tab::Sell {
                    return Some(Act::MyOffers);
                }
            }
            Hit::Close => self.open = false,
            Hit::SearchField => self.typing_price = false,
            Hit::PriceField => self.typing_price = true,
            Hit::Item(t) => {
                self.item = Some(t);
                self.offers.clear();
                self.message = None;
                return Some(Act::Search(t as i32));
            }
            Hit::ItemsUp => self.row = self.row.saturating_sub(self.rows.max(1)),
            Hit::ItemsDown => {
                let rows = self.matches.len().div_ceil(COLS);
                self.row = (self.row + self.rows.max(1)).min(rows.saturating_sub(self.rows.max(1)));
            }
            Hit::Buy(id) => return Some(Act::Buy(id)),
            Hit::Sort => {
                self.sort = self.sort.next();
                self.sort.apply(&mut self.offers);
            }
            Hit::Slot(s) => {
                if let Some(i) = self.picked.iter().position(|&p| p == s) {
                    self.picked.remove(i);
                } else {
                    self.picked.push(s);
                }
            }
            Hit::Sell => {
                let price: i32 = self.price.parse().unwrap_or(0);
                if price <= 0 {
                    self.message = Some(("Set a price first.".into(), true));
                } else if self.picked.is_empty() {
                    self.message = Some(("Pick at least one item to sell.".into(), true));
                } else {
                    return Some(Act::Add(self.picked.clone(), price));
                }
            }
            Hit::Remove(id) => return Some(Act::Remove(id)),
        }
        None
    }

    /// A key while the window is open: typing into the search or the price, Enter to search,
    /// Escape to close.
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, data: &GameData) {
        match code {
            KeyCode::Escape => self.open = false,
            KeyCode::Enter | KeyCode::NumpadEnter if !self.typing_price => self.find(data),
            KeyCode::Backspace => {
                if self.typing_price {
                    self.price.pop();
                } else {
                    self.query.pop();
                }
            }
            _ => {
                for c in text.unwrap_or("").chars() {
                    if self.typing_price {
                        if c.is_ascii_digit() && self.price.len() < 7 {
                            self.price.push(c);
                        }
                    } else if !c.is_control() && self.query.chars().count() < 32 {
                        self.query.push(c);
                    }
                }
            }
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.hits.clear();
        self.hover_item = None;
        if !self.open {
            self.rect = None;
            return;
        }
        let (dw, dh) = (780.0, 540.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label("Marketplace", r[0] + 22.0 * s, r[1] + 16.0 * s, font(22.0), true, color::TEXT);
        // Tabs.
        let mut x = r[0] + 200.0 * s;
        for (t, label) in [(Tab::Buy, "Buy"), (Tab::Sell, "Sell")] {
            let tr = [x, r[1] + 18.0 * s, 96.0 * s, 28.0 * s];
            let on = t == self.tab;
            theme::fill(ui.renderer, tr, tr[3] / 2.0, if on { color::ACCENT } else if inside(tr, mouse) { color::SECONDARY } else { color::CHIP }, 1.0);
            let ts = font(13.0);
            let lw = ui.measure(label, ts, true);
            ui.label(label, tr[0] + (tr[2] - lw) / 2.0, tr[1] + (tr[3] - ts * 1.22) / 2.0, ts, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
            self.hits.push((Hit::Tab(t), tr));
            x += tr[2] + 8.0 * s;
        }
        // The account's fame (FAME_STAT), which the market takes.
        let fame = me.map_or(0, |m| m.stat_int(39).unwrap_or(0));
        let ft = format!("{fame} fame");
        let fw = ui.measure(&ft, font(14.0), true);
        ui.label(&ft, r[0] + r[2] - fw - 70.0 * s, r[1] + 22.0 * s, font(14.0), true, color::ACCENT);
        // Close.
        let cb = [r[0] + r[2] - 42.0 * s, r[1] + 14.0 * s, 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));

        let left = [r[0] + 22.0 * s, r[1] + 62.0 * s, 250.0 * s, r[3] - 110.0 * s];
        let right = [left[0] + left[2] + 20.0 * s, left[1], r[2] - left[2] - 64.0 * s, left[3]];
        match self.tab {
            Tab::Buy => self.draw_buy(ui, left, right, s, mouse, now),
            Tab::Sell => self.draw_sell(ui, left, right, s, me, mouse, now),
        }
        // The server's word, and the item under the mouse.
        let foot_y = r[1] + r[3] - 36.0 * s;
        if let Some((m, err)) = &self.message {
            ui.label(m, r[0] + 22.0 * s, foot_y, font(13.0), true, if *err { color::DANGER } else { color::GOOD });
        } else if let Some(item) = self.hover_item {
            let name = ui.data.objects.get(&(item as u16)).map_or(String::new(), |d| d.display_id.clone());
            ui.label(&name, r[0] + 22.0 * s, foot_y, font(13.0), true, color::TEXT_DIM);
        }
    }

    fn field(&mut self, ui: &mut Ui, r: [f32; 4], text: &str, placeholder: &str, focused: bool, s: f32, now: i32) {
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

    fn draw_buy(&mut self, ui: &mut Ui, left: [f32; 4], right: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        // The search, and the items it finds.
        let fr = [left[0], left[1], left[2], 34.0 * s];
        let q = self.query.clone();
        self.field(ui, fr, &q, "Search items (Enter)", !self.typing_price, s, now);
        self.hits.push((Hit::SearchField, fr));
        let cell = 56.0 * s;
        let gy = fr[1] + fr[3] + 10.0 * s;
        // As many rows as fit above the paging buttons.
        let by = left[1] + left[3] - 30.0 * s;
        self.rows = (((by - 8.0 * s - gy) / (cell + 6.0 * s)).floor() as usize).max(1);
        for (i, &t) in self.matches.iter().skip(self.row * COLS).take(COLS * self.rows).enumerate() {
            let sr = [left[0] + (i % COLS) as f32 * (cell + 8.0 * s), gy + (i / COLS) as f32 * (cell + 6.0 * s), cell, cell];
            let over = inside(sr, mouse);
            let edge = if self.item == Some(t) { Some(color::ACCENT) } else { crate::screens::tier_edge(ui.data, t as i32) };
            ui.slot_box(sr, edge, over, s);
            ui.item_icon(t as i32, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
            if over {
                self.hover_item = Some(t as i32);
            }
            self.hits.push((Hit::Item(t), sr));
        }
        if self.matches.is_empty() {
            ui.label("Nothing by that name.", left[0], gy + 6.0 * s, font(13.0), false, color::TEXT_MUTED);
        }
        // Paging.
        let (up, down) = ([left[0], by, 60.0 * s, 28.0 * s], [left[0] + 68.0 * s, by, 60.0 * s, 28.0 * s]);
        ui.button(up, "Up", Kind::Secondary, inside(up, mouse), s * 0.8);
        ui.button(down, "Down", Kind::Secondary, inside(down, mouse), s * 0.8);
        self.hits.push((Hit::ItemsUp, up));
        self.hits.push((Hit::ItemsDown, down));
        let page = format!("{} items", self.matches.len());
        ui.label(&page, left[0] + 140.0 * s, by + 6.0 * s, font(12.0), false, color::TEXT_MUTED);

        // The offers for the item picked.
        theme::frame(ui.renderer, right, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
        let Some(item) = self.item else {
            ui.label("Pick an item to see its offers.", right[0] + 16.0 * s, right[1] + 16.0 * s, font(14.0), false, color::TEXT_MUTED);
            return;
        };
        let name = ui.data.objects.get(&item).map_or(String::new(), |d| d.display_id.clone());
        ui.label(&name, right[0] + 16.0 * s, right[1] + 12.0 * s, font(16.0), true, color::TEXT);
        let sb = [right[0] + right[2] - 150.0 * s, right[1] + 8.0 * s, 136.0 * s, 26.0 * s];
        ui.button(sb, self.sort.label(), Kind::Secondary, inside(sb, mouse), s * 0.8);
        self.hits.push((Hit::Sort, sb));
        if self.offers.is_empty() {
            ui.label("No one is selling this.", right[0] + 16.0 * s, right[1] + 50.0 * s, font(13.0), false, color::TEXT_MUTED);
        }
        let row_h = 44.0 * s;
        let fit = ((right[3] - 50.0 * s) / row_h).floor().max(1.0) as usize;
        for (i, o) in self.offers.clone().iter().take(fit).enumerate() {
            let rr = [right[0] + 8.0 * s, right[1] + 44.0 * s + i as f32 * row_h, right[2] - 16.0 * s, row_h - 4.0 * s];
            if i % 2 == 0 {
                theme::fill(ui.renderer, rr, 5.0 * s, 0xFFFFFF, 0.03);
            }
            ui.item_icon(o.item_type as i32, 80, rr[0] + 20.0 * s, rr[1] + rr[3] / 2.0, s * 0.8);
            let ty = rr[1] + (rr[3] - font(14.0) * 1.22) / 2.0;
            ui.label(&o.seller_name, rr[0] + 46.0 * s, ty, font(14.0), true, color::TEXT);
            ui.label(&format!("{:.1}h left", hours_left(o.time_left)), rr[0] + 200.0 * s, ty, font(12.0), false, color::TEXT_MUTED);
            let price = format!("{} fame", o.price);
            ui.label(&price, rr[0] + rr[2] - 220.0 * s, ty, font(14.0), true, color::ACCENT);
            let bb = [rr[0] + rr[2] - 96.0 * s, rr[1] + 5.0 * s, 90.0 * s, rr[3] - 10.0 * s];
            ui.button(bb, "Buy", Kind::Primary, inside(bb, mouse), s * 0.85);
            self.hits.push((Hit::Buy(o.id), bb));
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn draw_sell(&mut self, ui: &mut Ui, left: [f32; 4], right: [f32; 4], s: f32, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label("Pick items to sell", left[0], left[1], font(13.0), true, color::TEXT_DIM);
        let Some(me) = me else { return };
        let backpack = me.stat_int(crate::hud::stat::HAS_BACKPACK).unwrap_or(0) != 0;
        let slots: Vec<u8> = (4..if backpack { 20 } else { 12 }).collect();
        let cell = 56.0 * s;
        for (i, &slot) in slots.iter().enumerate() {
            let sr = [left[0] + (i % 4) as f32 * (cell + 8.0 * s), left[1] + 24.0 * s + (i / 4) as f32 * (cell + 6.0 * s), cell, cell];
            let item = me.stat_int(crate::hud::slot_stat(slot)).unwrap_or(-1);
            let picked = self.picked.contains(&slot);
            let sellable = item > 0 && !is_banned(ui.data, item as u16);
            let edge = if picked { Some(color::GOOD) } else if item > 0 { crate::screens::tier_edge(ui.data, item) } else { None };
            ui.slot_box(sr, edge, inside(sr, mouse) && sellable, s);
            if item > 0 {
                ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
                if !sellable {
                    theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.55);
                }
                if inside(sr, mouse) {
                    self.hover_item = Some(item);
                }
            }
            if picked {
                theme::fill(ui.renderer, [sr[0] + s, sr[1] + s, sr[2] - 2.0 * s, sr[3] - 2.0 * s], 4.0 * s, color::GOOD, 0.18);
            }
            if sellable {
                self.hits.push((Hit::Slot(slot), sr));
            }
        }
        let py = left[1] + left[3] - 80.0 * s;
        ui.label("Price, fame each", left[0], py - 22.0 * s, font(12.0), false, color::TEXT_DIM);
        let pr = [left[0], py, left[2], 34.0 * s];
        let price = self.price.clone();
        self.field(ui, pr, &price, "Price", self.typing_price, s, now);
        self.hits.push((Hit::PriceField, pr));
        let sb = [left[0], py + 42.0 * s, left[2], 36.0 * s];
        let label = if self.picked.is_empty() { "Sell for a day".to_owned() } else { format!("Sell {} for a day", self.picked.len()) };
        ui.button(sb, &label, Kind::Primary, inside(sb, mouse), s);
        self.hits.push((Hit::Sell, sb));

        // Our offers.
        theme::frame(ui.renderer, right, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::INSET_TOP, 0.9), (color::INSET_BOTTOM, 0.9)));
        ui.label("Your offers", right[0] + 16.0 * s, right[1] + 12.0 * s, font(16.0), true, color::TEXT);
        if self.mine.is_empty() {
            ui.label("You're not selling anything.", right[0] + 16.0 * s, right[1] + 50.0 * s, font(13.0), false, color::TEXT_MUTED);
        }
        let row_h = 44.0 * s;
        let fit = ((right[3] - 50.0 * s) / row_h).floor().max(1.0) as usize;
        for (i, o) in self.mine.clone().iter().take(fit).enumerate() {
            let rr = [right[0] + 8.0 * s, right[1] + 44.0 * s + i as f32 * row_h, right[2] - 16.0 * s, row_h - 4.0 * s];
            if i % 2 == 0 {
                theme::fill(ui.renderer, rr, 5.0 * s, 0xFFFFFF, 0.03);
            }
            ui.item_icon(o.item_type as i32, 80, rr[0] + 20.0 * s, rr[1] + rr[3] / 2.0, s * 0.8);
            let ty = rr[1] + (rr[3] - font(14.0) * 1.22) / 2.0;
            let name = ui.data.objects.get(&o.item_type).map_or(String::new(), |d| d.display_id.clone());
            ui.label(&name, rr[0] + 46.0 * s, ty, font(13.0), true, color::TEXT);
            ui.label(&format!("{} fame · {:.1}h", o.price, hours_left(o.time_left)), rr[0] + rr[2] - 250.0 * s, ty, font(12.0), true, color::ACCENT);
            let bb = [rr[0] + rr[2] - 96.0 * s, rr[1] + 5.0 * s, 90.0 * s, rr[3] - 10.0 * s];
            ui.button(bb, "Remove", Kind::Danger, inside(bb, mouse), s * 0.85);
            self.hits.push((Hit::Remove(o.id), bb));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn offer(id: i32, price: i32, start: i32, left: i32) -> MarketData {
        MarketData { id, item_type: 1, seller_name: "x".into(), seller_id: 1, currency: 1, price, start_time: start, time_left: left, item_data: String::new() }
    }

    #[test]
    fn sorting_and_answers() {
        let mut m = Market::default();
        m.event(Event::Search(vec![offer(1, 50, 1, 9), offer(2, 10, 3, 5), offer(3, 30, 2, 7)], String::new()));
        assert_eq!(m.offers.iter().map(|o| o.id).collect::<Vec<_>>(), [2, 3, 1]);
        m.sort = Sort::EndingSoon;
        m.sort.apply(&mut m.offers);
        assert_eq!(m.offers[0].id, 2);
        // A purchase takes the offer away; a sale asks for our offers again.
        m.event(Event::Buy(-1, String::new(), 2));
        assert!(m.offers.iter().all(|o| o.id != 2));
        m.picked = vec![4, 5];
        assert_eq!(m.event(Event::Add(-1, "Done".into())), vec![Act::MyOffers]);
        assert!(m.picked.is_empty());
        assert_eq!(m.event(Event::Add(3, "No".into())), Vec::new());
        assert_eq!(m.message, Some(("No".into(), true)));
    }
}
