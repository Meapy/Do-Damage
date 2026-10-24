//! The Divine Forge's window, opened from the forge in the Nexus's south yard.
//!
//! Forge: the recipes (`ForgeData`), filtered by what they make (weapons, abilities, armor,
//! rings and the rest, then by slot type), to untiered gear or each kind's highest tier, or by
//! name, or to what can be made now; a result
//! several recipes make is listed once. Picking one shows its ingredients as sockets; the player
//! puts in items from the inventory (forge shards come from the forge storage), forges, and
//! watches the items go into the fire and the forged item come out. Enchant: locked, a teaser
//! for a later update. Shards: the forge storage (`ForgeShards`), the shards the account has
//! used, which a click takes back out into the inventory. Sell: the inventory with what the
//! forge pays for each item under it; pick items and sell them for fame.
//!
//! The server checks everything (`ForgeHandlers.cs`): the window only says what it asks for. The
//! forged item takes the first ingredient's inventory slot.

use crate::hud::{Layout, SlotKind, Ui};
use crate::theme::{self, color, Kind};
use bsk_data::GameData;
use bsk_proto::{ForgePrice, ForgeRecipe, ForgeShard};
use bsk_world::GameObj;
use std::collections::{HashMap, HashSet};
use winit::keyboard::KeyCode;

/// What the player asked of the forge.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Act {
    /// The recipes and prices.
    Data,
    /// Forge the recipe making this item from the items in these inventory slots.
    Forge(u16, Vec<u8>),
    /// Sell the items in these inventory slots.
    Sell(Vec<u8>),
    /// Take one of this shard out of the forge storage.
    Withdraw(u16),
}

/// What the server said.
#[derive(Clone, Debug)]
pub enum Event {
    Data(Vec<ForgeRecipe>, Vec<ForgePrice>),
    Result { sold: bool, success: bool, item: u16, fame: i32, message: String },
    /// The forge storage's shards and counts.
    Shards(Vec<ForgeShard>),
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum Tab {
    #[default]
    Forge,
    /// Locked: a teaser for a later update.
    Enchant,
    Shards,
    Sell,
}

/// What a recipe makes, by the slots a class wears it in (`SlotTypes`: weapon, ability, armor,
/// ring).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default, Hash)]
pub enum Category {
    #[default]
    All,
    Weapons,
    Abilities,
    Armor,
    Rings,
    Other,
}

/// The second filter: untiered gear only, or only the highest tier the forge makes of each kind.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
enum Rarity {
    #[default]
    Any,
    Untiered,
    Top,
}

const CATEGORIES: [(Category, &str); 6] = [
    (Category::All, "All"),
    (Category::Weapons, "Weapons"),
    (Category::Abilities, "Abilities"),
    (Category::Armor, "Armor"),
    (Category::Rings, "Rings"),
    (Category::Other, "Other"),
];

/// A slot type's name on its filter.
fn slot_name(t: i32) -> &'static str {
    match t {
        6 => "Leather",
        7 => "Heavy",
        9 => "Ring",
        24 => "Katana",
        25 => "Shuriken",
        // the Doomed Realms classes' slots, which the HUD's names leave as "Item"
        27 => "Wakizashi",
        28 => "Lute",
        30 => "Pearl",
        32 => "Turret",
        36 => "Dual Blades",
        37 => "Jacket",
        126 => "Halo",
        128 => "Scroll",
        130 => "Fountain",
        200 => "Fists",
        5000 => "Oceanic Spell",
        5005 => "Cannon",
        _ => crate::hud::slot_type_name(t),
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Tab(Tab),
    Close,
    Search,
    Category(Category),
    Rarity(Rarity),
    Slot(Option<i32>),
    Ready,
    Recipe(usize),
    Up,
    Down,
    Socket(usize),
    InsertAll,
    Forge,
    Again,
    SellSlot(u8),
    SellAll,
    Sell,
    Withdraw(u16),
}

/// Where a forging is.
#[derive(Clone, Debug, PartialEq)]
enum Forging {
    /// The items fly into the fire (from this time); the server's answer may come meanwhile.
    Going { since: i32, answer: Option<Result<u16, String>> },
    /// Forged: the item came out (at this time).
    Done { since: i32, item: u16 },
}

/// The animation's steps, in ms: items into the fire, the fire flaring, the item coming out.
const INTO_FIRE: i32 = 900;
const FLARE: i32 = 800;
const OUT: i32 = 700;
/// How long to wait for the server before giving up.
const NO_ANSWER: i32 = 8000;

/// The Forge Amulet's name (`DoomedEconomy.ForgeAmulet`): worn in the ring slot, it halves the
/// price and unlocks the advanced recipes, and breaks when used.
const AMULET: &str = "Forge Amulet";
/// `FAME_STAT`: the account's fame.
const FAME_STAT: u8 = 39;
/// The forge's sprite sheet (`tools/draw_forge_sprite.py`): three frames of fire, then a flash.
const SHEET: &str = "customAssets32x32";
/// A socket filled from the forge storage rather than an inventory slot.
const STORAGE: u8 = 255;

/// The forge window.
#[derive(Default)]
pub struct Forge {
    pub open: bool,
    tab: Tab,
    recipes: Vec<ForgeRecipe>,
    prices: HashMap<u16, i32>,
    loaded: bool,
    /// Which category each slot type belongs to, from the classes' `SlotTypes`.
    kinds: HashMap<i32, Category>,
    amulet: Option<u16>,
    query: String,
    category: Category,
    slot: Option<i32>,
    ready_only: bool,
    rarity: Rarity,
    /// The recipes shown (indexes into `recipes`), and the first row drawn.
    shown: Vec<usize>,
    row: usize,
    rows: usize,
    picked: Option<usize>,
    /// The inventory slot put into each of the picked recipe's sockets (`STORAGE`: a shard from
    /// the forge storage).
    sockets: Vec<Option<u8>>,
    /// The forge storage's shards, in the server's order, and how many of each.
    shards: Vec<(u16, i32)>,
    forging: Option<Forging>,
    /// The Sell tab's picked slots.
    selling: Vec<u8>,
    /// The server's last word: (text, is it an error).
    message: Option<(String, bool)>,
    rect: Option<[f32; 4]>,
    hits: Vec<(Hit, [f32; 4])>,
    hover_item: Option<i32>,
    /// What the inventory held when last drawn, by slot (the clicks and sockets work on it).
    inventory: Vec<(u8, i32)>,
    fame: i32,
    wears_amulet: bool,
    backpack: bool,
    now: i32,
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

const COLS: usize = 5;

impl Forge {
    /// Opens the window; the first time, it asks for the recipes.
    pub fn show(&mut self, data: &GameData) -> Option<Act> {
        self.open = true;
        self.message = None;
        self.forging = None;
        self.selling.clear();
        if self.kinds.is_empty() {
            self.kinds = slot_categories(data);
            self.amulet = data.objects.values().find(|d| d.id == AMULET).map(|d| d.object_type);
        }
        (!self.loaded).then_some(Act::Data)
    }

    /// Whether the recipes came.
    pub fn loaded(&self) -> bool {
        self.loaded
    }

    fn category_of(&self, data: &GameData, item: u16) -> (Category, i32) {
        let slot = data.objects.get(&item).map_or(0, |d| d.xml.int("SlotType", 0));
        (self.kinds.get(&slot).copied().unwrap_or(Category::Other), slot)
    }

    /// How many of this shard the forge storage holds (0 for anything else).
    fn stored(&self, item: u16) -> i32 {
        self.shards.iter().find(|&&(t, _)| t == item).map_or(0, |&(_, n)| n)
    }

    /// Whether the inventory and the forge storage hold all of a recipe's ingredients.
    fn can_make(&self, r: &ForgeRecipe) -> bool {
        let mut have: Vec<i32> = self.inventory.iter().map(|&(_, t)| t).collect();
        let mut stored = self.shards.clone();
        r.items.iter().all(|&i| match have.iter().position(|&t| t == i as i32) {
            Some(k) => {
                have.swap_remove(k);
                true
            }
            None => match stored.iter_mut().find(|(t, n)| *t == i && *n > 0) {
                Some((_, n)) => {
                    *n -= 1;
                    true
                }
                None => false,
            },
        })
    }

    fn price(&self, r: &ForgeRecipe) -> i32 {
        r.fame / if self.wears_amulet { 2 } else { 1 }
    }

    /// The recipes the filters let through: untiered gear first, then tiered gear from the
    /// highest tier down, then everything else (`order`), each by category and name.
    fn filter(&mut self, data: &GameData) {
        let q = self.query.trim().to_lowercase();
        let name = |t: u16| data.objects.get(&t).map_or(String::new(), |d| d.display_id.to_lowercase());
        let tier = |t: u16| -> Option<i32> { data.objects.get(&t).and_then(|d| d.xml.text_of("Tier")).and_then(|t| t.trim().parse().ok()) };
        // the highest tier the forge makes of each slot type, for the Highest tier filter
        let mut top: HashMap<i32, i32> = HashMap::new();
        for r in &self.recipes {
            if let Some(t) = tier(r.result) {
                let e = top.entry(self.category_of(data, r.result).1).or_insert(t);
                *e = (*e).max(t);
            }
        }
        let mut shown: Vec<((u8, i32), Category, String, usize)> = self
            .recipes
            .iter()
            .enumerate()
            .filter(|(_, r)| {
                let (cat, slot) = self.category_of(data, r.result);
                (self.category == Category::All || cat == self.category)
                    && self.slot.is_none_or(|s| s == slot)
                    && (!self.ready_only || self.can_make(r))
                    && match self.rarity {
                        Rarity::Any => true,
                        Rarity::Untiered => tier(r.result).is_none() && cat != Category::Other,
                        Rarity::Top => tier(r.result).is_some_and(|t| top.get(&slot) == Some(&t)),
                    }
                    && (q.is_empty() || name(r.result).contains(&q) || r.items.iter().any(|&i| name(i).contains(&q)))
            })
            .map(|(i, r)| {
                let cat = self.category_of(data, r.result).0;
                let ascended = data.objects.get(&r.result).is_some_and(|d| d.xml.has("Ascended"));
                (order(cat, tier(r.result), ascended), cat, name(r.result), i)
            })
            .collect();
        shown.sort_by(|a, b| (a.0, a.1 as u8, &a.2).cmp(&(b.0, b.1 as u8, &b.2)));
        // A result several recipes make (an Ascended item from any T15 weapon) is listed once:
        // the first that can be made now, else the first.
        let mut seen: HashMap<u16, usize> = HashMap::new();
        let mut list: Vec<usize> = Vec::new();
        for (_, _, _, i) in shown {
            let result = self.recipes[i].result;
            match seen.get(&result) {
                Some(&at) => {
                    if !self.can_make(&self.recipes[list[at]]) && self.can_make(&self.recipes[i]) {
                        list[at] = i;
                    }
                }
                None => {
                    seen.insert(result, list.len());
                    list.push(i);
                }
            }
        }
        self.shown = list;
        self.row = 0;
    }

    /// The slot types among the recipes of the chosen category, for its second row of filters.
    fn slot_filters(&self, data: &GameData) -> Vec<i32> {
        if matches!(self.category, Category::All | Category::Other) {
            return Vec::new();
        }
        let mut slots: Vec<i32> = self
            .recipes
            .iter()
            .map(|r| self.category_of(data, r.result))
            .filter(|(c, _)| *c == self.category)
            .map(|(_, s)| s)
            .collect::<HashSet<_>>()
            .into_iter()
            .collect();
        slots.sort_by_key(|&s| slot_name(s));
        slots
    }

    fn pick(&mut self, i: usize) {
        if self.forging.as_ref().is_some_and(|f| matches!(f, Forging::Going { .. })) {
            return;
        }
        // of the recipes making the same item, one that can be made now
        let result = self.recipes[i].result;
        let i = if self.can_make(&self.recipes[i]) {
            i
        } else {
            (0..self.recipes.len()).find(|&k| self.recipes[k].result == result && self.can_make(&self.recipes[k])).unwrap_or(i)
        };
        self.picked = Some(i);
        self.sockets = vec![None; self.recipes[i].items.len()];
        self.forging = None;
        self.message = None;
    }

    /// Puts an inventory item of the right type into socket `k`, if there's one not in use.
    fn insert(&mut self, k: usize) -> bool {
        let Some(r) = self.picked.map(|i| &self.recipes[i]) else { return false };
        let want = r.items[k] as i32;
        let used: Vec<u8> = self.sockets.iter().flatten().copied().collect();
        // a shard comes from the forge storage while it has one not in use
        let from_storage = self.sockets.iter().enumerate().filter(|&(j, s)| *s == Some(STORAGE) && r.items[j] == r.items[k]).count() as i32;
        let found = if self.stored(r.items[k]) > from_storage {
            Some(STORAGE)
        } else {
            self.inventory.iter().find(|&&(s, t)| t == want && !used.contains(&s)).map(|&(s, _)| s)
        };
        if let Some(s) = found {
            self.sockets[k] = Some(s);
        }
        found.is_some()
    }

    /// The server's answers.
    pub fn event(&mut self, e: Event, data: &GameData) {
        match e {
            Event::Data(recipes, prices) => {
                log::info!("forge: {} recipes, {} prices", recipes.len(), prices.len());
                self.recipes = recipes;
                self.prices = prices.into_iter().map(|p| (p.item, p.fame)).collect();
                self.loaded = true;
                self.picked = None;
                self.filter(data);
            }
            Event::Shards(shards) => {
                self.shards = shards.into_iter().map(|s| (s.item, s.count)).collect();
                if self.loaded {
                    self.filter(data);
                }
            }
            Event::Result { sold: true, success, fame, message, .. } => {
                if success {
                    self.selling.clear();
                    self.message = Some((format!("Sold for {} fame.", crate::hud::group_digits(fame)), false));
                } else {
                    self.message = Some((message, true));
                }
            }
            Event::Result { sold: false, success, item, message, .. } => {
                if let Some(Forging::Going { answer, .. }) = &mut self.forging {
                    *answer = Some(if success { Ok(item) } else { Err(message) });
                }
            }
        }
    }

    pub fn click(&mut self, pos: (f64, f64), data: &GameData) -> Option<Act> {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.close();
            return None;
        }
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h)?;
        match hit {
            Hit::Tab(Tab::Enchant) => {
                self.message = Some(("Enchanting is locked: it comes in a later update.".into(), false));
            }
            Hit::Tab(t) => {
                if !matches!(self.forging, Some(Forging::Going { .. })) {
                    self.tab = t;
                    self.message = None;
                }
            }
            Hit::Close => self.close(),
            Hit::Search => {}
            Hit::Category(c) => {
                self.category = c;
                self.slot = None;
                self.filter(data);
            }
            Hit::Rarity(k) => {
                self.rarity = if self.rarity == k { Rarity::Any } else { k };
                self.filter(data);
            }
            Hit::Slot(s) => {
                self.slot = s;
                self.filter(data);
            }
            Hit::Ready => {
                self.ready_only = !self.ready_only;
                self.filter(data);
            }
            Hit::Recipe(i) => self.pick(i),
            Hit::Up => self.row = self.row.saturating_sub(self.rows.max(1)),
            Hit::Down => self.scroll(self.rows.max(1) as i32),
            Hit::Socket(k) => {
                if self.forging.is_none() {
                    if self.sockets[k].is_some() {
                        self.sockets[k] = None;
                    } else if !self.insert(k) {
                        self.message = Some(("You have none of those in your inventory or forge storage.".into(), true));
                    }
                }
            }
            Hit::InsertAll => {
                if self.forging.is_none() {
                    let mut missing = false;
                    for k in 0..self.sockets.len() {
                        if self.sockets[k].is_none() {
                            missing |= !self.insert(k);
                        }
                    }
                    self.message = missing.then(|| ("Some ingredients aren't in your inventory or forge storage.".into(), true));
                }
            }
            Hit::Forge => {
                let r = &self.recipes[self.picked?];
                if self.sockets.iter().any(Option::is_none) {
                    self.message = Some(("Put every ingredient in first.".into(), true));
                } else if r.advanced && !self.wears_amulet {
                    self.message = Some(("Wear a Forge Amulet to forge this.".into(), true));
                } else if self.fame < self.price(r) {
                    self.message = Some(("Not enough fame.".into(), true));
                } else {
                    let slots: Vec<u8> = self.sockets.iter().flatten().copied().filter(|&s| s != STORAGE).collect();
                    self.message = None;
                    self.forging = Some(Forging::Going { since: self.now, answer: None });
                    return Some(Act::Forge(r.result, slots));
                }
            }
            Hit::Again => {
                if let Some(i) = self.picked {
                    self.pick(i);
                }
            }
            Hit::SellSlot(s) => {
                if let Some(i) = self.selling.iter().position(|&p| p == s) {
                    self.selling.remove(i);
                } else {
                    self.selling.push(s);
                }
                self.message = None;
            }
            Hit::SellAll => {
                self.selling = self.inventory.iter().filter(|(_, t)| self.prices.contains_key(&(*t as u16))).map(|&(s, _)| s).collect();
                self.message = None;
            }
            Hit::Sell => {
                if self.selling.is_empty() {
                    self.message = Some(("Pick items to sell first.".into(), true));
                } else {
                    return Some(Act::Sell(self.selling.clone()));
                }
            }
            Hit::Withdraw(item) => {
                if self.stored(item) <= 0 {
                    self.message = Some(("None of those in your forge storage.".into(), true));
                } else {
                    self.message = None;
                    return Some(Act::Withdraw(item));
                }
            }
        }
        None
    }

    fn close(&mut self) {
        // A forging in flight finishes on the server whatever the window does.
        self.open = false;
        self.forging = None;
    }

    pub fn scroll(&mut self, rows: i32) {
        let total = self.shown.len().div_ceil(COLS);
        let max = total.saturating_sub(self.rows.max(1));
        self.row = (self.row as i32 + rows).clamp(0, max as i32) as usize;
    }

    /// A key while the window is open: typing into the search, Escape to close.
    pub fn key(&mut self, code: KeyCode, text: Option<&str>, data: &GameData) {
        match code {
            KeyCode::Escape => self.close(),
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

    /// Reads the inventory, fame and amulet, and drops sockets whose item has gone.
    fn look(&mut self, me: Option<&GameObj>) {
        self.inventory.clear();
        let Some(me) = me else { return };
        self.backpack = me.stat_int(crate::hud::stat::HAS_BACKPACK).unwrap_or(0) != 0;
        for slot in 4..if self.backpack { 20 } else { 12 } {
            let item = me.stat_int(crate::hud::slot_stat(slot)).unwrap_or(-1);
            if item > 0 {
                self.inventory.push((slot, item));
            }
        }
        self.fame = me.stat_int(FAME_STAT).unwrap_or(0);
        self.wears_amulet = self.amulet.is_some_and(|a| me.stat_int(crate::hud::slot_stat(3)).unwrap_or(-1) == a as i32);
        if self.forging.is_none() {
            if let Some(r) = self.picked.map(|i| self.recipes[i].clone()) {
                let mut from_storage: HashMap<u16, i32> = HashMap::new();
                for (k, s) in self.sockets.iter_mut().enumerate() {
                    let gone = match *s {
                        Some(STORAGE) => {
                            let n = from_storage.entry(r.items[k]).or_insert(0);
                            *n += 1;
                            *n > self.shards.iter().find(|&&(t, _)| t == r.items[k]).map_or(0, |&(_, c)| c)
                        }
                        Some(slot) => !self.inventory.contains(&(slot, r.items[k] as i32)),
                        None => false,
                    };
                    if gone {
                        *s = None;
                    }
                }
            }
        }
        let inv = &self.inventory;
        self.selling.retain(|s| inv.iter().any(|(i, _)| i == s));
    }

    /// Moves the forging along: on to Done once the fire has flared and the server said yes,
    /// back to the sockets if it said no or never answered.
    fn advance(&mut self) {
        let now = self.now;
        if let Some(Forging::Going { since, answer }) = &self.forging {
            let t = now - since;
            match answer {
                Some(Ok(item)) if t >= INTO_FIRE + FLARE => self.forging = Some(Forging::Done { since: now, item: *item }),
                Some(Err(why)) => {
                    self.message = Some((why.clone(), true));
                    self.forging = None;
                }
                None if t > NO_ANSWER => {
                    self.message = Some(("The forge didn't answer. Check your inventory.".into(), true));
                    self.forging = None;
                }
                _ => {}
            }
            if matches!(self.forging, Some(Forging::Done { .. })) {
                self.sockets.iter_mut().for_each(|s| *s = None);
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
        self.now = now;
        self.look(me);
        self.advance();
        let (dw, dh) = (860.0, 580.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        // The header row: the title, the tabs in the middle of the window, the fame and the close
        // button, all centred on one line.
        let mid = r[1] + 32.0 * s;
        let ts = font(22.0);
        ui.label("Divine Forge", r[0] + 22.0 * s, (mid - ui.digit_middle(ts, true)).round(), ts, true, color::GOLD);
        let tabs = [(Tab::Forge, "Forge"), (Tab::Enchant, "Enchant"), (Tab::Shards, "Shards"), (Tab::Sell, "Sell for fame")];
        let cs = font(13.0);
        let widths: Vec<f32> = tabs.iter().map(|(_, l)| ui.measure(l, cs, true) + 36.0 * s).collect();
        let gap = 8.0 * s;
        let mut x = (r[0] + (r[2] - widths.iter().sum::<f32>() - gap) / 2.0).round();
        for ((t, label), w) in tabs.into_iter().zip(widths) {
            let tr = [x, (mid - 14.0 * s).round(), w, 28.0 * s];
            if t == Tab::Enchant {
                locked_chip(ui, tr, label, cs, s);
            } else {
                self.chip(ui, tr, label, t == self.tab, mouse, cs);
            }
            self.hits.push((Hit::Tab(t), tr));
            x += w + gap;
        }
        let ft = crate::hud::group_digits(self.fame);
        let fs = font(15.0);
        let fw = fame_width(ui, &ft, fs, s);
        let fx = r[0] + r[2] - fw - 60.0 * s;
        let fy = (mid - ui.digit_middle(fs, true)).round();
        fame_text(ui, &ft, fx, fy, fs, color::TEXT, s);
        // underlined in the ember accent
        ui.rect(fx, (mid + fs * 0.62).round(), fw, (2.0 * s).max(1.0).round(), color::ACCENT, 1.0);
        let cb = [r[0] + r[2] - 42.0 * s, (mid - 14.0 * s).round(), 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        let xs = font(16.0);
        let xw = ui.measure("×", xs, true);
        ui.label("×", cb[0] + (cb[2] - xw) / 2.0, cb[1] + (cb[3] - xs * 1.22) / 2.0, xs, true, color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));

        let body = [r[0] + 22.0 * s, r[1] + 62.0 * s, r[2] - 44.0 * s, r[3] - 110.0 * s];
        match self.tab {
            Tab::Forge => self.draw_forge(ui, body, s, mouse, now),
            Tab::Sell => self.draw_sell(ui, body, s, mouse),
            Tab::Shards => self.draw_shards(ui, body, s, mouse),
            Tab::Enchant => {}
        }
        let foot_y = r[1] + r[3] - 36.0 * s;
        if let Some((m, err)) = &self.message {
            ui.label(m, r[0] + 22.0 * s, foot_y, font(13.0), true, if *err { color::DANGER } else { color::GOOD });
        } else if !self.loaded {
            ui.label("Asking the forge for its recipes...", r[0] + 22.0 * s, foot_y, font(13.0), false, color::TEXT_MUTED);
        } else if self.wears_amulet {
            ui.label("Forge Amulet worn: advanced recipes, half price; it breaks when you forge.", r[0] + 22.0 * s, foot_y, font(13.0), false, color::GOLD);
        }
        if let Some(item) = self.hover_item {
            let lines = crate::hud::item_tooltip(ui.data, item, me, SlotKind::Shop);
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }

    fn chip(&self, ui: &mut Ui, r: [f32; 4], label: &str, on: bool, mouse: (f64, f64), size: f32) {
        let fill = if on { color::ACCENT } else if inside(r, mouse) { color::SECONDARY } else { color::CHIP };
        theme::fill(ui.renderer, r, r[3] / 2.0, fill, 1.0);
        let lw = ui.measure(label, size, true);
        ui.label(label, r[0] + (r[2] - lw) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
    }

    /// A row of chips from `x`, wrapping within `right`; returns the y below them.
    #[allow(clippy::too_many_arguments)]
    fn chips(&mut self, ui: &mut Ui, x: f32, y: f32, right: f32, items: &[(Hit, String, bool)], s: f32, mouse: (f64, f64)) -> f32 {
        let (h, gap, size) = (24.0 * s, 5.0 * s, (11.5 * s).round().max(9.0));
        let (mut cx, mut cy) = (x, y);
        for (hit, label, on) in items {
            let w = ui.measure(label, size, true) + 15.0 * s;
            if cx + w > right && cx > x {
                cx = x;
                cy += h + gap;
            }
            let r = [cx, cy, w, h];
            self.chip(ui, r, label, *on, mouse, size);
            self.hits.push((*hit, r));
            cx += w + gap;
        }
        cy + h + gap
    }

    fn draw_forge(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let left = [body[0], body[1], 324.0 * s, body[3]];
        let right = [left[0] + left[2] + 18.0 * s, body[1], body[2] - left[2] - 18.0 * s, body[3]];
        // The search and the filters.
        let fr = [left[0], left[1], left[2], 32.0 * s];
        ui.field_box(fr, true, s);
        let fs = font(14.0);
        let ty = fr[1] + (fr[3] - fs * 1.22) / 2.0;
        if self.query.is_empty() {
            ui.label("Search the Forge...", fr[0] + 10.0 * s, ty, fs, false, color::TEXT_MUTED);
        } else {
            let q = self.query.clone();
            ui.label(&q, fr[0] + 10.0 * s, ty, fs, false, color::TEXT);
        }
        if (now / 500) % 2 == 0 {
            let cx = fr[0] + 10.0 * s + ui.measure(&self.query.clone(), fs, false) + s;
            ui.rect(cx, fr[1] + 7.0 * s, (1.5 * s).max(1.0), fr[3] - 14.0 * s, color::ACCENT, 1.0);
        }
        self.hits.push((Hit::Search, fr));
        let mut cats: Vec<(Hit, String, bool)> = CATEGORIES.iter().map(|&(c, l)| (Hit::Category(c), l.to_owned(), c == self.category)).collect();
        for (k, label) in [(Rarity::Untiered, "UT"), (Rarity::Top, "Highest tier")] {
            cats.push((Hit::Rarity(k), label.to_owned(), self.rarity == k));
        }
        let mut y = self.chips(ui, left[0], fr[1] + fr[3] + 10.0 * s, left[0] + left[2], &cats, s, mouse);
        let slots = self.slot_filters(ui.data);
        if !slots.is_empty() {
            let mut items = vec![(Hit::Slot(None), "Any".to_owned(), self.slot.is_none())];
            items.extend(slots.iter().map(|&t| (Hit::Slot(Some(t)), slot_name(t).to_owned(), self.slot == Some(t))));
            y = self.chips(ui, left[0], y, left[0] + left[2], &items, s, mouse);
        }
        // The recipes, as many rows as fit above the paging.
        let cell = 54.0 * s;
        let gap = 7.5 * s;
        let by = left[1] + left[3] - 28.0 * s;
        let gy = y + 4.0 * s;
        self.rows = (((by - 8.0 * s - gy) / (cell + gap)).floor() as usize).max(1);
        let picked = self.picked;
        for (n, &i) in self.shown.clone().iter().skip(self.row * COLS).take(COLS * self.rows).enumerate() {
            let r = self.recipes[i].clone();
            let sr = [left[0] + (n % COLS) as f32 * (cell + gap), gy + (n / COLS) as f32 * (cell + gap), cell, cell];
            let over = inside(sr, mouse);
            let edge = if picked == Some(i) { Some(color::GOLD) } else { crate::screens::tier_edge(ui.data, r.result as i32) };
            ui.slot_box(sr, edge, over, s);
            ui.item_icon(r.result as i32, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s);
            if r.advanced && !self.wears_amulet {
                theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.5);
            } else if self.can_make(&r) {
                theme::fill(ui.renderer, [sr[0] + sr[2] - 11.0 * s, sr[1] + 4.0 * s, 7.0 * s, 7.0 * s], 3.5 * s, color::GOOD, 1.0);
            }
            if over {
                self.hover_item = Some(r.result as i32);
            }
            self.hits.push((Hit::Recipe(i), sr));
        }
        if self.loaded && self.shown.is_empty() {
            ui.label("No recipes match.", left[0], gy + 6.0 * s, font(13.0), false, color::TEXT_MUTED);
        }
        let (up, down) = ([left[0], by, 56.0 * s, 26.0 * s], [left[0] + 62.0 * s, by, 56.0 * s, 26.0 * s]);
        ui.button(up, "Up", Kind::Secondary, inside(up, mouse), s * 0.8);
        ui.button(down, "Down", Kind::Secondary, inside(down, mouse), s * 0.8);
        self.hits.push((Hit::Up, up));
        self.hits.push((Hit::Down, down));
        let rb = [left[0] + 126.0 * s, by, left[2] - 126.0 * s, 26.0 * s];
        self.chip(ui, rb, "Forge Ready Items", self.ready_only, mouse, font(11.5));
        self.hits.push((Hit::Ready, rb));

        self.draw_stage(ui, right, s, mouse, now);
    }

    /// The picked recipe on the forge: the result at the top, the forge in the middle, the
    /// ingredient sockets below, and the animation over them.
    fn draw_stage(&mut self, ui: &mut Ui, r: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::WARM_TOP, 0.95), (color::WARM_BOTTOM, 0.95)));
        // A warm glow behind the forge.
        let (fx, fy) = (r[0] + r[2] / 2.0, r[1] + r[3] * 0.47);
        for k in 0..5 {
            let rad = (150.0 - k as f32 * 26.0) * s;
            theme::fill(ui.renderer, [fx - rad, fy - rad * 0.8, rad * 2.0, rad * 1.6], rad * 0.8, color::GOLD, 0.025 + k as f32 * 0.012);
        }
        let frame = ((now / 150).rem_euclid(3)) as u32;
        let Some(i) = self.picked else {
            ui.tile_icon(SHEET, frame, 80, fx, fy, s);
            let t = "Pick a recipe to forge.";
            let w = ui.measure(t, font(14.0), false);
            ui.label(t, fx - w / 2.0, r[1] + 20.0 * s, font(14.0), false, color::TEXT_MUTED);
            return;
        };
        let rec = self.recipes[i].clone();
        // The result: its name, kind and price.
        let name = ui.data.objects.get(&rec.result).map_or(String::new(), |d| d.display_id.clone());
        let (cat, slot) = self.category_of(ui.data, rec.result);
        let tier = ui.data.objects.get(&rec.result).and_then(|d| d.xml.text_of("Tier").map(|t| format!("T{} ", t.trim()))).unwrap_or_default();
        let kind = if cat == Category::Other { String::new() } else { format!("{tier}{}", crate::hud::slot_type_name(slot)) };
        let rs = 64.0 * s;
        let result_box = [r[0] + 18.0 * s, r[1] + 16.0 * s, rs, rs];
        ui.label(&name, result_box[0] + rs + 14.0 * s, result_box[1] + 4.0 * s, font(17.0), true, color::TEXT);
        if !kind.is_empty() {
            ui.label(&kind, result_box[0] + rs + 14.0 * s, result_box[1] + 28.0 * s, font(12.0), false, color::TEXT_DIM);
        }
        let price = self.price(&rec);
        let afford = self.fame >= price;
        let (px, py) = (result_box[0] + rs + 14.0 * s, result_box[1] + 46.0 * s);
        if rec.advanced && !self.wears_amulet {
            ui.label("Needs a Forge Amulet in your ring slot", px, py, font(13.0), true, color::DANGER);
        } else {
            let pt = crate::hud::group_digits(price);
            let w = fame_text(ui, &pt, px, py, font(13.0), if afford { color::GOLD } else { color::DANGER }, s);
            if self.wears_amulet {
                ui.label("(half price)", px + w + 6.0 * s, py, font(13.0), false, color::TEXT_DIM);
            }
        }

        // The result's slot: empty until forged, then the item with a burst behind it.
        let done = match &self.forging {
            Some(Forging::Done { since, item }) => Some((now - since, *item)),
            _ => None,
        };
        if let Some((t, _)) = done {
            let pulse = 0.5 + 0.5 * ((t as f32) / 300.0).sin();
            let g = 10.0 * s + pulse * 6.0 * s;
            theme::fill(ui.renderer, [result_box[0] - g, result_box[1] - g, rs + 2.0 * g, rs + 2.0 * g], 12.0 * s, color::GOLD, 0.18);
        }
        ui.slot_box(result_box, Some(color::GOLD), inside(result_box, mouse), s);
        let show_result = match (&self.forging, done) {
            (_, Some((t, _))) => t >= OUT,
            (None, _) => true,
            _ => false,
        };
        if show_result {
            ui.item_icon(rec.result as i32, 160, result_box[0] + rs / 2.0, result_box[1] + rs / 2.0, s * 0.8);
            if done.is_none() {
                theme::fill(ui.renderer, result_box, 5.0 * s, 0x000000, 0.45);
            }
        }
        if inside(result_box, mouse) {
            self.hover_item = Some(rec.result as i32);
        }

        // The forge, flaring while it works.
        let going = match &self.forging {
            Some(Forging::Going { since, .. }) => Some(now - since),
            _ => None,
        };
        let flare = going.map_or(0.0, |t| ((t - INTO_FIRE) as f32 / FLARE as f32).clamp(0.0, 1.0));
        let forge_size = 80 + (flare * 12.0) as i32;
        ui.tile_icon(SHEET, frame, forge_size, fx, fy, s);
        let mouth = (fx, fy - 6.0 * s);
        if flare > 0.0 {
            // The flash grows over the forge's mouth, and sparks fly.
            let k = flare * (1.0 + 0.15 * ((now as f32) / 60.0).sin());
            ui.tile_icon(SHEET, 3, (30.0 + 70.0 * k) as i32, mouth.0, mouth.1, s);
            sparks(ui, mouth, flare, now, s);
        }

        // The sockets: in a row under the forge.
        let n = rec.items.len();
        let cell = 50.0 * s;
        let gap = 10.0 * s;
        let row_w = n as f32 * cell + (n.saturating_sub(1)) as f32 * gap;
        let sy = r[1] + r[3] - 120.0 * s;
        let sx = fx - row_w / 2.0;
        // a result several recipes make: say so (the list shows it once)
        let others = self.recipes.iter().filter(|o| o.result == rec.result).count();
        let label = if others > 1 { format!("Ingredients  ·  one of {others} recipes") } else { "Ingredients".to_owned() };
        let lw = ui.measure(&label, font(12.0), true);
        ui.label(&label, fx - lw / 2.0, sy - 22.0 * s, font(12.0), true, color::TEXT_DIM);
        for (k, &item) in rec.items.iter().enumerate() {
            let cr = [sx + k as f32 * (cell + gap), sy, cell, cell];
            let inserted = self.sockets.get(k).copied().flatten();
            let have = inserted.is_some() || self.inventory.iter().any(|&(_, t)| t == item as i32) || self.stored(item) > 0;
            let edge = if inserted.is_some() { Some(color::GOLD) } else if have { crate::screens::tier_edge(ui.data, item as i32) } else { Some(color::DANGER) };
            // Once forged, the sockets stand empty: their items went into the fire.
            if done.is_some() {
                ui.slot_box(cr, None, false, s);
                continue;
            }
            ui.slot_box(cr, edge, inside(cr, mouse) && self.forging.is_none(), s);
            let (cx, cy) = (cr[0] + cell / 2.0, cr[1] + cell / 2.0);
            match going {
                // Each item flies into the fire in turn, shrinking.
                Some(t) => {
                    let start = k as i32 * 120;
                    let p = ((t - start) as f32 / (INTO_FIRE - 300) as f32).clamp(0.0, 1.0);
                    if p < 1.0 {
                        let e = p * p;
                        let (x, y) = (cx + (mouth.0 - cx) * e, cy + (mouth.1 - cy) * e - (1.0 - (2.0 * p - 1.0).powi(2)) * 40.0 * s);
                        ui.item_icon(item as i32, 80, x, y, s * (1.0 - 0.7 * e));
                    }
                }
                None => {
                    ui.item_icon(item as i32, 80, cx, cy, s);
                    if inserted.is_none() {
                        theme::fill(ui.renderer, cr, 5.0 * s, 0x000000, if have { 0.35 } else { 0.6 });
                    }
                    // a shard: how many the forge storage holds
                    if self.shards.iter().any(|&(t, _)| t == item) {
                        let t = self.stored(item).to_string();
                        let fs = font(11.0);
                        let w = ui.measure(&t, fs, true);
                        let col = if inserted == Some(STORAGE) { color::GOLD } else { color::TEXT_DIM };
                        ui.label(&t, cr[0] + cr[2] - w - 4.0 * s, cr[1] + cr[3] - fs * 1.25, fs, true, col);
                    }
                }
            }
            if inside(cr, mouse) {
                self.hover_item = Some(item as i32);
            }
            self.hits.push((Hit::Socket(k), cr));
        }

        // The item coming out of the fire into its slot.
        if let Some((t, item)) = done {
            if t < OUT {
                let p = t as f32 / OUT as f32;
                let e = 1.0 - (1.0 - p).powi(2);
                let (tx, ty) = (result_box[0] + rs / 2.0, result_box[1] + rs / 2.0);
                let (x, y) = (mouth.0 + (tx - mouth.0) * e, mouth.1 + (ty - mouth.1) * e - (1.0 - (2.0 * p - 1.0).powi(2)) * 50.0 * s);
                ui.tile_icon(SHEET, 3, (60.0 * (1.0 - p) + 20.0) as i32, x, y, s);
                ui.item_icon(item as i32, 160, x, y, s * (0.3 + 0.5 * e));
            }
        }

        // The buttons, or what was made.
        let by = r[1] + r[3] - 52.0 * s;
        let bw = 170.0 * s;
        if let Some((t, item)) = done {
            if t >= OUT {
                let name = ui.data.objects.get(&item).map_or(String::new(), |d| d.display_id.clone());
                let msg = format!("Forged {name}!");
                let w = ui.measure(&msg, font(14.0), true);
                ui.label(&msg, fx - w / 2.0, sy - 46.0 * s, font(14.0), true, color::GOLD);
            }
            let ab = [fx - bw / 2.0, by, bw, 36.0 * s];
            ui.button(ab, "Forge another", Kind::Secondary, inside(ab, mouse), s);
            self.hits.push((Hit::Again, ab));
        } else if going.is_some() {
            let t = "Forging...";
            let w = ui.measure(t, font(15.0), true);
            ui.label(t, fx - w / 2.0, by + 8.0 * s, font(15.0), true, color::GOLD);
        } else {
            let ib = [fx - bw - 6.0 * s, by, bw, 36.0 * s];
            let fb = [fx + 6.0 * s, by, bw, 36.0 * s];
            ui.button(ib, "Insert all", Kind::Secondary, inside(ib, mouse), s);
            let ready = self.sockets.iter().all(Option::is_some) && afford && (!rec.advanced || self.wears_amulet);
            let label = format!("Forge  ·  {}", crate::hud::group_digits(price));
            fame_button(ui, fb, &label, if ready { Kind::Primary } else { Kind::Secondary }, inside(fb, mouse), s);
            self.hits.push((Hit::InsertAll, ib));
            self.hits.push((Hit::Forge, fb));
        }
    }

    /// The Shards tab: the forge storage, the Infernal shards on one row and the Ascended on
    /// the next (the server's order, four a row), each with its count; a click takes one out.
    fn draw_shards(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64)) {
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label("Forge storage", body[0], body[1], font(14.0), true, color::TEXT_DIM);
        let help = "Shards you use go here, and the forge takes a recipe's shards from here. Click a shard to take one out.";
        ui.label(help, body[0], body[1] + 22.0 * s, font(12.0), false, color::TEXT_MUTED);
        if self.shards.is_empty() {
            ui.label("This server has no forge storage yet.", body[0], body[1] + 60.0 * s, font(13.0), false, color::TEXT_MUTED);
            return;
        }
        let cell = 64.0 * s;
        let (gx, gy) = (120.0 * s, 52.0 * s);
        let cols = 4;
        let grid_w = cols as f32 * cell + (cols - 1) as f32 * gx;
        let x0 = body[0] + (body[2] - grid_w) / 2.0;
        let y0 = body[1] + 70.0 * s;
        for (n, &(item, count)) in self.shards.clone().iter().enumerate() {
            let sr = [x0 + (n % cols) as f32 * (cell + gx), y0 + (n / cols) as f32 * (cell + gy), cell, cell];
            let over = inside(sr, mouse);
            let edge = if count > 0 { crate::screens::tier_edge(ui.data, item as i32) } else { None };
            ui.slot_box(sr, edge, over && count > 0, s);
            ui.item_icon(item as i32, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s * 1.1);
            if count <= 0 {
                theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.55);
            }
            let t = crate::hud::group_digits(count);
            let fs = font(12.0);
            let w = ui.measure(&t, fs, true);
            ui.label(&t, sr[0] + sr[2] - w - 5.0 * s, sr[1] + sr[3] - fs * 1.3, fs, true, if count > 0 { color::TEXT } else { color::TEXT_MUTED });
            let name = ui.data.objects.get(&item).map_or(String::new(), |d| d.display_id.clone());
            let ns = font(11.5);
            let nw = ui.measure(&name, ns, false);
            ui.label(&name, sr[0] + (sr[2] - nw) / 2.0, sr[1] + sr[3] + 6.0 * s, ns, false, color::TEXT_DIM);
            if over {
                self.hover_item = Some(item as i32);
            }
            self.hits.push((Hit::Withdraw(item), sr));
        }
    }

    /// The Sell tab: the inventory, each item with the fame the forge pays under it.
    fn draw_sell(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64)) {
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label("Pick items to sell to the forge for fame", body[0], body[1], font(14.0), true, color::TEXT_DIM);
        let cell = 64.0 * s;
        let (gx, gy) = (22.0 * s, 34.0 * s);
        let cols = 8;
        let grid_w = cols as f32 * cell + (cols - 1) as f32 * gx;
        let x0 = body[0] + (body[2] - grid_w) / 2.0;
        let y0 = body[1] + 40.0 * s;
        let slots: Vec<u8> = (4..if self.backpack { 20 } else { 12 }).collect();
        let mut total = 0;
        for (n, &slot) in slots.iter().enumerate() {
            let sr = [x0 + (n % cols) as f32 * (cell + gx), y0 + (n / cols) as f32 * (cell + gy), cell, cell];
            let item = self.inventory.iter().find(|&&(s, _)| s == slot).map_or(-1, |&(_, t)| t);
            let price = (item > 0).then(|| self.prices.get(&(item as u16)).copied()).flatten();
            let picked = self.selling.contains(&slot);
            if picked {
                total += price.unwrap_or(0);
            }
            let edge = if picked { Some(color::GOOD) } else if item > 0 { crate::screens::tier_edge(ui.data, item) } else { None };
            ui.slot_box(sr, edge, inside(sr, mouse) && price.is_some(), s);
            if item > 0 {
                ui.item_icon(item, 80, sr[0] + sr[2] / 2.0, sr[1] + sr[3] / 2.0, s * 1.1);
                if price.is_none() {
                    theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.55);
                }
                if inside(sr, mouse) {
                    self.hover_item = Some(item);
                }
            }
            if picked {
                theme::fill(ui.renderer, [sr[0] + s, sr[1] + s, sr[2] - 2.0 * s, sr[3] - 2.0 * s], 4.0 * s, color::GOOD, 0.18);
            }
            // The price under the slot.
            let fs = font(12.0);
            let ty = sr[1] + sr[3] + 5.0 * s;
            match (item > 0, price) {
                (true, Some(p)) => {
                    let t = crate::hud::group_digits(p);
                    let w = fame_width(ui, &t, fs, s);
                    fame_text(ui, &t, sr[0] + (sr[2] - w) / 2.0, ty, fs, if picked { color::GOOD } else { color::GOLD }, s);
                }
                (true, None) => {
                    let w = ui.measure("Not bought", fs, true);
                    ui.label("Not bought", sr[0] + (sr[2] - w) / 2.0, ty, fs, true, color::TEXT_MUTED);
                }
                _ => {}
            }
            if price.is_some() {
                self.hits.push((Hit::SellSlot(slot), sr));
            }
        }
        let by = body[1] + body[3] - 40.0 * s;
        let bw = 220.0 * s;
        let ab = [body[0] + body[2] / 2.0 - bw - 6.0 * s, by, bw, 38.0 * s];
        let sb = [body[0] + body[2] / 2.0 + 6.0 * s, by, bw, 38.0 * s];
        ui.button(ab, "Pick everything it buys", Kind::Secondary, inside(ab, mouse), s);
        if self.selling.is_empty() {
            ui.button(sb, "Sell", Kind::Secondary, inside(sb, mouse), s);
        } else {
            let label = format!("Sell {}  ·  {}", self.selling.len(), crate::hud::group_digits(total));
            fame_button(ui, sb, &label, Kind::Primary, inside(sb, mouse), s);
        }
        self.hits.push((Hit::SellAll, ab));
        self.hits.push((Hit::Sell, sb));
    }
}

/// A tab that can't be opened yet: dimmed, with a padlock before its label.
fn locked_chip(ui: &mut Ui, r: [f32; 4], label: &str, size: f32, s: f32) {
    theme::fill(ui.renderer, r, r[3] / 2.0, color::CHIP, 0.5);
    let lock = 10.0 * s;
    let lw = ui.measure(label, size, true);
    let x = r[0] + (r[2] - lw - lock - 5.0 * s) / 2.0;
    let ly = r[1] + r[3] / 2.0 - lock * 0.15;
    // the shackle, then the body
    let t = (1.5 * s).max(1.0);
    ui.rect(x + lock * 0.2, ly - lock * 0.45, t, lock * 0.45, color::TEXT_MUTED, 1.0);
    ui.rect(x + lock * 0.8 - t, ly - lock * 0.45, t, lock * 0.45, color::TEXT_MUTED, 1.0);
    ui.rect(x + lock * 0.2, ly - lock * 0.45, lock * 0.6, t, color::TEXT_MUTED, 1.0);
    ui.rect(x, ly, lock, lock * 0.6, color::TEXT_MUTED, 1.0);
    ui.label(label, x + lock + 5.0 * s, r[1] + (r[3] - size * 1.22) / 2.0, size, true, color::TEXT_MUTED);
}

/// Where a recipe's result goes in the list: Oryx 5's Ascended gear first, then the other
/// untiered gear (UTs), then tiered gear from the highest tier down, then the rest (potions,
/// keys, materials).
fn order(cat: Category, tier: Option<i32>, ascended: bool) -> (u8, i32) {
    match (cat, tier) {
        (Category::Other | Category::All, _) => (3, 0),
        _ if ascended => (0, 0),
        (_, None) => (1, 0),
        (_, Some(t)) => (2, -t),
    }
}

/// The HUD's fame icon (`draw_credits`: lofiObj3's 224).
fn fame_icon(ui: &mut Ui, cx: f32, cy: f32, size: f32, s: f32) {
    ui.tile_icon("lofiObj3", 224, (size / s * 2.4).round() as i32, cx, cy, s);
}

/// How wide `fame_text` draws.
fn fame_width(ui: &mut Ui, text: &str, size: f32, s: f32) -> f32 {
    ui.measure(text, size, true) + 4.0 * s + size
}

/// An amount of fame: the number, then the fame icon. Returns how wide it is.
fn fame_text(ui: &mut Ui, text: &str, x: f32, y: f32, size: f32, rgb: u32, s: f32) -> f32 {
    let tw = ui.measure(text, size, true);
    ui.label(text, x, y, size, true, rgb);
    fame_icon(ui, x + tw + 4.0 * s + size / 2.0, y + ui.digit_middle(size, true), size, s);
    tw + 4.0 * s + size
}

/// A button whose label ends in an amount of fame, with the icon after it.
fn fame_button(ui: &mut Ui, r: [f32; 4], label: &str, kind: Kind, hover: bool, s: f32) {
    ui.button(r, "", kind, hover, s);
    let size = (15.0 * s).round().max(10.0);
    let text = if kind == Kind::Primary { color::ACCENT_TEXT } else { color::TEXT };
    let w = fame_width(ui, label, size, s);
    fame_text(ui, label, r[0] + (r[2] - w) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, text, s);
}

/// Gold sparks thrown out of the forge's mouth as it flares.
fn sparks(ui: &mut Ui, (cx, cy): (f32, f32), flare: f32, now: i32, s: f32) {
    for k in 0..18u32 {
        // Each spark's own angle, speed and start, fixed by its number.
        let h = k.wrapping_mul(2654435761);
        let a = (h % 360) as f32 * std::f32::consts::PI / 180.0;
        let speed = 50.0 + (h >> 9) as f32 % 70.0;
        let life = 500 + (h >> 17) as i32 % 300;
        let t = (now + (h >> 5) as i32 % life).rem_euclid(life) as f32 / life as f32;
        let d = speed * t * s * flare;
        let (x, y) = (cx + a.cos() * d, cy + a.sin() * d * 0.8 - 20.0 * t * t * s);
        let size = (3.0 * (1.0 - t) * s).max(1.0);
        let col = if k % 3 == 0 { 0xFFF6C4 } else { color::GOLD };
        if k % 4 == 0 {
            theme::star(ui.renderer, x, y, size * 1.6, col);
        } else {
            ui.rect(x - size / 2.0, y - size / 2.0, size, size, col, 1.0 - t);
        }
    }
}

/// Which category each slot type belongs to: the classes' `SlotTypes` list the weapon, ability,
/// armor and ring slots first.
fn slot_categories(data: &GameData) -> HashMap<i32, Category> {
    let mut kinds = HashMap::new();
    for d in data.objects.values().filter(|d| d.player) {
        let Some(list) = d.xml.text_of("SlotTypes") else { continue };
        for (i, t) in list.split(',').take(4).enumerate() {
            if let Ok(t) = t.trim().parse::<i32>() {
                let cat = [Category::Weapons, Category::Abilities, Category::Armor, Category::Rings][i];
                kinds.entry(t).or_insert(cat);
            }
        }
    }
    kinds.remove(&0);
    kinds
}

#[cfg(test)]
mod tests {
    use super::*;

    fn recipe(result: u16, items: &[u16]) -> ForgeRecipe {
        ForgeRecipe { result, fame: 100, advanced: false, items: items.to_vec().into() }
    }

    #[test]
    fn ascended_then_untiered_gear_first_then_tiers_down() {
        let mut keys = [
            order(Category::Other, None, false),
            order(Category::Weapons, Some(3), false),
            order(Category::Armor, None, false),
            order(Category::Rings, Some(8), false),
            order(Category::Weapons, None, true),
        ];
        keys.sort();
        assert_eq!(keys, [(0, 0), (1, 0), (2, -8), (2, -3), (3, 0)]);
    }

    #[test]
    fn sockets_take_distinct_inventory_slots() {
        let mut f = Forge { recipes: vec![recipe(9, &[5, 5, 6])], ..Default::default() };
        f.inventory = vec![(4, 5), (6, 6), (9, 5)];
        f.pick(0);
        assert!(f.can_make(&f.recipes[0].clone()));
        assert!(f.insert(0) && f.insert(1) && f.insert(2));
        assert_eq!(f.sockets, vec![Some(4), Some(9), Some(6)]);
        // A third item of a kind there are only two of isn't found.
        f.recipes = vec![recipe(9, &[5, 5, 5])];
        f.pick(0);
        assert!(f.insert(0) && f.insert(1) && !f.insert(2));
        assert!(!f.can_make(&f.recipes[0].clone()));
    }

    #[test]
    fn shards_come_from_the_forge_storage() {
        let mut f = Forge { recipes: vec![recipe(9, &[5, 7]), recipe(9, &[6, 7])], ..Default::default() };
        f.inventory = vec![(4, 6)];
        assert!(!f.can_make(&f.recipes[1].clone()));
        f.shards = vec![(7, 1)];
        assert!(f.can_make(&f.recipes[1].clone()));
        // picking the first recipe for item 9 picks the one that can be made
        f.pick(0);
        assert_eq!(f.picked, Some(1));
        assert!(f.insert(0) && f.insert(1));
        assert_eq!(f.sockets, vec![Some(4), Some(STORAGE)]);
        // one in storage fills one socket only
        f.recipes = vec![recipe(9, &[7, 7])];
        f.pick(0);
        assert!(f.insert(0) && !f.insert(1));
        // the list shows a result once
        f.recipes = vec![recipe(9, &[5, 7]), recipe(9, &[6, 7]), recipe(8, &[6])];
        f.filter(&GameData::default());
        assert_eq!(f.shown.len(), 2);
        assert!(f.shown.contains(&1));
    }

    #[test]
    fn a_forging_waits_for_the_fire_and_the_answer() {
        let mut f = Forge { recipes: vec![recipe(9, &[5, 6])], ..Default::default() };
        f.pick(0);
        f.sockets = vec![Some(4), Some(5)];
        f.forging = Some(Forging::Going { since: 0, answer: None });
        f.event(Event::Result { sold: false, success: true, item: 9, fame: 100, message: String::new() }, &GameData::default());
        f.now = 100;
        f.advance();
        assert!(matches!(f.forging, Some(Forging::Going { .. })));
        f.now = INTO_FIRE + FLARE;
        f.advance();
        assert_eq!(f.forging, Some(Forging::Done { since: INTO_FIRE + FLARE, item: 9 }));
        assert_eq!(f.sockets, vec![None, None]);
        // A refusal puts the sockets back and says why.
        f.pick(0);
        f.forging = Some(Forging::Going { since: 0, answer: None });
        f.event(Event::Result { sold: false, success: false, item: 9, fame: 0, message: "No".into() }, &GameData::default());
        f.advance();
        assert_eq!((f.forging.clone(), f.message.clone()), (None, Some(("No".into(), true))));
    }
}
