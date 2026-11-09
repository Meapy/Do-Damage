//! Community goals (the world server's `CommunityGoals.cs`): what the whole server does together
//! each week, such as defeating Oryx 15 times. Each goal has four tiers along the way; everyone
//! who helped enough claims each tier the server reaches, and the top contributors claim a prize
//! more once it ends. Past the goal come the Overdrive levels, each a bonus more; many rewards
//! are rolls of the goal's own loot, luckier at each level. The server sizes each run by how the
//! last went (a goal's `goal` against its `base_goal`). The Warcaller at the north point of the
//! Nexus spawn opens the window.
//!
//! The client asks for the goals once in each world (`CommunityRequest`), so it can say when a
//! reward waits, and every few seconds while the window is open, so the bars move as the realm
//! plays. `CommunityNotice`s pop up as banners at the top of the view: a tier the server reached,
//! a goal completed, the week's new goals, someone's rare roll. The server checks every claim
//! (`CommunityClaim`).

use crate::hud::{Layout, SlotKind, Ui};
use crate::i18n::{mark, tf, tn, tr};
use crate::quests::{card, check, countdown, done_chip, glow, inside, locked, reward_row, rewards_width, LOOT_ROLL, TIER_COLORS};
use crate::theme::{self, color, Kind};
use bsk_proto::{CommunityData, CommunityGoal, CommunityRoll};
use bsk_world::GameObj;
use std::collections::HashMap;
use winit::keyboard::KeyCode;

/// `CommunityClaim`'s tiers beyond the goal's own: the top's prize, all a goal has waiting, all
/// its Overdrive levels waiting, and the first level (the next are after it).
pub const TOP: u8 = 255;
pub const ALL: u8 = 254;
pub const BONUS_ALL: u8 = 253;
pub const BONUS: u8 = 100;

/// `CommunityNotice` kinds.
const CLAIMED: u8 = 0;
const REFUSED: u8 = 1;
const COMPLETE: u8 = 3;
const NEW_WEEK: u8 = 4;
const LOOT: u8 = 5;
/// The client's own banner for a reward waiting (the server sends none).
const WAITING: u8 = 200;

/// How often the open window asks again (ms).
const REFRESH_MS: i32 = 4000;
/// How long a banner shows, and how long it takes to come and go (ms).
const BANNER_MS: i32 = 7000;
const BANNER_FADE: i32 = 400;
const MAX_BANNERS: usize = 3;

/// The tiers' colours, rising like a fire: Spark, Flame, Blaze, Inferno.
const TIER_FIRE: [u32; 4] = [color::GOLD, color::ACCENT_HI, color::ACCENT, color::DANGER];

/// What the window asks of the server.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Act {
    Request,
    Claim(String, u8),
}

/// What the server said.
#[derive(Clone, Debug)]
pub enum Event {
    Data(Box<CommunityData>),
    Notice(u8, String, String),
    /// A claim's loot rolls, to play on the reel.
    Rolls(Vec<CommunityRoll>),
}

#[derive(Clone, Debug, PartialEq)]
enum Hit {
    Close,
    Goal(String),
    Tier(String, u8),
    All,
    /// On the reel: land the roll turning, or go on to the next.
    RollNext,
    /// Land every roll left at once.
    RollSkip,
}

/// How long a roll's reel turns (ms): the first longer, as the death spin's, the rest quicker
/// when a claim has many.
const ROLL_MS: f32 = 4200.0;
const ROLL_QUICK_MS: f32 = 2200.0;
/// How long a landed roll shows before the next starts turning (ms).
const ROLL_REST_MS: i32 = 1100;

/// A claim's loot rolls, each played on a reel as the death spin's: it scrolls past the goal's
/// loot and slows onto what was won (the server rolled it and paid it already; this only shows
/// it).
struct Reels {
    rolls: Vec<CommunityRoll>,
    /// The roll turning or last landed.
    at: usize,
    /// When it started turning, and when it landed.
    start: i32,
    landed: Option<i32>,
    /// The slot under the marker, for the tick as each passes.
    slot: i32,
    /// Land the one turning now.
    skip: bool,
    /// The claim's answer (what it paid), shown at the window's foot once every roll has landed.
    message: Option<(String, bool, i32)>,
}

impl Reels {
    fn turn_ms(&self) -> f32 {
        if self.at == 0 || self.rolls.len() <= 2 { ROLL_MS } else { ROLL_QUICK_MS }
    }

    /// Whether every roll has landed.
    fn done(&self) -> bool {
        self.landed.is_some() && self.at + 1 >= self.rolls.len()
    }

    /// The next roll, from `now`.
    fn next(&mut self, now: i32) {
        self.at += 1;
        self.start = now;
        self.landed = None;
        self.slot = 0;
        self.skip = false;
    }
}

struct Banner {
    kind: u8,
    title: String,
    text: String,
    since: i32,
}

fn tier_color(t: usize) -> u32 {
    TIER_FIRE[t.min(TIER_FIRE.len() - 1)]
}

/// Whether our share counts for the goal's rewards.
fn qualifies(g: &CommunityGoal) -> bool {
    g.mine >= g.minimum.max(1)
}

/// The tiers of a goal we can claim now, its Overdrive levels (`BONUS` on), and the top's prize
/// (`TOP`) if it's ours.
pub fn claimable(g: &CommunityGoal) -> Vec<u8> {
    if !qualifies(g) {
        return Vec::new();
    }
    let mut out: Vec<u8> = g.tiers.iter().enumerate().filter(|(_, t)| g.total >= t.at && !t.claimed).map(|(i, _)| i as u8).collect();
    out.extend(g.bonus.iter().enumerate().filter(|(_, b)| g.total >= b.at && !b.claimed).map(|(k, _)| BONUS + k as u8));
    if !g.running && g.rank > 0 && g.rank <= g.top_count as i32 && !g.top_claimed && !g.top_rewards.is_empty() {
        out.push(TOP);
    }
    out
}

/// Tiers the server has reached on a goal.
fn reached(g: &CommunityGoal) -> usize {
    g.tiers.iter().take_while(|t| g.total >= t.at).count()
}

/// Overdrive levels the server has reached on a goal.
fn bonus_reached(g: &CommunityGoal) -> usize {
    g.bonus.iter().take_while(|b| g.total >= b.at).count()
}

/// How far the realm has got, in percent of the goal (past 100 in Overdrive).
fn percent(g: &CommunityGoal) -> i64 {
    g.total as i64 * 100 / g.goal.max(1) as i64
}

/// How much more each Overdrive level asks, in percent of the goal.
fn bonus_step(g: &CommunityGoal) -> i64 {
    g.bonus.last().map_or(25, |b| ((b.at as f64 / g.goal.max(1) as f64 - 1.0) * 100.0 / g.bonus.len() as f64).round() as i64)
}

/// Where the realm is along the Overdrive track, 0 at the goal to 1 at the last level, each
/// level an equal part of it.
fn overdrive_k(g: &CommunityGoal, shown: f32) -> f32 {
    let n = g.bonus.len();
    if n == 0 || shown <= g.goal as f32 {
        return 0.0;
    }
    let mut prev = g.goal as f32;
    for (i, b) in g.bonus.iter().enumerate() {
        let at = b.at as f32;
        if shown < at {
            return (i as f32 + (shown - prev) / (at - prev).max(1.0)) / n as f32;
        }
        prev = at;
    }
    1.0
}

/// The run made harder or easier than the goal as written: its tag and colour.
fn sizing(g: &CommunityGoal) -> Option<(String, u32)> {
    if g.base_goal <= 0 || g.goal == g.base_goal {
        return None;
    }
    let pct = ((g.goal as f64 / g.base_goal as f64 - 1.0) * 100.0).round() as i64;
    match pct {
        0 => None,
        p if p > 0 => Some((tf!("Harder +{p}%", p), color::ACCENT_HI)),
        p => Some((tf!("Easier {p}%", p), color::GOOD)),
    }
}

/// A chance in ten-thousandths, as a player reads it: 12%, 2.5%, 0.4%.
fn chance_text(c: u16) -> String {
    let pct = c as f64 / 100.0;
    if pct >= 10.0 {
        format!("{}%", pct.round() as i64)
    } else if pct >= 0.1 {
        let t = format!("{pct:.1}");
        format!("{}%", t.strip_suffix(".0").unwrap_or(&t))
    } else {
        "<0.1%".to_owned()
    }
}

fn roman(n: usize) -> String {
    if n == 0 || n >= 40 {
        return n.to_string();
    }
    let mut n = n;
    let mut s = String::new();
    for (v, r) in [(10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")] {
        while n >= v {
            s.push_str(r);
            n -= v;
        }
    }
    s
}

/// The names the server gives a goal's tiers and its levels past the goal (`tierNames` and
/// `bonusName` in data/community.json), so they're said in the player's language.
const SERVER_NAMES: [&str; 5] = [mark("server::Spark"), mark("server::Flame"), mark("server::Blaze"), mark("server::Inferno"), mark("server::Overdrive")];

/// A name from the server in the player's language, when it's one we know.
fn named(name: &str) -> String {
    SERVER_NAMES.iter().find(|k| k.strip_prefix("server::") == Some(name)).map_or_else(|| name.to_owned(), |k| tr(k).to_owned())
}

/// A rarity as the server names it ("rare"), as the death spin says it ("Rare", translated).
fn rarity_name(name: &str) -> String {
    crate::screens::RARITIES.iter().find(|r| r.0.eq_ignore_ascii_case(name)).map_or_else(|| capital(name), |r| tr(r.0).to_owned())
}

fn capital(s: &str) -> String {
    let mut c = s.chars();
    c.next().map_or(String::new(), |f| f.to_uppercase().collect::<String>() + c.as_str())
}

/// A quiet chip in place of a button, saying why there's nothing to press.
fn note(ui: &mut Ui, r: [f32; 4], text: &str, rgb: u32, s: f32) {
    let size = (11.0 * s).round().max(9.0);
    let text = crate::npc::fit(ui, text, size, r[2] - 10.0 * s);
    let w = ui.measure(&text, size, true);
    theme::fill(ui.renderer, r, r[3] / 2.0, color::CHIP, 0.6);
    ui.label(&text, r[0] + (r[2] - w) / 2.0, r[1] + (r[3] - size * 1.22) / 2.0, size, true, rgb);
}

/// The community goals window, and its banners.
#[derive(Default)]
pub struct Community {
    pub open: bool,
    data: Option<CommunityData>,
    /// When (game ms) `data` came: the countdowns run on from its `now`.
    received: i32,
    /// The goal shown, by its run's key.
    selected: Option<String>,
    /// When we last asked, while open.
    asked: i32,
    /// The totals as the bars show them, easing to the real ones; and when the window last drew.
    shown: HashMap<String, f32>,
    drawn: i32,
    /// The server's last word on a claim: (text, is it an error, when).
    message: Option<(String, bool, i32)>,
    hits: Vec<(Hit, [f32; 4])>,
    rect: Option<[f32; 4]>,
    banners: Vec<Banner>,
    banner_hits: Vec<[f32; 4]>,
    /// The rewards we said were waiting ("<key>:<tier>").
    said: Vec<String>,
    hover_item: Option<i32>,
    /// A line for the foot about what the mouse is on (the loot's odds, the goal's size).
    hint: Option<String>,
    now: i32,
    /// The last claim's loot rolls, playing over the window.
    reels: Option<Reels>,
    /// Sounds to play (the reel's ticks and landings), taken by the game each frame.
    pub sounds: Vec<&'static str>,
}

impl Community {
    /// Opens the window (at the goal with a reward waiting, else the first) and asks for the
    /// goals.
    pub fn show(&mut self) -> Option<Act> {
        self.open = true;
        self.message = None;
        if let Some(d) = &self.data {
            let best = d.goals.iter().find(|g| !claimable(g).is_empty()).or(d.goals.first());
            self.selected = best.map(|g| g.key.clone());
        }
        self.asked = self.now;
        Some(Act::Request)
    }

    pub fn close(&mut self) {
        self.open = false;
        self.reels = None;
    }

    /// Forgets the goals (a new character or account): the next join asks again.
    pub fn reset(&mut self) {
        self.data = None;
        self.open = false;
        self.reels = None;
        self.banners.clear();
        self.shown.clear();
    }

    /// Rewards waiting to be claimed, on every goal.
    pub fn ready(&self) -> usize {
        self.data.as_ref().map_or(0, |d| d.goals.iter().map(|g| claimable(g).len()).sum())
    }

    /// The server's time now, in Unix seconds.
    fn server_now(&self) -> i64 {
        self.data.as_ref().map_or(0, |d| d.now as i64 + ((self.now - self.received).max(0) / 1000) as i64)
    }

    /// Asks again every few seconds while the window is open.
    pub fn tick(&mut self, now: i32) -> Option<Act> {
        self.now = now;
        if self.open && now - self.asked >= REFRESH_MS {
            self.asked = now;
            return Some(Act::Request);
        }
        None
    }

    pub fn event(&mut self, e: Event, now: i32) {
        self.now = now;
        match e {
            Event::Data(d) => {
                log::info!("community: {} goals, {} rewards waiting", d.goals.len(), d.goals.iter().map(|g| claimable(g).len()).sum::<usize>());
                self.received = now;
                // a reward that's newly ours, said once
                let fresh: Vec<(String, String)> = d
                    .goals
                    .iter()
                    .flat_map(|g| claimable(g).into_iter().map(move |t| (format!("{}:{t}", g.key), g.title.clone())))
                    .filter(|(id, _)| !self.said.contains(id))
                    .collect();
                if !fresh.is_empty() && !self.open {
                    let text = if fresh.len() == 1 {
                        tf!("\"{title}\": the realm did it, and you helped. Claim it from the Warcaller.", title = fresh[0].1)
                    } else {
                        tn!(fresh.len(), "{n} reward from the realm's goals is yours. Claim it from the Warcaller.|{n} rewards from the realm's goals are yours. Claim them from the Warcaller.")
                    };
                    self.banner(WAITING, tr("A community reward waits!").into(), text, now);
                }
                self.said.extend(fresh.into_iter().map(|(id, _)| id));
                if self.selected.as_ref().is_none_or(|k| !d.goals.iter().any(|g| &g.key == k)) {
                    self.selected = d.goals.iter().find(|g| !claimable(g).is_empty()).or(d.goals.first()).map(|g| g.key.clone());
                }
                self.data = Some(*d);
            }
            Event::Notice(kind, title, text) => match kind {
                CLAIMED => self.message = Some((if title.is_empty() { text } else { format!("{title} {text}") }, false, now)),
                REFUSED => self.message = Some((text, true, now)),
                _ => self.banner(kind, title, text, now),
            },
            Event::Rolls(rolls) => {
                let rolls: Vec<CommunityRoll> = rolls.into_iter().filter(|r| !r.reel.is_empty()).collect();
                if self.open && !rolls.is_empty() {
                    // the answer names what they landed on: it waits for the last
                    let message = self.message.take();
                    self.reels = Some(Reels { rolls, at: 0, start: now, landed: None, slot: 0, skip: false, message });
                }
            }
        }
    }

    /// Whether the reel still has rolls to land (a claim's chat lines wait for it).
    pub fn reeling(&self) -> bool {
        self.reels.as_ref().is_some_and(|r| !r.done())
    }

    /// The reel's next step: land the roll turning, else the next roll, else put it away.
    fn reel_next(&mut self) {
        let now = self.now;
        let Some(r) = &mut self.reels else { return };
        if r.landed.is_none() {
            r.skip = true;
        } else if r.done() {
            self.put_away();
        } else {
            r.next(now);
        }
    }

    /// Puts the reel away, with the claim's answer at the foot.
    fn put_away(&mut self) {
        if let Some(m) = self.reels.take().and_then(|r| r.message) {
            self.message = Some(m);
        }
    }

    /// Lands every roll left, or puts the reel away once they all have.
    fn reel_skip(&mut self) {
        let Some(r) = &mut self.reels else { return };
        if r.done() {
            self.put_away();
            return;
        }
        r.at = r.rolls.len() - 1;
        r.skip = true;
        r.landed = None;
    }

    fn banner(&mut self, kind: u8, title: String, text: String, now: i32) {
        self.banners.push(Banner { kind, title, text, since: now });
        if self.banners.len() > MAX_BANNERS {
            self.banners.remove(0);
        }
    }

    /// A click on a banner: opens the window. `None` if it wasn't on one.
    pub fn banner_click(&mut self, pos: (f64, f64)) -> Option<Option<Act>> {
        if !self.banner_hits.iter().any(|r| inside(*r, pos)) {
            return None;
        }
        self.banners.clear();
        self.banner_hits.clear();
        Some(self.show())
    }

    /// Whether a point is on a banner (so a click there doesn't shoot).
    pub fn on_banner(&self, pos: (f64, f64)) -> bool {
        self.banner_hits.iter().any(|r| inside(*r, pos))
    }

    pub fn click(&mut self, pos: (f64, f64)) -> Option<Act> {
        if self.rect.is_some_and(|r| !inside(r, pos)) {
            self.close();
            return None;
        }
        // the last drawn first: the reel's buttons over the window's
        let hit = self.hits.iter().rev().find(|(_, r)| inside(*r, pos)).map(|(h, _)| h.clone());
        // the reel takes every click on the window while it shows
        if self.reels.is_some() {
            match hit {
                Some(Hit::Close) => self.close(),
                Some(Hit::RollSkip) => self.reel_skip(),
                _ => self.reel_next(),
            }
            return None;
        }
        match hit? {
            Hit::RollNext | Hit::RollSkip => {}
            Hit::Close => self.close(),
            Hit::Goal(key) => {
                self.selected = Some(key);
                self.message = None;
            }
            Hit::Tier(key, tier) => return Some(Act::Claim(key, tier)),
            Hit::All => return Some(Act::Claim(String::new(), ALL)),
        }
        None
    }

    /// A key while the window is open: Escape closes it, the arrows pick a goal.
    pub fn key(&mut self, code: KeyCode) {
        // on the reel: Escape lands them all (again: puts it away), Space or Enter steps on
        if self.reels.is_some() {
            match code {
                KeyCode::Escape => self.reel_skip(),
                KeyCode::Space | KeyCode::Enter | KeyCode::NumpadEnter => self.reel_next(),
                _ => {}
            }
            return;
        }
        let Some(d) = &self.data else {
            if code == KeyCode::Escape {
                self.close();
            }
            return;
        };
        let at = d.goals.iter().position(|g| Some(&g.key) == self.selected.as_ref()).unwrap_or(0);
        let n = d.goals.len().max(1);
        match code {
            KeyCode::Escape => self.close(),
            KeyCode::ArrowUp => self.selected = d.goals.get((at + n - 1) % n).map(|g| g.key.clone()),
            KeyCode::ArrowDown => self.selected = d.goals.get((at + 1) % n).map(|g| g.key.clone()),
            _ => {}
        }
    }

    /// The total a goal's bar shows, easing towards the real one (from 0 the first time, so the
    /// bar fills as the window opens).
    fn shown_total(&mut self, g: &CommunityGoal, dt: f32) -> f32 {
        let v = self.shown.entry(g.key.clone()).or_insert(0.0);
        let target = g.total as f32;
        *v += (target - *v) * (1.0 - (-dt * 4.5).exp());
        if (target - *v).abs() < 0.05 {
            *v = target;
        }
        *v
    }

    pub fn draw(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        // under the reel the window doesn't see the mouse
        let under = if self.reels.is_some() { (-1.0e6, -1.0e6) } else { mouse };
        self.draw_window(ui, lay, me, under, now);
        if self.open && self.reels.is_some() {
            self.draw_reels(ui, lay, me, mouse, now);
        }
    }

    /// The last claim's loot rolls, over the window: each reel scrolls past the goal's loot and
    /// slows onto what was won, then the next starts; what's been won so far lines up below.
    fn draw_reels(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        let Some(wr) = self.rect else { return };
        let rarities: Vec<String> = self.data.as_ref().map_or_else(Vec::new, |d| d.rarities.to_vec());
        let Some(reels) = &mut self.reels else { return };
        if reels.landed.is_some_and(|t| now - t >= ROLL_REST_MS) && !reels.done() {
            reels.next(now);
        }
        let n = reels.rolls.len();
        let roll = reels.rolls[reels.at].clone();
        let u = if reels.skip { 1.0 } else { ((now - reels.start) as f32 / reels.turn_ms()).clamp(0.0, 1.0) };
        let stop = (roll.stop as usize).min(roll.reel.len() - 1) as f32;
        let from = stop.min(2.0);
        // fast, then a long slow-down onto the prize
        let pos = from + (stop - from) * (1.0 - (1.0 - u).powi(4));
        let landed = u >= 1.0;
        let slot = (pos + 0.5).floor() as i32;
        if slot != reels.slot && !landed {
            reels.slot = slot;
            self.sounds.push("inventory_move_item");
        }
        if landed && reels.landed.is_none() {
            reels.landed = Some(now);
            self.sounds.push(if roll.rarity >= 3 { "level_up" } else { "loot_appears" });
        }
        let (at, done) = (reels.at, reels.done());
        if done {
            if let Some(m) = reels.message.take() {
                self.message = Some(m);
            }
        }
        // the rolls landed so far, the one turning too once it has
        let won: Vec<CommunityRoll> = reels.rolls.iter().take(if landed { at + 1 } else { at }).cloned().collect();

        let s = (wr[2] / 1000.0).max(0.1);
        let font = |size: f32| (size * s).round().max(9.0);
        let pulse = 0.5 + 0.5 * (now as f32 / 260.0).sin();
        // the window dims behind it
        theme::fill(ui.renderer, wr, 14.0 * s, color::BACKGROUND, 0.72);
        let (pw, ph) = (660.0 * s, 430.0 * s);
        let r = [wr[0] + (wr[2] - pw) / 2.0, wr[1] + (wr[3] - ph) / 2.0, pw, ph];
        theme::shadow(ui.renderer, r, 12.0 * s, 16.0 * s, 0.5);
        theme::panel(ui.renderer, r, s);
        let pad = 26.0 * s;
        let title = if n > 1 { tf!("Loot roll {at} of {n}", at = at + 1, n) } else { tr("Loot roll").to_owned() };
        theme::star(ui.renderer, r[0] + pad + 10.0 * s, r[1] + 33.0 * s, 11.0 * s, color::GOLD);
        ui.label(&title, r[0] + pad + 28.0 * s, r[1] + 18.0 * s, font(26.0), true, color::GOLD);
        let line = tr("The Warcaller rolls the goal's loot for you.");
        let line = crate::npc::fit(ui, line, font(13.0), r[2] - 2.0 * pad);
        ui.label(&line, r[0] + pad, r[1] + 58.0 * s, font(13.0), false, color::TEXT_DIM);

        // The reel: slots fade towards the ends, the marker in the middle.
        let track = [r[0] + pad, r[1] + 88.0 * s, r[2] - 2.0 * pad, 108.0 * s];
        theme::rounded(ui.renderer, track, 12.0 * s, (color::INSET_TOP, 1.0), (color::INSET_BOTTOM, 1.0));
        let side = 78.0 * s;
        let step = side + 12.0 * s;
        let (cx, cy) = (track[0] + track[2] / 2.0, track[1] + track[3] / 2.0);
        let half = track[2] / 2.0 - side / 2.0 - 8.0 * s;
        let mut hovered = None;
        for (i, slot) in roll.reel.iter().enumerate() {
            let dx = (i as f32 - pos) * step;
            if dx.abs() > half {
                continue;
            }
            let k = dx.abs() / half;
            let prize = landed && i == stop as usize;
            let alpha = if landed && !prize { 0.45 * (1.0 - k * k) } else { 1.0 - k * k * 0.85 };
            let rc = crate::screens::rarity_color(slot.rarity as i32);
            let sr = [cx + dx - side / 2.0, cy - side / 2.0, side, side];
            if prize {
                let g = 6.0 * s + 4.0 * s * pulse;
                theme::fill(ui.renderer, [sr[0] - g, sr[1] - g, side + 2.0 * g, side + 2.0 * g], 12.0 * s + g, rc, 0.22 + 0.18 * pulse);
            }
            theme::rounded(ui.renderer, sr, 9.0 * s, (theme::shade(rc, -0.62), alpha), (color::SLOT_BOTTOM, alpha));
            theme::fill(ui.renderer, [sr[0] + 8.0 * s, sr[1] + side - 7.0 * s, side - 16.0 * s, 3.0 * s], 1.5 * s, rc, alpha);
            ui.item_icon_faded(slot.item as i32, 80, sr[0] + side / 2.0, sr[1] + side / 2.0 - 3.0 * s, s * 1.3, alpha);
            if alpha > 0.4 && inside(sr, mouse) {
                hovered = Some(slot.item as i32);
            }
        }
        for y in [track[1] - 9.0 * s, track[1] + track[3] - 9.0 * s] {
            theme::fill(ui.renderer, [cx - 2.0 * s, y, 4.0 * s, 18.0 * s], 2.0 * s, color::GOLD, 0.95);
        }

        // What it landed on.
        let y = track[1] + track[3] + 20.0 * s;
        if landed {
            let rc = crate::screens::rarity_color(roll.rarity as i32);
            if roll.rarity >= 3 {
                // stars turning round the prize
                for j in 0..6 {
                    let a = j as f32 / 6.0 * std::f32::consts::TAU + now as f32 / 900.0;
                    let rad = side * 0.8;
                    let twinkle = 3.5 * s + 2.5 * s * (now as f32 / 180.0 + j as f32).sin().abs();
                    theme::star(ui.renderer, cx + rad * a.cos(), cy + rad * a.sin(), twinkle, rc);
                }
            }
            let name = rarities.get(roll.rarity as usize).map_or_else(
                || crate::screens::RARITIES.get(roll.rarity as usize).map_or_else(String::new, |r| tr(r.0).to_owned()),
                |n| rarity_name(n),
            );
            let head = tf!("{rarity}!", rarity = name.to_uppercase());
            let big = font(22.0);
            let hw = ui.measure(&head, big, true);
            ui.label(&head, cx - hw / 2.0, y, big, true, rc);
            let item = ui.data.objects.get(&roll.item).map_or_else(|| tr("a prize").to_owned(), |d| d.display_id.clone());
            let item = if roll.count > 1 { format!("{item} x{}", roll.count) } else { item };
            let mid = font(17.0);
            let iw = ui.measure(&item, mid, true);
            ui.label(&item, cx - iw / 2.0, y + big * 1.45, mid, true, color::TEXT);
        }

        // What's been won so far, in a row.
        let row_y = r[1] + ph - 118.0 * s;
        if n > 1 {
            let cell = 34.0 * s;
            let gap = 6.0 * s;
            let fit = (((r[2] - 2.0 * pad + gap) / (cell + gap)).floor() as usize).max(1);
            let shown = won.len().min(fit);
            let total_w = n.min(fit) as f32 * (cell + gap) - gap;
            let x0 = r[0] + (r[2] - total_w) / 2.0;
            for i in 0..n.min(fit) {
                let sr = [x0 + i as f32 * (cell + gap), row_y, cell, cell];
                if i < shown {
                    let w = &won[won.len() - shown + i];
                    let rc = crate::screens::rarity_color(w.rarity as i32);
                    let over = inside(sr, mouse);
                    ui.slot_box(sr, Some(rc), over, s);
                    ui.item_icon(w.item as i32, 60, sr[0] + cell / 2.0, sr[1] + cell / 2.0, s);
                    if w.count > 1 {
                        let cs = font(10.0);
                        let t = w.count.to_string();
                        let tw = ui.measure(&t, cs, true);
                        ui.label(&t, sr[0] + cell - tw - 2.0 * s, sr[1] + cell - cs * 1.2, cs, true, color::TEXT);
                    }
                    if over {
                        hovered = Some(w.item as i32);
                    }
                } else {
                    // a roll still to come
                    theme::fill(ui.renderer, sr, 6.0 * s, color::CHIP, 0.5);
                }
            }
        }

        // The buttons: on to the next roll (or land this one, or put it away), and land them all.
        let by = r[1] + ph - 64.0 * s;
        let bw = 170.0 * s;
        let next = if !landed {
            tr("Stop")
        } else if done {
            tr("Done")
        } else {
            tr("Next roll")
        };
        let two = n > 1 && !done;
        let nb = if two { [r[0] + r[2] / 2.0 + 6.0 * s, by, bw, 38.0 * s] } else { [r[0] + (r[2] - bw) / 2.0, by, bw, 38.0 * s] };
        if done {
            glow(ui, nb, pulse, s);
        }
        ui.button(nb, next, Kind::Primary, inside(nb, mouse), s);
        self.hits.push((Hit::RollNext, nb));
        if two {
            let sb = [r[0] + r[2] / 2.0 - 6.0 * s - bw, by, bw, 38.0 * s];
            ui.button(sb, tr("Skip all"), Kind::Secondary, inside(sb, mouse), s);
            self.hits.push((Hit::RollSkip, sb));
        }
        let note = tr("Items go to the gift chest in your vault.");
        let nw = ui.measure(note, font(11.0), false);
        ui.label(note, r[0] + (r[2] - nw) / 2.0, r[1] + ph - 20.0 * s, font(11.0), false, color::TEXT_MUTED);

        if let Some(item) = hovered {
            let lines = crate::hud::item_tooltip(ui.data, item, me, SlotKind::Shop);
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }

    fn draw_window(&mut self, ui: &mut Ui, lay: &Layout, me: Option<&GameObj>, mouse: (f64, f64), now: i32) {
        self.now = now;
        self.hits.clear();
        self.hover_item = None;
        self.hint = None;
        let dt = ((now - self.drawn).clamp(0, 200) as f32) / 1000.0;
        self.drawn = now;
        if !self.open {
            self.rect = None;
            return;
        }
        let (dw, dh) = (1000.0, 680.0);
        let s = lay.s * (lay.view_w() * 0.96 / (dw * lay.s)).min(lay.h * 0.96 / (dh * lay.s)).min(1.0);
        let r = [((lay.view_w() - dw * s) / 2.0).round(), ((lay.h - dh * s) / 2.0).round(), dw * s, dh * s];
        self.rect = Some(r);
        theme::panel(ui.renderer, r, s);
        let font = |size: f32| (size * s).round().max(9.0);
        let pulse = 0.5 + 0.5 * ((now as f32) / 260.0).sin();

        // The header: the name, what it's about, when the week's goals end, the close button.
        // An ember glow along its top, as if the banner's lit by torches.
        theme::rounded(ui.renderer, [r[0] + 2.0 * s, r[1] + 2.0 * s, r[2] - 4.0 * s, 70.0 * s], 12.0 * s, (color::ACCENT, 0.16), (color::ACCENT, 0.0));
        theme::star(ui.renderer, r[0] + 34.0 * s, r[1] + 36.0 * s, 13.0 * s, color::GOLD);
        let ts = font(24.0);
        ui.label(tr("Community Goals"), r[0] + 56.0 * s, r[1] + 16.0 * s, ts, true, color::GOLD);
        let about = tr("The whole realm, one goal. Everyone who helps shares every tier it reaches, and every level past it.");
        let about = crate::npc::fit(ui, about, font(12.0), r[2] - 56.0 * s - 60.0 * s);
        ui.label(&about, r[0] + 56.0 * s, r[1] + 46.0 * s, font(12.0), false, color::TEXT_DIM);
        let cb = [r[0] + r[2] - 44.0 * s, r[1] + 20.0 * s, 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        theme::cross(ui.renderer, cb[0] + cb[2] / 2.0, cb[1] + cb[3] / 2.0, 6.0 * s, (2.0 * s).max(1.0), color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));
        if let Some(d) = &self.data {
            let t = tf!("New goals in {time}", time = countdown(d.week_ends_at as i64 - self.server_now()));
            let tw = ui.measure(&t, font(13.0), true);
            ui.label(&t, cb[0] - 16.0 * s - tw, r[1] + 26.0 * s, font(13.0), true, color::TEXT_DIM);
        }

        let body = [r[0] + 22.0 * s, r[1] + 84.0 * s, r[2] - 44.0 * s, r[3] - 84.0 * s - 56.0 * s];
        let Some(d) = self.data.clone() else {
            ui.label(tr("Asking the Warcaller how the realm fares..."), body[0], body[1] + 10.0 * s, font(14.0), false, color::TEXT_MUTED);
            return;
        };
        if d.goals.is_empty() {
            ui.label(tr("The realm rests: no goals right now. Come back when the horn sounds again."), body[0], body[1] + 10.0 * s, font(14.0), false, color::TEXT_MUTED);
            return;
        }

        // The list: this week's goals (and specials), then those ended whose rewards wait.
        let lw = 250.0 * s;
        let mut y = body[1];
        let card_h = 60.0 * s;
        let mut section = None;
        for g in d.goals.iter() {
            if section != Some(g.running) {
                section = Some(g.running);
                let label = if g.running { tr("This week") } else { tr("Ended: claim before they go") };
                ui.label(label, body[0] + 2.0 * s, y, font(11.0), true, color::TEXT_MUTED);
                y += 20.0 * s;
            }
            if y + card_h > body[1] + body[3] {
                break;
            }
            let cr = [body[0], y, lw, card_h];
            let on = self.selected.as_ref() == Some(&g.key);
            let ready = claimable(g).len();
            let over = inside(cr, mouse);
            if on {
                theme::frame(ui.renderer, cr, 8.0 * s, (1.5 * s).max(1.0), (color::ACCENT, 1.0), ((color::WARM_TOP, 0.95), (color::WARM_BOTTOM, 0.95)));
            } else {
                card(ui, cr, ready > 0, !g.running, pulse, s);
                if over {
                    theme::fill(ui.renderer, cr, 8.0 * s, 0xFFFFFF, 0.04);
                }
            }
            crate::hud::object_icon(ui, g.icon, cr[0] + 28.0 * s, cr[1] + card_h / 2.0, 36.0 * s);
            let tx = cr[0] + 54.0 * s;
            let title = crate::npc::fit(ui, &g.title, font(13.0), lw - 54.0 * s - if ready > 0 { 30.0 } else { 10.0 } * s);
            ui.label(&title, tx, cr[1] + 10.0 * s, font(13.0), true, if g.running { color::TEXT } else { color::TEXT_DIM });
            let bar = [tx, cr[1] + 36.0 * s, lw - 54.0 * s - 52.0 * s, 8.0 * s];
            let shown = self.shown_total(g, dt);
            mini_bar(ui, bar, shown, g, s);
            let pct = format!("{}%", percent(g));
            let pc = if bonus_reached(g) > 0 { color::GOLD } else if g.total >= g.goal { color::GOOD } else { color::TEXT_DIM };
            ui.label(&pct, bar[0] + bar[2] + 8.0 * s, bar[1] - 4.0 * s, font(11.0), true, pc);
            if ready > 0 {
                let rd = 18.0 * s;
                let br = [cr[0] + lw - rd - 8.0 * s, cr[1] + 8.0 * s, rd, rd];
                theme::fill(ui.renderer, [br[0] - pulse * 2.0 * s, br[1] - pulse * 2.0 * s, br[2] + pulse * 4.0 * s, br[3] + pulse * 4.0 * s], rd, color::GOLD, 0.3);
                theme::fill(ui.renderer, br, rd / 2.0, color::GOLD, 1.0);
                let t = ready.to_string();
                let tsz = font(11.0);
                let tw = ui.measure(&t, tsz, true);
                ui.label(&t, br[0] + (rd - tw) / 2.0, br[1] + (rd - tsz * 1.22) / 2.0, tsz, true, color::ACCENT_TEXT);
            }
            self.hits.push((Hit::Goal(g.key.clone()), cr));
            y += card_h + 7.0 * s;
        }

        // The goal chosen.
        let Some(g) = d.goals.iter().find(|g| self.selected.as_ref() == Some(&g.key)).or(d.goals.first()).cloned() else { return };
        let px = body[0] + lw + 18.0 * s;
        let panel = [px, body[1], body[0] + body[2] - px, body[3]];
        self.draw_goal(ui, panel, &g, &d, s, mouse, now, dt);

        // The foot: the last word on a claim, or what rewards are; and Claim all.
        let foot_y = r[1] + r[3] - 40.0 * s;
        let all = self.ready();
        let bw = 150.0 * s;
        let ab = [r[0] + r[2] - 22.0 * s - bw, foot_y - 6.0 * s, bw, 34.0 * s];
        if all > 0 {
            glow(ui, ab, pulse, s);
            ui.button(ab, &tf!("Claim all ({all})", all), Kind::Primary, inside(ab, mouse), s);
            self.hits.push((Hit::All, ab));
        }
        let room = ab[0] - 16.0 * s - (r[0] + 22.0 * s);
        if self.hover_item == Some(LOOT_ROLL) {
            self.hint = Some(tr("A roll of this goal's loot (Loot rolls, below): luckier at each Overdrive level, luckiest for the top.").into());
        }
        if let Some(h) = &self.hint {
            let h = crate::npc::fit(ui, h, font(12.0), room);
            ui.label(&h, r[0] + 22.0 * s, foot_y, font(12.0), true, color::GOLD);
        } else if let Some((m, err, _)) = &self.message {
            let m = crate::npc::fit(ui, m, font(13.0), room);
            ui.label(&m, r[0] + 22.0 * s, foot_y, font(13.0), true, if *err { color::DANGER } else { color::GOOD });
        } else {
            let hint = tr("Rewards: fame to your account, items to the gift chest in your vault.");
            ui.label(hint, r[0] + 22.0 * s, foot_y, font(12.0), false, color::TEXT_MUTED);
        }
        if let Some(item) = self.hover_item.filter(|&i| i >= 0) {
            let lines = crate::hud::item_tooltip(ui.data, item, me, SlotKind::Shop);
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn draw_goal(&mut self, ui: &mut Ui, p: [f32; 4], g: &CommunityGoal, d: &CommunityData, s: f32, mouse: (f64, f64), now: i32, dt: f32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let pulse = 0.5 + 0.5 * ((now as f32) / 260.0).sin();
        let done = g.total >= g.goal;
        let levels = bonus_reached(g);
        let bonus_name = named(if d.bonus_name.is_empty() { "Overdrive" } else { d.bonus_name.as_str() });
        let bonus_name = bonus_name.as_str();
        theme::frame(ui.renderer, p, 10.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::INSET_TOP, 0.95), (color::INSET_BOTTOM, 0.95)));

        // The hero: the goal's picture in a glow, what it is, and the count.
        let (ix, iy) = (p[0] + 52.0 * s, p[1] + 50.0 * s);
        let halo = 34.0 * s + pulse * 3.0 * s;
        let glow_c = if levels > 0 { color::GOLD } else if done { color::GOOD } else { color::ACCENT };
        theme::fill(ui.renderer, [ix - halo, iy - halo, halo * 2.0, halo * 2.0], halo, glow_c, 0.10 + 0.06 * pulse + 0.02 * levels as f32);
        theme::fill(ui.renderer, [ix - 28.0 * s, iy - 28.0 * s, 56.0 * s, 56.0 * s], 28.0 * s, color::TRACK, 0.8);
        crate::hud::object_icon(ui, g.icon, ix, iy, 64.0 * s);
        let tx = p[0] + 100.0 * s;
        // the count, at the right
        let big = font(30.0);
        let total = crate::hud::group_digits(g.total);
        let of = format!(" / {}", crate::hud::group_digits(g.goal));
        let ow = ui.measure(&of, font(15.0), true);
        let bw = ui.measure(&total, big, true);
        let right = p[0] + p[2] - 20.0 * s;
        ui.label(&total, right - ow - bw, p[1] + 12.0 * s, big, true, if levels > 0 { color::GOLD } else if done { color::GOOD } else { color::GOLD });
        ui.label(&of, right - ow, p[1] + 25.0 * s, font(15.0), true, color::TEXT_DIM);
        let helping = tn!(g.participants, "{n} helping|{n} helping", n = crate::hud::group_digits(g.participants));
        let helping = format!("{helping} · {}%", percent(g));
        let hw = ui.measure(&helping, font(12.0), false);
        ui.label(&helping, right - hw, p[1] + 52.0 * s, font(12.0), false, color::TEXT_MUTED);
        // the title, its tags, the text, the time
        let room = right - ow - bw - 16.0 * s - tx;
        let title = crate::npc::fit(ui, &g.title, font(20.0), room);
        ui.label(&title, tx, p[1] + 14.0 * s, font(20.0), true, color::TEXT);
        let mut tag_x = tx;
        let tag_y = p[1] + 44.0 * s;
        let level_tag = format!("{bonus_name} {}", roman(levels));
        let sized = sizing(g);
        let mut tags: Vec<(String, u32, bool)> = Vec::new();
        if g.special {
            tags.push((tr("Special").into(), color::ACCENT, false));
        }
        if !g.running {
            tags.push((tr("Ended").into(), color::TEXT_MUTED, false));
        }
        if levels > 0 {
            tags.push((level_tag, color::GOLD, false));
        } else if done {
            tags.push((tr("Complete").into(), color::GOOD, false));
        }
        if let Some((t, c)) = &sized {
            tags.push((t.clone(), *c, true));
        }
        for (t, c, sizing_tag) in tags {
            let w = ui.measure(&t, font(10.5), true) + 14.0 * s;
            let tr = [tag_x, tag_y, w, 18.0 * s];
            theme::fill(ui.renderer, tr, 9.0 * s, c, 0.22);
            ui.label(&t, tr[0] + 7.0 * s, tr[1] + (tr[3] - font(10.5) * 1.22) / 2.0, font(10.5), true, c);
            if sizing_tag && inside(tr, mouse) {
                self.hint = Some(if g.goal > g.base_goal {
                    tf!("The realm sailed past this goal last time, so it asks more: {goal} (it was {was}).", goal = crate::hud::group_digits(g.goal), was = crate::hud::group_digits(g.base_goal))
                } else {
                    tf!("This goal was a struggle last time, so it asks less: {goal} (it was {was}).", goal = crate::hud::group_digits(g.goal), was = crate::hud::group_digits(g.base_goal))
                });
            }
            tag_x += w + 6.0 * s;
        }
        let when = if g.running {
            tf!("Ends in {time}", time = countdown(g.ends_at as i64 - self.server_now()))
        } else {
            tr("Its rewards can still be claimed for a while").to_owned()
        };
        let when = crate::npc::fit(ui, &when, font(12.0), (right - tag_x).max(10.0 * s));
        ui.label(&when, tag_x, tag_y + 2.0 * s, font(12.0), true, color::TEXT_DIM);
        let text = crate::npc::fit(ui, &g.text, font(13.0), p[0] + p[2] - 20.0 * s - tx);
        ui.label(&text, tx, p[1] + 70.0 * s, font(13.0), false, color::TEXT_DIM);

        // The bar, with each tier's marker on it, and the Overdrive track past the goal.
        let shown = self.shown_total(g, dt);
        let full = [p[0] + 24.0 * s, p[1] + 112.0 * s, p[2] - 48.0 * s, 24.0 * s];
        let od_w = if g.bonus.is_empty() { 0.0 } else { (full[2] * 0.27).round() };
        let bar = [full[0], full[1], full[2] - if od_w > 0.0 { od_w + 12.0 * s } else { 0.0 }, full[3]];
        big_bar(ui, bar, shown, g, now, s);
        for (t, tier) in g.tiers.iter().enumerate() {
            let k = (tier.at as f32 / g.goal.max(1) as f32).clamp(0.0, 1.0);
            let mx = bar[0] + bar[2] * k;
            let on = shown >= tier.at as f32;
            let c = tier_color(t);
            // a tick through the bar, and a gem above it
            ui.rect(mx - (1.0 * s).max(1.0), bar[1], (2.0 * s).max(1.0), bar[3], if on { color::TEXT } else { color::BORDER_HI }, if on { 0.5 } else { 0.9 });
            gem(ui, mx, bar[1] - 9.0 * s, 7.0 * s, c, on, pulse, s);
            let at = crate::hud::group_digits(tier.at);
            let aw = ui.measure(&at, font(10.5), true);
            let ax = (mx - aw / 2.0).min(bar[0] + bar[2] - aw).max(bar[0]);
            ui.label(&at, ax, bar[1] + bar[3] + 4.0 * s, font(10.5), true, if on { c } else { color::TEXT_MUTED });
        }
        if od_w > 0.0 {
            let od = [bar[0] + bar[2] + 12.0 * s, full[1], od_w, full[3]];
            overdrive_bar(ui, od, shown, g, now, s);
            let n = g.bonus.len();
            for (k, b) in g.bonus.iter().enumerate() {
                let mx = od[0] + od[2] * (k + 1) as f32 / n as f32;
                let on = shown >= b.at as f32;
                if k + 1 < n {
                    ui.rect(mx - 0.5 * s, od[1] + 3.0 * s, (1.0 * s).max(1.0), od[3] - 6.0 * s, color::BACKGROUND, if on { 0.35 } else { 0.7 });
                }
                gem(ui, mx - od[2] / n as f32 / 2.0, od[1] - 7.0 * s, 4.0 * s, color::GOLD, on, pulse, s);
            }
            let label = format!("{} +{}%", bonus_name.to_uppercase(), bonus_step(g));
            let lw = ui.measure(&label, font(9.5), true);
            ui.label(&label, od[0], od[1] + od[3] + 4.0 * s, font(9.5), true, if levels > 0 { color::GOLD } else { color::TEXT_MUTED });
            if let Some(last) = g.bonus.last() {
                let end = format!("{}%", (last.at as i64 * 100 / g.goal.max(1) as i64));
                let ew = ui.measure(&end, font(9.5), true);
                if od[2] - lw - ew > 6.0 * s {
                    ui.label(&end, od[0] + od[2] - ew, od[1] + od[3] + 4.0 * s, font(9.5), true, color::TEXT_MUTED);
                }
            }
        }

        // The tiers, and the Overdrive card: what each gives, and claiming it.
        let mine_ok = qualifies(g);
        let ty = full[1] + full[3] + 26.0 * s;
        let gap = 10.0 * s;
        let cols = g.tiers.len() + usize::from(!g.bonus.is_empty());
        let cw = (full[2] - gap * (cols.max(1) - 1) as f32) / cols.max(1) as f32;
        let ch = 122.0 * s;
        for (t, tier) in g.tiers.iter().enumerate() {
            let cr = [full[0] + t as f32 * (cw + gap), ty, cw, ch];
            let got = g.total >= tier.at;
            let ready = got && !tier.claimed && mine_ok;
            card(ui, cr, ready, tier.claimed, pulse, s);
            let c = tier_color(t);
            // a band of the tier's colour along the top
            theme::rounded(ui.renderer, [cr[0] + 1.0 * s, cr[1] + 1.0 * s, cr[2] - 2.0 * s, 26.0 * s], 7.0 * s, (c, if got { 0.32 } else { 0.10 }), (c, 0.0));
            let name = d.tier_names.get(t).map(|n| named(n)).unwrap_or_else(|| tf!("Tier {n}", n = t + 1));
            ui.label(&name, cr[0] + 10.0 * s, cr[1] + 7.0 * s, font(13.0), true, if got { c } else { color::TEXT_DIM });
            let pct = format!("{}%", ((tier.at as f32 / g.goal.max(1) as f32) * 100.0).round() as i32);
            let pw = ui.measure(&pct, font(10.5), true);
            ui.label(&pct, cr[0] + cw - pw - 10.0 * s, cr[1] + 9.0 * s, font(10.5), true, color::TEXT_MUTED);
            self.card_rewards(ui, cr, &tier.rewards, s, mouse, tier.claimed);
            let bb = [cr[0] + 10.0 * s, cr[1] + ch - 38.0 * s, cw - 20.0 * s, 28.0 * s];
            if tier.claimed {
                done_chip(ui, bb, tr("Claimed"), s * 0.9);
            } else if ready {
                glow(ui, bb, pulse, s);
                ui.button(bb, tr("Claim"), Kind::Primary, inside(bb, mouse), s * 0.9);
                self.hits.push((Hit::Tier(g.key.clone(), t as u8), bb));
            } else if got {
                // reached, but we haven't helped enough
                note(ui, bb, tr("Help to claim"), color::TEXT_DIM, s);
            } else {
                let left = tf!("{n} to go", n = crate::hud::group_digits(tier.at - g.total));
                let lw = ui.measure(&left, font(11.0), true);
                locked(ui, [bb[0], bb[1], 26.0 * s, bb[3]], s * 0.8);
                ui.label(&left, bb[0] + (bb[2] - lw) / 2.0 + 8.0 * s, bb[1] + (bb[3] - font(11.0) * 1.22) / 2.0, font(11.0), true, color::TEXT_MUTED);
            }
        }
        if let Some(first) = g.bonus.first() {
            let cr = [full[0] + g.tiers.len() as f32 * (cw + gap), ty, cw, ch];
            let waiting = if mine_ok { g.bonus.iter().filter(|b| g.total >= b.at && !b.claimed).count() } else { 0 };
            let all_claimed = levels > 0 && g.bonus.iter().take(levels).all(|b| b.claimed);
            card(ui, cr, waiting > 0, all_claimed && levels == g.bonus.len(), pulse, s);
            // a band of shimmering gold, brighter for each level reached
            let shimmer = 0.5 + 0.5 * ((now as f32) / 420.0).sin();
            let lit = if levels > 0 { 0.30 + 0.10 * shimmer } else { 0.10 };
            theme::rounded(ui.renderer, [cr[0] + 1.0 * s, cr[1] + 1.0 * s, cr[2] - 2.0 * s, 26.0 * s], 7.0 * s, (color::GOLD, lit), (0xB86BFF, 0.0));
            ui.label(bonus_name, cr[0] + 10.0 * s, cr[1] + 7.0 * s, font(13.0), true, if levels > 0 { color::GOLD } else { color::TEXT_DIM });
            let count = format!("{}/{}", levels, g.bonus.len());
            let cwid = ui.measure(&count, font(10.5), true);
            ui.label(&count, cr[0] + cw - cwid - 10.0 * s, cr[1] + 9.0 * s, font(10.5), true, if levels > 0 { color::GOLD } else { color::TEXT_MUTED });
            self.card_rewards(ui, cr, &first.rewards, s, mouse, all_claimed && levels == g.bonus.len());
            if inside(cr, mouse) && self.hover_item.is_none() {
                self.hint = Some(tf!("Each {bonus_name} level, every {step}% past the goal, pays this again to everyone who helped.", bonus_name, step = bonus_step(g)));
            }
            let bb = [cr[0] + 10.0 * s, cr[1] + ch - 38.0 * s, cw - 20.0 * s, 28.0 * s];
            if waiting > 0 {
                glow(ui, bb, pulse, s);
                let label = if waiting == 1 { tr("Claim").to_owned() } else { tf!("Claim x{waiting}", waiting) };
                ui.button(bb, &label, Kind::Primary, inside(bb, mouse), s * 0.9);
                self.hits.push((Hit::Tier(g.key.clone(), BONUS_ALL), bb));
            } else if !done {
                note(ui, bb, tr("Past the goal"), color::TEXT_MUTED, s);
            } else if levels > 0 && !mine_ok {
                note(ui, bb, tr("Help to claim"), color::TEXT_DIM, s);
            } else if let Some(next) = g.bonus.get(levels) {
                let left = tf!("{n} to go", n = crate::hud::group_digits(next.at - g.total));
                if all_claimed {
                    done_chip(ui, [bb[0], bb[1], bb[2], bb[3]], &left, s * 0.9);
                } else {
                    note(ui, bb, &left, color::TEXT_MUTED, s);
                }
            } else {
                done_chip(ui, bb, tr("All claimed"), s * 0.9);
            }
        }

        // Our part, the loot the rolls draw from, and the top contributors with their prize.
        let by = ty + ch + 14.0 * s;
        let bh = p[1] + p[3] - 14.0 * s - by;
        let inner = full[2] - 2.0 * gap;
        let (yw, lw) = ((inner * 0.27).round(), (inner * 0.37).round());
        let yr = [full[0], by, yw, bh];
        theme::frame(ui.renderer, yr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::PANEL_TOP, 0.7), (color::PANEL_BOTTOM, 0.7)));
        ui.label(tr("Your part"), yr[0] + 14.0 * s, yr[1] + 10.0 * s, font(12.0), true, color::TEXT_MUTED);
        let mine = crate::hud::group_digits(g.mine);
        ui.label(&mine, yr[0] + 14.0 * s, yr[1] + 30.0 * s, font(26.0), true, if mine_ok { color::GOLD } else { color::TEXT });
        if g.rank > 0 {
            let rank = tf!("#{rank} of {all}", rank = g.rank, all = crate::hud::group_digits(g.participants));
            ui.label(&rank, yr[0] + 14.0 * s, yr[1] + 66.0 * s, font(13.0), true, color::TEXT_DIM);
        }
        let ly = yr[1] + 94.0 * s;
        if mine_ok {
            check(ui, yr[0] + 20.0 * s, ly + 7.0 * s, 6.0 * s, s);
            let l = crate::npc::fit(ui, tr("You share it all."), font(12.0), yw - 44.0 * s);
            ui.label(&l, yr[0] + 32.0 * s, ly, font(12.0), true, color::GOOD);
            let more = crate::npc::wrap(ui, tr("Every tier and level the realm reaches is yours to claim."), font(11.0), yw - 28.0 * s);
            for (i, line) in more.iter().take(3).enumerate() {
                ui.label(line, yr[0] + 14.0 * s, ly + 22.0 * s + i as f32 * font(11.0) * 1.3, font(11.0), false, color::TEXT_MUTED);
            }
        } else {
            let need = g.minimum.max(1);
            let l = if g.mine == 0 { tf!("Add {n} to share.", n = crate::hud::group_digits(need)) } else { tf!("{n} more to share.", n = crate::hud::group_digits(need - g.mine)) };
            // smaller when it's long (some languages say it in more words), cut short if still
            let size = if ui.measure(&l, font(12.0), true) > yw - 28.0 * s { font(10.5) } else { font(12.0) };
            let l = crate::npc::fit(ui, &l, size, yw - 28.0 * s);
            ui.label(&l, yr[0] + 14.0 * s, ly + font(12.0) - size, size, true, color::ACCENT_HI);
            let mb = [yr[0] + 14.0 * s, ly + 22.0 * s, yw - 28.0 * s, 8.0 * s];
            theme::fill(ui.renderer, mb, mb[3] / 2.0, color::TRACK, 1.0);
            let k = (g.mine as f32 / need as f32).clamp(0.0, 1.0);
            if k > 0.0 {
                theme::fill(ui.renderer, [mb[0], mb[1], (mb[2] * k).max(mb[3]), mb[3]], mb[3] / 2.0, color::ACCENT, 1.0);
            }
        }

        let lr = [yr[0] + yw + gap, by, lw, bh];
        self.draw_loot(ui, lr, g, d, s, mouse, pulse);

        let tr = [lr[0] + lw + gap, by, full[0] + full[2] - lr[0] - lw - gap, bh];
        theme::frame(ui.renderer, tr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::PANEL_TOP, 0.7), (color::PANEL_BOTTOM, 0.7)));
        ui.label(crate::i18n::tr("Top contributors"), tr[0] + 14.0 * s, tr[1] + 10.0 * s, font(12.0), true, color::TEXT_MUTED);
        let row_h = 19.0 * s;
        let podium = [TIER_COLORS[2], TIER_COLORS[1], TIER_COLORS[0]];
        let list_bottom = tr[1] + bh - 92.0 * s;
        for (i, c) in g.top.iter().enumerate() {
            let ry = tr[1] + 30.0 * s + i as f32 * row_h;
            if ry + row_h > list_bottom {
                break;
            }
            let mc = podium.get(i).copied().unwrap_or(color::BORDER_HI);
            let (cx, cy) = (tr[0] + 22.0 * s, ry + row_h / 2.0);
            theme::fill(ui.renderer, [cx - 7.0 * s, cy - 7.0 * s, 14.0 * s, 14.0 * s], 7.0 * s, mc, if i < 3 { 1.0 } else { 0.5 });
            let num = (i + 1).to_string();
            let nw = ui.measure(&num, font(9.5), true);
            ui.label(&num, cx - nw / 2.0, cy - font(9.5) * 0.61, font(9.5), true, color::ACCENT_TEXT);
            let is_me = g.rank == i as i32 + 1;
            let amt = crate::hud::group_digits(c.amount);
            let aw = ui.measure(&amt, font(12.0), true);
            let name = crate::npc::fit(ui, &c.name, font(12.0), tr[2] - 60.0 * s - aw);
            ui.label(&name, tr[0] + 36.0 * s, ry + 2.0 * s, font(12.0), true, if is_me { color::GOLD } else { color::TEXT });
            ui.label(&amt, tr[0] + tr[2] - 14.0 * s - aw, ry + 2.0 * s, font(12.0), true, color::TEXT_DIM);
        }
        if g.top.is_empty() {
            ui.label(crate::i18n::tr("Nobody yet: be the first!"), tr[0] + 14.0 * s, tr[1] + 32.0 * s, font(12.0), false, color::TEXT_MUTED);
        }
        // the top's prize, under the list
        let zx = tr[0] + 12.0 * s;
        let zw = tr[2] - 24.0 * s;
        let zy = list_bottom + 2.0 * s;
        theme::fill(ui.renderer, [zx, zy, zw, (1.0 * s).max(1.0)], 0.0, color::BORDER, 1.0);
        let head = tf!("Top {n} when it ends", n = g.top_count);
        let head = crate::npc::fit(ui, &head, font(11.0), zw - 18.0 * s);
        ui.label(&head, zx, zy + 6.0 * s, font(11.0), true, color::GOLD);
        theme::star(ui.renderer, zx + zw - 6.0 * s, zy + 12.0 * s, 6.0 * s, color::GOLD);
        let fit = zw / rewards_width(ui, &g.top_rewards, s).max(1.0);
        let k = s * fit.min(1.0);
        let rw = rewards_width(ui, &g.top_rewards, k);
        reward_row(ui, &g.top_rewards, zx + (zw - rw) / 2.0 + 6.0 * k, zy + 42.0 * s, k, mouse, g.top_claimed, &mut self.hover_item);
        let zb = [zx, tr[1] + bh - 32.0 * s, zw, 24.0 * s];
        let ours = g.rank > 0 && g.rank <= g.top_count as i32 && mine_ok;
        if g.top_claimed {
            done_chip(ui, zb, crate::i18n::tr("Claimed"), s * 0.85);
        } else if !g.running && ours {
            glow(ui, zb, pulse, s);
            ui.button(zb, crate::i18n::tr("Claim"), Kind::Primary, inside(zb, mouse), s * 0.85);
            self.hits.push((Hit::Tier(g.key.clone(), TOP), zb));
        } else {
            let l = if g.running && ours {
                crate::i18n::tr("You're in it: hold on!")
            } else if g.running {
                crate::i18n::tr("Climb into the top")
            } else {
                crate::i18n::tr("Not in the top this time")
            };
            note(ui, zb, l, if ours { color::GOOD } else { color::TEXT_MUTED }, s);
        }
    }

    /// A tier card's rewards, centred under its band (smaller when they wouldn't fit).
    fn card_rewards(&mut self, ui: &mut Ui, cr: [f32; 4], rewards: &[bsk_proto::QuestReward], s: f32, mouse: (f64, f64), dim: bool) {
        let cw = cr[2];
        let fit = (cw - 14.0 * s) / rewards_width(ui, rewards, s).max(1.0);
        let k = s * fit.min(1.0);
        let rw = rewards_width(ui, rewards, k);
        reward_row(ui, rewards, cr[0] + (cw - rw) / 2.0 + 6.0 * k, cr[1] + 56.0 * s, k, mouse, dim, &mut self.hover_item);
    }

    /// The goal's loot: what its rolls draw from, rarest first, each edged in its rarity's colour
    /// with its chance a roll. Hovering one says its odds in Overdrive.
    #[allow(clippy::too_many_arguments)]
    fn draw_loot(&mut self, ui: &mut Ui, r: [f32; 4], g: &CommunityGoal, d: &CommunityData, s: f32, mouse: (f64, f64), pulse: f32) {
        let font = |size: f32| (size * s).round().max(9.0);
        theme::frame(ui.renderer, r, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::PANEL_TOP, 0.7), (color::PANEL_BOTTOM, 0.7)));
        let head = tr("Loot rolls");
        ui.label(head, r[0] + 14.0 * s, r[1] + 10.0 * s, font(12.0), true, color::TEXT_MUTED);
        let sub = tr("Overdrive rolls luckier");
        let sw = ui.measure(sub, font(10.0), false);
        // beside the heading, when there's room for both
        if r[2] - 14.0 * s - ui.measure(head, font(12.0), true) - 12.0 * s - 12.0 * s >= sw {
            ui.label(sub, r[0] + r[2] - 12.0 * s - sw, r[1] + 12.0 * s, font(10.0), false, color::GOLD);
        }
        if g.loot.is_empty() {
            ui.label(tr("No loot to roll on this goal."), r[0] + 14.0 * s, r[1] + 34.0 * s, font(12.0), false, color::TEXT_MUTED);
            return;
        }
        let mut loot: Vec<&bsk_proto::CommunityLoot> = g.loot.iter().collect();
        loot.sort_by(|a, b| b.rarity.cmp(&a.rarity).then(a.chance.cmp(&b.chance)));
        let side = 30.0 * s;
        let cell_w = side + 9.0 * s;
        let cell_h = side + 17.0 * s;
        let cols = (((r[2] - 20.0 * s) / cell_w).floor() as usize).max(1);
        let x0 = r[0] + (r[2] - cols as f32 * cell_w + 9.0 * s) / 2.0;
        let y0 = r[1] + 32.0 * s;
        for (i, e) in loot.iter().enumerate() {
            let (cx, cy) = (x0 + (i % cols) as f32 * cell_w, y0 + (i / cols) as f32 * cell_h);
            if cy + cell_h > r[1] + r[3] - 2.0 * s {
                break;
            }
            let sr = [cx, cy, side, side];
            let rc = crate::screens::rarity_color(e.rarity as i32);
            let over = inside(sr, mouse);
            if e.rarity >= 4 {
                // the rarest shine
                let g2 = (1.5 + pulse * 2.0) * s;
                theme::fill(ui.renderer, [sr[0] - g2, sr[1] - g2, side + 2.0 * g2, side + 2.0 * g2], 6.0 * s + g2, rc, 0.22);
            }
            ui.slot_box(sr, Some(rc), over, s);
            ui.item_icon(e.item as i32, 60, sr[0] + side / 2.0, sr[1] + side / 2.0, s * 0.9);
            if e.count > 1 {
                let cs = font(10.0);
                let t = e.count.to_string();
                let tw = ui.measure(&t, cs, true);
                ui.label(&t, sr[0] + side - tw - 2.0 * s, sr[1] + side - cs * 1.2, cs, true, color::TEXT);
            }
            let chance = chance_text(e.chance);
            let cs = font(9.5);
            let cw = ui.measure(&chance, cs, true);
            ui.label(&chance, sr[0] + (side - cw) / 2.0, sr[1] + side + 2.0 * s, cs, true, rc);
            if over {
                self.hover_item = Some(e.item as i32);
                let rarity = d.rarities.get(e.rarity as usize).map_or(String::new(), |n| rarity_name(n));
                let bonus = named(if d.bonus_name.is_empty() { "Overdrive" } else { &d.bonus_name });
                self.hint = Some(tf!(
                    "{rarity}: {chance} a roll at a tier, {best} at {bonus} {level}.",
                    rarity,
                    chance = chance_text(e.chance),
                    best = chance_text(e.best),
                    bonus,
                    level = roman(g.bonus.len())
                ));
            }
        }
    }

    /// The banners, at the top middle of the view, under the quest log's popups (`top`): they
    /// slide in, wait, and fade out.
    pub fn draw_banners(&mut self, ui: &mut Ui, lay: &Layout, top: f32, now: i32) {
        self.banners.retain(|b| now - b.since < BANNER_MS);
        self.banner_hits.clear();
        if self.open {
            return;
        }
        let s = lay.s;
        let w = 470.0 * s;
        let h = 78.0 * s;
        let mut y = top;
        let pulse = 0.5 + 0.5 * ((now as f32) / 220.0).sin();
        for b in &self.banners {
            let age = now - b.since;
            let fade = (age as f32 / BANNER_FADE as f32).min(((BANNER_MS - age) as f32 / BANNER_FADE as f32).max(0.0)).clamp(0.0, 1.0);
            let slide = (1.0 - fade) * -16.0 * s;
            let r = [((lay.view_w() - w) / 2.0).round(), (y + slide).round(), w, h];
            let accent = match b.kind {
                COMPLETE => color::GOOD,
                NEW_WEEK => color::ACCENT,
                LOOT => 0xB86BFF,
                _ => color::GOLD,
            };
            theme::shadow(ui.renderer, r, 10.0 * s, 12.0 * s, 0.45 * fade);
            let g = (2.0 + pulse * 3.0) * s;
            theme::fill(ui.renderer, [r[0] - g, r[1] - g, r[2] + 2.0 * g, r[3] + 2.0 * g], 12.0 * s + g, accent, 0.12 * fade);
            theme::frame(ui.renderer, r, 10.0 * s, (1.5 * s).max(1.0), (accent, fade), ((color::WARM_TOP, 0.97 * fade), (color::PANEL_BOTTOM, 0.97 * fade)));
            // the banner's emblem: a star in a ring of the realm's ember
            let (bx, by) = (r[0] + 32.0 * s, r[1] + h / 2.0);
            theme::fill(ui.renderer, [bx - 19.0 * s, by - 19.0 * s, 38.0 * s, 38.0 * s], 19.0 * s, accent, 0.25 * fade);
            theme::star(ui.renderer, bx, by + 1.0 * s, 12.0 * s, accent);
            if fade > 0.5 {
                let tsz = (15.0 * s).round().max(9.0);
                let title = crate::npc::fit(ui, &b.title, tsz, w - 74.0 * s);
                ui.label(&title, r[0] + 62.0 * s, r[1] + 10.0 * s, tsz, true, accent);
                // the text on two lines at most
                let bs = (12.0 * s).round().max(9.0);
                let mut lines = crate::npc::wrap(ui, &b.text, bs, w - 74.0 * s);
                if lines.len() > 2 {
                    let rest = lines[1..].join(" ");
                    lines.truncate(1);
                    lines.push(crate::npc::fit(ui, &rest, bs, w - 74.0 * s));
                }
                for (i, line) in lines.iter().enumerate() {
                    ui.label(line, r[0] + 62.0 * s, r[1] + 33.0 * s + i as f32 * bs * 1.3, bs, false, color::TEXT);
                }
            }
            self.banner_hits.push(r);
            y += h + 8.0 * s;
        }
    }
}

/// A goal's bar in the list.
fn mini_bar(ui: &mut Ui, r: [f32; 4], shown: f32, g: &CommunityGoal, s: f32) {
    theme::fill(ui.renderer, r, r[3] / 2.0, color::TRACK, 1.0);
    let k = (shown / g.goal.max(1) as f32).clamp(0.0, 1.0);
    let c = if g.total >= g.goal { color::GOOD } else { tier_color(reached(g).min(3)) };
    if k > 0.0 {
        theme::rounded(ui.renderer, [r[0], r[1], (r[2] * k).max(r[3]), r[3]], r[3] / 2.0, (theme::shade(c, 0.2), 1.0), (theme::shade(c, -0.2), 1.0));
    }
    // the tiers' notches
    for t in g.tiers.iter() {
        let x = r[0] + r[2] * (t.at as f32 / g.goal.max(1) as f32).clamp(0.0, 1.0);
        ui.rect(x - 0.5 * s, r[1], (1.0 * s).max(1.0), r[3], color::BACKGROUND, 0.6);
    }
    // in Overdrive, a band of gold creeps back over it
    let od = overdrive_k(g, shown);
    if od > 0.0 {
        theme::rounded(ui.renderer, [r[0], r[1], (r[2] * od).max(r[3]), r[3] * 0.55], r[3] * 0.27, (color::GOLD, 0.95), (theme::shade(color::GOLD, -0.2), 0.95));
    }
}

/// The Overdrive track past the goal: gold, with a quicker shine, filling level by level.
fn overdrive_bar(ui: &mut Ui, r: [f32; 4], shown: f32, g: &CommunityGoal, now: i32, s: f32) {
    let on = shown >= g.goal as f32;
    theme::frame(ui.renderer, [r[0] - 2.0 * s, r[1] - 2.0 * s, r[2] + 4.0 * s, r[3] + 4.0 * s], r[3] / 2.0 + 2.0 * s, s.max(1.0), (if on { color::GOLD } else { color::BORDER_HI }, if on { 0.8 } else { 1.0 }), ((color::TRACK, 1.0), (color::TRACK, 1.0)));
    let k = overdrive_k(g, shown);
    if k <= 0.0 {
        return;
    }
    let fw = (r[2] * k).max(r[3]);
    theme::rounded(ui.renderer, [r[0], r[1], fw, r[3]], r[3] / 2.0, (theme::shade(color::GOLD, 0.35), 1.0), (0xB86BFF, 1.0));
    theme::rounded(ui.renderer, [r[0] + 2.0 * s, r[1] + 2.0 * s, fw - 4.0 * s, r[3] * 0.4], r[3] * 0.2, (0xFFFFFF, 0.3), (0xFFFFFF, 0.05));
    let band = 26.0 * s;
    let t = (now.rem_euclid(1300) as f32) / 1300.0;
    let bx = r[0] - band + (fw + band * 2.0) * t;
    let x0 = bx.max(r[0] + 2.0 * s);
    let x1 = (bx + band).min(r[0] + fw - 2.0 * s);
    if x1 > x0 {
        theme::fill(ui.renderer, [x0, r[1] + 2.0 * s, x1 - x0, r[3] - 4.0 * s], (r[3] - 4.0 * s) / 2.0, 0xFFFFFF, 0.28);
    }
    let ex = r[0] + fw;
    theme::fill(ui.renderer, [ex - 5.0 * s, r[1] - 3.0 * s, 8.0 * s, r[3] + 6.0 * s], 4.0 * s, color::GOLD, 0.6);
}

/// The chosen goal's bar: it fills from ember to gold through the tiers it has passed (green
/// once complete), with a shine running along it.
fn big_bar(ui: &mut Ui, r: [f32; 4], shown: f32, g: &CommunityGoal, now: i32, s: f32) {
    theme::frame(ui.renderer, [r[0] - 2.0 * s, r[1] - 2.0 * s, r[2] + 4.0 * s, r[3] + 4.0 * s], r[3] / 2.0 + 2.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::TRACK, 1.0), (color::TRACK, 1.0)));
    let k = (shown / g.goal.max(1) as f32).clamp(0.0, 1.0);
    if k <= 0.0 {
        return;
    }
    let fw = (r[2] * k).max(r[3]);
    let c = if g.total >= g.goal { color::GOOD } else { tier_color(g.tiers.iter().take_while(|t| shown >= t.at as f32).count().min(3)) };
    theme::rounded(ui.renderer, [r[0], r[1], fw, r[3]], r[3] / 2.0, (theme::shade(c, 0.3), 1.0), (theme::shade(c, -0.25), 1.0));
    // a lighter top half, as glass
    theme::rounded(ui.renderer, [r[0] + 2.0 * s, r[1] + 2.0 * s, fw - 4.0 * s, r[3] * 0.4], r[3] * 0.2, (0xFFFFFF, 0.22), (0xFFFFFF, 0.04));
    // the shine: a soft band sweeping along the filled part every couple of seconds
    let band = 46.0 * s;
    let t = (now.rem_euclid(2400) as f32) / 2400.0;
    let bx = r[0] - band + (fw + band * 2.0) * t;
    let x0 = bx.max(r[0] + 2.0 * s);
    let x1 = (bx + band).min(r[0] + fw - 2.0 * s);
    if x1 > x0 {
        theme::fill(ui.renderer, [x0, r[1] + 2.0 * s, x1 - x0, r[3] - 4.0 * s], (r[3] - 4.0 * s) / 2.0, 0xFFFFFF, 0.16);
    }
    // the leading edge glows
    let ex = r[0] + fw;
    theme::fill(ui.renderer, [ex - 5.0 * s, r[1] - 3.0 * s, 8.0 * s, r[3] + 6.0 * s], 4.0 * s, theme::shade(c, 0.5), 0.5);
}

/// A tier's gem above the bar: a diamond, lit and pulsing once reached.
#[allow(clippy::too_many_arguments)]
fn gem(ui: &mut Ui, cx: f32, cy: f32, half: f32, rgb: u32, on: bool, pulse: f32, s: f32) {
    let white = ui.renderer.white();
    let quarter = std::f32::consts::FRAC_PI_4;
    if on {
        let g = half + (1.5 + pulse * 2.5) * s;
        ui.renderer.sprite(white, bsk_render::Quad::rotated(cx, cy, g * 1.42, g * 1.42, quarter), bsk_render::Tint::color(rgb, 0.3));
    }
    let edge = half * 1.42;
    ui.renderer.sprite(white, bsk_render::Quad::rotated(cx, cy, edge, edge, quarter), bsk_render::Tint::color(if on { theme::shade(rgb, -0.35) } else { color::BORDER_HI }, 1.0));
    let inner = edge - 3.0 * s;
    ui.renderer.sprite(white, bsk_render::Quad::rotated(cx, cy, inner, inner, quarter), bsk_render::Tint::color(if on { rgb } else { color::TRACK }, 1.0));
}

#[cfg(test)]
mod tests {
    use super::*;
    use bsk_proto::{CommunityContributor, CommunityLoot, CommunityTier, QuestReward};

    fn goal(total: i32, mine: i32, claimed: &[bool], running: bool, rank: i32) -> CommunityGoal {
        CommunityGoal {
            key: "oryx@20261019".into(),
            title: "Down with the Mad God".into(),
            text: "x".into(),
            icon: 0,
            special: false,
            starts_at: 0,
            ends_at: 0,
            running,
            goal: 16,
            base_goal: 16,
            total,
            participants: 3,
            minimum: 2,
            mine,
            rank,
            tiers: [4, 8, 12, 16].iter().zip(claimed).map(|(&at, &c)| CommunityTier { at, claimed: c, rewards: vec![QuestReward { kind: 0, item: 0, amount: 100 }].into() }).collect::<Vec<_>>().into(),
            bonus: [20, 24, 28, 32].iter().map(|&at| CommunityTier { at, claimed: false, rewards: vec![QuestReward { kind: 2, item: 0, amount: 1 }].into() }).collect::<Vec<_>>().into(),
            loot: vec![CommunityLoot { item: 0x0a1b, count: 1, rarity: 4, chance: 40, best: 90 }].into(),
            top_count: 3,
            top_claimed: false,
            top_rewards: vec![QuestReward { kind: 0, item: 0, amount: 1000 }].into(),
            top: vec![CommunityContributor { name: "Tester".into(), amount: 5 }].into(),
        }
    }

    fn data(goals: Vec<CommunityGoal>) -> CommunityData {
        CommunityData {
            now: 1_800_000_000,
            week_ends_at: 1_800_300_000,
            tier_names: vec!["Spark".to_owned(); 4].into(),
            bonus_name: "Overdrive".into(),
            rarities: vec!["common".to_owned(); 6].into(),
            goals: goals.into(),
        }
    }

    fn roll(item: u16) -> CommunityRoll {
        let reel = (0..40).map(|i| bsk_proto::CommunityReelSlot { item: if i == 34 { item } else { 0x0a00 }, rarity: 0 }).collect::<Vec<_>>();
        CommunityRoll { item, count: 1, rarity: 2, stop: 34, reel: reel.into() }
    }

    #[test]
    fn claimed_rolls_play_on_the_reel() {
        let mut c = Community::default();
        // shut: no reel
        c.event(Event::Rolls(vec![roll(1)]), 0);
        assert!(c.reels.is_none());
        c.show();
        // the answer waits at the foot till the last roll lands; the chat waits too (`reeling`)
        c.event(Event::Notice(CLAIMED, "Claimed!".into(), "x".into()), 100);
        c.event(Event::Rolls(vec![roll(1), roll(2), roll(3)]), 100);
        assert!(c.message.is_none() && c.reeling());
        // the first turns; a press lands it, the next goes on to the second roll
        c.key(KeyCode::Space);
        assert!(c.reels.as_ref().unwrap().skip);
        c.reels.as_mut().unwrap().landed = Some(100);
        c.key(KeyCode::Space);
        assert_eq!(c.reels.as_ref().map(|r| (r.at, r.landed)), Some((1, None)));
        // Escape lands the rest, again puts it away; the window stays
        c.key(KeyCode::Escape);
        assert_eq!(c.reels.as_ref().map(|r| (r.at, r.skip)), Some((2, true)));
        c.reels.as_mut().unwrap().landed = Some(200);
        assert!(!c.reeling());
        c.key(KeyCode::Escape);
        assert!(c.reels.is_none() && c.open);
        assert_eq!(c.message.as_ref().map(|m| m.0.as_str()), Some("Claimed! x"));
        // closing the window puts it away too
        c.event(Event::Rolls(vec![roll(1)]), 300);
        c.close();
        assert!(c.reels.is_none());
    }

    #[test]
    fn rewards_wait_for_those_who_helped() {
        // two tiers reached, one claimed
        assert_eq!(claimable(&goal(9, 5, &[true, false, false, false], true, 1)), vec![1]);
        // not enough of our own: nothing, however far the realm got
        assert!(claimable(&goal(16, 1, &[false; 4], true, 2)).is_empty());
        // ended, and we're in the top: its prize too
        assert_eq!(claimable(&goal(16, 5, &[true, true, true, false], false, 2)), vec![3, TOP]);
        // ended, but fourth
        assert_eq!(claimable(&goal(4, 5, &[false; 4], false, 4)), vec![0]);
        assert_eq!(reached(&goal(12, 0, &[false; 4], true, 0)), 3);
    }

    #[test]
    fn overdrive_goes_past_the_goal() {
        // all four tiers claimed, two levels reached past the goal: those two wait
        let g = goal(25, 5, &[true; 4], true, 1);
        assert_eq!(claimable(&g), vec![BONUS, BONUS + 1]);
        assert_eq!((bonus_reached(&g), percent(&g), bonus_step(&g)), (2, 156, 25));
        // the track: nothing at the goal, half way at the second level's line, full at the last
        assert_eq!(overdrive_k(&g, 16.0), 0.0);
        assert!((overdrive_k(&g, 24.0) - 0.5).abs() < 1e-6);
        assert!((overdrive_k(&g, 22.0) - 0.375).abs() < 1e-6);
        assert_eq!(overdrive_k(&g, 99.0), 1.0);
        // a run sized by the last
        let mut h = g.clone();
        h.goal = 20;
        assert_eq!(sizing(&h), Some(("Harder +25%".into(), color::ACCENT_HI)));
        h.goal = 12;
        assert_eq!(sizing(&h).map(|t| t.0), Some("Easier -25%".into()));
        assert_eq!(sizing(&g), None);
        assert_eq!((chance_text(1234), chance_text(250), chance_text(40), chance_text(5)), ("12%".into(), "2.5%".into(), "0.4%".into(), "<0.1%".into()));
        assert_eq!((roman(3), roman(8)), ("III".into(), "VIII".into()));
    }

    #[test]
    fn says_once_when_a_reward_waits() {
        let mut c = Community::default();
        c.event(Event::Data(Box::new(data(vec![goal(4, 5, &[false; 4], true, 1)]))), 0);
        assert_eq!(c.ready(), 1);
        assert_eq!(c.banners.len(), 1);
        // the same reward again: no new banner; a new tier: one more
        c.event(Event::Data(Box::new(data(vec![goal(4, 5, &[false; 4], true, 1)]))), 10);
        assert_eq!(c.banners.len(), 1);
        c.event(Event::Data(Box::new(data(vec![goal(8, 5, &[false; 4], true, 1)]))), 20);
        assert_eq!(c.banners.len(), 2);
        // a claim's answer goes to the window, not a banner
        c.event(Event::Notice(CLAIMED, "Claimed!".into(), "x".into()), 30);
        assert_eq!(c.banners.len(), 2);
        assert!(c.message.as_ref().is_some_and(|m| !m.1));
    }

    #[test]
    fn asks_again_while_open() {
        let mut c = Community::default();
        assert_eq!(c.tick(10_000), None);
        assert_eq!(c.show(), Some(Act::Request));
        assert_eq!(c.tick(12_000), None);
        assert_eq!(c.tick(14_100), Some(Act::Request));
        c.close();
        assert_eq!(c.tick(30_000), None);
    }
}
