//! The quest log: daily, weekly and monthly quests, the daily reward track and achievements
//! (the world server's `QuestTracker.cs`). It opens with J, the HUD's Quests button, or the
//! Tinkerer (quests) and the Chronicler (daily rewards, achievements) by the Nexus spawn.
//!
//! The client asks for everything once in each world (`QuestsRequest`) and keeps it up to date
//! from `QuestProgress`, so the HUD's badge counts the rewards waiting even while the window is
//! shut. `QuestNotice`s pop up at the top of the view: a quest done, an achievement earned, a new
//! rank. Gifts for a while (a free item for every account) sit on the Daily Rewards tab. The
//! server checks every claim (`QuestClaim`); the window only asks.

use crate::forge::{fame_text, fame_width};
use crate::hud::{Layout, SlotKind, Ui};
use crate::theme::{self, color, Kind};
use bsk_proto::{QuestChange, QuestReward, QuestsData};
use bsk_world::GameObj;
use winit::keyboard::KeyCode;

/// What a `QuestClaim` asks for (`QuestTracker.CLAIM_QUEST` and the rest).
pub const CLAIM_QUEST: u8 = 0;
pub const CLAIM_BONUS: u8 = 1;
pub const CLAIM_LOGIN: u8 = 2;
pub const CLAIM_ACHIEVEMENT: u8 = 3;
pub const SWAP: u8 = 4;
pub const COLOR: u8 = 5;
pub const CLAIM_GIFT: u8 = 6;

/// `QuestNotice` kinds.
const NOTICE_QUEST: u8 = 0;
const NOTICE_ACHIEVEMENT: u8 = 1;
const NOTICE_LOGIN: u8 = 2;
const NOTICE_RANK: u8 = 3;
/// The client's own popup for a gift waiting (the server sends none).
const NOTICE_GIFT: u8 = 200;

/// What the window asks of the server.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Act {
    Request,
    Claim(u8, String),
}

/// What the server said.
#[derive(Clone, Debug)]
pub enum Event {
    Data(Box<QuestsData>),
    Result(bool, String),
    Notice(u8, String, String),
    Progress(Vec<QuestChange>),
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum Tab {
    #[default]
    Daily,
    Weekly,
    Monthly,
    Login,
    Achievements,
}

impl Tab {
    /// A tab by name (`--quests`): daily, weekly, monthly, login, achievements.
    pub fn named(name: &str) -> Option<Tab> {
        Some(match name.to_ascii_lowercase().as_str() {
            "daily" => Tab::Daily,
            "weekly" => Tab::Weekly,
            "monthly" => Tab::Monthly,
            "login" | "rewards" => Tab::Login,
            "achievements" => Tab::Achievements,
            _ => return None,
        })
    }
}

const TABS: [(Tab, &str); 5] = [
    (Tab::Daily, "Daily"),
    (Tab::Weekly, "Weekly"),
    (Tab::Monthly, "Monthly"),
    (Tab::Login, "Daily Rewards"),
    (Tab::Achievements, "Achievements"),
];

const PERIOD_NAMES: [&str; 3] = ["daily", "weekly", "monthly"];

/// The achievement tiers' colours: bronze, silver, gold, ember.
const TIER_COLORS: [u32; 4] = [0xCD8A4E, 0xC9D2E3, color::GOLD, color::ACCENT];
const TIER_NAMES: [&str; 4] = ["Bronze", "Silver", "Gold", "Ember"];

/// How long a popup shows, and how long it takes to come and go (ms).
const TOAST_MS: i32 = 6000;
const TOAST_FADE: i32 = 350;
const MAX_TOASTS: usize = 4;

#[derive(Clone, Copy, Debug, PartialEq)]
enum Hit {
    Tab(Tab),
    Close,
    Claim(usize),
    Swap(usize),
    Bonus(u8),
    Login,
    Gift(usize),
    Achievement(usize),
    Category(Option<usize>),
    ReadyOnly,
    Color(i32),
    Up,
    Down,
}

struct Toast {
    kind: u8,
    title: String,
    text: String,
    since: i32,
}

/// Tiers of an achievement whose goal its progress has reached.
pub fn earned(goals: &[i32], progress: i32) -> usize {
    goals.iter().take_while(|&&g| progress >= g).count()
}

/// "5h 12m", "12m 30s", "40s".
pub fn countdown(seconds: i64) -> String {
    let s = seconds.max(0);
    let (d, h, m, sec) = (s / 86400, s / 3600 % 24, s / 60 % 60, s % 60);
    if d > 0 {
        format!("{d}d {h}h")
    } else if h > 0 {
        format!("{h}h {m}m")
    } else if m > 0 {
        format!("{m}m {sec}s")
    } else {
        format!("{sec}s")
    }
}

fn inside(r: [f32; 4], (x, y): (f64, f64)) -> bool {
    let (x, y) = (x as f32, y as f32);
    x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3]
}

/// The quest log, and the popups.
#[derive(Default)]
pub struct Quests {
    pub open: bool,
    tab: Tab,
    data: Option<QuestsData>,
    /// When (game ms) `data` came: the countdowns run on from its `now`.
    received: i32,
    /// The server's last word: (text, is it an error, when).
    message: Option<(String, bool, i32)>,
    hits: Vec<(Hit, [f32; 4])>,
    rect: Option<[f32; 4]>,
    toasts: Vec<Toast>,
    toast_hits: Vec<(Tab, [f32; 4])>,
    /// The achievements' category filter (an index into `categories()`), and Ready to claim.
    category: Option<usize>,
    ready_only: bool,
    /// The achievements list's first row, and how many fit.
    row: usize,
    rows: usize,
    hover_item: Option<i32>,
    /// The day (Unix days) we last said the daily reward was ready.
    login_said: i64,
    /// The gifts we said were waiting, by id.
    gifts_said: Vec<String>,
    now: i32,
}

impl Quests {
    /// Opens the window at a tab; the first time, it asks for the data.
    pub fn show(&mut self, tab: Option<Tab>) -> Option<Act> {
        self.open = true;
        self.message = None;
        if let Some(t) = tab {
            self.tab = t;
        }
        self.data.is_none().then_some(Act::Request)
    }

    /// The tab to open at by default: one with something to claim, else the dailies.
    pub fn best_tab(&self) -> Tab {
        TABS.iter().map(|&(t, _)| t).find(|&t| self.ready_in(t) > 0).unwrap_or(Tab::Daily)
    }

    pub fn close(&mut self) {
        self.open = false;
    }

    /// Forgets the data (a new character or account): the next join asks again.
    pub fn reset(&mut self) {
        self.data = None;
        self.open = false;
        self.toasts.clear();
    }

    pub fn loaded(&self) -> bool {
        self.data.is_some()
    }

    /// Rewards waiting to be claimed, in all.
    pub fn ready(&self) -> usize {
        TABS.iter().map(|&(t, _)| self.ready_in(t)).sum()
    }

    /// Rewards waiting to be claimed on a tab.
    pub fn ready_in(&self, tab: Tab) -> usize {
        let Some(d) = &self.data else { return 0 };
        match tab {
            Tab::Daily | Tab::Weekly | Tab::Monthly => {
                let p = tab as u8;
                let quests = d.quests.iter().filter(|q| q.period == p && !q.claimed && q.progress >= q.goal).count();
                quests + usize::from(self.bonus_ready(p))
            }
            Tab::Login => usize::from(d.login_ready) + self.gifts().iter().filter(|g| !g.claimed).count(),
            Tab::Achievements => d.achievements.iter().filter(|a| earned(&a.goals, a.progress) > a.claimed as usize).count(),
        }
    }

    /// The gifts open now (none from an older server).
    fn gifts(&self) -> &[bsk_proto::QuestGiftInfo] {
        self.data.as_ref().and_then(|d| d.gifts.as_ref()).map_or(&[], |g| &g[..])
    }

    fn bonus_ready(&self, period: u8) -> bool {
        let Some(d) = &self.data else { return false };
        let mut quests = d.quests.iter().filter(|q| q.period == period).peekable();
        let all = quests.peek().is_some() && quests.all(|q| q.claimed);
        all && d.periods.iter().any(|p| p.period == period && !p.bonus_claimed && !p.bonus.is_empty())
    }

    /// The server's time now, in Unix seconds.
    fn server_now(&self) -> i64 {
        self.data.as_ref().map_or(0, |d| d.now as i64 + ((self.now - self.received).max(0) / 1000) as i64)
    }

    pub fn event(&mut self, e: Event, now: i32) {
        self.now = now;
        match e {
            Event::Data(d) => {
                log::info!("quests: {} quests, {} achievements, rank {} ({} points)", d.quests.len(), d.achievements.len(), d.rank, d.points);
                self.received = now;
                let day = d.now as i64 / 86400;
                if d.login_ready && self.login_said != day {
                    self.login_said = day;
                    let n = d.login_day as usize + 1;
                    self.toast(NOTICE_LOGIN, "Daily reward ready!".into(), format!("Day {n} is waiting: press J, or see the Chronicler."), now);
                }
                let new_gifts: Vec<_> = d.gifts.iter().flat_map(|g| g.iter()).filter(|g| !g.claimed && !self.gifts_said.contains(&g.id)).cloned().collect();
                for g in new_gifts {
                    self.gifts_said.push(g.id.clone());
                    self.toast(NOTICE_GIFT, format!("A gift: {}!", g.title), "Claim it on the Daily Rewards tab: press J.".into(), now);
                }
                self.data = Some(*d);
                let max = self.achievement_order().len();
                self.row = self.row.min(max.saturating_sub(1));
            }
            Event::Progress(changes) => {
                let Some(d) = &mut self.data else { return };
                for c in changes {
                    match c.kind {
                        0 => {
                            if let Some(q) = d.quests.get_mut(c.index as usize) {
                                q.progress = c.progress;
                            }
                        }
                        _ => {
                            if let Some(a) = d.achievements.get_mut(c.index as usize) {
                                a.progress = c.progress;
                            }
                        }
                    }
                }
            }
            Event::Result(ok, text) => self.message = Some((text, !ok, now)),
            Event::Notice(kind, title, text) => self.toast(kind, title, text, now),
        }
    }

    fn toast(&mut self, kind: u8, title: String, text: String, now: i32) {
        self.toasts.push(Toast { kind, title, text, since: now });
        if self.toasts.len() > MAX_TOASTS {
            self.toasts.remove(0);
        }
    }

    /// The tab a popup leads to.
    fn toast_tab(&self, kind: u8) -> Tab {
        match kind {
            NOTICE_ACHIEVEMENT | NOTICE_RANK => Tab::Achievements,
            NOTICE_LOGIN | NOTICE_GIFT => Tab::Login,
            NOTICE_QUEST => [Tab::Daily, Tab::Weekly, Tab::Monthly].into_iter().find(|&t| self.ready_in(t) > 0).unwrap_or(Tab::Daily),
            _ => self.best_tab(),
        }
    }

    /// A click on a popup: opens the window where it leads. `None` if it wasn't on one, else
    /// what opening the window asks of the server.
    pub fn toast_click(&mut self, pos: (f64, f64)) -> Option<Option<Act>> {
        let tab = self.toast_hits.iter().find(|(_, r)| inside(*r, pos)).map(|(t, _)| *t)?;
        self.toasts.clear();
        self.toast_hits.clear();
        Some(self.show(Some(tab)))
    }

    /// Whether a point is on a popup (so a click there doesn't shoot).
    pub fn on_toast(&self, pos: (f64, f64)) -> bool {
        self.toast_hits.iter().any(|(_, r)| inside(*r, pos))
    }

    /// The achievement categories, in the order they first come.
    fn categories(&self) -> Vec<String> {
        let mut out: Vec<String> = Vec::new();
        if let Some(d) = &self.data {
            for a in d.achievements.iter() {
                if !out.contains(&a.category) {
                    out.push(a.category.clone());
                }
            }
        }
        out
    }

    /// The achievements the filters let through: those ready to claim first, then those in
    /// progress, then the finished ones, each in the data's order.
    fn achievement_order(&self) -> Vec<usize> {
        let Some(d) = &self.data else { return Vec::new() };
        let cats = self.categories();
        let cat = self.category.and_then(|c| cats.get(c).cloned());
        let mut list: Vec<(u8, usize)> = d
            .achievements
            .iter()
            .enumerate()
            .filter(|(_, a)| cat.as_ref().is_none_or(|c| &a.category == c))
            .filter(|(_, a)| !self.ready_only || earned(&a.goals, a.progress) > a.claimed as usize)
            .map(|(i, a)| {
                let rank = if earned(&a.goals, a.progress) > a.claimed as usize {
                    0
                } else if (a.claimed as usize) < a.goals.len() {
                    1
                } else {
                    2
                };
                (rank, i)
            })
            .collect();
        list.sort();
        list.into_iter().map(|(_, i)| i).collect()
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
            Hit::Claim(i) => return d.and_then(|d| d.quests.get(i)).map(|q| Act::Claim(CLAIM_QUEST, q.id.clone())),
            Hit::Swap(i) => return d.and_then(|d| d.quests.get(i)).map(|q| Act::Claim(SWAP, q.id.clone())),
            Hit::Bonus(p) => return Some(Act::Claim(CLAIM_BONUS, PERIOD_NAMES[p as usize % 3].to_owned())),
            Hit::Login => return Some(Act::Claim(CLAIM_LOGIN, String::new())),
            Hit::Gift(i) => return self.gifts().get(i).map(|g| Act::Claim(CLAIM_GIFT, g.id.clone())),
            Hit::Achievement(i) => return d.and_then(|d| d.achievements.get(i)).map(|a| Act::Claim(CLAIM_ACHIEVEMENT, a.id.clone())),
            Hit::Color(rank) => return Some(Act::Claim(COLOR, rank.to_string())),
            Hit::Category(c) => {
                self.category = c;
                self.row = 0;
            }
            Hit::ReadyOnly => {
                self.ready_only = !self.ready_only;
                self.row = 0;
            }
            Hit::Up => self.row = self.row.saturating_sub(self.rows.max(1)),
            Hit::Down => self.scroll(self.rows.max(1) as i32),
        }
        None
    }

    pub fn scroll(&mut self, rows: i32) {
        if self.tab != Tab::Achievements {
            return;
        }
        let max = self.achievement_order().len().saturating_sub(self.rows.max(1));
        self.row = (self.row as i32 + rows).clamp(0, max as i32) as usize;
    }

    /// A key while the window is open: Escape (or the window's own key) closes it, the arrows
    /// change tab.
    pub fn key(&mut self, code: KeyCode) {
        let at = TABS.iter().position(|&(t, _)| t == self.tab).unwrap_or(0);
        match code {
            KeyCode::Escape => self.close(),
            KeyCode::ArrowLeft => self.tab = TABS[(at + TABS.len() - 1) % TABS.len()].0,
            KeyCode::ArrowRight => self.tab = TABS[(at + 1) % TABS.len()].0,
            KeyCode::ArrowUp => self.scroll(-1),
            KeyCode::ArrowDown => self.scroll(1),
            _ => {}
        }
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.now = now;
        self.hits.clear();
        self.hover_item = None;
        if !self.open {
            self.rect = None;
            return;
        }
        let (dw, dh) = (860.0, 580.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        let mid = r[1] + 32.0 * s;
        let ts = font(22.0);
        ui.label("Quest Log", r[0] + 22.0 * s, (mid - ui.digit_middle(ts, true)).round(), ts, true, color::GOLD);

        // The tabs, each with a count of what waits on it.
        let cs = font(13.0);
        let widths: Vec<f32> = TABS.iter().map(|(t, l)| ui.measure(l, cs, true) + if self.ready_in(*t) > 0 { 46.0 } else { 28.0 } * s).collect();
        let gap = 6.0 * s;
        let total = widths.iter().sum::<f32>() + gap * (TABS.len() - 1) as f32;
        let mut x = (r[0] + 170.0 * s + (r[2] - 170.0 * s - 56.0 * s - total) / 2.0).round();
        for ((t, label), w) in TABS.into_iter().zip(widths) {
            let tr = [x, (mid - 14.0 * s).round(), w, 28.0 * s];
            let on = t == self.tab;
            chip(ui, tr, label, on, inside(tr, mouse), cs, Some(self.ready_in(t)), s);
            self.hits.push((Hit::Tab(t), tr));
            x += w + gap;
        }
        let cb = [r[0] + r[2] - 42.0 * s, (mid - 14.0 * s).round(), 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        theme::cross(ui.renderer, cb[0] + cb[2] / 2.0, cb[1] + cb[3] / 2.0, 6.0 * s, (2.0 * s).max(1.0), color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));

        let body = [r[0] + 22.0 * s, r[1] + 62.0 * s, r[2] - 44.0 * s, r[3] - 104.0 * s];
        if self.data.is_none() {
            let t = "Asking the Tinkerer for your quests...";
            ui.label(t, body[0], body[1] + 10.0 * s, font(14.0), false, color::TEXT_MUTED);
        } else {
            match self.tab {
                Tab::Daily | Tab::Weekly | Tab::Monthly => self.draw_period(ui, body, self.tab as u8, s, mouse, now),
                Tab::Login => self.draw_login(ui, body, s, mouse, now),
                Tab::Achievements => self.draw_achievements(ui, body, s, mouse, now),
            }
        }
        let foot_y = r[1] + r[3] - 34.0 * s;
        if let Some((m, err, _)) = &self.message {
            ui.label(m, r[0] + 22.0 * s, foot_y, font(13.0), true, if *err { color::DANGER } else { color::GOOD });
        } else {
            let hint = "Rewards: fame to your account, items to your inventory (or your vault's gift chest).";
            ui.label(hint, r[0] + 22.0 * s, foot_y, font(12.0), false, color::TEXT_MUTED);
        }
        if let Some(item) = self.hover_item {
            let lines = crate::hud::item_tooltip(ui.data, item, me, SlotKind::Shop);
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }

    /// Rewards in a row from `x`, centred on `cy`: fame as its number and icon, items in small
    /// slots (with a count when more than one). Returns where the row ends.
    #[allow(clippy::too_many_arguments)]
    fn rewards(&mut self, ui: &mut Ui, rewards: &[QuestReward], x: f32, cy: f32, s: f32, mouse: (f64, f64), dim: bool) -> f32 {
        let size = (14.0 * s).round().max(9.0);
        let mut x = x;
        for r in rewards {
            if r.kind == 0 {
                let t = crate::hud::group_digits(r.amount);
                let w = fame_text(ui, &t, x, (cy - ui.digit_middle(size, true)).round(), size, if dim { color::TEXT_MUTED } else { color::GOLD }, s);
                x += w + 12.0 * s;
            } else {
                let side = 34.0 * s;
                let sr = [x, (cy - side / 2.0).round(), side, side];
                let over = inside(sr, mouse);
                ui.slot_box(sr, crate::screens::tier_edge(ui.data, r.item as i32), over, s);
                ui.item_icon(r.item as i32, 60, sr[0] + side / 2.0, sr[1] + side / 2.0, s);
                if dim {
                    theme::fill(ui.renderer, sr, 5.0 * s, 0x000000, 0.45);
                }
                if r.amount > 1 {
                    let cs = (11.0 * s).round().max(9.0);
                    let t = format!("{}", r.amount);
                    let tw = ui.measure(&t, cs, true);
                    ui.label(&t, sr[0] + side - tw - 3.0 * s, sr[1] + side - cs * 1.2, cs, true, color::TEXT);
                }
                if over {
                    self.hover_item = Some(r.item as i32);
                }
                x += side + 8.0 * s;
            }
        }
        x
    }

    fn rewards_width(ui: &mut Ui, rewards: &[QuestReward], s: f32) -> f32 {
        let size = (14.0 * s).round().max(9.0);
        rewards
            .iter()
            .map(|r| if r.kind == 0 { fame_width(ui, &crate::hud::group_digits(r.amount), size, s) + 12.0 * s } else { 42.0 * s })
            .sum::<f32>()
    }

    fn draw_period(&mut self, ui: &mut Ui, body: [f32; 4], period: u8, s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let Some(d) = self.data.clone() else { return };
        let info = d.periods.iter().find(|p| p.period == period);
        let left = info.map_or(0, |p| p.resets_at as i64 - self.server_now());
        let name = PERIOD_NAMES[period as usize % 3];
        ui.label(&format!("New {name} quests in {}", countdown(left)), body[0], body[1], font(14.0), true, color::TEXT_DIM);
        let swaps = info.map_or(0, |p| p.rerolls_left);
        let st = if swaps == 0 { "No swaps left".to_owned() } else { format!("{swaps} swap{} left: trade a quest you don't like for another", if swaps == 1 { "" } else { "s" }) };
        let sw = ui.measure(&st, font(12.0), false);
        ui.label(&st, body[0] + body[2] - sw, body[1] + 2.0 * s, font(12.0), false, color::TEXT_MUTED);

        let quests: Vec<(usize, bsk_proto::QuestInfo)> = d.quests.iter().cloned().enumerate().filter(|(_, q)| q.period == period).collect();
        let card_h = 94.0 * s;
        let gap = 10.0 * s;
        let mut y = body[1] + 28.0 * s;
        let pulse = 0.5 + 0.5 * ((now as f32) / 260.0).sin();
        for (i, q) in &quests {
            let cr = [body[0], y, body[2], card_h];
            let ready = !q.claimed && q.progress >= q.goal;
            card(ui, cr, ready, q.claimed, pulse, s);
            // the title and what to do
            let tx = cr[0] + 18.0 * s;
            let title_c = if q.claimed { color::TEXT_MUTED } else { color::TEXT };
            ui.label(&q.title, tx, cr[1] + 12.0 * s, font(17.0), true, title_c);
            ui.label(&q.text, tx, cr[1] + 36.0 * s, font(13.0), false, color::TEXT_DIM);
            // the progress
            let bar = [tx, cr[1] + 62.0 * s, 300.0 * s, 16.0 * s];
            progress_bar(ui, bar, q.progress, q.goal, q.claimed, s);
            // the rewards, then the button
            let bw = 104.0 * s;
            let br = [cr[0] + cr[2] - bw - 16.0 * s, cr[1] + (card_h - 36.0 * s) / 2.0, bw, 36.0 * s];
            let rw = Self::rewards_width(ui, &q.rewards, s);
            let rx = br[0] - 18.0 * s - rw;
            ui.label("Reward", rx, cr[1] + 12.0 * s, font(11.0), true, color::TEXT_MUTED);
            self.rewards(ui, &q.rewards.clone(), rx, cr[1] + 52.0 * s, s, mouse, q.claimed);
            if q.claimed {
                done_chip(ui, br, "Claimed", s);
            } else if ready {
                glow(ui, br, pulse, s);
                ui.button(br, "Claim", Kind::Primary, inside(br, mouse), s);
                self.hits.push((Hit::Claim(*i), br));
            } else if swaps > 0 {
                let sr = [br[0] + 14.0 * s, br[1] + 4.0 * s, br[2] - 28.0 * s, br[3] - 8.0 * s];
                ui.button(sr, "Swap", Kind::Secondary, inside(sr, mouse), s * 0.85);
                self.hits.push((Hit::Swap(*i), sr));
            }
            y += card_h + gap;
        }
        if quests.is_empty() {
            ui.label("No quests right now.", body[0], y, font(14.0), false, color::TEXT_MUTED);
        }

        // The bonus for claiming them all.
        let Some(info) = info else { return };
        let br = [body[0], body[1] + body[3] - 66.0 * s, body[2], 66.0 * s];
        let ready = self.bonus_ready(period);
        let claimed_n = quests.iter().filter(|(_, q)| q.claimed).count();
        theme::frame(ui.renderer, br, 8.0 * s, s.max(1.0), (if ready { color::GOLD } else { color::BORDER }, 1.0), ((color::WARM_TOP, 0.9), (color::WARM_BOTTOM, 0.9)));
        theme::star(ui.renderer, br[0] + 26.0 * s, br[1] + br[3] / 2.0, 11.0 * s, if info.bonus_claimed || ready { color::GOLD } else { color::TEXT_MUTED });
        let title = format!("{} bonus", capital(name));
        ui.label(&title, br[0] + 46.0 * s, br[1] + 12.0 * s, font(15.0), true, color::GOLD);
        let sub = if info.bonus_claimed {
            "Claimed: well done!".to_owned()
        } else {
            format!("Claim all {} {name} quests ({claimed_n}/{})", quests.len(), quests.len())
        };
        ui.label(&sub, br[0] + 46.0 * s, br[1] + 36.0 * s, font(12.0), false, color::TEXT_DIM);
        let bw = 104.0 * s;
        let bb = [br[0] + br[2] - bw - 16.0 * s, br[1] + (br[3] - 34.0 * s) / 2.0, bw, 34.0 * s];
        let rw = Self::rewards_width(ui, &info.bonus, s);
        self.rewards(ui, &info.bonus.clone(), bb[0] - 18.0 * s - rw, br[1] + br[3] / 2.0, s, mouse, info.bonus_claimed);
        if info.bonus_claimed {
            done_chip(ui, bb, "Claimed", s);
        } else if ready {
            glow(ui, bb, pulse, s);
            ui.button(bb, "Claim", Kind::Primary, inside(bb, mouse), s);
            self.hits.push((Hit::Bonus(period), bb));
        } else {
            locked(ui, bb, s);
        }
    }

    fn draw_login(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let Some(d) = self.data.clone() else { return };
        let days = d.login_days.len().max(1);
        let pulse = 0.5 + 0.5 * ((now as f32) / 260.0).sin();
        // the header: which day, the streak, and the button or when the next comes
        let today = d.login_day as usize % days;
        let head = if d.login_ready { format!("Day {} of {days} is ready!", today + 1) } else { format!("Day {} of {days} comes tomorrow", today + 1) };
        ui.label(&head, body[0], body[1], font(18.0), true, if d.login_ready { color::GOLD } else { color::TEXT });
        let streak = if d.login_streak > 1 { format!("{} days in a row", d.login_streak) } else { "Come back every day".to_owned() };
        let total = if d.login_total == 1 { "1 reward collected".to_owned() } else { format!("{} rewards collected", d.login_total) };
        let sub = format!("{streak}  ·  {total}  ·  Missing a day never loses your place.");
        ui.label(&sub, body[0], body[1] + 28.0 * s, font(12.0), false, color::TEXT_DIM);
        let bw = 170.0 * s;
        let bb = [body[0] + body[2] - bw, body[1], bw, 40.0 * s];
        if d.login_ready {
            glow(ui, bb, pulse, s);
            ui.button(bb, &format!("Collect day {}", today + 1), Kind::Primary, inside(bb, mouse), s);
            self.hits.push((Hit::Login, bb));
        } else {
            let left = d.login_next_at as i64 - self.server_now();
            let t = format!("Next in {}", countdown(left));
            let tw = ui.measure(&t, font(14.0), true);
            ui.label(&t, bb[0] + bb[2] - tw, bb[1] + 10.0 * s, font(14.0), true, color::TEXT_DIM);
        }
        // the gifts open now, each a banner above the track
        let mut gy = body[1] + 58.0 * s;
        let gifts = self.gifts().to_vec();
        for (k, g) in gifts.iter().enumerate() {
            let gr = [body[0], gy, body[2], 66.0 * s];
            let ready = !g.claimed;
            theme::frame(ui.renderer, gr, 8.0 * s, s.max(1.0), (if ready { color::GOLD } else { color::BORDER }, 1.0), ((color::WARM_TOP, 0.95), (color::WARM_BOTTOM, 0.95)));
            theme::star(ui.renderer, gr[0] + 26.0 * s, gr[1] + gr[3] / 2.0, 11.0 * s, if ready { color::GOLD } else { color::TEXT_MUTED });
            let title = format!("Gift: {}", g.title);
            ui.label(&title, gr[0] + 46.0 * s, gr[1] + 10.0 * s, font(15.0), true, color::GOLD);
            let when = if g.claimed { "Claimed".to_owned() } else { format!("Ends in {}", countdown(g.ends_at as i64 - self.server_now())) };
            let tw = ui.measure(&title, font(15.0), true);
            ui.label(&when, gr[0] + 46.0 * s + tw + 12.0 * s, gr[1] + 12.0 * s, font(12.0), true, color::TEXT_MUTED);
            let mut sub = g.text.clone();
            let bw = 104.0 * s;
            let bb = [gr[0] + gr[2] - bw - 16.0 * s, gr[1] + (gr[3] - 34.0 * s) / 2.0, bw, 34.0 * s];
            let rw = Self::rewards_width(ui, &g.rewards, s);
            let rx = bb[0] - 18.0 * s - rw;
            // the text is cut short where the rewards start
            let room = rx - 12.0 * s - (gr[0] + 46.0 * s);
            while sub.chars().count() > 4 && ui.measure(&sub, font(12.0), false) > room {
                let n = sub.chars().count();
                sub = sub.chars().take(n - 4).collect::<String>() + "...";
            }
            ui.label(&sub, gr[0] + 46.0 * s, gr[1] + 36.0 * s, font(12.0), false, color::TEXT_DIM);
            self.rewards(ui, &g.rewards.clone(), rx, gr[1] + gr[3] / 2.0, s, mouse, g.claimed);
            if g.claimed {
                done_chip(ui, bb, "Claimed", s);
            } else {
                glow(ui, bb, pulse, s);
                ui.button(bb, "Claim", Kind::Primary, inside(bb, mouse), s);
                self.hits.push((Hit::Gift(k), bb));
            }
            gy += gr[3] + 10.0 * s;
        }
        // the track: seven days a row
        let cols = 7;
        let rows = days.div_ceil(cols);
        let gap = 8.0 * s;
        let tw = (body[2] - gap * (cols - 1) as f32) / cols as f32;
        let th = ((body[1] + body[3] - gy - gap * (rows - 1) as f32) / rows as f32).min(92.0 * s);
        for (k, day) in d.login_days.iter().enumerate() {
            let tr = [body[0] + (k % cols) as f32 * (tw + gap), gy + (k / cols) as f32 * (th + gap), tw, th];
            let claimed = k < today;
            let is_today = k == today;
            let border = if is_today && d.login_ready { color::GOLD } else if day.milestone { theme::shade(color::ACCENT, -0.3) } else { color::BORDER };
            let (top, bottom) = if day.milestone || (is_today && d.login_ready) { (color::WARM_TOP, color::WARM_BOTTOM) } else { (color::INSET_TOP, color::INSET_BOTTOM) };
            if is_today && d.login_ready {
                let g = 3.0 * s + pulse * 3.0 * s;
                theme::fill(ui.renderer, [tr[0] - g, tr[1] - g, tr[2] + 2.0 * g, tr[3] + 2.0 * g], 10.0 * s, color::GOLD, 0.18);
            }
            theme::frame(ui.renderer, tr, 7.0 * s, s.max(1.0), (border, 1.0), ((top, 0.95), (bottom, 0.95)));
            let label = format!("Day {}", k + 1);
            ui.label(&label, tr[0] + 7.0 * s, tr[1] + 5.0 * s, font(11.0), true, if is_today { color::GOLD } else { color::TEXT_MUTED });
            if day.milestone {
                theme::star(ui.renderer, tr[0] + tr[2] - 11.0 * s, tr[1] + 11.0 * s, 6.0 * s, color::GOLD);
            }
            // the rewards, centred
            // smaller when a day's rewards wouldn't fit its tile
            let fit = (tr[2] - 10.0 * s) / Self::rewards_width(ui, &day.rewards, s).max(1.0);
            let k = s * fit.min(0.85);
            let rw = Self::rewards_width(ui, &day.rewards, k);
            self.rewards(ui, &day.rewards.clone(), tr[0] + (tr[2] - rw) / 2.0 + 6.0 * k, tr[1] + tr[3] * 0.6, k, mouse, claimed);
            if claimed {
                check(ui, tr[0] + tr[2] - 14.0 * s, tr[1] + tr[3] - 14.0 * s, 7.0 * s, s);
            } else if is_today && !d.login_ready {
                let t = "Tomorrow";
                let w = ui.measure(t, font(10.0), true);
                ui.label(t, tr[0] + tr[2] - w - 6.0 * s, tr[1] + tr[3] - font(10.0) * 1.4, font(10.0), true, color::TEXT_DIM);
            }
        }
    }

    fn draw_achievements(&mut self, ui: &mut Ui, body: [f32; 4], s: f32, mouse: (f64, f64), now: i32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let Some(d) = self.data.clone() else { return };
        let pulse = 0.5 + 0.5 * ((now as f32) / 260.0).sin();
        // The left column: the rank, the name colours, the filters.
        let lw = 210.0 * s;
        let rank = d.ranks.get(d.rank as usize);
        let next = d.ranks.get(d.rank as usize + 1);
        let rr = [body[0], body[1], lw, 92.0 * s];
        theme::frame(ui.renderer, rr, 8.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::WARM_TOP, 0.95), (color::WARM_BOTTOM, 0.95)));
        let rank_color = rank.map(|r| r.color as u32).filter(|&c| c != 0).unwrap_or(color::TEXT);
        ui.label(rank.map_or("", |r| r.name.as_str()), rr[0] + 14.0 * s, rr[1] + 10.0 * s, font(20.0), true, rank_color);
        ui.label(&format!("{} points", crate::hud::group_digits(d.points)), rr[0] + 14.0 * s, rr[1] + 38.0 * s, font(13.0), true, color::GOLD);
        let bar = [rr[0] + 14.0 * s, rr[1] + 62.0 * s, lw - 28.0 * s, 10.0 * s];
        match (rank, next) {
            (Some(r), Some(n)) => {
                let k = ((d.points - r.points) as f32 / (n.points - r.points).max(1) as f32).clamp(0.0, 1.0);
                theme::fill(ui.renderer, bar, bar[3] / 2.0, color::TRACK, 1.0);
                theme::fill(ui.renderer, [bar[0], bar[1], bar[2] * k, bar[3]], bar[3] / 2.0, color::GOLD, 1.0);
                ui.label(&format!("{} at {}", n.name, n.points), bar[0], bar[1] + 12.0 * s, font(10.0), false, color::TEXT_MUTED);
            }
            _ => ui.label("The highest rank!", bar[0], bar[1] - 2.0 * s, font(11.0), true, color::GOOD),
        }
        // name colours: one chip per rank that has one (locked until reached)
        let mut y = rr[1] + rr[3] + 12.0 * s;
        ui.label("Chat name colour", body[0], y, font(12.0), true, color::TEXT_DIM);
        y += 20.0 * s;
        let sw = 26.0 * s;
        let mut x = body[0];
        let def = [x, y, sw, sw];
        swatch(ui, def, None, d.color_rank == 255, false, inside(def, mouse), s);
        self.hits.push((Hit::Color(-1), def));
        x += sw + 6.0 * s;
        for (k, rk) in d.ranks.iter().enumerate().filter(|(_, r)| r.color != 0) {
            if x + sw > body[0] + lw {
                x = body[0];
                y += sw + 6.0 * s;
            }
            let sr = [x, y, sw, sw];
            let locked = k > d.rank as usize;
            swatch(ui, sr, Some(rk.color as u32), d.color_rank as usize == k, locked, inside(sr, mouse), s);
            if !locked {
                self.hits.push((Hit::Color(k as i32), sr));
            }
            x += sw + 6.0 * s;
        }
        y += sw + 16.0 * s;
        // the filters
        let cats = self.categories();
        let mut items: Vec<(Hit, String, bool)> = vec![(Hit::Category(None), "All".into(), self.category.is_none())];
        for (k, c) in cats.iter().enumerate() {
            items.push((Hit::Category(Some(k)), c.clone(), self.category == Some(k)));
        }
        let (ch, cg) = (24.0 * s, 5.0 * s);
        let (mut cx, mut cy) = (body[0], y);
        let csz = font(11.5);
        for (hit, label, on) in items {
            let w = ui.measure(&label, csz, true) + 16.0 * s;
            if cx + w > body[0] + lw && cx > body[0] {
                cx = body[0];
                cy += ch + cg;
            }
            let r = [cx, cy, w, ch];
            chip(ui, r, &label, on, inside(r, mouse), csz, None, s);
            self.hits.push((hit, r));
            cx += w + cg;
        }
        cy += ch + cg + 4.0 * s;
        let ready_n = self.ready_in(Tab::Achievements);
        let rr2 = [body[0], cy, lw, ch];
        chip(ui, rr2, &format!("Ready to claim ({ready_n})"), self.ready_only, inside(rr2, mouse), csz, None, s);
        self.hits.push((Hit::ReadyOnly, rr2));

        // The list.
        let lx = body[0] + lw + 18.0 * s;
        let list = [lx, body[1], body[0] + body[2] - lx, body[3]];
        let row_h = 68.0 * s;
        let gap = 6.0 * s;
        let order = self.achievement_order();
        let paging = 30.0 * s;
        self.rows = (((list[3] - paging) / (row_h + gap)).floor() as usize).max(1);
        self.row = self.row.min(order.len().saturating_sub(self.rows));
        for (n, &i) in order.iter().skip(self.row).take(self.rows).enumerate() {
            let a = &d.achievements[i];
            let ar = [list[0], list[1] + n as f32 * (row_h + gap), list[2], row_h];
            let got = earned(&a.goals, a.progress);
            let ready = got > a.claimed as usize;
            let finished = a.claimed as usize >= a.goals.len();
            card(ui, ar, ready, finished, pulse, s);
            // the tier medals
            let mx = ar[0] + 16.0 * s;
            for (t, &tier_color) in TIER_COLORS.iter().enumerate().take(a.goals.len()) {
                let c = (mx + t as f32 * 15.0 * s, ar[1] + 16.0 * s);
                medal(ui, c, 6.0 * s, tier_color, t < a.claimed as usize, t < got, pulse, s);
            }
            let tx = mx + 4.0 * 15.0 * s + 8.0 * s;
            ui.label(&a.title, tx, ar[1] + 8.0 * s, font(15.0), true, if finished { color::TEXT_DIM } else { color::TEXT });
            let tier = (a.claimed as usize).min(a.goals.len().saturating_sub(1));
            let tag = if finished { format!("{}  ·  complete", a.category) } else { format!("{}  ·  {}", a.category, TIER_NAMES[tier.min(3)]) };
            let tw = ui.measure(&a.title, font(15.0), true);
            ui.label(&tag, tx + tw + 10.0 * s, ar[1] + 11.0 * s, font(11.0), false, color::TEXT_MUTED);
            ui.label(&a.text, mx, ar[1] + 30.0 * s, font(12.0), false, color::TEXT_DIM);
            let goal = a.goals.get(tier).copied().unwrap_or(0);
            let bar = [mx, ar[1] + 48.0 * s, 260.0 * s, 12.0 * s];
            progress_bar(ui, bar, a.progress.min(goal.max(1)), goal, finished, s);
            // the reward of the tier to claim next, and the button
            let bw = 96.0 * s;
            let bb = [ar[0] + ar[2] - bw - 12.0 * s, ar[1] + (row_h - 32.0 * s) / 2.0, bw, 32.0 * s];
            if !finished {
                let pts = format!("+{} pts", a.next_points);
                let pw = ui.measure(&pts, font(12.0), true);
                let rw = Self::rewards_width(ui, &a.next_rewards, s);
                let rx = bb[0] - 14.0 * s - rw - pw - 10.0 * s;
                ui.label(&pts, rx, (ar[1] + row_h / 2.0 - ui.digit_middle(font(12.0), true)).round(), font(12.0), true, color::ACCENT_HI);
                self.rewards(ui, &a.next_rewards.clone(), rx + pw + 10.0 * s, ar[1] + row_h / 2.0, s, mouse, false);
            }
            if ready {
                glow(ui, bb, pulse, s);
                ui.button(bb, "Claim", Kind::Primary, inside(bb, mouse), s * 0.9);
                self.hits.push((Hit::Achievement(i), bb));
            } else if finished {
                done_chip(ui, bb, "Complete", s);
            }
        }
        if order.is_empty() {
            ui.label("Nothing here yet.", list[0], list[1] + 8.0 * s, font(13.0), false, color::TEXT_MUTED);
        }
        // paging
        let py = list[1] + list[3] - 26.0 * s;
        let (up, down) = ([list[0], py, 56.0 * s, 26.0 * s], [list[0] + 62.0 * s, py, 56.0 * s, 26.0 * s]);
        ui.button(up, "Up", Kind::Secondary, inside(up, mouse), s * 0.8);
        ui.button(down, "Down", Kind::Secondary, inside(down, mouse), s * 0.8);
        self.hits.push((Hit::Up, up));
        self.hits.push((Hit::Down, down));
        if !order.is_empty() {
            let t = format!("{}-{} of {}", self.row + 1, (self.row + self.rows).min(order.len()), order.len());
            ui.label(&t, list[0] + 130.0 * s, py + 6.0 * s, font(12.0), false, color::TEXT_MUTED);
        }
    }

    /// The popups, at the top middle of the view: they slide in, wait, and fade out.
    pub fn draw_toasts(&mut self, ui: &mut Ui, lay: &Layout, now: i32) {
        self.toasts.retain(|t| now - t.since < TOAST_MS);
        self.toast_hits.clear();
        // the open log shows it all already, and they'd cover its tabs
        if self.open {
            return;
        }
        let s = lay.s;
        let w = 380.0 * s;
        let h = 58.0 * s;
        let mut y = 70.0 * s;
        let toasts: Vec<(u8, String, String, i32)> = self.toasts.iter().map(|t| (t.kind, t.title.clone(), t.text.clone(), t.since)).collect();
        for (kind, title, text, since) in toasts {
            let age = now - since;
            let fade = (age as f32 / TOAST_FADE as f32).min(((TOAST_MS - age) as f32 / TOAST_FADE as f32).max(0.0)).clamp(0.0, 1.0);
            let slide = (1.0 - fade) * -14.0 * s;
            let r = [((lay.view_w() - w) / 2.0).round(), (y + slide).round(), w, h];
            theme::shadow(ui.renderer, r, 9.0 * s, 10.0 * s, 0.4 * fade);
            let accent = match kind {
                NOTICE_ACHIEVEMENT | NOTICE_RANK => color::GOLD,
                NOTICE_LOGIN => color::GOOD,
                _ => color::ACCENT,
            };
            theme::frame(ui.renderer, r, 9.0 * s, s.max(1.0), (accent, fade), ((color::PANEL_TOP, 0.96 * fade), (color::PANEL_BOTTOM, 0.97 * fade)));
            // the badge: a star for deeds, a scroll's seal for quests
            let (bx, by) = (r[0] + 28.0 * s, r[1] + h / 2.0);
            theme::fill(ui.renderer, [bx - 16.0 * s, by - 16.0 * s, 32.0 * s, 32.0 * s], 16.0 * s, accent, 0.22 * fade);
            if kind == NOTICE_QUEST || kind == 4 {
                check(ui, bx, by, 8.0 * s, s);
            } else {
                theme::star(ui.renderer, bx, by + 1.0 * s, 10.0 * s, accent);
            }
            if fade > 0.5 {
                let tsz = (14.0 * s).round().max(9.0);
                ui.label(&title, r[0] + 54.0 * s, r[1] + 9.0 * s, tsz, true, accent);
                let body = crate::npc::fit(ui, &text, (12.0 * s).round().max(9.0), w - 64.0 * s);
                ui.label(&body, r[0] + 54.0 * s, r[1] + 31.0 * s, (12.0 * s).round().max(9.0), false, color::TEXT);
            }
            self.toast_hits.push((self.toast_tab(kind), r));
            y += h + 8.0 * s;
        }
    }
}

fn capital(s: &str) -> String {
    let mut c = s.chars();
    c.next().map_or(String::new(), |f| f.to_uppercase().collect::<String>() + c.as_str())
}

/// A tab or filter chip, with a count of what waits on it.
#[allow(clippy::too_many_arguments)]
fn chip(ui: &mut Ui, r: [f32; 4], label: &str, on: bool, hover: bool, size: f32, badge: Option<usize>, s: f32) {
    let fill = if on { color::ACCENT } else if hover { color::SECONDARY } else { color::CHIP };
    theme::fill(ui.renderer, r, r[3] / 2.0, fill, 1.0);
    let n = badge.unwrap_or(0);
    let lw = ui.measure(label, size, true);
    let bw = if n > 0 { 18.0 * s } else { 0.0 };
    let x = r[0] + (r[2] - lw - bw) / 2.0;
    ui.label(label, x, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if on { color::ACCENT_TEXT } else { color::TEXT_DIM });
    if n > 0 {
        let d = 16.0 * s;
        let br = [x + lw + 4.0 * s, r[1] + (r[3] - d) / 2.0, d, d];
        theme::fill(ui.renderer, br, d / 2.0, if on { color::ACCENT_TEXT } else { color::GOLD }, 1.0);
        let t = if n > 9 { "9+".to_owned() } else { n.to_string() };
        let ts = (10.5 * s).round().max(8.0);
        let tw = ui.measure(&t, ts, true);
        ui.label(&t, br[0] + (d - tw) / 2.0, br[1] + (d - ts * 1.22) / 2.0, ts, true, if on { color::ACCENT } else { color::ACCENT_TEXT });
    }
}

/// A quest's or achievement's box: warm and edged in gold (pulsing) when its reward waits,
/// quieter when done.
fn card(ui: &mut Ui, r: [f32; 4], ready: bool, done: bool, pulse: f32, s: f32) {
    if ready {
        let g = 2.0 * s + pulse * 3.0 * s;
        theme::fill(ui.renderer, [r[0] - g, r[1] - g, r[2] + 2.0 * g, r[3] + 2.0 * g], 10.0 * s + g, color::GOLD, 0.14);
    }
    let border = if ready { color::GOLD } else { color::BORDER };
    let (top, bottom) = if ready { (color::WARM_TOP, color::WARM_BOTTOM) } else { (color::INSET_TOP, color::INSET_BOTTOM) };
    theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (border, 1.0), ((top, if done { 0.6 } else { 0.95 }), (bottom, if done { 0.6 } else { 0.95 })));
}

/// A bar filling with progress towards a goal, the numbers on it.
fn progress_bar(ui: &mut Ui, r: [f32; 4], progress: i32, goal: i32, done: bool, s: f32) {
    let k = if goal <= 0 { 1.0 } else { (progress as f32 / goal as f32).clamp(0.0, 1.0) };
    theme::fill(ui.renderer, r, r[3] / 2.0, color::TRACK, 1.0);
    let full = k >= 1.0;
    let c = if done || full { color::GOOD } else { color::ACCENT };
    if k > 0.0 {
        theme::rounded(ui.renderer, [r[0], r[1], (r[2] * k).max(r[3]), r[3]], r[3] / 2.0, (theme::shade(c, 0.15), 1.0), (theme::shade(c, -0.15), 1.0));
    }
    let size = (r[3] * 0.78).round().max(9.0);
    let t = format!("{} / {}", crate::hud::group_digits(progress.min(goal.max(progress))), crate::hud::group_digits(goal));
    ui.label(&t, r[0] + r[2] + 10.0 * s, r[1] + (r[3] - size * 1.22) / 2.0, size, true, if full { color::GOOD } else { color::TEXT_DIM });
}

/// A soft gold glow behind a button whose reward waits.
fn glow(ui: &mut Ui, r: [f32; 4], pulse: f32, s: f32) {
    let g = 3.0 * s + pulse * 4.0 * s;
    theme::fill(ui.renderer, [r[0] - g, r[1] - g, r[2] + 2.0 * g, r[3] + 2.0 * g], 8.0 * s + g, color::ACCENT, 0.18 + 0.12 * pulse);
}

/// "Claimed" or "Complete": a quiet chip with a tick.
fn done_chip(ui: &mut Ui, r: [f32; 4], label: &str, s: f32) {
    theme::fill(ui.renderer, r, r[3] / 2.0, color::CHIP, 0.8);
    let size = (13.0 * s).round().max(9.0);
    let lw = ui.measure(label, size, true);
    let x = r[0] + (r[2] - lw - 16.0 * s) / 2.0;
    check(ui, x + 5.0 * s, r[1] + r[3] / 2.0, 5.0 * s, s);
    ui.label(label, x + 16.0 * s, r[1] + (r[3] - size * 1.22) / 2.0, size, true, color::GOOD);
}

/// A padlock on a quiet chip (the bonus before its quests are claimed).
fn locked(ui: &mut Ui, r: [f32; 4], s: f32) {
    theme::fill(ui.renderer, r, r[3] / 2.0, color::CHIP, 0.5);
    let lock = 10.0 * s;
    let (x, ly) = (r[0] + (r[2] - lock) / 2.0, r[1] + r[3] / 2.0 - lock * 0.1);
    let t = (1.5 * s).max(1.0);
    ui.rect(x + lock * 0.2, ly - lock * 0.45, t, lock * 0.45, color::TEXT_MUTED, 1.0);
    ui.rect(x + lock * 0.8 - t, ly - lock * 0.45, t, lock * 0.45, color::TEXT_MUTED, 1.0);
    ui.rect(x + lock * 0.2, ly - lock * 0.45, lock * 0.6, t, color::TEXT_MUTED, 1.0);
    ui.rect(x, ly, lock, lock * 0.6, color::TEXT_MUTED, 1.0);
}

/// A tick, centred on (cx, cy), `half` across each way.
fn check(ui: &mut Ui, cx: f32, cy: f32, half: f32, s: f32) {
    let white = ui.renderer.white();
    let t = (2.2 * s).max(1.5);
    let tint = bsk_render::Tint::color(color::GOOD, 1.0);
    // the short stroke down to the corner, then the long one up
    let (ax, ay) = (cx - half * 0.55, cy);
    let (bx, by) = (cx - half * 0.1, cy + half * 0.45);
    let (ex, ey) = (cx + half * 0.7, cy - half * 0.55);
    for ((x0, y0), (x1, y1)) in [((ax, ay), (bx, by)), ((bx, by), (ex, ey))] {
        let len = ((x1 - x0).powi(2) + (y1 - y0).powi(2)).sqrt() + t * 0.5;
        let angle = (y1 - y0).atan2(x1 - x0);
        ui.renderer.sprite(white, bsk_render::Quad::rotated((x0 + x1) / 2.0, (y0 + y1) / 2.0, len, t, angle), tint);
    }
}

/// An achievement tier's medal: filled when claimed, a pulsing ring when earned, faint when not.
#[allow(clippy::too_many_arguments)]
fn medal(ui: &mut Ui, (cx, cy): (f32, f32), rad: f32, rgb: u32, claimed: bool, got: bool, pulse: f32, s: f32) {
    let r = [cx - rad, cy - rad, rad * 2.0, rad * 2.0];
    if claimed {
        theme::rounded(ui.renderer, r, rad, (theme::shade(rgb, 0.25), 1.0), (theme::shade(rgb, -0.25), 1.0));
    } else if got {
        let g = pulse * 2.0 * s;
        theme::fill(ui.renderer, [r[0] - g, r[1] - g, r[2] + 2.0 * g, r[3] + 2.0 * g], rad + g, rgb, 0.35);
        theme::frame(ui.renderer, r, rad, (1.5 * s).max(1.0), (rgb, 1.0), ((color::TRACK, 1.0), (color::TRACK, 1.0)));
    } else {
        theme::frame(ui.renderer, r, rad, s.max(1.0), (color::BORDER_HI, 1.0), ((color::TRACK, 1.0), (color::TRACK, 1.0)));
    }
}

/// A name colour to pick: its colour (or the default's "A"), edged when chosen, locked when not
/// reached.
fn swatch(ui: &mut Ui, r: [f32; 4], rgb: Option<u32>, chosen: bool, locked_: bool, hover: bool, s: f32) {
    if chosen {
        theme::fill(ui.renderer, [r[0] - 3.0 * s, r[1] - 3.0 * s, r[2] + 6.0 * s, r[3] + 6.0 * s], r[3] / 2.0 + 3.0 * s, color::TEXT, 0.9);
    } else if hover {
        theme::fill(ui.renderer, [r[0] - 2.0 * s, r[1] - 2.0 * s, r[2] + 4.0 * s, r[3] + 4.0 * s], r[3] / 2.0 + 2.0 * s, color::TEXT, 0.35);
    }
    match rgb {
        Some(c) => theme::fill(ui.renderer, r, r[3] / 2.0, c, if locked_ { 0.25 } else { 1.0 }),
        None => {
            theme::fill(ui.renderer, r, r[3] / 2.0, color::CHIP_HI, 1.0);
            let size = (12.0 * s).round().max(9.0);
            let w = ui.measure("A", size, true);
            ui.label("A", r[0] + (r[2] - w) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, color::TEXT);
        }
    }
    if locked_ {
        locked(ui, r, s * 0.8);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use bsk_proto::{AchievementInfo, LoginDayInfo, QuestInfo, QuestPeriodInfo};

    fn quest(period: u8, progress: i32, goal: i32, claimed: bool) -> QuestInfo {
        QuestInfo { period, id: format!("q{period}{progress}"), title: "T".into(), text: "x".into(), progress, goal, claimed, rewards: vec![].into() }
    }

    fn data() -> QuestsData {
        QuestsData {
            now: 1_800_000_000,
            rank: 0,
            points: 0,
            color_rank: 255,
            ranks: vec![].into(),
            periods: vec![QuestPeriodInfo { period: 0, resets_at: 1_800_003_600, rerolls_left: 1, bonus_claimed: false, bonus: vec![QuestReward { kind: 0, item: 0, amount: 100 }].into() }].into(),
            quests: vec![quest(0, 400, 400, false), quest(0, 3, 40, false), quest(1, 3, 3, true)].into(),
            login_day: 2,
            login_ready: true,
            login_streak: 2,
            login_total: 2,
            login_next_at: 1_800_003_600,
            login_days: vec![LoginDayInfo { milestone: false, rewards: vec![].into() }; 28].into(),
            achievements: vec![AchievementInfo {
                id: "a".into(),
                category: "Combat".into(),
                title: "A".into(),
                text: "x".into(),
                claimed: 1,
                progress: 5000,
                goals: vec![500, 5000, 25000, 100000].into(),
                next_points: 20,
                next_rewards: vec![].into(),
            }]
            .into(),
            gifts: None,
        }
    }

    #[test]
    fn counts_what_waits_and_follows_progress() {
        let mut q = Quests::default();
        assert_eq!(q.ready(), 0);
        q.event(Event::Data(Box::new(data())), 0);
        // a finished daily, the login, an achievement tier
        assert_eq!((q.ready_in(Tab::Daily), q.ready_in(Tab::Weekly), q.ready_in(Tab::Login), q.ready_in(Tab::Achievements)), (1, 0, 1, 1));
        assert_eq!(q.ready(), 3);
        assert_eq!(q.best_tab(), Tab::Daily);
        // the second daily finishes
        q.event(Event::Progress(vec![QuestChange { kind: 0, index: 1, progress: 40 }]), 10);
        assert_eq!(q.ready_in(Tab::Daily), 2);
        // claiming both leaves the bonus
        let mut d = data();
        d.quests[0].claimed = true;
        d.quests[1].progress = 40;
        d.quests[1].claimed = true;
        q.event(Event::Data(Box::new(d)), 20);
        assert!(q.bonus_ready(0));
        assert_eq!(q.ready_in(Tab::Daily), 1);
        // the login toast comes once a day
        assert_eq!(q.toasts.iter().filter(|t| t.kind == NOTICE_LOGIN).count(), 1);
    }

    #[test]
    fn gifts_wait_on_the_daily_rewards_tab() {
        let gift = |claimed| bsk_proto::QuestGiftInfo {
            id: "lil_oryx".into(),
            title: "Lil' Oryx".into(),
            text: "x".into(),
            ends_at: 1_802_000_000,
            claimed,
            rewards: vec![QuestReward { kind: 1, item: 0xD16A, amount: 1 }].into(),
        };
        let mut q = Quests::default();
        let mut d = data();
        d.login_ready = false;
        d.gifts = Some(vec![gift(false)].into());
        q.event(Event::Data(Box::new(d.clone())), 0);
        assert_eq!(q.ready_in(Tab::Login), 1);
        assert_eq!(q.toasts.iter().filter(|t| t.kind == NOTICE_GIFT).count(), 1);
        // said once, however often the data comes
        q.event(Event::Data(Box::new(d.clone())), 10);
        assert_eq!(q.toasts.iter().filter(|t| t.kind == NOTICE_GIFT).count(), 1);
        assert_eq!(q.toast_tab(NOTICE_GIFT), Tab::Login);
        d.gifts = Some(vec![gift(true)].into());
        q.event(Event::Data(Box::new(d)), 20);
        assert_eq!(q.ready_in(Tab::Login), 0);
    }

    #[test]
    fn countdowns_and_tiers() {
        assert_eq!(countdown(5 * 3600 + 12 * 60 + 3), "5h 12m");
        assert_eq!(countdown(12 * 60 + 30), "12m 30s");
        assert_eq!(countdown(40), "40s");
        assert_eq!(countdown(-5), "0s");
        assert_eq!(countdown(3 * 86400 + 7200), "3d 2h");
        assert_eq!(earned(&[1, 10, 50, 200], 0), 0);
        assert_eq!(earned(&[1, 10, 50, 200], 10), 2);
        assert_eq!(earned(&[1, 10, 50, 200], 999), 4);
    }

    #[test]
    fn achievements_ready_first() {
        let mut d = data();
        let mut done = d.achievements[0].clone();
        done.id = "b".into();
        done.claimed = 4;
        done.progress = 200_000;
        let mut fresh = d.achievements[0].clone();
        fresh.id = "c".into();
        fresh.claimed = 0;
        fresh.progress = 10;
        d.achievements = vec![done, fresh, d.achievements[0].clone()].into();
        let mut q = Quests::default();
        q.event(Event::Data(Box::new(d)), 0);
        assert_eq!(q.achievement_order(), vec![2, 1, 0]);
        q.ready_only = true;
        assert_eq!(q.achievement_order(), vec![2]);
    }
}
