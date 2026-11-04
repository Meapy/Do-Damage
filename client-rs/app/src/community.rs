//! Community goals (the world server's `CommunityGoals.cs`): what the whole server does together
//! each week, such as defeating Oryx 15 times. Each goal has four tiers along the way; everyone
//! who helped enough claims each tier the server reaches, and the top contributors claim a prize
//! more once it ends. The Warcaller at the north point of the Nexus spawn opens the window.
//!
//! The client asks for the goals once in each world (`CommunityRequest`), so it can say when a
//! reward waits, and every few seconds while the window is open, so the bars move as the realm
//! plays. `CommunityNotice`s pop up as banners at the top of the view: a tier the server reached,
//! a goal completed, the week's new goals. The server checks every claim (`CommunityClaim`).

use crate::hud::{Layout, SlotKind, Ui};
use crate::quests::{card, check, countdown, done_chip, glow, inside, locked, reward_row, rewards_width, TIER_COLORS};
use crate::theme::{self, color, Kind};
use bsk_proto::{CommunityData, CommunityGoal};
use bsk_world::GameObj;
use std::collections::HashMap;
use winit::keyboard::KeyCode;

/// `CommunityClaim`'s tiers beyond the goal's own: the top's prize, and all a goal has waiting.
pub const TOP: u8 = 255;
pub const ALL: u8 = 254;

/// `CommunityNotice` kinds.
const CLAIMED: u8 = 0;
const REFUSED: u8 = 1;
const COMPLETE: u8 = 3;
const NEW_WEEK: u8 = 4;
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
}

#[derive(Clone, Debug, PartialEq)]
enum Hit {
    Close,
    Goal(String),
    Tier(String, u8),
    All,
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

/// The tiers of a goal we can claim now, and the top's prize (`TOP`) if it's ours.
pub fn claimable(g: &CommunityGoal) -> Vec<u8> {
    if !qualifies(g) {
        return Vec::new();
    }
    let mut out: Vec<u8> = g.tiers.iter().enumerate().filter(|(_, t)| g.total >= t.at && !t.claimed).map(|(i, _)| i as u8).collect();
    if !g.running && g.rank > 0 && g.rank <= g.top_count as i32 && !g.top_claimed && !g.top_rewards.is_empty() {
        out.push(TOP);
    }
    out
}

/// Tiers the server has reached on a goal.
fn reached(g: &CommunityGoal) -> usize {
    g.tiers.iter().take_while(|t| g.total >= t.at).count()
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
    now: i32,
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
    }

    /// Forgets the goals (a new character or account): the next join asks again.
    pub fn reset(&mut self) {
        self.data = None;
        self.open = false;
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
                        format!("\"{}\": the realm did it, and you helped. Claim it from the Warcaller.", fresh[0].1)
                    } else {
                        format!("{} rewards from the realm's goals are yours. Claim them from the Warcaller.", fresh.len())
                    };
                    self.banner(WAITING, "A community reward waits!".into(), text, now);
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
        }
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
        let hit = self.hits.iter().find(|(_, r)| inside(*r, pos)).map(|(h, _)| h.clone())?;
        match hit {
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
        self.now = now;
        self.hits.clear();
        self.hover_item = None;
        let dt = ((now - self.drawn).clamp(0, 200) as f32) / 1000.0;
        self.drawn = now;
        if !self.open {
            self.rect = None;
            return;
        }
        let (dw, dh) = (900.0, 600.0);
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
        ui.label("Community Goals", r[0] + 56.0 * s, r[1] + 16.0 * s, ts, true, color::GOLD);
        ui.label("The whole realm, one goal. Everyone who helps shares every tier it reaches.", r[0] + 56.0 * s, r[1] + 46.0 * s, font(12.0), false, color::TEXT_DIM);
        let cb = [r[0] + r[2] - 44.0 * s, r[1] + 20.0 * s, 28.0 * s, 28.0 * s];
        if inside(cb, mouse) {
            theme::fill(ui.renderer, cb, 6.0 * s, 0xFFFFFF, 0.1);
        }
        theme::cross(ui.renderer, cb[0] + cb[2] / 2.0, cb[1] + cb[3] / 2.0, 6.0 * s, (2.0 * s).max(1.0), color::TEXT_DIM);
        self.hits.push((Hit::Close, cb));
        if let Some(d) = &self.data {
            let t = format!("New goals in {}", countdown(d.week_ends_at as i64 - self.server_now()));
            let tw = ui.measure(&t, font(13.0), true);
            ui.label(&t, cb[0] - 16.0 * s - tw, r[1] + 26.0 * s, font(13.0), true, color::TEXT_DIM);
        }

        let body = [r[0] + 22.0 * s, r[1] + 84.0 * s, r[2] - 44.0 * s, r[3] - 84.0 * s - 56.0 * s];
        let Some(d) = self.data.clone() else {
            ui.label("Asking the Warcaller how the realm fares...", body[0], body[1] + 10.0 * s, font(14.0), false, color::TEXT_MUTED);
            return;
        };
        if d.goals.is_empty() {
            ui.label("The realm rests: no goals right now. Come back when the horn sounds again.", body[0], body[1] + 10.0 * s, font(14.0), false, color::TEXT_MUTED);
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
                let label = if g.running { "This week" } else { "Ended: claim before they go" };
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
            let bar = [tx, cr[1] + 36.0 * s, lw - 54.0 * s - 46.0 * s, 8.0 * s];
            let shown = self.shown_total(g, dt);
            mini_bar(ui, bar, shown, g, s);
            let pct = format!("{}%", ((g.total as f32 / g.goal.max(1) as f32) * 100.0).floor().min(100.0) as i32);
            ui.label(&pct, bar[0] + bar[2] + 8.0 * s, bar[1] - 4.0 * s, font(11.0), true, if g.total >= g.goal { color::GOOD } else { color::TEXT_DIM });
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
        self.draw_goal(ui, panel, &g, &d.tier_names, s, mouse, now, dt);

        // The foot: the last word on a claim, or what rewards are; and Claim all.
        let foot_y = r[1] + r[3] - 40.0 * s;
        let all = self.ready();
        let bw = 150.0 * s;
        let ab = [r[0] + r[2] - 22.0 * s - bw, foot_y - 6.0 * s, bw, 34.0 * s];
        if all > 0 {
            glow(ui, ab, pulse, s);
            ui.button(ab, &format!("Claim all ({all})"), Kind::Primary, inside(ab, mouse), s);
            self.hits.push((Hit::All, ab));
        }
        if let Some((m, err, _)) = &self.message {
            ui.label(m, r[0] + 22.0 * s, foot_y, font(13.0), true, if *err { color::DANGER } else { color::GOOD });
        } else {
            let hint = "Rewards: fame to your account, items to the gift chest in your vault.";
            ui.label(hint, r[0] + 22.0 * s, foot_y, font(12.0), false, color::TEXT_MUTED);
        }
        if let Some(item) = self.hover_item {
            let lines = crate::hud::item_tooltip(ui.data, item, me, SlotKind::Shop);
            crate::hud::draw_tooltip(ui, lay, &lines, Some(item), crate::screens::tier_edge(ui.data, item), mouse);
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn draw_goal(&mut self, ui: &mut Ui, p: [f32; 4], g: &CommunityGoal, tier_names: &[String], s: f32, mouse: (f64, f64), now: i32, dt: f32) {
        let font = |size: f32| (size * s).round().max(9.0);
        let pulse = 0.5 + 0.5 * ((now as f32) / 260.0).sin();
        let done = g.total >= g.goal;
        theme::frame(ui.renderer, p, 10.0 * s, s.max(1.0), (color::BORDER_HI, 1.0), ((color::INSET_TOP, 0.95), (color::INSET_BOTTOM, 0.95)));

        // The hero: the goal's picture in a glow, what it is, and the count.
        let (ix, iy) = (p[0] + 52.0 * s, p[1] + 50.0 * s);
        let halo = 34.0 * s + pulse * 3.0 * s;
        theme::fill(ui.renderer, [ix - halo, iy - halo, halo * 2.0, halo * 2.0], halo, if done { color::GOOD } else { color::ACCENT }, 0.10 + 0.06 * pulse);
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
        ui.label(&total, right - ow - bw, p[1] + 12.0 * s, big, true, if done { color::GOOD } else { color::GOLD });
        ui.label(&of, right - ow, p[1] + 25.0 * s, font(15.0), true, color::TEXT_DIM);
        let helping = if g.participants == 1 { "1 helping".to_owned() } else { format!("{} helping", crate::hud::group_digits(g.participants)) };
        let hw = ui.measure(&helping, font(12.0), false);
        ui.label(&helping, right - hw, p[1] + 52.0 * s, font(12.0), false, color::TEXT_MUTED);
        // the title, its tags, the text, the time
        let room = right - ow - bw - 16.0 * s - tx;
        let title = crate::npc::fit(ui, &g.title, font(20.0), room);
        ui.label(&title, tx, p[1] + 14.0 * s, font(20.0), true, color::TEXT);
        let mut tag_x = tx;
        let tag_y = p[1] + 44.0 * s;
        let tags: Vec<(&str, u32)> = [(g.special, ("Special", color::ACCENT)), (!g.running, ("Ended", color::TEXT_MUTED)), (done, ("Complete", color::GOOD))]
            .into_iter()
            .filter(|(on, _)| *on)
            .map(|(_, t)| t)
            .collect();
        for (t, c) in tags {
            let w = ui.measure(t, font(10.5), true) + 14.0 * s;
            let tr = [tag_x, tag_y, w, 18.0 * s];
            theme::fill(ui.renderer, tr, 9.0 * s, c, 0.22);
            ui.label(t, tr[0] + 7.0 * s, tr[1] + (tr[3] - font(10.5) * 1.22) / 2.0, font(10.5), true, c);
            tag_x += w + 6.0 * s;
        }
        let when = if g.running {
            format!("Ends in {}", countdown(g.ends_at as i64 - self.server_now()))
        } else {
            "Its rewards can still be claimed for a while".to_owned()
        };
        ui.label(&when, tag_x, tag_y + 2.0 * s, font(12.0), true, color::TEXT_DIM);
        let text = crate::npc::fit(ui, &g.text, font(13.0), p[0] + p[2] - 20.0 * s - tx);
        ui.label(&text, tx, p[1] + 70.0 * s, font(13.0), false, color::TEXT_DIM);

        // The bar, with each tier's marker on it.
        let shown = self.shown_total(g, dt);
        let bar = [p[0] + 24.0 * s, p[1] + 112.0 * s, p[2] - 48.0 * s, 24.0 * s];
        big_bar(ui, bar, shown, g, now, s);
        let n = g.tiers.len();
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

        // The tiers: what each gives, and claiming it.
        let mine_ok = qualifies(g);
        let ty = bar[1] + bar[3] + 26.0 * s;
        let gap = 10.0 * s;
        let cw = (bar[2] - gap * (n.max(1) - 1) as f32) / n.max(1) as f32;
        let ch = 122.0 * s;
        for (t, tier) in g.tiers.iter().enumerate() {
            let cr = [bar[0] + t as f32 * (cw + gap), ty, cw, ch];
            let got = g.total >= tier.at;
            let ready = got && !tier.claimed && mine_ok;
            card(ui, cr, ready, tier.claimed, pulse, s);
            let c = tier_color(t);
            // a band of the tier's colour along the top
            theme::rounded(ui.renderer, [cr[0] + 1.0 * s, cr[1] + 1.0 * s, cr[2] - 2.0 * s, 26.0 * s], 7.0 * s, (c, if got { 0.32 } else { 0.10 }), (c, 0.0));
            let name = tier_names.get(t).cloned().unwrap_or_else(|| format!("Tier {}", t + 1));
            ui.label(&name, cr[0] + 10.0 * s, cr[1] + 7.0 * s, font(13.0), true, if got { c } else { color::TEXT_DIM });
            let pct = format!("{}%", ((tier.at as f32 / g.goal.max(1) as f32) * 100.0).round() as i32);
            let pw = ui.measure(&pct, font(10.5), true);
            ui.label(&pct, cr[0] + cw - pw - 10.0 * s, cr[1] + 9.0 * s, font(10.5), true, color::TEXT_MUTED);
            // the rewards, centred (smaller when they wouldn't fit)
            let fit = (cw - 14.0 * s) / rewards_width(ui, &tier.rewards, s).max(1.0);
            let k = s * fit.min(1.0);
            let rw = rewards_width(ui, &tier.rewards, k);
            reward_row(ui, &tier.rewards, cr[0] + (cw - rw) / 2.0 + 6.0 * k, cr[1] + 58.0 * s, k, mouse, tier.claimed, &mut self.hover_item);
            let bb = [cr[0] + 10.0 * s, cr[1] + ch - 38.0 * s, cw - 20.0 * s, 28.0 * s];
            if tier.claimed {
                done_chip(ui, bb, "Claimed", s * 0.9);
            } else if ready {
                glow(ui, bb, pulse, s);
                ui.button(bb, "Claim", Kind::Primary, inside(bb, mouse), s * 0.9);
                self.hits.push((Hit::Tier(g.key.clone(), t as u8), bb));
            } else if got {
                // reached, but we haven't helped enough
                let l = "Help to claim";
                let lw = ui.measure(l, font(11.0), true);
                theme::fill(ui.renderer, bb, bb[3] / 2.0, color::CHIP, 0.6);
                ui.label(l, bb[0] + (bb[2] - lw) / 2.0, bb[1] + (bb[3] - font(11.0) * 1.22) / 2.0, font(11.0), true, color::TEXT_DIM);
            } else {
                let left = format!("{} to go", crate::hud::group_digits(tier.at - g.total));
                let lw = ui.measure(&left, font(11.0), true);
                locked(ui, [bb[0], bb[1], 26.0 * s, bb[3]], s * 0.8);
                ui.label(&left, bb[0] + (bb[2] - lw) / 2.0 + 8.0 * s, bb[1] + (bb[3] - font(11.0) * 1.22) / 2.0, font(11.0), true, color::TEXT_MUTED);
            }
        }

        // Our part, and the top contributors with their prize.
        let by = ty + ch + 14.0 * s;
        let bh = p[1] + p[3] - 14.0 * s - by;
        let yw = (bar[2] - gap) * 0.42;
        let yr = [bar[0], by, yw, bh];
        theme::frame(ui.renderer, yr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::PANEL_TOP, 0.7), (color::PANEL_BOTTOM, 0.7)));
        ui.label("Your part", yr[0] + 14.0 * s, yr[1] + 10.0 * s, font(12.0), true, color::TEXT_MUTED);
        let mine = crate::hud::group_digits(g.mine);
        ui.label(&mine, yr[0] + 14.0 * s, yr[1] + 30.0 * s, font(26.0), true, if mine_ok { color::GOLD } else { color::TEXT });
        let mw = ui.measure(&mine, font(26.0), true);
        if g.rank > 0 {
            let rank = format!("#{} of {}", g.rank, crate::hud::group_digits(g.participants));
            ui.label(&rank, yr[0] + 22.0 * s + mw, yr[1] + 40.0 * s, font(13.0), true, color::TEXT_DIM);
        }
        let ly = yr[1] + 72.0 * s;
        if mine_ok {
            check(ui, yr[0] + 20.0 * s, ly + 7.0 * s, 6.0 * s, s);
            let l = crate::npc::fit(ui, "You share every tier reached.", font(12.0), yw - 44.0 * s);
            ui.label(&l, yr[0] + 32.0 * s, ly, font(12.0), true, color::GOOD);
        } else {
            let need = g.minimum.max(1);
            let l = if g.mine == 0 { format!("Add {} to share the rewards.", crate::hud::group_digits(need)) } else { format!("{} more to share the rewards.", crate::hud::group_digits(need - g.mine)) };
            let l = crate::npc::fit(ui, &l, font(12.0), yw - 28.0 * s);
            ui.label(&l, yr[0] + 14.0 * s, ly, font(12.0), true, color::ACCENT_HI);
            let mb = [yr[0] + 14.0 * s, ly + 22.0 * s, yw - 28.0 * s, 8.0 * s];
            theme::fill(ui.renderer, mb, mb[3] / 2.0, color::TRACK, 1.0);
            let k = (g.mine as f32 / need as f32).clamp(0.0, 1.0);
            if k > 0.0 {
                theme::fill(ui.renderer, [mb[0], mb[1], (mb[2] * k).max(mb[3]), mb[3]], mb[3] / 2.0, color::ACCENT, 1.0);
            }
        }

        let tr = [bar[0] + yw + gap, by, bar[2] - yw - gap, bh];
        theme::frame(ui.renderer, tr, 8.0 * s, s.max(1.0), (color::BORDER, 1.0), ((color::PANEL_TOP, 0.7), (color::PANEL_BOTTOM, 0.7)));
        ui.label("Top contributors", tr[0] + 14.0 * s, tr[1] + 10.0 * s, font(12.0), true, color::TEXT_MUTED);
        let row_h = 19.0 * s;
        let podium = [TIER_COLORS[2], TIER_COLORS[1], TIER_COLORS[0]];
        let list_w = tr[2] * 0.5;
        for (i, c) in g.top.iter().enumerate() {
            let ry = tr[1] + 30.0 * s + i as f32 * row_h;
            if ry + row_h > tr[1] + bh - 4.0 * s {
                break;
            }
            let mc = podium.get(i).copied().unwrap_or(color::BORDER_HI);
            let (cx, cy) = (tr[0] + 22.0 * s, ry + row_h / 2.0);
            theme::fill(ui.renderer, [cx - 7.0 * s, cy - 7.0 * s, 14.0 * s, 14.0 * s], 7.0 * s, mc, if i < 3 { 1.0 } else { 0.5 });
            let num = (i + 1).to_string();
            let nw = ui.measure(&num, font(9.5), true);
            ui.label(&num, cx - nw / 2.0, cy - font(9.5) * 0.61, font(9.5), true, color::ACCENT_TEXT);
            let is_me = g.rank == i as i32 + 1;
            let name = crate::npc::fit(ui, &c.name, font(12.0), list_w - 90.0 * s);
            ui.label(&name, tr[0] + 36.0 * s, ry + 2.0 * s, font(12.0), true, if is_me { color::GOLD } else { color::TEXT });
            let amt = crate::hud::group_digits(c.amount);
            let aw = ui.measure(&amt, font(12.0), true);
            ui.label(&amt, tr[0] + list_w - aw, ry + 2.0 * s, font(12.0), true, color::TEXT_DIM);
        }
        if g.top.is_empty() {
            ui.label("Nobody yet: be the first!", tr[0] + 14.0 * s, tr[1] + 32.0 * s, font(12.0), false, color::TEXT_MUTED);
        }
        // the top's prize, beside the list
        let zx = tr[0] + list_w + 16.0 * s;
        let zw = tr[0] + tr[2] - 12.0 * s - zx;
        let head = format!("Top {} when it ends", g.top_count);
        let head = crate::npc::fit(ui, &head, font(11.0), zw - 18.0 * s);
        ui.label(&head, zx, tr[1] + 10.0 * s, font(11.0), true, color::GOLD);
        theme::star(ui.renderer, zx + zw - 8.0 * s, tr[1] + 16.0 * s, 6.0 * s, color::GOLD);
        let fit = zw / rewards_width(ui, &g.top_rewards, s).max(1.0);
        let k = s * fit.min(1.0);
        let rw = rewards_width(ui, &g.top_rewards, k);
        reward_row(ui, &g.top_rewards, zx + (zw - rw) / 2.0 + 6.0 * k, tr[1] + 50.0 * s, k, mouse, g.top_claimed, &mut self.hover_item);
        let zb = [zx, tr[1] + bh - 40.0 * s, zw, 28.0 * s];
        let ours = g.rank > 0 && g.rank <= g.top_count as i32 && mine_ok;
        if g.top_claimed {
            done_chip(ui, zb, "Claimed", s * 0.9);
        } else if !g.running && ours {
            glow(ui, zb, pulse, s);
            ui.button(zb, "Claim", Kind::Primary, inside(zb, mouse), s * 0.9);
            self.hits.push((Hit::Tier(g.key.clone(), TOP), zb));
        } else {
            let l = if g.running && ours { "You're in it: hold on!" } else if g.running { "Climb into the top" } else { "Not in the top this time" };
            let l = crate::npc::fit(ui, l, font(11.0), zb[2] - 10.0 * s);
            let lw = ui.measure(&l, font(11.0), true);
            ui.label(&l, zb[0] + (zb[2] - lw) / 2.0, zb[1] + (zb[3] - font(11.0) * 1.22) / 2.0, font(11.0), true, if ours { color::GOOD } else { color::TEXT_MUTED });
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
    use bsk_proto::{CommunityContributor, CommunityTier, QuestReward};

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
            total,
            participants: 3,
            minimum: 2,
            mine,
            rank,
            tiers: [4, 8, 12, 16].iter().zip(claimed).map(|(&at, &c)| CommunityTier { at, claimed: c, rewards: vec![QuestReward { kind: 0, item: 0, amount: 100 }].into() }).collect::<Vec<_>>().into(),
            top_count: 3,
            top_claimed: false,
            top_rewards: vec![QuestReward { kind: 0, item: 0, amount: 1000 }].into(),
            top: vec![CommunityContributor { name: "Tester".into(), amount: 5 }].into(),
        }
    }

    fn data(goals: Vec<CommunityGoal>) -> CommunityData {
        CommunityData { now: 1_800_000_000, week_ends_at: 1_800_300_000, tier_names: vec!["Spark".to_owned(); 4].into(), goals: goals.into() }
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
