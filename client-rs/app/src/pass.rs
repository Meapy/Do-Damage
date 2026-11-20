//! The Realm Pass (the world server's `PassTracker.cs`): a season a month of 30 tiers, a free
//! track and a Gilded one (1,000 gold), filled with pass XP from what players do anyway: quests,
//! the daily reward, dungeons, bosses, the Arena, fame. It opens with Y, the HUD's pass button
//! beside Quests, or the Wayfarer by the Nexus spawn.
//!
//! The client asks once in each world (`PassAct` 0) and from then on hears every gain
//! (`PassXp`), so the HUD's button shows the tier and what waits even while the window is shut,
//! and pills of XP rise beside it. A tier reached shows a banner (and the server a column of
//! light round the player, `relic::look::PASS_TIER`). The window has three tabs: the rewards
//! track, how XP is earned, and the looks (the auras, drawn by `auras.rs`, and the name colours)
//! to wear. The server checks every act; the window only asks.

use crate::auras;
use crate::hud::{group_digits, object_icon, ui_icon, Layout, Line, SlotKind, Ui};
use crate::i18n::{mark, tf, tn, tr};
use crate::quests::{check, countdown, glow, inside, locked};
use crate::theme::{self, color, Kind};
use bsk_proto::{PassData, PassResult, PassReward, PassXp};
use bsk_render::{Renderer, Tint};
use bsk_world::GameObj;
use std::f32::consts::{PI, TAU};
use winit::keyboard::KeyCode;

/// What a `PassAct` asks (`PassTracker.REQUEST` and the rest).
pub mod act {
    pub const REQUEST: u8 = 0;
    pub const CLAIM: u8 = 1;
    pub const UNLOCK: u8 = 2;
    pub const AURA: u8 = 3;
    pub const COLOR: u8 = 4;
    pub const CACHE: u8 = 5;
}

/// `PassReward` kinds (`PassRewardInfo.Kind*`).
pub mod kind {
    pub const FAME: u8 = 0;
    pub const ITEM: u8 = 1;
    pub const GOLD: u8 = 3;
    pub const SKIN: u8 = 4;
    pub const AURA: u8 = 5;
    pub const COLOR: u8 = 6;
    pub const SLOT: u8 = 7;
}

/// `PassResult` codes (`PassCode`).
mod code {
    pub const CLAIMED: u8 = 0;
    pub const CLAIMED_ALL: u8 = 1;
    pub const UNLOCKED: u8 = 2;
    pub const CACHE_OPENED: u8 = 3;
    pub const AURA_WORN: u8 = 4;
    pub const AURA_OFF: u8 = 5;
    pub const COLOR_WORN: u8 = 6;
    pub const COLOR_OFF: u8 = 7;
    pub const NO_SEASON: u8 = 20;
    pub const GUEST: u8 = 21;
    pub const NOT_REACHED: u8 = 22;
    pub const ALREADY_CLAIMED: u8 = 23;
    pub const NOTHING: u8 = 24;
    pub const NOT_ENOUGH_GOLD: u8 = 25;
    pub const ALREADY_GILDED: u8 = 26;
    pub const PRICE_CHANGED: u8 = 27;
    pub const NOT_OWNED: u8 = 28;
    pub const NO_CACHE: u8 = 29;
}

/// What earns pass XP (`PassSource`), named for the window and the pills.
const SOURCES: [&str; 16] = [
    mark("Daily quest"),
    mark("Weekly quest"),
    mark("Monthly quest"),
    mark("Daily quest bonus"),
    mark("Weekly quest bonus"),
    mark("Monthly quest bonus"),
    mark("Daily reward"),
    mark("First dungeon of the day"),
    mark("Dungeon cleared"),
    mark("Boss defeated"),
    mark("Oryx defeated"),
    mark("Realm closed"),
    mark("Arena wave"),
    mark("Fame earned"),
    mark("Elder boss defeated"),
    mark("A gift"),
];
const SOURCE_FAME: u8 = 13;
const SOURCE_DUNGEON: u8 = 8;
const SOURCE_FIRST_DUNGEON: u8 = 7;

/// The Gilded track's gold, and its light.
const GILD: u32 = 0xFFD24A;
const GILD_LIGHT: u32 = 0xFFF1B8;

/// How long an XP pill and the tier banner show (ms).
const PILL_MS: i32 = 2600;
const BANNER_MS: i32 = 4200;
/// How long a reveal plays before it can be clicked away, and the most it stays.
const REVEAL_MS: i32 = 9000;
/// The tiers the track shows at once.
const COLUMNS: usize = 6;

/// What the window asks of the server.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Act {
    Request,
    /// A tier's rewards (its index), or every one waiting (-1).
    Claim(i32),
    /// The Gilded pass, at the price shown.
    Unlock(i32),
    Aura(u8),
    Color(u8),
    Caches,
}

impl Act {
    /// The `PassAct` it is: its kind and value.
    pub fn wire(&self) -> (u8, i32) {
        match *self {
            Act::Request => (act::REQUEST, 0),
            Act::Claim(t) => (act::CLAIM, t),
            Act::Unlock(price) => (act::UNLOCK, price),
            Act::Aura(id) => (act::AURA, id as i32),
            Act::Color(id) => (act::COLOR, id as i32),
            Act::Caches => (act::CACHE, 0),
        }
    }
}

/// What the server said.
#[derive(Clone, Debug)]
pub enum Event {
    Data(Box<PassData>),
    Xp(PassXp),
    Result(Box<PassResult>),
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum Tab {
    #[default]
    Rewards,
    Earn,
    Looks,
    Perks,
}

impl Tab {
    /// A tab by name (`--pass`): rewards, earn, looks, perks.
    pub fn named(name: &str) -> Option<Tab> {
        Some(match name.to_ascii_lowercase().as_str() {
            "rewards" | "track" | "" => Tab::Rewards,
            "earn" | "xp" => Tab::Earn,
            "looks" | "auras" => Tab::Looks,
            "perks" | "gilded" => Tab::Perks,
            _ => return None,
        })
    }
}

const TABS: [(Tab, &str); 4] =
    [(Tab::Rewards, mark("Rewards")), (Tab::Earn, mark("Earn XP")), (Tab::Looks, mark("Looks")), (Tab::Perks, mark("Gilded perks"))];

/// `/visit` and `/summon` trips into one instance, without and with the Gilded pass (the
/// server's `VisitRules.Trips` and `GildedTrips`).
const TRIPS: i32 = 1;
const GILDED_TRIPS: i32 = 2;

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Tab(Tab),
    Close,
    Claim(usize),
    ClaimAll,
    Gild,
    Confirm,
    Cancel,
    Caches,
    Left,
    Right,
    Jump(usize),
    Aura(u8),
    Color(u8),
    Dismiss,
}

/// What the mouse is over, for its tooltip.
#[derive(Clone, Debug)]
enum Hover {
    Reward(PassReward),
    Cache,
}

struct Pill {
    gained: i32,
    source: u8,
    momentum: bool,
    since: i32,
}

struct Reveal {
    title: String,
    rewards: Vec<PassReward>,
    since: i32,
}

/// What the HUD's pass button shows.
#[derive(Clone, Copy, Debug, Default)]
pub struct HudPass {
    pub tier: usize,
    pub tiers: usize,
    /// How far into the next tier (or the next cache, after the last).
    pub frac: f32,
    pub gilded: bool,
    pub accent: u32,
    pub ready: usize,
}

/// The Realm Pass window, its pills and its banner.
#[derive(Default)]
pub struct Pass {
    pub open: bool,
    tab: Tab,
    data: Option<PassData>,
    /// When (game ms) `data` came: the countdowns run on from its `now`.
    received: i32,
    /// The server's last word: (text, is it an error, when).
    message: Option<(String, bool, i32)>,
    hits: Vec<(Hit, [f32; 4])>,
    rect: Option<[f32; 4]>,
    /// The first tier the track shows.
    first: usize,
    /// The Gild confirmation is up.
    confirm: bool,
    reveal: Option<Reveal>,
    pills: Vec<Pill>,
    /// A tier reached: which, and when.
    banner: Option<(usize, i32)>,
    hover: Option<Hover>,
    now: i32,
    /// The key bound to the pass, for the banner.
    pub key: String,
}

/// The tier a total of XP reaches.
fn tier_of(d: &PassData) -> usize {
    if d.xp_per_tier <= 0 {
        return 0;
    }
    ((d.xp.max(0) / d.xp_per_tier) as usize).min(d.tiers.len())
}

/// The XP needed for every tier.
fn all_tiers_xp(d: &PassData) -> i32 {
    d.tiers.len() as i32 * d.xp_per_tier
}

/// Wayfarer's Caches filled and not yet opened.
fn caches_ready(d: &PassData) -> i32 {
    if !d.active || d.bonus_xp <= 0 || d.xp <= all_tiers_xp(d) {
        return 0;
    }
    ((d.xp - all_tiers_xp(d)) / d.bonus_xp - d.caches_opened).max(0)
}

/// How far into the tier being filled (or the cache, after the last): (XP into it, its size).
fn progress(d: &PassData) -> (i32, i32) {
    let t = tier_of(d);
    if t < d.tiers.len() {
        (d.xp - t as i32 * d.xp_per_tier, d.xp_per_tier.max(1))
    } else {
        let over = d.xp - all_tiers_xp(d);
        (over.rem_euclid(d.bonus_xp.max(1)), d.bonus_xp.max(1))
    }
}

fn rgb(c: i32, or: u32) -> u32 {
    if c == 0 { or } else { c as u32 & 0xFF_FFFF }
}

pub(crate) fn mix(a: u32, b: u32, t: f32) -> u32 {
    let t = t.clamp(0.0, 1.0);
    let ch = |s: u32| -> u32 {
        let (x, y) = (((a >> s) & 0xFF) as f32, ((b >> s) & 0xFF) as f32);
        ((x + (y - x) * t).round() as u32) << s
    };
    ch(16) | ch(8) | ch(0)
}

fn hash(a: u32, b: u32) -> f32 {
    let mut x = a.wrapping_mul(0x9E37_79B1) ^ b.wrapping_mul(0x85EB_CA77);
    x ^= x >> 15;
    x = x.wrapping_mul(0x2C1B_3C6D);
    x ^= x >> 12;
    (x & 0xFFFF) as f32 / 65536.0
}

impl Pass {
    /// Opens the window at a tab; the first time, it asks for the data.
    pub fn show(&mut self, tab: Option<Tab>) -> Option<Act> {
        self.open = true;
        self.message = None;
        self.confirm = false;
        if let Some(t) = tab {
            self.tab = t;
        }
        self.centre();
        self.data.is_none().then_some(Act::Request)
    }

    /// Opens what `--pass <name>` names, once its data has come.
    pub fn show_named(&mut self, name: &str) {
        self.show(Some(Tab::named(name).unwrap_or_default()));
    }

    pub fn close(&mut self) {
        self.open = false;
        self.confirm = false;
        self.reveal = None;
    }

    /// Forgets the data (a new character or account): the next join asks again.
    pub fn reset(&mut self) {
        self.data = None;
        self.close();
        self.pills.clear();
        self.banner = None;
    }

    pub fn loaded(&self) -> bool {
        self.data.is_some()
    }

    /// Rewards waiting to be claimed, and caches to open.
    pub fn ready(&self) -> usize {
        let Some(d) = self.data.as_ref().filter(|d| d.active) else { return 0 };
        let t = tier_of(d);
        let tiers = d.tiers.iter().take(t);
        let free = tiers.clone().filter(|x| !x.free.is_empty() && !x.free_claimed).count();
        let gilded = if d.gilded { tiers.filter(|x| !x.gilded.is_empty() && !x.gilded_claimed).count() } else { 0 };
        free + gilded + caches_ready(d) as usize
    }

    /// What the HUD's button shows, while a season runs.
    pub fn hud(&self) -> Option<HudPass> {
        let d = self.data.as_ref().filter(|d| d.active)?;
        let (into, size) = progress(d);
        Some(HudPass {
            tier: tier_of(d),
            tiers: d.tiers.len(),
            frac: (into as f32 / size as f32).clamp(0.0, 1.0),
            gilded: d.gilded,
            accent: rgb(d.accent, color::ACCENT),
            ready: self.ready(),
        })
    }

    /// The tier to show first: the first with a reward waiting, else the one being filled.
    fn centre(&mut self) {
        let Some(d) = &self.data else { return };
        let t = tier_of(d);
        let waiting = d.tiers.iter().take(t).position(|x| (!x.free.is_empty() && !x.free_claimed) || (d.gilded && !x.gilded.is_empty() && !x.gilded_claimed));
        let at = waiting.unwrap_or(t.min(d.tiers.len().saturating_sub(1)));
        self.first = at.saturating_sub(1).min(d.tiers.len().saturating_sub(COLUMNS));
    }

    /// The server's time now, in Unix seconds.
    fn server_now(&self) -> i64 {
        self.data.as_ref().map_or(0, |d| d.now as i64 + ((self.now - self.received).max(0) / 1000) as i64)
    }

    pub fn event(&mut self, e: Event, now: i32) {
        self.now = now;
        match e {
            Event::Data(d) => {
                log::info!("pass: {} ({}), {} XP, tier {}, gilded {}", d.season_name, d.season_key, d.xp, tier_of(&d), d.gilded);
                let first = self.data.is_none();
                self.received = now;
                self.data = Some(*d);
                if first {
                    self.centre();
                }
            }
            Event::Xp(x) => {
                self.pills.push(Pill { gained: x.gained, source: x.source, momentum: x.momentum, since: now });
                if self.pills.len() > 4 {
                    self.pills.remove(0);
                }
                if let Some(d) = &mut self.data {
                    let before = tier_of(d);
                    d.xp = x.xp;
                    let after = tier_of(d);
                    if after > before {
                        self.banner = Some((after, now));
                    }
                }
            }
            Event::Result(r) => {
                let text = self.result_text(&r);
                self.message = Some((text, !r.success, now));
                if r.success && !r.rewards.is_empty() {
                    let title = if r.code == code::CACHE_OPENED { tr("The Wayfarer's Cache opens!") } else { tr("Rewards claimed!") };
                    self.reveal = Some(Reveal { title: title.to_owned(), rewards: r.rewards.0.clone(), since: now });
                }
                if r.success && r.code == code::UNLOCKED {
                    self.confirm = false;
                    self.reveal = Some(Reveal { title: tr("Your pass is Gilded!").to_owned(), rewards: Vec::new(), since: now });
                }
            }
        }
    }

    /// A result in the player's language (the server's English for what we don't know).
    fn result_text(&self, r: &PassResult) -> String {
        let name_of = |list: Option<&[bsk_proto::PassCosmetic]>, id: i32| list.and_then(|l| l.iter().find(|c| c.id as i32 == id)).map_or(String::new(), |c| c.name.clone());
        let d = self.data.as_ref();
        match r.code {
            code::CLAIMED => tf!("Tier {n} claimed!", n = r.count),
            code::CLAIMED_ALL => tn!(r.count, "{n} tier claimed!|{n} tiers claimed!"),
            code::UNLOCKED => tr("Your pass is Gilded: every Gilded reward you've passed is waiting.").into(),
            code::CACHE_OPENED => tn!(r.count, "{n} Wayfarer's Cache opened!|{n} Wayfarer's Caches opened!"),
            code::AURA_WORN => tf!("You wear {name}.", name = name_of(d.map(|d| &d.auras[..]), r.count)),
            code::AURA_OFF => tr("You wear no aura.").into(),
            code::COLOR_WORN => tf!("Your name shines in {name}.", name = name_of(d.map(|d| &d.colors[..]), r.count)),
            code::COLOR_OFF => tr("Your name takes its achievement colour again.").into(),
            code::NO_SEASON => tr("No season is running right now.").into(),
            code::GUEST => tr("Register your account to play the Realm Pass.").into(),
            code::NOT_REACHED => tr("You haven't reached that tier yet.").into(),
            code::ALREADY_CLAIMED => tr("Already claimed.").into(),
            code::NOTHING => tr("Nothing to claim right now.").into(),
            code::NOT_ENOUGH_GOLD => {
                let price = group_digits(d.map_or(0, |d| d.price));
                tf!("The Gilded pass costs {price} gold; you have {gold}.", price, gold = group_digits(r.count))
            }
            code::ALREADY_GILDED => tr("Your pass is already Gilded.").into(),
            code::PRICE_CHANGED => tr("The price has changed: have another look.").into(),
            code::NOT_OWNED => tr("You don't have that yet.").into(),
            code::NO_CACHE => tr("No Wayfarer's Cache to open yet.").into(),
            _ => r.message.clone(),
        }
    }

    pub fn click(&mut self, pos: (f64, f64)) -> Option<Act> {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.close();
            return None;
        }
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| *h)?;
        let d = self.data.as_ref();
        match hit {
            Hit::Tab(t) => {
                self.tab = t;
                self.message = None;
            }
            Hit::Close => self.close(),
            Hit::Claim(t) => return Some(Act::Claim(t as i32)),
            Hit::ClaimAll => return Some(Act::Claim(-1)),
            Hit::Gild => self.confirm = true,
            Hit::Cancel => self.confirm = false,
            Hit::Confirm => {
                self.confirm = false;
                return d.map(|d| Act::Unlock(d.price));
            }
            Hit::Caches => return Some(Act::Caches),
            Hit::Left => self.scroll(-(COLUMNS as i32)),
            Hit::Right => self.scroll(COLUMNS as i32),
            Hit::Jump(t) => {
                let n = d.map_or(0, |d| d.tiers.len());
                self.first = t.saturating_sub(COLUMNS / 2).min(n.saturating_sub(COLUMNS));
            }
            Hit::Aura(id) => return Some(Act::Aura(id)),
            Hit::Color(id) => return Some(Act::Color(id)),
            Hit::Dismiss => self.reveal = None,
        }
        None
    }

    pub fn scroll(&mut self, by: i32) {
        if self.tab != Tab::Rewards {
            return;
        }
        let n = self.data.as_ref().map_or(0, |d| d.tiers.len());
        let max = n.saturating_sub(COLUMNS) as i32;
        self.first = (self.first as i32 + by).clamp(0, max) as usize;
    }

    /// A key while the window is open: Escape closes it (or the confirmation), the arrows move
    /// along the track, Tab changes tab.
    pub fn key(&mut self, code: KeyCode) {
        match code {
            KeyCode::Escape if self.confirm => self.confirm = false,
            KeyCode::Escape if self.reveal.is_some() => self.reveal = None,
            KeyCode::Escape => self.close(),
            KeyCode::ArrowLeft => self.scroll(-1),
            KeyCode::ArrowRight => self.scroll(1),
            KeyCode::Tab => {
                let at = TABS.iter().position(|&(t, _)| t == self.tab).unwrap_or(0);
                self.tab = TABS[(at + 1) % TABS.len()].0;
            }
            _ => {}
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.now = now;
        self.hits.clear();
        self.hover = None;
        if !self.open {
            self.rect = None;
            return;
        }
        let (dw, dh) = (940.0, 620.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        let Some(d) = self.data.clone() else {
            ui.label(tr("Realm Pass"), r[0] + 22.0 * s, r[1] + 20.0 * s, font(22.0), true, color::GOLD);
            ui.label(tr("Asking the Wayfarer about the season..."), r[0] + 22.0 * s, r[1] + 70.0 * s, font(14.0), false, color::TEXT_MUTED);
            self.close_button(ui, r, s, mouse);
            return;
        };
        // a reveal or the confirmation takes every click
        if self.reveal.as_ref().is_some_and(|v| now - v.since > REVEAL_MS) {
            self.reveal = None;
        }
        // (the confirmation pushes its own, last)
        if self.reveal.is_some() {
            self.hits.push((Hit::Dismiss, r));
        }
        let modal = self.reveal.is_some() || self.confirm;
        let mark_hits = self.hits.len();

        let head = [r[0] + 12.0 * s, r[1] + 12.0 * s, r[2] - 24.0 * s, 132.0 * s];
        self.draw_header(ui, &d, head, s, now);
        // the tabs, and the Gilded pass's button or badge
        let ty = head[1] + head[3] + 12.0 * s;
        let cs = font(13.0);
        let mut x = r[0] + 22.0 * s;
        for (t, label) in TABS {
            let label = tr(label);
            let badge = if t == Tab::Rewards { Some(self.ready()) } else { None };
            let w = ui.measure(label, cs, true) + if badge.unwrap_or(0) > 0 { 46.0 } else { 28.0 } * s;
            let tr_ = [x, ty, w, 28.0 * s];
            crate::quests::chip(ui, tr_, label, t == self.tab, inside(tr_, mouse), cs, badge, s);
            self.hits.push((Hit::Tab(t), tr_));
            x += w + 6.0 * s;
        }
        if d.active {
            self.draw_gild(ui, &d, me, [r[0] + r[2] - 22.0 * s, ty, 0.0, 28.0 * s], s, mouse, now);
        }

        let body = [r[0] + 22.0 * s, ty + 40.0 * s, r[2] - 44.0 * s, r[1] + r[3] - (ty + 40.0 * s) - 44.0 * s];
        if !d.active {
            self.draw_between(ui, &d, body, s);
        } else {
            match self.tab {
                Tab::Rewards => self.draw_track(ui, &d, body, s, mouse, now),
                Tab::Earn => self.draw_earn(ui, &d, body, s),
                Tab::Looks => self.draw_looks(ui, &d, me, body, s, mouse, now),
                Tab::Perks => self.draw_perks(ui, &d, body, s, now),
            }
        }
        let foot_y = r[1] + r[3] - 32.0 * s;
        if let Some((m, err, _)) = &self.message {
            ui.label_fit(m, r[0] + 22.0 * s, foot_y, r[2] - 44.0 * s, font(13.0), true, if *err { color::DANGER } else { color::GOOD });
        } else {
            let hint = tr("Rewards: fame and gold to your account, items to the gift chest in your vault.");
            ui.label_fit(hint, r[0] + 22.0 * s, foot_y, r[2] - 44.0 * s, font(12.0), false, color::TEXT_MUTED);
        }
        if modal {
            // what was drawn under the reveal or the confirmation can't be clicked
            self.hits.truncate(mark_hits);
            self.hover = None;
        }
        if self.confirm {
            self.draw_confirm(ui, &d, me, r, s, mouse, now);
        } else if let Some(v) = &self.reveal {
            let (title, rewards, since) = (v.title.clone(), v.rewards.clone(), v.since);
            self.draw_reveal(ui, &d, r, &title, &rewards, now - since, s, mouse, now);
        }
        self.close_button(ui, r, s, mouse);
        self.draw_hover(ui, lay, &d, me, mouse);
    }

    fn close_button(&mut self, ui: &mut Ui, r: [f32; 4], s: f32, mouse: (f64, f64)) {
        let cb = [r[0] + r[2] - 50.0 * s, r[1] + 22.0 * s, 28.0 * s, 28.0 * s];
        let hover = inside(cb, mouse);
        theme::fill(ui.renderer, cb, 6.0 * s, 0x000000, if hover { 0.45 } else { 0.25 });
        theme::cross(ui.renderer, cb[0] + cb[2] / 2.0, cb[1] + cb[3] / 2.0, 6.0 * s, (2.0 * s).max(1.0), if hover { color::TEXT } else { color::TEXT_DIM });
        if self.reveal.is_none() {
            self.hits.insert(0, (Hit::Close, cb));
        }
    }

    /// The season's sky: its colours, its weather, mountains, its name and the time left, and
    /// the tier crest with the XP.
    fn draw_header(&mut self, ui: &mut Ui, d: &PassData, h: [f32; 4], s: f32, now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let accent = rgb(d.accent, color::ACCENT);
        let dark = rgb(d.dark, 0x120C20);
        let light = rgb(d.light, 0xF1ECFF);
        theme::rounded(ui.renderer, h, 10.0 * s, (mix(dark, 0x000000, 0.35), 1.0), (mix(dark, accent, 0.28), 1.0));
        sky(ui.renderer, h, &d.theme, accent, light, dark, now, s);
        theme::split_ring(ui.renderer, h, 10.0 * s, s.max(1.0), &[(mix(accent, dark, 0.3), 0.9)]);

        let tx = h[0] + 22.0 * s;
        ui.label(tr("REALM PASS"), tx, h[1] + 16.0 * s, font(12.0), true, mix(light, accent, 0.45));
        if !d.active {
            ui.label(tr("Between seasons"), tx, h[1] + 36.0 * s, font(32.0), true, light);
            return;
        }
        ui.label(&d.season_name, tx, h[1] + 34.0 * s, font(34.0), true, 0xFFFFFF);
        let left = d.ends_at as i64 - self.server_now();
        let ends = tf!("Ends in {time}", time = countdown(left));
        ui.label(&ends, tx, h[1] + 82.0 * s, font(14.0), true, light);
        if d.gilded {
            let ew = ui.measure(&ends, font(14.0), true);
            let gx = tx + ew + 14.0 * s;
            let label = tr("Gilded");
            let gw = ui.measure(label, font(12.0), true) + 30.0 * s;
            let gr = [gx, h[1] + 80.0 * s, gw, 22.0 * s];
            theme::rounded(ui.renderer, gr, 11.0 * s, (GILD_LIGHT, 1.0), (GILD, 1.0));
            theme::star(ui.renderer, gr[0] + 12.0 * s, gr[1] + 11.5 * s, 6.0 * s, 0x6A4A00);
            ui.label(label, gr[0] + 21.0 * s, gr[1] + 3.0 * s, font(12.0), true, 0x3A2600);
        }
        let sub = if d.momentum > 0 { None } else { Some(tr("Pass XP comes from what you do anyway: quests, dungeons, bosses, fame.")) };
        if let Some(sub) = sub {
            ui.label_fit(sub, tx, h[1] + 106.0 * s, h[2] * 0.55, font(12.0), false, mix(light, dark, 0.3));
        } else {
            // Momentum: behind the pace, XP comes faster
            let t = tf!("Momentum: +{n}% pass XP while you catch up", n = d.momentum);
            let w = ui.measure(&t, font(12.0), true) + 34.0 * s;
            let mr = [tx, h[1] + 104.0 * s, w, 22.0 * s];
            let pulse = 0.5 + 0.5 * (now as f32 / 300.0).sin();
            theme::fill(ui.renderer, [mr[0] - 2.0 * s, mr[1] - 2.0 * s, mr[2] + 4.0 * s, mr[3] + 4.0 * s], 13.0 * s, color::GOOD, 0.15 + 0.15 * pulse);
            theme::fill(ui.renderer, mr, 11.0 * s, 0x0A2A1C, 0.9);
            chevrons(ui.renderer, mr[0] + 14.0 * s, mr[1] + 11.0 * s, 5.0 * s, color::GOOD, now);
            ui.label(&t, mr[0] + 26.0 * s, mr[1] + 3.0 * s, font(12.0), true, color::GOOD);
        }

        // The crest: the tier in a ring that fills with XP.
        let (cx, cy, rad) = (h[0] + h[2] - 108.0 * s, h[1] + h[3] / 2.0 + 4.0 * s, 46.0 * s);
        let tier = tier_of(d);
        let (into, size) = progress(d);
        let k = into as f32 / size as f32;
        let ring_c = if d.gilded { GILD } else { accent };
        glow_disc(ui.renderer, cx, cy, rad * 1.7, ring_c, 0.28);
        disc(ui.renderer, cx, cy, rad, mix(dark, 0x000000, 0.35), 0.95);
        arc(ui.renderer, cx, cy, rad - 7.0 * s, rad, -PI / 2.0, -PI / 2.0 + TAU, 0x000000, 0.5);
        if k > 0.0 {
            arc(ui.renderer, cx, cy, rad - 7.0 * s, rad, -PI / 2.0, -PI / 2.0 + TAU * k, ring_c, 1.0);
            // a spark at its end
            let a = -PI / 2.0 + TAU * k;
            let (ex, ey) = (cx + (rad - 3.5 * s) * a.cos(), cy + (rad - 3.5 * s) * a.sin());
            glow_disc(ui.renderer, ex, ey, 9.0 * s, 0xFFFFFF, 0.8);
        }
        if d.gilded {
            // the Gilded crest wears a crown
            ui_icon(ui, crate::hud::ui_icon::CROWN, cx, cy - rad - 2.0 * s, 26.0 * s);
        }
        let label = tr("TIER");
        let lw = ui.measure(label, font(10.0), true);
        ui.label(label, cx - lw / 2.0, cy - 26.0 * s, font(10.0), true, mix(light, accent, 0.4));
        let n = tier.to_string();
        let ns = font(34.0);
        let nw = ui.measure(&n, ns, true);
        ui.label(&n, cx - nw / 2.0, cy - 14.0 * s, ns, true, 0xFFFFFF);
        let of = tf!("of {n}", n = d.tiers.len());
        let ow = ui.measure(&of, font(10.0), true);
        ui.label(&of, cx - ow / 2.0, cy + 22.0 * s, font(10.0), true, mix(light, dark, 0.2));
        // the XP beside it
        let xp_t = if tier < d.tiers.len() {
            tf!("{into} / {size} XP to tier {next}", into = group_digits(into), size = group_digits(size), next = tier + 1)
        } else {
            tf!("{into} / {size} XP to a Wayfarer's Cache", into = group_digits(into), size = group_digits(size))
        };
        let xs = font(12.0);
        let xw = ui.measure(&xp_t, xs, true);
        let xr = [cx - rad - 22.0 * s - xw.max(150.0 * s), cy - 6.0 * s, xw.max(150.0 * s), 6.0 * s];
        ui.label(&xp_t, xr[0] + xr[2] - xw, xr[1] - xs * 1.5, xs, true, light);
        theme::fill(ui.renderer, xr, 3.0 * s, 0x000000, 0.45);
        theme::rounded(ui.renderer, [xr[0], xr[1], (xr[2] * k).max(xr[3]), xr[3]], 3.0 * s, (mix(ring_c, 0xFFFFFF, 0.3), 1.0), (ring_c, 1.0));
        let total = tf!("{xp} pass XP this season", xp = group_digits(d.xp));
        let tw = ui.measure(&total, font(11.0), false);
        ui.label(&total, xr[0] + xr[2] - tw, xr[1] + 12.0 * s, font(11.0), false, mix(light, dark, 0.25));
    }

    /// The Gilded pass: a gold button with its price, or nothing once it's Gilded (the header
    /// says so). `at` is the right end of where it goes.
    #[allow(clippy::too_many_arguments)]
    fn draw_gild(&mut self, ui: &mut Ui, d: &PassData, me: Option<&GameObj>, at: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        if d.gilded {
            let t = if d.community_luck > 0 || d.community_rolls > 0 { tr("Gilded: luckier community goal loot too") } else { tr("Gilded") };
            let size = (12.0 * s).round().max(9.0);
            let w = ui.measure(t, size, true);
            ui.label(t, at[0] - w, at[1] + 6.0 * s, size, true, GILD);
            return;
        }
        let size = (14.0 * s).round().max(10.0);
        let label = tf!("Gild your pass  ·  {price}", price = group_digits(d.price));
        let w = ui.measure(&label, size, true) + 58.0 * s;
        let b = [at[0] - w, at[1] - 4.0 * s, w, 36.0 * s];
        let hover = inside(b, mouse);
        let pulse = 0.5 + 0.5 * (now as f32 / 420.0).sin();
        theme::fill(ui.renderer, [b[0] - 4.0 * s, b[1] - 4.0 * s, b[2] + 8.0 * s, b[3] + 8.0 * s], 10.0 * s, GILD, 0.12 + 0.12 * pulse);
        theme::shadow(ui.renderer, b, 7.0 * s, 4.0 * s, 0.4);
        theme::rounded(ui.renderer, b, 7.0 * s, (if hover { 0xFFF6D0 } else { GILD_LIGHT }, 1.0), (if hover { 0xFFC93A } else { 0xE8A820 }, 1.0));
        // a shine sweeping across now and then
        let sweep = ((now % 3200) as f32 / 900.0).min(1.5);
        if sweep < 1.2 {
            let sx = b[0] + (sweep / 1.2) * (b[2] + 30.0 * s) - 15.0 * s;
            let white = ui.renderer.white();
            let (top, bot) = (b[1] + 2.0 * s, b[1] + b[3] - 2.0 * s);
            let pts = [[sx, top], [sx + 12.0 * s, top], [sx - 6.0 * s, bot]];
            if sx > b[0] + 6.0 * s && sx < b[0] + b[2] - 14.0 * s {
                ui.renderer.triangle(white, pts, [[0.5, 0.5]; 3], Tint::color(0xFFFFFF, 0.55));
                ui.renderer.triangle(white, [[sx + 12.0 * s, top], [sx + 6.0 * s, bot], [sx - 6.0 * s, bot]], [[0.5, 0.5]; 3], Tint::color(0xFFFFFF, 0.55));
            }
        }
        ui.tile_icon("lofiObj3", 225, 40, b[0] + 20.0 * s, b[1] + b[3] / 2.0, s);
        ui.label(&label, b[0] + 38.0 * s, b[1] + (b[3] - size * 1.22) / 2.0, size, true, 0x3A2600);
        self.hits.push((Hit::Gild, b));
        let _ = me;
    }

    /// Between seasons: when the next comes.
    fn draw_between(&mut self, ui: &mut Ui, d: &PassData, body: [f32; 4], s: f32) {
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label(tr("The roads are quiet between seasons."), body[0], body[1] + 10.0 * s, font(18.0), true, color::TEXT);
        if !d.next_name.is_empty() {
            let t = tf!("Next: {name}, in {time}.", name = d.next_name, time = countdown(d.next_starts_at as i64 - self.server_now()));
            ui.label(&t, body[0], body[1] + 42.0 * s, font(14.0), false, color::TEXT_DIM);
        }
    }

    /// The track: the tiers in a row, a node each on a line that fills as they're reached, the
    /// free rewards under it and the Gilded ones under those; then Claim all and the caches.
    fn draw_track(&mut self, ui: &mut Ui, d: &PassData, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let n = d.tiers.len();
        if n == 0 {
            return;
        }
        self.first = self.first.min(n.saturating_sub(COLUMNS));
        let accent = rgb(d.accent, color::ACCENT);
        let tier = tier_of(d);
        let (into, size) = progress(d);
        let pulse = 0.5 + 0.5 * (now as f32 / 260.0).sin();
        let lw = 92.0 * s;
        let arrow = 26.0 * s;
        let gap = 8.0 * s;
        let x0 = body[0] + lw + arrow + gap;
        let x1 = body[0] + body[2] - arrow - gap;
        let cw = (x1 - x0 - gap * (COLUMNS - 1) as f32) / COLUMNS as f32;
        let node_y = body[1] + 16.0 * s;
        let row_h = 124.0 * s;
        let rows_y = [node_y + 26.0 * s, node_y + 26.0 * s + row_h + 10.0 * s];
        let col_x = |c: usize| x0 + c as f32 * (cw + gap);
        let shown = self.first..(self.first + COLUMNS).min(n);

        // the row labels
        for (k, y) in rows_y.iter().enumerate() {
            let gilded_row = k == 1;
            let lr = [body[0], *y, lw - 8.0 * s, row_h];
            let (top, bot) = if gilded_row { (0x3A2A10, 0x1E140A) } else { (color::INSET_TOP, color::INSET_BOTTOM) };
            theme::frame(ui.renderer, lr, 8.0 * s, s.max(1.0), (if gilded_row { mix(GILD, 0x000000, 0.35) } else { color::BORDER }, 1.0), ((top, 0.95), (bot, 0.95)));
            let (cx, cy) = (lr[0] + lr[2] / 2.0, lr[1] + lr[3] / 2.0 - 10.0 * s);
            if gilded_row {
                ui_icon(ui, crate::hud::ui_icon::CROWN, cx, cy - 6.0 * s, 26.0 * s);
            } else {
                theme::star(ui.renderer, cx, cy - 4.0 * s, 10.0 * s, color::TEXT_DIM);
            }
            let label = if gilded_row { tr("Gilded") } else { tr("Free") };
            let ls = font(14.0);
            let w = ui.measure(label, ls, true).min(lr[2] - 8.0 * s);
            ui.label_fit(label, cx - w / 2.0, cy + 14.0 * s, lr[2] - 8.0 * s, ls, true, if gilded_row { GILD } else { color::TEXT });
            if gilded_row && !d.gilded {
                locked(ui, [cx - 16.0 * s, cy + 36.0 * s, 32.0 * s, 20.0 * s], s * 0.8);
            }
        }

        // the line through the nodes, filled to where we are
        let line_y = node_y;
        let (lx0, lx1) = (col_x(0) + cw / 2.0, col_x(shown.len() - 1) + cw / 2.0);
        theme::fill(ui.renderer, [lx0 - cw / 2.0, line_y - 2.0 * s, lx1 - lx0 + cw, 4.0 * s], 2.0 * s, 0x000000, 0.5);
        let filled_to = |t: f32| -> f32 { col_x(0) + cw / 2.0 + (t - self.first as f32) * (cw + gap) };
        let reach = tier as f32 + if tier < n { into as f32 / size as f32 } else { 0.0 } - 1.0;
        let fx = filled_to(reach).clamp(lx0 - cw / 2.0, lx1 + cw / 2.0);
        if fx > lx0 - cw / 2.0 {
            theme::rounded(ui.renderer, [lx0 - cw / 2.0, line_y - 2.0 * s, fx - (lx0 - cw / 2.0), 4.0 * s], 2.0 * s, (mix(accent, 0xFFFFFF, 0.35), 1.0), (accent, 1.0));
            glow_disc(ui.renderer, fx, line_y, 10.0 * s, accent, 0.5);
        }

        for (c, t) in shown.clone().enumerate() {
            let x = col_x(c);
            let tinfo = &d.tiers[t];
            let reached = t < tier;
            let current = t == tier;
            // the node
            let (nx, ny) = (x + cw / 2.0, node_y);
            let nr = 13.0 * s;
            if current {
                glow_disc(ui.renderer, nx, ny, nr * 2.0, accent, 0.25 + 0.2 * pulse);
            }
            disc(ui.renderer, nx, ny, nr, if reached { accent } else { 0x1A1430 }, 1.0);
            ring(ui.renderer, nx, ny, nr, (1.5 * s).max(1.0), if reached { mix(accent, 0xFFFFFF, 0.4) } else if current { accent } else { color::BORDER_HI }, 1.0);
            let label = (t + 1).to_string();
            let ls = font(12.0);
            let w = ui.measure(&label, ls, true);
            ui.label(&label, nx - w / 2.0, (ny - ui.digit_middle(ls, true)).round(), ls, true, if reached { color::ACCENT_TEXT } else { color::TEXT });
            // the column under the tier being filled is lit
            if current {
                theme::fill(ui.renderer, [x - 3.0 * s, rows_y[0] - 4.0 * s, cw + 6.0 * s, 2.0 * row_h + 18.0 * s], 9.0 * s, accent, 0.08);
            }
            for (k, y) in rows_y.iter().enumerate() {
                let gilded_row = k == 1;
                let rewards: &[PassReward] = if gilded_row { &tinfo.gilded } else { &tinfo.free };
                let claimed = if gilded_row { tinfo.gilded_claimed } else { tinfo.free_claimed };
                let locked_ = gilded_row && !d.gilded;
                let ready = reached && !claimed && !locked_ && !rewards.is_empty();
                let cr = [x, *y, cw, row_h];
                self.reward_cell(ui, cr, rewards, gilded_row, reached, claimed, locked_, ready, pulse, s, mouse, now);
                if ready {
                    self.hits.push((Hit::Claim(t), cr));
                }
            }
        }

        // the arrows
        let ay = rows_y[0] + row_h - arrow / 2.0 + 5.0 * s;
        let left = [body[0] + lw, ay, arrow, arrow];
        let right = [x1 + gap, ay, arrow, arrow];
        for (r, dir, hit, can) in [(left, -1.0, Hit::Left, self.first > 0), (right, 1.0, Hit::Right, self.first + COLUMNS < n)] {
            let hover = can && inside(r, mouse);
            theme::fill(ui.renderer, r, r[3] / 2.0, if hover { color::CHIP_HI } else { color::CHIP }, if can { 1.0 } else { 0.4 });
            chevron(ui.renderer, r[0] + r[2] / 2.0, r[1] + r[3] / 2.0, 6.0 * s, dir, if can { color::TEXT } else { color::TEXT_MUTED });
            if can {
                self.hits.push((hit, r));
            }
        }

        // the whole season in a strip: a mark a tier, the part shown outlined
        let sy = rows_y[1] + row_h + 14.0 * s;
        let sw = x1 - x0;
        let seg = sw / n as f32;
        for t in 0..n {
            let ti = &d.tiers[t];
            let waiting = t < tier && ((!ti.free.is_empty() && !ti.free_claimed) || (d.gilded && !ti.gilded.is_empty() && !ti.gilded_claimed));
            let skin = ti.gilded.iter().chain(ti.free.iter()).any(|r| r.kind == kind::SKIN || r.kind == kind::AURA);
            let c = if waiting { mix(GILD, 0xFFFFFF, 0.3 * pulse) } else if t < tier { accent } else { 0x2A2240 };
            let mr = [x0 + t as f32 * seg + 1.0 * s, sy, seg - 2.0 * s, 8.0 * s];
            theme::fill(ui.renderer, mr, 2.0 * s, c, if t < tier || waiting { 1.0 } else { 0.9 });
            if skin {
                theme::star(ui.renderer, mr[0] + mr[2] / 2.0, sy + 16.0 * s, 4.0 * s, if t < tier { GILD } else { color::TEXT_MUTED });
            }
            self.hits.push((Hit::Jump(t), [mr[0], sy - 4.0 * s, seg, 26.0 * s]));
        }
        let view = [x0 + self.first as f32 * seg - 2.0 * s, sy - 4.0 * s, seg * COLUMNS.min(n) as f32 + 4.0 * s, 16.0 * s];
        theme::split_ring(ui.renderer, view, 4.0 * s, s.max(1.0), &[(color::TEXT, 0.8)]);

        // Claim all, and the Wayfarer's Caches
        let fy = sy + 32.0 * s;
        let ready = self.ready() as i32 - caches_ready(d);
        let bb = [body[0], fy, 170.0 * s, 38.0 * s];
        if ready > 0 {
            glow(ui, bb, pulse, s);
            ui.button(bb, &tf!("Claim all ({n})", n = ready), Kind::Primary, inside(bb, mouse), s);
            self.hits.push((Hit::ClaimAll, bb));
        } else {
            let t = if tier < n { tr("Every reward you've reached is yours.") } else { tr("The whole road walked!") };
            ui.label_fit(t, bb[0], bb[1] + 10.0 * s, 260.0 * s, font(13.0), true, color::TEXT_DIM);
        }
        self.draw_caches(ui, d, [body[0] + 290.0 * s, fy - 4.0 * s, body[2] - 290.0 * s, 46.0 * s], s, mouse, pulse);
    }

    /// One tier's rewards on one track.
    #[allow(clippy::too_many_arguments)]
    fn reward_cell(&mut self, ui: &mut Ui, r: [f32; 4], rewards: &[PassReward], gilded: bool, reached: bool, claimed: bool, locked_: bool, ready: bool, pulse: f32, s: f32, mouse: (f64, f64), now: i32) {
        let hover = inside(r, mouse);
        if ready {
            let g = 2.0 * s + pulse * 3.0 * s;
            theme::fill(ui.renderer, [r[0] - g, r[1] - g, r[2] + 2.0 * g, r[3] + 2.0 * g], 10.0 * s + g, if gilded { GILD } else { color::ACCENT }, 0.2);
        }
        let border = if ready {
            if gilded { GILD } else { color::ACCENT }
        } else if hover {
            color::BORDER_HI
        } else if gilded {
            mix(GILD, 0x000000, 0.55)
        } else {
            color::BORDER
        };
        let (top, bot) = if gilded { (0x2C2012, 0x17100A) } else { (color::INSET_TOP, color::INSET_BOTTOM) };
        let dim = if claimed { 0.55 } else { 0.95 };
        theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (border, 1.0), ((top, dim), (bot, dim)));
        if rewards.is_empty() {
            theme::star(ui.renderer, r[0] + r[2] / 2.0, r[1] + r[3] / 2.0 - 8.0 * s, 6.0 * s, color::CHIP_HI);
            return;
        }
        // the rewards side by side, the first one's name under them
        let tile = if rewards.len() == 1 { 58.0 * s } else { (r[2] - 18.0 * s) / rewards.len() as f32 - 4.0 * s }.min(58.0 * s);
        let total = rewards.len() as f32 * (tile + 4.0 * s) - 4.0 * s;
        let mut x = r[0] + (r[2] - total) / 2.0;
        let cy = r[1] + 12.0 * s + tile / 2.0;
        let faded = claimed || locked_ || !reached;
        for rw in rewards {
            let tr_ = [x, cy - tile / 2.0, tile, tile];
            draw_reward(ui, rw, tr_, s, now, if faded { 0.5 } else { 1.0 });
            if inside(tr_, mouse) || (rewards.len() == 1 && hover) {
                self.hover = Some(Hover::Reward(rw.clone()));
            }
            x += tile + 4.0 * s;
        }
        let caption = rewards.iter().map(|r| caption(ui.data, r)).collect::<Vec<_>>().join(" + ");
        let cs = (11.5 * s).round().max(9.0);
        let room = r[2] - 10.0 * s;
        let fit = crate::npc::fit(ui, &caption, cs, room);
        let w = ui.measure(&fit, cs, true);
        let cc = if claimed { color::TEXT_MUTED } else if gilded { mix(GILD, 0xFFFFFF, 0.25) } else { color::TEXT };
        ui.label(&fit, r[0] + (r[2] - w) / 2.0, r[1] + r[3] - 38.0 * s, cs, true, cc);
        // its state along the bottom
        let sy = r[1] + r[3] - 20.0 * s;
        if claimed {
            check(ui, r[0] + r[2] / 2.0 - 22.0 * s, sy + 2.0 * s, 5.0 * s, s);
            ui.label(tr("Claimed"), r[0] + r[2] / 2.0 - 14.0 * s, sy - 5.0 * s, (11.0 * s).round().max(9.0), true, color::GOOD);
        } else if ready {
            let t = tr("Claim");
            let ts = (12.0 * s).round().max(9.0);
            let tw = ui.measure(t, ts, true);
            let br = [r[0] + (r[2] - tw - 24.0 * s) / 2.0, sy - 7.0 * s, tw + 24.0 * s, 20.0 * s];
            theme::fill(ui.renderer, br, 10.0 * s, if gilded { GILD } else { color::ACCENT }, 1.0);
            ui.label(t, br[0] + 12.0 * s, br[1] + (br[3] - ts * 1.22) / 2.0, ts, true, color::ACCENT_TEXT);
        } else if locked_ {
            locked(ui, [r[0] + r[2] / 2.0 - 14.0 * s, sy - 6.0 * s, 28.0 * s, 18.0 * s], s * 0.75);
        }
    }

    /// The Wayfarer's Caches: one fills every `bonus_xp` past the last tier.
    fn draw_caches(&mut self, ui: &mut Ui, d: &PassData, r: [f32; 4], s: f32, mouse: (f64, f64), pulse: f32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let ready = caches_ready(d);
        theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (if ready > 0 { GILD } else { color::BORDER }, 1.0), ((color::WARM_TOP, 0.9), (color::WARM_BOTTOM, 0.9)));
        let icon = [r[0] + 6.0 * s, r[1] + 5.0 * s, r[3] - 10.0 * s, r[3] - 10.0 * s];
        chest(ui.renderer, icon, ready > 0, pulse, s);
        if inside(icon, mouse) {
            self.hover = Some(Hover::Cache);
        }
        let tx = icon[0] + icon[2] + 10.0 * s;
        ui.label(tr("Wayfarer's Cache"), tx, r[1] + 5.0 * s, font(14.0), true, GILD);
        let sub = if ready > 0 {
            tn!(ready, "{n} cache ready to open|{n} caches ready to open")
        } else if tier_of(d) < d.tiers.len() {
            tf!("After tier {n}, every {xp} pass XP fills one.", n = d.tiers.len(), xp = group_digits(d.bonus_xp))
        } else {
            let (into, size) = progress(d);
            tf!("{into} / {size} XP to the next", into = group_digits(into), size = group_digits(size))
        };
        ui.label_fit(&sub, tx, r[1] + 25.0 * s, r[2] - (tx - r[0]) - 130.0 * s, font(11.5), false, color::TEXT_DIM);
        if ready > 0 {
            let bb = [r[0] + r[2] - 118.0 * s, r[1] + 7.0 * s, 110.0 * s, r[3] - 14.0 * s];
            glow(ui, bb, pulse, s);
            ui.button(bb, tr("Open"), Kind::Primary, inside(bb, mouse), s);
            self.hits.push((Hit::Caches, bb));
        } else {
            // what may be in it
            let mut x = r[0] + r[2] - 10.0 * s;
            for rw in d.cache.iter().take(4) {
                let side = 28.0 * s;
                x -= side + 4.0 * s;
                let tr_ = [x, r[1] + (r[3] - side) / 2.0, side, side];
                draw_reward(ui, rw, tr_, s * 0.6, self.now, 0.8);
                if inside(tr_, mouse) {
                    self.hover = Some(Hover::Reward(rw.clone()));
                }
            }
        }
    }

    /// How pass XP is earned, and what changes it.
    fn draw_earn(&mut self, ui: &mut Ui, d: &PassData, body: [f32; 4], s: f32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let lw = body[2] * 0.56;
        ui.label(tr("What earns pass XP"), body[0], body[1], font(16.0), true, color::GOLD);
        let row = 30.0 * s;
        let cols = 2;
        let cw = (lw - 10.0 * s) / cols as f32;
        let accent = rgb(d.accent, color::ACCENT);
        for (i, src) in d.sources.iter().enumerate() {
            let (c, k) = (i % cols, i / cols);
            let rr = [body[0] + c as f32 * (cw + 10.0 * s), body[1] + 30.0 * s + k as f32 * (row + 4.0 * s), cw, row];
            theme::fill(ui.renderer, rr, 6.0 * s, color::CHIP, 0.9);
            source_icon(ui, src.id, rr[0] + 16.0 * s, rr[1] + row / 2.0, s);
            let name = SOURCES.get(src.id as usize).map_or("", |n| tr(n));
            let amount = match src.id {
                SOURCE_FAME => tf!("1 per {n} fame", n = src.amount),
                SOURCE_DUNGEON => tf!("+{n} and up", n = src.amount),
                _ => format!("+{}", group_digits(src.amount)),
            };
            let aw = ui.measure(&amount, font(12.0), true);
            ui.label(&amount, rr[0] + rr[2] - aw - 10.0 * s, rr[1] + (row - font(12.0) * 1.22) / 2.0, font(12.0), true, accent);
            ui.label_fit(name, rr[0] + 32.0 * s, rr[1] + (row - font(12.0) * 1.22) / 2.0, rr[2] - aw - 50.0 * s, font(12.0), false, color::TEXT);
        }
        // the right column: today's dungeon, Momentum, the community goals, the next season
        let rx = body[0] + lw + 20.0 * s;
        let rw = body[0] + body[2] - rx;
        let mut y = body[1];
        let boxed = |ui: &mut Ui, y: f32, h: f32, edge: u32| {
            theme::frame(ui.renderer, [rx, y, rw, h], 8.0 * s, s.max(1.0), (edge, 1.0), ((color::WARM_TOP, 0.9), (color::WARM_BOTTOM, 0.9)));
        };
        let first = d.sources.iter().find(|x| x.id == SOURCE_FIRST_DUNGEON).map_or(0, |x| x.amount);
        // a box: its icon's place, title and text, as many lines as the text needs (two at most)
        let text_lines = |ui: &mut Ui, t: &str| crate::npc::wrap(ui, t, font(12.0), rw - 56.0 * s).into_iter().take(2).collect::<Vec<_>>();
        let draw_lines = |ui: &mut Ui, lines: &[String], y: f32, c: u32| {
            for (i, l) in lines.iter().enumerate() {
                ui.label(l, rx + 46.0 * s, y + 32.0 * s + i as f32 * font(12.0) * 1.25, font(12.0), false, c);
            }
        };
        let t = if d.first_dungeon_ready { tf!("Clear any dungeon today for +{n} pass XP.", n = first) } else { tr("Done today: back at midnight (UTC).").to_owned() };
        let lines = text_lines(ui, &t);
        let h = 46.0 * s + lines.len() as f32 * font(12.0) * 1.25;
        boxed(ui, y, h, if d.first_dungeon_ready { accent } else { color::BORDER });
        ui_icon(ui, crate::hud::ui_icon::DUNGEON, rx + 24.0 * s, y + h / 2.0, 26.0 * s);
        ui.label(tr("First dungeon of the day"), rx + 46.0 * s, y + 10.0 * s, font(14.0), true, color::TEXT);
        draw_lines(ui, &lines, y, if d.first_dungeon_ready { color::GOOD } else { color::TEXT_DIM });
        y += h + 10.0 * s;
        boxed(ui, y, 78.0 * s, if d.momentum > 0 { color::GOOD } else { color::BORDER });
        chevrons(ui.renderer, rx + 24.0 * s, y + 39.0 * s, 7.0 * s, if d.momentum > 0 { color::GOOD } else { color::TEXT_MUTED }, self.now);
        ui.label(tr("Momentum"), rx + 46.0 * s, y + 10.0 * s, font(14.0), true, if d.momentum > 0 { color::GOOD } else { color::TEXT });
        let t = if d.momentum > 0 {
            tf!("You're behind the pace (tier {pace} by now), so every gain is {n}% bigger until you catch up.", pace = d.pace_tier, n = d.momentum)
        } else {
            tr("Fall a tier behind the season's pace and pass XP comes faster until you catch up. No need to play every day.").to_owned()
        };
        let lines = crate::npc::wrap(ui, &t, font(12.0), rw - 56.0 * s);
        for (i, l) in lines.iter().take(3).enumerate() {
            ui.label(l, rx + 46.0 * s, y + 32.0 * s + i as f32 * font(12.0) * 1.25, font(12.0), false, color::TEXT_DIM);
        }
        y += 88.0 * s;
        if d.community_luck > 0 || d.community_rolls > 0 {
            let t = if d.gilded { tr("Gilded: each stage's loot rolls luckier, with an extra roll.") } else { tr("A Gilded pass makes each stage's loot luckier, with an extra roll.") };
            let lines = text_lines(ui, t);
            let h = 46.0 * s + lines.len() as f32 * font(12.0) * 1.25;
            boxed(ui, y, h, if d.gilded { GILD } else { color::BORDER });
            theme::star(ui.renderer, rx + 24.0 * s, y + h / 2.0, 11.0 * s, if d.gilded { GILD } else { color::TEXT_MUTED });
            ui.label(tr("Community goals"), rx + 46.0 * s, y + 10.0 * s, font(14.0), true, if d.gilded { GILD } else { color::TEXT });
            draw_lines(ui, &lines, y, color::TEXT_DIM);
            y += h + 10.0 * s;
        }
        if !d.next_name.is_empty() {
            let t = tf!("Next season: {name}, in {time}", name = d.next_name, time = countdown(d.next_starts_at as i64 - self.server_now()));
            ui.label_fit(&t, rx, y + 4.0 * s, rw, font(12.0), true, color::TEXT_MUTED);
            y += 24.0 * s;
        }
        let rules = tr("Rewards you haven't claimed when the season ends are paid to you then. A Gilded pass is for one season.");
        let lines = crate::npc::wrap(ui, rules, font(11.5), rw);
        for (i, l) in lines.iter().take(3).enumerate() {
            ui.label(l, rx, y + 6.0 * s + i as f32 * font(11.5) * 1.25, font(11.5), false, color::TEXT_MUTED);
        }
    }

    /// What the Gilded pass does while it's held, beside its track's rewards: a card each, lit
    /// once the pass is Gilded.
    fn draw_perks(&mut self, ui: &mut Ui, d: &PassData, body: [f32; 4], s: f32, now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label(tr("What the Gilded pass gives"), body[0], body[1], font(16.0), true, color::GOLD);
        let sub = if d.gilded {
            tf!("All yours until {season} ends.", season = d.season_name)
        } else {
            tf!("Gild your pass and all of this is yours until {season} ends.", season = d.season_name)
        };
        ui.label_fit(&sub, body[0], body[1] + 24.0 * s, body[2], font(12.0), false, color::TEXT_MUTED);

        // (icon, title, text): the trips, the Arena, the community goals, the chat title, the track
        let trips = tf!("/visit and /summon take you into each dungeon {gilded} times instead of {plain}.", gilded = GILDED_TRIPS, plain = TRIPS);
        let mut perks: Vec<(u8, &str, String)> = vec![
            (0, tr("Twice the trips"), trips),
            (1, tr("A second chance in the Arena"), tr("Once a game, a defeat in the Arena puts you back on your feet instead of sending you out.").to_owned()),
        ];
        if d.community_luck > 0 || d.community_rolls > 0 {
            perks.push((2, tr("Luckier community goal loot"), tr("A Gilded pass makes each stage's loot luckier, with an extra roll.").to_owned()));
        }
        perks.push((3, tr("A title in chat"), tr("Your name in chat wears a gold [Gilded] title with a glint of its own.").to_owned()));
        perks.push((4, tr("The Gilded track"), tr("Every Gilded reward you've passed is waiting on the track.").to_owned()));

        let cols = if body[2] > 640.0 * s { 2 } else { 1 };
        let gap = 10.0 * s;
        let cw = (body[2] - gap * (cols - 1) as f32) / cols as f32;
        let ch = 74.0 * s;
        let top = body[1] + 52.0 * s;
        let lit = d.gilded;
        let edge = if lit { GILD } else { color::BORDER };
        for (i, (icon, title, text)) in perks.iter().enumerate() {
            let r = [body[0] + (i % cols) as f32 * (cw + gap), top + (i / cols) as f32 * (ch + gap), cw, ch];
            if r[1] + r[3] > body[1] + body[3] {
                break;
            }
            let (t, b) = if lit { (0x2C2012, 0x17100A) } else { (color::WARM_TOP, color::WARM_BOTTOM) };
            theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (edge, 1.0), ((t, 0.92), (b, 0.92)));
            let (ix, iy) = (r[0] + 28.0 * s, r[1] + ch / 2.0);
            if lit {
                // a slow breath of light behind each lit perk's icon, the cards out of step
                let breath = 0.12 + 0.06 * ((now as f32 / 700.0) + i as f32 * 1.3).sin();
                glow_disc(ui.renderer, ix, iy, 24.0 * s, GILD, breath);
            }
            let ic = if lit { GILD } else { color::TEXT_MUTED };
            match icon {
                0 => ui_icon(ui, crate::hud::ui_icon::NEXUS, ix, iy, 26.0 * s),
                1 => ui_icon(ui, crate::hud::ui_icon::SWORDS, ix, iy, 26.0 * s),
                2 => theme::star(ui.renderer, ix, iy, 11.0 * s, ic),
                3 => ui_icon(ui, crate::hud::ui_icon::BANNER, ix, iy, 26.0 * s),
                _ => ui_icon(ui, crate::hud::ui_icon::CROWN, ix, iy, 26.0 * s),
            }
            // the state, top right: on, or what turns it on
            let (state, sc) = if lit { (tr("Active"), color::GOOD) } else { (tr("Gilded only"), color::TEXT_MUTED) };
            let sw = ui.measure(state, font(11.0), true);
            ui.label(state, r[0] + r[2] - sw - 12.0 * s, r[1] + 10.0 * s, font(11.0), true, sc);
            let tx = r[0] + 54.0 * s;
            ui.label_fit(title, tx, r[1] + 10.0 * s, r[2] - (tx - r[0]) - sw - 24.0 * s, font(14.0), true, if lit { GILD_LIGHT } else { color::TEXT });
            let lines = crate::npc::wrap(ui, text, font(12.0), r[2] - (tx - r[0]) - 12.0 * s);
            for (k, l) in lines.iter().take(2).enumerate() {
                ui.label(l, tx, r[1] + 32.0 * s + k as f32 * font(12.0) * 1.25, font(12.0), false, color::TEXT_DIM);
            }
        }
    }

    /// The auras and name colours: each shown live, worn with a click.
    #[allow(clippy::too_many_arguments)]
    fn draw_looks(&mut self, ui: &mut Ui, d: &PassData, me: Option<&GameObj>, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        ui.label(tr("Auras"), body[0], body[1], font(16.0), true, color::GOLD);
        ui.label_fit(tr("Worn on every character, seen by everyone. Earn them on the Realm Pass."), body[0] + 70.0 * s, body[1] + 3.0 * s, body[2] - 70.0 * s, font(12.0), false, color::TEXT_MUTED);
        let figure = me.map(|m| m.stat_int(76).filter(|&t| t > 0).map_or(m.object_type, |t| t as u16));
        let n = d.auras.len() + 1;
        let gap = 10.0 * s;
        let cw = ((body[2] - gap * (n - 1) as f32) / n as f32).min(130.0 * s);
        let ch = 150.0 * s;
        let y = body[1] + 28.0 * s;
        let empty = auras::Trails::default();
        for i in 0..n {
            let (id, name, owned) = if i == 0 { (0u8, tr("None").to_owned(), true) } else { let a = &d.auras[i - 1]; (a.id, a.name.clone(), a.owned) };
            let cr = [body[0] + i as f32 * (cw + gap), y, cw, ch];
            let on = id == d.aura;
            let hover = inside(cr, mouse) && owned;
            let edge = if on { GILD } else if hover { color::BORDER_HI } else { color::BORDER };
            theme::frame(ui.renderer, cr, 8.0 * s, s.max(1.0), (edge, 1.0), ((0x141026, 1.0), (0x0A0814, 1.0)));
            // the aura round a little you
            let (fx, fy) = (cr[0] + cw / 2.0, cr[1] + 96.0 * s);
            let w = auras::Wearer { id: -1, x: fx, y: fy, tile: 52.0 * s, size: 1.0, aura: id, skin: None, alpha: if owned { 1.0 } else { 0.35 } };
            auras::ground(ui.renderer, &w, &empty, &|_, _| [fx, fy], now);
            if let Some(t) = figure {
                object_icon(ui, t, fx, fy - 20.0 * s, 40.0 * s);
            }
            auras::air(ui.renderer, &w, now);
            let ls = font(12.0);
            let lw = ui.measure(&name, ls, true).min(cw - 10.0 * s);
            ui.label_fit(&name, cr[0] + (cw - lw) / 2.0, cr[1] + ch - 26.0 * s, cw - 10.0 * s, ls, true, if on { GILD } else if owned { color::TEXT } else { color::TEXT_MUTED });
            if !owned {
                locked(ui, [cr[0] + cw - 30.0 * s, cr[1] + 6.0 * s, 24.0 * s, 18.0 * s], s * 0.7);
            } else {
                self.hits.push((Hit::Aura(id), cr));
            }
        }
        // name colours
        let y = y + ch + 22.0 * s;
        ui.label(tr("Name colours"), body[0], y, font(16.0), true, color::GOLD);
        ui.label_fit(tr("Your name in chat. One from the Realm Pass shines over your achievement rank's."), body[0] + 130.0 * s, y + 3.0 * s, body[2] - 130.0 * s, font(12.0), false, color::TEXT_MUTED);
        let y = y + 30.0 * s;
        let name = me.and_then(|m| m.name()).unwrap_or("Wayfarer").to_owned();
        let n = d.colors.len() + 1;
        let cw = ((body[2] - gap * (n - 1) as f32) / n as f32).min(180.0 * s);
        for i in 0..n {
            let (id, cname, owned, c) = if i == 0 { (0u8, tr("Achievement colour").to_owned(), true, color::TEXT) } else { let x = &d.colors[i - 1]; (x.id, x.name.clone(), x.owned, rgb(x.color, color::TEXT)) };
            let cr = [body[0] + i as f32 * (cw + gap), y, cw, 58.0 * s];
            let on = id == d.color;
            let hover = inside(cr, mouse) && owned;
            let edge = if on { GILD } else if hover { color::BORDER_HI } else { color::BORDER };
            theme::frame(ui.renderer, cr, 8.0 * s, s.max(1.0), (edge, 1.0), ((color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0)));
            let ns = font(15.0);
            let alpha = if owned { 1.0 } else { 0.35 };
            ui.label_alpha(&name, cr[0] + 12.0 * s, cr[1] + 8.0 * s, ns, true, c, alpha);
            ui.label_fit(&cname, cr[0] + 12.0 * s, cr[1] + 34.0 * s, cw - 40.0 * s, font(11.0), false, if owned { color::TEXT_DIM } else { color::TEXT_MUTED });
            if !owned {
                locked(ui, [cr[0] + cw - 30.0 * s, cr[1] + 6.0 * s, 24.0 * s, 18.0 * s], s * 0.7);
            } else {
                self.hits.push((Hit::Color(id), cr));
            }
        }
    }

    /// Gilding the pass: the price, what it gives, and Yes.
    #[allow(clippy::too_many_arguments)]
    fn draw_confirm(&mut self, ui: &mut Ui, d: &PassData, me: Option<&GameObj>, r: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        theme::fill(ui.renderer, r, 12.0 * s, 0x000000, 0.6);
        let (w, h) = (500.0 * s, 270.0 * s);
        let b = [r[0] + (r[2] - w) / 2.0, r[1] + (r[3] - h) / 2.0, w, h];
        theme::shadow(ui.renderer, b, 10.0 * s, 16.0 * s, 0.6);
        theme::frame(ui.renderer, b, 10.0 * s, (1.5 * s).max(1.0), (GILD, 1.0), ((0x2C2012, 1.0), (0x140E08, 1.0)));
        glow_disc(ui.renderer, b[0] + w / 2.0, b[1] + 46.0 * s, 70.0 * s, GILD, 0.25 + 0.08 * (now as f32 / 300.0).sin());
        ui_icon(ui, crate::hud::ui_icon::CROWN, b[0] + w / 2.0, b[1] + 44.0 * s, 40.0 * s);
        let title = tf!("Gild the {season} pass?", season = d.season_name);
        let tw = ui.measure(&title, font(20.0), true);
        ui.label(&title, b[0] + (w - tw) / 2.0, b[1] + 74.0 * s, font(20.0), true, GILD);
        let gilded_n: usize = d.tiers.iter().filter(|t| !t.gilded.is_empty()).count();
        let passed = d.tiers.iter().take(tier_of(d)).filter(|t| !t.gilded.is_empty()).count();
        let body = if passed > 0 {
            tn!(passed, "{n} Gilded reward you've already passed is yours at once, and {all} more wait along the road.|{n} Gilded rewards you've already passed are yours at once, and {all} more wait along the road.", all = gilded_n - passed)
        } else {
            tf!("{all} Gilded rewards wait along the road: skins, an aura, gold and more.", all = gilded_n)
        };
        let lines = crate::npc::wrap(ui, &body, font(13.0), w - 60.0 * s);
        for (i, l) in lines.iter().take(3).enumerate() {
            let lw = ui.measure(l, font(13.0), false);
            ui.label(l, b[0] + (w - lw) / 2.0, b[1] + 108.0 * s + i as f32 * font(13.0) * 1.3, font(13.0), false, color::TEXT);
        }
        let gold = me.and_then(|m| m.stat_int(35)).unwrap_or(d.gold);
        let enough = gold >= d.price;
        let t = if enough { tf!("You have {gold} gold.", gold = group_digits(gold)) } else { tf!("You have {gold} gold: {more} more needed.", gold = group_digits(gold), more = group_digits(d.price - gold)) };
        let gw = ui.measure(&t, font(12.0), true);
        ui.label(&t, b[0] + (w - gw) / 2.0, b[1] + 168.0 * s, font(12.0), true, if enough { color::TEXT_DIM } else { color::DANGER });
        let (bw, bh) = (180.0 * s, 40.0 * s);
        let yes = [b[0] + w / 2.0 - bw - 8.0 * s, b[1] + h - bh - 22.0 * s, bw, bh];
        let no = [b[0] + w / 2.0 + 8.0 * s, yes[1], 140.0 * s, bh];
        if enough {
            let hover = inside(yes, mouse);
            theme::rounded(ui.renderer, yes, 7.0 * s, (if hover { 0xFFF6D0 } else { GILD_LIGHT }, 1.0), (if hover { 0xFFC93A } else { 0xE8A820 }, 1.0));
            ui.tile_icon("lofiObj3", 225, 40, yes[0] + 22.0 * s, yes[1] + bh / 2.0, s);
            let l = tf!("Gild for {price}", price = group_digits(d.price));
            let ls = ui.fit_size(&l, bw - 50.0 * s, font(14.0), true);
            ui.label(&l, yes[0] + 40.0 * s, yes[1] + (bh - ls * 1.22) / 2.0, ls, true, 0x3A2600);
            self.hits.push((Hit::Confirm, yes));
        } else {
            ui.button(yes, tr("Not enough gold"), Kind::Secondary, false, s);
        }
        ui.button(no, tr("Cancel"), Kind::Secondary, inside(no, mouse), s);
        self.hits.push((Hit::Cancel, no));
        self.hits.push((Hit::Cancel, r));
    }

    /// What was claimed: each reward bursting in over rays of light.
    #[allow(clippy::too_many_arguments)]
    fn draw_reveal(&mut self, ui: &mut Ui, d: &PassData, r: [f32; 4], title: &str, rewards: &[PassReward], age: i32, s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let fade = (age as f32 / 250.0).min(1.0);
        theme::fill(ui.renderer, r, 12.0 * s, 0x000000, 0.72 * fade);
        let accent = if rewards.is_empty() || d.gilded && rewards.iter().any(|x| x.kind == kind::SKIN) { GILD } else { rgb(d.accent, color::ACCENT) };
        let (cx, cy) = (r[0] + r[2] / 2.0, r[1] + r[3] / 2.0 + 10.0 * s);
        // the rays turning behind
        let spin = now as f32 * 0.0004;
        let white = ui.renderer.white();
        for i in 0..14 {
            let a = spin + i as f32 * TAU / 14.0;
            let (len, half) = (260.0 * s * fade, 0.09);
            let p0 = [cx, cy];
            let p1 = [cx + len * (a - half).cos(), cy + len * (a - half).sin()];
            let p2 = [cx + len * (a + half).cos(), cy + len * (a + half).sin()];
            let c0 = Tint::color(accent, 0.22 * fade).mul;
            let c1 = Tint::color(accent, 0.0).mul;
            ui.renderer.triangle_colors(white, [p0, p1, p2], [[0.5, 0.5]; 3], [c0, c1, c1]);
        }
        glow_disc(ui.renderer, cx, cy, 120.0 * s * fade, accent, 0.35 * fade);
        let tw = ui.measure(title, font(26.0), true);
        ui.label_alpha(title, cx - tw / 2.0, r[1] + 70.0 * s, font(26.0), true, accent, fade);
        if rewards.is_empty() {
            // Gilded: the crown
            let k = ease_back((age as f32 / 500.0).min(1.0));
            ui_icon(ui, crate::hud::ui_icon::CROWN, cx, cy, 90.0 * s * k);
            let t = tr("Every Gilded reward you've passed is waiting on the track.");
            let w = ui.measure(t, font(14.0), false);
            ui.label_alpha(t, cx - w / 2.0, cy + 70.0 * s, font(14.0), false, color::TEXT, fade);
        } else {
            // up to seven a row, two rows at most
            let n = rewards.len().min(14);
            let per = n.min(7);
            let rows = n.div_ceil(per.max(1));
            let tile = if rows > 1 { 58.0 } else { 76.0 } * s;
            let gap = 18.0 * s;
            let row_h = tile + 40.0 * s;
            let step = if rows > 1 { 110 } else { 160 };
            for (i, rw) in rewards.iter().take(n).enumerate() {
                let (row, col) = (i / per, i % per);
                let in_row = if row + 1 < rows { per } else { n - row * per };
                let total = in_row as f32 * (tile + gap) - gap;
                let start = 200 + i as i32 * step;
                let k = ((age - start) as f32 / 420.0).clamp(0.0, 1.0);
                if k <= 0.0 {
                    continue;
                }
                let grow = ease_back(k);
                let x = cx - total / 2.0 + col as f32 * (tile + gap) + tile / 2.0;
                let cy = cy - (rows as f32 - 1.0) * row_h / 2.0 + row as f32 * row_h;
                let side = tile * grow;
                // a flash as it lands
                if k < 1.0 {
                    glow_disc(ui.renderer, x, cy, tile * (1.6 - k * 0.6), 0xFFFFFF, 0.5 * (1.0 - k));
                }
                let tr_ = [x - side / 2.0, cy - side / 2.0, side, side];
                draw_reward(ui, rw, tr_, s * grow, now, k);
                if inside(tr_, mouse) {
                    self.hover = Some(Hover::Reward(rw.clone()));
                }
                let c = caption(ui.data, rw);
                let cs = font(12.0);
                let fit = crate::npc::fit(ui, &c, cs, tile + gap - 4.0 * s);
                let w = ui.measure(&fit, cs, true);
                ui.label_alpha(&fit, x - w / 2.0, cy + tile / 2.0 + 10.0 * s, cs, true, color::TEXT, k);
            }
        }
        let hint = tr("Click to carry on");
        let hw = ui.measure(hint, font(12.0), false);
        ui.label_alpha(hint, cx - hw / 2.0, r[1] + r[3] - 70.0 * s, font(12.0), false, color::TEXT_MUTED, fade * (0.6 + 0.4 * (now as f32 / 400.0).sin()));
    }

    /// The tooltip of what the mouse is over.
    fn draw_hover(&mut self, ui: &mut Ui, lay: &Layout, d: &PassData, me: Option<&GameObj>, mouse: (f64, f64)) {
        let Some(h) = self.hover.clone() else { return };
        let lines: Vec<Line> = match h {
            Hover::Reward(r) if r.kind == kind::ITEM => {
                let mut lines = crate::hud::item_tooltip(ui.data, r.item as i32, me, SlotKind::Shop);
                if r.amount > 1 {
                    lines.push(Line::new(&tf!("x{n}", n = r.amount), 12.0, true, color::GOLD));
                }
                crate::hud::draw_tooltip(ui, lay, &lines, Some(r.item as i32), crate::screens::tier_edge(ui.data, r.item as i32), mouse);
                return;
            }
            Hover::Reward(r) => {
                let (title, c) = (caption(ui.data, &r), reward_color(&r));
                let text = match r.kind {
                    kind::FAME => tr("Fame to your account."),
                    kind::GOLD => tr("Gold to your account."),
                    kind::SKIN => {
                        let class = ui.data.objects.get(&(r.amount as u16)).map_or(String::new(), |c| c.display_id.clone());
                        return crate::hud::draw_tooltip(
                            ui,
                            lay,
                            &[
                                Line::new(&r.name, 15.0, true, c),
                                Line::new(&tf!("A skin for the {class}", class), 12.0, true, color::TEXT_DIM),
                                Line::gap(),
                                Line::new(tr("Hand-drawn for this season, with its own effects as you walk and fight. Wear it at the Armoire in the Nexus, or pick it for a new character. Yours for good."), 12.0, false, color::TEXT),
                            ],
                            None,
                            Some(c),
                            mouse,
                        );
                    }
                    kind::AURA => tr("An aura round your character, on every character, for everyone to see. Wear it from the Looks tab. Yours for good."),
                    kind::COLOR => tr("A colour for your name in chat. Wear it from the Looks tab. Yours for good."),
                    kind::SLOT => tr("Room for another character."),
                    _ => "",
                };
                vec![Line::new(&title, 15.0, true, c), Line::gap(), Line::new(text, 12.0, false, color::TEXT)]
            }
            Hover::Cache => {
                let mut v = vec![
                    Line::new(tr("Wayfarer's Cache"), 15.0, true, GILD),
                    Line::gap(),
                    Line::new(&tf!("Past the last tier, every {xp} pass XP fills a cache. Each holds one of:", xp = group_digits(d.bonus_xp)), 12.0, false, color::TEXT),
                ];
                for r in d.cache.iter() {
                    let name = caption(ui.data, r);
                    v.push(Line::new(&format!("·  {name}"), 12.0, false, color::TEXT_DIM));
                }
                v
            }
        };
        crate::hud::draw_tooltip(ui, lay, &lines, None, Some(GILD), mouse);
    }

    /// The XP pills rising beside the HUD's pass button (`button`), and the banner of a tier
    /// reached at the top middle of the view (under what's at `below`).
    pub fn draw_toasts(&mut self, ui: &mut Ui, lay: &Layout, button: Option<[f32; 4]>, below: f32, now: i32) {
        self.pills.retain(|p| now - p.since < PILL_MS);
        let s = lay.s;
        let accent = self.data.as_ref().map_or(color::ACCENT, |d| rgb(d.accent, color::ACCENT));
        if let Some(b) = button {
            let pills: Vec<(i32, u8, bool, i32)> = self.pills.iter().map(|p| (p.gained, p.source, p.momentum, p.since)).collect();
            let n = pills.len();
            for (i, (gained, source, momentum, since)) in pills.into_iter().enumerate() {
                let age = (now - since) as f32 / PILL_MS as f32;
                let fade = (age * 8.0).min(1.0).min((1.0 - age) * 4.0).clamp(0.0, 1.0);
                let size = (13.0 * s).round().max(9.0);
                let t = tf!("+{n} pass XP", n = group_digits(gained));
                let src = SOURCES.get(source as usize).map_or("", |x| tr(x));
                let ss = (11.0 * s).round().max(8.0);
                let w = ui.measure(&t, size, true).max(ui.measure(src, ss, false)) + 22.0 * s + if momentum { 18.0 * s } else { 0.0 };
                let h = 38.0 * s;
                let rise = age * 30.0 * s;
                let y = b[1] - 8.0 * s - (n - 1 - i) as f32 * (h + 4.0 * s) - h - rise;
                let r = [lay.view_w() - w - 12.0 * s, y, w, h];
                theme::shadow(ui.renderer, r, 8.0 * s, 6.0 * s, 0.4 * fade);
                theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (accent, fade), ((color::PANEL_TOP, 0.95 * fade), (color::PANEL_BOTTOM, 0.95 * fade)));
                ui.label_alpha(&t, r[0] + 11.0 * s, r[1] + 4.0 * s, size, true, mix(accent, 0xFFFFFF, 0.3), fade);
                ui.label_alpha(src, r[0] + 11.0 * s, r[1] + 21.0 * s, ss, false, color::TEXT_DIM, fade);
                if momentum {
                    chevrons(ui.renderer, r[0] + r[2] - 14.0 * s, r[1] + 12.0 * s, 4.0 * s, color::GOOD, now);
                }
            }
        }
        // the tier banner
        let Some((tier, since)) = self.banner else { return };
        let age = now - since;
        if age > BANNER_MS || self.open {
            self.banner = None;
            return;
        }
        let Some(d) = &self.data else { return };
        let fade = (age as f32 / 300.0).min((BANNER_MS - age) as f32 / 400.0).clamp(0.0, 1.0);
        let (w, h) = (440.0 * s, 74.0 * s);
        let r = [((lay.view_w() - w) / 2.0).round(), (below + (1.0 - fade) * -12.0 * s).round(), w, h];
        let dark = rgb(d.dark, 0x120C20);
        theme::shadow(ui.renderer, r, 10.0 * s, 12.0 * s, 0.5 * fade);
        theme::rounded(ui.renderer, r, 10.0 * s, (mix(dark, 0x000000, 0.2), 0.97 * fade), (mix(dark, accent, 0.4), 0.97 * fade));
        theme::split_ring(ui.renderer, r, 10.0 * s, (1.5 * s).max(1.0), &[(accent, fade)]);
        // a shine across it as it comes
        let k = (age as f32 / 900.0).min(1.0);
        if k < 1.0 {
            glow_disc(ui.renderer, r[0] + k * w, r[1] + h / 2.0, 50.0 * s, 0xFFFFFF, 0.35 * (1.0 - k) * fade);
        }
        let (cx, cy) = (r[0] + 44.0 * s, r[1] + h / 2.0);
        glow_disc(ui.renderer, cx, cy, 34.0 * s, accent, 0.4 * fade);
        disc(ui.renderer, cx, cy, 24.0 * s, mix(dark, 0x000000, 0.3), fade);
        ring(ui.renderer, cx, cy, 24.0 * s, 3.0 * s, accent, fade);
        let n = tier.to_string();
        let ns = (24.0 * s).round();
        let nw = ui.measure(&n, ns, true);
        ui.label_alpha(&n, cx - nw / 2.0, (cy - ui.digit_middle(ns, true)).round(), ns, true, 0xFFFFFF, fade);
        let title = tf!("Tier {n} reached!", n = tier);
        ui.label_alpha(&title, r[0] + 80.0 * s, r[1] + 12.0 * s, (19.0 * s).round(), true, 0xFFFFFF, fade);
        let has = d.tiers.get(tier - 1).is_some_and(|t| !t.free.is_empty() || (d.gilded && !t.gilded.is_empty()));
        let sub = if has { tf!("{season}: a reward waits. Press {key} to claim it.", season = d.season_name, key = self.key) } else { d.season_name.clone() };
        ui.label_alpha(&sub, r[0] + 80.0 * s, r[1] + 42.0 * s, (12.0 * s).round(), false, color::TEXT_DIM, fade);
    }
}

/// The HUD button's face: the tier in a ring that fills towards the next, gold once Gilded.
pub fn hud_crest(ui: &mut Ui, r: [f32; 4], p: &HudPass, s: f32) {
    let rad = r[3] / 2.0 - 3.0 * s;
    let (cx, cy) = (r[0] + 6.0 * s + rad, r[1] + r[3] / 2.0);
    let c = if p.gilded { GILD } else { p.accent };
    disc(ui.renderer, cx, cy, rad, 0x000000, 0.35);
    ring(ui.renderer, cx, cy, rad, (2.5 * s).max(1.5), 0x000000, 0.4);
    if p.frac > 0.0 {
        arc(ui.renderer, cx, cy, rad - (2.5 * s).max(1.5), rad, -PI / 2.0, -PI / 2.0 + TAU * p.frac, c, 1.0);
    }
    theme::star(ui.renderer, cx, cy + 0.6 * s, rad * 0.55, c);
    let t = p.tier.to_string();
    let ts = (14.0 * s).round().max(9.0);
    let tw = ui.measure(&t, ts, true);
    let x = cx + rad + ((r[0] + r[2]) - (cx + rad) - tw) / 2.0;
    ui.label(&t, x, (cy - ui.digit_middle(ts, true)).round(), ts, true, if p.gilded { GILD_LIGHT } else { color::TEXT });
}

/// A reward's name: "300 fame", "100 gold", an item's name, a skin's, an aura's.
fn caption(data: &bsk_data::GameData, r: &PassReward) -> String {
    match r.kind {
        kind::FAME => tn!(r.amount, "{n} fame|{n} fame", n = group_digits(r.amount)),
        kind::GOLD => tn!(r.amount, "{n} gold|{n} gold", n = group_digits(r.amount)),
        kind::SLOT => tn!(r.amount, "{n} character slot|{n} character slots"),
        kind::ITEM => {
            let name = data.objects.get(&r.item).map_or(String::new(), |o| if o.display_id.is_empty() { o.id.clone() } else { o.display_id.clone() });
            if r.amount > 1 { format!("{name} x{}", r.amount) } else { name }
        }
        _ => r.name.clone(),
    }
}

fn reward_color(r: &PassReward) -> u32 {
    match r.kind {
        kind::FAME => color::ACCENT,
        kind::GOLD => GILD,
        kind::SKIN => 0x8FD0FF,
        kind::AURA | kind::COLOR => rgb(r.amount, color::TEXT),
        _ => color::TEXT,
    }
}

/// A reward's picture in `r`: an item in its slot, fame or gold as coins, a skin standing in a
/// glow, an aura's orb, a name colour's letters, a slot.
fn draw_reward(ui: &mut Ui, rw: &PassReward, r: [f32; 4], s: f32, now: i32, alpha: f32) {
    let (cx, cy) = (r[0] + r[2] / 2.0, r[1] + r[3] / 2.0);
    let side = r[2];
    match rw.kind {
        kind::ITEM => {
            ui.slot_box(r, crate::screens::tier_edge(ui.data, rw.item as i32), false, (side / 46.0).max(0.4));
            ui.item_icon_faded(rw.item as i32, 60, cx, cy, side / 46.0, alpha);
            if rw.amount > 1 {
                let cs = (side * 0.24).round().max(9.0);
                let t = rw.amount.to_string();
                let tw = ui.measure(&t, cs, true);
                ui.label(&t, r[0] + side - tw - 4.0, r[1] + side - cs * 1.25, cs, true, color::TEXT);
            }
        }
        kind::FAME | kind::GOLD => {
            let c = if rw.kind == kind::GOLD { GILD } else { color::ACCENT };
            glow_disc(ui.renderer, cx, cy, side * 0.55, c, 0.25 * alpha);
            let lift = if side < 40.0 { side * 0.12 } else { 0.0 };
            ui.tile_icon("lofiObj3", if rw.kind == kind::GOLD { 225 } else { 224 }, 60, cx, cy - lift, side / 34.0);
            if side < 40.0 {
                let t = group_digits(rw.amount);
                let cs = (side * 0.3).round().max(9.0);
                let tw = ui.measure(&t, cs, true);
                ui.label_alpha(&t, cx - tw / 2.0, r[1] + side - cs * 1.15, cs, true, c, alpha);
            }
        }
        kind::SKIN => {
            // the season's prize: in a pool of light, sparkling
            let pulse = 0.5 + 0.5 * (now as f32 / 500.0).sin();
            glow_disc(ui.renderer, cx, cy + side * 0.1, side * 0.62, 0x8FD0FF, (0.28 + 0.12 * pulse) * alpha);
            object_icon(ui, rw.item, cx, cy, side * 0.95);
            for i in 0..3u32 {
                let ph = ((now as f32 / 900.0) + i as f32 / 3.0).fract();
                let a = hash(i, (now / 900) as u32) * TAU;
                let (px, py) = (cx + side * 0.38 * a.cos(), cy - side * 0.1 + side * 0.32 * a.sin());
                sparkle(ui.renderer, px, py, side * 0.08 * (ph * PI).sin(), 0xFFFFFF, alpha);
            }
        }
        kind::AURA => {
            let c = rgb(rw.amount, 0xCFEFFF);
            glow_disc(ui.renderer, cx, cy, side * 0.5, c, 0.4 * alpha);
            ring(ui.renderer, cx, cy + side * 0.12, side * 0.3, 2.0f32.max(side * 0.03), c, 0.8 * alpha);
            let spin = now as f32 * 0.002;
            for i in 0..5 {
                let a = spin + i as f32 * TAU / 5.0;
                sparkle(ui.renderer, cx + side * 0.3 * a.cos(), cy + side * 0.12 * a.sin() - side * 0.05, side * 0.07, if i % 2 == 0 { 0xFFFFFF } else { c }, alpha);
            }
            disc(ui.renderer, cx, cy - side * 0.05, side * 0.12, mix(c, 0xFFFFFF, 0.5), alpha);
        }
        kind::COLOR => {
            let c = rgb(rw.amount, color::TEXT);
            theme::fill(ui.renderer, [r[0] + side * 0.1, r[1] + side * 0.2, side * 0.8, side * 0.6], side * 0.12, 0x000000, 0.4 * alpha);
            let t = "Aa";
            let cs = (side * 0.42).round().max(9.0);
            let tw = ui.measure(t, cs, true);
            glow_disc(ui.renderer, cx, cy, side * 0.45, c, 0.25 * alpha);
            ui.label_alpha(t, cx - tw / 2.0, cy - cs * 0.62, cs, true, c, alpha);
        }
        kind::SLOT => {
            theme::frame(ui.renderer, [r[0] + side * 0.12, r[1] + side * 0.12, side * 0.76, side * 0.76], side * 0.12, 1.5, (color::BORDER_HI, alpha), ((color::SLOT_TOP, alpha), (color::SLOT_BOTTOM, alpha)));
            theme::cross(ui.renderer, cx, cy, side * 0.18, (side * 0.07).max(1.5), color::GOOD);
        }
        _ => {}
    }
    let _ = s;
}

/// The icon of what earns XP.
fn source_icon(ui: &mut Ui, id: u8, cx: f32, cy: f32, s: f32) {
    use crate::hud::ui_icon as i;
    let cell = match id {
        0..=5 => i::QUEST,
        6 => i::HOURGLASS,
        7 | 8 => i::DUNGEON,
        9 | 14 => i::SKULL,
        10 => i::CROWN,
        11 => i::TOWER,
        12 => i::SWORDS,
        _ => {
            theme::star(ui.renderer, cx, cy + 1.0 * s, 8.0 * s, color::GOLD);
            return;
        }
    };
    ui_icon(ui, cell, cx, cy, 20.0 * s);
}

/// Overshoots a little and settles: things popping in.
fn ease_back(k: f32) -> f32 {
    let c = 1.7;
    let k = k - 1.0;
    1.0 + (c + 1.0) * k * k * k + c * k * k
}

fn disc(r: &mut Renderer, cx: f32, cy: f32, rad: f32, rgb: u32, a: f32) {
    arc(r, cx, cy, 0.0, rad, 0.0, TAU, rgb, a);
}

fn ring(r: &mut Renderer, cx: f32, cy: f32, rad: f32, w: f32, rgb: u32, a: f32) {
    arc(r, cx, cy, rad - w, rad, 0.0, TAU, rgb, a);
}

/// A band of a circle from `r0` to `r1`, from angle `from` to `to`.
#[allow(clippy::too_many_arguments)]
fn arc(r: &mut Renderer, cx: f32, cy: f32, r0: f32, r1: f32, from: f32, to: f32, rgb: u32, a: f32) {
    if a <= 0.004 || r1 <= 0.0 {
        return;
    }
    let white = r.white();
    let tint = Tint::color(rgb, a.min(1.0));
    let n = (((to - from).abs() / TAU) * 48.0).ceil().max(3.0) as usize;
    let at = |ang: f32, rad: f32| [cx + rad * ang.cos(), cy + rad * ang.sin()];
    for i in 0..n {
        let a0 = from + (to - from) * i as f32 / n as f32;
        let a1 = from + (to - from) * (i + 1) as f32 / n as f32;
        r.triangle(white, [at(a0, r0), at(a0, r1), at(a1, r1)], [[0.5, 0.5]; 3], tint);
        if r0 > 0.0 {
            r.triangle(white, [at(a0, r0), at(a1, r1), at(a1, r0)], [[0.5, 0.5]; 3], tint);
        }
    }
}

/// A soft round glow, brightest in the middle.
fn glow_disc(r: &mut Renderer, cx: f32, cy: f32, rad: f32, rgb: u32, a: f32) {
    if a <= 0.004 || rad <= 0.0 {
        return;
    }
    let white = r.white();
    let mid = Tint::color(rgb, a.min(1.0)).mul;
    let edge = Tint::color(rgb, 0.0).mul;
    const N: usize = 24;
    for i in 0..N {
        let (a0, a1) = (i as f32 * TAU / N as f32, (i + 1) as f32 * TAU / N as f32);
        r.triangle_colors(white, [[cx, cy], [cx + rad * a0.cos(), cy + rad * a0.sin()], [cx + rad * a1.cos(), cy + rad * a1.sin()]], [[0.5, 0.5]; 3], [mid, edge, edge]);
    }
}

/// A four-pointed twinkle.
pub(crate) fn sparkle(r: &mut Renderer, x: f32, y: f32, len: f32, rgb: u32, a: f32) {
    if len < 0.5 || a <= 0.004 {
        return;
    }
    let white = r.white();
    let tint = Tint::color(rgb, a.min(1.0));
    let t = len * 0.22;
    r.triangle(white, [[x - len, y], [x, y - t], [x + len, y]], [[0.5, 0.5]; 3], tint);
    r.triangle(white, [[x - len, y], [x, y + t], [x + len, y]], [[0.5, 0.5]; 3], tint);
    r.triangle(white, [[x, y - len], [x - t, y], [x, y + len]], [[0.5, 0.5]; 3], tint);
    r.triangle(white, [[x, y - len], [x + t, y], [x, y + len]], [[0.5, 0.5]; 3], tint);
}

/// An arrow head pointing left (-1) or right (1).
fn chevron(r: &mut Renderer, cx: f32, cy: f32, half: f32, dir: f32, rgb: u32) {
    let white = r.white();
    let tint = Tint::color(rgb, 1.0);
    let t = half * 0.45;
    let tip = [cx + dir * half * 0.6, cy];
    let (top, bot) = ([cx - dir * half * 0.4, cy - half], [cx - dir * half * 0.4, cy + half]);
    r.triangle(white, [top, tip, [top[0] - dir * t, top[1]]], [[0.5, 0.5]; 3], tint);
    r.triangle(white, [[top[0] - dir * t, top[1]], tip, [tip[0] - dir * t, tip[1]]], [[0.5, 0.5]; 3], tint);
    r.triangle(white, [bot, tip, [bot[0] - dir * t, bot[1]]], [[0.5, 0.5]; 3], tint);
    r.triangle(white, [[bot[0] - dir * t, bot[1]], tip, [tip[0] - dir * t, tip[1]]], [[0.5, 0.5]; 3], tint);
}

/// Momentum's two chevrons pointing up, climbing.
fn chevrons(r: &mut Renderer, cx: f32, cy: f32, half: f32, rgb: u32, now: i32) {
    let white = r.white();
    for k in 0..2 {
        let lift = ((now as f32 / 500.0) + k as f32 * 0.5).fract();
        let y = cy + half * 0.6 - k as f32 * half * 0.9 - lift * half * 0.3;
        let a = 1.0 - lift * 0.5;
        let tint = Tint::color(rgb, a);
        let t = half * 0.35;
        let (l, m, rr) = ([cx - half, y + half * 0.5], [cx, y - half * 0.4], [cx + half, y + half * 0.5]);
        r.triangle(white, [l, m, [m[0], m[1] + t]], [[0.5, 0.5]; 3], tint);
        r.triangle(white, [l, [m[0], m[1] + t], [l[0], l[1] + t]], [[0.5, 0.5]; 3], tint);
        r.triangle(white, [rr, m, [m[0], m[1] + t]], [[0.5, 0.5]; 3], tint);
        r.triangle(white, [rr, [m[0], m[1] + t], [rr[0], rr[1] + t]], [[0.5, 0.5]; 3], tint);
    }
}

/// The Wayfarer's Cache: a small chest, glowing and its lid lifting when one waits.
fn chest(r: &mut Renderer, b: [f32; 4], ready: bool, pulse: f32, s: f32) {
    let (cx, cy) = (b[0] + b[2] / 2.0, b[1] + b[3] / 2.0);
    if ready {
        glow_disc(r, cx, cy, b[2] * 0.8, GILD, 0.3 + 0.2 * pulse);
    }
    let w = b[2] * 0.8;
    let h = b[3] * 0.5;
    let body = [cx - w / 2.0, cy - h * 0.2, w, h * 0.8];
    theme::rounded(r, body, 2.0 * s, (0x8A5A2A, 1.0), (0x5A3A1A, 1.0));
    let lift = if ready { 2.0 * s + pulse * 2.0 * s } else { 0.0 };
    let lid = [cx - w / 2.0 - s, cy - h * 0.55 - lift, w + 2.0 * s, h * 0.38];
    theme::rounded(r, lid, 3.0 * s, (0xA8743A, 1.0), (0x7A4E22, 1.0));
    theme::fill(r, [cx - w / 2.0, body[1] + body[3] * 0.25, w, 2.0 * s], 0.0, GILD, 1.0);
    theme::fill(r, [cx - 2.5 * s, body[1], 5.0 * s, 6.0 * s], 1.0 * s, GILD_LIGHT, 1.0);
    if ready {
        theme::fill(r, [cx - w / 2.0 + 2.0 * s, lid[1] + lid[3], w - 4.0 * s, lift.max(1.0)], 0.0, 0xFFF4C0, 0.9);
    }
}

/// The season's weather in its header: snow and the northern lights for frost, embers for
/// fire, stars and shooting stars otherwise; mountains along the foot.
#[allow(clippy::too_many_arguments)]
fn sky(r: &mut Renderer, h: [f32; 4], theme: &str, accent: u32, light: u32, dark: u32, now: i32, s: f32) {
    let white = r.white();
    let ms = now as f32;
    let (x0, y0, w, ht) = (h[0], h[1], h[2], h[3]);
    match theme {
        "frost" => {
            // the northern lights: curtains rippling across the top
            let n = 40;
            for j in 0..n {
                let k = j as f32 / n as f32;
                let x = x0 + w * (0.25 + 0.7 * k);
                let base = y0 + ht * (0.45 + 0.12 * (k * 7.0 + ms * 0.0007).sin());
                let tall = ht * (0.22 + 0.12 * (k * 11.0 - ms * 0.0011).sin().abs());
                let c = mix(0x6EF0BA, 0x7FA0FF, (k + (ms * 0.00005)).fract());
                let a = 0.42 * (1.0 - (2.0 * k - 1.0).abs().powi(2));
                let (lo, hi) = (Tint::color(c, a).mul, Tint::color(c, 0.0).mul);
                let half = w * 0.6 / n as f32;
                r.triangle_colors(white, [[x - half, base], [x + half, base], [x + half, base - tall]], [[0.5, 0.5]; 3], [lo, lo, hi]);
                r.triangle_colors(white, [[x - half, base], [x + half, base - tall], [x - half, base - tall]], [[0.5, 0.5]; 3], [lo, hi, hi]);
            }
            for i in 0..46u32 {
                let period = 5000 + (hash(i, 1) * 3000.0) as i32;
                let t = ((now + (hash(i, 2) * period as f32) as i32).rem_euclid(period)) as f32 / period as f32;
                let x = x0 + w * hash(i, 3) + 18.0 * s * (t * 6.0 + i as f32).sin() - t * 30.0 * s;
                let y = y0 + ht * t;
                if x > x0 + 4.0 && x < x0 + w - 4.0 {
                    let size = (1.5 + 1.5 * hash(i, 4)) * s;
                    r.fill(x, y, size, size, 0xFFFFFF, 0.6 * (t * PI).sin());
                }
            }
        }
        "ember" => {
            for i in 0..40u32 {
                let period = 2600 + (hash(i, 1) * 2000.0) as i32;
                let t = ((now + (hash(i, 2) * period as f32) as i32).rem_euclid(period)) as f32 / period as f32;
                let x = x0 + w * hash(i, 3) + 10.0 * s * (t * 5.0 + i as f32).sin();
                let y = y0 + ht * (1.0 - t);
                let size = (2.0 + 2.0 * hash(i, 4)) * s * (1.0 - 0.5 * t);
                let c = mix(0xFFE08A, 0xFF5020, t);
                r.fill(x, y, size, size, c, 0.8 * (1.0 - t));
            }
        }
        _ => {
            for i in 0..50u32 {
                let x = x0 + w * hash(i, 3);
                let y = y0 + ht * 0.75 * hash(i, 5);
                let tw = 0.4 + 0.6 * (ms * 0.002 * (0.5 + hash(i, 6)) + i as f32).sin().abs();
                let size = (1.0 + 1.5 * hash(i, 4)) * s;
                r.fill(x, y, size, size, if i % 4 == 0 { light } else { 0xFFFFFF }, 0.8 * tw);
            }
            let t = (now.rem_euclid(4200)) as f32 / 4200.0;
            if t < 0.2 {
                let k = t / 0.2;
                let (sx, sy) = (x0 + w * (0.3 + 0.5 * k), y0 + ht * (0.1 + 0.3 * k));
                for j in 0..8 {
                    let b = j as f32 * 5.0 * s;
                    r.fill(sx - b, sy - b * 0.6, 2.5 * s, 2.5 * s, 0xFFFFFF, (1.0 - j as f32 / 8.0) * (1.0 - k));
                }
            }
        }
    }
    // mountains: a far, pale range and a near, dark one
    for (layer, c, top, seed) in [(0, mix(dark, accent, 0.25), 0.55, 11u32), (1, mix(dark, 0x000000, 0.45), 0.7, 23u32)] {
        let peaks = 9 + layer * 3;
        let step = w / peaks as f32;
        let tint = Tint::color(c, 0.9);
        let foot = y0 + ht - 2.0;
        for p in 0..=peaks {
            let px = x0 + p as f32 * step + (hash(p as u32, seed) - 0.5) * step * 0.6;
            let peak = y0 + ht * (top + 0.25 * hash(p as u32, seed + 1));
            let half = step * (0.7 + 0.4 * hash(p as u32, seed + 2));
            let l = (px - half).max(x0 + 4.0);
            let rr = (px + half).min(x0 + w - 4.0);
            if rr > l {
                r.triangle(white, [[l, foot], [px.clamp(l, rr), peak], [rr, foot]], [[0.5, 0.5]; 3], tint);
                if layer == 0 && theme == "frost" {
                    // snow on the far peaks
                    let cap = (foot - peak) * 0.22;
                    let k = cap / (foot - peak);
                    let pc = px.clamp(l, rr);
                    r.triangle(white, [[pc - (pc - l) * k, peak + cap], [pc, peak], [pc + (rr - pc) * k, peak + cap]], [[0.5, 0.5]; 3], Tint::color(0xEAF7FF, 0.7));
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use bsk_proto::{PassCosmetic, PassSourceInfo, PassTier};

    fn reward(kind: u8, amount: i32) -> PassReward {
        PassReward { kind, item: 0, amount, name: String::new() }
    }

    fn data(xp: i32, gilded: bool) -> PassData {
        let tier = |_| PassTier { free: vec![reward(kind::FAME, 100)].into(), gilded: vec![reward(kind::GOLD, 50)].into(), free_claimed: false, gilded_claimed: false };
        PassData {
            now: 1_800_000_000,
            active: true,
            season_key: "frostfall-2026-11".into(),
            season_name: "Frostfall".into(),
            theme: "frost".into(),
            xp,
            xp_per_tier: 1000,
            bonus_xp: 1500,
            price: 1000,
            gilded,
            tiers: (0..30).map(tier).collect::<Vec<_>>().into(),
            sources: vec![PassSourceInfo { id: 0, amount: 150 }].into(),
            auras: vec![PassCosmetic { id: 1, name: "Snowfall".into(), color: 0xCFEFFF, owned: true }].into(),
            ..Default::default()
        }
    }

    #[test]
    fn tiers_claims_and_caches() {
        let mut p = Pass::default();
        assert_eq!(p.show(None), Some(Act::Request));
        p.event(Event::Data(Box::new(data(2_500, false))), 0);
        let h = p.hud().unwrap();
        assert_eq!((h.tier, h.ready, h.gilded), (2, 2, false));
        assert!((h.frac - 0.5).abs() < 1e-6);
        // Gilded: the Gilded rewards passed wait too
        p.event(Event::Data(Box::new(data(2_500, true))), 10);
        assert_eq!(p.ready(), 4);
        // past the last tier, caches
        let mut d = data(30_000 + 3_100, true);
        for t in d.tiers.iter_mut() {
            t.free_claimed = true;
            t.gilded_claimed = true;
        }
        d.caches_opened = 1;
        p.event(Event::Data(Box::new(d)), 20);
        assert_eq!(p.ready(), 1);
        assert_eq!(p.hud().unwrap().tier, 30);
    }

    #[test]
    fn xp_moves_the_tier_and_shows_the_banner() {
        let mut p = Pass::default();
        p.event(Event::Data(Box::new(data(900, false))), 0);
        p.event(Event::Xp(PassXp { gained: 150, source: 0, xp: 1_050, tier: 1, momentum: false }), 100);
        assert_eq!(p.banner.map(|b| b.0), Some(1));
        assert_eq!(p.pills.len(), 1);
        assert_eq!(p.hud().unwrap().tier, 1);
    }

    #[test]
    fn acts_go_out_as_the_server_reads_them() {
        assert_eq!(Act::Claim(-1).wire(), (act::CLAIM, -1));
        assert_eq!(Act::Unlock(1000).wire(), (act::UNLOCK, 1000));
        assert_eq!(Act::Aura(2).wire(), (act::AURA, 2));
        let mut p = Pass::default();
        p.event(Event::Data(Box::new(data(5_000, false))), 0);
        p.open = true;
        p.hits = vec![(Hit::Gild, [0.0, 0.0, 10.0, 10.0]), (Hit::Claim(3), [20.0, 0.0, 10.0, 10.0])];
        assert_eq!(p.click((5.0, 5.0)), None);
        assert!(p.confirm);
        p.hits = vec![(Hit::Confirm, [0.0, 0.0, 10.0, 10.0])];
        assert_eq!(p.click((5.0, 5.0)), Some(Act::Unlock(1000)));
        p.hits = vec![(Hit::Claim(3), [20.0, 0.0, 10.0, 10.0])];
        assert_eq!(p.click((25.0, 5.0)), Some(Act::Claim(3)));
        assert_eq!(Tab::named("looks"), Some(Tab::Looks));
    }
}
